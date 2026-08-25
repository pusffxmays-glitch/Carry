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
    [Tooltip("走り中ジャンプの水平速度倍率。飛距離 = runSpeed x これ x 滞空秒。runSpeed を下げても飛距離を保つための係数。")]
    public float runJumpBoost = 2.08f;
    public float backStepSpeedMultiplier = 0.6f;
    public float turnSpeed = 110f; // deg/sec while Left/Right arrow held
    public float gravity = -20f;
    public float terminalVelocity = -20f;

    [Header("Jump")]
    // 2026-08-24: 6 → 7 (ユーザー指定「もう少し高くてもいい」)。重力 -20 なので
    // 到達高さは 6^2/40 = 0.90m → 7^2/40 = 1.23m。
    public float jumpSpeed = 7f;

    // 溜め (anticipation)。押した瞬間に飛ばさず、この時間だけしゃがんでから踏み切る。
    // これが無いと体がそのまま上へ跳ね上がるだけでジャンプに見えない (ユーザー指摘)。
    // 長くすると入力が重く感じるので、目に見える最小限に留めること。
    [Tooltip("静止からのジャンプの溜め時間 (秒)。長くすると重く感じる。")]
    public float jumpAnticipation = 0.12f;

    // 走りながら屈む人はいない。移動中は既に脚に荷重が乗っているので、溜めは短く済む。
    // ここを静止と同じにすると、歩いていて跳ぼうとした瞬間に一度止まったように見える。
    // 0.06 だと歩行姿勢から踏切姿勢へ 4 コマで移り、そこがつなぎ目の最大 (23度/コマ) に
    // なっていた。0.08 で 18 度まで下がり、最大の位置も入口から離れる (実測)。
    [Tooltip("歩行/走行中のジャンプの溜め時間 (秒)。静止時より短くする。短すぎると踏切で姿勢が飛ぶ。")]
    public float jumpAnticipationMoving = 0.08f;

    /// <summary>入力から実際に地面を離れるまでの時間。姿勢側 (GoblinCarryRig) が
    /// 「沈み込み + 伸び上がりの途中まで」として持っているので、あればそれを使う。
    /// リグが無い場合 (壺なし等) だけ下の jumpAnticipation を使う。</summary>
    public float CurrentJumpAnticipation
    {
        get
        {
            var rig = GetComponent<GoblinCarryRig>();
            if (rig != null) return rig.PreLaunchTime(IsMoving);
            return IsMoving ? jumpAnticipationMoving : jumpAnticipation;
        }
    }
    float pendingJumpAt = -1f;

    // 調査用: 入力なしでジャンプを起こす。ゲームビューが非フォーカスだと InputSystem への
    // 入力注入が毎フレーム破棄されるため、エディタ外 (MCP) からジャンプを試せない (実測)。
    // 立てた次の Update で 1 回だけ消費される。
    [HideInInspector] public bool debugJumpRequest;

    // 調査用: 入力なしで前進させる。ゲームビューが非フォーカスだと入力注入が破棄されるため、
    // エディタ外 (MCP) から「歩きながらの挙動」を観測できない。true の間ずっと前進する。
    [HideInInspector] public bool debugMoveForward;
    [HideInInspector] public bool debugRun;      // 計測用: Shift を押しているものとして扱う

    /// <summary>溜め中 (入力済みだがまだ踏み切っていない) か。</summary>
    public bool JumpCharging => pendingJumpAt > 0f;
    /// <summary>現在の上下速度 (m/s)。ジャンプ姿勢の上昇/落下判定に使う。</summary>
    public float VerticalVelocity => verticalVelocity;
    /// <summary>接地しているか。</summary>
    public bool Grounded => controller != null && controller.isGrounded;
    // 熱い床 (マグマ、2026-08-16 ギミック 9): 踏むと強制的に高く飛ばされる。
    [Tooltip("熱い床を踏んだときの強制ジャンプ初速 (m/s)。8.5 で高さ約 3.7m (通常ジャンプ 1.8m の 2 倍)。")]
    public float hotJumpSpeed = 8.5f;
    float hotFloorCooldown;
    bool hotJumpQueued;
    bool hotFlightActive;   // 滞空中の落下速度制限に使う
    // 着地クッション (追補 15): 着地直後の Space をジャンプにしない猶予。
    // 「着地直前に押すつもりが僅かに遅れた」入力が意図しないジャンプに化けるのを防ぐ。
    [HideInInspector] public float jumpSuppressedUntil;
    /// <summary>診断用: Animator がジャンプ状態か。</summary>
    public bool InJumpState { get; private set; }
    /// <summary>診断用: 離陸速度で横移動が固定されているか。</summary>
    public bool JumpLockActive { get; private set; }
    /// <summary>熱い床で飛ばされた瞬間 true を 1 回返す (「あちち」アニメ再生用)。</summary>
    public bool ConsumeHotJump() { bool v = hotJumpQueued; hotJumpQueued = false; return v; }
    /// <summary>熱い床ジャンプで滞空中か (着地で false)。アニメの早期終了判定用。</summary>
    public bool HotFlightActive => hotFlightActive;
    /// <summary>最後にジャンプ (通常・熱い床とも) が始まった時刻。
    /// 追補 16: 離陸の瞬間から壺内 calm を効かせるための通知。滞空検出 (0.12 秒ゲート)
    /// だけだと離陸直後の無重力区間でスロッシュが育ってしまう。</summary>
    public float LastJumpStartTime { get; private set; } = -999f;

    // 追補 25: パリー押下で「膝で受ける」= 残りの落下速度を軟化する。
    // 高所からのパリーでも衝撃自体が小さくなり、clamp だけでは防げない
    // PBF の位置解決由来の吹き出しが減る。
    float softLandUntil = -999f;
    [Tooltip("パリー押下後の落下速度上限 (m/s)。膝のクッションで受ける表現。")]
    public float parrySoftFallSpeed = 3.5f;
    /// <summary>パリー押下時に呼ぶ。seconds の間、落下速度を parrySoftFallSpeed に抑える。</summary>
    public void SoftenLanding(float seconds) { softLandUntil = Time.time + seconds; }
    // ADDED 2026-08-15 (要望「W＋ジャンプはもっと飛距離出るように」): 歩きジャンプは
    // walkSpeed 1.5 をそのまま引き継ぐと滞空 0.6 秒で約 0.9m しか飛ばない。
    // 離陸時だけ水平速度を増幅して 2.4 m/s ≒ 1.44m にする。
    // **1.6 倍より大きくしすぎないこと**: Jump_Platforms (ギミック 5) は
    // 「走りジャンプなら届き、歩きジャンプでは届かない隙間 1.6m」の設計なので、
    // 歩きジャンプの飛距離 (walkSpeed x これ x 0.6s) は 1.6m 未満に収める。
    // 走りジャンプ (runSpeed 5 ≒ 3m) には掛けない。
    // 追補 23: walkSpeed 1.5 → 1.8 に伴い 1.6 → 1.4 へ (飛距離 1.8×1.4×0.6 = 1.51m < 1.6m を維持)。
    [Tooltip("歩き中ジャンプの水平速度倍率。飛距離 ≒ walkSpeed x これ x 0.6s。ジャンプ台の隙間 1.6m を歩きで越えられない値にすること (walkSpeed 1.8 なら 1.48 未満)。")]
    // 2026-08-24 実機修正: 飛距離が足りないという指摘。原因は walkSpeed が 0.9 だったこと
    // (1.5 だと思い込んでいた)。0.9 x 1.4 = 1.26m/s で滞空 0.70 秒 = 0.9m しか進まない。
    // 3.0 で 2.7m/s = 約 1.9m。歩行速度そのものを上げると歩容の足が滑るので、
    // ジャンプ中だけの倍率で稼ぐ ([[carry-walk-stride-decoupling]] 参照)。
    public float walkJumpBoost = 3.0f;

    // 2026-08-16 追補 13: 運搬中の加減速ランプ。
    // 満杯の壺は静止液面がリム直下 (fillFraction 0.95) にあり、瞬間的な速度変化
    // (従来は 1 フレームで 0 -> 1.5 m/s) が作るスロッシュ波が 0.7〜1.0 秒後にリムへ
    // 到達して一気に 25-30% 捨てられていた (WORKLOG 追補 12 の調査)。
    // 加減速を数百 ms かけて行うことで波の生成自体を抑える。
    [Header("Carry acceleration ramp (追補 13)")]
    // 実測 (満杯・平地歩き出し、calm なし): accel 3.0 → 残 71% / 2.0 → 95% / 1.5 → 99%。
    // 追補 22: 1.5 は「歩き出しが遅い」(ユーザー) ため 3.5 へ戻し、加速中だけ
    // 壺内 calm を自動適用してこぼれを抑える方式に変更 (RampingHard を参照)。
    [Tooltip("運搬中の加速上限 (m/s^2)。1.8 で歩行速度まで約 0.8 秒。")]
    // 2026-08-22: 3.5 → 1.8。加速 3.5 は液面の慣性傾き (atan(a/g)≒20 度) がフリーボードを
    // 大きく超え、W で歩き出すたびに後方へこぼれていた (実測系列: calm なしで accel 3.0 → 残 71%、
    // 2.0 → 95%、1.5 → 99%)。クランプ (calm) は fps によって効きが揺れるため、
    // 加速そのものを物理的にこぼれない水準へ下げるのが確実。
    public float carryAccel = 1.8f;
    /// <summary>運搬中に大きく加減速している最中か (追補 22: 加速時 calm のトリガー)。</summary>
    public bool RampingHard { get; private set; }
    [Tooltip("運搬中の減速上限 (m/s^2)。5 なら満杯でも停止時の流出は実測ゼロ (減速は短時間で済むため)。")]
    public float carryDecel = 5f;
    // 旋回の横加速度は v×ω。walk 1.5 m/s × 110°/s = 2.9 m/s^2 で、直進加速と同じ理屈で
    // 満杯時に大量流出する (実測 67% まで減)。移動中だけ旋回速度を落として上限に収める。
    [Tooltip("運搬中の旋回による横加速度上限 (m/s^2)。旋回速度を min(turnSpeed, これ/速度) に制限。静止時は制限なし。")]
    public float carryTurnLatAccel = 1.4f;   // 追補 23: walkSpeed 1.8 で旋回 ~44°/s を維持
    // その場旋回でも 28% 流出した (実測)。原因は旋回開始の瞬間に壁の接線速度が
    // 0 -> 0.88 m/s (リム半径 0.46m × 110°/s) へステップして撹拌波が立つこと。
    // 直進の加速ランプと同じ理屈で、旋回角速度もランプさせる。
    [Tooltip("運搬中の旋回角加速度上限 (deg/s^2)。110°/s まで約 0.7 秒。0 で無効。")]
    public float carryTurnAccel = 150f;
    float smoothedTurnDegPerSec;
    [Tooltip("壺を担いでいる間だけランプを使う (GoblinPotActions が更新)。壺なしは即応のまま。")]
    [HideInInspector] public bool gentleAccel = true;
    float smoothedSignedSpeed;   // 前後方向の平滑化済み速度 (+前 / -後)
    /// <summary>前後方向の符号つき平滑化速度 (+前/-後)。加速度フィードフォワード (追補 28) が読む。</summary>
    public float SignedSpeed => smoothedSignedSpeed;

    // 2026-08-15 追加: ツボおろし/拾い上げ/転倒のワンショット再生中は移動入力を受けない
    // (GoblinPotActions が立てる)。重力だけは効かせる。
    [HideInInspector] public bool movementLocked;
    NarrowBeamSensor beamSensor;   // 細い足場の上では減速する
    GoblinSwimmer swimmer;         // 水中では浮力 + 流れ + 泳ぎ速度 (2026-08-16 川ギミック)

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
        beamSensor = GetComponent<NarrowBeamSensor>();
        swimmer = GetComponent<GoblinSwimmer>();
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

        if (movementLocked)
        {
            // ワンショットアニメ中: 入力は無視、重力だけ適用して静止する。
            ApplyGravityOnly();
            IsMoving = false; IsRunning = false; IsMovingBackward = false;
            CurrentSpeed = 0f; TurnInputThisFrame = 0f;
            smoothedSignedSpeed = 0f; smoothedTurnDegPerSec = 0f;
            animator.speed = 0f;
            animator.SetBool("IsMoving", false);
            animator.SetBool("IsRunning", false);
            return;
        }

        var kb = Keyboard.current;
        float moveZ = 0f;   // +1 forward (W), -1 backward (S)
        float turnX = 0f;   // +1 turn right (D), -1 turn left (A)
        // SWAPPED AGAIN 2026-08-12 per explicit request ("走りとジャンプのキーを入れ替えたい。
        // シフトとスペース"): run is now held Shift, jump is now pressed Space (reverse of the
        // previous swap earlier the same day).
        bool runHeld = debugRun;
        if (kb != null)
        {
            // 移動は WASD。矢印キーは壺のバランス操作 (GoblinCarryRig) に割り当てている。
            if (kb.wKey.isPressed || debugMoveForward) moveZ += 1f;
            if (kb.sKey.isPressed) moveZ -= 1f;
            if (kb.dKey.isPressed) turnX += 1f;
            if (kb.aKey.isPressed) turnX -= 1f;
            runHeld = debugRun || kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        }

        TurnInputThisFrame = turnX;
        {
            // 追補 13: 運搬中の旋回は (a) 横加速度 v×ω を carryTurnLatAccel に収める
            // (移動中のみ)、(b) 角速度の変化自体を carryTurnAccel でランプさせる
            // (その場旋回の撹拌波対策)。壺なしは従来どおり即応。
            float targetTurn = 0f;
            if (Mathf.Abs(turnX) > 0.001f)
            {
                float allowed = turnSpeed;
                if (gentleAccel && carryTurnLatAccel > 0f)
                {
                    float v = Mathf.Abs(smoothedSignedSpeed);
                    if (v > 0.05f)
                        allowed = Mathf.Min(turnSpeed, carryTurnLatAccel / v * Mathf.Rad2Deg);
                }
                targetTurn = turnX * allowed;
            }
            if (gentleAccel && carryTurnAccel > 0f)
                smoothedTurnDegPerSec = Mathf.MoveTowards(smoothedTurnDegPerSec, targetTurn, carryTurnAccel * Time.deltaTime);
            else
                smoothedTurnDegPerSec = targetTurn;
            if (Mathf.Abs(smoothedTurnDegPerSec) > 0.001f)
                transform.Rotate(0f, smoothedTurnDegPerSec * Time.deltaTime, 0f, Space.World);
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
        // 水に浮いている間もジャンプ可 (川から岸へ上がる手段)。
        bool inWaterNow = swimmer != null && swimmer.InWater;
        bool canJump = (controller.isGrounded || inWaterNow) && !inJumpState;
        bool jumpPressed = (kb != null && kb.spaceKey.wasPressedThisFrame) || debugJumpRequest;
        debugJumpRequest = false;
        bool jumpTriggered = jumpPressed
            && canJump
            && Time.time >= jumpSuppressedUntil;   // 追補 15: 着地クッション直後の誤ジャンプ防止

        // 熱い床 (マグマ): 接地した瞬間に強制ハイジャンプ。着地するたび再発射されるので
        // マグマ帯はバウンドしながら渡ることになる。クールダウンは接地 1 回分の多重発火防止。
        if (hotFloorCooldown > 0f) hotFloorCooldown -= Time.deltaTime;
        HotFloorSurface hot = null;
        bool hotLaunch = !jumpTriggered && !inJumpState && controller.isGrounded
                      && hotFloorCooldown <= 0f && (hot = HotFloorUnderfoot()) != null;

        if (jumpTriggered || hotLaunch)
        {
            animator.SetTrigger("Jump");
            // 2026-08-25: runSpeed を下げた (5 → 3) ぶん、走りジャンプの飛距離が半分に
            // なってしまうのでここで戻す。コース側は「歩きジャンプでは越えられないが
            // 走りジャンプなら越えられる」隙間で作ってあるため、移動速度と飛距離は
            // 別々に持たせる。
            jumpHorizontalSpeed = IsRunning ? runSpeed * runJumpBoost
                                            : (IsMoving ? walkSpeed * walkJumpBoost : 0f);
            inJumpState = true; // about to transition this frame; treat as locked immediately
            LastJumpStartTime = Time.time;
            if (hotLaunch)
            {
                hotFloorCooldown = 0.35f;
                hotJumpQueued = true;
                hotFlightActive = true;
            }
        }

        // Vertical velocity is resolved once per frame -- computing it separately inside
        // both the moving/stationary branches let a same-frame jump impulse get immediately
        // stomped back to -1 by the grounded check, so it lives here instead.
        // 通常ジャンプは押した瞬間ではなく、溜めが終わってから踏み切る (jumpAnticipation)。
        // 熱い床は演出上の溜めが無いので即時のまま。
        if (jumpTriggered) pendingJumpAt = Time.time + CurrentJumpAnticipation;

        bool launchNow = pendingJumpAt > 0f && Time.time >= pendingJumpAt;
        if (launchNow) pendingJumpAt = -1f;

        if (launchNow) verticalVelocity = jumpSpeed;
        else if (hotLaunch) verticalVelocity = hot != null ? hot.launchSpeed : hotJumpSpeed;
        else ApplyVerticalVelocity();

        // 熱い床ジャンプの滞空中は落下速度を制限する (8.5 のままだと流体が一撃 40% 吹き出す)。
        // 追補 19: -5 → -6.5。パリー導入後は「生着地は痛い / パリーで守る」が狙いなので
        // 少し痛くした。ふわっと感は残る。
        if (hotFlightActive)
        {
            if (controller.isGrounded && !hotLaunch) hotFlightActive = false;
            else verticalVelocity = Mathf.Max(verticalVelocity, -6.5f);
        }
        // 追補 25: パリー押下中はさらに柔らかく落ちる (膝で受ける)
        if (Time.time < softLandUntil && !controller.isGrounded)
            verticalVelocity = Mathf.Max(verticalVelocity, -parrySoftFallSpeed);

        // 2026-08-25 (報告「静態パリー後に歩き出しまでに遅延がある」): ジャンプの
        // アニメーション状態は **着地したあとも ~0.8 秒続く**。その間ずっと横移動が
        // 離陸時の速度に固定されるので、静止ジャンプ (離陸速度 0) では着地後 0.8 秒
        // 一切歩き出せなかった (実測: 立ちジャンプ着地の 0.62〜1.44 秒が速度ちょうど 0。
        // パリーの有無に関係なく出る)。離陸速度を保つ意味があるのは滞空中だけなので、
        // 一度足が地面を離れて、また接地したらロックを解く。
        // 接地している間はロックしない。実測では、静止ジャンプの Animator の
        // ジャンプ状態が **着地の 0.5 秒後に始まり 0.85 秒続く** (静止中は
        // animator.speed = 0 で凍っているため、歩き出して初めて遷移が走る)。
        // その間ずっと横移動が離陸速度 = 0 に固定され、「パリーのあと歩き出せない」の
        // 正体になっていた。離陸速度を保つ意味があるのは滞空中だけ。
        bool jumpLockActive = inJumpState && !controller.isGrounded;
        InJumpState = inJumpState; JumpLockActive = jumpLockActive;

        Vector3 horizontalMove = Vector3.zero;
        if (jumpLockActive)
        {
            // Keep the takeoff direction/speed locked for the whole jump so late Space taps
            // (or releasing the arrow key mid-air) can't alter it.
            horizontalMove = transform.forward * jumpHorizontalSpeed;
            // 着地後の減速ランプが離陸速度から始まるように起点を合わせる
            smoothedSignedSpeed = jumpHorizontalSpeed;
            RampingHard = false;
        }
        else
        {
            float targetSigned = 0f;
            if (IsMoving)
            {
                float speed = movingForward ? (IsRunning ? runSpeed : walkSpeed) : walkSpeed * backStepSpeedMultiplier;
                // 細い足場の上は慎重に (NarrowBeamSurface.speedMultiplier)
                if (beamSensor != null && beamSensor.OnBeam) speed *= beamSensor.SpeedMultiplier;
                targetSigned = Mathf.Sign(moveZ) * speed;
            }
            // 追補 13: 運搬中は加減速をなだらかに。水中は浮力・流れ側の挙動を優先して従来通り。
            if (gentleAccel && carryAccel > 0f && !inWaterNow)
            {
                float rate = Mathf.Abs(targetSigned) > Mathf.Abs(smoothedSignedSpeed) ? carryAccel : carryDecel;
                smoothedSignedSpeed = Mathf.MoveTowards(smoothedSignedSpeed, targetSigned, rate * Time.deltaTime);
                // 追補 22: 大きな加減速の最中 (差 0.25 m/s 超) は GoblinPotActions が壺内 calm を当てる
                RampingHard = Mathf.Abs(targetSigned - smoothedSignedSpeed) > 0.25f;
            }
            else
            {
                smoothedSignedSpeed = targetSigned;
                RampingHard = false;
            }
            horizontalMove = transform.forward * smoothedSignedSpeed;
        }

        CurrentSpeed = horizontalMove.magnitude;

        // 水中 (2026-08-16 川ギミック): 入力は泳ぎ速度へ減速し、川の流れを加算。
        // 浮力はバネ + 減衰で浮き目標 (水面 - 浸かり深さ + ぷかぷか) へ吸い付く。
        // 重力も打ち消す (打ち消さないと平衡点が目標より下にずれる)。
        // ジャンプ直後 (上昇 3 m/s 超) は浮力を切って飛び出しを妨げない。
        if (inWaterNow)
        {
            horizontalMove = horizontalMove * swimmer.swimSpeedMultiplier + swimmer.Flow;
            CurrentSpeed = horizontalMove.magnitude;
            // 追補 25: 水底に足が着くと、接地リセット (verticalVelocity = -1) が浮力バネに
            // 勝ち続けて底から浮上できなくなる (水深 1.2 化で顕在化)。水中の接地では
            // リセットを打ち消してバネに任せる。
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = 0f;
            if (verticalVelocity < 3f)
            {
                // 明示オイラー積分は dt が跳ねると発散する (実測: エディタのヒッチで
                // 5m 打ち上げられた)。dt をクランプし、臨界減衰 (c = 2√k)、
                // 目標差と速度もクランプして絶対に暴れないようにする。
                float dtc = Mathf.Min(Time.deltaTime, 0.05f);
                float dy = Mathf.Clamp(swimmer.FloatTargetY - transform.position.y, -0.5f, 0.5f);
                verticalVelocity += ((dy * 25f - verticalVelocity * 10f) - gravity) * dtc;
                verticalVelocity = Mathf.Clamp(verticalVelocity, -4f, 3f);
            }
        }

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

    HotFloorSurface HotFloorUnderfoot()
    {
        var hits = Physics.RaycastAll(transform.position + Vector3.up * 0.2f, Vector3.down, 0.6f,
                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        HotFloorSurface found = null;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < best)
            {
                best = h.distance;
                found = h.collider.GetComponent<HotFloorSurface>();
            }
        }
        return found;
    }

    void ApplyGravityOnly()
    {
        // 拾い上げの位置合わせ中などは CharacterController が一時的に無効になっている。
        // 無効な controller に Move するとエラーログが出るだけなのでスキップする。
        if (controller == null || !controller.enabled) return;
        ApplyVerticalVelocity();
        controller.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
    }

    // Stage-gimmick hook (used by RiverFlowController): teleport the character
    // controller to a world position -- e.g. a recovery point or checkpoint --
    // without it fighting the move via collision resolution.
    public void SnapTo(Vector3 worldPosition, bool resetVerticalVelocity = true)
    {
        controller.enabled = false;
        transform.position = worldPosition;
        controller.enabled = true;
        if (resetVerticalVelocity) verticalVelocity = -1f;
    }
}
