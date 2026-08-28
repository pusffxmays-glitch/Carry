using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

// 重くなった瞬間のプロファイラを自動採取する (2026-08-28)。
// 実機 (このPC) の「パリー後 6FPS」が統制実験で再現できないため、
// 発生したそのフレームで Unity Profiler のバイナリログを 5 秒間記録する。
// プレイヤーの操作は不要。記録すると Console に保存先を出す。
// ログは Logs/heavy_capture_*.raw に残り、Profiler ウィンドウの
// Load Profile で開いて内訳 (描画 / 流体 / エディタ) を確認できる。
[InitializeOnLoad]
public static class CarryHeavyCapture
{
    const float HeavyMs = 120f;     // これを超えるフレームが
    const int HeavyStreak = 15;   // 連続でこれだけ続いたら記録開始
    const int CaptureFrames = 300;  // 記録するフレーム数 (プロファイラ既定リング以内)

    static int streak;
    static int capturing;           // 残り記録フレーム数
    static bool armed = true;       // セッション 1 回だけ
    static string path;

    static CarryHeavyCapture()
    {
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!Application.isPlaying) { streak = 0; return; }

        if (capturing > 0)
        {
            if (--capturing == 0)
            {
                UnityEngine.Profiling.Profiler.enabled = false;
                UnityEngine.Profiling.Profiler.enableBinaryLog = false;
                ProfilerDriver.enabled = false;
                Debug.Log($"[HeavyCapture] 記録完了: {path} (Profiler > Load Profile で開ける)");
            }
            return;
        }
        if (!armed) return;

        float dt = Time.unscaledDeltaTime * 1000f;
        if (dt > HeavyMs && dt < 5000f) streak++; else streak = 0;
        if (streak < HeavyStreak) return;

        armed = false;
        path = $"Logs/heavy_capture_{System.DateTime.Now:HHmmss}";
        System.IO.Directory.CreateDirectory("Logs");
        UnityEngine.Profiling.Profiler.logFile = path;
        UnityEngine.Profiling.Profiler.enableBinaryLog = true;
        UnityEngine.Profiling.Profiler.enabled = true;
        ProfilerDriver.enabled = true;
        capturing = CaptureFrames;
        Debug.Log($"[HeavyCapture] 重いフレームが連続したため記録開始 ({dt:F0}ms): {path}.raw");
    }
}
