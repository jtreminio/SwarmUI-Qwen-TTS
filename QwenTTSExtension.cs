using System.IO;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.Builtin_ComfyUIBackend;

namespace QwenTTS;

public class QwenTTSExtension : Extension
{
    public const int SectionID_Audio = 58800;
    public static T2IParamGroup QwenTTSGroup;
    public static T2IParamGroup QwenTTSAdvancedGroup;
    public static T2IRegisteredParam<string> QwenTTSVoices;
    public static T2IRegisteredParam<string> QwenTTSModel;
    public static T2IRegisteredParam<bool> QwenTTSUseInVideo;
    public static T2IRegisteredParam<int> QwenTTSMaxNewTokens;
    public static T2IRegisteredParam<double> QwenTTSTopP;
    public static T2IRegisteredParam<int> QwenTTSTopK;
    public static T2IRegisteredParam<double> QwenTTSTemperature;
    public static T2IRegisteredParam<double> QwenTTSRepetitionPenalty;
    public static T2IRegisteredParam<string> QwenTTSAttention;
    public static T2IRegisteredParam<bool> QwenTTSUnloadModelAfterGenerate;

    public override void OnPreInit()
    {
        PromptRegion.RegisterCustomPrefix("audio");

        T2IPromptHandling.PromptTagBasicProcessors["audio"] = (data, context) =>
        {
            context.SectionID = SectionID_Audio;
            return $"<audio//cid={context.SectionID}>";
        };
        T2IPromptHandling.PromptTagLengthEstimators["audio"] = (data, context) => "<break>";

        ScriptFiles.Add("Assets/qwentts.js");
    }

    public override void OnInit()
    {
        Logs.Info("Qwen-TTS Extension initializing...");
        RegisterParameters();
        InstallComfyUIQwenTTSNodes();
        var nodeFolder = Path.GetFullPath(Path.Join(FilePath, "comfy_node"));
        ComfyUISelfStartBackend.CustomNodePaths.Add(nodeFolder);
        Logs.Init($"Qwen-TTS: added {nodeFolder} to ComfyUI CustomNodePaths");

        WorkflowGenerator.AddStep(g => QwenTTSWorkflow.RunForAudio(g), -20);
        WorkflowGenerator.AddStep(g => QwenTTSWorkflow.RunForVideo(g), 15);
    }

    private void InstallComfyUIQwenTTSNodes()
    {
        ComfyUIBackendExtension.NodeToFeatureMap["FB_Qwen3TTSCustomVoice"] = "qwen_tts";
        ComfyUIBackendExtension.NodeToFeatureMap["FB_Qwen3TTSVoiceDesign"] = "qwen_tts";
        ComfyUIBackendExtension.NodeToFeatureMap["FB_Qwen3TTSVoiceClonePrompt"] = "qwen_tts";
        ComfyUIBackendExtension.NodeToFeatureMap["FB_Qwen3TTSRoleBank"] = "qwen_tts";
        ComfyUIBackendExtension.NodeToFeatureMap["FB_Qwen3TTSDialogueInference"] = "qwen_tts";
        InstallableFeatures.RegisterInstallableFeature(new(
            "Qwen3 TTS",
            "qwen_tts",
            "https://github.com/flybirdxx/ComfyUI-Qwen-TTS",
            "flybirdxx",
            "This will install the ComfyUI-Qwen-TTS custom nodes.\nDo you wish to install?"
        ));
    }

