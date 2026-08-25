using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================================
// GoblinPotActions -- 壺の「下ろす / 拾う」(E キー) と「転倒」の状態管理。
//
// 2026-08-15 追加 (要望①②③)。
//   E キー (運搬中・接地中)  : ツボおろし (Carry_PotDown ベイク) を再生し、壺を前方の地面へ。
//   E キー (壺なし・壺の近く): 壺の正面へ位置合わせしてツボおろしを逆再生 = 拾い上げ。
//   よろけ最大が続く         : 転倒 (Carry_FallOver) を再生。壺は前方に落ちて大量にこぼれ、
//                              終わると「壺が地面にある」状態 (= ツボおろし後と同じ) になる。
// 壺なしの間は GoblinClipAnimator がベイク済みの Idle/Walk/Run/Jump を再生する。
// 壺は下ろした瞬間にゴブリンの子から外れ (歩いても付いてこない)、拾うと子へ戻る。
// ============================================================================================
[RequireComponent(typeof(GoblinClipAnimator))]
public class GoblinPotActions : MonoBehaviour
{
    public enum State { Carrying, PuttingDown, PotDown, PickingUp, Falling }

    [Header("Keys")]
    [Tooltip("下ろす / 拾うキー。")]
    // 2026-08-16 ユーザー指定で E -> R に変更。
    public Key actionKey = Key.R;

    [Header("Pickup")]
    [Tooltip("拾えるようになる壺までの水平距離 (m)。")]
    public float pickupRange = 1.6f;
    // 2026-08-22: 水平距離しか見ていなかったため、崖下や川へ落とした壺が真下にあるだけで
    // 拾えてしまった。斜面や段差の許容は要るので、垂直方向にも別枠で上限を設ける。
    [Tooltip("拾えるようになる壺との垂直距離 (m)。落として下へ転がった壺を離れた場所から拾えないようにする。")]
    public float pickupHeightRange = 1.2f;
    [Tooltip("拾い上げ時の壺正面への位置合わせ距離。ツボおろしクリップが壺を root 前方に置く距離と一致させること (2026-08-16 の経路改訂で 1.00m)。")]
    public float pickupStandDistance = 1.00f;

    [Header("Spill")]
    // 2026-08-22: 転倒しても壺が直立のまま着地する (実測 potTilt 0.0 度) ため、
    // 中身が 56% も残っていた。ゲーム性としては「転んだら / 水に落ちたら失う」が正しいので、
    // 手放したあとに壺そのものを倒して、流体を物理的に流し出す。粒子を消すのではなく
    // 実際に注ぎ出すので、見た目もこぼれた液体の量も一致する。
    [Tooltip("転倒・落水で手放したとき壺を倒す角度 (度)。90 を超えると口が下を向く。0 で無効。")]
    public float spillTipAngle = 125f;
    [Tooltip("倒れきるまでの時間 (s)。短くしすぎると壺の移動がテレポート扱いになり中身が飛ぶ。")]
    public float spillTipSeconds = 0.55f;
    [Tooltip("倒すときに壺を持ち上げる量 (m)。横倒しで胴が地面へめり込むのを防ぐ。")]
    public float spillTipLift = 0.45f;

    [Header("Fall")]
    [Tooltip("転倒を有効にする。2026-08-24 は崩れの見せ方を固めるため一時的にオフ (ユーザー指示)。")]
    public bool fallEnabled = false;

    [Tooltip("よろけ強度が最大のままこの秒数経過したら転倒する。")]
    public float fallAfterSeconds = 0.9f;

    // 2026-08-24: 転倒の「支払い先」を残量からタイムへ寄せる。
    // 失敗の役割分担を分けるため:
    //   こぼれ    残量をじわじわ削る  (急いだ代償)
    //   転倒      **時間を大きく持っていく**  (バランスを崩した代償)
    //   川へ落下  全部失う            (場所を間違えた代償)
    // 従来の転倒は残量も時間も最大で持っていくので、川と役割が重なっていた。
    // 転倒しきった後、こぼれたポーションを壺へ吸い戻す時間 (秒) と強さ。
    // 戻る量は「時間 x 強さ」で決まる (RecallSpill は割合ではなく持続時間を取る)。
    // 0 秒にすると従来どおりの全損になる。着地パリィは 0.5〜0.7 秒 / 強さ 6〜10。
    [Tooltip("転倒後にこぼれたポーションを吸い戻す時間 (秒)。0 で全損 (従来の挙動)。")]
    public float fallRecallSeconds = 0.8f;

    [Tooltip("転倒後の吸い戻しの強さ。着地パリィは 6〜10。")]
    public float fallRecallStrength = 5f;
    [Tooltip("転倒の再トリガー禁止時間 (s)。")]
    public float fallCooldown = 2.0f;
    // 2026-08-16: 転倒クリップの序盤 (踏ん張りフェーズ f1-19、壺リリースは f22) の間は
    // 倒れる方向と反対の矢印キーで踏みとどまれる。
    [Tooltip("転倒クリップのこのフレームまでは反対矢印キーで復帰できる (壺リリース 22f より前にすること)。")]
    public float fallRecoverFrames = 18f;
    [Tooltip("復帰時の逆再生速度。")]
    public float fallRecoverSpeed = 1.4f;

    [Header("Locomotion clips")]
    // 2026-08-16 修正: 当初「足の最大開き」を 1 周距離にしていたが理論的に誤り。
    // 正しくは「接地中に足が体の下を通過する距離 ÷ 接地時間の割合」。
    // ベイクデータから実測: 歩行 0.582/0.48=1.202、走り 0.407/0.21=1.935。
    // (旧値では脚が歩行 1.8 倍 / 走り 2.65 倍速く回り、走りが走りに見えなかった)
    [Tooltip("壺なし歩行の 1 周距離 (m)。接地解析による実測 1.202。")]
    public float walkStride = 1.202f;
    [Tooltip("壺なし走りの 1 周距離 (m)。接地解析による実測 1.935。")]
    public float runStride = 1.935f;

    public State Current { get; private set; } = State.Carrying;
    public bool Carrying => Current == State.Carrying;

    GoblinClipAnimator anim;
    GoblinCarryRig rig;
    GoblinLocomotion loco;
    GoblinTerrainTilt terrainTilt;
    PotionGaugeUI gaugeUI;
    GoblinSwimmer swimmerRef;
    CharacterController cc;
    Transform pot;
    FluidCore fluid;
    float staggerMaxTimer;
    float fallCooldownTimer;
    bool fallMirror;        // 転倒クリップをミラー再生中か (復帰キーの向き判定用)
    bool fallRecovering;    // 転倒クリップを逆再生して踏みとどまり中

