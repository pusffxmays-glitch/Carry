using UnityEngine;
using UnityEngine.InputSystem;

// REWRITE (2026-08-10): replaces ArmTwoBoneIK.cs + PotAttach.cs. The approved
// "Carry_Balance_Neutral" pose was being played back as a separately-exported Animator clip
// (Grimfang_Goblin_CarryBalanceNeutral.fbx), but that clip's curve paths did not match the main
// character's actual hierarchy paths, so the Animator silently failed to apply it and the rig sat
// at its raw FBX bind pose instead (visible as a Y-pose). Any script capturing "neutral" from the
// live bones (the old ArmTwoBoneIK) just faithfully reproduced that wrong bind pose.
//
// This version stops depending on the Animator clip / FBX hierarchy matching entirely. The full
// body pose (all 24 posed bones) is captured directly in Blender as each bone's WORLD position
// and local X/Y axis directions (Blender Z-up,-Y-fwd -> Unity Y-up,+Z-fwd converted, the same
// per-axis conversion already validated for the arm reach/pole/fingertip directions and the pot
// offset), then applied here every LateUpdate via name lookup (GoblinBoneUtil.FindDeep) --
// independent of whatever hierarchy path the FBX importer produced. The arm bones are included in
// this base pose too, then the two-bone IK articulation overrides just the arms on top of it for
// the Q/E balance control, and the pot is placed relative to the (now-correct) Head bone -- all
// three steps run in a fixed order inside one LateUpdate so there is no cross-script
// execution-order risk.
//
// Rig is Generic (not Humanoid) -- see WORKLOG.md -- so this manipulates bone Transforms
// directly rather than using Unity's built-in Avatar IK.
public class GoblinCarryRig : MonoBehaviour
{
    // REDESIGNED 2026-08-10 per explicit request: simplified from independent Q/A/E/D per-arm
    // control to a single see-saw balance -- Q raises the left arm (and lowers the right by the
    // same amount), E raises the right arm (and lowers the left). armBalance=0 is Blender's
    // "Carry_Balance_Neutral" pose exactly (both arms at their captured neutral); +1 = left fully
    // up/right fully down; -1 = right fully up/left fully down.
    // 2026-08-14 ユーザー指定でキー構成を変更。壺のバランスは **矢印キー** で 2 軸操作する。
    //   左右キー = 左右のバランス（腕の高さ差。armBalance）   … 旧 Q/E
    //   上下キー = 前後のバランス（前傾・後傾。pitchBalance） … 新規
    // 移動は WASD へ移した (GoblinLocomotion)。
    [Header("Pot balance input: 矢印キー（左右=左右バランス / 上下=前後バランス）")]
    // Raised 2026-08-12 per request ("Q/Eキー入力時の変化量をもう少し大きく"), twice: 1.2->2.2
    // was still judged too slow, raised further to 4.0 (full -1..1 range in ~0.5s).
    //
    // 2026-08-15: 最大傾きを増やした際 (heightRange/pitchRangeDeg の注記)、キー保持中の
    // 傾き速度 [deg/s] = 入力速度 x 最大傾き なので、範囲を広げると速度まで増えてしまう。
    // ユーザー指定は「最大傾きだけ変えて、一回のキーでの変化量は据え置き」なので、
    // 入力速度を範囲の増加分だけ下げて相殺した。旧実測 43.6 deg/s (4.0 x 10.9 度) に対し
    // 2.4 x 17.8 度 = 42.7 deg/s。前後は範囲が別なので pitchInputSpeed に分離した。
    public float armInputSpeed = 2.4f;
    [Tooltip("上下キー (前後バランス) の入力速度。旧実測 64.4 deg/s (4.0 x 16.1 度) に合わせて 3.6 x 18 度 = 64.8 deg/s。")]
    public float pitchInputSpeed = 3.6f;

    // 2026-08-21: マウスによる連続バランス操作 (要望「マウスでバランスを取りながら WASD で
    // 進む」)。マウスの移動量が armBalance / pitchBalance に積算される (矢印キーも併用可)。
    // 適用側のスルーレート制限 (balanceApplySpeed) はそのままなので、入力がいくら速くても
    // 壺の実回転速度の上限は変わらない (= こぼれ特性・ゲームバランスは不変)。
    [Header("Mouse balance (マウスでの連続バランス操作)")]
    [Tooltip("マウスでのバランス操作を有効にする。右へ動かす = 右へ傾く、奥 (上) へ = 前傾。")]
    public bool mouseBalance = true;
    [Tooltip("感度 (バランス値 / ピクセル)。0.006 なら 170px の移動でフルチルト。")]
    public float mouseSensitivity = 0.006f;   // 2026-08-22 ユーザー要望で 0.003 -> 0.006
    [Tooltip("マウス操作中はカーソルをロックする (画面端で移動量が死なないように)。Esc で解除、左クリックで再ロック。")]
    public bool lockCursorWhileCarrying = true;
    // 2026-08-22 マウス入力の品質対策 (バグ報告「動かしてない方に動く/かくっと動く」):
    //  * ロック切替の瞬間は OS のカーソル再センタリングで巨大なデルタが 1 フレーム乗る → 数フレーム捨てる
    //  * ロックが外れている間 (エディタ UI 操作中など) のカーソル移動は壺に流さない
    //  * デルタに速度上限 (px/s) と軽い平滑化を掛けてスパイクを丸める
    [Tooltip("マウス入力の最大速度 (px/秒)。これを超える瞬間デルタはスパイクとして頭打ちにする。")]
    public float mouseMaxSpeed = 4000f;
    [Tooltip("マウス入力の平滑化の速さ。大きいほど生に近い。20-30 で「かくつき」だけ消える。")]
    public float mouseSmoothing = 25f;
    [Tooltip("左右軸を反転する。")]
    public bool invertMouseX = false;
    [Tooltip("前後軸を反転する。")]
    public bool invertMouseY = false;
    // 2026-08-22: 既定を**絶対位置モード**に変更。相対デルタ方式はエディタのカーソルロックが
    // 毎フレームの強制リセンタリングをデルタに混ぜることがあり、「動かしてない方に動く/
    // かくっと動く」ジャンクの原因になっていた。絶対位置ならワープもロックも不要で、
    // カーソル位置 = バランス値が常に 1:1 対応する (ジョイスティック式)。
    [Tooltip("絶対位置モード: 画面中心からのカーソル位置がそのままバランス値になる。OFF で従来の移動量積算式。")]
    public bool mouseAbsolute = true;
    [Tooltip("絶対位置モードで最大チルトになる画面中心からの距離 (px)。小さいほど敏感。")]
    public float mouseAbsoluteRangePx = 220f;
    Vector2 smoothedMouseDelta;
    bool prevCursorLocked;
    int mouseSuppressFrames;
    bool cursorCentredOnce;   // 絶対位置モードの開始時センタリング (2026-08-22)


    [Tooltip("左右のバランス。右キーで右へ、左キーで左へ傾く。")]
    [Range(-1f, 1f)] public float armBalance = 0f;
    [Tooltip("前後のバランス。上キーで前傾、下キーで後傾。")]
    [Range(-1f, 1f)] public float pitchBalance = 0f;
    // 2026-08-15 ユーザー要望「上下左右キーの許容移動量をもう少し増やしたら」で増量。
    // 実測 (平地・キー最大): 旧 前後16.1度/左右10.9度 → 新 両軸とも約18度で対称に揃えた。
    [Tooltip("pitchBalance = 1 のときの前後の傾き角 (度)。左右バランスの効き (heightRange 0.20 で実測 17.8 度) と同程度にしてある。")]
    public float pitchRangeDeg = 18f;
    [Tooltip("前後バランスに合わせて両手を前後へ動かす量 (m)。壺だけが傾いて手が置き去りに見えるのを防ぐ。")]
    public float pitchHandReach = 0.07f;
    // 追補 18: 入力値 (armBalance/pitchBalance = プレイヤーの意図、UI のドットもこれ) と
    // 「実際に壺へ適用される傾き」を分離する。従来は入力速度 (2.4-3.6/s ≈ 40-65°/s の
    // 壺回転) がそのまま適用され、満杯時は**バランス操作をすること自体**がこぼれを生んで
    // いた (実測: 坂の対抗チルトで 85%→54-55%、タップでも 40%)。適用側をスルーレート
    // 制限することで「ゆっくり効く重い操作」になり、対抗チルトが成立する。
    // 追補 23: 0.8 は「動き始めが遅い」(ユーザー) ため 1.8 (約 32°/s) へ。代償の
    // スロッシュは操作中の自動 calm (GoblinPotActions が BalanceMoving を見る) で吸収。
    // 追補 37 (2026-08-22 バグ報告「マウスに対するツボの応答が遅い」): 1.8 (32°/s) では
    // マウスを動かしてから壺が追いつくまで最大 0.55 秒かかり、操作が効いていないように
    // 感じる。実際に人が抱えた壺を傾けられる速さ (100°/s 前後) まで上げる。
    // 「操作自体でこぼれる」対策はスルーレートではなく calm 側 (balanceCalmClamp) の担当で、
    // そちらは速く振ったときだけ外れる (balanceInertiaRate) ので慣性も残る。
    [Tooltip("バランスの適用速度 (units/s)。壺の回転速度 ≒ これ × 18°。6.0 で約 108°/s。")]
    public float balanceApplySpeed = 6f;
    float appliedArmBalance, appliedPitchBalance;
    /// <summary>適用中のバランスが動いているか (追補 23: 操作中 calm のトリガー)。</summary>
    public bool BalanceMoving { get; private set; }
    // REDESIGNED 2026-08-10 per explicit request: the palm's front-back/left-right position must
    // stay fixed while only its HEIGHT changes, achieved through natural shoulder
    // abduction/adduction ("armpit opening/closing") plus elbow extension/flexion -- i.e. real
    // 2-bone IK toward a target that only moves vertically, not the previous "tilt the whole reach
    // direction" hack (which dragged the wrist forward/back and side to side too).
    [Tooltip("How far up/down (meters) the palm target moves at armValue=1/0, holding its X/Z (left-right/front-back) fixed.")]
    // 0.15 (壺の傾き実測 10.9 度) -> 0.20 (17.8 度)。2026-08-15 の増量 (pitchRangeDeg の注記を参照)。
    public float heightRange = 0.20f;

