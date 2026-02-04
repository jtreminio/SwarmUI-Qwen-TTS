using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace QwenTTS;

internal static class QwenTTSWorkflow
{
    private const int CustomVoiceIdBase = 63000;
    private const int VoiceDesignIdBase = 63100;
    private const int SwarmInputAudioIdBase = 63200;
    private const int VoiceClonePromptIdBase = 63300;
    private const int RoleBankIdBase = 63400;
    private const int DialogueInferenceIdBase = 63500;
    private const int SaveAudioWsIdBase = 63600;
    private const int VideoInjectionIdBase = 63700;

    public static void RunForAudio(WorkflowGenerator g)
    {
        if (!IsExtensionActive(g))
        {
            return;
        }

        if (g.UserInput.Get(QwenTTSExtension.QwenTTSUseInVideo, false))
        {
            return;
        }

        if (!g.UserInput.TryGet(QwenTTSExtension.QwenTTSVoices, out string json)
            || string.IsNullOrWhiteSpace(json)
            || json.Trim() == "[]")
        {
            return;
        }

        if (!GetVoices(json, out List<VoiceSpec> voices, out string error))
        {
            throw new SwarmReadableErrorException($"Qwen-TTS: invalid voices payload. {error}");
        }

        if (!GetAudioSection(g.UserInput.Get(T2IParamTypes.Prompt, ""), out string audioSection, out error))
        {
            throw new SwarmReadableErrorException($"Qwen-TTS: invalid audio section. {error}");
        }

        string dialogueId = CreateDialogueInferenceNode(g, audioSection, voices);

        JObject saveWsInputs = new()
        {
            ["audio"] = new JArray(dialogueId, 0)
        };

        g.CreateNode(
            "SwarmSaveAudioWS",
            saveWsInputs,
            g.GetStableDynamicID(SaveAudioWsIdBase, 0)
        );

        // Prevent image workflow from being added when we have TTS-only content
        g.SkipFurtherSteps = true;
    }

    public static void RunForVideo(WorkflowGenerator g)
    {
        if (!IsExtensionActive(g))
        {
            return;
        }

        if (!g.UserInput.Get(QwenTTSExtension.QwenTTSUseInVideo, false))
        {
            return;
        }

        if (!g.IsLTXV2() || g.FinalAudioVae is null)
        {
            return;
        }

        if (!g.UserInput.TryGet(QwenTTSExtension.QwenTTSVoices, out string json)
            || string.IsNullOrWhiteSpace(json)
            || json.Trim() == "[]")
        {
            return;
        }

        if (!GetVoices(json, out List<VoiceSpec> voices, out string error))
        {
            throw new SwarmReadableErrorException($"Qwen-TTS: invalid voices payload. {error}");
        }

        if (!GetAudioSection(g.UserInput.Get(T2IParamTypes.Prompt, ""), out string audioSection, out error))
        {
            throw new SwarmReadableErrorException($"Qwen-TTS: invalid audio section. {error}");
        }

        AttachToLtx2(g, CreateDialogueInferenceNode(g, audioSection, voices));
    }

    private static bool IsExtensionActive(WorkflowGenerator g)
    {
        T2IParamType type = QwenTTSExtension.QwenTTSModel?.Type;
        return g?.UserInput is not null
            && type is not null
            && g.UserInput.TryGetRaw(type, out _);
    }

