using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;

namespace QwenTTS;

/// <summary>
/// Video stage (runs after the audio stage and the core video graph steps).
/// Find save-audio node registered by the audio stage.
/// Removes the save node and injects the dialogue output into the LTX2 pipeline.
/// </summary>
internal class VideoWorkflow(WorkflowGenerator g)
{
    private const int VideoInjectionIdBase = 63700;

    private WGNodeData WrapAudio(JArray path) => new(path, g, WGNodeData.DT_AUDIO, g.CurrentCompat());

    public void Run()
    {
        if (!g.UserInput.Get(QwenTTSExtension.QwenTTSUseInVideo, false)
            || !g.IsLTXV2()
            || g.CurrentAudioVae is null
            || !g.NodeHelpers.TryGetValue(NodeHelperKeys.AudioSave, out string saveNodeId))
        {
            return;
        }

        if (g.Workflow[saveNodeId] is not JObject saveNode
            || saveNode["inputs"] is not JObject saveInputs
            || saveInputs["audio"] is not JArray audioArr
            || audioArr.Count < 2)
        {
            return;
        }

        string dialogueNodeId = $"{audioArr[0]}";
        AttachToLtx2(dialogueNodeId, saveNodeId);
    }

    private void AttachToLtx2(string dialogueId, string saveNodeId)
    {
        string concatNodeId = null;
        string emptyLatentNodeId = null;
        JArray oldAudioLatent = null;
        int? workflowFps = null;

        foreach (JProperty prop in g.Workflow.Properties())
        {
            if (prop.Value is not JObject node || $"{node["class_type"]}" != NodeTypes.LTXVConcatAVLatent)
            {
                continue;
            }

            if (node["inputs"] is not JObject inputs
                || !inputs.TryGetValue("audio_latent", out JToken audioTok)
                || audioTok is not JArray arr)
            {
                continue;
            }

            string sourceId = $"{arr[0]}";
            if (!g.Workflow.ContainsKey(sourceId))
            {
                continue;
            }

            if (g.Workflow[sourceId] is not JObject srcNode
                || $"{srcNode["class_type"]}" != NodeTypes.LTXVEmptyLatentAudio)
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

        g.Workflow.Remove(saveNodeId);

        int fps = workflowFps ?? g.Text2VideoFPS();
        if (fps <= 0) fps = 24;
        int width = g.UserInput.GetImageWidth();
        int height = g.UserInput.GetImageHeight();

        string lengthToFramesId = g.CreateNode(NodeTypes.AudioLengthToFrames, new JObject
        {
            ["audio"] = new JArray(dialogueId, 0),
            ["frame_rate"] = fps
        }, g.GetStableDynamicID(VideoInjectionIdBase + 400, 0));

        var framesConnection = new JArray(lengthToFramesId, 1);

        if (g.Workflow[emptyLatentNodeId] is JObject emptyNode && emptyNode["inputs"] is JObject emptyInputs)
        {
            emptyInputs["frames_number"] = framesConnection;
        }

        g.RunOnNodesOfClass(NodeTypes.EmptyLTXVLatentVideo, (videoId, videoData) =>
        {
            if (videoData["inputs"] is JObject videoInputs)
            {
                videoInputs["length"] = framesConnection;
            }
        });

        WGNodeData dialogueAudio = WrapAudio([lengthToFramesId, 0]);
        WGNodeData encodedAudio = dialogueAudio.EncodeToLatent(g.CurrentAudioVae);

        string solidMaskId = g.CreateNode(NodeTypes.SolidMask, new JObject
        {
            ["value"] = 0.0,
            ["width"] = width,
            ["height"] = height
        }, g.GetStableDynamicID(VideoInjectionIdBase + 600, 0));

        string setMaskId = g.CreateNode(NodeTypes.SetLatentNoiseMask, new JObject
        {
            ["samples"] = encodedAudio.Path,
            ["mask"] = new JArray(solidMaskId, 0)
        }, g.GetStableDynamicID(VideoInjectionIdBase + 700, 0));

        var newAudioLatent = new JArray(setMaskId, 0);
        g.ReplaceNodeConnection(oldAudioLatent, newAudioLatent);

        g.UsedInputs = null;
        if (!g.NodeIsConnectedAnywhere(emptyLatentNodeId))
        {
            g.Workflow.Remove(emptyLatentNodeId);
        }
    }
}