    [Header("Palm-normal (NEEDS VISUAL CHECK IN PLAY MODE -- see WORKLOG.md)")]
    public float leftPalmSign = -1f;
    public float rightPalmSign = 1f;

    // ADDED 2026-08-10 per explicit request: when the pot tilts (armBalance magnitude) past a
    // threshold, the character staggers toward the side the pot is spilling on. Data is baked
    // per-frame from the Blender "Carry_Balance_Stagger_Right/Left" actions (see GoblinStagger.cs)
    // and blended on top of ApplyBasePose()'s Hips/leg bones, the same way SolveArm() blends the
    // arms on top -- Spine/neck/Head/arms are left alone since the source animation holds them at
    // the neutral pose throughout.
    // よろけの判定は **世界基準で壺がどれだけ傾いているか（度）** で行う。
    //
    // 以前は |armBalance|、つまり「ゴブリンに対して壺がどれだけ傾いているか」で判定して
    // いた。これは平地でしか正しくない。斜面では体ごと傾くので、
    //   * armBalance = 0（腕は左右対称）でも、壺は世界基準で斜面ぶん傾いている
    //     → こぼれる姿勢なのに、よろけない
    //   * 斜面で Q/E を使って壺を水平に保つと armBalance は大きくなる
    //     → 正しくバランスを取っているのに、よろける
    // という逆の挙動になっていた。
    //
    // 平地での効き方は据え置き（armBalance 0.6 のとき実測 5.5 度、0.9 で 16 度）。
    [Header("Stagger (壺が世界基準でどれだけ傾いたかで判定)")]
    [Tooltip("世界基準での壺の傾きがこの角度(度)を超えるとよろけ始める。")]
    public float staggerThresholdDeg = 5.5f;
    [Tooltip("しきい値からこの角度(度)ぶん超えると、よろけが最大になる。")]
    public float staggerRampDeg = 10.5f;
    // 人は横方向より前後方向にずっと安定している（足が左右に並んでいるので、支持面は
    // 左右に狭く前後に長い）。実際、上り坂を登ってもよろけないが、横に傾いた斜面では
    // すぐバランスを崩す。よろけの判定でも前後成分の重みを下げる。
    // 既定 0.35 なら 15 度の上り坂は 5.25 度相当となり、しきい値 5.5 度に届かない。
    [Tooltip("前後方向の傾きをよろけ判定に算入する重み。1 で左右と同等、0 で前後を完全に無視。")]
    [Range(0f, 1f)] public float staggerPitchWeight = 0.35f;
    [Tooltip("Seconds per full stagger cycle (source Blender animation is 60 frames @ 24fps = 2.5s).")]
    public float staggerCycleDuration = 2.5f;
    [Tooltip("How fast the stagger blends in/out as armBalance crosses the threshold.")]
    public float staggerBlendSpeed = 3f;
    [Tooltip("Diagonal movement speed (m/s) at full stagger intensity -- character advances in the direction it's staggering, per spec (\"よろけながら斜めに進んでいく\").")]
    public float staggerMoveSpeed = 1.0f;
    [Tooltip("Flip if the stagger visually moves the wrong way relative to which side it leans -- the lean-vs-movement sign was wrong on the first playtest, so this exists as a quick Inspector fix if it's ever wrong again without another code round-trip.")]
    public bool invertStaggerMoveSide = true;

    // ADDED 2026-08-10 per explicit request: plays the Carry_Balance_Walk gait (legs + Hips sway,
    // baked the same way as the stagger -- see GoblinWalk.cs) while GoblinLocomotion reports
    // IsMoving, blended in/out so starting/stopping doesn't pop. Runs BEFORE ApplyStagger() in
    // LateUpdate so a stagger (which reads as more urgent) still wins if both are active at once.
    [Header("Walk cycle (assigns Carry_Balance_Walk to movement)")]
    [Tooltip("Seconds per full walk cycle at walkStrideRefSpeed (source Blender animation is 60 frames @ 24fps = 2.5s); scales automatically with actual speed.")]
    public float walkCycleDuration = 2.5f;
    // ADDED 2026-08-15 (要望「歩行スピードを速くしたい。ただし歩行アニメと移動量が
    // 乖離しないように」): 従来は位相速度の基準に locomotion.walkSpeed そのものを
    // 使っていた。この方式は「1 サイクルで進む距離 = walkSpeed x walkCycleDuration」に
    // なるため、walkSpeed を上げると歩幅の定義まで一緒に伸びて足滑りが出る。
    // 基準をこの定数に分離すると、歩幅 = walkStrideRefSpeed x walkCycleDuration
    // (1.0 x 2.5 = 2.5m/サイクル) が walkSpeed と無関係に固定され、移動速度を
    // 変えても「速く歩く = 足も比例して速く回る」が常に成り立つ。
    // 歩行アニメの見え方を調整した当時の速度が 1.0 m/s だったので既定は 1.0。
    [Tooltip("歩行アニメの周期 (walkCycleDuration) を調整した基準速度 (m/s)。歩幅 = これ x walkCycleDuration。locomotion.walkSpeed を変えてもここは変えないこと。")]
    public float walkStrideRefSpeed = 1.0f;
    [Tooltip("How fast the walk cycle blends in/out as movement starts/stops.")]
    public float walkBlendSpeed = 4f;
    [Tooltip("Extra vertical bob (meters) added to both arm IK targets while walking, so the carried pot visibly sways with each step.")]
    public float walkArmBobAmplitude = 0.02f;

    Transform hipsBone, leftUpLegBone, leftLegBone, leftFootBone, leftToeBone;
    Transform rightUpLegBone, rightLegBone, rightFootBone, rightToeBone;
    float leftUpLegLen, leftLegLen, leftFootLen;
    float rightUpLegLen, rightLegLen, rightFootLen;
    CharacterController controller;
    GoblinLocomotion locomotion;
    // 2026-08-15 追加: ベイク済み全身クリップ (ツボおろし/転倒/壺なしロコモーション) の再生機と、
    // 細い足場センサー (綱渡り歩容への切り替え)。どちらも無ければ従来どおり。
    GoblinClipAnimator clipAnimator;
    NarrowBeamSensor beamSensor;
    GoblinSwimmer swimmer;         // 水中はバタ足歩容 (GoblinSwimGait) に切り替え
    bool swimGaitActive;           // ClampFeetToGround を止めるためのフラグ (足が水中に潜るため)
    /// <summary>よろけ強度 (0..1)。転倒トリガー (GoblinPotActions) が読む。</summary>
    public float StaggerIntensity01 => staggerIntensity;
    /// <summary>いまのよろけが右側 (root.right = +X) か。転倒の向き (ミラー再生) の判定に使う。</summary>
    public bool StaggerLeanRightNow => staggerLeanRight;
    float staggerPhase, staggerIntensity;
    bool staggerLeanRight;
    float walkPhase, walkIntensity;
    bool cursorLockedOnce;   // マウスバランス用の初回カーソルロック (2026-08-21)

    // REDESIGNED 2026-08-10 per explicit request: the pot is no longer placed at a fixed
    // Head-relative offset. Its bottom face (see potBottomOffsetLocal below) is now anchored at
    // the midpoint between the two palms, and its orientation tilts to follow the hands, so
    // raising/lowering one arm visibly tips the pot -- both requested directly.
    [Header("Pot placement (palm-relative)")]
    // Carry_Pot's object origin sits almost exactly at its own bottom face (Blender local Z:
    // 0.0014 to 0.720, confirmed live) -- effectively zero offset, so the pot's Transform.position
    // can be treated as its bottom-center directly, with no separate bottom-offset math needed.
    // 3.38 (measured 2026-08-10: committed Carry_Pot.fbx is a stale mesh exactly 2.6x undersized
    // vs. the live Blender pot, times Blender's own 1.3 object scale) was reported as too big;
    // scaled down another 0.7x per direct feedback.
    public Vector3 potScale = new Vector3(2.366f, 2.366f, 2.366f);

    // ---- 追補 30: 壺追従の低域通過 (root 相対)。詳細は配置コードのコメント参照。
    [Tooltip("壺の位置追従レート (1/s)。小さいほど滑らか。歩容の高周波揺すりを消すのが目的。")]
    public float potFollowRate = 15f;
    // 追補 37: 25 だと 40ms の遅れが乗り、マウス応答の鈍さに上乗せされていた。
    // 暴れていたのは位置 (歩容ボブ) だけで回転は実測 0.08 rad/s と静かなので、
    // 回転はほぼ素通し (60 = 17ms) にしてよい。
    [Tooltip("壺の回転追従レート (1/s)。マウスバランスの応答を保つため位置より速め。")]
    public float potFollowRotRate = 60f;
    Vector3 smoothedPotLocal;
    Quaternion smoothedPotLocalRot = Quaternion.identity;
    bool potFollowInit;

    // ---- Base pose data: captured 2026-08-10 directly from the live, approved
    // "Carry_Balance_Neutral" pose in Blender (armature.matrix_world @ pose_bone.matrix, per-bone
    // world position and local +Y "toward child" axis direction, Blender->Unity axis converted).
    // FIXED 2026-08-12 (bug reports: legs twisted / sunk to the shin at rest and worse during
    // walk/stagger, then a follow-up report that the torso looked twisted all the way around at
    // the waist): local +X (roll reference) is now captured for every bone EXCEPT arms and applied
    // in ApplyBasePose -- previously only Y was ever captured/corrected for any bone. Arms are the
    // one deliberate exception: SolveArm fully re-derives arm rotation every frame from IK,
    // bypassing bind-pose roll entirely, so they don't need (and don't have) an xDir entry here.
    // Every other bone's roll was, until now, whatever the FBX bind pose happened to have,
    // uncorrected forever (AimLocalY only ever touches Y) -- for Hips/legs that additionally drifted
    // frame-to-frame from leftover walk/stagger blending, but even for the never-touched spine/neck/
    // head bones it was simply the wrong (arbitrary bind-pose) roll rather than the real captured
    // neutral-pose roll, which is exactly what produced a visible seam where a corrected bone (e.g.
    // Hips) met an uncorrected one (e.g. Spine02) right at the waist. ----
    struct BonePose
    {
        public string name;
        public Vector3 pos, yDir, xDir; // xDir left Vector3.zero only for arms (SolveArm re-derives their rotation from IK every frame -- see ApplyBasePose)
        public BonePose(string n, Vector3 p, Vector3 y) { name = n; pos = p; yDir = y; xDir = Vector3.zero; }
        public BonePose(string n, Vector3 p, Vector3 y, Vector3 x) { name = n; pos = p; yDir = y; xDir = x; }
    }

