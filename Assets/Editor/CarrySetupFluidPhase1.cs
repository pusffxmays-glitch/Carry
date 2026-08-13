using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Phase 1 のテストシーンを構築する。壺は無い。単純なテスト箱の中で
// 「粘性のある 3D Fluid が安定して動く」ことだけを確認する (§37 Phase 1)。
public static class CarrySetupFluidPhase1
{
    const string ScenePath = "Assets/Scenes/FluidCoreTest.unity";
    const string ComputePath = "Assets/Shaders/Fluid/FluidCore.compute";

    [MenuItem("Carry/Fluid/Phase 1 - Build Fluid Core Test Scene")]
    public static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.35f;
        light.color = new Color(1f, 0.97f, 0.92f);
        light.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(46f, 150f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.45f, 0.51f, 0.60f);
        RenderSettings.ambientEquatorColor = new Color(0.34f, 0.35f, 0.36f);
        RenderSettings.ambientGroundColor = new Color(0.18f, 0.17f, 0.15f);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 40f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.18f, 0.21f);
        camGo.transform.position = new Vector3(0f, 1.35f, -2.15f);
        camGo.transform.rotation = Quaternion.Euler(12f, 0f, 0f);

        var fluidGo = new GameObject("FluidCore");
        fluidGo.transform.position = new Vector3(0f, 0.75f, 0f);
        var bnd = fluidGo.AddComponent<FluidBoundary>();
        bnd.mode = FluidBoundary.Mode.Box;
        bnd.boxInnerSize = new Vector3(1.0f, 1.2f, 1.0f);

        var core = fluidGo.AddComponent<FluidCore>();
        core.fluidCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
        core.particleCount = 16384;
        core.fillFraction = 0.45f;

        var view = fluidGo.AddComponent<FluidDebugView>();
        view.debugShader = Shader.Find("Hidden/Fluid/DebugParticles");
        // Phase 2 以降は Surface だけを見る。粒子表示は既定オフ (§10)。
        view.showParticles = false;

        var surface = fluidGo.AddComponent<FluidSurface>();
        surface.surfaceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Fluid/FluidSurface.compute");
        surface.liquidShader = Shader.Find("Custom/PotionLiquidSurface");

        fluidGo.AddComponent<FluidCoreTestRig>();

        EditorSceneManager.MarkSceneDirty(scene);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        Debug.Log("CarrySetupFluidPhase1: built " + ScenePath);
    }
}
