using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-16 v3: ユーザー指示により全面刷新。
//  - PathSlab(path_stone.fbx)は「足場として見えにくい」との指摘で廃止し、丸太積み/切り株など
//    実在感のある平たいプラットフォーム系アセットへ置き換え。
//  - 岩・丸太・切り株・根の組み合わせバリエーションを大幅に拡張(単調な繰り返しを避ける)。
//  - ギミックとして「地面が斜めに傾いている区間」を2箇所、「ジャンプしないと渡れない隙間」を
//    2箇所、コース中に設置。
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

    // ---- ギミック区間(コースの絶対Z座標で指定) ----
    static readonly (float z0, float z1)[] TiltZones = { (20f, 26f), (55f, 61f) };
    // ステーションはZStart(奇数値9)から2m刻みの奇数値(9,11,...,109)にしか生成されないため、
    // 意図通り2駅ぶん連続でスキップさせるには、その2駅の座標をちょうど挟む範囲を指定する必要が
    // ある(実測: 1駅だけスキップされると隙間は0.8mしかなくジャンプ不要になってしまっていた)。
    // 2駅連続スキップでできる実際の隙間は約2.8m(同高度ジャンプ最大3.0m以内、要ジャンプ)。
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
            var coastRocks = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "coast_rocks_01/coast_rocks_01_2k.fbx");
            var mossRocks = (GameObject[])loadMossRocksM.Invoke(null, null);

            var kenneyLogStack = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "log_stackLarge.fbx");
            var kenneyStumpSquare = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "stump_squareDetailedWide.fbx");
            var kenneyStumpRound = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "stump_roundDetailed.fbx");

            // Kenney木材アセットは自動マテリアルリンクが白く抜ける既知の問題があるため、
            // BuildStairs/path_stoneと同じ手法で共通のwoodBark.matを強制適用する。
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

            var rng = new System.Random(7072);

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
                            tiltDeg = 16f * edgeT; // 端でなめらかにゼロへ戻す
                        }
                    }
                }
                // 傾斜区間・隙間区間は、ふらつき防止のため通常より幅を少し広めに保証する
                // (ただし安全マージンの上限は超えない)。
                if (kind == StationKind.Tilted) halfWidth = Mathf.Min(Mathf.Max(halfWidth, WideWidth * 0.5f), maxHalfWidth);

                stations.Add((z, centerX, centerY, halfWidth, kind, tiltDeg));
            }

            // ---- 1. 歩行用Collider(隙間区間は意図的にColliderを置かない) ----
            var colliderRoot = new GameObject("WalkCollider");
            colliderRoot.transform.SetParent(footRoot.transform, false);
            int colliderCount = 0;
            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                if (s.kind == StationKind.Gap) continue; // ここは意図的に渡れない(ジャンプ必須)

                var segGo = new GameObject("WalkSeg_" + i);
                segGo.transform.SetParent(colliderRoot.transform, false);
                segGo.transform.position = new Vector3(s.x, s.y - 0.3f, s.z);
                if (s.kind == StationKind.Tilted)
                    segGo.transform.rotation = Quaternion.Euler(0f, 0f, s.tiltDeg); // 横方向(進行方向に直交する軸)に傾ける
                var box = segGo.AddComponent<BoxCollider>();
                box.size = new Vector3(s.halfWidth * 2f, 0.6f, StationSpacing * 1.6f);
                colliderCount++;
            }

            // ---- 2. 視覚的な岩・丸太・切り株・根で覆う(バリエーション豊富に) ----
            // プラットフォーム系(広め区間の主役、path_stoneの代替): 種類をローテーション。
            var platformChoices = new List<GameObject>();
            if (kenneyLogStack != null) platformChoices.Add(kenneyLogStack);
            if (kenneyStumpSquare != null) platformChoices.Add(kenneyStumpSquare);
            if (kenneyStumpRound != null) platformChoices.Add(kenneyStumpRound);
            if (rootCluster2 != null) platformChoices.Add(rootCluster2);

            // 中型アクセント系(細め区間・境界埋め用)。
            var accentChoices = new List<GameObject>();
            if (stumpPrefab1 != null) accentChoices.Add(stumpPrefab1);
            if (stumpPrefab2 != null) accentChoices.Add(stumpPrefab2);
            if (rootCluster1 != null) accentChoices.Add(rootCluster1);
            if (pineRoots != null) accentChoices.Add(pineRoots);
            if (mossRocks != null) accentChoices.AddRange(mossRocks);

            var logChoices = new List<GameObject>();
            if (logPrefab != null) logChoices.Add(logPrefab);
            if (logPrefab2 != null) logChoices.Add(logPrefab2);

            int visualCount = 0, tiltedVisuals = 0, gapVisuals = 0;
            GameObject lastUsed = null; // 直前と同じ種類の連続を避ける
            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                bool wide = s.halfWidth * 2f >= (WideWidth + NarrowWidth) * 0.5f;

                if (s.kind == StationKind.Gap)
                {
                    // 隙間の縁: 両端に大岩を置いて「ここで途切れている」ことを視覚的に明確にする。
                    if (i == 0 || stations[i - 1].kind != StationKind.Gap)
                    {
                        var edge = PlaceFlatTop(boulderPrefab, footRoot.transform, s.x, s.y, s.z, 1.5f + (float)rng.NextDouble() * 0.4f, 0.930f, getTopLocalYM, rng, "GapEdge_" + i);
                        gapVisuals++;
                    }
                    continue;
                }

                GameObject chosen;
                float topLocal;
                float scale;
                bool isLog = false;
                Vector3 logDir = Vector3.forward;

                if (s.kind == StationKind.Tilted)
                {
                    // 傾斜区間: 幅広い根の塊/岩を、Colliderと同じ角度だけ傾けて置く。
                    chosen = (rootCluster2 != null && rng.Next(2) == 0) ? rootCluster2 : boulderPrefab;
                    topLocal = (float)getTopLocalYM.Invoke(null, new object[] { chosen });
                    scale = chosen == boulderPrefab ? (2.0f + (float)rng.NextDouble() * 0.6f) : Mathf.Max(1f, s.halfWidth * 2f / 2.38f);
                    var tInst = (GameObject)PrefabUtility.InstantiatePrefab(chosen, footRoot.transform);
                    tInst.name = "TiltPlatform_" + i;
                    tInst.transform.localScale = chosen == boulderPrefab ? Vector3.one * scale : new Vector3(scale, 1.2f, StationSpacing * 1.5f / 2.71f * 1.2f);
                    tInst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 40f - 20f, s.tiltDeg);
                    tInst.transform.position = new Vector3(s.x, s.y - topLocal * scale * (chosen == boulderPrefab ? 1f : 1f) + 0.02f, s.z);
                    tiltedVisuals++;
                    continue;
                }

                if (wide)
                {
                    // 広め区間: プラットフォーム系をローテーション(直前と別の種類を選ぶ)。
                    var candidates = new List<GameObject>(platformChoices);
                    if (lastUsed != null) candidates.RemoveAll(c => c == lastUsed);
                    if (candidates.Count == 0) candidates = platformChoices;
                    chosen = candidates.Count > 0 ? candidates[rng.Next(candidates.Count)] : boulderPrefab;
                    lastUsed = chosen;

                    bool isKenneyPlatform = chosen == kenneyLogStack || chosen == kenneyStumpSquare || chosen == kenneyStumpRound;
                    topLocal = (float)getTopLocalYM.Invoke(null, new object[] { chosen });

                    if (isKenneyPlatform)
                    {
                        var b0 = GetLocalSize(chosen);
                        float scaleX = Mathf.Max(1f, (s.halfWidth * 2f) / Mathf.Max(0.2f, b0.x));
                        float scaleZ = Mathf.Max(1f, (StationSpacing * 1.5f) / Mathf.Max(0.2f, b0.z));
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(chosen, footRoot.transform);
                        inst.name = "PlatformKenney_" + i;
                        inst.transform.localScale = new Vector3(scaleX, 1.1f + (float)rng.NextDouble() * 0.3f, scaleZ);
                        inst.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 3f - 1.5f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 3f - 1.5f);
                        inst.transform.position = new Vector3(s.x, s.y - topLocal * inst.transform.localScale.y, s.z);
                        if (woodMat != null)
                            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
                            {
                                var mats = new Material[mr.sharedMaterials.Length];
                                for (int mi = 0; mi < mats.Length; mi++) mats[mi] = woodMat;
                                mr.sharedMaterials = mats;
                            }
                        visualCount++;
                    }
                    else // root_cluster_02 (PolyHaven, 自前のマテリアルで正常表示)
                    {
                        var b0 = GetLocalSize(chosen);
                        float scaleX = Mathf.Max(1f, (s.halfWidth * 2f) / Mathf.Max(0.5f, b0.x));
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(chosen, footRoot.transform);
                        inst.name = "PlatformRoot_" + i;
                        inst.transform.localScale = Vector3.one * scaleX;
                        inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                        inst.transform.position = new Vector3(s.x, s.y - topLocal * scaleX + 0.03f, s.z);
                        visualCount++;
                    }

                    // 広め区間には縁にアクセントの岩/切り株/根を添えて単調さを崩す。
                    if (rng.Next(3) != 0 && accentChoices.Count > 0)
                    {
                        var acc = accentChoices[rng.Next(accentChoices.Count)];
                        float aScale = 0.5f + (float)rng.NextDouble() * 0.5f;
                        float aTop = (float)getTopLocalYM.Invoke(null, new object[] { acc });
                        var ainst = (GameObject)PrefabUtility.InstantiatePrefab(acc, footRoot.transform);
                        ainst.name = "WideAccent_" + i;
                        float side = rng.Next(2) == 0 ? 1f : -1f;
                        float ex = s.x + side * Mathf.Max(0.3f, s.halfWidth - 0.4f);
                        ainst.transform.localScale = Vector3.one * aScale;
                        ainst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                        ainst.transform.position = new Vector3(ex, s.y - aTop * aScale + 0.05f, s.z);
                        visualCount++;
                    }
                }
                else
                {
                    // 細め区間: 丸太(2種類をローテーション)と、根/切り株/苔岩をランダムに交互配置。
                    int pick = rng.Next(3);
                    if (pick == 0 && logChoices.Count > 0)
                    {
                        isLog = true;
                        chosen = logChoices[rng.Next(logChoices.Count)];
                        var nextIdx = Mathf.Min(i + 1, stations.Count - 1);
                        var s2 = stations[nextIdx];
                        logDir = new Vector3(s2.x - s.x, 0f, s2.z - s.z);
                        if (logDir.sqrMagnitude < 0.01f) logDir = Vector3.forward;
                        float span = Mathf.Max(StationSpacing * 1.6f, logDir.magnitude + StationSpacing * 1.3f);
                        var b0 = GetLocalSize(chosen);
                        float logScale = Mathf.Clamp(span / Mathf.Max(0.5f, b0.x), 1.0f, 5.0f);
                        topLocal = (float)getTopLocalYM.Invoke(null, new object[] { chosen });
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(chosen, footRoot.transform);
                        inst.name = "PathLog_" + i;
                        inst.transform.rotation = Quaternion.LookRotation(logDir.normalized) * Quaternion.Euler(0f, 90f, 0f);
                        inst.transform.localScale = Vector3.one * logScale;
                        inst.transform.position = new Vector3(s.x, s.y - topLocal * logScale + 0.05f, s.z);
                        visualCount++;
                    }
                    else if (pick == 1 && accentChoices.Count > 0)
                    {
                        chosen = accentChoices[rng.Next(accentChoices.Count)];
                        float scale2 = 0.8f + (float)rng.NextDouble() * 0.6f;
                        topLocal = (float)getTopLocalYM.Invoke(null, new object[] { chosen });
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(chosen, footRoot.transform);
                        inst.name = "PathAccent_" + i;
                        inst.transform.localScale = Vector3.one * scale2;
                        inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                        inst.transform.position = new Vector3(s.x, s.y - topLocal * scale2 + 0.04f, s.z);
                        visualCount++;
                    }
                    else if (mossRocks != null && mossRocks.Length > 0)
                    {
                        chosen = mossRocks[rng.Next(mossRocks.Length)];
                        float scale3 = 0.8f + (float)rng.NextDouble() * 0.5f;
                        topLocal = (float)getTopLocalYM.Invoke(null, new object[] { chosen });
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(chosen, footRoot.transform);
                        inst.name = "PathRock_" + i;
                        inst.transform.localScale = Vector3.one * scale3;
                        inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                        inst.transform.position = new Vector3(s.x, s.y - topLocal * scale3 + 0.04f, s.z);
                        visualCount++;
                    }
                }

                if (wide && i % 5 == 0)
                {
                    float bScale = 1.5f + (float)rng.NextDouble() * 0.7f;
                    float bTopLocal = (float)getTopLocalYM.Invoke(null, new object[] { boulderPrefab });
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(boulderPrefab, footRoot.transform);
                    inst.name = "PathBoulder_" + i;
                    inst.transform.localScale = Vector3.one * bScale;
                    inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    inst.transform.position = new Vector3(s.x, s.y - bTopLocal * bScale + 0.08f, s.z);
                    visualCount++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine($"Foothold course v3 rebuilt: {stations.Count} stations, {colliderCount} walk-collider segments, {visualCount} platform/accent visuals, {tiltedVisuals} tilt-zone visuals, {gapVisuals} gap-edge visuals. bridgeDeckY={bridgeDeckY:F2}. SUCCESS");
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

    static GameObject PlaceFlatTop(GameObject prefab, Transform parent, float x, float y, float z, float scale, float topLocal, MethodInfo getTopLocalYM, System.Random rng, string name)
    {
        float actualTopLocal = (float)getTopLocalYM.Invoke(null, new object[] { prefab });
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        inst.name = name;
        inst.transform.localScale = Vector3.one * scale;
        inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        inst.transform.position = new Vector3(x, y - actualTopLocal * scale + 0.08f, z);
        return inst;
    }
}
