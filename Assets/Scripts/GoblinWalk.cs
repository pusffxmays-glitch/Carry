using UnityEngine;

// Baked world-space Y+X axis data for the Carry_Balance_Walk action, ported the same way as
// GoblinStagger.cs (see that file's header for the full rationale -- procedural per-frame data
// instead of an Animator/FBX clip, to sidestep this project's repeated hierarchy-path mismatches).
// Covers Hips + the 4 leg bones only; Spine/neck/Head/arms are handled elsewhere (arms via
// GoblinCarryRig.SolveArm's IK, with a small additive bob while walking -- see ApplyWalkArmBob).
public static class GoblinWalk
{
    public const int FrameCount = 60;

    public static void SampleHips(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(HipsWalkYDir, phase01); xDir = Sample(HipsWalkXDir, phase01); }
    public static void SampleLeftUpLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftUpLegWalkYDir, phase01); xDir = Sample(LegLeftUpLegWalkXDir, phase01); }
    public static void SampleLeftLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftLegWalkYDir, phase01); xDir = Sample(LegLeftLegWalkXDir, phase01); }
    public static void SampleRightUpLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightUpLegWalkYDir, phase01); xDir = Sample(LegRightUpLegWalkXDir, phase01); }
    public static void SampleRightLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightLegWalkYDir, phase01); xDir = Sample(LegRightLegWalkXDir, phase01); }
    public static void SampleLeftFoot(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftFootWalkYDir, phase01); xDir = Sample(LegLeftFootWalkXDir, phase01); }
    public static void SampleRightFoot(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightFootWalkYDir, phase01); xDir = Sample(LegRightFootWalkXDir, phase01); }

    static Vector3 Sample(Vector3[] frames, float phase01)
    {
        phase01 = Mathf.Repeat(phase01, 1f);
        float f = phase01 * frames.Length;
        int i0 = Mathf.FloorToInt(f) % frames.Length;
        int i1 = (i0 + 1) % frames.Length;
        float t = f - Mathf.Floor(f);
        return Vector3.Slerp(frames[i0], frames[i1], t).normalized;
    }

    static readonly Vector3[] HipsWalkYDir = {
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
        new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f), new Vector3(-0.04070f,0.95632f,-0.28949f),
    };

    static readonly Vector3[] HipsWalkXDir = {
        new Vector3(0.99872f,0.03018f,-0.04070f), new Vector3(0.99891f,0.03230f,-0.03372f), new Vector3(0.99905f,0.03440f,-0.02681f), new Vector3(0.99913f,0.03645f,-0.02006f),
        new Vector3(0.99917f,0.03843f,-0.01353f), new Vector3(0.99916f,0.04031f,-0.00730f), new Vector3(0.99911f,0.04209f,-0.00143f), new Vector3(0.99904f,0.04373f,0.00400f),
        new Vector3(0.99894f,0.04522f,0.00895f), new Vector3(0.99883f,0.04655f,0.01335f), new Vector3(0.99871f,0.04770f,0.01716f), new Vector3(0.99861f,0.04865f,0.02033f),
        new Vector3(0.99852f,0.04941f,0.02284f), new Vector3(0.99845f,0.04995f,0.02465f), new Vector3(0.99840f,0.05028f,0.02574f), new Vector3(0.99839f,0.05039f,0.02611f),
        new Vector3(0.99840f,0.05028f,0.02574f), new Vector3(0.99845f,0.04995f,0.02465f), new Vector3(0.99852f,0.04941f,0.02284f), new Vector3(0.99861f,0.04865f,0.02033f),
        new Vector3(0.99871f,0.04770f,0.01716f), new Vector3(0.99883f,0.04655f,0.01335f), new Vector3(0.99894f,0.04522f,0.00895f), new Vector3(0.99904f,0.04373f,0.00400f),
        new Vector3(0.99911f,0.04209f,-0.00143f), new Vector3(0.99916f,0.04031f,-0.00730f), new Vector3(0.99917f,0.03843f,-0.01353f), new Vector3(0.99913f,0.03645f,-0.02006f),
        new Vector3(0.99905f,0.03440f,-0.02681f), new Vector3(0.99891f,0.03230f,-0.03372f), new Vector3(0.99872f,0.03018f,-0.04070f), new Vector3(0.99847f,0.02806f,-0.04768f),
        new Vector3(0.99817f,0.02596f,-0.05457f), new Vector3(0.99783f,0.02390f,-0.06132f), new Vector3(0.99746f,0.02192f,-0.06783f), new Vector3(0.99705f,0.02002f,-0.07405f),
        new Vector3(0.99664f,0.01823f,-0.07990f), new Vector3(0.99622f,0.01657f,-0.08531f), new Vector3(0.99581f,0.01506f,-0.09024f), new Vector3(0.99542f,0.01372f,-0.09462f),
        new Vector3(0.99507f,0.01256f,-0.09841f), new Vector3(0.99476f,0.01159f,-0.10157f), new Vector3(0.99451f,0.01082f,-0.10406f), new Vector3(0.99433f,0.01027f,-0.10586f),
        new Vector3(0.99422f,0.00994f,-0.10694f), new Vector3(0.99418f,0.00983f,-0.10731f), new Vector3(0.99422f,0.00994f,-0.10694f), new Vector3(0.99433f,0.01027f,-0.10586f),
        new Vector3(0.99451f,0.01082f,-0.10406f), new Vector3(0.99476f,0.01159f,-0.10157f), new Vector3(0.99507f,0.01256f,-0.09841f), new Vector3(0.99542f,0.01372f,-0.09462f),
        new Vector3(0.99581f,0.01506f,-0.09024f), new Vector3(0.99622f,0.01657f,-0.08531f), new Vector3(0.99664f,0.01823f,-0.07990f), new Vector3(0.99705f,0.02002f,-0.07405f),
        new Vector3(0.99746f,0.02192f,-0.06783f), new Vector3(0.99783f,0.02390f,-0.06132f), new Vector3(0.99817f,0.02596f,-0.05457f), new Vector3(0.99847f,0.02806f,-0.04768f),
    };

    static readonly Vector3[] LegLeftUpLegWalkYDir = {
        new Vector3(0.44799f,-0.73760f,0.50522f), new Vector3(0.44374f,-0.71203f,0.54415f), new Vector3(0.43809f,-0.68544f,0.58159f), new Vector3(0.43118f,-0.65824f,0.61710f),
        new Vector3(0.42323f,-0.63086f,0.65030f), new Vector3(0.41450f,-0.60379f,0.68090f), new Vector3(0.40528f,-0.57751f,0.70868f), new Vector3(0.39589f,-0.55249f,0.73350f),
        new Vector3(0.38666f,-0.52920f,0.75527f), new Vector3(0.37790f,-0.50808f,0.77398f), new Vector3(0.36992f,-0.48950f,0.78965f), new Vector3(0.36298f,-0.47382f,0.80233f),
        new Vector3(0.35733f,-0.46132f,0.81209f), new Vector3(0.35315f,-0.45223f,0.81901f), new Vector3(0.35059f,-0.44671f,0.82313f), new Vector3(0.34972f,-0.44486f,0.82450f),
        new Vector3(0.35059f,-0.44671f,0.82313f), new Vector3(0.35315f,-0.45223f,0.81901f), new Vector3(0.35733f,-0.46132f,0.81209f), new Vector3(0.36298f,-0.47382f,0.80233f),
        new Vector3(0.36992f,-0.48950f,0.78965f), new Vector3(0.37790f,-0.50808f,0.77398f), new Vector3(0.38666f,-0.52920f,0.75527f), new Vector3(0.39589f,-0.55249f,0.73350f),
        new Vector3(0.40528f,-0.57751f,0.70868f), new Vector3(0.41450f,-0.60379f,0.68090f), new Vector3(0.42323f,-0.63086f,0.65030f), new Vector3(0.43118f,-0.65824f,0.61710f),
        new Vector3(0.43809f,-0.68544f,0.58159f), new Vector3(0.44374f,-0.71203f,0.54415f), new Vector3(0.44799f,-0.73760f,0.50522f), new Vector3(0.45075f,-0.76178f,0.46531f),
        new Vector3(0.45200f,-0.78430f,0.42494f), new Vector3(0.45179f,-0.80491f,0.38472f), new Vector3(0.45024f,-0.82348f,0.34521f), new Vector3(0.44752f,-0.83993f,0.30700f),
        new Vector3(0.44387f,-0.85424f,0.27065f), new Vector3(0.43953f,-0.86648f,0.23669f), new Vector3(0.43480f,-0.87674f,0.20560f), new Vector3(0.42996f,-0.88516f,0.17781f),
        new Vector3(0.42530f,-0.89191f,0.15369f), new Vector3(0.42109f,-0.89714f,0.13354f), new Vector3(0.41755f,-0.90101f,0.11762f), new Vector3(0.41487f,-0.90367f,0.10611f),
        new Vector3(0.41321f,-0.90522f,0.09915f), new Vector3(0.41265f,-0.90573f,0.09683f), new Vector3(0.41321f,-0.90522f,0.09915f), new Vector3(0.41487f,-0.90367f,0.10611f),
        new Vector3(0.41755f,-0.90101f,0.11762f), new Vector3(0.42109f,-0.89714f,0.13354f), new Vector3(0.42530f,-0.89191f,0.15369f), new Vector3(0.42996f,-0.88516f,0.17781f),
        new Vector3(0.43480f,-0.87674f,0.20560f), new Vector3(0.43953f,-0.86648f,0.23669f), new Vector3(0.44387f,-0.85424f,0.27065f), new Vector3(0.44752f,-0.83993f,0.30700f),
        new Vector3(0.45024f,-0.82348f,0.34521f), new Vector3(0.45179f,-0.80491f,0.38472f), new Vector3(0.45200f,-0.78430f,0.42494f), new Vector3(0.45075f,-0.76178f,0.46531f),
    };

    static readonly Vector3[] LegLeftUpLegWalkXDir = {
        new Vector3(0.89335f,0.39154f,-0.22052f), new Vector3(0.89403f,0.39350f,-0.21417f), new Vector3(0.89466f,0.39543f,-0.20788f), new Vector3(0.89524f,0.39731f,-0.20172f),
        new Vector3(0.89575f,0.39914f,-0.19577f), new Vector3(0.89619f,0.40088f,-0.19008f), new Vector3(0.89658f,0.40251f,-0.18473f), new Vector3(0.89691f,0.40403f,-0.17977f),
        new Vector3(0.89718f,0.40541f,-0.17525f), new Vector3(0.89740f,0.40664f,-0.17123f), new Vector3(0.89758f,0.40770f,-0.16774f), new Vector3(0.89771f,0.40858f,-0.16484f),
        new Vector3(0.89781f,0.40928f,-0.16255f), new Vector3(0.89788f,0.40979f,-0.16089f), new Vector3(0.89792f,0.41009f,-0.15989f), new Vector3(0.89793f,0.41019f,-0.15956f),
        new Vector3(0.89792f,0.41009f,-0.15989f), new Vector3(0.89788f,0.40979f,-0.16089f), new Vector3(0.89781f,0.40928f,-0.16255f), new Vector3(0.89771f,0.40858f,-0.16484f),
        new Vector3(0.89758f,0.40770f,-0.16774f), new Vector3(0.89740f,0.40664f,-0.17123f), new Vector3(0.89718f,0.40541f,-0.17525f), new Vector3(0.89691f,0.40403f,-0.17977f),
        new Vector3(0.89658f,0.40251f,-0.18473f), new Vector3(0.89619f,0.40088f,-0.19008f), new Vector3(0.89575f,0.39914f,-0.19577f), new Vector3(0.89524f,0.39731f,-0.20172f),
        new Vector3(0.89466f,0.39543f,-0.20788f), new Vector3(0.89403f,0.39350f,-0.21417f), new Vector3(0.89335f,0.39154f,-0.22052f), new Vector3(0.89261f,0.38959f,-0.22687f),
        new Vector3(0.89183f,0.38766f,-0.23314f), new Vector3(0.89103f,0.38577f,-0.23927f), new Vector3(0.89021f,0.38394f,-0.24518f), new Vector3(0.88939f,0.38220f,-0.25083f),
        new Vector3(0.88858f,0.38056f,-0.25613f), new Vector3(0.88780f,0.37904f,-0.26104f), new Vector3(0.88706f,0.37766f,-0.26551f), new Vector3(0.88639f,0.37643f,-0.26948f),
        new Vector3(0.88579f,0.37536f,-0.27291f), new Vector3(0.88528f,0.37447f,-0.27577f), new Vector3(0.88487f,0.37377f,-0.27803f), new Vector3(0.88457f,0.37327f,-0.27966f),
        new Vector3(0.88439f,0.37296f,-0.28064f), new Vector3(0.88433f,0.37286f,-0.28097f), new Vector3(0.88439f,0.37296f,-0.28064f), new Vector3(0.88457f,0.37327f,-0.27966f),
        new Vector3(0.88487f,0.37377f,-0.27803f), new Vector3(0.88528f,0.37447f,-0.27577f), new Vector3(0.88579f,0.37536f,-0.27291f), new Vector3(0.88639f,0.37643f,-0.26948f),
        new Vector3(0.88706f,0.37766f,-0.26551f), new Vector3(0.88780f,0.37904f,-0.26104f), new Vector3(0.88858f,0.38056f,-0.25613f), new Vector3(0.88939f,0.38220f,-0.25083f),
        new Vector3(0.89021f,0.38394f,-0.24518f), new Vector3(0.89103f,0.38577f,-0.23927f), new Vector3(0.89183f,0.38766f,-0.23314f), new Vector3(0.89261f,0.38959f,-0.22687f),
    };

    static readonly Vector3[] LegLeftLegWalkYDir = {
        new Vector3(-0.14628f,-0.41666f,-0.89721f), new Vector3(-0.12447f,-0.43141f,-0.89353f), new Vector3(-0.10280f,-0.44576f,-0.88923f), new Vector3(-0.08155f,-0.45956f,-0.88439f),
        new Vector3(-0.06098f,-0.47267f,-0.87912f), new Vector3(-0.04132f,-0.48498f,-0.87355f), new Vector3(-0.02281f,-0.49636f,-0.86782f), new Vector3(-0.00566f,-0.50673f,-0.86209f),
        new Vector3(0.00992f,-0.51602f,-0.85652f), new Vector3(0.02378f,-0.52416f,-0.85129f), new Vector3(0.03575f,-0.53111f,-0.84655f), new Vector3(0.04571f,-0.53683f,-0.84245f),
        new Vector3(0.05356f,-0.54130f,-0.83912f), new Vector3(0.05922f,-0.54451f,-0.83666f), new Vector3(0.06264f,-0.54644f,-0.83516f), new Vector3(0.06378f,-0.54708f,-0.83465f),
        new Vector3(0.06264f,-0.54644f,-0.83516f), new Vector3(0.05922f,-0.54451f,-0.83666f), new Vector3(0.05356f,-0.54130f,-0.83912f), new Vector3(0.04571f,-0.53683f,-0.84245f),
        new Vector3(0.03575f,-0.53111f,-0.84655f), new Vector3(0.02378f,-0.52416f,-0.85129f), new Vector3(0.00992f,-0.51602f,-0.85652f), new Vector3(-0.00566f,-0.50673f,-0.86209f),
        new Vector3(-0.02281f,-0.49636f,-0.86782f), new Vector3(-0.04132f,-0.48498f,-0.87355f), new Vector3(-0.06098f,-0.47267f,-0.87912f), new Vector3(-0.08155f,-0.45956f,-0.88439f),
        new Vector3(-0.10280f,-0.44576f,-0.88923f), new Vector3(-0.12447f,-0.43141f,-0.89353f), new Vector3(-0.14628f,-0.41666f,-0.89721f), new Vector3(-0.17277f,-0.37972f,-0.90883f),
        new Vector3(-0.19880f,-0.34244f,-0.91826f), new Vector3(-0.22403f,-0.30535f,-0.92551f), new Vector3(-0.24812f,-0.26896f,-0.93064f), new Vector3(-0.27079f,-0.23379f,-0.93382f),
        new Vector3(-0.29178f,-0.20035f,-0.93526f), new Vector3(-0.31090f,-0.16911f,-0.93528f), new Vector3(-0.32798f,-0.14050f,-0.93418f), new Vector3(-0.34291f,-0.11492f,-0.93231f),
        new Vector3(-0.35560f,-0.09270f,-0.93003f), new Vector3(-0.36602f,-0.07413f,-0.92765f), new Vector3(-0.37413f,-0.05946f,-0.92547f), new Vector3(-0.37992f,-0.04885f,-0.92373f),
        new Vector3(-0.38339f,-0.04243f,-0.92261f), new Vector3(-0.38455f,-0.04028f,-0.92222f), new Vector3(-0.38339f,-0.04243f,-0.92261f), new Vector3(-0.37992f,-0.04885f,-0.92373f),
        new Vector3(-0.37413f,-0.05946f,-0.92547f), new Vector3(-0.36602f,-0.07413f,-0.92765f), new Vector3(-0.35560f,-0.09270f,-0.93003f), new Vector3(-0.34291f,-0.11492f,-0.93231f),
        new Vector3(-0.32798f,-0.14050f,-0.93418f), new Vector3(-0.31090f,-0.16911f,-0.93528f), new Vector3(-0.29178f,-0.20035f,-0.93526f), new Vector3(-0.27079f,-0.23379f,-0.93382f),
        new Vector3(-0.24812f,-0.26896f,-0.93064f), new Vector3(-0.22403f,-0.30535f,-0.92551f), new Vector3(-0.19880f,-0.34244f,-0.91826f), new Vector3(-0.17277f,-0.37972f,-0.90883f),
    };

    static readonly Vector3[] LegLeftLegWalkXDir = {
        new Vector3(0.96911f,0.12170f,-0.21452f), new Vector3(0.97263f,0.12502f,-0.19585f), new Vector3(0.97568f,0.12878f,-0.17736f), new Vector3(0.97824f,0.13292f,-0.15928f),
        new Vector3(0.98031f,0.13735f,-0.14185f), new Vector3(0.98192f,0.14195f,-0.12525f), new Vector3(0.98309f,0.14662f,-0.10970f), new Vector3(0.98389f,0.15124f,-0.09536f),
        new Vector3(0.98437f,0.15567f,-0.08239f), new Vector3(0.98460f,0.15981f,-0.07090f), new Vector3(0.98465f,0.16352f,-0.06102f), new Vector3(0.98459f,0.16672f,-0.05282f),
        new Vector3(0.98447f,0.16930f,-0.04638f), new Vector3(0.98435f,0.17120f,-0.04175f), new Vector3(0.98426f,0.17237f,-0.03896f), new Vector3(0.98423f,0.17276f,-0.03802f),
        new Vector3(0.98426f,0.17237f,-0.03896f), new Vector3(0.98435f,0.17120f,-0.04175f), new Vector3(0.98447f,0.16930f,-0.04638f), new Vector3(0.98459f,0.16672f,-0.05282f),
        new Vector3(0.98465f,0.16352f,-0.06102f), new Vector3(0.98460f,0.15981f,-0.07090f), new Vector3(0.98437f,0.15567f,-0.08239f), new Vector3(0.98389f,0.15124f,-0.09536f),
        new Vector3(0.98309f,0.14662f,-0.10970f), new Vector3(0.98192f,0.14195f,-0.12525f), new Vector3(0.98031f,0.13735f,-0.14185f), new Vector3(0.97824f,0.13292f,-0.15928f),
        new Vector3(0.97568f,0.12878f,-0.17736f), new Vector3(0.97263f,0.12502f,-0.19585f), new Vector3(0.96911f,0.12170f,-0.21452f), new Vector3(0.96515f,0.11887f,-0.23314f),
        new Vector3(0.96082f,0.11655f,-0.25149f), new Vector3(0.95619f,0.11476f,-0.26932f), new Vector3(0.95135f,0.11348f,-0.28644f), new Vector3(0.94642f,0.11265f,-0.30265f),
        new Vector3(0.94150f,0.11225f,-0.31778f), new Vector3(0.93670f,0.11218f,-0.33166f), new Vector3(0.93216f,0.11238f,-0.34418f), new Vector3(0.92796f,0.11277f,-0.35521f),
        new Vector3(0.92422f,0.11326f,-0.36467f), new Vector3(0.92103f,0.11379f,-0.37250f), new Vector3(0.91846f,0.11427f,-0.37864f), new Vector3(0.91659f,0.11466f,-0.38305f),
        new Vector3(0.91544f,0.11491f,-0.38570f), new Vector3(0.91506f,0.11499f,-0.38659f), new Vector3(0.91544f,0.11491f,-0.38570f), new Vector3(0.91659f,0.11466f,-0.38305f),
        new Vector3(0.91846f,0.11427f,-0.37864f), new Vector3(0.92103f,0.11379f,-0.37250f), new Vector3(0.92422f,0.11326f,-0.36467f), new Vector3(0.92796f,0.11277f,-0.35521f),
        new Vector3(0.93216f,0.11238f,-0.34418f), new Vector3(0.93670f,0.11218f,-0.33166f), new Vector3(0.94150f,0.11225f,-0.31778f), new Vector3(0.94642f,0.11265f,-0.30265f),
        new Vector3(0.95135f,0.11348f,-0.28644f), new Vector3(0.95619f,0.11476f,-0.26932f), new Vector3(0.96082f,0.11655f,-0.25149f), new Vector3(0.96515f,0.11887f,-0.23314f),
    };

    static readonly Vector3[] LegRightUpLegWalkYDir = {
        new Vector3(-0.46228f,-0.72975f,0.50375f), new Vector3(-0.46520f,-0.75427f,0.46331f), new Vector3(-0.46658f,-0.77709f,0.42241f), new Vector3(-0.46646f,-0.79798f,0.38163f),
        new Vector3(-0.46498f,-0.81678f,0.34157f), new Vector3(-0.46229f,-0.83342f,0.30282f), new Vector3(-0.45864f,-0.84789f,0.26596f), new Vector3(-0.45430f,-0.86024f,0.23151f),
        new Vector3(-0.44953f,-0.87059f,0.19998f), new Vector3(-0.44465f,-0.87907f,0.17179f), new Vector3(-0.43995f,-0.88586f,0.14732f), new Vector3(-0.43569f,-0.89111f,0.12688f),
        new Vector3(-0.43211f,-0.89500f,0.11073f), new Vector3(-0.42940f,-0.89766f,0.09906f), new Vector3(-0.42772f,-0.89922f,0.09201f), new Vector3(-0.42715f,-0.89973f,0.08965f),
        new Vector3(-0.42772f,-0.89922f,0.09201f), new Vector3(-0.42940f,-0.89766f,0.09906f), new Vector3(-0.43211f,-0.89500f,0.11073f), new Vector3(-0.43569f,-0.89111f,0.12688f),
        new Vector3(-0.43995f,-0.88586f,0.14732f), new Vector3(-0.44465f,-0.87907f,0.17179f), new Vector3(-0.44953f,-0.87059f,0.19998f), new Vector3(-0.45430f,-0.86024f,0.23151f),
        new Vector3(-0.45864f,-0.84789f,0.26596f), new Vector3(-0.46229f,-0.83342f,0.30282f), new Vector3(-0.46498f,-0.81678f,0.34157f), new Vector3(-0.46646f,-0.79798f,0.38163f),
        new Vector3(-0.46658f,-0.77709f,0.42241f), new Vector3(-0.46520f,-0.75427f,0.46331f), new Vector3(-0.46228f,-0.72975f,0.50375f), new Vector3(-0.45784f,-0.70381f,0.54318f),
        new Vector3(-0.45196f,-0.67682f,0.58107f), new Vector3(-0.44480f,-0.64921f,0.61699f), new Vector3(-0.43659f,-0.62141f,0.65056f), new Vector3(-0.42759f,-0.59392f,0.68149f),
        new Vector3(-0.41809f,-0.56724f,0.70954f), new Vector3(-0.40843f,-0.54184f,0.73457f), new Vector3(-0.39893f,-0.51819f,0.75653f), new Vector3(-0.38992f,-0.49674f,0.77537f),
        new Vector3(-0.38172f,-0.47789f,0.79115f), new Vector3(-0.37459f,-0.46197f,0.80391f), new Vector3(-0.36879f,-0.44928f,0.81372f), new Vector3(-0.36450f,-0.44005f,0.82067f),
        new Vector3(-0.36187f,-0.43445f,0.82481f), new Vector3(-0.36098f,-0.43257f,0.82618f), new Vector3(-0.36187f,-0.43445f,0.82481f), new Vector3(-0.36450f,-0.44005f,0.82067f),
        new Vector3(-0.36879f,-0.44928f,0.81372f), new Vector3(-0.37459f,-0.46197f,0.80391f), new Vector3(-0.38172f,-0.47789f,0.79115f), new Vector3(-0.38992f,-0.49674f,0.77537f),
        new Vector3(-0.39893f,-0.51819f,0.75653f), new Vector3(-0.40843f,-0.54184f,0.73457f), new Vector3(-0.41809f,-0.56724f,0.70954f), new Vector3(-0.42759f,-0.59392f,0.68149f),
        new Vector3(-0.43659f,-0.62141f,0.65056f), new Vector3(-0.44480f,-0.64921f,0.61699f), new Vector3(-0.45196f,-0.67682f,0.58107f), new Vector3(-0.45784f,-0.70381f,0.54318f),
    };

    static readonly Vector3[] LegRightUpLegWalkXDir = {
        new Vector3(0.88587f,-0.40511f,0.22609f), new Vector3(0.88513f,-0.40330f,0.23215f), new Vector3(0.88435f,-0.40152f,0.23814f), new Vector3(0.88354f,-0.39979f,0.24399f),
        new Vector3(0.88271f,-0.39811f,0.24965f), new Vector3(0.88189f,-0.39651f,0.25503f), new Vector3(0.88108f,-0.39502f,0.26010f), new Vector3(0.88031f,-0.39363f,0.26479f),
        new Vector3(0.87958f,-0.39237f,0.26906f), new Vector3(0.87891f,-0.39125f,0.27285f), new Vector3(0.87831f,-0.39028f,0.27613f), new Vector3(0.87781f,-0.38947f,0.27887f),
        new Vector3(0.87740f,-0.38884f,0.28102f), new Vector3(0.87710f,-0.38838f,0.28258f), new Vector3(0.87692f,-0.38810f,0.28352f), new Vector3(0.87686f,-0.38801f,0.28383f),
        new Vector3(0.87692f,-0.38810f,0.28352f), new Vector3(0.87710f,-0.38838f,0.28258f), new Vector3(0.87740f,-0.38884f,0.28102f), new Vector3(0.87781f,-0.38947f,0.27887f),
        new Vector3(0.87831f,-0.39028f,0.27613f), new Vector3(0.87891f,-0.39125f,0.27285f), new Vector3(0.87958f,-0.39237f,0.26906f), new Vector3(0.88031f,-0.39363f,0.26479f),
        new Vector3(0.88108f,-0.39502f,0.26010f), new Vector3(0.88189f,-0.39651f,0.25503f), new Vector3(0.88271f,-0.39811f,0.24965f), new Vector3(0.88354f,-0.39979f,0.24399f),
        new Vector3(0.88435f,-0.40152f,0.23814f), new Vector3(0.88513f,-0.40330f,0.23215f), new Vector3(0.88587f,-0.40511f,0.22609f), new Vector3(0.88657f,-0.40691f,0.22003f),
        new Vector3(0.88722f,-0.40870f,0.21403f), new Vector3(0.88781f,-0.41045f,0.20816f), new Vector3(0.88833f,-0.41215f,0.20248f), new Vector3(0.88880f,-0.41377f,0.19705f),
        new Vector3(0.88920f,-0.41530f,0.19195f), new Vector3(0.88955f,-0.41672f,0.18721f), new Vector3(0.88984f,-0.41801f,0.18290f), new Vector3(0.89008f,-0.41916f,0.17907f),
        new Vector3(0.89027f,-0.42016f,0.17575f), new Vector3(0.89042f,-0.42099f,0.17298f), new Vector3(0.89053f,-0.42165f,0.17079f), new Vector3(0.89061f,-0.42212f,0.16922f),
        new Vector3(0.89065f,-0.42241f,0.16826f), new Vector3(0.89067f,-0.42250f,0.16794f), new Vector3(0.89065f,-0.42241f,0.16826f), new Vector3(0.89061f,-0.42212f,0.16922f),
        new Vector3(0.89053f,-0.42165f,0.17079f), new Vector3(0.89042f,-0.42099f,0.17298f), new Vector3(0.89027f,-0.42016f,0.17575f), new Vector3(0.89008f,-0.41916f,0.17907f),
        new Vector3(0.88984f,-0.41801f,0.18290f), new Vector3(0.88955f,-0.41672f,0.18721f), new Vector3(0.88920f,-0.41530f,0.19195f), new Vector3(0.88880f,-0.41377f,0.19705f),
        new Vector3(0.88833f,-0.41215f,0.20248f), new Vector3(0.88781f,-0.41045f,0.20816f), new Vector3(0.88722f,-0.40870f,0.21403f), new Vector3(0.88657f,-0.40691f,0.22003f),
    };

    static readonly Vector3[] LegRightLegWalkYDir = {
        new Vector3(0.15775f,-0.40980f,-0.89843f), new Vector3(0.18481f,-0.37240f,-0.90949f), new Vector3(0.21138f,-0.33467f,-0.91832f), new Vector3(0.23712f,-0.29715f,-0.92492f),
        new Vector3(0.26168f,-0.26036f,-0.92937f), new Vector3(0.28477f,-0.22482f,-0.93186f), new Vector3(0.30614f,-0.19105f,-0.93262f), new Vector3(0.32559f,-0.15951f,-0.93196f),
        new Vector3(0.34295f,-0.13064f,-0.93022f), new Vector3(0.35812f,-0.10484f,-0.92777f), new Vector3(0.37101f,-0.08244f,-0.92496f), new Vector3(0.38158f,-0.06373f,-0.92214f),
        new Vector3(0.38981f,-0.04895f,-0.91959f), new Vector3(0.39568f,-0.03826f,-0.91759f), new Vector3(0.39921f,-0.03180f,-0.91631f), new Vector3(0.40038f,-0.02964f,-0.91587f),
        new Vector3(0.39921f,-0.03180f,-0.91631f), new Vector3(0.39568f,-0.03826f,-0.91759f), new Vector3(0.38981f,-0.04895f,-0.91959f), new Vector3(0.38158f,-0.06373f,-0.92214f),
        new Vector3(0.37101f,-0.08244f,-0.92496f), new Vector3(0.35812f,-0.10484f,-0.92777f), new Vector3(0.34295f,-0.13064f,-0.93022f), new Vector3(0.32559f,-0.15951f,-0.93196f),
        new Vector3(0.30614f,-0.19105f,-0.93262f), new Vector3(0.28477f,-0.22482f,-0.93186f), new Vector3(0.26168f,-0.26036f,-0.92937f), new Vector3(0.23712f,-0.29715f,-0.92492f),
        new Vector3(0.21138f,-0.33467f,-0.91832f), new Vector3(0.18481f,-0.37240f,-0.90949f), new Vector3(0.15775f,-0.40980f,-0.89843f), new Vector3(0.13664f,-0.42511f,-0.89477f),
        new Vector3(0.11568f,-0.44003f,-0.89050f), new Vector3(0.09510f,-0.45440f,-0.88571f), new Vector3(0.07516f,-0.46807f,-0.88049f), new Vector3(0.05610f,-0.48090f,-0.87498f),
        new Vector3(0.03815f,-0.49279f,-0.86931f), new Vector3(0.02152f,-0.50363f,-0.86365f), new Vector3(0.00638f,-0.51335f,-0.85816f), new Vector3(-0.00707f,-0.52187f,-0.85299f),
        new Vector3(-0.01870f,-0.52916f,-0.84832f), new Vector3(-0.02838f,-0.53517f,-0.84427f), new Vector3(-0.03602f,-0.53986f,-0.84098f), new Vector3(-0.04152f,-0.54323f,-0.83856f),
        new Vector3(-0.04485f,-0.54525f,-0.83707f), new Vector3(-0.04596f,-0.54593f,-0.83657f), new Vector3(-0.04485f,-0.54525f,-0.83707f), new Vector3(-0.04152f,-0.54323f,-0.83856f),
        new Vector3(-0.03602f,-0.53986f,-0.84098f), new Vector3(-0.02838f,-0.53517f,-0.84427f), new Vector3(-0.01870f,-0.52916f,-0.84832f), new Vector3(-0.00707f,-0.52187f,-0.85299f),
        new Vector3(0.00638f,-0.51335f,-0.85816f), new Vector3(0.02152f,-0.50363f,-0.86365f), new Vector3(0.03815f,-0.49279f,-0.86931f), new Vector3(0.05610f,-0.48090f,-0.87498f),
        new Vector3(0.07516f,-0.46807f,-0.88049f), new Vector3(0.09510f,-0.45440f,-0.88571f), new Vector3(0.11568f,-0.44003f,-0.89050f), new Vector3(0.13664f,-0.42511f,-0.89477f),
    };

    static readonly Vector3[] LegRightLegWalkXDir = {
        new Vector3(0.95469f,-0.16923f,0.24481f), new Vector3(0.95048f,-0.16755f,0.26174f), new Vector3(0.94596f,-0.16634f,0.27837f), new Vector3(0.94120f,-0.16558f,0.29449f),
        new Vector3(0.93629f,-0.16525f,0.30992f), new Vector3(0.93133f,-0.16531f,0.32449f), new Vector3(0.92643f,-0.16568f,0.33805f), new Vector3(0.92169f,-0.16631f,0.35046f),
        new Vector3(0.91722f,-0.16711f,0.36163f), new Vector3(0.91313f,-0.16801f,0.37145f), new Vector3(0.90949f,-0.16893f,0.37986f), new Vector3(0.90640f,-0.16980f,0.38680f),
        new Vector3(0.90392f,-0.17054f,0.39224f), new Vector3(0.90210f,-0.17112f,0.39614f), new Vector3(0.90100f,-0.17148f,0.39849f), new Vector3(0.90063f,-0.17160f,0.39927f),
        new Vector3(0.90100f,-0.17148f,0.39849f), new Vector3(0.90210f,-0.17112f,0.39614f), new Vector3(0.90392f,-0.17054f,0.39224f), new Vector3(0.90640f,-0.16980f,0.38680f),
        new Vector3(0.90949f,-0.16893f,0.37986f), new Vector3(0.91313f,-0.16801f,0.37145f), new Vector3(0.91722f,-0.16711f,0.36163f), new Vector3(0.92169f,-0.16631f,0.35046f),
        new Vector3(0.92643f,-0.16568f,0.33805f), new Vector3(0.93133f,-0.16531f,0.32449f), new Vector3(0.93629f,-0.16525f,0.30992f), new Vector3(0.94120f,-0.16558f,0.29449f),
        new Vector3(0.94596f,-0.16634f,0.27837f), new Vector3(0.95048f,-0.16755f,0.26174f), new Vector3(0.95469f,-0.16923f,0.24481f), new Vector3(0.95852f,-0.17135f,0.22779f),
        new Vector3(0.96192f,-0.17389f,0.21088f), new Vector3(0.96488f,-0.17680f,0.19430f), new Vector3(0.96738f,-0.18001f,0.17827f), new Vector3(0.96943f,-0.18342f,0.16297f),
        new Vector3(0.97107f,-0.18695f,0.14859f), new Vector3(0.97232f,-0.19049f,0.13530f), new Vector3(0.97324f,-0.19393f,0.12325f), new Vector3(0.97388f,-0.19717f,0.11256f),
        new Vector3(0.97431f,-0.20011f,0.10335f), new Vector3(0.97456f,-0.20265f,0.09569f), new Vector3(0.97471f,-0.20472f,0.08967f), new Vector3(0.97477f,-0.20624f,0.08534f),
        new Vector3(0.97480f,-0.20717f,0.08272f), new Vector3(0.97481f,-0.20749f,0.08184f), new Vector3(0.97480f,-0.20717f,0.08272f), new Vector3(0.97477f,-0.20624f,0.08534f),
        new Vector3(0.97471f,-0.20472f,0.08967f), new Vector3(0.97456f,-0.20265f,0.09569f), new Vector3(0.97431f,-0.20011f,0.10335f), new Vector3(0.97388f,-0.19717f,0.11256f),
        new Vector3(0.97324f,-0.19393f,0.12325f), new Vector3(0.97232f,-0.19049f,0.13530f), new Vector3(0.97107f,-0.18695f,0.14859f), new Vector3(0.96943f,-0.18342f,0.16297f),
        new Vector3(0.96738f,-0.18001f,0.17827f), new Vector3(0.96488f,-0.17680f,0.19430f), new Vector3(0.96192f,-0.17389f,0.21088f), new Vector3(0.95852f,-0.17135f,0.22779f),
    };
    static readonly Vector3[] LegLeftFootWalkYDir = {
        new Vector3(0.07136f,-0.82579f,0.55944f), new Vector3(0.06485f,-0.81658f,0.57358f), new Vector3(0.05819f,-0.80732f,0.58723f), new Vector3(0.05148f,-0.79813f,0.60028f),
        new Vector3(0.04481f,-0.78911f,0.61261f), new Vector3(0.03830f,-0.78040f,0.62411f), new Vector3(0.03203f,-0.77209f,0.63470f), new Vector3(0.02611f,-0.76432f,0.64431f),
        new Vector3(0.02063f,-0.75717f,0.65289f), new Vector3(0.01569f,-0.75076f,0.66039f), new Vector3(0.01136f,-0.74516f,0.66678f), new Vector3(0.00772f,-0.74047f,0.67204f),
        new Vector3(0.00482f,-0.73675f,0.67614f), new Vector3(0.00272f,-0.73406f,0.67908f), new Vector3(0.00144f,-0.73242f,0.68085f), new Vector3(0.00101f,-0.73188f,0.68144f),
        new Vector3(0.00144f,-0.73242f,0.68085f), new Vector3(0.00272f,-0.73406f,0.67908f), new Vector3(0.00482f,-0.73675f,0.67614f), new Vector3(0.00772f,-0.74047f,0.67204f),
        new Vector3(0.01136f,-0.74516f,0.66678f), new Vector3(0.01569f,-0.75076f,0.66039f), new Vector3(0.02063f,-0.75717f,0.65289f), new Vector3(0.02611f,-0.76432f,0.64431f),
        new Vector3(0.03203f,-0.77209f,0.63470f), new Vector3(0.03830f,-0.78040f,0.62411f), new Vector3(0.04481f,-0.78911f,0.61261f), new Vector3(0.05148f,-0.79813f,0.60028f),
        new Vector3(0.05819f,-0.80732f,0.58723f), new Vector3(0.06485f,-0.81658f,0.57358f), new Vector3(0.07136f,-0.82579f,0.55944f), new Vector3(0.07446f,-0.84771f,0.52521f),
        new Vector3(0.07617f,-0.86809f,0.49052f), new Vector3(0.07655f,-0.88675f,0.45588f), new Vector3(0.07568f,-0.90353f,0.42179f), new Vector3(0.07373f,-0.91838f,0.38878f),
        new Vector3(0.07088f,-0.93128f,0.35733f), new Vector3(0.06738f,-0.94230f,0.32793f), new Vector3(0.06348f,-0.95152f,0.30098f), new Vector3(0.05943f,-0.95907f,0.27687f),
        new Vector3(0.05550f,-0.96510f,0.25594f), new Vector3(0.05192f,-0.96977f,0.23844f), new Vector3(0.04889f,-0.97322f,0.22461f), new Vector3(0.04661f,-0.97559f,0.21462f),
        new Vector3(0.04518f,-0.97696f,0.20857f), new Vector3(0.04470f,-0.97741f,0.20655f), new Vector3(0.04518f,-0.97696f,0.20857f), new Vector3(0.04661f,-0.97559f,0.21462f),
        new Vector3(0.04889f,-0.97322f,0.22461f), new Vector3(0.05192f,-0.96977f,0.23844f), new Vector3(0.05550f,-0.96510f,0.25594f), new Vector3(0.05943f,-0.95907f,0.27687f),
        new Vector3(0.06348f,-0.95152f,0.30098f), new Vector3(0.06738f,-0.94230f,0.32793f), new Vector3(0.07088f,-0.93128f,0.35733f), new Vector3(0.07373f,-0.91838f,0.38878f),
        new Vector3(0.07568f,-0.90353f,0.42179f), new Vector3(0.07655f,-0.88675f,0.45588f), new Vector3(0.07617f,-0.86809f,0.49052f), new Vector3(0.07446f,-0.84771f,0.52521f),
    };

    static readonly Vector3[] LegLeftFootWalkXDir = {
        new Vector3(0.98964f,-0.01144f,-0.14313f), new Vector3(0.99251f,-0.00686f,-0.12198f), new Vector3(0.99488f,-0.00182f,-0.10108f), new Vector3(0.99673f,0.00360f,-0.08069f),
        new Vector3(0.99809f,0.00928f,-0.06105f), new Vector3(0.99899f,0.01512f,-0.04239f), new Vector3(0.99947f,0.02097f,-0.02492f), new Vector3(0.99960f,0.02670f,-0.00883f),
        new Vector3(0.99947f,0.03217f,0.00572f), new Vector3(0.99913f,0.03723f,0.01858f), new Vector3(0.99869f,0.04176f,0.02965f), new Vector3(0.99820f,0.04564f,0.03882f),
        new Vector3(0.99775f,0.04877f,0.04602f), new Vector3(0.99738f,0.05106f,0.05120f), new Vector3(0.99714f,0.05246f,0.05433f), new Vector3(0.99706f,0.05294f,0.05537f),
        new Vector3(0.99714f,0.05246f,0.05433f), new Vector3(0.99738f,0.05106f,0.05120f), new Vector3(0.99775f,0.04877f,0.04602f), new Vector3(0.99820f,0.04564f,0.03882f),
        new Vector3(0.99869f,0.04176f,0.02965f), new Vector3(0.99913f,0.03723f,0.01858f), new Vector3(0.99947f,0.03217f,0.00572f), new Vector3(0.99960f,0.02670f,-0.00883f),
        new Vector3(0.99947f,0.02097f,-0.02492f), new Vector3(0.99899f,0.01512f,-0.04239f), new Vector3(0.99809f,0.00928f,-0.06105f), new Vector3(0.99673f,0.00360f,-0.08069f),
        new Vector3(0.99488f,-0.00182f,-0.10108f), new Vector3(0.99251f,-0.00686f,-0.12198f), new Vector3(0.98964f,-0.01144f,-0.14313f), new Vector3(0.98573f,-0.01718f,-0.16747f),
        new Vector3(0.98123f,-0.02215f,-0.19157f), new Vector3(0.97623f,-0.02632f,-0.21512f), new Vector3(0.97085f,-0.02970f,-0.23782f), new Vector3(0.96523f,-0.03233f,-0.25941f),
        new Vector3(0.95950f,-0.03426f,-0.27963f), new Vector3(0.95382f,-0.03559f,-0.29825f), new Vector3(0.94836f,-0.03640f,-0.31509f), new Vector3(0.94327f,-0.03681f,-0.32998f),
        new Vector3(0.93869f,-0.03692f,-0.34278f), new Vector3(0.93475f,-0.03685f,-0.35339f), new Vector3(0.93156f,-0.03668f,-0.36172f), new Vector3(0.92922f,-0.03650f,-0.36771f),
        new Vector3(0.92779f,-0.03637f,-0.37132f), new Vector3(0.92731f,-0.03632f,-0.37253f), new Vector3(0.92779f,-0.03637f,-0.37132f), new Vector3(0.92922f,-0.03650f,-0.36771f),
        new Vector3(0.93156f,-0.03668f,-0.36172f), new Vector3(0.93475f,-0.03685f,-0.35339f), new Vector3(0.93869f,-0.03692f,-0.34278f), new Vector3(0.94327f,-0.03681f,-0.32998f),
        new Vector3(0.94836f,-0.03640f,-0.31509f), new Vector3(0.95382f,-0.03559f,-0.29825f), new Vector3(0.95950f,-0.03426f,-0.27963f), new Vector3(0.96523f,-0.03233f,-0.25941f),
        new Vector3(0.97085f,-0.02970f,-0.23782f), new Vector3(0.97623f,-0.02632f,-0.21512f), new Vector3(0.98123f,-0.02215f,-0.19157f), new Vector3(0.98573f,-0.01718f,-0.16747f),
    };

    static readonly Vector3[] LegRightFootWalkYDir = {
        new Vector3(-0.08825f,-0.82544f,0.55754f), new Vector3(-0.09142f,-0.84764f,0.52264f), new Vector3(-0.09318f,-0.86827f,0.48726f), new Vector3(-0.09356f,-0.88713f,0.45193f),
        new Vector3(-0.09267f,-0.90409f,0.41717f), new Vector3(-0.09068f,-0.91907f,0.38351f), new Vector3(-0.08777f,-0.93208f,0.35145f), new Vector3(-0.08419f,-0.94317f,0.32147f),
        new Vector3(-0.08020f,-0.95243f,0.29401f), new Vector3(-0.07606f,-0.96001f,0.26944f), new Vector3(-0.07204f,-0.96605f,0.24810f), new Vector3(-0.06837f,-0.97072f,0.23028f),
        new Vector3(-0.06528f,-0.97417f,0.21619f), new Vector3(-0.06294f,-0.97652f,0.20601f), new Vector3(-0.06148f,-0.97790f,0.19985f), new Vector3(-0.06099f,-0.97835f,0.19779f),
        new Vector3(-0.06148f,-0.97790f,0.19985f), new Vector3(-0.06294f,-0.97652f,0.20601f), new Vector3(-0.06528f,-0.97417f,0.21619f), new Vector3(-0.06837f,-0.97072f,0.23028f),
        new Vector3(-0.07204f,-0.96605f,0.24810f), new Vector3(-0.07606f,-0.96001f,0.26944f), new Vector3(-0.08020f,-0.95243f,0.29401f), new Vector3(-0.08419f,-0.94317f,0.32147f),
        new Vector3(-0.08777f,-0.93208f,0.35145f), new Vector3(-0.09068f,-0.91907f,0.38351f), new Vector3(-0.09267f,-0.90409f,0.41717f), new Vector3(-0.09356f,-0.88713f,0.45193f),
        new Vector3(-0.09318f,-0.86827f,0.48726f), new Vector3(-0.09142f,-0.84764f,0.52264f), new Vector3(-0.08825f,-0.82544f,0.55754f), new Vector3(-0.08166f,-0.81584f,0.57248f),
        new Vector3(-0.07488f,-0.80617f,0.58692f), new Vector3(-0.06802f,-0.79656f,0.60072f), new Vector3(-0.06117f,-0.78713f,0.61375f), new Vector3(-0.05444f,-0.77800f,0.62591f),
        new Vector3(-0.04795f,-0.76929f,0.63710f), new Vector3(-0.04180f,-0.76113f,0.64725f), new Vector3(-0.03609f,-0.75363f,0.65631f), new Vector3(-0.03092f,-0.74690f,0.66422f),
        new Vector3(-0.02638f,-0.74103f,0.67096f), new Vector3(-0.02256f,-0.73610f,0.67650f), new Vector3(-0.01951f,-0.73219f,0.68082f), new Vector3(-0.01730f,-0.72936f,0.68391f),
        new Vector3(-0.01595f,-0.72764f,0.68577f), new Vector3(-0.01550f,-0.72707f,0.68639f), new Vector3(-0.01595f,-0.72764f,0.68577f), new Vector3(-0.01730f,-0.72936f,0.68391f),
        new Vector3(-0.01951f,-0.73219f,0.68082f), new Vector3(-0.02256f,-0.73610f,0.67650f), new Vector3(-0.02638f,-0.74103f,0.67096f), new Vector3(-0.03092f,-0.74690f,0.66422f),
        new Vector3(-0.03609f,-0.75363f,0.65631f), new Vector3(-0.04180f,-0.76113f,0.64725f), new Vector3(-0.04795f,-0.76929f,0.63710f), new Vector3(-0.05444f,-0.77800f,0.62591f),
        new Vector3(-0.06117f,-0.78713f,0.61375f), new Vector3(-0.06802f,-0.79656f,0.60072f), new Vector3(-0.07488f,-0.80617f,0.58692f), new Vector3(-0.08166f,-0.81584f,0.57248f),
    };

    static readonly Vector3[] LegRightFootWalkXDir = {
        new Vector3(0.98470f,0.01212f,0.17381f), new Vector3(0.97993f,0.01677f,0.19862f), new Vector3(0.97457f,0.02063f,0.22312f), new Vector3(0.96873f,0.02366f,0.24700f),
        new Vector3(0.96252f,0.02591f,0.26998f), new Vector3(0.95610f,0.02742f,0.29177f), new Vector3(0.94962f,0.02827f,0.31213f), new Vector3(0.94325f,0.02857f,0.33085f),
        new Vector3(0.93716f,0.02843f,0.34774f), new Vector3(0.93151f,0.02798f,0.36264f), new Vector3(0.92645f,0.02733f,0.37543f), new Vector3(0.92211f,0.02662f,0.38601f),
        new Vector3(0.91861f,0.02595f,0.39431f), new Vector3(0.91605f,0.02540f,0.40027f), new Vector3(0.91448f,0.02504f,0.40386f), new Vector3(0.91395f,0.02491f,0.40506f),
        new Vector3(0.91448f,0.02504f,0.40386f), new Vector3(0.91605f,0.02540f,0.40027f), new Vector3(0.91861f,0.02595f,0.39431f), new Vector3(0.92211f,0.02662f,0.38601f),
        new Vector3(0.92645f,0.02733f,0.37543f), new Vector3(0.93151f,0.02798f,0.36264f), new Vector3(0.93716f,0.02843f,0.34774f), new Vector3(0.94325f,0.02857f,0.33085f),
        new Vector3(0.94962f,0.02827f,0.31213f), new Vector3(0.95610f,0.02742f,0.29177f), new Vector3(0.96252f,0.02591f,0.26998f), new Vector3(0.96873f,0.02366f,0.24700f),
        new Vector3(0.97457f,0.02063f,0.22312f), new Vector3(0.97993f,0.01677f,0.19862f), new Vector3(0.98470f,0.01212f,0.17381f), new Vector3(0.98815f,0.00864f,0.15327f),
        new Vector3(0.99112f,0.00471f,0.13292f), new Vector3(0.99359f,0.00039f,0.11302f), new Vector3(0.99558f,-0.00423f,0.09381f), new Vector3(0.99710f,-0.00903f,0.07551f),
        new Vector3(0.99820f,-0.01390f,0.05834f), new Vector3(0.99892f,-0.01871f,0.04250f), new Vector3(0.99933f,-0.02334f,0.02814f), new Vector3(0.99950f,-0.02766f,0.01542f),
        new Vector3(0.99949f,-0.03154f,0.00447f), new Vector3(0.99938f,-0.03488f,-0.00463f), new Vector3(0.99922f,-0.03758f,-0.01178f), new Vector3(0.99907f,-0.03957f,-0.01693f),
        new Vector3(0.99897f,-0.04079f,-0.02004f), new Vector3(0.99893f,-0.04120f,-0.02108f), new Vector3(0.99897f,-0.04079f,-0.02004f), new Vector3(0.99907f,-0.03957f,-0.01693f),
        new Vector3(0.99922f,-0.03758f,-0.01178f), new Vector3(0.99938f,-0.03488f,-0.00463f), new Vector3(0.99949f,-0.03154f,0.00447f), new Vector3(0.99950f,-0.02766f,0.01542f),
        new Vector3(0.99933f,-0.02334f,0.02814f), new Vector3(0.99892f,-0.01871f,0.04250f), new Vector3(0.99820f,-0.01390f,0.05834f), new Vector3(0.99710f,-0.00903f,0.07551f),
        new Vector3(0.99558f,-0.00423f,0.09381f), new Vector3(0.99359f,0.00039f,0.11302f), new Vector3(0.99112f,0.00471f,0.13292f), new Vector3(0.98815f,0.00864f,0.15327f),
    };
    // ==== 2026-08-23 追加: 重量物歩行 (骨盤の位置 + 上半身) ====
    // 骨盤の位置と上半身の向きは、この歩行が「重いツボを担いでいる」ことを表す本体。
    // 旧データは脚の向きしか持っておらず、腰が固定・上半身が直立のままだった。

    /// <summary>クリップ内の足の最低 Y。腰の位置はここを 0 として返す。</summary>
    public const float GroundY = -0.20542f;

    /// <summary>骨盤の root ローカル位置 (接地正規化済み)。一歩ごとの沈み込みと左右移動が入っている。</summary>
    public static Vector3 SampleHipsPos(float phase01)
    {
        Vector3 p = SamplePos(HipsWalkPos, phase01);
        p.y -= GroundY;
        return p;
    }

    static Vector3 SamplePos(Vector3[] frames, float phase01)
    {
        phase01 = Mathf.Repeat(phase01, 1f);
        float f = phase01 * frames.Length;
        int i0 = Mathf.FloorToInt(f) % frames.Length;
        int i1 = (i0 + 1) % frames.Length;
        return Vector3.Lerp(frames[i0], frames[i1], f - Mathf.Floor(f));
    }

    public static void SampleSpine(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(SpineWalkYDir, phase01); xDir = Sample(SpineWalkXDir, phase01); }
    public static void SampleSpine01(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(Spine01WalkYDir, phase01); xDir = Sample(Spine01WalkXDir, phase01); }
    public static void SampleSpine02(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(Spine02WalkYDir, phase01); xDir = Sample(Spine02WalkXDir, phase01); }
    public static void SampleNeck(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(NeckWalkYDir, phase01); xDir = Sample(NeckWalkXDir, phase01); }
    public static void SampleHead(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(HeadWalkYDir, phase01); xDir = Sample(HeadWalkXDir, phase01); }

    static readonly Vector3[] HipsWalkPos = {
        new Vector3(-0.00315f,0.47340f,0.01698f), new Vector3(-0.01079f,0.47316f,0.01729f), new Vector3(-0.01852f,0.47559f,0.01680f), new Vector3(-0.02625f,0.47802f,0.01631f),
        new Vector3(-0.03397f,0.48044f,0.01581f), new Vector3(-0.03257f,0.48315f,0.01495f), new Vector3(-0.03118f,0.48585f,0.01408f), new Vector3(-0.02984f,0.48855f,0.01322f),
        new Vector3(-0.02857f,0.49125f,0.01236f), new Vector3(-0.02742f,0.49394f,0.01150f), new Vector3(-0.02641f,0.49663f,0.01065f), new Vector3(-0.02545f,0.49666f,0.01061f),
        new Vector3(-0.02467f,0.49668f,0.01058f), new Vector3(-0.02411f,0.49670f,0.01056f), new Vector3(-0.02377f,0.49671f,0.01054f), new Vector3(-0.02365f,0.49671f,0.01054f),
        new Vector3(-0.02377f,0.49671f,0.01054f), new Vector3(-0.02411f,0.49670f,0.01056f), new Vector3(-0.02467f,0.49668f,0.01058f), new Vector3(-0.02545f,0.49666f,0.01061f),
        new Vector3(-0.02641f,0.49663f,0.01065f), new Vector3(-0.02753f,0.49660f,0.01070f), new Vector3(-0.02869f,0.49390f,0.01155f), new Vector3(-0.02995f,0.49120f,0.01241f),
        new Vector3(-0.03130f,0.48850f,0.01328f), new Vector3(-0.03269f,0.48580f,0.01414f), new Vector3(-0.03409f,0.48310f,0.01501f), new Vector3(-0.02637f,0.48067f,0.01550f),
        new Vector3(-0.01864f,0.47825f,0.01600f), new Vector3(-0.01090f,0.47582f,0.01649f), new Vector3(-0.00315f,0.47340f,0.01698f), new Vector3(0.00448f,0.47363f,0.01667f),
        new Vector3(0.01212f,0.47386f,0.01636f), new Vector3(0.01965f,0.47674f,0.01524f), new Vector3(0.02718f,0.47963f,0.01413f), new Vector3(0.03471f,0.48252f,0.01301f),
        new Vector3(0.03314f,0.48513f,0.01227f), new Vector3(0.03161f,0.48775f,0.01152f), new Vector3(0.03015f,0.49036f,0.01077f), new Vector3(0.02881f,0.49298f,0.01002f),
        new Vector3(0.02760f,0.49560f,0.00926f), new Vector3(0.02655f,0.49823f,0.00849f), new Vector3(0.02580f,0.49821f,0.00852f), new Vector3(0.02525f,0.49819f,0.00855f),
        new Vector3(0.02491f,0.49818f,0.00856f), new Vector3(0.02480f,0.49818f,0.00856f), new Vector3(0.02491f,0.49818f,0.00856f), new Vector3(0.02525f,0.49819f,0.00855f),
        new Vector3(0.02580f,0.49821f,0.00852f), new Vector3(0.02655f,0.49823f,0.00849f), new Vector3(0.02748f,0.49826f,0.00846f), new Vector3(0.02869f,0.49564f,0.00922f),
        new Vector3(0.03004f,0.49302f,0.00997f), new Vector3(0.03149f,0.49040f,0.01072f), new Vector3(0.03302f,0.48779f,0.01146f), new Vector3(0.03460f,0.48518f,0.01221f),
        new Vector3(0.02707f,0.48229f,0.01332f), new Vector3(0.01954f,0.47940f,0.01444f), new Vector3(0.01201f,0.47651f,0.01556f), new Vector3(0.00448f,0.47363f,0.01667f),
    };

    static readonly Vector3[] SpineWalkYDir = {
        new Vector3(-0.03304f,0.90402f,0.42621f), new Vector3(-0.06056f,0.90275f,0.42587f), new Vector3(-0.08799f,0.90079f,0.42525f), new Vector3(-0.11525f,0.89813f,0.42436f),
        new Vector3(-0.14229f,0.89479f,0.42321f), new Vector3(-0.14013f,0.89526f,0.42293f), new Vector3(-0.13785f,0.89573f,0.42269f), new Vector3(-0.13552f,0.89616f,0.42252f),
        new Vector3(-0.13324f,0.89657f,0.42239f), new Vector3(-0.13108f,0.89692f,0.42231f), new Vector3(-0.12911f,0.89723f,0.42226f), new Vector3(-0.12741f,0.89749f,0.42224f),
        new Vector3(-0.12602f,0.89769f,0.42223f), new Vector3(-0.12499f,0.89783f,0.42223f), new Vector3(-0.12436f,0.89792f,0.42223f), new Vector3(-0.12415f,0.89795f,0.42223f),
        new Vector3(-0.12436f,0.89792f,0.42223f), new Vector3(-0.12499f,0.89783f,0.42223f), new Vector3(-0.12602f,0.89769f,0.42223f), new Vector3(-0.12741f,0.89749f,0.42224f),
        new Vector3(-0.12911f,0.89723f,0.42226f), new Vector3(-0.13108f,0.89692f,0.42231f), new Vector3(-0.13324f,0.89657f,0.42239f), new Vector3(-0.13552f,0.89616f,0.42252f),
        new Vector3(-0.13785f,0.89573f,0.42269f), new Vector3(-0.14013f,0.89526f,0.42293f), new Vector3(-0.14229f,0.89479f,0.42321f), new Vector3(-0.11525f,0.89813f,0.42436f),
        new Vector3(-0.08799f,0.90079f,0.42525f), new Vector3(-0.06056f,0.90275f,0.42587f), new Vector3(-0.03304f,0.90402f,0.42621f), new Vector3(-0.00547f,0.90458f,0.42627f),
        new Vector3(0.02207f,0.90443f,0.42605f), new Vector3(0.04954f,0.90357f,0.42556f), new Vector3(0.07686f,0.90201f,0.42482f), new Vector3(0.10399f,0.89974f,0.42386f),
        new Vector3(0.10182f,0.90029f,0.42321f), new Vector3(0.09961f,0.90080f,0.42265f), new Vector3(0.09743f,0.90127f,0.42217f), new Vector3(0.09536f,0.90168f,0.42176f),
        new Vector3(0.09348f,0.90203f,0.42143f), new Vector3(0.09184f,0.90232f,0.42117f), new Vector3(0.09050f,0.90255f,0.42097f), new Vector3(0.08951f,0.90271f,0.42083f),
        new Vector3(0.08890f,0.90281f,0.42075f), new Vector3(0.08870f,0.90284f,0.42072f), new Vector3(0.08890f,0.90281f,0.42075f), new Vector3(0.08951f,0.90271f,0.42083f),
        new Vector3(0.09050f,0.90255f,0.42097f), new Vector3(0.09184f,0.90232f,0.42117f), new Vector3(0.09348f,0.90203f,0.42143f), new Vector3(0.09536f,0.90168f,0.42176f),
        new Vector3(0.09743f,0.90127f,0.42217f), new Vector3(0.09961f,0.90080f,0.42265f), new Vector3(0.10182f,0.90029f,0.42321f), new Vector3(0.10399f,0.89974f,0.42386f),
        new Vector3(0.07686f,0.90201f,0.42482f), new Vector3(0.04954f,0.90357f,0.42556f), new Vector3(0.02207f,0.90443f,0.42605f), new Vector3(-0.00547f,0.90458f,0.42627f),
    };

    static readonly Vector3[] SpineWalkXDir = {
        new Vector3(0.99945f,0.02990f,0.01405f), new Vector3(0.99812f,0.05890f,0.01708f), new Vector3(0.99594f,0.08780f,0.02008f), new Vector3(0.99292f,0.11654f,0.02302f),
        new Vector3(0.98909f,0.14506f,0.02585f), new Vector3(0.98948f,0.14216f,0.02691f), new Vector3(0.98988f,0.13917f,0.02790f), new Vector3(0.99027f,0.13618f,0.02880f),
        new Vector3(0.99064f,0.13327f,0.02961f), new Vector3(0.99098f,0.13054f,0.03033f), new Vector3(0.99128f,0.12808f,0.03095f), new Vector3(0.99154f,0.12595f,0.03146f),
        new Vector3(0.99174f,0.12423f,0.03187f), new Vector3(0.99189f,0.12296f,0.03216f), new Vector3(0.99198f,0.12218f,0.03233f), new Vector3(0.99201f,0.12192f,0.03239f),
        new Vector3(0.99198f,0.12218f,0.03233f), new Vector3(0.99189f,0.12296f,0.03216f), new Vector3(0.99174f,0.12423f,0.03187f), new Vector3(0.99154f,0.12595f,0.03146f),
        new Vector3(0.99128f,0.12808f,0.03095f), new Vector3(0.99098f,0.13054f,0.03033f), new Vector3(0.99064f,0.13327f,0.02961f), new Vector3(0.99027f,0.13618f,0.02880f),
        new Vector3(0.98988f,0.13917f,0.02790f), new Vector3(0.98948f,0.14216f,0.02691f), new Vector3(0.98909f,0.14506f,0.02585f), new Vector3(0.99292f,0.11654f,0.02302f),
        new Vector3(0.99594f,0.08780f,0.02008f), new Vector3(0.99812f,0.05890f,0.01708f), new Vector3(0.99945f,0.02990f,0.01405f), new Vector3(0.99994f,0.00086f,0.01101f),
        new Vector3(0.99957f,-0.02816f,0.00799f), new Vector3(0.99836f,-0.05711f,0.00504f), new Vector3(0.99630f,-0.08592f,0.00218f), new Vector3(0.99342f,-0.11454f,-0.00057f),
        new Vector3(0.99374f,-0.11168f,-0.00152f), new Vector3(0.99406f,-0.10880f,-0.00239f), new Vector3(0.99436f,-0.10601f,-0.00318f), new Vector3(0.99463f,-0.10338f,-0.00388f),
        new Vector3(0.99488f,-0.10100f,-0.00448f), new Vector3(0.99508f,-0.09895f,-0.00498f), new Vector3(0.99524f,-0.09729f,-0.00537f), new Vector3(0.99536f,-0.09606f,-0.00566f),
        new Vector3(0.99543f,-0.09531f,-0.00583f), new Vector3(0.99545f,-0.09506f,-0.00588f), new Vector3(0.99543f,-0.09531f,-0.00583f), new Vector3(0.99536f,-0.09606f,-0.00566f),
        new Vector3(0.99524f,-0.09729f,-0.00537f), new Vector3(0.99508f,-0.09895f,-0.00498f), new Vector3(0.99488f,-0.10100f,-0.00448f), new Vector3(0.99463f,-0.10338f,-0.00388f),
        new Vector3(0.99436f,-0.10601f,-0.00318f), new Vector3(0.99406f,-0.10880f,-0.00239f), new Vector3(0.99374f,-0.11168f,-0.00152f), new Vector3(0.99342f,-0.11454f,-0.00057f),
        new Vector3(0.99630f,-0.08592f,0.00218f), new Vector3(0.99836f,-0.05711f,0.00504f), new Vector3(0.99957f,-0.02816f,0.00799f), new Vector3(0.99994f,0.00086f,0.01101f),
    };

    static readonly Vector3[] Spine01WalkYDir = {
        new Vector3(-0.02678f,0.94184f,0.33501f), new Vector3(-0.04851f,0.94105f,0.33477f), new Vector3(-0.07016f,0.93986f,0.33428f), new Vector3(-0.09170f,0.93827f,0.33353f),
        new Vector3(-0.11308f,0.93628f,0.33255f), new Vector3(-0.11200f,0.93651f,0.33226f), new Vector3(-0.11080f,0.93674f,0.33203f), new Vector3(-0.10953f,0.93695f,0.33184f),
        new Vector3(-0.10824f,0.93715f,0.33171f), new Vector3(-0.10700f,0.93733f,0.33162f), new Vector3(-0.10584f,0.93748f,0.33156f), new Vector3(-0.10483f,0.93760f,0.33153f),
        new Vector3(-0.10400f,0.93770f,0.33152f), new Vector3(-0.10339f,0.93777f,0.33151f), new Vector3(-0.10301f,0.93781f,0.33151f), new Vector3(-0.10288f,0.93782f,0.33151f),
        new Vector3(-0.10301f,0.93781f,0.33151f), new Vector3(-0.10339f,0.93777f,0.33151f), new Vector3(-0.10400f,0.93770f,0.33152f), new Vector3(-0.10483f,0.93760f,0.33153f),
        new Vector3(-0.10584f,0.93748f,0.33156f), new Vector3(-0.10700f,0.93733f,0.33162f), new Vector3(-0.10824f,0.93715f,0.33171f), new Vector3(-0.10953f,0.93695f,0.33184f),
        new Vector3(-0.11080f,0.93674f,0.33203f), new Vector3(-0.11200f,0.93651f,0.33226f), new Vector3(-0.11308f,0.93628f,0.33255f), new Vector3(-0.09170f,0.93827f,0.33353f),
        new Vector3(-0.07016f,0.93986f,0.33428f), new Vector3(-0.04851f,0.94105f,0.33477f), new Vector3(-0.02678f,0.94184f,0.33501f), new Vector3(-0.00503f,0.94221f,0.33498f),
        new Vector3(0.01670f,0.94218f,0.33468f), new Vector3(0.03836f,0.94174f,0.33414f), new Vector3(0.05990f,0.94089f,0.33337f), new Vector3(0.08129f,0.93963f,0.33239f),
        new Vector3(0.08018f,0.93995f,0.33175f), new Vector3(0.07899f,0.94025f,0.33119f), new Vector3(0.07779f,0.94052f,0.33071f), new Vector3(0.07661f,0.94076f,0.33031f),
        new Vector3(0.07552f,0.94096f,0.32998f), new Vector3(0.07457f,0.94113f,0.32971f), new Vector3(0.07378f,0.94126f,0.32952f), new Vector3(0.07319f,0.94136f,0.32938f),
        new Vector3(0.07283f,0.94141f,0.32930f), new Vector3(0.07270f,0.94143f,0.32927f), new Vector3(0.07283f,0.94141f,0.32930f), new Vector3(0.07319f,0.94136f,0.32938f),
        new Vector3(0.07378f,0.94126f,0.32952f), new Vector3(0.07457f,0.94113f,0.32971f), new Vector3(0.07552f,0.94096f,0.32998f), new Vector3(0.07661f,0.94076f,0.33031f),
        new Vector3(0.07779f,0.94052f,0.33071f), new Vector3(0.07899f,0.94025f,0.33119f), new Vector3(0.08018f,0.93995f,0.33175f), new Vector3(0.08129f,0.93963f,0.33239f),
        new Vector3(0.05990f,0.94089f,0.33337f), new Vector3(0.03836f,0.94174f,0.33414f), new Vector3(0.01670f,0.94218f,0.33468f), new Vector3(-0.00503f,0.94221f,0.33498f),
    };

    static readonly Vector3[] Spine01WalkXDir = {
        new Vector3(0.99964f,0.02429f,0.01162f), new Vector3(0.99882f,0.04681f,0.01314f), new Vector3(0.99749f,0.06926f,0.01462f), new Vector3(0.99567f,0.09160f,0.01606f),
        new Vector3(0.99335f,0.11378f,0.01743f), new Vector3(0.99351f,0.11215f,0.01879f), new Vector3(0.99368f,0.11042f,0.02008f), new Vector3(0.99385f,0.10864f,0.02128f),
        new Vector3(0.99402f,0.10689f,0.02237f), new Vector3(0.99417f,0.10523f,0.02334f), new Vector3(0.99431f,0.10371f,0.02418f), new Vector3(0.99443f,0.10239f,0.02488f),
        new Vector3(0.99453f,0.10132f,0.02543f), new Vector3(0.99460f,0.10052f,0.02583f), new Vector3(0.99464f,0.10003f,0.02608f), new Vector3(0.99466f,0.09987f,0.02616f),
        new Vector3(0.99464f,0.10003f,0.02608f), new Vector3(0.99460f,0.10052f,0.02583f), new Vector3(0.99453f,0.10132f,0.02543f), new Vector3(0.99443f,0.10239f,0.02488f),
        new Vector3(0.99431f,0.10371f,0.02418f), new Vector3(0.99417f,0.10523f,0.02334f), new Vector3(0.99402f,0.10689f,0.02237f), new Vector3(0.99385f,0.10864f,0.02128f),
        new Vector3(0.99368f,0.11042f,0.02008f), new Vector3(0.99351f,0.11215f,0.01879f), new Vector3(0.99335f,0.11378f,0.01743f), new Vector3(0.99567f,0.09160f,0.01606f),
        new Vector3(0.99749f,0.06926f,0.01462f), new Vector3(0.99882f,0.04681f,0.01314f), new Vector3(0.99964f,0.02429f,0.01162f), new Vector3(0.99995f,0.00174f,0.01011f),
        new Vector3(0.99975f,-0.02079f,0.00862f), new Vector3(0.99904f,-0.04325f,0.00719f), new Vector3(0.99783f,-0.06559f,0.00583f), new Vector3(0.99613f,-0.08779f,0.00457f),
        new Vector3(0.99628f,-0.08615f,0.00332f), new Vector3(0.99642f,-0.08447f,0.00215f), new Vector3(0.99657f,-0.08281f,0.00109f), new Vector3(0.99670f,-0.08122f,0.00014f),
        new Vector3(0.99681f,-0.07977f,-0.00069f), new Vector3(0.99691f,-0.07850f,-0.00137f), new Vector3(0.99699f,-0.07747f,-0.00192f), new Vector3(0.99705f,-0.07671f,-0.00231f),
        new Vector3(0.99709f,-0.07624f,-0.00255f), new Vector3(0.99710f,-0.07608f,-0.00263f), new Vector3(0.99709f,-0.07624f,-0.00255f), new Vector3(0.99705f,-0.07671f,-0.00231f),
        new Vector3(0.99699f,-0.07747f,-0.00192f), new Vector3(0.99691f,-0.07850f,-0.00137f), new Vector3(0.99681f,-0.07977f,-0.00069f), new Vector3(0.99670f,-0.08122f,0.00014f),
        new Vector3(0.99657f,-0.08281f,0.00109f), new Vector3(0.99642f,-0.08447f,0.00215f), new Vector3(0.99628f,-0.08615f,0.00332f), new Vector3(0.99613f,-0.08779f,0.00457f),
        new Vector3(0.99783f,-0.06559f,0.00583f), new Vector3(0.99904f,-0.04325f,0.00719f), new Vector3(0.99975f,-0.02079f,0.00862f), new Vector3(0.99995f,0.00174f,0.01011f),
    };

    static readonly Vector3[] Spine02WalkYDir = {
        new Vector3(-0.01977f,0.99572f,0.09024f), new Vector3(-0.03301f,0.99538f,0.09020f), new Vector3(-0.04621f,0.99487f,0.09001f), new Vector3(-0.05932f,0.99420f,0.08967f),
        new Vector3(-0.07232f,0.99339f,0.08918f), new Vector3(-0.07267f,0.99337f,0.08907f), new Vector3(-0.07289f,0.99336f,0.08898f), new Vector3(-0.07301f,0.99336f,0.08891f),
        new Vector3(-0.07304f,0.99336f,0.08887f), new Vector3(-0.07302f,0.99337f,0.08884f), new Vector3(-0.07295f,0.99337f,0.08882f), new Vector3(-0.07287f,0.99338f,0.08881f),
        new Vector3(-0.07279f,0.99339f,0.08881f), new Vector3(-0.07272f,0.99339f,0.08881f), new Vector3(-0.07268f,0.99339f,0.08882f), new Vector3(-0.07266f,0.99339f,0.08882f),
        new Vector3(-0.07268f,0.99339f,0.08882f), new Vector3(-0.07272f,0.99339f,0.08881f), new Vector3(-0.07279f,0.99339f,0.08881f), new Vector3(-0.07287f,0.99338f,0.08881f),
        new Vector3(-0.07295f,0.99337f,0.08882f), new Vector3(-0.07302f,0.99337f,0.08884f), new Vector3(-0.07304f,0.99336f,0.08887f), new Vector3(-0.07301f,0.99336f,0.08891f),
        new Vector3(-0.07289f,0.99336f,0.08898f), new Vector3(-0.07267f,0.99337f,0.08907f), new Vector3(-0.07232f,0.99339f,0.08918f), new Vector3(-0.05932f,0.99420f,0.08967f),
        new Vector3(-0.04621f,0.99487f,0.09001f), new Vector3(-0.03301f,0.99538f,0.09020f), new Vector3(-0.01977f,0.99572f,0.09024f), new Vector3(-0.00652f,0.99591f,0.09012f),
        new Vector3(0.00671f,0.99593f,0.08985f), new Vector3(0.01988f,0.99579f,0.08943f), new Vector3(0.03295f,0.99550f,0.08888f), new Vector3(0.04590f,0.99504f,0.08821f),
        new Vector3(0.04617f,0.99507f,0.08774f), new Vector3(0.04633f,0.99510f,0.08731f), new Vector3(0.04640f,0.99513f,0.08693f), new Vector3(0.04641f,0.99516f,0.08660f),
        new Vector3(0.04638f,0.99519f,0.08633f), new Vector3(0.04632f,0.99521f,0.08610f), new Vector3(0.04626f,0.99523f,0.08593f), new Vector3(0.04621f,0.99524f,0.08581f),
        new Vector3(0.04617f,0.99525f,0.08573f), new Vector3(0.04616f,0.99525f,0.08571f), new Vector3(0.04617f,0.99525f,0.08573f), new Vector3(0.04621f,0.99524f,0.08581f),
        new Vector3(0.04626f,0.99523f,0.08593f), new Vector3(0.04632f,0.99521f,0.08610f), new Vector3(0.04638f,0.99519f,0.08633f), new Vector3(0.04641f,0.99516f,0.08660f),
        new Vector3(0.04640f,0.99513f,0.08693f), new Vector3(0.04633f,0.99510f,0.08731f), new Vector3(0.04617f,0.99507f,0.08774f), new Vector3(0.04590f,0.99504f,0.08821f),
        new Vector3(0.03295f,0.99550f,0.08888f), new Vector3(0.01988f,0.99579f,0.08943f), new Vector3(0.00671f,0.99593f,0.08985f), new Vector3(-0.00652f,0.99591f,0.09012f),
    };

    static readonly Vector3[] Spine02WalkXDir = {
        new Vector3(0.99976f,0.01884f,0.01113f), new Vector3(0.99935f,0.03158f,0.01725f), new Vector3(0.99875f,0.04428f,0.02330f), new Vector3(0.99795f,0.05691f,0.02919f),
        new Vector3(0.99698f,0.06946f,0.03485f), new Vector3(0.99675f,0.06922f,0.04125f), new Vector3(0.99650f,0.06889f,0.04728f), new Vector3(0.99625f,0.06849f,0.05287f),
        new Vector3(0.99600f,0.06805f,0.05797f), new Vector3(0.99575f,0.06760f,0.06250f), new Vector3(0.99553f,0.06717f,0.06643f), new Vector3(0.99533f,0.06678f,0.06971f),
        new Vector3(0.99517f,0.06646f,0.07230f), new Vector3(0.99505f,0.06621f,0.07417f), new Vector3(0.99497f,0.06606f,0.07530f), new Vector3(0.99495f,0.06601f,0.07567f),
        new Vector3(0.99497f,0.06606f,0.07530f), new Vector3(0.99505f,0.06621f,0.07417f), new Vector3(0.99517f,0.06646f,0.07230f), new Vector3(0.99533f,0.06678f,0.06971f),
        new Vector3(0.99553f,0.06717f,0.06643f), new Vector3(0.99575f,0.06760f,0.06250f), new Vector3(0.99600f,0.06805f,0.05797f), new Vector3(0.99625f,0.06849f,0.05287f),
        new Vector3(0.99650f,0.06889f,0.04728f), new Vector3(0.99675f,0.06922f,0.04125f), new Vector3(0.99698f,0.06946f,0.03485f), new Vector3(0.99795f,0.05691f,0.02919f),
        new Vector3(0.99875f,0.04428f,0.02330f), new Vector3(0.99935f,0.03158f,0.01725f), new Vector3(0.99976f,0.01884f,0.01113f), new Vector3(0.99997f,0.00609f,0.00501f),
        new Vector3(0.99998f,-0.00665f,-0.00103f), new Vector3(0.99979f,-0.01934f,-0.00691f), new Vector3(0.99941f,-0.03196f,-0.01255f), new Vector3(0.99885f,-0.04449f,-0.01788f),
        new Vector3(0.99874f,-0.04423f,-0.02390f), new Vector3(0.99860f,-0.04390f,-0.02947f), new Vector3(0.99845f,-0.04354f,-0.03456f), new Vector3(0.99830f,-0.04316f,-0.03908f),
        new Vector3(0.99816f,-0.04279f,-0.04300f), new Vector3(0.99803f,-0.04245f,-0.04628f), new Vector3(0.99792f,-0.04217f,-0.04886f), new Vector3(0.99783f,-0.04195f,-0.05073f),
        new Vector3(0.99778f,-0.04182f,-0.05185f), new Vector3(0.99776f,-0.04178f,-0.05223f), new Vector3(0.99778f,-0.04182f,-0.05185f), new Vector3(0.99783f,-0.04195f,-0.05073f),
        new Vector3(0.99792f,-0.04217f,-0.04886f), new Vector3(0.99803f,-0.04245f,-0.04628f), new Vector3(0.99816f,-0.04279f,-0.04300f), new Vector3(0.99830f,-0.04316f,-0.03908f),
        new Vector3(0.99845f,-0.04354f,-0.03456f), new Vector3(0.99860f,-0.04390f,-0.02947f), new Vector3(0.99874f,-0.04423f,-0.02390f), new Vector3(0.99885f,-0.04449f,-0.01788f),
        new Vector3(0.99941f,-0.03196f,-0.01255f), new Vector3(0.99979f,-0.01934f,-0.00691f), new Vector3(0.99998f,-0.00665f,-0.00103f), new Vector3(0.99997f,0.00609f,0.00501f),
    };

    static readonly Vector3[] NeckWalkYDir = {
        new Vector3(-0.04610f,0.85082f,0.52343f), new Vector3(-0.07237f,0.84921f,0.52307f), new Vector3(-0.09854f,0.84697f,0.52243f), new Vector3(-0.12453f,0.84409f,0.52155f),
        new Vector3(-0.15031f,0.84058f,0.52042f), new Vector3(-0.14841f,0.84109f,0.52013f), new Vector3(-0.14639f,0.84159f,0.51990f), new Vector3(-0.14432f,0.84206f,0.51972f),
        new Vector3(-0.14228f,0.84249f,0.51959f), new Vector3(-0.14034f,0.84287f,0.51950f), new Vector3(-0.13856f,0.84320f,0.51944f), new Vector3(-0.13702f,0.84347f,0.51941f),
        new Vector3(-0.13576f,0.84368f,0.51939f), new Vector3(-0.13483f,0.84383f,0.51939f), new Vector3(-0.13426f,0.84392f,0.51939f), new Vector3(-0.13407f,0.84395f,0.51939f),
        new Vector3(-0.13426f,0.84392f,0.51939f), new Vector3(-0.13483f,0.84383f,0.51939f), new Vector3(-0.13576f,0.84368f,0.51939f), new Vector3(-0.13702f,0.84347f,0.51941f),
        new Vector3(-0.13856f,0.84320f,0.51944f), new Vector3(-0.14034f,0.84287f,0.51950f), new Vector3(-0.14228f,0.84249f,0.51959f), new Vector3(-0.14432f,0.84206f,0.51972f),
        new Vector3(-0.14639f,0.84159f,0.51990f), new Vector3(-0.14841f,0.84109f,0.52013f), new Vector3(-0.15031f,0.84058f,0.52042f), new Vector3(-0.12453f,0.84409f,0.52155f),
        new Vector3(-0.09854f,0.84697f,0.52243f), new Vector3(-0.07237f,0.84921f,0.52307f), new Vector3(-0.04610f,0.85082f,0.52343f), new Vector3(-0.01978f,0.85178f,0.52353f),
        new Vector3(0.00653f,0.85208f,0.52337f), new Vector3(0.03278f,0.85174f,0.52295f), new Vector3(0.05889f,0.85073f,0.52229f), new Vector3(0.08483f,0.84907f,0.52142f),
        new Vector3(0.08291f,0.84963f,0.52083f), new Vector3(0.08094f,0.85014f,0.52031f), new Vector3(0.07899f,0.85059f,0.51986f), new Vector3(0.07712f,0.85099f,0.51949f),
        new Vector3(0.07542f,0.85133f,0.51918f), new Vector3(0.07393f,0.85161f,0.51894f), new Vector3(0.07272f,0.85182f,0.51876f), new Vector3(0.07183f,0.85197f,0.51863f),
        new Vector3(0.07128f,0.85207f,0.51856f), new Vector3(0.07109f,0.85210f,0.51854f), new Vector3(0.07128f,0.85207f,0.51856f), new Vector3(0.07183f,0.85197f,0.51863f),
        new Vector3(0.07272f,0.85182f,0.51876f), new Vector3(0.07393f,0.85161f,0.51894f), new Vector3(0.07542f,0.85133f,0.51918f), new Vector3(0.07712f,0.85099f,0.51949f),
        new Vector3(0.07899f,0.85059f,0.51986f), new Vector3(0.08094f,0.85014f,0.52031f), new Vector3(0.08291f,0.84963f,0.52083f), new Vector3(0.08483f,0.84907f,0.52142f),
        new Vector3(0.05889f,0.85073f,0.52229f), new Vector3(0.03278f,0.85174f,0.52295f), new Vector3(0.00653f,0.85208f,0.52337f), new Vector3(-0.01978f,0.85178f,0.52353f),
    };

    static readonly Vector3[] NeckWalkXDir = {
        new Vector3(0.99886f,0.03284f,0.03460f), new Vector3(0.99738f,0.06182f,0.03763f), new Vector3(0.99505f,0.09070f,0.04063f), new Vector3(0.99189f,0.11943f,0.04356f),
        new Vector3(0.98791f,0.14793f,0.04639f), new Vector3(0.98829f,0.14504f,0.04745f), new Vector3(0.98867f,0.14206f,0.04843f), new Vector3(0.98905f,0.13907f,0.04933f),
        new Vector3(0.98942f,0.13617f,0.05015f), new Vector3(0.98975f,0.13344f,0.05087f), new Vector3(0.99005f,0.13098f,0.05148f), new Vector3(0.99030f,0.12886f,0.05200f),
        new Vector3(0.99050f,0.12713f,0.05240f), new Vector3(0.99065f,0.12586f,0.05269f), new Vector3(0.99074f,0.12509f,0.05286f), new Vector3(0.99077f,0.12483f,0.05292f),
        new Vector3(0.99074f,0.12509f,0.05286f), new Vector3(0.99065f,0.12586f,0.05269f), new Vector3(0.99050f,0.12713f,0.05240f), new Vector3(0.99030f,0.12886f,0.05200f),
        new Vector3(0.99005f,0.13098f,0.05148f), new Vector3(0.98975f,0.13344f,0.05087f), new Vector3(0.98942f,0.13617f,0.05015f), new Vector3(0.98905f,0.13907f,0.04933f),
        new Vector3(0.98867f,0.14206f,0.04843f), new Vector3(0.98829f,0.14504f,0.04745f), new Vector3(0.98791f,0.14793f,0.04639f), new Vector3(0.99189f,0.11943f,0.04356f),
        new Vector3(0.99505f,0.09070f,0.04063f), new Vector3(0.99738f,0.06182f,0.03763f), new Vector3(0.99886f,0.03284f,0.03460f), new Vector3(0.99949f,0.00381f,0.03156f),
        new Vector3(0.99927f,-0.02520f,0.02855f), new Vector3(0.99821f,-0.05413f,0.02560f), new Vector3(0.99630f,-0.08293f,0.02273f), new Vector3(0.99356f,-0.11154f,0.01998f),
        new Vector3(0.99390f,-0.10866f,0.01903f), new Vector3(0.99422f,-0.10577f,0.01816f), new Vector3(0.99453f,-0.10297f,0.01737f), new Vector3(0.99481f,-0.10033f,0.01667f),
        new Vector3(0.99506f,-0.09795f,0.01607f), new Vector3(0.99527f,-0.09589f,0.01557f), new Vector3(0.99544f,-0.09422f,0.01517f), new Vector3(0.99556f,-0.09299f,0.01489f),
        new Vector3(0.99563f,-0.09224f,0.01472f), new Vector3(0.99565f,-0.09199f,0.01466f), new Vector3(0.99563f,-0.09224f,0.01472f), new Vector3(0.99556f,-0.09299f,0.01489f),
        new Vector3(0.99544f,-0.09422f,0.01517f), new Vector3(0.99527f,-0.09589f,0.01557f), new Vector3(0.99506f,-0.09795f,0.01607f), new Vector3(0.99481f,-0.10033f,0.01667f),
        new Vector3(0.99453f,-0.10297f,0.01737f), new Vector3(0.99422f,-0.10577f,0.01816f), new Vector3(0.99390f,-0.10866f,0.01903f), new Vector3(0.99356f,-0.11154f,0.01998f),
        new Vector3(0.99630f,-0.08293f,0.02273f), new Vector3(0.99821f,-0.05413f,0.02560f), new Vector3(0.99927f,-0.02520f,0.02855f), new Vector3(0.99949f,0.00381f,0.03156f),
    };

    static readonly Vector3[] HeadWalkYDir = {
        new Vector3(-0.01219f,0.08138f,0.99661f), new Vector3(-0.01758f,0.08117f,0.99655f), new Vector3(-0.02296f,0.08102f,0.99645f), new Vector3(-0.02831f,0.08093f,0.99632f),
        new Vector3(-0.03360f,0.08089f,0.99616f), new Vector3(-0.03445f,0.08114f,0.99611f), new Vector3(-0.03521f,0.08134f,0.99606f), new Vector3(-0.03588f,0.08150f,0.99603f),
        new Vector3(-0.03646f,0.08163f,0.99600f), new Vector3(-0.03696f,0.08171f,0.99597f), new Vector3(-0.03737f,0.08177f,0.99595f), new Vector3(-0.03771f,0.08180f,0.99594f),
        new Vector3(-0.03796f,0.08182f,0.99592f), new Vector3(-0.03814f,0.08183f,0.99592f), new Vector3(-0.03825f,0.08183f,0.99591f), new Vector3(-0.03829f,0.08183f,0.99591f),
        new Vector3(-0.03825f,0.08183f,0.99591f), new Vector3(-0.03814f,0.08183f,0.99592f), new Vector3(-0.03796f,0.08182f,0.99592f), new Vector3(-0.03771f,0.08180f,0.99594f),
        new Vector3(-0.03737f,0.08177f,0.99595f), new Vector3(-0.03696f,0.08171f,0.99597f), new Vector3(-0.03646f,0.08163f,0.99600f), new Vector3(-0.03588f,0.08150f,0.99603f),
        new Vector3(-0.03521f,0.08134f,0.99606f), new Vector3(-0.03445f,0.08114f,0.99611f), new Vector3(-0.03360f,0.08089f,0.99616f), new Vector3(-0.02831f,0.08093f,0.99632f),
        new Vector3(-0.02296f,0.08102f,0.99645f), new Vector3(-0.01758f,0.08117f,0.99655f), new Vector3(-0.01219f,0.08138f,0.99661f), new Vector3(-0.00679f,0.08166f,0.99664f),
        new Vector3(-0.00141f,0.08200f,0.99663f), new Vector3(0.00394f,0.08238f,0.99659f), new Vector3(0.00923f,0.08281f,0.99652f), new Vector3(0.01446f,0.08325f,0.99642f),
        new Vector3(0.01523f,0.08389f,0.99636f), new Vector3(0.01592f,0.08445f,0.99630f), new Vector3(0.01652f,0.08494f,0.99625f), new Vector3(0.01703f,0.08535f,0.99621f),
        new Vector3(0.01746f,0.08569f,0.99617f), new Vector3(0.01781f,0.08597f,0.99614f), new Vector3(0.01807f,0.08617f,0.99612f), new Vector3(0.01826f,0.08632f,0.99610f),
        new Vector3(0.01838f,0.08640f,0.99609f), new Vector3(0.01841f,0.08643f,0.99609f), new Vector3(0.01838f,0.08640f,0.99609f), new Vector3(0.01826f,0.08632f,0.99610f),
        new Vector3(0.01807f,0.08617f,0.99612f), new Vector3(0.01781f,0.08597f,0.99614f), new Vector3(0.01746f,0.08569f,0.99617f), new Vector3(0.01703f,0.08535f,0.99621f),
        new Vector3(0.01652f,0.08494f,0.99625f), new Vector3(0.01592f,0.08445f,0.99630f), new Vector3(0.01523f,0.08389f,0.99636f), new Vector3(0.01446f,0.08325f,0.99642f),
        new Vector3(0.00923f,0.08281f,0.99652f), new Vector3(0.00394f,0.08238f,0.99659f), new Vector3(-0.00141f,0.08200f,0.99663f), new Vector3(-0.00679f,0.08166f,0.99664f),
    };

    static readonly Vector3[] HeadWalkXDir = {
        new Vector3(0.99973f,0.02086f,0.01052f), new Vector3(0.99866f,0.04987f,0.01356f), new Vector3(0.99675f,0.07879f,0.01657f), new Vector3(0.99401f,0.10755f,0.01951f),
        new Vector3(0.99044f,0.13610f,0.02236f), new Vector3(0.99081f,0.13320f,0.02342f), new Vector3(0.99119f,0.13021f,0.02440f), new Vector3(0.99155f,0.12721f,0.02531f),
        new Vector3(0.99190f,0.12430f,0.02612f), new Vector3(0.99222f,0.12157f,0.02684f), new Vector3(0.99250f,0.11910f,0.02746f), new Vector3(0.99274f,0.11697f,0.02798f),
        new Vector3(0.99293f,0.11525f,0.02838f), new Vector3(0.99307f,0.11398f,0.02867f), new Vector3(0.99315f,0.11320f,0.02884f), new Vector3(0.99318f,0.11294f,0.02890f),
        new Vector3(0.99315f,0.11320f,0.02884f), new Vector3(0.99307f,0.11398f,0.02867f), new Vector3(0.99293f,0.11525f,0.02838f), new Vector3(0.99274f,0.11697f,0.02798f),
        new Vector3(0.99250f,0.11910f,0.02746f), new Vector3(0.99222f,0.12157f,0.02684f), new Vector3(0.99190f,0.12430f,0.02612f), new Vector3(0.99155f,0.12721f,0.02531f),
        new Vector3(0.99119f,0.13021f,0.02440f), new Vector3(0.99081f,0.13320f,0.02342f), new Vector3(0.99044f,0.13610f,0.02236f), new Vector3(0.99401f,0.10755f,0.01951f),
        new Vector3(0.99675f,0.07879f,0.01657f), new Vector3(0.99866f,0.04987f,0.01356f), new Vector3(0.99973f,0.02086f,0.01052f), new Vector3(0.99994f,-0.00818f,0.00748f),
        new Vector3(0.99930f,-0.03720f,0.00447f), new Vector3(0.99781f,-0.06614f,0.00152f), new Vector3(0.99548f,-0.09493f,-0.00133f), new Vector3(0.99233f,-0.12354f,-0.00408f),
        new Vector3(0.99268f,-0.12067f,-0.00502f), new Vector3(0.99302f,-0.11780f,-0.00588f), new Vector3(0.99334f,-0.11501f,-0.00666f), new Vector3(0.99364f,-0.11239f,-0.00736f),
        new Vector3(0.99390f,-0.11002f,-0.00796f), new Vector3(0.99412f,-0.10797f,-0.00845f), new Vector3(0.99429f,-0.10630f,-0.00885f), new Vector3(0.99442f,-0.10508f,-0.00913f),
        new Vector3(0.99450f,-0.10433f,-0.00930f), new Vector3(0.99453f,-0.10408f,-0.00936f), new Vector3(0.99450f,-0.10433f,-0.00930f), new Vector3(0.99442f,-0.10508f,-0.00913f),
        new Vector3(0.99429f,-0.10630f,-0.00885f), new Vector3(0.99412f,-0.10797f,-0.00845f), new Vector3(0.99390f,-0.11002f,-0.00796f), new Vector3(0.99364f,-0.11239f,-0.00736f),
        new Vector3(0.99334f,-0.11501f,-0.00666f), new Vector3(0.99302f,-0.11780f,-0.00588f), new Vector3(0.99268f,-0.12067f,-0.00502f), new Vector3(0.99233f,-0.12354f,-0.00408f),
        new Vector3(0.99548f,-0.09493f,-0.00133f), new Vector3(0.99781f,-0.06614f,0.00152f), new Vector3(0.99930f,-0.03720f,0.00447f), new Vector3(0.99994f,-0.00818f,0.00748f),
    };
}
