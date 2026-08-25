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
        var src = FindFirstObjectByType<FluidCore>() as IPotionVolumeSource;
        var potT = rig.transform.Find("Carry_Pot");
        var handL = GoblinBoneUtil.FindDeep(rig.transform, "LeftHand");
        var handR = GoblinBoneUtil.FindDeep(rig.transform, "RightHand");
        var footL = GoblinBoneUtil.FindDeep(rig.transform, "LeftFoot");
        var footR = GoblinBoneUtil.FindDeep(rig.transform, "RightFoot");

        loco.debugMoveForward = false;
        rig.armBalance = 0f;
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
                if (air > 0.40f && descending && maxGd > 0.35f
                    && gd < Mathf.Max(0.22f, maxGd * 0.45f)) break;
                yield return null;
            }
            ApexGroundDistance = maxGd;
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