    private void RegisterParameters()
    {
        QwenTTSGroup = new(
            Name: "Qwen-TTS",
            Description: "Generate multi-character dialogue audio using the <audio> prompt section.",
            Toggles: true,
            Open: false,
            OrderPriority: -2.8
        );

        QwenTTSAdvancedGroup = new(
            Name: "Qwen-TTS Advanced Options",
            Toggles: false,
            Open: false,
            OrderPriority: 50,
            Description: "Sampling parameters for voice generation.",
            IsAdvanced: true,
            Parent: QwenTTSGroup
        );

        QwenTTSVoices = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "Qwen-TTS Voices",
            Description: "Internal JSON payload for Qwen-TTS voice entries.",
            Default: "[]",
            VisibleNormally: false,
            IsAdvanced: true,
            HideFromMetadata: true,
            DoNotPreview: true,
            Group: QwenTTSGroup,
            FeatureFlag: "comfyui"
        ));

        QwenTTSModel = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "Qwen-TTS Model",
            Description: "Qwen-TTS model to use for voice generation.\n"
                + "Set to 'None' to leave the extension deactivated.",
            Default: "None",
            GetValues: (_) => ["None", "0.6B", "1.7B"],
            Group: QwenTTSGroup,
            OrderPriority: 1,
            FeatureFlag: "comfyui"
        ));

        QwenTTSUseInVideo = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "Qwen-TTS Use in Video",
            Description: "When enabled with LTXV2 video model, injects Qwen-TTS\n"
                + "generated audio into the video workflow instead of generating audio-only output.",
            Default: "false",
            Group: QwenTTSGroup,
            OrderPriority: 2,
            FeatureFlag: "comfyui"
        ));

        QwenTTSMaxNewTokens = T2IParamTypes.Register<int>(new T2IParamType(
            Name: "Qwen-TTS Max New Tokens",
            Description: "Maximum number of new tokens to generate.",
            Default: "2048",
            Min: 1,
            Max: 8192,
            ViewMax: 4096,
            Step: 32,
            ViewType: ParamViewType.SLIDER,
            Group: QwenTTSAdvancedGroup,
            OrderPriority: 10,
            FeatureFlag: "comfyui"
        ));

        QwenTTSTopP = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "Qwen-TTS Top P",
            Description: "Nucleus sampling probability.",
            Default: "0.8",
            Min: 0,
            Max: 1,
            Step: 0.01,
            ViewType: ParamViewType.SLIDER,
            Group: QwenTTSAdvancedGroup,
            OrderPriority: 11,
            FeatureFlag: "comfyui"
        ));

        QwenTTSTopK = T2IParamTypes.Register<int>(new T2IParamType(
            Name: "Qwen-TTS Top K",
            Description: "Top-k sampling parameter.",
            Default: "20",
            Min: 1,
            Max: 100,
            Step: 1,
            ViewType: ParamViewType.SLIDER,
            Group: QwenTTSAdvancedGroup,
            OrderPriority: 12,
            FeatureFlag: "comfyui"
        ));

        QwenTTSTemperature = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "Qwen-TTS Temperature",
            Description: "Sampling temperature.",
            Default: "1",
            Min: 0,
            Max: 2,
            Step: 0.01,
            ViewType: ParamViewType.SLIDER,
            Group: QwenTTSAdvancedGroup,
            OrderPriority: 13,
            FeatureFlag: "comfyui"
        ));

        QwenTTSRepetitionPenalty = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "Qwen-TTS Repetition Penalty",
            Description: "Repetition penalty to reduce repetitive outputs.",
            Default: "1.05",
            Min: 0.5,
            Max: 2,
            Step: 0.01,
            ViewType: ParamViewType.SLIDER,
            Group: QwenTTSAdvancedGroup,
            OrderPriority: 14,
            FeatureFlag: "comfyui"
        ));

        QwenTTSAttention = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "Qwen-TTS Attention",
            Description: "Attention mechanism to use.",
            Default: "flash_attn",
            GetValues: (_) => ["auto", "sage_attn", "flash_attn", "sdpa", "eager"],
            Group: QwenTTSAdvancedGroup,
            OrderPriority: 15,
            FeatureFlag: "comfyui"
        ));

        QwenTTSUnloadModelAfterGenerate = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "Qwen-TTS Unload Model After Generate",
            Description: "Unload the model from memory after generation to free up VRAM.",
            Default: "false",
            Group: QwenTTSAdvancedGroup,
            OrderPriority: 16,
            FeatureFlag: "comfyui"
        ));
    }
}
