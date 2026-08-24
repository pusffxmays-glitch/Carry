using UnityEngine;

// ジャンプ姿勢セットの受け口 (2026-08-24)。
//
// 静止からのジャンプと歩行/走行からのジャンプは、人体としては別の動作:
//   静止  両足で沈み込み、真上へ伸び上がり、両足で着地する (左右対称)
//   歩行  片足で蹴り出し、空中で脚が前後に開き、片足で着地する (左右非対称)
// これを 1 つの姿勢セットで表そうとすると、どちらかが嘘になる。セットを差し替え可能に
// しておき、踏み切った瞬間の移動速度でどちらを使うかを決める。
//
// 姿勢は 0〜1 の「ジャンプ姿勢軸 u」で引く。u のどこが溜め/踏切/滞空/着地かはセットごとに
// 違う (素材の並び順が違うため) ので、セット自身が申告する。
public interface IGoblinJumpPoses
{
    /// <summary>溜め (踏み切る前の荷重) の姿勢軸。</summary>
    float UCrouch { get; }
    /// <summary>踏切 (伸び上がり/蹴り出し) の姿勢軸。</summary>
    float UExtend { get; }
    /// <summary>滞空の姿勢軸。</summary>
    float UAir { get; }
    /// <summary>着地の沈み込みの姿勢軸。</summary>
    float ULand { get; }

    /// <summary>踏切の瞬間に接地している側。true = リグの leftFootBone 側。
    /// 歩行中どちらの足が接地しているかと突き合わせて、踏み切る足の合うセットを選ぶ。</summary>
    bool SupportIsLeftSide { get; }

    // 腰の高さの補正は持たない。素材の立ち高さが担ぎ姿勢と違っても、脚の向きは絶対値で
    // 入るので足が腰に追従し、最終的に ClampFeetToGround (持ち上げ専用) が体を地面へ戻す。
    // 補正を掛けても打ち消されるだけだった (実測)。

    Vector3 SampleHipsPos(float u);

    void SampleHips(float u, out Vector3 yDir, out Vector3 xDir);
    void SampleLeftUpLeg(float u, out Vector3 yDir, out Vector3 xDir);
    void SampleLeftLeg(float u, out Vector3 yDir, out Vector3 xDir);
    void SampleLeftFoot(float u, out Vector3 yDir, out Vector3 xDir);
    void SampleLeftToe(float u, out Vector3 yDir, out Vector3 xDir);
    void SampleRightUpLeg(float u, out Vector3 yDir, out Vector3 xDir);
    void SampleRightLeg(float u, out Vector3 yDir, out Vector3 xDir);
    void SampleRightFoot(float u, out Vector3 yDir, out Vector3 xDir);
    void SampleRightToe(float u, out Vector3 yDir, out Vector3 xDir);

    Quaternion SampleSpineAdd(float u);
    Quaternion SampleSpine01Add(float u);
    Quaternion SampleSpine02Add(float u);
    Quaternion SampleNeckAdd(float u);
    Quaternion SampleHeadAdd(float u);
    Quaternion SampleLeftShoulderAdd(float u);
    Quaternion SampleRightShoulderAdd(float u);
}
