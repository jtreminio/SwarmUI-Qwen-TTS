using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Text2Image;
using Xunit;

namespace QwenTTS.Tests;

[Collection("QwenTTSTests")]
public class WorkflowTests
{
    [Fact]
    public void Custom_voice_applies_reference_text_to_custom_voice_and_clone_prompt()
    {
        var voices = new JArray(
            Voice("custom", "Serena", referenceText: "Reference A", styleInstruction: "Style A", speaker: "Serena")
        );

        JObject workflow = GenerateAudioWorkflow("scene <audio>Serena: Hello", voices);

        JObject customVoice = SingleNodeOfType(workflow, "FB_Qwen3TTSCustomVoice").Node;
        Assert.Equal("Reference A", customVoice["inputs"]?["text"]?.ToString());

        JObject clonePrompt = SingleNodeOfType(workflow, "FB_Qwen3TTSVoiceClonePrompt").Node;
        Assert.Equal("Reference A", clonePrompt["inputs"]?["ref_text"]?.ToString());
    }

    [Fact]
    public void Voice_design_generates_design_node_and_clone_prompt()
    {
        var voices = new JArray(
            Voice("design", "Designer", referenceText: "Reference B", styleInstruction: "Style B")
        );

        JObject workflow = GenerateAudioWorkflow("scene <audio>Designer: Hello", voices);

        JObject voiceDesign = SingleNodeOfType(workflow, "FB_Qwen3TTSVoiceDesign").Node;
        Assert.Equal("Reference B", voiceDesign["inputs"]?["text"]?.ToString());

        JObject clonePrompt = SingleNodeOfType(workflow, "FB_Qwen3TTSVoiceClonePrompt").Node;
        Assert.Equal("Reference B", clonePrompt["inputs"]?["ref_text"]?.ToString());
    }

    [Fact]
    public void Audio_file_generates_swarm_input_audio_and_clone_prompt_without_reference_text()
    {
        var voices = new JArray(
            Voice("audio", "AudioVoice", audioBase64: "dGVzdA==")
        );

        JObject workflow = GenerateAudioWorkflow("scene <audio>AudioVoice: Hello", voices);

        JObject inputAudio = SingleNodeOfType(workflow, "SwarmInputAudio").Node;
        Assert.Equal("dGVzdA==", inputAudio["inputs"]?["value"]?.ToString());

        JObject clonePrompt = SingleNodeOfType(workflow, "FB_Qwen3TTSVoiceClonePrompt").Node;
        Assert.Equal("", clonePrompt["inputs"]?["ref_text"]?.ToString());
    }

    [Fact]
    public void Mixed_voice_types_generate_in_single_workflow()
    {
        var voices = new JArray(
            Voice("custom", "Serena", referenceText: "Reference A", styleInstruction: "Style A", speaker: "Serena"),
            Voice("design", "Designer", referenceText: "Reference B", styleInstruction: "Style B"),
            Voice("audio", "AudioVoice", audioBase64: "dGVzdA==")
        );

        JObject workflow = GenerateAudioWorkflow("global <audio>Serena: Hello\nAudioVoice: Hi", voices);

        Assert.Single(NodesOfType(workflow, "FB_Qwen3TTSCustomVoice"));
        Assert.Single(NodesOfType(workflow, "FB_Qwen3TTSVoiceDesign"));
        Assert.Single(NodesOfType(workflow, "SwarmInputAudio"));

        List<(string Id, JObject Node)> clonePrompts = OrderByNumericId(NodesOfType(workflow, "FB_Qwen3TTSVoiceClonePrompt"));
        Assert.Equal(3, clonePrompts.Count);

        JObject roleBank = SingleNodeOfType(workflow, "FB_Qwen3TTSRoleBank").Node;
        Assert.Equal("Serena", roleBank["inputs"]?["role_name_1"]?.ToString());
        Assert.Equal("Designer", roleBank["inputs"]?["role_name_2"]?.ToString());
        Assert.Equal("AudioVoice", roleBank["inputs"]?["role_name_3"]?.ToString());

        Assert.Equal(new JArray(clonePrompts[0].Id, 0), RequireConnection(roleBank, "prompt_1"));
        Assert.Equal(new JArray(clonePrompts[1].Id, 0), RequireConnection(roleBank, "prompt_2"));
        Assert.Equal(new JArray(clonePrompts[2].Id, 0), RequireConnection(roleBank, "prompt_3"));
    }

    [Fact]
    public void Audio_only_dialogue_inference_hands_off_to_swarm_save_audio()
    {
        var voices = new JArray(
            Voice("custom", "Serena", referenceText: "Reference A", styleInstruction: "Style A", speaker: "Serena")
        );

        JObject workflow = GenerateAudioWorkflow("global <audio>Serena: Hello", voices);

        (string Id, JObject Node) dialogue = SingleNodeOfType(workflow, "FB_Qwen3TTSDialogueInference");
        JObject saveNode = SingleNodeOfType(workflow, "SwarmSaveAudioWS").Node;

        Assert.Equal(new JArray(dialogue.Id, 0), RequireConnection(saveNode, "audio"));
    }

