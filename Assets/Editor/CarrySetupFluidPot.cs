using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Phase 4〜6 のテストシーン。実際の Carry_Pot メッシュを容器として使い、
// 壺内部境界（Boundary Particles）と Moving Boundary を検証する (§37 Phase 4/5/6)。
public static class CarrySetupFluidPot
{
    const string ScenePath = "Assets/Scenes/FluidPotTest.unity";
    const string PotFbxPath = "Assets/Pot/Carry_Pot.fbx";
    const string CorePath = "Assets/Shaders/Fluid/FluidCore.compute";
    const string SurfacePath = "Assets/Shaders/Fluid/FluidSurface.compute";
    const float PotScale = 2.366f;

    [MenuItem("Carry/Fluid/Phase 4 - Build Fluid Pot Test Scene")]
    public static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.4f;
        light.color = new Color(1f, 0.97f, 0.92f);
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(44f, 148f, 0f);

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
        camGo.transform.position = new Vector3(0.05f, 2.55f, -2.15f);
        camGo.transform.rotation = Quaternion.Euler(31f, -1f, 0f);

        var floorMat = MakeLitMaterial("Assets/Pot/Mat_TestFloor.mat", new Color(0.31f, 0.31f, 0.33f), 0.12f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0f, -0.25f, 0f);
        floor.transform.localScale = new Vector3(24f, 0.5f, 24f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

        var potPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PotFbxPath);
        if (potPrefab == null) { Debug.LogError("CarrySetupFluidPot: could not load " + PotFbxPath); return; }
        var pot = (GameObject)PrefabUtility.InstantiatePrefab(potPrefab);
        pot.name = "Carry_Pot";
        pot.transform.position = new Vector3(0f, 0.95f, 0f);
        pot.transform.localScale = Vector3.one * PotScale;
        // 流体は壺の内部形状（実測プロファイル）と衝突する。MeshCollider は使わない。
        foreach (var c in pot.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

        var bnd = pot.AddComponent<FluidBoundary>();
        bnd.mode = FluidBoundary.Mode.PotProfile;
        bnd.meshSource = pot.transform;
        bnd.container = pot.transform;
        bnd.rimFadePerKernel = 1.0f;    // 開口端の境界斥力フェード (OI-1 対策1)

        var core = pot.AddComponent<FluidCore>();
        core.fluidCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(CorePath);
        core.particleCount = 16384;
        core.maxSpeed = 5f;          // 跳ね上がる高さの上限
        core.minSubSteps = 6;
        core.maxSubSteps = 20;       // CFL を満たせない急な動きで発散するのを防ぐ (実測)        // Phase 12: 品質を保てる下限 (実測)
        core.viscosity = 2.8f;          // dt 比例化後の値 (Phase 6 と同じ効き)
        core.boundaryViscosity = 0.55f; // 剛体回転比 0.844 (実測)
        core.boundaryPressureScale = 1.6f;  // 壁の貫通 465 -> 309 個 (実測)
        core.fillFraction = 0.95f;     // 満タン
        core.simPadding = 0.45f;
        core.groundY = 0f;              // Floor の上面 (y=0)
        core.lateralSpread = 0.8f;    // 地面の水たまりが見える範囲     // 注ぎ出した液体が横へ広がる余地 (Phase 7 実測)
        core.groundMargin = 0.12f;
        core.topMargin = 1.2f;      // 跳ね上がった液体が天井で潰されない高さ
        core.rimOpeningHeight = 0.08f;

        var surface = pot.AddComponent<FluidSurface>();
        surface.surfaceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(SurfacePath);
        surface.liquidShader = Shader.Find("Custom/PotionLiquidSurface");

        var view = pot.AddComponent<FluidDebugView>();
        view.debugShader = Shader.Find("Hidden/Fluid/DebugParticles");
        view.showParticles = false;

        var rig = pot.AddComponent<FluidCoreTestRig>();
        rig.restPosition = new Vector3(0f, 0.95f, 0f);

        // §17: PotionVolume は Fluid の状態から導かれる。ゲージは FluidCore を
        // IPotionVolumeSource として参照するだけで、逆向きに書き込む経路は無い。
        var gaugeGo = new GameObject("PotionGauge");
        var gauge = gaugeGo.AddComponent<PotionGaugeUI>();
        gauge.potionSourceBehaviour = core;
        if (gauge != null) gauge.gaugeFillColor = new Color(0.20f, 0.52f, 1.00f, 0.95f);

        EditorSceneManager.MarkSceneDirty(scene);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        Debug.Log("CarrySetupFluidPot: built " + ScenePath);
    }

    static Material MakeLitMaterial(string path, Color color, float smoothness)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
