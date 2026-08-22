using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// CarryStartupProfile -- 「プレイを押してから遊べるようになるまで」の内訳を測る (2026-08-23)。
//
// 体感で待たされる原因を「この処理が何 ms」という形に落とすための計測だけを行う。
// エディタ側 (CarryStartupProfilerHook) が **プレイボタンを押した時刻** を SessionState に
// 残すので、ドメインリロードを跨いでも「押してから」の実時間が取れる。
// ランタイム側は Awake / Start / 最初の Update / 最初の描画完了に印を打ち、
// FluidCore など重い初期化は自分で Mark を呼んで内訳を足す。
//
// 使い方: 何もしなくてよい。プレイに入ると自動で計測し、最初の描画が終わった次の
// フレームにコンソールへ一覧を出す。Report で文字列としても取れる。
// ============================================================================================
public static class CarryStartupProfile
{
    public struct Entry { public string name; public double ms; }

    static readonly List<Entry> entries = new List<Entry>(32);
    static readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
    static bool started;

    /// <summary>エディタが記録した「プレイボタンを押してから最初の Awake まで」の秒数。
    /// ドメインリロードとシーンロードがここに含まれる。0 なら未計測。</summary>
    public static double PrePlaySeconds { get; set; }

    public static bool Started => started;
    public static IReadOnlyList<Entry> Entries => entries;

    /// <summary>最初の Awake で 1 回だけ呼ぶ。以降の Mark はここからの経過時間。</summary>
    public static void Begin()
    {
        entries.Clear();
        started = true;
        sw.Restart();
    }

    /// <summary>区間の終わりに印を打つ (経過時間を記録)。</summary>
    public static void Mark(string name)
    {
        if (!started) return;
        entries.Add(new Entry { name = name, ms = sw.Elapsed.TotalMilliseconds });
    }

    /// <summary>自分で測った所要時間を、経過時間とは別に足す (例: FluidCore.Initialise 単体)。</summary>
    public static void AddDuration(string name, double ms)
    {
        if (!started) return;
        entries.Add(new Entry { name = name + " [単体]", ms = -ms });   // 負値 = 累積ではなく単体時間
    }

    public static string Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 起動時間の内訳 ===");
        if (PrePlaySeconds > 0)
            sb.AppendLine($"  プレイ押下 → 最初の Awake : {PrePlaySeconds * 1000.0,8:F0} ms  (ドメインリロード + シーンロード)");
        double prev = 0;
        foreach (var e in entries)
        {
            if (e.ms < 0)
            {
                sb.AppendLine($"      └ {e.name,-40} {-e.ms,8:F0} ms");
                continue;
            }
            sb.AppendLine($"  {e.name,-44} {e.ms - prev,8:F0} ms   (累積 {e.ms,7:F0} ms)");
            prev = e.ms;
        }
        double total = (PrePlaySeconds * 1000.0) + prev;
        sb.AppendLine($"  合計 (押下 → 最初の描画完了)                {total,8:F0} ms");
        return sb.ToString();
    }

    // ---- 自動セットアップ: シーンに何も置かずに計測を回す ----
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        Begin();
        Mark("BeforeSceneLoad");
        var go = new GameObject("~CarryStartupProfile") { hideFlags = HideFlags.HideAndDontSave };
        go.AddComponent<CarryStartupProfileRunner>();
        Object.DontDestroyOnLoad(go);
    }
}

/// <summary>フレームの節目に印を打ち、最初の描画が終わったら一覧を出す。</summary>
[DefaultExecutionOrder(-32000)]
public class CarryStartupProfileRunner : MonoBehaviour
{
    int frames;
    bool reported;

    void Start() { CarryStartupProfile.Mark("最初の Start まで"); }

    void Update()
    {
        if (frames == 0) CarryStartupProfile.Mark("最初の Update まで");
        frames++;
    }

    void LateUpdate()
    {
        // 2 フレーム目まで待つ: 1 フレーム目はシェーダ/コンピュートの初回コンパイルが乗る。
        if (reported || frames < 2) return;
        reported = true;
        CarryStartupProfile.Mark("2 フレーム目の終わり (描画・シェーダ初回込み)");
        Debug.Log(CarryStartupProfile.Report());
    }
}
