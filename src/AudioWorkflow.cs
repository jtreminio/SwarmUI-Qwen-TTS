using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace QwenTTS;

/// <summary>
/// Audio stage (runs first). Builds the TTS dialogue graph and adds a save-audio node.
/// Registers the save node in <see cref="WorkflowGenerator.NodeHelpers"/> so the
/// video stage can verify success and reuse the dialogue output without rebuilding.
/// </summary>
internal class AudioWorkflow(WorkflowGenerator g)
{
    private const int SaveAudioIdBase = 63600;

    private WGNodeData WrapAudio(JArray path) => new(path, g, WGNodeData.DT_AUDIO, g.CurrentCompat());

    public void Run()
    {
        if (!g.UserInput.TryGet(QwenTTSExtension.QwenTTSVoices, out string json)
            || string.IsNullOrWhiteSpace(json)
            || json.Trim() == "[]")
        {
            return;
        }

        if (!VoiceParser.TryParse(json, out List<VoiceSpec> voices, out string error))
        {
            Logs.Error($"Qwen-TTS: invalid voices payload. {error}");
            return;
        }

        if (!PromptParser.TryGetAudioSection(g.UserInput.Get(T2IParamTypes.Prompt, ""), out string audioSection, out error))
        {
            Logs.Error($"Qwen-TTS: invalid audio section. {error}");
            return;
        }

        string dialogueId = new DialogueBuilder(g).Build(audioSection, voices);
        WGNodeData audio = WrapAudio([dialogueId, 0]);
        string saveNodeId = audio.SaveOutput(null, null, id: g.GetStableDynamicID(SaveAudioIdBase, 0));
        g.NodeHelpers[NodeHelperKeys.AudioSave] = saveNodeId;

        if (!g.UserInput.Get(QwenTTSExtension.QwenTTSUseInVideo, false))
        {
            g.SkipFurtherSteps = true;
        }
    }
}