    static readonly BonePose[] BasePose =
    {
        new BonePose("Hips",          new Vector3(0.000000f, 0.494531f, 0.000000f), new Vector3(-0.040698f, 0.956316f, -0.289488f), new Vector3(0.998716f, 0.030183f, -0.040698f)),
        // FIXED 2026-08-12 (bug report: "waist looks twisted all the way around"): spine/neck/head
        // never had roll data either (same gap legs/hips had -- see above), so their roll was
        // whatever the FBX bind pose happened to have, uncorrected, since Awake() first ran. This
        // is a fixed value (nothing else ever rotates these bones -- ApplyWalkCycle/ApplyStagger
        // only touch Hips+legs, SolveArm only touches arms), so it doesn't drift frame-to-frame
        // like the legs' problem did, but it's still whatever arbitrary roll the bind pose had
        // rather than the real captured neutral-pose roll -- exactly the kind of mismatch that
        // produces a visible twist where Spine02 (whose roll was never corrected) meets Hips
        // (whose roll now IS corrected). Re-extracted from Blender the same way.
        new BonePose("Spine02",       new Vector3(-0.001218f, 0.598925f, -0.008432f), new Vector3(-0.014780f, 0.977374f, 0.211003f), new Vector3(0.999853f, 0.012609f, 0.011632f)),
        new BonePose("Spine01",       new Vector3(-0.002766f, 0.701297f, 0.013668f), new Vector3(-0.016481f, 0.894158f, 0.447447f), new Vector3(0.999853f, 0.012609f, 0.011632f)),
        new BonePose("Spine",         new Vector3(-0.004493f, 0.794953f, 0.060535f), new Vector3(-0.019163f, 0.845625f, 0.533433f), new Vector3(0.999810f, 0.014222f, 0.013372f)),
        new BonePose("neck",          new Vector3(-0.006668f, 0.846868f, 0.101973f), new Vector3(-0.032733f, 0.781144f, 0.623492f), new Vector3(0.999310f, 0.014631f, 0.034133f)),
        new BonePose("Head",          new Vector3(-0.008902f, 0.900179f, 0.144524f), new Vector3(-0.008529f, -0.040670f, 0.999136f), new Vector3(0.999945f, 0.005674f, 0.008767f)),
        new BonePose("headfront",     new Vector3(-0.005917f, 0.733702f, 0.321854f), new Vector3(0.012272f, -0.684395f, 0.729008f), new Vector3(0.999914f, 0.011776f, -0.005777f)),
        new BonePose("head_end",      new Vector3(-0.016177f, 1.046204f, 0.469641f), new Vector3(-0.020406f, 0.409632f, 0.912022f), new Vector3(0.999792f, 0.007638f, 0.018939f)),
        // Name swapped left<->right on these 8 arm pairs (2026-08-10, confirmed correct via
        // playtest: the Unity bone literally named "LeftArm" is this character's visual RIGHT
        // arm) AND on these 8 leg pairs (2026-08-12, confirmed correct via direct user observation
        // after rotating the camera to view the goblin from the front: the feet were visibly on the
        // wrong sides). The leg swap was removed earlier today after finding a position/rotation
        // AUTHORITY mismatch bug (ApplyBasePose placed each leg bone by its own exact/unswapped
        // name while ApplyWalkCycle/ApplyStagger rotated it using the opposite leg's data via a
        // swapped Awake() bone reference) -- that removal fixed the internal-consistency bug (legs
        // twisting during walk/stagger) but, on its own, left the SAME left/right identity mismatch
        // the arms have, just no longer self-contradicting. The real fix needed BOTH halves done
        // together, same as arms: the NAME here determines which Unity bone ApplyBasePose positions
        // with this row's data, and Awake()'s leftUpLegBone/etc. must look up that SAME (swapped)
        // Unity name -- so position and rotation authority land on one consistent, AND physically
        // correct, bone. The DATA on each leg row is still exactly what Blender measured for that
        // Left/Right bone; only the Unity bone NAME it gets applied to is swapped.
        new BonePose("RightShoulder", new Vector3(0.030397f, 0.832256f, 0.080034f), new Vector3(0.995995f, 0.064481f, -0.061937f)),
        new BonePose("RightArm",      new Vector3(0.171104f, 0.841365f, 0.071284f), new Vector3(0.696845f, 0.089225f, 0.711650f)),
        new BonePose("RightForeArm",  new Vector3(0.361686f, 0.865767f, 0.265914f), new Vector3(-0.280686f, 0.923010f, -0.263186f)),
        new BonePose("RightHand",     new Vector3(0.278283f, 1.140028f, 0.187712f), new Vector3(-0.599999f, -0.000001f, -0.800001f)),
        new BonePose("LeftShoulder",  new Vector3(-0.040004f, 0.829173f, 0.077464f), new Vector3(-0.997588f, 0.044038f, -0.053654f)),
        new BonePose("LeftArm",       new Vector3(-0.180946f, 0.835394f, 0.069884f), new Vector3(-0.634239f, 0.137067f, 0.760889f)),
        new BonePose("LeftForeArm",   new Vector3(-0.350536f, 0.872045f, 0.273339f), new Vector3(0.257028f, 0.916655f, -0.306072f)),
        new BonePose("LeftHand",      new Vector3(-0.275399f, 1.140012f, 0.183865f), new Vector3(0.600000f, -0.000000f, -0.800000f)),
        new BonePose("RightUpLeg",    new Vector3(0.115463f, 0.406070f, 0.014226f), new Vector3(0.447992f, -0.737600f, 0.505223f), new Vector3(0.893345f, 0.391543f, -0.220519f)),
        new BonePose("RightLeg",      new Vector3(0.277781f, 0.138820f, 0.197280f), new Vector3(-0.055805f, -0.739027f, -0.671360f), new Vector3(0.969108f, 0.121696f, -0.214521f)),
        new BonePose("RightFoot",     new Vector3(0.258402f, -0.117820f, -0.035862f), new Vector3(0.096722f, -0.539439f, 0.836451f), new Vector3(0.994906f, 0.028408f, -0.096726f)),
        new BonePose("RightToeBase",  new Vector3(0.279049f, -0.232974f, 0.142695f), new Vector3(0.114869f, 0.000000f, 0.993381f), new Vector3(0.993380f, 0.000000f, -0.114872f)),
        new BonePose("LeftUpLeg",     new Vector3(-0.111189f, 0.406807f, 0.015939f), new Vector3(-0.462280f, -0.729747f, 0.503753f), new Vector3(0.885873f, -0.405106f, 0.226095f)),
        new BonePose("LeftLeg",       new Vector3(-0.277924f, 0.143603f, 0.197631f), new Vector3(0.045060f, -0.730926f, -0.680967f), new Vector3(0.954688f, -0.169225f, 0.244812f)),
        new BonePose("LeftFoot",      new Vector3(-0.262031f, -0.114191f, -0.042542f), new Vector3(-0.108725f, -0.541682f, 0.833523f), new Vector3(0.993553f, -0.032123f, 0.108723f)),
        new BonePose("LeftToeBase",   new Vector3(-0.286047f, -0.233840f, 0.141571f), new Vector3(-0.129344f, 0.000000f, 0.991600f), new Vector3(0.991600f, 0.000000f, 0.129343f)),
    };

    // 壺の姿勢を作るときの「体の向き」。GoblinTerrainTilt が地形傾斜用の子を作ったら
    // それが入る。未設定なら root（＝yaw だけの姿勢）に戻るので、傾き機能を使わない
    // 構成でも従来どおり動く。
    [HideInInspector] public Transform postureRoot;
    Transform Posture { get { return postureRoot != null ? postureRoot : root; } }

    Transform root;
    Transform[] baseBones; // parallel to BasePose; null entries mean "not found, skip"
    Transform leftUpperArm, leftForeArm, leftHand;
    Transform rightUpperArm, rightForeArm, rightHand;
    Transform head, pot;
    float leftUpperLen, leftForeLen, rightUpperLen, rightForeLen;
    bool bonesFound;
    bool neutralCaptured;

    struct ArmNeutral
    {
        public Vector3 wristOffsetLocal; // shoulder -> wrist, root-local, full vector (not a unit direction)
        public Vector3 poleDirLocal, fingertipDirLocal;
    }
    ArmNeutral leftNeutral, rightNeutral;

