using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-16: ユーザー指示により、川のコース(Footholds: GiantBoulder/MossBoulder/Log/RootSpan/
// DirtMound/SmallRockCluster/RuinSlab)の高さを橋(StoneBridge_Meshy)のデッキと同じ高さへ変更。
// 「浮遊させて問題ない(見た目の接地は不要)」との明示的な許可を得ているため、地形/水面への
// 接地処理は行わない。
//
// 安全マージン: GoblinLocomotionの実測値(runSpeed=5, jumpSpeed=6, gravity=-20)から、
// 橋の高さ(Y≈2.36)から岸(Y≈0〜1程度)へ落差込みでジャンプした場合の最大到達距離は
// 理論上 約3.9〜4.4m(t=(v0+sqrt(v0^2+2|g|D))/|g|, dist=runSpeed*t)。
// 「コースからジャンプして川の両岸に着地できないように」との指示を受け、川岸(新しい急な
// 土手の縁、中心からの距離=RiverHalfWidth(z)相当)からの水平クリアランスを常に6m以上
// 確保するよう、既存の左右への蛇行(weave)振幅を必要に応じて圧縮する。Z方向の間隔(コース
// 沿いに歩数を刻むリズム)は変更しない(高さ変更でジャンプ物理は変わらないため、コース沿いの
// 隣接ジャンプ難度は従来のまま)。
public static class CarryRaiseFootholdsToBridgeHeight
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float BankClearance = 6f; // 岸(RiverHalfWidthで定義される急な土手の縁)からの最低水平距離

    [MenuItem("Carry/Raise River Footholds To Bridge Height")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var footRoot = GameObject.Find("ForestStage_Terrain/Footholds");
            if (footRoot == null) { Debug.LogError("Footholds not found."); return; }

            var t = typeof(CarryBuildTerrainForest);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var riverXM = t.GetMethod("RiverX", flags);
            var riverHalfWidthM = t.GetMethod("RiverHalfWidth", flags);
            var computeBridgeDeckYM = t.GetMethod("ComputeBridgeDeckY", flags);
            float bridgeDeckY = (float)computeBridgeDeckYM.Invoke(null, null);

            int moved = 0, clamped = 0;
            var children = new System.Collections.Generic.List<Transform>();
            foreach (Transform c in footRoot.transform) children.Add(c);

            foreach (var tr in children)
            {
                float z = tr.position.z;
                float rx = (float)riverXM.Invoke(null, new object[] { z });
                float hw = (float)riverHalfWidthM.Invoke(null, new object[] { z });

                float distFromCenter = tr.position.x - rx;
                float maxAmplitude = Mathf.Max(0.3f, hw - BankClearance); // 常に少しは横位置を残す
                float newDist = Mathf.Clamp(distFromCenter, -maxAmplitude, maxAmplitude);
                if (!Mathf.Approximately(newDist, distFromCenter)) clamped++;

                var pos = tr.position;
                pos.x = rx + newDist;
                pos.y = bridgeDeckY;
                tr.position = pos;
                moved++;

                log.AppendLine($"{tr.name}: z={z:F1} hw={hw:F1} distFromCenter {distFromCenter:F1}->{newDist:F1} clearanceToBank={hw - Mathf.Abs(newDist):F1} newY={bridgeDeckY:F2}");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine($"Moved {moved} footholds to bridge height (Y={bridgeDeckY:F2}), clamped lateral position on {clamped} of them for bank safety. SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