    private static string CreateDialogueInferenceNode(
        WorkflowGenerator g,
        string audioSection,
        List<VoiceSpec> voices)
    {
        string modelChoice = g.UserInput.Get(QwenTTSExtension.QwenTTSModel, "None");
        long baseSeed = g.UserInput.Get(T2IParamTypes.Seed, -1L);
        int maxNewTokens = g.UserInput.Get(QwenTTSExtension.QwenTTSMaxNewTokens, 2048);
        double topP = g.UserInput.Get(QwenTTSExtension.QwenTTSTopP, 0.8);
        int topK = g.UserInput.Get(QwenTTSExtension.QwenTTSTopK, 20);
        double temperature = g.UserInput.Get(QwenTTSExtension.QwenTTSTemperature, 1.0);
        double repetitionPenalty = g.UserInput.Get(QwenTTSExtension.QwenTTSRepetitionPenalty, 1.05);
        string attention = g.UserInput.Get(QwenTTSExtension.QwenTTSAttention, "flash_attn");
        bool unloadModelAfterGenerate = g.UserInput.Get(QwenTTSExtension.QwenTTSUnloadModelAfterGenerate, false);

        List<string> clonePromptNodes = [];

        for (int i = 0; i < voices.Count; i++)
        {
            VoiceSpec voice = voices[i];
            string sourceNode = CreateVoiceSourceNode(
                g,
                voice,
                i,
                baseSeed,
                modelChoice,
                maxNewTokens,
                topP,
                topK,
                temperature,
                repetitionPenalty,
                attention,
                unloadModelAfterGenerate
            );

            JObject cloneInputs = new()
            {
                ["ref_audio"] = new JArray(sourceNode, 0),
                ["ref_text"] = voice.Type == VoiceType.AudioFile ? "" : voice.ReferenceText,
                ["model_choice"] = modelChoice,
                ["device"] = NodeDefaults.VoiceClonePromptDevice,
                ["precision"] = NodeDefaults.VoiceClonePromptPrecision,
                ["attention"] = attention,
                ["x_vector_only"] = false,
                ["unload_model_after_generate"] = unloadModelAfterGenerate
            };

            clonePromptNodes.Add(g.CreateNode(
                "FB_Qwen3TTSVoiceClonePrompt",
                cloneInputs,
                g.GetStableDynamicID(VoiceClonePromptIdBase, i)
            ));
        }

        JObject roleInputs = new();
        for (int i = 0; i < voices.Count; i++)
        {
            int roleIndex = i + 1;
            roleInputs[$"prompt_{roleIndex}"] = new JArray(clonePromptNodes[i], 0);
            roleInputs[$"role_name_{roleIndex}"] = voices[i].Name;
        }

        string roleBankId = g.CreateNode(
            "FB_Qwen3TTSRoleBank",
            roleInputs,
            g.GetStableDynamicID(RoleBankIdBase, 0)
        );

        JObject dialogueInputs = new()
        {
            ["role_bank"] = new JArray(roleBankId, 0),
            ["script"] = audioSection,
            ["model_choice"] = modelChoice,
            ["device"] = NodeDefaults.DialogueDevice,
            ["precision"] = NodeDefaults.DialoguePrecision,
            ["language"] = NodeDefaults.DialogueLanguage,
            ["pause_linebreak"] = NodeDefaults.PauseLinebreak,
            ["period_pause"] = NodeDefaults.PeriodPause,
            ["comma_pause"] = NodeDefaults.CommaPause,
            ["question_pause"] = NodeDefaults.QuestionPause,
            ["hyphen_pause"] = NodeDefaults.HyphenPause,
            ["merge_outputs"] = NodeDefaults.MergeOutputs,
            ["batch_size"] = NodeDefaults.BatchSize,
            ["seed"] = baseSeed + 9,
            ["max_new_tokens_per_line"] = maxNewTokens,
            ["top_p"] = topP,
            ["top_k"] = topK,
            ["temperature"] = temperature,
            ["repetition_penalty"] = repetitionPenalty,
            ["attention"] = attention,
            ["unload_model_after_generate"] = unloadModelAfterGenerate
        };

        return g.CreateNode(
            "FB_Qwen3TTSDialogueInference",
            dialogueInputs,
            g.GetStableDynamicID(DialogueInferenceIdBase, 0)
        );
    }

