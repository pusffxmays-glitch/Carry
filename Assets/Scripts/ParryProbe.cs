using System.Collections;
using UnityEngine;

// ParryProbe -- パリー 1 回ぶんの残量変化を測る計測用コンポーネント (2026-08-24)。
//
// MCP から毎フレーム値を読むと、その読み出し自体がヒッチを作って流体のこぼれ量を
// 変えてしまう (実測)。そこでゲーム内で一連の動作を走らせ、終わってから結果を
// 1 回だけ読む。A/B の条件はインスペクタ相当のフィールドを外から書いて変える。
public class ParryProbe : MonoBehaviour
{
    public string Result = "";
    public bool Running;
    /// <summary>(着地からの実時間秒, 壺の高さ)。グラフ用。</summary>
    public readonly System.Collections.Generic.List<Vector2> Samples =
        new System.Collections.Generic.List<Vector2>();

    /// <summary>Samples を "t:y t:y ..." で返す (MCP から 1 回で読む用)。</summary>
    public string SampleText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var v in Samples) sb.AppendFormat("{0:F3}:{1:F4} ", v.x, v.y);
        return sb.ToString();
    }

    public static ParryProbe Attach()
    {
        var rig = FindFirstObjectByType<GoblinCarryRig>();
        if (rig == null) return null;
        var p = rig.GetComponent<ParryProbe>();
        if (p == null) p = rig.gameObject.AddComponent<ParryProbe>();
        return p;
    }

    /// <summary>流体 Step の実測分布。危険域 (デバイスロスト直前) かどうかの判定用。
    ///
    /// MCP から毎フレーム読むと、その読み出し自体が数百 ms のヒッチを作って値を壊す。
    /// ここで seconds 秒ぶん貯めてから 1 回だけ読むこと。</summary>
    public string StepHealth = "";

    public void MeasureStep(float seconds)
    {
        if (Running) return;
        StopAllCoroutines();
        StartCoroutine(StepLoop(seconds));
    }

    IEnumerator StepLoop(float seconds)
    {
        Running = true;
        StepHealth = "";
        var fluid = FindFirstObjectByType<FluidCore>();
        var vals = new System.Collections.Generic.List<float>();
        int overWatchdog = 0, longestRun = 0, run = 0, frames = 0;

        // 実時計で測る。Time.unscaledDeltaTime は maximumDeltaTime のクランプを受けうるので、
        // 「6 秒間で 2 フレーム」のような値が本当なのか計測側の嘘なのか区別できない。
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            frames++;
            if (fluid != null)
            {
                float ms = fluid.LastStepMs;
                vals.Add(ms);
                // 危険なのは単発のスパイクではなく **続くこと**。連続長を数える。
                if (ms > fluid.watchdogStepMs) { overWatchdog++; run++; if (run > longestRun) longestRun = run; }
                else run = 0;
            }
            yield return null;
        }
        watch.Stop();
        vals.Sort();
        float med = vals.Count > 0 ? vals[vals.Count / 2] : -1f;
        float p95 = vals.Count > 0 ? vals[Mathf.Min(vals.Count - 1, (int)(vals.Count * 0.95f))] : -1f;
        float max = vals.Count > 0 ? vals[vals.Count - 1] : -1f;
        int particles = fluid != null ? fluid.FluidCount : -1;
        int subs = fluid != null ? fluid.LastSubStepCount : -1;
        StepHealth = string.Format("{0},{1:F1},{2:F1},{3:F1},{4},{5},{6:F2},{7:F1},{8},{9}",
                                   frames, med, p95, max, overWatchdog, longestRun,
                                   watch.Elapsed.TotalSeconds, frames / watch.Elapsed.TotalSeconds,
                                   particles, subs);
        Running = false;
    }

    /// <summary>home で待機 → 前進 → ジャンプ → 空中でパリー → 着地後に残量を読む。</summary>
    public void Run(Vector3 home, bool parry, float dropHeight = 0f)
    {
        if (Running) return;
        StopAllCoroutines();
        StartCoroutine(Sequence(home, parry, dropHeight));
    }

    IEnumerator Sequence(Vector3 home, bool parry, float dropHeight)
    {
        Running = true;
        Result = "";
        var rig = GetComponent<GoblinCarryRig>();
        var loco = GetComponent<GoblinLocomotion>();
        var acts = GetComponent<GoblinPotActions>();
        var cc = GetComponent<CharacterController>();
        var src = FindFirstObjectByType<FluidCore>() as IPotionVolumeSource;

        loco.debugMoveForward = false;
        rig.armBalance = 0f;
        cc.enabled = false; transform.position = home; cc.enabled = true;
        yield return new WaitForSeconds(2.5f);          // 液面が静定するまで待つ

        float before = src != null ? src.FillFraction01 : -1f;

        float t = 0f;
        if (dropHeight > 0.01f)
        {
            // 高所落下のパリー。ジャンプの高さでは戻り際のこぼれがほとんど出ないので、
            // 落差を作って本番に近い衝撃で測る。
            cc.enabled = false; transform.position = home + Vector3.up * dropHeight; cc.enabled = true;
        }
        else
        {
            loco.debugMoveForward = true;
            yield return new WaitForSeconds(1.2f);
            loco.debugJumpRequest = true;
        }

        // 離陸 (または落下開始) を待つ
        while (cc.isGrounded && t < 1.5f) { t += Time.deltaTime; yield return null; }
        // パリーの受付窓は着地前 0.35 秒しかない。5m 落下は約 1 秒かかるので、
        // 落下開始直後に押すと「早すぎ」で不成立になる (最初の計測はこれで外していた)。
        // 押した瞬間から SoftenLanding で落下が 3.5 m/s に抑えられるので、
        // 「押した高さ / 3.5」がそのまま着地までの時間になる。窓 0.35 秒の内側に
        // 確実に入れるため 0.75m (= 約 0.21 秒) で押す。1.2m だと 0.36 秒で外れた。
        t = 0f;
        while (transform.position.y - home.y > 0.75f && t < 4f) { t += Time.deltaTime; yield return null; }
        if (parry) acts.debugParryRequest = true;

        // 着地を待つ
        t = 0f;
        while (!cc.isGrounded && t < 3f) { t += Time.deltaTime; yield return null; }
        float atLand = src != null ? src.FillFraction01 : -1f;

        // クッションクリップが流れている時間を測る。壺の速度を直接見ようとすると
        // 着地時の CharacterController の押し戻しが混ざって読めない (実測 3 m/s 級の
        // スパイクが出る)。再生時間なら決定的に出るし、「伸び上がりを遅くした」の
        // 効果はそのままここに現れる。
        // ゲーム内時間と実時間の両方を測る。スローモーションはゲーム内時間を
        // 変えない (クリップの進み方は同じ) が、実時間では長く見えるようになる。
        // この 2 つの差がそのままスローの体感時間。
        // 壺の上昇速度をこの中で計算する。MCP から毎フレーム読むと、その読み出しが
        // エディタを 1 秒近く止め、その 1 フレームだけ巨大な dt になって偽のスパイクが出る
        // (実測: 停止フレームで +2.9 m/s)。dt が 50ms を超えたフレームは停止とみなして
        // 捨てる。60fps なら 16ms なので、正常なフレームは 1 つも落ちない。
        var anim = GetComponent<GoblinClipAnimator>();
        var potT = rig.transform.Find("Carry_Pot");
        float clipSecs = 0f, minScale = 1f, maxRise = 0f, stalls = 0f;
        float prevY = potT != null ? potT.position.y - rig.transform.position.y : 0f;
        Samples.Clear();
        t = 0f;
        while (t < 4f)
        {
            float rdt = Time.unscaledDeltaTime;
            t += rdt;
            if (anim != null && anim.OneShotActive) clipSecs += Time.deltaTime;
            if (Time.timeScale < minScale) minScale = Time.timeScale;
            if (potT != null)
            {
                float y = potT.position.y - rig.transform.position.y;
                if (rdt > 0.05f) stalls += 1f;
                else if (rdt > 1e-4f)
                {
                    float v = (y - prevY) / rdt;
                    if (v > maxRise) maxRise = v;
                }
                prevY = y;
                Samples.Add(new Vector2(t, y));
            }
            yield return null;
        }

        yield return new WaitForSeconds(1.5f);          // 戻り際のこぼれが出きるまで
        float after = src != null ? src.FillFraction01 : -1f;
        loco.debugMoveForward = false;

        Result = string.Format("{0:F4},{1:F4},{2:F4},{3:F3},{4:F3},{5:F3},{6:F0}",
                               before, atLand, after, clipSecs, minScale, maxRise, stalls);
        Running = false;
    }
}
