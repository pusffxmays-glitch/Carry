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

    // **FindFirstObjectByType<FluidCore>() を使ってはいけない。**
    // シーンには滝 (PotionWaterfallFluid、mode=Box、粒子 2400) と壺 (Carry_Pot、
    // mode=PotProfile、粒子 16384) の 2 つがあり、Find は滝を掴むことがある。
    // 実際 2026-08-26 まで、この計測はずっと滝の残量を読んでいた
    // (壺は inside=16384 / fill=1.000 なのに、読めていたのは滝の fill=0.854)。
    // ゲーム側 (GoblinPotActions) と同じく **子から探す**。
    FluidCore PotFluid()
    {
        var f = GetComponentInChildren<FluidCore>();
        return f != null ? f : FluidCore.FindPotFluid();
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
        var fluid = PotFluid();
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
        var src = PotFluid() as IPotionVolumeSource;

        loco.debugMoveForward = false;
        rig.armBalance = 0f;
        // 前回の走行で消費されずに残った予約を捨てる。残っていると次のジャンプの
        // 最初のフレームで消費され、1 滞空 1 回の制限に引っかかって本命の押しが無視される。
        var actsClear = GetComponent<GoblinPotActions>();
        if (actsClear != null) actsClear.debugParryRequest = false;
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

    // ================= 診断 (2026-08-25 ユーザー報告 3 点) =================
    // 1. 静態パリーの伸び上がりで左右差があってこぼれる
    // 2. 静態パリー後、歩き出しまでに遅延がある
    // 3. 歩行ジャンプパリーで着地モーションのまま前に滑る
    // どれも「クッションのワンショットが体を握っている間」に起きるので、
    // 着地からの 1 フレームごとに壺の傾き・両手の高さ差・足の滑りをまとめて記録する。
    public string Trace = "";
    /// <summary>歩行ジャンプの頂点で測った足元までの距離 (m)。押す高さの根拠。</summary>
    public float ApexGroundDistance;

    /// <summary>診断走行。dropHeight>0 = 静態 (真上から落とす)、0 = 歩行ジャンプ。
    /// moveAfterLand = 着地の瞬間に前進入力を入れる (歩き出し遅延の計測)。</summary>
    public void RunDiag(Vector3 home, float dropHeight, bool moveAfterLand)
    {
        if (Running) return;
        StopAllCoroutines();
        StartCoroutine(DiagSequence(home, dropHeight, moveAfterLand));
    }

    /// <summary>歩行 → 歩行ジャンプの移行を測る。足の開き (左右の足の横位置の差) と
    /// ジャンプ姿勢のブレンド量を 1 フレームずつ記録する。</summary>
    public void RunWalkJumpDiag(Vector3 home)
    {
        if (Running) return;
        StopAllCoroutines();
        StartCoroutine(WalkJumpSequence(home));
    }

    IEnumerator WalkJumpSequence(Vector3 home)
    {
        Running = true;
        Trace = "";
        var rig = GetComponent<GoblinCarryRig>();
        var loco = GetComponent<GoblinLocomotion>();
        var cc = GetComponent<CharacterController>();
        var footL = GoblinBoneUtil.FindDeep(rig.transform, "LeftFoot");
        var footR = GoblinBoneUtil.FindDeep(rig.transform, "RightFoot");
        var kneeL = GoblinBoneUtil.FindDeep(rig.transform, "LeftLeg");
        var kneeR = GoblinBoneUtil.FindDeep(rig.transform, "RightLeg");
        var hips  = GoblinBoneUtil.FindDeep(rig.transform, "Hips");
        loco.debugMoveForward = false;
        rig.armBalance = 0f;
        // 前回の走行で消費されずに残った予約を捨てる。残っていると次のジャンプの
        // 最初のフレームで消費され、1 滞空 1 回の制限に引っかかって本命の押しが無視される。
        var actsClear = GetComponent<GoblinPotActions>();
        if (actsClear != null) actsClear.debugParryRequest = false;
        cc.enabled = false; transform.position = home; cc.enabled = true;
        yield return new WaitForSeconds(2.0f);

        loco.debugMoveForward = true;
        yield return new WaitForSeconds(1.6f);      // 歩容が定常になるまで
        var sb = new System.Text.StringBuilder();
        float t = 0f;
        bool jumped = false;
        while (t < 2.2f)
        {
            float rdt = Time.unscaledDeltaTime;
            t += rdt;
            if (!jumped && t > 0.5f) { loco.debugJumpRequest = true; jumped = true; }
            Vector3 lf = rig.transform.InverseTransformPoint(footL.position);
            Vector3 rf = rig.transform.InverseTransformPoint(footR.position);
            Vector3 lk = rig.transform.InverseTransformPoint(kneeL.position);
            Vector3 rk = rig.transform.InverseTransformPoint(kneeR.position);
            Vector3 hp = rig.transform.InverseTransformPoint(hips.position);
            // 足の開き = 左右の足の横方向の距離 (cm)。前後の開きとは別物。
            sb.AppendFormat("{0:F3},{1:F2},{2:F2},{3:F2},{4:F2},{5:F3},{6:F2},{7},{8},{9:F2},{10:F2},{11:F3},{12:F2}|",
                t, (lf.x - rf.x) * 100f, (lk.x - rk.x) * 100f, (lf.z - rf.z) * 100f,
                hp.y * 100f, rig.JumpBlend01, rig.StaggerIntensity01,
                cc.isGrounded ? 1 : 0, rig.JumpPhaseName, loco.CurrentSpeed, rdt * 1000f,
                rig.LandRecoil * 100f,
                rig.transform.InverseTransformPoint(footL.position).y * 100f);
            yield return null;
        }
        loco.debugMoveForward = false;
        Trace = "walkjump" + System.Environment.NewLine + sb.ToString();
        Running = false;
    }

    /// <summary>一定時間まっすぐ歩く/走るだけ。移動そのもののこぼれ量を測る。</summary>
    public void RunSteady(Vector3 home, float seconds, bool running, float swayAmp = 0f, float swayHz = 1.2f)
    {
        if (Running) return;
        StopAllCoroutines();
        StartCoroutine(SteadySequence(home, seconds, running, swayAmp, swayHz));
    }

    IEnumerator SteadySequence(Vector3 home, float seconds, bool running, float swayAmp, float swayHz)
    {
        Running = true;
        Trace = "";
        var rig = GetComponent<GoblinCarryRig>();
        var loco = GetComponent<GoblinLocomotion>();
        var cc = GetComponent<CharacterController>();
        var src = PotFluid() as IPotionVolumeSource;
        loco.debugMoveForward = false; loco.debugRun = false;
        rig.armBalance = 0f; rig.pitchBalance = 0f;
        cc.enabled = false; transform.position = home; cc.enabled = true;
        yield return new WaitForSeconds(2.5f);
        float before = src != null ? src.FillFraction01 : -1f;
        Vector3 p0 = transform.position;

        loco.debugRun = running;
        loco.debugMoveForward = true;
        float t = 0f;
        float maxSpd = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            // 歩容由来の揺れを模す。歩幅の周期で壺を左右に振る。
            if (swayAmp > 0.0001f) rig.armBalance = Mathf.Sin(t * Mathf.PI * 2f * swayHz) * swayAmp;
            if (loco.CurrentSpeed > maxSpd) maxSpd = loco.CurrentSpeed;
            yield return null;
        }
        rig.armBalance = 0f;
        loco.debugMoveForward = false; loco.debugRun = false;
        yield return new WaitForSeconds(1.5f);
        float after = src != null ? src.FillFraction01 : -1f;
        float dist = Vector3.ProjectOnPlane(transform.position - p0, Vector3.up).magnitude;
        Trace = string.Format("steady run={0} sway={1:F2} before={2:F4} after={3:F4} loss={4:F1}% dist={5:F1}m maxSpeed={6:F2}",
                              running, swayAmp, before, after, (before - after) * 100f, dist, maxSpd);
        Running = false;
    }

    /// <summary>ジャンプ中に壺を振ってこぼしてから着地でパリーする。
    /// 青 (グッド) と金 (ジャスト) の回収量の違いを測るためのもの。</summary>
    public void RunSpillParry(Vector3 home, float shakeAmp)
    {
        if (Running) return;
        StopAllCoroutines();
        StartCoroutine(SpillParrySequence(home, shakeAmp));
    }

    IEnumerator SpillParrySequence(Vector3 home, float shakeAmp)
    {
        Running = true;
        Trace = "";
        var rig = GetComponent<GoblinCarryRig>();
        var loco = GetComponent<GoblinLocomotion>();
        var acts = GetComponent<GoblinPotActions>();
        var anim = GetComponent<GoblinClipAnimator>();
        var cc = GetComponent<CharacterController>();
        var tilt = GetComponent<GoblinTerrainTilt>();
        var fc = PotFluid();
        var src = fc as IPotionVolumeSource;
        loco.debugMoveForward = false; loco.debugRun = false;
        rig.armBalance = 0f; rig.pitchBalance = 0f;
        cc.enabled = false; transform.position = home; cc.enabled = true;
        yield return new WaitForSeconds(2.5f);
        float before = src != null ? src.FillFraction01 : -1f;

        loco.debugJumpRequest = true;
        float t = 0f;
        while (cc.isGrounded && t < 1.5f) { t += Time.deltaTime; yield return null; }

        // 滞空中に壺を振ってこぼす。balanceInertiaRate を超える速さで振らないと
        // calm が掛かったままで慣性が死ぬので、1 往復 0.25 秒で振る。
        float shakeT = 0f, maxGd = 0f, prevY = transform.position.y;
        bool pressed = false;
        t = 0f;
        while (t < 4f)
        {
            float dt = Time.deltaTime;
            t += dt; shakeT += dt;
            rig.armBalance = Mathf.Sin(shakeT * Mathf.PI * 8f) * shakeAmp;
            float gd = tilt != null ? tilt.GroundDistance : (cc.isGrounded ? 0f : 9f);
            if (gd > maxGd) maxGd = gd;
            bool descending = transform.position.y < prevY - 0.001f;
            prevY = transform.position.y;
            if (!pressed && t > 0.40f && descending && maxGd > 0.35f
                && gd < Mathf.Max(0.22f, maxGd * 0.45f))
            { acts.debugParryRequest = true; pressed = true; }
            if (pressed && cc.isGrounded && t > 0.5f) break;
            yield return null;
        }
        rig.armBalance = 0f;
        float atLand = src != null ? src.FillFraction01 : -1f;
        int escAtLand = fc != null ? fc.EscapedCount : -1;

        // 回収 (RecallSpill) が効き切るまで待つ
        yield return new WaitForSeconds(2.5f);
        float after = src != null ? src.FillFraction01 : -1f;
        bool cushion = anim != null && anim.CurrentOneShot != null;
        Trace = string.Format("spillparry before={0:F4} atLand={1:F4} after={2:F4} 回収={3:F1}% escaped={4} pressed={5} clip={6}",
                              before, atLand, after, (after - atLand) * 100f, escAtLand, pressed, cushion);
        Running = false;
    }

    /// <summary>パリー成功が状況ごとに本当に回収できているかを測る。
    /// mode 0 = 大きく揺らしてから (こぼしそうな状態でのパリー)
    /// mode 1 = よろけた状態からのジャンプ
    /// mode 2 = 走りジャンプ</summary>
    public void RunParryCase(Vector3 home, int mode)
    {
        if (Running) return;
        StopAllCoroutines();
        StartCoroutine(ParryCaseSequence(home, mode));
    }

    IEnumerator ParryCaseSequence(Vector3 home, int mode)
    {
        Running = true;
        Trace = "";
        var rig = GetComponent<GoblinCarryRig>();
        var loco = GetComponent<GoblinLocomotion>();
        var acts = GetComponent<GoblinPotActions>();
        var cc = GetComponent<CharacterController>();
        var tilt = GetComponent<GoblinTerrainTilt>();
        var fc = PotFluid();
        var src = fc as IPotionVolumeSource;
        loco.debugMoveForward = false; loco.debugRun = false;
        acts.debugParryRequest = false;
        acts.LastParryResult = "";
        rig.armBalance = 0f; rig.pitchBalance = 0f;
        cc.enabled = false; transform.position = home; cc.enabled = true;
        yield return new WaitForSeconds(2.5f);

        if (mode == 1)
        {
            // よろけさせてから跳ぶ。外乱は入力とは別系統 (armBalance はマウスに上書きされる)。
            rig.DisturbPotOutward(14f);
            yield return new WaitForSeconds(0.35f);
        }
        if (mode == 2)
        {
            loco.debugRun = true; loco.debugMoveForward = true;
            yield return new WaitForSeconds(1.8f);
        }
        int insideBefore = fc != null ? fc.InsideCount : -1;

        loco.debugJumpRequest = true;
        float t = 0f;
        bool airborneSeen = false, armed = false;
        float shakeT = 0f;
        while (t < 4f)
        {
            float dt = Time.deltaTime;
            t += dt; shakeT += dt;
            float gd = tilt != null ? tilt.GroundDistance : (cc.isGrounded ? 0f : 9f);
            if (gd > 0.70f) airborneSeen = true;
            // mode 0: 滞空中ずっと壺を揺らして「こぼしそう」を作る
            if (mode == 0 && airborneSeen && shakeT > 0.09f)
            { rig.DisturbPot(Mathf.Sin(t * 22f) * 9f); shakeT = 0f; }
            // **接地中も VerticalVelocity は -1**。滞空を見てから押すこと。
            // しきい値 0.42m は低すぎた。20fps では 1 フレーム 0.2m 落ちるので、
            // 予約が消費される前に接地して丸ごと無視されていた (判定=none が 9/9)。
            // 0.75m なら SoftenLanding (3.5 m/s) で着地まで 0.21 秒 = グッドの窓の内側。
            if (airborneSeen && loco.VerticalVelocity < -0.2f && gd < 0.75f && !cc.isGrounded)
            { acts.debugParryRequest = true; armed = true; }
            if (armed && cc.isGrounded && t > 0.3f) break;
            yield return null;
        }
        loco.debugMoveForward = false; loco.debugRun = false;
        // **着地の瞬間** の内訳。ここから後の増分が「飛び出した分の巻き戻し回収」。
        int insideAtLand = fc != null ? fc.InsideCount : -1;
        int airAtLand = fc != null ? fc.AirborneCount : -1;
        int escAtLand = fc != null ? fc.EscapedCount : -1;
        int groundAtLand = fc != null ? fc.GroundCount : -1;

        yield return new WaitForSeconds(3.0f);   // 回収 (RecallSpill) と沈静を待つ
        int insideAfter = fc != null ? fc.InsideCount : -1;
        int groundAfter = fc != null ? fc.GroundCount : -1;
        Trace = string.Format(
            "mode={0} 判定={1} 壺内 {2}→{3}→{4} | 踏切〜着地の損失 {5} | 着地後の回収 {6:+0;-0} | 着地時の空中 {7} 地面 {8}→{9}",
            mode, acts.LastParryResult, insideBefore, insideAtLand, insideAfter,
            insideAtLand - insideBefore, insideAfter - insideAtLand,
            airAtLand, groundAtLand, groundAfter) + string.Format(" | 脱出判定 {0}", escAtLand);
        Running = false;
    }

    /// <summary>比較対照: パリーを挟まずに、その場から歩き出すだけ。
    /// 「歩き出しの遅延」がパリー由来なのか、もともとの加速ランプなのかを分ける。</summary>
    public void RunWalkStart(Vector3 home)
    {
        if (Running) return;
        StopAllCoroutines();
        StartCoroutine(WalkStartSequence(home));
    }

    IEnumerator WalkStartSequence(Vector3 home)
    {
        Running = true;
        Trace = "";
        var loco = GetComponent<GoblinLocomotion>();
        var anim = GetComponent<GoblinClipAnimator>();
        var cc = GetComponent<CharacterController>();
        var rig = GetComponent<GoblinCarryRig>();
        loco.debugMoveForward = false;
        rig.armBalance = 0f;
        // 前回の走行で消費されずに残った予約を捨てる。残っていると次のジャンプの
        // 最初のフレームで消費され、1 滞空 1 回の制限に引っかかって本命の押しが無視される。
        var actsClear = GetComponent<GoblinPotActions>();
        if (actsClear != null) actsClear.debugParryRequest = false;
        cc.enabled = false; transform.position = home; cc.enabled = true;
        yield return new WaitForSeconds(2.5f);

        var sb = new System.Text.StringBuilder();
        Vector3 prevRoot = transform.position;
        loco.debugMoveForward = true;
        float t = 0f;
        while (t < 2.5f)
        {
            float rdt = Time.unscaledDeltaTime;
            t += rdt;
            float move = Vector3.ProjectOnPlane(transform.position - prevRoot, Vector3.up).magnitude * 100f;
            prevRoot = transform.position;
            var acts = GetComponent<GoblinPotActions>();
            sb.AppendFormat("{0:F3},{1},0,0,0,0,0,{2:F2},{3:F2},0,{4},{5},{6:F2}|",
                            t, anim != null && anim.OneShotActive ? 1 : 0, move, loco.CurrentSpeed,
                            (int)acts.Current, loco.movementLocked ? 1 : 0, rig.StaggerIntensity01);
            yield return null;
        }
        loco.debugMoveForward = false;
        Trace = "walkstart" + System.Environment.NewLine + sb.ToString();
        Running = false;
    }

    IEnumerator DiagSequence(Vector3 home, float dropHeight, bool moveAfterLand)
    {
        Running = true;
        Trace = "";
        var rig = GetComponent<GoblinCarryRig>();
        var loco = GetComponent<GoblinLocomotion>();
        var acts = GetComponent<GoblinPotActions>();
        var anim = GetComponent<GoblinClipAnimator>();
        var cc = GetComponent<CharacterController>();
        var src = PotFluid() as IPotionVolumeSource;
        var potT = rig.transform.Find("Carry_Pot");
        var handL = GoblinBoneUtil.FindDeep(rig.transform, "LeftHand");
        var handR = GoblinBoneUtil.FindDeep(rig.transform, "RightHand");
        var footL = GoblinBoneUtil.FindDeep(rig.transform, "LeftFoot");
        var footR = GoblinBoneUtil.FindDeep(rig.transform, "RightFoot");

        loco.debugMoveForward = false;
        rig.armBalance = 0f;
        // 前回の走行で消費されずに残った予約を捨てる。残っていると次のジャンプの
        // 最初のフレームで消費され、1 滞空 1 回の制限に引っかかって本命の押しが無視される。
        var actsClear = GetComponent<GoblinPotActions>();
        if (actsClear != null) actsClear.debugParryRequest = false;
        cc.enabled = false; transform.position = home; cc.enabled = true;
        yield return new WaitForSeconds(2.5f);
        float before = src != null ? src.FillFraction01 : -1f;

        float t = 0f;
        if (dropHeight > 0.01f)
        {
            cc.enabled = false; transform.position = home + Vector3.up * dropHeight; cc.enabled = true;
        }
        else if (dropHeight < -0.01f)
        {
            // 立ちジャンプ (本当の「静態パリー」)。テレポート落下だと落下速度が
            // 本番と違いすぎるので、素直に跳ばせる。
            loco.debugJumpRequest = true;
        }
        else
        {
            loco.debugMoveForward = true;
            yield return new WaitForSeconds(1.2f);
            loco.debugJumpRequest = true;
        }
        while (cc.isGrounded && t < 1.5f) { t += Time.deltaTime; yield return null; }
        t = 0f;
        if (dropHeight > 0.01f)
        {
            while (transform.position.y - home.y > 0.75f && t < 4f) { t += Time.deltaTime; yield return null; }
        }
        else
        {
            // 歩行ジャンプは頂点が 0.75m に届かないので、静態と同じ判定高度だと
            // 「押すのが早すぎ」で毎回不成立になる (実測: 滞空 0.03s で押していた)。
            // home.y も使えない。前進しているぶん地面の高さが違うため、離陸前から
            // 条件を満たしてしまう。足元レイの実測 (GroundDistance) を見る。
            // 跳ぶ高さを決め打ちできない (歩行ジャンプの頂点は実測しないと分からず、
            // 0.30m 固定だと離陸直後に条件を満たして「滞空 0.03s で押す」になっていた)。
            // 足元レイの最大値を頂点として覚え、そこから 40% まで落ちたら押す。
            var tilt = GetComponent<GoblinTerrainTilt>();
            float maxGd = 0f, air = 0f, prevY = transform.position.y;
            while (t < 4f)
            {
                t += Time.deltaTime; air += Time.deltaTime;
                float gd = tilt != null ? tilt.GroundDistance : (cc.isGrounded ? 0f : 9f);
                if (gd > maxGd) maxGd = gd;
                bool descending = transform.position.y < prevY - 0.001f;
                prevY = transform.position.y;
                // 頂点を過ぎて、地面まで頂点の 40% (最低 0.20m) を切ったら押す。
                // 0.15 秒だと離陸直後の足元レイのばらつきで「滞空 0.03〜0.10 秒で押す」が
                // 起き、5 回中 4 回が「早すぎ」で不成立になっていた。成功した回の押下は
                // どれも滞空 0.76 秒以降なので、頂点を過ぎたことをはっきり確かめてから押す。
                if (air > 0.25f && descending && maxGd > 0.35f
                    && gd < Mathf.Max(0.30f, maxGd * 0.5f)) break;
                yield return null;
            }
            ApexGroundDistance = maxGd;
            // 予約が 1 フレームで消費されないことがある (低フレームレートだと、条件を
            // 満たした次のフレームにはもう接地している)。接地するまで毎フレーム立て直す。
            // ゲーム側が 1 滞空 1 回に制限しているので、実際に押されるのは最初の 1 回だけ。
            while (!cc.isGrounded && t < 4f)
            {
                acts.debugParryRequest = true;
                t += Time.deltaTime;
                yield return null;
            }
        }
        acts.debugParryRequest = true;
        t = 0f;
        while (!cc.isGrounded && t < 3f) { t += Time.deltaTime; yield return null; }

        // ---- 着地。ここから 1 フレームごとに記録する ----
        if (moveAfterLand) loco.debugMoveForward = true;
        var sb = new System.Text.StringBuilder();
        Vector3 land = transform.position;
        Vector3 prevFootL = footL != null ? footL.position : Vector3.zero;
        Vector3 prevFootR = footR != null ? footR.position : Vector3.zero;
        Vector3 prevRoot = land;
        t = 0f;
        while (t < 3f)
        {
            float rdt = Time.unscaledDeltaTime;
            t += rdt;
            // 壺のロール (root の前方まわり) とピッチ。左右差はロールに出る。
            Vector3 fwd = rig.transform.forward, up = rig.transform.up, right = rig.transform.right;
            Vector3 potUp = potT != null ? potT.up : Vector3.up;
            float roll = Mathf.Atan2(Vector3.Dot(potUp, right), Vector3.Dot(potUp, up)) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(Vector3.Dot(potUp, fwd), Vector3.Dot(potUp, up)) * Mathf.Rad2Deg;
            // 両手の高さ差 (cm)。担ぎ姿勢の左右非対称がそのまま壺のロールになる。
            float handDy = (handL != null && handR != null) ? (handL.position.y - handR.position.y) * 100f : 0f;
            // 左右差は高さだけとは限らない。root ローカルで見て、
            // latSum = 左右の横位置の和 (対称なら 0)、fwdDiff = 前後の差 (対称なら 0)。
            float latSum = 0f, fwdDiff = 0f;
            if (handL != null && handR != null)
            {
                Vector3 lL = rig.transform.InverseTransformPoint(handL.position);
                Vector3 lR = rig.transform.InverseTransformPoint(handR.position);
                latSum = (lL.x + lR.x) * 100f;
                fwdDiff = (lL.z - lR.z) * 100f;
            }
            // 足の滑り: 接地している足がワールドで動いた量 (cm/frame)。
            float slipL = 0f, slipR = 0f;
            if (footL != null) { slipL = Vector3.ProjectOnPlane(footL.position - prevFootL, Vector3.up).magnitude * 100f; prevFootL = footL.position; }
            if (footR != null) { slipR = Vector3.ProjectOnPlane(footR.position - prevFootR, Vector3.up).magnitude * 100f; prevFootR = footR.position; }
            float move = Vector3.ProjectOnPlane(transform.position - prevRoot, Vector3.up).magnitude * 100f;
            prevRoot = transform.position;
            var fc = src as FluidCore;
            sb.AppendFormat("{0:F3},{1},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2},{9:F4},{10},{11},{12},{13},{14},{15},{16},{17:F2},{18:F2},{19:F2}|",
                            t, anim != null && anim.OneShotActive ? 1 : 0,
                            roll, pitch, handDy, slipL, slipR, move, loco.CurrentSpeed,
                            src != null ? src.FillFraction01 : -1f,
                            fc != null ? fc.InsideCount : -1,
                            fc != null ? fc.AirborneCount : -1,
                            fc != null ? fc.EscapedCount : -1,
                            (int)acts.Current, loco.movementLocked ? 1 : 0,
                            loco.InJumpState ? 1 : 0, loco.JumpLockActive ? 1 : 0,
                            rig.StaggerIntensity01, latSum, fwdDiff);
            yield return null;
        }
        loco.debugMoveForward = false;
        yield return new WaitForSeconds(1.5f);
        float after = src != null ? src.FillFraction01 : -1f;
        Trace = string.Format("before={0:F4} after={1:F4}\n", before, after) + sb.ToString();
        Running = false;
    }
}
