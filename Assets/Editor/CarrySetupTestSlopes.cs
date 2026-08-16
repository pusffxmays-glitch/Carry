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
        // 4b (2026-08-16): 氷は滑るだけで登れないため、右脇に高摩擦の上りレーンを併設。
        // 氷から滑り落ちてもレーンへ移れば登れる。ワープは氷側 (4) を使い回す。
        MakeSlope(root.transform, "Slope_Slippery_GripLane", 0, null,
                  new Vector3(12f + UpWidth * 0.5f + 0.62f, 0f, 8f), Quaternion.Euler(-AngleDeg, 0f, 0f),
                  new Vector3(1.2f, Thickness, UpLength), 1f, new Color(0.45f, 0.42f, 0.38f));

        // 奥の列 (z=11 以降)。手前の列 (z=4..12) と重ならない x に置く。
        MakeJumpPlatforms(root.transform, 5, "ジャンプで渡る台", new Vector3(-8f, 0f, 11f));
        MakeSwayingBridge(root.transform, 6, "揺れる橋", new Vector3(8f, 0f, 11f));
        // 7: 細い平均台。バランスを取りながら渡る (綱渡り歩容 + 減速 + こぼれ注意)。
        MakeBalanceBeam(root.transform, 7, "細い平均台", new Vector3(0f, 0f, 9f));
        // 8: 川。浮力と流れがあり、ぷかぷか浮かびながら流される (バタ足歩容)。
        MakeRiver(root.transform, 8, "川 (流れと浮力)", new Vector3(0f, 0f, -14.25f));
        // 9: 熱い床 (マグマ)。踏むと強制的に高く飛ばされる (あちちジャンプ)。
        MakeMagma(root.transform, 9, "熱い床 (マグマ)", new Vector3(13f, 0f, -8.5f));

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
        // warpNumber 0 以下はワープなし (付帯レーン用)。
        if (warpNumber > 0)
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

    // ----------------------------------------------------------------------------------------
    // 7: 細い平均台。登り坂 -> 台 A -> 幅 0.30m の細い梁 -> 台 B。
    // 梁には NarrowBeamSurface が付き、上では綱渡り歩容 (GoblinRopeGait) + 減速になる。
    // こぼれたポーションは梁や台の上に残る (GroundSurface = 流体の衝突対象)。
    // ----------------------------------------------------------------------------------------
    static void MakeBalanceBeam(Transform parent, int warpNumber, string label, Vector3 origin)
    {
        const float H = 0.8f;          // 台と梁の上面高さ
        const float PlatW = 1.6f;      // 台の一辺
        const float BeamW = 0.30f;     // 梁の幅
        const float BeamLen = 4.0f;    // 梁の長さ
        const float BeamThick = 0.12f;
        const float RampLen = 2.6f;    // 台 A への登り坂

        var group = new GameObject("Balance_Beam");
        group.transform.SetParent(parent, false);

        float rampDeg = Mathf.Asin(Mathf.Clamp01(H / RampLen)) * Mathf.Rad2Deg;
        float rampHorizontal = RampLen * Mathf.Cos(rampDeg * Mathf.Deg2Rad);
        float platAFront = origin.z + rampHorizontal;
        var ramp = MakeBox(group.transform, "Beam_Ramp",
                           new Vector3(origin.x, 0f, origin.z + rampHorizontal * 0.5f),
                           Quaternion.Euler(-rampDeg, 0f, 0f),
                           new Vector3(PlatW, Thickness, RampLen), 1f, new Color(0.55f, 0.5f, 0.42f));
        SinkTopEdgeToFloor(ramp, new Vector3(origin.x, 0f, origin.z + rampHorizontal * 0.5f));

        float aCentre = platAFront + PlatW * 0.5f;
        MakeBox(group.transform, "Beam_PlatformA",
                new Vector3(origin.x, H * 0.5f, aCentre), Quaternion.identity,
                new Vector3(PlatW, H, PlatW), 1f, new Color(0.5f, 0.46f, 0.4f));

        float beamCentre = platAFront + PlatW + BeamLen * 0.5f;
        var beam = MakeBox(group.transform, "Beam_Narrow",
                new Vector3(origin.x, H - BeamThick * 0.5f, beamCentre), Quaternion.identity,
                new Vector3(BeamW, BeamThick, BeamLen), 1f, new Color(0.72f, 0.6f, 0.35f));
        beam.AddComponent<NarrowBeamSurface>();

        float bCentre = platAFront + PlatW + BeamLen + PlatW * 0.5f;
        MakeBox(group.transform, "Beam_PlatformB",
                new Vector3(origin.x, H * 0.5f, bCentre), Quaternion.identity,
                new Vector3(PlatW, H, PlatW), 1f, new Color(0.5f, 0.46f, 0.4f));

        MakeWarpPoint(parent, warpNumber, label, new Vector3(origin.x, 0.03f, origin.z - 2.0f));
    }

    // ----------------------------------------------------------------------------------------
    // 8: 川。低い側壁の水路 (幅 3.1m x 長さ 14m x 水深 0.95m)。WaterVolume が浮力と流れ (+X)
    // を与え、GoblinSwimmer + GoblinSwimGait で「壺を担いだままバタ足で浮かぶ」。
    // 入水は入口の台 (壁と同じ高さ) から。脱出はジャンプ (Space) で壁を越える。
    // ----------------------------------------------------------------------------------------
    static void MakeRiver(Transform parent, int warpNumber, string label, Vector3 centre)
    {
        const float Len = 14f;       // X 方向
        const float WidthIn = 3.1f;  // 内側の水幅 (Z)
        // 2026-08-16: 水深 0.95 → 1.2 (腰のあたりで浮く感じに)。壁とランプも連動。
        const float WallH = 1.3f;
        const float WallT = 0.3f;
        const float WaterTop = 1.2f;

        var group = new GameObject("River");
        group.transform.SetParent(parent, false);

        var wallCol = new Color(0.45f, 0.45f, 0.5f);
        // 側壁 (Z の前後) と端の壁 (X の両端)
        MakeBox(group.transform, "River_Wall_N",
                new Vector3(centre.x, WallH * 0.5f, centre.z + WidthIn * 0.5f + WallT * 0.5f),
                Quaternion.identity, new Vector3(Len + WallT * 2f, WallH, WallT), 1f, wallCol);
        MakeBox(group.transform, "River_Wall_S",
                new Vector3(centre.x, WallH * 0.5f, centre.z - WidthIn * 0.5f - WallT * 0.5f),
                Quaternion.identity, new Vector3(Len + WallT * 2f, WallH, WallT), 1f, wallCol);
        MakeBox(group.transform, "River_Cap_E",
                new Vector3(centre.x + Len * 0.5f + WallT * 0.5f, WallH * 0.5f, centre.z),
                Quaternion.identity, new Vector3(WallT, WallH, WidthIn), 1f, wallCol);
        MakeBox(group.transform, "River_Cap_W",
                new Vector3(centre.x - Len * 0.5f - WallT * 0.5f, WallH * 0.5f, centre.z),
                Quaternion.identity, new Vector3(WallT, WallH, WidthIn), 1f, wallCol);

        // 水 (トリガー + 半透明の見た目)。forward (+Z) を +X に向けて「流れ」の向きにする。
        var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
        water.name = "River_Water";
        water.transform.SetParent(group.transform, false);
        water.transform.rotation = Quaternion.Euler(0f, 90f, 0f);   // forward = +X
        water.transform.localScale = new Vector3(WidthIn, WaterTop, Len);   // 回転後: X=Len, Z=WidthIn
        water.transform.position = new Vector3(centre.x, WaterTop * 0.5f, centre.z);
        var wcol = water.GetComponent<BoxCollider>();
        wcol.isTrigger = true;
        var wmr = water.GetComponent<MeshRenderer>();
        var wmat = new Material(wmr.sharedMaterial) { name = "River_Water_Mat" };
        wmat.color = new Color(0.20f, 0.45f, 0.85f, 0.45f);
        wmat.SetFloat("_Surface", 1f);
        wmat.SetOverrideTag("RenderType", "Transparent");
        wmat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        wmat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        wmat.SetInt("_ZWrite", 0);
        wmat.renderQueue = 3000;
        wmat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        wmr.sharedMaterial = wmat;
        wmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        water.AddComponent<WaterVolume>().flowSpeed = 1.8f;   // 2026-08-16: 1.2 → 1.8 (もう少し速く)

        // 入水用の台 + 登り坂 (壁の上面と同じ高さ)。手前 (+Z) 側から。
        float platZ = centre.z + WidthIn * 0.5f + WallT + 0.8f;
        MakeBox(group.transform, "River_Entry_Platform",
                new Vector3(centre.x - 5.5f, WallH * 0.5f, platZ),
                Quaternion.identity, new Vector3(1.6f, WallH, 1.6f), 1f, new Color(0.55f, 0.5f, 0.42f));
        const float RampLen = 4.6f;   // 壁 1.3 で約 16.4 度を維持。急坂だと入水前に大きくこぼれる
        float rampDeg = Mathf.Asin(Mathf.Clamp01(WallH / RampLen)) * Mathf.Rad2Deg;
        float rampHorizontal = RampLen * Mathf.Cos(rampDeg * Mathf.Deg2Rad);
        var rampCentre = new Vector3(centre.x - 5.5f, 0f, platZ + 0.8f + rampHorizontal * 0.5f);
        var ramp = MakeBox(group.transform, "River_Entry_Ramp",
                rampCentre, Quaternion.Euler(rampDeg, 0f, 0f),   // -Z へ向かって登る
                new Vector3(1.6f, Thickness, RampLen), 1f, new Color(0.5f, 0.46f, 0.4f));
        SinkTopEdgeToFloor(ramp, rampCentre);

        // ワープ (坂の手前、川の方 (-Z) を向く)
        var wp = new GameObject("WarpPoint_" + warpNumber);
        wp.transform.SetParent(parent, false);
        wp.transform.position = new Vector3(centre.x - 5.5f, 0.03f, platZ + 0.8f + rampHorizontal + 2.0f);
        wp.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        var p = wp.AddComponent<GimmickWarpPoint>();
        p.number = warpNumber;
        p.label = label;
    }

    // ----------------------------------------------------------------------------------------
    // 9: 熱い床 (マグマ)。薄い発光オレンジの帯 (幅 3.5m x 長さ 8m)。踏むと HotFloorSurface が
    // GoblinLocomotion に検出され、強制ハイジャンプ (初速 8.5 m/s、高さ約 1.8m) + あちちアニメ。
    // 帯の途中に安全な石の足場を 2 つ置き、バウンドしながら渡るコースにする。
    // ----------------------------------------------------------------------------------------
    static void MakeMagma(Transform parent, int warpNumber, string label, Vector3 centre)
    {
        const float Wd = 3.5f;    // X
        const float Ln = 8f;      // Z
        const float Top = 0.06f;  // 薄い帯。段差としてはほぼ感じない高さ

        var group = new GameObject("Magma");
        group.transform.SetParent(parent, false);

        // マグマ本体。URP Lit の Emission で光らせる。
        var magma = MakeBox(group.transform, "Magma_Floor",
                new Vector3(centre.x, Top * 0.5f, centre.z),
                Quaternion.identity, new Vector3(Wd, Top, Ln), 1f, new Color(1.0f, 0.35f, 0.05f));
        var mmat = magma.GetComponent<MeshRenderer>().sharedMaterial;
        mmat.EnableKeyword("_EMISSION");
        mmat.SetColor("_EmissionColor", new Color(1.0f, 0.25f, 0.02f) * 2.0f);
        magma.AddComponent<HotFloorSurface>();

        // 安全地帯: 石の足場 (マグマより一段高い)。ここでは飛ばされない。
        var stoneCol = new Color(0.40f, 0.38f, 0.36f);
        MakeBox(group.transform, "Magma_Stone_1",
                new Vector3(centre.x, 0.09f, centre.z + 1.5f),
                Quaternion.identity, new Vector3(1.1f, 0.18f, 1.1f), 1f, stoneCol);
        MakeBox(group.transform, "Magma_Stone_2",
                new Vector3(centre.x, 0.09f, centre.z - 1.7f),
                Quaternion.identity, new Vector3(1.1f, 0.18f, 1.1f), 1f, stoneCol);

        // ワープ (帯の手前 +Z 側、マグマの方 (-Z) を向く)
        var wp = new GameObject("WarpPoint_" + warpNumber);
        wp.transform.SetParent(parent, false);
        wp.transform.position = new Vector3(centre.x, 0.03f, centre.z + Ln * 0.5f + 2.0f);
        wp.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        var p = wp.AddComponent<GimmickWarpPoint>();
        p.number = warpNumber;
        p.label = label;
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
        // 2026-08-15: ベイク済みクリップ再生 (ツボおろし/転倒/壺なし)、E キーの状態管理、
        // 細い足場センサー
        if (loco.GetComponent<GoblinClipAnimator>() == null) loco.gameObject.AddComponent<GoblinClipAnimator>();
        if (loco.GetComponent<GoblinPotActions>() == null) loco.gameObject.AddComponent<GoblinPotActions>();
        if (loco.GetComponent<NarrowBeamSensor>() == null) loco.gameObject.AddComponent<NarrowBeamSensor>();
        if (loco.GetComponent<GoblinSwimmer>() == null) loco.gameObject.AddComponent<GoblinSwimmer>();
        EditorUtility.SetDirty(loco.gameObject);

        // 部屋を広げたぶん、地面の水たまりが描画される範囲も広げる。
        // 高さ 6.5m: 上り勾配 (15 度 x 8m = +2.07m) の頂上で壺のリムが y≒5.1m に達する。
        // 4.5m のままだと頂上付近で壺の中の液体が描画ドメインの上端からはみ出して
        // 見た目からも消える (2026-08-15 バグ報告の一部)。Sparse Brick Pool なので
        // ドメインを縦に広げても増えるのは Brick 索引だけで、毎フレームのコストは
        // 液体の量にしか比例しない。
        var srf = Object.FindObjectOfType<FluidSurface>();
        if (srf != null)
        {
            srf.domainSize = new Vector3(30f, 6.5f, 30f);
            EditorUtility.SetDirty(srf);
        }
    }
}
