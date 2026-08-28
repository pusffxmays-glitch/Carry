using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Profiling;

// 重くなった瞬間のプロファイラ内訳を自動でテキスト化する (2026-08-28 v2)。
// v1 の logFile 方式は書き出した .raw を LoadProfile が読めなかった (10GB 化もした)。
// v2 はプレイ中ずっとメモリ内リング (直近 ~300 フレーム) に記録し、重いフレームが
// 連続した瞬間にリングを凍結して、その場で階層ビューから上位コストを抽出、
// Logs/heavy_report_*.txt に書く。プレイヤーの操作は不要。
[InitializeOnLoad]
public static class CarryHeavyCapture
{
    const float HeavyMs = 120f;
    const int HeavyStreak = 10;
    const int TailFrames = 60;    // 発火後もこれだけ録ってから解析 (重い最中を確実に含める)

    static int streak, tail = -1;
    static bool armed;
    static bool wasPlaying;

    static CarryHeavyCapture() { EditorApplication.update += Tick; }

    static void Tick()
    {
        bool playing = Application.isPlaying;
        if (playing && !wasPlaying)
        {
            // プレイ開始: リング記録を開始し、1 プレイ 1 回の解析を武装
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            armed = true; streak = 0; tail = -1;
        }
        if (!playing && wasPlaying)
        {
            ProfilerDriver.enabled = false;
        }
        wasPlaying = playing;
        if (!playing || !armed) return;

        if (tail >= 0)
        {
            if (--tail <= 0) { armed = false; ProfilerDriver.enabled = false; Analyze(); }
            return;
        }
        float dt = Time.unscaledDeltaTime * 1000f;
        if (dt > HeavyMs && dt < 5000f) streak++; else streak = 0;
        if (streak >= HeavyStreak) tail = TailFrames;
    }

    static void WalkItem(HierarchyFrameDataView v, int id, int depth, System.Text.StringBuilder sb)
    {
        var kids = new System.Collections.Generic.List<int>();
        v.GetItemChildren(id, kids);
        var rows = new System.Collections.Generic.List<(int id, float ms)>();
        foreach (var k in kids)
            rows.Add((k, v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime)));
        rows.Sort((a, b) => b.ms.CompareTo(a.ms));
        foreach (var r in rows)
        {
            if (r.ms < 5f) break;               // 5ms 未満は枝ごと省く
            sb.AppendLine($"  {new string(' ', depth * 2)}{r.ms,8:F2}ms  {v.GetItemName(r.id)}");
            if (depth < 6) WalkItem(v, r.id, depth + 1, sb);
        }
    }

    static void Analyze()
    {
        int f0 = ProfilerDriver.firstFrameIndex, f1 = ProfilerDriver.lastFrameIndex;
        if (f1 < 0) { Debug.LogWarning("[HeavyCapture] リングが空"); return; }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"HeavyCapture {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  frames {f0}..{f1}");
        // 最悪フレームを 5 つ選ぶ
        var frames = new System.Collections.Generic.List<(int idx, float ms)>();
        for (int f = f0; f <= f1; f++)
        {
            using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0,
                HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (v == null || !v.valid) continue;
                frames.Add((f, v.frameTimeMs));
            }
        }
        frames.Sort((a, b) => b.ms.CompareTo(a.ms));
        float sum = 0; foreach (var fr in frames) sum += fr.ms;
        sb.AppendLine($"n={frames.Count} 平均={(frames.Count > 0 ? sum / frames.Count : 0):F0}ms" +
                      $" 最悪={(frames.Count > 0 ? frames[0].ms : 0):F0}ms");
        for (int k = 0; k < Mathf.Min(5, frames.Count); k++)
        {
            int f = frames[k].idx;
            sb.AppendLine($"--- frame {f}: {frames[k].ms:F1}ms ---");
            using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0,
                HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (v == null || !v.valid) continue;
                // 5ms 以上のノードだけを再帰的に降りてツリー表示する
                WalkItem(v, v.GetRootItemID(), 0, sb);
            }
        }
        System.IO.Directory.CreateDirectory("Logs");
        string path = $"Logs/heavy_report_{System.DateTime.Now:HHmmss}.txt";
        System.IO.File.WriteAllText(path, sb.ToString());
        Debug.Log($"[HeavyCapture] 重いフレームの内訳を書き出した: {path}\n" + sb.ToString().Substring(0, Mathf.Min(600, sb.Length)));
    }
}