    void Awake()
    {
        root = transform;

        baseBones = new Transform[BasePose.Length];
        for (int i = 0; i < BasePose.Length; i++)
            baseBones[i] = GoblinBoneUtil.FindDeep(root, BasePose[i].name);

        // SWAPPED (2026-08-10, testing user's "left/right arms are reversed" report): the Unity
        // bone literally named "LeftArm" is being treated here as this character's actual RIGHT
        // arm, and vice versa. Verified this is not a transcription bug on my end -- the BasePose
        // data below was checked value-by-value against the original Blender extraction and is
        // exactly right. If a mismatch exists, it must be in the imported rig itself (the FBX's
        // bone names not matching the character's actual left/right, which does happen with some
        // auto-rigged assets). If this makes things WORSE, revert this swap immediately -- it
        // means the naming was fine and the real cause is still unidentified.
        leftUpperArm = GoblinBoneUtil.FindDeep(root, "RightArm");
        leftForeArm = GoblinBoneUtil.FindDeep(root, "RightForeArm");
        leftHand = GoblinBoneUtil.FindDeep(root, "RightHand");
        rightUpperArm = GoblinBoneUtil.FindDeep(root, "LeftArm");
        rightForeArm = GoblinBoneUtil.FindDeep(root, "LeftForeArm");
        rightHand = GoblinBoneUtil.FindDeep(root, "LeftHand");
        head = GoblinBoneUtil.FindDeep(root, "Head");
        pot = root.Find("Carry_Pot");

        // RE-SWAPPED 2026-08-12 (see BasePose comment above): leftUpLegBone etc. look up the
        // OPPOSITE Unity bone name, matching the BasePose table above so position (ApplyBasePose)
        // and rotation (ApplyWalkCycle/ApplyStagger via these same variables) both land on the same
        // physically-correct bone -- same pattern as the arms just above.
        hipsBone = GoblinBoneUtil.FindDeep(root, "Hips");
        leftUpLegBone = GoblinBoneUtil.FindDeep(root, "RightUpLeg");
        leftLegBone = GoblinBoneUtil.FindDeep(root, "RightLeg");
        leftFootBone = GoblinBoneUtil.FindDeep(root, "RightFoot");
        leftToeBone = GoblinBoneUtil.FindDeep(root, "RightToeBase");
        rightUpLegBone = GoblinBoneUtil.FindDeep(root, "LeftUpLeg");
        rightLegBone = GoblinBoneUtil.FindDeep(root, "LeftLeg");
        rightFootBone = GoblinBoneUtil.FindDeep(root, "LeftFoot");
        rightToeBone = GoblinBoneUtil.FindDeep(root, "LeftToeBase");
        controller = GetComponent<CharacterController>();
        locomotion = GetComponent<GoblinLocomotion>();
        clipAnimator = GetComponent<GoblinClipAnimator>();
        beamSensor = GetComponent<NarrowBeamSensor>();
        swimmer = GetComponent<GoblinSwimmer>();

        bonesFound = leftUpperArm && leftForeArm && leftHand && rightUpperArm && rightForeArm && rightHand;
        if (!bonesFound)
            Debug.LogError("GoblinCarryRig: could not find one or more arm bones under " + root.name + ".");
        if (head == null || pot == null)
            Debug.LogError("GoblinCarryRig: could not find Head bone and/or Carry_Pot child under " + root.name + ".");

        // Bone lengths are measured from the captured BasePose data (trusted, Blender-authored),
        // NOT from the live Transform.position here in Awake(): at this point the Animator has
        // never evaluated any clip yet, so the bones are still at the FBX bind pose, whose
        // proportions are not guaranteed to match the approved pose (this mismatch was the actual
        // cause of the arms rendering fully stretched out).
        //
        // Uses the SAME (swapped) BasePose name each leftUpperArm/etc. Transform is actually
        // looked up under just above, so the length matches the data ApplyBasePose will place on
        // that exact bone -- NOT the data literally labelled "left" (see the swap comment above).
        leftUpperLen = Vector3.Distance(PosOf("RightArm"), PosOf("RightForeArm"));
        leftForeLen = Vector3.Distance(PosOf("RightForeArm"), PosOf("RightHand"));
        rightUpperLen = Vector3.Distance(PosOf("LeftArm"), PosOf("LeftForeArm"));
        rightForeLen = Vector3.Distance(PosOf("LeftForeArm"), PosOf("LeftHand"));

        // Leg segment lengths, used by ApplyStagger() to reattach Leg/Foot/ToeBase via forward
        // kinematics after UpLeg/Leg get re-aimed for the stagger -- same rationale as the arm
        // lengths above (measured from the trusted baked BasePose data, not live Transforms).
        // Uses the SAME (swapped) BasePose name each leftUpLegBone/etc. Transform is actually
        // looked up under just above, so the length matches the data ApplyBasePose will place on
        // that exact bone -- NOT the data literally labelled "left" (see the swap comment above).
        leftUpLegLen = Vector3.Distance(PosOf("RightUpLeg"), PosOf("RightLeg"));
        leftLegLen = Vector3.Distance(PosOf("RightLeg"), PosOf("RightFoot"));
        leftFootLen = Vector3.Distance(PosOf("RightFoot"), PosOf("RightToeBase"));
        rightUpLegLen = Vector3.Distance(PosOf("LeftUpLeg"), PosOf("LeftLeg"));
        rightLegLen = Vector3.Distance(PosOf("LeftLeg"), PosOf("LeftFoot"));
        rightFootLen = Vector3.Distance(PosOf("LeftFoot"), PosOf("LeftToeBase"));

        prevCursorLocked = Cursor.lockState == CursorLockMode.Locked;
    }

    static Vector3 PosOf(string boneName)
    {
        for (int i = 0; i < BasePose.Length; i++)
            if (BasePose[i].name == boneName) return BasePose[i].pos;
        Debug.LogError("GoblinCarryRig: '" + boneName + "' not found in BasePose data.");
        return Vector3.zero;
    }

    // パリー成功時の高速リセンタリング (追補 19)。この時刻まで適用スルーを増速し、
    // 入力値も 0 へ引き戻す。強 calm 下で回るので横スロッシュごと吸収される。
    float recenterUntil = -1f;
    public void CushionRecenter(float seconds) { recenterUntil = Time.time + seconds; }

    // 適用値を入力値へスルーレート制限つきで追従させる (追補 18)。
    /// <summary>バランス入力を動かしている速さ (バランス値/秒)。追補 37: 速く振ったときは
    /// GoblinPotActions が calm を外して慣性を残す判断に使う。</summary>
    public float BalanceRate { get; private set; }
    float prevArmInput, prevPitchInput;

    void ApplyBalanceSlew(float dt)
    {
        if (dt > 1e-5f)
        {
            float rateNow = new Vector2(armBalance - prevArmInput, pitchBalance - prevPitchInput).magnitude / dt;
            // 1 フレームだけのノイズで慣性判定が暴れないよう軽く均す (立ち上がりは速く)
            BalanceRate = rateNow > BalanceRate
                ? rateNow
                : Mathf.Lerp(BalanceRate, rateNow, 1f - Mathf.Exp(-12f * dt));
        }
        prevArmInput = armBalance; prevPitchInput = pitchBalance;

        bool recenter = Time.time < recenterUntil;
        if (recenter)
        {
            armBalance = Mathf.MoveTowards(armBalance, 0f, 4f * dt);
            pitchBalance = Mathf.MoveTowards(pitchBalance, 0f, 4f * dt);
        }
        float rate = recenter ? 2.5f : balanceApplySpeed;
        float step = rate > 0f ? rate * dt : 999f;
        appliedArmBalance = Mathf.MoveTowards(appliedArmBalance, armBalance, step);
        appliedPitchBalance = Mathf.MoveTowards(appliedPitchBalance, pitchBalance, step);
        // 入力と適用値に差が残っている = 壺が回転している最中
        BalanceMoving = Mathf.Abs(armBalance - appliedArmBalance) > 0.03f
                     || Mathf.Abs(pitchBalance - appliedPitchBalance) > 0.03f;
    }

    /// <summary>バランス入力と適用値を即座にゼロへ戻す (デバッグワープ用)。</summary>
    public void ResetBalance()
    {
        armBalance = pitchBalance = 0f;
        appliedArmBalance = appliedPitchBalance = 0f;
    }

    /// <summary>外部から小さなバランス外乱を与える (追補 15: 着地クッションの早すぎ押し
    /// ペナルティ)。armBalance 換算なので ±1 が最大傾き入力に相当する。</summary>
    public void NudgeBalance(float amount)
    {
        armBalance = Mathf.Clamp(armBalance + amount, -1f, 1f);
    }

