using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-16 v3: 川のコース(石橋から上流)を全面刷新。
//  - PathSlab(path_stone.fbx)は「足場として見えにくい」との指摘で廃止し、丸太積み/切り株/根の
//    塊など実在感のあるプラットフォーム系アセットへ置き換え。
//  - 岩・丸太・切り株・根の組み合わせバリエーションを大幅に拡張(単調な繰り返しを避ける)。
//  - ギミックとして「地面が斜めに傾いている区間」を2箇所、「ジャンプしないと渡れない隙間」を
//    2箇所、コース中に設置。
//
// v4(2026-08-16): 「物理判定がアセットより大きく設定されておりゴブリンが浮いている瞬間がある」
// との指摘で、見た目のRenderer.boundsを実測してColliderを合わせる方式に変更。
//
// v5(2026-08-16): 「ジャンプで届かない隙間がある」との指摘で、隙間ギミックの中央に踏み台の岩を追加。
//
// v6(2026-08-16): 「アセットやTerrainのメッシュに沿ってあたり判定を設定。見えないものには判定を
// 付けない。あれば削除。」との指摘。v4/v5はRenderer.boundsから作った「軸なしBoxCollider」を
// 見た目とは別のGameObjectとして敷いていた ―― ボックスは実際のメッシュ形状(凹凸・隙間)を無視して
// 直方体で覆うため、特に不定形な岩・根の塊では見た目の外側(何も描画されていない空間)にまで
// 判定が広がっていた(＝「見えない判定」)。加えて隣接ステーションとの継ぎ目を確実に埋めるため
// 奥行きを人為的にかさ増し(minZDepth)しており、これも実際のメッシュより判定を広げる一因だった。
// v6では別GameObjectのBoxColliderを廃止し、各アセットの実インスタンスに直接
// MeshCollider(非convex)を付与して判定形状を見た目メッシュそのものに一致させる。継ぎ目の連続性は
// 「判定を水増しする」のではなく「アセット自体のZ方向スケールを、隣接ステーションと実際に重なる
// 大きさまで正直に引き伸ばす」ことで確保する(EnsureMinZFootprint)。
public static class CarryBuildRiverFootholdCourse
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const string PH = "Assets/ExternalAssets/PolyHaven/";
    const string Kenney = "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/";

    const float ZStart = 9f;
    const float ZEnd = 110f;
    const float StationSpacing = 2.0f;
    const float BankClearance = 6f;
    const float WideWidth = 3.0f;
    const float NarrowWidth = 1.4f;
    const float ZoneLength = 20f;
    const float ZoneBlend = 4f;
    const float MinFootprintDepth = StationSpacing * 1.6f; // 隣接ステーションと実メッシュが重なるよう保証する最低奥行き(正直にスケールで確保、判定の水増しはしない)
    const float GapStoneOverlapMargin = 0.3f; // 隙間ギミック用踏み台岩を、隣接メッシュの実測端より片側この分だけ多く覆わせる

    static readonly (float z0, float z1)[] TiltZones = { (20f, 26f), (55f, 61f) };
    static readonly (float z0, float z1)[] GapZones = { (37f, 39.5f), (75f, 77.5f) };

    enum StationKind { Normal, Tilted, Gap }

    [MenuItem("Carry/Build River Foothold Course (Gapless)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find("ForestStage_Terrain");
            if (root == null) { Debug.LogError("ForestStage_Terrain not found."); return; }

            var oldFoot = root.transform.Find("Footholds");
            if (oldFoot != null) Object.DestroyImmediate(oldFoot.gameObject);

            var footRoot = new GameObject("Footholds");
            footRoot.transform.SetParent(root.transform, false);

            var t = typeof(CarryBuildTerrainForest);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var riverXM = t.GetMethod("RiverX", flags);
            var riverHalfWidthM = t.GetMethod("RiverHalfWidth", flags);
            var lakeFactorM = t.GetMethod("LakeFactor", flags);
            var computeBridgeDeckYM = t.GetMethod("ComputeBridgeDeckY", flags);
            var bridgeDeckWorldYAtM = t.GetMethod("BridgeDeckWorldYAt", flags);
            var loadMossRocksM = t.GetMethod("LoadIndividualMossRocks", flags);
            var getTopLocalYM = t.GetMethod("GetPrefabTopLocalY", flags);

            float bridgeDeckY = (float)computeBridgeDeckYM.Invoke(null, null);
            float bridgeX = (float)riverXM.Invoke(null, new object[] { 8f });
            float bridgeDeckYAtConnection = (float)bridgeDeckWorldYAtM.Invoke(null, new object[] { bridgeX });

            // ---- アセット読み込み ----
            var boulderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "boulder_01/boulder_01_2k.fbx");
            var logPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "dead_tree_trunk_02/dead_tree_trunk_02_2k.fbx");
            var logPrefab2 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "dead_tree_trunk/dead_tree_trunk_2k.fbx");
            var stumpPrefab1 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "tree_stump_01/tree_stump_01_2k.fbx");
            var stumpPrefab2 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "tree_stump_02/tree_stump_02_2k.fbx");
            var rootCluster1 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "root_cluster_01/root_cluster_01_1k.fbx");
            var rootCluster2 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "root_cluster_02/root_cluster_02_2k.fbx");
            var pineRoots = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
            var mossRocks = (GameObject[])loadMossRocksM.Invoke(null, null);

            var kenneyLogStack = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "log_stackLarge.fbx");
            var kenneyStumpSquare = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "stump_squareDetailedWide.fbx");
            var kenneyStumpRound = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "stump_roundDetailed.fbx");

            Material woodMat = null;
            var kenneyWoodMat = AssetDatabase.LoadAssetAtPath<Material>(Kenney + "Materials/woodBark.mat");
            if (kenneyWoodMat != null)
            {
                woodMat = new Material(kenneyWoodMat);
                woodMat.name = "OldPlatformWood";
                Color baseCol = new Color(0.28f, 0.22f, 0.15f);
                if (woodMat.HasProperty("_BaseColor")) woodMat.SetColor("_BaseColor", baseCol);
                if (woodMat.HasProperty("_Color")) woodMat.SetColor("_Color", baseCol);
                if (woodMat.HasProperty("_Smoothness")) woodMat.SetFloat("_Smoothness", 0.1f);
            }

            if (boulderPrefab == null || logPrefab == null)
            {
                Debug.LogError("Required prefabs not found.");
                return;
            }

            var rng = new System.Random(7073);

            // ---- 中心線とステーションを生成 ----
            var stations = new List<(float z, float x, float y, float halfWidth, StationKind kind, float tiltDeg)>();
            for (float z = ZStart; z <= ZEnd; z += StationSpacing)
            {
                float rx = (float)riverXM.Invoke(null, new object[] { z });
                float hw = (float)riverHalfWidthM.Invoke(null, new object[] { z });

                float zoneT = (z - ZStart) / ZoneLength;
                int zoneIndex = Mathf.FloorToInt(zoneT);
                float withinZone = (zoneT - zoneIndex) * ZoneLength;
                bool isWideZone = (zoneIndex % 2 == 0);
                bool nextIsWideZone = !isWideZone;
                float target = isWideZone ? WideWidth : NarrowWidth;
                float nextTarget = nextIsWideZone ? WideWidth : NarrowWidth;
                float blendT = Mathf.Clamp01((withinZone - (ZoneLength - ZoneBlend)) / ZoneBlend);
                float targetWidth = Mathf.Lerp(target, nextTarget, blendT);

                float meander = Mathf.Sin(z * 0.045f + 3f) * 1.2f + Mathf.Sin(z * 0.017f + 9f) * 1.6f;
                float connectT = Mathf.Clamp01((z - ZStart) / 6f);
                float centerX = Mathf.Lerp(bridgeX, rx + meander, connectT);

                float maxHalfWidth = Mathf.Max(0.6f, hw - BankClearance - Mathf.Abs(centerX - rx));
                float halfWidth = Mathf.Min(targetWidth * 0.5f, maxHalfWidth);
                float maxCenterOffset = Mathf.Max(0f, hw - BankClearance - halfWidth);
                float offsetFromRx = Mathf.Clamp(centerX - rx, -maxCenterOffset, maxCenterOffset);
                centerX = rx + offsetFromRx;

                float centerY = Mathf.Lerp(bridgeDeckYAtConnection, bridgeDeckY, connectT);

                StationKind kind = StationKind.Normal;
                float tiltDeg = 0f;
                foreach (var gz in GapZones) if (z >= gz.z0 && z <= gz.z1) kind = StationKind.Gap;
                if (kind == StationKind.Normal)
                {
                    foreach (var tz in TiltZones)
                    {
                        if (z >= tz.z0 && z <= tz.z1)
                        {
                            kind = StationKind.Tilted;
                            float mid = (tz.z0 + tz.z1) * 0.5f;
                            float span = (tz.z1 - tz.z0) * 0.5f;
                            float edgeT = Mathf.Clamp01(1f - Mathf.Abs(z - mid) / Mathf.Max(0.01f, span));
                            tiltDeg = 16f * edgeT;
                        }
                    }
                }
                if (kind == StationKind.Tilted) halfWidth = Mathf.Min(Mathf.Max(halfWidth, WideWidth * 0.5f), maxHalfWidth);

                stations.Add((z, centerX, centerY, halfWidth, kind, tiltDeg));
            }

            // ---- アセットのカテゴリ分け ----
            var platformChoices = new List<GameObject>();
            if (kenneyLogStack != null) platformChoices.Add(kenneyLogStack);
            if (kenneyStumpSquare != null) platformChoices.Add(kenneyStumpSquare);
            if (kenneyStumpRound != null) platformChoices.Add(kenneyStumpRound);
            if (rootCluster2 != null) platformChoices.Add(rootCluster2);

            var accentChoices = new List<GameObject>();
            if (stumpPrefab1 != null) accentChoices.Add(stumpPrefab1);
            if (stumpPrefab2 != null) accentChoices.Add(stumpPrefab2);
            if (rootCluster1 != null) accentChoices.Add(rootCluster1);
            if (pineRoots != null) accentChoices.Add(pineRoots);
            if (mossRocks != null) accentChoices.AddRange(mossRocks);

            var logChoices = new List<GameObject>();
            if (logPrefab != null) logChoices.Add(logPrefab);
            if (logPrefab2 != null) logChoices.Add(logPrefab2);

            int visualCount = 0, tiltedVisuals = 0, gapVisuals = 0, colliderCount = 0;
            GameObject lastUsed = null;
            var chain = new List<GameObject>(); // 継続性(隙間なし)に責任を持つ「主役」インスタンスだけを順に記録する

            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                bool wide = s.halfWidth * 2f >= (WideWidth + NarrowWidth) * 0.5f;

                if (s.kind == StationKind.Gap)
                {
                    if (i == 0 || stations[i - 1].kind != StationKind.Gap)
                    {
                        float eScale = 1.5f + (float)rng.NextDouble() * 0.4f;
                        var eInst = InstantiateWithTop(boulderPrefab, footRoot.transform, s.x, s.y + 0.08f, s.z, eScale, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), getTopLocalYM, "GapEdge_" + i);
                        gapVisuals++; // 意図的にColliderなし(ジャンプ必須)
                    }
                    continue;
                }

                if (s.kind == StationKind.Tilted)
                {
                    // 傾斜ギミック: MeshColliderは常にインスタンス自身のtransform(位置・スケール・回転)
                    // をそのまま使うため、傾き(s.tiltDeg)を含む見た目の回転がそのまま判定に反映される
                    // (v4までのような「傾きだけを別Boxで再現する」回避策が不要になった)。
                    bool useRoot = rootCluster2 != null && rng.Next(2) == 0;
                    GameObject chosenT = useRoot ? rootCluster2 : boulderPrefab;
                    float yawT = (float)rng.NextDouble() * 40f - 20f;
                    var rotT = Quaternion.Euler(0f, yawT, s.tiltDeg);
                    float scaleT = useRoot ? Mathf.Max(1f, s.halfWidth * 2f / 2.38f) : (2.0f + (float)rng.NextDouble() * 0.6f);
                    Vector3 scaleVecT = useRoot
                        ? new Vector3(scaleT, 1.2f, Mathf.Max(1f, MinFootprintDepth / 2.71f * 1.2f))
                        : EnsureMinZFootprint(chosenT, Vector3.one * scaleT, MinFootprintDepth);
                    var tInst = InstantiateWithTop(chosenT, footRoot.transform, s.x, s.y + 0.02f, s.z, scaleVecT, rotT, getTopLocalYM, "TiltPlatform_" + i);
                    AddMeshColliders(tInst);
                    chain.Add(tInst);
                    tiltedVisuals++; colliderCount++;
                    continue;
                }

                // ---- 通常区間 ----
                GameObject primaryInst = null;

                if (wide)
                {
                    var candidates = new List<GameObject>(platformChoices);
                    if (lastUsed != null) candidates.RemoveAll(c => c == lastUsed);
                    if (candidates.Count == 0) candidates = platformChoices;
                    var chosen = candidates.Count > 0 ? candidates[rng.Next(candidates.Count)] : boulderPrefab;
                    lastUsed = chosen;
                    bool isKenneyPlatform = chosen == kenneyLogStack || chosen == kenneyStumpSquare || chosen == kenneyStumpRound;
                    // プラットフォーム系は「幅をターゲットに合わせてスケーリング」した後に大きく
                    // Y回転させると見た目の外形が斜めに張り出す(MeshColliderはそれを正確に反映
                    // するのでケガはしないが、意図した安全回廊からはみ出す恐れがあるため、抑制は
                    // 引き続き維持する)。
                    var rot = Quaternion.Euler(isKenneyPlatform ? (float)rng.NextDouble() * 3f - 1.5f : 0f, (float)rng.NextDouble() * 24f - 12f, isKenneyPlatform ? (float)rng.NextDouble() * 3f - 1.5f : 0f);

                    if (isKenneyPlatform)
                    {
                        var b0 = GetLocalSize(chosen);
                        float scaleX = Mathf.Max(1f, (s.halfWidth * 2f) / Mathf.Max(0.2f, b0.x));
                        float scaleZ = Mathf.Max((MinFootprintDepth) / Mathf.Max(0.2f, b0.z), 1f);
                        float scaleY = 1.1f + (float)rng.NextDouble() * 0.3f;
                        primaryInst = InstantiateWithTop(chosen, footRoot.transform, s.x, s.y, s.z, new Vector3(scaleX, scaleY, scaleZ), rot, getTopLocalYM, "PlatformKenney_" + i);
                        if (woodMat != null)
                            foreach (var mr in primaryInst.GetComponentsInChildren<MeshRenderer>())
                            {
                                var mats = new Material[mr.sharedMaterials.Length];
                                for (int mi = 0; mi < mats.Length; mi++) mats[mi] = woodMat;
                                mr.sharedMaterials = mats;
                            }
                    }
                    else
                    {
                        var b0 = GetLocalSize(chosen);
                        float scaleX = Mathf.Max(1f, (s.halfWidth * 2f) / Mathf.Max(0.5f, b0.x));
                        Vector3 scaleVec = EnsureMinZFootprint(chosen, Vector3.one * scaleX, MinFootprintDepth);
                        primaryInst = InstantiateWithTop(chosen, footRoot.transform, s.x, s.y + 0.03f, s.z, scaleVec, rot, getTopLocalYM, "PlatformRoot_" + i);
                    }
                    AddMeshColliders(primaryInst);
                    chain.Add(primaryInst);
                    visualCount++; colliderCount++;

                    // 縁のアクセント(装飾): 継続性は主役プラットフォームが担うので、Z方向の
                    // かさ増しはせず実寸のままMeshColliderを付ける。
                    if (rng.Next(3) != 0 && accentChoices.Count > 0)
                    {
                        var acc = accentChoices[rng.Next(accentChoices.Count)];
                        float aScale = 0.5f + (float)rng.NextDouble() * 0.5f;
                        float side = rng.Next(2) == 0 ? 1f : -1f;
                        float ex = s.x + side * Mathf.Max(0.3f, s.halfWidth - 0.4f);
                        var ainst = InstantiateWithTop(acc, footRoot.transform, ex, s.y + 0.05f, s.z, aScale, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), getTopLocalYM, "WideAccent_" + i);
                        AddMeshColliders(ainst);
                        visualCount++; colliderCount++;
                    }
                }
                else
                {
                    int pick = rng.Next(3);
                    if (pick == 0 && logChoices.Count > 0)
                    {
                        var chosen = logChoices[rng.Next(logChoices.Count)];
                        var nextIdx = Mathf.Min(i + 1, stations.Count - 1);
                        var s2 = stations[nextIdx];
                        Vector3 logDir = new Vector3(s2.x - s.x, 0f, s2.z - s.z);
                        if (logDir.sqrMagnitude < 0.01f) logDir = Vector3.forward;
                        float span = Mathf.Max(MinFootprintDepth, logDir.magnitude + StationSpacing * 1.3f);
                        var b0 = GetLocalSize(chosen);
                        float logScale = Mathf.Clamp(span / Mathf.Max(0.5f, b0.x), 1.0f, 5.0f);
                        var rot = Quaternion.LookRotation(logDir.normalized) * Quaternion.Euler(0f, 90f, 0f);
                        primaryInst = InstantiateWithTop(chosen, footRoot.transform, s.x, s.y + 0.05f, s.z, logScale, rot, getTopLocalYM, "PathLog_" + i);
                        visualCount++;
                    }
                    else if (pick == 1 && accentChoices.Count > 0)
                    {
                        // EnsureMinZFootprintはprefabのローカルZ軸を引き伸ばすため、大きくヨー回転
                        // させるとその「奥行き」が進行方向(ワールドZ)からずれてしまい、隣接ステー
                        // ションと実際には重ならなくなる(実測で発覚)。継続性を担う主役ピースは
                        // 回転を小さく抑える(装飾のWideAccent/GapEdge/PathBoulderは無関係なので
                        // 引き続き自由回転のまま)。
                        var chosen = accentChoices[rng.Next(accentChoices.Count)];
                        float scale2 = 0.8f + (float)rng.NextDouble() * 0.6f;
                        Vector3 scaleVec2 = EnsureMinZFootprint(chosen, Vector3.one * scale2, MinFootprintDepth);
                        primaryInst = InstantiateWithTop(chosen, footRoot.transform, s.x, s.y + 0.04f, s.z, scaleVec2, Quaternion.Euler(0f, (float)rng.NextDouble() * 36f - 18f, 0f), getTopLocalYM, "PathAccent_" + i);
                        visualCount++;
                    }
                    else if (mossRocks != null && mossRocks.Length > 0)
                    {
                        var chosen = mossRocks[rng.Next(mossRocks.Length)];
                        float scale3 = 0.8f + (float)rng.NextDouble() * 0.5f;
                        Vector3 scaleVec3 = EnsureMinZFootprint(chosen, Vector3.one * scale3, MinFootprintDepth);
                        primaryInst = InstantiateWithTop(chosen, footRoot.transform, s.x, s.y + 0.04f, s.z, scaleVec3, Quaternion.Euler(0f, (float)rng.NextDouble() * 36f - 18f, 0f), getTopLocalYM, "PathRock_" + i);
                        visualCount++;
                    }
                }

                if (primaryInst != null)
                {
                    AddMeshColliders(primaryInst);
                    chain.Add(primaryInst);
                    colliderCount++;
                }

                if (wide && i % 5 == 0)
                {
                    float bScale = 1.5f + (float)rng.NextDouble() * 0.7f;
                    var bInst = InstantiateWithTop(boulderPrefab, footRoot.transform, s.x, s.y + 0.08f, s.z, bScale, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), getTopLocalYM, "PathBoulder_" + i);
                    // 既にこのstationにprimaryInstのColliderがある場合はそちらを優先し、
                    // このランドマーク岩は視覚のみ(重複Colliderで段差を作らないため)。
                    visualCount++;
                }
            }

            // ---- 隙間ギミックの中間に踏み台の大岩を追加する ----
            // chainを実測(Renderer.bounds、パディングなし)のZ順に並べ、1.5m以上の隙間(=意図的な
            // ジャンプギミック2箇所)の中央に凹凸のある岩を置く。両隣の実測端よりさらに
            // GapStoneOverlapMarginぶん多く覆う大きさにし、MeshCollider化で判定が実メッシュに
            // 忠実になっても継ぎ目が空かないようにする。
            {
                chain.Sort((a, b) => GetRealBounds(a).center.z.CompareTo(GetRealBounds(b).center.z));
                int steppingStones = 0;
                var newStones = new List<GameObject>();
                for (int i = 1; i < chain.Count; i++)
                {
                    var beforeB = GetRealBounds(chain[i - 1]);
                    var afterB = GetRealBounds(chain[i]);
                    float gapZ = afterB.min.z - beforeB.max.z;
                    if (gapZ < 1.5f) continue; // 意図的なギャップギミック以外の通常の継ぎ目

                    float midZ = (beforeB.max.z + afterB.min.z) * 0.5f;
                    float midX = (beforeB.center.x + afterB.center.x) * 0.5f;
                    float midY = (beforeB.max.y + afterB.min.y + 0.3f) * 0.5f;

                    float rockZSpan = gapZ + GapStoneOverlapMargin * 2f;
                    float scale = Mathf.Clamp(rockZSpan / 1.42f, 1.6f, 3.2f); // boulder_01のネイティブ奥行き実測(約1.42m)基準
                    var rockInst = InstantiateWithTop(boulderPrefab, footRoot.transform, midX, midY, midZ, scale, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), getTopLocalYM, "GapSteppingStone_" + i);
                    AddMeshColliders(rockInst);
                    newStones.Add(rockInst);
                    steppingStones++; colliderCount++;
                }
                chain.AddRange(newStones);
                log.AppendLine($"Gap stepping stones added: {steppingStones}.");
            }

            // ---- 最終検証: 実測の継ぎ目に、意図しない隙間(ジャンプ必須ではないのに繋がっていない
            // 箇所)が残っていないか確認する。見つかった場合は削除ではなく警告ログのみ(削除だと
            // 経路が完全に途切れるため)。 ----
            {
                chain.Sort((a, b) => GetRealBounds(a).center.z.CompareTo(GetRealBounds(b).center.z));
                int unexpectedGaps = 0;
                for (int i = 1; i < chain.Count; i++)
                {
                    float gapZ = GetRealBounds(chain[i]).min.z - GetRealBounds(chain[i - 1]).max.z;
                    if (gapZ > 0.4f && gapZ < 1.5f) // stepOffset(0.4)を超えるのに意図的ギャップ(>=1.5)でもない
                    {
                        unexpectedGaps++;
                        log.AppendLine($"WARNING: unexpected {gapZ:F2}m gap between {chain[i - 1].name} and {chain[i].name}");
                    }
                }
                log.AppendLine($"Unexpected gaps after mesh-accurate colliders: {unexpectedGaps}.");
            }

            // ---- 見えない判定の除去: Footholds配下でRendererを持たない(=見た目のない)Colliderが
            // 万一残っていないか確認し、あれば削除する。 ----
            {
                int removed = 0;
                foreach (var col in footRoot.GetComponentsInChildren<Collider>())
                {
                    var mr = col.GetComponent<MeshRenderer>();
                    if (mr == null || !mr.enabled)
                    {
                        Object.DestroyImmediate(col);
                        removed++;
                    }
                }
                log.AppendLine($"Invisible (renderer-less) colliders removed: {removed}.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine($"Foothold course v6 rebuilt: {stations.Count} stations, {colliderCount} mesh-accurate colliders, {visualCount} platform/accent visuals, {tiltedVisuals} tilt-zone visuals, {gapVisuals} gap-edge visuals. bridgeDeckY={bridgeDeckY:F2}. SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    static Vector3 GetLocalSize(GameObject prefab)
    {
        var rends = prefab.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return Vector3.one;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b.size;
    }

    // instance の実際のRenderer.bounds(配置・スケール・回転すべて反映済みのワールドAABB)。
    // 隙間検出・踏み台配置の実測基準として使う(Colliderの種類に依存しない、常に見た目そのもの)。
    static Bounds GetRealBounds(GameObject instance)
    {
        var rends = instance.GetComponentsInChildren<Renderer>();
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    // prefabの素のローカルZサイズがminDepthに満たない場合のみ、Z軸のスケールだけを引き伸ばして
    // 隣接ステーションと実際に重なる奥行きを正直に確保する(横幅・高さは変更しない)。
    static Vector3 EnsureMinZFootprint(GameObject prefab, Vector3 scale, float minDepth)
    {
        var size = GetLocalSize(prefab);
        float curDepth = size.z * scale.z;
        if (curDepth >= minDepth) return scale;
        float neededZScale = minDepth / Mathf.Max(0.05f, size.z);
        return new Vector3(scale.x, scale.y, neededZScale);
    }

    // 指定した「目標の上面ワールド高さ」に実際のメッシュ上面が来るよう配置してインスタンス化する。
    static GameObject InstantiateWithTop(GameObject prefab, Transform parent, float x, float targetTopY, float z, Vector3 scale, Quaternion rot, MethodInfo getTopLocalYM, string name)
    {
        float topLocal = (float)getTopLocalYM.Invoke(null, new object[] { prefab });
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        inst.name = name;
        inst.transform.localScale = scale;
        inst.transform.rotation = rot;
        inst.transform.position = new Vector3(x, targetTopY - topLocal * scale.y, z);
        return inst;
    }
    static GameObject InstantiateWithTop(GameObject prefab, Transform parent, float x, float targetTopY, float z, float uniformScale, Quaternion rot, MethodInfo getTopLocalYM, string name)
        => InstantiateWithTop(prefab, parent, x, targetTopY, z, Vector3.one * uniformScale, rot, getTopLocalYM, name);

    // instance内の、実際にレンダリングされている(有効なMeshRendererを伴う)MeshFilterそれぞれに
    // 直接MeshCollider(非convex)を付与する。判定の位置・スケール・回転はGameObjectのtransformに
    // 完全に追従するため、見た目のメッシュ形状・傾き・凹凸がそのまま判定になる。非表示の
    // LODサブメッシュなど、見えていないものには付与しない。
    static void AddMeshColliders(GameObject instance)
    {
        foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var go = mf.gameObject;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled || !go.activeInHierarchy) continue;

            var mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
        }
    }
}
