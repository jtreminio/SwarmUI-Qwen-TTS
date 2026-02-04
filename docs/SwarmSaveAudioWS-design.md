# SwarmSaveAudioWS – Custom ComfyUI node for audio over websocket

**Summary:** You can **(A)** tell SwarmUI you generated a video and send **audio-only MP4** over the existing video path (no core changes; frontend shows a `<video>` element—often **muted** by default, so audio may not play), or **(B)** send **FLAC** using **eventId 4 + metadata** over the same progress path (12346). The backend already parses eventId 4 and returns `MediaType.AudioFlac`; the frontend already shows `<audio controls>` for `audio/flac`, so **FLAC works with no backend or frontend changes** and playback is audible. SwarmUI-Qwen-TTS uses FLAC (eventId 4).

---

## Option A: Audio-only MP4 as “video” (no SwarmUI changes)

**Best option if you want zero core changes.** The backend already accepts “video” over the websocket (progress `12346`, binary with `format = 5` → `VideoMp4`). The frontend shows a `<video>` element; browsers can play **audio-only MP4** in a video element (they just show a black or static frame while the audio plays).

So you can:

1. **Generate audio-only MP4 instead of FLAC**  
   Use an MP4 container with only an audio track (e.g. AAC). Same as “audio-only H.264” in the sense of “MP4 container, audio codec only.”

2. **Send it over the existing video path**  
   A custom ComfyUI node takes the audio from your pipeline, encodes it as audio-only MP4, then sends:
   - `progress` with `max=12346` (same as `SwarmSaveAnimationWS`)
   - Binary payload: **4 bytes `eventId`** (e.g. `0`, not `10`) + **4 bytes `format = 5`** (VideoMp4) + **raw MP4 bytes**  
   (Backend uses `eventId != 10` so `format` 5 maps to `VideoMp4`; `preBytes = 8`.)

No backend or frontend changes. No placeholder image. User gets a “video” result that plays as audio (with a black frame).

**Payload format (big-endian):**

```python
import struct
from server import PromptServer, BinaryEventTypes

VIDEO_ID = 12346  # same as SwarmSaveAnimationWS

def send_audio_only_mp4_to_server(mp4_bytes: bytes):
    payload = struct.pack(">I", 0) + struct.pack(">I", 5) + mp4_bytes  # eventId 0, format 5 = VideoMp4
    server = PromptServer.instance
    server.send_sync("progress", {"value": VIDEO_ID, "max": VIDEO_ID}, sid=server.client_id)
    server.send_sync(BinaryEventTypes.PREVIEW_IMAGE, payload, sid=server.client_id)
```

The custom node only needs to: take audio from the Dialogue/Save Audio pipeline → encode to audio-only MP4 (e.g. via ffmpeg, pydub, or ComfyUI’s existing audio nodes if they support MP4) → call `send_audio_only_mp4_to_server(mp4_bytes)`.

---

## Option B: Native audio (FLAC) over websocket (no backend changes in practice)

**FLAC works today.** Send `progress` with `max=12346` (same as video) so `isReceivingOutputs` is true, then send binary with **eventId 4** + metadata `{"mime_type": "audio/flac", "id": 0}` + raw FLAC bytes. The backend’s `ComfyRawWebsocketOutputToFormatLabel` already handles eventId 4 and returns `MediaType.AudioFlac`; the output is stored as a `MediaFile` with that type, and the frontend’s `isAudioExt(src)` already shows an `<audio controls>` player for `data:audio/flac;base64,...`. So no backend or frontend changes are required. (The design doc previously described adding progress ID 12348 and an `AudioFile` branch; those are optional improvements. Using 12346 + eventId 4 is sufficient.)

## How the Swarm nodes work today (Option B – native audio)