    void Update()
    {
        // クリップ再生中 (ツボおろし/転倒/壺なし) は矢印キーのバランス入力を受けない。
        // 壺を持っていないのにバランスパッドのドットだけ動く、を防ぐ。
        if (clipAnimator != null && clipAnimator.IsDrivingBody)
        {
            armBalance = Mathf.MoveTowards(armBalance, 0f, 4f * Time.deltaTime);
            pitchBalance = Mathf.MoveTowards(pitchBalance, 0f, 4f * Time.deltaTime);
            ApplyBalanceSlew(Time.deltaTime);
            return;
        }
        var kb = Keyboard.current;
        if (kb == null) return;
        float dt = Time.deltaTime;
        ApplyBalanceSlew(dt);

        // マウスバランス (2026-08-21 宣言部の注記)。運搬パイプラインが動いている間だけ。
        var mouse = Mouse.current;
        if (mouseBalance && mouse != null && mouseAbsolute)
        {
            // 絶対位置モード (2026-08-22 宣言部の注記)。ロック由来のデルタジャンクが原理的に無い。
            if (Cursor.lockState == CursorLockMode.Locked)
                Cursor.lockState = CursorLockMode.Confined;   // 画面内には留める
            // 開始時 (と復帰時) はカーソルを中央へ寄せてニュートラルから始める。
            // これが無いと「カーソルがたまたま端にあった」だけで開幕から壺が傾く。
            if (!cursorCentredOnce)
            {
                mouse.WarpCursorPosition(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                cursorCentredOnce = true;
            }
            Vector2 mp = mouse.position.ReadValue();
            bool inside = mp.x >= 0f && mp.x <= Screen.width && mp.y >= 0f && mp.y <= Screen.height;
            if (inside && Application.isFocused)   // ゲームビュー外・非フォーカス中は保持
            {
                Vector2 off = (mp - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f))
                              / Mathf.Max(1f, mouseAbsoluteRangePx);
                armBalance = Mathf.Clamp(invertMouseX ? -off.x : off.x, -1f, 1f);     // 中心より右 = 右へ傾く
                pitchBalance = Mathf.Clamp(invertMouseY ? off.y : -off.y, -1f, 1f);   // 中心より上 = 前傾
            }
        }
        else if (mouseBalance && mouse != null)
        {
            if (lockCursorWhileCarrying)
            {
                // ロックが外れていたら左クリックで再ロック (Esc で外すのは Unity 標準挙動)
                if (Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
                    Cursor.lockState = CursorLockMode.Locked;
                if (!cursorLockedOnce)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    cursorLockedOnce = true;
                }
            }

            // ロック切替の瞬間は再センタリングの巨大デルタが乗るので数フレーム捨てる
            bool lockedNow = Cursor.lockState == CursorLockMode.Locked;
            if (lockedNow != prevCursorLocked)
            {
                prevCursorLocked = lockedNow;
                mouseSuppressFrames = 3;
                smoothedMouseDelta = Vector2.zero;
            }

            // ロック中だけ壺に流す。ロックが外れている間 (エディタ UI 操作中など) の
            // カーソル移動が壺を動かすのを防ぐ。
            if (lockedNow && mouseSuppressFrames <= 0)
            {
                Vector2 md = mouse.delta.ReadValue();
                md = Vector2.ClampMagnitude(md, mouseMaxSpeed * dt);   // スパイク頭打ち
                smoothedMouseDelta = Vector2.Lerp(smoothedMouseDelta, md,
                                                  1f - Mathf.Exp(-mouseSmoothing * dt));
                armBalance += (invertMouseX ? -1f : 1f) * smoothedMouseDelta.x * mouseSensitivity;   // 右 = 右へ傾く
                pitchBalance -= (invertMouseY ? -1f : 1f) * smoothedMouseDelta.y * mouseSensitivity; // 奥 (上) = 前傾
            }
            else if (mouseSuppressFrames > 0) mouseSuppressFrames--;
        }
        // SWAPPED 2026-08-12 per explicit request ("QキーとEキーの機能を逆にしたい。感覚的に
        //逆のほうがやりやすそう"): E now raises the left arm (lowers right), Q now raises the
        // right arm (lowers left) -- opposite of the original mapping.
        // 左右キー = 左右バランス（旧 E/Q に相当）
        if (kb.rightArrowKey.isPressed) armBalance += armInputSpeed * dt;
        if (kb.leftArrowKey.isPressed) armBalance -= armInputSpeed * dt;
        armBalance = Mathf.Clamp(armBalance, -1f, 1f);

        // 上下キー = 前後バランス。上で前傾（壺の口を前へ倒す）、下で後傾。
        // 2026-08-14 に上下を入れ替え（ユーザー指定）。
        if (kb.upArrowKey.isPressed) pitchBalance -= pitchInputSpeed * dt;
        if (kb.downArrowKey.isPressed) pitchBalance += pitchInputSpeed * dt;
        pitchBalance = Mathf.Clamp(pitchBalance, -1f, 1f);
    }

    void LateUpdate()
    {
        // 2026-08-15: ベイク済みクリップが体を駆動している間 (ツボおろし/転倒/壺なし) は
        // 運搬パイプライン (BasePose + 歩行 + よろけ + 腕 IK + 壺配置) を丸ごと休止する。
        // 壺はクリップ側が駆動するか、地面に置かれたまま (子から外れている) なので触らない。
        if (clipAnimator != null && clipAnimator.ApplyBody())
        {
            staggerIntensity = 0f;
            walkIntensity = 0f;
            return;
        }

        ApplyBasePose();
        ApplyWalkCycle();
        ApplyStagger();
        ClampFeetToGround();

        if (bonesFound)
        {
            if (!neutralCaptured)
            {
                leftNeutral = CaptureNeutral(leftUpperArm, leftForeArm, leftHand, leftUpperLen, leftForeLen);
                rightNeutral = CaptureNeutral(rightUpperArm, rightForeArm, rightHand, rightUpperLen, rightForeLen);
                neutralCaptured = true;
            }

            // Small additive up/down bob (both arms together, so the carried pot visibly sways
            // per step) while ApplyWalkCycle() above has the walk gait blended in.
            float armBob = walkIntensity * walkArmBobAmplitude * Mathf.Sin(walkPhase * 4f * Mathf.PI);

            // leftUpperArm/rightUpperArm are looked up via the swapped names in Awake() (see
            // comment there) -- leftUpperArm is empirically the VISUAL right arm and
            // rightUpperArm the VISUAL left arm, confirmed by the user after the Q/E key-mapping
            // fix. armBalance>0 ("Q", left up/right down) must raise the visual-left arm
            // (rightUpperArm) and lower the visual-right arm (leftUpperArm), hence the sign flip.
            float armPush = -appliedPitchBalance * pitchHandReach;   // 後傾で手を手前へ引く
            SolveArm(leftUpperArm, leftForeArm, leftHand, -appliedArmBalance, leftNeutral, leftUpperLen, leftForeLen, leftPalmSign, armBob, armPush);
            SolveArm(rightUpperArm, rightForeArm, rightHand, appliedArmBalance, rightNeutral, rightUpperLen, rightForeLen, rightPalmSign, armBob, armPush);

            // 追補 25: 直前に終わったワンショットの最終ポーズを 0.25 秒かけて混ぜ、
            // 「転倒復帰した瞬間に通常ポーズへパッと切り替わる」唐突さを消す。
            // 壺配置 (下) より前に呼ぶこと — 壺は手ボーンの位置から置かれるため。
            if (clipAnimator != null) clipAnimator.ApplyHandoverBlend();
        }

        // Pot bottom anchored at the midpoint between the two palms (its own object origin is
        // already effectively at its bottom face, see potScale comment above), tilting to follow
        // whichever hand is higher/lower -- both requested directly.
        if (bonesFound && pot != null)
        {
            Vector3 handMid = (leftHand.position + rightHand.position) * 0.5f;
            // leftHand is the visual RIGHT hand, rightHand the visual LEFT hand (see Awake()
            // comment) -- this points from visual-left to visual-right, i.e. toward +X when the
            // hands are level, so it tilts naturally as either hand rises or falls.
            Vector3 sideAxis = (leftHand.position - rightHand.position);
            if (sideAxis.sqrMagnitude < 1e-8f) sideAxis = Posture.right;
            sideAxis.Normalize();

            // 壺の姿勢は **体の姿勢 (Posture) を土台にして、腕の高さ差ぶんだけ回す**。
            //
            // 以前は手の位置だけから上方向を作っていた (Cross(root.forward, sideAxis))。
            // これだと腕の左右差は反映されるが、**体そのものの傾きが入らない**。
            // 地形で体が横に傾いても壺は水平のままで、斜面に立った意味が無かった。
            // 手の傾きは「体に対する相対角」として取り出し、それを Posture に足す。
            Vector3 fwd = Posture.forward;
            Vector3 sideOnPlane = Vector3.ProjectOnPlane(sideAxis, fwd);
            Vector3 refRight = Vector3.ProjectOnPlane(Posture.right, fwd);
            float armRoll = 0f;
            if (sideOnPlane.sqrMagnitude > 1e-8f && refRight.sqrMagnitude > 1e-8f)
                armRoll = Vector3.SignedAngle(refRight.normalized, sideOnPlane.normalized, fwd);

            // 前後バランス (上下キー) は、体の姿勢に対する **ピッチ** として足す。
            // 左右バランスが手の高さ差から出てくるのに対し、こちらは両手が同じだけ動くので
            // 手の位置からは傾きが出ない。壺の回転として明示的に与える必要がある。
            // (加速度フィードフォワード (旧追補 28) は削除済み: 入力の一瞬の途切れや減速で
            //  壺が最大 18 度「かくっ」と後傾する副作用があり、こぼれ対策としても calm 側で
            //  十分だったため。2026-08-22)
            Quaternion basePose = Posture.rotation
                * Quaternion.Euler(-appliedPitchBalance * pitchRangeDeg, 0f, 0f);

            // 追補 30 (2026-08-22 QA): 壺の位置追従を **root 相対で低域通過** する。
            // 歩容の粗いフレームサンプリング (特に低 fps) で手の位置が毎フレーム跳ね、
            // 走行 3 m/s に対し壺が瞬間 5〜7.3 m/s で振り回されていた (実測)。壁のこの
            // 高周波の暴れが液体を掬い出すため、流体側の速度クランプ (calm) では
            // 止められなかった。root 相対で均すので移動・旋回そのものは一切遅延せず、
            // 揺すりの高周波成分 (±数 cm) だけが消える。手と壺のずれは 1〜2cm 程度。
            // 回転は実測 0.08 rad/s と暴れておらず、マウスバランスの応答を保つため
            // 軽め (potFollowRotRate) に留める。
            Quaternion targetRot = Quaternion.AngleAxis(armRoll, fwd) * basePose;
            Vector3 localTarget = root.InverseTransformPoint(handMid);
            Quaternion localTargetRot = Quaternion.Inverse(root.rotation) * targetRot;
            if (!potFollowInit)
            {
                smoothedPotLocal = localTarget;
                smoothedPotLocalRot = localTargetRot;
                potFollowInit = true;
            }
            float kp = 1f - Mathf.Exp(-potFollowRate * Time.deltaTime);
            float kr = 1f - Mathf.Exp(-potFollowRotRate * Time.deltaTime);
            smoothedPotLocal = Vector3.Lerp(smoothedPotLocal, localTarget, kp);
            smoothedPotLocalRot = Quaternion.Slerp(smoothedPotLocalRot, localTargetRot, kr);

            pot.position = root.TransformPoint(smoothedPotLocal);
            pot.rotation = root.rotation * smoothedPotLocalRot;
            pot.localScale = potScale;
        }
    }

