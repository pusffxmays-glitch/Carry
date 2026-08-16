using UnityEngine;

// ============================================================================================
// GoblinRopeGait -- 細い足場 (平均台) 用の歩行サイクルを GoblinWalk と同じ API で供給する。
//
// 2026-08-15 追加。データは GoblinClipData_RopeWalk (Blender の Carry_RopeWalk を bake)。
// GoblinCarryRig.ApplyWalkCycle が NarrowBeamSensor.OnBeam のとき GoblinWalk の代わりに
// これをサンプリングする。足はほぼ一直線 (x≒±0.03) に置かれ、腰は支持脚側へ移る。
//
// 命名の約束は GoblinWalk と同じ: SampleLeft* は「Blender の Left ボーンのデータ」で、
// Unity 側では名前が入れ替わったボーン (リグの leftUpLegBone = Unity 名 "RightUpLeg") に
// 適用される。ベイク時に名前スワップ済みなので、"RightUpLeg" の行が SampleLeft* に対応する。
// ============================================================================================
public static class GoblinRopeGait
{
    /// <summary>1 周で進む距離 (m)。位相速度 = 移動速度 / これ。gen_ropewalk.py の STRIDE*2。</summary>
    public const float StrideLength = 0.72f;

    static int iHips = -1, iLUp, iLLeg, iLFoot, iRUp, iRLeg, iRFoot;

    static void EnsureIndices()
    {
        if (iHips >= 0) return;
        var c = GoblinClip.RopeWalk;
        iHips = c.BoneIndex("Hips");
        // ベイク済み名は Blender から見て左右スワップ済み: Blender Left -> Unity "Right..."
        iLUp = c.BoneIndex("RightUpLeg"); iLLeg = c.BoneIndex("RightLeg"); iLFoot = c.BoneIndex("RightFoot");
        iRUp = c.BoneIndex("LeftUpLeg"); iRLeg = c.BoneIndex("LeftLeg"); iRFoot = c.BoneIndex("LeftFoot");
    }

    static void Sample(int idx, float phase01, out Vector3 y, out Vector3 x)
    {
        EnsureIndices();
        var c = GoblinClip.RopeWalk;
        c.SampleBone(idx, Mathf.Repeat(phase01, 1f) * c.frameCount, out _, out y, out x);
    }

    public static void SampleHips(float p, out Vector3 y, out Vector3 x) { Sample(iHipsSafe(), p, out y, out x); }
    static int iHipsSafe() { EnsureIndices(); return iHips; }
    public static void SampleLeftUpLeg(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iLUp, p, out y, out x); }
    public static void SampleLeftLeg(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iLLeg, p, out y, out x); }
    public static void SampleLeftFoot(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iLFoot, p, out y, out x); }
    public static void SampleRightUpLeg(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iRUp, p, out y, out x); }
    public static void SampleRightLeg(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iRLeg, p, out y, out x); }
    public static void SampleRightFoot(float p, out Vector3 y, out Vector3 x) { EnsureIndices(); Sample(iRFoot, p, out y, out x); }

    /// <summary>腰の root ローカル位置 (接地正規化済み)。ロープ歩きは腰の左右移動が本体。</summary>
    public static Vector3 SampleHipsPos(float phase01)
    {
        EnsureIndices();
        var c = GoblinClip.RopeWalk;
        c.SampleBone(iHips, Mathf.Repeat(phase01, 1f) * c.frameCount, out Vector3 p, out _, out _);
        p.y -= c.groundY;
        return p;
    }
}
