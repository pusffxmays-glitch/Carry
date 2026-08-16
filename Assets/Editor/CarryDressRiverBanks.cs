using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-16: CarryCarveRiverBanks.cs で地形そのものを急な崖状+倍幅に彫った(石橋の少し先〜
// 奥の森、川の左右両岸)のに続けて、見た目を整える装飾パス。CLAUDE.md「Rock / Cliff / Boulder /
// Tree の接地ルール」に従い、既存の PlaceBoulderEmbedded / TryGetTerrainSurface
// (CarryBuildTerrainForest.cs内、実際のTerrain表面をレイキャストして法線に沿わせる)をリフレク
// ション経由で再利用する。
//
// v3 修正: ユーザーから「RiverBankWall(coastal_cliff_01の大型岩壁)は違和感がある」との指摘、
// および「川幅を2倍にする」指示を受け、(1) 大型岩壁の配置を完全に削除(中型の苔岩+根+シダの
// みで境界を作る)、(2) 各アンカーXをCarryCarveRiverBanksと同じ倍幅チャンネル基準
// (hw2*0.5 = 旧hwとちょうど同じ値)に合わせて再計算した。
public static class CarryDressRiverBanks
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const string PH = "Assets/ExternalAssets/PolyHaven/";

    const float ZStart = 3f;
    const float ZEnd = 118f;
    const float LakeFactorEpsilon = 0.0005f;
    const float WidthMultiplier = 2.0f; // CarryCarveRiverBanks.WidthMultiplier と揃える

    [MenuItem("Carry/Dress River Banks (Boulders + Roots)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainGo = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGo != null ? terrainGo.GetComponent<Terrain>() : Terrain.activeTerrain;
            var root = GameObject.Find("ForestStage_Terrain");
            if (terrain == null || root == null) { Debug.LogError("Terrain/root not found."); return; }

            var existing = root.transform.Find("RiverBankDressing");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            var bankRoot = new GameObject("RiverBankDressing");
            bankRoot.transform.SetParent(root.transform, false);

            var t = typeof(CarryBuildTerrainForest);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var riverXM = t.GetMethod("RiverX", flags);
            var riverHalfWidthM = t.GetMethod("RiverHalfWidth", flags);
            var lakeFactorM = t.GetMethod("LakeFactor", flags);
            var placeBoulderM = t.GetMethod("PlaceBoulderEmbedded", flags);
            var tryGetSurfaceM = t.GetMethod("TryGetTerrainSurface", flags);
            var loadMossRocksM = t.GetMethod("LoadIndividualMossRocks", flags);
            var getTopLocalYM = t.GetMethod("GetPrefabTopLocalY", flags);
            var addSolidColliderM = t.GetMethod("AddSolidCollider", flags);

            var rootsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
            var fernPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "fern_02/fern_02_2k.fbx");
            var mossRocks = (GameObject[])loadMossRocksM.Invoke(null, null);

            if (rootsPrefab == null || mossRocks == null || mossRocks.Length == 0)
            {
                log.AppendLine("FAILED: required prefab(s) not found.");
                Debug.Log(log.ToString());
                return;
            }
            float rootsTopLocal = (float)getTopLocalYM.Invoke(null, new object[] { rootsPrefab });
            float fernTopLocal = fernPrefab != null ? (float)getTopLocalYM.Invoke(null, new object[] { fernPrefab }) : 0f;

            var rng = new System.Random(9031);
            float[] sides = { 1f, -1f };
            int placedBoulders = 0, placedRoots = 0, placedFerns = 0, skippedLake = 0;

            foreach (float side in sides)
            {
                // ---- 中型の苔岩: 崖の縁に沿って境界を自然にする。約6-9mおき。 ----
                float z = ZStart + (side > 0 ? 0f : 3f);
                int wi = 0;
                while (z < ZEnd)
                {
                    float rx = (float)riverXM.Invoke(null, new object[] { z });
                    float hw2 = (float)riverHalfWidthM.Invoke(null, new object[] { z }) * WidthMultiplier;
                    float anchorX = rx + side * (hw2 * 0.5f + 0.5f + (float)rng.NextDouble() * 0.8f);

                    float lf = (float)lakeFactorM.Invoke(null, new object[] { anchorX, z });
                    if (lf <= LakeFactorEpsilon)
                    {
                        var mossPrefab = mossRocks[rng.Next(mossRocks.Length)];
                        float bScale = 0.9f + (float)rng.NextDouble() * 0.7f;
                        var binst = (GameObject)placeBoulderM.Invoke(null, new object[] { mossPrefab, bankRoot.transform, terrain, anchorX, z, bScale, 0.4f, rng, $"RiverBankBoulder_{(side > 0 ? "R" : "L")}_{wi}" });
                        if (binst != null)
                        {
                            float topLocal = (float)getTopLocalYM.Invoke(null, new object[] { mossPrefab });
                            addSolidColliderM.Invoke(null, new object[] { binst, topLocal * bScale });
                            placedBoulders++;
                        }
                    }
                    else skippedLake++;

                    z += 6f + (float)rng.NextDouble() * 3f;
                    wi++;
                }

                // ---- 露出した木の根 + シダ: BuildStairsのStairsDressingと同じ「地面に直に置く」
                // パターン(法線チルトなし、平たいメッシュ向け)。疎に配置。 ----
                float zr = ZStart + 5f + (side > 0 ? 2f : 6f);
                int ri2 = 0;
                while (zr < ZEnd)
                {
                    float rx = (float)riverXM.Invoke(null, new object[] { zr });
                    float hw2 = (float)riverHalfWidthM.Invoke(null, new object[] { zr }) * WidthMultiplier;
                    float rootX = rx + side * (hw2 * 0.5f + 0.5f);
                    float lf = (float)lakeFactorM.Invoke(null, new object[] { rootX, zr });
                    if (lf <= LakeFactorEpsilon)
                    {
                        object[] surfaceArgs = { terrain, rootX, zr, null, null };
                        tryGetSurfaceM.Invoke(null, surfaceArgs);
                        Vector3 hitPoint = (Vector3)surfaceArgs[3];

                        float scale = 1.4f + (float)rng.NextDouble() * 0.8f;
                        var rinst = (GameObject)PrefabUtility.InstantiatePrefab(rootsPrefab, bankRoot.transform);
                        rinst.name = $"RiverBankRoot_{(side > 0 ? "R" : "L")}_{ri2}";
                        rinst.transform.localScale = Vector3.one * scale;
                        rinst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                        float topY = hitPoint.y + 0.25f;
                        rinst.transform.position = new Vector3(rootX, topY - rootsTopLocal * scale, zr);
                        placedRoots++;

                        if (fernPrefab != null && rng.Next(2) == 0)
                        {
                            float fx = rootX + side * (0.8f + (float)rng.NextDouble() * 1.2f);
                            float fz = zr + 0.6f;
                            float lfF = (float)lakeFactorM.Invoke(null, new object[] { fx, fz });
                            if (lfF <= LakeFactorEpsilon)
                            {
                                object[] fSurfaceArgs = { terrain, fx, fz, null, null };
                                tryGetSurfaceM.Invoke(null, fSurfaceArgs);
                                Vector3 fHit = (Vector3)fSurfaceArgs[3];
                                float fScale = 0.6f + (float)rng.NextDouble() * 0.5f;
                                var finst = (GameObject)PrefabUtility.InstantiatePrefab(fernPrefab, bankRoot.transform);
                                finst.name = $"RiverBankFern_{(side > 0 ? "R" : "L")}_{ri2}";
                                finst.transform.localScale = Vector3.one * fScale;
                                finst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                                float fTopY = fHit.y + 0.1f;
                                finst.transform.position = new Vector3(fx, fTopY - fernTopLocal * fScale, fz);
                                placedFerns++;
                            }
                        }
                    }
                    zr += 11f + (float)rng.NextDouble() * 4f;
                    ri2++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine($"River bank dressing: boulders={placedBoulders} roots={placedRoots} ferns={placedFerns} skippedLake={skippedLake}. SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