    private static string CreateVoiceSourceNode(
        WorkflowGenerator g,
        VoiceSpec voice,
        int index,
        long baseSeed,
        string modelChoice,
        int maxNewTokens,
        double topP,
        int topK,
        double temperature,
        double repetitionPenalty,
        string attention,
        bool unloadModelAfterGenerate)
    {
        switch (voice.Type)
        {
            case VoiceType.CustomVoice:
            {
                string refText = string.IsNullOrWhiteSpace(voice.ReferenceText) ? NodeDefaults.PlaceholderReferenceText : voice.ReferenceText.Trim();
                string styleInstruct = string.IsNullOrWhiteSpace(voice.StyleInstruction) ? NodeDefaults.PlaceholderStyleInstruction : voice.StyleInstruction.Trim();

                JObject inputs = new()
                {
                    ["text"] = refText,
                    ["speaker"] = string.IsNullOrWhiteSpace(voice.Speaker) ? NodeDefaults.CustomVoiceSpeaker : voice.Speaker,
                    ["model_choice"] = modelChoice,
                    ["device"] = NodeDefaults.CustomVoiceDevice,
                    ["precision"] = NodeDefaults.CustomVoicePrecision,
                    ["language"] = NodeDefaults.CustomVoiceLanguage,
                    ["seed"] = baseSeed + (index + 1),
                    ["instruct"] = styleInstruct,
                    ["max_new_tokens"] = maxNewTokens,
                    ["top_p"] = topP,
                    ["top_k"] = topK,
                    ["temperature"] = temperature,
                    ["repetition_penalty"] = repetitionPenalty,
                    ["attention"] = attention,
                    ["unload_model_after_generate"] = unloadModelAfterGenerate,
                    ["custom_model_path"] = "",
                    ["custom_speaker_name"] = ""
                };

                return g.CreateNode(
                    "FB_Qwen3TTSCustomVoice",
                    inputs,
                    g.GetStableDynamicID(CustomVoiceIdBase, index)
                );
            }
            case VoiceType.VoiceDesign:
            {
                string refTextDesign = string.IsNullOrWhiteSpace(voice.ReferenceText) ? NodeDefaults.PlaceholderReferenceText : voice.ReferenceText.Trim();
                string styleInstructDesign = string.IsNullOrWhiteSpace(voice.StyleInstruction) ? NodeDefaults.PlaceholderStyleInstruction : voice.StyleInstruction.Trim();

                JObject inputs = new()
                {
                    ["text"] = refTextDesign,
                    ["instruct"] = styleInstructDesign,
                    ["model_choice"] = modelChoice,
                    ["device"] = NodeDefaults.VoiceDesignDevice,
                    ["precision"] = NodeDefaults.VoiceDesignPrecision,
                    ["language"] = NodeDefaults.VoiceDesignLanguage,
                    ["seed"] = baseSeed + (index + 1),
                    ["max_new_tokens"] = maxNewTokens,
                    ["top_p"] = topP,
                    ["top_k"] = topK,
                    ["temperature"] = temperature,
                    ["repetition_penalty"] = repetitionPenalty,
                    ["attention"] = attention,
                    ["unload_model_after_generate"] = unloadModelAfterGenerate
                };

                return g.CreateNode(
                    "FB_Qwen3TTSVoiceDesign",
                    inputs,
                    g.GetStableDynamicID(VoiceDesignIdBase, index)
                );
            }
            case VoiceType.AudioFile:
            {
                if (string.IsNullOrWhiteSpace(voice.AudioBase64))
                {
                    throw new SwarmReadableErrorException("Qwen-TTS: Audio File requires a local audio upload.");
                }

                string title = string.IsNullOrWhiteSpace(voice.Name)
                    ? "Voice Audio"
                    : $"{voice.Name} Audio";

                JObject inputs = new()
                {
                    ["title"] = title,
                    ["value"] = voice.AudioBase64,
                    ["description"] = "Reference audio file for voice cloning.",
                    ["order_priority"] = 0.0,
                    ["is_advanced"] = false,
                    ["raw_id"] = ""
                };

                return g.CreateNode(
                    "SwarmInputAudio",
                    inputs,
                    g.GetStableDynamicID(SwarmInputAudioIdBase, index)
                );
            }
            default:
                throw new SwarmReadableErrorException($"Qwen-TTS: unknown voice type '{voice.TypeRaw}'.");
        }
    }

