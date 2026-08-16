using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-16: ユーザー報告「陸から橋に乗るときに引っかかる」を実測調査。橋の両側の取り付け部分
// (AbutmentCollider_East/West周辺)で、地形の高さを橋の縁(abutmentTopY)と比較したところ、
// 場所によっては最大1.1m地形が橋より高く盛り上がっていた(逆に最大2m低い箇所もあった)。
//
// 原因: CarryBuildTerrainForest.RawHeightAt は BridgeApproachMoundHeight(自然な地形ノイズを
// 橋のデッキ実測カーブへブレンドする、正しい滑らかな取り付け坂を計算する関数)の結果を
// Mathf.Max(既存の高さ, moundY) でしか適用していない(「盛るだけで削らない」方針)ため、
// 地形の高周波ノイズがデッキ高さを上回る箇所ではその盛り上がりがそのまま残ってしまい、
// プレイヤーがそこに引っかかる(段差 or 出っ張りに衝突する)。既存の
// CarryBuildLakeRampPath.FixBridgeTerrainSeam も同じMax方向(上げるだけ)の修正のため、
// この「盛り上がり」には効かない。
//
// 修正: BridgeApproachMoundHeight自体は取り付け部分だけに限定されたゾーン関数(ゾーン外は
// -999を返す)なので、そのゾーン内では既存の高さを問わず、計算されたブレンド値へ直接
// 置き換える(上げるのも下げるのも両方行う)。ゾーンの境界(-999になる場所)には一切触れない
// ため、周囲の自然な地形へは影響しない。
public static class CarryFixBridgeApproachBumps
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    [MenuItem("Carry/Fix Bridge Approach Bumps (Terrain, non-destructive)")]
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
            var moundM = t.GetMethod("BridgeApproachMoundHeight", flags);
            var riverXM = t.GetMethod("RiverX", flags);
            var lakeFactorM = t.GetMethod("LakeFactor", flags);
            var bridgeCenterZF = t.GetField("BridgeCenterZ", flags);
            var halfSpanF = t.GetField("MeshyBridgeWorldHalfSpan", flags);
            var halfDepthF = t.GetField("MeshyBridgeWorldHalfDepth", flags);

            float bridgeCenterZ = (float)bridgeCenterZF.GetValue(null);
            float halfSpan = (float)halfSpanF.GetValue(null);
            float halfDepth = (float)halfDepthF.GetValue(null);
            float riverCenterX = (float)riverXM.Invoke(null, new object[] { bridgeCenterZ });

            // ゾーンを覆うのに十分な走査範囲(内部でBridgeApproachMoundHeightが-999を返す場所は
            // 自動的にスキップされるので、多少広めに取っても安全)。
            float scanXMin = riverCenterX - halfSpan - 6f, scanXMax = riverCenterX + halfSpan + 6f;
            float scanZMin = bridgeCenterZ - halfDepth - 1f, scanZMax = bridgeCenterZ + halfDepth + 1f;

            float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
            float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
            int hr = data.heightmapResolution;

            int minXi = Mathf.Max(0, Mathf.FloorToInt((scanXMin - originX) / sizeX * (hr - 1)));
            int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((scanXMax - originX) / sizeX * (hr - 1)));
            int minZi = Mathf.Max(0, Mathf.FloorToInt((scanZMin - originZ) / sizeZ * (hr - 1)));
            int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((scanZMax - originZ) / sizeZ * (hr - 1)));

            var heights = data.GetHeights(0, 0, hr, hr);
            int touched = 0; float maxRaise = 0f, maxLower = 0f;
            for (int zi = minZi; zi <= maxZi; zi++)
            {
                float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
                for (int xi = minXi; xi <= maxXi; xi++)
                {
                    float worldX = originX + (xi / (float)(hr - 1)) * sizeX;
                    float moundY = (float)moundM.Invoke(null, new object[] { worldX, worldZ });
                    if (moundY <= -998f) continue; // ゾーン外(この関数の設計上の判定をそのまま尊重)

                    float originalWorldY = originY + heights[zi, xi] * sizeY;
                    float delta = moundY - originalWorldY;
                    if (Mathf.Abs(delta) < 0.01f) continue;
                    if (delta > 0f) maxRaise = Mathf.Max(maxRaise, delta); else maxLower = Mathf.Max(maxLower, -delta);
                    heights[zi, xi] = Mathf.Clamp01((moundY - originY) / sizeY);
                    touched++;
                }
            }

            // ---- 2周目: BridgeApproachMoundHeight自体のzLimit(橋の実際のZ範囲よりわずかに狭い)
            // の外側、しかし橋台(Abutment)自身のZ範囲内にあたる「角」の部分を補う。
            // 例: 東側z=2.0付近はzLimit外だが橋台の footprint 内 -- ここも段差が残っていた。
            // 湖の影響がある座標(LakeFactor>0)は絶対に触らない(東側z=2付近は湖の造形の一部)。
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
                        if (lf > 0.0005f) continue; // 湖の影響がある地点は一切触らない

                        float dx = Mathf.Max(0f, Mathf.Max(b.min.x - worldX, worldX - b.max.x));
                        float dz = Mathf.Max(0f, Mathf.Max(b.min.z - worldZ, worldZ - b.max.z));
                        float dist = Mathf.Sqrt(dx * dx + dz * dz);
                        if (dist > outerMargin) continue;
                        float weight = dist <= 0f ? 1f : 0.5f * (1f + Mathf.Cos(dist / outerMargin * Mathf.PI));

                        float originalWorldY = originY + heights[zi, xi] * sizeY;
                        float blended = Mathf.Lerp(originalWorldY, targetY, weight);
                        float delta = blended - originalWorldY;
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

            log.AppendLine($"Bridge approach bumps fixed: pass1 touched {touched} cells (maxRaise={maxRaise:F2}m, maxLower={maxLower:F2}m), pass2(corners) touched {touched2} cells (maxRaise={maxRaise2:F2}m, maxLower={maxLower2:F2}m). SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
