using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Text2Image;

namespace QwenTTS;

public class Runner(WorkflowGenerator g)
{
    public void Run(bool isForAudio)
    {
        if (!IsExtensionActive())
        {
            return;
        }

        if (isForAudio)
        {
            new AudioWorkflow(g).Run();
        }
        else
        {
            new VideoWorkflow(g).Run();
        }
    }

    private bool IsExtensionActive()
    {
        T2IParamType type = QwenTTSExtension.QwenTTSModel?.Type;
        return type is not null && g.UserInput.TryGetRaw(type, out _);
    }
}
