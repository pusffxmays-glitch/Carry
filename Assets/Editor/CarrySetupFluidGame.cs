using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Phase 11: 本番シーン (CastleStage) のゴブリンが運ぶ壺へ流体を組み込む (§21)。
//
// 液体は GoblinCarryRig が LateUpdate で置いた Carry_Pot の Transform を、
// Moving Boundary としてそのまま読む。実行順は
//   GoblinCarryRig.LateUpdate (order 0) -> FluidCore (100) -> FluidSurface (200)
// なので、その時点の壺の姿勢が必ず使われる。
// ゴブリンの姿勢から液面角度を直接決める経路は存在しない (§18)。
public static class CarrySetupFluidGame
{
    const string ScenePath = "Assets/Scenes/CastleStage.unity";
    const string CorePath = "Assets/Shaders/Fluid/FluidCore.compute";
    const string SurfacePath = "Assets/Shaders/Fluid/FluidSurface.compute";

    [MenuItem("Carry/Fluid/Phase 11 - Install Fluid Into CastleStage")]
    public static void Install()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath);

        var goblin = GameObject.Find("Goblin");
        if (goblin == null) { Debug.LogError("CarrySetupFluidGame: Goblin が見つかりません。"); return; }
        var pot = goblin.transform.Find("Carry_Pot");
        if (pot == null) { Debug.LogError("CarrySetupFluidGame: Carry_Pot が見つかりません。"); return; }

        // 削除済みスクリプト（旧 PotionLiquid 系）の MISSING 参照を掃除する。
        int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(pot.gameObject);

        var bnd = pot.GetComponent<FluidBoundary>();
        if (bnd == null) bnd = pot.gameObject.AddComponent<FluidBoundary>();
        bnd.mode = FluidBoundary.Mode.PotProfile;
        bnd.meshSource = pot;
        bnd.container = pot;
        bnd.rimFadePerKernel = 1.0f;

        var core = pot.GetComponent<FluidCore>();
        if (core == null) core = pot.gameObject.AddComponent<FluidCore>();
        core.fluidCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(CorePath);
        core.particleCount = 16384;
        core.fillFraction = 0.95f;     // 満タン
        core.maxSpeed = 5f;          // 跳ね上がる高さの上限
        core.minSubSteps = 6;
        core.maxSubSteps = 20;       // CFL を満たせない急な動きで発散するのを防ぐ (実測)        // Phase 12: 品質を保てる下限 (実測)
        core.viscosity = 2.8f;
        core.boundaryViscosity = 0.55f;
        core.boundaryPressureScale = 1.6f;
        core.groundY = 0f;              // Room_Floor の上面
        core.lateralSpread = 0.8f;    // 地面の水たまりが見える範囲
        core.groundMargin = 0.12f;
        core.topMargin = 1.2f;      // 跳ね上がった液体が天井で潰されない高さ
        core.groundLifetime = 10f;
        core.escapeAboveRim = true;        // ふちを越えた液体は戻さず地面へ
        core.escapeMarginSpacings = 2f;     // こぼれた液体を地面に残す

        var srf = pot.GetComponent<FluidSurface>();
        if (srf == null) srf = pot.gameObject.AddComponent<FluidSurface>();
        srf.surfaceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(SurfacePath);
        srf.liquidShader = Shader.Find("Custom/PotionLiquidSurface");
        // §14 Sparse Brick Pool。描画ドメインは 24m 角で、こぼした液体は 12m 離れるまで
        // 描画され続ける（以前は 1.8m の箱だったので、少し歩くと地面の液体が消え、
        // 箱の縁が四角い境界線として見えていた）。
        srf.domainSize = new Vector3(24f, 4.5f, 24f);
        srf.poolBrickCapacity = 16384;
        srf.maxTriangles = 2400000;   // 地面の液滴は 1 粒ずつ閉曲面になるので枚数が要る

        // §17: ゲージは Fluid の状態を読むだけ。逆向きに書く経路は無い。
        var gauge = Object.FindObjectOfType<PotionGaugeUI>();
        if (gauge != null) gauge.potionSourceBehaviour = core;
        if (gauge != null) gauge.fillColor = new Color(0.20f, 0.52f, 1.00f, 0.95f);
        else Debug.LogWarning("CarrySetupFluidGame: PotionGaugeUI がシーンに見つかりません。");

        EditorUtility.SetDirty(pot.gameObject);
        if (gauge != null) EditorUtility.SetDirty(gauge);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"CarrySetupFluidGame: 組み込み完了。MISSING スクリプト {removed} 個を除去。gauge={(gauge != null ? gauge.name : "なし")}");
    }
}
