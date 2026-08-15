using UnityEngine;
using UnityEngine.InputSystem;

// 操作系 (2026-08-14 にユーザー指定で変更):
//   移動      = WASD   … W/S 前後、A/D その場旋回、Shift 走り、Space ジャンプ
//   壺のバランス = 矢印キー … 左右で左右バランス、上で前傾／下で後傾（GoblinCarryRig）
//
// 以前は移動が矢印キー、腕のバランスが Q/E だった。壺のバランスに前後の軸
// （前傾・後傾）を足したことで操作が 2 軸になり、矢印キーで一括して扱う方が
// 分かりやすいため入れ替えた。移動と壺の操作でキーが衝突しないので、
// 旧構成にあった「A/D が旋回と腕操作で取り合いになる」問題も起きない。
//
// Strafe Left/Right の Animator ステートは仕様どおり残っているが、この入力構成では
// 到達しない（A/D は旋回に割り当てているため）。従来どおり worklog に記録済み。
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class GoblinLocomotion : MonoBehaviour
{
    // Carry_Walk_Low is a slow, deliberate "carrying carefully" gait (~1 step per 1.4s),
    // not a brisk continuous walk cycle. A lower walkSpeed reduces (but can't fully remove,
    // since movement is code-driven and not root-motion) the foot-slide look; too low and
    // it reads as "not moving" in a big room. Tune by eye in Play Mode.
    public float walkSpeed = 1.0f;
    public float runSpeed = 3.0f;
    public float backStepSpeedMultiplier = 0.6f;
    public float turnSpeed = 110f; // deg/sec while Left/Right arrow held
    public float gravity = -20f;
    public float terminalVelocity = -20f;

    [Header("Jump")]
    public float jumpSpeed = 6f;
    // ADDED 2026-08-15 (要望「W＋ジャンプはもっと飛距離出るように」): 歩きジャンプは
    // walkSpeed 1.5 をそのまま引き継ぐと滞空 0.6 秒で約 0.9m しか飛ばない。
    // 離陸時だけ水平速度を増幅して 2.4 m/s ≒ 1.44m にする。
    // **1.6 倍より大きくしすぎないこと**: Jump_Platforms (ギミック 5) は
    // 「走りジャンプなら届き、歩きジャンプでは届かない隙間 1.6m」の設計なので、
    // 歩きジャンプの飛距離 (walkSpeed x これ x 0.6s) は 1.6m 未満に収める。
    // 走りジャンプ (runSpeed 5 ≒ 3m) には掛けない。
    [Tooltip("歩き中ジャンプの水平速度倍率。飛距離 ≒ walkSpeed x これ x 0.6s。1.78 以上でジャンプ台の隙間 1.6m を歩きで越えられてしまう。")]
    public float walkJumpBoost = 1.6f;

    CharacterController controller;
    Animator animator;
    float verticalVelocity;
    // Horizontal speed is locked in at takeoff so tapping Space mid-air (after the jump
    // already committed to the walk- or run-jump animation) can't speed the character up
    // while it's still airborne / mid-animation.
    float jumpHorizontalSpeed;

    static readonly int JumpFromIdleHash = Animator.StringToHash("JumpFromIdle");
    static readonly int JumpFromWalkHash = Animator.StringToHash("JumpFromWalk");
    static readonly int JumpFromRunHash = Animator.StringToHash("JumpFromRun");

    // Read by ArmBalanceController / PotRigController / BalanceWobbleController so every
    // system agrees on "are we moving / how fast" without re-reading Keyboard state itself.
    public bool IsMoving { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsMovingBackward { get; private set; }
    public float CurrentSpeed { get; private set; }
    public float TurnInputThisFrame { get; private set; } // -1..1, for wobble-on-direction-change

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
    }

    void Update()
    {
        bool thirdPerson = CarryCameraRig.Instance == null || CarryCameraRig.Instance.IsThirdPerson;

        if (!thirdPerson)
        {
            ApplyGravityOnly();
            animator.speed = 0f;
            return;
        }

        var kb = Keyboard.current;
        float moveZ = 0f;   // +1 forward (W), -1 backward (S)
        float turnX = 0f;   // +1 turn right (D), -1 turn left (A)
        // SWAPPED AGAIN 2026-08-12 per explicit request ("走りとジャンプのキーを入れ替えたい。
        // シフトとスペース"): run is now held Shift, jump is now pressed Space (reverse of the
        // previous swap earlier the same day).
        bool runHeld = false;
        if (kb != null)
        {
            // 移動は WASD。矢印キーは壺のバランス操作 (GoblinCarryRig) に割り当てている。
            if (kb.wKey.isPressed) moveZ += 1f;
            if (kb.sKey.isPressed) moveZ -= 1f;
            if (kb.dKey.isPressed) turnX += 1f;
            if (kb.aKey.isPressed) turnX -= 1f;
            runHeld = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        }

        TurnInputThisFrame = turnX;
        if (Mathf.Abs(turnX) > 0.001f)
        {
            transform.Rotate(0f, turnX * turnSpeed * Time.deltaTime, 0f, Space.World);
        }

        bool movingForward = moveZ > 0.001f;
        bool movingBackward = moveZ < -0.001f;
        IsMoving = movingForward || movingBackward;
        IsMovingBackward = movingBackward && !movingForward;
        // Dash/Run (Shift, held) only applies to forward movement; back-stepping always
        // uses the slower dedicated back-step speed per the anim transition spec.
        IsRunning = movingForward && runHeld;

        bool inJumpState = IsInJumpState();

        // Jump: Space. Only from the ground and only when not already mid-jump.
        bool canJump = controller.isGrounded && !inJumpState;
        bool jumpTriggered = kb != null
            && kb.spaceKey.wasPressedThisFrame
            && canJump;
        if (jumpTriggered)
        {
            animator.SetTrigger("Jump");
            jumpHorizontalSpeed = IsRunning ? runSpeed : (IsMoving ? walkSpeed * walkJumpBoost : 0f);
            inJumpState = true; // about to transition this frame; treat as locked immediately
        }

        // Vertical velocity is resolved once per frame -- computing it separately inside
        // both the moving/stationary branches let a same-frame jump impulse get immediately
        // stomped back to -1 by the grounded check, so it lives here instead.
        if (jumpTriggered) verticalVelocity = jumpSpeed;
        else ApplyVerticalVelocity();

        Vector3 horizontalMove = Vector3.zero;
        if (inJumpState)
        {
            // Keep the takeoff direction/speed locked for the whole jump so late Space taps
            // (or releasing the arrow key mid-air) can't alter it.
            horizontalMove = transform.forward * jumpHorizontalSpeed;
        }
        else if (IsMoving)
        {
            float speed = movingForward ? (IsRunning ? runSpeed : walkSpeed) : walkSpeed * backStepSpeedMultiplier;
            horizontalMove = transform.forward * Mathf.Sign(moveZ) * speed;
        }

        CurrentSpeed = horizontalMove.magnitude;

        Vector3 fullMove = horizontalMove;
        fullMove.y = verticalVelocity;
        controller.Move(fullMove * Time.deltaTime);

        // Stopping should freeze on the current pose, not jump to a separate idle animation --
        // so playback speed is gated instead of switching state for idle. While a jump is in
        // flight keep the animator playing regardless of movement input so the one-shot jump
        // clip isn't frozen mid-air.
        animator.speed = (IsMoving || inJumpState) ? 1f : 0f;
        animator.SetBool("IsMoving", IsMoving);
        animator.SetBool("IsRunning", IsRunning);
        animator.SetBool("IsMovingBackward", IsMovingBackward);
        // Always false from keyboard in the arrow-key movement scheme (see class comment);
        // kept as Animator params so the Strafe states exist and can be driven by another
        // input source later without touching the Animator Controller again.
        animator.SetBool("StrafeLeftInput", false);
        animator.SetBool("StrafeRightInput", false);
    }

    static bool IsJumpHash(int hash)
    {
        return hash == JumpFromIdleHash || hash == JumpFromWalkHash || hash == JumpFromRunHash;
    }

    bool IsInJumpState()
    {
        var current = animator.GetCurrentAnimatorStateInfo(0);
        bool currentIsJump = IsJumpHash(current.shortNameHash);
        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            bool nextIsJump = IsJumpHash(next.shortNameHash);
            return currentIsJump || nextIsJump;
        }
        return currentIsJump;
    }

    void ApplyVerticalVelocity()
    {
        if (controller.isGrounded)
        {
            verticalVelocity = -1f;
        }
        else
        {
            verticalVelocity = Mathf.Max(verticalVelocity + gravity * Time.deltaTime, terminalVelocity);
        }
    }

    void ApplyGravityOnly()
    {
        ApplyVerticalVelocity();
        controller.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
    }
}