    void Awake()
    {
        anim = GetComponent<GoblinClipAnimator>();
        rig = GetComponent<GoblinCarryRig>();
        loco = GetComponent<GoblinLocomotion>();
        cc = GetComponent<CharacterController>();
        terrainTilt = GetComponent<GoblinTerrainTilt>();
        pot = transform.Find("Carry_Pot");
        // FIXED 2026-08-22: FindFirstObjectByType はシーンに FluidCore が 2 つ (壺と滝) ある
        // ForestStage で**滝を掴んでいた**。calm・ジョルト・パリー回収など流体メカニクス
        // 一式が滝に適用され、壺は保護ゼロだった (「歩くだけで大量にこぼれる」の真犯人)。
        // 壺は自分の子 (Carry_Pot) にあるので、そこから取る。
        fluid = GetComponentInChildren<FluidCore>();
        if (fluid == null) fluid = FindFirstObjectByType<FluidCore>();

        // 追補 24: 待機画面 (エディットモード) では壺をゴブリンの横の地面に置いて
        // 見せている (頭に壺が埋まるのを避けるため)。実行開始時は **流体の初期化前に**
        // 運搬位置へ戻す。地面位置のまま流体を初期化すると、リグが壺を手元へ動かした
        // ときに中身が置き去りになる (実測: 残量 9% になった)。
        // GoblinPotActions は実行順 0 < FluidCore(100) なのでここで戻せば間に合う。
        if (pot != null && Current == State.Carrying)
        {
            pot.localPosition = new Vector3(0f, 1.17f, 0.12f);
            pot.localRotation = Quaternion.identity;
        }
    }

    // 拾い上げの間だけ流体の速度上限を落とす (2026-08-16: 「持ち上げ終わった時に
    // ジャンプと同じくらいポーションが上に飛び出る」対策)。噴き上げ高さは v^2/2g なので
    // 2.5 m/s なら約 0.3m = リムをほぼ越えられない。終了 1 秒後に元へ戻す。
    // 熱い床の連続バウンド用: 発射のたびに延長される calm 解除時刻 (0 = 未使用)
    // 追補 14 で通常ジャンプ・落下の滞空にも共用 (滞空中は延長され続け、着地 0.6 秒後に解除)
    float hotCalmUntil;
    // 追補 22: パリーなし着地の直後は加速 calm を当てない (着地の掛け金を守る)
    float rampCalmBlockedUntil;
    float airborneTime;   // 連続滞空時間。歩行中の isGrounded ちらつきを除くゲート用

    /// <summary>空中でパリーを押した (Space)。計測用のデバッグフックからも同じ経路を通す。</summary>
    void PressCushion()
    {
        cushionPressed = true;
        cushionPressTime = Time.time;
        LogParry($"<color=cyan>空中押し → 予約</color> (滞空 {airborneTime:F2}s 時点)");
        // 追補 19: 押した瞬間から clamp が入る (パリーの手応え)。
        // 着地判定で成立なら 0.6/0.5 へ強化、失敗なら即解除される。
        BeginFluidCalm(1.2f);
        hotCalmUntil = Mathf.Max(hotCalmUntil, Time.time + 1.0f);
        // 傾いたままのジャンプ対策: 水平化は「空中で」行う。滞空中は壺内が
        // 実効無重力で、クランプ下の回転には流体がほぼ完全に追従する
        // (着地後に回すと回転自体がスロッシュ源になる — 実測 42% で悪化した)。
        if (rig != null) rig.CushionRecenter(0.5f);
        // 追補 25: 膝で受ける = 残りの落下を軟化して衝撃自体を減らす
        if (loco != null) loco.SoftenLanding(1.2f);
    }

    /// <summary>計測用: 次に空中判定になったフレームでパリーを押したことにする。
    /// ゲームビューが非アクティブだとキー注入が毎フレーム消えるため (実測)、
    /// A/B を取るにはこの経路が要る。</summary>
    [HideInInspector] public bool debugParryRequest;

    // ---- 着地クッション (追補 15) ----
    // 滞空中に Space を押し、着地に間に合えば膝で衝撃を吸収してこぼれを抑える。
    [Header("Landing cushion (追補 15)")]
    [Tooltip("着地のこの秒数前までの空中 Space 押しでクッション成立。")]
    public float cushionWindow = 0.35f;
    [Tooltip("ジャスト窓 (秒)。着地のこの秒数前までに押すとこぼれほぼゼロ + 強発光。")]
    public float cushionJustWindow = 0.12f;
    // 実測 (満杯・定速走りジャンプ): なし 88-89% / グッド (0.6) 92% / ジャスト (0.5) 99%。
    // 0.8 だとグッドが「なし」と差別化できなかった (89%) ため 0.6 に強化 (追補 16)。
    [Tooltip("成立時の壺内クランプ。滞空 calm (1.2) より強い。")]
    public float cushionCalm = 0.5f;    // 追補 25: 0.6 → 0.5 (高所パリーの吸収強化)
    [Tooltip("ジャスト時の壺内クランプ。")]
    public float cushionJustCalm = 0.35f;   // 追補 25: 0.5 → 0.35
    [Tooltip("早すぎた押しのよろけペナルティ (armBalance 換算)。")]
    public float cushionFailNudge = 0.25f;
    [Tooltip("着地後のジャンプ抑止時間 (秒)。惜しい遅押しの誤ジャンプ防止。")]
    public float cushionJumpSuppress = 0.2f;
    // 追補 27 (2026-08-21): 道の段差 (15-25cm) を降りるだけで「生着地」となり、通常ジャンプ用の
    // 掛け金 (cushionMissJolt) が発火して不自然に大量にこぼれていた (実測: 橋→道進入の直線
    // 歩行だけで 45% 喪失、滞空 0.13-0.24s の生着地が 3-4 回)。実際のジャンプの滞空は 0.6s
    // 前後なので、このしきい値未満の小落下では掛け金 (ジョルト + calm 解除) を発動しない。
    [Tooltip("この滞空秒数以上の落下だけを「本物のジャンプ/落下」としてパリーなし着地の掛け金 (ジョルト + calm 解除) を発動する。歩行の段差 (滞空 0.1-0.25s) を除外する。")]
    public float significantFallAirtime = 0.35f;
    [Tooltip("加減速中 (歩き出し・停止) の壺内クランプ (m/s)。跳ね上がり v^2/2g がフリーボード (~3cm) を越えない値にする。0.7 で 2.5cm。")]
    public float rampCalmClamp = 0.5f;   // 0.7 では歩き出しで 11% 溢れた。0.5 で実測ゼロ
    [Tooltip("歩行中の壺内クランプ (m/s)。歩容の揺すりで液が溢れ続けるのを抑える (追補 29)。1.0 で歩行 14m の保持 97.8% (実測)。")]
    public float walkCalmClamp = 1.0f;
    [Tooltip("走行中の壺内クランプ (m/s)。走りは歩きよりスロッシュが激しいため強めに絞る (追補 30)。")]
    public float runCalmClamp = 0.7f;
    // ---- 追補 37: バランス操作の慣性 ----
    [Tooltip("バランスをゆっくり動かしているときの壺内クランプ (m/s)。微調整でこぼれないようにする分。")]
    public float balanceCalmClamp = 1.3f;
    [Tooltip("この速さ (バランス値/秒) を超えてバランスを動かしている間は calm を一切掛けない。速く振れば慣性でこぼれる。小さくすると少し動かしただけでこぼれるようになる。")]
    public float balanceInertiaRate = 1.2f;
    // 注入は壺内クランプ (MaxSpeed 5 相対) で頭打ちになるため 5 が実効最大 (実測)。
    [Tooltip("パリーなし着地で壺内に注入する跳ね返り速度 (m/s、上+前方)。通常ジャンプの掛け金。")]
    public float cushionMissJolt = 5.0f;