    [Fact]
    public void Audio_only_dialogue_inference_wires_role_bank()
    {
        var voices = new JArray(
            Voice("custom", "Serena", referenceText: "Reference A", styleInstruction: "Style A", speaker: "Serena"),
            Voice("custom", "Alexander", referenceText: "Reference B", styleInstruction: "Style B", speaker: "Serena")
        );

        const string prompt = """
<audio>
Serena: Knock, knock.
Alexander: Who’s there?
Serena: Control freak.
Alexander: Control freak
Serena: Okay, now you say “Control freak who?”
""";
        const string expectedScript = """
Serena: Knock, knock.
Alexander: Who’s there?
Serena: Control freak.
Alexander: Control freak
Serena: Okay, now you say “Control freak who?”
""";

        JObject workflow = GenerateAudioWorkflow(prompt, voices);

        (string roleBankId, _) = SingleNodeOfType(workflow, "FB_Qwen3TTSRoleBank");
        JObject dialogueNode = SingleNodeOfType(workflow, "FB_Qwen3TTSDialogueInference").Node;

        Assert.Equal(new JArray(roleBankId, 0), RequireConnection(dialogueNode, "role_bank"));
        Assert.Equal(expectedScript, dialogueNode["inputs"]?["script"]?.ToString());
    }

    [Fact]
    public void Video_dialogue_inference_hands_off_to_audio_length_to_frames()
    {
        var voices = new JArray(
            Voice("custom", "Serena", referenceText: "Reference A", styleInstruction: "Style A", speaker: "Serena")
        );

        JObject workflow = GenerateTextToVideoWorkflow("global <audio>Serena: Hello", voices);

        (string Id, JObject Node) dialogue = SingleNodeOfType(workflow, "FB_Qwen3TTSDialogueInference");
        JObject lengthToFrames = SingleNodeOfType(workflow, "SwarmAudioLengthToFrames").Node;

        Assert.Equal(new JArray(dialogue.Id, 0), RequireConnection(lengthToFrames, "audio"));
    }

    [Fact]
    public void Swarm_audio_length_to_frames_drives_video_length_with_frames()
    {
        var voices = new JArray(
            Voice("custom", "Serena", referenceText: "Reference A", styleInstruction: "Style A", speaker: "Serena")
        );

        JObject workflow = GenerateTextToVideoWorkflow("global <audio>Serena: Hello", voices);

        string lengthToFramesId = SingleNodeOfType(workflow, "SwarmAudioLengthToFrames").Id;
        JObject emptyVideo = SingleNodeOfType(workflow, "EmptyLTXVLatentVideo").Node;
        Assert.Equal(new JArray(lengthToFramesId, 1), RequireConnection(emptyVideo, "length"));
    }

    [Fact]
    public void Text_to_video_ltx2_injects_audio_vae_encode_and_noise_mask()
    {
        var voices = new JArray(
            Voice("custom", "Serena", referenceText: "Reference A", styleInstruction: "Style A", speaker: "Serena")
        );

        JObject workflow = GenerateTextToVideoWorkflow("global <audio>Serena: Hello", voices);

        (_, JObject encodeNode) = SingleNodeOfType(workflow, "LTXVAudioVAEEncode");
        (string setMaskId, _) = SingleNodeOfType(workflow, "SetLatentNoiseMask");
        JObject concat = SingleNodeOfType(workflow, "LTXVConcatAVLatent").Node;
        SingleNodeOfType(workflow, "EmptyLTXVLatentVideo");

        Assert.Equal(new JArray(setMaskId, 0), RequireConnection(concat, "audio_latent"));
        Assert.NotNull(encodeNode);
    }

    [Fact]
    public void Image_to_video_ltx2_injects_audio_vae_encode_and_noise_mask()
    {
        var voices = new JArray(
            Voice("custom", "Serena", referenceText: "Reference A", styleInstruction: "Style A", speaker: "Serena")
        );

        JObject workflow = GenerateImageToVideoWorkflow("global <audio>Serena: Hello", voices);

        (_, JObject encodeNode) = SingleNodeOfType(workflow, "LTXVAudioVAEEncode");
        (string setMaskId, _) = SingleNodeOfType(workflow, "SetLatentNoiseMask");
        JObject concat = SingleNodeOfType(workflow, "LTXVConcatAVLatent").Node;
        SingleNodeOfType(workflow, "EmptyLTXVLatentVideo");

        Assert.Equal(new JArray(setMaskId, 0), RequireConnection(concat, "audio_latent"));
        Assert.NotNull(encodeNode);
    }

