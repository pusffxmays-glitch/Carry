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
        new Vector3(0.44799f,-0.73760f,0.50522f), new Vector3(0.44469f,-0.72149f,0.53076f), new Vector3(0.44071f,-0.70504f,0.55560f), new Vector3(0.43612f,-0.68849f,0.57947f),
        new Vector3(0.43105f,-0.67206f,0.60211f), new Vector3(0.42563f,-0.65599f,0.62331f), new Vector3(0.42002f,-0.64053f,0.64289f), new Vector3(0.41438f,-0.62593f,0.66068f),
        new Vector3(0.40889f,-0.61241f,0.67658f), new Vector3(0.40371f,-0.60021f,0.69048f), new Vector3(0.39902f,-0.58952f,0.70232f), new Vector3(0.39496f,-0.58051f,0.71205f),
        new Vector3(0.39166f,-0.57335f,0.71964f), new Vector3(0.38922f,-0.56814f,0.72507f), new Vector3(0.38773f,-0.56498f,0.72833f), new Vector3(0.38722f,-0.56392f,0.72942f),
        new Vector3(0.38773f,-0.56498f,0.72833f), new Vector3(0.38922f,-0.56814f,0.72507f), new Vector3(0.39166f,-0.57335f,0.71964f), new Vector3(0.39496f,-0.58051f,0.71205f),
        new Vector3(0.39902f,-0.58952f,0.70232f), new Vector3(0.40371f,-0.60021f,0.69048f), new Vector3(0.40889f,-0.61241f,0.67658f), new Vector3(0.41438f,-0.62593f,0.66068f),
        new Vector3(0.42002f,-0.64053f,0.64289f), new Vector3(0.42563f,-0.65599f,0.62331f), new Vector3(0.43105f,-0.67206f,0.60211f), new Vector3(0.43612f,-0.68849f,0.57947f),
        new Vector3(0.44071f,-0.70504f,0.55560f), new Vector3(0.44469f,-0.72149f,0.53076f), new Vector3(0.44799f,-0.73760f,0.50522f), new Vector3(0.45055f,-0.75318f,0.47929f),
        new Vector3(0.45235f,-0.76806f,0.45328f), new Vector3(0.45340f,-0.78208f,0.42752f), new Vector3(0.45375f,-0.79513f,0.40234f), new Vector3(0.45347f,-0.80711f,0.37808f),
        new Vector3(0.45265f,-0.81795f,0.35506f), new Vector3(0.45142f,-0.82761f,0.33357f), new Vector3(0.44991f,-0.83609f,0.31391f), new Vector3(0.44824f,-0.84337f,0.29633f),
        new Vector3(0.44656f,-0.84946f,0.28106f), new Vector3(0.44499f,-0.85440f,0.26830f), new Vector3(0.44364f,-0.85821f,0.25820f), new Vector3(0.44260f,-0.86090f,0.25090f),
        new Vector3(0.44195f,-0.86251f,0.24648f), new Vector3(0.44173f,-0.86305f,0.24500f), new Vector3(0.44195f,-0.86251f,0.24648f), new Vector3(0.44260f,-0.86090f,0.25090f),
        new Vector3(0.44364f,-0.85821f,0.25820f), new Vector3(0.44499f,-0.85440f,0.26830f), new Vector3(0.44656f,-0.84946f,0.28106f), new Vector3(0.44824f,-0.84337f,0.29633f),
        new Vector3(0.44991f,-0.83609f,0.31391f), new Vector3(0.45142f,-0.82761f,0.33357f), new Vector3(0.45265f,-0.81795f,0.35506f), new Vector3(0.45347f,-0.80711f,0.37808f),
        new Vector3(0.45375f,-0.79513f,0.40234f), new Vector3(0.45340f,-0.78208f,0.42752f), new Vector3(0.45235f,-0.76806f,0.45328f), new Vector3(0.45055f,-0.75318f,0.47929f),
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
        new Vector3(-0.05581f,-0.73903f,-0.67136f), new Vector3(-0.04270f,-0.74236f,-0.66864f), new Vector3(-0.02969f,-0.74557f,-0.66577f), new Vector3(-0.01692f,-0.74863f,-0.66277f),
        new Vector3(-0.00455f,-0.75152f,-0.65970f), new Vector3(0.00729f,-0.75420f,-0.65661f), new Vector3(0.01846f,-0.75666f,-0.65355f), new Vector3(0.02882f,-0.75888f,-0.65059f),
        new Vector3(0.03826f,-0.76086f,-0.64779f), new Vector3(0.04667f,-0.76258f,-0.64521f), new Vector3(0.05395f,-0.76404f,-0.64291f), new Vector3(0.06003f,-0.76523f,-0.64095f),
        new Vector3(0.06482f,-0.76616f,-0.63937f), new Vector3(0.06829f,-0.76682f,-0.63821f), new Vector3(0.07038f,-0.76722f,-0.63751f), new Vector3(0.07108f,-0.76736f,-0.63727f),
        new Vector3(0.07038f,-0.76722f,-0.63751f), new Vector3(0.06829f,-0.76682f,-0.63821f), new Vector3(0.06482f,-0.76616f,-0.63937f), new Vector3(0.06003f,-0.76523f,-0.64095f),
        new Vector3(0.05395f,-0.76404f,-0.64291f), new Vector3(0.04667f,-0.76258f,-0.64521f), new Vector3(0.03826f,-0.76086f,-0.64779f), new Vector3(0.02882f,-0.75888f,-0.65059f),
        new Vector3(0.01846f,-0.75666f,-0.65355f), new Vector3(0.00729f,-0.75420f,-0.65661f), new Vector3(-0.00455f,-0.75152f,-0.65970f), new Vector3(-0.01692f,-0.74863f,-0.66277f),
        new Vector3(-0.02969f,-0.74557f,-0.66577f), new Vector3(-0.04270f,-0.74236f,-0.66864f), new Vector3(-0.05581f,-0.73903f,-0.67136f), new Vector3(-0.07383f,-0.72209f,-0.68785f),
        new Vector3(-0.09188f,-0.70480f,-0.70343f), new Vector3(-0.10973f,-0.68740f,-0.71794f), new Vector3(-0.12714f,-0.67012f,-0.73128f), new Vector3(-0.14388f,-0.65322f,-0.74337f),
        new Vector3(-0.15973f,-0.63695f,-0.75417f), new Vector3(-0.17449f,-0.62158f,-0.76367f), new Vector3(-0.18797f,-0.60734f,-0.77188f), new Vector3(-0.19999f,-0.59448f,-0.77885f),
        new Vector3(-0.21042f,-0.58320f,-0.78461f), new Vector3(-0.21911f,-0.57369f,-0.78922f), new Vector3(-0.22598f,-0.56613f,-0.79274f), new Vector3(-0.23094f,-0.56063f,-0.79521f),
        new Vector3(-0.23394f,-0.55730f,-0.79668f), new Vector3(-0.23495f,-0.55618f,-0.79716f), new Vector3(-0.23394f,-0.55730f,-0.79668f), new Vector3(-0.23094f,-0.56063f,-0.79521f),
        new Vector3(-0.22598f,-0.56613f,-0.79274f), new Vector3(-0.21911f,-0.57369f,-0.78922f), new Vector3(-0.21042f,-0.58320f,-0.78461f), new Vector3(-0.19999f,-0.59448f,-0.77885f),
        new Vector3(-0.18797f,-0.60734f,-0.77188f), new Vector3(-0.17449f,-0.62158f,-0.76367f), new Vector3(-0.15973f,-0.63695f,-0.75417f), new Vector3(-0.14388f,-0.65322f,-0.74337f),
        new Vector3(-0.12714f,-0.67012f,-0.73128f), new Vector3(-0.10973f,-0.68740f,-0.71794f), new Vector3(-0.09188f,-0.70480f,-0.70343f), new Vector3(-0.07383f,-0.72209f,-0.68785f),
    };

    static readonly Vector3[] LegLeftLegWalkXDir = {
        new Vector3(0.96911f,0.12170f,-0.21452f), new Vector3(0.97179f,0.12451f,-0.20030f), new Vector3(0.97421f,0.12748f,-0.18621f), new Vector3(0.97633f,0.13056f,-0.17241f),
        new Vector3(0.97817f,0.13371f,-0.15907f), new Vector3(0.97972f,0.13686f,-0.14633f), new Vector3(0.98100f,0.13997f,-0.13435f), new Vector3(0.98202f,0.14296f,-0.12326f),
        new Vector3(0.98282f,0.14578f,-0.11318f), new Vector3(0.98343f,0.14837f,-0.10422f), new Vector3(0.98387f,0.15066f,-0.09648f), new Vector3(0.98418f,0.15261f,-0.09003f),
        new Vector3(0.98438f,0.15418f,-0.08495f), new Vector3(0.98451f,0.15532f,-0.08129f), new Vector3(0.98458f,0.15602f,-0.07907f), new Vector3(0.98461f,0.15626f,-0.07833f),
        new Vector3(0.98458f,0.15602f,-0.07907f), new Vector3(0.98451f,0.15532f,-0.08129f), new Vector3(0.98438f,0.15418f,-0.08495f), new Vector3(0.98418f,0.15261f,-0.09003f),
        new Vector3(0.98387f,0.15066f,-0.09648f), new Vector3(0.98343f,0.14837f,-0.10422f), new Vector3(0.98282f,0.14578f,-0.11318f), new Vector3(0.98202f,0.14296f,-0.12326f),
        new Vector3(0.98100f,0.13997f,-0.13435f), new Vector3(0.97972f,0.13686f,-0.14633f), new Vector3(0.97817f,0.13371f,-0.15907f), new Vector3(0.97633f,0.13056f,-0.17241f),
        new Vector3(0.97421f,0.12748f,-0.18621f), new Vector3(0.97179f,0.12451f,-0.20030f), new Vector3(0.96911f,0.12170f,-0.21452f), new Vector3(0.96618f,0.11908f,-0.22871f),
        new Vector3(0.96306f,0.11668f,-0.24270f), new Vector3(0.95978f,0.11452f,-0.25634f), new Vector3(0.95640f,0.11261f,-0.26947f), new Vector3(0.95299f,0.11095f,-0.28194f),
        new Vector3(0.94962f,0.10952f,-0.29363f), new Vector3(0.94636f,0.10833f,-0.30442f), new Vector3(0.94327f,0.10735f,-0.31418f), new Vector3(0.94044f,0.10656f,-0.32283f),
        new Vector3(0.93792f,0.10595f,-0.33029f), new Vector3(0.93577f,0.10548f,-0.33648f), new Vector3(0.93404f,0.10514f,-0.34135f), new Vector3(0.93277f,0.10491f,-0.34486f),
        new Vector3(0.93200f,0.10477f,-0.34698f), new Vector3(0.93174f,0.10473f,-0.34769f), new Vector3(0.93200f,0.10477f,-0.34698f), new Vector3(0.93277f,0.10491f,-0.34486f),
        new Vector3(0.93404f,0.10514f,-0.34135f), new Vector3(0.93577f,0.10548f,-0.33648f), new Vector3(0.93792f,0.10595f,-0.33029f), new Vector3(0.94044f,0.10656f,-0.32283f),
        new Vector3(0.94327f,0.10735f,-0.31418f), new Vector3(0.94636f,0.10833f,-0.30442f), new Vector3(0.94962f,0.10952f,-0.29363f), new Vector3(0.95299f,0.11095f,-0.28194f),
        new Vector3(0.95640f,0.11261f,-0.26947f), new Vector3(0.95978f,0.11452f,-0.25634f), new Vector3(0.96306f,0.11668f,-0.24270f), new Vector3(0.96618f,0.11908f,-0.22871f),
    };

    static readonly Vector3[] LegRightUpLegWalkYDir = {
        new Vector3(-0.46228f,-0.72975f,0.50375f), new Vector3(-0.46495f,-0.74567f,0.47729f), new Vector3(-0.46683f,-0.76085f,0.45074f), new Vector3(-0.46796f,-0.77516f,0.42444f),
        new Vector3(-0.46836f,-0.78845f,0.39872f), new Vector3(-0.46812f,-0.80065f,0.37394f), new Vector3(-0.46734f,-0.81167f,0.35041f), new Vector3(-0.46613f,-0.82149f,0.32845f),
        new Vector3(-0.46462f,-0.83009f,0.30835f), new Vector3(-0.46295f,-0.83747f,0.29038f), new Vector3(-0.46126f,-0.84365f,0.27476f), new Vector3(-0.45968f,-0.84865f,0.26171f),
        new Vector3(-0.45832f,-0.85250f,0.25139f), new Vector3(-0.45728f,-0.85522f,0.24392f), new Vector3(-0.45662f,-0.85685f,0.23940f), new Vector3(-0.45640f,-0.85739f,0.23789f),
        new Vector3(-0.45662f,-0.85685f,0.23940f), new Vector3(-0.45728f,-0.85522f,0.24392f), new Vector3(-0.45832f,-0.85250f,0.25139f), new Vector3(-0.45968f,-0.84865f,0.26171f),
        new Vector3(-0.46126f,-0.84365f,0.27476f), new Vector3(-0.46295f,-0.83747f,0.29038f), new Vector3(-0.46462f,-0.83009f,0.30835f), new Vector3(-0.46613f,-0.82149f,0.32845f),
        new Vector3(-0.46734f,-0.81167f,0.35041f), new Vector3(-0.46812f,-0.80065f,0.37394f), new Vector3(-0.46836f,-0.78845f,0.39872f), new Vector3(-0.46796f,-0.77516f,0.42444f),
        new Vector3(-0.46683f,-0.76085f,0.45074f), new Vector3(-0.46495f,-0.74567f,0.47729f), new Vector3(-0.46228f,-0.72975f,0.50375f), new Vector3(-0.45886f,-0.71327f,0.52980f),
        new Vector3(-0.45474f,-0.69645f,0.55512f), new Vector3(-0.45001f,-0.67951f,0.57944f), new Vector3(-0.44479f,-0.66269f,0.60250f), new Vector3(-0.43921f,-0.64623f,0.62409f),
        new Vector3(-0.43344f,-0.63039f,0.64400f), new Vector3(-0.42765f,-0.61542f,0.66210f), new Vector3(-0.42201f,-0.60157f,0.67825f), new Vector3(-0.41670f,-0.58906f,0.69237f),
        new Vector3(-0.41188f,-0.57809f,0.70439f), new Vector3(-0.40771f,-0.56885f,0.71426f), new Vector3(-0.40433f,-0.56150f,0.72196f), new Vector3(-0.40183f,-0.55616f,0.72747f),
        new Vector3(-0.40030f,-0.55292f,0.73078f), new Vector3(-0.39978f,-0.55184f,0.73188f), new Vector3(-0.40030f,-0.55292f,0.73078f), new Vector3(-0.40183f,-0.55616f,0.72747f),
        new Vector3(-0.40433f,-0.56150f,0.72196f), new Vector3(-0.40771f,-0.56885f,0.71426f), new Vector3(-0.41188f,-0.57809f,0.70439f), new Vector3(-0.41670f,-0.58906f,0.69237f),
        new Vector3(-0.42201f,-0.60157f,0.67825f), new Vector3(-0.42765f,-0.61542f,0.66210f), new Vector3(-0.43344f,-0.63039f,0.64400f), new Vector3(-0.43921f,-0.64623f,0.62409f),
        new Vector3(-0.44479f,-0.66269f,0.60250f), new Vector3(-0.45001f,-0.67951f,0.57944f), new Vector3(-0.45474f,-0.69645f,0.55512f), new Vector3(-0.45886f,-0.71327f,0.52980f),
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
        new Vector3(0.04506f,-0.73093f,-0.68097f), new Vector3(0.06355f,-0.71356f,-0.69771f), new Vector3(0.08206f,-0.69584f,-0.71349f), new Vector3(0.10036f,-0.67800f,-0.72818f),
        new Vector3(0.11821f,-0.66028f,-0.74166f), new Vector3(0.13536f,-0.64295f,-0.75385f), new Vector3(0.15160f,-0.62628f,-0.76472f), new Vector3(0.16671f,-0.61052f,-0.77426f),
        new Vector3(0.18051f,-0.59592f,-0.78249f), new Vector3(0.19282f,-0.58274f,-0.78946f), new Vector3(0.20348f,-0.57118f,-0.79521f), new Vector3(0.21238f,-0.56144f,-0.79980f),
        new Vector3(0.21941f,-0.55369f,-0.80330f), new Vector3(0.22448f,-0.54806f,-0.80576f), new Vector3(0.22755f,-0.54464f,-0.80721f), new Vector3(0.22857f,-0.54349f,-0.80769f),
        new Vector3(0.22755f,-0.54464f,-0.80721f), new Vector3(0.22448f,-0.54806f,-0.80576f), new Vector3(0.21941f,-0.55369f,-0.80330f), new Vector3(0.21238f,-0.56144f,-0.79980f),
        new Vector3(0.20348f,-0.57118f,-0.79521f), new Vector3(0.19282f,-0.58274f,-0.78946f), new Vector3(0.18051f,-0.59592f,-0.78249f), new Vector3(0.16671f,-0.61052f,-0.77426f),
        new Vector3(0.15160f,-0.62628f,-0.76472f), new Vector3(0.13536f,-0.64295f,-0.75385f), new Vector3(0.11821f,-0.66028f,-0.74166f), new Vector3(0.10036f,-0.67800f,-0.72818f),
        new Vector3(0.08206f,-0.69584f,-0.71349f), new Vector3(0.06355f,-0.71356f,-0.69771f), new Vector3(0.04506f,-0.73093f,-0.68097f), new Vector3(0.03257f,-0.73470f,-0.67761f),
        new Vector3(0.02019f,-0.73835f,-0.67411f), new Vector3(0.00804f,-0.74184f,-0.67053f), new Vector3(-0.00373f,-0.74514f,-0.66690f), new Vector3(-0.01498f,-0.74821f,-0.66329f),
        new Vector3(-0.02559f,-0.75104f,-0.65976f), new Vector3(-0.03543f,-0.75360f,-0.65638f), new Vector3(-0.04440f,-0.75589f,-0.65319f), new Vector3(-0.05238f,-0.75788f,-0.65029f),
        new Vector3(-0.05929f,-0.75958f,-0.64771f), new Vector3(-0.06506f,-0.76097f,-0.64551f), new Vector3(-0.06961f,-0.76206f,-0.64376f), new Vector3(-0.07289f,-0.76284f,-0.64247f),
        new Vector3(-0.07488f,-0.76330f,-0.64169f), new Vector3(-0.07555f,-0.76346f,-0.64142f), new Vector3(-0.07488f,-0.76330f,-0.64169f), new Vector3(-0.07289f,-0.76284f,-0.64247f),
        new Vector3(-0.06961f,-0.76206f,-0.64376f), new Vector3(-0.06506f,-0.76097f,-0.64551f), new Vector3(-0.05929f,-0.75958f,-0.64771f), new Vector3(-0.05238f,-0.75788f,-0.65029f),
        new Vector3(-0.04440f,-0.75589f,-0.65319f), new Vector3(-0.03543f,-0.75360f,-0.65638f), new Vector3(-0.02559f,-0.75104f,-0.65976f), new Vector3(-0.01498f,-0.74821f,-0.66329f),
        new Vector3(-0.00373f,-0.74514f,-0.66690f), new Vector3(0.00804f,-0.74184f,-0.67053f), new Vector3(0.02019f,-0.73835f,-0.67411f), new Vector3(0.03257f,-0.73470f,-0.67761f),
    };

    static readonly Vector3[] LegRightLegWalkXDir = {
        new Vector3(0.95469f,-0.16923f,0.24481f), new Vector3(0.95156f,-0.16740f,0.25788f), new Vector3(0.94827f,-0.16578f,0.27074f), new Vector3(0.94485f,-0.16436f,0.28326f),
        new Vector3(0.94137f,-0.16316f,0.29530f), new Vector3(0.93788f,-0.16217f,0.30672f), new Vector3(0.93446f,-0.16136f,0.31740f), new Vector3(0.93117f,-0.16074f,0.32724f),
        new Vector3(0.92807f,-0.16026f,0.33614f), new Vector3(0.92524f,-0.15992f,0.34402f), new Vector3(0.92273f,-0.15968f,0.35081f), new Vector3(0.92060f,-0.15952f,0.35644f),
        new Vector3(0.91889f,-0.15943f,0.36086f), new Vector3(0.91764f,-0.15937f,0.36405f), new Vector3(0.91688f,-0.15935f,0.36597f), new Vector3(0.91663f,-0.15934f,0.36662f),
        new Vector3(0.91688f,-0.15935f,0.36597f), new Vector3(0.91764f,-0.15937f,0.36405f), new Vector3(0.91889f,-0.15943f,0.36086f), new Vector3(0.92060f,-0.15952f,0.35644f),
        new Vector3(0.92273f,-0.15968f,0.35081f), new Vector3(0.92524f,-0.15992f,0.34402f), new Vector3(0.92807f,-0.16026f,0.33614f), new Vector3(0.93117f,-0.16074f,0.32724f),
        new Vector3(0.93446f,-0.16136f,0.31740f), new Vector3(0.93788f,-0.16217f,0.30672f), new Vector3(0.94137f,-0.16316f,0.29530f), new Vector3(0.94485f,-0.16436f,0.28326f),
        new Vector3(0.94827f,-0.16578f,0.27074f), new Vector3(0.95156f,-0.16740f,0.25788f), new Vector3(0.95469f,-0.16923f,0.24481f), new Vector3(0.95760f,-0.17123f,0.23169f),
        new Vector3(0.96027f,-0.17340f,0.21868f), new Vector3(0.96267f,-0.17569f,0.20591f), new Vector3(0.96480f,-0.17806f,0.19355f), new Vector3(0.96665f,-0.18047f,0.18174f),
        new Vector3(0.96822f,-0.18287f,0.17061f), new Vector3(0.96954f,-0.18520f,0.16029f), new Vector3(0.97062f,-0.18741f,0.15090f), new Vector3(0.97149f,-0.18946f,0.14255f),
        new Vector3(0.97216f,-0.19128f,0.13532f), new Vector3(0.97267f,-0.19284f,0.12930f), new Vector3(0.97304f,-0.19410f,0.12455f), new Vector3(0.97329f,-0.19502f,0.12113f),
        new Vector3(0.97343f,-0.19558f,0.11905f), new Vector3(0.97348f,-0.19577f,0.11836f), new Vector3(0.97343f,-0.19558f,0.11905f), new Vector3(0.97329f,-0.19502f,0.12113f),
        new Vector3(0.97304f,-0.19410f,0.12455f), new Vector3(0.97267f,-0.19284f,0.12930f), new Vector3(0.97216f,-0.19128f,0.13532f), new Vector3(0.97149f,-0.18946f,0.14255f),
        new Vector3(0.97062f,-0.18741f,0.15090f), new Vector3(0.96954f,-0.18520f,0.16029f), new Vector3(0.96822f,-0.18287f,0.17061f), new Vector3(0.96665f,-0.18047f,0.18174f),
        new Vector3(0.96480f,-0.17806f,0.19355f), new Vector3(0.96267f,-0.17569f,0.20591f), new Vector3(0.96027f,-0.17340f,0.21868f), new Vector3(0.95760f,-0.17123f,0.23169f),
    };
    static readonly Vector3[] LegLeftFootWalkYDir = {
        new Vector3(0.09672f,-0.53944f,0.83645f), new Vector3(0.08603f,-0.53516f,0.84036f), new Vector3(0.07544f,-0.53096f,0.84403f), new Vector3(0.06509f,-0.52690f,0.84743f),
        new Vector3(0.05508f,-0.52302f,0.85054f), new Vector3(0.04553f,-0.51934f,0.85335f), new Vector3(0.03655f,-0.51592f,0.85586f), new Vector3(0.02823f,-0.51277f,0.85806f),
        new Vector3(0.02068f,-0.50993f,0.85996f), new Vector3(0.01396f,-0.50743f,0.86158f), new Vector3(0.00816f,-0.50527f,0.86292f), new Vector3(0.00333f,-0.50348f,0.86400f),
        new Vector3(-0.00048f,-0.50208f,0.86482f), new Vector3(-0.00323f,-0.50107f,0.86540f), new Vector3(-0.00489f,-0.50046f,0.86575f), new Vector3(-0.00544f,-0.50026f,0.86586f),
        new Vector3(-0.00489f,-0.50046f,0.86575f), new Vector3(-0.00323f,-0.50107f,0.86540f), new Vector3(-0.00048f,-0.50208f,0.86482f), new Vector3(0.00333f,-0.50348f,0.86400f),
        new Vector3(0.00816f,-0.50527f,0.86292f), new Vector3(0.01396f,-0.50743f,0.86158f), new Vector3(0.02068f,-0.50993f,0.85996f), new Vector3(0.02823f,-0.51277f,0.85806f),
        new Vector3(0.03655f,-0.51592f,0.85586f), new Vector3(0.04553f,-0.51934f,0.85335f), new Vector3(0.05508f,-0.52302f,0.85054f), new Vector3(0.06509f,-0.52690f,0.84743f),
        new Vector3(0.07544f,-0.53096f,0.84403f), new Vector3(0.08603f,-0.53516f,0.84036f), new Vector3(0.09672f,-0.53944f,0.83645f), new Vector3(0.10684f,-0.56029f,0.82138f),
        new Vector3(0.11627f,-0.58063f,0.80583f), new Vector3(0.12493f,-0.60023f,0.79001f), new Vector3(0.13275f,-0.61888f,0.77418f), new Vector3(0.13970f,-0.63641f,0.75859f),
        new Vector3(0.14579f,-0.65266f,0.74349f), new Vector3(0.15103f,-0.66748f,0.72915f), new Vector3(0.15545f,-0.68076f,0.71582f), new Vector3(0.15910f,-0.69242f,0.70374f),
        new Vector3(0.16205f,-0.70237f,0.69312f), new Vector3(0.16436f,-0.71058f,0.68415f), new Vector3(0.16608f,-0.71700f,0.67700f), new Vector3(0.16727f,-0.72160f,0.67180f),
        new Vector3(0.16797f,-0.72437f,0.66864f), new Vector3(0.16820f,-0.72529f,0.66758f), new Vector3(0.16797f,-0.72437f,0.66864f), new Vector3(0.16727f,-0.72160f,0.67180f),
        new Vector3(0.16608f,-0.71700f,0.67700f), new Vector3(0.16436f,-0.71058f,0.68415f), new Vector3(0.16205f,-0.70237f,0.69312f), new Vector3(0.15910f,-0.69242f,0.70374f),
        new Vector3(0.15545f,-0.68076f,0.71582f), new Vector3(0.15103f,-0.66748f,0.72915f), new Vector3(0.14579f,-0.65266f,0.74349f), new Vector3(0.13970f,-0.63641f,0.75859f),
        new Vector3(0.13275f,-0.61888f,0.77418f), new Vector3(0.12493f,-0.60023f,0.79001f), new Vector3(0.11627f,-0.58063f,0.80583f), new Vector3(0.10684f,-0.56029f,0.82138f),
    };

    static readonly Vector3[] LegLeftFootWalkXDir = {
        new Vector3(0.99491f,0.02841f,-0.09673f), new Vector3(0.99615f,0.03187f,-0.08169f), new Vector3(0.99713f,0.03548f,-0.06681f), new Vector3(0.99786f,0.03919f,-0.05228f),
        new Vector3(0.99835f,0.04294f,-0.03825f), new Vector3(0.99860f,0.04666f,-0.02488f), new Vector3(0.99866f,0.05030f,-0.01232f), new Vector3(0.99855f,0.05379f,-0.00071f),
        new Vector3(0.99832f,0.05705f,0.00982f), new Vector3(0.99801f,0.06003f,0.01918f), new Vector3(0.99766f,0.06267f,0.02726f), new Vector3(0.99731f,0.06490f,0.03398f),
        new Vector3(0.99700f,0.06669f,0.03927f), new Vector3(0.99675f,0.06800f,0.04309f), new Vector3(0.99660f,0.06880f,0.04539f), new Vector3(0.99654f,0.06906f,0.04616f),
        new Vector3(0.99660f,0.06880f,0.04539f), new Vector3(0.99675f,0.06800f,0.04309f), new Vector3(0.99700f,0.06669f,0.03927f), new Vector3(0.99731f,0.06490f,0.03398f),
        new Vector3(0.99766f,0.06267f,0.02726f), new Vector3(0.99801f,0.06003f,0.01918f), new Vector3(0.99832f,0.05705f,0.00982f), new Vector3(0.99855f,0.05379f,-0.00071f),
        new Vector3(0.99866f,0.05030f,-0.01232f), new Vector3(0.99860f,0.04666f,-0.02488f), new Vector3(0.99835f,0.04294f,-0.03825f), new Vector3(0.99786f,0.03919f,-0.05228f),
        new Vector3(0.99713f,0.03548f,-0.06681f), new Vector3(0.99615f,0.03187f,-0.08169f), new Vector3(0.99491f,0.02841f,-0.09673f), new Vector3(0.99326f,0.02276f,-0.11367f),
        new Vector3(0.99130f,0.01742f,-0.13048f), new Vector3(0.98907f,0.01244f,-0.14695f), new Vector3(0.98661f,0.00786f,-0.16289f), new Vector3(0.98400f,0.00369f,-0.17812f),
        new Vector3(0.98131f,-0.00004f,-0.19246f), new Vector3(0.97860f,-0.00332f,-0.20574f), new Vector3(0.97597f,-0.00618f,-0.21781f), new Vector3(0.97349f,-0.00860f,-0.22855f),
        new Vector3(0.97125f,-0.01061f,-0.23783f), new Vector3(0.96930f,-0.01222f,-0.24556f), new Vector3(0.96772f,-0.01345f,-0.25165f), new Vector3(0.96656f,-0.01432f,-0.25605f),
        new Vector3(0.96584f,-0.01484f,-0.25871f), new Vector3(0.96560f,-0.01501f,-0.25960f), new Vector3(0.96584f,-0.01484f,-0.25871f), new Vector3(0.96656f,-0.01432f,-0.25605f),
        new Vector3(0.96772f,-0.01345f,-0.25165f), new Vector3(0.96930f,-0.01222f,-0.24556f), new Vector3(0.97125f,-0.01061f,-0.23783f), new Vector3(0.97349f,-0.00860f,-0.22855f),
        new Vector3(0.97597f,-0.00618f,-0.21781f), new Vector3(0.97860f,-0.00332f,-0.20574f), new Vector3(0.98131f,-0.00004f,-0.19246f), new Vector3(0.98400f,0.00369f,-0.17812f),
        new Vector3(0.98661f,0.00786f,-0.16289f), new Vector3(0.98907f,0.01244f,-0.14695f), new Vector3(0.99130f,0.01742f,-0.13048f), new Vector3(0.99326f,0.02276f,-0.11367f),
    };

    static readonly Vector3[] LegRightFootWalkYDir = {
        new Vector3(-0.10872f,-0.54168f,0.83352f), new Vector3(-0.11899f,-0.56289f,0.81792f), new Vector3(-0.12856f,-0.58357f,0.80182f), new Vector3(-0.13733f,-0.60349f,0.78545f),
        new Vector3(-0.14525f,-0.62244f,0.76907f), new Vector3(-0.15230f,-0.64023f,0.75293f), new Vector3(-0.15845f,-0.65671f,0.73731f), new Vector3(-0.16374f,-0.67174f,0.72247f),
        new Vector3(-0.16820f,-0.68519f,0.70867f), new Vector3(-0.17189f,-0.69700f,0.69617f), new Vector3(-0.17486f,-0.70708f,0.68518f), new Vector3(-0.17718f,-0.71538f,0.67590f),
        new Vector3(-0.17891f,-0.72187f,0.66850f), new Vector3(-0.18011f,-0.72652f,0.66312f), new Vector3(-0.18081f,-0.72932f,0.65985f), new Vector3(-0.18104f,-0.73025f,0.65876f),
        new Vector3(-0.18081f,-0.72932f,0.65985f), new Vector3(-0.18011f,-0.72652f,0.66312f), new Vector3(-0.17891f,-0.72187f,0.66850f), new Vector3(-0.17718f,-0.71538f,0.67590f),
        new Vector3(-0.17486f,-0.70708f,0.68518f), new Vector3(-0.17189f,-0.69700f,0.69617f), new Vector3(-0.16820f,-0.68519f,0.70867f), new Vector3(-0.16374f,-0.67174f,0.72247f),
        new Vector3(-0.15845f,-0.65671f,0.73731f), new Vector3(-0.15230f,-0.64023f,0.75293f), new Vector3(-0.14525f,-0.62244f,0.76907f), new Vector3(-0.13733f,-0.60349f,0.78545f),
        new Vector3(-0.12856f,-0.58357f,0.80182f), new Vector3(-0.11899f,-0.56289f,0.81792f), new Vector3(-0.10872f,-0.54168f,0.83352f), new Vector3(-0.09831f,-0.53685f,0.83793f),
        new Vector3(-0.08798f,-0.53210f,0.84210f), new Vector3(-0.07785f,-0.52749f,0.84599f), new Vector3(-0.06804f,-0.52307f,0.84957f), new Vector3(-0.05867f,-0.51888f,0.85283f),
        new Vector3(-0.04983f,-0.51496f,0.85576f), new Vector3(-0.04163f,-0.51136f,0.85836f), new Vector3(-0.03417f,-0.50810f,0.86062f), new Vector3(-0.02753f,-0.50521f,0.86255f),
        new Vector3(-0.02179f,-0.50273f,0.86417f), new Vector3(-0.01700f,-0.50067f,0.86547f), new Vector3(-0.01322f,-0.49905f,0.86647f), new Vector3(-0.01049f,-0.49788f,0.86718f),
        new Vector3(-0.00884f,-0.49718f,0.86760f), new Vector3(-0.00829f,-0.49694f,0.86774f), new Vector3(-0.00884f,-0.49718f,0.86760f), new Vector3(-0.01049f,-0.49788f,0.86718f),
        new Vector3(-0.01322f,-0.49905f,0.86647f), new Vector3(-0.01700f,-0.50067f,0.86547f), new Vector3(-0.02179f,-0.50273f,0.86417f), new Vector3(-0.02753f,-0.50521f,0.86255f),
        new Vector3(-0.03417f,-0.50810f,0.86062f), new Vector3(-0.04163f,-0.51136f,0.85836f), new Vector3(-0.04983f,-0.51496f,0.85576f), new Vector3(-0.05867f,-0.51888f,0.85283f),
        new Vector3(-0.06804f,-0.52307f,0.84957f), new Vector3(-0.07785f,-0.52749f,0.84599f), new Vector3(-0.08798f,-0.53210f,0.84210f), new Vector3(-0.09831f,-0.53685f,0.83793f),
    };

    static readonly Vector3[] LegRightFootWalkXDir = {
        new Vector3(0.99355f,-0.03212f,0.10872f), new Vector3(0.99168f,-0.02668f,0.12591f), new Vector3(0.98949f,-0.02156f,0.14295f), new Vector3(0.98703f,-0.01682f,0.15965f),
        new Vector3(0.98435f,-0.01249f,0.17581f), new Vector3(0.98151f,-0.00858f,0.19123f), new Vector3(0.97859f,-0.00511f,0.20575f), new Vector3(0.97568f,-0.00208f,0.21920f),
        new Vector3(0.97285f,0.00053f,0.23142f), new Vector3(0.97020f,0.00273f,0.24228f), new Vector3(0.96780f,0.00454f,0.25167f), new Vector3(0.96573f,0.00598f,0.25948f),
        new Vector3(0.96405f,0.00707f,0.26564f), new Vector3(0.96280f,0.00784f,0.27009f), new Vector3(0.96204f,0.00829f,0.27277f), new Vector3(0.96179f,0.00844f,0.27367f),
        new Vector3(0.96204f,0.00829f,0.27277f), new Vector3(0.96280f,0.00784f,0.27009f), new Vector3(0.96405f,0.00707f,0.26564f), new Vector3(0.96573f,0.00598f,0.25948f),
        new Vector3(0.96780f,0.00454f,0.25167f), new Vector3(0.97020f,0.00273f,0.24228f), new Vector3(0.97285f,0.00053f,0.23142f), new Vector3(0.97568f,-0.00208f,0.21920f),
        new Vector3(0.97859f,-0.00511f,0.20575f), new Vector3(0.98151f,-0.00858f,0.19123f), new Vector3(0.98435f,-0.01249f,0.17581f), new Vector3(0.98703f,-0.01682f,0.15965f),
        new Vector3(0.98949f,-0.02156f,0.14295f), new Vector3(0.99168f,-0.02668f,0.12591f), new Vector3(0.99355f,-0.03212f,0.10872f), new Vector3(0.99493f,-0.03499f,0.09431f),
        new Vector3(0.99607f,-0.03801f,0.08005f), new Vector3(0.99696f,-0.04114f,0.06610f), new Vector3(0.99763f,-0.04432f,0.05262f), new Vector3(0.99808f,-0.04750f,0.03976f),
        new Vector3(0.99833f,-0.05062f,0.02767f), new Vector3(0.99843f,-0.05362f,0.01648f), new Vector3(0.99839f,-0.05645f,0.00632f), new Vector3(0.99825f,-0.05903f,-0.00271f),
        new Vector3(0.99806f,-0.06133f,-0.01051f), new Vector3(0.99785f,-0.06328f,-0.01701f), new Vector3(0.99765f,-0.06484f,-0.02213f), new Vector3(0.99749f,-0.06598f,-0.02582f),
        new Vector3(0.99738f,-0.06668f,-0.02805f), new Vector3(0.99734f,-0.06692f,-0.02879f), new Vector3(0.99738f,-0.06668f,-0.02805f), new Vector3(0.99749f,-0.06598f,-0.02582f),
        new Vector3(0.99765f,-0.06484f,-0.02213f), new Vector3(0.99785f,-0.06328f,-0.01701f), new Vector3(0.99806f,-0.06133f,-0.01051f), new Vector3(0.99825f,-0.05903f,-0.00271f),
        new Vector3(0.99839f,-0.05645f,0.00632f), new Vector3(0.99843f,-0.05362f,0.01648f), new Vector3(0.99833f,-0.05062f,0.02767f), new Vector3(0.99808f,-0.04750f,0.03976f),
        new Vector3(0.99763f,-0.04432f,0.05262f), new Vector3(0.99696f,-0.04114f,0.06610f), new Vector3(0.99607f,-0.03801f,0.08005f), new Vector3(0.99493f,-0.03499f,0.09431f),
    };
}
