using UnityEngine;

// ============================================================================================
// GroundSurface -- 足元の地面の性質（今は摩擦だけ）を持たせるマーカー。
//
// **なぜ PhysicMaterial ではないのか**:
// ゴブリンは CharacterController で動いている。CharacterController は Rigidbody の
// ソルバを通らないので、**Collider に貼った PhysicMaterial の摩擦を一切見ない**。
// 氷の坂を作りたければ、滑る挙動は自前で書くしかない (GoblinGroundSlide)。
// このコンポーネントはそのための「地面の摩擦値」を置く場所。
// ============================================================================================
public class GroundSurface : MonoBehaviour
{
    // 斜面のクーロン摩擦係数 μ。滑り出す角度は atan(μ)。
    //   1.00 → 45 度まで滑らない（通常の地面）
    //   0.30 → 約 17 度から滑る
    //   0.08 → 約 4.6 度から滑る（氷）
    [Tooltip("摩擦係数 μ。滑り出す角度は atan(μ)。1 = 通常の地面、0.08 = 氷。")]
    [Range(0f, 1f)] public float friction = 1f;

    [Tooltip("Scene ビューでの識別用。挙動には影響しない。")]
    public string label = "";
}
