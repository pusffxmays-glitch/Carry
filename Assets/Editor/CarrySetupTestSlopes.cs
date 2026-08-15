using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// ============================================================================================
// CarrySetupTestSlopes -- 検証用ステージ。部屋を広げ、ギミックとデバッグワープを置く。
//
// ギミック（数字キーでその手前へワープ、ポーションは満タンに戻る）:
//   1  Slope_BankLeft   左傾斜   … +Z へ歩くとき左 (-X) が下がる
//   2  Slope_Up         上り勾配 … +Z へ歩くと登る
//   3  Slope_BankRight  右傾斜   … +Z へ歩くとき右 (+X) が下がる
//   4  Slope_Slippery   摩擦の低い上り勾配（氷）
//   5  Jump_Platforms   ジャンプで渡る 2 つの台（隙間 1.6m）
//   6  Bridge_Swaying   左右に揺れる橋
//
// 置き方の要点:
//   * 傾けたあと、**上面のいちばん低い角が床の高さに来るまで沈める**。
//     こうしないと、傾けた箱は片側が床に埋まり片側が浮くので、乗れる場所が無くなる。
//   * CharacterController の slopeLimit は 50 度なので 15 度は問題なく歩ける。
//   * 摩擦は GroundSurface で持たせる。CharacterController は PhysicMaterial の摩擦を
//     見ないので、PhysicMaterial を貼っても滑らない（GoblinGroundSlide が自前で滑らせる）。
//
// 何度でも実行してよい。実行のたびに古い TestSlopes を作り直す。
// ============================================================================================
public static class CarrySetupTestSlopes
{
    const string RootName = "TestSlopes";

    // 傾斜角。ここを変えれば全部変わる。
    const float AngleDeg = 15f;

    const float Thickness = 0.6f;
    const float UpLength = 8f;    // 上り勾配の長さ (Z)
    const float UpWidth = 4.5f;
    const float BankLength = 8f;  // 傾斜路の長さ (Z)
    const float BankWidth = 4f;

    // 部屋の拡大率。Room_* は Blender からの取り込みで rotation (270,0,0) なので、
    // **local X/Y が world XZ、local Z が world Y** に対応する。
    // local X/Y だけ拡大すれば、高さを変えずに床面だけ広げられる。
    const float RoomScale = 1.75f;   // 24m 角 -> 42m 角