    // 戻り際のこぼれ対策 (2026-08-24)。クリップの伸び上がり区間だけを遅く再生する。
    // 流体は同じ時計で回っているので、これは見た目ではなく実際に壺の速度を落とす。
    [Tooltip("着地クッションの伸び上がり (折り返し以降) の再生速度。1 = 素材どおり。")]
    [Range(0.2f, 1f)] public float cushionRiseSpeed = 0.5f;

    bool cushionPressed;         // この滞空中に Space を押したか
    float cushionPressTime = -999f;

    [Tooltip("着地クッションを加算再生する (担ぎ姿勢からの差分)。切ると従来の絶対再生に戻る。A/B 用。")]
    public bool cushionAdditive = true;
    // 2026-08-25: 着地クッションは **加算再生** (担ぎ姿勢からの差分) にしてある。
    // 体は運搬パイプラインが書き続けるので、クッション中でも歩容・地形の傾き・
    // バランス入力がそのまま生きる。「着地ポーズのまま滑る」「歩き出しが遅れる」ために
    // 一時期入れていた「途中で打ち切る」処理は、これで不要になったので削除した。

    // ---- パリーデバッグ HUD (2026-08-16: 「全然発動しない」調査用) ----
    [Header("Debug")]
    [Tooltip("パリー入力のタイミング判定を画面に表示する (調整が終わったら切る)。")]
    public bool debugParryHud = true;
    readonly System.Collections.Generic.List<string> parryLog = new System.Collections.Generic.List<string>();
    void LogParry(string msg)
    {
        parryLog.Insert(0, $"[{Time.time:F2}] {msg}");
        if (parryLog.Count > 7) parryLog.RemoveAt(parryLog.Count - 1);
    }

