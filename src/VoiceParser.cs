using Newtonsoft.Json.Linq;

namespace QwenTTS;

internal static class VoiceParser
{
    public static bool TryParse(string json, out List<VoiceSpec> voices, out string error)
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

                string rawType = GetString(obj, "type");
                if (string.IsNullOrWhiteSpace(rawType))
                {
                    error = "voice entry is missing type.";
                    return false;
                }

                VoiceType type = ParseVoiceType(rawType, out string typeError);
                if (type == VoiceType.Unknown)
                {
                    error = typeError;
                    return false;
                }

                VoiceSpec voice = new(
                    TypeRaw: rawType,
                    Type: type,
                    Name: GetString(obj, "name")?.Trim(),
                    ReferenceText: GetString(obj, "referenceText") ?? "",
                    StyleInstruction: GetString(obj, "styleInstruction") ?? "",
                    Speaker: GetString(obj, "speaker") ?? "",
                    AudioBase64: GetString(obj, "audioBase64") ?? ""
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

    private static VoiceType ParseVoiceType(string raw, out string error)
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
        {
            error = $"unknown voice type '{raw}'.";
        }

        return result;
    }

    private static string GetString(JObject obj, string key)
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
}
