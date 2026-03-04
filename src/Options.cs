using SwarmUI.Text2Image;

namespace QwenTTS;

internal enum VoiceType
{
    Unknown,
    CustomVoice,
    VoiceDesign,
    AudioFile
}

internal sealed record VoiceSpec(
    string TypeRaw,
    VoiceType Type,
    string Name,
    string ReferenceText,
    string StyleInstruction,
    string Speaker,
    string AudioBase64
);

internal sealed record SamplingParams(
    string ModelChoice,
    long BaseSeed,
    int MaxNewTokens,
    double TopP,
    int TopK,
    double Temperature,
    double RepetitionPenalty,
    string Attention,
    bool UnloadModelAfterGenerate
)
{
    public static SamplingParams FromUserInput(T2IParamInput input) => new(
        ModelChoice: input.Get(QwenTTSExtension.QwenTTSModel, "None"),
        BaseSeed: input.Get(T2IParamTypes.Seed, -1L),
        MaxNewTokens: input.Get(QwenTTSExtension.QwenTTSMaxNewTokens, 2048),
        TopP: input.Get(QwenTTSExtension.QwenTTSTopP, 0.8),
        TopK: input.Get(QwenTTSExtension.QwenTTSTopK, 20),
        Temperature: input.Get(QwenTTSExtension.QwenTTSTemperature, 1.0),
        RepetitionPenalty: input.Get(QwenTTSExtension.QwenTTSRepetitionPenalty, 1.05),
        Attention: input.Get(QwenTTSExtension.QwenTTSAttention, "flash_attn"),
        UnloadModelAfterGenerate: input.Get(QwenTTSExtension.QwenTTSUnloadModelAfterGenerate, false)
    );
}

internal static class NodeHelperKeys
{
    public const string AudioSave = "qwentts_audio_save";
}

internal static class NodeTypes
{
    public const string CustomVoice = "FB_Qwen3TTSCustomVoice";
    public const string VoiceDesign = "FB_Qwen3TTSVoiceDesign";
    public const string VoiceClonePrompt = "FB_Qwen3TTSVoiceClonePrompt";
    public const string RoleBank = "FB_Qwen3TTSRoleBank";
    public const string DialogueInference = "FB_Qwen3TTSDialogueInference";
    public const string SwarmInputAudio = "SwarmInputAudio";
    public const string AudioLengthToFrames = "SwarmAudioLengthToFrames";
    public const string LTXVEmptyLatentAudio = "LTXVEmptyLatentAudio";
    public const string LTXVConcatAVLatent = "LTXVConcatAVLatent";
    public const string EmptyLTXVLatentVideo = "EmptyLTXVLatentVideo";
    public const string SolidMask = "SolidMask";
    public const string SetLatentNoiseMask = "SetLatentNoiseMask";
}

internal static class NodeDefaults
{
    public const string PlaceholderReferenceText = "Reference sample.";
    public const string PlaceholderStyleInstruction = "Neutral speaking style.";
    public const string CustomVoiceDevice = "cuda";
    public const string CustomVoicePrecision = "bf16";
    public const string CustomVoiceLanguage = "English";
    public const string CustomVoiceSpeaker = "Serena";
    public const string VoiceDesignDevice = "auto";
    public const string VoiceDesignPrecision = "bf16";
    public const string VoiceDesignLanguage = "English";
    public const string VoiceClonePromptDevice = "auto";
    public const string VoiceClonePromptPrecision = "bf16";
    public const string DialogueDevice = "auto";
    public const string DialoguePrecision = "bf16";
    public const string DialogueLanguage = "Auto";
    public const double PauseLinebreak = 0.5;
    public const double PeriodPause = 0.4;
    public const double CommaPause = 0.2;
    public const double QuestionPause = 0.6;
    public const double HyphenPause = 0.3;
    public const bool MergeOutputs = true;
    public const int BatchSize = 4;
}