    void OnGUI()
    {
        if (!debugParryHud) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true };
        style.normal.textColor = Color.white;
        float gd = terrainTilt != null ? terrainTilt.GroundDistance : -1f;
        GUI.Label(new Rect(20, 170, 1200, 28),
            $"<b>[Parry]</b> state={Current}  groundDist={gd:F2}  air={airborneTime:F2}  予約={(cushionPressed ? "<color=cyan>あり</color>" : "なし")}",
            style);
        for (int i = 0; i < parryLog.Count; i++)
            GUI.Label(new Rect(20, 200 + i * 26, 1400, 28), parryLog[i], style);
    }
    float emissionBase = -1f;    // 発光パルスの基準値 (初回に取得)
    bool glowPulsing;

    void BeginFluidCalm(float clamp = 2.5f)
    {
        if (fluid == null) return;
        // 2026-08-16: 全体の maxSpeed ではなく **壺内限定** の maxSpeedInPot を絞る。
        // 以前は壺外の液滴まで 1.2 m/s に制限され、こぼれた液体がスローモーションで
        // 落ちていた (ユーザー報告)。既に calm 中でも、より低いクランプ要求は上書きする。
        fluid.maxSpeedInPot = fluid.maxSpeedInPot > 0f
            ? Mathf.Min(fluid.maxSpeedInPot, clamp) : clamp;
    }

    IEnumerator EndFluidCalm(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (fluid != null) fluid.maxSpeedInPot = -1f;
    }

    void Update()
    {
        if (fallCooldownTimer > 0f) fallCooldownTimer -= Time.deltaTime;
        var kb = Keyboard.current;
        bool actionPressed = kb != null && kb[actionKey].wasPressedThisFrame;

        switch (Current)
        {
            case State.Carrying:
                if (loco != null) loco.gentleAccel = true;    // 追補 13: 運搬中は加減速ランプ
                if (terrainTilt != null) terrainTilt.gentleMode = true;   // 追補 18: 傾き角速度制限
                UpdateFallTrigger();
                // 熱い床で飛ばされたら「あちち」ジャンプを再生 (移動はロックしない:
                // 飛ばされながらの操作はそのまま生きる)
                if (loco != null && loco.ConsumeHotJump())
                {
                    // 初速 8.5 m/s の急発進は流体が追従できず大噴出するので、
                    // 発射直後 (上昇 ~0.42s) だけ壺内クランプを絞る。
                    // 追補 19: 従来は滞空全体 (2.2s) を calm していたが、それだと 3.6m 落下の
                    // 着地までほぼ無傷になり「パリーの意味がない」。降下は素通しにして、
                    // 着地はパリー (着地クッション) で守るゲーム性に統一する。
                    BeginFluidCalm(1.2f);
                    hotCalmUntil = Mathf.Max(hotCalmUntil, Time.time + 0.45f);
                    // 連続バウンドでは (前のクリップが残っていても) 頭から再生し直す
                    if (!anim.OneShotActive || anim.CurrentOneShot == GoblinClip.HotJump)
                        anim.PlayOneShot(GoblinClip.HotJump, reverse: false, drivePotToEnd: true,
                                         potEvent: null, done: null,
                                         speed: 0.5f);   // 滞空 ~1.7 秒に合わせて 20f を引き伸ばす
                }
                // 追補 14: 通常ジャンプ・落下でも滞空中は壺内 calm を効かせる。
                // 弾道滞空中は壺内が実効無重力になり、離陸時に残っていた揺れが減衰せず
                // 壁を這い上がって流出する (実測: 歩き/走りジャンプで 37-39% 損失、
                // 静止ジャンプは液が静止しているので 0%)。0.12 秒のゲートで歩行中の
                // isGrounded ちらつきを除外。解除は着地 0.6 秒後 (hotCalmUntil を共用)。
                // 追補 19: 滞空判定は CharacterController.isGrounded ではなく
                // GoblinTerrainTilt.GroundDistance (足元レイキャストの実測) を使う。
                // isGrounded は「最後の Move の結果」なので、よろけ中はリグの追加 Move
                // (下方向成分つき) が飛行終盤に true を返し、着地直前のパリー入力が
                // 「地上の Space」扱いで丸ごと無視されていた (実測ログで確認)。
                // 追補 25: 水中は「滞空」ではない (浮いていると水底まで 0.6m あるため
                // 誤判定していた)。水中のジャンプ・着水はパリー対象外。
                if (swimmerRef == null) swimmerRef = GetComponent<GoblinSwimmer>();
                bool inWaterCushion = swimmerRef != null && swimmerRef.InWater;
                bool airborneNow = !inWaterCushion && (terrainTilt != null
                    ? terrainTilt.GroundDistance > 0.15f
                    : (cc != null && !cc.isGrounded));
                if (airborneNow)
                {
                    airborneTime += Time.deltaTime;
                    // 追補 15: 空中の Space はクッション予約 (地上判定が無いのでジャンプには化けない)
                    if ((kb != null && kb.spaceKey.wasPressedThisFrame) || debugParryRequest)
                    {
                        debugParryRequest = false;
                        PressCushion();
                    }
                }
                else
                {
                    if (kb != null && kb.spaceKey.wasPressedThisFrame)
                    {
                        bool suppressed = loco != null && Time.time < loco.jumpSuppressedUntil;
                        LogParry(suppressed
                            ? "<color=orange>地上押し (着地直後の抑止中 → 何も起きない)</color>"
                            : "地上押し → ジャンプ");
                    }
                    if (airborneTime > 0.12f) OnCushionLanding();
                    else if (airborneTime > 0f)
                        LogParry($"接地 (滞空 {airborneTime:F2}s < 0.12 → 判定なし)");
                    airborneTime = 0f;
                }
                // 追補 16→19 改訂: calm は「離陸直後 0.35 秒 (上昇の保護 = 理不尽なし)」と
                // 「パリー押下後」だけ。降下中は素通しにして揺れを再発達させる。
                // 従来の滞空ぜんぶ calm では、パリーなしでも着地がほぼ無傷で
                // 「パリーの意味がない」状態だった (ユーザー指摘)。
                bool jumpJustStarted = loco != null && Time.time - loco.LastJumpStartTime < 0.35f;
                if (jumpJustStarted)
                {
                    BeginFluidCalm(1.2f);
                    hotCalmUntil = Mathf.Max(hotCalmUntil, Time.time + 0.35f);
                }
                // 追補 22: 地上での大きな加減速中は自動 calm (歩き出しを速くした代償を吸収)。
                // 追補 23: バランス操作 (壺の回転) 中も同様に当てる (適用速度 1.8 の代償)。
                // パリーなし着地直後 (rampCalmBlockedUntil) は当てず、着地の掛け金を守る。
                // 追補 28 (2026-08-22): 加減速時のクランプを 1.3 → rampCalmClamp (0.7) に強化。
                // 1.3 では跳ね上がり高さ v^2/2g ≒ 8.6cm がフリーボード (~3cm) を大きく超え、
                // 歩き出すたびに盛大に溢れていた (「少し歩いただけで大量のこぼれ」の主因)。
                // 0.7 なら 2.5cm でリムを越えない。バランス操作中は従来の 1.3 のまま
                // (傾け操作のこぼれはゲーム性なので殺さない)。
                // 追補 29 (2026-08-22): **歩行中も常時 calm を当てる**。位置ベースの実測で、
                // 通常歩行だけで液が物理的に 25-43% 壺の外へ溢れていた (ゲージの分類値は
                // これを大幅に過小報告していた)。クランプ 1.0 で歩行 14m の保持 97.8% を確認。
                // 着地の掛け金 (rampCalmBlockedUntil) 中は当てないので、ジャンプ着地・
                // パリー失敗のこぼれはそのまま。傾け操作 (バランス 1.3) やよろけ・川も従来通り。
                // 追補 30 (2026-08-22 QA): 旋回 (その場含む) と走りが calm の対象外だった。
                // 実測: その場旋回 2 秒で ~13-30% 流出 / 走り 4 秒で 36% 流出。
                // 旋回は「移動扱い」に含め、走りは歩きより強いクランプを当てる。
                bool balanceMoving = rig != null && rig.BalanceMoving;
                bool turningNow = loco != null && Mathf.Abs(loco.TurnInputThisFrame) > 0.1f;
                bool movingOnGround = loco != null && (loco.IsMoving || turningNow);

                // 追補 37 (2026-08-22 バグ報告「マウスを急激に動かしてもポーションが全く
                // こぼれない。慣性が働いていない」):
                // バランスを **速く** 動かしている間は calm を一切掛けない。傾け操作中に
                // 一律クランプを当てていたため、壺を勢いよく振っても中身が容器に貼り付いた
                // まま動かず、慣性が完全に消えていた。
                // ゆっくりした微調整 (歩きながらの姿勢制御) には従来どおり calm が要るが、
                // **意図的に速く振ったらこぼれる** のがこのゲームの操作感なので、そこは殺さない。
                bool balanceFast = rig != null && rig.BalanceRate > balanceInertiaRate;
                if (balanceFast)
                {
                    // 直前まで掛かっていた calm を即座に解く (残っていると慣性が死ぬ)
                    if (fluid != null) fluid.maxSpeedInPot = -1f;
                    hotCalmUntil = 0f;
                }
                else if (loco != null && (loco.RampingHard || balanceMoving || movingOnGround) && !airborneNow
                    && Time.time >= rampCalmBlockedUntil)
                {
                    float clamp = loco.RampingHard ? rampCalmClamp
                               : balanceMoving ? balanceCalmClamp
                               : loco.IsRunning ? runCalmClamp : walkCalmClamp;
                    BeginFluidCalm(clamp);
                    hotCalmUntil = Mathf.Max(hotCalmUntil, Time.time + 0.45f);
                }

                // 着地したら「あちち」クリップを引きずらない: 残りを 0.15 秒で畳む
                // (連続バウンド中は HotFlightActive が続くので畳まれない)
                if (anim.CurrentOneShot == GoblinClip.HotJump
                    && loco != null && !loco.HotFlightActive
                    && cc != null && cc.isGrounded)
                    anim.FinishOneShotFast(0.15f);
                if (hotCalmUntil > 0f && Time.time >= hotCalmUntil)
                {
                    hotCalmUntil = 0f;
                    StartCoroutine(EndFluidCalm(0f));
                }
                if (actionPressed && cc != null && cc.isGrounded && !anim.OneShotActive)
                    BeginPutDown();
                break;

            case State.Falling:
                TryFallRecovery(kb);
                break;

            case State.PotDown:
                if (loco != null)
                {
                    loco.gentleAccel = false;   // 壺なしは即応のまま
                    loco.ConsumeHotJump();      // 壺なしは既存のジャンプクリップで飛ぶ
                }
                if (terrainTilt != null) terrainTilt.gentleMode = false;
                UpdateNoPotLocomotion();
                if (actionPressed && cc != null && cc.isGrounded && !anim.OneShotActive && PotInRange())
                    StartCoroutine(BeginPickUp());
                break;
        }
    }

    // ---- ツボおろし ----
    [Header("Speed")]
    [Tooltip("ツボおろしの再生速度倍率。")]
    // 2026-08-16: 1.4 -> 2.0 (「もっと速く」)。流体は BeginFluidCalm で鎮めるので破綻しない。
    public float putDownSpeed = 2.0f;
    [Tooltip("拾い上げの再生速度倍率。")]
    public float pickUpSpeed = 1.8f;

    void BeginPutDown()
    {
        Current = State.PuttingDown;
        if (loco != null) loco.movementLocked = true;
        BeginFluidCalm();   // 高速化してもこぼれ・噴き上げが出ないよう、下ろし中も流体を鎮める
        anim.PlayOneShot(GoblinClip.PotDown, reverse: false, drivePotToEnd: false,
            potEvent: () =>
            {
                // 手を離した瞬間: 壺を世界へ切り離す
                if (pot != null) pot.SetParent(null, true);
            },
            done: () =>
            {
                Current = State.PotDown;
                if (loco != null) loco.movementLocked = false;
                StartCoroutine(SettlePotToGround());
                StartCoroutine(EndFluidCalm(1.0f));
            },
            speed: putDownSpeed, easeOutFrames: 12f);
    }

    // ---- 拾い上げ (ツボおろしの逆再生) ----
    bool PotInRange()
    {
        if (pot == null) return false;
        Vector3 d = pot.position - transform.position;
        if (Mathf.Abs(d.y) > pickupHeightRange) return false;   // 落とした壺を上/下から拾わせない
        d.y = 0f;
        return d.magnitude < pickupRange;
    }

    IEnumerator BeginPickUp()
    {
        Current = State.PickingUp;
        if (loco != null) loco.movementLocked = true;

        // クリップは「壺が root の前方 0.80m」で焼いてあるので、そこへ体を合わせる。
        Vector3 potXZ = pot.position; potXZ.y = transform.position.y;
        Vector3 toPot = potXZ - transform.position; toPot.y = 0f;
        Quaternion targetRot = toPot.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(toPot.normalized, Vector3.up) : transform.rotation;
        Vector3 targetPos = potXZ - targetRot * Vector3.forward * pickupStandDistance;

        // 0.2 秒で位置合わせ (CharacterController は一旦切る)
        bool ccWas = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;
        Vector3 p0 = transform.position; Quaternion r0 = transform.rotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f)
        {
            transform.position = Vector3.Lerp(p0, targetPos, t);
            transform.rotation = Quaternion.Slerp(r0, targetRot, t);
            yield return null;
        }
        transform.position = targetPos; transform.rotation = targetRot;
        if (cc != null) cc.enabled = ccWas;

        // 終端イーズ 20 フレーム: 逆再生の終盤 (壺を頭上へ振り上げて止まる瞬間) を
        // ゆっくりにしないと、急停止の慣性で中身が上へ吹き出す (2026-08-16 バグ報告)。
        BeginFluidCalm();
        anim.PlayOneShot(GoblinClip.PotDown, reverse: true, drivePotToEnd: false,
            potEvent: () =>
            {
                // (逆再生で) 掴んだ瞬間: 壺を子へ戻す
                if (pot != null) pot.SetParent(transform, true);
            },
            done: () =>
            {
                Current = State.Carrying;
                if (loco != null) loco.movementLocked = false;
                anim.StopAll();   // 通常の運搬パイプラインへ返す
                StartCoroutine(EndFluidCalm(1.0f));
            },
            speed: pickUpSpeed, easeOutFrames: 30f);   // 30f = 逆再生の「胸から頭上へ振り上げる」区間全体を減速
    }

    // ---- 着地クッション (追補 15) ----
    // 実際の飛行 (連続滞空 0.12 秒超) から着地した瞬間に呼ばれる。
    void OnCushionLanding()
    {
        // マグマの再発射フレームはクッション対象外 (そのまま次の滞空へ)
        if (loco != null && loco.HotFlightActive)
        {
            LogParry("着地 (マグマ再発射 → 判定なし)");
            return;
        }

        // 惜しい遅押しが誤ジャンプに化けないよう、着地直後のジャンプを抑止
        if (loco != null) loco.jumpSuppressedUntil = Time.time + cushionJumpSuppress;

        bool parried = false;
        if (cushionPressed)
        {
            cushionPressed = false;
            float sincePress = Time.time - cushionPressTime;
            if (sincePress <= cushionJustWindow)
            {
                LogParry($"<color=yellow>着地: {sincePress:F2}s 前の押し → ジャスト!</color> (窓 {cushionJustWindow:F2})");
                DoCushion(just: true); parried = true;
            }
            else if (sincePress <= cushionWindow)
            {
                LogParry($"<color=cyan>着地: {sincePress:F2}s 前の押し → グッド</color> (窓 {cushionWindow:F2})");
                DoCushion(just: false); parried = true;
            }
            else
            {
                LogParry($"<color=orange>着地: {sincePress:F2}s 前の押し → 早すぎ</color> (窓 {cushionWindow:F2} + よろけ)");
                if (rig != null)
                    rig.NudgeBalance((Random.value < 0.5f ? -1f : 1f) * cushionFailNudge);
            }
        }
        else
        {
            LogParry($"着地: 押しなし (滞空 {airborneTime:F2}s) → 生着地");
        }
        // 追補 19: パリーなし (または失敗) の着地は、その瞬間に滞空 calm を解いて
        // 衝撃をそのまま受ける。従来は calm が着地後 0.6 秒残り、パリーなしでも
        // ほぼこぼれず「パリーの意味がない」状態だった (ユーザー指摘)。
        // 離陸〜滞空の保護 (追補 16) はそのままなので理不尽さは戻らない。
        // 追補 27: 小さな段差の踏み外し (滞空 < significantFallAirtime) は掛け金の対象外。
        // ジョルトも calm 解除もせず、通常歩行の連続として扱う (宣言部の注記を参照)。
        if (!parried && airborneTime < significantFallAirtime)
        {
            LogParry($"小落下 (滞空 {airborneTime:F2}s < {significantFallAirtime:F2}) → 掛け金なし");
            return;
        }
        if (!parried)
        {
            hotCalmUntil = 0f;
            StartCoroutine(EndFluidCalm(0f));
            rampCalmBlockedUntil = Time.time + 0.7f;   // 着地スロッシュが加速 calm で無効化されないように
            // 追補 26: パリーなし着地は「ドスン」を流体へ注入。静定した液体は着地だけでは
            // こぼれない (実測) ため、通常ジャンプにも掛け金を作る (ユーザー指定:
            // 「パリー成功の嬉しさがもっと欲しいため、通常ジャンプはもう少しこぼれていい」)。
            if (fluid != null)
                fluid.JoltPot((Vector3.up + transform.forward * 0.8f) * cushionMissJolt);
        }
    }

    // 壺内の calm は着地から 0.8〜1.0 秒で切れる設定だが、クッションのクリップは 1.05 秒ある
    // (伸び上がりを cushionRiseSpeed で半速にしているため)。つまり **伸び上がりの終盤が
    // 無防備**だった。クリップが終わるまで延長する。
    void ExtendCalmThroughHandover(bool just)
    {
        float hand = rig != null ? rig.potHandoverSeconds : 0.45f;
        float fade = anim != null ? anim.handoverFadeSeconds : 0.25f;
        BeginFluidCalm(just ? cushionJustCalm : cushionCalm);
        hotCalmUntil = Mathf.Max(hotCalmUntil, Time.time + Mathf.Max(hand, fade) + 0.2f);
    }

    void DoCushion(bool just)
    {
        // 走り着地 (水平速度 3 超) は深いスタンスのバリエーション
        var clip = (loco != null && loco.CurrentSpeed > 3f) ? GoblinClip.LandCushionDeep : GoblinClip.LandCushion;
        if (!anim.OneShotActive || anim.CurrentOneShot == GoblinClip.HotJump)
            // 沈み込みは素材どおりの速さで受け、**伸び上がりだけ** を遅くする。
            // 等速で再生すると壺が最大 0.33 m/s (深いほうは 0.39 m/s) で持ち上がり、
            // パリーが成功しているのに戻り際でこぼれていた (ユーザー報告)。
            // 折り返しは壺の速度が 0 になる点なので、そこで速度を切り替えても段差は出ない。
            anim.PlayOneShot(clip, reverse: false, drivePotToEnd: true, potEvent: null,
                             done: () => ExtendCalmThroughHandover(just),
                             slowFromFrame: clip.LowestPotFrame, slowSpeed: cushionRiseSpeed,
                             // 担ぎ姿勢からの **差分** として乗せる。終端で差分が 0 になるので、
                             // 「クリップの終端姿勢 → 担ぎ姿勢」の受け渡しそのものが無くなる
                             // (従来はここで手が 23cm・前腕が 32cm 動いていた)。
                             additive: cushionAdditive);
        // パリーは「自分で受けた」着地なので、素の着地反動は止める。クッションのクリップが
        // 吸収を見せる側なので、二重に沈むと腰が抜けて見える。
        if (rig != null) rig.SuppressLandRecoil(0.6f);
        // 滞空 calm (1.2) よりさらに強く絞って着地衝撃を吸収する
        BeginFluidCalm(just ? cushionJustCalm : cushionCalm);
        hotCalmUntil = Mathf.Max(hotCalmUntil, Time.time + (just ? 1.0f : 0.8f));
        // 追補 26: はみ出して落下中のポーションを口へ吸い戻す (パリーの嬉しさ演出も兼ねる)
        if (fluid != null) fluid.RecallSpill(just ? 0.7f : 0.5f, just ? 10f : 6f);
        StartCoroutine(GlowPulse(just ? 6.5f : 4.5f, 0.25f));
        // 追補 20: 足元リング衝撃波 (グッド = シアン / ジャスト = 金、HDR で Bloom が滲む)
        ParryRingFX.Spawn(transform.position,
            just ? new Color(6.0f, 4.4f, 1.1f, 1.0f) : new Color(1.0f, 4.4f, 6.0f, 1.0f));
        // ゲージの色も同色系でフラッシュ
        if (gaugeUI == null) gaugeUI = FindFirstObjectByType<PotionGaugeUI>();
        if (gaugeUI != null) gaugeUI.FlashParry(just);
        // 2026-08-25: ヒットストップ + スローモーション (ParrySlowMo) は
        // 「違和感がすごい」との判断で削除した。timeScale はもう触らない。
    }

    // 成功フィードバック: ポーションの発光を一瞬強める (Bloom が滲ませてくれる)
    IEnumerator GlowPulse(float peak, float duration)
    {
        if (glowPulsing) yield break;
        // 壺自身の FluidSurface を光らせる (FindFirst だと滝の表面を掴むことがある)
        var fs = fluid != null ? fluid.GetComponent<FluidSurface>() : null;
        if (fs == null) fs = FindFirstObjectByType<FluidSurface>();
        if (fs == null || fs.liquidMaterial == null) yield break;
        var m = fs.liquidMaterial;
        if (emissionBase < 0f) emissionBase = m.GetFloat("_EmissionStrength");
        glowPulsing = true;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
            m.SetFloat("_EmissionStrength", Mathf.Lerp(emissionBase, peak, k));
            yield return null;
        }
        m.SetFloat("_EmissionStrength", emissionBase);
        glowPulsing = false;
    }

    // ---- 転倒 ----
    void UpdateFallTrigger()
    {
        // 2026-08-24: 転倒は一時停止 (ユーザー指示)。崩れの見せ方を固めてから戻す。
        if (!fallEnabled) { staggerMaxTimer = 0f; return; }
        if (rig == null || anim.OneShotActive || fallCooldownTimer > 0f) { staggerMaxTimer = 0f; return; }
        bool grounded = cc == null || cc.isGrounded;
        if (grounded && rig.StaggerIntensity01 >= 0.999f) staggerMaxTimer += Time.deltaTime;
        else staggerMaxTimer = 0f;
        if (staggerMaxTimer >= fallAfterSeconds)
        {
            staggerMaxTimer = 0f;
            BeginFall();
        }
    }

    void BeginFall()
    {
        Current = State.Falling;
        fallCooldownTimer = fallCooldown;
        if (loco != null) loco.movementLocked = true;
        // 転倒クリップは「+X へ倒れる」形で焼いてある。リグの staggerLeanRight は
        // 実測合わせで leanSide<0 を「右」と呼んでいる (符号が直感と逆) ため、
        // 「壺が +X へ傾いている = StaggerLeanRightNow が false」のとき通常再生、
        // true のときミラー再生が正しい (2026-08-16 実測で確認)。
        bool mirror = rig != null && rig.StaggerLeanRightNow;
        fallMirror = mirror;
        fallRecovering = false;
        // 追補 19: 踏ん張り〜復帰の間は壺内 calm。従来は転倒クリップの激しい傾きが
        // そのまま流体に伝わり、「復帰できてもポーションがこぼれすぎ」だった。
        // 壺を手放す瞬間 (potEvent) に即解除するので、転倒完遂時は通常どおりぶちまける。
        BeginFluidCalm(1.0f);
        anim.PlayOneShot(GoblinClip.FallOver, reverse: false, drivePotToEnd: true,
            potEvent: () =>
            {
                if (pot != null) pot.SetParent(null, true);
                if (fluid != null) fluid.maxSpeedInPot = -1f;   // 手放したら calm 解除
                // **手を離した瞬間から倒し始める**。転倒クリップは drivePotToEnd で壺を
                // 最後まで直立のまま駆動するので、クリップが終わってから倒すと
                // 「直立で落ちきってから、おもむろに倒れる」二段モーションになる
                // (2026-08-22 ユーザー報告)。GoblinClipAnimator.PotExtraRotation に
                // 上乗せすることで、クリップの落下と転倒を同時に見せる。
                StartCoroutine(TipPotForSpill());
            },
            done: () =>
            {
                Current = State.PotDown;   // 壺は横の地面。拾い直すところから
                if (loco != null) loco.movementLocked = false;
                // 転倒はタイムで払わせる失敗なので、残量は全損にしない (宣言部の注記を参照)。
                // 拾い直しに掛かる時間そのものが主な代償。
                if (fluid != null && fallRecallSeconds > 0.001f)
                    fluid.RecallSpill(fallRecallSeconds, fallRecallStrength);
                // 倒すのは TipPotForSpill が引き続き担当。ここは高さを地面へ合わせるだけ。
                StartCoroutine(SettlePotToGround(spillTipLift));
            },
            speed: 1f, easeOutFrames: 0f, mirror: mirror);
    }

    // 転倒クリップの踏ん張りフェーズ中に反対方向キーが押されたら、現在フレームから
    // 逆再生して立ち姿へ戻り、運搬状態に復帰する (2026-08-16 「転ぶ寸前からは復帰
    // できるようにしたい」)。壺リリース (f22) を跨ぐ前だけ受け付ける。
    void TryFallRecovery(Keyboard kb)
    {
        if (fallRecovering || kb == null) return;
        if (!anim.OneShotActive || anim.CurrentOneShot != GoblinClip.FallOver) return;
        if (anim.OneShotFrame > fallRecoverFrames) return;
        // 通常再生 = +X (右) へ倒れる → 左キーで踏みとどまる。ミラー = 左へ → 右キー。
        bool counter = fallMirror ? kb.rightArrowKey.isPressed : kb.leftArrowKey.isPressed;
        if (!counter) return;
        fallRecovering = true;
        anim.ReverseOneShot(newDone: () =>
        {
            fallRecovering = false;
            Current = State.Carrying;
            if (loco != null) loco.movementLocked = false;
            // fallCooldownTimer は BeginFall で設定済み: 復帰直後の即再転倒は起きない
            // 転倒 calm (追補 19) は立ち直り 0.5 秒後に解除 (Carrying 側の共通処理が畳む)
            hotCalmUntil = Mathf.Max(hotCalmUntil, Time.time + 0.5f);
        }, speed: fallRecoverSpeed);
    }

    // ---- 壺なしロコモーション ----
    GoblinClip jumpClipInUse;   // 離陸時に選んだジャンプクリップ (空中で切り替えない)

    /// <summary>川に流されている間 true (RiverFlowController が設定)。おぼれもがきを再生する。</summary>
    [HideInInspector] public bool sweptByRiver;

    void UpdateNoPotLocomotion()
    {
        if (anim.OneShotActive) return;
        // 川に流されている間はおぼれもがき (2026-08-17)。接地判定より先に見る
        // (sweep 中は水面を Move されるので isGrounded が不定なため)。
        if (sweptByRiver)
        {
            jumpClipInUse = null;
            anim.SetLocomotion(GoblinClip.Drown, 0f);
            return;
        }
        // 壺なしで水に入った場合はジャンプポーズ固定を避け、歩きクリップでばたつかせる
        var swm = GetComponent<GoblinSwimmer>();
        if (swm != null && swm.InWater)
        {
            jumpClipInUse = null;
            anim.SetLocomotion(GoblinClip.NoPotWalk, 0f);
            return;
        }
        bool grounded = cc == null || cc.isGrounded;
        if (grounded) jumpClipInUse = null;   // 着地したら次のジャンプで選び直す
        if (!grounded)
        {
            // 離陸時の状態でジャンプの種類を決める (歩きジャンプ / 走りジャンプ)
            if (jumpClipInUse == null)
                jumpClipInUse = (loco != null && loco.IsRunning) ? GoblinClip.NoPotJumpRun : GoblinClip.NoPotJumpWalk;
            anim.SetLocomotion(jumpClipInUse, 0f);
        }
        else if (loco != null && loco.IsMoving)
        {
            if (loco.IsRunning) anim.SetLocomotion(GoblinClip.NoPotRun, runStride);
            else anim.SetLocomotion(GoblinClip.NoPotWalk, walkStride);
            anim.locoSpeed = loco.CurrentSpeed;
        }
        else
        {
            anim.SetLocomotion(GoblinClip.NoPotIdle, 0f);
        }
    }

    // ---- 置いた壺を実際の床の高さへ合わせる (坂や台の上で浮く/めり込むのを防ぐ) ----
    /// <summary><paramref name="lift"/> は横倒しの壺が地面へめり込まないよう底面から浮かせる量 (m)。</summary>
    IEnumerator SettlePotToGround(float lift = 0f)
    {
        if (pot == null) yield break;
        Vector3 origin = pot.position + Vector3.up * 0.5f;
        float targetY = pot.position.y;
        // トリガー (水ボリューム) の上に壺を「置く」ことはしない
        var hits = Physics.RaycastAll(origin, Vector3.down, 8f,
                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.transform == pot || h.collider.transform.IsChildOf(pot)) continue;
            if (h.distance < best) { best = h.distance; targetY = h.point.y; }
        }
        if (best == float.MaxValue) yield break;
        targetY += lift;
        // 自由落下相当の速度で下ろす (瞬間移動はテレポート扱いで流体が飛ぶ)
        while (Mathf.Abs(pot.position.y - targetY) > 0.005f)
        {
            float y = Mathf.MoveTowards(pot.position.y, targetY, 3.0f * Time.deltaTime);
            pot.position = new Vector3(pot.position.x, y, pot.position.z);
            yield return null;
        }
    }

    /// <summary>転倒で手放した壺を倒して中身を流し出す。宣言部の "Spill" を参照。</summary>
    IEnumerator TipPotForSpill()
    {
        if (pot == null || spillTipAngle <= 0f) yield break;
        // 倒す向きは **転倒した側と同じ** へ。右へ転んだら壺の口も右へ向く。
        // 符号に注意 (2026-08-22 に一度逆にして「右に転んだのに口が左」になった):
        // Unity の AngleAxis は +Z 軸まわりの正回転で +Y (壺の口) を -X (左) へ倒す。
        // したがって「口を右 (+X) へ倒す」には **-forward** まわりに回す必要がある。
        // fallMirror = true は左へ倒れるクリップなので、そのときだけ +forward。
        Vector3 axis = transform.forward * (fallMirror ? 1f : -1f);
        float dur = Mathf.Max(0.05f, spillTipSeconds);
        // 転倒クリップが壺を駆動している間は PotExtraRotation へ足す (クリップの落下と
        // 転倒が同時に見える)。クリップが終わったら壺は誰も駆動しないので、そのときの
        // 姿勢を基準に自分で回し切る。切り替えの瞬間に飛ばないよう、上乗せ分を外した
        // 姿勢 (= クリップが出していた姿勢) を基準として取り直す。
        bool direct = false;
        Quaternion baseRot = pot.rotation;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            if (pot == null) yield break;
            float ang = spillTipAngle * Mathf.SmoothStep(0f, 1f, t / dur);
            var tip = Quaternion.AngleAxis(ang, axis);
            if (!direct && anim != null && anim.OneShotActive)
            {
                anim.PotExtraRotation = tip;
            }
            else
            {
                if (!direct)
                {
                    direct = true;
                    if (anim != null)
                    {
                        baseRot = Quaternion.Inverse(anim.PotExtraRotation) * pot.rotation;
                        anim.PotExtraRotation = Quaternion.identity;
                    }
                    else baseRot = pot.rotation;
                }
                pot.rotation = tip * baseRot;
            }
            yield return null;
        }
        if (pot == null) yield break;
        var final = Quaternion.AngleAxis(spillTipAngle, axis);
        if (!direct && anim != null)
        {
            // まだクリップ駆動中に倒し切った。以降もクリップが姿勢を出し続けるので
            // 上乗せは維持する (クリップ終了時にその姿勢のまま置き去りになる)。
            anim.PotExtraRotation = final;
        }
        else pot.rotation = final * baseRot;
    }

    /// <summary>川に落ちたとき (RiverFlowController.BeginSweep) に呼ばれる。
    /// 壺を即座に手放して世界へ切り離し、壺なし状態にする。ツボおろしクリップは
    /// 再生しない (流されている最中なので)。壺の漂流は RiverFlowController が駆動する。</summary>
    public void ReleasePotForSweep()
    {
        if (Current != State.Carrying && Current != State.PuttingDown && Current != State.PickingUp
            && Current != State.Falling) return;
        StopAllCoroutines();
        anim.StopAll();
        // BeginPickUp の位置合わせ中は cc が一時無効。そのコルーチンを止めた場合に備えて戻す。
        if (cc != null && !cc.enabled) cc.enabled = true;
        if (pot != null && pot.parent == transform) pot.SetParent(null, true);
        if (loco != null) loco.movementLocked = false;   // 移動は sweep 側が locomotion 自体を無効化する
        // 漂流中の中身の暴れは抑える (漂流終了時に RiverFlowController が -1 へ戻す)
        if (fluid != null) fluid.maxSpeedInPot = 2.5f;
        if (rig != null) rig.ResetBalance();
        Current = State.PotDown;
        // 落水時に中身をぶちまけるのは RiverFlowController 側 (potCapsizeDeg)。
        // 漂流中の姿勢は UpdatePotDrift が毎フレーム上書きするので、ここで傾けても消える。
    }

    /// <summary>デバッグワープ用: 壺を即座に手元へ戻して運搬状態にする。</summary>
    public void ForceCarry()
    {
        StopAllCoroutines();
        anim.StopAll();
        if (pot != null && pot.parent != transform) pot.SetParent(transform, true);
        if (loco != null) loco.movementLocked = false;
        if (fluid != null) fluid.maxSpeedInPot = -1f;
        if (rig != null) rig.ResetBalance();   // 前の場所のチルト入力を持ち越さない
        Current = State.Carrying;
    }
}