1. **Progress ID** – The node sends `progress` with `max` in `{12345, 12346, 12347}` so the backend sets `isReceivingOutputs = true` and treats the next binary message as a final output.
2. **Binary payload** – The node sends raw bytes (e.g. PNG). The backend parses the first 4 bytes as `eventId`, then uses `ComfyRawWebsocketOutputToFormatLabel` to get `(MediaType, index, eventId, preBytes)` and builds `new Image(output[preBytes..], mediaType)` and calls `takeOutput(ImageOutput { File = ... })`.
3. **ImageOutput.File** is typed as `MediaFile`, so it can be an `AudioFile`; the frontend already uses `isAudioExt(src)` and shows an `<audio controls>` player for audio.

So the missing pieces are:

1. A **new progress ID** (e.g. `12348` = `AUDIO_ID`) so the backend sets `isReceivingOutputs` when the audio node runs.
2. When building the output, **if `mediaType.MetaType == MediaMetaType.Audio`**, create `new AudioFile(output[preBytes..], mediaType)` and pass that to `takeOutput` instead of `new Image(...)`.

No change is needed in `ComfyRawWebsocketOutputToFormatLabel`: the existing **eventId 4** (metadata) path already supports any `mime_type` in the metadata JSON (including `audio/flac`), so the node can send eventId 4 + metadata + raw FLAC and the backend will get `MediaType.AudioFlac` and the correct `preBytes`.

## Custom ComfyUI node (Python sketch)

The node should:

1. Take **audio** input (tensor or bytes from the Dialogue / Save Audio pipeline).
2. Send `progress` with `max=12348` so SwarmUI treats the next binary as output.
3. Send **binary** in the same format the backend already parses for eventId 4:
   - 4 bytes: `eventId = 4` (big-endian).
   - 4 bytes: length of metadata JSON (big-endian).
   - Metadata JSON, e.g. `{"mime_type": "audio/flac", "id": 0}`.
   - Raw audio bytes (e.g. FLAC).

Example (conceptual):

```python
import struct
from server import PromptServer, BinaryEventTypes

AUDIO_ID = 12348

def send_audio_to_server_raw(audio_bytes: bytes, mime_type: str = "audio/flac"):
    import json
    meta = json.dumps({"mime_type": mime_type, "id": 0}).encode("utf-8")
    out = io.BytesIO()
    out.write(struct.pack(">I", 4))       # eventId 4 = metadata format
    out.write(struct.pack(">I", len(meta)))
    out.write(meta)
    out.write(audio_bytes)
    payload = out.getvalue()
    server = PromptServer.instance
    server.send_sync("progress", {"value": AUDIO_ID, "max": AUDIO_ID}, sid=server.client_id)
    server.send_sync(BinaryEventTypes.PREVIEW_IMAGE, payload, sid=server.client_id)

class SwarmSaveAudioWS:
    # INPUT_TYPES: audio input from previous node
    # OUTPUT_NODE = True
    def save_audio(self, audio):  # audio = tensor or bytes
        # Convert to raw FLAC bytes if needed, then:
        send_audio_to_server_raw(flac_bytes, "audio/flac")
        return {}
```

The node can live in ComfyUI-Qwen-TTS or in SwarmUI’s Comfy extra nodes (e.g. next to `SwarmSaveImageWS`). The important part is using **progress max=12348** and **eventId 4 + metadata + raw audio** so the existing backend parsing and the two small backend changes below are enough.

## Backend changes (SwarmUI core)

**1. Add 12348 so the backend accepts audio output**

In `ComfyUIAPIAbstractBackend.cs`, where `isReceivingOutputs` is set (e.g. around line 414):

```csharp
isReceivingOutputs = max == 12345 || max == 12346 || max == 12347 || max == 12348;
```

**2. Emit AudioFile when the received media is audio**

In the same file, where the backend builds the output (e.g. around line 500), replace the single `takeOutput` that uses `new Image(...)` with a branch on `mediaType.MetaType`:

- If `mediaType.MetaType == MediaMetaType.Audio`: build `new AudioFile(output[preBytes..], mediaType)` and pass that in `ImageOutput.File`.
- Otherwise: keep `new Image(output[preBytes..], mediaType)` as today.

After that, a custom ComfyUI node that emits audio in the format above will work without generating any placeholder image.
