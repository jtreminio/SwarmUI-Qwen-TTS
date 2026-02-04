"""
SwarmAudioLengthToFrames – Compute video frame count from audio length.

Takes AUDIO from the pipeline (e.g. Qwen-TTS DialogueInference), computes its
duration in seconds, and outputs:
  - frames = duration_sec * frame_rate + 1
  - audio passthrough for downstream nodes

Use this so the generated video length matches the generated audio.
"""
import torch


def _num_samples(waveform):
    """Return number of samples (last dimension). Handles (C, S) or (B, C, S)."""
    if waveform is None:
        return 0
    if isinstance(waveform, torch.Tensor):
        return int(waveform.shape[-1])
    return 0


class SwarmAudioLengthToFrames:
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "audio": ("AUDIO",),
                "frame_rate": ("INT", {"default": 24, "min": 1, "max": 120}),
            },
        }

    CATEGORY = "SwarmUI/audio"
    RETURN_TYPES = ("AUDIO", "INT")
    RETURN_NAMES = ("audio", "frames")
    FUNCTION = "compute"
    DESCRIPTION = "Compute video frame count from audio length: frames = duration_sec * frame_rate + 1"

    def compute(self, audio, frame_rate):
        if audio is None:
            return (None, 1)

        waveform = audio.get("waveform")
        sample_rate = audio.get("sample_rate")

        if waveform is None or sample_rate is None or sample_rate <= 0:
            return (audio, 1)

        num_samples = _num_samples(waveform)

        if num_samples <= 0:
            return (audio, 1)

        duration_sec = num_samples / float(sample_rate)
        frames = max(1, int(round(duration_sec * frame_rate)) + 1)

        return (audio, frames)


NODE_CLASS_MAPPINGS = {
    "SwarmAudioLengthToFrames": SwarmAudioLengthToFrames,
}
