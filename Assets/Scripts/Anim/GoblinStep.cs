using UnityEngine;

// 踏み替え (よろけの 2 段目)。GoblinCarryRig.ApplyRecoverStep が引く。
// 生成元: Boxing_Practice フレーム 98〜108 (0.42 秒) -- bake_step_cs.py (2026-08-24)
//
// **0 フレーム目からの差分** として持っている。素材の土台は足を前後に大きく開いた
// 構えなので、絶対姿勢で乗せると歩行の足位置ごと置き換わってしまう。差分なら
// 歩行の上に「1 歩ぶんの変化」だけを重ねられる。
//
// 左右のミラーは実行時に行う (Mirror = true)。矢状面の反転は、差分クォータニオンが
// (x, -y, -z, w)、横位置が符号反転。
public static class GoblinStep
{
    public const int FrameCount = 21;
    /// <summary>この踏み替えで腰が横へ動く量 (m)。移動量の目安。</summary>
    public const float SideShift = 0.2705f;

    /// <summary>腰の 0 フレーム目からのずれ。mirror で左右反転。</summary>
    public static Vector3 HipsOffset(float u, bool mirror)
    {
        Vector3 p = SamplePos(HipsPos, u);
        if (mirror) p.x = -p.x;
        return p;
    }

    public static Quaternion LeftUpLegDelta(float u, bool mirror)
    { return Delta(LeftUpLegY, LeftUpLegX, LeftUpLegRef, u, mirror); }
    public static Quaternion LeftLegDelta(float u, bool mirror)
    { return Delta(LeftLegY, LeftLegX, LeftLegRef, u, mirror); }
    public static Quaternion LeftFootDelta(float u, bool mirror)
    { return Delta(LeftFootY, LeftFootX, LeftFootRef, u, mirror); }
    public static Quaternion LeftToeDelta(float u, bool mirror)
    { return Delta(LeftToeY, LeftToeX, LeftToeRef, u, mirror); }
    public static Quaternion RightUpLegDelta(float u, bool mirror)
    { return Delta(RightUpLegY, RightUpLegX, RightUpLegRef, u, mirror); }
    public static Quaternion RightLegDelta(float u, bool mirror)
    { return Delta(RightLegY, RightLegX, RightLegRef, u, mirror); }
    public static Quaternion RightFootDelta(float u, bool mirror)
    { return Delta(RightFootY, RightFootX, RightFootRef, u, mirror); }
    public static Quaternion RightToeDelta(float u, bool mirror)
    { return Delta(RightToeY, RightToeX, RightToeRef, u, mirror); }
    public static Quaternion Spine02Delta(float u, bool mirror)
    { return Delta(Spine02Y, Spine02X, Spine02Ref, u, mirror); }
    public static Quaternion Spine01Delta(float u, bool mirror)
    { return Delta(Spine01Y, Spine01X, Spine01Ref, u, mirror); }

    static Quaternion Delta(Vector3[] ys, Vector3[] xs, Quaternion refBasis, float u, bool mirror)
    {
        Quaternion d = Basis(Sample(ys, u), Sample(xs, u)) * Quaternion.Inverse(refBasis);
        if (mirror) d = new Quaternion(d.x, -d.y, -d.z, d.w);
        return d;
    }

    static Quaternion Basis(Vector3 y, Vector3 x)
    {
        y = y.normalized;
        x = (x - y * Vector3.Dot(x, y)).normalized;
        return Quaternion.LookRotation(Vector3.Cross(x, y), y);
    }