    private static bool GetVoices(string json, out List<VoiceSpec> voices, out string error)
    {
        voices = [];
        error = null;

        try
        {
            JToken token = JToken.Parse(json);
            if (token is not JArray arr)
            {
                error = "voices payload must be a JSON array.";
                return false;
            }

            foreach (JToken item in arr)
            {
                if (item is not JObject obj)
                {
                    continue;
                }

                string getStr(string key)
                {
                    foreach (JProperty p in obj.Properties())
                    {
                        if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                        {
                            return p.Value?.Type == JTokenType.Null ? null : $"{p.Value}";
                        }
                    }
                    return null;
                }

                string rawType = getStr("type");
                if (string.IsNullOrWhiteSpace(rawType))
                {
                    error = "voice entry is missing type.";
                    return false;
                }

                VoiceType type = VoiceTypeFrom(rawType, out string typeError);
                if (type == VoiceType.Unknown)
                {
                    error = typeError;
                    return false;
                }

                VoiceSpec voice = new(
                    TypeRaw: rawType,
                    Type: type,
                    Name: getStr("name")?.Trim(),
                    ReferenceText: getStr("referenceText") ?? "",
                    StyleInstruction: getStr("styleInstruction") ?? "",
                    Speaker: getStr("speaker") ?? "",
                    AudioBase64: getStr("audioBase64") ?? ""
                );

                if (string.IsNullOrWhiteSpace(voice.Name))
                {
                    error = "voice entry is missing a name.";
                    return false;
                }

                voices.Add(voice);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (voices.Count < 1)
        {
            error = "at least one voice is required";
            return false;
        }

        if (voices.Count > 8)
        {
            error = "RoleBank supports at most 8 voices";
            return false;
        }

        return true;
    }

    private static bool GetAudioSection(string prompt, out string section, out string error)
    {
        error = "";
        section = "";

        if (string.IsNullOrWhiteSpace(prompt) 
            || !prompt.Contains("<audio", StringComparison.OrdinalIgnoreCase))
        {
            error = "missing <audio> section in the prompt";
            return false;
        }

        HashSet<string> sectionEndingTags =
        [
            "base", "refiner", "video", "videoswap", "region", "segment", "object", "extend",
        ];

        int sectionCount = 0;
        string result = "";
        bool inAudio = false;

        foreach (string piece in prompt.Split('<'))
        {
            if (string.IsNullOrEmpty(piece))
            {
                continue;
            }

            int end = piece.IndexOf('>');
            if (end == -1)
            {
                if (inAudio)
                {
                    result += "<" + piece;
                }
                continue;
            }

            string tag = piece[..end];
            string content = piece[(end + 1)..];

            string prefixPart = tag;
            int colon = tag.IndexOf(':');
            if (colon != -1)
            {
                prefixPart = tag[..colon];
            }
            prefixPart = prefixPart.Split('/')[0];

            string prefixName = prefixPart;
            if (prefixName.EndsWith(']') && prefixName.Contains('['))
            {
                int open = prefixName.LastIndexOf('[');
                if (open != -1)
                {
                    prefixName = prefixName[..open];
                }
            }

            string prefixLower = prefixName.ToLowerInvariant();

            if (prefixLower == "audio")
            {
                bool matches = true;
                int cidCut = tag.LastIndexOf("//cid=", StringComparison.OrdinalIgnoreCase);
                if (cidCut != -1 && int.TryParse(tag[(cidCut + "//cid=".Length)..], out int cid))
                {
                    matches = cid == QwenTTSExtension.SectionID_Audio;
                }

                if (matches)
                {
                    sectionCount++;
                    inAudio = true;
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        result += content;
                    }
                }
                else
                {
                    inAudio = false;
                }
                continue;
            }

            if (inAudio)
            {
                if (sectionEndingTags.Contains(prefixLower))
                {
                    inAudio = false;
                    continue;
                }
                result += "<" + piece;
            }
        }

        if (sectionCount == 0 || string.IsNullOrWhiteSpace(result))
        {
            throw new SwarmReadableErrorException("missing <audio> section in the prompt.");
        }

        if (sectionCount > 1)
        {
            throw new SwarmReadableErrorException("only one <audio> section is supported.");
        }

        section = result.Trim();
        return true;
    }

    private static void AttachToLtx2(WorkflowGenerator g, string dialogueId)
    {
        // Find LTXVConcatAVLatent whose audio_latent comes from LTXVEmptyLatentAudio
        string concatNodeId = null;
        string emptyLatentNodeId = null;
        JArray oldAudioLatent = null;
        int? workflowFps = null;

        foreach (JProperty prop in g.Workflow.Properties())
        {
            var node = prop.Value as JObject;
            if (node is null || $"{node["class_type"]}" != "LTXVConcatAVLatent")
            {
                continue;
            }

            var inputs = node["inputs"] as JObject;
            if (inputs is null || !inputs.TryGetValue("audio_latent", out JToken audioTok) || audioTok is not JArray arr)
            {
                continue;
            }

            string sourceId = $"{arr[0]}";
            if (!g.Workflow.ContainsKey(sourceId))
            {
                continue;
            }

            var srcNode = g.Workflow[sourceId] as JObject;
            if (srcNode is null || $"{srcNode["class_type"]}" != "LTXVEmptyLatentAudio")
            {
                continue;
            }

            if (srcNode["inputs"] is JObject srcInputs)
            {
                workflowFps =
                    srcInputs.Value<int?>("frame_rate")
                    ?? (srcInputs.Value<double?>("frame_rate") is double fr ? (int?)Math.Round(fr) : null)
                    ?? workflowFps;
            }

            concatNodeId = prop.Name;
            emptyLatentNodeId = sourceId;
            oldAudioLatent = arr;
            break;
        }

        if (concatNodeId is null || emptyLatentNodeId is null || oldAudioLatent is null)
        {
            return;
        }

        // Video length driven by TTS audio: SwarmAudioLengthToFrames computes frames = duration_sec * fps + 1
        int fps = workflowFps ?? g.Text2VideoFPS();
        if (fps <= 0) fps = 24;
        int width = g.UserInput.GetImageWidth();
        int height = g.UserInput.GetImageHeight();

        string lengthToFramesId = g.CreateNode("SwarmAudioLengthToFrames", new JObject
        {
            ["audio"] = new JArray(dialogueId, 0),
            ["frame_rate"] = fps
        }, g.GetStableDynamicID(VideoInjectionIdBase + 400, 0));

        var framesConnection = new JArray(lengthToFramesId, 1);

        if (g.Workflow[emptyLatentNodeId] is JObject emptyNode && emptyNode["inputs"] is JObject emptyInputs)
        {
            emptyInputs["frames_number"] = framesConnection;
        }

        g.RunOnNodesOfClass("EmptyLTXVLatentVideo", (videoId, videoData) =>
        {
            if (videoData["inputs"] is JObject videoInputs)
            {
                videoInputs["length"] = framesConnection;
            }
        });

        string encodeId = g.CreateNode("LTXVAudioVAEEncode", new JObject
        {
            ["audio"] = new JArray(lengthToFramesId, 0),
            ["audio_vae"] = g.FinalAudioVae
        }, g.GetStableDynamicID(VideoInjectionIdBase + 500, 0));

        string solidMaskId = g.CreateNode("SolidMask", new JObject
        {
            ["value"] = 0.0,
            ["width"] = width,
            ["height"] = height
        }, g.GetStableDynamicID(VideoInjectionIdBase + 600, 0));

        string setMaskId = g.CreateNode("SetLatentNoiseMask", new JObject
        {
            ["samples"] = new JArray(encodeId, 0),
            ["mask"] = new JArray(solidMaskId, 0)
        }, g.GetStableDynamicID(VideoInjectionIdBase + 700, 0));

        var newAudioLatent = new JArray(setMaskId, 0);
        g.ReplaceNodeConnection(oldAudioLatent, newAudioLatent);
        
        // Cleanup danging LTXV Empty Latent Audio
        g.UsedInputs = null;
        if (!g.NodeIsConnectedAnywhere(emptyLatentNodeId))
        {
            g.Workflow.Remove(emptyLatentNodeId);
        }
    }

    private static VoiceType VoiceTypeFrom(string raw, out string error)
    {
        error = null;
        string t = raw?.Trim().ToLowerInvariant() ?? "";
        VoiceType result = t switch
        {
            "custom" => VoiceType.CustomVoice,
            "customvoice" => VoiceType.CustomVoice,
            "voice_design" => VoiceType.VoiceDesign,
            "voicedesign" => VoiceType.VoiceDesign,
            "design" => VoiceType.VoiceDesign,
            "audio" => VoiceType.AudioFile,
            "audiofile" => VoiceType.AudioFile,
            _ => VoiceType.Unknown
        };
        if (result == VoiceType.Unknown)
            error = $"unknown voice type '{raw}'.";
        return result;
    }

    private enum VoiceType
    {
        Unknown,
        CustomVoice,
        VoiceDesign,
        AudioFile
    }

    private sealed record VoiceSpec(
        string TypeRaw,
        VoiceType Type,
        string Name,
        string ReferenceText,
        string StyleInstruction,
        string Speaker,
        string AudioBase64
    );

    private static class NodeDefaults
    {
        public const string PlaceholderReferenceText = "Reference sample.";
        public const string PlaceholderStyleInstruction = "Neutral speaking style.";
        public const string CustomVoiceDevice = "cuda";
        public const string CustomVoicePrecision = "bf16";
        public const string CustomVoiceLanguage = "English";
        public const string CustomVoiceSpeaker = "Serena";
        public const long CustomVoiceSeed = 197224622432220;
        public const string VoiceDesignDevice = "auto";
        public const string VoiceDesignPrecision = "bf16";
        public const string VoiceDesignLanguage = "English";
        public const long VoiceDesignSeed = 489742050863978;
        public const string VoiceClonePromptDevice = "auto";
        public const string VoiceClonePromptPrecision = "bf16";
        public const string DialogueDevice = "auto";
        public const string DialoguePrecision = "bf16";
        public const string DialogueLanguage = "Auto";
        public const long DialogueSeed = 968106275015887;
        public const double PauseLinebreak = 0.5;
        public const double PeriodPause = 0.4;
        public const double CommaPause = 0.2;
        public const double QuestionPause = 0.6;
        public const double HyphenPause = 0.3;
        public const bool MergeOutputs = true;
        public const int BatchSize = 4;
    }
}
