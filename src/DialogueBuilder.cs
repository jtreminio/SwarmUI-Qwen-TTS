using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Utils;

namespace QwenTTS;

/// <summary>
/// Builds the ComfyUI dialogue graph: per-voice source nodes, clone prompts,
/// a role bank, and the final dialogue inference node. Shared by both the audio-only and
/// video integration paths.
/// </summary>
internal class DialogueBuilder(WorkflowGenerator g)
{
    private const int CustomVoiceIdBase = 63000;
    private const int VoiceDesignIdBase = 63100;
    private const int SwarmInputAudioIdBase = 63200;
    private const int VoiceClonePromptIdBase = 63300;
    private const int RoleBankIdBase = 63400;
    private const int DialogueInferenceIdBase = 63500;

    public string Build(string audioSection, List<VoiceSpec> voices)
    {
        var samplingParams = SamplingParams.FromUserInput(g.UserInput);

        List<string> clonePromptNodes = BuildVoiceClonePrompts(voices, samplingParams);
        string roleBankId = BuildRoleBank(voices, clonePromptNodes);

        return BuildDialogueInference(roleBankId, audioSection, samplingParams);
    }

    private List<string> BuildVoiceClonePrompts(List<VoiceSpec> voices, SamplingParams p)
    {
        List<string> clonePromptNodes = [];

        for (int i = 0; i < voices.Count; i++)
        {
            VoiceSpec voice = voices[i];
            string sourceNode = CreateVoiceSourceNode(voice, i, p);

            JObject cloneInputs = new()
            {
                ["ref_audio"] = new JArray(sourceNode, 0),
                ["ref_text"] = voice.Type == VoiceType.AudioFile ? "" : voice.ReferenceText,
                ["model_choice"] = p.ModelChoice,
                ["device"] = NodeDefaults.VoiceClonePromptDevice,
                ["precision"] = NodeDefaults.VoiceClonePromptPrecision,
                ["attention"] = p.Attention,
                ["x_vector_only"] = false,
                ["unload_model_after_generate"] = p.UnloadModelAfterGenerate
            };

            clonePromptNodes.Add(g.CreateNode(
                NodeTypes.VoiceClonePrompt,
                cloneInputs,
                g.GetStableDynamicID(VoiceClonePromptIdBase, i)
            ));
        }

        return clonePromptNodes;
    }

    private string BuildRoleBank(List<VoiceSpec> voices, List<string> clonePromptNodes)
    {
        JObject roleInputs = new();
        for (int i = 0; i < voices.Count; i++)
        {
            int roleIndex = i + 1;
            roleInputs[$"prompt_{roleIndex}"] = new JArray(clonePromptNodes[i], 0);
            roleInputs[$"role_name_{roleIndex}"] = voices[i].Name;
        }

        return g.CreateNode(
            NodeTypes.RoleBank,
            roleInputs,
            g.GetStableDynamicID(RoleBankIdBase, 0)
        );
    }

    private string BuildDialogueInference(string roleBankId, string audioSection, SamplingParams p)
    {
        JObject dialogueInputs = new()
        {
            ["role_bank"] = new JArray(roleBankId, 0),
            ["script"] = audioSection,
            ["model_choice"] = p.ModelChoice,
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
            ["seed"] = p.BaseSeed + 9,
            ["max_new_tokens_per_line"] = p.MaxNewTokens,
            ["top_p"] = p.TopP,
            ["top_k"] = p.TopK,
            ["temperature"] = p.Temperature,
            ["repetition_penalty"] = p.RepetitionPenalty,
            ["attention"] = p.Attention,
            ["unload_model_after_generate"] = p.UnloadModelAfterGenerate
        };

        return g.CreateNode(
            NodeTypes.DialogueInference,
            dialogueInputs,
            g.GetStableDynamicID(DialogueInferenceIdBase, 0)
        );
    }

    private string CreateVoiceSourceNode(VoiceSpec voice, int index, SamplingParams p)
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
                    ["model_choice"] = p.ModelChoice,
                    ["device"] = NodeDefaults.CustomVoiceDevice,
                    ["precision"] = NodeDefaults.CustomVoicePrecision,
                    ["language"] = NodeDefaults.CustomVoiceLanguage,
                    ["seed"] = p.BaseSeed + (index + 1),
                    ["instruct"] = styleInstruct,
                    ["max_new_tokens"] = p.MaxNewTokens,
                    ["top_p"] = p.TopP,
                    ["top_k"] = p.TopK,
                    ["temperature"] = p.Temperature,
                    ["repetition_penalty"] = p.RepetitionPenalty,
                    ["attention"] = p.Attention,
                    ["unload_model_after_generate"] = p.UnloadModelAfterGenerate,
                    ["custom_model_path"] = "",
                    ["custom_speaker_name"] = ""
                };

                return g.CreateNode(
                    NodeTypes.CustomVoice,
                    inputs,
                    g.GetStableDynamicID(CustomVoiceIdBase, index)
                );
            }
            case VoiceType.VoiceDesign:
            {
                string refText = string.IsNullOrWhiteSpace(voice.ReferenceText) ? NodeDefaults.PlaceholderReferenceText : voice.ReferenceText.Trim();
                string styleInstruct = string.IsNullOrWhiteSpace(voice.StyleInstruction) ? NodeDefaults.PlaceholderStyleInstruction : voice.StyleInstruction.Trim();

                JObject inputs = new()
                {
                    ["text"] = refText,
                    ["instruct"] = styleInstruct,
                    ["model_choice"] = p.ModelChoice,
                    ["device"] = NodeDefaults.VoiceDesignDevice,
                    ["precision"] = NodeDefaults.VoiceDesignPrecision,
                    ["language"] = NodeDefaults.VoiceDesignLanguage,
                    ["seed"] = p.BaseSeed + (index + 1),
                    ["max_new_tokens"] = p.MaxNewTokens,
                    ["top_p"] = p.TopP,
                    ["top_k"] = p.TopK,
                    ["temperature"] = p.Temperature,
                    ["repetition_penalty"] = p.RepetitionPenalty,
                    ["attention"] = p.Attention,
                    ["unload_model_after_generate"] = p.UnloadModelAfterGenerate
                };

                return g.CreateNode(
                    NodeTypes.VoiceDesign,
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
                    NodeTypes.SwarmInputAudio,
                    inputs,
                    g.GetStableDynamicID(SwarmInputAudioIdBase, index)
                );
            }
            default:
                throw new SwarmReadableErrorException($"Qwen-TTS: unknown voice type '{voice.TypeRaw}'.");
        }
    }
}
