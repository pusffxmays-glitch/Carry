using System.Reflection;
using UnityEditor;
using UnityEngine;

// 歩行の **合成結果** をコマ送りの PNG にするエディタ用ハーネス (2026-08-24)。
//
// なぜ必要か: Blender でレンダリングした絵は「クリップ単体」でしかない。ゲーム内の見え方は
// ApplyBasePose (全 25 ボーンを上書き) → 歩行クリップ → よろけ → 接地補正 → 腕 IK → 壺配置
// の合成結果で、クリップだけ見ても良し悪しが判断できない。
//
// Play mode に **入らない** のが要点。入ると流体シミュレーションが起動して GPU に負荷が
// かかるが、姿勢パイプラインは Time.deltaTime に依存しないので、エディタ上で walkPhase を
// 直接与えて LateUpdate を呼べば同じ絵が出る (エディタでは deltaTime が 0 なので、
// walkIntensity / walkPhase はこちらが入れた値のまま保たれる)。
public static class CarryWalkPreview
{
    const int W = 420, H = 700;

    [MenuItem("Carry/歩行プレビューを撮る")]
    static void Menu() { Debug.Log(Capture(8, "Temp/WalkPreview", 0f, 1f)); }

    [MenuItem("Carry/ジャンププレビューを撮る (静止)")]
    static void MenuJump() { Debug.Log(CaptureJump(10, "Temp/JumpPreview", 0f, false)); }

    [MenuItem("Carry/ジャンププレビューを撮る (歩行から)")]
    static void MenuJumpRun() { Debug.Log(CaptureJump(10, "Temp/JumpPreviewRun", 0f, true)); }

    /// <summary>ジャンプの姿勢軸を「溜め → 踏切 → 滞空 → 着地」の順に送ってコマ撮りする。
    /// 実際の滞空時間はジャンプの高さで変わるので、ここでは姿勢の並びだけを見る。</summary>
    public static string CaptureJump(int cols, string dir, float yawDeg, bool moving)
    {
        var rig = Object.FindFirstObjectByType<GoblinCarryRig>();
        if (rig == null) return "GoblinCarryRig が見つからない";
        var T = typeof(GoblinCarryRig);
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;
        var fU = T.GetField("jumpU", BF);
        var fW = T.GetField("jumpWeight", BF);
        var fSet = T.GetField("jumpSet", BF);
        if (fU == null || fW == null || fSet == null) return "jumpU / jumpWeight / jumpSet が取れない";
        IGoblinJumpPoses set = moving ? (IGoblinJumpPoses)GoblinJumpRun.I : GoblinJumpStand.I;
        fSet.SetValue(rig, set);

        // 溜め(1) → 踏切(UExtend) → 滞空(UAir) → 着地(1) を等分で辿る
        var keys = new float[] { set.UCrouch, set.UExtend, set.UAir, set.ULand };
        var us = new float[cols];
        for (int i = 0; i < cols; i++)
        {
            float x = i / (float)(cols - 1) * (keys.Length - 1);
            int k = Mathf.Clamp(Mathf.FloorToInt(x), 0, keys.Length - 2);
            us[i] = Mathf.Lerp(keys[k], keys[k + 1], x - k);
        }
        // 歩行のブレンドは切る。残っているとジャンプ姿勢と混ざり、どちらの姿勢を見ているのか
        // 分からなくなる (実測: 左右対称なはずの静止跳びが歩行の開脚に見えた)。
        var fInt = T.GetField("walkIntensity", BF);
        return CaptureSequence(rig, dir, yawDeg, cols,
            i => { fInt.SetValue(rig, 0f); fU.SetValue(rig, us[i]); fW.SetValue(rig, 1f); },
            () => { fInt.SetValue(rig, 0f); fW.SetValue(rig, 0f); });
    }

    [MenuItem("Carry/歩行→ジャンプ→歩行のつなぎを撮る")]
    static void MenuTrans() { Debug.Log(CaptureTransition("Temp/JumpTransition", 0f, true)); }