    // Skinning only depends on a bone's rotation RELATIVE TO ITS OWN BIND POSE -- it does not
    // care what any local axis "means". The previous version reconstructed each bone's FULL
    // absolute orientation from scratch (aim + an invented roll reference), which throws away
    // the bind pose entirely and replaces it with a rotation that has no guaranteed relationship
    // to the delta the mesh actually needs. That is what produced the twisted body / face
    // pointing the wrong way: for a 2-bone limb (the arms), using the elbow's actual position as
    // the roll reference happens to work because the elbow *is* the physically correct reference
    // for that joint's twist -- but there is no such reference for a spine/neck/head bone, so
    // "world-up" was just as arbitrary a guess as the local-X guess it replaced.
    //
    // This version only corrects AIM: it rotates each bone by the minimal rotation that redirects
    // its CURRENT local +Y axis (Blender's guaranteed head-to-tail/toward-child axis, and the
    // already-validated "local +Y == toward child" finding from the arm bones) to the captured
    // target direction, starting from whatever rotation the bone already has (its bind pose,
    // since Awake/the Animator have not posed it otherwise). This is the same minimal-rotation
    // technique already proven safe for the hand's fingertip aim (AimLocalY below) -- it leaves
    // roll/twist exactly as the bind pose had it rather than inventing a value for it, which is
    // far closer to correct than a fabricated roll reference.
    //
    // UPDATED 2026-08-12: that reasoning still holds for arms (BonePose.xDir is Vector3.zero there,
    // so the roll step below is skipped -- SolveArm fully re-derives their rotation from IK
    // afterward anyway), but every other bone now has a real captured roll reference (see BasePose
    // above), and skipping it was actively harmful in two ways: (1) for Hips/legs specifically, any
    // roll a walk/stagger blend introduced via RollAroundY (see BlendAimFull) stayed baked into the
    // bone permanently afterward -- ApplyBasePose only ever nudged Y, so roll drift never reset even
    // once walkIntensity/staggerIntensity decayed back to 0; (2) for spine/neck/head, which nothing
    // else ever rotates, it meant a fixed but WRONG (arbitrary FBX bind-pose) roll forever, which
    // produced a visible twist/seam wherever a corrected bone met an uncorrected one. Applying
    // RollAroundY here too (wherever xDir is non-zero) makes the rest pose fully deterministic and
    // matches Blender's approved neutral pose exactly, not just in aim but in roll too.
    // FIXED 2026-08-12 (bug report: "feet buried up to the shin"; REVISED same day after a
    // follow-up "still a little buried" report): CharacterController's own capsule (center.y=0.95,
    // height=1.9 -> capsule bottom at local Y=0) plus its 0.03 skinWidth means the game's actual
    // floor contact reference is local Y≈0 (confirmed: Room_Floor's collider top is at world Y=0,
    // and the character rests with root.position.y≈0.03 = exactly skinWidth). The first version of
    // this fix aligned the ANKLE ("Foot") bone to that reference, using the raw captured LeftFoot/
    // RightFoot Y (-0.1178/-0.1142) as the offset -- but the ankle joint is never the ground contact
    // point on a real foot; ToeBase is (and sits a further ~11.5cm below Foot in the captured data:
    // LeftToeBase=-0.232974, RightToeBase=-0.233840), so that first version left the toes/boot-front
    // still buried by that same ~11.5cm even though the ankle itself now looked right. Using the
    // true lowest point across BOTH Foot and ToeBase for both legs instead now puts the ankle at a
    // plausible ~11-12cm above ground (consistent with a small foot) and the toes right at floor
    // level. Still one uniform offset (kept separate from the raw captured BasePose.pos values, so
    // those stay exactly what Blender measured) that raises the whole body together without
    // changing any bone's position relative to any other bone.
    static readonly Vector3 GroundOffset = new Vector3(0f, 0.233840f, 0f);

    void ApplyBasePose()
    {
        for (int i = 0; i < BasePose.Length; i++)
        {
            var bone = baseBones[i];
            if (bone == null) continue;
            BonePose bp = BasePose[i];
            bone.position = Posture.position + Posture.rotation * (bp.pos + GroundOffset);

            Vector3 aimWorld = Posture.TransformDirection(bp.yDir).normalized;
            AimLocalY(bone, aimWorld);

            if (bp.xDir != Vector3.zero)
            {
                Vector3 rollWorld = Posture.TransformDirection(bp.xDir).normalized;
                RollAroundY(bone, rollWorld);
            }
        }
    }

    // ADDED 2026-08-10: plays the Carry_Balance_Walk gait (see GoblinWalk.cs) on the Hips + 4 leg
    // bones while GoblinLocomotion reports IsMoving, blended in/out so starting/stopping doesn't
    // pop, and running scales the cycle rate so the stride keeps pace with CurrentSpeed.
    void ApplyWalkCycle()
    {
        // 水中 (2026-08-16 川ギミック): バタ足歩容。浮かんでいる間は入力が無くても
        // 足を動かし続ける (立ち泳ぎ) ので、moving 扱いにする。
        swimGaitActive = swimmer != null && swimmer.InWater;
        // 2026-08-15: 細い足場 (NarrowBeamSurface) の上では綱渡り歩容 (GoblinRopeGait) に
        // 切り替える。足をほぼ一直線に置き、腰を支持脚側へ移す慎重な歩き。
        bool ropeGait = !swimGaitActive && beamSensor != null && beamSensor.OnBeam;
        // 追補 25: ビーム上では停止中も歩容を維持する (moving 扱い)。従来は停止すると
        // walkIntensity が減衰して通常の突っ立ちポーズに戻ってしまっていた (ユーザー報告)。
        // 位相は下の Max(0.15, speed) によりゆっくり進み、「バランスを取りながらの足踏み」になる。
        bool moving = swimGaitActive || ropeGait || (locomotion != null && locomotion.IsMoving);
        float target = moving ? 1f : 0f;
        walkIntensity = Mathf.MoveTowards(walkIntensity, target, walkBlendSpeed * Time.deltaTime);

        if (walkIntensity > 0.001f)
        {
            float dtw = Time.deltaTime;
            if (swimGaitActive)
            {
                // バタ足: 停止中もゆっくり、泳ぐと速く
                float speed = locomotion != null ? locomotion.CurrentSpeed : 0f;
                walkPhase = Mathf.Repeat(walkPhase + dtw * (0.55f + 0.20f * speed), 1f);
            }
            else if (ropeGait)
            {
                float speed = locomotion != null ? Mathf.Max(0.15f, locomotion.CurrentSpeed) : 0.8f;
                walkPhase = Mathf.Repeat(walkPhase + dtw * speed / GoblinRopeGait.StrideLength, 1f);
            }
            else
            {
                // 基準は locomotion.walkSpeed ではなく walkStrideRefSpeed (宣言部の注記を参照)。
                // これで歩幅がゲームプレイ速度から独立し、walkSpeed を上げても足滑りしない。
                float speedRatio = locomotion != null
                    ? Mathf.Max(0.2f, locomotion.CurrentSpeed / Mathf.Max(0.01f, walkStrideRefSpeed))
                    : 1f;
                walkPhase = Mathf.Repeat(walkPhase + dtw * speedRatio / Mathf.Max(0.01f, walkCycleDuration), 1f);
            }
        }
        else
        {
            walkPhase = 0f;
        }

        if (walkIntensity <= 0.001f || hipsBone == null) return;

        Vector3 hy, hx, luy, lux, lly, llx, ruy, rux, rly, rlx, lfy, lfx, rfy, rfx;
        if (swimGaitActive)
        {
            GoblinSwimGait.SampleHips(walkPhase, out hy, out hx);
            GoblinSwimGait.SampleLeftUpLeg(walkPhase, out luy, out lux);
            GoblinSwimGait.SampleLeftLeg(walkPhase, out lly, out llx);
            GoblinSwimGait.SampleRightUpLeg(walkPhase, out ruy, out rux);
            GoblinSwimGait.SampleRightLeg(walkPhase, out rly, out rlx);
            GoblinSwimGait.SampleLeftFoot(walkPhase, out lfy, out lfx);
            GoblinSwimGait.SampleRightFoot(walkPhase, out rfy, out rfx);
            // 後傾 + ぷかぷかは腰の位置が本体。ネイティブ座標系 (GroundOffset 込み) で適用。
            Vector3 hp = GoblinSwimGait.SampleHipsPosNative(walkPhase);
            Vector3 hipsTarget = Posture.position + Posture.rotation * hp;
            hipsBone.position = Vector3.Lerp(hipsBone.position, hipsTarget, walkIntensity);
        }
        else if (ropeGait)
        {
            GoblinRopeGait.SampleHips(walkPhase, out hy, out hx);
            GoblinRopeGait.SampleLeftUpLeg(walkPhase, out luy, out lux);
            GoblinRopeGait.SampleLeftLeg(walkPhase, out lly, out llx);
            GoblinRopeGait.SampleRightUpLeg(walkPhase, out ruy, out rux);
            GoblinRopeGait.SampleRightLeg(walkPhase, out rly, out rlx);
            GoblinRopeGait.SampleLeftFoot(walkPhase, out lfy, out lfx);
            GoblinRopeGait.SampleRightFoot(walkPhase, out rfy, out rfx);
            // ロープ歩きは腰の位置 (低め + 支持脚側へのスウェイ) が本体なので、位置も適用する。
            // SampleHipsPos は接地正規化済み (クリップ内 GroundY を減算済み) なので、
            // ここで GroundOffset を足してはいけない (二重加算になる)。
            Vector3 hp = GoblinRopeGait.SampleHipsPos(walkPhase);
            Vector3 hipsTarget = Posture.position + Posture.rotation * hp;
            hipsBone.position = Vector3.Lerp(hipsBone.position, hipsTarget, walkIntensity);
        }
        else
        {
            GoblinWalk.SampleHips(walkPhase, out hy, out hx);
            GoblinWalk.SampleLeftUpLeg(walkPhase, out luy, out lux);
            GoblinWalk.SampleLeftLeg(walkPhase, out lly, out llx);
            GoblinWalk.SampleRightUpLeg(walkPhase, out ruy, out rux);
            GoblinWalk.SampleRightLeg(walkPhase, out rly, out rlx);
            GoblinWalk.SampleLeftFoot(walkPhase, out lfy, out lfx);
            GoblinWalk.SampleRightFoot(walkPhase, out rfy, out rfx);
        }

        BlendAimFull(hipsBone, hy, hx, walkIntensity);
        ApplyLegChain(leftUpLegBone, leftLegBone, leftFootBone, leftToeBone,
            luy, lux, lly, llx, lfy, lfx, leftUpLegLen, leftLegLen, leftFootLen, walkIntensity);
        ApplyLegChain(rightUpLegBone, rightLegBone, rightFootBone, rightToeBone,
            ruy, rux, rly, rlx, rfy, rfx, rightUpLegLen, rightLegLen, rightFootLen, walkIntensity);
    }

