using SwarmUI.Utils;

namespace QwenTTS;

internal static class PromptParser
{
    public static bool TryGetAudioSection(string prompt, out string section, out string error)
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
}
