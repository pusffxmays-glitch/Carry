using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ============================================================================================
// CarryBuildPathSmoothColliders -- アセット道の「ならしコライダー」(2026-08-21)。
//
// 橋の出口〜足場チェーン (PlatformKenney / PathAccent) の上に、見えない滑らかな
// コライダー帯を敷いて歩行時の段差 (±5〜30cm) を消す。見た目のメッシュは一切変えない。
// 実測: 橋→道進入の直線歩行だけでポーションが 35-45% こぼれていた主因が、
// 段差の踏み外し/せり上がりによる壺の上下ジョルトだった。
//
//  * 連続区間 (ノード間隔 <= runBreakGap) ごとに帯を作り、**ジャンプ用の切れ目は跨がない**
//  * 帯の高さは近傍ノードのローリング最大値 → 既存コライダーが帯を突き破らない
//  * 隣接セグメントは上面がノードで一致する傾いた箱 → 帯自体に段差ゼロ
// ============================================================================================
public static class CarryBuildPathSmoothColliders
{
    const string RootName = "PathSmoothColliders";
    const float RunBreakGap = 4.5f;     // これ以上離れたノード間は別区間 (ジャンプの切れ目)
    const float SampleStep = 1.0f;
    const float MaxWindow = 1.6f;       // ローリング最大の半窓 (m)
    const float StripWidth = 2.4f;   // 2026-08-22: 1.7 -> 2.4。足場の幅より広くして、帯の側壁 (段差リップ) が歩行域内に出ないように
    const float StripThickness = 0.25f;
    const float TopClearance = 0.015f;  // 既存トップからのわずかな持ち上げ
    const float EndOverhang = 0.8f;     // 区間端をプラットフォームの縁まで延ばす

    [MenuItem("Tools/Carry/道のならしコライダーを生成")]
    public static void Generate()
    {
        Clear();

        var nodes = new List<Vector3>();
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            var n = c.gameObject.name;
            if ((n.StartsWith("PlatformKenney") || n.StartsWith("PathAccent")) && c.bounds.center.z > 8f)
                nodes.Add(new Vector3(c.bounds.center.x, c.bounds.max.y, c.bounds.center.z));
        }
        if (nodes.Count < 2)
        {
            Debug.LogError("ならしコライダー: 対象の道 (PlatformKenney/PathAccent) が見つかりません。");
            return;
        }
        nodes.Sort((a, b) => a.z.CompareTo(b.z));
        // 橋の出口 (スタートの正面)。橋の面 topY≒2.17 から道へ滑らかにつなぐ。
        nodes.Insert(0, new Vector3(-3.2f, 2.17f, 6.6f));

        // ---- 連続区間へ分割 ----
        var runs = new List<List<Vector3>>();
        var cur = new List<Vector3> { nodes[0] };
        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i].z - nodes[i - 1].z > RunBreakGap)
            {
                if (cur.Count >= 2) runs.Add(cur);
                cur = new List<Vector3>();
            }
            cur.Add(nodes[i]);
        }
        if (cur.Count >= 2) runs.Add(cur);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "PathSmoothColliders");
        int total = 0;

        foreach (var run in runs)
        {
            float z0 = run[0].z - EndOverhang, z1 = run[run.Count - 1].z + EndOverhang;
            var pts = new List<Vector3>();
            for (float z = z0; z <= z1 + 0.01f; z += SampleStep)
            {
                // x は線形補間、y は近傍ノードのローリング最大 (既存コライダーを突き破らせない)
                int i = 0;
                while (i < run.Count - 2 && run[i + 1].z < z) i++;
                float t = Mathf.InverseLerp(run[i].z, run[i + 1].z, Mathf.Clamp(z, run[i].z, run[i + 1].z));
                float x = Mathf.Lerp(run[i].x, run[i + 1].x, t);
                float y = float.MinValue;
                foreach (var nd in run)
                    if (Mathf.Abs(nd.z - z) <= MaxWindow) y = Mathf.Max(y, nd.y);
                if (y == float.MinValue) y = Mathf.Lerp(run[i].y, run[i + 1].y, t);
                pts.Add(new Vector3(x, y + TopClearance, z));
            }
            if (pts.Count < 2) continue;

            var runGo = new GameObject($"SmoothRun_z{run[0].z:F0}");
            runGo.transform.SetParent(root.transform, false);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                Vector3 mid = (a + b) * 0.5f;
                Vector3 d = b - a;
                var seg = new GameObject($"Seg_{i:D2}");
                seg.transform.SetParent(runGo.transform, false);
                seg.transform.position = mid - Vector3.up * (StripThickness * 0.5f);
                seg.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                var bc = seg.AddComponent<BoxCollider>();
                bc.size = new Vector3(StripWidth, StripThickness, d.magnitude + 0.06f);
                total++;
            }
        }

        Debug.Log($"ならしコライダー: {runs.Count} 区間 / {total} セグメント生成");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
    }

    [MenuItem("Tools/Carry/道のならしコライダーを削除")]
    public static void Clear()
    {
        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
    }
}