    static void Index(float u, int len, out int i0, out int i1, out float f)
    {
        float t = Mathf.Clamp01(u) * (len - 1);
        i0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, len - 1);
        i1 = Mathf.Min(i0 + 1, len - 1);
        f = t - i0;
    }

    static Vector3 Sample(Vector3[] a, float u)
    { int i0, i1; float f; Index(u, a.Length, out i0, out i1, out f); return Vector3.Slerp(a[i0], a[i1], f).normalized; }

    static Vector3 SamplePos(Vector3[] a, float u)
    { int i0, i1; float f; Index(u, a.Length, out i0, out i1, out f); return Vector3.Lerp(a[i0], a[i1], f); }

    static readonly Vector3[] HipsPos = {
        new Vector3(0.00000f,0.00000f,0.00000f), new Vector3(-0.01409f,-0.00734f,-0.00780f), new Vector3(-0.02818f,-0.01468f,-0.01560f), new Vector3(-0.04391f,-0.02456f,-0.02288f),
        new Vector3(-0.05964f,-0.03445f,-0.03015f), new Vector3(-0.07469f,-0.04917f,-0.03356f), new Vector3(-0.08974f,-0.06388f,-0.03697f), new Vector3(-0.10308f,-0.07837f,-0.03457f),
        new Vector3(-0.11641f,-0.09286f,-0.03217f), new Vector3(-0.12996f,-0.10353f,-0.02825f), new Vector3(-0.14351f,-0.11420f,-0.02433f), new Vector3(-0.15861f,-0.12083f,-0.01883f),
        new Vector3(-0.17370f,-0.12745f,-0.01334f), new Vector3(-0.19004f,-0.13006f,-0.00730f), new Vector3(-0.20637f,-0.13268f,-0.00127f), new Vector3(-0.22234f,-0.13036f,0.00352f),
        new Vector3(-0.23831f,-0.12804f,0.00832f), new Vector3(-0.24814f,-0.12149f,0.01144f), new Vector3(-0.25797f,-0.11494f,0.01456f), new Vector3(-0.26421f,-0.10819f,0.01759f),
        new Vector3(-0.27046f,-0.10143f,0.02061f),
    };

    static readonly Vector3[] LeftUpLegY = {
        new Vector3(0.56230f,-0.77338f,-0.29274f), new Vector3(0.58908f,-0.75762f,-0.28105f), new Vector3(0.61531f,-0.74085f,-0.26932f), new Vector3(0.65413f,-0.71293f,-0.25268f),
        new Vector3(0.69151f,-0.68272f,-0.23600f), new Vector3(0.72741f,-0.64779f,-0.22638f), new Vector3(0.76165f,-0.61062f,-0.21687f), new Vector3(0.78433f,-0.58195f,-0.21485f),
        new Vector3(0.80608f,-0.55202f,-0.21335f), new Vector3(0.81977f,-0.53091f,-0.21474f), new Vector3(0.83290f,-0.50942f,-0.21628f), new Vector3(0.83738f,-0.50064f,-0.21945f),
        new Vector3(0.84175f,-0.49185f,-0.22258f), new Vector3(0.83730f,-0.49777f,-0.22615f), new Vector3(0.83310f,-0.50294f,-0.23022f), new Vector3(0.82408f,-0.51660f,-0.23241f),
        new Vector3(0.81568f,-0.52815f,-0.23605f), new Vector3(0.79591f,-0.55789f,-0.23514f), new Vector3(0.77665f,-0.58356f,-0.23720f), new Vector3(0.75550f,-0.61427f,-0.22782f),
        new Vector3(0.73503f,-0.64030f,-0.22303f),
    };

    static readonly Vector3[] LeftUpLegX = {
        new Vector3(0.26013f,0.50147f,-0.82515f), new Vector3(0.24890f,0.50101f,-0.82888f), new Vector3(0.23788f,0.50023f,-0.83257f), new Vector3(0.21899f,0.49826f,-0.83892f),
        new Vector3(0.20082f,0.49552f,-0.84506f), new Vector3(0.17638f,0.49533f,-0.85061f), new Vector3(0.15274f,0.49444f,-0.85568f), new Vector3(0.13366f,0.49674f,-0.85755f),
        new Vector3(0.11492f,0.49964f,-0.85857f), new Vector3(0.10323f,0.50581f,-0.85644f), new Vector3(0.09165f,0.51238f,-0.85386f), new Vector3(0.09162f,0.52433f,-0.84657f),
        new Vector3(0.09145f,0.53624f,-0.83910f), new Vector3(0.10527f,0.55267f,-0.82672f), new Vector3(0.11870f,0.56909f,-0.81366f), new Vector3(0.14506f,0.58906f,-0.79496f),
        new Vector3(0.17040f,0.60929f,-0.77442f), new Vector3(0.21361f,0.62220f,-0.75316f), new Vector3(0.25551f,0.63603f,-0.72814f), new Vector3(0.31711f,0.64715f,-0.69328f),
        new Vector3(0.37542f,0.65824f,-0.65252f),
    };

    static readonly Vector3[] LeftLegY = {
        new Vector3(-0.29976f,-0.74540f,-0.59542f), new Vector3(-0.28598f,-0.75390f,-0.59149f), new Vector3(-0.27200f,-0.76215f,-0.58749f), new Vector3(-0.26699f,-0.76799f,-0.58215f),
        new Vector3(-0.26174f,-0.77404f,-0.57650f), new Vector3(-0.25899f,-0.77850f,-0.57172f), new Vector3(-0.25575f,-0.78312f,-0.56684f), new Vector3(-0.24519f,-0.78758f,-0.56533f),
        new Vector3(-0.23422f,-0.79120f,-0.56492f), new Vector3(-0.21289f,-0.79466f,-0.56851f), new Vector3(-0.19148f,-0.79703f,-0.57278f), new Vector3(-0.16316f,-0.79576f,-0.58322f),
        new Vector3(-0.13483f,-0.79364f,-0.59326f), new Vector3(-0.10582f,-0.78677f,-0.60810f), new Vector3(-0.07621f,-0.77933f,-0.62196f), new Vector3(-0.04657f,-0.76754f,-0.63930f),
        new Vector3(-0.01539f,-0.75501f,-0.65554f), new Vector3(-0.01009f,-0.74142f,-0.67097f), new Vector3(-0.00176f,-0.72587f,-0.68783f), new Vector3(0.00069f,-0.70377f,-0.71043f),
        new Vector3(0.00777f,-0.67828f,-0.73476f),
    };

    static readonly Vector3[] LeftLegX = {
        new Vector3(0.35124f,0.49404f,-0.79533f), new Vector3(0.33753f,0.49846f,-0.79851f), new Vector3(0.32382f,0.50241f,-0.80170f), new Vector3(0.30079f,0.50749f,-0.80745f),
        new Vector3(0.27807f,0.51152f,-0.81303f), new Vector3(0.25066f,0.51746f,-0.81817f), new Vector3(0.22368f,0.52250f,-0.82278f), new Vector3(0.20327f,0.52840f,-0.82430f),
        new Vector3(0.18302f,0.53481f,-0.82491f), new Vector3(0.17091f,0.54258f,-0.82243f), new Vector3(0.15887f,0.55072f,-0.81943f), new Vector3(0.15958f,0.56207f,-0.81155f),
        new Vector3(0.16018f,0.57339f,-0.80347f), new Vector3(0.17582f,0.58710f,-0.79019f), new Vector3(0.19105f,0.60080f,-0.77623f), new Vector3(0.21910f,0.61656f,-0.75620f),
        new Vector3(0.24600f,0.63260f,-0.73437f), new Vector3(0.28969f,0.64006f,-0.71162f), new Vector3(0.33175f,0.64845f,-0.68517f), new Vector3(0.39174f,0.65384f,-0.64733f),
        new Vector3(0.44802f,0.65927f,-0.60385f),
    };

    static readonly Vector3[] LeftFootY = {
        new Vector3(0.70281f,-0.63844f,0.31378f), new Vector3(0.70773f,-0.62499f,0.32939f), new Vector3(0.71224f,-0.61128f,0.34505f), new Vector3(0.71298f,-0.61061f,0.34470f),
        new Vector3(0.71340f,-0.60991f,0.34506f), new Vector3(0.71487f,-0.60823f,0.34499f), new Vector3(0.71600f,-0.60652f,0.34565f), new Vector3(0.71728f,-0.60494f,0.34578f),
        new Vector3(0.71845f,-0.60367f,0.34556f), new Vector3(0.72103f,-0.60031f,0.34604f), new Vector3(0.72341f,-0.59735f,0.34620f), new Vector3(0.72806f,-0.59222f,0.34526f),
        new Vector3(0.73246f,-0.58722f,0.34450f), new Vector3(0.73731f,-0.58307f,0.34117f), new Vector3(0.74217f,-0.57855f,0.33832f), new Vector3(0.74785f,-0.57472f,0.33230f),
        new Vector3(0.75415f,-0.56955f,0.32691f), new Vector3(0.74483f,-0.57834f,0.33279f), new Vector3(0.73637f,-0.58576f,0.33860f), new Vector3(0.70743f,-0.60065f,0.37252f),
        new Vector3(0.67666f,-0.61528f,0.40443f),
    };

    static readonly Vector3[] LeftFootX = {
        new Vector3(0.35911f,-0.06234f,-0.93121f), new Vector3(0.36853f,-0.07118f,-0.92689f), new Vector3(0.37834f,-0.07974f,-0.92223f), new Vector3(0.37802f,-0.07931f,-0.92239f),
        new Vector3(0.37809f,-0.07957f,-0.92234f), new Vector3(0.37832f,-0.07850f,-0.92234f), new Vector3(0.37919f,-0.07782f,-0.92204f), new Vector3(0.37802f,-0.07903f,-0.92242f),
        new Vector3(0.37727f,-0.07919f,-0.92271f), new Vector3(0.37662f,-0.07965f,-0.92294f), new Vector3(0.37621f,-0.07941f,-0.92312f), new Vector3(0.37610f,-0.07600f,-0.92346f),
        new Vector3(0.37614f,-0.07273f,-0.92371f), new Vector3(0.37723f,-0.06361f,-0.92393f), new Vector3(0.37858f,-0.05466f,-0.92396f), new Vector3(0.38512f,-0.03214f,-0.92231f),
        new Vector3(0.39193f,-0.00906f,-0.91995f), new Vector3(0.42782f,0.03119f,-0.90332f), new Vector3(0.46426f,0.07341f,-0.88265f), new Vector3(0.53233f,0.10609f,-0.83986f),
        new Vector3(0.59800f,0.13878f,-0.78939f),
    };

    static readonly Vector3[] LeftToeY = {
        new Vector3(0.92552f,0.09886f,0.36558f), new Vector3(0.92131f,0.11076f,0.37273f), new Vector3(0.91678f,0.12270f,0.38007f), new Vector3(0.91689f,0.12307f,0.37970f),
        new Vector3(0.91686f,0.12342f,0.37965f), new Vector3(0.91656f,0.12443f,0.38006f), new Vector3(0.91600f,0.12546f,0.38106f), new Vector3(0.91637f,0.12584f,0.38005f),
        new Vector3(0.91654f,0.12589f,0.37960f), new Vector3(0.91645f,0.12883f,0.37885f), new Vector3(0.91626f,0.13130f,0.37845f), new Vector3(0.91554f,0.13718f,0.37811f),
        new Vector3(0.91474f,0.14283f,0.37797f), new Vector3(0.91314f,0.14943f,0.37927f), new Vector3(0.91130f,0.15638f,0.38089f), new Vector3(0.90593f,0.16691f,0.38914f),
        new Vector3(0.89977f,0.17856f,0.39816f), new Vector3(0.88279f,0.16540f,0.43969f), new Vector3(0.86296f,0.15232f,0.48177f), new Vector3(0.82249f,0.13209f,0.55323f),
        new Vector3(0.77508f,0.11085f,0.62207f),
    };

    static readonly Vector3[] LeftToeX = {
        new Vector3(0.37639f,-0.13349f,-0.91679f), new Vector3(0.38582f,-0.14112f,-0.91172f), new Vector3(0.39561f,-0.14847f,-0.90634f), new Vector3(0.39527f,-0.14777f,-0.90660f),
        new Vector3(0.39529f,-0.14775f,-0.90660f), new Vector3(0.39583f,-0.14699f,-0.90648f), new Vector3(0.39700f,-0.14662f,-0.90603f), new Vector3(0.39617f,-0.14836f,-0.90611f),
        new Vector3(0.39578f,-0.14904f,-0.90617f), new Vector3(0.39564f,-0.14996f,-0.90608f), new Vector3(0.39574f,-0.15019f,-0.90600f), new Vector3(0.39631f,-0.14699f,-0.90627f),
        new Vector3(0.39702f,-0.14394f,-0.90645f), new Vector3(0.39880f,-0.13478f,-0.90708f), new Vector3(0.40087f,-0.12577f,-0.90746f), new Vector3(0.40853f,-0.10299f,-0.90692f),
        new Vector3(0.41655f,-0.07958f,-0.90562f), new Vector3(0.45121f,-0.03808f,-0.89161f), new Vector3(0.48670f,0.00556f,-0.87355f), new Vector3(0.55338f,0.03899f,-0.83202f),
        new Vector3(0.61794f,0.07254f,-0.78287f),
    };

    static readonly Vector3[] RightUpLegY = {
        new Vector3(0.03377f,-0.74944f,0.66121f), new Vector3(0.05799f,-0.72220f,0.68925f), new Vector3(0.08179f,-0.69330f,0.71599f), new Vector3(0.08672f,-0.66197f,0.74450f),
        new Vector3(0.09088f,-0.62942f,0.77173f), new Vector3(0.07937f,-0.60446f,0.79267f), new Vector3(0.06886f,-0.57840f,0.81284f), new Vector3(0.03993f,-0.58989f,0.80650f),
        new Vector3(0.01112f,-0.59666f,0.80242f), new Vector3(-0.02134f,-0.61174f,0.79077f), new Vector3(-0.05435f,-0.62271f,0.78056f), new Vector3(-0.07815f,-0.62821f,0.77411f),
        new Vector3(-0.10190f,-0.63267f,0.76769f), new Vector3(-0.10878f,-0.63053f,0.76850f), new Vector3(-0.11580f,-0.62835f,0.76927f), new Vector3(-0.10889f,-0.60582f,0.78811f),
        new Vector3(-0.10520f,-0.58003f,0.80777f), new Vector3(-0.12215f,-0.55893f,0.82017f), new Vector3(-0.14180f,-0.53453f,0.83317f), new Vector3(-0.15628f,-0.51971f,0.83993f),
        new Vector3(-0.17294f,-0.50140f,0.84776f),
    };

    static readonly Vector3[] RightUpLegX = {
        new Vector3(0.83313f,-0.34432f,-0.43283f), new Vector3(0.84388f,-0.33341f,-0.42035f), new Vector3(0.85420f,-0.32135f,-0.40874f), new Vector3(0.86561f,-0.31985f,-0.38523f),
        new Vector3(0.87668f,-0.31705f,-0.36182f), new Vector3(0.87152f,-0.34392f,-0.34953f), new Vector3(0.86593f,-0.36995f,-0.33661f), new Vector3(0.83053f,-0.42916f,-0.35502f),
        new Vector3(0.79009f,-0.48662f,-0.37278f), new Vector3(0.75391f,-0.52933f,-0.38915f), new Vector3(0.71424f,-0.57052f,-0.40542f), new Vector3(0.69402f,-0.59171f,-0.41013f),
        new Vector3(0.67290f,-0.61221f,-0.41522f), new Vector3(0.68206f,-0.60975f,-0.40373f), new Vector3(0.69097f,-0.60733f,-0.39206f), new Vector3(0.73243f,-0.58492f,-0.34844f),
        new Vector3(0.77021f,-0.56135f,-0.30278f), new Vector3(0.81542f,-0.52762f,-0.23812f), new Vector3(0.85363f,-0.49219f,-0.17048f), new Vector3(0.88325f,-0.45416f,-0.11667f),
        new Vector3(0.90736f,-0.41592f,-0.06090f),
    };

    static readonly Vector3[] RightLegY = {
        new Vector3(-0.55694f,-0.78537f,-0.27020f), new Vector3(-0.54962f,-0.78578f,-0.28368f), new Vector3(-0.54146f,-0.78639f,-0.29733f), new Vector3(-0.53980f,-0.77605f,-0.32612f),
        new Vector3(-0.53555f,-0.76606f,-0.35543f), new Vector3(-0.56115f,-0.72509f,-0.39919f), new Vector3(-0.58082f,-0.68293f,-0.44301f), new Vector3(-0.63462f,-0.62893f,-0.44912f),
        new Vector3(-0.68464f,-0.57250f,-0.45111f), new Vector3(-0.72335f,-0.55008f,-0.41735f), new Vector3(-0.76073f,-0.52584f,-0.38051f), new Vector3(-0.77825f,-0.52129f,-0.35013f),
        new Vector3(-0.79509f,-0.51583f,-0.31899f), new Vector3(-0.78812f,-0.52606f,-0.31958f), new Vector3(-0.78115f,-0.53610f,-0.31999f), new Vector3(-0.74416f,-0.56905f,-0.34986f),
        new Vector3(-0.70510f,-0.60100f,-0.37633f), new Vector3(-0.65024f,-0.64610f,-0.39968f), new Vector3(-0.59354f,-0.68941f,-0.41524f), new Vector3(-0.54022f,-0.72821f,-0.42175f),
        new Vector3(-0.48687f,-0.76509f,-0.42141f),
    };

    static readonly Vector3[] RightLegX = {
        new Vector3(0.78578f,-0.39289f,-0.47770f), new Vector3(0.79557f,-0.38868f,-0.46475f), new Vector3(0.80519f,-0.38333f,-0.45247f), new Vector3(0.81654f,-0.38855f,-0.42695f),
        new Vector3(0.82762f,-0.39236f,-0.40136f), new Vector3(0.82059f,-0.42424f,-0.38294f), new Vector3(0.81268f,-0.45514f,-0.36386f), new Vector3(0.77279f,-0.51077f,-0.37672f),
        new Vector3(0.72816f,-0.56466f,-0.38852f), new Vector3(0.68991f,-0.60037f,-0.40444f), new Vector3(0.64874f,-0.63457f,-0.42007f), new Vector3(0.62793f,-0.65086f,-0.42671f),
        new Vector3(0.60639f,-0.66645f,-0.43374f), new Vector3(0.61542f,-0.66391f,-0.42484f), new Vector3(0.62424f,-0.66142f,-0.41575f), new Vector3(0.66785f,-0.64468f,-0.37196f),
        new Vector3(0.70797f,-0.62662f,-0.32576f), new Vector3(0.75675f,-0.59736f,-0.26550f), new Vector3(0.79903f,-0.56648f,-0.20162f), new Vector3(0.83360f,-0.53169f,-0.14973f),
        new Vector3(0.86275f,-0.49658f,-0.09521f),
    };

    static readonly Vector3[] RightFootY = {
        new Vector3(0.41550f,-0.61884f,0.66663f), new Vector3(0.39462f,-0.61549f,0.68224f), new Vector3(0.37431f,-0.61229f,0.69641f), new Vector3(0.31033f,-0.60845f,0.73040f),
        new Vector3(0.24511f,-0.60285f,0.75927f), new Vector3(0.11397f,-0.58971f,0.79953f), new Vector3(-0.01805f,-0.56176f,0.82710f), new Vector3(-0.13045f,-0.54959f,0.82519f),
        new Vector3(-0.23723f,-0.51497f,0.82373f), new Vector3(-0.26168f,-0.50757f,0.82091f), new Vector3(-0.28314f,-0.49914f,0.81896f), new Vector3(-0.24678f,-0.51547f,0.82060f),
        new Vector3(-0.20583f,-0.53087f,0.82208f), new Vector3(-0.14165f,-0.55971f,0.81649f), new Vector3(-0.07325f,-0.57864f,0.81229f), new Vector3(-0.06868f,-0.58606f,0.80735f),
        new Vector3(-0.06271f,-0.59105f,0.80419f), new Vector3(-0.06303f,-0.59369f,0.80222f), new Vector3(-0.06368f,-0.59294f,0.80272f), new Vector3(-0.06141f,-0.59740f,0.79959f),
        new Vector3(-0.06023f,-0.59805f,0.79920f),
    };

    static readonly Vector3[] RightFootX = {
        new Vector3(0.86291f,0.03641f,-0.50404f), new Vector3(0.87484f,0.02466f,-0.48378f), new Vector3(0.88587f,0.01414f,-0.46371f), new Vector3(0.91306f,-0.02310f,-0.40718f),
        new Vector3(0.93529f,-0.05915f,-0.34890f), new Vector3(0.95620f,-0.15331f,-0.24938f), new Vector3(0.95752f,-0.24783f,-0.14743f), new Vector3(0.91749f,-0.38236f,-0.10962f),
        new Vector3(0.85653f,-0.51095f,-0.07276f), new Vector3(0.85505f,-0.51643f,-0.04675f), new Vector3(0.85330f,-0.52093f,-0.02249f), new Vector3(0.90382f,-0.42791f,0.00301f),
        new Vector3(0.94404f,-0.32897f,0.02393f), new Vector3(0.97992f,-0.19619f,0.03552f), new Vector3(0.99709f,-0.05986f,0.04728f), new Vector3(0.99701f,-0.01164f,0.07636f),
        new Vector3(0.99387f,0.03654f,0.10436f), new Vector3(0.99376f,0.03678f,0.10530f), new Vector3(0.99381f,0.03570f,0.10520f), new Vector3(0.99364f,0.03920f,0.10561f),
        new Vector3(0.99359f,0.04083f,0.10543f),
    };

    static readonly Vector3[] RightToeY = {
        new Vector3(0.50247f,0.11237f,0.85726f), new Vector3(0.48300f,0.11574f,0.86794f), new Vector3(0.46382f,0.11877f,0.87793f), new Vector3(0.41103f,0.11851f,0.90389f),
        new Vector3(0.35695f,0.11935f,0.92647f), new Vector3(0.26878f,0.11614f,0.95617f), new Vector3(0.18014f,0.12398f,0.97580f), new Vector3(0.15546f,0.10339f,0.98242f),
        new Vector3(0.13186f,0.09436f,0.98677f), new Vector3(0.10250f,0.09088f,0.99057f), new Vector3(0.07574f,0.08913f,0.99314f), new Vector3(0.04865f,0.10126f,0.99367f),
        new Vector3(0.02441f,0.10761f,0.99389f), new Vector3(0.00969f,0.10788f,0.99412f), new Vector3(-0.00471f,0.10895f,0.99404f), new Vector3(-0.02749f,0.12646f,0.99159f),
        new Vector3(-0.05100f,0.14603f,0.98797f), new Vector3(-0.05233f,0.14289f,0.98835f), new Vector3(-0.05253f,0.14387f,0.98820f), new Vector3(-0.05371f,0.13857f,0.98890f),
        new Vector3(-0.05415f,0.13790f,0.98896f),
    };

    static readonly Vector3[] RightToeX = {
        new Vector3(0.85200f,0.10426f,-0.51305f), new Vector3(0.86481f,0.09218f,-0.49356f), new Vector3(0.87668f,0.08132f,-0.47416f), new Vector3(0.90738f,0.04238f,-0.41817f),
        new Vector3(0.93293f,0.00453f,-0.36003f), new Vector3(0.96131f,-0.09447f,-0.25875f), new Vector3(0.96874f,-0.19435f,-0.15414f), new Vector3(0.93550f,-0.33483f,-0.11280f),
        new Vector3(0.87964f,-0.47007f,-0.07260f), new Vector3(0.87839f,-0.47560f,-0.04726f), new Vector3(0.87687f,-0.48014f,-0.02379f), new Vector3(0.92471f,-0.38061f,-0.00649f),
        new Vector3(0.96131f,-0.27539f,0.00621f), new Vector3(0.99097f,-0.13400f,0.00489f), new Vector3(0.99994f,0.01012f,0.00362f), new Vector3(0.99789f,0.06191f,0.01977f),
        new Vector3(0.99297f,0.11320f,0.03452f), new Vector3(0.99295f,0.11286f,0.03626f), new Vector3(0.99315f,0.11091f,0.03665f), new Vector3(0.99274f,0.11414f,0.03793f),
        new Vector3(0.99260f,0.11519f,0.03829f),
    };

    static readonly Vector3[] Spine02Y = {
        new Vector3(0.25620f,0.96625f,0.02671f), new Vector3(0.29276f,0.95532f,0.04075f), new Vector3(0.32906f,0.94277f,0.05390f), new Vector3(0.36152f,0.92944f,0.07381f),
        new Vector3(0.39393f,0.91451f,0.09217f), new Vector3(0.41207f,0.90518f,0.10414f), new Vector3(0.43027f,0.89539f,0.11466f), new Vector3(0.43314f,0.89572f,0.10036f),
        new Vector3(0.43473f,0.89651f,0.08534f), new Vector3(0.42965f,0.89975f,0.07657f), new Vector3(0.42406f,0.90308f,0.06791f), new Vector3(0.41463f,0.90702f,0.07340f),
        new Vector3(0.40519f,0.91082f,0.07897f), new Vector3(0.39311f,0.91403f,0.10006f), new Vector3(0.38012f,0.91704f,0.12065f), new Vector3(0.35804f,0.92113f,0.15271f),
        new Vector3(0.33315f,0.92497f,0.18290f), new Vector3(0.30109f,0.92911f,0.21469f), new Vector3(0.26452f,0.93353f,0.24199f), new Vector3(0.22595f,0.93435f,0.27557f),
        new Vector3(0.18263f,0.93519f,0.30343f),
    };

    static readonly Vector3[] Spine02X = {
        new Vector3(0.43981f,-0.09192f,-0.89338f), new Vector3(0.41537f,-0.08867f,-0.90532f), new Vector3(0.39081f,-0.08400f,-0.91663f), new Vector3(0.35408f,-0.06363f,-0.93305f),
        new Vector3(0.31738f,-0.04123f,-0.94740f), new Vector3(0.26624f,-0.01031f,-0.96385f), new Vector3(0.21460f,0.02191f,-0.97646f), new Vector3(0.14613f,0.04008f,-0.98845f),
        new Vector3(0.07678f,0.05752f,-0.99539f), new Vector3(0.03118f,0.06996f,-0.99706f), new Vector3(-0.01460f,0.08180f,-0.99654f), new Vector3(-0.02087f,0.09012f,-0.99571f),
        new Vector3(-0.02723f,0.09836f,-0.99478f), new Vector3(0.00985f,0.10463f,-0.99446f), new Vector3(0.04680f,0.11120f,-0.99270f), new Vector3(0.12137f,0.11626f,-0.98578f),
        new Vector3(0.19500f,0.12220f,-0.97316f), new Vector3(0.30703f,0.11869f,-0.94427f), new Vector3(0.41469f,0.11644f,-0.90248f), new Vector3(0.52758f,0.12043f,-0.84092f),
        new Vector3(0.63140f,0.12501f,-0.76532f),
    };

    static readonly Vector3[] Spine01Y = {
        new Vector3(0.29735f,0.95473f,-0.00902f), new Vector3(0.33281f,0.94290f,-0.01351f), new Vector3(0.36674f,0.93013f,-0.01887f), new Vector3(0.40637f,0.91296f,-0.03704f),
        new Vector3(0.44131f,0.89549f,-0.05787f), new Vector3(0.46946f,0.87920f,-0.08136f), new Vector3(0.49184f,0.86384f,-0.10895f), new Vector3(0.50476f,0.85530f,-0.11693f),
        new Vector3(0.51725f,0.84605f,-0.12909f), new Vector3(0.51774f,0.84573f,-0.12917f), new Vector3(0.51864f,0.84501f,-0.13027f), new Vector3(0.51381f,0.84967f,-0.11853f),
        new Vector3(0.50902f,0.85411f,-0.10676f), new Vector3(0.50202f,0.86093f,-0.08226f), new Vector3(0.49394f,0.86755f,-0.05815f), new Vector3(0.48123f,0.87636f,-0.02020f),
        new Vector3(0.46541f,0.88495f,0.01618f), new Vector3(0.43878f,0.89518f,0.07829f), new Vector3(0.40211f,0.90571f,0.13418f), new Vector3(0.35130f,0.91672f,0.19028f),
        new Vector3(0.29236f,0.92705f,0.23475f),
    };

    static readonly Vector3[] Spine01X = {
        new Vector3(0.40553f,-0.13484f,-0.90408f), new Vector3(0.35782f,-0.13953f,-0.92331f), new Vector3(0.30890f,-0.14088f,-0.94060f), new Vector3(0.22887f,-0.14095f,-0.96320f),
        new Vector3(0.14661f,-0.13557f,-0.97986f), new Vector3(0.04398f,-0.11532f,-0.99235f), new Vector3(-0.05940f,-0.09155f,-0.99403f), new Vector3(-0.14202f,-0.05133f,-0.98853f),
        new Vector3(-0.22361f,-0.01200f,-0.97461f), new Vector3(-0.26536f,0.01521f,-0.96403f), new Vector3(-0.30674f,0.04168f,-0.95088f), new Vector3(-0.30752f,0.05343f,-0.95004f),
        new Vector3(-0.30847f,0.06522f,-0.94899f), new Vector3(-0.26872f,0.06487f,-0.96103f), new Vector3(-0.22845f,0.06496f,-0.97139f), new Vector3(-0.14624f,0.05754f,-0.98757f),
        new Vector3(-0.06276f,0.05123f,-0.99671f), new Vector3(0.07908f,0.04832f,-0.99570f), new Vector3(0.21909f,0.04711f,-0.97457f), new Vector3(0.37024f,0.05065f,-0.92755f),
        new Vector3(0.51192f,0.05563f,-0.85723f),
    };

    // 0 フレーム目の姿勢 = 差分の基準。配列より後に置くこと (宣言順に初期化されるため)。
    static readonly Quaternion LeftUpLegRef = Basis(LeftUpLegY[0], LeftUpLegX[0]);
    static readonly Quaternion LeftLegRef = Basis(LeftLegY[0], LeftLegX[0]);
    static readonly Quaternion LeftFootRef = Basis(LeftFootY[0], LeftFootX[0]);
    static readonly Quaternion LeftToeRef = Basis(LeftToeY[0], LeftToeX[0]);
    static readonly Quaternion RightUpLegRef = Basis(RightUpLegY[0], RightUpLegX[0]);
    static readonly Quaternion RightLegRef = Basis(RightLegY[0], RightLegX[0]);
    static readonly Quaternion RightFootRef = Basis(RightFootY[0], RightFootX[0]);
    static readonly Quaternion RightToeRef = Basis(RightToeY[0], RightToeX[0]);
    static readonly Quaternion Spine02Ref = Basis(Spine02Y[0], Spine02X[0]);
    static readonly Quaternion Spine01Ref = Basis(Spine01Y[0], Spine01X[0]);
}
