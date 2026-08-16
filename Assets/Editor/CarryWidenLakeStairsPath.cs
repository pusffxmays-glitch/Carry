using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-16: 湖東岸の「階段(LakeStairs)を上がってから橋に着くまでの通路」が狭い、との報告。
// 実測(Terrain.GetSteepness + 高さサンプリングの列スキャン)で、階段の踊り場から橋の東岸まで
// のあいだ、湖岸(水際)と急斜面(崖の立ち上がり)がほぼ同じX座標にある区間(z=-4..+1あたりで
// 幅0〜1.2m)を確認した。
//
// 最初にZスライスごとに「湖岸X」「崖立ち上がりX」を個別検出して押し出す実装を試したが、
// 局所ノイズ(小さな岩・入江)に弱く、結果が場所によってまだら(数mの幅とほぼ0mが交互)に
// なった。代わりに、階段の踊り場(CarryBuildTerrainForest.landPt相当)と橋の東岸を結ぶ
// 直線に沿って、一定幅の帯を最初から最後まで均一に均すシンプルな「カプセル状カービング」に
// 変更した -- 局所形状に依存しないぶん頑健。
public static class CarryWidenLakeStairsPath
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    // CarryBuildTerrainForest から reflection 経由で実測した値(2026-08-16時点):
    //   StairsAngleDeg=55 の shorePt=(14.57,-5.80), landPt=(21.94,-0.64)
    // ここでは landPt をそのまま起点(階段の踊り場)に使う。
    static readonly Vector2 PathStart = new Vector2(21.94f, -0.64f);
    // 終点: 橋東岸のたもと。BridgeEmbankment_1 の実測位置(5.95,*,6.44)付近に合わせる
    // (川の中心 riverX(BridgeCenterZ)=-3.16 から東へ8m=デッキ端、その少し先)。
    static readonly Vector2 PathEnd = new Vector2(6f, 5f);

    const float CorridorHalfWidth = 3.0f;   // 中心線から片側3m = 全幅6m
    const float FalloffMargin = 2.0f;       // 帯の外側でなだらかに元の地形へ収束させる幅

    [MenuItem("Carry/Widen Lake Stairs-to-Bridge Path")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = Terrain.activeTerrain;
            if (terrain == null) { Debug.LogError("Terrain.activeTerrain が見つかりません。"); return; }

            var data = terrain.terrainData;
            float originX = terrain.transform.position.x, originY = terrain.transform.position.y, originZ = terrain.transform.position.z;
            float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
            int hr = data.heightmapResolution;

            float startY = SampleWorldHeight(terrain, PathStart.x, PathStart.y);
            float endY = SampleWorldHeight(terrain, PathEnd.x, PathEnd.y);

            Vector2 seg = PathEnd - PathStart;
            float segLenSq = Mathf.Max(1e-6f, seg.sqrMagnitude);
            float maxReach = CorridorHalfWidth + FalloffMargin;

            var heights = data.GetHeights(0, 0, hr, hr);

            // 影響を受けうる矩形範囲だけ走査する(全面走査は不要)。
            float minX = Mathf.Min(PathStart.x, PathEnd.x) - maxReach;
            float maxX = Mathf.Max(PathStart.x, PathEnd.x) + maxReach;
            float minZ = Mathf.Min(PathStart.y, PathEnd.y) - maxReach;
            float maxZ = Mathf.Max(PathStart.y, PathEnd.y) + maxReach;
            int minXi = Mathf.Max(0, Mathf.FloorToInt((minX - originX) / sizeX * (hr - 1)));
            int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxX - originX) / sizeX * (hr - 1)));
            int minZi = Mathf.Max(0, Mathf.FloorToInt((minZ - originZ) / sizeZ * (hr - 1)));
            int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxZ - originZ) / sizeZ * (hr - 1)));

            int touched = 0;
            for (int zi = minZi; zi <= maxZi; zi++)
            {
                float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
                for (int xi = minXi; xi <= maxXi; xi++)
                {
                    float worldX = originX + (xi / (float)(hr - 1)) * sizeX;
                    Vector2 p = new Vector2(worldX, worldZ);

                    // 線分 PathStart-PathEnd 上の最近点への射影(0..1にクランプ = カプセル形状)。
                    float t = Vector2.Dot(p - PathStart, seg) / segLenSq;
                    float tClamped = Mathf.Clamp01(t);
                    Vector2 closest = PathStart + seg * tClamped;
                    float distToLine = Vector2.Distance(p, closest);
                    if (distToLine > maxReach) continue;

                    float originalWorldY = originY + heights[zi, xi] * sizeY;
                    // 中心線に沿った目標の歩道高さ(踊り場から橋のたもとへ線形補間)。
                    float pathY = Mathf.Lerp(startY, endY, tClamped);
                    // 中心線からの距離で、帯の内側(平坦)から外側(元の地形へ収束)へブレンド。
                    float widthT = Mathf.Clamp01((distToLine - CorridorHalfWidth) / FalloffMargin); // 0=帯の内側(平坦), 1=帯の外側(元の高さ)
                    float targetY = Mathf.Lerp(pathY, originalWorldY, Mathf.SmoothStep(0f, 1f, widthT));
                    float newWorldY = Mathf.Min(targetY, originalWorldY); // 掘るだけで盛らない(CarveRecessと同じ方針)
                    heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
                    touched++;
                }
            }

            data.SetHeights(0, 0, heights);
            Physics.SyncTransforms();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine($"PathStart={PathStart} (Y={startY:F2}) -> PathEnd={PathEnd} (Y={endY:F2}), corridor halfWidth={CorridorHalfWidth}m, touched {touched} height cells. SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    static float SampleWorldHeight(Terrain terrain, float worldX, float worldZ)
    {
        return terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrain.transform.position.y;
    }
}