    /// <summary>歩行 → 溜め → 踏切 → 滞空 → 着地 → 歩行復帰 を通しでコマ撮りする。
    /// ジャンプ姿勢は歩行の上に weight でブレンドされるので、**つなぎ目で飛ばないか**
    /// を見るにはこの合成を通しで並べるしかない (各段を単独で見ても分からない)。</summary>
    public static string CaptureTransition(string dir, float yawDeg, bool moving)
    {
        var rig = Object.FindFirstObjectByType<GoblinCarryRig>();
        if (rig == null) return "GoblinCarryRig が見つからない";
        var T = typeof(GoblinCarryRig);
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;
        var fPhase = T.GetField("walkPhase", BF);
        var fInt = T.GetField("walkIntensity", BF);
        var fU = T.GetField("jumpU", BF);
        var fW = T.GetField("jumpWeight", BF);
        if (fPhase == null || fInt == null || fU == null || fW == null) return "フィールドが取れない";

        var fSet = T.GetField("jumpSet", BF);
        if (fSet != null) fSet.SetValue(rig, moving ? (IGoblinJumpPoses)GoblinJumpRun.I : GoblinJumpStand.I);
        float[] uu, ww, pp;
        int COLS = 24;
        TransitionSchedule(COLS, moving, out uu, out ww, out pp);
        return CaptureSequence(rig, dir, yawDeg, COLS,
            i => { fInt.SetValue(rig, 1f); fPhase.SetValue(rig, pp[i]); fU.SetValue(rig, uu[i]); fW.SetValue(rig, ww[i]); },
            () => { fInt.SetValue(rig, 0f); fW.SetValue(rig, 0f); });
    }

