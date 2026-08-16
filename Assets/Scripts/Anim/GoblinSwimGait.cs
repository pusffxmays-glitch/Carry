using UnityEngine;

// 浮かび泳ぎ (バタ足) の歩容供給。GoblinRopeGait と同じ API 形式で、
// GoblinCarryRig.ApplyWalkCycle が水中のとき GoblinWalk の代わりに使う。
// データは GoblinClipData_Swim (Carry_Swim ベイク)。腕は運搬 IK のままなのでマスク不要。
public static class GoblinSwimGait
{
    /// <summary>ApplyBasePose の GroundOffset と同じ値。泳ぎの腰位置は接地正規化ではなく
    /// リグ本来の座標系 (ニュートラルと同じ基準) で使うため、ベイク生値にこれを足す。</summary>
    const float RigGroundOffsetY = 0.233840f;

    static int iHips = -1, iLUp, iLLeg, iLFoot, iRUp, iRLeg, iRFoot;
    static GoblinClip _clip;

    static GoblinClip Clip => _clip ??= new GoblinClip
    {
        name = "Swim", frameCount = GoblinClipData_Swim.FrameCount, fps = GoblinClipData_Swim.Fps,
        loop = true, groundY = GoblinClipData_Swim.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_Swim.Bones,
        pos = GoblinClipData_Swim.Pos, ydir = GoblinClipData_Swim.YDir, xdir = GoblinClipData_Swim.XDir,
    };

    static void EnsureIndices()
    {
        if (iHips >= 0) return;
        var c = Clip;
        iHips = c.BoneIndex("Hips");
        iLUp = c.BoneIndex("RightUpLeg"); iLLeg = c.BoneIndex("RightLeg"); iLFoot = c.BoneIndex("RightFoot");
        iRUp = c.BoneIndex("LeftUpLeg"); iRLeg = c.BoneIndex("LeftLeg"); iRFoot = c.BoneIndex("LeftFoot");
    }

    static void Sample(int idx, float phase01, out Vector3 y, out Vector3 x)
    {
        var c = Clip;
        c.SampleBone(idx, Mathf.Repeat(phase01, 1f) * c.frameCount, out _, out y, out x);
    }

    public static void SampleHips(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iHips, p, out y, out x); }
    public static void SampleLeftUpLeg(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iLUp, p, out y, out x); }
    public static void SampleLeftLeg(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iLLeg, p, out y, out x); }
    public static void SampleLeftFoot(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iLFoot, p, out y, out x); }
    public static void SampleRightUpLeg(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iRUp, p, out y, out x); }
    public static void SampleRightLeg(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iRLeg, p, out y, out x); }
    public static void SampleRightFoot(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iRFoot, p, out y, out x); }

    /// <summary>腰の root ローカル位置 (リグ本来の座標系)。後傾 + ぷかぷかが入っている。</summary>
    public static Vector3 SampleHipsPosNative(float phase01)
    {
        EnsureIndices();
        var c = Clip;
        c.SampleBone(iHips, Mathf.Repeat(phase01, 1f) * c.frameCount, out Vector3 p, out _, out _);
        // GroundOffset 前提の ApplyBasePose 座標系に合わせる: ベイク生値 + RigGroundOffsetY
        p.y += RigGroundOffsetY;
        return p;
    }
}
