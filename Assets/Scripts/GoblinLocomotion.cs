using UnityEngine;
using UnityEngine.InputSystem;

// Movement scheme per 設計図.png "操作方法(キーボード)": Arrow keys drive movement
// (Up/Down = forward/back, Left/Right = turn-in-place), NOT WASD. This is a deliberate choice:
// the arm-balance controls need Q/A/E/D (see ArmBalanceController), and 設計図.png's own worked
// example ("Q+D = 右腕を下げながら左腕を上げる") only makes sense if D is never also a movement
// key. アニメーション遷移図.png's summary box lists WASD+strafe instead, which collides with
// the arm keys (A/D used for both "strafe" and "lower arm") -- see WORKLOG.md for the full
// reasoning. Strafe Left/Right animator states still exist (see CarrySetupBalanceGame) for
// spec fidelity but are unreachable from this input scheme; flagged in the worklog.
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
        float moveZ = 0f;   // +1 forward (Up), -1 backward (Down)
        float turnX = 0f;   // +1 turn right, -1 turn left
        // SWAPPED AGAIN 2026-08-12 per explicit request ("走りとジャンプのキーを入れ替えたい。
        // シフトとスペース"): run is now held Shift, jump is now pressed Space (reverse of the
        // previous swap earlier the same day).
        bool runHeld = false;
        if (kb != null)
        {
            if (kb.upArrowKey.isPressed) moveZ += 1f;
            if (kb.downArrowKey.isPressed) moveZ -= 1f;
            if (kb.rightArrowKey.isPressed) turnX += 1f;
            if (kb.leftArrowKey.isPressed) turnX -= 1f;
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
            jumpHorizontalSpeed = IsRunning ? runSpeed : (IsMoving ? walkSpeed : 0f);
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