    // ADDED 2026-08-10: blends the Hips + 4 leg bones (already placed by ApplyBasePose()/
    // ApplyWalkCycle() above) toward the corresponding frame of the baked Blender stagger cycle,
    // by an intensity that ramps in once the pot's WORLD tilt passes staggerThresholdDeg. Runs AFTER
    // ApplyWalkCycle() so a stagger still wins if the character is staggering while walking.
    //
    // Direction: the very first playtest reported the lean backwards, so `leanRight` below is the
    // flipped version of the original physical-reasoning guess (see git history for that
    // reasoning) -- treat this sign as empirically-fixed now, not re-derived from first principles.
    void ApplyStagger()
    {
        // **世界基準**での壺の傾き。ゴブリンに対する相対角ではない。
        // pot.rotation はこの LateUpdate の末尾で更新されるので、ここで読むのは
        // 1 フレーム前の姿勢。よろけの判定にとっては問題にならない遅れ。
        // 傾きを **左右倒れ** と **前後倒れ** に分けて評価する。
        // root は yaw だけなので root.right / root.forward は常に水平で、基準に使える。
        float tiltDeg = 0f;
        float leanSide = 0f;          // 正 = 壺の上方向がゴブリンの右へ倒れている
        if (pot != null)
        {
            Vector3 up = pot.up;
            leanSide = Vector3.Dot(up, root.right);
            float lateralDeg = Mathf.Asin(Mathf.Clamp(leanSide, -1f, 1f)) * Mathf.Rad2Deg;
            float foreDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(up, root.forward), -1f, 1f)) * Mathf.Rad2Deg;
            float weighted = foreDeg * staggerPitchWeight;
            tiltDeg = Mathf.Sqrt(lateralDeg * lateralDeg + weighted * weighted);
        }

        // FIXED 2026-08-12 (bug report: holding Q then E, or vice versa, produces a momentary
        // "freeze/snap" -- 一瞬硬直する -- right at the reversal). Root cause: `leanRight` used to
        // be `armBalance < 0f`, a raw sign check that flips the INSTANT armBalance crosses zero.
        // But staggerIntensity (the blend weight applied to the whole baked hip/leg stagger pose,
        // AND to the sideways stagger-move direction below) only decays gradually via
        // MoveTowards -- with armInputSpeed raised to 4 for Q/E sensitivity, armBalance can cross
        // zero well before staggerIntensity has decayed back to 0, so the entire pose (and move
        // direction) would mirror-flip in a single frame while still substantially blended in.
        // Fix: latch the lean side, only adopting a new side once the previous stagger pose has
        // actually blended out to ~0, and force the target intensity to 0 while a reversal is
        // pending so it's guaranteed to reach that latch point instead of racing the crossing.
        // 平地で armBalance < 0 のとき leanSide < 0 になることを実測で確認済み。
        // 旧判定 (armBalance < 0 → 右) と同じ向きになるよう符号を合わせてあるので、
        // 平地での見え方は今までと変わらない。
        bool requestedSideRight = leanSide < 0f;
        if (staggerIntensity <= 0.001f)
            staggerLeanRight = requestedSideRight;
        bool reversalPending = requestedSideRight != staggerLeanRight;

        float rawTargetIntensity = Mathf.Clamp01((tiltDeg - staggerThresholdDeg) / Mathf.Max(0.001f, staggerRampDeg));
        float targetIntensity = reversalPending ? 0f : rawTargetIntensity;
        staggerIntensity = Mathf.MoveTowards(staggerIntensity, targetIntensity, staggerBlendSpeed * Time.deltaTime);

        if (staggerIntensity > 0.001f)
            staggerPhase = Mathf.Repeat(staggerPhase + Time.deltaTime / Mathf.Max(0.01f, staggerCycleDuration), 1f);
        else
            staggerPhase = 0f;

        if (staggerIntensity <= 0.001f || hipsBone == null) return;

        bool leanRight = staggerLeanRight;

        GoblinStagger.SampleHips(staggerPhase, leanRight, out Vector3 hy, out Vector3 hx);
        GoblinStagger.SampleLeftUpLeg(staggerPhase, out Vector3 luy, out Vector3 lux);
        GoblinStagger.SampleLeftLeg(staggerPhase, out Vector3 lly, out Vector3 llx);
        GoblinStagger.SampleRightUpLeg(staggerPhase, out Vector3 ruy, out Vector3 rux);
        GoblinStagger.SampleRightLeg(staggerPhase, out Vector3 rly, out Vector3 rlx);
        GoblinStagger.SampleLeftFoot(staggerPhase, out Vector3 lfy, out Vector3 lfx);
        GoblinStagger.SampleRightFoot(staggerPhase, out Vector3 rfy, out Vector3 rfx);

        BlendAimFull(hipsBone, hy, hx, staggerIntensity);
        ApplyLegChain(leftUpLegBone, leftLegBone, leftFootBone, leftToeBone,
            luy, lux, lly, llx, lfy, lfx, leftUpLegLen, leftLegLen, leftFootLen, staggerIntensity);
        ApplyLegChain(rightUpLegBone, rightLegBone, rightFootBone, rightToeBone,
            ruy, rux, rly, rlx, rfy, rfx, rightUpLegLen, rightLegLen, rightFootLen, staggerIntensity);

        // Stagger toward the side it's leaning, plus forward -- "よろけながら斜めに進んでいく"
        // per the original animation spec. Goes through CharacterController.Move() (not a raw
        // transform.position edit) since GoblinLocomotion already drives this same object via the
        // CharacterController and a direct position write would fight/desync with that.
        if (controller != null)
        {
            float sideSign = (leanRight ? 1f : -1f) * (invertStaggerMoveSide ? -1f : 1f);
            Vector3 sideDir = Posture.right * sideSign;
            Vector3 moveDir = (Posture.forward + sideDir).normalized;
            // FIXED 2026-08-15 (バグ報告「壺に傾きがあるときジャンプできない」):
            // CharacterController.isGrounded は **最後に呼ばれた Move** の結果で決まる。
            // この横移動だけの Move が毎フレーム最後 (LateUpdate) に走ると接地が外れ、
            // 傾き > staggerThresholdDeg の間ずっと GoblinLocomotion の canJump が
            // false になっていた (実測: tilt=0.6 で 60 フレーム後 grounded=False)。
            // 接地中は下向き成分を混ぜて接地を保つ (GoblinLocomotion の
            // verticalVelocity=-1 と同じ手法)。空中 (ジャンプ中) では混ぜない --
            // 混ぜるとよろけ中のジャンプだけ弾道が重くなる。
            // ここで読む isGrounded は今フレームの GoblinLocomotion.Update の Move の
            // 結果なので、着地状態を正しく表している。
            Vector3 staggerMove = moveDir * (staggerMoveSpeed * staggerIntensity);
            if (controller.isGrounded) staggerMove += Vector3.down * 1f;
            controller.Move(staggerMove * Time.deltaTime);
        }
    }

    // Drives one leg's UpLeg->Leg->Foot->ToeBase chain: rotate each bone (aim+roll blended toward
    // its own baked target), then immediately re-derive the NEXT bone's position via forward
    // kinematics from the one just rotated, before rotating that next bone -- so by the time
    // ToeBase is placed, every bone above it in the chain already reflects this frame's real
    // rotation instead of a stale position left over from ApplyBasePose().
    void ApplyLegChain(Transform upLeg, Transform leg, Transform foot, Transform toe,
        Vector3 upLegY, Vector3 upLegX, Vector3 legY, Vector3 legX, Vector3 footY, Vector3 footX,
        float upLegLen, float legLen, float footLen, float t)
    {
        BlendAimFull(upLeg, upLegY, upLegX, t);
        PositionFromParent(upLeg, leg, upLegLen);
        BlendAimFull(leg, legY, legX, t);
        PositionFromParent(leg, foot, legLen);
        BlendAimFull(foot, footY, footX, t);
        PositionFromParent(foot, toe, footLen);
    }

    static void PositionFromParent(Transform parent, Transform child, float boneLen)
    {
        if (parent == null || child == null) return;
        child.position = parent.position + (parent.rotation * Vector3.up) * boneLen;
    }

    // Blends a bone's Y-axis (aim) AND X-axis (roll reference) toward the given target
    // directions. REVERTED 2026-08-12 from a Gram-Schmidt "rebuild the whole rotation" approach
    // back to this delta form: the rebuild was suspected safer in theory, but after switching to
    // it the reported symptoms got WORSE (legs sinking to the shin), meaning it was introducing a
    // Y-axis (position-driving) error somewhere it isn't safe to risk. AimLocalY below is
    // mathematically guaranteed to leave the Y axis at EXACTLY targetY (it's a delta rotation
    // solving Y_current -> Y_target, nothing more), so PositionFromParent's FK chain can't be
    // thrown off by it -- only RollAroundY (which by construction only rotates AROUND the
    // already-fixed Y axis, so it structurally cannot move Y either) is layered on top for roll.
    void BlendAimFull(Transform bone, Vector3 targetYLocal, Vector3 targetXLocal, float t)
    {
        if (bone == null) return;
        Vector3 baseY = (bone.rotation * Vector3.up).normalized;
        Vector3 baseX = (bone.rotation * Vector3.right).normalized;
        Vector3 targetY = Posture.TransformDirection(targetYLocal).normalized;
        Vector3 targetX = Posture.TransformDirection(targetXLocal).normalized;

        Vector3 blendedY = Vector3.Slerp(baseY, targetY, t).normalized;
        Vector3 blendedX = Vector3.Slerp(baseX, targetX, t).normalized;

        AimLocalY(bone, blendedY);
        RollAroundY(bone, blendedX);
    }

    // Rotates the bone around its OWN (already-placed) local Y axis so its local X gets as close
    // as possible to targetXWorld. Cannot change the Y axis itself (the rotation axis IS Y), so
    // this can only ever affect roll/twist, never position -- the safe half of the aim+roll split.
    static void RollAroundY(Transform bone, Vector3 targetXWorld)
    {
        Vector3 yAxis = (bone.rotation * Vector3.up).normalized;
        Vector3 curX = (bone.rotation * Vector3.right).normalized;
        Vector3 targetXProj = targetXWorld - Vector3.Dot(targetXWorld, yAxis) * yAxis;
        if (targetXProj.sqrMagnitude < 1e-8f) return;
        targetXProj.Normalize();
        float angle = Vector3.SignedAngle(curX, targetXProj, yAxis);
        bone.rotation = Quaternion.AngleAxis(angle, yAxis) * bone.rotation;
    }

    // ADDED 2026-08-10: the stagger's deeper hip/knee bend can push a foot below the height it
    // sits at in the normal walk/neutral pose, which reads as the foot sinking into the ground.
    // Only corrects the DEFICIT below that already-approved baseline (never touches anything when
    // both feet are at/above it), so normal walk/idle is untouched -- this is deliberately not a
    // real ground raycast, just a clamp against the one height that's already known-good.
    //
    // FIXED 2026-08-12 (bug report: "body stretches" periodically during one stagger direction):
    // the lift loop used to be `baseBones[i].position += lift`, applied in BasePose's Hips-first,
    // then-UpLeg-then-Leg-then-Foot-then-ToeBase array order. Hips/UpLeg/Leg/Foot/ToeBase form a
    // real Unity parent-child Transform chain, so by the time the loop reached e.g. UpLeg, its
    // `.position` GETTER already reflected Hips' shift moments earlier in the SAME loop (world
    // position is computed live from the parent's current transform) -- so `+= lift` added a
    // SECOND lift on top of the one UpLeg had already inherited for free. This compounded once per
    // link down the chain (confirmed by direct measurement: Hips got 1x lift, UpLeg 2x, Leg 3x,
    // Foot 4x, ToeBase 5x, for a single `deficit`), silently stretching each leg by several times
    // the intended correction and violating the fixed bone lengths ApplyLegChain/PositionFromParent
    // had just carefully set up. Snapshotting every bone's position BEFORE touching any of them
    // (rather than reading current -- possibly parent-already-moved -- position inside the loop)
    // makes every bone's shift come from the SAME unmodified baseline, so it's a true uniform lift.
    void ClampFeetToGround()
    {
        if (leftFootBone == null || rightFootBone == null) return;
        // 水中のバタ足は足が意図的に基準より下 (水面下) へ潜るので、持ち上げ補正をしない。
        if (swimGaitActive) return;
        // + GroundOffset.y: PosOf() returns the raw (un-offset) captured value; ApplyBasePose adds
        // GroundOffset before placing any bone, so the baseline compared against here must too.
        float baseFootY = Mathf.Min(PosOf("LeftFoot").y, PosOf("RightFoot").y) + GroundOffset.y;
        float curLeftY = Posture.InverseTransformPoint(leftFootBone.position).y;
        float curRightY = Posture.InverseTransformPoint(rightFootBone.position).y;
        float deficit = baseFootY - Mathf.Min(curLeftY, curRightY);
        if (deficit <= 0f) return;

        Vector3 lift = Posture.up * deficit;
        var originalPositions = new Vector3[baseBones.Length];
        for (int i = 0; i < baseBones.Length; i++)
            if (baseBones[i] != null) originalPositions[i] = baseBones[i].position;
        for (int i = 0; i < baseBones.Length; i++)
            if (baseBones[i] != null) baseBones[i].position = originalPositions[i] + lift;
    }

    ArmNeutral CaptureNeutral(Transform upperArm, Transform foreArm, Transform hand, float upperLen, float foreLen)
    {
        Vector3 shoulderPos = upperArm.position;
        Vector3 elbowPos = foreArm.position;
        Vector3 wristPos = hand.position;

        Vector3 wristOffsetWorld = wristPos - shoulderPos;
        Vector3 reachWorld = wristOffsetWorld.normalized;
        Vector3 elbowOffset = elbowPos - shoulderPos;
        Vector3 poleWorld = (elbowOffset - Vector3.Dot(elbowOffset, reachWorld) * reachWorld);
        if (poleWorld.sqrMagnitude < 1e-8f) poleWorld = Vector3.Cross(reachWorld, Vector3.up);
        poleWorld.Normalize();

        Vector3 fingertipWorld = (hand.rotation * Vector3.up).normalized;

        return new ArmNeutral
        {
            wristOffsetLocal = Posture.InverseTransformDirection(wristOffsetWorld),
            poleDirLocal = Posture.InverseTransformDirection(poleWorld),
            fingertipDirLocal = Posture.InverseTransformDirection(fingertipWorld),
        };
    }

    // t: -1 (fully lowered) .. 0 (Blender's captured Carry_Balance_Neutral, exactly) .. +1 (fully raised).
    // extraHeightLocal: a small additional raw-meters offset (not t-scaled) layered on top, used
    // for the walk-cycle arm bob so it doesn't interact with the armBalance -1..+1 range at all.
    void SolveArm(Transform upperArm, Transform foreArm, Transform hand, float t, ArmNeutral neutral,
        float upperLen, float foreLen, float palmSign, float extraHeightLocal = 0f,
        float extraForwardLocal = 0f)
    {
        Vector3 shoulderPos = upperArm.position;

        // Target = the captured neutral wrist offset with ONLY its root-local Y (height) component
        // shifted -- X (left-right) and Z (front-back) stay exactly as captured, per spec. The
        // resulting shoulder abduction ("armpit open/close") and elbow bend are whatever a real
        // 2-bone IK solve naturally produces to reach that point, not separately parameterized --
        // that is what keeps the motion anatomically consistent instead of an arbitrary blend.
        // extraForwardLocal は前後バランス。両手を同じだけ前後へ動かすので、これ単体では
        // 壺は傾かない（傾き自体は壺の回転として与えている）。手が置き去りに見えないための追従。
        Vector3 targetOffsetLocal = neutral.wristOffsetLocal
                                  + Vector3.up * (heightRange * t + extraHeightLocal)
                                  + Vector3.forward * extraForwardLocal;
        Vector3 wristTarget = shoulderPos + Posture.TransformDirection(targetOffsetLocal);

        Vector3 toTarget = wristTarget - shoulderPos;
        float maxReach = upperLen + foreLen - 0.001f;
        float minReach = Mathf.Abs(upperLen - foreLen) + 0.001f;
        float d = Mathf.Clamp(toTarget.magnitude, minReach, maxReach);
        Vector3 axisWorld = toTarget.normalized;
        wristTarget = shoulderPos + axisWorld * d; // re-clamp so the rest of the solve stays consistent

        Vector3 poleWorld = Posture.TransformDirection(neutral.poleDirLocal).normalized;
        Vector3 bendWorld = (poleWorld - Vector3.Dot(poleWorld, axisWorld) * axisWorld);
        if (bendWorld.sqrMagnitude < 1e-6f) bendWorld = Vector3.Cross(axisWorld, Vector3.up);
        bendWorld.Normalize();

        float shoulderAngleRad = Mathf.Acos(Mathf.Clamp(
            (upperLen * upperLen + d * d - foreLen * foreLen) / (2f * upperLen * d), -1f, 1f));
        Vector3 elbowDir = Mathf.Cos(shoulderAngleRad) * axisWorld + Mathf.Sin(shoulderAngleRad) * bendWorld;
        Vector3 elbowPos = shoulderPos + elbowDir * upperLen;
        Vector3 foreDir = (wristTarget - elbowPos);
        if (foreDir.sqrMagnitude < 1e-8f) foreDir = elbowDir;
        foreDir.Normalize();

        // Bone length axis assumption: local +Y == "toward child" (Blender authoring
        // convention). If arms look twisted 90/180 degrees, check this first.
        upperArm.rotation = Quaternion.LookRotation(bendWorld, elbowDir);
        foreArm.rotation = Quaternion.LookRotation(bendWorld, foreDir);

        Vector3 fingertipWorld = Posture.TransformDirection(neutral.fingertipDirLocal).normalized;
        AimLocalY(hand, fingertipWorld);
        RollPalmUp(hand, palmSign);
    }

    static void AimLocalY(Transform bone, Vector3 worldDir)
    {
        Vector3 curY = bone.rotation * Vector3.up;
        Quaternion delta = Quaternion.FromToRotation(curY, worldDir);
        bone.rotation = delta * bone.rotation;
    }

    static void RollPalmUp(Transform bone, float palmSign)
    {
        Vector3 yAxis = (bone.rotation * Vector3.up).normalized;
        Vector3 curPalm = (bone.rotation * Vector3.right).normalized * palmSign;
        Vector3 worldUp = Vector3.up;
        Vector3 target = worldUp - Vector3.Dot(worldUp, yAxis) * yAxis;
        if (target.sqrMagnitude < 1e-8f) return;
        target.Normalize();

        float cosA = Mathf.Clamp(Vector3.Dot(curPalm, target), -1f, 1f);
        float angle = Mathf.Acos(cosA) * Mathf.Rad2Deg;
        float sign = Vector3.Dot(yAxis, Vector3.Cross(curPalm, target)) >= 0f ? 1f : -1f;

        bone.rotation = Quaternion.AngleAxis(angle * sign, yAxis) * bone.rotation;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!bonesFound) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(leftUpperArm.position, 0.02f);
        Gizmos.DrawWireSphere(leftForeArm.position, 0.025f);
        Gizmos.DrawWireSphere(leftHand.position, 0.02f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(rightUpperArm.position, 0.02f);
        Gizmos.DrawWireSphere(rightForeArm.position, 0.025f);
        Gizmos.DrawWireSphere(rightHand.position, 0.02f);
    }
#endif
}
