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
    // 2026-08-24: 0.20 -> 0.24。「もう少し左右に動かせるように」(ユーザー指示)。
    // 実測で壺の可動域が中心 +-17 度から **+-23 度** になる (0.30 だと +-35 度で行き過ぎた)。
    // 広がったぶん、その先に「もう一段強いよろけ」(Stagger C) を置いている。
    public float heightRange = 0.24f;

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
    [Header("Brace (よろけ中に逆入力で踏ん張る)")]
    // 頭上の荷は **倒立振子**。倒れた方向へ体を差し込んで支点を移すのが正しく、
    // 反対へ逃げる (カウンターウェイト) のは荷を体の横に持つときの動き。
    // 逃げる向きで作ったところ「壺が左に傾いているのに右手を伸ばして左脚を突っ張る」
    // という意味の通らない姿勢になった (2026-08-24 ユーザー指摘)。
    //
    // さらに、これを傾きだけで自動発動させると体が勝手にバランスを取ってしまい、
    // プレイヤーのマウス操作を肩代わりする。**よろけ中に逆入力しているときだけ**出す。
    // そうすれば「正しく押し返している」ことが画に出るフィードバックになる。
    [Tooltip("踏ん張りが出るのに必要な壺の傾き (度)。")]
    public float braceMinTiltDeg = 4f;

    [Tooltip("踏ん張りが出るのに必要な逆入力の大きさ (0-1)。")]
    public float braceMinInput = 0.15f;

    [Range(0f, 1f)]
    [Tooltip("踏ん張りの強さ。素材ポーズは 22〜24 度倒れているので、0.8 で約 18 度。")]
    public float braceWeight = 0.8f;

    [Tooltip("踏ん張りの出入りに掛ける時間 (秒)。")]
    public float braceBlendTime = 0.18f;

    // 壺は入力に対して速く、逆入力を入れると 0.4 秒ほどで反対側まで振れてしまう (実測)。
    // 条件が切れた瞬間に消すと、押し返した手応えが画に残らない。少しだけ保持する。
    [Tooltip("逆入力の条件が切れた後も踏ん張りを保つ時間 (秒)。")]
    public float braceHold = 0.18f;

    [Range(0f, 1f)]
    [Tooltip("肩から上を水平に保つ度合い。1 で完全に据え置き。傾けると手の高さ差から壺のロールへ逆流する。")]
    public float braceShoulderLevel = 0.9f;

    [Header("Stagger C: もう一段強いよろけ")]
    // 可動域を広げたぶん、その先にもう一段置く。割り込みではなく **同じ変調の続き** なので
    // モードは増えない。ここまで来ると歩幅がほとんど無くなり (その場で足を掻く)、腰が大きく
    // 落ち、体ごと傾いた方へ振られ、流される速さも一段上がる。
    [Tooltip("もう一段強いよろけが始まる壺の傾き (度)。1 段目が振り切る手前から重ねる。")]
    public float staggerHeavyStartDeg = 16f;

    [Tooltip("もう一段強いよろけが最大になる壺の傾き (度)。")]
    public float staggerHeavyFullDeg = 24f;

    [Range(0f, 1f)]
    [Tooltip("最大時に歩幅をさらに縮める量。1 段目と合わせてほぼその場での足掻きになる。")]
    public float staggerHeavyStrideShrink = 0.4f;

    [Tooltip("最大時に腰をさらに落とす量 (m)。")]
    public float staggerHeavyHipDrop = 0.06f;

    [Tooltip("最大時に腰が傾いた方へ振られる量 (m)。体ごと持っていかれる表現。")]
    public float staggerHeavyLurch = 0.15f;

    [Tooltip("最大時に加算される流される速さ (m/s)。1 段目の引っぱりに上乗せする。")]
    public float staggerHeavyDriftSpeed = 0.45f;

    [Header("Stagger B: 傾いた方向へ引っぱられる")]
    // 2026-08-24 差し替え。当初は深いよろけを「短い割り込み」にしたが、挙動が読みにくかった
    // (ユーザー報告)。代わりに **傾いた方向へ進行方向が引っぱられる** 連続的な形にする。
    // モードが無いので分かりやすく、失う通貨が「位置」になる:
    //   こぼれ = 残量 / 引っぱられ = 位置 / (転倒 = 時間、いまは停止中)
    // 崖や川のそばでだけ本当に怖くなるので、ステージ配置がそのまま難易度になる。
    [Tooltip("引っぱりが始まる壺の傾き (度)。歩容の変調より後から効き始める。")]
    public float staggerDriftStartDeg = 10f;

    [Tooltip("引っぱりが最大になる壺の傾き (度)。")]
    public float staggerDriftFullDeg = 22f;

    // 歩行速度は 0.9 m/s。横へ流される速さがこれを超えると「進行方向が引っぱられる」
    // ではなく「横へ飛ばされる」になる (実測で 1.0〜1.2 m/s 出て強すぎた)。
    // 1 段目 0.45 + もう一段 0.45 で、最大でも歩行速度と同じ 0.9 m/s に収める。
    [Tooltip("最大時に傾いた方向へ流される速さ (m/s)。歩行速度 (0.9) を超えないこと。")]
    public float staggerDriftSpeed = 0.45f;

    // 担ぎ姿勢の左右の偏りを打ち消す量 (度)。入力 0 で両手の高さが 3.7 度ずれており、
    // そのぶん壺が傾いたままだった。ここで引くと中立で水平になり、左右の可動域が揃う。
    // 再計測は「立ち止まって armBalance = 0 で 5 秒待ち、両手の高さ差を見る」。
    // 落ち着く前に測ると値がずれる (実測: 直後 2.2 度 / 静定後 3.7 度)。
    [Tooltip("担ぎ姿勢の左右の偏りを打ち消す角度 (度)。入力 0 で壺が水平になる値。")]
    public float potNeutralRollDeg = 3.7f;

    // potNeutralRollDeg で打ち消した後の残り (実測 +1.1 度)。判定の中心をここで合わせると
    // 左右のよろけ強度が揃う (実測: 入力 -1 で 0.61 / +1 で 0.64)。
    [Tooltip("よろけ判定から差し引く壺の傾き (度)。potNeutralRollDeg で打ち消しきれない残り。")]
    public float potTiltBiasDeg = 1.2f;

    [Header("Stagger (壺が世界基準でどれだけ傾いたかで判定)")]
    // 2026-08-24 再設計。よろけと歩行が両立しなかったのは、両方が **腰と脚を絶対値で駆動**
    // していて同じ骨を奪い合っていたため。ブレンドすると「どちらでもない姿勢」になり、
    // 片方を優先すると「よろけ姿勢のまま歩く」になる。専用クリップは使わず、
    // 歩容そのものを **変調** することで解決した。歩行は途切れず、操作も奪わない。
    //
    //   1 段目 (5.5〜18 度)  歩幅が詰まり、足幅が広がり、腰が落ち、上体が壺の側へ入る。
    //   もう一段 (16〜24 度) その上にさらに歩幅短縮・腰落ち・腰の横ずれ・上体の倒れを重ねる。
    //   流され (10 度〜)     傾いた方向へ進行方向が引っぱられる。歩行中のみ。
    //
    // 割り込み (よろけクリップの単発再生) は挙動が読めないため入れていない。
    [Tooltip("よろけを有効にする。")]
    public bool staggerEnabled = true;

    [Header("Stagger A: 歩容の変調 (浅い〜中)")]
    [Range(0f, 1f)]
    [Tooltip("よろけ最大時に脚の振り幅をどれだけ縮めるか。歩幅が詰まって小刻みになる。")]
    public float staggerStrideShrink = 0.5f;

    [Tooltip("よろけ最大時の歩調の倍率。歩幅を詰めたぶん回転を上げないと足が滑る。")]
    public float staggerCadenceBoost = 1.7f;

    [Tooltip("よろけ最大時に足を外へ開く角度 (度)。支持面を広げて耐えている表現。")]
    public float staggerStanceWidenDeg = 8f;

    [Tooltip("よろけ最大時に腰を落とす量 (m)。膝を曲げて足の位置は保つ。")]
    public float staggerHipDrop = 0.07f;

    [Range(0f, 1f)]
    [Tooltip("よろけ最大時に上体を壺の側へ入れる強さ。逆入力の踏ん張りはこれに上乗せされる。")]
    public float staggerLeanWeight = 0.45f;

    [Header("Stagger B: 割り込み (深い)")]

    [Tooltip("世界基準での壺の傾きがこの角度(度)を超えるとよろけ始める。")]
    public float staggerThresholdDeg = 5.5f;
    // 2026-08-24: 10.5 -> 18。変調が 16 度で頭打ちになっていたが、傾きの大きさに応じて
    // 変容量が伸び続けるほうが分かりやすい (ユーザー指示)。23.5 度で最大になる。
    [Tooltip("しきい値からこの角度(度)ぶん超えると、よろけの変調が最大になる。")]
    public float staggerRampDeg = 18f;
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
    [Tooltip("歩行 1 周期の秒数 (walkStrideRefSpeed のときの値)。元クリップは 81 フレーム @24fps = 3.375 秒。実速度に応じて自動で伸縮する。")]
    public float walkCycleDuration = 3.375f;
    // ADDED 2026-08-15 (要望「歩行スピードを速くしたい。ただし歩行アニメと移動量が
    // 乖離しないように」): 従来は位相速度の基準に locomotion.walkSpeed そのものを
    // 使っていた。この方式は「1 サイクルで進む距離 = walkSpeed x walkCycleDuration」に
    // なるため、walkSpeed を上げると歩幅の定義まで一緒に伸びて足滑りが出る。
    // 基準をこの定数に分離すると、歩幅 = walkStrideRefSpeed x walkCycleDuration
    // (1.0 x 2.5 = 2.5m/サイクル) が walkSpeed と無関係に固定され、移動速度を
    // 変えても「速く歩く = 足も比例して速く回る」が常に成り立つ。
    // 歩行アニメの見え方を調整した当時の速度が 1.0 m/s だったので既定は 1.0。
    [Tooltip("歩行アニメの周期 (walkCycleDuration) を調整した基準速度 (m/s)。歩幅 = これ x walkCycleDuration。locomotion.walkSpeed を変えてもここは変えないこと。")]
    public float walkStrideRefSpeed = 0.4531f;

    [Range(0f, 1f)]
    [Tooltip("歩行クリップの肩の動きをどれだけ乗せるか。元クリップは腕を下げて振る歩きなので、壺を担いだ姿勢では強すぎることがある。0 で肩を固定。")]
    public float walkShoulderWeight = 0.35f;

    // 元クリップ (Slow_Orc_Walk) は大柄な重量級の歩きで、上体の前後振りが実測 54 度、
    // 左右振りが 66 度あった。壺を頭上に担いだ状態でそのまま乗せると壺が 40 度傾き、
    // 中身がこぼれそうな絵になる。重い物を担ぐ人間は逆に上体を止めて安定させるので、
    // ここを絞るのは見た目としても正しい。脚と腰は歩容そのものなので絶対値のまま。
    [Range(0f, 1f)]
    [Tooltip("歩行クリップの上体 (背骨) の振りをどれだけ乗せるか。1 で元クリップそのまま、0 で担ぎ姿勢のまま固定。")]
    public float walkUpperBodyWeight = 0.2f;

    // 首と頭を背骨と分けたのは「顔が動きすぎている」という指摘 (2026-08-24) による。
    // 頭は画面上でいちばん注目される部位なので、体幹より一段強く抑える。
    [Range(0f, 1f)]
    [Tooltip("歩行クリップの首・頭の振りをどれだけ乗せるか。顔の揺れが気になるときはここを下げる。")]
    public float walkHeadWeight = 0.08f;

    [Header("Jump (2026-08-24)")]
    // ジャンプは「溜め → 踏切 → 滞空 → 着地の吸収 → 復帰」の 5 段。既存は体がそのまま
    // 上へ跳ね上がるだけで、この 5 段がどれも無かった。姿勢は IGoblinJumpPoses の実装
    // (GoblinJumpStand / GoblinJumpRun) を、歩行と同じく BasePose の上に乗せる。
    [Tooltip("静止からのジャンプで沈み込みに掛ける時間 (秒)。")]
    public float jumpCrouchTime = 0.06f;

    [Tooltip("歩行/走行からのジャンプで沈み込みに掛ける時間 (秒)。走りながら屈む人はいないので短く。")]
    public float jumpCrouchTimeMoving = 0.04f;

    // 2026-08-25 (報告「歩行からジャンプへ移るとき足の開き具合が急激に変わる。蟹股で
    // 歩いていたのが急に閉じてジャンプする」)。ジャンプ姿勢の混ぜ量は沈み込み時間
    // (歩行時 0.04 秒) で入れていた。20fps では 1 フレームに満たないので実質パッと
    // 切り替わり、**足の左右の開きが 1 フレームで 66cm → 32cm** と 34cm 閉じていた (実測)。
    // 沈み込みの長さは踏切のタイミングを決めるので触らず、混ぜ量だけ別の時間で入れる。
    // 既定 0.20 秒 = 踏切 (入力から 0.16 秒) の少し後に混ぜ終わる = 地面を離れながら
    // 脚がまとまる、という人の動きに合う。
    [Tooltip("ジャンプ姿勢を混ぜ込むのに掛ける時間 (秒)。沈み込み時間とは別。短いと足の開きがパッと変わる。")]
    public float jumpBlendInSeconds = 0.20f;

    // 2026-08-25 (報告「パリーなしの着地にモーションの反動が欲しい」)。落下速度に比例した
    // 衝撃量をバネで減衰させ、その量だけ **上体を前へ折る**。脚には触らない (下の注記)。
    //
    // バネは **陰解法** で進めること。明示オイラーだとエディタの 20fps (w*dt > 1) で
    // 1 フレームで符号が反転し、沈むどころか腰が跳ね上がる (実測 -6.7cm)。
    // 2026-08-25 (報告「ジャンプ以外の挙動でのこぼれ量が少ない。走ってもほとんどこぼれない」)。
    // 調べたら壺内クランプ (calm) は原因ではなかった。**calm を全部切っても走行 8.6m で
    // こぼれ 0%**。runSpeed を 5 → 2.4 に下げたので、そもそも液面がリムに届いていない。
    // 腕の振り幅も壺の追従レートも効かず、効いた唯一のレバーが「壺を左右に振ること」。
    // 実測 (走行 2 秒、ゲージ): 揺れなし 58→56% / 揺れ 6 度 + 横 6cm で 54→38%。
    // 歩きは同じ設定でも 38→38% で減らない = 「歩きは安全・走りは危険」が数字で出る。
    // 6 度/6cm は 2 秒で壺が空になる勢いだったので既定はその半分にしてある。
    // ロール成分だけでも噴き出すので、**バランス操作 (armBalance) で打ち消せる** =
    // 上手く操作すれば走ってもこぼれを抑えられる、という設計が成立する。
    // そこで歩容の位相で壺の目標ロールを揺らす。**プレイヤーの入力 (armBalance) とは
    // 別系統の外乱**なので、走ると壺が揺れる → マウスで抑え込む、という操作になる。
    [Header("Gait sway (2026-08-25)")]
    [Tooltip("歩容で壺が左右に揺れる角度 (度)。歩き。0 で揺れなし。")]
    public float gaitSwayWalkDeg = 1.0f;
    [Tooltip("同、走り。走るほうを大きくすると「速いがリスク」になる。")]
    public float gaitSwayRunDeg = 3.0f;
    [Tooltip("揺れの立ち上がり/収まりの速さ (1/s)。")]
    public float gaitSwayRate = 5f;
    // ロールだけでは液が動かない (実測: 壺を +-12 度で振っても走行 8.6m のこぼれ 0%)。
    // 中身を揺らすのは **容器の横移動**。壺の軸まわりに回すだけだと液面が傾くだけで
    // 慣性が生まれない。歩容の位相で壺を左右にずらす量をここで持つ。
    [Tooltip("歩容で壺が左右にずれる量 (m)。歩き。液を揺らすのは主にこちら。")]
    public float gaitSwayWalkLateral = 0.01f;
    [Tooltip("同、走り。")]
    public float gaitSwayRunLateral = 0.03f;
    float gaitSwayAmp, gaitSwayLateralAmp;

    // 2026-08-26: 外乱 (パリー失敗など)。**NudgeBalance は使えない** — armBalance は
    // マウスバランスが毎フレーム上書きするので、外から足しても次のフレームで消える
    // (実測: NudgeBalance(0.5) を入れても armBalance は -0.01 のまま動かなかった)。
    // 歩容の揺れと同じく、入力とは別系統で壺の目標ロールへ足す。
    // 壺が傾けば staggerIntensity が上がるので、上体のよろけは既存の系が出してくれる。
    [Tooltip("外乱が収まる速さ (度/秒)。小さいほど長く尾を引く。")]
    public float disturbDecayDegPerSec = 14f;
    float disturbDeg;
    float potLateralSlow;   // PotLateralDeg の低域通過。外乱の向きを決めるのに使う
    /// <summary>壺に外乱を与える (度)。プレイヤーはマウスで打ち消す。</summary>
    public void DisturbPot(float deg) { disturbDeg = Mathf.Clamp(disturbDeg + deg, -30f, 30f); }
    /// <summary>いま倒れている側へ外乱を与える。向きをランダムにすると、たまたま
    /// 壺を水平へ戻す方向に入って **よろけが出ない** 回ができる (実測: stagger 0.23 → 0.00)。
    /// 必ず不利な向きへ押す。</summary>
    public void DisturbPotOutward(float deg)
    {
        // **符号は逆**。実測: DisturbPot(+12) で PotLateralDeg が -9.7 → -24.8
        // (stagger 0.23 → 1.00)、-12 で +2.7 (stagger 0.00)。
        // つまり「いま傾いている側へさらに押す」= -Sign(PotLateralDeg)。
        //
        // ただし **着地の瞬間の値を使ってはいけない**。その一瞬だけ傾きが逆へ振れて
        // いることがあり、外乱が壺を水平へ戻してしまう回ができる
        // (実測: 2 回に 1 回 stagger が 0.23 → 0.04 と下がった)。
        // 担ぎ姿勢の偏りを表す **平滑化した値** で向きを決める。
        float side = Mathf.Abs(potLateralSlow) > 0.5f ? -Mathf.Sign(potLateralSlow)
                                                      : (Random.value < 0.5f ? -1f : 1f);
        DisturbPot(side * Mathf.Abs(deg));
    }
    /// <summary>診断用: いまの外乱 (度)。</summary>
    public float PotDisturbDeg => disturbDeg;

    [Header("Landing recoil (2026-08-25)")]
    [Tooltip("着地の衝撃量。落下速度 1 m/s あたり。0 で無効。")]
    public float landRecoilPerSpeed = 0.024f;
    [Tooltip("衝撃量の上限。ここで頭打ちになる。")]
    public float landRecoilMax = 0.17f;
    [Tooltip("これ以下の落下速度では反動を出さない (m/s)。歩行中の接地ちらつき対策。")]
    public float landRecoilMinSpeed = 2.5f;
    [Tooltip("戻りのバネの速さ。小さいほどゆっくり戻る。")]
    public float landRecoilFrequency = 10f;
    [Tooltip("戻りの減衰。1 で行き過ぎ無し、小さいほど揺り返しが残る。")]
    public float landRecoilDamping = 0.5f;
    [Tooltip("上体を前へ折る量 (0-1)。衝撃量に比例して掛かる。0 で反動なし。")]
    [Range(0f, 1f)] public float landRecoilUpperBody = 0.35f;


    // 上向きの初速を「伸び上がりのどこで」与えるか。0.8 = 伸びきる少し手前。
    // 以前は伸び上がりが始まる前に飛ばしていたため、**しゃがんだまま浮き上がり、空中で伸びる**
    // という逆さまの動きになっていた (実測: 0.15 秒で高さ 0.21m のときまだ沈んだ姿勢、
    // 伸びきるのは 0.26 秒 = 高さ 0.80m)。人は伸ばしきる過程で地面を離れる。
    [Range(0.4f, 1f)]
    [Tooltip("伸び上がりのどこで地面を離れるか。1 に近いほど「伸ばしきってから飛ぶ」= 入力から浮くまでが遅くなる。")]
    public float jumpLaunchAt = 0.8f;

    float JumpCrouchTime => (locomotion != null && locomotion.IsMoving) ? jumpCrouchTimeMoving : jumpCrouchTime;

    /// <summary>ジャンプ入力から実際に地面を離れるまでの時間。沈み込み + 伸び上がりの途中まで。
    /// GoblinLocomotion がこれを読んで初速を遅らせる (時間の出どころをリグ側に一本化)。</summary>
    public float PreLaunchTime(bool moving)
    {
        float crouch = moving ? jumpCrouchTimeMoving : jumpCrouchTime;
        return crouch + jumpTakeoffTime * jumpLaunchAt;
    }

    // 0.09 → 0.15 (2026-08-24)。0.09 だと伸び上がりで **1 コマに腰が 17cm (毎秒 10m)** 動き、
    // 壺を強く振ってポーションをこぼす原因になっていた。0.15 で 10cm/コマ まで下がり、
    // つなぎ目の最大角度変化も歩行そのものより小さくなる (実測)。
    [Tooltip("踏切 (しゃがみ→伸び上がり) に掛ける時間 (秒)。短くしすぎると壺を振り回してこぼれる。")]
    public float jumpTakeoffTime = 0.15f;

    [Tooltip("滞空で脚をたたむまでの時間 (秒)。")]
    public float jumpAirTime = 0.18f;

    [Tooltip("着地の沈み込みに掛ける時間 (秒)。")]
    public float jumpLandTime = 0.09f;

    [Tooltip("沈み込みから立ち姿勢へ戻る時間 (秒)。長いほど重い荷物に見える。")]
    public float jumpRecoverTime = 0.26f;

    [Range(0f, 1f)]
    [Tooltip("ジャンプ姿勢の上体 (背骨) をどれだけ乗せるか。歩行より強めでよい (溜めの前傾・踏切の伸びが出る)。")]
    public float jumpUpperBodyWeight = 0.45f;

    [Range(0f, 1f)]
    [Tooltip("ジャンプ姿勢の首・頭をどれだけ乗せるか。")]
    public float jumpHeadWeight = 0.15f;

    [Tooltip("小さな段差の踏み外しでは着地モーションを出さない滞空時間のしきい値 (秒)。")]
    public float jumpLandMinAirtime = 0.15f;

    [Tooltip("How fast the walk cycle blends in/out as movement starts/stops.")]
    public float walkBlendSpeed = 4f;
    [Tooltip("Extra vertical bob (meters) added to both arm IK targets while walking, so the carried pot visibly sways with each step.")]
    public float walkArmBobAmplitude = 0.02f;

    Transform hipsBone, leftUpLegBone, leftLegBone, leftFootBone, leftToeBone;
    // 2026-08-23: 重量物歩行で上半身も歩行に反応させるため (旧実装は BasePose のまま固定だった)
    Transform spineBone, spine01Bone, spine02Bone, neckBone, headBone;
    Transform leftShoulderBone, rightShoulderBone;
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
    /// <summary>踏ん張りの強さ 0-1 (よろけ中に逆入力しているときだけ立つ)。</summary>
    public float BraceAmount01 => braceAmt;
    /// <summary>いまのよろけが右側 (root.right = +X) か。転倒の向き (ミラー再生) の判定に使う。</summary>
    public bool StaggerLeanRightNow => staggerLeanRight;
    float staggerPhase, staggerIntensity;
    float braceAmt;          // 踏ん張りの強さ 0-1 (追従済み)
    float braceHoldUntil;    // 条件が切れた後の保持期限
    float braceSign;         // 壺が倒れている向き (+1 = 右)
    bool staggerLeanRight;
    float walkPhase, walkIntensity;
    // エディタのプレビュー (CarryWalkPreview) 用。true の間は walkPhase / walkIntensity を
    // 外から与えた値のまま使い、時間による進行と減衰を止める。エディタでは Time.deltaTime が
    // エディタ側のフレーム間隔 (しばしば数百 ms) になるため、これが無いと指定した位相で
    // 絵が撮れない (実際、最初のプレビューは全コマとも別位相になっていた)。
    [System.NonSerialized] public bool previewLock;
    // ApplyBasePose 直後の上半身の向き (world)。**骨盤を動かす前** に控えるのが要点。
    // ボーンは Unity の親子階層なので、腰を回すと背骨以降の .rotation も一緒に回ってしまう。
    // 歩行を乗せた後に読むと「骨盤の振れ込みの姿勢」を素の姿勢と誤認し、重みを 0 にしても
    // 骨盤の左右の振れがそのまま肩→腕→壺のロールに伝わり続ける (実測: 肩線が 60 度振れた)。
    Quaternion baseSpine, baseSpine01, baseSpine02, baseNeck, baseHead, baseShoulderL, baseShoulderR;

    // ジャンプ姿勢の進行。u は GoblinJump の姿勢軸 (1 = 最も沈む / UExtend = 伸び上がり)。
    enum JumpPhase { None, Crouch, Takeoff, Air, Land, Recover }
    JumpPhase jumpPhase;
    float jumpPhaseT;       // 現フェーズの経過秒
    float jumpU;            // 姿勢軸の現在値
    float jumpU0;           // 現フェーズに入った時点の姿勢軸。ここから補間するので繋ぎ目が飛ばない
    float jumpWeight;       // 立ち姿勢とのブレンド量
    float jumpBlendT;       // 混ぜ込み開始からの経過秒 (jumpBlendInSeconds 用)
    float landRecoilY, landRecoilV;   // 着地の沈み込み (m) とその速度
    float prevVerticalVel;            // 接地する直前の落下速度を拾うため
    bool prevGrounded = true;
    float landRecoilSuppressUntil;    // パリー成功中は掛けない
    /// <summary>診断用: いまの沈み込み量 (m)。</summary>
    public float LandRecoil => landRecoilY;
    /// <summary>診断用: ジャンプ姿勢のブレンド量 (0-1)。</summary>
    public float JumpBlend01 => jumpWeight;
    /// <summary>診断用: ジャンプの局面名。</summary>
    public string JumpPhaseName => jumpPhase.ToString();
    float jumpAirborne;     // 連続滞空時間 (小さな踏み外しで着地モーションを出さないため)
    float lastSeenJumpStart = -999f;
    IGoblinJumpPoses jumpSet = GoblinJumpStand.I;   // 踏み切った瞬間に決めて、そのジャンプ中は変えない
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
    bool clipDrovePot;          // 直前のフレームでクリップが壺を駆動していた
    float potHandoverUntil;     // この時刻まで壺の追従を緩める (クリップからの復帰)

    // クリップ終端から通常担ぎ姿勢へ壺を戻すのにかける時間。ここを速くすると
    // パリー成功後の「勢いよく伸び上がる」が戻ってくる。
    [Tooltip("クリップ終了後、壺を通常の担ぎ位置へ戻すのに緩やかな追従を使う時間 (秒)。")]
    public float potHandoverSeconds = 0.45f;
    [Tooltip("その間の追従の速さ。potFollowRate より小さくすること。")]
    public float potHandoverFollowRate = 3.5f;
    // 2026-08-25 (報告「静態パリーから腕を伸ばすときに左右差があってこぼれる」)。
    // 緩めていたのは **位置だけ** で、回転は potFollowRotRate (60 = 17ms) の素通しだった。
    // クリップの姿勢は左右対称 (実測ロール 0.00 度) なのに担ぎ姿勢は非対称なので、
    // クリップが終わった 0.15 秒で壺が +4.4 → -4.7 度と 9 度振れ、これが液体を横へ持っていく。
    // 回転も同じ間だけ緩める。
    [Tooltip("受け渡し中の回転追従レート (1/s)。potFollowRotRate より小さくすること。")]
    public float potHandoverFollowRotRate = 6f;
    // 受け渡しの初速は「残差 x レート」なので、クリップを途中で打ち切ると残差ぶん速くなる。
    // 実測でこぼれ始めるのが 0.33 m/s なので、その手前で頭打ちにする。
    [Tooltip("受け渡し中に壺が動ける最大速度 (m/s)。0 で無制限。")]
    public float potHandoverMaxSpeed = 0.30f;

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
        spineBone = GoblinBoneUtil.FindDeep(root, "Spine");
        spine01Bone = GoblinBoneUtil.FindDeep(root, "Spine01");
        spine02Bone = GoblinBoneUtil.FindDeep(root, "Spine02");
        neckBone = GoblinBoneUtil.FindDeep(root, "neck");
        headBone = GoblinBoneUtil.FindDeep(root, "Head");
        leftShoulderBone = GoblinBoneUtil.FindDeep(root, "RightShoulder");
        rightShoulderBone = GoblinBoneUtil.FindDeep(root, "LeftShoulder");
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

    /// <summary>パリー成功のように「着地を自分で吸収した」ときに、素の着地反動を止める。</summary>
    public void SuppressLandRecoil(float seconds = 0.6f)
    {
        landRecoilSuppressUntil = Time.time + seconds;
        landRecoilY = 0f; landRecoilV = 0f;
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
            clipDrovePot = true;
            return;
        }
        // クリップが壺を駆動していた直後は、追従フィルタの内部値を **壺の実位置** から
        // 引き直す。これが無いと、クリップ終端の壺高さ (着地クッションでは 1.41m) と
        // 通常担ぎの高さ (1.56m) の差 15cm が 1 フレームで埋められ、壺が +2.8 m/s で
        // 跳ね上がっていた (実測)。パリー成功後に「腕を勢いよく伸ばす」動きの正体で、
        // その加速で液体が持っていかれてこぼれる。引き直せば、この段差は既存の
        // 低域通過フィルタ (potFollowRate) が滑らかに吸収する。
        if (clipDrovePot)
        {
            clipDrovePot = false;
            if (pot != null && root != null)
            {
                smoothedPotLocal = root.InverseTransformPoint(pot.position);
                smoothedPotLocalRot = Quaternion.Inverse(root.rotation) * pot.rotation;
                potFollowInit = true;
                potHandoverUntil = Time.time + potHandoverSeconds;
            }
        }

        ApplyBasePose();
        ApplyWalkCycle();
        ApplyJumpPose();
        ApplyStagger();
        ApplyBraceUnderPot();
        ApplyLandRecoil();
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
            // 加算ワンショット (着地クッション) の差分を、SolveArm の後・壺配置の前に乗せる。
            if (clipAnimator != null) clipAnimator.ApplyAdditive();
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
            // 担ぎ姿勢そのものが左右非対称で、入力 0 でも両手の高さが 3.7 度ずれている。
            // そのぶん壺が傾いたままになり、片側だけ傾けられる量が少なくなっていた
            // (実測: 入力 +1 で +13.4 度に対し -1 で -20.2 度。「右傾きだけ段階を感じない」)。
            // ここで打ち消すと、中立で壺が水平になり、左右の可動域も揃う。
            // 歩容由来の揺れ。左右の踏み替えで 1 周なので位相は 1 サイクル 1 往復。
            // walkIntensity が 0 (静止) なら 0 に収束するので、止まれば揺れない。
            float swayTarget = (locomotion != null && locomotion.IsRunning ? gaitSwayRunDeg : gaitSwayWalkDeg)
                             * Mathf.Clamp01(walkIntensity);
            float latTarget = (locomotion != null && locomotion.IsRunning ? gaitSwayRunLateral : gaitSwayWalkLateral)
                            * Mathf.Clamp01(walkIntensity);
            float k = 1f - Mathf.Exp(-gaitSwayRate * Time.deltaTime);
            gaitSwayAmp = Mathf.Lerp(gaitSwayAmp, swayTarget, k);
            gaitSwayLateralAmp = Mathf.Lerp(gaitSwayLateralAmp, latTarget, k);
            float swayPhase = Mathf.Sin(walkPhase * 2f * Mathf.PI);
            float gaitSway = gaitSwayAmp * swayPhase;
            disturbDeg = Mathf.MoveTowards(disturbDeg, 0f, disturbDecayDegPerSec * Time.deltaTime);
            potLateralSlow = Mathf.Lerp(potLateralSlow, PotLateralDeg(),
                                        1f - Mathf.Exp(-1.2f * Time.deltaTime));
            Quaternion targetRot = Quaternion.AngleAxis(armRoll - potNeutralRollDeg + gaitSway + disturbDeg, fwd) * basePose;
            Vector3 localTarget = root.InverseTransformPoint(handMid);
            // 横ずれは root ローカルの X。低域通過 (potFollowRate 15 = 2.4Hz) を通るので
            // 歩容 (~1.2Hz) はほぼそのまま残る。
            localTarget.x += gaitSwayLateralAmp * swayPhase;
            Quaternion localTargetRot = Quaternion.Inverse(root.rotation) * targetRot;
            if (!potFollowInit)
            {
                smoothedPotLocal = localTarget;
                smoothedPotLocalRot = localTargetRot;
                potFollowInit = true;
            }
            bool handover = Time.time < potHandoverUntil;
            float rate = handover ? potHandoverFollowRate : potFollowRate;
            float rotRate = handover ? potHandoverFollowRotRate : potFollowRotRate;
            float kp = 1f - Mathf.Exp(-rate * Time.deltaTime);
            float kr = 1f - Mathf.Exp(-rotRate * Time.deltaTime);
            Vector3 nextLocal = Vector3.Lerp(smoothedPotLocal, localTarget, kp);
            if (handover && potHandoverMaxSpeed > 0.001f)
            {
                float lim = potHandoverMaxSpeed * Time.deltaTime;
                Vector3 step = nextLocal - smoothedPotLocal;
                if (step.magnitude > lim) nextLocal = smoothedPotLocal + step.normalized * lim;
            }
            smoothedPotLocal = nextLocal;
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

        if (spineBone   != null) baseSpine   = spineBone.rotation;
        if (spine01Bone != null) baseSpine01 = spine01Bone.rotation;
        if (spine02Bone != null) baseSpine02 = spine02Bone.rotation;
        if (neckBone    != null) baseNeck    = neckBone.rotation;
        if (headBone    != null) baseHead    = headBone.rotation;
        if (leftShoulderBone  != null) baseShoulderL = leftShoulderBone.rotation;
        if (rightShoulderBone != null) baseShoulderR = rightShoulderBone.rotation;
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
        if (!previewLock)
            walkIntensity = Mathf.MoveTowards(walkIntensity, target, walkBlendSpeed * Time.deltaTime);

        if (previewLock)
        {
            // 位相はプレビュー側が固定する
        }
        else if (walkIntensity > 0.001f)
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
                // 案A: よろけているぶんだけ歩調を上げる。下で脚の振り幅を縮めるので、
                // ここを上げないと同じ速度に対して足が滑る (歩幅 x 歩調 = 進む距離)。
                float cadence = Mathf.Lerp(1f, staggerCadenceBoost, StaggerWalkAmount);
                walkPhase = Mathf.Repeat(walkPhase + dtw * speedRatio * cadence / Mathf.Max(0.01f, walkCycleDuration), 1f);
            }
        }
        else
        {
            walkPhase = 0f;
        }

        if (walkIntensity <= 0.001f || hipsBone == null) return;

        Vector3 hy, hx, luy, lux, lly, llx, ruy, rux, rly, rlx, lfy, lfx, rfy, rfx;
        Vector3 lty, ltx, rty, rtx;
        bool heavyUpper = false;   // 通常歩行のときだけ上半身も駆動する
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
            // 案A: 脚の向きを 1 周期の平均姿勢へ寄せて振り幅 = 歩幅を縮める。
            // 位相を速めるだけでは歩幅は変わらず足が滑るので、振り幅そのものを縮める。
            // 1 段目 + もう一段。両方合わせるとほぼその場での足掻きになる。
            float shrink = Mathf.Clamp01(StaggerWalkAmount * staggerStrideShrink
                                       + StaggerHeavyAmount * staggerHeavyStrideShrink);
            if (shrink > 0.001f)
            {
                Vector3 my, mx;
                GoblinWalk.MeanLeftUpLeg(out my, out mx);  GoblinWalk.ShrinkStride(shrink, ref luy, ref lux, my, mx);
                GoblinWalk.MeanLeftLeg(out my, out mx);    GoblinWalk.ShrinkStride(shrink, ref lly, ref llx, my, mx);
                GoblinWalk.MeanLeftFoot(out my, out mx);   GoblinWalk.ShrinkStride(shrink, ref lfy, ref lfx, my, mx);
                GoblinWalk.MeanRightUpLeg(out my, out mx); GoblinWalk.ShrinkStride(shrink, ref ruy, ref rux, my, mx);
                GoblinWalk.MeanRightLeg(out my, out mx);   GoblinWalk.ShrinkStride(shrink, ref rly, ref rlx, my, mx);
                GoblinWalk.MeanRightFoot(out my, out mx);  GoblinWalk.ShrinkStride(shrink, ref rfy, ref rfx, my, mx);
            }
            // 2026-08-23 重量物歩行: **腰の位置がこの歩きの本体**。一歩ごとの沈み込み (荷重) と
            // 支持脚側への左右移動が入っている。向きだけ適用していた旧実装では腰が完全に固定で、
            // 「脚だけが小刻みに動き、上半身が乗っているだけ」に見えていた。
            // SampleHipsPos は接地正規化済みなので GroundOffset は足さない (ロープ歩きと同じ)。
            Vector3 hpw = GoblinWalk.SampleHipsPos(walkPhase);
            Vector3 hipsTargetW = Posture.position + Posture.rotation * hpw;
            hipsBone.position = Vector3.Lerp(hipsBone.position, hipsTargetW, walkIntensity);
            heavyUpper = true;
        }

        BlendAimFull(hipsBone, hy, hx, walkIntensity);
        ApplyLegChain(leftUpLegBone, leftLegBone, leftFootBone, leftToeBone,
            luy, lux, lly, llx, lfy, lfx, leftUpLegLen, leftLegLen, leftFootLen, walkIntensity);
        ApplyLegChain(rightUpLegBone, rightLegBone, rightFootBone, rightToeBone,
            ruy, rux, rly, rlx, rfy, rfx, rightUpLegLen, rightLegLen, rightFootLen, walkIntensity);
        if (heavyUpper)
        {
            ApplyStaggerStance();
            ApplyWalkUpperBody(walkIntensity);
            // 爪先。蹴り出しと着地の踏み替えはここが動かないと「板の足」に見える。
            // ApplyLegChain が位置を決めた後なので、向きを足すだけで FK は崩れない。
            GoblinWalk.SampleLeftToe(walkPhase, out lty, out ltx);
            GoblinWalk.SampleRightToe(walkPhase, out rty, out rtx);
            if (leftToeBone  != null) BlendAimFull(leftToeBone, lty, ltx, walkIntensity);
            if (rightToeBone != null) BlendAimFull(rightToeBone, rty, rtx, walkIntensity);
        }
    }



    /// <summary>傾き(度)を -1..1 に均す。leanStartDeg まで無反応、leanFullDeg で最大。</summary>

    // 左右と前後のズレを重み付きで重ねて、いまの向きの上に足す。




    // 差分回転を「いまの向き」に重ねる。差分は root ローカルで定義してある。

    // ==== 踏ん張り (よろけ中に逆入力したとき / 2026-08-24) ====
    //
    // 頭上の荷は **倒立振子**。倒れた方向へ体を差し込んで支点を移すのが正しい動きで、
    // 反対へ逃げる (カウンターウェイト) のは荷を体の横に持つときの動き。
    //
    // ただし傾きだけで自動発動させると、体が勝手にバランスを取ってプレイヤーのマウス操作を
    // 肩代わりしてしまう。**よろけ中に逆入力しているときだけ** 出すことで、
    // 「正しく押し返している」ことが画に出るフィードバックになる (アシストではない)。
    //
    // 倒すのは腰寄りの背骨だけで、肩を載せている Spine の向きは戻す。肩まで傾けると
    // 肩線 → 手の高さ差 → armRoll → 壺のロール、と逆流して自動補正になってしまう
    // (実測: 戻さないと肩線 35 度・手の高さ差 44 度、戻せば 6 度・7 度)。
    void ApplyBraceUnderPot()
    {
        if (spine01Bone == null || spine02Bone == null) return;

        float target = 0f;
        if (pot != null && staggerIntensity > 0.05f)
        {
            // 壺の左右の倒れ (担ぎ姿勢の偏りを差し引いた値)。正 = ゴブリンの右へ倒れている。
            float tilt = PotLateralDeg();
            if (Mathf.Abs(tilt) >= braceMinTiltDeg)
            {
                braceSign = Mathf.Sign(tilt);
                // 案A の一部として、よろけている間は常に少しだけ壺の側へ入る (引っぱられている)。
                target = Mathf.Clamp01(staggerIntensity * staggerLeanWeight + StaggerHeavyAmount * 0.5f);
                // 逆入力 = 傾きと反対向きの入力。armBalance は壺の傾きと同符号なので
                // (実測: armBalance +1 → 壺 +14.8 度)、符号が逆なら押し返している。
                // 押し返しているときは深く入る = 踏ん張り。
                float push = -Mathf.Sign(tilt) * armBalance;
                if (push >= braceMinInput)
                {
                    target = Mathf.Max(target, Mathf.Min(staggerIntensity, push));
                    braceHoldUntil = Time.time + braceHold;
                }
            }
        }
        // 条件が切れても braceHold の間は落とさない。
        if (target <= 0f && Time.time < braceHoldUntil) target = braceAmt;
        braceAmt = Mathf.MoveTowards(braceAmt, target, Time.deltaTime / Mathf.Max(0.02f, braceBlendTime));
        if (braceAmt <= 0.002f) return;

        // **壺が倒れている側へ** 体を入れる (符号を反転しない = カウンターではない)。
        float w = braceAmt * braceWeight * braceSign;

        Quaternion spineKeep = spineBone != null ? spineBone.rotation : Quaternion.identity;

        // 親から子の順に (Hips → Spine02 → Spine01 → Spine)。逆順だと親の回転が子を引きずる。
        BraceBone(spine02Bone, GoblinLean.Spine02SideP, GoblinLean.Spine02SideN, w);
        BraceBone(spine01Bone, GoblinLean.Spine01SideP, GoblinLean.Spine01SideN, w);

        if (spineBone != null && braceShoulderLevel > 0.001f)
        {
            Quaternion keep = Quaternion.Slerp(spineBone.rotation, spineKeep, braceShoulderLevel);
            Quaternion local = Quaternion.Inverse(Posture.rotation) * keep;
            BlendAimFull(spineBone, local * Vector3.up, local * Vector3.right, 1f);
        }
    }

    void BraceBone(Transform bone, Quaternion sideP, Quaternion sideN, float w)
    {
        if (bone == null || Mathf.Abs(w) < 0.001f) return;
        Quaternion d = Quaternion.Slerp(Quaternion.identity, w > 0f ? sideP : sideN, Mathf.Abs(w));
        // ズレは root ローカルで定義してあるので、いまの向きも root ローカルへ落として掛ける。
        Quaternion local = d * (Quaternion.Inverse(Posture.rotation) * bone.rotation);
        BlendAimFull(bone, local * Vector3.up, local * Vector3.right, 1f);
    }

    // ==== ツボ担ぎジャンプ (2026-08-24) ====
    //
    // 既存は GoblinLocomotion が上向きの初速を与えるだけで、体は歩行/立ちの姿勢のまま
    // 平行移動していた。「ジャンプ感が無い」の正体はここで、跳躍を跳躍に見せているのは
    // 上下の移動そのものではなく **溜め・踏切・着地** の 3 つ。
    //
    //   溜め    しゃがんで荷重をためる      (この間は飛ばない。Locomotion 側で初速を遅らせている)
    //   踏切    一気に伸び上がる            (ここで初速が入る)
    //   滞空    脚をたたんで着地に備える
    //   着地    沈み込んで衝撃を吸収する
    //   復帰    立ち姿勢へ戻る
    //
    // 姿勢セットは踏み切った瞬間の移動状態で選ぶ (GoblinJumpStand / GoblinJumpRun)。
    // 静止跳びの素材は「しゃがみ→伸び上がり→着地して沈む」の並びなので、溜めは着地の
    // 沈み込みポーズを共用し、そこから u を UExtend へ動かす = 素材の逆再生になり、
    // 「沈んでから伸び上がる」がそのまま得られる。走り跳びの素材は素直な順序。
    void ApplyJumpPose()
    {
        if (hipsBone == null) return;
        // プレビュー (CarryWalkPreview) では姿勢軸とブレンド量を外から与える。
        if (previewLock) { if (jumpWeight > 0.001f) ApplyJumpBones(jumpSet, jumpU, jumpWeight); return; }
        if (locomotion == null) return;

        bool grounded = locomotion.Grounded;
        jumpAirborne = grounded ? 0f : jumpAirborne + Time.deltaTime;

        // ジャンプ入力の検出。Locomotion は押した時刻を記録するだけで、実際の踏切は
        // jumpAnticipation 後なので、こちらは押した瞬間から溜めに入れる。
        if (locomotion.LastJumpStartTime > lastSeenJumpStart + 0.01f)
        {
            lastSeenJumpStart = locomotion.LastJumpStartTime;
            // 静止からと歩行/走行からでは人体の動きが別物 (両足で沈んで真上 / 片足で蹴って
            // 脚が前後に開く)。踏み切った瞬間の速度でセットを選び、ジャンプ中は切り替えない。
            // 歩行からのときは、**そのとき接地している足で踏み切る** セットを選ぶ。
            // 逆の足のセットを使うと、浮いている足で地面を蹴る絵になる。
            jumpSet = locomotion.IsMoving ? PickRunJumpSet() : GoblinJumpStand.I;
            jumpPhase = JumpPhase.Crouch;
            jumpPhaseT = 0f;
            jumpBlendT = 0f;
            jumpU0 = jumpU;
        }

        // 歩いていて崖から落ちた場合も、着地は吸収させたい (溜め・踏切は無い)。
        if (jumpPhase == JumpPhase.None && !grounded && jumpAirborne > jumpLandMinAirtime)
        {
            // 崖から歩いて落ちた場合。踏切は無いので、そのときの移動状態でセットを選ぶ。
            jumpSet = locomotion.IsMoving ? PickRunJumpSet() : GoblinJumpStand.I;
            jumpPhase = JumpPhase.Air;
            jumpPhaseT = jumpAirTime;      // 既に脚をたたんだ状態から始める
            jumpBlendT = 0f;               // 崖から歩いて落ちた場合も脚は混ぜて入れる
            jumpU0 = jumpSet.UExtend;
        }

        if (jumpPhase == JumpPhase.None)
        {
            jumpWeight = Mathf.MoveTowards(jumpWeight, 0f, Time.deltaTime / Mathf.Max(0.01f, jumpRecoverTime));
            if (jumpWeight <= 0.001f) return;
        }

        jumpPhaseT += Time.deltaTime;
        jumpBlendT += Time.deltaTime;
        // 混ぜ込みは局面をまたいで一本の時間で進める。局面ごとに 1 を代入していたのが
        // 「1 フレームで切り替わる」の原因だった。
        float blendIn = Ease(jumpBlendT / Mathf.Max(0.01f, jumpBlendInSeconds));
        switch (jumpPhase)
        {
            case JumpPhase.Crouch:
                // 立ち姿勢から沈み込む見せ方にするため、伸びた姿勢から UCrouch へ送る。
                jumpU = Mathf.Lerp(jumpSet.UExtend, jumpSet.UCrouch,
                    Ease(jumpPhaseT / Mathf.Max(0.01f, JumpCrouchTime)));
                // 割り込みは溜めの全体を使って入れる。0.05 秒で入れると歩行姿勢から
                // しゃがみへ 3 コマで飛び、そこが最大の飛び (36 度) になっていた。
                jumpWeight = blendIn;
                // 沈み込みが終わったら伸び上がりへ。初速は伸び上がりの途中 (jumpLaunchAt) で
                // 入るので、「飛んだかどうか」では溜めを抜けられない。時間の出どころは
                // このリグ側に一本化してあり、Locomotion は PreLaunchTime を読む。
                if (jumpPhaseT >= JumpCrouchTime)
                {
                    jumpPhase = JumpPhase.Takeoff;
                    jumpPhaseT = 0f;
                    jumpU0 = jumpU;
                }
                break;

            case JumpPhase.Takeoff:
                jumpU = Mathf.Lerp(jumpU0, jumpSet.UExtend,
                    Ease(jumpPhaseT / Mathf.Max(0.01f, jumpTakeoffTime)));
                jumpWeight = blendIn;
                if (jumpPhaseT >= jumpTakeoffTime)
                { jumpPhase = JumpPhase.Air; jumpPhaseT = 0f; jumpU0 = jumpU; }
                break;

            case JumpPhase.Air:
                // 上昇中は伸びたまま、落下に入ると脚をたたむ。滞空時間は高さで変わるので
                // 時間ではなく上下速度で送る方が、低い跳躍でも高い跳躍でも破綻しない。
                float fall = Mathf.Clamp01(-locomotion.VerticalVelocity / 4f);
                float byTime = Mathf.Clamp01(jumpPhaseT / Mathf.Max(0.01f, jumpAirTime));
                jumpU = Mathf.Lerp(jumpU0, jumpSet.UAir, Ease(Mathf.Max(fall, byTime)));
                jumpWeight = blendIn;
                if (grounded && jumpAirborne <= 0f && jumpPhaseT > 0.05f)
                {
                    jumpPhase = JumpPhase.Land;
                    jumpPhaseT = 0f;
                    jumpU0 = jumpU;
                }
                break;

            case JumpPhase.Land:
                jumpU = Mathf.Lerp(jumpU0, jumpSet.ULand,
                    Ease(jumpPhaseT / Mathf.Max(0.01f, jumpLandTime)));
                jumpWeight = blendIn;
                if (jumpPhaseT >= jumpLandTime)
                { jumpPhase = JumpPhase.Recover; jumpPhaseT = 0f; jumpU0 = jumpU; }
                break;

            case JumpPhase.Recover:
                jumpU = jumpSet.ULand;
                // 復帰は抜けるほうの時間。短いジャンプで混ぜ終わる前に着地したときに
                // 一度 1 まで上がってしまわないよう、小さいほうを採る。
                jumpWeight = Mathf.Min(blendIn, 1f - Ease(jumpPhaseT / Mathf.Max(0.01f, jumpRecoverTime)));
                if (jumpWeight <= 0.001f) { jumpPhase = JumpPhase.None; jumpWeight = 0f; return; }
                break;
        }

        ApplyJumpBones(jumpSet, jumpU, jumpWeight);
    }

    // 段の切り替わりで速度が段差にならないよう、両端の傾きが 0 になる補間を使う。
    // 線形のままだと 1 コマで 36 度動く箇所ができた (歩行区間の最大は 19 度、実測)。
    static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    /// <summary>歩行からのジャンプで、**いま接地している足で踏み切る** セットを選ぶ。
    /// 左右の取り違えを避けるため、セット側が「踏切の瞬間にどちらが支持脚か」を
    /// SupportIsLeftSide で申告し、ここではリグの leftToeBone / rightToeBone の高さと
    /// 突き合わせるだけにしてある (ボーン名の左右入れ替えに依存しない)。</summary>
    IGoblinJumpPoses PickRunJumpSet()
    {
        if (leftToeBone == null || rightToeBone == null) return GoblinJumpRun.I;
        bool leftPlanted = Posture.InverseTransformPoint(leftToeBone.position).y
                         < Posture.InverseTransformPoint(rightToeBone.position).y;
        return leftPlanted == GoblinJumpRun.I.SupportIsLeftSide
            ? (IGoblinJumpPoses)GoblinJumpRun.I
            : GoblinJumpRunL.I;
    }

    void ApplyJumpBones(IGoblinJumpPoses set, float u, float t)
    {
        Vector3 hy, hx, luy, lux, lly, llx, ruy, rux, rly, rlx, lfy, lfx, rfy, rfx, lty, ltx, rty, rtx;
        set.SampleHips(u, out hy, out hx);
        set.SampleLeftUpLeg(u, out luy, out lux);
        set.SampleLeftLeg(u, out lly, out llx);
        set.SampleRightUpLeg(u, out ruy, out rux);
        set.SampleRightLeg(u, out rly, out rlx);
        set.SampleLeftFoot(u, out lfy, out lfx);
        set.SampleRightFoot(u, out rfy, out rfx);
        set.SampleLeftToe(u, out lty, out ltx);
        set.SampleRightToe(u, out rty, out rtx);

        // 腰の高さがこの動きの本体 (沈む/伸びる)。接地正規化済みなので GroundOffset は足さない。
        Vector3 hp = set.SampleHipsPos(u);
        Vector3 target = Posture.position + Posture.rotation * hp;
        hipsBone.position = Vector3.Lerp(hipsBone.position, target, t);

        BlendAimFull(hipsBone, hy, hx, t);
        ApplyLegChain(leftUpLegBone, leftLegBone, leftFootBone, leftToeBone,
            luy, lux, lly, llx, lfy, lfx, leftUpLegLen, leftLegLen, leftFootLen, t);
        ApplyLegChain(rightUpLegBone, rightLegBone, rightFootBone, rightToeBone,
            ruy, rux, rly, rlx, rfy, rfx, rightUpLegLen, rightLegLen, rightFootLen, t);
        if (leftToeBone  != null) BlendAimFull(leftToeBone, lty, ltx, t);
        if (rightToeBone != null) BlendAimFull(rightToeBone, rty, rtx, t);

        // 上半身は歩行と同じく加算。基準は ApplyBasePose 直後に控えた向き (baseSpine ほか)。
        // **親から子の順**に適用すること (Hips → Spine02 → Spine01 → Spine)。
        float uw = t * jumpUpperBodyWeight;
        float hw = t * jumpHeadWeight;
        AimAdditive(spine02Bone, set.SampleSpine02Add(u), baseSpine02, uw);
        AimAdditive(spine01Bone, set.SampleSpine01Add(u), baseSpine01, uw);
        AimAdditive(spineBone,   set.SampleSpineAdd(u),   baseSpine,   uw);
        AimAdditive(neckBone,    set.SampleNeckAdd(u),    baseNeck,    hw);
        AimAdditive(headBone,    set.SampleHeadAdd(u),    baseHead,    hw);
        float sw = t * walkShoulderWeight;
        AimAdditive(leftShoulderBone,  set.SampleLeftShoulderAdd(u),  baseShoulderL, sw);
        AimAdditive(rightShoulderBone, set.SampleRightShoulderAdd(u), baseShoulderR, sw);
    }

    /// <summary>案A の変調量。割り込み (案B) 中は歩容を止めているので 0。</summary>
    float StaggerWalkAmount => staggerEnabled ? staggerIntensity : 0f;

    /// <summary>もう一段強いよろけの量 (0-1)。1 段目が振り切る手前から重なって効く。</summary>
    float StaggerHeavyAmount
    {
        get
        {
            if (!staggerEnabled || pot == null) return 0f;
            float deg = Mathf.Abs(PotLateralDeg());
            return Mathf.InverseLerp(staggerHeavyStartDeg,
                Mathf.Max(staggerHeavyStartDeg + 0.1f, staggerHeavyFullDeg), deg);
        }
    }

    // 案A: 足を外へ開いて支持面を広げ、腰を落とす。
    // 腰を落とすときは **足首の位置を保ったまま膝を曲げる**。単に腰を下げると足が地面へ
    // めり込み、接地補正が体ごと持ち上げて何も起きなくなる。
    void ApplyStaggerStance()
    {
        float a = StaggerWalkAmount;
        if (a <= 0.001f || hipsBone == null) return;

        if (staggerStanceWidenDeg > 0.01f)
        {
            float deg = staggerStanceWidenDeg * a;
            WidenLeg(leftUpLegBone, leftLegBone, leftFootBone, leftToeBone,
                     +deg, leftUpLegLen, leftLegLen, leftFootLen);
            WidenLeg(rightUpLegBone, rightLegBone, rightFootBone, rightToeBone,
                     -deg, rightUpLegLen, rightLegLen, rightFootLen);
        }

        float heavy = StaggerHeavyAmount;

        // もう一段: 腰を傾いた方へ振る。足は付いていかないので体ごと持っていかれて見える。
        if (heavy > 0.001f && staggerHeavyLurch > 0.001f)
            hipsBone.position += Posture.right * (Mathf.Sign(PotLateralDeg()) * staggerHeavyLurch * heavy);

        float drop = staggerHipDrop * a + staggerHeavyHipDrop * heavy;
        if (drop > 0.001f) DropHipsKeepingFeet(drop);
    }

    // 上脚を進行軸まわりに回して足を外へ開く。子は FK で置き直す。
    void WidenLeg(Transform upLeg, Transform leg, Transform foot, Transform toe,
                  float deg, float upLen, float legLen, float footLen)
    {
        if (upLeg == null) return;
        Vector3 axis = Posture.forward;
        upLeg.rotation = Quaternion.AngleAxis(deg, axis) * upLeg.rotation;
        PositionFromParent(upLeg, leg, upLen);
        PositionFromParent(leg, foot, legLen);
        PositionFromParent(foot, toe, footLen);
    }

    // 腰を下げ、足首が元の位置に残るよう膝を曲げ直す (2 ボーン IK)。
    // 着地の反動。膝で沈んで、上体が少し前へ折れて、バネで戻る。
    void ApplyLandRecoil()
    {
        if (hipsBone == null || locomotion == null || landRecoilPerSpeed <= 0.0001f) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        bool grounded = locomotion.Grounded;

        if (grounded && !prevGrounded && Time.time >= landRecoilSuppressUntil)
        {
            float impact = Mathf.Abs(prevVerticalVel);
            if (impact >= landRecoilMinSpeed)
            {
                landRecoilY = Mathf.Min(landRecoilMax, impact * landRecoilPerSpeed);
                landRecoilV = 0f;
            }
        }
        prevGrounded = grounded;
        if (!grounded) prevVerticalVel = locomotion.VerticalVelocity;

        if (Mathf.Abs(landRecoilY) <= 0.0005f && Mathf.Abs(landRecoilV) <= 0.001f)
        { landRecoilY = 0f; landRecoilV = 0f; return; }

        // **先に今の値を使ってから**進める。後回しだと一番深いところが 1 フレーム抜ける。
        // 伸び上がり側は脚が伸びきるので浅く抑える。
        // 2026-08-25: **脚は触らない**。腰を沈める版は 2 度作って 2 度とも
        // 「足を跳ね上げているように見えて変」と却下された。原因は
        // DropHipsKeepingFeet が「いまの足首の位置」を保つことで、ジャンプ姿勢の
        // 上がったままの足を基準にすると脚だけが目立って動くため。
        // 衝撃は上体で見せる。壺は手の位置から置かれるので、上体が折れれば
        // 壺も一緒に前へ突き出て「受けた重さ」が出る。
        float shock = Mathf.Clamp01(landRecoilY / Mathf.Max(0.01f, landRecoilMax));
        float uw = landRecoilUpperBody * shock;
        if (uw > 0.001f)
        {
            LeanBone(spine02Bone, GoblinLean.Spine02Fore, uw);
            LeanBone(spine01Bone, GoblinLean.Spine01Fore, uw);
        }

        // 減衰バネを **陰解法** で進める。明示オイラーだとエディタの 20fps
        // (w*dt が 1 を超える) で 1 フレームで符号が反転し、沈むはずが腰が跳ね上がる。
        float w = Mathf.Max(0.1f, landRecoilFrequency);
        float f = 1f + 2f * dt * landRecoilDamping * w;
        float oo = w * w;
        float hoo = dt * oo;
        float detInv = 1f / (f + dt * hoo);
        float y0 = landRecoilY;
        landRecoilY = (f * y0 + dt * landRecoilV) * detInv;
        landRecoilV = (landRecoilV - hoo * y0) * detInv;
    }

    // 基準姿勢からのズレ (GoblinLean) を、いまの向きに重み付きで乗せる。
    void LeanBone(Transform bone, Quaternion delta, float w)
    {
        if (bone == null || w < 0.001f) return;
        Quaternion d = Quaternion.Slerp(Quaternion.identity, delta, Mathf.Clamp01(w));
        Quaternion local = d * (Quaternion.Inverse(Posture.rotation) * bone.rotation);
        BlendAimFull(bone, local * Vector3.up, local * Vector3.right, 1f);
    }

    void DropHipsKeepingFeet(float drop)
    {
        Vector3 lAnkle = leftFootBone != null ? leftFootBone.position : Vector3.zero;
        Vector3 rAnkle = rightFootBone != null ? rightFootBone.position : Vector3.zero;
        Vector3 lKnee = leftLegBone != null ? leftLegBone.position : Vector3.zero;
        Vector3 rKnee = rightLegBone != null ? rightLegBone.position : Vector3.zero;

        hipsBone.position -= Posture.up * drop;

        SolveLegToAnkle(leftUpLegBone, leftLegBone, leftFootBone, leftToeBone,
                        lAnkle, lKnee, leftUpLegLen, leftLegLen, leftFootLen);
        SolveLegToAnkle(rightUpLegBone, rightLegBone, rightFootBone, rightToeBone,
                        rAnkle, rKnee, rightUpLegLen, rightLegLen, rightFootLen);
    }

    // 2 ボーン IK。曲げ平面は元の膝位置で決めるので、膝の向きは元の歩容のまま。
    void SolveLegToAnkle(Transform upLeg, Transform leg, Transform foot, Transform toe,
                         Vector3 ankle, Vector3 kneeHint, float upLen, float legLen, float footLen)
    {
        if (upLeg == null || leg == null || foot == null) return;
        Vector3 root0 = upLeg.position;
        Vector3 d = ankle - root0;
        float dist = Mathf.Clamp(d.magnitude, 1e-4f, upLen + legLen - 1e-4f);
        Vector3 u = d.normalized;
        float a = (upLen * upLen - legLen * legLen + dist * dist) / (2f * dist);
        float h = Mathf.Sqrt(Mathf.Max(0f, upLen * upLen - a * a));
        Vector3 n = Vector3.ProjectOnPlane(kneeHint - root0, u);
        if (n.sqrMagnitude < 1e-8f) n = Vector3.ProjectOnPlane(Posture.forward, u);
        n = n.normalized;
        Vector3 knee = root0 + u * a + n * h;

        AimLocalY(upLeg, (knee - root0).normalized);
        PositionFromParent(upLeg, leg, upLen);
        AimLocalY(leg, (ankle - knee).normalized);
        PositionFromParent(leg, foot, legLen);
        PositionFromParent(foot, toe, footLen);
    }

    // 2026-08-24: 上半身は歩行クリップの **平均姿勢からのズレ** だけを加算する。
    // 絶対値で入れると、元クリップ (前かがみの重い歩き) の姿勢が、壺を頭上に担いだ
    // BasePose の立ち姿勢を丸ごと押し潰してしまう (実測で上体が 60 度以上倒れた)。
    //
    // 基準は **ApplyBasePose 直後に控えた向き** を使う (baseSpine ほか)。ここで現在値を
    // 読んではいけない: 腰を回した後なので骨盤の振れが混入し、重みを 0 にしても上体が
    // 振れ続ける。重みは「歩行の振りをどれだけ乗せるか」であると同時に「骨盤の振れから
    // どれだけ上体を切り離すか」でもあり、壺が水平に保たれるかを直接決める。
    void ApplyWalkUpperBody(float t)
    {
        float uw = t * walkUpperBodyWeight;
        float hw = t * walkHeadWeight;
        // **親から子の順**で適用すること。このリグの背骨は Hips → Spine02 → Spine01 → Spine
        // (Spine が最上位で、首と肩がその子) という並びで、Spine02 が腰に最も近い。子を決めて
        // から親を回すと、親の回転が子を引きずって設定値が壊れる (実測: 重み 0 でも背骨が
        // 基準姿勢から 67 度ずれていた)。BasePose の並び順が根拠。
        AimAdditive(spine02Bone, GoblinWalk.SampleSpine02Add(walkPhase), baseSpine02, uw);
        AimAdditive(spine01Bone, GoblinWalk.SampleSpine01Add(walkPhase), baseSpine01, uw);
        AimAdditive(spineBone,   GoblinWalk.SampleSpineAdd(walkPhase),   baseSpine,   uw);
        AimAdditive(neckBone,    GoblinWalk.SampleNeckAdd(walkPhase),    baseNeck,    hw);
        AimAdditive(headBone,    GoblinWalk.SampleHeadAdd(walkPhase),    baseHead,    hw);

        // 肩は腕 IK (SolveArm) より **前** に動く。IK は壺の取っ手を世界座標で狙うので、
        // 手の位置は変わらず、肩線の傾きと上腕の付け根だけが歩行に連動する。
        float sw = t * walkShoulderWeight;
        AimAdditive(leftShoulderBone,  GoblinWalk.SampleLeftShoulderAdd(walkPhase),  baseShoulderL, sw);
        AimAdditive(rightShoulderBone, GoblinWalk.SampleRightShoulderAdd(walkPhase), baseShoulderR, sw);
    }

    // baseRot (BasePose が置いた向き) に歩行の差分 add を掛けた向きへ、weight で寄せる。
    // 座標系に注意: GoblinWalk の焼き込みは root ローカル、BlendAimFull が受け取るのも
    // Posture ローカルなので、world の baseRot をいったん Posture ローカルへ落としてから
    // 差分を掛ける。ここを world のまま渡すと Posture の回転が二重に掛かる。
    void AimAdditive(Transform bone, Quaternion add, Quaternion baseRot, float weight)
    {
        if (bone == null) return;
        Quaternion baseLocal = Quaternion.Inverse(Posture.rotation) * baseRot;
        Quaternion local = Quaternion.Slerp(baseLocal, add * baseLocal, weight);
        BlendAimFull(bone, local * Vector3.up, local * Vector3.right, 1f);
    }

    // ADDED 2026-08-10: blends the Hips + 4 leg bones (already placed by ApplyBasePose()/
    // ApplyWalkCycle() above) toward the corresponding frame of the baked Blender stagger cycle,
    // by an intensity that ramps in once the pot's WORLD tilt passes staggerThresholdDeg. Runs AFTER
    // ApplyWalkCycle() so a stagger still wins if the character is staggering while walking.
    //
    // Direction: the very first playtest reported the lean backwards, so `leanRight` below is the
    // flipped version of the original physical-reasoning guess (see git history for that
    // reasoning) -- treat this sign as empirically-fixed now, not re-derived from first principles.
    /// <summary>担ぎ姿勢の偏りを差し引いた、壺の左右の倒れ (度)。正 = ゴブリンの右へ倒れている。
    /// よろけ・引っぱり・踏ん張りはすべてこの値で判定する。</summary>
    float PotLateralDeg()
    {
        if (pot == null) return 0f;
        float d = Mathf.Asin(Mathf.Clamp(Vector3.Dot(pot.up, root.right), -1f, 1f)) * Mathf.Rad2Deg;
        return d - potTiltBiasDeg;
    }

    void ApplyStagger()
    {
        if (!staggerEnabled)
        {
            // 止めている間は強度も 0 に落とす。転倒の判定と踏ん張りがこれを見ているため。
            staggerIntensity = 0f;
            staggerPhase = 0f;
            return;
        }
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
            float lateralDeg = PotLateralDeg();
            leanSide = -lateralDeg;   // 従来の符号 (leanSide < 0 を「右」と呼ぶ実測合わせ) を維持
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

        // ---- 案B: 傾いた方向へ進行方向を引っぱる ----
        // モードを作らず、傾きの大きさに応じて連続的に強くする。歩行は止めない。
        // 失うのは「位置」なので、崖や川のそばでだけ本当に危険になる。
        // 引っぱりは **歩いている間だけ**。止まっているのに横へ滑るのは、進行方向が
        // 引っぱられるという意図とも違うし、操作していないのに位置が動くので理不尽 (ユーザー報告)。
        bool movingNow = locomotion != null && locomotion.IsMoving;
        if (controller != null && pot != null && movingNow)
        {
            float deg = PotLateralDeg();
            float drift = Mathf.InverseLerp(staggerDriftStartDeg,
                Mathf.Max(staggerDriftStartDeg + 0.1f, staggerDriftFullDeg), Mathf.Abs(deg));
            if (drift > 0.001f && jumpWeight < 0.01f)
            {
                // 壺が倒れている側へ流される。前進はそのままなので、進路が弧を描く。
                float speed = staggerDriftSpeed * drift + staggerHeavyDriftSpeed * StaggerHeavyAmount;
                Vector3 push = Posture.right * Mathf.Sign(deg) * speed;
                // 下向き成分を残す: 水平だけ Move すると isGrounded が落ちてジャンプできなくなる
                // (2026-08-19 の既知の不具合。よろけの押し出しでも同じ)。
                push.y = -1f;
                controller.Move(push * Time.deltaTime);
            }
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
