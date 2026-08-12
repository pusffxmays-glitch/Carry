using UnityEngine;

// Baked world-space bone-orientation data for the Carry_Balance_Stagger_Right/Left animations,
// authored in Blender (2026-08-10) as a procedural, sine-wave-driven stumble cycle. Ported here
// as sampled per-frame data -- each entry is a bone's local +Y axis direction in world space,
// captured with the armature at identity (so directly usable as a root-local direction, same
// convention as GoblinCarryRig's BasePose array) -- rather than as an imported FBX/Animator clip.
// Separately-exported pose FBX files have repeatedly failed to apply correctly in this project
// due to hierarchy path mismatches (see WORKLOG.md); this sidesteps that risk entirely using the
// same technique already proven for the base pose and arm IK.
//
// Only Hips and the 4 leg bones are covered -- in the source Blender animation, Spine/neck/Head
// and the arms are held at the Carry_Balance_Neutral pose throughout the stagger, so
// GoblinCarryRig's existing ApplyBasePose()/SolveArm() already handle them correctly with no
// changes needed. The leg motion itself is identical between the Right and Left stagger (only
// the body lean direction differs), so only Hips has two variants here.
public static class GoblinStagger
{
    public const int FrameCount = 60;

    // Each bone also has an X-axis ("roll reference") table alongside its Y-axis ("aim") table,
    // added 2026-08-10 after the Y-only version left leg roll/twist untouched during the stagger
    // and it read as the legs twisting at the knee -- see GoblinCarryRig.ReattachLeg/BlendAim for
    // how the two combine into a full rotation instead of just an aim.
    public static void SampleLeftUpLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftUpLegYDir, phase01); xDir = Sample(LegLeftUpLegXDir, phase01); }
    public static void SampleLeftLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftLegYDir, phase01); xDir = Sample(LegLeftLegXDir, phase01); }
    public static void SampleRightUpLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightUpLegYDir, phase01); xDir = Sample(LegRightUpLegXDir, phase01); }
    public static void SampleRightLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightLegYDir, phase01); xDir = Sample(LegRightLegXDir, phase01); }
    public static void SampleHips(float phase01, bool leanRight, out Vector3 yDir, out Vector3 xDir)
    {
        yDir = Sample(leanRight ? HipsRightYDir : HipsLeftYDir, phase01);
        xDir = Sample(leanRight ? HipsRightXDir : HipsLeftXDir, phase01);
    }
    public static void SampleLeftFoot(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftFootYDir, phase01); xDir = Sample(LegLeftFootXDir, phase01); }
    public static void SampleRightFoot(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightFootYDir, phase01); xDir = Sample(LegRightFootXDir, phase01); }

    static Vector3 Sample(Vector3[] frames, float phase01)
    {
        phase01 = Mathf.Repeat(phase01, 1f);
        float f = phase01 * frames.Length;
        int i0 = Mathf.FloorToInt(f) % frames.Length;
        int i1 = (i0 + 1) % frames.Length;
        float t = f - Mathf.Floor(f);
        return Vector3.Slerp(frames[i0], frames[i1], t).normalized;
    }

    static readonly Vector3[] LegLeftUpLegYDir = {
        new Vector3(0.53536f,-0.65884f,0.52850f), new Vector3(0.50511f,-0.65279f,0.56456f), new Vector3(0.47355f,-0.64550f,0.59923f), new Vector3(0.44137f,-0.63723f,0.63177f),
        new Vector3(0.40936f,-0.62824f,0.66163f), new Vector3(0.37840f,-0.61868f,0.68851f), new Vector3(0.34935f,-0.60875f,0.71231f), new Vector3(0.32291f,-0.59860f,0.73308f),
        new Vector3(0.29963f,-0.58846f,0.75096f), new Vector3(0.27979f,-0.57861f,0.76611f), new Vector3(0.26343f,-0.56939f,0.77872f), new Vector3(0.25039f,-0.56119f,0.78891f),
        new Vector3(0.24030f,-0.55441f,0.79680f), new Vector3(0.23276f,-0.54942f,0.80247f), new Vector3(0.22733f,-0.54655f,0.80598f), new Vector3(0.22367f,-0.54599f,0.80738f),
        new Vector3(0.22160f,-0.54783f,0.80671f), new Vector3(0.22117f,-0.55200f,0.80398f), new Vector3(0.22264f,-0.55829f,0.79921f), new Vector3(0.22648f,-0.56634f,0.79244f),
        new Vector3(0.23331f,-0.57566f,0.78370f), new Vector3(0.24380f,-0.58563f,0.77304f), new Vector3(0.25859f,-0.59559f,0.76053f), new Vector3(0.27815f,-0.60486f,0.74618f),
        new Vector3(0.30273f,-0.61277f,0.72998f), new Vector3(0.33226f,-0.61878f,0.71183f), new Vector3(0.36637f,-0.62248f,0.69159f), new Vector3(0.40434f,-0.62364f,0.66901f),
        new Vector3(0.44521f,-0.62223f,0.64391f), new Vector3(0.48776f,-0.61843f,0.61614f), new Vector3(0.53071f,-0.61256f,0.58576f), new Vector3(0.54960f,-0.62712f,0.55196f),
        new Vector3(0.56650f,-0.64153f,0.51722f), new Vector3(0.58072f,-0.65580f,0.48238f), new Vector3(0.59182f,-0.66991f,0.44829f), new Vector3(0.59965f,-0.68376f,0.41580f),
        new Vector3(0.60429f,-0.69721f,0.38565f), new Vector3(0.60601f,-0.71013f,0.35843f), new Vector3(0.60518f,-0.72237f,0.33457f), new Vector3(0.60224f,-0.73383f,0.31431f),
        new Vector3(0.59764f,-0.74443f,0.29773f), new Vector3(0.59178f,-0.75414f,0.28473f), new Vector3(0.58500f,-0.76294f,0.27514f), new Vector3(0.57761f,-0.77083f,0.26869f),
        new Vector3(0.56988f,-0.77779f,0.26509f), new Vector3(0.56209f,-0.78379f,0.26405f), new Vector3(0.55450f,-0.78874f,0.26536f), new Vector3(0.54738f,-0.79253f,0.26885f),
        new Vector3(0.54102f,-0.79497f,0.27447f), new Vector3(0.53569f,-0.79585f,0.28222f), new Vector3(0.53165f,-0.79496f,0.29221f), new Vector3(0.52906f,-0.79204f,0.30457f),
        new Vector3(0.52800f,-0.78688f,0.31945f), new Vector3(0.52838f,-0.77927f,0.33698f), new Vector3(0.52997f,-0.76909f,0.35726f), new Vector3(0.53237f,-0.75628f,0.38029f),
        new Vector3(0.53502f,-0.74091f,0.40596f), new Vector3(0.53728f,-0.72314f,0.43405f), new Vector3(0.53847f,-0.70326f,0.46420f), new Vector3(0.53798f,-0.68167f,0.49589f),
    };

    static readonly Vector3[] LegLeftLegYDir = {
        new Vector3(0.12587f,-0.71988f,-0.68260f), new Vector3(0.12756f,-0.72431f,-0.67757f), new Vector3(0.13024f,-0.72835f,-0.67272f), new Vector3(0.13301f,-0.73212f,-0.66807f),
        new Vector3(0.13503f,-0.73576f,-0.66364f), new Vector3(0.13563f,-0.73941f,-0.65945f), new Vector3(0.13438f,-0.74316f,-0.65548f), new Vector3(0.13110f,-0.74704f,-0.65172f),
        new Vector3(0.12591f,-0.75102f,-0.64817f), new Vector3(0.11918f,-0.75498f,-0.64483f), new Vector3(0.11156f,-0.75875f,-0.64175f), new Vector3(0.10387f,-0.76215f,-0.63901f),
        new Vector3(0.09702f,-0.76496f,-0.63673f), new Vector3(0.09200f,-0.76697f,-0.63506f), new Vector3(0.08968f,-0.76800f,-0.63414f), new Vector3(0.09083f,-0.76792f,-0.63408f),
        new Vector3(0.09599f,-0.76659f,-0.63492f), new Vector3(0.10542f,-0.76394f,-0.63661f), new Vector3(0.11909f,-0.75992f,-0.63901f), new Vector3(0.13664f,-0.75451f,-0.64190f),
        new Vector3(0.15745f,-0.74776f,-0.64504f), new Vector3(0.18062f,-0.73974f,-0.64820f), new Vector3(0.20510f,-0.73064f,-0.65123f), new Vector3(0.22976f,-0.72068f,-0.65409f),
        new Vector3(0.25347f,-0.71017f,-0.65682f), new Vector3(0.27526f,-0.69943f,-0.65957f), new Vector3(0.29430f,-0.68882f,-0.66251f), new Vector3(0.31006f,-0.67865f,-0.66580f),
        new Vector3(0.32225f,-0.66919f,-0.66958f), new Vector3(0.33089f,-0.66060f,-0.67388f), new Vector3(0.33626f,-0.65298f,-0.67864f), new Vector3(0.31150f,-0.63650f,-0.70557f),
        new Vector3(0.28419f,-0.62040f,-0.73098f), new Vector3(0.25549f,-0.60458f,-0.75446f), new Vector3(0.22665f,-0.58894f,-0.77574f), new Vector3(0.19892f,-0.57343f,-0.79474f),
        new Vector3(0.17335f,-0.55809f,-0.81147f), new Vector3(0.15075f,-0.54306f,-0.82605f), new Vector3(0.13162f,-0.52859f,-0.83861f), new Vector3(0.11610f,-0.51502f,-0.84928f),
        new Vector3(0.10396f,-0.50277f,-0.85814f), new Vector3(0.09471f,-0.49228f,-0.86527f), new Vector3(0.08762f,-0.48395f,-0.87070f), new Vector3(0.08184f,-0.47818f,-0.87444f),
        new Vector3(0.07655f,-0.47525f,-0.87651f), new Vector3(0.07098f,-0.47535f,-0.87693f), new Vector3(0.06462f,-0.47855f,-0.87568f), new Vector3(0.05720f,-0.48484f,-0.87273f),
        new Vector3(0.04879f,-0.49408f,-0.86805f), new Vector3(0.03977f,-0.50608f,-0.86157f), new Vector3(0.03086f,-0.52058f,-0.85325f), new Vector3(0.02295f,-0.53728f,-0.84309f),
        new Vector3(0.01712f,-0.55584f,-0.83111f), new Vector3(0.01441f,-0.57587f,-0.81741f), new Vector3(0.01579f,-0.59696f,-0.80211f), new Vector3(0.02198f,-0.61866f,-0.78535f),
        new Vector3(0.03338f,-0.64047f,-0.76726f), new Vector3(0.05002f,-0.66193f,-0.74790f), new Vector3(0.07154f,-0.68256f,-0.72732f), new Vector3(0.09718f,-0.70197f,-0.70554f),
    };

    static readonly Vector3[] LegRightUpLegYDir = {
        new Vector3(-0.35910f,-0.81285f,0.45862f), new Vector3(-0.34888f,-0.83633f,0.42290f), new Vector3(-0.33770f,-0.85838f,0.38618f), new Vector3(-0.32566f,-0.87855f,0.34943f),
        new Vector3(-0.31304f,-0.89648f,0.31358f), new Vector3(-0.30024f,-0.91199f,0.27953f), new Vector3(-0.28778f,-0.92503f,0.24798f), new Vector3(-0.27619f,-0.93570f,0.21951f),
        new Vector3(-0.26589f,-0.94419f,0.19446f), new Vector3(-0.25719f,-0.95075f,0.17301f), new Vector3(-0.25026f,-0.95567f,0.15516f), new Vector3(-0.24506f,-0.95923f,0.14078f),
        new Vector3(-0.24145f,-0.96171f,0.12966f), new Vector3(-0.23916f,-0.96335f,0.12151f), new Vector3(-0.23791f,-0.96433f,0.11605f), new Vector3(-0.23740f,-0.96482f,0.11301f),
        new Vector3(-0.23739f,-0.96492f,0.11219f), new Vector3(-0.23774f,-0.96468f,0.11344f), new Vector3(-0.23841f,-0.96413f,0.11672f), new Vector3(-0.23946f,-0.96320f,0.12208f),
        new Vector3(-0.24105f,-0.96181f,0.12965f), new Vector3(-0.24338f,-0.95983f,0.13961f), new Vector3(-0.24667f,-0.95708f,0.15218f), new Vector3(-0.25111f,-0.95334f,0.16759f),
        new Vector3(-0.25679f,-0.94839f,0.18604f), new Vector3(-0.26373f,-0.94198f,0.20767f), new Vector3(-0.27179f,-0.93384f,0.23252f), new Vector3(-0.28072f,-0.92376f,0.26049f),
        new Vector3(-0.29020f,-0.91155f,0.29131f), new Vector3(-0.29986f,-0.89709f,0.32452f), new Vector3(-0.30936f,-0.88039f,0.35946f), new Vector3(-0.29013f,-0.86587f,0.40754f),
        new Vector3(-0.27137f,-0.84846f,0.45440f), new Vector3(-0.25357f,-0.82870f,0.49895f), new Vector3(-0.23733f,-0.80731f,0.54030f), new Vector3(-0.22328f,-0.78503f,0.57782f),
        new Vector3(-0.21199f,-0.76263f,0.61111f), new Vector3(-0.20389f,-0.74082f,0.64001f), new Vector3(-0.19918f,-0.72020f,0.66456f), new Vector3(-0.19778f,-0.70125f,0.68493f),
        new Vector3(-0.19935f,-0.68434f,0.70139f), new Vector3(-0.20334f,-0.66970f,0.71425f), new Vector3(-0.20900f,-0.65751f,0.72388f), new Vector3(-0.21554f,-0.64786f,0.73063f),
        new Vector3(-0.22220f,-0.64079f,0.73486f), new Vector3(-0.22831f,-0.63635f,0.73684f), new Vector3(-0.23344f,-0.63457f,0.73677f), new Vector3(-0.23739f,-0.63544f,0.73476f),
        new Vector3(-0.24023f,-0.63895f,0.73078f), new Vector3(-0.24230f,-0.64504f,0.72472f), new Vector3(-0.24413f,-0.65359f,0.71639f), new Vector3(-0.24643f,-0.66444f,0.70554f),
        new Vector3(-0.24993f,-0.67735f,0.69191f), new Vector3(-0.25530f,-0.69202f,0.67523f), new Vector3(-0.26307f,-0.70812f,0.65525f), new Vector3(-0.27356f,-0.72529f,0.63176f),
        new Vector3(-0.28680f,-0.74314f,0.60456f), new Vector3(-0.30254f,-0.76125f,0.57355f), new Vector3(-0.32029f,-0.77921f,0.53874f), new Vector3(-0.33940f,-0.79656f,0.50030f),
    };

    static readonly Vector3[] LegRightLegYDir = {
        new Vector3(0.22408f,-0.69374f,-0.68448f), new Vector3(0.25017f,-0.66357f,-0.70504f), new Vector3(0.27687f,-0.63204f,-0.72379f), new Vector3(0.30301f,-0.59982f,-0.74055f),
        new Vector3(0.32748f,-0.56766f,-0.75533f), new Vector3(0.34936f,-0.53637f,-0.76828f), new Vector3(0.36798f,-0.50674f,-0.77962f), new Vector3(0.38298f,-0.47945f,-0.78959f),
        new Vector3(0.39432f,-0.45502f,-0.79842f), new Vector3(0.40226f,-0.43383f,-0.80621f), new Vector3(0.40733f,-0.41605f,-0.81301f), new Vector3(0.41027f,-0.40170f,-0.81873f),
        new Vector3(0.41193f,-0.39068f,-0.82321f), new Vector3(0.41321f,-0.38278f,-0.82628f), new Vector3(0.41492f,-0.37777f,-0.82773f), new Vector3(0.41776f,-0.37541f,-0.82737f),
        new Vector3(0.42220f,-0.37548f,-0.82508f), new Vector3(0.42843f,-0.37783f,-0.82079f), new Vector3(0.43636f,-0.38234f,-0.81450f), new Vector3(0.44560f,-0.38897f,-0.80631f),
        new Vector3(0.45551f,-0.39777f,-0.79642f), new Vector3(0.46529f,-0.40879f,-0.78511f), new Vector3(0.47401f,-0.42216f,-0.77272f), new Vector3(0.48077f,-0.43799f,-0.75963f),
        new Vector3(0.48473f,-0.45637f,-0.74617f), new Vector3(0.48522f,-0.47733f,-0.73262f), new Vector3(0.48178f,-0.50080f,-0.71909f), new Vector3(0.47422f,-0.52658f,-0.70557f),
        new Vector3(0.46259f,-0.55435f,-0.69188f), new Vector3(0.44724f,-0.58364f,-0.67775f), new Vector3(0.42873f,-0.61385f,-0.66285f), new Vector3(0.43224f,-0.61876f,-0.65598f),
        new Vector3(0.43434f,-0.62425f,-0.64936f), new Vector3(0.43566f,-0.63003f,-0.64285f), new Vector3(0.43680f,-0.63586f,-0.63630f), new Vector3(0.43823f,-0.64152f,-0.62961f),
        new Vector3(0.44026f,-0.64684f,-0.62271f), new Vector3(0.44302f,-0.65177f,-0.61557f), new Vector3(0.44640f,-0.65632f,-0.60826f), new Vector3(0.45010f,-0.66056f,-0.60088f),
        new Vector3(0.45367f,-0.66464f,-0.59366f), new Vector3(0.45651f,-0.66870f,-0.58689f), new Vector3(0.45797f,-0.67290f,-0.58091f), new Vector3(0.45743f,-0.67736f,-0.57615f),
        new Vector3(0.45432f,-0.68212f,-0.57297f), new Vector3(0.44821f,-0.68719f,-0.57173f), new Vector3(0.43884f,-0.69248f,-0.57262f), new Vector3(0.42613f,-0.69785f,-0.57569f),
        new Vector3(0.41026f,-0.70308f,-0.58083f), new Vector3(0.39161f,-0.70796f,-0.58774f), new Vector3(0.37078f,-0.71224f,-0.59602f), new Vector3(0.34857f,-0.71569f,-0.60521f),
        new Vector3(0.32592f,-0.71814f,-0.61486f), new Vector3(0.30381f,-0.71943f,-0.62460f), new Vector3(0.28324f,-0.71947f,-0.63414f), new Vector3(0.26506f,-0.71822f,-0.64335f),
        new Vector3(0.24996f,-0.71567f,-0.65218f), new Vector3(0.23833f,-0.71186f,-0.66064f), new Vector3(0.23031f,-0.70686f,-0.66881f), new Vector3(0.22571f,-0.70076f,-0.67675f),
    };

    static readonly Vector3[] HipsRightYDir = {
        new Vector3(-0.19041f,0.94090f,-0.28008f), new Vector3(-0.19829f,0.93948f,-0.27939f), new Vector3(-0.20592f,0.93804f,-0.27872f), new Vector3(-0.21308f,0.93663f,-0.27806f),
        new Vector3(-0.21961f,0.93530f,-0.27745f), new Vector3(-0.22535f,0.93410f,-0.27690f), new Vector3(-0.23022f,0.93305f,-0.27643f), new Vector3(-0.23417f,0.93218f,-0.27604f),
        new Vector3(-0.23719f,0.93151f,-0.27574f), new Vector3(-0.23932f,0.93103f,-0.27553f), new Vector3(-0.24065f,0.93072f,-0.27540f), new Vector3(-0.24130f,0.93057f,-0.27533f),
        new Vector3(-0.24141f,0.93055f,-0.27532f), new Vector3(-0.24114f,0.93061f,-0.27535f), new Vector3(-0.24067f,0.93072f,-0.27539f), new Vector3(-0.24017f,0.93083f,-0.27544f),
        new Vector3(-0.23983f,0.93091f,-0.27548f), new Vector3(-0.23978f,0.93092f,-0.27548f), new Vector3(-0.24017f,0.93083f,-0.27544f), new Vector3(-0.24108f,0.93062f,-0.27535f),
        new Vector3(-0.24258f,0.93028f,-0.27520f), new Vector3(-0.24468f,0.92979f,-0.27499f), new Vector3(-0.24735f,0.92917f,-0.27471f), new Vector3(-0.25053f,0.92841f,-0.27438f),
        new Vector3(-0.25412f,0.92755f,-0.27401f), new Vector3(-0.25795f,0.92661f,-0.27360f), new Vector3(-0.26188f,0.92563f,-0.27318f), new Vector3(-0.26569f,0.92467f,-0.27276f),
        new Vector3(-0.26919f,0.92377f,-0.27238f), new Vector3(-0.27216f,0.92299f,-0.27205f), new Vector3(-0.27440f,0.92240f,-0.27180f), new Vector3(-0.27573f,0.92205f,-0.27165f),
        new Vector3(-0.27599f,0.92198f,-0.27162f), new Vector3(-0.27504f,0.92224f,-0.27173f), new Vector3(-0.27279f,0.92283f,-0.27198f), new Vector3(-0.26920f,0.92376f,-0.27238f),
        new Vector3(-0.26428f,0.92503f,-0.27292f), new Vector3(-0.25806f,0.92658f,-0.27359f), new Vector3(-0.25065f,0.92838f,-0.27437f), new Vector3(-0.24220f,0.93037f,-0.27524f),
        new Vector3(-0.23289f,0.93247f,-0.27617f), new Vector3(-0.22296f,0.93460f,-0.27713f), new Vector3(-0.21267f,0.93671f,-0.27810f), new Vector3(-0.20228f,0.93873f,-0.27904f),
        new Vector3(-0.19210f,0.94060f,-0.27993f), new Vector3(-0.18242f,0.94229f,-0.28075f), new Vector3(-0.17350f,0.94375f,-0.28148f), new Vector3(-0.16561f,0.94498f,-0.28210f),
        new Vector3(-0.15896f,0.94597f,-0.28261f), new Vector3(-0.15374f,0.94672f,-0.28300f), new Vector3(-0.15008f,0.94722f,-0.28327f), new Vector3(-0.14805f,0.94750f,-0.28342f),
        new Vector3(-0.14768f,0.94755f,-0.28345f), new Vector3(-0.14893f,0.94738f,-0.28336f), new Vector3(-0.15172f,0.94700f,-0.28315f), new Vector3(-0.15590f,0.94641f,-0.28284f),
        new Vector3(-0.16130f,0.94563f,-0.28243f), new Vector3(-0.16770f,0.94466f,-0.28194f), new Vector3(-0.17486f,0.94353f,-0.28137f), new Vector3(-0.18251f,0.94227f,-0.28074f),
    };

    static readonly Vector3[] HipsLeftYDir = {
        new Vector3(0.10994f,0.94998f,-0.29232f), new Vector3(0.11792f,0.94903f,-0.29228f), new Vector3(0.12565f,0.94806f,-0.29223f), new Vector3(0.13292f,0.94709f,-0.29216f),
        new Vector3(0.13954f,0.94616f,-0.29209f), new Vector3(0.14537f,0.94530f,-0.29201f), new Vector3(0.15031f,0.94455f,-0.29194f), new Vector3(0.15432f,0.94392f,-0.29188f),
        new Vector3(0.15739f,0.94343f,-0.29182f), new Vector3(0.15956f,0.94308f,-0.29178f), new Vector3(0.16092f,0.94286f,-0.29176f), new Vector3(0.16158f,0.94275f,-0.29175f),
        new Vector3(0.16169f,0.94273f,-0.29175f), new Vector3(0.16141f,0.94278f,-0.29175f), new Vector3(0.16093f,0.94286f,-0.29176f), new Vector3(0.16043f,0.94294f,-0.29177f),
        new Vector3(0.16008f,0.94300f,-0.29178f), new Vector3(0.16003f,0.94300f,-0.29178f), new Vector3(0.16043f,0.94294f,-0.29177f), new Vector3(0.16135f,0.94279f,-0.29175f),
        new Vector3(0.16287f,0.94253f,-0.29172f), new Vector3(0.16501f,0.94218f,-0.29168f), new Vector3(0.16773f,0.94171f,-0.29163f), new Vector3(0.17097f,0.94115f,-0.29156f),
        new Vector3(0.17462f,0.94051f,-0.29148f), new Vector3(0.17852f,0.93980f,-0.29139f), new Vector3(0.18252f,0.93906f,-0.29129f), new Vector3(0.18641f,0.93833f,-0.29119f),
        new Vector3(0.18997f,0.93764f,-0.29109f), new Vector3(0.19300f,0.93705f,-0.29101f), new Vector3(0.19529f,0.93660f,-0.29094f), new Vector3(0.19664f,0.93633f,-0.29090f),
        new Vector3(0.19690f,0.93627f,-0.29089f), new Vector3(0.19593f,0.93647f,-0.29092f), new Vector3(0.19364f,0.93692f,-0.29099f), new Vector3(0.18999f,0.93764f,-0.29109f),
        new Vector3(0.18496f,0.93860f,-0.29122f), new Vector3(0.17863f,0.93978f,-0.29138f), new Vector3(0.17109f,0.94113f,-0.29155f), new Vector3(0.16249f,0.94260f,-0.29173f),
        new Vector3(0.15303f,0.94413f,-0.29190f), new Vector3(0.14294f,0.94566f,-0.29204f), new Vector3(0.13249f,0.94715f,-0.29217f), new Vector3(0.12197f,0.94853f,-0.29226f),
        new Vector3(0.11165f,0.94978f,-0.29231f), new Vector3(0.10185f,0.95088f,-0.29233f), new Vector3(0.09283f,0.95180f,-0.29233f), new Vector3(0.08485f,0.95255f,-0.29231f),
        new Vector3(0.07814f,0.95314f,-0.29227f), new Vector3(0.07287f,0.95357f,-0.29224f), new Vector3(0.06917f,0.95385f,-0.29221f), new Vector3(0.06712f,0.95400f,-0.29219f),
        new Vector3(0.06675f,0.95403f,-0.29218f), new Vector3(0.06801f,0.95394f,-0.29220f), new Vector3(0.07083f,0.95372f,-0.29222f), new Vector3(0.07505f,0.95339f,-0.29225f),
        new Vector3(0.08050f,0.95294f,-0.29229f), new Vector3(0.08697f,0.95236f,-0.29231f), new Vector3(0.09420f,0.95167f,-0.29233f), new Vector3(0.10195f,0.95087f,-0.29233f),
    };
    static readonly Vector3[] HipsRightXDir = {
        new Vector3(0.97746f,0.20821f,0.03496f), new Vector3(0.97486f,0.21859f,0.04315f), new Vector3(0.97197f,0.22907f,0.05284f), new Vector3(0.96893f,0.23915f,0.06305f),
        new Vector3(0.96592f,0.24840f,0.07282f), new Vector3(0.96313f,0.25646f,0.08133f), new Vector3(0.96075f,0.26311f,0.08796f), new Vector3(0.95892f,0.26823f,0.09235f),
        new Vector3(0.95770f,0.27182f,0.09446f), new Vector3(0.95708f,0.27399f,0.09451f), new Vector3(0.95695f,0.27496f,0.09301f), new Vector3(0.95715f,0.27502f,0.09068f),
        new Vector3(0.95751f,0.27454f,0.08836f), new Vector3(0.95782f,0.27391f,0.08694f), new Vector3(0.95790f,0.27351f,0.08725f), new Vector3(0.95760f,0.27370f,0.08998f),
        new Vector3(0.95675f,0.27477f,0.09558f), new Vector3(0.95523f,0.27689f,0.10422f), new Vector3(0.95296f,0.28013f,0.11577f), new Vector3(0.94986f,0.28446f,0.12977f),
        new Vector3(0.94599f,0.28972f,0.14551f), new Vector3(0.94144f,0.29567f,0.16206f), new Vector3(0.93646f,0.30203f,0.17839f), new Vector3(0.93136f,0.30849f,0.19342f),
        new Vector3(0.92652f,0.31474f,0.20617f), new Vector3(0.92234f,0.32049f,0.21582f), new Vector3(0.91915f,0.32551f,0.22182f), new Vector3(0.91720f,0.32958f,0.22386f),
        new Vector3(0.91659f,0.33255f,0.22198f), new Vector3(0.91727f,0.33428f,0.21651f), new Vector3(0.91906f,0.33472f,0.20806f), new Vector3(0.92172f,0.33381f,0.19748f),
        new Vector3(0.92495f,0.33160f,0.18576f), new Vector3(0.92847f,0.32815f,0.17397f), new Vector3(0.93204f,0.32358f,0.16311f), new Vector3(0.93548f,0.31804f,0.15406f),
        new Vector3(0.93867f,0.31168f,0.14747f), new Vector3(0.94156f,0.30466f,0.14371f), new Vector3(0.94411f,0.29710f,0.14282f), new Vector3(0.94633f,0.28910f,0.14449f),
        new Vector3(0.94828f,0.28073f,0.14817f), new Vector3(0.95004f,0.27203f,0.15304f), new Vector3(0.95174f,0.26303f,0.15814f), new Vector3(0.95352f,0.25377f,0.16248f),
        new Vector3(0.95554f,0.24429f,0.16510f), new Vector3(0.95793f,0.23467f,0.16522f), new Vector3(0.96075f,0.22501f,0.16224f), new Vector3(0.96399f,0.21547f,0.15589f),
        new Vector3(0.96752f,0.20625f,0.14618f), new Vector3(0.97116f,0.19760f,0.13345f), new Vector3(0.97466f,0.18981f,0.11834f), new Vector3(0.97780f,0.18321f,0.10172f),
        new Vector3(0.98037f,0.17810f,0.08462f), new Vector3(0.98224f,0.17480f,0.06816f), new Vector3(0.98338f,0.17352f,0.05342f), new Vector3(0.98381f,0.17441f,0.04132f),
        new Vector3(0.98358f,0.17750f,0.03258f), new Vector3(0.98278f,0.18271f,0.02762f), new Vector3(0.98146f,0.18981f,0.02656f), new Vector3(0.97968f,0.19845f,0.02918f),
    };

    static readonly Vector3[] HipsLeftXDir = {
        new Vector3(0.98215f,-0.14899f,-0.11481f), new Vector3(0.97953f,-0.15953f,-0.12280f), new Vector3(0.97650f,-0.17019f,-0.13225f), new Vector3(0.97325f,-0.18046f,-0.14220f),
        new Vector3(0.97001f,-0.18989f,-0.15171f), new Vector3(0.96703f,-0.19813f,-0.15998f), new Vector3(0.96453f,-0.20493f,-0.16641f), new Vector3(0.96266f,-0.21016f,-0.17066f),
        new Vector3(0.96149f,-0.21381f,-0.17266f), new Vector3(0.96100f,-0.21602f,-0.17267f), new Vector3(0.96105f,-0.21699f,-0.17117f), new Vector3(0.96145f,-0.21704f,-0.16886f),
        new Vector3(0.96196f,-0.21653f,-0.16658f), new Vector3(0.96235f,-0.21588f,-0.16518f), new Vector3(0.96238f,-0.21548f,-0.16550f), new Vector3(0.96187f,-0.21570f,-0.16820f),
        new Vector3(0.96063f,-0.21682f,-0.17371f), new Vector3(0.95855f,-0.21905f,-0.18221f), new Vector3(0.95554f,-0.22246f,-0.19354f), new Vector3(0.95159f,-0.22700f,-0.20725f),
        new Vector3(0.94677f,-0.23252f,-0.22264f), new Vector3(0.94126f,-0.23877f,-0.23879f), new Vector3(0.93536f,-0.24546f,-0.25466f), new Vector3(0.92945f,-0.25225f,-0.26924f),
        new Vector3(0.92398f,-0.25881f,-0.28157f), new Vector3(0.91938f,-0.26483f,-0.29088f), new Vector3(0.91603f,-0.27005f,-0.29660f), new Vector3(0.91417f,-0.27423f,-0.29849f),
        new Vector3(0.91389f,-0.27723f,-0.29657f), new Vector3(0.91511f,-0.27891f,-0.29118f), new Vector3(0.91761f,-0.27921f,-0.28290f), new Vector3(0.92106f,-0.27812f,-0.27257f),
        new Vector3(0.92509f,-0.27569f,-0.26115f), new Vector3(0.92934f,-0.27201f,-0.24967f), new Vector3(0.93350f,-0.26720f,-0.23913f), new Vector3(0.93732f,-0.26144f,-0.23038f),
        new Vector3(0.94065f,-0.25489f,-0.22406f), new Vector3(0.94341f,-0.24769f,-0.22053f), new Vector3(0.94556f,-0.23999f,-0.21982f), new Vector3(0.94715f,-0.23188f,-0.22166f),
        new Vector3(0.94829f,-0.22341f,-0.22546f), new Vector3(0.94912f,-0.21463f,-0.23043f), new Vector3(0.94985f,-0.20556f,-0.23563f), new Vector3(0.95072f,-0.19622f,-0.24008f),
        new Vector3(0.95194f,-0.18664f,-0.24283f), new Vector3(0.95373f,-0.17690f,-0.24312f), new Vector3(0.95620f,-0.16708f,-0.24036f), new Vector3(0.95935f,-0.15735f,-0.23427f),
        new Vector3(0.96310f,-0.14791f,-0.22486f), new Vector3(0.96723f,-0.13902f,-0.21244f), new Vector3(0.97148f,-0.13100f,-0.19765f), new Vector3(0.97555f,-0.12417f,-0.18132f),
        new Vector3(0.97919f,-0.11888f,-0.16447f), new Vector3(0.98220f,-0.11543f,-0.14822f), new Vector3(0.98445f,-0.11405f,-0.13361f), new Vector3(0.98591f,-0.11488f,-0.12158f),
        new Vector3(0.98658f,-0.11796f,-0.11286f), new Vector3(0.98650f,-0.12320f,-0.10787f), new Vector3(0.98571f,-0.13035f,-0.10672f), new Vector3(0.98424f,-0.13910f,-0.10921f),
    };

    static readonly Vector3[] LegLeftUpLegXDir = {
        new Vector3(0.82227f,0.54955f,-0.14787f), new Vector3(0.83687f,0.53036f,-0.13549f), new Vector3(0.85074f,0.51137f,-0.12147f), new Vector3(0.86378f,0.49242f,-0.10678f),
        new Vector3(0.87597f,0.47342f,-0.09245f), new Vector3(0.88728f,0.45435f,-0.07938f), new Vector3(0.89771f,0.43526f,-0.06830f), new Vector3(0.90726f,0.41632f,-0.05969f),
        new Vector3(0.91589f,0.39783f,-0.05370f), new Vector3(0.92356f,0.38016f,-0.05018f), new Vector3(0.93020f,0.36381f,-0.04867f), new Vector3(0.93575f,0.34932f,-0.04850f),
        new Vector3(0.94014f,0.33728f,-0.04886f), new Vector3(0.94332f,0.32827f,-0.04886f), new Vector3(0.94525f,0.32283f,-0.04770f), new Vector3(0.94588f,0.32141f,-0.04469f),
        new Vector3(0.94512f,0.32434f,-0.03937f), new Vector3(0.94283f,0.33177f,-0.03158f), new Vector3(0.93884f,0.34370f,-0.02144f), new Vector3(0.93294f,0.35992f,-0.00941f),
        new Vector3(0.92495f,0.38007f,0.00382f), new Vector3(0.91475f,0.40365f,0.01729f), new Vector3(0.90232f,0.43003f,0.02997f), new Vector3(0.88774f,0.45854f,0.04077f),
        new Vector3(0.87122f,0.48846f,0.04873f), new Vector3(0.85306f,0.51910f,0.05306f), new Vector3(0.83359f,0.54981f,0.05328f), new Vector3(0.81313f,0.58000f,0.04922f),
        new Vector3(0.79198f,0.60917f,0.04107f), new Vector3(0.77040f,0.63688f,0.02936f), new Vector3(0.74863f,0.66282f,0.01487f), new Vector3(0.74981f,0.66163f,0.00511f),
        new Vector3(0.75186f,0.65930f,-0.00574f), new Vector3(0.75463f,0.65594f,-0.01671f), new Vector3(0.75801f,0.65169f,-0.02686f), new Vector3(0.76192f,0.64670f,-0.03537f),
        new Vector3(0.76630f,0.64113f,-0.04166f), new Vector3(0.77108f,0.63511f,-0.04539f), new Vector3(0.77622f,0.62874f,-0.04652f), new Vector3(0.78164f,0.62208f,-0.04530f),
        new Vector3(0.78728f,0.61514f,-0.04226f), new Vector3(0.79307f,0.60793f,-0.03813f), new Vector3(0.79896f,0.60043f,-0.03378f), new Vector3(0.80490f,0.59264f,-0.03013f),
        new Vector3(0.81086f,0.58456f,-0.02804f), new Vector3(0.81679f,0.57624f,-0.02825f), new Vector3(0.82259f,0.56777f,-0.03127f), new Vector3(0.82813f,0.55929f,-0.03739f),
        new Vector3(0.83322f,0.55098f,-0.04655f), new Vector3(0.83764f,0.54309f,-0.05846f), new Vector3(0.84116f,0.53590f,-0.07250f), new Vector3(0.84361f,0.52972f,-0.08787f),
        new Vector3(0.84487f,0.52485f,-0.10360f), new Vector3(0.84492f,0.52157f,-0.11868f), new Vector3(0.84383f,0.52009f,-0.13213f), new Vector3(0.84174f,0.52057f,-0.14311f),
        new Vector3(0.83884f,0.52303f,-0.15095f), new Vector3(0.83530f,0.52740f,-0.15530f), new Vector3(0.83129f,0.53349f,-0.15605f), new Vector3(0.82691f,0.54100f,-0.15342f),
    };

    static readonly Vector3[] LegLeftLegXDir = {
        new Vector3(0.94451f,0.29740f,-0.13947f), new Vector3(0.95402f,0.27643f,-0.11589f), new Vector3(0.96238f,0.25604f,-0.09089f), new Vector3(0.96952f,0.23605f,-0.06566f),
        new Vector3(0.97544f,0.21634f,-0.04139f), new Vector3(0.98025f,0.19683f,-0.01909f), new Vector3(0.98411f,0.17757f,0.00043f), new Vector3(0.98719f,0.15869f,0.01668f),
        new Vector3(0.98965f,0.14047f,0.02948f), new Vector3(0.99161f,0.12324f,0.03899f), new Vector3(0.99317f,0.10742f,0.04564f), new Vector3(0.99436f,0.09351f,0.05009f),
        new Vector3(0.99521f,0.08201f,0.05312f), new Vector3(0.99575f,0.07343f,0.05556f), new Vector3(0.99597f,0.06825f,0.05819f), new Vector3(0.99585f,0.06688f,0.06166f),
        new Vector3(0.99536f,0.06962f,0.06642f), new Vector3(0.99441f,0.07668f,0.07265f), new Vector3(0.99287f,0.08810f,0.08026f), new Vector3(0.99062f,0.10378f,0.08888f),
        new Vector3(0.98751f,0.12349f,0.09788f), new Vector3(0.98342f,0.14687f,0.10641f), new Vector3(0.97828f,0.17344f,0.11351f), new Vector3(0.97210f,0.20264f,0.11818f),
        new Vector3(0.96490f,0.23388f,0.11949f), new Vector3(0.95675f,0.26652f,0.11665f), new Vector3(0.94770f,0.29992f,0.10916f), new Vector3(0.93778f,0.33345f,0.09682f),
        new Vector3(0.92698f,0.36654f,0.07980f), new Vector3(0.91524f,0.39863f,0.05862f), new Vector3(0.90252f,0.42928f,0.03414f), new Vector3(0.90389f,0.42756f,0.01335f),
        new Vector3(0.90530f,0.42469f,-0.00849f), new Vector3(0.90666f,0.42078f,-0.03017f), new Vector3(0.90796f,0.41600f,-0.05054f), new Vector3(0.90926f,0.41052f,-0.06863f),
        new Vector3(0.91069f,0.40453f,-0.08368f), new Vector3(0.91236f,0.39816f,-0.09526f), new Vector3(0.91437f,0.39149f,-0.10325f), new Vector3(0.91677f,0.38456f,-0.10789f),
        new Vector3(0.91956f,0.37734f,-0.10968f), new Vector3(0.92266f,0.36977f,-0.10939f), new Vector3(0.92600f,0.36177f,-0.10790f), new Vector3(0.92947f,0.35329f,-0.10620f),
        new Vector3(0.93295f,0.34428f,-0.10520f), new Vector3(0.93635f,0.33478f,-0.10568f), new Vector3(0.93954f,0.32488f,-0.10821f), new Vector3(0.94242f,0.31474f,-0.11309f),
        new Vector3(0.94485f,0.30461f,-0.12028f), new Vector3(0.94675f,0.29480f,-0.12946f), new Vector3(0.94804f,0.28568f,-0.14002f), new Vector3(0.94873f,0.27764f,-0.15111f),
        new Vector3(0.94886f,0.27109f,-0.16176f), new Vector3(0.94858f,0.26639f,-0.17095f), new Vector3(0.94805f,0.26386f,-0.17772f), new Vector3(0.94742f,0.26372f,-0.18124f),
        new Vector3(0.94683f,0.26606f,-0.18091f), new Vector3(0.94632f,0.27084f,-0.17641f), new Vector3(0.94585f,0.27789f,-0.16776f), new Vector3(0.94530f,0.28689f,-0.15524f),
    };

    static readonly Vector3[] LegRightUpLegXDir = {
        new Vector3(0.92552f,-0.24684f,0.28719f), new Vector3(0.92583f,-0.23759f,0.29391f), new Vector3(0.92559f,-0.22832f,0.30191f), new Vector3(0.92495f,-0.21943f,0.31035f),
        new Vector3(0.92410f,-0.21130f,0.31842f), new Vector3(0.92325f,-0.20420f,0.32543f), new Vector3(0.92260f,-0.19833f,0.33086f), new Vector3(0.92228f,-0.19377f,0.33443f),
        new Vector3(0.92236f,-0.19052f,0.33609f), new Vector3(0.92280f,-0.18848f,0.33603f), new Vector3(0.92349f,-0.18749f,0.33470f), new Vector3(0.92425f,-0.18730f,0.33268f),
        new Vector3(0.92490f,-0.18762f,0.33070f), new Vector3(0.92522f,-0.18814f,0.32951f), new Vector3(0.92504f,-0.18853f,0.32979f), new Vector3(0.92420f,-0.18850f,0.33214f),
        new Vector3(0.92261f,-0.18781f,0.33692f), new Vector3(0.92020f,-0.18629f,0.34427f), new Vector3(0.91697f,-0.18388f,0.35406f), new Vector3(0.91297f,-0.18060f,0.36588f),
        new Vector3(0.90835f,-0.17655f,0.37911f), new Vector3(0.90334f,-0.17190f,0.39297f), new Vector3(0.89825f,-0.16686f,0.40657f), new Vector3(0.89347f,-0.16167f,0.41902f),
        new Vector3(0.88938f,-0.15656f,0.42952f), new Vector3(0.88637f,-0.15173f,0.43741f), new Vector3(0.88472f,-0.14739f,0.44221f), new Vector3(0.88457f,-0.14369f,0.44373f),
        new Vector3(0.88590f,-0.14079f,0.44199f), new Vector3(0.88855f,-0.13882f,0.43727f), new Vector3(0.89218f,-0.13789f,0.43011f), new Vector3(0.90110f,-0.10377f,0.42102f),
        new Vector3(0.90921f,-0.07110f,0.41022f), new Vector3(0.91621f,-0.04031f,0.39867f), new Vector3(0.92186f,-0.01176f,0.38735f), new Vector3(0.92603f,0.01425f,0.37719f),
        new Vector3(0.92870f,0.03749f,0.36894f), new Vector3(0.92995f,0.05776f,0.36312f), new Vector3(0.92998f,0.07491f,0.35991f), new Vector3(0.92903f,0.08881f,0.35919f),
        new Vector3(0.92744f,0.09934f,0.36053f), new Vector3(0.92559f,0.10640f,0.36326f), new Vector3(0.92388f,0.10991f,0.36657f), new Vector3(0.92269f,0.10981f,0.36957f),
        new Vector3(0.92239f,0.10609f,0.37141f), new Vector3(0.92322f,0.09878f,0.37137f), new Vector3(0.92529f,0.08795f,0.36893f), new Vector3(0.92855f,0.07377f,0.36380f),
        new Vector3(0.93278f,0.05646f,0.35600f), new Vector3(0.93760f,0.03634f,0.34581f), new Vector3(0.94254f,0.01380f,0.33380f), new Vector3(0.94711f,-0.01068f,0.32074f),
        new Vector3(0.95081f,-0.03662f,0.30759f), new Vector3(0.95328f,-0.06349f,0.29536f), new Vector3(0.95422f,-0.09079f,0.28498f), new Vector3(0.95350f,-0.11812f,0.27728f),
        new Vector3(0.95107f,-0.14512f,0.27279f), new Vector3(0.94694f,-0.17156f,0.27178f), new Vector3(0.94122f,-0.19734f,0.27415f), new Vector3(0.93403f,-0.22241f,0.27951f),
    };

    static readonly Vector3[] LegRightLegXDir = {
        new Vector3(0.94945f,-0.00304f,0.31390f), new Vector3(0.94404f,0.00553f,0.32977f), new Vector3(0.93784f,0.01367f,0.34681f), new Vector3(0.93117f,0.02101f,0.36399f),
        new Vector3(0.92445f,0.02722f,0.38034f), new Vector3(0.91809f,0.03213f,0.39506f), new Vector3(0.91250f,0.03565f,0.40753f), new Vector3(0.90793f,0.03782f,0.41742f),
        new Vector3(0.90453f,0.03875f,0.42464f), new Vector3(0.90229f,0.03865f,0.42940f), new Vector3(0.90103f,0.03775f,0.43211f), new Vector3(0.90047f,0.03635f,0.43340f),
        new Vector3(0.90025f,0.03476f,0.43399f), new Vector3(0.89999f,0.03331f,0.43464f), new Vector3(0.89934f,0.03232f,0.43606f), new Vector3(0.89798f,0.03207f,0.43886f),
        new Vector3(0.89571f,0.03278f,0.44342f), new Vector3(0.89242f,0.03461f,0.44989f), new Vector3(0.88809f,0.03761f,0.45813f), new Vector3(0.88287f,0.04175f,0.46777f),
        new Vector3(0.87701f,0.04690f,0.47818f), new Vector3(0.87091f,0.05289f,0.48859f), new Vector3(0.86505f,0.05950f,0.49814f), new Vector3(0.85999f,0.06646f,0.50596f),
        new Vector3(0.85627f,0.07354f,0.51127f), new Vector3(0.85436f,0.08048f,0.51341f), new Vector3(0.85460f,0.08704f,0.51195f), new Vector3(0.85711f,0.09298f,0.50667f),
        new Vector3(0.86182f,0.09806f,0.49765f), new Vector3(0.86844f,0.10207f,0.48518f), new Vector3(0.87650f,0.10480f,0.46987f), new Vector3(0.88241f,0.14034f,0.44906f),
        new Vector3(0.88759f,0.17383f,0.42657f), new Vector3(0.89173f,0.20485f,0.40356f), new Vector3(0.89463f,0.23308f,0.38121f), new Vector3(0.89623f,0.25831f,0.36061f),
        new Vector3(0.89664f,0.28040f,0.34266f), new Vector3(0.89603f,0.29929f,0.32796f), new Vector3(0.89469f,0.31495f,0.31677f), new Vector3(0.89295f,0.32736f,0.30901f),
        new Vector3(0.89117f,0.33650f,0.30428f), new Vector3(0.88972f,0.34237f,0.30197f), new Vector3(0.88896f,0.34494f,0.30127f), new Vector3(0.88923f,0.34422f,0.30132f),
        new Vector3(0.89077f,0.34021f,0.30130f), new Vector3(0.89378f,0.33295f,0.30050f), new Vector3(0.89830f,0.32252f,0.29839f), new Vector3(0.90424f,0.30905f,0.29469f),
        new Vector3(0.91136f,0.29274f,0.28937f), new Vector3(0.91930f,0.27384f,0.28267f), new Vector3(0.92761f,0.25268f,0.27512f), new Vector3(0.93582f,0.22964f,0.26742f),
        new Vector3(0.94343f,0.20515f,0.26048f), new Vector3(0.95005f,0.17963f,0.25522f), new Vector3(0.95533f,0.15349f,0.25256f), new Vector3(0.95902f,0.12707f,0.25326f),
        new Vector3(0.96093f,0.10066f,0.25783f), new Vector3(0.96096f,0.07441f,0.26650f), new Vector3(0.95904f,0.04839f,0.27910f), new Vector3(0.95518f,0.02260f,0.29517f),
    };
    static readonly Vector3[] LegLeftFootYDir = {
        new Vector3(0.13139f,-0.53475f,0.83474f), new Vector3(0.09834f,-0.53518f,0.83899f), new Vector3(0.06467f,-0.53525f,0.84221f), new Vector3(0.03131f,-0.53490f,0.84433f),
        new Vector3(-0.00085f,-0.53402f,0.84547f), new Vector3(-0.03101f,-0.53248f,0.84588f), new Vector3(-0.05850f,-0.53018f,0.84587f), new Vector3(-0.08287f,-0.52709f,0.84576f),
        new Vector3(-0.10385f,-0.52329f,0.84580f), new Vector3(-0.12140f,-0.51894f,0.84614f), new Vector3(-0.13567f,-0.51435f,0.84678f), new Vector3(-0.14695f,-0.50987f,0.84761f),
        new Vector3(-0.15567f,-0.50593f,0.84841f), new Vector3(-0.16227f,-0.50296f,0.84894f), new Vector3(-0.16720f,-0.50135f,0.84894f), new Vector3(-0.17082f,-0.50140f,0.84818f),
        new Vector3(-0.17334f,-0.50331f,0.84654f), new Vector3(-0.17475f,-0.50712f,0.84397f), new Vector3(-0.17488f,-0.51274f,0.84054f), new Vector3(-0.17336f,-0.51993f,0.83643f),
        new Vector3(-0.16965f,-0.52830f,0.83193f), new Vector3(-0.16318f,-0.53736f,0.82742f), new Vector3(-0.15335f,-0.54652f,0.82329f), new Vector3(-0.13965f,-0.55516f,0.81993f),
        new Vector3(-0.12172f,-0.56270f,0.81765f), new Vector3(-0.09938f,-0.56856f,0.81662f), new Vector3(-0.07266f,-0.57232f,0.81680f), new Vector3(-0.04187f,-0.57367f,0.81802f),
        new Vector3(-0.00749f,-0.57248f,0.81989f), new Vector3(0.02973f,-0.56880f,0.82194f), new Vector3(0.06887f,-0.56285f,0.82368f), new Vector3(0.09635f,-0.58285f,0.80685f),
        new Vector3(0.12267f,-0.60210f,0.78894f), new Vector3(0.14677f,-0.62071f,0.77018f), new Vector3(0.16776f,-0.63875f,0.75091f), new Vector3(0.18503f,-0.65620f,0.73155f),
        new Vector3(0.19823f,-0.67299f,0.71260f), new Vector3(0.20729f,-0.68897f,0.69451f), new Vector3(0.21241f,-0.70395f,0.67774f), new Vector3(0.21395f,-0.71771f,0.66265f),
        new Vector3(0.21245f,-0.73002f,0.64956f), new Vector3(0.20851f,-0.74066f,0.63871f), new Vector3(0.20279f,-0.74944f,0.63025f), new Vector3(0.19593f,-0.75619f,0.62432f),
        new Vector3(0.18855f,-0.76080f,0.62099f), new Vector3(0.18123f,-0.76317f,0.62027f), new Vector3(0.17448f,-0.76320f,0.62215f), new Vector3(0.16870f,-0.76087f,0.62659f),
        new Vector3(0.16423f,-0.75613f,0.63348f), new Vector3(0.16124f,-0.74895f,0.64271f), new Vector3(0.15975f,-0.73934f,0.65410f), new Vector3(0.15963f,-0.72732f,0.66747f),
        new Vector3(0.16055f,-0.71293f,0.68261f), new Vector3(0.16200f,-0.69626f,0.69927f), new Vector3(0.16335f,-0.67744f,0.71721f), new Vector3(0.16386f,-0.65667f,0.73616f),
        new Vector3(0.16277f,-0.63420f,0.75584f), new Vector3(0.15940f,-0.61038f,0.77591f), new Vector3(0.15321f,-0.58558f,0.79600f), new Vector3(0.14388f,-0.56023f,0.81575f),
    };

    static readonly Vector3[] LegLeftFootXDir = {
        new Vector3(0.97807f,0.20721f,-0.02121f), new Vector3(0.98257f,0.18586f,0.00339f), new Vector3(0.98583f,0.16516f,0.02926f), new Vector3(0.98790f,0.14494f,0.05518f),
        new Vector3(0.98892f,0.12507f,0.07999f), new Vector3(0.98910f,0.10552f,0.10268f), new Vector3(0.98871f,0.08633f,0.12249f), new Vector3(0.98798f,0.06765f,0.13896f),
        new Vector3(0.98713f,0.04974f,0.15198f), new Vector3(0.98629f,0.03292f,0.16170f), new Vector3(0.98553f,0.01758f,0.16858f), new Vector3(0.98487f,0.00415f,0.17325f),
        new Vector3(0.98428f,-0.00691f,0.17647f), new Vector3(0.98372f,-0.01516f,0.17905f), new Vector3(0.98314f,-0.02017f,0.18172f), new Vector3(0.98248f,-0.02157f,0.18512f),
        new Vector3(0.98166f,-0.01907f,0.18967f), new Vector3(0.98062f,-0.01248f,0.19554f), new Vector3(0.97924f,-0.00174f,0.20268f), new Vector3(0.97746f,0.01309f,0.21072f),
        new Vector3(0.97519f,0.03183f,0.21908f), new Vector3(0.97239f,0.05420f,0.22697f), new Vector3(0.96909f,0.07979f,0.23347f), new Vector3(0.96532f,0.10814f,0.23763f),
        new Vector3(0.96117f,0.13873f,0.23855f), new Vector3(0.95672f,0.17100f,0.23548f), new Vector3(0.95200f,0.20437f,0.22789f), new Vector3(0.94699f,0.23824f,0.21554f),
        new Vector3(0.94158f,0.27205f,0.19855f), new Vector3(0.93561f,0.30522f,0.17738f), new Vector3(0.92892f,0.33728f,0.15280f), new Vector3(0.93421f,0.33267f,0.12875f),
        new Vector3(0.93934f,0.32700f,0.10350f), new Vector3(0.94405f,0.32038f,0.07830f), new Vector3(0.94821f,0.31296f,0.05437f), new Vector3(0.95181f,0.30493f,0.03278f),
        new Vector3(0.95493f,0.29648f,0.01435f), new Vector3(0.95770f,0.28776f,-0.00039f), new Vector3(0.96026f,0.27889f,-0.01127f), new Vector3(0.96270f,0.26994f,-0.01846f),
        new Vector3(0.96511f,0.26089f,-0.02245f), new Vector3(0.96750f,0.25173f,-0.02394f), new Vector3(0.96988f,0.24240f,-0.02384f), new Vector3(0.97224f,0.23284f,-0.02309f),
        new Vector3(0.97454f,0.22306f,-0.02263f), new Vector3(0.97676f,0.21307f,-0.02323f), new Vector3(0.97885f,0.20300f,-0.02548f), new Vector3(0.98075f,0.19301f,-0.02968f),
        new Vector3(0.98239f,0.18337f,-0.03582f), new Vector3(0.98371f,0.17438f,-0.04358f), new Vector3(0.98466f,0.16643f,-0.05236f), new Vector3(0.98522f,0.15992f,-0.06136f),
        new Vector3(0.98542f,0.15526f,-0.06961f), new Vector3(0.98532f,0.15281f,-0.07612f), new Vector3(0.98501f,0.15287f,-0.07995f), new Vector3(0.98454f,0.15564f,-0.08032f),
        new Vector3(0.98394f,0.16118f,-0.07666f), new Vector3(0.98315f,0.16942f,-0.06871f), new Vector3(0.98202f,0.18011f,-0.05651f), new Vector3(0.98039f,0.19288f,-0.04046f),
    };

    static readonly Vector3[] LegRightFootYDir = {
        new Vector3(-0.06958f,-0.57339f,0.81632f), new Vector3(-0.07048f,-0.60398f,0.79388f), new Vector3(-0.07095f,-0.63378f,0.77026f), new Vector3(-0.07054f,-0.66224f,0.74596f),
        new Vector3(-0.06898f,-0.68889f,0.72158f), new Vector3(-0.06622f,-0.71331f,0.69771f), new Vector3(-0.06238f,-0.73523f,0.67494f), new Vector3(-0.05775f,-0.75450f,0.65375f),
        new Vector3(-0.05269f,-0.77107f,0.63457f), new Vector3(-0.04761f,-0.78500f,0.61767f), new Vector3(-0.04293f,-0.79641f,0.60323f), new Vector3(-0.03897f,-0.80548f,0.59134f),
        new Vector3(-0.03602f,-0.81239f,0.58201f), new Vector3(-0.03427f,-0.81731f,0.57518f), new Vector3(-0.03387f,-0.82041f,0.57077f), new Vector3(-0.03486f,-0.82181f,0.56870f),
        new Vector3(-0.03726f,-0.82160f,0.56884f), new Vector3(-0.04104f,-0.81986f,0.57110f), new Vector3(-0.04613f,-0.81658f,0.57539f), new Vector3(-0.05239f,-0.81176f,0.58163f),
        new Vector3(-0.05967f,-0.80534f,0.58980f), new Vector3(-0.06774f,-0.79723f,0.59986f), new Vector3(-0.07631f,-0.78731f,0.61182f), new Vector3(-0.08503f,-0.77541f,0.62571f),
        new Vector3(-0.09352f,-0.76137f,0.64154f), new Vector3(-0.10136f,-0.74502f,0.65929f), new Vector3(-0.10813f,-0.72625f,0.67888f), new Vector3(-0.11347f,-0.70496f,0.70011f),
        new Vector3(-0.11710f,-0.68118f,0.72269f), new Vector3(-0.11889f,-0.65506f,0.74617f), new Vector3(-0.11887f,-0.62686f,0.77001f), new Vector3(-0.08634f,-0.61001f,0.78768f),
        new Vector3(-0.05444f,-0.59170f,0.80432f), new Vector3(-0.02411f,-0.57258f,0.81949f), new Vector3(0.00374f,-0.55330f,0.83298f), new Vector3(0.02830f,-0.53447f,0.84472f),
        new Vector3(0.04893f,-0.51663f,0.85481f), new Vector3(0.06525f,-0.50022f,0.86344f), new Vector3(0.07713f,-0.48554f,0.87080f), new Vector3(0.08475f,-0.47280f,0.87709f),
        new Vector3(0.08855f,-0.46207f,0.88241f), new Vector3(0.08919f,-0.45339f,0.88684f), new Vector3(0.08749f,-0.44672f,0.89039f), new Vector3(0.08431f,-0.44199f,0.89305f),
        new Vector3(0.08051f,-0.43914f,0.89480f), new Vector3(0.07681f,-0.43811f,0.89564f), new Vector3(0.07373f,-0.43883f,0.89554f), new Vector3(0.07153f,-0.44125f,0.89453f),
        new Vector3(0.07021f,-0.44530f,0.89262f), new Vector3(0.06947f,-0.45092f,0.88986f), new Vector3(0.06878f,-0.45797f,0.88630f), new Vector3(0.06746f,-0.46631f,0.88204f),
        new Vector3(0.06471f,-0.47579f,0.87717f), new Vector3(0.05975f,-0.48622f,0.87179f), new Vector3(0.05188f,-0.49742f,0.86596f), new Vector3(0.04058f,-0.50923f,0.85967f),
        new Vector3(0.02553f,-0.52154f,0.85284f), new Vector3(0.00666f,-0.53422f,0.84532f), new Vector3(-0.01582f,-0.54718f,0.83687f), new Vector3(-0.04145f,-0.56029f,0.82726f),
    };

    static readonly Vector3[] LegRightFootXDir = {
        new Vector3(0.97281f,0.14220f,0.18280f), new Vector3(0.96640f,0.15589f,0.20440f), new Vector3(0.95905f,0.16893f,0.22734f), new Vector3(0.95108f,0.18085f,0.25049f),
        new Vector3(0.94289f,0.19125f,0.27273f), new Vector3(0.93494f,0.19991f,0.29312f), new Vector3(0.92768f,0.20672f,0.31093f), new Vector3(0.92146f,0.21171f,0.32572f),
        new Vector3(0.91650f,0.21501f,0.33735f), new Vector3(0.91285f,0.21684f,0.34596f), new Vector3(0.91040f,0.21750f,0.35194f), new Vector3(0.90890f,0.21732f,0.35591f),
        new Vector3(0.90801f,0.21664f,0.35859f), new Vector3(0.90734f,0.21583f,0.36076f), new Vector3(0.90652f,0.21524f,0.36317f), new Vector3(0.90522f,0.21518f,0.36643f),
        new Vector3(0.90321f,0.21587f,0.37096f), new Vector3(0.90036f,0.21748f,0.37691f), new Vector3(0.89665f,0.22006f,0.38418f), new Vector3(0.89221f,0.22357f,0.39240f),
        new Vector3(0.88731f,0.22789f,0.40094f), new Vector3(0.88232f,0.23281f,0.40905f), new Vector3(0.87772f,0.23808f,0.41584f), new Vector3(0.87405f,0.24343f,0.42045f),
        new Vector3(0.87182f,0.24856f,0.42208f), new Vector3(0.87144f,0.25319f,0.42010f), new Vector3(0.87320f,0.25704f,0.41407f), new Vector3(0.87716f,0.25985f,0.40382f),
        new Vector3(0.88318f,0.26136f,0.38946f), new Vector3(0.89093f,0.26136f,0.37140f), new Vector3(0.89991f,0.25967f,0.35032f), new Vector3(0.89920f,0.29269f,0.32524f),
        new Vector3(0.89791f,0.32335f,0.29865f), new Vector3(0.89592f,0.35134f,0.27184f), new Vector3(0.89317f,0.37645f,0.24604f), new Vector3(0.88977f,0.39858f,0.22238f),
        new Vector3(0.88590f,0.41771f,0.20174f), new Vector3(0.88185f,0.43384f,0.18470f), new Vector3(0.87793f,0.44702f,0.17149f), new Vector3(0.87446f,0.45726f,0.16199f),
        new Vector3(0.87172f,0.46457f,0.15580f), new Vector3(0.87000f,0.46896f,0.15225f), new Vector3(0.86951f,0.47040f,0.15057f), new Vector3(0.87045f,0.46889f,0.14988f),
        new Vector3(0.87293f,0.46442f,0.14938f), new Vector3(0.87700f,0.45702f,0.14834f), new Vector3(0.88262f,0.44675f,0.14625f), new Vector3(0.88965f,0.43374f,0.14281f),
        new Vector3(0.89784f,0.41815f,0.13798f), new Vector3(0.90686f,0.40022f,0.13200f), new Vector3(0.91635f,0.38025f,0.12537f), new Vector3(0.92590f,0.35860f,0.11877f),
        new Vector3(0.93517f,0.33566f,0.11308f), new Vector3(0.94385f,0.31181f,0.10922f), new Vector3(0.95168f,0.28744f,0.10809f), new Vector3(0.95849f,0.26285f,0.11046f),
        new Vector3(0.96413f,0.23831f,0.11688f), new Vector3(0.96847f,0.21397f,0.12759f), new Vector3(0.97141f,0.18987f,0.14250f), new Vector3(0.97287f,0.16598f,0.16116f),
    };
}
