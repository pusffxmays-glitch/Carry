using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// ============================================================================================
// CarryBuildParallelBlockRoad -- アセット道に並走する平坦なブロック道 (2026-08-21)。
//
// スタート時に向いている方向 (+z) のアセット道 (PlatformKenney / PathAccent チェーン) の
// 中心線を拾い、横に lateralOffset だけずらした位置へブロックの道を敷く。
// 2 本の道が並走するイメージ。おおまかな経路形状 (蛇行・高さ) は元の道に合わせ、
// 表面は完全に滑らか:
//   * 中心線の x と高さは移動平均で平滑化
//   * ブロックはノード間を 1 個ずつつなぐ傾いた板で、上面がノードで正確に一致 (段差ゼロ)
// 凹凸比較 (ヒートマップ/歩行感) のリファレンス用。
// ============================================================================================
public static class CarryBuildParallelBlockRoad
{
    const string RootName = "ParallelBlockRoad";
    const float LateralOffset = 4.0f;   // 元の道からのオフセット (+x = スタート時の右手側)
    const float RoadWidth = 2.2f;
    const float RoadThickness = 0.4f;
    const float SampleStep = 2.5f;      // 再サンプル間隔 (m)
    const float TopClearance = 0.02f;

    [MenuItem("Tools/Carry/並走ブロック道を生成")]
    public static void Generate()
    {
        Clear();

        // ---- 元の道の中心線を収集 (橋より先の +z 方向チェーン) ----
        var nodes = new List<Vector3>();
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            var n = c.gameObject.name;
            if ((n.StartsWith("PlatformKenney") || n.StartsWith("PathAccent")) && c.bounds.center.z > 8f)
                nodes.Add(new Vector3(c.bounds.center.x, c.bounds.max.y, c.bounds.center.z));
        }
        if (nodes.Count < 2)
        {
            Debug.LogError("並走ブロック道: 元になる道 (PlatformKenney/PathAccent) が見つかりません。");
            return;
        }
        nodes.Sort((a, b) => a.z.CompareTo(b.z));
        // 橋の出口 (スタート地点の少し先) から始める
        nodes.Insert(0, new Vector3(-3.2f, 2.17f, 7f));

        // ---- z で等間隔に再サンプル (x, y は線形補間) ----
        float z0 = nodes[0].z, z1 = nodes[nodes.Count - 1].z;
        var pts = new List<Vector3>();
        for (float z = z0; z <= z1 + 0.01f; z += SampleStep)
        {
            int i = 0;
            while (i < nodes.Count - 2 && nodes[i + 1].z < z) i++;
            float t = Mathf.InverseLerp(nodes[i].z, nodes[i + 1].z, z);
            float x = Mathf.Lerp(nodes[i].x, nodes[i + 1].x, t);
            float y = Mathf.Lerp(nodes[i].y, nodes[i + 1].y, t);
            pts.Add(new Vector3(x + LateralOffset, y + TopClearance, z));
        }

        // ---- 移動平均で平滑化 (x と y、窓 5) ----
        var sm = new List<Vector3>(pts);
        for (int i = 0; i < pts.Count; i++)
        {
            float sx = 0, sy = 0; int c = 0;
            for (int k = -2; k <= 2; k++)
            {
                int j = Mathf.Clamp(i + k, 0, pts.Count - 1);
                sx += pts[j].x; sy += pts[j].y; c++;
            }
            sm[i] = new Vector3(sx / c, sy / c, pts[i].z);
        }

        // ---- ブロック生成: ノード間を 1 個の傾いた板でつなぐ (上面ツライチ) ----
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "ParallelBlockRoad");
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", new Color(0.62f, 0.60f, 0.55f, 1f));   // 落ち着いた石色
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/Materials/Generated")) AssetDatabase.CreateFolder("Assets/Materials", "Generated");
        string matPath = "Assets/Materials/Generated/ParallelBlockRoad.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing == null) AssetDatabase.CreateAsset(mat, matPath);
        mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        for (int i = 0; i < sm.Count - 1; i++)
        {
            Vector3 a = sm[i], b = sm[i + 1];
            Vector3 mid = (a + b) * 0.5f;
            Vector3 d = b - a;
            float len = d.magnitude + 0.06f;    // わずかに重ねて継ぎ目の隙間を消す

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"RoadBlock_{i:D2}";
            go.transform.SetParent(root.transform, false);
            // 上面がノード a-b を通るように、中心は半厚みだけ下げる
            go.transform.position = mid - Vector3.up * (RoadThickness * 0.5f);
            go.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            go.transform.localScale = new Vector3(RoadWidth, RoadThickness, len);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        Debug.Log($"並走ブロック道: {sm.Count - 1} ブロック生成 (z {z0:F0}→{z1:F0}, オフセット +{LateralOffset}m)");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
    }

    [MenuItem("Tools/Carry/並走ブロック道を削除")]
    public static void Clear()
    {
        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
    }
}
