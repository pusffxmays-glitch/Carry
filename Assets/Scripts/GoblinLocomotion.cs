using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class GoblinLocomotion : MonoBehaviour
{
    // Carry_Walk_Low is a slow, deliberate "carrying carefully" gait (~1 step per 1.4s),
    // not a brisk continuous walk cycle. A lower walkSpeed reduces (but can't fully remove,
    // since movement is code-driven and not root-motion) the foot-slide look; too low and
    // it reads as "not moving" in a big room. Tune by eye in Play Mode.
    public float walkSpeed = 1.0f;
    public float runSpeed = 5.0f;
    public float turnSmoothTime = 0.12f;
    public float gravity = -20f;
    public float terminalVelocity = -20f;

    [Header("Jump")]
    public float jumpSpeed = 6f;

    CharacterController controller;
    Animator animator;
    float turnSmoothVelocity;
    float verticalVelocity;
    // Horizontal speed is locked in at takeoff so tapping Space mid-air (after the jump
    // already committed to the walk- or run-jump animation) can't speed the character up
    // while it's still airborne / mid-animation.
    float jumpHorizontalSpeed;

    static readonly int JumpFromWalkHash = Animator.StringToHash("JumpFromWalk");
    static readonly int JumpFromRunHash = Animator.StringToHash("JumpFromRun");

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
        float h = 0f, v = 0f;
        if (kb != null)
        {
            if (kb.aKey.isPressed) h -= 1f;
            if (kb.dKey.isPressed) h += 1f;
            if (kb.sKey.isPressed) v -= 1f;
            if (kb.wKey.isPressed) v += 1f;
        }

        Vector3 inputDir = new Vector3(h, 0f, v);
        bool isMoving = inputDir.sqrMagnitude > 0.0001f;
        bool isRunning = isMoving && kb != null && kb.spaceKey.isPressed;

        // "Are we still mid-jump" is read directly from the Animator instead of a separate
        // timer, so it can never drift out of sync with when the JumpFromWalk/JumpFromRun
        // clip actually hands control back to Walk/Run (a fixed-duration timer previously
        // let the Animator switch to Run slightly before the code did, so for ~0.1s the
        // run animation was playing while movement speed was still locked to the takeoff
        // speed -- looked like running in place).
        bool inJumpState = IsInJumpState();

        // Jump: Left Shift. Only from the ground and only when not already mid-jump.
        bool canJump = controller.isGrounded && !inJumpState;
        bool jumpTriggered = kb != null && kb.leftShiftKey.wasPressedThisFrame && canJump;
        if (jumpTriggered)
        {
            animator.SetTrigger("Jump");
            jumpHorizontalSpeed = isRunning ? runSpeed : walkSpeed;
            inJumpState = true; // about to transition this frame; treat as locked immediately
        }

        // Vertical velocity is resolved once per frame -- computing it separately inside
        // both the moving/stationary branches let a same-frame jump impulse get immediately
        // stomped back to -1 by the grounded check, so it lives here instead.
        if (jumpTriggered) verticalVelocity = jumpSpeed;
        else ApplyVerticalVelocity();

        Vector3 horizontalMove = Vector3.zero;
        if (isMoving)
        {
            float camYaw = CarryCameraRig.Instance != null ? CarryCameraRig.Instance.Yaw : transform.eulerAngles.y;
            float targetAngle = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg + camYaw;
            float smoothAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, smoothAngle, 0f);

            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            float speed = inJumpState ? jumpHorizontalSpeed : (isRunning ? runSpeed : walkSpeed);
            horizontalMove = moveDir.normalized * speed;
        }

        Vector3 fullMove = horizontalMove;
        fullMove.y = verticalVelocity;
        controller.Move(fullMove * Time.deltaTime);

        // Stopping should freeze on the current pose (Walk or Run), not jump to a
        // separate idle animation -- so we gate playback speed instead of switching state.
        // While a jump is in flight keep the animator playing regardless of movement input
        // so the one-shot jump clip isn't frozen mid-air.
        animator.speed = (isMoving || inJumpState) ? 1f : 0f;
        animator.SetBool("IsRunning", isRunning);
    }

    bool IsInJumpState()
    {
        var current = animator.GetCurrentAnimatorStateInfo(0);
        bool currentIsJump = current.shortNameHash == JumpFromWalkHash || current.shortNameHash == JumpFromRunHash;
        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            bool nextIsJump = next.shortNameHash == JumpFromWalkHash || next.shortNameHash == JumpFromRunHash;
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
