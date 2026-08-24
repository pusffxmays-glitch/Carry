using UnityEngine;

// ジャンプ姿勢セット。GoblinCarryRig.ApplyJumpPose が IGoblinJumpPoses 越しに引く。
// 生成元: Running フレーム 9〜19 -- bake_jump_cs.py (2026-08-24)
//
// 手・肘・指は入っていない: そこは SolveArm の IK が壺の位置から解く。
// ループしないので端はクランプする (歩行の Repeat と違う点)。
public sealed class GoblinJumpRun : IGoblinJumpPoses
{
    public static readonly GoblinJumpRun I = new GoblinJumpRun();
    GoblinJumpRun() { }

    public const int FrameCount = 21;
    public const float GroundY = -0.01134f;

    public float UCrouch { get { return 0.0000f; } }
    public float UExtend { get { return 0.1000f; } }
    public float UAir    { get { return 0.5000f; } }
    public float ULand   { get { return 1.0000f; } }
    /// <summary>踏切の瞬間に接地している側。true = リグの leftFootBone 側。</summary>
    public bool SupportIsLeftSide { get { return true; } }

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
        new Vector3(-0.00945f,0.83445f,-0.04457f), new Vector3(-0.00806f,0.83770f,-0.03279f), new Vector3(-0.00668f,0.84096f,-0.02100f), new Vector3(-0.00396f,0.84762f,-0.00239f),
        new Vector3(-0.00124f,0.85429f,0.01622f), new Vector3(0.00170f,0.86670f,0.02373f), new Vector3(0.00463f,0.87911f,0.03124f), new Vector3(0.00445f,0.88653f,0.03137f),
        new Vector3(0.00426f,0.89394f,0.03150f), new Vector3(0.00398f,0.89904f,0.02414f), new Vector3(0.00370f,0.90414f,0.01678f), new Vector3(0.00317f,0.90382f,0.01257f),
        new Vector3(0.00263f,0.90350f,0.00837f), new Vector3(0.00238f,0.89813f,0.00100f), new Vector3(0.00212f,0.89277f,-0.00636f), new Vector3(0.00191f,0.88437f,-0.00792f),
        new Vector3(0.00171f,0.87598f,-0.00948f), new Vector3(0.00021f,0.87223f,-0.01263f), new Vector3(-0.00129f,0.86849f,-0.01577f), new Vector3(-0.00249f,0.86291f,-0.01992f),
        new Vector3(-0.00369f,0.85733f,-0.02406f),
    };

    static readonly Vector3[] HipsYDir = {
        new Vector3(-0.19343f,0.97878f,-0.06767f), new Vector3(-0.17594f,0.98266f,-0.05860f), new Vector3(-0.15836f,0.98614f,-0.04952f), new Vector3(-0.14028f,0.98896f,-0.04769f),
        new Vector3(-0.12215f,0.99145f,-0.04590f), new Vector3(-0.11194f,0.99263f,-0.04643f), new Vector3(-0.10171f,0.99371f,-0.04696f), new Vector3(-0.09430f,0.99448f,-0.04591f),
        new Vector3(-0.08690f,0.99521f,-0.04483f), new Vector3(-0.07932f,0.99559f,-0.05014f), new Vector3(-0.07174f,0.99588f,-0.05543f), new Vector3(-0.06632f,0.99554f,-0.06714f),
        new Vector3(-0.06081f,0.99503f,-0.07879f), new Vector3(-0.05536f,0.99462f,-0.08761f), new Vector3(-0.04973f,0.99411f,-0.09631f), new Vector3(-0.04613f,0.99404f,-0.09882f),
        new Vector3(-0.04244f,0.99396f,-0.10120f), new Vector3(-0.02407f,0.99436f,-0.10330f), new Vector3(-0.00562f,0.99448f,-0.10477f), new Vector3(0.01661f,0.99349f,-0.11270f),
        new Vector3(0.03893f,0.99198f,-0.12029f),
    };

    static readonly Vector3[] HipsXDir = {
        new Vector3(0.96774f,0.17899f,-0.17730f), new Vector3(0.97040f,0.16313f,-0.17808f), new Vector3(0.97277f,0.14723f,-0.17901f), new Vector3(0.97485f,0.12954f,-0.18137f),
        new Vector3(0.97659f,0.11181f,-0.18376f), new Vector3(0.97788f,0.10172f,-0.18277f), new Vector3(0.97906f,0.09162f,-0.18177f), new Vector3(0.98050f,0.08479f,-0.17729f),
        new Vector3(0.98187f,0.07795f,-0.17281f), new Vector3(0.98266f,0.06964f,-0.17186f), new Vector3(0.98338f,0.06133f,-0.17086f), new Vector3(0.98508f,0.05462f,-0.16323f),
        new Vector3(0.98667f,0.04798f,-0.15550f), new Vector3(0.98990f,0.04321f,-0.13499f), new Vector3(0.99269f,0.03858f,-0.11437f), new Vector3(0.99611f,0.03834f,-0.07936f),
        new Vector3(0.99829f,0.03812f,-0.04425f), new Vector3(0.99968f,0.02319f,-0.00971f), new Vector3(0.99966f,0.00827f,0.02486f), new Vector3(0.99912f,-0.01215f,0.04009f),
        new Vector3(0.99793f,-0.03243f,0.05548f),
    };

    static readonly Vector3[] LegLeftUpLegYDir = {
        new Vector3(0.20589f,-0.93626f,0.28465f), new Vector3(0.21830f,-0.96555f,0.14163f), new Vector3(0.22432f,-0.97450f,-0.00467f), new Vector3(0.21074f,-0.95977f,-0.18554f),
        new Vector3(0.18646f,-0.91397f,-0.36041f), new Vector3(0.16509f,-0.89149f,-0.42189f), new Vector3(0.14476f,-0.86435f,-0.48160f), new Vector3(0.13039f,-0.87268f,-0.47056f),
        new Vector3(0.11489f,-0.88036f,-0.46018f), new Vector3(0.11678f,-0.90154f,-0.41663f), new Vector3(0.11769f,-0.92072f,-0.37205f), new Vector3(0.11781f,-0.93344f,-0.33883f),
        new Vector3(0.11734f,-0.94503f,-0.30519f), new Vector3(0.10685f,-0.96228f,-0.25019f), new Vector3(0.09293f,-0.97635f,-0.19521f), new Vector3(0.13075f,-0.98041f,-0.14730f),
        new Vector3(0.16698f,-0.98108f,-0.09800f), new Vector3(0.19757f,-0.97997f,0.02496f), new Vector3(0.22402f,-0.96339f,0.14730f), new Vector3(0.22069f,-0.91832f,0.32862f),
        new Vector3(0.21145f,-0.84097f,0.49805f),
    };

    static readonly Vector3[] LegLeftUpLegXDir = {
        new Vector3(0.92351f,0.08970f,-0.37294f), new Vector3(0.90632f,0.14678f,-0.39628f), new Vector3(0.88920f,0.20664f,-0.40821f), new Vector3(0.86424f,0.27162f,-0.42345f),
        new Vector3(0.84078f,0.33822f,-0.42272f), new Vector3(0.84932f,0.34597f,-0.39870f), new Vector3(0.85825f,0.35189f,-0.37359f), new Vector3(0.89411f,0.30859f,-0.32455f),
        new Vector3(0.92499f,0.26372f,-0.27357f), new Vector3(0.93109f,0.24535f,-0.26994f), new Vector3(0.93693f,0.22712f,-0.26568f), new Vector3(0.94034f,0.21453f,-0.26406f),
        new Vector3(0.94370f,0.20182f,-0.26209f), new Vector3(0.95830f,0.16674f,-0.23206f), new Vector3(0.97091f,0.13231f,-0.19959f), new Vector3(0.96504f,0.15989f,-0.20767f),
        new Vector3(0.95845f,0.18483f,-0.21729f), new Vector3(0.95224f,0.18580f,-0.24232f), new Vector3(0.94638f,0.17894f,-0.26896f), new Vector3(0.95348f,0.13218f,-0.27094f),
        new Vector3(0.95996f,0.08291f,-0.26757f),
    };

    static readonly Vector3[] LegLeftLegYDir = {
        new Vector3(-0.21851f,-0.67976f,-0.70013f), new Vector3(-0.22167f,-0.61674f,-0.75531f), new Vector3(-0.22404f,-0.54737f,-0.80635f), new Vector3(-0.24282f,-0.52001f,-0.81892f),
        new Vector3(-0.25027f,-0.48976f,-0.83517f), new Vector3(-0.24430f,-0.41349f,-0.87712f), new Vector3(-0.23656f,-0.33287f,-0.91282f), new Vector3(-0.20092f,-0.18679f,-0.96163f),
        new Vector3(-0.13582f,-0.04048f,-0.98991f), new Vector3(-0.12578f,0.11726f,-0.98510f), new Vector3(-0.10228f,0.27194f,-0.95686f), new Vector3(-0.09942f,0.41901f,-0.90252f),
        new Vector3(-0.09761f,0.55549f,-0.82577f), new Vector3(-0.07465f,0.66653f,-0.74173f), new Vector3(-0.04871f,0.76484f,-0.64238f), new Vector3(-0.08784f,0.83659f,-0.54074f),
        new Vector3(-0.12992f,0.89273f,-0.43146f), new Vector3(-0.14751f,0.87743f,-0.45646f), new Vector3(-0.17080f,0.86038f,-0.48018f), new Vector3(-0.14000f,0.77222f,-0.61974f),
        new Vector3(-0.10661f,0.66228f,-0.74163f),
    };

    static readonly Vector3[] LegLeftLegXDir = {
        new Vector3(0.97520f,-0.12617f,-0.18186f), new Vector3(0.97287f,-0.08730f,-0.21424f), new Vector3(0.97016f,-0.04654f,-0.23796f), new Vector3(0.95540f,0.01809f,-0.29477f),
        new Vector3(0.93804f,0.09087f,-0.33439f), new Vector3(0.92499f,0.17211f,-0.33877f), new Vector3(0.90989f,0.25363f,-0.32829f), new Vector3(0.95865f,0.16449f,-0.23225f),
        new Vector3(0.98818f,0.06617f,-0.13829f), new Vector3(0.99205f,0.01897f,-0.12441f), new Vector3(0.99312f,-0.02718f,-0.11388f), new Vector3(0.99362f,-0.00673f,-0.11258f),
        new Vector3(0.99408f,0.01471f,-0.10761f), new Vector3(0.99453f,-0.00475f,-0.10437f), new Vector3(0.99494f,-0.01945f,-0.09860f), new Vector3(0.99369f,0.03560f,-0.10635f),
        new Vector3(0.98949f,0.08885f,-0.11412f), new Vector3(0.98855f,0.14567f,-0.03945f), new Vector3(0.97779f,0.20807f,0.02501f), new Vector3(0.97703f,0.20929f,0.04006f),
        new Vector3(0.97531f,0.21476f,0.05157f),
    };

    static readonly Vector3[] LegLeftFootYDir = {
        new Vector3(0.10080f,-0.67628f,0.72971f), new Vector3(0.08202f,-0.78752f,0.61081f), new Vector3(0.05987f,-0.87901f,0.47302f), new Vector3(0.12456f,-0.96038f,0.24931f),
        new Vector3(0.15883f,-0.98727f,0.00827f), new Vector3(0.17646f,-0.95022f,-0.25680f), new Vector3(0.18509f,-0.84408f,-0.50327f), new Vector3(0.07706f,-0.77754f,-0.62409f),
        new Vector3(0.00276f,-0.67105f,-0.74141f), new Vector3(-0.06856f,-0.50998f,-0.85745f), new Vector3(-0.11994f,-0.32200f,-0.93911f), new Vector3(-0.09278f,-0.21040f,-0.97320f),
        new Vector3(-0.06495f,-0.09512f,-0.99334f), new Vector3(-0.05102f,-0.06074f,-0.99685f), new Vector3(-0.03456f,-0.02579f,-0.99907f), new Vector3(-0.01809f,-0.07860f,-0.99674f),
        new Vector3(-0.00210f,-0.13158f,-0.99130f), new Vector3(0.02390f,-0.18384f,-0.98267f), new Vector3(0.05018f,-0.24066f,-0.96931f), new Vector3(0.11888f,-0.34126f,-0.93242f),
        new Vector3(0.18364f,-0.43649f,-0.88077f),
    };

    static readonly Vector3[] LegLeftFootXDir = {
        new Vector3(0.98733f,-0.02235f,-0.15711f), new Vector3(0.99099f,-0.00066f,-0.13393f), new Vector3(0.99423f,0.01025f,-0.10678f), new Vector3(0.97051f,0.06566f,-0.23196f),
        new Vector3(0.93035f,0.14686f,-0.33599f), new Vector3(0.92002f,0.25196f,-0.30012f), new Vector3(0.90749f,0.34332f,-0.24207f), new Vector3(0.95627f,0.23478f,-0.17443f),
        new Vector3(0.98552f,0.12754f,-0.11177f), new Vector3(0.99065f,0.06683f,-0.11896f), new Vector3(0.99106f,0.01679f,-0.13233f), new Vector3(0.99366f,0.04276f,-0.10398f),
        new Vector3(0.99547f,0.06305f,-0.07113f), new Vector3(0.99820f,0.02837f,-0.05282f), new Vector3(0.99940f,-0.00391f,-0.03448f), new Vector3(0.99956f,0.02186f,-0.01987f),
        new Vector3(0.99897f,0.04468f,-0.00805f), new Vector3(0.98256f,0.18564f,-0.01083f), new Vector3(0.94604f,0.32256f,-0.03111f), new Vector3(0.94466f,0.32804f,0.00038f),
        new Vector3(0.93867f,0.34389f,0.02529f),
    };

    static readonly Vector3[] LegLeftToeYDir = {
        new Vector3(0.18005f,-0.16107f,0.97038f), new Vector3(0.15403f,-0.23913f,0.95869f), new Vector3(0.12513f,-0.31908f,0.93943f), new Vector3(0.25043f,-0.52904f,0.81080f),
        new Vector3(0.35581f,-0.69759f,0.62191f), new Vector3(0.36692f,-0.88005f,0.30145f), new Vector3(0.36907f,-0.92691f,-0.06803f), new Vector3(0.23122f,-0.95168f,-0.20209f),
        new Vector3(0.11409f,-0.93042f,-0.34828f), new Vector3(0.02528f,-0.84529f,-0.53371f), new Vector3(-0.04933f,-0.71512f,-0.69726f), new Vector3(-0.02249f,-0.62794f,-0.77793f),
        new Vector3(0.00531f,-0.53154f,-0.84702f), new Vector3(0.00047f,-0.50400f,-0.86370f), new Vector3(-0.00064f,-0.47478f,-0.88010f), new Vector3(0.02654f,-0.52275f,-0.85207f),
        new Vector3(0.05137f,-0.56920f,-0.82059f), new Vector3(0.13675f,-0.60113f,-0.78736f), new Vector3(0.22067f,-0.62597f,-0.74797f), new Vector3(0.27581f,-0.69929f,-0.65948f),
        new Vector3(0.32747f,-0.76050f,-0.56071f),
    };

    static readonly Vector3[] LegLeftToeXDir = {
        new Vector3(0.98365f,0.02600f,-0.17820f), new Vector3(0.98807f,0.03734f,-0.14943f), new Vector3(0.99214f,0.03741f,-0.11945f), new Vector3(0.96682f,0.09309f,-0.23789f),
        new Vector3(0.92596f,0.17306f,-0.33564f), new Vector3(0.91713f,0.28800f,-0.27554f), new Vector3(0.90751f,0.37520f,-0.18881f), new Vector3(0.95894f,0.25799f,-0.11775f),
        new Vector3(0.98860f,0.14099f,-0.05281f), new Vector3(0.99596f,0.06729f,-0.05941f), new Vector3(0.99718f,0.00430f,-0.07496f), new Vector3(0.99857f,0.02362f,-0.04794f),
        new Vector3(0.99915f,0.03741f,-0.01722f), new Vector3(1.00000f,0.00091f,0.00001f), new Vector3(0.99930f,-0.03314f,0.01715f), new Vector3(0.99941f,-0.00485f,0.03410f),
        new Vector3(0.99863f,0.02070f,0.04816f), new Vector3(0.98523f,0.16526f,0.04494f), new Vector3(0.95154f,0.30659f,0.02414f), new Vector3(0.94621f,0.31828f,0.05823f),
        new Vector3(0.93634f,0.34070f,0.08476f),
    };

    static readonly Vector3[] LegRightUpLegYDir = {
        new Vector3(-0.08981f,-0.66568f,0.74081f), new Vector3(-0.07228f,-0.61083f,0.78846f), new Vector3(-0.05126f,-0.55350f,0.83127f), new Vector3(-0.04895f,-0.51682f,0.85469f),
        new Vector3(-0.04562f,-0.47927f,0.87648f), new Vector3(-0.04874f,-0.46760f,0.88260f), new Vector3(-0.05170f,-0.45584f,0.88856f), new Vector3(-0.04068f,-0.49028f,0.87062f),
        new Vector3(-0.03079f,-0.52435f,0.85094f), new Vector3(-0.02738f,-0.59738f,0.80149f), new Vector3(-0.02442f,-0.66576f,0.74576f), new Vector3(-0.01027f,-0.69505f,0.71889f),
        new Vector3(0.00396f,-0.72309f,0.69074f), new Vector3(-0.02242f,-0.75720f,0.65280f), new Vector3(-0.04457f,-0.78944f,0.61221f), new Vector3(-0.05328f,-0.77325f,0.63186f),
        new Vector3(-0.06453f,-0.75598f,0.65141f), new Vector3(-0.06996f,-0.79459f,0.60310f), new Vector3(-0.07211f,-0.82986f,0.55329f), new Vector3(-0.08745f,-0.88131f,0.46438f),
        new Vector3(-0.09851f,-0.92346f,0.37085f),
    };

    static readonly Vector3[] LegRightUpLegXDir = {
        new Vector3(0.99544f,-0.03594f,0.08838f), new Vector3(0.99738f,-0.04225f,0.05870f), new Vector3(0.99834f,-0.05026f,0.02810f), new Vector3(0.99827f,-0.05326f,0.02497f),
        new Vector3(0.99819f,-0.05629f,0.02117f), new Vector3(0.99787f,-0.06124f,0.02266f), new Vector3(0.99752f,-0.06620f,0.02408f), new Vector3(0.99646f,-0.08408f,-0.00079f),
        new Vector3(0.99457f,-0.10072f,-0.02607f), new Vector3(0.99467f,-0.09603f,-0.03760f), new Vector3(0.99476f,-0.09027f,-0.04801f), new Vector3(0.99497f,-0.07875f,-0.06193f),
        new Vector3(0.99494f,-0.06648f,-0.07529f), new Vector3(0.99886f,-0.04446f,-0.01727f), new Vector3(0.99883f,-0.02383f,0.04200f), new Vector3(0.99463f,0.01513f,0.10239f),
        new Vector3(0.98541f,0.05475f,0.16116f), new Vector3(0.97912f,0.06098f,0.19392f), new Vector3(0.97142f,0.06735f,0.22762f), new Vector3(0.96222f,0.04593f,0.26836f),
        new Vector3(0.95100f,0.02240f,0.30839f),
    };

    static readonly Vector3[] LegRightLegYDir = {
        new Vector3(-0.02826f,0.50215f,-0.86432f), new Vector3(-0.04693f,0.33527f,-0.94095f), new Vector3(-0.07501f,0.15769f,-0.98464f), new Vector3(-0.08154f,-0.03318f,-0.99612f),
        new Vector3(-0.09023f,-0.22265f,-0.97071f), new Vector3(-0.09698f,-0.41451f,-0.90486f), new Vector3(-0.09667f,-0.58945f,-0.80200f), new Vector3(-0.10809f,-0.73059f,-0.67420f),
        new Vector3(-0.10372f,-0.84547f,-0.52385f), new Vector3(-0.10989f,-0.93266f,-0.34360f), new Vector3(-0.09753f,-0.98382f,-0.15029f), new Vector3(-0.08575f,-0.99630f,-0.00550f),
        new Vector3(-0.07464f,-0.98743f,0.13933f), new Vector3(-0.04032f,-0.98879f,0.14379f), new Vector3(-0.00856f,-0.98833f,0.15207f), new Vector3(0.00312f,-0.99546f,-0.09512f),
        new Vector3(0.01731f,-0.94258f,-0.33353f), new Vector3(0.01393f,-0.88036f,-0.47410f), new Vector3(0.01249f,-0.79852f,-0.60184f), new Vector3(0.02642f,-0.72196f,-0.69143f),
        new Vector3(0.04632f,-0.63578f,-0.77048f),
    };

    static readonly Vector3[] LegRightLegXDir = {
        new Vector3(0.97509f,-0.17647f,-0.13441f), new Vector3(0.96510f,-0.22773f,-0.12928f), new Vector3(0.95495f,-0.27297f,-0.11647f), new Vector3(0.95288f,-0.29558f,-0.06816f),
        new Vector3(0.95129f,-0.30778f,-0.01784f), new Vector3(0.96236f,-0.27095f,0.02098f), new Vector3(0.97254f,-0.22733f,0.04986f), new Vector3(0.98246f,-0.18218f,0.03990f),
        new Vector3(0.99087f,-0.13343f,0.01916f), new Vector3(0.99364f,-0.11167f,-0.01468f), new Vector3(0.99461f,-0.09103f,-0.04961f), new Vector3(0.99603f,-0.08559f,-0.02426f),
        new Vector3(0.99717f,-0.07511f,0.00186f), new Vector3(0.99777f,-0.03217f,0.05854f), new Vector3(0.99308f,0.00942f,0.11710f), new Vector3(0.99627f,-0.00511f,0.08619f),
        new Vector3(0.99899f,0.00250f,0.04478f), new Vector3(0.99984f,0.00689f,0.01657f), new Vector3(0.99965f,0.02403f,-0.01114f), new Vector3(0.99964f,0.01620f,0.02128f),
        new Vector3(0.99853f,0.00773f,0.05364f),
    };

    static readonly Vector3[] LegRightFootYDir = {
        new Vector3(-0.27334f,-0.53457f,-0.79970f), new Vector3(-0.30180f,-0.70907f,-0.63728f), new Vector3(-0.32604f,-0.83964f,-0.43441f), new Vector3(-0.31889f,-0.89825f,-0.30242f),
        new Vector3(-0.30862f,-0.93741f,-0.16128f), new Vector3(-0.27618f,-0.96108f,0.00756f), new Vector3(-0.24032f,-0.95429f,0.17769f), new Vector3(-0.18441f,-0.91615f,0.35591f),
        new Vector3(-0.11578f,-0.84618f,0.52016f), new Vector3(-0.03028f,-0.71497f,0.69850f), new Vector3(0.06283f,-0.54504f,0.83605f), new Vector3(0.04010f,-0.55448f,0.83123f),
        new Vector3(0.01950f,-0.56411f,0.82547f), new Vector3(0.00260f,-0.67442f,0.73834f), new Vector3(-0.00326f,-0.77044f,0.63750f), new Vector3(-0.04975f,-0.72365f,0.68837f),
        new Vector3(-0.04992f,-0.66719f,0.74321f), new Vector3(-0.03604f,-0.69626f,0.71689f), new Vector3(-0.01365f,-0.72298f,0.69073f), new Vector3(-0.00639f,-0.77679f,0.62973f),
        new Vector3(0.00524f,-0.82547f,0.56442f),
    };

    static readonly Vector3[] LegRightFootXDir = {
        new Vector3(0.96051f,-0.19662f,-0.19688f), new Vector3(0.95155f,-0.26536f,-0.15538f), new Vector3(0.94405f,-0.31329f,-0.10301f), new Vector3(0.94258f,-0.33398f,-0.00191f),
        new Vector3(0.93993f,-0.32656f,0.09945f), new Vector3(0.95024f,-0.27188f,0.15205f), new Vector3(0.95960f,-0.20595f,0.19172f), new Vector3(0.96854f,-0.10781f,0.22431f),
        new Vector3(0.97313f,0.00829f,0.23009f), new Vector3(0.98458f,0.09912f,0.14414f), new Vector3(0.98436f,0.17201f,0.03816f), new Vector3(0.98284f,0.17181f,0.06720f),
        new Vector3(0.97942f,0.17667f,0.09760f), new Vector3(0.97394f,0.16916f,0.15109f), new Vector3(0.96661f,0.16092f,0.19943f), new Vector3(0.98438f,0.08100f,0.15629f),
        new Vector3(0.99574f,0.02449f,0.08887f), new Vector3(0.99785f,0.01418f,0.06394f), new Vector3(0.99919f,0.01638f,0.03689f), new Vector3(0.99991f,0.00258f,0.01334f),
        new Vector3(0.99995f,-0.00077f,-0.01042f),
    };

    static readonly Vector3[] LegRightToeYDir = {
        new Vector3(-0.30008f,-0.84232f,-0.44772f), new Vector3(-0.32813f,-0.91975f,-0.21539f), new Vector3(-0.34491f,-0.93812f,0.03123f), new Vector3(-0.36081f,-0.91628f,0.17391f),
        new Vector3(-0.37270f,-0.87430f,0.31095f), new Vector3(-0.34307f,-0.81720f,0.46313f), new Vector3(-0.31234f,-0.73421f,0.60281f), new Vector3(-0.27205f,-0.61660f,0.73878f),
        new Vector3(-0.22682f,-0.47971f,0.84760f), new Vector3(-0.13905f,-0.28740f,0.94766f), new Vector3(-0.05380f,-0.07541f,0.99570f), new Vector3(-0.09948f,0.04209f,0.99415f),
        new Vector3(-0.14770f,0.15746f,0.97642f), new Vector3(-0.18399f,0.05722f,0.98126f), new Vector3(-0.21601f,-0.04270f,0.97546f), new Vector3(-0.17111f,-0.11733f,0.97824f),
        new Vector3(-0.11474f,-0.19272f,0.97452f), new Vector3(-0.09106f,-0.19184f,0.97719f), new Vector3(-0.06308f,-0.18976f,0.97980f), new Vector3(-0.03956f,-0.18724f,0.98152f),
        new Vector3(-0.01460f,-0.18520f,0.98259f),
    };

    static readonly Vector3[] LegRightToeXDir = {
        new Vector3(0.94595f,-0.20226f,-0.25350f), new Vector3(0.93556f,-0.28490f,-0.20869f), new Vector3(0.92642f,-0.34558f,-0.14942f), new Vector3(0.92685f,-0.37303f,-0.04248f),
        new Vector3(0.92614f,-0.37138f,0.06583f), new Vector3(0.93822f,-0.32186f,0.12707f), new Vector3(0.94958f,-0.25941f,0.17605f), new Vector3(0.96214f,-0.16114f,0.21980f),
        new Vector3(0.97089f,-0.04256f,0.23573f), new Vector3(0.98568f,0.05198f,0.16040f), new Vector3(0.98938f,0.13080f,0.06336f), new Vector3(0.98459f,0.14861f,0.09223f),
        new Vector3(0.97784f,0.17134f,0.12029f), new Vector3(0.97098f,0.16579f,0.17239f), new Vector3(0.96232f,0.15968f,0.22009f), new Vector3(0.98232f,0.05624f,0.17857f),
        new Vector3(0.99339f,-0.02375f,0.11226f), new Vector3(0.99576f,-0.03018f,0.08687f), new Vector3(0.99793f,-0.02404f,0.05959f), new Vector3(0.99900f,-0.02806f,0.03491f),
        new Vector3(0.99971f,-0.02157f,0.01079f),
    };

    static readonly Vector3[] SpineYDir = {
        new Vector3(-0.05482f,0.99801f,0.03104f), new Vector3(-0.03869f,0.99850f,0.03869f), new Vector3(-0.02512f,0.99860f,0.04651f), new Vector3(-0.01396f,0.99928f,0.03537f),
        new Vector3(-0.00394f,0.99969f,0.02444f), new Vector3(-0.00589f,0.99983f,0.01719f), new Vector3(-0.00805f,0.99992f,0.00984f), new Vector3(-0.01720f,0.99978f,0.01177f),
        new Vector3(-0.02635f,0.99956f,0.01365f), new Vector3(-0.03688f,0.99926f,0.01120f), new Vector3(-0.04750f,0.99883f,0.00897f), new Vector3(-0.05571f,0.99845f,0.00169f),
        new Vector3(-0.06464f,0.99789f,-0.00546f), new Vector3(-0.06746f,0.99772f,-0.00265f), new Vector3(-0.07220f,0.99739f,-0.00065f), new Vector3(-0.07461f,0.99713f,0.01268f),
        new Vector3(-0.07950f,0.99653f,0.02472f), new Vector3(-0.08757f,0.99581f,0.02632f), new Vector3(-0.09723f,0.99488f,0.02762f), new Vector3(-0.10658f,0.99425f,0.01084f),
        new Vector3(-0.11651f,0.99318f,-0.00508f),
    };

    static readonly Vector3[] SpineXDir = {
        new Vector3(0.99643f,0.05269f,0.06593f), new Vector3(0.99258f,0.03394f,0.11680f), new Vector3(0.98577f,0.01701f,0.16722f), new Vector3(0.97900f,0.00646f,0.20377f),
        new Vector3(0.97077f,-0.00204f,0.23999f), new Vector3(0.96654f,0.00129f,0.25653f), new Vector3(0.96207f,0.00506f,0.27274f), new Vector3(0.96144f,0.01331f,0.27468f),
        new Vector3(0.96074f,0.02155f,0.27662f), new Vector3(0.96284f,0.03253f,0.26809f), new Vector3(0.96471f,0.04354f,0.25970f), new Vector3(0.96999f,0.05372f,0.23713f),
        new Vector3(0.97458f,0.06430f,0.21461f), new Vector3(0.98181f,0.06686f,0.17771f), new Vector3(0.98748f,0.07157f,0.14054f), new Vector3(0.99213f,0.07294f,0.10181f),
        new Vector3(0.99499f,0.07782f,0.06275f), new Vector3(0.99580f,0.08679f,0.02928f), new Vector3(0.99524f,0.09737f,-0.00367f), new Vector3(0.99338f,0.10694f,-0.04186f),
        new Vector3(0.99004f,0.11573f,-0.08021f),
    };

    static readonly Vector3[] Spine01YDir = {
        new Vector3(0.01219f,0.96754f,0.25242f), new Vector3(0.01474f,0.97187f,0.23507f), new Vector3(0.01754f,0.97599f,0.21711f), new Vector3(0.01760f,0.98300f,0.18276f),
        new Vector3(0.01843f,0.98884f,0.14786f), new Vector3(0.01198f,0.99180f,0.12727f), new Vector3(0.00579f,0.99430f,0.10642f), new Vector3(-0.00057f,0.99433f,0.10630f),
        new Vector3(-0.00693f,0.99433f,0.10614f), new Vector3(-0.01236f,0.99370f,0.11142f), new Vector3(-0.01775f,0.99299f,0.11684f), new Vector3(-0.02516f,0.99155f,0.12729f),
        new Vector3(-0.03253f,0.98992f,0.13786f), new Vector3(-0.03896f,0.98593f,0.16258f), new Vector3(-0.04570f,0.98134f,0.18677f), new Vector3(-0.05018f,0.97488f,0.21702f),
        new Vector3(-0.05575f,0.96763f,0.24615f), new Vector3(-0.06057f,0.96393f,0.25919f), new Vector3(-0.06631f,0.96003f,0.27191f), new Vector3(-0.06682f,0.96317f,0.26047f),
        new Vector3(-0.06753f,0.96602f,0.24950f),
    };

    static readonly Vector3[] Spine01XDir = {
        new Vector3(0.99972f,-0.01693f,0.01663f), new Vector3(0.99746f,-0.03067f,0.06425f), new Vector3(0.99284f,-0.04266f,0.11154f), new Vector3(0.98793f,-0.04524f,0.14815f),
        new Vector3(0.98183f,-0.04583f,0.18413f), new Vector3(0.97896f,-0.03756f,0.20058f), new Vector3(0.97584f,-0.02886f,0.21657f), new Vector3(0.97574f,-0.02272f,0.21774f),
        new Vector3(0.97560f,-0.01657f,0.21891f), new Vector3(0.97761f,-0.01141f,0.21012f), new Vector3(0.97948f,-0.00620f,0.20145f), new Vector3(0.98332f,0.00161f,0.18185f),
        new Vector3(0.98669f,0.00981f,0.16235f), new Vector3(0.99096f,0.01722f,0.13305f), new Vector3(0.99425f,0.02655f,0.10379f), new Vector3(0.99656f,0.03453f,0.07532f),
        new Vector3(0.99786f,0.04555f,0.04694f), new Vector3(0.99814f,0.05661f,0.02270f), new Vector3(0.99760f,0.06918f,-0.00094f), new Vector3(0.99643f,0.07794f,-0.03258f),
        new Vector3(0.99420f,0.08613f,-0.06439f),
    };

    static readonly Vector3[] Spine02YDir = {
        new Vector3(0.08636f,0.95525f,0.28293f), new Vector3(0.08502f,0.96458f,0.24971f), new Vector3(0.08563f,0.97278f,0.21533f), new Vector3(0.07840f,0.98210f,0.17127f),
        new Vector3(0.07271f,0.98937f,0.12594f), new Vector3(0.06335f,0.99296f,0.10008f), new Vector3(0.05441f,0.99579f,0.07380f), new Vector3(0.05126f,0.99603f,0.07278f),
        new Vector3(0.04812f,0.99626f,0.07173f), new Vector3(0.04724f,0.99566f,0.08017f), new Vector3(0.04645f,0.99498f,0.08868f), new Vector3(0.03915f,0.99355f,0.10641f),
        new Vector3(0.03219f,0.99173f,0.12422f), new Vector3(0.01698f,0.98720f,0.15860f), new Vector3(0.00204f,0.98123f,0.19281f), new Vector3(-0.01316f,0.97229f,0.23339f),
        new Vector3(-0.02883f,0.96154f,0.27316f), new Vector3(-0.03691f,0.95429f,0.29660f), new Vector3(-0.04539f,0.94641f,0.31975f), new Vector3(-0.04282f,0.94816f,0.31488f),
        new Vector3(-0.04003f,0.94977f,0.31037f),
    };

    static readonly Vector3[] Spine02XDir = {
        new Vector3(0.99623f,-0.08050f,-0.03230f), new Vector3(0.99576f,-0.09108f,0.01281f), new Vector3(0.99331f,-0.10016f,0.05752f), new Vector3(0.99087f,-0.09567f,0.09497f),
        new Vector3(0.98733f,-0.08926f,0.13123f), new Vector3(0.98582f,-0.07787f,0.14865f), new Vector3(0.98400f,-0.06603f,0.16545f), new Vector3(0.98393f,-0.06285f,0.16712f),
        new Vector3(0.98385f,-0.05967f,0.16878f), new Vector3(0.98529f,-0.05964f,0.16016f), new Vector3(0.98665f,-0.05957f,0.15157f), new Vector3(0.98957f,-0.05332f,0.13384f),
        new Vector3(0.99213f,-0.04676f,0.11617f), new Vector3(0.99513f,-0.03209f,0.09324f), new Vector3(0.99736f,-0.01598f,0.07080f), new Vector3(0.99862f,0.00090f,0.05258f),
        new Vector3(0.99918f,0.01999f,0.03507f), new Vector3(0.99927f,0.03241f,0.02007f), new Vector3(0.99893f,0.04599f,0.00570f), new Vector3(0.99844f,0.05193f,-0.02062f),
        new Vector3(0.99724f,0.05742f,-0.04711f),
    };

    static readonly Vector3[] NeckYDir = {
        new Vector3(-0.03015f,0.98670f,0.15973f), new Vector3(-0.03257f,0.98709f,0.15684f), new Vector3(-0.03641f,0.98761f,0.15265f), new Vector3(-0.03417f,0.99028f,0.13484f),
        new Vector3(-0.03256f,0.99263f,0.11674f), new Vector3(-0.03594f,0.99355f,0.10758f), new Vector3(-0.03946f,0.99437f,0.09829f), new Vector3(-0.04797f,0.99413f,0.09694f),
        new Vector3(-0.05647f,0.99382f,0.09553f), new Vector3(-0.06595f,0.99390f,0.08835f), new Vector3(-0.07563f,0.99381f,0.08137f), new Vector3(-0.08013f,0.99424f,0.07115f),
        new Vector3(-0.08553f,0.99447f,0.06091f), new Vector3(-0.08025f,0.99471f,0.06409f), new Vector3(-0.07688f,0.99486f,0.06597f), new Vector3(-0.06787f,0.99420f,0.08339f),
        new Vector3(-0.06102f,0.99324f,0.09875f), new Vector3(-0.05711f,0.99234f,0.10959f), new Vector3(-0.05424f,0.99136f,0.11945f), new Vector3(-0.05305f,0.99127f,0.12072f),
        new Vector3(-0.05115f,0.99119f,0.12220f),
    };

    static readonly Vector3[] NeckXDir = {
        new Vector3(0.99748f,0.01944f,0.06820f), new Vector3(0.99320f,0.01441f,0.11555f), new Vector3(0.98663f,0.01125f,0.16257f), new Vector3(0.98081f,0.00732f,0.19481f),
        new Vector3(0.97397f,0.00530f,0.22660f), new Vector3(0.97083f,0.00918f,0.23958f), new Vector3(0.96757f,0.01346f,0.25225f), new Vector3(0.96710f,0.02195f,0.25344f),
        new Vector3(0.96656f,0.03044f,0.25463f), new Vector3(0.96783f,0.04217f,0.24803f), new Vector3(0.96888f,0.05395f,0.24158f), new Vector3(0.97297f,0.06251f,0.22233f),
        new Vector3(0.97652f,0.07154f,0.20320f), new Vector3(0.98303f,0.06833f,0.17023f), new Vector3(0.98828f,0.06729f,0.13703f), new Vector3(0.99301f,0.05922f,0.10214f),
        new Vector3(0.99628f,0.05458f,0.06671f), new Vector3(0.99789f,0.05332f,0.03718f), new Vector3(0.99853f,0.05366f,0.00808f), new Vector3(0.99812f,0.05633f,-0.02396f),
        new Vector3(0.99672f,0.05835f,-0.05610f),
    };

    static readonly Vector3[] HeadYDir = {
        new Vector3(-0.03906f,0.75860f,0.65039f), new Vector3(-0.03391f,0.74715f,0.66379f), new Vector3(-0.03076f,0.73586f,0.67643f), new Vector3(-0.02731f,0.72915f,0.68381f),
        new Vector3(-0.02502f,0.72241f,0.69102f), new Vector3(-0.02906f,0.71592f,0.69757f), new Vector3(-0.03318f,0.70944f,0.70399f), new Vector3(-0.03792f,0.71021f,0.70296f),
        new Vector3(-0.04267f,0.71099f,0.70191f), new Vector3(-0.04403f,0.72558f,0.68673f), new Vector3(-0.04587f,0.73971f,0.67136f), new Vector3(-0.04319f,0.75552f,0.65370f),
        new Vector3(-0.04167f,0.77092f,0.63557f), new Vector3(-0.03233f,0.77245f,0.63425f), new Vector3(-0.02466f,0.77466f,0.63189f), new Vector3(-0.01469f,0.76485f,0.64404f),
        new Vector3(-0.00635f,0.75595f,0.65460f), new Vector3(-0.00507f,0.75256f,0.65850f), new Vector3(-0.00506f,0.74956f,0.66192f), new Vector3(-0.00824f,0.75435f,0.65643f),
        new Vector3(-0.01110f,0.75870f,0.65135f),
    };

    static readonly Vector3[] HeadXDir = {
        new Vector3(0.99909f,0.01841f,0.03853f), new Vector3(0.99907f,0.00769f,0.04238f), new Vector3(0.99894f,-0.00061f,0.04609f), new Vector3(0.99881f,-0.00783f,0.04824f),
        new Vector3(0.99865f,-0.01342f,0.05019f), new Vector3(0.99857f,-0.01049f,0.05236f), new Vector3(0.99850f,-0.00716f,0.05427f), new Vector3(0.99837f,-0.00306f,0.05695f),
        new Vector3(0.99822f,0.00103f,0.05964f), new Vector3(0.99809f,0.00221f,0.06167f), new Vector3(0.99794f,0.00369f,0.06411f), new Vector3(0.99807f,0.00342f,0.06200f),
        new Vector3(0.99817f,0.00421f,0.06035f), new Vector3(0.99876f,0.00101f,0.04968f), new Vector3(0.99924f,-0.00005f,0.03906f), new Vector3(0.99968f,-0.00204f,0.02522f),
        new Vector3(0.99994f,-0.00132f,0.01122f), new Vector3(0.99998f,0.00547f,0.00145f), new Vector3(0.99988f,0.01355f,-0.00771f), new Vector3(0.99961f,0.02374f,-0.01473f),
        new Vector3(0.99920f,0.03345f,-0.02193f),
    };

    static readonly Vector3[] LeftShoulderYDir = {
        new Vector3(0.99315f,-0.10802f,0.04455f), new Vector3(0.99244f,-0.09393f,0.07904f), new Vector3(0.99045f,-0.07793f,0.11372f), new Vector3(0.98765f,-0.06769f,0.14133f),
        new Vector3(0.98418f,-0.05574f,0.16820f), new Vector3(0.98176f,-0.05209f,0.18288f), new Vector3(0.97918f,-0.04803f,0.19725f), new Vector3(0.97709f,-0.04770f,0.20743f),
        new Vector3(0.97490f,-0.04745f,0.21751f), new Vector3(0.97423f,-0.04963f,0.22003f), new Vector3(0.97352f,-0.05180f,0.22267f), new Vector3(0.97627f,-0.05805f,0.20862f),
        new Vector3(0.97875f,-0.06381f,0.19485f), new Vector3(0.98385f,-0.07242f,0.16367f), new Vector3(0.98805f,-0.07891f,0.13240f), new Vector3(0.99115f,-0.08797f,0.09946f),
        new Vector3(0.99340f,-0.09369f,0.06625f), new Vector3(0.99529f,-0.08921f,0.03797f), new Vector3(0.99649f,-0.08310f,0.01029f), new Vector3(0.99786f,-0.06028f,-0.02519f),
        new Vector3(0.99737f,-0.03819f,-0.06160f),
    };

    static readonly Vector3[] LeftShoulderXDir = {
        new Vector3(0.08291f,0.38278f,-0.92011f), new Vector3(0.10924f,0.38203f,-0.91767f), new Vector3(0.13497f,0.38009f,-0.91505f), new Vector3(0.15633f,0.36294f,-0.91860f),
        new Vector3(0.17706f,0.34533f,-0.92163f), new Vector3(0.18963f,0.33893f,-0.92150f), new Vector3(0.20188f,0.33254f,-0.92123f), new Vector3(0.21119f,0.33822f,-0.91706f),
        new Vector3(0.22039f,0.34394f,-0.91276f), new Vector3(0.22338f,0.34769f,-0.91061f), new Vector3(0.22647f,0.35157f,-0.90835f), new Vector3(0.21561f,0.34981f,-0.91167f),
        new Vector3(0.20482f,0.34792f,-0.91488f), new Vector3(0.17876f,0.35256f,-0.91856f), new Vector3(0.15200f,0.35649f,-0.92185f), new Vector3(0.12529f,0.37140f,-0.91998f),
        new Vector3(0.09752f,0.38511f,-0.91770f), new Vector3(0.06975f,0.38680f,-0.91952f), new Vector3(0.04186f,0.38787f,-0.92076f), new Vector3(-0.00133f,0.36669f,-0.93034f),
        new Vector3(-0.04469f,0.34520f,-0.93747f),
    };

    static readonly Vector3[] RightShoulderYDir = {
        new Vector3(-0.99005f,-0.09453f,-0.10426f), new Vector3(-0.98543f,-0.04814f,-0.16313f), new Vector3(-0.97527f,-0.00349f,-0.22097f), new Vector3(-0.96715f,0.02280f,-0.25319f),
        new Vector3(-0.95727f,0.04714f,-0.28533f), new Vector3(-0.95251f,0.05557f,-0.29938f), new Vector3(-0.94754f,0.06359f,-0.31324f), new Vector3(-0.94793f,0.05236f,-0.31416f),
        new Vector3(-0.94817f,0.04113f,-0.31508f), new Vector3(-0.95234f,0.01194f,-0.30479f), new Vector3(-0.95552f,-0.01729f,-0.29442f), new Vector3(-0.96094f,-0.05041f,-0.27211f),
        new Vector3(-0.96475f,-0.08389f,-0.24942f), new Vector3(-0.96970f,-0.11567f,-0.21519f), new Vector3(-0.97208f,-0.14949f,-0.18085f), new Vector3(-0.97429f,-0.17445f,-0.14258f),
        new Vector3(-0.97361f,-0.20280f,-0.10463f), new Vector3(-0.97494f,-0.21239f,-0.06618f), new Vector3(-0.97428f,-0.22355f,-0.02816f), new Vector3(-0.97445f,-0.22410f,0.01520f),
        new Vector3(-0.97291f,-0.22369f,0.05841f),
    };

    static readonly Vector3[] RightShoulderXDir = {
        new Vector3(-0.06397f,-0.35755f,0.93170f), new Vector3(-0.13525f,-0.35979f,0.92318f), new Vector3(-0.20496f,-0.35967f,0.91029f), new Vector3(-0.24525f,-0.34585f,0.90567f),
        new Vector3(-0.28444f,-0.33178f,0.89946f), new Vector3(-0.30081f,-0.32431f,0.89685f), new Vector3(-0.31680f,-0.31691f,0.89398f), new Vector3(-0.31379f,-0.32239f,0.89308f),
        new Vector3(-0.31070f,-0.32778f,0.89220f), new Vector3(-0.29147f,-0.33020f,0.89778f), new Vector3(-0.27224f,-0.33231f,0.90303f), new Vector3(-0.24120f,-0.32951f,0.91282f),
        new Vector3(-0.20992f,-0.32620f,0.92170f), new Vector3(-0.16660f,-0.33116f,0.92875f), new Vector3(-0.12226f,-0.33515f,0.93420f), new Vector3(-0.07427f,-0.34879f,0.93425f),
        new Vector3(-0.02504f,-0.36080f,0.93231f), new Vector3(0.01478f,-0.35868f,0.93334f), new Vector3(0.05462f,-0.35557f,0.93305f), new Vector3(0.09171f,-0.33518f,0.93768f),
        new Vector3(0.12884f,-0.31481f,0.94037f),
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
