using UnityEngine;

// ジャンプ姿勢セット。GoblinCarryRig.ApplyJumpPose が IGoblinJumpPoses 越しに引く。
// 生成元: Skill_01 フレーム 1〜16 -- bake_jump_cs.py (2026-08-24)
//
// 手・肘・指は入っていない: そこは SolveArm の IK が壺の位置から解く。
// ループしないので端はクランプする (歩行の Repeat と違う点)。
public sealed class GoblinJumpStand : IGoblinJumpPoses
{
    public static readonly GoblinJumpStand I = new GoblinJumpStand();
    GoblinJumpStand() { }

    public const int FrameCount = 31;
    public const float GroundY = -0.00203f;

    public float UCrouch { get { return 1.0000f; } }
    public float UExtend { get { return 0.6000f; } }
    public float UAir    { get { return 0.8000f; } }
    public float ULand   { get { return 1.0000f; } }
    /// <summary>踏切の瞬間に接地している側。true = リグの leftFootBone 側。</summary>
    public bool SupportIsLeftSide { get { return false; } }

    public Vector3 SampleHipsPos(float u)
    {
        Vector3 p = SamplePos(HipsPos, u);
        p.y -= GroundY;
        return p;
    }

    public void SampleHips(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(HipsYDir, u); xDir = Sample(HipsXDir, u); }
    public void SampleLeftUpLeg(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftUpLegYDir, u); xDir = Sample(LegLeftUpLegXDir, u); }
    public void SampleLeftLeg(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftLegYDir, u); xDir = Sample(LegLeftLegXDir, u); }
    public void SampleLeftFoot(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftFootYDir, u); xDir = Sample(LegLeftFootXDir, u); }
    public void SampleLeftToe(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftToeYDir, u); xDir = Sample(LegLeftToeXDir, u); }
    public void SampleRightUpLeg(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightUpLegYDir, u); xDir = Sample(LegRightUpLegXDir, u); }
    public void SampleRightLeg(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightLegYDir, u); xDir = Sample(LegRightLegXDir, u); }
    public void SampleRightFoot(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightFootYDir, u); xDir = Sample(LegRightFootXDir, u); }
    public void SampleRightToe(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightToeYDir, u); xDir = Sample(LegRightToeXDir, u); }
    public void SampleSpine(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(SpineYDir, u); xDir = Sample(SpineXDir, u); }
    public void SampleSpine01(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(Spine01YDir, u); xDir = Sample(Spine01XDir, u); }
    public void SampleSpine02(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(Spine02YDir, u); xDir = Sample(Spine02XDir, u); }
    public void SampleNeck(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(NeckYDir, u); xDir = Sample(NeckXDir, u); }
    public void SampleHead(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(HeadYDir, u); xDir = Sample(HeadXDir, u); }
    public void SampleLeftShoulder(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LeftShoulderYDir, u); xDir = Sample(LeftShoulderXDir, u); }
    public void SampleRightShoulder(float u, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(RightShoulderYDir, u); xDir = Sample(RightShoulderXDir, u); }

    public Quaternion SampleSpineAdd(float u)
    { return Basis(Sample(SpineYDir, u), Sample(SpineXDir, u)) * Quaternion.Inverse(SpineMean); }
    public Quaternion SampleSpine01Add(float u)
    { return Basis(Sample(Spine01YDir, u), Sample(Spine01XDir, u)) * Quaternion.Inverse(Spine01Mean); }
    public Quaternion SampleSpine02Add(float u)
    { return Basis(Sample(Spine02YDir, u), Sample(Spine02XDir, u)) * Quaternion.Inverse(Spine02Mean); }
    public Quaternion SampleNeckAdd(float u)
    { return Basis(Sample(NeckYDir, u), Sample(NeckXDir, u)) * Quaternion.Inverse(NeckMean); }
    public Quaternion SampleHeadAdd(float u)
    { return Basis(Sample(HeadYDir, u), Sample(HeadXDir, u)) * Quaternion.Inverse(HeadMean); }
    public Quaternion SampleLeftShoulderAdd(float u)
    { return Basis(Sample(LeftShoulderYDir, u), Sample(LeftShoulderXDir, u)) * Quaternion.Inverse(LeftShoulderMean); }
    public Quaternion SampleRightShoulderAdd(float u)
    { return Basis(Sample(RightShoulderYDir, u), Sample(RightShoulderXDir, u)) * Quaternion.Inverse(RightShoulderMean); }

    static void Index(float u, int len, out int i0, out int i1, out float f)
    {
        float x = Mathf.Clamp01(u) * (len - 1);
        i0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, len - 1);
        i1 = Mathf.Min(i0 + 1, len - 1);
        f = x - i0;
    }

    static Vector3 Sample(Vector3[] frames, float u)
    {
        int i0, i1; float f; Index(u, frames.Length, out i0, out i1, out f);
        return Vector3.Slerp(frames[i0], frames[i1], f).normalized;
    }

    static Vector3 SamplePos(Vector3[] frames, float u)
    {
        int i0, i1; float f; Index(u, frames.Length, out i0, out i1, out f);
        return Vector3.Lerp(frames[i0], frames[i1], f);
    }

    // 上半身は「加算」で乗せる (素材の姿勢で担ぎ姿勢を潰さないため)。
    static Quaternion Basis(Vector3 y, Vector3 x)
    {
        y = y.normalized;
        x = (x - y * Vector3.Dot(x, y)).normalized;
        return Quaternion.LookRotation(Vector3.Cross(x, y), y);
    }

    static Quaternion MeanBasis(Vector3[] ys, Vector3[] xs)
    {
        Vector3 y = Vector3.zero, x = Vector3.zero;
        for (int i = 0; i < ys.Length; i++) { y += ys[i]; x += xs[i]; }
        return Basis(y, x);
    }

    static readonly Vector3[] HipsPos = {
        new Vector3(0.03049f,0.70323f,-0.04741f), new Vector3(0.01686f,0.72502f,-0.05241f), new Vector3(0.00324f,0.74681f,-0.05740f), new Vector3(-0.01139f,0.76894f,-0.06226f),
        new Vector3(-0.02603f,0.79107f,-0.06711f), new Vector3(-0.03434f,0.80651f,-0.06716f), new Vector3(-0.04266f,0.82195f,-0.06720f), new Vector3(-0.04406f,0.83297f,-0.06248f),
        new Vector3(-0.04546f,0.84399f,-0.05776f), new Vector3(-0.04520f,0.85621f,-0.05126f), new Vector3(-0.04495f,0.86843f,-0.04476f), new Vector3(-0.04324f,0.88001f,-0.03703f),
        new Vector3(-0.04153f,0.89159f,-0.02930f), new Vector3(-0.03877f,0.90101f,-0.02090f), new Vector3(-0.03601f,0.91042f,-0.01249f), new Vector3(-0.03293f,0.91496f,-0.00381f),
        new Vector3(-0.02986f,0.91949f,0.00487f), new Vector3(-0.02613f,0.91901f,0.01201f), new Vector3(-0.02240f,0.91854f,0.01916f), new Vector3(-0.01809f,0.91110f,0.02476f),
        new Vector3(-0.01377f,0.90365f,0.03036f), new Vector3(-0.00549f,0.87625f,0.03427f), new Vector3(0.00280f,0.84885f,0.03818f), new Vector3(0.01789f,0.80072f,0.04235f),
        new Vector3(0.03299f,0.75260f,0.04652f), new Vector3(0.05082f,0.70217f,0.05646f), new Vector3(0.06866f,0.65175f,0.06640f), new Vector3(0.08221f,0.61630f,0.07838f),
        new Vector3(0.09575f,0.58085f,0.09036f), new Vector3(0.09879f,0.57102f,0.09567f), new Vector3(0.10182f,0.56119f,0.10097f),
    };

    static readonly Vector3[] HipsYDir = {
        new Vector3(0.00000f,0.90501f,-0.42540f), new Vector3(0.00000f,0.92379f,-0.38290f), new Vector3(0.00000f,0.94070f,-0.33923f), new Vector3(0.00000f,0.95503f,-0.29653f),
        new Vector3(0.00000f,0.96750f,-0.25287f), new Vector3(0.00000f,0.97387f,-0.22711f), new Vector3(0.00000f,0.97955f,-0.20121f), new Vector3(0.00000f,0.98163f,-0.19077f),
        new Vector3(0.00000f,0.98365f,-0.18009f), new Vector3(0.00000f,0.98509f,-0.17201f), new Vector3(0.00000f,0.98655f,-0.16348f), new Vector3(0.00000f,0.98750f,-0.15760f),
        new Vector3(0.00000f,0.98853f,-0.15105f), new Vector3(0.00000f,0.98910f,-0.14722f), new Vector3(0.00000f,0.98979f,-0.14253f), new Vector3(0.00000f,0.99016f,-0.13993f),
        new Vector3(0.00000f,0.99065f,-0.13640f), new Vector3(0.00000f,0.99084f,-0.13505f), new Vector3(0.00000f,0.99113f,-0.13286f), new Vector3(0.00000f,0.99097f,-0.13410f),
        new Vector3(0.00000f,0.99087f,-0.13482f), new Vector3(0.00000f,0.98813f,-0.15364f), new Vector3(0.00000f,0.98491f,-0.17309f), new Vector3(0.00000f,0.97771f,-0.20996f),
        new Vector3(0.00000f,0.96856f,-0.24877f), new Vector3(0.00000f,0.95752f,-0.28838f), new Vector3(0.00000f,0.94415f,-0.32951f), new Vector3(0.00000f,0.93464f,-0.35560f),
        new Vector3(0.00000f,0.92403f,-0.38233f), new Vector3(0.00000f,0.91838f,-0.39570f), new Vector3(0.00000f,0.91269f,-0.40866f),
    };

    static readonly Vector3[] HipsXDir = {
        new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f),
        new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f),
        new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f),
        new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f),
        new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f),
        new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f),
        new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f),
        new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f), new Vector3(1.00000f,0.00000f,0.00000f),
    };

    static readonly Vector3[] LegLeftUpLegYDir = {
        new Vector3(0.74795f,-0.50251f,0.43364f), new Vector3(0.72748f,-0.52870f,0.43733f), new Vector3(0.70710f,-0.55362f,0.43990f), new Vector3(0.68400f,-0.58554f,0.43507f),
        new Vector3(0.66092f,-0.61574f,0.42902f), new Vector3(0.64212f,-0.64214f,0.41872f), new Vector3(0.62306f,-0.66758f,0.40759f), new Vector3(0.60608f,-0.68937f,0.39678f),
        new Vector3(0.58903f,-0.71019f,0.38559f), new Vector3(0.56808f,-0.73579f,0.36864f), new Vector3(0.54760f,-0.75942f,0.35131f), new Vector3(0.53056f,-0.78149f,0.32829f),
        new Vector3(0.51448f,-0.80134f,0.30522f), new Vector3(0.50405f,-0.81826f,0.27639f), new Vector3(0.49429f,-0.83324f,0.24775f), new Vector3(0.49379f,-0.83860f,0.23005f),
        new Vector3(0.49327f,-0.84348f,0.21266f), new Vector3(0.50734f,-0.83969f,0.19373f), new Vector3(0.52084f,-0.83570f,0.17414f), new Vector3(0.54334f,-0.82208f,0.17017f),
        new Vector3(0.56523f,-0.80825f,0.16510f), new Vector3(0.60473f,-0.76864f,0.20856f), new Vector3(0.64374f,-0.72386f,0.24826f), new Vector3(0.68234f,-0.66476f,0.30415f),
        new Vector3(0.71859f,-0.59864f,0.35392f), new Vector3(0.70381f,-0.58159f,0.40792f), new Vector3(0.68113f,-0.57051f,0.45889f), new Vector3(0.64844f,-0.59251f,0.47797f),
        new Vector3(0.61083f,-0.62195f,0.48997f), new Vector3(0.60516f,-0.63005f,0.48664f), new Vector3(0.59987f,-0.63805f,0.48276f),
    };

    static readonly Vector3[] LegLeftUpLegXDir = {
        new Vector3(0.57671f,0.44062f,-0.68793f), new Vector3(0.59602f,0.41195f,-0.68924f), new Vector3(0.61300f,0.38214f,-0.69152f), new Vector3(0.62758f,0.35432f,-0.69326f),
        new Vector3(0.63945f,0.32570f,-0.69644f), new Vector3(0.64877f,0.31158f,-0.69427f), new Vector3(0.65697f,0.29738f,-0.69279f), new Vector3(0.66952f,0.29874f,-0.68007f),
        new Vector3(0.68178f,0.29952f,-0.66743f), new Vector3(0.69867f,0.30695f,-0.64625f), new Vector3(0.71509f,0.31326f,-0.62491f), new Vector3(0.72965f,0.32128f,-0.60365f),
        new Vector3(0.74369f,0.32871f,-0.58212f), new Vector3(0.75128f,0.33588f,-0.56812f), new Vector3(0.75860f,0.34316f,-0.55387f), new Vector3(0.75838f,0.34956f,-0.55014f),
        new Vector3(0.75816f,0.35591f,-0.54637f), new Vector3(0.73365f,0.36313f,-0.57436f), new Vector3(0.70794f,0.37033f,-0.60140f), new Vector3(0.67964f,0.37703f,-0.62923f),
        new Vector3(0.65029f,0.38244f,-0.65640f), new Vector3(0.64059f,0.38431f,-0.66480f), new Vector3(0.62994f,0.38549f,-0.67422f), new Vector3(0.63356f,0.40350f,-0.66014f),
        new Vector3(0.63272f,0.42419f,-0.64786f), new Vector3(0.66103f,0.49574f,-0.56327f), new Vector3(0.67166f,0.56832f,-0.47527f), new Vector3(0.66342f,0.63988f,-0.38785f),
        new Vector3(0.64147f,0.70647f,-0.29903f), new Vector3(0.63864f,0.71500f,-0.28446f), new Vector3(0.63538f,0.72359f,-0.26966f),
    };

    static readonly Vector3[] LegLeftLegYDir = {
        new Vector3(-0.12755f,-0.89379f,-0.42996f), new Vector3(-0.11305f,-0.90969f,-0.39960f), new Vector3(-0.10092f,-0.92429f,-0.36812f), new Vector3(-0.08475f,-0.93895f,-0.33345f),
        new Vector3(-0.07053f,-0.95209f,-0.29757f), new Vector3(-0.05720f,-0.95777f,-0.28178f), new Vector3(-0.04428f,-0.96326f,-0.26489f), new Vector3(-0.03019f,-0.96238f,-0.27002f),
        new Vector3(-0.01581f,-0.96177f,-0.27341f), new Vector3(0.00549f,-0.96022f,-0.27919f), new Vector3(0.02731f,-0.95905f,-0.28192f), new Vector3(0.04896f,-0.95741f,-0.28456f),
        new Vector3(0.07146f,-0.95610f,-0.28420f), new Vector3(0.09145f,-0.95519f,-0.28150f), new Vector3(0.11244f,-0.95439f,-0.27656f), new Vector3(0.12522f,-0.95055f,-0.28421f),
        new Vector3(0.13812f,-0.94669f,-0.29102f), new Vector3(0.13708f,-0.94684f,-0.29104f), new Vector3(0.13634f,-0.94695f,-0.29103f), new Vector3(0.12594f,-0.94452f,-0.30335f),
        new Vector3(0.11406f,-0.94252f,-0.31408f), new Vector3(0.07724f,-0.92412f,-0.37421f), new Vector3(0.03522f,-0.90104f,-0.43231f), new Vector3(-0.01083f,-0.85846f,-0.51276f),
        new Vector3(-0.05643f,-0.80410f,-0.59181f), new Vector3(-0.02862f,-0.74093f,-0.67097f), new Vector3(0.00849f,-0.66252f,-0.74900f), new Vector3(0.07569f,-0.59044f,-0.80353f),
        new Vector3(0.14645f,-0.50219f,-0.85227f), new Vector3(0.16272f,-0.48508f,-0.85920f), new Vector3(0.17965f,-0.46668f,-0.86599f),
    };

    static readonly Vector3[] LegLeftLegXDir = {
        new Vector3(0.59264f,0.50678f,-0.62606f), new Vector3(0.61299f,0.47517f,-0.63124f), new Vector3(0.63114f,0.44197f,-0.63744f), new Vector3(0.64853f,0.40910f,-0.64191f),
        new Vector3(0.66313f,0.37507f,-0.64775f), new Vector3(0.67455f,0.35644f,-0.64647f), new Vector3(0.68472f,0.33760f,-0.64590f), new Vector3(0.69757f,0.33564f,-0.63304f),
        new Vector3(0.71008f,0.33307f,-0.62036f), new Vector3(0.72737f,0.33628f,-0.59821f), new Vector3(0.74410f,0.33833f,-0.57606f), new Vector3(0.76057f,0.34213f,-0.55180f),
        new Vector3(0.77632f,0.34522f,-0.52740f), new Vector3(0.78670f,0.34815f,-0.50981f), new Vector3(0.79662f,0.35106f,-0.49209f), new Vector3(0.79825f,0.35652f,-0.48548f),
        new Vector3(0.79985f,0.36188f,-0.47884f), new Vector3(0.77985f,0.37029f,-0.50470f), new Vector3(0.75881f,0.37877f,-0.52985f), new Vector3(0.73246f,0.39000f,-0.55803f),
        new Vector3(0.70498f,0.40001f,-0.58566f), new Vector3(0.68858f,0.41375f,-0.59554f), new Vector3(0.67101f,0.42628f,-0.60665f), new Vector3(0.66491f,0.45755f,-0.59038f),
        new Vector3(0.65379f,0.49054f,-0.57613f), new Vector3(0.66853f,0.56250f,-0.48648f), new Vector3(0.66548f,0.63355f,-0.39465f), new Vector3(0.64621f,0.70094f,-0.30181f),
        new Vector3(0.61306f,0.76195f,-0.20880f), new Vector3(0.60901f,0.76894f,-0.19454f), new Vector3(0.60457f,0.77592f,-0.18011f),
    };

    static readonly Vector3[] LegLeftFootYDir = {
        new Vector3(0.55034f,-0.63268f,0.54483f), new Vector3(0.54970f,-0.63118f,0.54721f), new Vector3(0.55066f,-0.62896f,0.54880f), new Vector3(0.55030f,-0.62803f,0.55023f),
        new Vector3(0.55212f,-0.62607f,0.55063f), new Vector3(0.54760f,-0.63144f,0.54901f), new Vector3(0.54420f,-0.63635f,0.54671f), new Vector3(0.53542f,-0.64455f,0.54578f),
        new Vector3(0.52705f,-0.65222f,0.54482f), new Vector3(0.51537f,-0.66552f,0.53989f), new Vector3(0.50424f,-0.67770f,0.53522f), new Vector3(0.48929f,-0.70130f,0.51844f),
        new Vector3(0.47542f,-0.72285f,0.50146f), new Vector3(0.46208f,-0.75051f,0.47245f), new Vector3(0.44981f,-0.77559f,0.44286f), new Vector3(0.44028f,-0.79634f,0.41472f),
        new Vector3(0.43105f,-0.81555f,0.38611f), new Vector3(0.43012f,-0.82755f,0.36076f), new Vector3(0.42808f,-0.83932f,0.33510f), new Vector3(0.41914f,-0.84870f,0.32253f),
        new Vector3(0.40856f,-0.85819f,0.31080f), new Vector3(0.39219f,-0.86738f,0.30633f), new Vector3(0.37666f,-0.87609f,0.30100f), new Vector3(0.35414f,-0.88504f,0.30213f),
        new Vector3(0.33946f,-0.89222f,0.29785f), new Vector3(0.25651f,-0.90249f,0.34598f), new Vector3(0.16585f,-0.91314f,0.37238f), new Vector3(0.09882f,-0.91626f,0.38820f),
        new Vector3(0.02181f,-0.92079f,0.38945f), new Vector3(0.00699f,-0.92155f,0.38820f), new Vector3(-0.00756f,-0.92248f,0.38597f),
    };

    static readonly Vector3[] LegLeftFootXDir = {
        new Vector3(0.67672f,-0.04981f,-0.73455f), new Vector3(0.67763f,-0.05346f,-0.73346f), new Vector3(0.67606f,-0.05821f,-0.73455f), new Vector3(0.67670f,-0.06120f,-0.73371f),
        new Vector3(0.67445f,-0.06507f,-0.73545f), new Vector3(0.67735f,-0.05929f,-0.73327f), new Vector3(0.67901f,-0.05378f,-0.73215f), new Vector3(0.68782f,-0.03962f,-0.72480f),
        new Vector3(0.69657f,-0.02621f,-0.71701f), new Vector3(0.70738f,-0.00713f,-0.70679f), new Vector3(0.71843f,0.01070f,-0.69552f), new Vector3(0.72763f,0.03425f,-0.68512f),
        new Vector3(0.73744f,0.05730f,-0.67298f), new Vector3(0.74357f,0.08164f,-0.66365f), new Vector3(0.75014f,0.10598f,-0.65274f), new Vector3(0.75389f,0.12441f,-0.64511f),
        new Vector3(0.75777f,0.14243f,-0.63679f), new Vector3(0.74829f,0.15364f,-0.64533f), new Vector3(0.73867f,0.16472f,-0.65363f), new Vector3(0.73105f,0.16302f,-0.66257f),
        new Vector3(0.72304f,0.15931f,-0.67219f), new Vector3(0.72326f,0.15084f,-0.67390f), new Vector3(0.72035f,0.14041f,-0.67926f), new Vector3(0.73028f,0.13689f,-0.66929f),
        new Vector3(0.73254f,0.13806f,-0.66658f), new Vector3(0.79219f,0.11535f,-0.59928f), new Vector3(0.84039f,0.09716f,-0.53320f), new Vector3(0.87529f,0.10687f,-0.47163f),
        new Vector3(0.90538f,0.11270f,-0.40937f), new Vector3(0.91032f,0.11279f,-0.39825f), new Vector3(0.91501f,0.11334f,-0.38718f),
    };

    static readonly Vector3[] LegLeftToeYDir = {
        new Vector3(0.72972f,0.12845f,0.67157f), new Vector3(0.72847f,0.12729f,0.67315f), new Vector3(0.72955f,0.12681f,0.67207f), new Vector3(0.72816f,0.12536f,0.67385f),
        new Vector3(0.72944f,0.12495f,0.67254f), new Vector3(0.72882f,0.12409f,0.67337f), new Vector3(0.72936f,0.12366f,0.67286f), new Vector3(0.72532f,0.12311f,0.67731f),
        new Vector3(0.72142f,0.12287f,0.68151f), new Vector3(0.71091f,0.12220f,0.69258f), new Vector3(0.70026f,0.12206f,0.70338f), new Vector3(0.68696f,0.12156f,0.71645f),
        new Vector3(0.67329f,0.12188f,0.72927f), new Vector3(0.66227f,0.12162f,0.73933f), new Vector3(0.65093f,0.12211f,0.74926f), new Vector3(0.64680f,0.12175f,0.75288f),
        new Vector3(0.64270f,0.12163f,0.75640f), new Vector3(0.65909f,0.12482f,0.74164f), new Vector3(0.67532f,0.12806f,0.72632f), new Vector3(0.69064f,0.13229f,0.71099f),
        new Vector3(0.70620f,0.13652f,0.69472f), new Vector3(0.69793f,0.13482f,0.70336f), new Vector3(0.69268f,0.13451f,0.70859f), new Vector3(0.67255f,0.13044f,0.72846f),
        new Vector3(0.65833f,0.12974f,0.74146f), new Vector3(0.57948f,0.13681f,0.80342f), new Vector3(0.50059f,0.14045f,0.85422f), new Vector3(0.42656f,0.15212f,0.89157f),
        new Vector3(0.34992f,0.15976f,0.92305f), new Vector3(0.35021f,0.16007f,0.92289f), new Vector3(0.35053f,0.16013f,0.92276f),
    };

    static readonly Vector3[] LegLeftToeXDir = {
        new Vector3(0.68049f,-0.11630f,-0.72347f), new Vector3(0.68187f,-0.11572f,-0.72227f), new Vector3(0.68076f,-0.11619f,-0.72323f), new Vector3(0.68228f,-0.11569f,-0.72188f),
        new Vector3(0.68095f,-0.11601f,-0.72308f), new Vector3(0.68163f,-0.11583f,-0.72247f), new Vector3(0.68107f,-0.11585f,-0.72300f), new Vector3(0.68543f,-0.11606f,-0.71883f),
        new Vector3(0.68966f,-0.11674f,-0.71466f), new Vector3(0.70064f,-0.11756f,-0.70377f), new Vector3(0.71152f,-0.11939f,-0.69244f), new Vector3(0.72449f,-0.12074f,-0.67862f),
        new Vector3(0.73744f,-0.12258f,-0.66419f), new Vector3(0.74739f,-0.12391f,-0.65273f), new Vector3(0.75744f,-0.12503f,-0.64082f), new Vector3(0.76097f,-0.12541f,-0.63655f),
        new Vector3(0.76447f,-0.12584f,-0.63226f), new Vector3(0.75019f,-0.12594f,-0.64912f), new Vector3(0.73534f,-0.12624f,-0.66584f), new Vector3(0.72059f,-0.12611f,-0.68179f),
        new Vector3(0.70486f,-0.12802f,-0.69769f), new Vector3(0.71333f,-0.12509f,-0.68957f), new Vector3(0.71844f,-0.12457f,-0.68435f), new Vector3(0.73775f,-0.12261f,-0.66385f),
        new Vector3(0.75069f,-0.11701f,-0.65021f), new Vector3(0.81290f,-0.11941f,-0.57002f), new Vector3(0.86306f,-0.11821f,-0.49108f), new Vector3(0.90164f,-0.11810f,-0.41606f),
        new Vector3(0.93272f,-0.12366f,-0.33875f), new Vector3(0.93261f,-0.12390f,-0.33895f), new Vector3(0.93248f,-0.12375f,-0.33937f),
    };

    static readonly Vector3[] LegRightUpLegYDir = {
        new Vector3(-0.74795f,-0.50251f,0.43364f), new Vector3(-0.72748f,-0.52870f,0.43733f), new Vector3(-0.70710f,-0.55362f,0.43990f), new Vector3(-0.68400f,-0.58554f,0.43507f),
        new Vector3(-0.66092f,-0.61574f,0.42902f), new Vector3(-0.64212f,-0.64214f,0.41872f), new Vector3(-0.62306f,-0.66758f,0.40759f), new Vector3(-0.60608f,-0.68937f,0.39678f),
        new Vector3(-0.58903f,-0.71019f,0.38559f), new Vector3(-0.56808f,-0.73579f,0.36864f), new Vector3(-0.54760f,-0.75942f,0.35131f), new Vector3(-0.53056f,-0.78149f,0.32829f),
        new Vector3(-0.51448f,-0.80134f,0.30522f), new Vector3(-0.50405f,-0.81826f,0.27639f), new Vector3(-0.49429f,-0.83324f,0.24775f), new Vector3(-0.49379f,-0.83860f,0.23005f),
        new Vector3(-0.49327f,-0.84348f,0.21266f), new Vector3(-0.50734f,-0.83969f,0.19373f), new Vector3(-0.52084f,-0.83570f,0.17414f), new Vector3(-0.54334f,-0.82208f,0.17017f),
        new Vector3(-0.56523f,-0.80825f,0.16510f), new Vector3(-0.60473f,-0.76864f,0.20856f), new Vector3(-0.64374f,-0.72386f,0.24826f), new Vector3(-0.68234f,-0.66476f,0.30415f),
        new Vector3(-0.71859f,-0.59864f,0.35392f), new Vector3(-0.70381f,-0.58159f,0.40792f), new Vector3(-0.68113f,-0.57051f,0.45889f), new Vector3(-0.64844f,-0.59251f,0.47797f),
        new Vector3(-0.61083f,-0.62195f,0.48997f), new Vector3(-0.60516f,-0.63005f,0.48664f), new Vector3(-0.59987f,-0.63805f,0.48276f),
    };

    static readonly Vector3[] LegRightUpLegXDir = {
        new Vector3(0.57671f,-0.44062f,0.68793f), new Vector3(0.59602f,-0.41195f,0.68924f), new Vector3(0.61300f,-0.38214f,0.69152f), new Vector3(0.62758f,-0.35432f,0.69326f),
        new Vector3(0.63945f,-0.32570f,0.69644f), new Vector3(0.64877f,-0.31158f,0.69427f), new Vector3(0.65697f,-0.29738f,0.69279f), new Vector3(0.66952f,-0.29874f,0.68007f),
        new Vector3(0.68178f,-0.29952f,0.66743f), new Vector3(0.69867f,-0.30695f,0.64625f), new Vector3(0.71509f,-0.31326f,0.62491f), new Vector3(0.72965f,-0.32128f,0.60365f),
        new Vector3(0.74369f,-0.32871f,0.58212f), new Vector3(0.75128f,-0.33588f,0.56812f), new Vector3(0.75860f,-0.34316f,0.55387f), new Vector3(0.75838f,-0.34956f,0.55014f),
        new Vector3(0.75816f,-0.35591f,0.54637f), new Vector3(0.73365f,-0.36313f,0.57436f), new Vector3(0.70794f,-0.37033f,0.60140f), new Vector3(0.67964f,-0.37703f,0.62923f),
        new Vector3(0.65029f,-0.38244f,0.65640f), new Vector3(0.64059f,-0.38431f,0.66480f), new Vector3(0.62994f,-0.38549f,0.67422f), new Vector3(0.63356f,-0.40350f,0.66014f),
        new Vector3(0.63272f,-0.42419f,0.64786f), new Vector3(0.66103f,-0.49574f,0.56327f), new Vector3(0.67166f,-0.56832f,0.47527f), new Vector3(0.66342f,-0.63988f,0.38785f),
        new Vector3(0.64147f,-0.70647f,0.29903f), new Vector3(0.63864f,-0.71500f,0.28446f), new Vector3(0.63538f,-0.72359f,0.26966f),
    };

    static readonly Vector3[] LegRightLegYDir = {
        new Vector3(0.12755f,-0.89379f,-0.42996f), new Vector3(0.11305f,-0.90969f,-0.39960f), new Vector3(0.10092f,-0.92429f,-0.36812f), new Vector3(0.08475f,-0.93895f,-0.33345f),
        new Vector3(0.07053f,-0.95209f,-0.29757f), new Vector3(0.05720f,-0.95777f,-0.28178f), new Vector3(0.04428f,-0.96326f,-0.26489f), new Vector3(0.03019f,-0.96238f,-0.27002f),
        new Vector3(0.01581f,-0.96177f,-0.27341f), new Vector3(-0.00549f,-0.96022f,-0.27919f), new Vector3(-0.02731f,-0.95905f,-0.28192f), new Vector3(-0.04896f,-0.95741f,-0.28456f),
        new Vector3(-0.07146f,-0.95610f,-0.28420f), new Vector3(-0.09145f,-0.95519f,-0.28150f), new Vector3(-0.11244f,-0.95439f,-0.27656f), new Vector3(-0.12522f,-0.95055f,-0.28421f),
        new Vector3(-0.13812f,-0.94669f,-0.29102f), new Vector3(-0.13708f,-0.94684f,-0.29104f), new Vector3(-0.13634f,-0.94695f,-0.29103f), new Vector3(-0.12594f,-0.94452f,-0.30335f),
        new Vector3(-0.11406f,-0.94252f,-0.31408f), new Vector3(-0.07724f,-0.92412f,-0.37421f), new Vector3(-0.03522f,-0.90104f,-0.43231f), new Vector3(0.01083f,-0.85846f,-0.51276f),
        new Vector3(0.05643f,-0.80410f,-0.59181f), new Vector3(0.02862f,-0.74093f,-0.67097f), new Vector3(-0.00849f,-0.66252f,-0.74900f), new Vector3(-0.07569f,-0.59044f,-0.80353f),
        new Vector3(-0.14645f,-0.50219f,-0.85227f), new Vector3(-0.16272f,-0.48508f,-0.85920f), new Vector3(-0.17965f,-0.46668f,-0.86599f),
    };

    static readonly Vector3[] LegRightLegXDir = {
        new Vector3(0.59264f,-0.50678f,0.62606f), new Vector3(0.61299f,-0.47517f,0.63124f), new Vector3(0.63114f,-0.44197f,0.63744f), new Vector3(0.64853f,-0.40910f,0.64191f),
        new Vector3(0.66313f,-0.37507f,0.64775f), new Vector3(0.67455f,-0.35644f,0.64647f), new Vector3(0.68472f,-0.33760f,0.64590f), new Vector3(0.69757f,-0.33564f,0.63304f),
        new Vector3(0.71008f,-0.33307f,0.62036f), new Vector3(0.72737f,-0.33628f,0.59821f), new Vector3(0.74410f,-0.33833f,0.57606f), new Vector3(0.76057f,-0.34213f,0.55180f),
        new Vector3(0.77632f,-0.34522f,0.52740f), new Vector3(0.78670f,-0.34815f,0.50981f), new Vector3(0.79662f,-0.35106f,0.49209f), new Vector3(0.79825f,-0.35652f,0.48548f),
        new Vector3(0.79985f,-0.36188f,0.47884f), new Vector3(0.77985f,-0.37029f,0.50470f), new Vector3(0.75881f,-0.37877f,0.52985f), new Vector3(0.73246f,-0.39000f,0.55803f),
        new Vector3(0.70498f,-0.40001f,0.58566f), new Vector3(0.68858f,-0.41375f,0.59554f), new Vector3(0.67101f,-0.42628f,0.60665f), new Vector3(0.66491f,-0.45755f,0.59038f),
        new Vector3(0.65379f,-0.49054f,0.57613f), new Vector3(0.66853f,-0.56250f,0.48648f), new Vector3(0.66548f,-0.63355f,0.39465f), new Vector3(0.64621f,-0.70094f,0.30181f),
        new Vector3(0.61306f,-0.76195f,0.20880f), new Vector3(0.60901f,-0.76894f,0.19454f), new Vector3(0.60457f,-0.77592f,0.18011f),
    };

    static readonly Vector3[] LegRightFootYDir = {
        new Vector3(-0.55034f,-0.63268f,0.54483f), new Vector3(-0.54970f,-0.63118f,0.54721f), new Vector3(-0.55066f,-0.62896f,0.54880f), new Vector3(-0.55030f,-0.62803f,0.55023f),
        new Vector3(-0.55212f,-0.62607f,0.55063f), new Vector3(-0.54760f,-0.63144f,0.54901f), new Vector3(-0.54420f,-0.63635f,0.54671f), new Vector3(-0.53542f,-0.64455f,0.54578f),
        new Vector3(-0.52705f,-0.65222f,0.54482f), new Vector3(-0.51537f,-0.66552f,0.53989f), new Vector3(-0.50424f,-0.67770f,0.53522f), new Vector3(-0.48929f,-0.70130f,0.51844f),
        new Vector3(-0.47542f,-0.72285f,0.50146f), new Vector3(-0.46208f,-0.75051f,0.47245f), new Vector3(-0.44981f,-0.77559f,0.44286f), new Vector3(-0.44028f,-0.79634f,0.41472f),
        new Vector3(-0.43105f,-0.81555f,0.38611f), new Vector3(-0.43012f,-0.82755f,0.36076f), new Vector3(-0.42808f,-0.83932f,0.33510f), new Vector3(-0.41914f,-0.84870f,0.32253f),
        new Vector3(-0.40856f,-0.85819f,0.31080f), new Vector3(-0.39219f,-0.86738f,0.30633f), new Vector3(-0.37666f,-0.87609f,0.30100f), new Vector3(-0.35414f,-0.88504f,0.30213f),
        new Vector3(-0.33946f,-0.89222f,0.29785f), new Vector3(-0.25651f,-0.90249f,0.34598f), new Vector3(-0.16585f,-0.91314f,0.37238f), new Vector3(-0.09882f,-0.91626f,0.38820f),
        new Vector3(-0.02181f,-0.92079f,0.38945f), new Vector3(-0.00699f,-0.92155f,0.38820f), new Vector3(0.00756f,-0.92248f,0.38597f),
    };

    static readonly Vector3[] LegRightFootXDir = {
        new Vector3(0.67672f,0.04981f,0.73455f), new Vector3(0.67763f,0.05346f,0.73346f), new Vector3(0.67606f,0.05821f,0.73455f), new Vector3(0.67670f,0.06120f,0.73371f),
        new Vector3(0.67445f,0.06507f,0.73545f), new Vector3(0.67735f,0.05929f,0.73327f), new Vector3(0.67901f,0.05378f,0.73215f), new Vector3(0.68782f,0.03962f,0.72480f),
        new Vector3(0.69657f,0.02621f,0.71701f), new Vector3(0.70738f,0.00713f,0.70679f), new Vector3(0.71843f,-0.01070f,0.69552f), new Vector3(0.72763f,-0.03425f,0.68512f),
        new Vector3(0.73744f,-0.05730f,0.67298f), new Vector3(0.74357f,-0.08164f,0.66365f), new Vector3(0.75014f,-0.10598f,0.65274f), new Vector3(0.75389f,-0.12441f,0.64511f),
        new Vector3(0.75777f,-0.14243f,0.63679f), new Vector3(0.74829f,-0.15364f,0.64533f), new Vector3(0.73867f,-0.16472f,0.65363f), new Vector3(0.73105f,-0.16302f,0.66257f),
        new Vector3(0.72304f,-0.15931f,0.67219f), new Vector3(0.72326f,-0.15084f,0.67390f), new Vector3(0.72035f,-0.14041f,0.67926f), new Vector3(0.73028f,-0.13689f,0.66929f),
        new Vector3(0.73254f,-0.13806f,0.66658f), new Vector3(0.79219f,-0.11535f,0.59928f), new Vector3(0.84039f,-0.09716f,0.53320f), new Vector3(0.87529f,-0.10687f,0.47163f),
        new Vector3(0.90538f,-0.11270f,0.40937f), new Vector3(0.91032f,-0.11279f,0.39825f), new Vector3(0.91501f,-0.11334f,0.38718f),
    };

    static readonly Vector3[] LegRightToeYDir = {
        new Vector3(-0.72972f,0.12845f,0.67157f), new Vector3(-0.72847f,0.12729f,0.67315f), new Vector3(-0.72955f,0.12681f,0.67207f), new Vector3(-0.72816f,0.12536f,0.67385f),
        new Vector3(-0.72944f,0.12495f,0.67254f), new Vector3(-0.72882f,0.12409f,0.67337f), new Vector3(-0.72936f,0.12366f,0.67286f), new Vector3(-0.72532f,0.12311f,0.67731f),
        new Vector3(-0.72142f,0.12287f,0.68151f), new Vector3(-0.71091f,0.12220f,0.69258f), new Vector3(-0.70026f,0.12206f,0.70338f), new Vector3(-0.68696f,0.12156f,0.71645f),
        new Vector3(-0.67329f,0.12188f,0.72927f), new Vector3(-0.66227f,0.12162f,0.73933f), new Vector3(-0.65093f,0.12211f,0.74926f), new Vector3(-0.64680f,0.12175f,0.75288f),
        new Vector3(-0.64270f,0.12163f,0.75640f), new Vector3(-0.65909f,0.12482f,0.74164f), new Vector3(-0.67532f,0.12806f,0.72632f), new Vector3(-0.69064f,0.13229f,0.71099f),
        new Vector3(-0.70620f,0.13652f,0.69472f), new Vector3(-0.69793f,0.13482f,0.70336f), new Vector3(-0.69268f,0.13451f,0.70859f), new Vector3(-0.67255f,0.13044f,0.72846f),
        new Vector3(-0.65833f,0.12974f,0.74146f), new Vector3(-0.57948f,0.13681f,0.80342f), new Vector3(-0.50059f,0.14045f,0.85422f), new Vector3(-0.42656f,0.15212f,0.89157f),
        new Vector3(-0.34992f,0.15976f,0.92305f), new Vector3(-0.35021f,0.16007f,0.92289f), new Vector3(-0.35053f,0.16013f,0.92276f),
    };

    static readonly Vector3[] LegRightToeXDir = {
        new Vector3(0.68049f,0.11630f,0.72347f), new Vector3(0.68187f,0.11572f,0.72227f), new Vector3(0.68076f,0.11619f,0.72323f), new Vector3(0.68228f,0.11569f,0.72188f),
        new Vector3(0.68095f,0.11601f,0.72308f), new Vector3(0.68163f,0.11583f,0.72247f), new Vector3(0.68107f,0.11585f,0.72300f), new Vector3(0.68543f,0.11606f,0.71883f),
        new Vector3(0.68966f,0.11674f,0.71466f), new Vector3(0.70064f,0.11756f,0.70377f), new Vector3(0.71152f,0.11939f,0.69244f), new Vector3(0.72449f,0.12074f,0.67862f),
        new Vector3(0.73744f,0.12258f,0.66419f), new Vector3(0.74739f,0.12391f,0.65273f), new Vector3(0.75744f,0.12503f,0.64082f), new Vector3(0.76097f,0.12541f,0.63655f),
        new Vector3(0.76447f,0.12584f,0.63226f), new Vector3(0.75019f,0.12594f,0.64912f), new Vector3(0.73534f,0.12624f,0.66584f), new Vector3(0.72059f,0.12611f,0.68179f),
        new Vector3(0.70486f,0.12802f,0.69769f), new Vector3(0.71333f,0.12509f,0.68957f), new Vector3(0.71844f,0.12457f,0.68435f), new Vector3(0.73775f,0.12261f,0.66385f),
        new Vector3(0.75069f,0.11701f,0.65021f), new Vector3(0.81290f,0.11941f,0.57002f), new Vector3(0.86306f,0.11821f,0.49108f), new Vector3(0.90164f,0.11810f,0.41606f),
        new Vector3(0.93272f,0.12366f,0.33875f), new Vector3(0.93261f,0.12390f,0.33895f), new Vector3(0.93248f,0.12375f,0.33937f),
    };

    static readonly Vector3[] SpineYDir = {
        new Vector3(0.12037f,0.96847f,0.21814f), new Vector3(0.10177f,0.93799f,0.33139f), new Vector3(0.07535f,0.89523f,0.43918f), new Vector3(0.04460f,0.84133f,0.53868f),
        new Vector3(0.00674f,0.77695f,0.62953f), new Vector3(-0.00959f,0.73921f,0.67340f), new Vector3(-0.02666f,0.69903f,0.71460f), new Vector3(-0.01374f,0.69007f,0.72361f),
        new Vector3(-0.00037f,0.68090f,0.73238f), new Vector3(0.02523f,0.67635f,0.73615f), new Vector3(0.05126f,0.67158f,0.73916f), new Vector3(0.08297f,0.67249f,0.73545f),
        new Vector3(0.11471f,0.67313f,0.73058f), new Vector3(0.14479f,0.68066f,0.71815f), new Vector3(0.17423f,0.68778f,0.70470f), new Vector3(0.19439f,0.70314f,0.68397f),
        new Vector3(0.21307f,0.71778f,0.66287f), new Vector3(0.21471f,0.74101f,0.63624f), new Vector3(0.21418f,0.76283f,0.61009f), new Vector3(0.18881f,0.79367f,0.57831f),
        new Vector3(0.16082f,0.82155f,0.54699f), new Vector3(0.07224f,0.87008f,0.48759f), new Vector3(-0.01771f,0.90661f,0.42159f), new Vector3(-0.16043f,0.93985f,0.30157f),
        new Vector3(-0.28862f,0.94353f,0.16266f), new Vector3(-0.39888f,0.91700f,-0.00318f), new Vector3(-0.47587f,0.86103f,-0.17938f), new Vector3(-0.50835f,0.80018f,-0.31825f),
        new Vector3(-0.51683f,0.72578f,-0.45402f), new Vector3(-0.50647f,0.70857f,-0.49134f), new Vector3(-0.49599f,0.68949f,-0.52782f),
    };

    static readonly Vector3[] SpineXDir = {
        new Vector3(0.89239f,-0.00929f,-0.45117f), new Vector3(0.91837f,0.03947f,-0.39375f), new Vector3(0.94042f,0.08265f,-0.32982f), new Vector3(0.95838f,0.11618f,-0.26079f),
        new Vector3(0.97174f,0.14345f,-0.18745f), new Vector3(0.97541f,0.15519f,-0.15647f), new Vector3(0.97843f,0.16475f,-0.12465f), new Vector3(0.97551f,0.16813f,-0.14182f),
        new Vector3(0.97232f,0.17138f,-0.15884f), new Vector3(0.96571f,0.17385f,-0.19283f), new Vector3(0.95793f,0.17622f,-0.22654f), new Vector3(0.94593f,0.17907f,-0.27046f),
        new Vector3(0.93193f,0.18177f,-0.31380f), new Vector3(0.91397f,0.18606f,-0.36061f), new Vector3(0.89361f,0.19019f,-0.40656f), new Vector3(0.87117f,0.19673f,-0.44984f),
        new Vector3(0.84644f,0.20325f,-0.49216f), new Vector3(0.82334f,0.21308f,-0.52603f), new Vector3(0.79847f,0.22304f,-0.55919f), new Vector3(0.78007f,0.23652f,-0.57927f),
        new Vector3(0.76052f,0.25008f,-0.59922f), new Vector3(0.75608f,0.27106f,-0.59572f), new Vector3(0.75094f,0.29045f,-0.59307f), new Vector3(0.76073f,0.31240f,-0.56894f),
        new Vector3(0.77165f,0.32980f,-0.54387f), new Vector3(0.79013f,0.34194f,-0.50870f), new Vector3(0.81113f,0.35079f,-0.46800f), new Vector3(0.82830f,0.35325f,-0.43490f),
        new Vector3(0.84671f,0.35511f,-0.39619f), new Vector3(0.85440f,0.33565f,-0.39666f), new Vector3(0.86182f,0.31660f,-0.39628f),
    };

    static readonly Vector3[] Spine01YDir = {
        new Vector3(0.17184f,0.94018f,0.29415f), new Vector3(0.14963f,0.90368f,0.40122f), new Vector3(0.12040f,0.85624f,0.50236f), new Vector3(0.08544f,0.79913f,0.59505f),
        new Vector3(0.04417f,0.73287f,0.67893f), new Vector3(0.02564f,0.69474f,0.71880f), new Vector3(0.00649f,0.65449f,0.75604f), new Vector3(0.01992f,0.64594f,0.76313f),
        new Vector3(0.03373f,0.63720f,0.76996f), new Vector3(0.06051f,0.63316f,0.77165f), new Vector3(0.08761f,0.62889f,0.77254f), new Vector3(0.12089f,0.63041f,0.76679f),
        new Vector3(0.15409f,0.63164f,0.75979f), new Vector3(0.18608f,0.63984f,0.74564f), new Vector3(0.21736f,0.64761f,0.73032f), new Vector3(0.24002f,0.66410f,0.70807f),
        new Vector3(0.26113f,0.67990f,0.68524f), new Vector3(0.26563f,0.70439f,0.65823f), new Vector3(0.26797f,0.72760f,0.63150f), new Vector3(0.24671f,0.75956f,0.60183f),
        new Vector3(0.22295f,0.78901f,0.57249f), new Vector3(0.13940f,0.84267f,0.52008f), new Vector3(0.05380f,0.88569f,0.46116f), new Vector3(-0.08701f,0.93043f,0.35599f),
        new Vector3(-0.21673f,0.94842f,0.23137f), new Vector3(-0.33477f,0.93905f,0.07823f), new Vector3(-0.42374f,0.90147f,-0.08830f), new Vector3(-0.46803f,0.85485f,-0.22403f),
        new Vector3(-0.48991f,0.79426f,-0.35937f), new Vector3(-0.48176f,0.78101f,-0.39740f), new Vector3(-0.47354f,0.76596f,-0.43482f),
    };

    static readonly Vector3[] Spine01XDir = {
        new Vector3(0.88836f,-0.01884f,-0.45876f), new Vector3(0.91487f,0.02737f,-0.40283f), new Vector3(0.93759f,0.06822f,-0.34099f), new Vector3(0.95694f,0.10047f,-0.27234f),
        new Vector3(0.97168f,0.12639f,-0.19965f), new Vector3(0.97609f,0.13787f,-0.16806f), new Vector3(0.97978f,0.14705f,-0.13570f), new Vector3(0.97677f,0.15031f,-0.15272f),
        new Vector3(0.97349f,0.15343f,-0.16961f), new Vector3(0.96652f,0.15596f,-0.20376f), new Vector3(0.95836f,0.15838f,-0.23762f), new Vector3(0.94579f,0.16143f,-0.28183f),
        new Vector3(0.93117f,0.16432f,-0.32545f), new Vector3(0.91253f,0.16878f,-0.37257f), new Vector3(0.89144f,0.17308f,-0.41879f), new Vector3(0.86823f,0.17941f,-0.46258f),
        new Vector3(0.84269f,0.18569f,-0.50537f), new Vector3(0.81911f,0.19517f,-0.53941f), new Vector3(0.79376f,0.20475f,-0.57273f), new Vector3(0.77552f,0.21766f,-0.59261f),
        new Vector3(0.75618f,0.23065f,-0.61237f), new Vector3(0.75344f,0.25055f,-0.60791f), new Vector3(0.75006f,0.26902f,-0.60418f), new Vector3(0.76350f,0.29183f,-0.57611f),
        new Vector3(0.77713f,0.31105f,-0.54710f), new Vector3(0.79748f,0.32656f,-0.50732f), new Vector3(0.81887f,0.33959f,-0.46274f), new Vector3(0.83540f,0.34530f,-0.42764f),
        new Vector3(0.85258f,0.35048f,-0.38766f), new Vector3(0.85935f,0.33229f,-0.38872f), new Vector3(0.86588f,0.31446f,-0.38905f),
    };

    static readonly Vector3[] Spine02YDir = {
        new Vector3(0.00473f,0.99973f,-0.02277f), new Vector3(-0.00008f,0.99775f,0.06698f), new Vector3(-0.01030f,0.98775f,0.15573f), new Vector3(-0.02428f,0.97027f,0.24081f),
        new Vector3(-0.04360f,0.94541f,0.32296f), new Vector3(-0.04924f,0.93056f,0.36281f), new Vector3(-0.05521f,0.91404f,0.40186f), new Vector3(-0.04009f,0.91342f,0.40505f),
        new Vector3(-0.02489f,0.91274f,0.40778f), new Vector3(-0.00200f,0.91597f,0.40123f), new Vector3(0.02027f,0.91907f,0.39358f), new Vector3(0.04379f,0.92506f,0.37729f),
        new Vector3(0.06575f,0.93074f,0.35973f), new Vector3(0.08316f,0.93835f,0.33553f), new Vector3(0.09831f,0.94546f,0.31056f), new Vector3(0.10478f,0.95375f,0.28176f),
        new Vector3(0.10873f,0.96130f,0.25313f), new Vector3(0.10111f,0.96896f,0.22558f), new Vector3(0.09150f,0.97571f,0.19906f), new Vector3(0.06950f,0.98197f,0.17580f),
        new Vector3(0.04636f,0.98704f,0.15364f), new Vector3(-0.00693f,0.99216f,0.12481f), new Vector3(-0.05939f,0.99378f,0.09422f), new Vector3(-0.13783f,0.98927f,0.04840f),
        new Vector3(-0.21009f,0.97767f,-0.00475f), new Vector3(-0.27735f,0.95867f,-0.06348f), new Vector3(-0.33469f,0.93346f,-0.12901f), new Vector3(-0.37146f,0.91122f,-0.17802f),
        new Vector3(-0.40180f,0.88647f,-0.22961f), new Vector3(-0.40445f,0.88796f,-0.21898f), new Vector3(-0.40732f,0.88920f,-0.20838f),
    };

    static readonly Vector3[] Spine02XDir = {
        new Vector3(0.88680f,-0.01472f,-0.46191f), new Vector3(0.91209f,0.02754f,-0.40906f), new Vector3(0.93403f,0.06511f,-0.35121f), new Vector3(0.95301f,0.09522f,-0.28757f),
        new Vector3(0.96800f,0.11994f,-0.22042f), new Vector3(0.97112f,0.12951f,-0.20039f), new Vector3(0.97398f,0.13790f,-0.17984f), new Vector3(0.96671f,0.13799f,-0.21550f),
        new Vector3(0.95811f,0.13819f,-0.25084f), new Vector3(0.94203f,0.13634f,-0.30657f), new Vector3(0.92274f,0.13435f,-0.36126f), new Vector3(0.89495f,0.13153f,-0.42635f),
        new Vector3(0.86264f,0.12818f,-0.48931f), new Vector3(0.82377f,0.12474f,-0.55302f), new Vector3(0.78031f,0.12044f,-0.61368f), new Vector3(0.73574f,0.11629f,-0.66721f),
        new Vector3(0.68760f,0.11117f,-0.71753f), new Vector3(0.64702f,0.10820f,-0.75476f), new Vector3(0.60447f,0.10444f,-0.78975f), new Vector3(0.57949f,0.10370f,-0.80835f),
        new Vector3(0.55393f,0.10259f,-0.82622f), new Vector3(0.56872f,0.10657f,-0.81560f), new Vector3(0.58319f,0.11114f,-0.80469f), new Vector3(0.63422f,0.12568f,-0.76287f),
        new Vector3(0.68163f,0.14300f,-0.71759f), new Vector3(0.73270f,0.16831f,-0.65941f), new Vector3(0.77775f,0.19633f,-0.59712f), new Vector3(0.80529f,0.22078f,-0.55024f),
        new Vector3(0.82940f,0.24602f,-0.50157f), new Vector3(0.82514f,0.25103f,-0.50609f), new Vector3(0.82072f,0.25630f,-0.51061f),
    };

    static readonly Vector3[] NeckYDir = {
        new Vector3(-0.06975f,0.96586f,0.24949f), new Vector3(-0.04687f,0.94434f,0.32560f), new Vector3(-0.02891f,0.91521f,0.40195f), new Vector3(-0.01302f,0.87867f,0.47726f),
        new Vector3(-0.00245f,0.83476f,0.55061f), new Vector3(0.00865f,0.80746f,0.58986f), new Vector3(0.01910f,0.77820f,0.62773f), new Vector3(0.03598f,0.76794f,0.63951f),
        new Vector3(0.05349f,0.75743f,0.65073f), new Vector3(0.07609f,0.74763f,0.65974f), new Vector3(0.09967f,0.73748f,0.66797f), new Vector3(0.12519f,0.73007f,0.67181f),
        new Vector3(0.15170f,0.72225f,0.67480f), new Vector3(0.17543f,0.72034f,0.67107f), new Vector3(0.19970f,0.71797f,0.66681f), new Vector3(0.21542f,0.72458f,0.65466f),
        new Vector3(0.23083f,0.73058f,0.64263f), new Vector3(0.23121f,0.74795f,0.62218f), new Vector3(0.23024f,0.76418f,0.60251f), new Vector3(0.20679f,0.79345f,0.57242f),
        new Vector3(0.18095f,0.82011f,0.54284f), new Vector3(0.09347f,0.87649f,0.47226f), new Vector3(0.00410f,0.91907f,0.39408f), new Vector3(-0.13572f,0.95912f,0.24834f),
        new Vector3(-0.25754f,0.96271f,0.08284f), new Vector3(-0.35141f,0.92966f,-0.11063f), new Vector3(-0.40397f,0.86085f,-0.30944f), new Vector3(-0.41050f,0.78786f,-0.45909f),
        new Vector3(-0.38888f,0.69983f,-0.59918f), new Vector3(-0.37230f,0.67984f,-0.63183f), new Vector3(-0.35569f,0.65821f,-0.66351f),
    };

    static readonly Vector3[] NeckXDir = {
        new Vector3(0.98274f,0.10948f,-0.14912f), new Vector3(0.98873f,0.09026f,-0.11946f), new Vector3(0.99286f,0.07286f,-0.09449f), new Vector3(0.99585f,0.05437f,-0.07293f),
        new Vector3(0.99769f,0.03944f,-0.05534f), new Vector3(0.99748f,0.03460f,-0.06200f), new Vector3(0.99711f,0.03132f,-0.06918f), new Vector3(0.99416f,0.03765f,-0.10115f),
        new Vector3(0.99015f,0.04420f,-0.13284f), new Vector3(0.98312f,0.05416f,-0.17476f), new Vector3(0.97425f,0.06413f,-0.21617f), new Vector3(0.96176f,0.07695f,-0.26285f),
        new Vector3(0.94690f,0.08961f,-0.30878f), new Vector3(0.92889f,0.10473f,-0.35525f), new Vector3(0.90828f,0.11968f,-0.40088f), new Vector3(0.88611f,0.13668f,-0.44286f),
        new Vector3(0.86141f,0.15367f,-0.48411f), new Vector3(0.83772f,0.17216f,-0.51826f), new Vector3(0.81175f,0.19065f,-0.55201f), new Vector3(0.79050f,0.20924f,-0.57561f),
        new Vector3(0.76759f,0.22731f,-0.59928f), new Vector3(0.75936f,0.24402f,-0.60318f), new Vector3(0.75175f,0.25704f,-0.60729f), new Vector3(0.76587f,0.26058f,-0.58782f),
        new Vector3(0.78595f,0.25858f,-0.56163f), new Vector3(0.82004f,0.24862f,-0.51548f), new Vector3(0.85817f,0.23948f,-0.45409f), new Vector3(0.88778f,0.23038f,-0.39846f),
        new Vector3(0.91613f,0.22501f,-0.33178f), new Vector3(0.92359f,0.20423f,-0.32447f), new Vector3(0.93066f,0.18427f,-0.31610f),
    };

    static readonly Vector3[] HeadYDir = {
        new Vector3(-0.05117f,0.34717f,0.93641f), new Vector3(-0.05287f,0.29903f,0.95278f), new Vector3(-0.05422f,0.24814f,0.96721f), new Vector3(-0.05731f,0.19836f,0.97845f),
        new Vector3(-0.06016f,0.14644f,0.98739f), new Vector3(-0.03688f,0.10076f,0.99423f), new Vector3(-0.01364f,0.05445f,0.99842f), new Vector3(0.04051f,-0.00058f,0.99918f),
        new Vector3(0.09437f,-0.05570f,0.99398f), new Vector3(0.15849f,-0.12847f,0.97897f), new Vector3(0.22041f,-0.20065f,0.95455f), new Vector3(0.28366f,-0.27633f,0.91825f),
        new Vector3(0.34214f,-0.35038f,0.87188f), new Vector3(0.39617f,-0.41330f,0.81990f), new Vector3(0.44428f,-0.47423f,0.76008f), new Vector3(0.48800f,-0.51192f,0.70696f),
        new Vector3(0.52718f,-0.54840f,0.64911f), new Vector3(0.56652f,-0.55211f,0.61174f), new Vector3(0.60393f,-0.55551f,0.57155f), new Vector3(0.64573f,-0.51540f,0.56338f),
        new Vector3(0.68579f,-0.47399f,0.55229f), new Vector3(0.73309f,-0.30765f,0.60657f), new Vector3(0.75503f,-0.13185f,0.64230f), new Vector3(0.70464f,0.18166f,0.68591f),
        new Vector3(0.58901f,0.47516f,0.65367f), new Vector3(0.38197f,0.74158f,0.55150f), new Vector3(0.15976f,0.92066f,0.35617f), new Vector3(-0.02652f,0.98606f,0.16424f),
        new Vector3(-0.18173f,0.98154f,-0.05960f), new Vector3(-0.21882f,0.96450f,-0.14783f), new Vector3(-0.25257f,0.93830f,-0.23621f),
    };

    static readonly Vector3[] HeadXDir = {
        new Vector3(0.99631f,0.08241f,0.02389f), new Vector3(0.99631f,0.08046f,0.03004f), new Vector3(0.99594f,0.08314f,0.03450f), new Vector3(0.99595f,0.07940f,0.04224f),
        new Vector3(0.99553f,0.08094f,0.04865f), new Vector3(0.99677f,0.07479f,0.02939f), new Vector3(0.99740f,0.07146f,0.00973f), new Vector3(0.99720f,0.06295f,-0.04039f),
        new Vector3(0.99425f,0.05603f,-0.09126f), new Vector3(0.98699f,0.04783f,-0.15351f), new Vector3(0.97540f,0.04148f,-0.21650f), new Vector3(0.95794f,0.03828f,-0.28440f),
        new Vector3(0.93521f,0.03688f,-0.35217f), new Vector3(0.90755f,0.04081f,-0.41796f), new Vector3(0.87468f,0.04609f,-0.48250f), new Vector3(0.84018f,0.05601f,-0.53940f),
        new Vector3(0.80131f,0.06659f,-0.59453f), new Vector3(0.76581f,0.07862f,-0.63825f), new Vector3(0.72714f,0.09036f,-0.68051f), new Vector3(0.69806f,0.09946f,-0.70910f),
        new Vector3(0.66742f,0.10695f,-0.73697f), new Vector3(0.65978f,0.10514f,-0.74407f), new Vector3(0.65464f,0.09612f,-0.74980f), new Vector3(0.68301f,0.08830f,-0.72505f),
        new Vector3(0.71529f,0.06987f,-0.69533f), new Vector3(0.76519f,0.08085f,-0.63870f), new Vector3(0.80842f,0.08504f,-0.58243f), new Vector3(0.84234f,0.11051f,-0.52750f),
        new Vector3(0.86817f,0.13169f,-0.47847f), new Vector3(0.88120f,0.13026f,-0.45445f), new Vector3(0.89225f,0.13142f,-0.43200f),
    };

    static readonly Vector3[] LeftShoulderYDir = {
        new Vector3(0.99099f,0.11213f,-0.07326f), new Vector3(0.98235f,0.18305f,-0.03855f), new Vector3(0.96702f,0.25444f,0.01137f), new Vector3(0.94537f,0.31772f,0.07303f),
        new Vector3(0.91462f,0.37644f,0.14750f), new Vector3(0.89687f,0.40080f,0.18704f), new Vector3(0.87707f,0.42208f,0.22933f), new Vector3(0.87987f,0.41577f,0.23014f),
        new Vector3(0.88289f,0.40892f,0.23083f), new Vector3(0.89325f,0.39068f,0.22245f), new Vector3(0.90336f,0.37194f,0.21354f), new Vector3(0.91648f,0.34891f,0.19579f),
        new Vector3(0.92867f,0.32563f,0.17762f), new Vector3(0.94007f,0.30537f,0.15173f), new Vector3(0.95015f,0.28524f,0.12593f), new Vector3(0.95655f,0.27605f,0.09387f),
        new Vector3(0.96159f,0.26729f,0.06237f), new Vector3(0.96126f,0.27431f,0.02705f), new Vector3(0.95951f,0.28156f,-0.00766f), new Vector3(0.94991f,0.30903f,-0.04653f),
        new Vector3(0.93824f,0.33527f,-0.08536f), new Vector3(0.90285f,0.40342f,-0.14872f), new Vector3(0.86312f,0.45641f,-0.21615f), new Vector3(0.80020f,0.50577f,-0.32229f),
        new Vector3(0.74879f,0.50566f,-0.42851f), new Vector3(0.71535f,0.45003f,-0.53456f), new Vector3(0.71446f,0.34432f,-0.60909f), new Vector3(0.73222f,0.23073f,-0.64079f),
        new Vector3(0.76565f,0.10170f,-0.63516f), new Vector3(0.77054f,0.04436f,-0.63584f), new Vector3(0.77453f,-0.01299f,-0.63240f),
    };

    static readonly Vector3[] LeftShoulderXDir = {
        new Vector3(-0.11338f,0.41105f,-0.90454f), new Vector3(-0.12964f,0.51761f,-0.84574f), new Vector3(-0.15145f,0.61033f,-0.77753f), new Vector3(-0.17811f,0.69098f,-0.70058f),
        new Vector3(-0.21095f,0.75555f,-0.62020f), new Vector3(-0.23055f,0.78453f,-0.57564f), new Vector3(-0.25069f,0.80944f,-0.53100f), new Vector3(-0.25032f,0.81715f,-0.51923f),
        new Vector3(-0.24943f,0.82490f,-0.50728f), new Vector3(-0.23929f,0.83205f,-0.50044f), new Vector3(-0.22862f,0.83887f,-0.49400f), new Vector3(-0.21475f,0.84190f,-0.49506f),
        new Vector3(-0.20092f,0.84414f,-0.49705f), new Vector3(-0.19101f,0.84019f,-0.50754f), new Vector3(-0.18204f,0.83536f,-0.51868f), new Vector3(-0.18477f,0.82296f,-0.53721f),
        new Vector3(-0.18913f,0.80994f,-0.55518f), new Vector3(-0.20894f,0.78913f,-0.57760f), new Vector3(-0.23005f,0.76771f,-0.59808f), new Vector3(-0.27002f,0.73662f,-0.62006f),
        new Vector3(-0.30966f,0.70376f,-0.63940f), new Vector3(-0.38979f,0.62195f,-0.67915f), new Vector3(-0.45859f,0.52914f,-0.71393f), new Vector3(-0.53331f,0.35429f,-0.76816f),
        new Vector3(-0.57236f,0.16728f,-0.80276f), new Vector3(-0.57722f,-0.05058f,-0.81502f), new Vector3(-0.55383f,-0.25368f,-0.79305f), new Vector3(-0.52638f,-0.40531f,-0.74743f),
        new Vector3(-0.49648f,-0.53440f,-0.68405f), new Vector3(-0.50602f,-0.56400f,-0.65257f), new Vector3(-0.51606f,-0.59112f,-0.61989f),
    };

    static readonly Vector3[] RightShoulderYDir = {
        new Vector3(-0.58832f,-0.01789f,0.80843f), new Vector3(-0.66723f,-0.07206f,0.74135f), new Vector3(-0.73813f,-0.11619f,0.66458f), new Vector3(-0.80098f,-0.14615f,0.58058f),
        new Vector3(-0.85467f,-0.16494f,0.49228f), new Vector3(-0.87459f,-0.16639f,0.45543f), new Vector3(-0.89280f,-0.16481f,0.41922f), new Vector3(-0.88183f,-0.16259f,0.44266f),
        new Vector3(-0.87018f,-0.16047f,0.46588f), new Vector3(-0.84431f,-0.16180f,0.51085f), new Vector3(-0.81610f,-0.16321f,0.55438f), new Vector3(-0.77672f,-0.16576f,0.60765f),
        new Vector3(-0.73384f,-0.16805f,0.65820f), new Vector3(-0.68567f,-0.16887f,0.70805f), new Vector3(-0.63407f,-0.16913f,0.75455f), new Vector3(-0.58616f,-0.16487f,0.79324f),
        new Vector3(-0.53589f,-0.16011f,0.82897f), new Vector3(-0.50127f,-0.15108f,0.85200f), new Vector3(-0.46579f,-0.14223f,0.87339f), new Vector3(-0.45919f,-0.12932f,0.87887f),
        new Vector3(-0.45294f,-0.11835f,0.88365f), new Vector3(-0.51602f,-0.10111f,0.85059f), new Vector3(-0.57732f,-0.09559f,0.81091f), new Vector3(-0.69347f,-0.11226f,0.71168f),
        new Vector3(-0.78599f,-0.15852f,0.59758f), new Vector3(-0.85786f,-0.23836f,0.45526f), new Vector3(-0.88454f,-0.34304f,0.31609f), new Vector3(-0.87380f,-0.43531f,0.21674f),
        new Vector3(-0.83554f,-0.53291f,0.13372f), new Vector3(-0.82960f,-0.54496f,0.12160f), new Vector3(-0.82213f,-0.55834f,0.11116f),
    };

    static readonly Vector3[] RightShoulderXDir = {
        new Vector3(0.74445f,-0.40231f,0.53286f), new Vector3(0.67760f,-0.47200f,0.56398f), new Vector3(0.60652f,-0.54569f,0.57824f), new Vector3(0.52999f,-0.62415f,0.57406f),
        new Vector3(0.45274f,-0.70085f,0.55121f), new Vector3(0.41565f,-0.74094f,0.52749f), new Vector3(0.37836f,-0.77938f,0.49940f), new Vector3(0.38698f,-0.78595f,0.48221f),
        new Vector3(0.39494f,-0.79251f,0.46471f), new Vector3(0.41964f,-0.79250f,0.44254f), new Vector3(0.44307f,-0.79261f,0.41889f), new Vector3(0.47596f,-0.78633f,0.39389f),
        new Vector3(0.50712f,-0.78021f,0.36620f), new Vector3(0.54198f,-0.76777f,0.34173f), new Vector3(0.57514f,-0.75541f,0.31398f), new Vector3(0.60804f,-0.73657f,0.29622f),
        new Vector3(0.63997f,-0.71745f,0.27515f), new Vector3(0.66985f,-0.69105f,0.27156f), new Vector3(0.69954f,-0.66364f,0.26500f), new Vector3(0.72376f,-0.62812f,0.28572f),
        new Vector3(0.74754f,-0.59052f,0.30408f), new Vector3(0.75905f,-0.51415f,0.39937f), new Vector3(0.75830f,-0.43106f,0.48905f), new Vector3(0.70752f,-0.29260f,0.64327f),
        new Vector3(0.61738f,-0.15034f,0.77216f), new Vector3(0.46818f,0.00273f,0.88363f), new Vector3(0.28432f,0.14072f,0.94835f), new Vector3(0.12004f,0.23882f,0.96361f),
        new Vector3(-0.05158f,0.31840f,0.94655f), new Vector3(-0.08952f,0.34478f,0.93440f), new Vector3(-0.12716f,0.37041f,0.92012f),
    };

    // 平均姿勢。static フィールドは宣言順に初期化されるので、上の配列より後に置くこと。
    static readonly Quaternion SpineMean = MeanBasis(SpineYDir, SpineXDir);
    static readonly Quaternion Spine01Mean = MeanBasis(Spine01YDir, Spine01XDir);
    static readonly Quaternion Spine02Mean = MeanBasis(Spine02YDir, Spine02XDir);
    static readonly Quaternion NeckMean = MeanBasis(NeckYDir, NeckXDir);
    static readonly Quaternion HeadMean = MeanBasis(HeadYDir, HeadXDir);
    static readonly Quaternion LeftShoulderMean = MeanBasis(LeftShoulderYDir, LeftShoulderXDir);
    static readonly Quaternion RightShoulderMean = MeanBasis(RightShoulderYDir, RightShoulderXDir);
}
