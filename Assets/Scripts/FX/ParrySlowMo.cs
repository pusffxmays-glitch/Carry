using UnityEngine;

// ParrySlowMo -- パリー成功時のヒットストップ + スローモーション (2026-08-24)。
//
// 狙いはストリートファイター 6 のパリー: 成功した「瞬間」をごく短く止め、そのあと
// 少しの間だけ時間が伸びて、通常速度へ戻る。止めっぱなしにしないのが要点で、
// 全体でも 0.6 秒ほどの出来事にする。
//
// timeScale を落とすと流体もクリップも同じ時計で遅くなるので、見た目だけが伸びて
// 挙動 (こぼれ方) は変わらない。演出のためだけの装置で、判定には触らない。
//
// Time.timeScale はエディタでは再生を止めても残る。例外や無効化で 0.05 のまま
// 取り残されると「エディタが壊れた」ように見えるため、OnDisable で必ず戻す。
public class ParrySlowMo : MonoBehaviour
{
    static ParrySlowMo instance;

    [Tooltip("成功の瞬間に止める時間 (実時間秒)。")]
    public float hitStopSeconds = 0.07f;
    // 0 にはしない。流体の Step が dt=0 で回るのを避けるため (下限 0.05 = 20 分の 1 倍速)。
    [Tooltip("ヒットストップ中の時間の速さ。0 にはしない。")]
    public float hitStopScale = 0.05f;

    [Tooltip("そのあとのスロー再生の速さ (ジャスト)。")]
    public float justScale = 0.26f;
    [Tooltip("そのあとのスロー再生の速さ (グッド)。")]
    public float goodScale = 0.42f;
    [Tooltip("スローを保つ時間 (実時間秒)。")]
    public float holdSeconds = 0.20f;
    [Tooltip("通常速度へ戻すのにかける時間 (実時間秒)。")]
    public float recoverSeconds = 0.30f;

    [Tooltip("スロー中にカメラの画角を狭める量 (度)。0 で寄りなし。")]
    public float fovPunchDeg = 4.5f;

    float baseFixedDelta = -1f;
    Camera cam;
    float baseFov = -1f;
    Coroutine running;

    /// <summary>パリー成功時に呼ぶ。just = ジャスト判定。</summary>
    public static void Play(bool just)
    {
        if (instance == null)
        {
            var go = new GameObject("ParrySlowMo") { hideFlags = HideFlags.HideAndDontSave };
            instance = go.AddComponent<ParrySlowMo>();
            DontDestroyOnLoad(go);
        }
        instance.Begin(just);
    }

    void Begin(bool just)
    {
        if (baseFixedDelta < 0f) baseFixedDelta = Time.fixedDeltaTime;
        if (running != null) StopCoroutine(running);      // 連続成功は最後の 1 回に上書き
        running = StartCoroutine(Run(just ? justScale : goodScale));
    }

    System.Collections.IEnumerator Run(float slow)
    {
        // 画角の基準は「まだ触っていない状態」の値。連続でパリーしたときに
        // 縮めた画角を基準として拾わないよう、最初の 1 回だけ覚える。
        cam = Camera.main;
        if (cam != null && baseFov < 0f) baseFov = cam.fieldOfView;

        Set(hitStopScale, 1f);
        yield return new WaitForSecondsRealtime(hitStopSeconds);

        Set(slow, 0f);
        yield return new WaitForSecondsRealtime(holdSeconds);

        // 戻りは緩め (1 - (1-t)^2)。急に等速へ戻すと「切れた」ように見える。
        float t = 0f;
        while (t < recoverSeconds)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / recoverSeconds);
            Set(Mathf.Lerp(slow, 1f, 1f - (1f - u) * (1f - u)), 1f - u);
            yield return null;
        }
        Set(1f, 0f);
        running = null;
    }

    // punch: 画角を狭める強さ (0〜1)。ヒットストップの一瞬だけ最大にして、あとは抜く。
    void Set(float scale, float punch)
    {
        Time.timeScale = scale;
        if (baseFixedDelta > 0f) Time.fixedDeltaTime = baseFixedDelta * Mathf.Max(0.02f, scale);
        if (cam != null && fovPunchDeg > 0.01f && baseFov > 0f)
            cam.fieldOfView = baseFov - fovPunchDeg * Mathf.Clamp01(punch);
    }

    void OnDisable()
    {
        Time.timeScale = 1f;
        if (baseFixedDelta > 0f) Time.fixedDeltaTime = baseFixedDelta;
        if (cam != null && baseFov > 0f) cam.fieldOfView = baseFov;
    }
}
