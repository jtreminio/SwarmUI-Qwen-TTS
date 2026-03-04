using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Text2Image;

namespace QwenTTS.Tests;

internal static class WorkflowTestHarness
{
    private static readonly object LockObj = new();
    private static bool _initialized;
    private static List<WorkflowGenerator.WorkflowGenStep> _qwenttsSteps = [];

    private static void EnsureInitialized()
    {
        lock (LockObj)
        {
            if (_initialized)
            {
                return;
            }

            List<WorkflowGenerator.WorkflowGenStep> before = [.. WorkflowGenerator.Steps];

            if (T2IParamTypes.Width is null)
            {
                T2IParamTypes.RegisterDefaults();
            }

            var ext = new QwenTTSExtension();
            ext.OnPreInit();
            ext.OnInit();

            List<WorkflowGenerator.WorkflowGenStep> after = [.. WorkflowGenerator.Steps];
            _qwenttsSteps = after.Where(step => !before.Contains(step)).ToList();

            WorkflowGenerator.Steps = before;

            if (_qwenttsSteps.Count == 0)
            {
                throw new InvalidOperationException("Qwen-TTS did not register any WorkflowGenerator steps during init.");
            }

            _initialized = true;
        }
    }

    public static IReadOnlyList<WorkflowGenerator.WorkflowGenStep> QwenTTSSteps()
    {
        EnsureInitialized();
        return _qwenttsSteps;
    }

    public static WorkflowGenerator.WorkflowGenStep MinimalGraphSeedStep() =>
        new(g =>
        {
            _ = g.CreateNode("UnitTest_Model", new JObject(), id: "4", idMandatory: false);
            _ = g.CreateNode("UnitTest_Latent", new JObject(), id: "10", idMandatory: false);

            g.CurrentModel = new WGNodeData(["4", 0], g, WGNodeData.DT_MODEL, g.CurrentCompat());
            g.CurrentTextEnc = new WGNodeData(["4", 1], g, WGNodeData.DT_TEXTENC, g.CurrentCompat());
            g.CurrentVae = new WGNodeData(["4", 2], g, WGNodeData.DT_VAE, g.CurrentCompat());
            g.CurrentMedia = new WGNodeData(["10", 0], g, WGNodeData.DT_LATENT_IMAGE, g.CurrentCompat());
            g.FinalLoadedModel = null;
            g.FinalLoadedModelList = [];
        }, -1000);

    public static WorkflowGenerator.WorkflowGenStep Ltx2TextToVideoSeedStep() =>
        new(g =>
        {
            SetLtxv2ModelClass(g);

            string audioVaeId = g.CreateNode("UnitTest_AudioVAE", new JObject(), id: "101", idMandatory: false);
            g.CurrentAudioVae = new WGNodeData([audioVaeId, 0], g, WGNodeData.DT_AUDIOVAE, g.CurrentCompat());

            string emptyAudioId = g.CreateNode("LTXVEmptyLatentAudio", new JObject
            {
                ["batch_size"] = 1,
                ["frames_number"] = 97,
                ["frame_rate"] = 24,
                ["audio_vae"] = g.CurrentAudioVae.Path
            }, id: "102", idMandatory: false);

            string emptyVideoId = g.CreateNode("EmptyLTXVLatentVideo", new JObject
            {
                ["batch_size"] = 1,
                ["length"] = 97,
                ["height"] = 512,
                ["width"] = 512
            }, id: "103", idMandatory: false);

            _ = g.CreateNode("LTXVConcatAVLatent", new JObject
            {
                ["video_latent"] = new JArray(emptyVideoId, 0),
                ["audio_latent"] = new JArray(emptyAudioId, 0)
            }, id: "104", idMandatory: false);
        }, -999);

    public static WorkflowGenerator.WorkflowGenStep Ltx2ImageToVideoSeedStep() =>
        new(g =>
        {
            SetLtxv2ModelClass(g);

            string audioVaeId = g.CreateNode("UnitTest_AudioVAE", new JObject(), id: "201", idMandatory: false);
            g.CurrentAudioVae = new WGNodeData([audioVaeId, 0], g, WGNodeData.DT_AUDIOVAE, g.CurrentCompat());

            string emptyAudioId = g.CreateNode("LTXVEmptyLatentAudio", new JObject
            {
                ["batch_size"] = 1,
                ["frames_number"] = 120,
                ["frame_rate"] = 24,
                ["audio_vae"] = g.CurrentAudioVae.Path
            }, id: "202", idMandatory: false);

            string emptyVideoId = g.CreateNode("EmptyLTXVLatentVideo", new JObject
            {
                ["batch_size"] = 1,
                ["length"] = 120,
                ["height"] = 512,
                ["width"] = 512
            }, id: "203", idMandatory: false);

            string imageId = g.CreateNode("UnitTest_Image", new JObject(), id: "204", idMandatory: false);
            string videoVaeId = g.CreateNode("UnitTest_VAE", new JObject(), id: "205", idMandatory: false);
            string preprocessId = g.CreateNode("LTXVPreprocess", new JObject
            {
                ["image"] = new JArray(imageId, 0)
            }, id: "206", idMandatory: false);

            string i2vId = g.CreateNode("LTXVImgToVideoInplace", new JObject
            {
                ["vae"] = new JArray(videoVaeId, 0),
                ["image"] = new JArray(preprocessId, 0),
                ["latent"] = new JArray(emptyVideoId, 0)
            }, id: "207", idMandatory: false);

            _ = g.CreateNode("LTXVConcatAVLatent", new JObject
            {
                ["video_latent"] = new JArray(i2vId, 0),
                ["audio_latent"] = new JArray(emptyAudioId, 0)
            }, id: "208", idMandatory: false);
        }, -999);

    public static JObject GenerateWithSteps(T2IParamInput input, IEnumerable<WorkflowGenerator.WorkflowGenStep> steps)
    {
        EnsureInitialized();
        List<WorkflowGenerator.WorkflowGenStep> priorSteps = [.. WorkflowGenerator.Steps];

        try
        {
            WorkflowGenerator.Steps = [.. steps.OrderBy(s => s.Priority)];
            input.ApplyLateSpecialLogic();

            var gen = new WorkflowGenerator
            {
                UserInput = input,
                Features = [],
                ModelFolderFormat = "/"
            };

            return gen.Generate();
        }
        finally
        {
            WorkflowGenerator.Steps = priorSteps;
        }
    }

    private static void SetLtxv2ModelClass(WorkflowGenerator g)
    {
        // Tests don't run the full SwarmUI bootstrap, so T2IModelClassSorter.ModelClasses may be uninitialized.
        // For workflow generation, all we need is a ModelClass with the LTXV2 compat class ID.
        var ltxv2Class = new T2IModelClass
        {
            ID = "lightricks-ltx-video-2",
            Name = "Lightricks LTX Video 2 (Test)",
            CompatClass = T2IModelClassSorter.CompatLtxv2,
            StandardWidth = 960,
            StandardHeight = 960,
            IsThisModelOfClass = (_, _) => false
        };

        g.FinalLoadedModel = new T2IModel(null!, "", "", "ltxv2-test") { ModelClass = ltxv2Class };
    }
}