    [MenuItem("Carry/Setup/Test Stage を作る（部屋拡大 + ギミック + デバッグワープ）")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();

        EnlargeRoom();

        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject(RootName);

        // 部屋は 42m 角。ギミックは +Z 側に並べ、手前(-Z)から歩いて乗れるようにする。
        MakeSlope(root.transform, "Slope_BankLeft", 1, "左傾斜",
                  new Vector3(-12f, 0f, 8f), Quaternion.Euler(0f, 0f, AngleDeg),
                  new Vector3(BankWidth, Thickness, BankLength), 1f, new Color(0.55f, 0.60f, 0.66f));

        MakeSlope(root.transform, "Slope_Up", 2, "上り勾配",
                  new Vector3(-4f, 0f, 8f), Quaternion.Euler(-AngleDeg, 0f, 0f),
                  new Vector3(UpWidth, Thickness, UpLength), 1f, new Color(0.62f, 0.60f, 0.55f));

        MakeSlope(root.transform, "Slope_BankRight", 3, "右傾斜",
                  new Vector3(4f, 0f, 8f), Quaternion.Euler(0f, 0f, -AngleDeg),
                  new Vector3(BankWidth, Thickness, BankLength), 1f, new Color(0.66f, 0.56f, 0.50f));

        // 4: 摩擦の低い上り勾配。氷。
        MakeSlope(root.transform, "Slope_Slippery", 4, "氷の勾配（低摩擦）",
                  new Vector3(12f, 0f, 8f), Quaternion.Euler(-AngleDeg, 0f, 0f),
                  new Vector3(UpWidth, Thickness, UpLength), 0.08f, new Color(0.62f, 0.85f, 0.95f));

        // 奥の列 (z=11 以降)。手前の列 (z=4..12) と重ならない x に置く。
        MakeJumpPlatforms(root.transform, 5, "ジャンプで渡る台", new Vector3(-8f, 0f, 11f));
        MakeSwayingBridge(root.transform, 6, "揺れる橋", new Vector3(8f, 0f, 11f));

        EnsureDebugComponents();

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"CarrySetupTestSlopes: 部屋を {RoomScale}倍 に拡大し、{AngleDeg} 度のギミックを 4 つ作成。" +
                  "数字キー 1-4 でワープ（ポーション満タン）。", root);
    }

    // Room_* を world XZ 方向にだけ拡大する。高さ (world Y = local Z) は据え置き。
    static void EnlargeRoom()
    {
        string[] names = { "Room_Floor", "Room_Wall_North", "Room_Wall_South", "Room_Wall_East", "Room_Wall_West" };
        foreach (var n in names)
        {
            var g = GameObject.Find(n);
            if (g == null) { Debug.LogWarning($"CarrySetupTestSlopes: {n} が見つかりません。"); continue; }
            var s = g.transform.localScale;
            // 元の取り込みスケールは 100。何度実行しても同じ結果になるよう、毎回 100 から作る。
            g.transform.localScale = new Vector3(100f * RoomScale, 100f * RoomScale, 100f);
            EditorUtility.SetDirty(g);
        }
    }

    static void MakeSlope(Transform parent, string name, int warpNumber, string label,
                          Vector3 centre, Quaternion rot, Vector3 size, float friction, Color colour)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.rotation = rot;
        go.transform.localScale = size;
        go.transform.position = centre;

        // 上面 4 隅のうち **いちばん低い角** を床の高さ(0)に合わせる。
        // 傾けただけだと片側が浮いて乗れないので、ここで沈める。
        float lowest = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            var local = new Vector3(((i & 1) == 0) ? -0.5f : 0.5f, 0.5f, ((i & 2) == 0) ? -0.5f : 0.5f);
            lowest = Mathf.Min(lowest, go.transform.TransformPoint(local).y);
        }
        go.transform.position = centre + Vector3.down * (lowest - centre.y) + Vector3.down * 0.02f;

        var mr = go.GetComponent<MeshRenderer>();
        var mat = new Material(mr.sharedMaterial) { name = name + "_Mat", color = colour };
        mr.sharedMaterial = mat;

        var surf = go.AddComponent<GroundSurface>();
        surf.friction = friction;
        surf.label = label;

        // ワープ先はスロープの手前 (-Z 側)、床の上。向きはスロープを向く (+Z)。
        MakeWarpPoint(go.transform.parent, warpNumber, label,
                      new Vector3(centre.x, 0.03f, centre.z - size.z * 0.5f - 2.5f));
    }

    // ----------------------------------------------------------------------------------------
    // 5: ジャンプで渡る台。スロープで登り、段差の空いた 2 つの台を跳んで渡る。
    //
    // 隙間の広さは「走ってジャンプすれば届くが、歩きジャンプでは届かない」を狙う。
    // jumpSpeed 6 m/s、重力 -20 なので滞空 0.6 秒。
    //   走り 3.0 m/s → 約 1.8m 跳べる / 歩き 1.0 m/s → 約 0.6m
    // よって隙間 1.6m。
    // ----------------------------------------------------------------------------------------
    static void MakeJumpPlatforms(Transform parent, int warpNumber, string label, Vector3 origin)
    {
        const float H = 0.8f;        // 台の高さ
        const float W = 3.5f;        // 幅
        const float RampLen = 2.2f;  // 登り坂の長さ
        const float PlatLen = 2.7f;  // 台の長さ
        const float Gap = 1.6f;      // 隙間

        var group = new GameObject("Jump_Platforms");
        group.transform.SetParent(parent, false);
        group.transform.position = Vector3.zero;

        // 登り坂。上端がちょうど台の高さ H になる角度にする。
        float rampDeg = Mathf.Asin(Mathf.Clamp01(H / RampLen)) * Mathf.Rad2Deg;
        float rampHorizontal = RampLen * Mathf.Cos(rampDeg * Mathf.Deg2Rad);
        float platAStartZ = origin.z + rampHorizontal;
        var ramp = MakeBox(group.transform, "Jump_Ramp",
                           new Vector3(origin.x, 0f, origin.z + rampHorizontal * 0.5f),
                           Quaternion.Euler(-rampDeg, 0f, 0f),
                           new Vector3(W, Thickness, RampLen), 1f, new Color(0.60f, 0.58f, 0.52f));
        SinkTopEdgeToFloor(ramp, new Vector3(origin.x, 0f, origin.z + rampHorizontal * 0.5f));

        // 台 2 つ。厚み H の箱を中心 H/2 に置けば上面が H になる。
        float aCentre = platAStartZ + PlatLen * 0.5f;
        MakeBox(group.transform, "Jump_PlatformA",
                new Vector3(origin.x, H * 0.5f, aCentre), Quaternion.identity,
                new Vector3(W, H, PlatLen), 1f, new Color(0.58f, 0.56f, 0.50f));

        float bCentre = platAStartZ + PlatLen + Gap + PlatLen * 0.5f;
        MakeBox(group.transform, "Jump_PlatformB",
                new Vector3(origin.x, H * 0.5f, bCentre), Quaternion.identity,
                new Vector3(W, H, PlatLen), 1f, new Color(0.58f, 0.56f, 0.50f));

        MakeWarpPoint(parent, warpNumber, label, new Vector3(origin.x, 0.03f, origin.z - 2.5f));
    }

    // ----------------------------------------------------------------------------------------
    // 6: 揺れる橋。長手方向まわりに左右へロールする板。
    // 乗っている相手は SwayingBridge が自分で運ぶ（CharacterController は動く床に乗らない）。
    // ----------------------------------------------------------------------------------------
    static void MakeSwayingBridge(Transform parent, int warpNumber, string label, Vector3 origin)
    {
        const float Len = 8f;
        const float W = 2.6f;
        const float H = 0.35f;
        const float Deck = 0.5f;   // 板の上面の高さ

        var go = MakeBox(parent, "Bridge_Swaying",
                         new Vector3(origin.x, Deck - H * 0.5f, origin.z + Len * 0.5f),
                         Quaternion.identity,
                         new Vector3(W, H, Len), 1f, new Color(0.52f, 0.40f, 0.28f));
        var sway = go.AddComponent<SwayingBridge>();
        sway.rollAmplitudeDeg = 8f;
        sway.rollPeriod = 2.6f;
        sway.bobAmplitude = 0.05f;
        sway.bobPeriod = 1.7f;

        // 橋の高さ (0.5m) へ乗るための短い坂。
        float rampLen = 1.6f;
        float rampDeg = Mathf.Asin(Mathf.Clamp01(Deck / rampLen)) * Mathf.Rad2Deg;
        float rampHorizontal = rampLen * Mathf.Cos(rampDeg * Mathf.Deg2Rad);
        var centre = new Vector3(origin.x, 0f, origin.z - rampHorizontal * 0.5f);
        var ramp = MakeBox(parent, "Bridge_Ramp", centre, Quaternion.Euler(-rampDeg, 0f, 0f),
                           new Vector3(W, Thickness, rampLen), 1f, new Color(0.50f, 0.38f, 0.26f));
        SinkTopEdgeToFloor(ramp, centre);

        MakeWarpPoint(parent, warpNumber, label,
                      new Vector3(origin.x, 0.03f, origin.z - rampHorizontal - 2.0f));
    }

    // 箱を 1 つ作る。摩擦マーカーと色をつける。
    static GameObject MakeBox(Transform parent, string name, Vector3 centre, Quaternion rot,
                              Vector3 size, float friction, Color colour)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.rotation = rot;
        go.transform.localScale = size;
        go.transform.position = centre;

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(mr.sharedMaterial) { name = name + "_Mat", color = colour };

        var surf = go.AddComponent<GroundSurface>();
        surf.friction = friction;
        surf.label = name;
        return go;
    }

    // 傾けた箱の「上面のいちばん低い角」を床の高さに合わせる。
    // これをしないと片側が床に埋まり片側が浮いて、乗れる場所が無くなる。
    static void SinkTopEdgeToFloor(GameObject go, Vector3 centre)
    {
        float lowest = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            var local = new Vector3(((i & 1) == 0) ? -0.5f : 0.5f, 0.5f, ((i & 2) == 0) ? -0.5f : 0.5f);
            lowest = Mathf.Min(lowest, go.transform.TransformPoint(local).y);
        }
        go.transform.position = centre + Vector3.down * (lowest - centre.y) + Vector3.down * 0.02f;
    }

    static void MakeWarpPoint(Transform parent, int number, string label, Vector3 pos)
    {
        var wp = new GameObject("WarpPoint_" + number);
        wp.transform.SetParent(parent, false);
        wp.transform.position = pos;
        wp.transform.rotation = Quaternion.identity;   // +Z を向く
        var p = wp.AddComponent<GimmickWarpPoint>();
        p.number = number;
        p.label = label;
    }

    // ゴブリンにデバッグ用コンポーネントを付ける。既にあれば何もしない。
    static void EnsureDebugComponents()
    {
        var loco = Object.FindObjectOfType<GoblinLocomotion>();
        if (loco == null) { Debug.LogWarning("CarrySetupTestSlopes: GoblinLocomotion が見つかりません。"); return; }

        if (loco.GetComponent<GoblinTerrainTilt>() == null) loco.gameObject.AddComponent<GoblinTerrainTilt>();
        if (loco.GetComponent<GoblinGroundSlide>() == null) loco.gameObject.AddComponent<GoblinGroundSlide>();
        if (loco.GetComponent<DebugGimmickWarp>() == null) loco.gameObject.AddComponent<DebugGimmickWarp>();
        EditorUtility.SetDirty(loco.gameObject);

        // 部屋を広げたぶん、地面の水たまりが描画される範囲も広げる。
        var srf = Object.FindObjectOfType<FluidSurface>();
        if (srf != null)
        {
            srf.domainSize = new Vector3(30f, 4.5f, 30f);
            EditorUtility.SetDirty(srf);
        }
    }
}