    // 歩行→ジャンプ→歩行を実時間で並べた台本。ApplyJumpPose の各段の秒数と、
    // 「滞空中も walkPhase は進み続ける」という実装をそのまま写している。
    static void TransitionSchedule(int cols, bool moving, out float[] uu, out float[] ww, out float[] pp)
    {
        IGoblinJumpPoses set = moving ? (IGoblinJumpPoses)GoblinJumpRun.I : GoblinJumpStand.I;
        // 秒数はリグの実値を読む。ここに数字を書き写すと、インスペクタで調整したときに
        // 「プレビューでは滑らかなのに実機では飛ぶ」というずれ方をする。
        var rig = Object.FindFirstObjectByType<GoblinCarryRig>();
        var loco = rig != null ? rig.GetComponent<GoblinLocomotion>() : null;
        // 溜めは静止/移動で長さが違う。CurrentJumpAnticipation は実行時の IsMoving を
        // 見るのでエディタでは使えず、ここでは moving から直接選ぶ。
        float cd = loco == null ? 0.12f : (moving ? loco.jumpAnticipationMoving : loco.jumpAnticipation);
        float td = rig != null ? rig.jumpTakeoffTime : 0.09f;
        float ad = 0.45f;
        float ld = rig != null ? rig.jumpLandTime : 0.09f;
        float rd = rig != null ? rig.jumpRecoverTime : 0.26f;
        float airBlend = rig != null ? rig.jumpAirTime : 0.18f;
        float total = 0.30f + cd + td + ad + ld + rd + 0.30f;
        float walkCycle = 0.70f;     // 1.53m / 1.5m/s ≒ 1.02s。速歩相当で厳しめに見る
        uu = new float[cols]; ww = new float[cols]; pp = new float[cols];
        System.Func<float, float> ease = k => { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); };
        for (int i = 0; i < cols; i++)
        {
            float t = i / (float)(cols - 1) * total;
            pp[i] = Mathf.Repeat(t / walkCycle, 1f);
            float s0 = 0.30f, s1 = s0 + cd, s2 = s1 + td, s3 = s2 + ad, s4 = s3 + ld, s5 = s4 + rd;
            if (t < s0) { uu[i] = set.UExtend; ww[i] = 0f; }
            else if (t < s1)
            {
                uu[i] = Mathf.Lerp(set.UExtend, set.UCrouch, ease((t - s0) / cd));
                ww[i] = Mathf.Clamp01((t - s0) / cd);
            }
            else if (t < s2) { uu[i] = Mathf.Lerp(set.UCrouch, set.UExtend, ease((t - s1) / td)); ww[i] = 1f; }
            else if (t < s3) { uu[i] = Mathf.Lerp(set.UExtend, set.UAir, ease((t - s2) / airBlend)); ww[i] = 1f; }
            else if (t < s4) { uu[i] = Mathf.Lerp(set.UAir, set.ULand, ease((t - s3) / ld)); ww[i] = 1f; }
            else if (t < s5) { uu[i] = set.ULand; ww[i] = 1f - ease((t - s4) / rd); }
            else { uu[i] = set.ULand; ww[i] = 0f; }
        }
    }

    /// <summary>つなぎ目で姿勢が飛んでいないかを数値で見る。60fps 相当で刻み、1 コマあたりの
    /// 骨の角度変化と腰の移動量を出す。歩行だけの区間の値と比べて突出していなければ滑らか。</summary>
    public static string MeasureTransition(bool moving)
    {
        var rig = Object.FindFirstObjectByType<GoblinCarryRig>();
        if (rig == null) return "GoblinCarryRig が見つからない";
        var T = typeof(GoblinCarryRig);
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;
        var awake = T.GetMethod("Awake", BF); var late = T.GetMethod("LateUpdate", BF);
        var fPhase = T.GetField("walkPhase", BF); var fInt = T.GetField("walkIntensity", BF);
        var fU = T.GetField("jumpU", BF); var fW = T.GetField("jumpWeight", BF);
        awake.Invoke(rig, null);
        rig.previewLock = true;
        // よろけ (ApplyStagger) は壺の傾きに反応し、その壺は Time.deltaTime で平滑化されている。
        // エディタでは deltaTime が毎回違うので、切らずに測ると値がランごとにばらつく
        // (実測: 同じ台本で最大 32 度と 48 度)。つなぎ目そのものを見たいので止める。
        float savedThreshold = rig.staggerThresholdDeg;
        rig.staggerThresholdDeg = 9999f;

        var loco0 = rig.GetComponent<GoblinLocomotion>();
        float total = 0.30f + (loco0 == null ? 0.12f : (moving ? loco0.jumpAnticipationMoving : loco0.jumpAnticipation))
                    + rig.jumpTakeoffTime + 0.45f + rig.jumpLandTime + rig.jumpRecoverTime + 0.30f;
        int cols = Mathf.RoundToInt(total * 60f);
        var fSet2 = T.GetField("jumpSet", BF);
        if (fSet2 != null) fSet2.SetValue(rig, moving ? (IGoblinJumpPoses)GoblinJumpRun.I : GoblinJumpStand.I);
        float[] uu, ww, pp; TransitionSchedule(cols, moving, out uu, out ww, out pp);

        var names = new string[] { "Hips", "RightUpLeg", "RightLeg", "RightFoot", "Spine02", "Head" };
        var bones = new Transform[names.Length];
        foreach (var tr in rig.GetComponentsInChildren<Transform>(true))
            for (int k = 0; k < names.Length; k++) if (tr.name == names[k] && bones[k] == null) bones[k] = tr;

        var prev = new Quaternion[names.Length];
        Vector3 prevHips = Vector3.zero;
        var sb = new System.Text.StringBuilder();
        float walkOnlyMax = 0f, transMax = 0f; int transAt = -1;
        try
        {
            for (int i = 0; i < cols; i++)
            {
                fInt.SetValue(rig, 1f); fPhase.SetValue(rig, pp[i]);
                fU.SetValue(rig, uu[i]); fW.SetValue(rig, ww[i]);
                late.Invoke(rig, null);
                float worst = 0f;
                for (int k = 0; k < names.Length; k++)
                {
                    if (bones[k] == null) continue;
                    if (i > 0) worst = Mathf.Max(worst, Quaternion.Angle(prev[k], bones[k].rotation));
                    prev[k] = bones[k].rotation;
                }
                Vector3 h = rig.transform.InverseTransformPoint(bones[0].position);
                float dh = i > 0 ? (h - prevHips).magnitude : 0f;
                prevHips = h;
                float t = i / (float)(cols - 1) * total;
                bool walkOnly = t < 0.30f || t > total - 0.30f;
                if (i > 0)
                {
                    if (walkOnly) walkOnlyMax = Mathf.Max(walkOnlyMax, worst);
                    else if (worst > transMax) { transMax = worst; transAt = i; }
                }
                if (i > 0 && (worst > 12f || dh > 0.05f))
                    sb.Append("t=").Append(t.ToString("F2")).Append(" 角度差").Append(worst.ToString("F1"))
                      .Append("度 腰移動").Append((dh * 100f).ToString("F1")).Append("cm / ");
            }
        }
        finally
        {
            rig.previewLock = false;
            rig.staggerThresholdDeg = savedThreshold;
            fInt.SetValue(rig, 0f); fW.SetValue(rig, 0f);
            late.Invoke(rig, null);
        }
        return "60fps 相当 " + cols + " コマ / 歩行だけの区間の最大 " + walkOnlyMax.ToString("F1")
             + "度 / つなぎ区間の最大 " + transMax.ToString("F1") + "度 (t="
             + (transAt / (float)(cols - 1) * total).ToString("F2") + "秒) / 大きい所: "
             + (sb.Length == 0 ? "なし" : sb.ToString());
    }

    [MenuItem("Carry/よろけの段階を撮る (背面)")]
    static void MenuStaggerBack()
    {
        Debug.Log(CaptureStaggerLadder("Temp/StaggerBack", 180f));
    }

    /// <summary>壺の傾きを段階的に変えながら、各段階の歩行を 1 周期ぶん撮る。
    /// 傾き量 -> よろけ強度は Update ではなく LateUpdate の中で決まるので、
    /// 各段階の頭で LateUpdate を空回しして収束させてから撮る。</summary>
    public static string CaptureStaggerLadder(string dir, float yawDeg)
    {
        float[] bal = { 0f, -0.5f, -0.75f, -1f, 0.5f, 0.75f, 1f };
        var sb = new System.Text.StringBuilder();
        foreach (float b in bal)
        {
            string sub = string.Format("{0}/ab{1}{2:00}", dir, b < 0 ? "L" : "R", Mathf.RoundToInt(Mathf.Abs(b) * 100));
            sb.AppendLine(CaptureStagger(sub, yawDeg, 8, b));
        }
        return sb.ToString();
    }

    /// <summary>armBalance を b に固定したまま、歩行 1 周期を cols コマ撮る。</summary>
    public static int settleSpins = 90;

    public static string CaptureStagger(string dir, float yawDeg, int cols, float b)
    {
        var rig = Object.FindFirstObjectByType<GoblinCarryRig>();
        if (rig == null) return "GoblinCarryRig が見つからない";
        var T = typeof(GoblinCarryRig);
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;
        var fPhase = T.GetField("walkPhase", BF);
        var fInt = T.GetField("walkIntensity", BF);
        var fApplied = T.GetField("appliedArmBalance", BF);
        var late = T.GetMethod("LateUpdate", BF);
        if (fPhase == null || fInt == null || fApplied == null || late == null)
            return "walkPhase / walkIntensity / appliedArmBalance / LateUpdate が取れない";

        float savedBal = rig.armBalance;
        keepStagger = true;
        string res;
        try
        {
            res = CaptureSequence(rig, dir, yawDeg, cols,
                i =>
                {
                    fInt.SetValue(rig, 1f);
                    fPhase.SetValue(rig, i / (float)cols);
                    rig.armBalance = b;
                    fApplied.SetValue(rig, b);
                    // 壺の姿勢とよろけ強度は LateUpdate の中で少しずつ動く。
                    // 撮る前に空回しして、その傾きでの定常状態にしておく。
                    if (i == 0) for (int k = 0; k < settleSpins; k++) late.Invoke(rig, null);
                },
                () => { fInt.SetValue(rig, 0f); rig.armBalance = savedBal; fApplied.SetValue(rig, savedBal); });
        }
        finally { keepStagger = false; rig.armBalance = savedBal; }
        return dir + ": " + res;
    }

    /// <summary>cols コマ撮って dir に PNG を書く。yawDeg はカメラの回り込み角 (0=真横)、
    /// intensity は歩行のブレンド量 (0 にすると素の立ちポーズが撮れる = 比較用)。</summary>
    public static string Capture(int cols, string dir, float yawDeg, float intensity)
    {
        var rig = Object.FindFirstObjectByType<GoblinCarryRig>();
        if (rig == null) return "GoblinCarryRig が見つからない";
        var T = typeof(GoblinCarryRig);
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;
        var fPhase = T.GetField("walkPhase", BF);
        var fInt = T.GetField("walkIntensity", BF);
        if (fPhase == null || fInt == null) return "walkPhase / walkIntensity が取れない";
        return CaptureSequence(rig, dir, yawDeg, cols,
            i => { fInt.SetValue(rig, intensity); fPhase.SetValue(rig, i / (float)cols); },
            () => fInt.SetValue(rig, 0f));
    }

    [MenuItem("Carry/ジャンプの通しシートを撮る (静止)")]
    static void MenuSheetStand() { Debug.Log(CaptureJumpSheet("Temp/JumpSheetStand", false, 16, 180f, false)); }

    [MenuItem("Carry/ジャンプの通しシートを撮る (歩行から)")]
    static void MenuSheetRun() { Debug.Log(CaptureJumpSheet("Temp/JumpSheetRun", true, 16, 180f, false)); }

    /// <summary>踏切から着地までを実時間で等間隔に刻んで撮る。ApplyJumpPose の各段の秒数と、
    /// 実際の跳躍の弧 (初速と重力から計算した高さ) を再現するので、通しの絵として見られる。
    /// 戻り値に各コマの「時刻と段」が入るので、並べるときの見出しに使える。</summary>
    public static string CaptureJumpSheet(string dir, bool moving, int cols, float yawDeg, bool withArc)
    {
        var rig = Object.FindFirstObjectByType<GoblinCarryRig>();
        if (rig == null) return "GoblinCarryRig が見つからない";
        var loco = rig.GetComponent<GoblinLocomotion>();
        var T = typeof(GoblinCarryRig);
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;
        var fU = T.GetField("jumpU", BF);
        var fW = T.GetField("jumpWeight", BF);
        var fSet = T.GetField("jumpSet", BF);
        var fInt = T.GetField("walkIntensity", BF);
        if (fU == null || fW == null || fSet == null || fInt == null) return "フィールドが取れない";

        IGoblinJumpPoses set = moving ? (IGoblinJumpPoses)GoblinJumpRun.I : GoblinJumpStand.I;
        fSet.SetValue(rig, set);

        float g = loco != null ? -loco.gravity : 20f;
        float v0 = loco != null ? loco.jumpSpeed : 7f;
        float cd = loco == null ? 0.12f : (moving ? loco.jumpAnticipationMoving : loco.jumpAnticipation);
        float td = rig.jumpTakeoffTime, ld = rig.jumpLandTime, rd = rig.jumpRecoverTime;
        float flight = 2f * v0 / g;                 // 踏み切ってから着地するまで
        float t0 = 0f, t1 = cd, t2 = cd + flight, t3 = t2 + ld, total = t3 + rd;

        var us = new float[cols];
        var ws = new float[cols];
        var ys = new float[cols];
        var names = new string[cols];
        for (int i = 0; i < cols; i++)
        {
            float t = i / (float)(cols - 1) * total;
            float u, w, y;
            string nm;
            if (t < t1)                             // 溜め: まだ地面にいる
            {
                u = Mathf.Lerp(set.UExtend, set.UCrouch, Ease(t / Mathf.Max(0.01f, cd)));
                w = Mathf.Clamp01(t / Mathf.Max(0.02f, cd)); y = 0f; nm = "溜め";
            }
            else if (t < t2)                        // 踏切〜滞空: 弧を描いて上がる
            {
                float ft = t - t1;
                y = v0 * ft - 0.5f * g * ft * ft;
                if (ft < td) { u = Mathf.Lerp(set.UCrouch, set.UExtend, Ease(ft / Mathf.Max(0.01f, td))); nm = "踏切"; }
                else
                {
                    float k = Mathf.Max(Mathf.Clamp01((ft - td) / Mathf.Max(0.01f, rig.jumpAirTime)),
                                        Mathf.Clamp01((g * ft - v0) / 4f));
                    u = Mathf.Lerp(set.UExtend, set.UAir, Ease(k));
                    nm = (g * ft < v0) ? "上昇" : "落下";
                }
                w = 1f;
            }
            else if (t < t3) { u = Mathf.Lerp(set.UAir, set.ULand, Ease((t - t2) / Mathf.Max(0.01f, ld))); w = 1f; y = 0f; nm = "着地"; }
            else { u = set.ULand; w = 1f - Ease((t - t3) / Mathf.Max(0.01f, rd)); y = 0f; nm = "復帰"; }
            us[i] = u; ws[i] = w;
            // withArc=false のときは体を持ち上げない (枠が広がって姿勢が小さくなるため)。
            ys[i] = withArc ? Mathf.Max(0f, y) : 0f;
            names[i] = nm + " " + t.ToString("F2") + "s";
        }

        string res = CaptureSequence(rig, dir, yawDeg, cols,
            i => { fInt.SetValue(rig, 0f); fU.SetValue(rig, us[i]); fW.SetValue(rig, ws[i]); },
            () => { fInt.SetValue(rig, 0f); fW.SetValue(rig, 0f); },
            ys, withArc ? Mathf.Pow(v0, 2f) / (2f * g) : 0f);
        return res + " | " + string.Join(",", names);
    }

    static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    // 撮影の共通部分。setPose が i コマ目の姿勢を仕込み、reset が撮り終えたあとの後始末をする。
    static string CaptureSequence(GoblinCarryRig rig, string dir, float yawDeg, int cols,
        System.Action<int> setPose, System.Action reset)
    { return CaptureSequence(rig, dir, yawDeg, cols, setPose, reset, null, 0f); }

    // よろけを撮るときだけ true。既定では下の CaptureSequence がよろけを切って撮る
    // (エディタの deltaTime が毎回違うため、切らないとコマごとに姿勢が勝手に混ざる)。
    static bool keepStagger;

    // lift[i] を渡すと、そのコマだけ体を上へ持ち上げて撮る (ジャンプの弧の再現)。
    // headroom は枠に余分に確保する高さ。撮り終えたら位置は必ず元へ戻す。
    static string CaptureSequence(GoblinCarryRig rig, string dir, float yawDeg, int cols,
        System.Action<int> setPose, System.Action reset, float[] lift, float headroom)
    {
        // 森のステージでは体のすぐ横に木が生えていて、どの向きから撮っても幹が絵に入る。
        // 遮蔽物を消す方式は木の bounds 中心が遠くて判定に掛からず効かなかったので、
        // **撮影のあいだだけ上空へ退避させる**。位置は finally で必ず戻す。
        const float Escape = 28f;
        var T = typeof(GoblinCarryRig);
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;
        var awake = T.GetMethod("Awake", BF);
        var late = T.GetMethod("LateUpdate", BF);
        if (awake == null || late == null) return "Awake / LateUpdate が取れない";

        awake.Invoke(rig, null);            // エディタでは Awake が走っていないのでボーンを解決させる
        rig.previewLock = true;             // 位相・強度を時間で動かさない (下の finally で戻す)
        // よろけは壺の傾きに反応し、その壺は Time.deltaTime で平滑化されている。エディタでは
        // deltaTime が毎回違うので、切らないとコマごとに勝手によろけ姿勢が混ざる (実測)。
        float savedStagger = rig.staggerThresholdDeg;
        if (!keepStagger) rig.staggerThresholdDeg = 9999f;

        System.IO.Directory.CreateDirectory(dir);
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        var go = new GameObject("~WalkPreviewCam");
        var cam = go.AddComponent<Camera>();
        cam.targetTexture = rt;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.19f, 0.21f);
        cam.fieldOfView = 32f;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        Vector3 center = Vector3.zero;
        float frame = -1f;
        Vector3 rigHome = rig.transform.position;
        if (lift != null) rig.transform.position = rigHome + Vector3.up * Escape;

        // 森のステージでは体のすぐ横に大木が生えていて、背面から撮ると幹が全身を隠す。
        // 以前は「bounds.center が近い Renderer を消す」で書いていたが、幹の bounds 中心は
        // 木の高さの真ん中にあって体から遠く、判定に一度も掛からなかった。
        // bounds の **最近接点** で測れば大木でも掛かる。撮り終えたら finally で必ず戻す。
        var hidden = new System.Collections.Generic.List<Renderer>();

        try
        {
            // 枠は **素の立ち姿勢** を基準に決める。最初のコマを基準にすると、そのコマが
            // しゃがみだった場合にカメラが寄りすぎ、伸び上がったコマで体が枠やクリップ面から
            // はみ出して消える (実測)。
            reset();
            late.Invoke(rig, null);
            {
                {
                    // 壺は体より大きく、枠に入れると本体が小さくなって判断できない。
                    // 体 (SkinnedMeshRenderer) だけで枠を決め、頭上の壺は多少はみ出させる。
                    var rends = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
                    var b = rends[0].bounds;
                    for (int k = 1; k < rends.Length; k++) b.Encapsulate(rends[k].bounds);
                    center = b.center + Vector3.up * b.extents.y * 0.55f;
                    frame = b.extents.y * 9.0f;
                    // 背景も手前も切り落として、体のシルエットだけ見る。森のステージでは
                    // カメラと体の間に木が入ることがあるので、ニアクリップも体の直前に置く。
                    // ニアクリップは「体の手前ぎりぎり」に置く。枠取り距離の割合で決めると、
                    // 基準にしたコマが縮んだ姿勢 (しゃがみ) のときにカメラが寄りすぎ、
                    // 伸び上がったコマで体がクリップ面に刺さって消える (実測)。
                    float radius = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
                    cam.nearClipPlane = Mathf.Max(0.05f, frame - radius * 3f);
                    frame = frame;   // 以降のコマでも同じ枠を使う
                    cam.farClipPlane = frame + 2.2f;
                }
            }
            // 跳躍の弧を入れるぶん、枠を上へ広げる。
            center += Vector3.up * headroom * 0.5f;
            frame += headroom * 1.2f;
            {
                var mine = new System.Collections.Generic.HashSet<Renderer>(
                    rig.GetComponentsInChildren<Renderer>(true));
                foreach (var rd in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!rd.enabled || mine.Contains(rd)) continue;
                    if (rd.bounds.SqrDistance(center) > 12f * 12f) continue;
                    // 地面は残す (足の接地が見えなくなるため)。板状 = 高さがほぼ無いもの。
                    if (rd.bounds.extents.y < 0.3f && rd.bounds.extents.x > 3f) continue;
                    rd.enabled = false;
                    hidden.Add(rd);
                }
            }
            Quaternion q = rig.transform.rotation * Quaternion.Euler(0f, 90f + yawDeg, 0f);
            cam.transform.position = center + q * Vector3.forward * frame;
            cam.transform.rotation = Quaternion.LookRotation(center - cam.transform.position);
            cam.nearClipPlane = Mathf.Max(0.05f, cam.nearClipPlane);
            cam.farClipPlane = frame + 3.0f;

            for (int i = 0; i < cols; i++)
            {
                setPose(i);
                if (lift != null) rig.transform.position = rigHome + Vector3.up * (Escape + lift[i]);
                late.Invoke(rig, null);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                System.IO.File.WriteAllBytes(string.Format("{0}/w{1:00}.png", dir, i), tex.EncodeToPNG());
            }
        }
        finally
        {
            foreach (var rd in hidden) if (rd != null) rd.enabled = true;
            rig.previewLock = false;
            rig.staggerThresholdDeg = savedStagger;
            rig.transform.position = rigHome;
            reset();
            late.Invoke(rig, null);         // 立ちポーズへ戻してからシーンを離れる
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
        return string.Format("{0} に {1} コマ書いた (yaw {2}度)", dir, cols, yawDeg);
    }
}
