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
        new Vector3(0.44799f,-0.73760f,0.50522f), new Vector3(0.44209f,-0.69750f,0.56396f), new Vector3(0.43328f,-0.65468f,0.61940f), new Vector3(0.42186f,-0.60998f,0.67079f),
        new Vector3(0.40827f,-0.56430f,0.71755f), new Vector3(0.39303f,-0.51862f,0.75931f), new Vector3(0.37675f,-0.47392f,0.79590f), new Vector3(0.36005f,-0.43117f,0.82732f),
        new Vector3(0.34356f,-0.39126f,0.85374f), new Vector3(0.32791f,-0.35502f,0.87547f), new Vector3(0.31364f,-0.32315f,0.89286f), new Vector3(0.30127f,-0.29627f,0.90635f),
        new Vector3(0.29120f,-0.27486f,0.91633f), new Vector3(0.28377f,-0.25931f,0.92317f), new Vector3(0.27921f,-0.24987f,0.92715f), new Vector3(0.27768f,-0.24671f,0.92846f),
        new Vector3(0.27921f,-0.24987f,0.92715f), new Vector3(0.28377f,-0.25931f,0.92317f), new Vector3(0.29120f,-0.27486f,0.91633f), new Vector3(0.30127f,-0.29627f,0.90635f),
        new Vector3(0.31364f,-0.32315f,0.89286f), new Vector3(0.32791f,-0.35502f,0.87547f), new Vector3(0.34356f,-0.39126f,0.85374f), new Vector3(0.36005f,-0.43117f,0.82732f),
        new Vector3(0.37675f,-0.47392f,0.79590f), new Vector3(0.39303f,-0.51862f,0.75931f), new Vector3(0.40827f,-0.56430f,0.71755f), new Vector3(0.42186f,-0.60998f,0.67079f),
        new Vector3(0.43328f,-0.65468f,0.61940f), new Vector3(0.44209f,-0.69750f,0.56396f), new Vector3(0.44799f,-0.73760f,0.50522f), new Vector3(0.45082f,-0.77430f,0.44409f),
        new Vector3(0.45057f,-0.80707f,0.38160f), new Vector3(0.44738f,-0.83558f,0.31884f), new Vector3(0.44154f,-0.85967f,0.25693f), new Vector3(0.43345f,-0.87939f,0.19696f),
        new Vector3(0.42363f,-0.89496f,0.13995f), new Vector3(0.41265f,-0.90674f,0.08682f), new Vector3(0.40113f,-0.91522f,0.03837f), new Vector3(0.38966f,-0.92095f,-0.00473f),
        new Vector3(0.37885f,-0.92451f,-0.04194f), new Vector3(0.36921f,-0.92649f,-0.07286f), new Vector3(0.36121f,-0.92741f,-0.09717f), new Vector3(0.35522f,-0.92772f,-0.11467f),
        new Vector3(0.35152f,-0.92777f,-0.12522f), new Vector3(0.35027f,-0.92776f,-0.12875f), new Vector3(0.35152f,-0.92777f,-0.12522f), new Vector3(0.35522f,-0.92772f,-0.11467f),
        new Vector3(0.36121f,-0.92741f,-0.09717f), new Vector3(0.36921f,-0.92649f,-0.07286f), new Vector3(0.37885f,-0.92451f,-0.04194f), new Vector3(0.38966f,-0.92095f,-0.00473f),
        new Vector3(0.40113f,-0.91522f,0.03837f), new Vector3(0.41265f,-0.90674f,0.08682f), new Vector3(0.42363f,-0.89496f,0.13995f), new Vector3(0.43345f,-0.87939f,0.19696f),
        new Vector3(0.44154f,-0.85967f,0.25693f), new Vector3(0.44738f,-0.83558f,0.31884f), new Vector3(0.45057f,-0.80707f,0.38160f), new Vector3(0.45082f,-0.77430f,0.44409f),
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
        new Vector3(-0.09628f,-0.61409f,-0.78334f), new Vector3(-0.06655f,-0.63793f,-0.76721f), new Vector3(-0.03718f,-0.66067f,-0.74976f), new Vector3(-0.00854f,-0.68204f,-0.73127f),
        new Vector3(0.01900f,-0.70185f,-0.71207f), new Vector3(0.04511f,-0.71995f,-0.69256f), new Vector3(0.06947f,-0.73624f,-0.67314f), new Vector3(0.09184f,-0.75067f,-0.65426f),
        new Vector3(0.11199f,-0.76322f,-0.63636f), new Vector3(0.12974f,-0.77392f,-0.61985f), new Vector3(0.14494f,-0.78281f,-0.60514f), new Vector3(0.15750f,-0.78996f,-0.59258f),
        new Vector3(0.16734f,-0.79543f,-0.58248f), new Vector3(0.17440f,-0.79929f,-0.57508f), new Vector3(0.17864f,-0.80158f,-0.57057f), new Vector3(0.18006f,-0.80234f,-0.56906f),
        new Vector3(0.17864f,-0.80158f,-0.57057f), new Vector3(0.17440f,-0.79929f,-0.57508f), new Vector3(0.16734f,-0.79543f,-0.58248f), new Vector3(0.15750f,-0.78996f,-0.59258f),
        new Vector3(0.14494f,-0.78281f,-0.60514f), new Vector3(0.12974f,-0.77392f,-0.61985f), new Vector3(0.11199f,-0.76322f,-0.63636f), new Vector3(0.09184f,-0.75067f,-0.65426f),
        new Vector3(0.06947f,-0.73624f,-0.67314f), new Vector3(0.04511f,-0.71995f,-0.69256f), new Vector3(0.01900f,-0.70185f,-0.71207f), new Vector3(-0.00854f,-0.68204f,-0.73127f),
        new Vector3(-0.03718f,-0.66067f,-0.74976f), new Vector3(-0.06655f,-0.63793f,-0.76721f), new Vector3(-0.09628f,-0.61409f,-0.78334f), new Vector3(-0.13349f,-0.56347f,-0.81528f),
        new Vector3(-0.17050f,-0.51097f,-0.84252f), new Vector3(-0.20668f,-0.45750f,-0.86486f), new Vector3(-0.24142f,-0.40404f,-0.88231f), new Vector3(-0.27417f,-0.35160f,-0.89510f),
        new Vector3(-0.30449f,-0.30115f,-0.90366f), new Vector3(-0.33202f,-0.25359f,-0.90855f), new Vector3(-0.35648f,-0.20977f,-0.91045f), new Vector3(-0.37773f,-0.17041f,-0.91010f),
        new Vector3(-0.39567f,-0.13614f,-0.90824f), new Vector3(-0.41028f,-0.10745f,-0.90561f), new Vector3(-0.42158f,-0.08476f,-0.90282f), new Vector3(-0.42960f,-0.06834f,-0.90043f),
        new Vector3(-0.43440f,-0.05842f,-0.89883f), new Vector3(-0.43599f,-0.05510f,-0.89826f), new Vector3(-0.43440f,-0.05842f,-0.89883f), new Vector3(-0.42960f,-0.06834f,-0.90043f),
        new Vector3(-0.42158f,-0.08476f,-0.90282f), new Vector3(-0.41028f,-0.10745f,-0.90561f), new Vector3(-0.39567f,-0.13614f,-0.90824f), new Vector3(-0.37773f,-0.17041f,-0.91010f),
        new Vector3(-0.35648f,-0.20977f,-0.91045f), new Vector3(-0.33202f,-0.25359f,-0.90855f), new Vector3(-0.30449f,-0.30115f,-0.90366f), new Vector3(-0.27417f,-0.35160f,-0.89510f),
        new Vector3(-0.24142f,-0.40404f,-0.88231f), new Vector3(-0.20668f,-0.45750f,-0.86486f), new Vector3(-0.17050f,-0.51097f,-0.84252f), new Vector3(-0.13349f,-0.56347f,-0.81528f),
    };

    static readonly Vector3[] LegLeftLegWalkXDir = {
        new Vector3(0.96911f,0.12170f,-0.21452f), new Vector3(0.97384f,0.12591f,-0.18918f), new Vector3(0.97767f,0.13125f,-0.16413f), new Vector3(0.98058f,0.13756f,-0.13976f),
        new Vector3(0.98261f,0.14468f,-0.11639f), new Vector3(0.98381f,0.15238f,-0.09434f), new Vector3(0.98428f,0.16041f,-0.07387f), new Vector3(0.98415f,0.16852f,-0.05520f),
        new Vector3(0.98356f,0.17642f,-0.03851f), new Vector3(0.98266f,0.18388f,-0.02391f), new Vector3(0.98159f,0.19063f,-0.01149f), new Vector3(0.98051f,0.19647f,-0.00130f),
        new Vector3(0.97953f,0.20121f,0.00663f), new Vector3(0.97875f,0.20470f,0.01230f), new Vector3(0.97825f,0.20684f,0.01569f), new Vector3(0.97808f,0.20756f,0.01683f),
        new Vector3(0.97825f,0.20684f,0.01569f), new Vector3(0.97875f,0.20470f,0.01230f), new Vector3(0.97953f,0.20121f,0.00663f), new Vector3(0.98051f,0.19647f,-0.00130f),
        new Vector3(0.98159f,0.19063f,-0.01149f), new Vector3(0.98266f,0.18388f,-0.02391f), new Vector3(0.98356f,0.17642f,-0.03851f), new Vector3(0.98415f,0.16852f,-0.05520f),
        new Vector3(0.98428f,0.16041f,-0.07387f), new Vector3(0.98381f,0.15238f,-0.09434f), new Vector3(0.98261f,0.14468f,-0.11639f), new Vector3(0.98058f,0.13756f,-0.13976f),
        new Vector3(0.97767f,0.13125f,-0.16413f), new Vector3(0.97384f,0.12591f,-0.18918f), new Vector3(0.96911f,0.12170f,-0.21452f), new Vector3(0.96354f,0.11868f,-0.23979f),
        new Vector3(0.95725f,0.11687f,-0.26460f), new Vector3(0.95036f,0.11625f,-0.28861f), new Vector3(0.94305f,0.11673f,-0.31150f), new Vector3(0.93551f,0.11816f,-0.33297f),
        new Vector3(0.92793f,0.12036f,-0.35279f), new Vector3(0.92053f,0.12314f,-0.37077f), new Vector3(0.91349f,0.12628f,-0.38677f), new Vector3(0.90700f,0.12955f,-0.40071f),
        new Vector3(0.90123f,0.13274f,-0.41252f), new Vector3(0.89631f,0.13565f,-0.42217f), new Vector3(0.89237f,0.13810f,-0.42967f), new Vector3(0.88948f,0.13995f,-0.43501f),
        new Vector3(0.88773f,0.14111f,-0.43821f), new Vector3(0.88714f,0.14150f,-0.43927f), new Vector3(0.88773f,0.14111f,-0.43821f), new Vector3(0.88948f,0.13995f,-0.43501f),
        new Vector3(0.89237f,0.13810f,-0.42967f), new Vector3(0.89631f,0.13565f,-0.42217f), new Vector3(0.90123f,0.13274f,-0.41252f), new Vector3(0.90700f,0.12955f,-0.40071f),
        new Vector3(0.91349f,0.12628f,-0.38677f), new Vector3(0.92053f,0.12314f,-0.37077f), new Vector3(0.92793f,0.12036f,-0.35279f), new Vector3(0.93551f,0.11816f,-0.33297f),
        new Vector3(0.94305f,0.11673f,-0.31150f), new Vector3(0.95036f,0.11625f,-0.28861f), new Vector3(0.95725f,0.11687f,-0.26460f), new Vector3(0.96354f,0.11868f,-0.23979f),
    };

    static readonly Vector3[] LegRightUpLegWalkYDir = {
        new Vector3(-0.46228f,-0.72975f,0.50375f), new Vector3(-0.46535f,-0.76680f,0.44211f), new Vector3(-0.46526f,-0.79990f,0.37908f), new Vector3(-0.46216f,-0.82867f,0.31576f),
        new Vector3(-0.45634f,-0.85299f,0.25330f), new Vector3(-0.44822f,-0.87289f,0.19279f), new Vector3(-0.43831f,-0.88859f,0.13527f), new Vector3(-0.42720f,-0.90046f,0.08167f),
        new Vector3(-0.41552f,-0.90899f,0.03280f), new Vector3(-0.40389f,-0.91475f,-0.01067f), new Vector3(-0.39290f,-0.91832f,-0.04819f), new Vector3(-0.38311f,-0.92029f,-0.07936f),
        new Vector3(-0.37498f,-0.92120f,-0.10387f), new Vector3(-0.36889f,-0.92150f,-0.12151f), new Vector3(-0.36512f,-0.92153f,-0.13214f), new Vector3(-0.36385f,-0.92152f,-0.13569f),
        new Vector3(-0.36512f,-0.92153f,-0.13214f), new Vector3(-0.36889f,-0.92150f,-0.12151f), new Vector3(-0.37498f,-0.92120f,-0.10387f), new Vector3(-0.38311f,-0.92029f,-0.07936f),
        new Vector3(-0.39290f,-0.91832f,-0.04819f), new Vector3(-0.40389f,-0.91475f,-0.01067f), new Vector3(-0.41552f,-0.90899f,0.03280f), new Vector3(-0.42720f,-0.90046f,0.08167f),
        new Vector3(-0.43831f,-0.88859f,0.13527f), new Vector3(-0.44822f,-0.87289f,0.19279f), new Vector3(-0.45634f,-0.85299f,0.25330f), new Vector3(-0.46216f,-0.82867f,0.31576f),
        new Vector3(-0.46526f,-0.79990f,0.37908f), new Vector3(-0.46535f,-0.76680f,0.44211f), new Vector3(-0.46228f,-0.72975f,0.50375f), new Vector3(-0.45607f,-0.68926f,0.56296f),
        new Vector3(-0.44688f,-0.64603f,0.61882f), new Vector3(-0.43503f,-0.60090f,0.67057f), new Vector3(-0.42097f,-0.55480f,0.71762f), new Vector3(-0.40524f,-0.50871f,0.75960f),
        new Vector3(-0.38844f,-0.46363f,0.79634f), new Vector3(-0.37123f,-0.42052f,0.82786f), new Vector3(-0.35425f,-0.38029f,0.85434f), new Vector3(-0.33814f,-0.34376f,0.87607f),
        new Vector3(-0.32346f,-0.31165f,0.89345f), new Vector3(-0.31073f,-0.28458f,0.90690f), new Vector3(-0.30038f,-0.26302f,0.91684f), new Vector3(-0.29274f,-0.24736f,0.92364f),
        new Vector3(-0.28806f,-0.23786f,0.92760f), new Vector3(-0.28648f,-0.23468f,0.92890f), new Vector3(-0.28806f,-0.23786f,0.92760f), new Vector3(-0.29274f,-0.24736f,0.92364f),
        new Vector3(-0.30038f,-0.26302f,0.91684f), new Vector3(-0.31073f,-0.28458f,0.90690f), new Vector3(-0.32346f,-0.31165f,0.89345f), new Vector3(-0.33814f,-0.34376f,0.87607f),
        new Vector3(-0.35425f,-0.38029f,0.85434f), new Vector3(-0.37123f,-0.42052f,0.82786f), new Vector3(-0.38844f,-0.46363f,0.79634f), new Vector3(-0.40524f,-0.50871f,0.75960f),
        new Vector3(-0.42097f,-0.55480f,0.71762f), new Vector3(-0.43503f,-0.60090f,0.67057f), new Vector3(-0.44688f,-0.64603f,0.61882f), new Vector3(-0.45607f,-0.68926f,0.56296f),
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
        new Vector3(0.09497f,-0.60636f,-0.78950f), new Vector3(0.13320f,-0.55529f,-0.82092f), new Vector3(0.17121f,-0.50234f,-0.84755f), new Vector3(0.20833f,-0.44844f,-0.86919f),
        new Vector3(0.24394f,-0.39460f,-0.88588f), new Vector3(0.27750f,-0.34180f,-0.89787f), new Vector3(0.30855f,-0.29103f,-0.90559f), new Vector3(0.33671f,-0.24321f,-0.90966f),
        new Vector3(0.36173f,-0.19918f,-0.91076f), new Vector3(0.38344f,-0.15965f,-0.90966f), new Vector3(0.40177f,-0.12524f,-0.90714f), new Vector3(0.41669f,-0.09646f,-0.90392f),
        new Vector3(0.42822f,-0.07370f,-0.90066f), new Vector3(0.43641f,-0.05725f,-0.89793f), new Vector3(0.44129f,-0.04730f,-0.89612f), new Vector3(0.44292f,-0.04397f,-0.89548f),
        new Vector3(0.44129f,-0.04730f,-0.89612f), new Vector3(0.43641f,-0.05725f,-0.89793f), new Vector3(0.42822f,-0.07370f,-0.90066f), new Vector3(0.41669f,-0.09646f,-0.90392f),
        new Vector3(0.40177f,-0.12524f,-0.90714f), new Vector3(0.38344f,-0.15965f,-0.90966f), new Vector3(0.36173f,-0.19918f,-0.91076f), new Vector3(0.33671f,-0.24321f,-0.90966f),
        new Vector3(0.30855f,-0.29103f,-0.90559f), new Vector3(0.27750f,-0.34180f,-0.89787f), new Vector3(0.24394f,-0.39460f,-0.88588f), new Vector3(0.20833f,-0.44844f,-0.86919f),
        new Vector3(0.17121f,-0.50234f,-0.84755f), new Vector3(0.13320f,-0.55529f,-0.82092f), new Vector3(0.09497f,-0.60636f,-0.78950f), new Vector3(0.06597f,-0.63074f,-0.77318f),
        new Vector3(0.03733f,-0.65401f,-0.75556f), new Vector3(0.00940f,-0.67590f,-0.73693f), new Vector3(-0.01745f,-0.69622f,-0.71762f), new Vector3(-0.04290f,-0.71480f,-0.69801f),
        new Vector3(-0.06665f,-0.73155f,-0.67853f), new Vector3(-0.08846f,-0.74640f,-0.65960f), new Vector3(-0.10810f,-0.75934f,-0.64165f), new Vector3(-0.12540f,-0.77039f,-0.62512f),
        new Vector3(-0.14023f,-0.77959f,-0.61039f), new Vector3(-0.15248f,-0.78699f,-0.59782f), new Vector3(-0.16207f,-0.79267f,-0.58772f), new Vector3(-0.16895f,-0.79667f,-0.58032f),
        new Vector3(-0.17310f,-0.79905f,-0.57580f), new Vector3(-0.17448f,-0.79984f,-0.57429f), new Vector3(-0.17310f,-0.79905f,-0.57580f), new Vector3(-0.16895f,-0.79667f,-0.58032f),
        new Vector3(-0.16207f,-0.79267f,-0.58772f), new Vector3(-0.15248f,-0.78699f,-0.59782f), new Vector3(-0.14023f,-0.77959f,-0.61039f), new Vector3(-0.12540f,-0.77039f,-0.62512f),
        new Vector3(-0.10810f,-0.75934f,-0.64165f), new Vector3(-0.08846f,-0.74640f,-0.65960f), new Vector3(-0.06665f,-0.73155f,-0.67853f), new Vector3(-0.04290f,-0.71480f,-0.69801f),
        new Vector3(-0.01745f,-0.69622f,-0.71762f), new Vector3(0.00940f,-0.67590f,-0.73693f), new Vector3(0.03733f,-0.65401f,-0.75556f), new Vector3(0.06597f,-0.63074f,-0.77318f),
    };

    static readonly Vector3[] LegRightLegWalkXDir = {
        new Vector3(0.95469f,-0.16923f,0.24481f), new Vector3(0.94881f,-0.16789f,0.26752f), new Vector3(0.94232f,-0.16763f,0.28971f), new Vector3(0.93535f,-0.16839f,0.31107f),
        new Vector3(0.92806f,-0.17008f,0.33132f), new Vector3(0.92064f,-0.17254f,0.35022f), new Vector3(0.91326f,-0.17562f,0.36759f), new Vector3(0.90610f,-0.17910f,0.38328f),
        new Vector3(0.89935f,-0.18280f,0.39717f), new Vector3(0.89317f,-0.18649f,0.40922f), new Vector3(0.88770f,-0.19000f,0.41939f), new Vector3(0.88306f,-0.19313f,0.42768f),
        new Vector3(0.87934f,-0.19573f,0.43410f), new Vector3(0.87664f,-0.19769f,0.43866f), new Vector3(0.87499f,-0.19890f,0.44139f), new Vector3(0.87444f,-0.19931f,0.44230f),
        new Vector3(0.87499f,-0.19890f,0.44139f), new Vector3(0.87664f,-0.19769f,0.43866f), new Vector3(0.87934f,-0.19573f,0.43410f), new Vector3(0.88306f,-0.19313f,0.42768f),
        new Vector3(0.88770f,-0.19000f,0.41939f), new Vector3(0.89317f,-0.18649f,0.40922f), new Vector3(0.89935f,-0.18280f,0.39717f), new Vector3(0.90610f,-0.17910f,0.38328f),
        new Vector3(0.91326f,-0.17562f,0.36759f), new Vector3(0.92064f,-0.17254f,0.35022f), new Vector3(0.92806f,-0.17008f,0.33132f), new Vector3(0.93535f,-0.16839f,0.31107f),
        new Vector3(0.94232f,-0.16763f,0.28971f), new Vector3(0.94881f,-0.16789f,0.26752f), new Vector3(0.95469f,-0.16923f,0.24481f), new Vector3(0.95984f,-0.17164f,0.22192f),
        new Vector3(0.96420f,-0.17508f,0.19918f), new Vector3(0.96773f,-0.17945f,0.17693f), new Vector3(0.97043f,-0.18460f,0.15550f), new Vector3(0.97236f,-0.19036f,0.13518f),
        new Vector3(0.97359f,-0.19651f,0.11623f), new Vector3(0.97421f,-0.20282f,0.09886f), new Vector3(0.97435f,-0.20907f,0.08327f), new Vector3(0.97413f,-0.21502f,0.06958f),
        new Vector3(0.97368f,-0.22047f,0.05789f), new Vector3(0.97312f,-0.22520f,0.04827f), new Vector3(0.97256f,-0.22907f,0.04076f), new Vector3(0.97209f,-0.23193f,0.03538f),
        new Vector3(0.97178f,-0.23368f,0.03215f), new Vector3(0.97167f,-0.23427f,0.03108f), new Vector3(0.97178f,-0.23368f,0.03215f), new Vector3(0.97209f,-0.23193f,0.03538f),
        new Vector3(0.97256f,-0.22907f,0.04076f), new Vector3(0.97312f,-0.22520f,0.04827f), new Vector3(0.97368f,-0.22047f,0.05789f), new Vector3(0.97413f,-0.21502f,0.06958f),
        new Vector3(0.97435f,-0.20907f,0.08327f), new Vector3(0.97421f,-0.20282f,0.09886f), new Vector3(0.97359f,-0.19651f,0.11623f), new Vector3(0.97236f,-0.19036f,0.13518f),
        new Vector3(0.97043f,-0.18460f,0.15550f), new Vector3(0.96773f,-0.17945f,0.17693f), new Vector3(0.96420f,-0.17508f,0.19918f), new Vector3(0.95984f,-0.17164f,0.22192f),
    };
    static readonly Vector3[] LegLeftFootWalkYDir = {
        new Vector3(0.09070f,-0.67463f,0.73256f), new Vector3(0.07546f,-0.65171f,0.75471f), new Vector3(0.05976f,-0.62861f,0.77542f), new Vector3(0.04386f,-0.60565f,0.79452f),
        new Vector3(0.02800f,-0.58315f,0.81188f), new Vector3(0.01246f,-0.56141f,0.82744f), new Vector3(-0.00251f,-0.54074f,0.84119f), new Vector3(-0.01665f,-0.52141f,0.85314f),
        new Vector3(-0.02972f,-0.50371f,0.86336f), new Vector3(-0.04152f,-0.48785f,0.87194f), new Vector3(-0.05183f,-0.47407f,0.87896f), new Vector3(-0.06050f,-0.46254f,0.88453f),
        new Vector3(-0.06739f,-0.45341f,0.88875f), new Vector3(-0.07239f,-0.44681f,0.89170f), new Vector3(-0.07542f,-0.44281f,0.89344f), new Vector3(-0.07644f,-0.44147f,0.89401f),
        new Vector3(-0.07542f,-0.44281f,0.89344f), new Vector3(-0.07239f,-0.44681f,0.89170f), new Vector3(-0.06739f,-0.45341f,0.88875f), new Vector3(-0.06050f,-0.46254f,0.88453f),
        new Vector3(-0.05183f,-0.47407f,0.87896f), new Vector3(-0.04152f,-0.48785f,0.87194f), new Vector3(-0.02972f,-0.50371f,0.86336f), new Vector3(-0.01665f,-0.52141f,0.85314f),
        new Vector3(-0.00251f,-0.54074f,0.84119f), new Vector3(0.01246f,-0.56141f,0.82744f), new Vector3(0.02800f,-0.58315f,0.81188f), new Vector3(0.04386f,-0.60565f,0.79452f),
        new Vector3(0.05976f,-0.62861f,0.77542f), new Vector3(0.07546f,-0.65171f,0.75471f), new Vector3(0.09070f,-0.67463f,0.73256f), new Vector3(0.10248f,-0.71948f,0.68691f),
        new Vector3(0.11142f,-0.76121f,0.63886f), new Vector3(0.11750f,-0.79930f,0.58934f), new Vector3(0.12081f,-0.83338f,0.53933f), new Vector3(0.12161f,-0.86327f,0.48987f),
        new Vector3(0.12023f,-0.88894f,0.44197f), new Vector3(0.11712f,-0.91050f,0.39658f), new Vector3(0.11276f,-0.92821f,0.35456f), new Vector3(0.10766f,-0.94240f,0.31670f),
        new Vector3(0.10233f,-0.95346f,0.28364f), new Vector3(0.09724f,-0.96180f,0.25591f), new Vector3(0.09281f,-0.96781f,0.23394f), new Vector3(0.08939f,-0.97184f,0.21803f),
        new Vector3(0.08723f,-0.97415f,0.20840f), new Vector3(0.08649f,-0.97490f,0.20518f), new Vector3(0.08723f,-0.97415f,0.20840f), new Vector3(0.08939f,-0.97184f,0.21803f),
        new Vector3(0.09281f,-0.96781f,0.23394f), new Vector3(0.09724f,-0.96180f,0.25591f), new Vector3(0.10233f,-0.95346f,0.28364f), new Vector3(0.10766f,-0.94240f,0.31670f),
        new Vector3(0.11276f,-0.92821f,0.35456f), new Vector3(0.11712f,-0.91050f,0.39658f), new Vector3(0.12023f,-0.88894f,0.44197f), new Vector3(0.12161f,-0.86327f,0.48987f),
        new Vector3(0.12081f,-0.83338f,0.53933f), new Vector3(0.11750f,-0.79930f,0.58934f), new Vector3(0.11142f,-0.76121f,0.63886f), new Vector3(0.10248f,-0.71948f,0.68691f),
    };

    static readonly Vector3[] LegLeftFootWalkXDir = {
        new Vector3(0.99338f,0.00920f,-0.11453f), new Vector3(0.99622f,0.01668f,-0.08520f), new Vector3(0.99809f,0.02534f,-0.05638f), new Vector3(0.99898f,0.03499f,-0.02847f),
        new Vector3(0.99897f,0.04541f,-0.00184f), new Vector3(0.99814f,0.05632f,0.02318f), new Vector3(0.99665f,0.06743f,0.04631f), new Vector3(0.99464f,0.07842f,0.06734f),
        new Vector3(0.99231f,0.08900f,0.08608f), new Vector3(0.98982f,0.09885f,0.10243f), new Vector3(0.98736f,0.10770f,0.11631f), new Vector3(0.98509f,0.11529f,0.12767f),
        new Vector3(0.98317f,0.12143f,0.13650f), new Vector3(0.98171f,0.12594f,0.14280f), new Vector3(0.98079f,0.12869f,0.14658f), new Vector3(0.98048f,0.12962f,0.14783f),
        new Vector3(0.98079f,0.12869f,0.14658f), new Vector3(0.98171f,0.12594f,0.14280f), new Vector3(0.98317f,0.12143f,0.13650f), new Vector3(0.98509f,0.11529f,0.12767f),
        new Vector3(0.98736f,0.10770f,0.11631f), new Vector3(0.98982f,0.09885f,0.10243f), new Vector3(0.99231f,0.08900f,0.08608f), new Vector3(0.99464f,0.07842f,0.06734f),
        new Vector3(0.99665f,0.06743f,0.04631f), new Vector3(0.99814f,0.05632f,0.02318f), new Vector3(0.99897f,0.04541f,-0.00184f), new Vector3(0.99898f,0.03499f,-0.02847f),
        new Vector3(0.99809f,0.02534f,-0.05638f), new Vector3(0.99622f,0.01668f,-0.08520f), new Vector3(0.99338f,0.00920f,-0.11453f), new Vector3(0.98904f,-0.00010f,-0.14767f),
        new Vector3(0.98352f,-0.00765f,-0.18065f), new Vector3(0.97696f,-0.01342f,-0.21298f), new Vector3(0.96957f,-0.01747f,-0.24418f), new Vector3(0.96159f,-0.01991f,-0.27378f),
        new Vector3(0.95326f,-0.02093f,-0.30142f), new Vector3(0.94488f,-0.02078f,-0.32675f), new Vector3(0.93672f,-0.01972f,-0.34951f), new Vector3(0.92905f,-0.01804f,-0.36950f),
        new Vector3(0.92212f,-0.01604f,-0.38657f), new Vector3(0.91614f,-0.01397f,-0.40062f), new Vector3(0.91129f,-0.01210f,-0.41158f), new Vector3(0.90773f,-0.01061f,-0.41942f),
        new Vector3(0.90555f,-0.00965f,-0.42414f), new Vector3(0.90481f,-0.00932f,-0.42571f), new Vector3(0.90555f,-0.00965f,-0.42414f), new Vector3(0.90773f,-0.01061f,-0.41942f),
        new Vector3(0.91129f,-0.01210f,-0.41158f), new Vector3(0.91614f,-0.01397f,-0.40062f), new Vector3(0.92212f,-0.01604f,-0.38657f), new Vector3(0.92905f,-0.01804f,-0.36950f),
        new Vector3(0.93672f,-0.01972f,-0.34951f), new Vector3(0.94488f,-0.02078f,-0.32675f), new Vector3(0.95326f,-0.02093f,-0.30142f), new Vector3(0.96159f,-0.01991f,-0.27378f),
        new Vector3(0.96957f,-0.01747f,-0.24418f), new Vector3(0.97696f,-0.01342f,-0.21298f), new Vector3(0.98352f,-0.00765f,-0.18065f), new Vector3(0.98904f,-0.00010f,-0.14767f),
    };

    static readonly Vector3[] LegRightFootWalkYDir = {
        new Vector3(-0.10583f,-0.67550f,0.72972f), new Vector3(-0.11788f,-0.72052f,0.68334f), new Vector3(-0.12703f,-0.76239f,0.63453f), new Vector3(-0.13324f,-0.80057f,0.58424f),
        new Vector3(-0.13662f,-0.83470f,0.53349f), new Vector3(-0.13743f,-0.86459f,0.48331f), new Vector3(-0.13602f,-0.89023f,0.43473f), new Vector3(-0.13282f,-0.91173f,0.38871f),
        new Vector3(-0.12835f,-0.92936f,0.34614f), new Vector3(-0.12313f,-0.94346f,0.30778f), new Vector3(-0.11767f,-0.95442f,0.27430f), new Vector3(-0.11245f,-0.96267f,0.24623f),
        new Vector3(-0.10792f,-0.96860f,0.22399f), new Vector3(-0.10442f,-0.97256f,0.20790f), new Vector3(-0.10221f,-0.97483f,0.19816f), new Vector3(-0.10145f,-0.97556f,0.19490f),
        new Vector3(-0.10221f,-0.97483f,0.19816f), new Vector3(-0.10442f,-0.97256f,0.20790f), new Vector3(-0.10792f,-0.96860f,0.22399f), new Vector3(-0.11245f,-0.96267f,0.24623f),
        new Vector3(-0.11767f,-0.95442f,0.27430f), new Vector3(-0.12313f,-0.94346f,0.30778f), new Vector3(-0.12835f,-0.92936f,0.34614f), new Vector3(-0.13282f,-0.91173f,0.38871f),
        new Vector3(-0.13602f,-0.89023f,0.43473f), new Vector3(-0.13743f,-0.86459f,0.48331f), new Vector3(-0.13662f,-0.83470f,0.53349f), new Vector3(-0.13324f,-0.80057f,0.58424f),
        new Vector3(-0.12703f,-0.76239f,0.63453f), new Vector3(-0.11788f,-0.72052f,0.68334f), new Vector3(-0.10583f,-0.67550f,0.72972f), new Vector3(-0.09068f,-0.65215f,0.75264f),
        new Vector3(-0.07500f,-0.62860f,0.77410f), new Vector3(-0.05905f,-0.60517f,0.79390f), new Vector3(-0.04308f,-0.58218f,0.81192f), new Vector3(-0.02736f,-0.55996f,0.82807f),
        new Vector3(-0.01218f,-0.53881f,0.84234f), new Vector3(0.00220f,-0.51904f,0.85475f), new Vector3(0.01554f,-0.50092f,0.86536f), new Vector3(0.02759f,-0.48469f,0.87425f),
        new Vector3(0.03815f,-0.47058f,0.88153f), new Vector3(0.04704f,-0.45878f,0.88731f), new Vector3(0.05412f,-0.44943f,0.89167f), new Vector3(0.05925f,-0.44267f,0.89472f),
        new Vector3(0.06237f,-0.43858f,0.89653f), new Vector3(0.06341f,-0.43721f,0.89712f), new Vector3(0.06237f,-0.43858f,0.89653f), new Vector3(0.05925f,-0.44267f,0.89472f),
        new Vector3(0.05412f,-0.44943f,0.89167f), new Vector3(0.04704f,-0.45878f,0.88731f), new Vector3(0.03815f,-0.47058f,0.88153f), new Vector3(0.02759f,-0.48469f,0.87425f),
        new Vector3(0.01554f,-0.50092f,0.86536f), new Vector3(0.00220f,-0.51904f,0.85475f), new Vector3(-0.01218f,-0.53881f,0.84234f), new Vector3(-0.02736f,-0.55996f,0.82807f),
        new Vector3(-0.04308f,-0.58218f,0.81192f), new Vector3(-0.05905f,-0.60517f,0.79390f), new Vector3(-0.07500f,-0.62860f,0.77410f), new Vector3(-0.09068f,-0.65215f,0.75264f),
    };

    static readonly Vector3[] LegRightFootWalkXDir = {
        new Vector3(0.99089f,-0.01014f,0.13432f), new Vector3(0.98575f,-0.00176f,0.16819f), new Vector3(0.97941f,0.00479f,0.20183f), new Vector3(0.97202f,0.00952f,0.23472f),
        new Vector3(0.96379f,0.01249f,0.26637f), new Vector3(0.95499f,0.01385f,0.29633f), new Vector3(0.94588f,0.01381f,0.32422f), new Vector3(0.93677f,0.01263f,0.34973f),
        new Vector3(0.92794f,0.01061f,0.37259f), new Vector3(0.91967f,0.00806f,0.39261f), new Vector3(0.91222f,0.00528f,0.40967f), new Vector3(0.90580f,0.00256f,0.42369f),
        new Vector3(0.90062f,0.00016f,0.43461f), new Vector3(0.89681f,-0.00171f,0.44242f), new Vector3(0.89448f,-0.00290f,0.44710f), new Vector3(0.89370f,-0.00331f,0.44866f),
        new Vector3(0.89448f,-0.00290f,0.44710f), new Vector3(0.89681f,-0.00171f,0.44242f), new Vector3(0.90062f,0.00016f,0.43461f), new Vector3(0.90580f,0.00256f,0.42369f),
        new Vector3(0.91222f,0.00528f,0.40967f), new Vector3(0.91967f,0.00806f,0.39261f), new Vector3(0.92794f,0.01061f,0.37259f), new Vector3(0.93677f,0.01263f,0.34973f),
        new Vector3(0.94588f,0.01381f,0.32422f), new Vector3(0.95499f,0.01385f,0.29633f), new Vector3(0.96379f,0.01249f,0.26637f), new Vector3(0.97202f,0.00952f,0.23472f),
        new Vector3(0.97941f,0.00479f,0.20183f), new Vector3(0.98575f,-0.00176f,0.16819f), new Vector3(0.99089f,-0.01014f,0.13432f), new Vector3(0.99427f,-0.01632f,0.10564f),
        new Vector3(0.99672f,-0.02365f,0.07737f), new Vector3(0.99824f,-0.03195f,0.04989f), new Vector3(0.99888f,-0.04102f,0.02358f), new Vector3(0.99872f,-0.05063f,-0.00124f),
        new Vector3(0.99787f,-0.06049f,-0.02427f), new Vector3(0.99650f,-0.07033f,-0.04528f), new Vector3(0.99475f,-0.07984f,-0.06408f), new Vector3(0.99279f,-0.08874f,-0.08053f),
        new Vector3(0.99081f,-0.09677f,-0.09454f), new Vector3(0.98894f,-0.10369f,-0.10605f), new Vector3(0.98733f,-0.10929f,-0.11501f), new Vector3(0.98610f,-0.11342f,-0.12142f),
        new Vector3(0.98533f,-0.11594f,-0.12526f), new Vector3(0.98506f,-0.11679f,-0.12655f), new Vector3(0.98533f,-0.11594f,-0.12526f), new Vector3(0.98610f,-0.11342f,-0.12142f),
        new Vector3(0.98733f,-0.10929f,-0.11501f), new Vector3(0.98894f,-0.10369f,-0.10605f), new Vector3(0.99081f,-0.09677f,-0.09454f), new Vector3(0.99279f,-0.08874f,-0.08053f),
        new Vector3(0.99475f,-0.07984f,-0.06408f), new Vector3(0.99650f,-0.07033f,-0.04528f), new Vector3(0.99787f,-0.06049f,-0.02427f), new Vector3(0.99872f,-0.05063f,-0.00124f),
        new Vector3(0.99888f,-0.04102f,0.02358f), new Vector3(0.99824f,-0.03195f,0.04989f), new Vector3(0.99672f,-0.02365f,0.07737f), new Vector3(0.99427f,-0.01632f,0.10564f),
    };
}
