using UnityEngine;

// 歩行 1 周期の焼き込みデータ。GoblinCarryRig.ApplyWalkCycle が毎フレーム引く。
// 生成元: Slow_Orc_Walk フレーム 80〜160 (81 フレーム / 3.38 秒) -- bake_carry_walk.py
//
// 2026-08-24 全面差し替え。旧データは自作カーブを機械的に増幅したもので、腰が固定・
// 上半身が直立のままの「脚だけが歩く」動きだった。今回は既製の重量歩行から 1 周期を
// 切り出し、ルートモーションを抜いてループ化している。切り出し位置は find_loop.py が
// 「ゲームが実際に使う骨だけ」で繋ぎ目を最小化して選んだもの。
//
// 手・肘・指は入っていない: そこは SolveArm の IK が壺の位置から解くので上書きされる。
// 肩は IK より前に適用されるため入っている (腕の振りの根元)。
public static class GoblinWalk
{
    public const int FrameCount = 81;

    /// <summary>クリップ内の足の最低 Y。腰の位置はここを 0 として返す。</summary>
    public const float GroundY = 0.13363f;

    /// <summary>1 周期 (= 2 歩) で進む距離 [m]。walkStrideRefSpeed の根拠。
    /// walkStrideRefSpeed = StrideDistance / walkCycleDuration にすると足が滑らない。</summary>
    public const float StrideDistance = 0.07212f;

