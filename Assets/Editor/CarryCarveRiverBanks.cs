using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-16: 川(石橋から上流、深い森の中)の左右の岸が、現行のRawHeightAt/ChannelFactorの
// なだらかなブレンド(BankFalloff=4.5m)のせいで、川に落ちたプレイヤーがそのまま岸へ歩いて
// 上がれてしまう問題を修正する。CarryBuildTerrainForest.csのRawHeightAt自体は変更せず
// (再現性のある「基本生成式」はそのまま)、CarveRecess/CarryWidenLakeStairsPathと同じ
// 「後から追加で彫る」パターンで、既存の緩傾斜を橋の少し先から上流域だけ急な崖状に上書きする。
//
// 2026-08-16 v2: ユーザー指示により川幅を2倍に拡張。RiverHalfWidth(z)が返す値をそのまま
// 2倍(hw2 = hw*2)して、フラットな川底(旧: 半径hw*0.5 → 新: 半径hw、ちょうど旧hwと同じ値)
// と急な土手をそこから起こす。RiverBankWall(coastal_cliff_01の岩壁)はユーザーから
// 「違和感がある」との指摘があったため今回の装飾パス(CarryDressRiverBanks.cs)から削除した。
//
// 安全策: どの点についても CarryBuildTerrainForest.LakeFactor(x,z) が実質0(湖の影響が皆無)
// であることを確認してからのみ高さを変更する。湖側のRawHeightAtはMathf.Max(riverCarve,
// lakeCarve)で決まるため、LakeFactorが効いている場所ではlakeCarveが支配的で今回の変更は
// 本来無害なはずだが、「湖エリアは絶対に変更しない」という最重要ルールのため、疑わしきは
// 一切触らない(LakeFactorが0でない点は完全スキップ)。
public static class CarryCarveRiverBanks
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    // 橋のすぐ先(橋の側面をショートカットされないよう、橋のZ範囲の途中から緩やかに効き始める)
    // から、川が奥の森でフェードアウトするRiverZ1まで。RiverHalfWidth自体がRiverZ1に向けて
    // 0へ収束するので、この範囲の終端は自然に効果がなくなる。
    const float ZRampStart = 0f;   // ここから効果が立ち上がり始める
    const float ZFullStrength = 9f; // CourseZ0相当、ここから先は全力
    const float ZEnd = 122f;       // RiverZ1(120)より少し先まで走査(念のため)

    // 新しい「崖」側の遷移幅。水際(inner = halfWidth*0.5)からこの距離だけで
    // 通常の地面高さへ戻す(元は+4.5mだったのを、川幅に関係なく固定の短い距離に圧縮する)。
    const float SteepBankSpan = 1.4f;

    // ユーザー指示(2026-08-16): 川幅を2倍にする。RiverHalfWidth(z)の返り値をそのまま2倍して
    // フラットな川底の半径(inner = hw2*0.5 = 旧hwとちょうど同じ値)に使う。
    const float WidthMultiplier = 2.0f;

    const float LakeFactorEpsilon = 0.0005f; // これを超えたら湖の影響ありとみなして一切触らない

    [MenuItem("Carry/Carve River Banks (Steep, Upstream of Bridge)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = Terrain.activeTerrain;
            if (terrain == null) { Debug.LogError("Terrain.activeTerrain が見つかりません。"); return; }

            var t = typeof(CarryBuildTerrainForest);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var riverXM = t.GetMethod("RiverX", flags);
            var riverHalfWidthM = t.GetMethod("RiverHalfWidth", flags);
            var channelFactorM = t.GetMethod("ChannelFactor", flags);
            var lakeFactorM = t.GetMethod("LakeFactor", flags);
            var riverDepthF = t.GetField("RiverDepth", flags);
            float riverDepth = (float)riverDepthF.GetValue(null);

            var data = terrain.terrainData;
            float originX = terrain.transform.position.x, originY = terrain.transform.position.y, originZ = terrain.transform.position.z;
            float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
            int hr = data.heightmapResolution;

            var heights = data.GetHeights(0, 0, hr, hr);

            int minZi = Mathf.Max(0, Mathf.FloorToInt((ZRampStart - originZ) / sizeZ * (hr - 1)));
            int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((ZEnd - originZ) / sizeZ * (hr - 1)));

            int touched = 0, skippedLake = 0;
            for (int zi = minZi; zi <= maxZi; zi++)
            {
                float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
                if (worldZ < ZRampStart || worldZ > ZEnd) continue;

                float rampT = Mathf.Clamp01(Mathf.InverseLerp(ZRampStart, ZFullStrength, worldZ));

                float rx = (float)riverXM.Invoke(null, new object[] { worldZ });
                float hw = (float)riverHalfWidthM.Invoke(null, new object[] { worldZ });
                if (hw <= 0.01f) continue; // このZに川そのものが存在しない(奥のフェードアウト域)

                float hw2 = hw * WidthMultiplier;

                // このZ帯で影響し得るXの範囲だけ走査(旧遷移帯+新しい倍幅チャンネル+新遷移帯を
                // 両方カバーする余裕を持たせる)。
                float scanHalf = hw2 + 6f;
                int minXi = Mathf.Max(0, Mathf.FloorToInt((rx - scanHalf - originX) / sizeX * (hr - 1)));
                int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((rx + scanHalf - originX) / sizeX * (hr - 1)));

                for (int xi = minXi; xi <= maxXi; xi++)
                {
                    float worldX = originX + (xi / (float)(hr - 1)) * sizeX;

                    float lakeF = (float)lakeFactorM.Invoke(null, new object[] { worldX, worldZ });
                    if (lakeF > LakeFactorEpsilon) { skippedLake++; continue; }

                    float d = Mathf.Abs(worldX - rx);
                    float oldFactor = (float)channelFactorM.Invoke(null, new object[] { d, hw });

                    // 倍幅チャンネル: フラット底の半径を hw2*0.5 (= 旧hwとちょうど同じ値)まで広げ、
                    // そこから急な土手(SteepBankSpan)で通常の高さへ戻す。
                    float inner = hw2 * 0.5f;
                    float outerNew = inner + SteepBankSpan;
                    float tNew = Mathf.InverseLerp(inner, outerNew, d);
                    float newFactorFull = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tNew));
                    // 橋付近ではrampTで新旧をブレンドし、橋のZレンジ内で急に地形が変わらないようにする。
                    float newFactor = Mathf.Lerp(oldFactor, newFactorFull, rampT);

                    float delta = riverDepth * (newFactor - oldFactor);
                    if (Mathf.Abs(delta) < 1e-5f) continue;

                    float originalWorldY = originY + heights[zi, xi] * sizeY;
                    float newWorldY = originalWorldY - delta;
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

            log.AppendLine($"River banks carved: touched {touched} cells, skipped {skippedLake} lake-guarded cells. SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    // RiverWater(BuildWaterが作る帯状メッシュ)は RiverWaterZ0=-22(湖に近い側)から RiverZ1=120
    // まで一本のストリップとして生成されるため、川幅の変更を反映するにはこのメッシュの頂点も
    // 同じ倍率で押し広げる必要がある。ただし RiverWaterZ0=-22 は湖のすぐ手前まで入り込んでおり、
    // 「湖エリアには一切変更を加えない」という最重要ルールのため、地形カービングと全く同じ
    // Zゲート(ZRampStart=0からZFullStrength=9でランプ)を使い、z<0の頂点は一切動かさない。
    [MenuItem("Carry/Widen River Water Mesh (matches bank carve)")]
    public static void WidenWaterMesh()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var riverWaterGo = GameObject.Find("ForestStage_Terrain/RiverWater");
            if (riverWaterGo == null) { Debug.LogError("RiverWater not found."); return; }
            var mf = riverWaterGo.GetComponent<MeshFilter>();
            var mesh = mf.sharedMesh;
            var verts = mesh.vertices;

            var t = typeof(CarryBuildTerrainForest);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var riverXM = t.GetMethod("RiverX", flags);
            var lakeFactorM = t.GetMethod("LakeFactor", flags);

            int changed = 0, skippedLake = 0;
            // BuildWaterはstation毎に(left, right)の2頂点を順番に積んでいくため、偶数/奇数
            // インデックスのペアが1つのZステーションに対応する。
            for (int i = 0; i + 1 < verts.Length; i += 2)
            {
                Vector3 left = verts[i], right = verts[i + 1];
                float z = left.z; // ローカル座標=ワールド座標(RiverWaterはtransformが単位行列で配置)
                if (z < ZRampStart) continue; // 湖側は一切触らない

                float rampT = Mathf.Clamp01(Mathf.InverseLerp(ZRampStart, ZFullStrength, z));
                float widthMul = Mathf.Lerp(1f, WidthMultiplier, rampT);
                if (Mathf.Approximately(widthMul, 1f)) continue;

                float rx = (float)riverXM.Invoke(null, new object[] { z });
                float lf = (float)lakeFactorM.Invoke(null, new object[] { rx, z });
                if (lf > LakeFactorEpsilon) { skippedLake++; continue; } // 安全側: 疑わしきは触らない

                float newLeftX = rx - (rx - left.x) * widthMul;
                float newRightX = rx + (right.x - rx) * widthMul;
                verts[i] = new Vector3(newLeftX, left.y, left.z);
                verts[i + 1] = new Vector3(newRightX, right.y, right.z);
                changed++;
            }

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine($"River water mesh widened: {changed} stations changed, {skippedLake} lake-guarded stations skipped. SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
