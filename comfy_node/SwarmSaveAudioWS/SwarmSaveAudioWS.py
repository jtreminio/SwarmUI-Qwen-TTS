"""
SwarmSaveAudioWS – Send audio-only MP4 over websocket

Takes AUDIO from the pipeline (e.g. Qwen-TTS DialogueInference), encodes it as
AAC-in-MP4 (audio-only MP4), and sends it using the same websocket output format
that SwarmUI already understands for MP4 previews (type_num=5 -> VideoMp4).

Payload (data portion): 4 bytes type_num (5) + raw MP4 bytes.
Websocket wrapper (ComfyUI): 4 bytes BinaryEventTypes.PREVIEW_IMAGE (1) + payload above.
Progress: max=12346 so SwarmUI treats the next binary as a final output.
"""
import io
import os
import random
import struct
import subprocess
import tempfile
import wave

from server import PromptServer, BinaryEventTypes

# Same progress ID as video so backend sets isReceivingOutputs;
# we send eventId 4 so backend parses mime_type and returns AudioFlac.
PROGRESS_ID = 12346


def get_ffmpeg_exe():
    try:
        from imageio_ffmpeg import get_ffmpeg_exe as _get
        return _get()
    except Exception:
        return "ffmpeg"


def send_mp4_to_server(mp4_bytes: bytes):
    """
    Send an audio-only MP4 as SwarmUI 'video/mp4' output.

    SwarmUI parses binary frames as:
      - first 4 bytes: ComfyUI BinaryEventTypes (we use PREVIEW_IMAGE = 1)
      - next 4 bytes: format/type_num (5 -> VideoMp4)
      - remaining bytes: raw MP4 file bytes
    """
    if not mp4_bytes:
        return

    # Quick sanity check: most MP4s contain 'ftyp' in the first box.
    # This is not a full validation, but it helps catch "not actually MP4" cases early.
    if b"ftyp" not in mp4_bytes[:64]:
        raise RuntimeError("Generated MP4 bytes do not look like a valid MP4 (missing 'ftyp' header).")

    payload = struct.pack(">I", 5) + mp4_bytes
    server = PromptServer.instance
    server.send_sync("progress", {"value": PROGRESS_ID, "max": PROGRESS_ID}, sid=server.client_id)
    server.send_sync(BinaryEventTypes.PREVIEW_IMAGE, payload, sid=server.client_id)


class SwarmSaveAudioWS:
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "audio": ("AUDIO",),
            },
        }

    CATEGORY = "SwarmUI/audio"
    RETURN_TYPES = ()
    FUNCTION = "save_audio"
    OUTPUT_NODE = True
    DESCRIPTION = "Encodes audio as audio-only MP4 (AAC) and sends it to SwarmUI as a video/mp4 output."

    def save_audio(self, audio):
        if audio is None:
            return {}

        waveform = audio.get("waveform")
        sample_rate = audio.get("sample_rate")

        if waveform is None or sample_rate is None:
            return {}

        import numpy as np

        wav = waveform.cpu().numpy()

        if wav.ndim == 3:
            wav = wav[0]

        channels = wav.shape[0]
        wav_int16 = (np.clip(wav, -1.0, 1.0) * 32767).astype(np.int16)
        frames = wav_int16.T.tobytes()
        ffmpeg_exe = get_ffmpeg_exe()
        tmp_dir = tempfile.gettempdir()
        rand = "%016x" % random.getrandbits(64)
        wav_path = os.path.join(tmp_dir, "swarm_audio_%s.wav" % rand)
        mp4_path = os.path.join(tmp_dir, "swarm_audio_%s.mp4" % rand)

        try:
            with wave.open(wav_path, "wb") as wav_file:
                wav_file.setnchannels(channels)
                wav_file.setsampwidth(2)
                wav_file.setframerate(sample_rate)
                wav_file.writeframes(frames)

            result = subprocess.run(
                [
                    ffmpeg_exe,
                    "-v", "error",
                    "-y",
                    "-i", wav_path,
                    "-vn",
                    "-c:a", "aac",
                    # A reasonable default bitrate; quality is fine for TTS output
                    "-b:a", "192k",
                    # Make MP4 streamable (moov atom first)
                    "-movflags", "+faststart",
                    "-f", "mp4",
                    mp4_path,
                ],
                capture_output=True,
                timeout=60,
            )
            if result.returncode != 0:
                err = (result.stderr or result.stdout or b"").decode("utf-8", errors="replace").strip()
                raise RuntimeError("ffmpeg failed: %s" % (err or "unknown"))

            with open(mp4_path, "rb") as f:
                mp4_bytes = f.read()

            send_mp4_to_server(mp4_bytes)
        finally:
            for p in (wav_path, mp4_path):
                if os.path.isfile(p):
                    try:
                        os.remove(p)
                    except OSError:
                        pass
        return {}


NODE_CLASS_MAPPINGS = {
    "SwarmSaveAudioWS": SwarmSaveAudioWS,
}
