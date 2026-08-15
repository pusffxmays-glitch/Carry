using UnityEngine;

// ============================================================================================
// GoblinGroundSlide -- 摩擦の低い地面（氷の坂など）で滑らせる。
//
// **なぜ自前で書くのか**:
// ゴブリンは CharacterController で動く。CharacterController は Rigidbody のソルバを
// 通らないので、Collider に PhysicMaterial を貼っても **摩擦は一切効かない**。
// 「摩擦係数の低い勾配」を作るには、滑る速度を自分で積分して Move する必要がある。
//
// 地面の摩擦と法線は GoblinTerrainTilt が既に毎フレーム Raycast で取っているので、
// それを読むだけにして二重に撃たない。
// ============================================================================================
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(5)]   // GoblinLocomotion(既定 0) の後に足す
public class GoblinGroundSlide : MonoBehaviour
{
    [Tooltip("滑りの最大速度 (m/s)。")]
    public float maxSlideSpeed = 8f;
    [Tooltip("この角度(度)未満の斜面では滑らない。段差の継ぎ目などで微妙な法線が出ても動き出さないための下限。")]
    public float minSlopeDeg = 2f;
    [Tooltip("足元から地面までがこの距離(m)以内なら接地とみなす。CharacterController.isGrounded は使わない（下のコメント参照）。")]
    public float groundedDistance = 0.20f;
    [Tooltip("滑っている間、地面へ押し付ける速さ (m/s)。斜面に沿って動くと接地が外れやすいので、少しだけ下へ押す。")]
    public float groundStickSpeed = 2.5f;

    /// <summary>現在の滑り速度 (world, 水平成分)。Debug 用。</summary>
    public Vector3 SlideVelocity => slideVelocity;

    CharacterController controller;
    GoblinTerrainTilt tilt;
    Vector3 slideVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        tilt = GetComponent<GoblinTerrainTilt>();
    }

    /// <summary>滑りを止める。ワープ直後など、前の場所の勢いを持ち越したくないとき。</summary>
    public void ResetSlide() { slideVelocity = Vector3.zero; }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // **接地判定に controller.isGrounded を使わない。**
        // isGrounded は「最後に呼んだ Move の結果」でしかない。ここは
        // GoblinLocomotion の Move より後に自分でも Move するので、斜面に沿って
        // 横へ動かした結果 isGrounded が false に落ち、次のフレームは「空中」の枝へ入る。
        // すると加速と減速を毎フレーム往復し、滑らずに **その場で振動する**
        // （実測: 滑り速度が -0.05 → -0.02 → -0.09 と行き来し、24 フレームで 2.4cm しか進まない）。
        // 実測した足元からの距離で判定すれば、自分の Move に影響されない。
        float friction = 1f;
        Vector3 n = Vector3.up;
        bool grounded;
        if (tilt != null)
        {
            friction = tilt.GroundFriction;
            n = tilt.GroundNormal;
            grounded = tilt.HasGround && tilt.GroundDistance <= groundedDistance;
        }
        else
        {
            grounded = controller.isGrounded;
        }

        if (grounded)
        {
            // 斜面上のクーロン摩擦そのもの。
            //   下ろうとする加速度 = g sinθ
            //   摩擦が止める加速度 = g μ cosθ
            // 差が正なら滑り出し、負なら止まる。μ = GroundSurface.friction。
            // μ = 1 の通常の地面は tanθ > 1（45 度超）でないと滑らないので、
            // 15 度の坂では一切滑らない。μ = 0.08 の氷は約 4.6 度から滑り出す。
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, n);
            float sin = Mathf.Clamp01(downhill.magnitude);
            float cos = Mathf.Clamp01(Mathf.Abs(n.y));
            float slopeDeg = Mathf.Asin(sin) * Mathf.Rad2Deg;
            float g = Mathf.Abs(Physics.gravity.y);

            float net = g * (sin - friction * cos);
            if (slopeDeg >= minSlopeDeg && net > 0f && downhill.sqrMagnitude > 1e-8f)
                slideVelocity += downhill.normalized * net * dt;
            else
                slideVelocity = Vector3.MoveTowards(slideVelocity, Vector3.zero,
                                                    Mathf.Max(g * friction * cos, 0.1f) * dt);
        }
        else
        {
            // 空中では滑りを増やさない。着地時に持ち越すぶんだけ緩やかに減らす。
            slideVelocity = Vector3.MoveTowards(slideVelocity, Vector3.zero, 2f * dt);
        }

        if (slideVelocity.sqrMagnitude > maxSlideSpeed * maxSlideSpeed)
            slideVelocity = slideVelocity.normalized * maxSlideSpeed;

        if (slideVelocity.sqrMagnitude > 1e-6f)
        {
            // 斜面に沿って動くと接地がわずかに外れる。少しだけ下へ押し付けて
            // 貼り付かせる。これが無いと「滑る → 浮く → 落ちる」を繰り返してガタつく。
            Vector3 stick = grounded ? Vector3.down * groundStickSpeed : Vector3.zero;
            controller.Move((slideVelocity + stick) * dt);
        }
    }
}