    [Fact]
    public void Ltxv_empty_latent_audio_is_removed_after_injection()
    {
        JArray voices =
        [
            Voice("custom", "Serena", referenceText: "Reference A", styleInstruction: "Style A", speaker: "Serena")
        ];

        JObject workflow = GenerateTextToVideoWorkflow("global <audio>Serena: Hello", voices);

        Assert.Empty(NodesOfType(workflow, "LTXVEmptyLatentAudio"));
    }

    private static JObject GenerateAudioWorkflow(string prompt, JArray voices)
    {
        var qwenSteps = WorkflowTestHarness.QwenTTSSteps();
        var input = new T2IParamInput(null);
        input.Set(T2IParamTypes.Prompt, prompt);
        input.Set(QwenTTSExtension.QwenTTSVoices, voices.ToString());
        input.Set(QwenTTSExtension.QwenTTSModel, "1.7B");

        IEnumerable<WorkflowGenerator.WorkflowGenStep> steps =
            new[] { WorkflowTestHarness.MinimalGraphSeedStep() }
                .Concat(qwenSteps);

        return WorkflowTestHarness.GenerateWithSteps(input, steps);
    }

    private static JObject GenerateTextToVideoWorkflow(string prompt, JArray voices)
    {
        var qwenSteps = WorkflowTestHarness.QwenTTSSteps();
        var input = new T2IParamInput(null);
        input.Set(T2IParamTypes.Prompt, prompt);
        input.Set(QwenTTSExtension.QwenTTSVoices, voices.ToString());
        input.Set(QwenTTSExtension.QwenTTSModel, "1.7B");
        input.Set(QwenTTSExtension.QwenTTSUseInVideo, true);

        IEnumerable<WorkflowGenerator.WorkflowGenStep> steps =
            new[] { WorkflowTestHarness.MinimalGraphSeedStep(), WorkflowTestHarness.Ltx2TextToVideoSeedStep() }
                .Concat(qwenSteps);

        return WorkflowTestHarness.GenerateWithSteps(input, steps);
    }

    private static JObject GenerateImageToVideoWorkflow(string prompt, JArray voices)
    {
        var qwenSteps = WorkflowTestHarness.QwenTTSSteps();
        var input = new T2IParamInput(null);
        input.Set(T2IParamTypes.Prompt, prompt);
        input.Set(QwenTTSExtension.QwenTTSVoices, voices.ToString());
        input.Set(QwenTTSExtension.QwenTTSModel, "1.7B");
        input.Set(QwenTTSExtension.QwenTTSUseInVideo, true);

        IEnumerable<WorkflowGenerator.WorkflowGenStep> steps =
            new[] { WorkflowTestHarness.MinimalGraphSeedStep(), WorkflowTestHarness.Ltx2ImageToVideoSeedStep() }
                .Concat(qwenSteps);

        return WorkflowTestHarness.GenerateWithSteps(input, steps);
    }

    private static JObject Voice(
        string type,
        string name,
        string referenceText = "",
        string styleInstruction = "",
        string speaker = "",
        string audioBase64 = "")
    {
        return new JObject
        {
            ["type"] = type,
            ["name"] = name,
            ["referenceText"] = referenceText,
            ["styleInstruction"] = styleInstruction,
            ["speaker"] = speaker,
            ["audioBase64"] = audioBase64
        };
    }

    private static (string Id, JObject Node) SingleNodeOfType(JObject workflow, string classType)
    {
        List<(string Id, JObject Node)> nodes = NodesOfType(workflow, classType);
        Assert.Single(nodes);
        return nodes[0];
    }

    private static List<(string Id, JObject Node)> NodesOfType(JObject workflow, string classType)
    {
        List<(string Id, JObject Node)> results = [];
        foreach (JProperty prop in workflow.Properties())
        {
            if (prop.Value is not JObject obj)
            {
                continue;
            }
            if (obj.TryGetValue("class_type", out JToken ctTok) && $"{ctTok}" == classType)
            {
                results.Add((prop.Name, obj));
            }
        }
        return results;
    }

    private static List<(string Id, JObject Node)> OrderByNumericId(List<(string Id, JObject Node)> nodes)
    {
        return nodes.OrderBy(node =>
        {
            if (int.TryParse(node.Id, out int numeric))
            {
                return numeric;
            }
            return int.MaxValue;
        }).ToList();
    }

    private static JArray RequireConnection(JObject node, string inputName)
    {
        Assert.True(node?["inputs"] is JObject, "Expected node to have an inputs object.");
        JObject inputs = (JObject)node["inputs"];
        Assert.True(inputs.TryGetValue(inputName, out JToken tok), $"Expected input '{inputName}' on node.");
        Assert.True(tok is JArray arr && arr.Count == 2, $"Expected '{inputName}' to be a [nodeId, outputIndex] pair.");
        return (JArray)tok;
    }
}