    public static void SampleHips(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(HipsWalkYDir, phase01); xDir = Sample(HipsWalkXDir, phase01); }
    public static void SampleLeftUpLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftUpLegWalkYDir, phase01); xDir = Sample(LegLeftUpLegWalkXDir, phase01); }
    public static void SampleLeftLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftLegWalkYDir, phase01); xDir = Sample(LegLeftLegWalkXDir, phase01); }
    public static void SampleLeftFoot(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftFootWalkYDir, phase01); xDir = Sample(LegLeftFootWalkXDir, phase01); }
    public static void SampleLeftToe(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegLeftToeWalkYDir, phase01); xDir = Sample(LegLeftToeWalkXDir, phase01); }
    public static void SampleRightUpLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightUpLegWalkYDir, phase01); xDir = Sample(LegRightUpLegWalkXDir, phase01); }
    public static void SampleRightLeg(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightLegWalkYDir, phase01); xDir = Sample(LegRightLegWalkXDir, phase01); }
    public static void SampleRightFoot(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightFootWalkYDir, phase01); xDir = Sample(LegRightFootWalkXDir, phase01); }
    public static void SampleRightToe(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LegRightToeWalkYDir, phase01); xDir = Sample(LegRightToeWalkXDir, phase01); }
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
    public static void SampleLeftShoulder(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(LeftShoulderWalkYDir, phase01); xDir = Sample(LeftShoulderWalkXDir, phase01); }
    public static void SampleRightShoulder(float phase01, out Vector3 yDir, out Vector3 xDir)
    { yDir = Sample(RightShoulderWalkYDir, phase01); xDir = Sample(RightShoulderWalkXDir, phase01); }

    /// <summary>骨盤の root ローカル位置 (接地正規化済み)。一歩ごとの沈み込みと左右移動。</summary>
    public static Vector3 SampleHipsPos(float phase01)
    {
        Vector3 p = SamplePos(HipsWalkPos, phase01);
        p.y -= GroundY;
        return p;
    }

    static Vector3 Sample(Vector3[] frames, float phase01)
    {
        phase01 = Mathf.Repeat(phase01, 1f);
        float f = phase01 * frames.Length;
        int i0 = Mathf.FloorToInt(f) % frames.Length;
        int i1 = (i0 + 1) % frames.Length;
        return Vector3.Slerp(frames[i0], frames[i1], f - Mathf.Floor(f)).normalized;
    }

    static Vector3 SamplePos(Vector3[] frames, float phase01)
    {
        phase01 = Mathf.Repeat(phase01, 1f);
        float f = phase01 * frames.Length;
        int i0 = Mathf.FloorToInt(f) % frames.Length;
        int i1 = (i0 + 1) % frames.Length;
        return Vector3.Lerp(frames[i0], frames[i1], f - Mathf.Floor(f));
    }

    // ---- 歩容の変調 (よろけの浅い段) 用 ----
    // よろけているときは歩幅を詰めて小刻みにする。脚の向きを **1 周期の平均姿勢** へ
    // 寄せると、振り幅そのものが縮んで歩幅が短くなる (位相を速めるだけだと足が滑る)。
    public static void ShrinkStride(float k, ref Vector3 y, ref Vector3 x, Vector3 my, Vector3 mx)
    {
        if (k <= 0.001f) return;
        y = Vector3.Slerp(y, my, k).normalized;
        x = Vector3.Slerp(x, mx, k).normalized;
    }

    public static void MeanLeftUpLeg(out Vector3 yDir, out Vector3 xDir)
    { yDir = LegLeftUpLegMeanY; xDir = LegLeftUpLegMeanX; }
    public static void MeanLeftLeg(out Vector3 yDir, out Vector3 xDir)
    { yDir = LegLeftLegMeanY; xDir = LegLeftLegMeanX; }
    public static void MeanLeftFoot(out Vector3 yDir, out Vector3 xDir)
    { yDir = LegLeftFootMeanY; xDir = LegLeftFootMeanX; }
    public static void MeanLeftToe(out Vector3 yDir, out Vector3 xDir)
    { yDir = LegLeftToeMeanY; xDir = LegLeftToeMeanX; }
    public static void MeanRightUpLeg(out Vector3 yDir, out Vector3 xDir)
    { yDir = LegRightUpLegMeanY; xDir = LegRightUpLegMeanX; }
    public static void MeanRightLeg(out Vector3 yDir, out Vector3 xDir)
    { yDir = LegRightLegMeanY; xDir = LegRightLegMeanX; }
    public static void MeanRightFoot(out Vector3 yDir, out Vector3 xDir)
    { yDir = LegRightFootMeanY; xDir = LegRightFootMeanX; }
    public static void MeanRightToe(out Vector3 yDir, out Vector3 xDir)
    { yDir = LegRightToeMeanY; xDir = LegRightToeMeanX; }

    static readonly Vector3[] HipsWalkPos = {
        new Vector3(-0.01303f,0.86783f,0.01037f), new Vector3(-0.02044f,0.85863f,0.00940f), new Vector3(-0.02668f,0.85738f,0.01223f), new Vector3(-0.03221f,0.86134f,0.01274f),
        new Vector3(-0.03745f,0.86914f,0.01285f), new Vector3(-0.04290f,0.87938f,0.01262f), new Vector3(-0.04868f,0.89091f,0.01258f), new Vector3(-0.05370f,0.90417f,0.01222f),
        new Vector3(-0.05851f,0.91737f,0.01170f), new Vector3(-0.06295f,0.93017f,0.01096f), new Vector3(-0.06679f,0.94221f,0.00962f), new Vector3(-0.06982f,0.95315f,0.00706f),
        new Vector3(-0.07228f,0.96333f,0.00326f), new Vector3(-0.07399f,0.97251f,-0.00055f), new Vector3(-0.07527f,0.98080f,-0.00425f), new Vector3(-0.07606f,0.98819f,-0.00795f),
        new Vector3(-0.07598f,0.99461f,-0.01205f), new Vector3(-0.07548f,0.99959f,-0.01547f), new Vector3(-0.07485f,1.00357f,-0.01876f), new Vector3(-0.07398f,1.00660f,-0.02151f),
        new Vector3(-0.07286f,1.00880f,-0.02369f), new Vector3(-0.07154f,1.01011f,-0.02531f), new Vector3(-0.07012f,1.01142f,-0.02618f), new Vector3(-0.06898f,1.01108f,-0.02655f),
        new Vector3(-0.06792f,1.01075f,-0.02638f), new Vector3(-0.06676f,1.01042f,-0.02551f), new Vector3(-0.06531f,1.01009f,-0.02384f), new Vector3(-0.06347f,1.00976f,-0.02301f),
        new Vector3(-0.06113f,1.00942f,-0.02086f), new Vector3(-0.05851f,1.00909f,-0.01881f), new Vector3(-0.05539f,1.00876f,-0.01787f), new Vector3(-0.05166f,1.00690f,-0.01625f),
        new Vector3(-0.04717f,1.00445f,-0.01415f), new Vector3(-0.04215f,1.00042f,-0.01261f), new Vector3(-0.03652f,0.99423f,-0.01083f), new Vector3(-0.03027f,0.98518f,-0.00774f),
        new Vector3(-0.02349f,0.97276f,-0.00502f), new Vector3(-0.01531f,0.95364f,-0.00070f), new Vector3(-0.00682f,0.92972f,0.00406f), new Vector3(0.00091f,0.90428f,0.00927f),
        new Vector3(0.00766f,0.88009f,0.01458f), new Vector3(0.01349f,0.86280f,0.01853f), new Vector3(0.01982f,0.85435f,0.02071f), new Vector3(0.02547f,0.85168f,0.02504f),
        new Vector3(0.03067f,0.85361f,0.02978f), new Vector3(0.03582f,0.85891f,0.03355f), new Vector3(0.04152f,0.86623f,0.03514f), new Vector3(0.04787f,0.87477f,0.03424f),
        new Vector3(0.05417f,0.88358f,0.03269f), new Vector3(0.06044f,0.89261f,0.03077f), new Vector3(0.06681f,0.90255f,0.02863f), new Vector3(0.07273f,0.91220f,0.02621f),
        new Vector3(0.07780f,0.92149f,0.02224f), new Vector3(0.08195f,0.93063f,0.01910f), new Vector3(0.08514f,0.93947f,0.01590f), new Vector3(0.08722f,0.94793f,0.01255f),
        new Vector3(0.08808f,0.95611f,0.00924f), new Vector3(0.08773f,0.96347f,0.00627f), new Vector3(0.08674f,0.97038f,0.00315f), new Vector3(0.08521f,0.97679f,-0.00014f),
        new Vector3(0.08326f,0.98265f,-0.00317f), new Vector3(0.08108f,0.98787f,-0.00511f), new Vector3(0.07833f,0.99241f,-0.00833f), new Vector3(0.07543f,0.99602f,-0.01056f),
        new Vector3(0.07237f,0.99903f,-0.01212f), new Vector3(0.06922f,1.00163f,-0.01258f), new Vector3(0.06608f,1.00375f,-0.01370f), new Vector3(0.06176f,1.00554f,-0.01305f),
        new Vector3(0.05741f,1.00695f,-0.01282f), new Vector3(0.05321f,1.00835f,-0.01273f), new Vector3(0.04897f,1.00922f,-0.01275f), new Vector3(0.04418f,1.01008f,-0.01232f),
        new Vector3(0.03961f,1.00938f,-0.01102f), new Vector3(0.03446f,1.00868f,-0.00964f), new Vector3(0.02891f,1.00629f,-0.00767f), new Vector3(0.02330f,1.00172f,-0.00599f),
        new Vector3(0.01828f,0.99401f,-0.00447f), new Vector3(0.01278f,0.97957f,-0.00331f), new Vector3(0.00651f,0.96076f,-0.00177f), new Vector3(0.00032f,0.93868f,0.00250f),
        new Vector3(-0.00628f,0.91451f,0.00734f),
    };

    // ---- 上半身は「加算」で乗せる ----
    // 元クリップは前かがみの重い歩き。その姿勢を絶対値で入れると、壺を頭上に担いだ
    // BasePose の立ち姿勢を丸ごと押し潰してしまう (実測で上体が 60 度以上倒れた)。
    // ここではクリップの **平均姿勢からのズレ** だけを取り出し、担ぎ姿勢の上に足す。
    // 結果: 担ぎ姿勢は保ったまま、歩行由来の上体のうねり・肩線の傾き・頭の遅れが乗る。

    /// <summary>Y をボーンの軸、X を捻りの基準として回転を組む (BlendAimFull と同じ規約)。</summary>
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

    /// <summary>Spine の平均姿勢からのズレ。BasePose の向きに掛けて使う。</summary>
    public static Quaternion SampleSpineAdd(float phase01)
    { return Basis(Sample(SpineWalkYDir, phase01), Sample(SpineWalkXDir, phase01)) * Quaternion.Inverse(SpineMean); }
    /// <summary>Spine01 の平均姿勢からのズレ。BasePose の向きに掛けて使う。</summary>
    public static Quaternion SampleSpine01Add(float phase01)
    { return Basis(Sample(Spine01WalkYDir, phase01), Sample(Spine01WalkXDir, phase01)) * Quaternion.Inverse(Spine01Mean); }
    /// <summary>Spine02 の平均姿勢からのズレ。BasePose の向きに掛けて使う。</summary>
    public static Quaternion SampleSpine02Add(float phase01)
    { return Basis(Sample(Spine02WalkYDir, phase01), Sample(Spine02WalkXDir, phase01)) * Quaternion.Inverse(Spine02Mean); }
    /// <summary>Neck の平均姿勢からのズレ。BasePose の向きに掛けて使う。</summary>
    public static Quaternion SampleNeckAdd(float phase01)
    { return Basis(Sample(NeckWalkYDir, phase01), Sample(NeckWalkXDir, phase01)) * Quaternion.Inverse(NeckMean); }
    /// <summary>Head の平均姿勢からのズレ。BasePose の向きに掛けて使う。</summary>
    public static Quaternion SampleHeadAdd(float phase01)
    { return Basis(Sample(HeadWalkYDir, phase01), Sample(HeadWalkXDir, phase01)) * Quaternion.Inverse(HeadMean); }
    /// <summary>LeftShoulder の平均姿勢からのズレ。BasePose の向きに掛けて使う。</summary>
    public static Quaternion SampleLeftShoulderAdd(float phase01)
    { return Basis(Sample(LeftShoulderWalkYDir, phase01), Sample(LeftShoulderWalkXDir, phase01)) * Quaternion.Inverse(LeftShoulderMean); }
    /// <summary>RightShoulder の平均姿勢からのズレ。BasePose の向きに掛けて使う。</summary>
    public static Quaternion SampleRightShoulderAdd(float phase01)
    { return Basis(Sample(RightShoulderWalkYDir, phase01), Sample(RightShoulderWalkXDir, phase01)) * Quaternion.Inverse(RightShoulderMean); }


    static readonly Vector3[] HipsWalkYDir = {
        new Vector3(-0.02912f,0.99731f,0.06726f), new Vector3(-0.05137f,0.99533f,0.08174f), new Vector3(-0.06530f,0.99177f,0.11014f), new Vector3(-0.08831f,0.98799f,0.12683f),
        new Vector3(-0.11664f,0.98381f,0.13607f), new Vector3(-0.14525f,0.97911f,0.14228f), new Vector3(-0.16978f,0.97447f,0.14693f), new Vector3(-0.19802f,0.96877f,0.14922f),
        new Vector3(-0.22390f,0.96329f,0.14814f), new Vector3(-0.24684f,0.95825f,0.14433f), new Vector3(-0.26642f,0.95395f,0.13781f), new Vector3(-0.28234f,0.95074f,0.12800f),
        new Vector3(-0.29478f,0.94898f,0.11201f), new Vector3(-0.30816f,0.94657f,0.09508f), new Vector3(-0.32148f,0.94379f,0.07688f), new Vector3(-0.33428f,0.94076f,0.05683f),
        new Vector3(-0.34680f,0.93732f,0.03412f), new Vector3(-0.35039f,0.93652f,0.01277f), new Vector3(-0.35409f,0.93515f,-0.01062f), new Vector3(-0.35647f,0.93372f,-0.03315f),
        new Vector3(-0.35743f,0.93233f,-0.05481f), new Vector3(-0.35625f,0.93145f,-0.07413f), new Vector3(-0.35347f,0.93085f,-0.09266f), new Vector3(-0.34806f,0.93099f,-0.11008f),
        new Vector3(-0.34038f,0.93184f,-0.12579f), new Vector3(-0.33135f,0.93324f,-0.13881f), new Vector3(-0.32256f,0.93471f,-0.14927f), new Vector3(-0.31153f,0.93663f,-0.16021f),
        new Vector3(-0.29477f,0.94269f,-0.15634f), new Vector3(-0.27998f,0.94732f,-0.15558f), new Vector3(-0.26277f,0.95021f,-0.16750f), new Vector3(-0.24470f,0.95477f,-0.16894f),
        new Vector3(-0.22639f,0.96047f,-0.16199f), new Vector3(-0.20631f,0.96623f,-0.15440f), new Vector3(-0.18485f,0.97252f,-0.14151f), new Vector3(-0.16115f,0.97984f,-0.11813f),
        new Vector3(-0.13284f,0.98605f,-0.10029f), new Vector3(-0.10482f,0.99164f,-0.07518f), new Vector3(-0.07467f,0.99607f,-0.04759f), new Vector3(-0.04551f,0.99883f,-0.01652f),
        new Vector3(-0.02378f,0.99955f,0.01838f), new Vector3(-0.02015f,0.99835f,0.05376f), new Vector3(-0.00401f,0.99659f,0.08239f), new Vector3(0.01416f,0.99275f,0.11940f),
        new Vector3(0.03344f,0.98705f,0.15692f), new Vector3(0.05478f,0.98057f,0.18836f), new Vector3(0.08138f,0.97476f,0.20788f), new Vector3(0.11047f,0.97019f,0.21570f),
        new Vector3(0.13614f,0.96555f,0.22178f), new Vector3(0.15978f,0.96204f,0.22126f), new Vector3(0.17991f,0.95933f,0.21751f), new Vector3(0.19640f,0.95769f,0.21037f),
        new Vector3(0.21037f,0.95763f,0.19670f), new Vector3(0.22162f,0.95793f,0.18234f), new Vector3(0.22981f,0.95901f,0.16578f), new Vector3(0.23541f,0.96084f,0.14621f),
        new Vector3(0.23935f,0.96294f,0.12436f), new Vector3(0.24133f,0.96494f,0.10319f), new Vector3(0.24036f,0.96748f,0.07883f), new Vector3(0.23625f,0.97021f,0.05365f),
        new Vector3(0.22986f,0.97277f,0.02975f), new Vector3(0.22330f,0.97471f,0.00828f), new Vector3(0.21425f,0.97661f,-0.01800f), new Vector3(0.20346f,0.97822f,-0.04100f),
        new Vector3(0.19307f,0.97915f,-0.06312f), new Vector3(0.18365f,0.98021f,-0.07387f), new Vector3(0.17251f,0.98078f,-0.09117f), new Vector3(0.16097f,0.98201f,-0.09871f),
        new Vector3(0.14949f,0.98304f,-0.10623f), new Vector3(0.13792f,0.98391f,-0.11357f), new Vector3(0.12467f,0.98459f,-0.12264f), new Vector3(0.11001f,0.98563f,-0.12818f),
        new Vector3(0.09352f,0.98782f,-0.12436f), new Vector3(0.07744f,0.98990f,-0.11876f), new Vector3(0.06055f,0.99205f,-0.11030f), new Vector3(0.04314f,0.99421f,-0.09846f),
        new Vector3(0.02627f,0.99602f,-0.08521f), new Vector3(0.01025f,0.99774f,-0.06647f), new Vector3(-0.01165f,0.99862f,-0.05124f), new Vector3(-0.02413f,0.99946f,-0.02223f),
        new Vector3(-0.03546f,0.99936f,0.00433f),
    };

    static readonly Vector3[] HipsWalkXDir = {
        new Vector3(0.87123f,0.05831f,-0.48740f), new Vector3(0.87015f,0.08478f,-0.48544f), new Vector3(0.86665f,0.11108f,-0.48640f), new Vector3(0.86575f,0.13910f,-0.48076f),
        new Vector3(0.86577f,0.16786f,-0.47145f), new Vector3(0.86537f,0.19543f,-0.46146f), new Vector3(0.86410f,0.21889f,-0.45324f), new Vector3(0.85812f,0.24491f,-0.45127f),
        new Vector3(0.85338f,0.26719f,-0.44761f), new Vector3(0.85010f,0.28562f,-0.44243f), new Vector3(0.84854f,0.29995f,-0.43589f), new Vector3(0.84899f,0.30976f,-0.42810f),
        new Vector3(0.85327f,0.31418f,-0.41620f), new Vector3(0.85546f,0.31945f,-0.40760f), new Vector3(0.85712f,0.32454f,-0.40002f), new Vector3(0.85851f,0.32883f,-0.39348f),
        new Vector3(0.85890f,0.33198f,-0.38997f), new Vector3(0.86571f,0.32904f,-0.37720f), new Vector3(0.87271f,0.32632f,-0.36318f), new Vector3(0.87979f,0.32352f,-0.34828f),
        new Vector3(0.88684f,0.32041f,-0.33295f), new Vector3(0.89381f,0.31658f,-0.31761f), new Vector3(0.90167f,0.31266f,-0.29872f), new Vector3(0.91067f,0.30789f,-0.27547f),
        new Vector3(0.92054f,0.30296f,-0.24661f), new Vector3(0.93055f,0.29895f,-0.21142f), new Vector3(0.93959f,0.29709f,-0.17002f), new Vector3(0.94728f,0.29282f,-0.13009f),
        new Vector3(0.95507f,0.28533f,-0.08021f), new Vector3(0.95998f,0.27747f,-0.03802f), new Vector3(0.96340f,0.26792f,0.00849f), new Vector3(0.96483f,0.25704f,0.05517f),
        new Vector3(0.96466f,0.24411f,0.09918f), new Vector3(0.96294f,0.22851f,0.14326f), new Vector3(0.95957f,0.20971f,0.18775f), new Vector3(0.95447f,0.18517f,0.23386f),
        new Vector3(0.94602f,0.15632f,0.28393f), new Vector3(0.93552f,0.12397f,0.33080f), new Vector3(0.92351f,0.08708f,0.37357f), new Vector3(0.91262f,0.04830f,0.40596f),
        new Vector3(0.90593f,0.01377f,0.42320f), new Vector3(0.90669f,-0.00441f,0.42178f), new Vector3(0.90749f,-0.03098f,0.41893f), new Vector3(0.91055f,-0.06214f,0.40870f),
        new Vector3(0.91403f,-0.09371f,0.39468f), new Vector3(0.91692f,-0.12409f,0.37930f), new Vector3(0.91872f,-0.15423f,0.36353f), new Vector3(0.91956f,-0.18212f,0.34819f),
        new Vector3(0.91947f,-0.20650f,0.33458f), new Vector3(0.91934f,-0.22666f,0.32162f), new Vector3(0.91952f,-0.24256f,0.30926f), new Vector3(0.92060f,-0.25396f,0.29664f),
        new Vector3(0.92127f,-0.26151f,0.28787f), new Vector3(0.92284f,-0.26645f,0.27816f), new Vector3(0.92542f,-0.26806f,0.26784f), new Vector3(0.92890f,-0.26668f,0.25695f),
        new Vector3(0.93275f,-0.26361f,0.24596f), new Vector3(0.93645f,-0.25945f,0.23611f), new Vector3(0.94121f,-0.25215f,0.22483f), new Vector3(0.94724f,-0.24226f,0.20987f),
        new Vector3(0.95400f,-0.23126f,0.19082f), new Vector3(0.96029f,-0.22144f,0.16973f), new Vector3(0.96654f,-0.20930f,0.14828f), new Vector3(0.97258f,-0.19711f,0.12343f),
        new Vector3(0.97769f,-0.18656f,0.09658f), new Vector3(0.98155f,-0.17879f,0.06776f), new Vector3(0.98480f,-0.16985f,0.03626f), new Vector3(0.98685f,-0.16163f,0.00138f),
        new Vector3(0.98730f,-0.15425f,-0.03799f), new Vector3(0.98621f,-0.14701f,-0.07596f), new Vector3(0.98326f,-0.13915f,-0.11764f), new Vector3(0.97860f,-0.12997f,-0.15952f),
        new Vector3(0.97166f,-0.11779f,-0.20493f), new Vector3(0.96321f,-0.10503f,-0.24738f), new Vector3(0.95353f,-0.09017f,-0.28749f), new Vector3(0.94216f,-0.07327f,-0.32705f),
        new Vector3(0.92889f,-0.05582f,-0.36612f), new Vector3(0.91494f,-0.03618f,-0.40197f), new Vector3(0.90006f,-0.01185f,-0.43561f), new Vector3(0.88588f,0.01107f,-0.46379f),
        new Vector3(0.87308f,0.03309f,-0.48645f),
    };

    static readonly Vector3[] LegLeftUpLegWalkYDir = {
        new Vector3(0.56843f,-0.76473f,0.30344f), new Vector3(0.56431f,-0.79766f,0.21282f), new Vector3(0.53935f,-0.83472f,0.11108f), new Vector3(0.49666f,-0.86793f,-0.00525f),
        new Vector3(0.47400f,-0.87917f,-0.04888f), new Vector3(0.45252f,-0.88743f,-0.08772f), new Vector3(0.43730f,-0.89142f,-0.11887f), new Vector3(0.43024f,-0.89322f,-0.13055f),
        new Vector3(0.43079f,-0.89343f,-0.12726f), new Vector3(0.43875f,-0.89214f,-0.10768f), new Vector3(0.44928f,-0.88912f,-0.08729f), new Vector3(0.45713f,-0.88593f,-0.07846f),
        new Vector3(0.46526f,-0.88228f,-0.07153f), new Vector3(0.47576f,-0.87768f,-0.05770f), new Vector3(0.48887f,-0.87128f,-0.04346f), new Vector3(0.50467f,-0.86290f,-0.02662f),
        new Vector3(0.52142f,-0.85330f,-0.00230f), new Vector3(0.53977f,-0.84137f,0.02719f), new Vector3(0.55831f,-0.82705f,0.06539f), new Vector3(0.57429f,-0.81109f,0.11102f),
        new Vector3(0.58856f,-0.79152f,0.16460f), new Vector3(0.60313f,-0.76620f,0.22176f), new Vector3(0.61503f,-0.73601f,0.28288f), new Vector3(0.62296f,-0.70295f,0.34321f),
        new Vector3(0.62668f,-0.66752f,0.40210f), new Vector3(0.62551f,-0.63114f,0.45870f), new Vector3(0.61864f,-0.59478f,0.51335f), new Vector3(0.60835f,-0.56644f,0.55593f),
        new Vector3(0.59200f,-0.55351f,0.58580f), new Vector3(0.58074f,-0.53812f,0.61088f), new Vector3(0.56413f,-0.51488f,0.64549f), new Vector3(0.53873f,-0.50824f,0.67191f),
        new Vector3(0.51282f,-0.50674f,0.69299f), new Vector3(0.48864f,-0.50948f,0.70828f), new Vector3(0.46615f,-0.52161f,0.71458f), new Vector3(0.44640f,-0.54074f,0.71297f),
        new Vector3(0.42610f,-0.55270f,0.71621f), new Vector3(0.39712f,-0.57653f,0.71408f), new Vector3(0.36604f,-0.57616f,0.73079f), new Vector3(0.33997f,-0.53451f,0.77377f),
        new Vector3(0.32628f,-0.48471f,0.81154f), new Vector3(0.32443f,-0.44539f,0.83449f), new Vector3(0.30825f,-0.42490f,0.85114f), new Vector3(0.29224f,-0.41609f,0.86109f),
        new Vector3(0.27639f,-0.41765f,0.86555f), new Vector3(0.26098f,-0.42696f,0.86579f), new Vector3(0.24743f,-0.43977f,0.86335f), new Vector3(0.23706f,-0.48655f,0.84088f),
        new Vector3(0.22500f,-0.54084f,0.81047f), new Vector3(0.20953f,-0.59640f,0.77486f), new Vector3(0.18467f,-0.63498f,0.75013f), new Vector3(0.15911f,-0.67318f,0.72216f),
        new Vector3(0.14166f,-0.70969f,0.69013f), new Vector3(0.12857f,-0.74417f,0.65550f), new Vector3(0.11753f,-0.77680f,0.61868f), new Vector3(0.10903f,-0.80764f,0.57951f),
        new Vector3(0.10515f,-0.83607f,0.53846f), new Vector3(0.10692f,-0.86215f,0.49525f), new Vector3(0.11091f,-0.88532f,0.45155f), new Vector3(0.11518f,-0.90724f,0.40454f),
        new Vector3(0.11898f,-0.92728f,0.35495f), new Vector3(0.12277f,-0.94309f,0.30904f), new Vector3(0.12643f,-0.95869f,0.25482f), new Vector3(0.12889f,-0.97058f,0.20338f),
        new Vector3(0.13114f,-0.97802f,0.16212f), new Vector3(0.13380f,-0.98508f,0.10823f), new Vector3(0.13590f,-0.98869f,0.06337f), new Vector3(0.14171f,-0.98979f,0.01554f),
        new Vector3(0.14895f,-0.98810f,-0.03827f), new Vector3(0.15607f,-0.98377f,-0.08851f), new Vector3(0.16330f,-0.97801f,-0.12971f), new Vector3(0.17176f,-0.97029f,-0.17042f),
        new Vector3(0.17914f,-0.95962f,-0.21688f), new Vector3(0.18683f,-0.94405f,-0.27178f), new Vector3(0.19775f,-0.93711f,-0.28760f), new Vector3(0.21286f,-0.93804f,-0.27345f),
        new Vector3(0.23269f,-0.94329f,-0.23678f), new Vector3(0.26671f,-0.94662f,-0.18106f), new Vector3(0.32020f,-0.93936f,-0.12280f), new Vector3(0.38271f,-0.92050f,-0.07881f),
        new Vector3(0.43404f,-0.89834f,-0.06777f),
    };

    static readonly Vector3[] LegLeftUpLegWalkXDir = {
        new Vector3(0.78513f,0.39399f,-0.47785f), new Vector3(0.77621f,0.42485f,-0.46583f), new Vector3(0.78017f,0.44568f,-0.43899f), new Vector3(0.79217f,0.45576f,-0.40589f),
        new Vector3(0.81000f,0.45713f,-0.36734f), new Vector3(0.82866f,0.45480f,-0.32631f), new Vector3(0.84387f,0.45244f,-0.28842f), new Vector3(0.85871f,0.44957f,-0.24596f),
        new Vector3(0.86727f,0.44885f,-0.21535f), new Vector3(0.86593f,0.45177f,-0.21464f), new Vector3(0.85538f,0.45630f,-0.24517f), new Vector3(0.83947f,0.45893f,-0.29098f),
        new Vector3(0.81861f,0.45961f,-0.34443f), new Vector3(0.79667f,0.45779f,-0.39464f), new Vector3(0.77235f,0.45545f,-0.44276f), new Vector3(0.74692f,0.45188f,-0.48776f),
        new Vector3(0.72511f,0.44451f,-0.52595f), new Vector3(0.70443f,0.43377f,-0.56180f), new Vector3(0.68859f,0.41799f,-0.59256f), new Vector3(0.67967f,0.39679f,-0.61693f),
        new Vector3(0.67628f,0.37045f,-0.63672f), new Vector3(0.67337f,0.34007f,-0.65645f), new Vector3(0.67395f,0.30447f,-0.67312f), new Vector3(0.67895f,0.26796f,-0.68353f),
        new Vector3(0.68842f,0.23243f,-0.68706f), new Vector3(0.70231f,0.19937f,-0.68339f), new Vector3(0.72053f,0.16899f,-0.67251f), new Vector3(0.73850f,0.14741f,-0.65794f),
        new Vector3(0.75836f,0.13653f,-0.63738f), new Vector3(0.77265f,0.12795f,-0.62181f), new Vector3(0.79251f,0.11827f,-0.59828f), new Vector3(0.81668f,0.11919f,-0.56465f),
        new Vector3(0.83934f,0.12636f,-0.52872f), new Vector3(0.85713f,0.12867f,-0.49877f), new Vector3(0.87037f,0.12554f,-0.47613f), new Vector3(0.87970f,0.11926f,-0.46034f),
        new Vector3(0.88811f,0.10476f,-0.44753f), new Vector3(0.90141f,0.09876f,-0.42156f), new Vector3(0.91798f,0.09468f,-0.38515f), new Vector3(0.93290f,0.08772f,-0.34929f),
        new Vector3(0.94016f,0.07719f,-0.33189f), new Vector3(0.94033f,0.05615f,-0.33561f), new Vector3(0.94563f,0.03927f,-0.32286f), new Vector3(0.95011f,0.02368f,-0.31101f),
        new Vector3(0.95386f,0.00930f,-0.30010f), new Vector3(0.95678f,-0.00481f,-0.29077f), new Vector3(0.95834f,-0.02019f,-0.28493f), new Vector3(0.95805f,-0.02640f,-0.28537f),
        new Vector3(0.95790f,-0.02944f,-0.28557f), new Vector3(0.95807f,-0.03315f,-0.28460f), new Vector3(0.96038f,-0.04552f,-0.27496f), new Vector3(0.96253f,-0.05694f,-0.26514f),
        new Vector3(0.96255f,-0.06404f,-0.26344f), new Vector3(0.96162f,-0.06800f,-0.26581f), new Vector3(0.96086f,-0.06842f,-0.26844f), new Vector3(0.96044f,-0.06471f,-0.27087f),
        new Vector3(0.95979f,-0.05640f,-0.27499f), new Vector3(0.95815f,-0.04368f,-0.28291f), new Vector3(0.95595f,-0.02921f,-0.29206f), new Vector3(0.95393f,-0.01255f,-0.29976f),
        new Vector3(0.95240f,0.00552f,-0.30481f), new Vector3(0.95148f,0.02332f,-0.30683f), new Vector3(0.95129f,0.04435f,-0.30510f), new Vector3(0.95233f,0.06397f,-0.29827f),
        new Vector3(0.95375f,0.07985f,-0.28979f), new Vector3(0.95543f,0.09923f,-0.27805f), new Vector3(0.95728f,0.11456f,-0.26549f), new Vector3(0.95908f,0.13339f,-0.24974f),
        new Vector3(0.95947f,0.15378f,-0.23616f), new Vector3(0.96018f,0.17213f,-0.22005f), new Vector3(0.95960f,0.18799f,-0.20934f), new Vector3(0.95763f,0.20504f,-0.20226f),
        new Vector3(0.95639f,0.22155f,-0.19033f), new Vector3(0.95339f,0.24096f,-0.18161f), new Vector3(0.95108f,0.25447f,-0.17521f), new Vector3(0.94824f,0.26582f,-0.17372f),
        new Vector3(0.94203f,0.27912f,-0.18620f), new Vector3(0.92503f,0.30416f,-0.22760f), new Vector3(0.89033f,0.34268f,-0.29980f), new Vector3(0.84535f,0.38332f,-0.37209f),
        new Vector3(0.79428f,0.41709f,-0.44176f),
    };

    static readonly Vector3[] LegLeftLegWalkYDir = {
        new Vector3(-0.06525f,-0.66269f,-0.74604f), new Vector3(-0.08444f,-0.61240f,-0.78603f), new Vector3(-0.08017f,-0.57943f,-0.81107f), new Vector3(-0.06391f,-0.56388f,-0.82338f),
        new Vector3(-0.03399f,-0.57470f,-0.81766f), new Vector3(-0.00535f,-0.58764f,-0.80911f), new Vector3(0.01900f,-0.60198f,-0.79828f), new Vector3(0.04048f,-0.60920f,-0.79198f),
        new Vector3(0.05029f,-0.61166f,-0.78952f), new Vector3(0.03854f,-0.60814f,-0.79290f), new Vector3(0.00486f,-0.59630f,-0.80275f), new Vector3(-0.04323f,-0.57358f,-0.81801f),
        new Vector3(-0.10344f,-0.54191f,-0.83405f), new Vector3(-0.16470f,-0.50974f,-0.84441f), new Vector3(-0.22543f,-0.47863f,-0.84858f), new Vector3(-0.28343f,-0.45100f,-0.84632f),
        new Vector3(-0.33562f,-0.42710f,-0.83961f), new Vector3(-0.38618f,-0.40799f,-0.82729f), new Vector3(-0.43227f,-0.39470f,-0.81077f), new Vector3(-0.47278f,-0.38568f,-0.79229f),
        new Vector3(-0.50797f,-0.38291f,-0.77159f), new Vector3(-0.54050f,-0.38862f,-0.74622f), new Vector3(-0.56538f,-0.40888f,-0.71635f), new Vector3(-0.58306f,-0.43777f,-0.68440f),
        new Vector3(-0.59192f,-0.47541f,-0.65086f), new Vector3(-0.59050f,-0.52221f,-0.61531f), new Vector3(-0.57772f,-0.57895f,-0.57538f), new Vector3(-0.55524f,-0.63573f,-0.53624f),
        new Vector3(-0.52514f,-0.68237f,-0.50853f), new Vector3(-0.49251f,-0.73340f,-0.46857f), new Vector3(-0.45018f,-0.79175f,-0.41287f), new Vector3(-0.40019f,-0.83391f,-0.38006f),
        new Vector3(-0.34411f,-0.86953f,-0.35426f), new Vector3(-0.29587f,-0.89743f,-0.32723f), new Vector3(-0.25898f,-0.91508f,-0.30912f), new Vector3(-0.23005f,-0.92559f,-0.30061f),
        new Vector3(-0.20268f,-0.94189f,-0.26789f), new Vector3(-0.15171f,-0.96052f,-0.23319f), new Vector3(-0.09804f,-0.96987f,-0.22304f), new Vector3(-0.06566f,-0.95943f,-0.27421f),
        new Vector3(-0.05112f,-0.94452f,-0.32444f), new Vector3(-0.06210f,-0.93335f,-0.35357f), new Vector3(-0.05602f,-0.92459f,-0.37682f), new Vector3(-0.04787f,-0.91942f,-0.39034f),
        new Vector3(-0.03828f,-0.91682f,-0.39746f), new Vector3(-0.02846f,-0.91579f,-0.40064f), new Vector3(-0.02061f,-0.91581f,-0.40107f), new Vector3(0.00201f,-0.89126f,-0.45348f),
        new Vector3(0.02806f,-0.86072f,-0.50830f), new Vector3(0.05577f,-0.83017f,-0.55471f), new Vector3(0.07823f,-0.81257f,-0.57759f), new Vector3(0.10062f,-0.79534f,-0.59775f),
        new Vector3(0.11551f,-0.77858f,-0.61682f), new Vector3(0.12655f,-0.76465f,-0.63190f), new Vector3(0.13606f,-0.75255f,-0.64432f), new Vector3(0.14389f,-0.74171f,-0.65510f),
        new Vector3(0.14854f,-0.73281f,-0.66402f), new Vector3(0.14841f,-0.72596f,-0.67153f), new Vector3(0.14610f,-0.72152f,-0.67680f), new Vector3(0.14382f,-0.71784f,-0.68120f),
        new Vector3(0.14216f,-0.71478f,-0.68475f), new Vector3(0.14194f,-0.71412f,-0.68548f), new Vector3(0.14287f,-0.71391f,-0.68551f), new Vector3(0.14551f,-0.71551f,-0.68328f),
        new Vector3(0.14775f,-0.72047f,-0.67757f), new Vector3(0.14972f,-0.71952f,-0.67814f), new Vector3(0.15153f,-0.72374f,-0.67323f), new Vector3(0.15354f,-0.72894f,-0.66713f),
        new Vector3(0.15426f,-0.73540f,-0.65984f), new Vector3(0.15679f,-0.74421f,-0.64928f), new Vector3(0.15772f,-0.75630f,-0.63493f), new Vector3(0.15796f,-0.76954f,-0.61875f),
        new Vector3(0.16048f,-0.78300f,-0.60097f), new Vector3(0.16367f,-0.79864f,-0.57913f), new Vector3(0.16891f,-0.80711f,-0.56572f), new Vector3(0.17200f,-0.80051f,-0.57411f),
        new Vector3(0.16783f,-0.77688f,-0.60687f), new Vector3(0.14986f,-0.73718f,-0.65887f), new Vector3(0.11417f,-0.69763f,-0.70730f), new Vector3(0.06620f,-0.65711f,-0.75088f),
        new Vector3(-0.01250f,-0.60473f,-0.79633f),
    };

    static readonly Vector3[] LegLeftLegWalkXDir = {
        new Vector3(0.80390f,0.40802f,-0.43275f), new Vector3(0.79826f,0.43055f,-0.42120f), new Vector3(0.80695f,0.43994f,-0.39406f), new Vector3(0.82228f,0.43776f,-0.36362f),
        new Vector3(0.84149f,0.42493f,-0.33365f), new Vector3(0.86172f,0.40780f,-0.30188f), new Vector3(0.87876f,0.39088f,-0.27385f), new Vector3(0.89488f,0.37470f,-0.24248f),
        new Vector3(0.90464f,0.36287f,-0.22350f), new Vector3(0.90334f,0.36042f,-0.23253f), new Vector3(0.89037f,0.36800f,-0.26797f), new Vector3(0.86990f,0.38105f,-0.31316f),
        new Vector3(0.84334f,0.39679f,-0.36240f), new Vector3(0.81639f,0.40998f,-0.40672f), new Vector3(0.78845f,0.42202f,-0.44749f), new Vector3(0.76071f,0.43164f,-0.48478f),
        new Vector3(0.73692f,0.43616f,-0.51644f), new Vector3(0.71341f,0.43643f,-0.54825f), new Vector3(0.69280f,0.43017f,-0.57878f), new Vector3(0.67678f,0.41687f,-0.60678f),
        new Vector3(0.66383f,0.39679f,-0.63394f), new Vector3(0.64951f,0.37104f,-0.66368f), new Vector3(0.63685f,0.33553f,-0.69415f), new Vector3(0.62642f,0.29418f,-0.72184f),
        new Vector3(0.61998f,0.24744f,-0.74458f), new Vector3(0.61955f,0.19525f,-0.76028f), new Vector3(0.62730f,0.13610f,-0.76679f), new Vector3(0.64180f,0.08255f,-0.76242f),
        new Vector3(0.66584f,0.04270f,-0.74487f), new Vector3(0.68712f,0.00275f,-0.72653f), new Vector3(0.71769f,-0.04573f,-0.69486f), new Vector3(0.75887f,-0.06904f,-0.64757f),
        new Vector3(0.80100f,-0.07501f,-0.59394f), new Vector3(0.83495f,-0.07656f,-0.54497f), new Vector3(0.85993f,-0.07271f,-0.50521f), new Vector3(0.87777f,-0.06397f,-0.47479f),
        new Vector3(0.89325f,-0.06573f,-0.44473f), new Vector3(0.91584f,-0.04786f,-0.39868f), new Vector3(0.93581f,-0.01359f,-0.35223f), new Vector3(0.94654f,0.02709f,-0.32146f),
        new Vector3(0.94849f,0.05579f,-0.31187f), new Vector3(0.94528f,0.05868f,-0.32094f), new Vector3(0.94749f,0.06977f,-0.31208f), new Vector3(0.95048f,0.07821f,-0.30077f),
        new Vector3(0.95365f,0.08529f,-0.28859f), new Vector3(0.95636f,0.09166f,-0.27743f), new Vector3(0.95763f,0.09718f,-0.27112f), new Vector3(0.96160f,0.12618f,-0.24372f),
        new Vector3(0.96527f,0.15546f,-0.20997f), new Vector3(0.96874f,0.17950f,-0.17125f), new Vector3(0.97173f,0.19160f,-0.13793f), new Vector3(0.97417f,0.20083f,-0.10324f),
        new Vector3(0.97513f,0.20713f,-0.07883f), new Vector3(0.97556f,0.21130f,-0.06031f), new Vector3(0.97574f,0.21440f,-0.04437f), new Vector3(0.97575f,0.21668f,-0.03101f),
        new Vector3(0.97548f,0.21884f,-0.02330f), new Vector3(0.97499f,0.22102f,-0.02346f), new Vector3(0.97433f,0.22339f,-0.02781f), new Vector3(0.97365f,0.22573f,-0.03231f),
        new Vector3(0.97306f,0.22776f,-0.03573f), new Vector3(0.97236f,0.23028f,-0.03857f), new Vector3(0.97187f,0.23222f,-0.03929f), new Vector3(0.97184f,0.23276f,-0.03677f),
        new Vector3(0.97211f,0.23196f,-0.03467f), new Vector3(0.97284f,0.22968f,-0.02892f), new Vector3(0.97369f,0.22656f,-0.02440f), new Vector3(0.97439f,0.22395f,-0.02044f),
        new Vector3(0.97475f,0.22242f,-0.02001f), new Vector3(0.97517f,0.22077f,-0.01756f), new Vector3(0.97524f,0.22024f,-0.02009f), new Vector3(0.97477f,0.22159f,-0.02676f),
        new Vector3(0.97464f,0.22192f,-0.02887f), new Vector3(0.97371f,0.22505f,-0.03518f), new Vector3(0.97243f,0.23009f,-0.03792f), new Vector3(0.97045f,0.23781f,-0.04085f),
        new Vector3(0.96611f,0.25209f,-0.05554f), new Vector3(0.95407f,0.28267f,-0.09926f), new Vector3(0.92705f,0.33076f,-0.17659f), new Vector3(0.88896f,0.38061f,-0.25471f),
        new Vector3(0.84239f,0.42270f,-0.33421f),
    };

    static readonly Vector3[] LegLeftFootWalkYDir = {
        new Vector3(0.24759f,-0.61748f,0.74660f), new Vector3(0.24888f,-0.61799f,0.74575f), new Vector3(0.25057f,-0.62034f,0.74323f), new Vector3(0.25144f,-0.62908f,0.73555f),
        new Vector3(0.25246f,-0.65055f,0.71628f), new Vector3(0.25431f,-0.68656f,0.68115f), new Vector3(0.25765f,-0.73441f,0.62790f), new Vector3(0.26106f,-0.79436f,0.54849f),
        new Vector3(0.27759f,-0.84635f,0.45457f), new Vector3(0.31572f,-0.88234f,0.34899f), new Vector3(0.36351f,-0.89813f,0.24743f), new Vector3(0.40743f,-0.89313f,0.19057f),
        new Vector3(0.45388f,-0.86973f,0.19383f), new Vector3(0.48609f,-0.85590f,0.17650f), new Vector3(0.50103f,-0.85529f,0.13212f), new Vector3(0.50023f,-0.86320f,0.06820f),
        new Vector3(0.49420f,-0.86935f,0.00093f), new Vector3(0.50222f,-0.86304f,-0.05418f), new Vector3(0.51088f,-0.85403f,-0.09816f), new Vector3(0.51281f,-0.84800f,-0.13384f),
        new Vector3(0.50468f,-0.84815f,-0.16104f), new Vector3(0.48499f,-0.85585f,-0.17972f), new Vector3(0.45585f,-0.87143f,-0.18113f), new Vector3(0.42859f,-0.88763f,-0.16857f),
        new Vector3(0.40229f,-0.90466f,-0.14055f), new Vector3(0.37554f,-0.92158f,-0.09832f), new Vector3(0.34732f,-0.93671f,-0.04404f), new Vector3(0.32282f,-0.94640f,0.01088f),
        new Vector3(0.29189f,-0.95490f,0.05447f), new Vector3(0.26661f,-0.95822f,0.10364f), new Vector3(0.24364f,-0.95542f,0.16676f), new Vector3(0.21654f,-0.95195f,0.21658f),
        new Vector3(0.19538f,-0.94359f,0.26733f), new Vector3(0.18343f,-0.92371f,0.33632f), new Vector3(0.17278f,-0.89060f,0.42068f), new Vector3(0.16270f,-0.84059f,0.51667f),
        new Vector3(0.15150f,-0.76166f,0.63002f), new Vector3(0.13409f,-0.67955f,0.72127f), new Vector3(0.11320f,-0.62582f,0.77171f), new Vector3(0.09522f,-0.60669f,0.78921f),
        new Vector3(0.09006f,-0.61167f,0.78597f), new Vector3(0.09084f,-0.61311f,0.78476f), new Vector3(0.08958f,-0.61308f,0.78493f), new Vector3(0.08790f,-0.61321f,0.78501f),
        new Vector3(0.08710f,-0.61315f,0.78515f), new Vector3(0.08617f,-0.61498f,0.78382f), new Vector3(0.08570f,-0.61324f,0.78523f), new Vector3(0.08433f,-0.61196f,0.78638f),
        new Vector3(0.08332f,-0.61384f,0.78502f), new Vector3(0.08213f,-0.61417f,0.78489f), new Vector3(0.08106f,-0.61456f,0.78469f), new Vector3(0.08003f,-0.61544f,0.78411f),
        new Vector3(0.07933f,-0.61542f,0.78419f), new Vector3(0.07867f,-0.61583f,0.78394f), new Vector3(0.07825f,-0.61623f,0.78367f), new Vector3(0.07805f,-0.61672f,0.78331f),
        new Vector3(0.07814f,-0.61728f,0.78285f), new Vector3(0.07817f,-0.61762f,0.78258f), new Vector3(0.07837f,-0.61794f,0.78231f), new Vector3(0.07883f,-0.61824f,0.78202f),
        new Vector3(0.07931f,-0.61873f,0.78159f), new Vector3(0.07907f,-0.61900f,0.78140f), new Vector3(0.07884f,-0.61881f,0.78157f), new Vector3(0.07921f,-0.61838f,0.78188f),
        new Vector3(0.08019f,-0.61548f,0.78407f), new Vector3(0.07976f,-0.61924f,0.78114f), new Vector3(0.07981f,-0.61798f,0.78213f), new Vector3(0.08117f,-0.61885f,0.78130f),
        new Vector3(0.08306f,-0.62047f,0.77982f), new Vector3(0.08105f,-0.62095f,0.77965f), new Vector3(0.08114f,-0.61833f,0.78172f), new Vector3(0.08249f,-0.61577f,0.78359f),
        new Vector3(0.08272f,-0.61530f,0.78394f), new Vector3(0.08333f,-0.61525f,0.78392f), new Vector3(0.08321f,-0.61440f,0.78459f), new Vector3(0.08302f,-0.61440f,0.78462f),
        new Vector3(0.08281f,-0.61441f,0.78463f), new Vector3(0.08166f,-0.61496f,0.78431f), new Vector3(0.08435f,-0.61068f,0.78737f), new Vector3(0.08978f,-0.62191f,0.77793f),
        new Vector3(0.10974f,-0.63713f,0.76291f),
    };

    static readonly Vector3[] LegLeftFootWalkXDir = {
        new Vector3(0.93496f,-0.04980f,-0.35124f), new Vector3(0.93449f,-0.04912f,-0.35258f), new Vector3(0.93217f,-0.05261f,-0.35818f), new Vector3(0.92861f,-0.05745f,-0.36657f),
        new Vector3(0.92354f,-0.05885f,-0.37896f), new Vector3(0.91667f,-0.05338f,-0.39606f), new Vector3(0.90705f,-0.04010f,-0.41910f), new Vector3(0.89871f,-0.00741f,-0.43848f),
        new Vector3(0.88871f,0.04651f,-0.45611f), new Vector3(0.87719f,0.13120f,-0.46187f), new Vector3(0.86428f,0.22600f,-0.44938f), new Vector3(0.85277f,0.29740f,-0.42934f),
        new Vector3(0.84691f,0.35344f,-0.39727f), new Vector3(0.83689f,0.39775f,-0.37606f), new Vector3(0.82493f,0.42583f,-0.37169f), new Vector3(0.81073f,0.43925f,-0.38702f),
        new Vector3(0.78971f,0.44849f,-0.41858f), new Vector3(0.75086f,0.46630f,-0.46773f), new Vector3(0.70287f,0.48072f,-0.52430f), new Vector3(0.65079f,0.48567f,-0.58361f),
        new Vector3(0.59988f,0.47868f,-0.64109f), new Vector3(0.55591f,0.46037f,-0.69212f), new Vector3(0.51601f,0.42457f,-0.74396f), new Vector3(0.49925f,0.38817f,-0.77465f),
        new Vector3(0.50783f,0.34824f,-0.78793f), new Vector3(0.53626f,0.30259f,-0.78795f), new Vector3(0.57280f,0.24910f,-0.78093f), new Vector3(0.61562f,0.20123f,-0.76192f),
        new Vector3(0.66008f,0.15991f,-0.73398f), new Vector3(0.70067f,0.11886f,-0.70352f), new Vector3(0.75018f,0.07667f,-0.65677f), new Vector3(0.80067f,0.04623f,-0.59732f),
        new Vector3(0.84314f,0.02238f,-0.53723f), new Vector3(0.87834f,0.00037f,-0.47804f), new Vector3(0.90537f,-0.02459f,-0.42390f), new Vector3(0.92610f,-0.05056f,-0.37388f),
        new Vector3(0.94457f,-0.07626f,-0.31933f), new Vector3(0.95912f,-0.09404f,-0.26691f), new Vector3(0.96866f,-0.10331f,-0.22587f), new Vector3(0.97269f,-0.11189f,-0.20336f),
        new Vector3(0.97196f,-0.11814f,-0.20331f), new Vector3(0.97155f,-0.11850f,-0.20504f), new Vector3(0.97168f,-0.11924f,-0.20402f), new Vector3(0.97185f,-0.12012f,-0.20266f),
        new Vector3(0.97191f,-0.12069f,-0.20208f), new Vector3(0.97187f,-0.12120f,-0.20193f), new Vector3(0.97191f,-0.12193f,-0.20129f), new Vector3(0.97184f,-0.12376f,-0.20053f),
        new Vector3(0.97212f,-0.12319f,-0.19950f), new Vector3(0.97224f,-0.12375f,-0.19856f), new Vector3(0.97231f,-0.12438f,-0.19786f), new Vector3(0.97232f,-0.12505f,-0.19739f),
        new Vector3(0.97240f,-0.12539f,-0.19677f), new Vector3(0.97245f,-0.12569f,-0.19633f), new Vector3(0.97249f,-0.12583f,-0.19605f), new Vector3(0.97247f,-0.12595f,-0.19606f),
        new Vector3(0.97242f,-0.12590f,-0.19634f), new Vector3(0.97243f,-0.12577f,-0.19639f), new Vector3(0.97242f,-0.12553f,-0.19657f), new Vector3(0.97243f,-0.12503f,-0.19686f),
        new Vector3(0.97240f,-0.12455f,-0.19727f), new Vector3(0.97256f,-0.12414f,-0.19676f), new Vector3(0.97289f,-0.12324f,-0.19572f), new Vector3(0.97318f,-0.12196f,-0.19505f),
        new Vector3(0.97327f,-0.12147f,-0.19490f), new Vector3(0.97363f,-0.11963f,-0.19426f), new Vector3(0.97385f,-0.11910f,-0.19349f), new Vector3(0.97402f,-0.11701f,-0.19387f),
        new Vector3(0.97404f,-0.11482f,-0.19510f), new Vector3(0.97488f,-0.11342f,-0.19168f), new Vector3(0.97521f,-0.11274f,-0.19040f), new Vector3(0.97529f,-0.11181f,-0.19054f),
        new Vector3(0.97581f,-0.10973f,-0.18909f), new Vector3(0.97617f,-0.10778f,-0.18836f), new Vector3(0.97669f,-0.10605f,-0.18663f), new Vector3(0.97729f,-0.10384f,-0.18472f),
        new Vector3(0.97810f,-0.10073f,-0.18211f), new Vector3(0.97909f,-0.09760f,-0.17847f), new Vector3(0.98089f,-0.08814f,-0.17345f), new Vector3(0.98017f,-0.08336f,-0.17976f),
        new Vector3(0.97585f,-0.07680f,-0.20452f),
    };

    static readonly Vector3[] LegLeftToeWalkYDir = {
        new Vector3(0.33762f,0.14553f,0.92997f), new Vector3(0.33420f,0.16674f,0.92763f), new Vector3(0.33472f,0.16711f,0.92738f), new Vector3(0.33416f,0.16556f,0.92786f),
        new Vector3(0.33436f,0.16505f,0.92788f), new Vector3(0.33516f,0.16558f,0.92750f), new Vector3(0.33426f,0.16517f,0.92789f), new Vector3(0.34182f,0.09656f,0.93479f),
        new Vector3(0.35482f,-0.03294f,0.93435f), new Vector3(0.37197f,-0.20235f,0.90592f), new Vector3(0.39141f,-0.35539f,0.84882f), new Vector3(0.39994f,-0.40051f,0.82440f),
        new Vector3(0.38769f,-0.38879f,0.83579f), new Vector3(0.38510f,-0.40151f,0.83096f), new Vector3(0.39791f,-0.44065f,0.80467f), new Vector3(0.42434f,-0.49484f,0.75833f),
        new Vector3(0.46009f,-0.54591f,0.70022f), new Vector3(0.51165f,-0.56525f,0.64708f), new Vector3(0.56551f,-0.57061f,0.59549f), new Vector3(0.61404f,-0.57134f,0.54454f),
        new Vector3(0.65160f,-0.57219f,0.49802f), new Vector3(0.67504f,-0.57494f,0.46236f), new Vector3(0.68536f,-0.58847f,0.42893f), new Vector3(0.67969f,-0.59756f,0.42537f),
        new Vector3(0.65869f,-0.60378f,0.44897f), new Vector3(0.62356f,-0.60793f,0.49152f), new Vector3(0.57724f,-0.61340f,0.53902f), new Vector3(0.53002f,-0.61191f,0.58707f),
        new Vector3(0.48542f,-0.61409f,0.62231f), new Vector3(0.45601f,-0.60048f,0.65687f), new Vector3(0.42872f,-0.56903f,0.70171f), new Vector3(0.40297f,-0.54282f,0.73686f),
        new Vector3(0.39073f,-0.51168f,0.76519f), new Vector3(0.39062f,-0.45291f,0.80143f), new Vector3(0.39882f,-0.36553f,0.84103f), new Vector3(0.41111f,-0.25294f,0.87579f),
        new Vector3(0.41810f,-0.11452f,0.90115f), new Vector3(0.41781f,0.01696f,0.90838f), new Vector3(0.41548f,0.08073f,0.90601f), new Vector3(0.41393f,0.08252f,0.90656f),
        new Vector3(0.41829f,0.05436f,0.90669f), new Vector3(0.41989f,0.05328f,0.90601f), new Vector3(0.41980f,0.05394f,0.90601f), new Vector3(0.41935f,0.05395f,0.90622f),
        new Vector3(0.41961f,0.05429f,0.90608f), new Vector3(0.42002f,0.05232f,0.90601f), new Vector3(0.42048f,0.05469f,0.90565f), new Vector3(0.41995f,0.05615f,0.90581f),
        new Vector3(0.41867f,0.05488f,0.90648f), new Vector3(0.41763f,0.05516f,0.90694f), new Vector3(0.41745f,0.05506f,0.90703f), new Vector3(0.41739f,0.05435f,0.90710f),
        new Vector3(0.41713f,0.05483f,0.90719f), new Vector3(0.41697f,0.05470f,0.90727f), new Vector3(0.41671f,0.05462f,0.90740f), new Vector3(0.41671f,0.05444f,0.90741f),
        new Vector3(0.41694f,0.05422f,0.90731f), new Vector3(0.41699f,0.05358f,0.90733f), new Vector3(0.41695f,0.05298f,0.90738f), new Vector3(0.41691f,0.05249f,0.90743f),
        new Vector3(0.41745f,0.05175f,0.90723f), new Vector3(0.41888f,0.05117f,0.90660f), new Vector3(0.41985f,0.05134f,0.90614f), new Vector3(0.42099f,0.05193f,0.90558f),
        new Vector3(0.42274f,0.05531f,0.90456f), new Vector3(0.42289f,0.05086f,0.90475f), new Vector3(0.42323f,0.05148f,0.90456f), new Vector3(0.42423f,0.05000f,0.90417f),
        new Vector3(0.42586f,0.04748f,0.90354f), new Vector3(0.42335f,0.04612f,0.90479f), new Vector3(0.42322f,0.04834f,0.90474f), new Vector3(0.42442f,0.05054f,0.90405f),
        new Vector3(0.42386f,0.05063f,0.90431f), new Vector3(0.42409f,0.05014f,0.90423f), new Vector3(0.42409f,0.05068f,0.90420f), new Vector3(0.42413f,0.05030f,0.90420f),
        new Vector3(0.42404f,0.05006f,0.90426f), new Vector3(0.42350f,0.04908f,0.90457f), new Vector3(0.42352f,0.05532f,0.90419f), new Vector3(0.42347f,0.04986f,0.90454f),
        new Vector3(0.42436f,0.04976f,0.90413f),
    };

    static readonly Vector3[] LegLeftToeWalkXDir = {
        new Vector3(0.94004f,-0.10294f,-0.32517f), new Vector3(0.94174f,-0.09858f,-0.32157f), new Vector3(0.94156f,-0.09877f,-0.32204f), new Vector3(0.94176f,-0.09805f,-0.32167f),
        new Vector3(0.94167f,-0.09840f,-0.32183f), new Vector3(0.94137f,-0.09914f,-0.32248f), new Vector3(0.94172f,-0.09803f,-0.32180f), new Vector3(0.93917f,-0.07040f,-0.33616f),
        new Vector3(0.93438f,-0.02173f,-0.35560f), new Vector3(0.92801f,0.05907f,-0.36785f), new Vector3(0.92022f,0.15140f,-0.36094f), new Vector3(0.91524f,0.22245f,-0.33594f),
        new Vector3(0.91348f,0.28348f,-0.29187f), new Vector3(0.90729f,0.32949f,-0.26128f), new Vector3(0.90069f,0.35438f,-0.25133f), new Vector3(0.89421f,0.36085f,-0.26491f),
        new Vector3(0.88312f,0.36285f,-0.29738f), new Vector3(0.85781f,0.37874f,-0.34743f), new Vector3(0.82473f,0.39400f,-0.40568f), new Vector3(0.78775f,0.40073f,-0.46783f),
        new Vector3(0.75116f,0.39518f,-0.52876f), new Vector3(0.72009f,0.37702f,-0.58251f), new Vector3(0.69099f,0.33968f,-0.63808f), new Vector3(0.68197f,0.30130f,-0.66644f),
        new Vector3(0.69475f,0.25898f,-0.67101f), new Vector3(0.72354f,0.21068f,-0.65734f), new Vector3(0.75705f,0.15459f,-0.63481f), new Vector3(0.79119f,0.10775f,-0.60200f),
        new Vector3(0.81921f,0.07086f,-0.56909f), new Vector3(0.83886f,0.04347f,-0.54261f), new Vector3(0.86129f,0.02297f,-0.50759f), new Vector3(0.88181f,0.01476f,-0.47137f),
        new Vector3(0.89464f,0.01540f,-0.44653f), new Vector3(0.90256f,0.01716f,-0.43022f), new Vector3(0.90496f,0.00855f,-0.42541f), new Vector3(0.90363f,-0.01359f,-0.42810f),
        new Vector3(0.90348f,-0.05067f,-0.42562f), new Vector3(0.90524f,-0.09288f,-0.41463f), new Vector3(0.90637f,-0.12060f,-0.40490f), new Vector3(0.90609f,-0.13313f,-0.40160f),
        new Vector3(0.90299f,-0.13279f,-0.40862f), new Vector3(0.90223f,-0.13270f,-0.41033f), new Vector3(0.90229f,-0.13277f,-0.41017f), new Vector3(0.90248f,-0.13292f,-0.40971f),
        new Vector3(0.90239f,-0.13284f,-0.40994f), new Vector3(0.90218f,-0.13220f,-0.41061f), new Vector3(0.90202f,-0.13271f,-0.41079f), new Vector3(0.90218f,-0.13421f,-0.40995f),
        new Vector3(0.90286f,-0.13259f,-0.40897f), new Vector3(0.90335f,-0.13254f,-0.40791f), new Vector3(0.90341f,-0.13270f,-0.40773f), new Vector3(0.90339f,-0.13281f,-0.40773f),
        new Vector3(0.90351f,-0.13298f,-0.40740f), new Vector3(0.90358f,-0.13301f,-0.40725f), new Vector3(0.90368f,-0.13309f,-0.40699f), new Vector3(0.90367f,-0.13313f,-0.40701f),
        new Vector3(0.90357f,-0.13300f,-0.40728f), new Vector3(0.90352f,-0.13294f,-0.40740f), new Vector3(0.90351f,-0.13300f,-0.40740f), new Vector3(0.90353f,-0.13281f,-0.40743f),
        new Vector3(0.90326f,-0.13269f,-0.40806f), new Vector3(0.90256f,-0.13299f,-0.40951f), new Vector3(0.90212f,-0.13308f,-0.41045f), new Vector3(0.90165f,-0.13291f,-0.41155f),
        new Vector3(0.90086f,-0.13433f,-0.41281f), new Vector3(0.90076f,-0.13264f,-0.41357f), new Vector3(0.90050f,-0.13390f,-0.41372f), new Vector3(0.90007f,-0.13297f,-0.41496f),
        new Vector3(0.89933f,-0.13167f,-0.41696f), new Vector3(0.90045f,-0.13144f,-0.41462f), new Vector3(0.90047f,-0.13281f,-0.41414f), new Vector3(0.89991f,-0.13397f,-0.41499f),
        new Vector3(0.90020f,-0.13369f,-0.41445f), new Vector3(0.90007f,-0.13365f,-0.41474f), new Vector3(0.90005f,-0.13411f,-0.41463f), new Vector3(0.90000f,-0.13429f,-0.41469f),
        new Vector3(0.90007f,-0.13391f,-0.41466f), new Vector3(0.90024f,-0.13418f,-0.41420f), new Vector3(0.90086f,-0.13072f,-0.41397f), new Vector3(0.90067f,-0.13038f,-0.41447f),
        new Vector3(0.90006f,-0.13243f,-0.41517f),
    };

    static readonly Vector3[] LegRightUpLegWalkYDir = {
        new Vector3(-0.23324f,-0.44667f,0.86376f), new Vector3(-0.22970f,-0.41810f,0.87888f), new Vector3(-0.20878f,-0.41845f,0.88392f), new Vector3(-0.19653f,-0.42935f,0.88150f),
        new Vector3(-0.18826f,-0.44939f,0.87327f), new Vector3(-0.17795f,-0.47707f,0.86066f), new Vector3(-0.15952f,-0.51188f,0.84412f), new Vector3(-0.14432f,-0.55110f,0.82187f),
        new Vector3(-0.13078f,-0.59062f,0.79628f), new Vector3(-0.12003f,-0.62912f,0.76798f), new Vector3(-0.11232f,-0.66562f,0.73779f), new Vector3(-0.10706f,-0.69983f,0.70624f),
        new Vector3(-0.10560f,-0.73120f,0.67393f), new Vector3(-0.10585f,-0.76071f,0.64040f), new Vector3(-0.10591f,-0.78884f,0.60540f), new Vector3(-0.10547f,-0.81591f,0.56848f),
        new Vector3(-0.10651f,-0.84194f,0.52896f), new Vector3(-0.11042f,-0.86265f,0.49359f), new Vector3(-0.11427f,-0.88071f,0.45967f), new Vector3(-0.11902f,-0.89675f,0.42624f),
        new Vector3(-0.12380f,-0.91056f,0.39439f), new Vector3(-0.12782f,-0.92371f,0.36114f), new Vector3(-0.13196f,-0.93370f,0.33285f), new Vector3(-0.13491f,-0.94253f,0.30566f),
        new Vector3(-0.13709f,-0.94972f,0.28148f), new Vector3(-0.13932f,-0.95361f,0.26687f), new Vector3(-0.14281f,-0.95915f,0.24421f), new Vector3(-0.14676f,-0.96349f,0.22392f),
        new Vector3(-0.15571f,-0.97063f,0.18342f), new Vector3(-0.15898f,-0.97348f,0.16449f), new Vector3(-0.16318f,-0.97188f,0.16974f), new Vector3(-0.17177f,-0.97283f,0.15523f),
        new Vector3(-0.18088f,-0.97442f,0.13341f), new Vector3(-0.19165f,-0.97481f,0.11413f), new Vector3(-0.20450f,-0.97412f,0.09627f), new Vector3(-0.22095f,-0.97147f,0.08612f),
        new Vector3(-0.24695f,-0.96476f,0.09084f), new Vector3(-0.28687f,-0.95077f,0.11720f), new Vector3(-0.33596f,-0.93012f,0.14836f), new Vector3(-0.38267f,-0.90756f,0.17290f),
        new Vector3(-0.42198f,-0.88629f,0.19086f), new Vector3(-0.45084f,-0.86961f,0.20131f), new Vector3(-0.46468f,-0.86338f,0.19660f), new Vector3(-0.46331f,-0.86661f,0.18531f),
        new Vector3(-0.45529f,-0.87445f,0.16747f), new Vector3(-0.44802f,-0.88251f,0.14304f), new Vector3(-0.44753f,-0.88717f,0.11250f), new Vector3(-0.45372f,-0.88600f,0.09560f),
        new Vector3(-0.46116f,-0.88433f,0.07276f), new Vector3(-0.47060f,-0.88044f,0.05797f), new Vector3(-0.46531f,-0.88515f,0.00138f), new Vector3(-0.45731f,-0.88765f,-0.05421f),
        new Vector3(-0.44860f,-0.88771f,-0.10361f), new Vector3(-0.46843f,-0.88030f,-0.07519f), new Vector3(-0.48557f,-0.87292f,-0.04725f), new Vector3(-0.49989f,-0.86595f,-0.01582f),
        new Vector3(-0.51001f,-0.85989f,0.02190f), new Vector3(-0.52092f,-0.85069f,0.07055f), new Vector3(-0.52969f,-0.83964f,0.12019f), new Vector3(-0.53654f,-0.82565f,0.17442f),
        new Vector3(-0.53774f,-0.81168f,0.22803f), new Vector3(-0.53106f,-0.79887f,0.28247f), new Vector3(-0.52277f,-0.78184f,0.33976f), new Vector3(-0.50937f,-0.76269f,0.39856f),
        new Vector3(-0.49242f,-0.73793f,0.46150f), new Vector3(-0.47249f,-0.71904f,0.50964f), new Vector3(-0.44998f,-0.69595f,0.55962f), new Vector3(-0.41962f,-0.67595f,0.60581f),
        new Vector3(-0.38719f,-0.65812f,0.64572f), new Vector3(-0.35670f,-0.64107f,0.67955f), new Vector3(-0.32291f,-0.62186f,0.71346f), new Vector3(-0.28574f,-0.60602f,0.74236f),
        new Vector3(-0.25564f,-0.60184f,0.75660f), new Vector3(-0.22438f,-0.60464f,0.76424f), new Vector3(-0.19850f,-0.61535f,0.76285f), new Vector3(-0.18352f,-0.63212f,0.75282f),
        new Vector3(-0.18455f,-0.65240f,0.73507f), new Vector3(-0.20106f,-0.68032f,0.70480f), new Vector3(-0.20819f,-0.70823f,0.67459f), new Vector3(-0.20772f,-0.66268f,0.71952f),
        new Vector3(-0.20156f,-0.58751f,0.78371f),
    };

    static readonly Vector3[] LegRightUpLegWalkXDir = {
        new Vector3(0.97117f,-0.15193f,0.18368f), new Vector3(0.97269f,-0.12948f,0.19262f), new Vector3(0.97752f,-0.11660f,0.17570f), new Vector3(0.98027f,-0.10533f,0.16725f),
        new Vector3(0.98208f,-0.09429f,0.16320f), new Vector3(0.98403f,-0.08312f,0.15738f), new Vector3(0.98713f,-0.07280f,0.14240f), new Vector3(0.98938f,-0.06565f,0.12971f),
        new Vector3(0.99110f,-0.05767f,0.12001f), new Vector3(0.99219f,-0.04958f,0.11446f), new Vector3(0.99271f,-0.04246f,0.11282f), new Vector3(0.99284f,-0.03738f,0.11346f),
        new Vector3(0.99226f,-0.03291f,0.11977f), new Vector3(0.99153f,-0.03201f,0.12586f), new Vector3(0.99098f,-0.03349f,0.12972f), new Vector3(0.99076f,-0.03717f,0.13046f),
        new Vector3(0.99074f,-0.04478f,0.12821f), new Vector3(0.98984f,-0.05071f,0.13282f), new Vector3(0.98877f,-0.05595f,0.13861f), new Vector3(0.98761f,-0.06272f,0.14383f),
        new Vector3(0.98657f,-0.07028f,0.14743f), new Vector3(0.98586f,-0.07855f,0.14800f), new Vector3(0.98504f,-0.08598f,0.14934f), new Vector3(0.98436f,-0.09223f,0.15008f),
        new Vector3(0.98377f,-0.09733f,0.15074f), new Vector3(0.98323f,-0.10119f,0.15171f), new Vector3(0.98226f,-0.10705f,0.15394f), new Vector3(0.98154f,-0.11377f,0.15376f),
        new Vector3(0.97953f,-0.12774f,0.15555f), new Vector3(0.97936f,-0.13444f,0.15091f), new Vector3(0.97815f,-0.13691f,0.15645f), new Vector3(0.97592f,-0.14653f,0.16157f),
        new Vector3(0.97420f,-0.15889f,0.16029f), new Vector3(0.97204f,-0.17244f,0.15940f), new Vector3(0.96917f,-0.18768f,0.15964f), new Vector3(0.96484f,-0.20485f,0.16469f),
        new Vector3(0.95542f,-0.22675f,0.18909f), new Vector3(0.94110f,-0.25684f,0.21993f), new Vector3(0.92174f,-0.29228f,0.25488f), new Vector3(0.90190f,-0.32638f,0.28294f),
        new Vector3(0.88514f,-0.35722f,0.29819f), new Vector3(0.87332f,-0.38310f,0.30090f), new Vector3(0.86832f,-0.40080f,0.29222f), new Vector3(0.87203f,-0.40859f,0.26949f),
        new Vector3(0.87865f,-0.41090f,0.24319f), new Vector3(0.88329f,-0.41222f,0.22333f), new Vector3(0.88202f,-0.41713f,0.21920f), new Vector3(0.87657f,-0.42439f,0.22699f),
        new Vector3(0.86825f,-0.43282f,0.24249f), new Vector3(0.85833f,-0.44157f,0.26131f), new Vector3(0.84966f,-0.44621f,0.28102f), new Vector3(0.83995f,-0.45115f,0.30158f),
        new Vector3(0.82797f,-0.45643f,0.32579f), new Vector3(0.81430f,-0.46319f,0.34982f), new Vector3(0.80152f,-0.46613f,0.37455f), new Vector3(0.79197f,-0.46442f,0.39635f),
        new Vector3(0.78935f,-0.45775f,0.40914f), new Vector3(0.79030f,-0.44940f,0.41648f), new Vector3(0.79277f,-0.43970f,0.42212f), new Vector3(0.79849f,-0.42985f,0.42148f),
        new Vector3(0.80868f,-0.42006f,0.41181f), new Vector3(0.82388f,-0.40893f,0.39243f), new Vector3(0.83803f,-0.39826f,0.37296f), new Vector3(0.85335f,-0.38790f,0.34832f),
        new Vector3(0.86810f,-0.37821f,0.32150f), new Vector3(0.88112f,-0.37267f,0.29110f), new Vector3(0.89276f,-0.36638f,0.26221f), new Vector3(0.90558f,-0.35738f,0.22851f),
        new Vector3(0.91664f,-0.35016f,0.19276f), new Vector3(0.92467f,-0.34600f,0.15895f), new Vector3(0.93133f,-0.34290f,0.12265f), new Vector3(0.93780f,-0.33623f,0.08648f),
        new Vector3(0.94231f,-0.33005f,0.05586f), new Vector3(0.94681f,-0.32087f,0.02412f), new Vector3(0.95146f,-0.30777f,-0.00069f), new Vector3(0.95683f,-0.29044f,-0.01062f),
        new Vector3(0.96329f,-0.26845f,0.00359f), new Vector3(0.97082f,-0.23441f,0.05069f), new Vector3(0.97588f,-0.19674f,0.09462f), new Vector3(0.97729f,-0.17207f,0.12365f),
        new Vector3(0.97902f,-0.14522f,0.14292f),
    };

    static readonly Vector3[] LegRightLegWalkYDir = {
        new Vector3(-0.21294f,-0.97693f,-0.01636f), new Vector3(-0.20676f,-0.97559f,-0.07398f), new Vector3(-0.22007f,-0.97049f,-0.09854f), new Vector3(-0.22902f,-0.96472f,-0.12983f),
        new Vector3(-0.23606f,-0.95794f,-0.16316f), new Vector3(-0.24518f,-0.94952f,-0.19568f), new Vector3(-0.26135f,-0.93767f,-0.22905f), new Vector3(-0.27752f,-0.92589f,-0.25634f),
        new Vector3(-0.29098f,-0.91408f,-0.28245f), new Vector3(-0.30098f,-0.90352f,-0.30505f), new Vector3(-0.30758f,-0.89458f,-0.32421f), new Vector3(-0.31148f,-0.88645f,-0.34232f),
        new Vector3(-0.31066f,-0.88003f,-0.35922f), new Vector3(-0.30979f,-0.87289f,-0.37697f), new Vector3(-0.30987f,-0.86488f,-0.39491f), new Vector3(-0.31081f,-0.85599f,-0.41314f),
        new Vector3(-0.31224f,-0.84616f,-0.43188f), new Vector3(-0.30774f,-0.83869f,-0.44934f), new Vector3(-0.30138f,-0.83185f,-0.46604f), new Vector3(-0.29502f,-0.82419f,-0.48340f),
        new Vector3(-0.28896f,-0.81620f,-0.50031f), new Vector3(-0.28337f,-0.80714f,-0.51791f), new Vector3(-0.27741f,-0.79879f,-0.53383f), new Vector3(-0.27134f,-0.79091f,-0.54848f),
        new Vector3(-0.26508f,-0.78264f,-0.56321f), new Vector3(-0.25978f,-0.77660f,-0.57394f), new Vector3(-0.25194f,-0.76574f,-0.59175f), new Vector3(-0.24577f,-0.75701f,-0.60542f),
        new Vector3(-0.23713f,-0.74037f,-0.62898f), new Vector3(-0.23160f,-0.73065f,-0.64227f), new Vector3(-0.22286f,-0.72923f,-0.64696f), new Vector3(-0.21281f,-0.72174f,-0.65864f),
        new Vector3(-0.20440f,-0.71143f,-0.67238f), new Vector3(-0.19557f,-0.69972f,-0.68713f), new Vector3(-0.18540f,-0.68459f,-0.70495f), new Vector3(-0.17245f,-0.66594f,-0.72580f),
        new Vector3(-0.15111f,-0.64496f,-0.74913f), new Vector3(-0.12505f,-0.61544f,-0.77820f), new Vector3(-0.09313f,-0.58164f,-0.80810f), new Vector3(-0.05898f,-0.54539f,-0.83611f),
        new Vector3(-0.02983f,-0.51087f,-0.85914f), new Vector3(-0.01309f,-0.48685f,-0.87339f), new Vector3(-0.00896f,-0.48099f,-0.87668f), new Vector3(-0.01787f,-0.48349f,-0.87517f),
        new Vector3(-0.03248f,-0.49498f,-0.86830f), new Vector3(-0.04617f,-0.51585f,-0.85543f), new Vector3(-0.05370f,-0.54603f,-0.83604f), new Vector3(-0.05428f,-0.57715f,-0.81484f),
        new Vector3(-0.05044f,-0.60489f,-0.79471f), new Vector3(-0.04526f,-0.63130f,-0.77422f), new Vector3(-0.01595f,-0.61938f,-0.78493f), new Vector3(0.01312f,-0.60420f,-0.79673f),
        new Vector3(0.04179f,-0.58732f,-0.80827f), new Vector3(0.06615f,-0.58324f,-0.80960f), new Vector3(0.09237f,-0.57850f,-0.81043f), new Vector3(0.11738f,-0.57497f,-0.80971f),
        new Vector3(0.13623f,-0.57429f,-0.80724f), new Vector3(0.14639f,-0.58088f,-0.80072f), new Vector3(0.15263f,-0.59160f,-0.79165f), new Vector3(0.15136f,-0.60752f,-0.77975f),
        new Vector3(0.14262f,-0.62559f,-0.76700f), new Vector3(0.12848f,-0.64528f,-0.75307f), new Vector3(0.11177f,-0.66674f,-0.73686f), new Vector3(0.09139f,-0.68989f,-0.71813f),
        new Vector3(0.06614f,-0.71994f,-0.69088f), new Vector3(0.03714f,-0.74332f,-0.66791f), new Vector3(0.00723f,-0.77193f,-0.63567f), new Vector3(-0.02681f,-0.79844f,-0.60148f),
        new Vector3(-0.06414f,-0.82256f,-0.56505f), new Vector3(-0.10372f,-0.84636f,-0.52242f), new Vector3(-0.14782f,-0.87366f,-0.46353f), new Vector3(-0.19015f,-0.89868f,-0.39524f),
        new Vector3(-0.22544f,-0.91909f,-0.32318f), new Vector3(-0.25808f,-0.93642f,-0.23772f), new Vector3(-0.28013f,-0.94922f,-0.14318f), new Vector3(-0.28549f,-0.95743f,-0.04274f),
        new Vector3(-0.26957f,-0.96104f,0.06108f), new Vector3(-0.23359f,-0.95961f,0.15681f), new Vector3(-0.20915f,-0.93790f,0.27677f), new Vector3(-0.20616f,-0.94645f,0.24847f),
        new Vector3(-0.20318f,-0.96112f,0.18699f),
    };

    static readonly Vector3[] LegRightLegWalkXDir = {
        new Vector3(0.96508f,-0.21291f,0.15264f), new Vector3(0.96156f,-0.21658f,0.16881f), new Vector3(0.96061f,-0.23318f,0.15121f), new Vector3(0.95889f,-0.24655f,0.14050f),
        new Vector3(0.95708f,-0.25826f,0.13153f), new Vector3(0.95527f,-0.27104f,0.11828f), new Vector3(0.95317f,-0.28813f,0.09192f), new Vector3(0.95056f,-0.30333f,0.06654f),
        new Vector3(0.94811f,-0.31505f,0.04283f), new Vector3(0.94611f,-0.32301f,0.02321f), new Vector3(0.94471f,-0.32779f,0.00821f), new Vector3(0.94391f,-0.33017f,-0.00387f),
        new Vector3(0.94407f,-0.32962f,-0.00893f), new Vector3(0.94417f,-0.32917f,-0.01370f), new Vector3(0.94417f,-0.32879f,-0.02079f), new Vector3(0.94421f,-0.32788f,-0.03102f),
        new Vector3(0.94426f,-0.32635f,-0.04327f), new Vector3(0.94558f,-0.32205f,-0.04649f), new Vector3(0.94735f,-0.31667f,-0.04740f), new Vector3(0.94901f,-0.31160f,-0.04792f),
        new Vector3(0.95058f,-0.30661f,-0.04881f), new Vector3(0.95227f,-0.30076f,-0.05230f), new Vector3(0.95386f,-0.29541f,-0.05364f), new Vector3(0.95556f,-0.28962f,-0.05509f),
        new Vector3(0.95726f,-0.28368f,-0.05634f), new Vector3(0.95856f,-0.27937f,-0.05584f), new Vector3(0.96024f,-0.27381f,-0.05450f), new Vector3(0.96159f,-0.26918f,-0.05378f),
        new Vector3(0.96293f,-0.26484f,-0.05129f), new Vector3(0.96437f,-0.25924f,-0.05283f), new Vector3(0.96550f,-0.25680f,-0.04312f), new Vector3(0.96639f,-0.25499f,-0.03283f),
        new Vector3(0.96721f,-0.25257f,-0.02679f), new Vector3(0.96754f,-0.25201f,-0.01876f), new Vector3(0.96727f,-0.25362f,-0.00810f), new Vector3(0.96567f,-0.25962f,0.00877f),
        new Vector3(0.96039f,-0.27528f,0.04328f), new Vector3(0.94969f,-0.30126f,0.08565f), new Vector3(0.93342f,-0.33346f,0.13244f), new Vector3(0.91461f,-0.36514f,0.17366f),
        new Vector3(0.89673f,-0.39338f,0.20278f), new Vector3(0.88275f,-0.41590f,0.21860f), new Vector3(0.87640f,-0.42593f,0.22473f), new Vector3(0.87989f,-0.42330f,0.21589f),
        new Vector3(0.88768f,-0.41353f,0.20253f), new Vector3(0.89505f,-0.40162f,0.19388f), new Vector3(0.89808f,-0.39244f,0.19862f), new Vector3(0.89622f,-0.38797f,0.21509f),
        new Vector3(0.89132f,-0.38624f,0.23741f), new Vector3(0.88408f,-0.38617f,0.26320f), new Vector3(0.87310f,-0.39121f,0.29096f), new Vector3(0.85997f,-0.39972f,0.31729f),
        new Vector3(0.84482f,-0.41113f,0.34242f), new Vector3(0.82848f,-0.42009f,0.37033f), new Vector3(0.81247f,-0.42674f,0.39722f), new Vector3(0.79933f,-0.42914f,0.42061f),
        new Vector3(0.79304f,-0.42514f,0.43629f), new Vector3(0.79092f,-0.41743f,0.44743f), new Vector3(0.79080f,-0.40733f,0.45687f), new Vector3(0.79453f,-0.39452f,0.46160f),
        new Vector3(0.80379f,-0.37898f,0.45858f), new Vector3(0.81941f,-0.35867f,0.44713f), new Vector3(0.83485f,-0.33919f,0.43355f), new Vector3(0.85275f,-0.31820f,0.41421f),
        new Vector3(0.87201f,-0.29485f,0.39073f), new Vector3(0.89041f,-0.27880f,0.35979f), new Vector3(0.90811f,-0.26107f,0.32737f), new Vector3(0.92662f,-0.24559f,0.28470f),
        new Vector3(0.94241f,-0.23617f,0.23682f), new Vector3(0.95401f,-0.23319f,0.18839f), new Vector3(0.96291f,-0.23410f,0.13416f), new Vector3(0.96773f,-0.23937f,0.07870f),
        new Vector3(0.96821f,-0.24824f,0.03059f), new Vector3(0.96505f,-0.26147f,-0.01773f), new Vector3(0.95987f,-0.27493f,-0.05534f), new Vector3(0.95661f,-0.28198f,-0.07333f),
        new Vector3(0.95973f,-0.27333f,-0.06492f), new Vector3(0.97047f,-0.24007f,-0.02345f), new Vector3(0.97706f,-0.21206f,0.01973f), new Vector3(0.97851f,-0.19841f,0.05611f),
        new Vector3(0.97778f,-0.18909f,0.09052f),
    };

    static readonly Vector3[] LegRightFootWalkYDir = {
        new Vector3(-0.15175f,-0.58990f,0.79309f), new Vector3(-0.15163f,-0.59044f,0.79271f), new Vector3(-0.15033f,-0.59038f,0.79300f), new Vector3(-0.14957f,-0.59067f,0.79293f),
        new Vector3(-0.14841f,-0.59119f,0.79276f), new Vector3(-0.14700f,-0.59196f,0.79245f), new Vector3(-0.14550f,-0.59288f,0.79204f), new Vector3(-0.14440f,-0.59382f,0.79153f),
        new Vector3(-0.14324f,-0.59480f,0.79101f), new Vector3(-0.14211f,-0.59570f,0.79054f), new Vector3(-0.14097f,-0.59729f,0.78954f), new Vector3(-0.14053f,-0.59701f,0.78983f),
        new Vector3(-0.13970f,-0.59746f,0.78963f), new Vector3(-0.13932f,-0.59738f,0.78977f), new Vector3(-0.13882f,-0.59838f,0.78910f), new Vector3(-0.13869f,-0.59827f,0.78920f),
        new Vector3(-0.13852f,-0.59944f,0.78834f), new Vector3(-0.13879f,-0.59951f,0.78824f), new Vector3(-0.13858f,-0.60010f,0.78783f), new Vector3(-0.13878f,-0.60031f,0.78763f),
        new Vector3(-0.13907f,-0.60058f,0.78737f), new Vector3(-0.13924f,-0.60174f,0.78646f), new Vector3(-0.13948f,-0.60226f,0.78602f), new Vector3(-0.13969f,-0.60303f,0.78539f),
        new Vector3(-0.13988f,-0.60403f,0.78459f), new Vector3(-0.14009f,-0.60235f,0.78585f), new Vector3(-0.14011f,-0.60708f,0.78219f), new Vector3(-0.14058f,-0.60712f,0.78208f),
        new Vector3(-0.14332f,-0.61699f,0.77381f), new Vector3(-0.13812f,-0.61974f,0.77256f), new Vector3(-0.13983f,-0.61163f,0.77869f), new Vector3(-0.14297f,-0.61007f,0.77934f),
        new Vector3(-0.14363f,-0.61163f,0.77800f), new Vector3(-0.14469f,-0.61221f,0.77734f), new Vector3(-0.14554f,-0.61354f,0.77614f), new Vector3(-0.14608f,-0.61844f,0.77214f),
        new Vector3(-0.14911f,-0.61329f,0.77565f), new Vector3(-0.15144f,-0.61385f,0.77476f), new Vector3(-0.15419f,-0.61477f,0.77349f), new Vector3(-0.15715f,-0.61506f,0.77266f),
        new Vector3(-0.15935f,-0.61545f,0.77190f), new Vector3(-0.16047f,-0.61565f,0.77151f), new Vector3(-0.16198f,-0.61531f,0.77146f), new Vector3(-0.16323f,-0.61517f,0.77131f),
        new Vector3(-0.16359f,-0.61491f,0.77144f), new Vector3(-0.16341f,-0.61473f,0.77162f), new Vector3(-0.16443f,-0.61514f,0.77108f), new Vector3(-0.17485f,-0.65258f,0.73727f),
        new Vector3(-0.18943f,-0.70260f,0.68591f), new Vector3(-0.20835f,-0.75134f,0.62616f), new Vector3(-0.22805f,-0.79442f,0.56293f), new Vector3(-0.24260f,-0.82789f,0.50571f),
        new Vector3(-0.26027f,-0.83243f,0.48921f), new Vector3(-0.29198f,-0.82063f,0.49124f), new Vector3(-0.33160f,-0.80577f,0.49069f), new Vector3(-0.37160f,-0.79341f,0.48210f),
        new Vector3(-0.40407f,-0.78648f,0.46708f), new Vector3(-0.42083f,-0.77910f,0.46466f), new Vector3(-0.43503f,-0.76860f,0.46904f), new Vector3(-0.44834f,-0.75358f,0.48074f),
        new Vector3(-0.45962f,-0.73747f,0.49486f), new Vector3(-0.46643f,-0.72382f,0.50845f), new Vector3(-0.46660f,-0.71129f,0.52570f), new Vector3(-0.46316f,-0.70128f,0.54193f),
        new Vector3(-0.45681f,-0.69162f,0.55945f), new Vector3(-0.44968f,-0.68875f,0.56869f), new Vector3(-0.44099f,-0.68374f,0.58140f), new Vector3(-0.42709f,-0.68430f,0.59104f),
        new Vector3(-0.40893f,-0.68838f,0.59909f), new Vector3(-0.39093f,-0.69374f,0.60490f), new Vector3(-0.36829f,-0.69855f,0.61351f), new Vector3(-0.34002f,-0.70478f,0.62263f),
        new Vector3(-0.30154f,-0.70989f,0.63650f), new Vector3(-0.25321f,-0.70819f,0.65906f), new Vector3(-0.20086f,-0.69726f,0.68810f), new Vector3(-0.15377f,-0.67538f,0.72126f),
        new Vector3(-0.12547f,-0.64338f,0.75520f), new Vector3(-0.13973f,-0.58748f,0.79709f), new Vector3(-0.15670f,-0.56098f,0.81287f), new Vector3(-0.16580f,-0.57384f,0.80200f),
        new Vector3(-0.16299f,-0.59532f,0.78678f),
    };

    static readonly Vector3[] LegRightFootWalkXDir = {
        new Vector3(0.96470f,0.08632f,0.24879f), new Vector3(0.96495f,0.08537f,0.24816f), new Vector3(0.96517f,0.08610f,0.24707f), new Vector3(0.96522f,0.08668f,0.24664f),
        new Vector3(0.96536f,0.08736f,0.24586f), new Vector3(0.96556f,0.08800f,0.24485f), new Vector3(0.96570f,0.08891f,0.24395f), new Vector3(0.96582f,0.08943f,0.24329f),
        new Vector3(0.96595f,0.08996f,0.24257f), new Vector3(0.96607f,0.09054f,0.24189f), new Vector3(0.96616f,0.09100f,0.24134f), new Vector3(0.96621f,0.09139f,0.24100f),
        new Vector3(0.96632f,0.09180f,0.24041f), new Vector3(0.96636f,0.09211f,0.24014f), new Vector3(0.96640f,0.09222f,0.23994f), new Vector3(0.96641f,0.09235f,0.23984f),
        new Vector3(0.96637f,0.09231f,0.24000f), new Vector3(0.96653f,0.09143f,0.23971f), new Vector3(0.96640f,0.09194f,0.24002f), new Vector3(0.96632f,0.09196f,0.24035f),
        new Vector3(0.96623f,0.09189f,0.24075f), new Vector3(0.96619f,0.09149f,0.24106f), new Vector3(0.96616f,0.09117f,0.24130f), new Vector3(0.96611f,0.09087f,0.24160f),
        new Vector3(0.96605f,0.09059f,0.24197f), new Vector3(0.96611f,0.09062f,0.24169f), new Vector3(0.96587f,0.09004f,0.24289f), new Vector3(0.96577f,0.08984f,0.24333f),
        new Vector3(0.96553f,0.08447f,0.24618f), new Vector3(0.96693f,0.08450f,0.24065f), new Vector3(0.96620f,0.08771f,0.24240f), new Vector3(0.96540f,0.08757f,0.24565f),
        new Vector3(0.96531f,0.08664f,0.24632f), new Vector3(0.96511f,0.08597f,0.24735f), new Vector3(0.96489f,0.08535f,0.24840f), new Vector3(0.96486f,0.08325f,0.24922f),
        new Vector3(0.96443f,0.08294f,0.25098f), new Vector3(0.96403f,0.08148f,0.25299f), new Vector3(0.96356f,0.07962f,0.25537f), new Vector3(0.96310f,0.07765f,0.25769f),
        new Vector3(0.96284f,0.07582f,0.25922f), new Vector3(0.96268f,0.07497f,0.26004f), new Vector3(0.96246f,0.07403f,0.26112f), new Vector3(0.96230f,0.07314f,0.26198f),
        new Vector3(0.96230f,0.07279f,0.26208f), new Vector3(0.96232f,0.07298f,0.26193f), new Vector3(0.96211f,0.07234f,0.26287f), new Vector3(0.95617f,0.06610f,0.28527f),
        new Vector3(0.94478f,0.05983f,0.32221f), new Vector3(0.93129f,0.04319f,0.36170f), new Vector3(0.91937f,0.01465f,0.39312f), new Vector3(0.91310f,-0.01877f,0.40730f),
        new Vector3(0.90531f,-0.03423f,0.42338f), new Vector3(0.89828f,-0.05893f,0.43546f), new Vector3(0.88788f,-0.09072f,0.45103f), new Vector3(0.87424f,-0.12427f,0.46933f),
        new Vector3(0.86038f,-0.15339f,0.48602f), new Vector3(0.85455f,-0.16859f,0.49125f), new Vector3(0.85083f,-0.18040f,0.49351f), new Vector3(0.84910f,-0.19098f,0.49250f),
        new Vector3(0.84880f,-0.20080f,0.48911f), new Vector3(0.85025f,-0.20834f,0.48339f), new Vector3(0.85367f,-0.20668f,0.47804f), new Vector3(0.85811f,-0.20191f,0.47210f),
        new Vector3(0.86272f,-0.19109f,0.46819f), new Vector3(0.86599f,-0.18026f,0.46645f), new Vector3(0.86927f,-0.16416f,0.46628f), new Vector3(0.87522f,-0.14865f,0.46033f),
        new Vector3(0.88366f,-0.13479f,0.44830f), new Vector3(0.89159f,-0.12222f,0.43604f), new Vector3(0.90188f,-0.10818f,0.41823f), new Vector3(0.91373f,-0.09099f,0.39600f),
        new Vector3(0.92876f,-0.06775f,0.36443f), new Vector3(0.94474f,-0.03438f,0.32602f), new Vector3(0.95853f,0.00509f,0.28495f), new Vector3(0.96822f,0.04273f,0.24644f),
        new Vector3(0.97366f,0.06623f,0.21818f), new Vector3(0.97211f,0.07173f,0.22328f), new Vector3(0.96513f,0.08778f,0.24663f), new Vector3(0.95990f,0.09252f,0.26464f),
        new Vector3(0.95763f,0.09644f,0.27136f),
    };

    static readonly Vector3[] LegRightToeWalkYDir = {
        new Vector3(-0.30913f,0.09713f,0.94605f), new Vector3(-0.31173f,0.09377f,0.94553f), new Vector3(-0.31123f,0.09433f,0.94564f), new Vector3(-0.31130f,0.09450f,0.94560f),
        new Vector3(-0.31101f,0.09436f,0.94571f), new Vector3(-0.31054f,0.09440f,0.94586f), new Vector3(-0.31024f,0.09424f,0.94598f), new Vector3(-0.31006f,0.09414f,0.94604f),
        new Vector3(-0.30976f,0.09400f,0.94616f), new Vector3(-0.30947f,0.09383f,0.94627f), new Vector3(-0.30921f,0.09282f,0.94645f), new Vector3(-0.30921f,0.09373f,0.94636f),
        new Vector3(-0.30888f,0.09375f,0.94647f), new Vector3(-0.30885f,0.09445f,0.94641f), new Vector3(-0.30870f,0.09372f,0.94653f), new Vector3(-0.30876f,0.09435f,0.94645f),
        new Vector3(-0.30890f,0.09343f,0.94649f), new Vector3(-0.30848f,0.09396f,0.94658f), new Vector3(-0.30872f,0.09366f,0.94653f), new Vector3(-0.30893f,0.09389f,0.94644f),
        new Vector3(-0.30920f,0.09420f,0.94632f), new Vector3(-0.30928f,0.09345f,0.94637f), new Vector3(-0.30933f,0.09349f,0.94635f), new Vector3(-0.30946f,0.09339f,0.94632f),
        new Vector3(-0.30962f,0.09300f,0.94630f), new Vector3(-0.30940f,0.09596f,0.94608f), new Vector3(-0.31005f,0.09099f,0.94636f), new Vector3(-0.31033f,0.09180f,0.94619f),
        new Vector3(-0.31163f,0.08094f,0.94675f), new Vector3(-0.30574f,0.07823f,0.94890f), new Vector3(-0.30809f,0.08883f,0.94720f), new Vector3(-0.31102f,0.09149f,0.94599f),
        new Vector3(-0.31105f,0.09035f,0.94609f), new Vector3(-0.31153f,0.09038f,0.94593f), new Vector3(-0.31195f,0.08929f,0.94589f), new Vector3(-0.31169f,0.08386f,0.94648f),
        new Vector3(-0.31299f,0.09090f,0.94540f), new Vector3(-0.31373f,0.09088f,0.94515f), new Vector3(-0.31444f,0.09053f,0.94495f), new Vector3(-0.31523f,0.09099f,0.94464f),
        new Vector3(-0.31546f,0.09130f,0.94454f), new Vector3(-0.31511f,0.09116f,0.94467f), new Vector3(-0.31560f,0.09168f,0.94445f), new Vector3(-0.31607f,0.09193f,0.94427f),
        new Vector3(-0.31603f,0.09227f,0.94425f), new Vector3(-0.31585f,0.09244f,0.94429f), new Vector3(-0.31659f,0.09196f,0.94410f), new Vector3(-0.31649f,0.09442f,0.94389f),
        new Vector3(-0.31533f,0.09835f,0.94387f), new Vector3(-0.32407f,0.05336f,0.94453f), new Vector3(-0.34266f,-0.04740f,0.93826f), new Vector3(-0.35833f,-0.14972f,0.92151f),
        new Vector3(-0.37628f,-0.16124f,0.91237f), new Vector3(-0.39295f,-0.13760f,0.90921f), new Vector3(-0.41368f,-0.11269f,0.90342f), new Vector3(-0.43746f,-0.09713f,0.89398f),
        new Vector3(-0.45945f,-0.08732f,0.88390f), new Vector3(-0.46884f,-0.08415f,0.87927f), new Vector3(-0.47401f,-0.07417f,0.87739f), new Vector3(-0.47400f,-0.05269f,0.87895f),
        new Vector3(-0.47054f,-0.02996f,0.88187f), new Vector3(-0.46424f,-0.00886f,0.88567f), new Vector3(-0.45632f,0.01533f,0.88968f), new Vector3(-0.44978f,0.02951f,0.89265f),
        new Vector3(-0.44448f,0.04635f,0.89459f), new Vector3(-0.44295f,0.05592f,0.89480f), new Vector3(-0.44370f,0.06404f,0.89389f), new Vector3(-0.43944f,0.06688f,0.89578f),
        new Vector3(-0.42996f,0.06425f,0.90056f), new Vector3(-0.42055f,0.05934f,0.90533f), new Vector3(-0.40374f,0.06342f,0.91267f), new Vector3(-0.37809f,0.09307f,0.92108f),
        new Vector3(-0.35058f,0.08182f,0.93295f), new Vector3(-0.31541f,0.08237f,0.94537f), new Vector3(-0.27769f,0.10018f,0.95543f), new Vector3(-0.24388f,0.13447f,0.96044f),
        new Vector3(-0.22062f,0.17644f,0.95927f), new Vector3(-0.22815f,0.24912f,0.94122f), new Vector3(-0.25688f,0.23326f,0.93787f), new Vector3(-0.27598f,0.15297f,0.94891f),
        new Vector3(-0.28156f,0.07730f,0.95642f),
    };

    static readonly Vector3[] LegRightToeWalkXDir = {
        new Vector3(0.94418f,0.15045f,0.29307f), new Vector3(0.94400f,0.14382f,0.29696f), new Vector3(0.94417f,0.14387f,0.29640f), new Vector3(0.94416f,0.14378f,0.29646f),
        new Vector3(0.94425f,0.14378f,0.29618f), new Vector3(0.94441f,0.14368f,0.29573f), new Vector3(0.94448f,0.14386f,0.29542f), new Vector3(0.94454f,0.14378f,0.29526f),
        new Vector3(0.94463f,0.14377f,0.29498f), new Vector3(0.94470f,0.14385f,0.29469f), new Vector3(0.94476f,0.14378f,0.29455f), new Vector3(0.94478f,0.14382f,0.29445f),
        new Vector3(0.94488f,0.14386f,0.29411f), new Vector3(0.94490f,0.14396f,0.29399f), new Vector3(0.94494f,0.14383f,0.29394f), new Vector3(0.94493f,0.14395f,0.29392f),
        new Vector3(0.94487f,0.14385f,0.29417f), new Vector3(0.94510f,0.14310f,0.29379f), new Vector3(0.94494f,0.14372f,0.29398f), new Vector3(0.94487f,0.14389f,0.29414f),
        new Vector3(0.94478f,0.14398f,0.29436f), new Vector3(0.94476f,0.14379f,0.29455f), new Vector3(0.94475f,0.14374f,0.29460f), new Vector3(0.94471f,0.14369f,0.29475f),
        new Vector3(0.94465f,0.14363f,0.29497f), new Vector3(0.94477f,0.14410f,0.29436f), new Vector3(0.94446f,0.14350f,0.29563f), new Vector3(0.94438f,0.14369f,0.29579f),
        new Vector3(0.94421f,0.13816f,0.29898f), new Vector3(0.94591f,0.13856f,0.29335f), new Vector3(0.94505f,0.14299f,0.29398f), new Vector3(0.94415f,0.14374f,0.29651f),
        new Vector3(0.94412f,0.14357f,0.29669f), new Vector3(0.94395f,0.14371f,0.29715f), new Vector3(0.94376f,0.14390f,0.29766f), new Vector3(0.94381f,0.14247f,0.29819f),
        new Vector3(0.94351f,0.14373f,0.29855f), new Vector3(0.94324f,0.14403f,0.29924f), new Vector3(0.94300f,0.14407f,0.29999f), new Vector3(0.94278f,0.14398f,0.30074f),
        new Vector3(0.94275f,0.14369f,0.30097f), new Vector3(0.94285f,0.14374f,0.30063f), new Vector3(0.94271f,0.14372f,0.30107f), new Vector3(0.94260f,0.14345f,0.30154f),
        new Vector3(0.94262f,0.14353f,0.30145f), new Vector3(0.94262f,0.14404f,0.30119f), new Vector3(0.94240f,0.14378f,0.30201f), new Vector3(0.94243f,0.14456f,0.30154f),
        new Vector3(0.94287f,0.14517f,0.29987f), new Vector3(0.93978f,0.13277f,0.31494f), new Vector3(0.93227f,0.10621f,0.34583f), new Vector3(0.92581f,0.07019f,0.37141f),
        new Vector3(0.91976f,0.05367f,0.38881f), new Vector3(0.91613f,0.02675f,0.39999f), new Vector3(0.90957f,-0.00826f,0.41547f), new Vector3(0.89924f,-0.04549f,0.43509f),
        new Vector3(0.88773f,-0.07777f,0.45375f), new Vector3(0.88209f,-0.09643f,0.46111f), new Vector3(0.87822f,-0.11176f,0.46501f), new Vector3(0.87630f,-0.12591f,0.46503f),
        new Vector3(0.87564f,-0.13912f,0.46249f), new Vector3(0.87633f,-0.14972f,0.45785f), new Vector3(0.87861f,-0.15041f,0.45323f), new Vector3(0.88122f,-0.14811f,0.44891f),
        new Vector3(0.88392f,-0.13936f,0.44639f), new Vector3(0.88536f,-0.12992f,0.44639f), new Vector3(0.88641f,-0.11553f,0.44826f), new Vector3(0.89017f,-0.10119f,0.44425f),
        new Vector3(0.89644f,-0.08824f,0.43429f), new Vector3(0.90234f,-0.07649f,0.42418f), new Vector3(0.91112f,-0.06243f,0.40739f), new Vector3(0.92273f,-0.04263f,0.38308f),
        new Vector3(0.93534f,-0.01975f,0.35321f), new Vector3(0.94887f,0.01376f,0.31538f), new Vector3(0.96037f,0.05402f,0.27346f), new Vector3(0.96810f,0.09250f,0.23287f),
        new Vector3(0.97245f,0.11570f,0.20237f), new Vector3(0.97168f,0.11928f,0.20396f), new Vector3(0.96401f,0.13066f,0.23155f), new Vector3(0.95745f,0.13047f,0.25743f),
        new Vector3(0.95390f,0.13047f,0.27027f),
    };

    static readonly Vector3[] SpineWalkYDir = {
        new Vector3(0.26143f,0.81548f,0.51638f), new Vector3(0.24512f,0.80668f,0.53776f), new Vector3(0.22003f,0.79704f,0.56242f), new Vector3(0.19965f,0.78841f,0.58185f),
        new Vector3(0.18019f,0.78257f,0.59591f), new Vector3(0.15974f,0.77958f,0.60558f), new Vector3(0.13875f,0.77864f,0.61194f), new Vector3(0.11797f,0.78205f,0.61194f),
        new Vector3(0.09883f,0.78622f,0.60999f), new Vector3(0.08128f,0.79284f,0.60398f), new Vector3(0.06398f,0.80184f,0.59410f), new Vector3(0.04461f,0.81136f,0.58285f),
        new Vector3(0.02733f,0.82247f,0.56815f), new Vector3(0.00803f,0.83558f,0.54930f), new Vector3(-0.00895f,0.85058f,0.52578f), new Vector3(-0.02424f,0.86672f,0.49820f),
        new Vector3(-0.04387f,0.88239f,0.46846f), new Vector3(-0.06850f,0.89487f,0.44104f), new Vector3(-0.08982f,0.90842f,0.40830f), new Vector3(-0.10751f,0.92134f,0.37361f),
        new Vector3(-0.12296f,0.93301f,0.33818f), new Vector3(-0.13673f,0.94249f,0.30500f), new Vector3(-0.14592f,0.95103f,0.27248f), new Vector3(-0.15101f,0.95747f,0.24584f),
        new Vector3(-0.15328f,0.96239f,0.22430f), new Vector3(-0.15387f,0.96601f,0.20771f), new Vector3(-0.15414f,0.96858f,0.19517f), new Vector3(-0.15598f,0.96940f,0.18956f),
        new Vector3(-0.15509f,0.96740f,0.20019f), new Vector3(-0.15603f,0.96577f,0.20725f), new Vector3(-0.15890f,0.96625f,0.20278f), new Vector3(-0.15959f,0.96491f,0.20850f),
        new Vector3(-0.16365f,0.96078f,0.22389f), new Vector3(-0.16874f,0.95467f,0.24523f), new Vector3(-0.17633f,0.94614f,0.27153f), new Vector3(-0.18593f,0.93383f,0.30560f),
        new Vector3(-0.19738f,0.92255f,0.33157f), new Vector3(-0.20861f,0.90604f,0.36822f), new Vector3(-0.21573f,0.88925f,0.40335f), new Vector3(-0.21809f,0.87014f,0.44192f),
        new Vector3(-0.21321f,0.84782f,0.48553f), new Vector3(-0.19482f,0.82415f,0.53181f), new Vector3(-0.17162f,0.79783f,0.57794f), new Vector3(-0.15336f,0.76972f,0.61969f),
        new Vector3(-0.13447f,0.74364f,0.65492f), new Vector3(-0.11340f,0.72108f,0.68351f), new Vector3(-0.09256f,0.70148f,0.70665f), new Vector3(-0.06872f,0.69338f,0.71729f),
        new Vector3(-0.04680f,0.68833f,0.72389f), new Vector3(-0.02481f,0.69017f,0.72323f), new Vector3(-0.00388f,0.69587f,0.71816f), new Vector3(0.01612f,0.70360f,0.71041f),
        new Vector3(0.03269f,0.71708f,0.69622f), new Vector3(0.04621f,0.73122f,0.68057f), new Vector3(0.05796f,0.74646f,0.66290f), new Vector3(0.06888f,0.76242f,0.64340f),
        new Vector3(0.07967f,0.77802f,0.62317f), new Vector3(0.09041f,0.79903f,0.59446f), new Vector3(0.10137f,0.81737f,0.56712f), new Vector3(0.11259f,0.83582f,0.53733f),
        new Vector3(0.12119f,0.85371f,0.50645f), new Vector3(0.12235f,0.86790f,0.48144f), new Vector3(0.12647f,0.88360f,0.45084f), new Vector3(0.13073f,0.89832f,0.41944f),
        new Vector3(0.13619f,0.91236f,0.38606f), new Vector3(0.14168f,0.92075f,0.36353f), new Vector3(0.14544f,0.92901f,0.34028f), new Vector3(0.14893f,0.93448f,0.32336f),
        new Vector3(0.15362f,0.93753f,0.31214f), new Vector3(0.15699f,0.94025f,0.30213f), new Vector3(0.16048f,0.94333f,0.29048f), new Vector3(0.16492f,0.94423f,0.28501f),
        new Vector3(0.16904f,0.94176f,0.29073f), new Vector3(0.17512f,0.93837f,0.29798f), new Vector3(0.18260f,0.93311f,0.30978f), new Vector3(0.19152f,0.92507f,0.32798f),
        new Vector3(0.20257f,0.91237f,0.35572f), new Vector3(0.21588f,0.90066f,0.37709f), new Vector3(0.22491f,0.88985f,0.39697f), new Vector3(0.24027f,0.86961f,0.43134f),
        new Vector3(0.25390f,0.84721f,0.46665f),
    };

    static readonly Vector3[] SpineWalkXDir = {
        new Vector3(0.87654f,0.02343f,-0.48076f), new Vector3(0.87662f,0.05249f,-0.47832f), new Vector3(0.87669f,0.09126f,-0.47231f), new Vector3(0.87642f,0.12187f,-0.46587f),
        new Vector3(0.87628f,0.14750f,-0.45867f), new Vector3(0.87540f,0.17165f,-0.45189f), new Vector3(0.87104f,0.19806f,-0.44951f), new Vector3(0.86423f,0.22264f,-0.45115f),
        new Vector3(0.85825f,0.24289f,-0.45211f), new Vector3(0.85394f,0.25711f,-0.45242f), new Vector3(0.85111f,0.26699f,-0.45202f), new Vector3(0.84815f,0.27753f,-0.45124f),
        new Vector3(0.84777f,0.28208f,-0.44914f), new Vector3(0.84904f,0.28450f,-0.44519f), new Vector3(0.84959f,0.28376f,-0.44460f), new Vector3(0.84967f,0.28044f,-0.44655f),
        new Vector3(0.85187f,0.27801f,-0.44388f), new Vector3(0.85808f,0.27837f,-0.43153f), new Vector3(0.86499f,0.27436f,-0.42013f), new Vector3(0.87288f,0.26737f,-0.40816f),
        new Vector3(0.88227f,0.25880f,-0.39322f), new Vector3(0.89391f,0.25007f,-0.37201f), new Vector3(0.90483f,0.23966f,-0.35192f), new Vector3(0.91499f,0.22952f,-0.33183f),
        new Vector3(0.92559f,0.21932f,-0.30851f), new Vector3(0.93653f,0.20960f,-0.28104f), new Vector3(0.94679f,0.20128f,-0.25116f), new Vector3(0.95719f,0.19571f,-0.21326f),
        new Vector3(0.96755f,0.18966f,-0.16697f), new Vector3(0.97457f,0.18469f,-0.12690f), new Vector3(0.98093f,0.17779f,-0.07849f), new Vector3(0.98538f,0.16848f,-0.02548f),
        new Vector3(0.98649f,0.16100f,0.03016f), new Vector3(0.98441f,0.15070f,0.09068f), new Vector3(0.97817f,0.13763f,0.15567f), new Vector3(0.96750f,0.11975f,0.22271f),
        new Vector3(0.95222f,0.10001f,0.28859f), new Vector3(0.93505f,0.07442f,0.34663f), new Vector3(0.91690f,0.04243f,0.39685f), new Vector3(0.89913f,0.00308f,0.43766f),
        new Vector3(0.88441f,-0.04369f,0.46467f), new Vector3(0.87752f,-0.09577f,0.46989f), new Vector3(0.87530f,-0.14573f,0.46110f), new Vector3(0.87490f,-0.18576f,0.44726f),
        new Vector3(0.87534f,-0.22062f,0.43024f), new Vector3(0.87607f,-0.25194f,0.41114f), new Vector3(0.87691f,-0.27875f,0.39157f), new Vector3(0.87469f,-0.30391f,0.37757f),
        new Vector3(0.86992f,-0.32811f,0.36822f), new Vector3(0.86508f,-0.34776f,0.36153f), new Vector3(0.86194f,-0.36178f,0.35521f), new Vector3(0.86093f,-0.37108f,0.34799f),
        new Vector3(0.86073f,-0.37427f,0.34507f), new Vector3(0.86230f,-0.37314f,0.34236f), new Vector3(0.86458f,-0.36953f,0.34052f), new Vector3(0.86775f,-0.36399f,0.33842f),
        new Vector3(0.87314f,-0.35607f,0.33293f), new Vector3(0.87900f,-0.34462f,0.32953f), new Vector3(0.88748f,-0.33190f,0.31972f), new Vector3(0.89807f,-0.31701f,0.30493f),
        new Vector3(0.91003f,-0.29933f,0.28680f), new Vector3(0.92234f,-0.27856f,0.26777f), new Vector3(0.93489f,-0.25812f,0.24363f), new Vector3(0.94600f,-0.23961f,0.21832f),
        new Vector3(0.95559f,-0.22378f,0.19173f), new Vector3(0.96333f,-0.21278f,0.16348f), new Vector3(0.97010f,-0.20145f,0.13535f), new Vector3(0.97575f,-0.19192f,0.10522f),
        new Vector3(0.98030f,-0.18429f,0.07107f), new Vector3(0.98345f,-0.17684f,0.03933f), new Vector3(0.98569f,-0.16855f,0.00280f), new Vector3(0.98628f,-0.16020f,-0.03997f),
        new Vector3(0.98503f,-0.15131f,-0.08259f), new Vector3(0.98135f,-0.14197f,-0.12963f), new Vector3(0.97521f,-0.13184f,-0.17771f), new Vector3(0.96654f,-0.11965f,-0.22691f),
        new Vector3(0.95454f,-0.10286f,-0.27976f), new Vector3(0.94053f,-0.08806f,-0.32811f), new Vector3(0.92466f,-0.06644f,-0.37494f), new Vector3(0.90793f,-0.04411f,-0.41680f),
        new Vector3(0.89281f,-0.01969f,-0.45001f),
    };

    static readonly Vector3[] Spine01WalkYDir = {
        new Vector3(0.27964f,0.77831f,0.56218f), new Vector3(0.26364f,0.76848f,0.58304f), new Vector3(0.24000f,0.75744f,0.60719f), new Vector3(0.22050f,0.74787f,0.62615f),
        new Vector3(0.20143f,0.74161f,0.63988f), new Vector3(0.18143f,0.73837f,0.64953f), new Vector3(0.16162f,0.73742f,0.65581f), new Vector3(0.14264f,0.74128f,0.65586f),
        new Vector3(0.12490f,0.74606f,0.65406f), new Vector3(0.10824f,0.75341f,0.64858f), new Vector3(0.09155f,0.76320f,0.63964f), new Vector3(0.07318f,0.77348f,0.62958f),
        new Vector3(0.05656f,0.78537f,0.61644f), new Vector3(0.03775f,0.79948f,0.59951f), new Vector3(0.02127f,0.81569f,0.57810f), new Vector3(0.00654f,0.83328f,0.55282f),
        new Vector3(-0.01256f,0.85062f,0.52562f), new Vector3(-0.03697f,0.86461f,0.50109f), new Vector3(-0.05827f,0.87998f,0.47142f), new Vector3(-0.07621f,0.89483f,0.43985f),
        new Vector3(-0.09233f,0.90849f,0.40759f), new Vector3(-0.10742f,0.91974f,0.37755f), new Vector3(-0.11805f,0.93009f,0.34785f), new Vector3(-0.12479f,0.93801f,0.32337f),
        new Vector3(-0.12916f,0.94401f,0.30359f), new Vector3(-0.13237f,0.94829f,0.28848f), new Vector3(-0.13559f,0.95123f,0.27707f), new Vector3(-0.14089f,0.95193f,0.27198f),
        new Vector3(-0.14392f,0.94842f,0.28247f), new Vector3(-0.14814f,0.94578f,0.28907f), new Vector3(-0.15498f,0.94618f,0.28413f), new Vector3(-0.15977f,0.94395f,0.28883f),
        new Vector3(-0.16756f,0.93828f,0.30257f), new Vector3(-0.17673f,0.93032f,0.32135f), new Vector3(-0.18858f,0.91966f,0.34448f), new Vector3(-0.20228f,0.90479f,0.37474f),
        new Vector3(-0.21747f,0.89180f,0.39672f), new Vector3(-0.23118f,0.87354f,0.42835f), new Vector3(-0.24043f,0.85545f,0.45869f), new Vector3(-0.24445f,0.83494f,0.49307f),
        new Vector3(-0.24103f,0.81128f,0.53266f), new Vector3(-0.22406f,0.78613f,0.57602f), new Vector3(-0.20160f,0.75880f,0.61933f), new Vector3(-0.18315f,0.72955f,0.65895f),
        new Vector3(-0.16392f,0.70236f,0.69270f), new Vector3(-0.14258f,0.67884f,0.72031f), new Vector3(-0.12135f,0.65868f,0.74257f), new Vector3(-0.09812f,0.65048f,0.75316f),
        new Vector3(-0.07746f,0.64537f,0.75994f), new Vector3(-0.05677f,0.64746f,0.75998f), new Vector3(-0.03686f,0.65349f,0.75604f), new Vector3(-0.01747f,0.66169f,0.74957f),
        new Vector3(-0.00172f,0.67581f,0.73707f), new Vector3(0.01146f,0.69063f,0.72312f), new Vector3(0.02288f,0.70665f,0.70720f), new Vector3(0.03351f,0.72355f,0.68946f),
        new Vector3(0.04438f,0.74010f,0.67103f), new Vector3(0.05508f,0.76214f,0.64506f), new Vector3(0.06663f,0.78150f,0.62034f), new Vector3(0.07896f,0.80098f,0.59346f),
        new Vector3(0.08919f,0.81983f,0.56562f), new Vector3(0.09245f,0.83462f,0.54302f), new Vector3(0.09894f,0.85130f,0.51526f), new Vector3(0.10562f,0.86711f,0.48680f),
        new Vector3(0.11335f,0.88255f,0.45634f), new Vector3(0.12085f,0.89175f,0.43610f), new Vector3(0.12691f,0.90103f,0.41477f), new Vector3(0.13268f,0.90708f,0.39950f),
        new Vector3(0.14017f,0.91043f,0.38919f), new Vector3(0.14619f,0.91352f,0.37962f), new Vector3(0.15268f,0.91713f,0.36818f), new Vector3(0.16047f,0.91812f,0.36237f),
        new Vector3(0.16747f,0.91508f,0.36685f), new Vector3(0.17667f,0.91104f,0.37254f), new Vector3(0.18716f,0.90493f,0.38218f), new Vector3(0.19890f,0.89587f,0.39731f),
        new Vector3(0.21275f,0.88190f,0.42070f), new Vector3(0.22819f,0.86921f,0.43864f), new Vector3(0.23907f,0.85792f,0.45478f), new Vector3(0.25529f,0.83653f,0.48481f),
        new Vector3(0.26941f,0.81331f,0.51570f),
    };

    static readonly Vector3[] Spine01WalkXDir = {
        new Vector3(0.88059f,0.02540f,-0.47319f), new Vector3(0.88084f,0.05459f,-0.47025f), new Vector3(0.88073f,0.09318f,-0.46436f), new Vector3(0.88041f,0.12370f,-0.45779f),
        new Vector3(0.88027f,0.14945f,-0.45032f), new Vector3(0.87934f,0.17390f,-0.44331f), new Vector3(0.87472f,0.20062f,-0.44115f), new Vector3(0.86770f,0.22514f,-0.44317f),
        new Vector3(0.86151f,0.24544f,-0.44448f), new Vector3(0.85702f,0.25990f,-0.44494f), new Vector3(0.85401f,0.27018f,-0.44460f), new Vector3(0.85079f,0.28097f,-0.44408f),
        new Vector3(0.85024f,0.28577f,-0.44208f), new Vector3(0.85132f,0.28844f,-0.43825f), new Vector3(0.85169f,0.28806f,-0.43778f), new Vector3(0.85159f,0.28514f,-0.43987f),
        new Vector3(0.85357f,0.28292f,-0.43745f), new Vector3(0.85963f,0.28320f,-0.42524f), new Vector3(0.86642f,0.27915f,-0.41399f), new Vector3(0.87419f,0.27213f,-0.40216f),
        new Vector3(0.88348f,0.26355f,-0.38730f), new Vector3(0.89501f,0.25481f,-0.36610f), new Vector3(0.90577f,0.24443f,-0.34617f), new Vector3(0.91577f,0.23432f,-0.32630f),
        new Vector3(0.92622f,0.22419f,-0.30307f), new Vector3(0.93703f,0.21462f,-0.27554f), new Vector3(0.94713f,0.20653f,-0.24556f), new Vector3(0.95736f,0.20099f,-0.20753f),
        new Vector3(0.96756f,0.19475f,-0.16094f), new Vector3(0.97444f,0.18951f,-0.12064f), new Vector3(0.98059f,0.18229f,-0.07219f), new Vector3(0.98482f,0.17255f,-0.01915f),
        new Vector3(0.98574f,0.16430f,0.03638f), new Vector3(0.98341f,0.15334f,0.09690f), new Vector3(0.97687f,0.13966f,0.16191f), new Vector3(0.96588f,0.12113f,0.22891f),
        new Vector3(0.95034f,0.10076f,0.29445f), new Vector3(0.93307f,0.07437f,0.35191f), new Vector3(0.91491f,0.04186f,0.40149f), new Vector3(0.89722f,0.00191f,0.44157f),
        new Vector3(0.88258f,-0.04506f,0.46800f), new Vector3(0.87593f,-0.09667f,0.47265f), new Vector3(0.87394f,-0.14614f,0.46353f), new Vector3(0.87408f,-0.18593f,0.44880f),
        new Vector3(0.87510f,-0.22059f,0.43074f), new Vector3(0.87632f,-0.25174f,0.41072f), new Vector3(0.87751f,-0.27848f,0.39042f), new Vector3(0.87540f,-0.30356f,0.37621f),
        new Vector3(0.87066f,-0.32758f,0.36694f), new Vector3(0.86581f,-0.34712f,0.36039f), new Vector3(0.86266f,-0.36109f,0.35417f), new Vector3(0.86160f,-0.37040f,0.34706f),
        new Vector3(0.86122f,-0.37361f,0.34456f), new Vector3(0.86258f,-0.37262f,0.34221f), new Vector3(0.86461f,-0.36913f,0.34087f), new Vector3(0.86752f,-0.36360f,0.33941f),
        new Vector3(0.87271f,-0.35563f,0.33452f), new Vector3(0.87838f,-0.34418f,0.33164f), new Vector3(0.88669f,-0.33147f,0.32235f), new Vector3(0.89719f,-0.31659f,0.30792f),
        new Vector3(0.90914f,-0.29896f,0.28997f), new Vector3(0.92145f,-0.27838f,0.27098f), new Vector3(0.93403f,-0.25805f,0.24699f), new Vector3(0.94520f,-0.23964f,0.22177f),
        new Vector3(0.95490f,-0.22364f,0.19532f), new Vector3(0.96279f,-0.21227f,0.16726f), new Vector3(0.96970f,-0.20070f,0.13927f), new Vector3(0.97553f,-0.19079f,0.10923f),
        new Vector3(0.98023f,-0.18304f,0.07516f), new Vector3(0.98353f,-0.17546f,0.04348f), new Vector3(0.98594f,-0.16695f,0.00701f), new Vector3(0.98673f,-0.15839f,-0.03566f),
        new Vector3(0.98573f,-0.14906f,-0.07815f), new Vector3(0.98232f,-0.13940f,-0.12496f), new Vector3(0.97649f,-0.12904f,-0.17266f), new Vector3(0.96816f,-0.11674f,-0.22145f),
        new Vector3(0.95646f,-0.09992f,-0.27422f), new Vector3(0.94286f,-0.08494f,-0.32219f), new Vector3(0.92744f,-0.06303f,-0.36863f), new Vector3(0.91120f,-0.04048f,-0.40996f),
        new Vector3(0.89652f,-0.01626f,-0.44271f),
    };

    static readonly Vector3[] Spine02WalkYDir = {
        new Vector3(0.23534f,0.77771f,0.58291f), new Vector3(0.21794f,0.76120f,0.61080f), new Vector3(0.18768f,0.74847f,0.63606f), new Vector3(0.15612f,0.74019f,0.65402f),
        new Vector3(0.12638f,0.73582f,0.66527f), new Vector3(0.09951f,0.73534f,0.67035f), new Vector3(0.07398f,0.74061f,0.66785f), new Vector3(0.04848f,0.74415f,0.66625f),
        new Vector3(0.02554f,0.75042f,0.66046f), new Vector3(0.00316f,0.75925f,0.65079f), new Vector3(-0.01927f,0.76978f,0.63802f), new Vector3(-0.04074f,0.78071f,0.62356f),
        new Vector3(-0.06116f,0.79274f,0.60648f), new Vector3(-0.08316f,0.80699f,0.58468f), new Vector3(-0.10612f,0.82133f,0.56050f), new Vector3(-0.12893f,0.83493f,0.53504f),
        new Vector3(-0.15051f,0.84794f,0.50828f), new Vector3(-0.16535f,0.86094f,0.48109f), new Vector3(-0.18223f,0.87370f,0.45105f), new Vector3(-0.19538f,0.88592f,0.42067f),
        new Vector3(-0.20447f,0.89749f,0.39077f), new Vector3(-0.21205f,0.90692f,0.36405f), new Vector3(-0.21795f,0.91515f,0.33910f), new Vector3(-0.22317f,0.92181f,0.31696f),
        new Vector3(-0.22821f,0.92672f,0.29852f), new Vector3(-0.23337f,0.92971f,0.28492f), new Vector3(-0.23904f,0.93088f,0.27626f), new Vector3(-0.24596f,0.93165f,0.26747f),
        new Vector3(-0.24816f,0.92895f,0.27471f), new Vector3(-0.25014f,0.92717f,0.27890f), new Vector3(-0.25392f,0.92873f,0.27014f), new Vector3(-0.25539f,0.92845f,0.26975f),
        new Vector3(-0.25384f,0.92458f,0.28409f), new Vector3(-0.25238f,0.91937f,0.30178f), new Vector3(-0.25284f,0.91141f,0.32466f), new Vector3(-0.25479f,0.89896f,0.35631f),
        new Vector3(-0.25759f,0.88811f,0.38066f), new Vector3(-0.25774f,0.87428f,0.41135f), new Vector3(-0.25616f,0.85863f,0.44401f), new Vector3(-0.24857f,0.83798f,0.48580f),
        new Vector3(-0.23330f,0.81037f,0.53747f), new Vector3(-0.20725f,0.77741f,0.59386f), new Vector3(-0.19480f,0.74075f,0.64292f), new Vector3(-0.17374f,0.70546f,0.68712f),
        new Vector3(-0.14519f,0.67376f,0.72455f), new Vector3(-0.11259f,0.64839f,0.75294f), new Vector3(-0.08162f,0.63330f,0.76959f), new Vector3(-0.05195f,0.62574f,0.77830f),
        new Vector3(-0.02662f,0.62167f,0.78283f), new Vector3(-0.00288f,0.62382f,0.78156f), new Vector3(0.01821f,0.62919f,0.77704f), new Vector3(0.03658f,0.63812f,0.76906f),
        new Vector3(0.05531f,0.65669f,0.75213f), new Vector3(0.07148f,0.67416f,0.73512f), new Vector3(0.08529f,0.69242f,0.71643f), new Vector3(0.09745f,0.71260f,0.69477f),
        new Vector3(0.10900f,0.73490f,0.66936f), new Vector3(0.11926f,0.75436f,0.64554f), new Vector3(0.13259f,0.77585f,0.61682f), new Vector3(0.14476f,0.79596f,0.58778f),
        new Vector3(0.15316f,0.81449f,0.55959f), new Vector3(0.15651f,0.83405f,0.52903f), new Vector3(0.16102f,0.85119f,0.49955f), new Vector3(0.16497f,0.86663f,0.47089f),
        new Vector3(0.16893f,0.88124f,0.44145f), new Vector3(0.17188f,0.88989f,0.42256f), new Vector3(0.17318f,0.89957f,0.40099f), new Vector3(0.17629f,0.90576f,0.38537f),
        new Vector3(0.18240f,0.91002f,0.37229f), new Vector3(0.18737f,0.91353f,0.36104f), new Vector3(0.19245f,0.91702f,0.34934f), new Vector3(0.19788f,0.91836f,0.34272f),
        new Vector3(0.19978f,0.91849f,0.34128f), new Vector3(0.20321f,0.91684f,0.34367f), new Vector3(0.20655f,0.91299f,0.35184f), new Vector3(0.21003f,0.90733f,0.36420f),
        new Vector3(0.21504f,0.90044f,0.37811f), new Vector3(0.22097f,0.88748f,0.40442f), new Vector3(0.22237f,0.87861f,0.42260f), new Vector3(0.22547f,0.86117f,0.45557f),
        new Vector3(0.22271f,0.84130f,0.49256f),
    };

    static readonly Vector3[] Spine02WalkXDir = {
        new Vector3(0.89696f,0.05717f,-0.43840f), new Vector3(0.89748f,0.08958f,-0.43186f), new Vector3(0.89689f,0.13343f,-0.42165f), new Vector3(0.89652f,0.17173f,-0.40837f),
        new Vector3(0.89548f,0.20391f,-0.39565f), new Vector3(0.89315f,0.23096f,-0.38593f), new Vector3(0.88839f,0.25532f,-0.38155f), new Vector3(0.88008f,0.28361f,-0.38081f),
        new Vector3(0.87271f,0.30549f,-0.38085f), new Vector3(0.86708f,0.32211f,-0.38001f), new Vector3(0.86327f,0.33473f,-0.37778f), new Vector3(0.86062f,0.34447f,-0.37506f),
        new Vector3(0.86162f,0.34866f,-0.36885f), new Vector3(0.86208f,0.35258f,-0.36402f), new Vector3(0.86169f,0.35725f,-0.36037f), new Vector3(0.86062f,0.36226f,-0.35791f),
        new Vector3(0.85973f,0.36609f,-0.35616f), new Vector3(0.86527f,0.36071f,-0.34813f), new Vector3(0.87088f,0.35638f,-0.33848f), new Vector3(0.87751f,0.34946f,-0.32840f),
        new Vector3(0.88546f,0.33977f,-0.31705f), new Vector3(0.89427f,0.33030f,-0.30196f), new Vector3(0.90323f,0.32075f,-0.28512f), new Vector3(0.91223f,0.31211f,-0.26540f),
        new Vector3(0.92165f,0.30445f,-0.24056f), new Vector3(0.93119f,0.29806f,-0.20987f), new Vector3(0.93999f,0.29318f,-0.17454f), new Vector3(0.94758f,0.28918f,-0.13588f),
        new Vector3(0.95569f,0.28112f,-0.08732f), new Vector3(0.96090f,0.27306f,-0.04595f), new Vector3(0.96468f,0.26341f,0.00117f), new Vector3(0.96662f,0.25110f,0.05089f),
        new Vector3(0.96690f,0.23467f,0.10021f), new Vector3(0.96471f,0.21486f,0.15222f), new Vector3(0.95925f,0.19240f,0.20694f), new Vector3(0.95040f,0.16482f,0.26377f),
        new Vector3(0.93740f,0.13414f,0.32137f), new Vector3(0.92305f,0.09696f,0.37227f), new Vector3(0.90743f,0.05531f,0.41655f), new Vector3(0.89363f,0.00490f,0.44878f),
        new Vector3(0.88482f,-0.05233f,0.46298f), new Vector3(0.88543f,-0.10908f,0.45179f), new Vector3(0.88420f,-0.15109f,0.44199f), new Vector3(0.88574f,-0.19302f,0.42214f),
        new Vector3(0.88781f,-0.23451f,0.39597f), new Vector3(0.88886f,-0.27297f,0.36799f), new Vector3(0.88829f,-0.30395f,0.34433f), new Vector3(0.88508f,-0.33211f,0.32609f),
        new Vector3(0.88029f,-0.35650f,0.31304f), new Vector3(0.87558f,-0.37597f,0.30332f), new Vector3(0.87239f,-0.38969f,0.29510f), new Vector3(0.87121f,-0.39736f,0.28827f),
        new Vector3(0.87046f,-0.40072f,0.28586f), new Vector3(0.87135f,-0.40088f,0.28292f), new Vector3(0.87345f,-0.39793f,0.28061f), new Vector3(0.87672f,-0.39186f,0.27893f),
        new Vector3(0.88145f,-0.38274f,0.27668f), new Vector3(0.88588f,-0.37443f,0.27389f), new Vector3(0.89200f,-0.36473f,0.26703f), new Vector3(0.90012f,-0.35261f,0.25582f),
        new Vector3(0.91002f,-0.33700f,0.24143f), new Vector3(0.92098f,-0.31673f,0.22688f), new Vector3(0.93142f,-0.29844f,0.20828f), new Vector3(0.94133f,-0.28087f,0.18713f),
        new Vector3(0.95023f,-0.26456f,0.16450f), new Vector3(0.95783f,-0.25123f,0.13948f), new Vector3(0.96505f,-0.23630f,0.11331f), new Vector3(0.97095f,-0.22436f,0.08318f),
        new Vector3(0.97539f,-0.21517f,0.04808f), new Vector3(0.97836f,-0.20641f,0.01453f), new Vector3(0.98017f,-0.19679f,-0.02341f), new Vector3(0.98022f,-0.18696f,-0.06497f),
        new Vector3(0.97900f,-0.17265f,-0.10845f), new Vector3(0.97539f,-0.15890f,-0.15284f), new Vector3(0.96985f,-0.14352f,-0.19695f), new Vector3(0.96219f,-0.12575f,-0.24161f),
        new Vector3(0.95149f,-0.10592f,-0.28889f), new Vector3(0.93989f,-0.08308f,-0.33122f), new Vector3(0.92682f,-0.05599f,-0.37130f), new Vector3(0.91434f,-0.02558f,-0.40415f),
        new Vector3(0.90440f,0.01032f,-0.42655f),
    };

    static readonly Vector3[] NeckWalkYDir = {
        new Vector3(0.37045f,0.81852f,0.43909f), new Vector3(0.35557f,0.79992f,0.48342f), new Vector3(0.32937f,0.78674f,0.52206f), new Vector3(0.30683f,0.76810f,0.56203f),
        new Vector3(0.29088f,0.75339f,0.58974f), new Vector3(0.27920f,0.74602f,0.60456f), new Vector3(0.26496f,0.74182f,0.61604f), new Vector3(0.25212f,0.74173f,0.62151f),
        new Vector3(0.24070f,0.74517f,0.62192f), new Vector3(0.22967f,0.75102f,0.61905f), new Vector3(0.21738f,0.75993f,0.61258f), new Vector3(0.20155f,0.77435f,0.59980f),
        new Vector3(0.18171f,0.78944f,0.58631f), new Vector3(0.16783f,0.80229f,0.57285f), new Vector3(0.15537f,0.81583f,0.55703f), new Vector3(0.14156f,0.83081f,0.53824f),
        new Vector3(0.12481f,0.84603f,0.51833f), new Vector3(0.10779f,0.86545f,0.48925f), new Vector3(0.08749f,0.88117f,0.46464f), new Vector3(0.06778f,0.89496f,0.44096f),
        new Vector3(0.05003f,0.90853f,0.41481f), new Vector3(0.03548f,0.92160f,0.38651f), new Vector3(0.02357f,0.93288f,0.35942f), new Vector3(0.01636f,0.94389f,0.32986f),
        new Vector3(0.01130f,0.95487f,0.29679f), new Vector3(0.00636f,0.96481f,0.26286f), new Vector3(-0.00107f,0.97244f,0.23313f), new Vector3(0.00070f,0.97702f,0.21312f),
        new Vector3(-0.00351f,0.97622f,0.21676f), new Vector3(-0.01040f,0.97624f,0.21646f), new Vector3(-0.01971f,0.97768f,0.20919f), new Vector3(-0.03584f,0.97371f,0.22494f),
        new Vector3(-0.05873f,0.96527f,0.25456f), new Vector3(-0.08526f,0.95277f,0.29149f), new Vector3(-0.10957f,0.93828f,0.32805f), new Vector3(-0.12845f,0.92202f,0.36522f),
        new Vector3(-0.14487f,0.90996f,0.38858f), new Vector3(-0.15028f,0.89191f,0.42651f), new Vector3(-0.14906f,0.87472f,0.46114f), new Vector3(-0.14123f,0.86240f,0.48612f),
        new Vector3(-0.12708f,0.85131f,0.50904f), new Vector3(-0.11122f,0.82950f,0.54732f), new Vector3(-0.10155f,0.79575f,0.59705f), new Vector3(-0.09960f,0.75372f,0.64960f),
        new Vector3(-0.09426f,0.71197f,0.69585f), new Vector3(-0.08217f,0.67764f,0.73079f), new Vector3(-0.06754f,0.65564f,0.75204f), new Vector3(-0.04551f,0.65285f,0.75612f),
        new Vector3(-0.02191f,0.65372f,0.75642f), new Vector3(-0.00016f,0.66040f,0.75092f), new Vector3(0.01666f,0.67081f,0.74144f), new Vector3(0.03031f,0.68626f,0.72672f),
        new Vector3(0.04185f,0.69811f,0.71477f), new Vector3(0.05537f,0.71591f,0.69599f), new Vector3(0.06892f,0.73570f,0.67380f), new Vector3(0.08002f,0.75601f,0.64965f),
        new Vector3(0.08548f,0.77775f,0.62273f), new Vector3(0.09969f,0.79980f,0.59192f), new Vector3(0.10475f,0.82497f,0.55539f), new Vector3(0.10318f,0.84574f,0.52352f),
        new Vector3(0.10375f,0.86288f,0.49465f), new Vector3(0.11957f,0.88391f,0.45211f), new Vector3(0.12957f,0.89961f,0.41702f), new Vector3(0.13386f,0.91566f,0.37900f),
        new Vector3(0.13879f,0.93193f,0.33504f), new Vector3(0.14549f,0.94352f,0.29768f), new Vector3(0.15151f,0.95462f,0.25640f), new Vector3(0.15729f,0.96202f,0.22310f),
        new Vector3(0.15916f,0.96606f,0.20344f), new Vector3(0.16371f,0.96832f,0.18855f), new Vector3(0.17436f,0.96977f,0.17073f), new Vector3(0.18901f,0.96989f,0.15356f),
        new Vector3(0.20057f,0.96701f,0.15706f), new Vector3(0.20913f,0.96233f,0.17376f), new Vector3(0.22493f,0.95476f,0.19451f), new Vector3(0.24872f,0.94451f,0.21456f),
        new Vector3(0.27276f,0.93200f,0.23870f), new Vector3(0.30324f,0.91640f,0.26128f), new Vector3(0.33708f,0.90140f,0.27177f), new Vector3(0.36326f,0.88454f,0.29264f),
        new Vector3(0.37227f,0.87112f,0.32026f),
    };

    static readonly Vector3[] NeckWalkXDir = {
        new Vector3(0.85690f,-0.11874f,-0.50161f), new Vector3(0.86435f,-0.08463f,-0.49572f), new Vector3(0.87232f,-0.04195f,-0.48713f), new Vector3(0.88063f,-0.00511f,-0.47377f),
        new Vector3(0.88554f,0.02137f,-0.46408f), new Vector3(0.88695f,0.04088f,-0.46005f), new Vector3(0.88699f,0.06309f,-0.45746f), new Vector3(0.88523f,0.08265f,-0.45774f),
        new Vector3(0.88349f,0.09710f,-0.45828f), new Vector3(0.88290f,0.10689f,-0.45723f), new Vector3(0.88333f,0.11386f,-0.45471f), new Vector3(0.88324f,0.12102f,-0.45304f),
        new Vector3(0.88588f,0.12738f,-0.44608f), new Vector3(0.88839f,0.12879f,-0.44065f), new Vector3(0.89012f,0.12893f,-0.43711f), new Vector3(0.89157f,0.12928f,-0.43404f),
        new Vector3(0.89454f,0.13004f,-0.42766f), new Vector3(0.89867f,0.12564f,-0.42025f), new Vector3(0.90401f,0.12570f,-0.40861f), new Vector3(0.91003f,0.12571f,-0.39502f),
        new Vector3(0.91688f,0.12291f,-0.37978f), new Vector3(0.92482f,0.11630f,-0.36219f), new Vector3(0.93200f,0.10957f,-0.34550f), new Vector3(0.93867f,0.09915f,-0.33025f),
        new Vector3(0.94556f,0.08634f,-0.31378f), new Vector3(0.95275f,0.07398f,-0.29461f), new Vector3(0.95979f,0.06644f,-0.27273f), new Vector3(0.96718f,0.05349f,-0.24840f),
        new Vector3(0.97533f,0.05118f,-0.21475f), new Vector3(0.98116f,0.05173f,-0.18614f), new Vector3(0.98790f,0.05124f,-0.14641f), new Vector3(0.99349f,0.05907f,-0.09740f),
        new Vector3(0.99634f,0.07257f,-0.04528f), new Vector3(0.99630f,0.08482f,0.01416f), new Vector3(0.99312f,0.08960f,0.07545f), new Vector3(0.98734f,0.08432f,0.13437f),
        new Vector3(0.97872f,0.07410f,0.19134f), new Vector3(0.96995f,0.04951f,0.23823f), new Vector3(0.96184f,0.02006f,0.27287f), new Vector3(0.95443f,-0.01179f,0.29821f),
        new Vector3(0.94833f,-0.04614f,0.31391f), new Vector3(0.94455f,-0.08299f,0.31771f), new Vector3(0.94235f,-0.11541f,0.31410f), new Vector3(0.94112f,-0.14061f,0.30744f),
        new Vector3(0.94120f,-0.16404f,0.29534f), new Vector3(0.94235f,-0.18585f,0.27829f), new Vector3(0.94368f,-0.20272f,0.26148f), new Vector3(0.94348f,-0.22066f,0.24730f),
        new Vector3(0.94207f,-0.23978f,0.23452f), new Vector3(0.93983f,-0.25644f,0.22573f), new Vector3(0.93750f,-0.26826f,0.22165f), new Vector3(0.93561f,-0.27532f,0.22097f),
        new Vector3(0.93429f,-0.28085f,0.21960f), new Vector3(0.93404f,-0.28347f,0.21727f), new Vector3(0.93431f,-0.28437f,0.21493f), new Vector3(0.93509f,-0.28267f,0.21377f),
        new Vector3(0.93691f,-0.27538f,0.21532f), new Vector3(0.93902f,-0.27238f,0.20988f), new Vector3(0.94261f,-0.26039f,0.20900f), new Vector3(0.94776f,-0.24332f,0.20628f),
        new Vector3(0.95399f,-0.22700f,0.19589f), new Vector3(0.96041f,-0.21839f,0.17296f), new Vector3(0.96648f,-0.20858f,0.14967f), new Vector3(0.97172f,-0.19635f,0.13119f),
        new Vector3(0.97606f,-0.18594f,0.11285f), new Vector3(0.97930f,-0.18014f,0.09233f), new Vector3(0.98214f,-0.17467f,0.06996f), new Vector3(0.98421f,-0.17127f,0.04464f),
        new Vector3(0.98598f,-0.16601f,0.01693f), new Vector3(0.98626f,-0.16493f,-0.00928f), new Vector3(0.98462f,-0.16968f,-0.04175f), new Vector3(0.98064f,-0.17830f,-0.08090f),
        new Vector3(0.97574f,-0.18282f,-0.12046f), new Vector3(0.96972f,-0.18116f,-0.16378f), new Vector3(0.96005f,-0.18306f,-0.21164f), new Vector3(0.94561f,-0.18883f,-0.26489f),
        new Vector3(0.92774f,-0.18910f,-0.32179f), new Vector3(0.90787f,-0.19453f,-0.37139f), new Vector3(0.87868f,-0.19755f,-0.43461f), new Vector3(0.85336f,-0.18983f,-0.48552f),
        new Vector3(0.84069f,-0.17028f,-0.51404f),
    };

    static readonly Vector3[] HeadWalkYDir = {
        new Vector3(0.20675f,0.19020f,0.95973f), new Vector3(0.20772f,0.18118f,0.96126f), new Vector3(0.21205f,0.17384f,0.96167f), new Vector3(0.21165f,0.16636f,0.96308f),
        new Vector3(0.20599f,0.16004f,0.96538f), new Vector3(0.19758f,0.15549f,0.96788f), new Vector3(0.19282f,0.15370f,0.96912f), new Vector3(0.19069f,0.15549f,0.96926f),
        new Vector3(0.18929f,0.15838f,0.96906f), new Vector3(0.18596f,0.16566f,0.96849f), new Vector3(0.18092f,0.17464f,0.96787f), new Vector3(0.17629f,0.18273f,0.96723f),
        new Vector3(0.17512f,0.19320f,0.96541f), new Vector3(0.17016f,0.20597f,0.96365f), new Vector3(0.16715f,0.22007f,0.96106f), new Vector3(0.16708f,0.23468f,0.95761f),
        new Vector3(0.16554f,0.24871f,0.95433f), new Vector3(0.16243f,0.25974f,0.95192f), new Vector3(0.16156f,0.27531f,0.94768f), new Vector3(0.16264f,0.28894f,0.94343f),
        new Vector3(0.16303f,0.29919f,0.94016f), new Vector3(0.15968f,0.30552f,0.93870f), new Vector3(0.15540f,0.31064f,0.93774f), new Vector3(0.14687f,0.31531f,0.93756f),
        new Vector3(0.13597f,0.31717f,0.93857f), new Vector3(0.12517f,0.31587f,0.94051f), new Vector3(0.11776f,0.31447f,0.94193f), new Vector3(0.10065f,0.31027f,0.94530f),
        new Vector3(0.08590f,0.29725f,0.95093f), new Vector3(0.07490f,0.29202f,0.95348f), new Vector3(0.05162f,0.29800f,0.95317f), new Vector3(0.02742f,0.29663f,0.95460f),
        new Vector3(0.00858f,0.28750f,0.95774f), new Vector3(-0.01508f,0.27747f,0.96062f), new Vector3(-0.04409f,0.26330f,0.96371f), new Vector3(-0.07578f,0.24205f,0.96730f),
        new Vector3(-0.10913f,0.23377f,0.96615f), new Vector3(-0.15211f,0.23046f,0.96112f), new Vector3(-0.18138f,0.22509f,0.95731f), new Vector3(-0.20442f,0.21020f,0.95605f),
        new Vector3(-0.22402f,0.18600f,0.95667f), new Vector3(-0.23759f,0.16148f,0.95785f), new Vector3(-0.24173f,0.15021f,0.95865f), new Vector3(-0.24392f,0.14279f,0.95923f),
        new Vector3(-0.24203f,0.13191f,0.96126f), new Vector3(-0.23645f,0.11672f,0.96461f), new Vector3(-0.23068f,0.10356f,0.96750f), new Vector3(-0.22480f,0.10072f,0.96919f),
        new Vector3(-0.22094f,0.09869f,0.97028f), new Vector3(-0.21611f,0.10417f,0.97080f), new Vector3(-0.21011f,0.11333f,0.97109f), new Vector3(-0.20269f,0.12358f,0.97141f),
        new Vector3(-0.19528f,0.13513f,0.97139f), new Vector3(-0.18835f,0.14934f,0.97068f), new Vector3(-0.18302f,0.16269f,0.96955f), new Vector3(-0.17912f,0.17396f,0.96832f),
        new Vector3(-0.17512f,0.18419f,0.96716f), new Vector3(-0.17376f,0.19876f,0.96452f), new Vector3(-0.17036f,0.20613f,0.96358f), new Vector3(-0.16304f,0.21518f,0.96287f),
        new Vector3(-0.15293f,0.22649f,0.96193f), new Vector3(-0.14386f,0.23293f,0.96179f), new Vector3(-0.13368f,0.24055f,0.96139f), new Vector3(-0.12409f,0.24648f,0.96117f),
        new Vector3(-0.11365f,0.25335f,0.96068f), new Vector3(-0.10353f,0.24937f,0.96286f), new Vector3(-0.09073f,0.24751f,0.96463f), new Vector3(-0.07721f,0.24194f,0.96722f),
        new Vector3(-0.05779f,0.23590f,0.97006f), new Vector3(-0.04356f,0.23310f,0.97148f), new Vector3(-0.02953f,0.23422f,0.97173f), new Vector3(-0.01394f,0.23084f,0.97289f),
        new Vector3(0.00629f,0.22310f,0.97478f), new Vector3(0.03521f,0.21682f,0.97558f), new Vector3(0.06357f,0.20981f,0.97567f), new Vector3(0.09051f,0.20139f,0.97532f),
        new Vector3(0.12087f,0.19011f,0.97429f), new Vector3(0.13520f,0.18702f,0.97301f), new Vector3(0.16598f,0.18622f,0.96839f), new Vector3(0.19613f,0.17141f,0.96548f),
        new Vector3(0.21769f,0.16228f,0.96243f),
    };

    static readonly Vector3[] HeadWalkXDir = {
        new Vector3(0.97088f,0.08148f,-0.22530f), new Vector3(0.96954f,0.09224f,-0.22690f), new Vector3(0.96647f,0.10851f,-0.23272f), new Vector3(0.96494f,0.12094f,-0.23295f),
        new Vector3(0.96448f,0.13348f,-0.22793f), new Vector3(0.96412f,0.14775f,-0.22055f), new Vector3(0.96258f,0.16202f,-0.21722f), new Vector3(0.96046f,0.17449f,-0.21695f),
        new Vector3(0.95884f,0.18291f,-0.21718f), new Vector3(0.95787f,0.18898f,-0.21625f), new Vector3(0.95720f,0.19481f,-0.21407f), new Vector3(0.95588f,0.20280f,-0.21254f),
        new Vector3(0.95488f,0.20558f,-0.21435f), new Vector3(0.95467f,0.20794f,-0.21301f), new Vector3(0.95394f,0.21022f,-0.21405f), new Vector3(0.95264f,0.21188f,-0.21814f),
        new Vector3(0.95192f,0.21263f,-0.22054f), new Vector3(0.95231f,0.21130f,-0.22015f), new Vector3(0.95191f,0.20985f,-0.22324f), new Vector3(0.95204f,0.20520f,-0.22697f),
        new Vector3(0.95335f,0.19760f,-0.22820f), new Vector3(0.95593f,0.18945f,-0.22427f), new Vector3(0.95818f,0.18350f,-0.21958f), new Vector3(0.96097f,0.17915f,-0.21079f),
        new Vector3(0.96415f,0.17554f,-0.19900f), new Vector3(0.96744f,0.17133f,-0.18629f), new Vector3(0.97042f,0.16485f,-0.17636f), new Vector3(0.97415f,0.16242f,-0.15703f),
        new Vector3(0.97815f,0.15621f,-0.13719f), new Vector3(0.98042f,0.15307f,-0.12389f), new Vector3(0.98371f,0.14937f,-0.09997f), new Vector3(0.98731f,0.14141f,-0.07230f),
        new Vector3(0.99037f,0.12993f,-0.04787f), new Vector3(0.99309f,0.11602f,-0.01792f), new Vector3(0.99455f,0.10276f,0.01743f), new Vector3(0.99442f,0.08971f,0.05545f),
        new Vector3(0.99276f,0.07479f,0.09404f), new Vector3(0.98799f,0.06225f,0.14144f), new Vector3(0.98339f,0.04774f,0.17510f), new Vector3(0.97886f,0.03712f,0.20113f),
        new Vector3(0.97449f,0.02884f,0.22259f), new Vector3(0.97109f,0.01616f,0.23815f), new Vector3(0.96968f,0.00102f,0.24436f), new Vector3(0.96869f,-0.01144f,0.24803f),
        new Vector3(0.96872f,-0.02301f,0.24707f), new Vector3(0.96983f,-0.03231f,0.24164f), new Vector3(0.97121f,-0.03634f,0.23546f), new Vector3(0.97239f,-0.04078f,0.22978f),
        new Vector3(0.97301f,-0.04562f,0.22620f), new Vector3(0.97359f,-0.05198f,0.22231f), new Vector3(0.97420f,-0.05946f,0.21772f), new Vector3(0.97491f,-0.06778f,0.21204f),
        new Vector3(0.97551f,-0.07545f,0.20660f), new Vector3(0.97620f,-0.07970f,0.20168f), new Vector3(0.97684f,-0.08109f,0.19800f), new Vector3(0.97747f,-0.08025f,0.19523f),
        new Vector3(0.97831f,-0.07782f,0.19195f), new Vector3(0.97896f,-0.07152f,0.19110f), new Vector3(0.98018f,-0.06492f,0.18718f), new Vector3(0.98219f,-0.05700f,0.17905f),
        new Vector3(0.98477f,-0.04657f,0.16752f), new Vector3(0.98738f,-0.03119f,0.15524f), new Vector3(0.98974f,-0.01700f,0.14188f), new Vector3(0.99154f,-0.00629f,0.12962f),
        new Vector3(0.99313f,0.00193f,0.11698f), new Vector3(0.99444f,0.00723f,0.10505f), new Vector3(0.99583f,0.01357f,0.09018f), new Vector3(0.99701f,0.02077f,0.07439f),
        new Vector3(0.99826f,0.02508f,0.05337f), new Vector3(0.99886f,0.02903f,0.03782f), new Vector3(0.99917f,0.03416f,0.02213f), new Vector3(0.99920f,0.03963f,0.00491f),
        new Vector3(0.99897f,0.04241f,-0.01616f), new Vector3(0.99791f,0.04526f,-0.04607f), new Vector3(0.99586f,0.05038f,-0.07572f), new Vector3(0.99286f,0.05819f,-0.10415f),
        new Vector3(0.98847f,0.06716f,-0.13573f), new Vector3(0.98552f,0.07606f,-0.15156f), new Vector3(0.97772f,0.09685f,-0.18621f), new Vector3(0.97059f,0.10626f,-0.21604f),
        new Vector3(0.96603f,0.10485f,-0.23619f),
    };

    static readonly Vector3[] LeftShoulderWalkYDir = {
        new Vector3(0.88042f,-0.05806f,-0.47063f), new Vector3(0.88294f,-0.04550f,-0.46728f), new Vector3(0.88743f,-0.02843f,-0.46006f), new Vector3(0.89171f,-0.01538f,-0.45234f),
        new Vector3(0.89337f,-0.00151f,-0.44932f), new Vector3(0.89251f,0.01699f,-0.45070f), new Vector3(0.88968f,0.03909f,-0.45491f), new Vector3(0.88547f,0.06355f,-0.46033f),
        new Vector3(0.87933f,0.08551f,-0.46847f), new Vector3(0.87400f,0.10451f,-0.47455f), new Vector3(0.87051f,0.12190f,-0.47682f), new Vector3(0.86786f,0.14069f,-0.47646f),
        new Vector3(0.86744f,0.15394f,-0.47313f), new Vector3(0.87026f,0.16099f,-0.46554f), new Vector3(0.87300f,0.16653f,-0.45842f), new Vector3(0.87510f,0.17225f,-0.45225f),
        new Vector3(0.87862f,0.17836f,-0.44297f), new Vector3(0.88292f,0.18459f,-0.43171f), new Vector3(0.88955f,0.18989f,-0.41550f), new Vector3(0.89866f,0.19496f,-0.39293f),
        new Vector3(0.90907f,0.20013f,-0.36544f), new Vector3(0.91910f,0.20351f,-0.33740f), new Vector3(0.93065f,0.20214f,-0.30502f), new Vector3(0.94170f,0.20042f,-0.27024f),
        new Vector3(0.95159f,0.19965f,-0.23371f), new Vector3(0.96009f,0.19934f,-0.19620f), new Vector3(0.96724f,0.19837f,-0.15845f), new Vector3(0.97388f,0.19571f,-0.11516f),
        new Vector3(0.97893f,0.19268f,-0.06761f), new Vector3(0.98130f,0.19073f,-0.02588f), new Vector3(0.98183f,0.18794f,0.02623f), new Vector3(0.97965f,0.18200f,0.08457f),
        new Vector3(0.97465f,0.17155f,0.14364f), new Vector3(0.96514f,0.15892f,0.20795f), new Vector3(0.95118f,0.14243f,0.27381f), new Vector3(0.93345f,0.11904f,0.33839f),
        new Vector3(0.91148f,0.09314f,0.40065f), new Vector3(0.88535f,0.05675f,0.46145f), new Vector3(0.86083f,0.01515f,0.50866f), new Vector3(0.83774f,-0.03158f,0.54516f),
        new Vector3(0.81826f,-0.08535f,0.56848f), new Vector3(0.80538f,-0.15684f,0.57163f), new Vector3(0.79354f,-0.22220f,0.56651f), new Vector3(0.78066f,-0.28179f,0.55783f),
        new Vector3(0.76720f,-0.33767f,0.54533f), new Vector3(0.75415f,-0.38896f,0.52912f), new Vector3(0.74308f,-0.43347f,0.50983f), new Vector3(0.73869f,-0.45672f,0.49572f),
        new Vector3(0.73219f,-0.47577f,0.48738f), new Vector3(0.72627f,-0.48889f,0.48324f), new Vector3(0.72229f,-0.49708f,0.48085f), new Vector3(0.72108f,-0.49838f,0.48132f),
        new Vector3(0.71752f,-0.50168f,0.48320f), new Vector3(0.72562f,-0.49381f,0.47919f), new Vector3(0.73738f,-0.48076f,0.47449f), new Vector3(0.75010f,-0.46514f,0.47010f),
        new Vector3(0.76558f,-0.44595f,0.46369f), new Vector3(0.78270f,-0.42595f,0.45382f), new Vector3(0.79789f,-0.40475f,0.44671f), new Vector3(0.81342f,-0.38392f,0.43698f),
        new Vector3(0.83152f,-0.36294f,0.42054f), new Vector3(0.85660f,-0.33501f,0.39245f), new Vector3(0.87705f,-0.30938f,0.36752f), new Vector3(0.89493f,-0.28285f,0.34510f),
        new Vector3(0.91122f,-0.25697f,0.32194f), new Vector3(0.92565f,-0.23751f,0.29455f), new Vector3(0.93904f,-0.22053f,0.26375f), new Vector3(0.95030f,-0.20897f,0.23080f),
        new Vector3(0.96083f,-0.19683f,0.19512f), new Vector3(0.96951f,-0.18504f,0.16065f), new Vector3(0.97756f,-0.17307f,0.12007f), new Vector3(0.98435f,-0.16042f,0.07292f),
        new Vector3(0.98832f,-0.15082f,0.02192f), new Vector3(0.98982f,-0.13840f,-0.03318f), new Vector3(0.98790f,-0.12555f,-0.09108f), new Vector3(0.98222f,-0.11247f,-0.15030f),
        new Vector3(0.97280f,-0.09621f,-0.21073f), new Vector3(0.96054f,-0.08257f,-0.26561f), new Vector3(0.94436f,-0.06952f,-0.32149f), new Vector3(0.92374f,-0.06266f,-0.37786f),
        new Vector3(0.90119f,-0.05163f,-0.43034f),
    };

    static readonly Vector3[] LeftShoulderWalkXDir = {
        new Vector3(-0.28804f,0.72292f,-0.62803f), new Vector3(-0.28812f,0.73332f,-0.61581f), new Vector3(-0.28921f,0.74285f,-0.60377f), new Vector3(-0.28858f,0.75059f,-0.59442f),
        new Vector3(-0.29380f,0.75465f,-0.58667f), new Vector3(-0.30736f,0.75422f,-0.58024f), new Vector3(-0.32688f,0.75013f,-0.57484f), new Vector3(-0.35088f,0.74091f,-0.57266f),
        new Vector3(-0.37555f,0.72939f,-0.57179f), new Vector3(-0.39754f,0.71536f,-0.57464f), new Vector3(-0.41642f,0.69882f,-0.58158f), new Vector3(-0.43434f,0.68045f,-0.59021f),
        new Vector3(-0.44633f,0.66099f,-0.60323f), new Vector3(-0.45130f,0.63936f,-0.62254f), new Vector3(-0.45546f,0.61453f,-0.64413f), new Vector3(-0.45992f,0.58678f,-0.66646f),
        new Vector3(-0.46136f,0.55639f,-0.69107f), new Vector3(-0.45960f,0.52778f,-0.71429f), new Vector3(-0.45197f,0.49817f,-0.73997f), new Vector3(-0.43728f,0.46854f,-0.76763f),
        new Vector3(-0.41665f,0.43954f,-0.79574f), new Vector3(-0.39307f,0.41411f,-0.82098f), new Vector3(-0.36216f,0.38967f,-0.84676f), new Vector3(-0.32818f,0.37019f,-0.86906f),
        new Vector3(-0.29263f,0.35574f,-0.88759f), new Vector3(-0.25648f,0.34762f,-0.90187f), new Vector3(-0.21998f,0.34326f,-0.91312f), new Vector3(-0.17825f,0.34470f,-0.92164f),
        new Vector3(-0.13528f,0.36396f,-0.92154f), new Vector3(-0.09776f,0.37804f,-0.92061f), new Vector3(-0.04843f,0.38186f,-0.92295f), new Vector3(0.00591f,0.39504f,-0.91865f),
        new Vector3(0.06029f,0.41686f,-0.90697f), new Vector3(0.11817f,0.44433f,-0.88803f), new Vector3(0.17640f,0.47708f,-0.86097f), new Vector3(0.23273f,0.51688f,-0.82382f),
        new Vector3(0.28896f,0.54823f,-0.78482f), new Vector3(0.34478f,0.58571f,-0.73354f), new Vector3(0.39256f,0.61630f,-0.68270f), new Vector3(0.43377f,0.64494f,-0.62921f),
        new Vector3(0.46945f,0.66996f,-0.57512f), new Vector3(0.50549f,0.68537f,-0.52416f), new Vector3(0.53596f,0.69607f,-0.47774f), new Vector3(0.56349f,0.70340f,-0.43325f),
        new Vector3(0.59046f,0.70389f,-0.39484f), new Vector3(0.61601f,0.69821f,-0.36474f), new Vector3(0.63743f,0.69043f,-0.34204f), new Vector3(0.64913f,0.68013f,-0.34067f),
        new Vector3(0.66086f,0.66941f,-0.33935f), new Vector3(0.67127f,0.65582f,-0.34539f), new Vector3(0.67918f,0.64097f,-0.35759f), new Vector3(0.68312f,0.62743f,-0.37373f),
        new Vector3(0.68956f,0.60956f,-0.39108f), new Vector3(0.68310f,0.60071f,-0.41535f), new Vector3(0.67193f,0.59401f,-0.44234f), new Vector3(0.65886f,0.58693f,-0.47056f),
        new Vector3(0.64169f,0.58091f,-0.50078f), new Vector3(0.62172f,0.56930f,-0.53792f), new Vector3(0.60256f,0.55661f,-0.57193f), new Vector3(0.58167f,0.54088f,-0.60755f),
        new Vector3(0.55537f,0.52698f,-0.64331f), new Vector3(0.51554f,0.52406f,-0.67792f), new Vector3(0.47930f,0.51180f,-0.71298f), new Vector3(0.44445f,0.49657f,-0.74557f),
        new Vector3(0.40935f,0.47760f,-0.77739f), new Vector3(0.37464f,0.46621f,-0.80143f), new Vector3(0.33821f,0.45477f,-0.82389f), new Vector3(0.30276f,0.44729f,-0.84158f),
        new Vector3(0.26486f,0.44480f,-0.85557f), new Vector3(0.22821f,0.44298f,-0.86700f), new Vector3(0.18588f,0.44067f,-0.87821f), new Vector3(0.13803f,0.44469f,-0.88499f),
        new Vector3(0.08966f,0.45904f,-0.88388f), new Vector3(0.03701f,0.47541f,-0.87898f), new Vector3(-0.01697f,0.49618f,-0.86805f), new Vector3(-0.07004f,0.52326f,-0.84929f),
        new Vector3(-0.12268f,0.55768f,-0.82094f), new Vector3(-0.16881f,0.58591f,-0.79260f), new Vector3(-0.21468f,0.61026f,-0.76256f), new Vector3(-0.25071f,0.64690f,-0.72018f),
        new Vector3(-0.28270f,0.68258f,-0.67392f),
    };

    static readonly Vector3[] RightShoulderWalkYDir = {
        new Vector3(-0.72114f,-0.18458f,0.66775f), new Vector3(-0.71327f,-0.23296f,0.66105f), new Vector3(-0.69952f,-0.27466f,0.65973f), new Vector3(-0.68822f,-0.31737f,0.65240f),
        new Vector3(-0.67808f,-0.34922f,0.64673f), new Vector3(-0.66539f,-0.37039f,0.64813f), new Vector3(-0.64495f,-0.39203f,0.65601f), new Vector3(-0.63169f,-0.40625f,0.66026f),
        new Vector3(-0.62003f,-0.41628f,0.66504f), new Vector3(-0.61297f,-0.42396f,0.66673f), new Vector3(-0.60947f,-0.42818f,0.66724f), new Vector3(-0.60768f,-0.42588f,0.67033f),
        new Vector3(-0.61750f,-0.42503f,0.66185f), new Vector3(-0.62483f,-0.41905f,0.65878f), new Vector3(-0.63306f,-0.40957f,0.65687f), new Vector3(-0.64235f,-0.39621f,0.65605f),
        new Vector3(-0.64996f,-0.37573f,0.66059f), new Vector3(-0.66238f,-0.36332f,0.65517f), new Vector3(-0.67752f,-0.34292f,0.65067f), new Vector3(-0.69808f,-0.31918f,0.64095f),
        new Vector3(-0.72080f,-0.29212f,0.62858f), new Vector3(-0.73841f,-0.26809f,0.61877f), new Vector3(-0.76300f,-0.24083f,0.59986f), new Vector3(-0.78224f,-0.21785f,0.58364f),
        new Vector3(-0.79938f,-0.19720f,0.56755f), new Vector3(-0.81747f,-0.17724f,0.54803f), new Vector3(-0.83625f,-0.15775f,0.52517f), new Vector3(-0.85884f,-0.14338f,0.49177f),
        new Vector3(-0.88077f,-0.13403f,0.45419f), new Vector3(-0.89815f,-0.12464f,0.42166f), new Vector3(-0.91826f,-0.11249f,0.37967f), new Vector3(-0.93723f,-0.10616f,0.33215f),
        new Vector3(-0.95623f,-0.10171f,0.27437f), new Vector3(-0.97051f,-0.09853f,0.21998f), new Vector3(-0.98218f,-0.09615f,0.16145f), new Vector3(-0.99127f,-0.09357f,0.09290f),
        new Vector3(-0.99627f,-0.08601f,0.00618f), new Vector3(-0.99517f,-0.08016f,-0.05663f), new Vector3(-0.99026f,-0.06735f,-0.12187f), new Vector3(-0.98218f,-0.05140f,-0.18079f),
        new Vector3(-0.97243f,-0.03270f,-0.23088f), new Vector3(-0.96178f,-0.01407f,-0.27345f), new Vector3(-0.95820f,-0.00522f,-0.28604f), new Vector3(-0.95755f,0.00296f,-0.28825f),
        new Vector3(-0.95810f,0.01066f,-0.28625f), new Vector3(-0.95924f,0.01925f,-0.28195f), new Vector3(-0.96149f,0.03257f,-0.27290f), new Vector3(-0.96327f,0.05701f,-0.26241f),
        new Vector3(-0.96374f,0.08371f,-0.25336f), new Vector3(-0.96294f,0.10861f,-0.24688f), new Vector3(-0.96184f,0.12681f,-0.24244f), new Vector3(-0.96023f,0.14365f,-0.23943f),
        new Vector3(-0.96283f,0.14830f,-0.22576f), new Vector3(-0.96341f,0.15645f,-0.21763f), new Vector3(-0.96384f,0.16447f,-0.20967f), new Vector3(-0.96466f,0.17064f,-0.20080f),
        new Vector3(-0.96557f,0.17445f,-0.19299f), new Vector3(-0.96763f,0.17335f,-0.18344f), new Vector3(-0.97230f,0.16774f,-0.16276f), new Vector3(-0.97770f,0.15860f,-0.13767f),
        new Vector3(-0.98222f,0.14987f,-0.11309f), new Vector3(-0.98392f,0.15068f,-0.09586f), new Vector3(-0.98785f,0.14129f,-0.06466f), new Vector3(-0.99134f,0.12933f,-0.02275f),
        new Vector3(-0.99222f,0.12227f,0.02328f), new Vector3(-0.99054f,0.12045f,0.06572f), new Vector3(-0.98810f,0.11798f,0.09865f), new Vector3(-0.98438f,0.11592f,0.13248f),
        new Vector3(-0.98000f,0.11292f,0.16388f), new Vector3(-0.97557f,0.11051f,0.18985f), new Vector3(-0.96926f,0.10904f,0.22056f), new Vector3(-0.95956f,0.10748f,0.26018f),
        new Vector3(-0.94489f,0.09934f,0.31194f), new Vector3(-0.93054f,0.08654f,0.35582f), new Vector3(-0.91376f,0.07184f,0.39985f), new Vector3(-0.89449f,0.05355f,0.44386f),
        new Vector3(-0.87088f,0.03260f,0.49042f), new Vector3(-0.84022f,0.00445f,0.54222f), new Vector3(-0.80994f,-0.02822f,0.58583f), new Vector3(-0.78334f,-0.06382f,0.61831f),
        new Vector3(-0.75994f,-0.09765f,0.64261f),
    };

    static readonly Vector3[] RightShoulderWalkXDir = {
        new Vector3(0.57782f,-0.69199f,0.43275f), new Vector3(0.60133f,-0.68793f,0.40640f), new Vector3(0.62688f,-0.67905f,0.38198f), new Vector3(0.65012f,-0.66892f,0.36041f),
        new Vector3(0.66901f,-0.65763f,0.34634f), new Vector3(0.68682f,-0.64391f,0.33713f), new Vector3(0.71027f,-0.62429f,0.32523f), new Vector3(0.72736f,-0.60523f,0.32349f),
        new Vector3(0.74209f,-0.58630f,0.32488f), new Vector3(0.75325f,-0.56829f,0.33115f), new Vector3(0.76119f,-0.55134f,0.34149f), new Vector3(0.76705f,-0.53348f,0.35642f),
        new Vector3(0.76505f,-0.52002f,0.37983f), new Vector3(0.76395f,-0.50227f,0.40509f), new Vector3(0.76122f,-0.48355f,0.43212f), new Vector3(0.75667f,-0.46391f,0.46069f),
        new Vector3(0.75254f,-0.43944f,0.49048f), new Vector3(0.74475f,-0.41417f,0.52328f), new Vector3(0.73295f,-0.38839f,0.55851f), new Vector3(0.71468f,-0.36533f,0.59646f),
        new Vector3(0.69248f,-0.34313f,0.63461f), new Vector3(0.67407f,-0.31973f,0.66588f), new Vector3(0.64628f,-0.30190f,0.70084f), new Vector3(0.62291f,-0.28737f,0.72760f),
        new Vector3(0.60076f,-0.27706f,0.74989f), new Vector3(0.57585f,-0.27096f,0.77135f), new Vector3(0.54811f,-0.26877f,0.79205f), new Vector3(0.51194f,-0.27320f,0.81442f),
        new Vector3(0.47305f,-0.29291f,0.83092f), new Vector3(0.43902f,-0.30743f,0.84424f), new Vector3(0.39538f,-0.31353f,0.86335f), new Vector3(0.34833f,-0.32890f,0.87778f),
        new Vector3(0.29253f,-0.35483f,0.88799f), new Vector3(0.24102f,-0.38524f,0.89078f), new Vector3(0.18711f,-0.42063f,0.88773f), new Vector3(0.12602f,-0.46492f,0.87634f),
        new Vector3(0.04895f,-0.50500f,0.86173f), new Vector3(-0.00327f,-0.54958f,0.83543f), new Vector3(-0.05868f,-0.59188f,0.80389f), new Vector3(-0.10784f,-0.63371f,0.76602f),
        new Vector3(-0.14897f,-0.67464f,0.72296f), new Vector3(-0.18280f,-0.71055f,0.67949f), new Vector3(-0.18794f,-0.74232f,0.64315f), new Vector3(-0.18565f,-0.77131f,0.60878f),
        new Vector3(-0.18175f,-0.79500f,0.57875f), new Vector3(-0.17905f,-0.81329f,0.55362f), new Vector3(-0.17902f,-0.82764f,0.53195f), new Vector3(-0.19155f,-0.83075f,0.52265f),
        new Vector3(-0.20782f,-0.83103f,0.51595f), new Vector3(-0.22587f,-0.82499f,0.51806f), new Vector3(-0.24015f,-0.81586f,0.52602f), new Vector3(-0.25418f,-0.80461f,0.53665f),
        new Vector3(-0.25235f,-0.79198f,0.55595f), new Vector3(-0.25603f,-0.77745f,0.57448f), new Vector3(-0.25918f,-0.76150f,0.59409f), new Vector3(-0.25972f,-0.74459f,0.61493f),
        new Vector3(-0.25848f,-0.72722f,0.63587f), new Vector3(-0.25201f,-0.70359f,0.66442f), new Vector3(-0.23367f,-0.68349f,0.69155f), new Vector3(-0.20874f,-0.66162f,0.72020f),
        new Vector3(-0.18350f,-0.63885f,0.74713f), new Vector3(-0.16956f,-0.61970f,0.76630f), new Vector3(-0.13721f,-0.59790f,0.78974f), new Vector3(-0.09380f,-0.57614f,0.81195f),
        new Vector3(-0.04853f,-0.55224f,0.83227f), new Vector3(-0.00936f,-0.53719f,0.84341f), new Vector3(0.02314f,-0.52009f,0.85380f), new Vector3(0.05574f,-0.50864f,0.85917f),
        new Vector3(0.08602f,-0.50222f,0.86045f), new Vector3(0.11132f,-0.49632f,0.86097f), new Vector3(0.14075f,-0.48956f,0.86054f), new Vector3(0.17672f,-0.48944f,0.85394f),
        new Vector3(0.22359f,-0.50016f,0.83657f), new Vector3(0.26505f,-0.51131f,0.81750f), new Vector3(0.30579f,-0.52637f,0.79337f), new Vector3(0.34572f,-0.54664f,0.76267f),
        new Vector3(0.38457f,-0.57617f,0.72120f), new Vector3(0.43217f,-0.59844f,0.67461f), new Vector3(0.47579f,-0.61565f,0.62816f), new Vector3(0.50529f,-0.64470f,0.57362f),
        new Vector3(0.52684f,-0.67158f,0.52098f),
    };


    // 脚の平均向き (歩幅を詰めるときの寄せ先)。配列より後に置くこと。
    static Vector3 Avg(Vector3[] a)
    { Vector3 v = Vector3.zero; for (int i = 0; i < a.Length; i++) v += a[i]; return v.normalized; }
    static readonly Vector3 LegLeftUpLegMeanY = Avg(LegLeftUpLegWalkYDir);
    static readonly Vector3 LegLeftUpLegMeanX = Avg(LegLeftUpLegWalkXDir);
    static readonly Vector3 LegLeftLegMeanY = Avg(LegLeftLegWalkYDir);
    static readonly Vector3 LegLeftLegMeanX = Avg(LegLeftLegWalkXDir);
    static readonly Vector3 LegLeftFootMeanY = Avg(LegLeftFootWalkYDir);
    static readonly Vector3 LegLeftFootMeanX = Avg(LegLeftFootWalkXDir);
    static readonly Vector3 LegLeftToeMeanY = Avg(LegLeftToeWalkYDir);
    static readonly Vector3 LegLeftToeMeanX = Avg(LegLeftToeWalkXDir);
    static readonly Vector3 LegRightUpLegMeanY = Avg(LegRightUpLegWalkYDir);
    static readonly Vector3 LegRightUpLegMeanX = Avg(LegRightUpLegWalkXDir);
    static readonly Vector3 LegRightLegMeanY = Avg(LegRightLegWalkYDir);
    static readonly Vector3 LegRightLegMeanX = Avg(LegRightLegWalkXDir);
    static readonly Vector3 LegRightFootMeanY = Avg(LegRightFootWalkYDir);
    static readonly Vector3 LegRightFootMeanX = Avg(LegRightFootWalkXDir);
    static readonly Vector3 LegRightToeMeanY = Avg(LegRightToeWalkYDir);
    static readonly Vector3 LegRightToeMeanX = Avg(LegRightToeWalkXDir);

    // 平均姿勢。static フィールドは宣言順に初期化されるので、上の配列より後に置くこと。
    static readonly Quaternion SpineMean = MeanBasis(SpineWalkYDir, SpineWalkXDir);
    static readonly Quaternion Spine01Mean = MeanBasis(Spine01WalkYDir, Spine01WalkXDir);
    static readonly Quaternion Spine02Mean = MeanBasis(Spine02WalkYDir, Spine02WalkXDir);
    static readonly Quaternion NeckMean = MeanBasis(NeckWalkYDir, NeckWalkXDir);
    static readonly Quaternion HeadMean = MeanBasis(HeadWalkYDir, HeadWalkXDir);
    static readonly Quaternion LeftShoulderMean = MeanBasis(LeftShoulderWalkYDir, LeftShoulderWalkXDir);
    static readonly Quaternion RightShoulderMean = MeanBasis(RightShoulderWalkYDir, RightShoulderWalkXDir);
}
