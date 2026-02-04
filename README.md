# SwarmUI Qwen‑TTS Extension

Generate natural‑sounding dialogue with multiple characters from a single prompt section.

## Features

- Multi‑speaker dialogue in one generation
- Three voice types: Custom, Design, and Audio‑clone
- Works for audio‑only or with video workflows
- Simple `<audio>` prompt tag for scripts

## Quick Start

1. Toggle the **Qwen‑TTS** group ON
2. Add one or more voices
3. Pick a model (0.6B or 1.7B)
4. Put your dialogue inside `<audio>...` in the main prompt
5. Generate

## How to Write the Dialogue

Use the `<audio>` section for the script. Each line starts with the voice name:

```
<audio>
Serena: Hello there!
Designer: Hi! How are you?
```

The voice name must match the **Name** you set for that voice in the UI.

## Voice Types

- **Custom Voice**: Type text and a style note for a consistent synthetic voice
- **Voice Design**: Describe the voice in text and let Qwen design it
- **Audio Clone**: Upload a reference audio clip to clone a voice

## Using with Video

Enable **Use in Video** to sync the generated audio with video workflows. Qwen‑TTS will drive the video length from the dialogue audio automatically.

- Currently only works with LTX-2 text-to-video and image-to-video workflows

## Tips

- Keep each speaker’s lines short for best pacing
- If you only want audio output, leave **Use in Video** off
- You can mix voice types in the same dialogue
