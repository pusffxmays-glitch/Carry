using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-16: CarryFixBridgeApproachBumps.cs のpass2(橋台の角のギャップ埋め)が、川の中心付近
// (川岸ではなく水面下のチャンネル本体)まで誤って盛り土してしまっていたことが発覚(西橋台
// 付近、距離2.5mマージンの対角線がRiverHalfWidth内側まで届いていた)。この一回限りの復旧
// スクリプトで、影響範囲の各セルを「本来あるべき正しい高さ」(RawHeightAt + このセッションで
// 行ったCarryCarveRiverBanksの倍幅・急傾斜デルタ)へ絶対値で復元してから、pass2を川のチャン
// ネル内を除外する条件付きでやり直す。
public static class CarryRepairBridgeApproachRiverDamage
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float LakeFactorEpsilon = 0.0005f;
    // CarryCarveRiverBanks.cs と揃える
    const float ZRampStart = 0f, ZFullStrength = 9f, SteepBankSpan = 1.4f, WidthMultiplier = 2f;

    [MenuItem("Carry/Repair Bridge Approach River Damage (one-off)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();
            var data = terrain.terrainData;

            var t = typeof(CarryBuildTerrainForest);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var rawHeightAtM = t.GetMethod("RawHeightAt", flags);
            var riverXM = t.GetMethod("RiverX", flags);
            var riverHalfWidthM = t.GetMethod("RiverHalfWidth", flags);
            var channelFactorM = t.GetMethod("ChannelFactor", flags);
            var lakeFactorM = t.GetMethod("LakeFactor", flags);
            var riverDepthF = t.GetField("RiverDepth", flags);
            float riverDepth = (float)riverDepthF.GetValue(null);
            var moundM = t.GetMethod("BridgeApproachMoundHeight", flags);

            float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
            float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
            int hr = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, hr, hr);

            // ---- ステップ1: 破損した可能性のある範囲(両橋台 + マージン)を、正しい式で絶対値
            // 復元する(RawHeightAt = 素の地形。そこへCarryCarveRiverBanksと同じ倍幅・急傾斜
            // デルタを再適用)。 ----
            int restored = 0;
            foreach (var abutName in new[] { "AbutmentCollider_East", "AbutmentCollider_West" })
            {
                var abutGo = GameObject.Find("ForestStage_Terrain/StoneBridge_Meshy/" + abutName);
                if (abutGo == null) continue;
                var box = abutGo.GetComponent<BoxCollider>();
                Bounds b = box.bounds;
                const float margin = 3f;
                float axMin = b.min.x - margin, axMax = b.max.x + margin;
                float azMin = b.min.z - margin, azMax = b.max.z + margin;
                int axi0 = Mathf.Max(0, Mathf.FloorToInt((axMin - originX) / sizeX * (hr - 1)));
                int axi1 = Mathf.Min(hr - 1, Mathf.CeilToInt((axMax - originX) / sizeX * (hr - 1)));
                int azi0 = Mathf.Max(0, Mathf.FloorToInt((azMin - originZ) / sizeZ * (hr - 1)));
                int azi1 = Mathf.Min(hr - 1, Mathf.CeilToInt((azMax - originZ) / sizeZ * (hr - 1)));

                for (int zi = azi0; zi <= azi1; zi++)
                {
                    float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
                    for (int xi = axi0; xi <= axi1; xi++)
                    {
                        float worldX = originX + (xi / (float)(hr - 1)) * sizeX;

                        // 素の地形(このセッションのriver-widening/steepeningより前の基準値)。
                        float baseH = (float)rawHeightAtM.Invoke(null, new object[] { worldX, worldZ });

                        // CarryCarveRiverBanksと同じデルタを再適用(湖ガード込み)。
                        float lf = (float)lakeFactorM.Invoke(null, new object[] { worldX, worldZ });
                        float correctH = baseH;
                        if (lf <= LakeFactorEpsilon && worldZ >= ZRampStart)
                        {
                            float rx = (float)riverXM.Invoke(null, new object[] { worldZ });
                            float hw = (float)riverHalfWidthM.Invoke(null, new object[] { worldZ });
                            if (hw > 0.01f)
                            {
                                float d = Mathf.Abs(worldX - rx);
                                float oldFactor = (float)channelFactorM.Invoke(null, new object[] { d, hw });
                                float hw2 = hw * WidthMultiplier;
                                float inner = hw2 * 0.5f;
                                float outerNew = inner + SteepBankSpan;
                                float tNew = Mathf.InverseLerp(inner, outerNew, d);
                                float newFactorFull = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tNew));
                                float rampT = Mathf.Clamp01(Mathf.InverseLerp(ZRampStart, ZFullStrength, worldZ));
                                float newFactor = Mathf.Lerp(oldFactor, newFactorFull, rampT);
                                float delta = riverDepth * (newFactor - oldFactor);
                                correctH = baseH - delta;
                            }
                        }

                        heights[zi, xi] = Mathf.Clamp01((correctH - originY) / sizeY);
                        restored++;
                    }
                }
            }

            // ---- ステップ2: 橋台の角のギャップ埋め(pass2)を、川のチャンネル本体を除外する
            // 条件付きでやり直す。 ----
            int touched2 = 0; float maxRaise2 = 0f, maxLower2 = 0f;
            foreach (var abutName in new[] { "AbutmentCollider_East", "AbutmentCollider_West" })
            {
                var abutGo = GameObject.Find("ForestStage_Terrain/StoneBridge_Meshy/" + abutName);
                if (abutGo == null) continue;
                var box = abutGo.GetComponent<BoxCollider>();
                Bounds b = box.bounds;
                float targetY = b.max.y;
                const float outerMargin = 2.5f;
                float axMin = b.min.x - outerMargin, axMax = b.max.x + outerMargin;
                float azMin = b.min.z - outerMargin, azMax = b.max.z + outerMargin;
                int axi0 = Mathf.Max(0, Mathf.FloorToInt((axMin - originX) / sizeX * (hr - 1)));
                int axi1 = Mathf.Min(hr - 1, Mathf.CeilToInt((axMax - originX) / sizeX * (hr - 1)));
                int azi0 = Mathf.Max(0, Mathf.FloorToInt((azMin - originZ) / sizeZ * (hr - 1)));
                int azi1 = Mathf.Min(hr - 1, Mathf.CeilToInt((azMax - originZ) / sizeZ * (hr - 1)));

                for (int zi = azi0; zi <= azi1; zi++)
                {
                    float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
                    for (int xi = axi0; xi <= axi1; xi++)
                    {
                        float worldX = originX + (xi / (float)(hr - 1)) * sizeX;

                        float lf = (float)lakeFactorM.Invoke(null, new object[] { worldX, worldZ });
                        if (lf > LakeFactorEpsilon) continue; // 湖は絶対に触らない

                        float dx = Mathf.Max(0f, Mathf.Max(b.min.x - worldX, worldX - b.max.x));
                        float dz = Mathf.Max(0f, Mathf.Max(b.min.z - worldZ, worldZ - b.max.z));
                        float dist = Mathf.Sqrt(dx * dx + dz * dz);
                        if (dist > outerMargin) continue;
                        float weight = dist <= 0f ? 1f : 0.5f * (1f + Mathf.Cos(dist / outerMargin * Mathf.PI));

                        float originalWorldY = originY + heights[zi, xi] * sizeY;
                        float blended = Mathf.Lerp(originalWorldY, targetY, weight);
                        float delta = blended - originalWorldY;
                        // 距離ベースの川除外ではなく、変化量そのものに上限を設ける方が頑健:
                        // 橋台に隣接する「本物の乾いた地面の小さな出っ張り/凹み」は普通せいぜい
                        // 1m程度しかずれないが、前回のバグ(川のチャンネル本体を誤って埋めた)は
                        // 単一セルで2.9m以上の変化を要求していた -- これを直接足切りする。
                        const float maxAllowedChange = 1.2f;
                        if (Mathf.Abs(delta) > maxAllowedChange) continue;
                        if (Mathf.Abs(delta) < 0.01f) continue;
                        if (delta > 0f) maxRaise2 = Mathf.Max(maxRaise2, delta); else maxLower2 = Mathf.Max(maxLower2, -delta);
                        heights[zi, xi] = Mathf.Clamp01((blended - originY) / sizeY);
                        touched2++;
                    }
                }
            }

            data.SetHeights(0, 0, heights);
            Physics.SyncTransforms();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine($"Repair complete: restored {restored} cells to correct baseline, pass2(corners, river-safe) touched {touched2} (maxRaise={maxRaise2:F2}m maxLower={maxLower2:F2}m). SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
