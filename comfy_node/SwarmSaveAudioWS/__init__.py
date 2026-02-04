"""
SwarmSaveAudioWS – ComfyUI custom node package for SwarmUI-Qwen-TTS.

Register the node so ComfyUI discovers it. Copy this entire SwarmSaveAudioWS folder
into your ComfyUI custom_nodes directory (e.g. ComfyUI/custom_nodes/SwarmSaveAudioWS).
"""
import traceback
import os
import folder_paths

NODE_CLASS_MAPPINGS = {}

try:
    from . import SwarmSaveAudioWS as _mod
    NODE_CLASS_MAPPINGS.update(_mod.NODE_CLASS_MAPPINGS)
except Exception:
    print("Error: [SwarmSaveAudioWS] node not available")
    traceback.print_exc()

try:
    from . import SwarmAudioLengthToFrames as _frames_mod
    NODE_CLASS_MAPPINGS.update(_frames_mod.NODE_CLASS_MAPPINGS)
except Exception:
    print("Error: [SwarmAudioLengthToFrames] node not available")
    traceback.print_exc()


# Register qwen-tts model paths for ComfyUI-Qwen-TTS extension
# The extension checks folder_paths.get_folder_paths("TTS") to find models (see nodes.py line 339)
# We need to register qwen-tts paths as "TTS" type so the extension can find models there
try:
    import folder_paths
    
    # Get ComfyUI models directory
    comfy_models_dir = folder_paths.models_dir
    
    # Register ComfyUI's native qwen-tts directory as TTS type
    # This is where check_and_download_tokenizer() downloads models
    qwen_tts_native = os.path.join(comfy_models_dir, "qwen-tts")
    if os.path.exists(qwen_tts_native) and os.path.isdir(qwen_tts_native):
        try:
            folder_paths.add_model_folder_path("TTS", qwen_tts_native, is_default=False)
            print(f"[SwarmUI-Qwen-TTS] Registered TTS path: {qwen_tts_native}")
        except Exception:
            pass
    
    # Also register any qwen-tts paths from SwarmUI's YAML config
    # SwarmUI registers "qwen-tts" as a folder type in the YAML, but the extension looks for "TTS" type
    # So we need to register those paths as "TTS" type as well
    try:
        # Check if qwen-tts paths are registered (from SwarmUI's YAML config via extra_config.py)
        registered_qwen_tts = folder_paths.get_folder_paths("qwen-tts") or []
        registered_tts = folder_paths.get_folder_paths("TTS") or []
        
        for qwen_path in registered_qwen_tts:
            if qwen_path not in registered_tts:
                folder_paths.add_model_folder_path("TTS", qwen_path, is_default=False)
                print(f"[SwarmUI-Qwen-TTS] Registered TTS path from SwarmUI config: {qwen_path}")
    except Exception:
        # folder_paths.get_folder_paths("qwen-tts") might fail if "qwen-tts" isn't registered yet
        # This is okay - the YAML config will register it when ComfyUI loads
        pass
except Exception as e:
    print(f"[SwarmUI-Qwen-TTS] Warning: Could not register TTS paths: {e}")
