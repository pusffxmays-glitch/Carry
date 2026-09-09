#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Play-mode measurement only; never saved into a scene or included in a player.</summary>
[DefaultExecutionOrder(30000)]
public sealed class CarryForestFpsBenchmark : MonoBehaviour
{
    const string Output = "ScreenshotsOut/ForestSkybox/FPS";
    static readonly Vector3[] Positions = {
        new Vector3(-3.2f, 6.91f, -2.19f), new Vector3(0f, 5.5f, 55f), new Vector3(0f, 5.5f, 95f)
    };
    static readonly string[] Names = { "start", "middle", "end" };
    [Serializable] public struct Sample { public double milliseconds, gpuMilliseconds, mainThreadMilliseconds; public long triangles; }
    [Serializable] public sealed class ViewResult
    {
        public string view;
        public Vector3 cameraPosition;
        public double fps, meanMs, p95Ms, p99Ms, meanGpuMs, meanMainThreadMs, meanTriangles;
        public int frames, focusedFrames, gpuSamples;
        public List<Sample> samples;
    }
    [Serializable] public sealed class RunResult
    {
        public string label, scene, gpu, cpu, unity, graphicsApi, startedUtc;
        public int width = 1280, height = 720;
        public float renderScale, fieldOfView;
        public string method = "Editor Play mode; fixed camera rendered to 1280x720 RT; gameplay/physics/fluid continue; vSync off and FPS unlimited. 15s first warmup, 5s other warmups, then >=15s per view. Frame interval is real elapsed time between LateUpdates (includes Editor overhead). GPU and Main Thread are ProfilerRecorder counters; zero GPU means unavailable. No MCP calls during measured windows.";
        public List<ViewResult> views = new List<ViewResult>();
        public bool complete;
    }
    Camera cam;
    CarryCameraRig rig;
    bool rigEnabled, oldBackground, oldDynamic;
    int oldVsync, oldLimit;
    float oldAspect;
    Vector3 oldPosition;
    Quaternion oldRotation;
    RenderTexture target, oldTarget;
    ProfilerRecorder gpuRecorder, cpuRecorder, trianglesRecorder;
    RunResult result;
    readonly List<Sample> samples = new List<Sample>(4096);
    double phaseStart, previous;
    int phase, focused;
    bool running;

    public static string StartRun(string label)
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) throw new InvalidOperationException("Enter unpaused Play mode first.");
        if (FindFirstObjectByType<CarryForestFpsBenchmark>()) throw new InvalidOperationException("Benchmark already exists.");
        Directory.CreateDirectory(Output);
        if (File.Exists(Output + "/" + label + ".json")) throw new IOException("Run label already exists.");
        var go = new GameObject("__ForestFPS") { hideFlags = HideFlags.DontSave };
        var runner = go.AddComponent<CarryForestFpsBenchmark>();
        try { runner.Begin(label); }
        catch { Destroy(go); throw; }
        return "Started " + label + ": three fixed views; result in " + Output;
    }

    void Begin(string label)
    {
        cam = Camera.main;
        if (!cam) throw new InvalidOperationException("Main Camera missing.");
        rig = cam.GetComponent<CarryCameraRig>();
        rigEnabled = rig && rig.enabled;
        oldPosition = cam.transform.position; oldRotation = cam.transform.rotation;
        oldTarget = cam.targetTexture; oldAspect = cam.aspect; oldDynamic = cam.allowDynamicResolution;
        oldVsync = QualitySettings.vSyncCount; oldLimit = Application.targetFrameRate; oldBackground = Application.runInBackground;
        if (rig) rig.enabled = false;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; Application.runInBackground = true;
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        target = new RenderTexture(1280, 720, 24, cam.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default)
        { name = "ForestBenchmark1280x720", antiAliasing = cam.allowMSAA && pipeline ? pipeline.msaaSampleCount : 1 };
        target.Create(); cam.targetTexture = target; cam.aspect = 16f / 9f; cam.allowDynamicResolution = false;
        gpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1);
        cpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
        result = new RunResult { label = label, scene = gameObject.scene.path, gpu = SystemInfo.graphicsDeviceName,
            cpu = SystemInfo.processorType, unity = Application.unityVersion, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            startedUtc = DateTime.UtcNow.ToString("o"), renderScale = pipeline ? pipeline.renderScale : 1f, fieldOfView = cam.fieldOfView };
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        running = true;
        BeginView();
    }

    void BeginView()
    {
        cam.transform.SetPositionAndRotation(Positions[phase], Quaternion.Euler(26f, 0f, 0f));
        var sky = FindFirstObjectByType<ForestSkyboxBlend>();
        if (sky) sky.Refresh();
        samples.Clear(); focused = 0;
        previous = phaseStart = Time.realtimeSinceStartupAsDouble;
        File.WriteAllText(Output + "/progress.txt", result.label + " " + Names[phase] + " warming");
    }

    void LateUpdate()
    {
        if (!running) return;
        double now = Time.realtimeSinceStartupAsDouble;
        double warmup = phase == 0 ? 15 : 5;
        if (previous >= phaseStart + warmup)
        {
            samples.Add(new Sample { milliseconds = (now - previous) * 1000,
                gpuMilliseconds = gpuRecorder.Valid ? gpuRecorder.LastValue / 1000000.0 : 0,
                mainThreadMilliseconds = cpuRecorder.Valid ? cpuRecorder.LastValue / 1000000.0 : 0,
                triangles = trianglesRecorder.Valid ? trianglesRecorder.LastValue : 0 });
            if (Application.isFocused) focused++;
        }
        previous = now;
        if (now < phaseStart + warmup + 15 || samples.Count == 0) return;
        var ordered = samples.Select(s => s.milliseconds).OrderBy(x => x).ToArray();
        var gpu = samples.Where(s => s.gpuMilliseconds > 0).ToArray();
        result.views.Add(new ViewResult { view = Names[phase], cameraPosition = Positions[phase], frames = samples.Count,
            focusedFrames = focused, fps = 1000 / ordered.Average(), meanMs = ordered.Average(),
            p95Ms = ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(ordered.Length * .95) - 1)],
            p99Ms = ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(ordered.Length * .99) - 1)],
            meanGpuMs = gpu.Length > 0 ? gpu.Average(s => s.gpuMilliseconds) : 0, gpuSamples = gpu.Length,
            meanMainThreadMs = samples.Average(s => s.mainThreadMilliseconds), meanTriangles = samples.Average(s => (double)s.triangles),
            samples = new List<Sample>(samples) });
        phase++;
        if (phase < Positions.Length) { WriteResult(); BeginView(); return; }
        result.complete = true; WriteResult();
        File.WriteAllText(Output + "/progress.txt", result.label + " COMPLETE");
        running = false;
        Restore();
        Destroy(gameObject);
    }

    void WriteResult() => File.WriteAllText(Output + "/" + result.label + ".json", JsonUtility.ToJson(result, true));

    void Restore()
    {
        if (!target) return;
        if (cam)
        {
            cam.targetTexture = oldTarget; cam.aspect = oldAspect; cam.allowDynamicResolution = oldDynamic;
            cam.transform.SetPositionAndRotation(oldPosition, oldRotation);
        }
        if (rig) rig.enabled = rigEnabled;
        QualitySettings.vSyncCount = oldVsync; Application.targetFrameRate = oldLimit; Application.runInBackground = oldBackground;
        gpuRecorder.Dispose(); cpuRecorder.Dispose(); trianglesRecorder.Dispose();
        target.Release(); Destroy(target); target = null;
    }
    void OnDestroy() => Restore();
}
#endif
