using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// ============================================================================================
// FluidCore -- Position Based Fluids solver driver (GPU).
//
// FLUID_DESIGN.md §2/§3/§6/§7/§8。
//
// 外力は Physics.gravity のみ。疑似重力 (-a_container) は存在しない (§2)。
// 容器の並進・回転による影響は、FluidBoundary が供給する「動く境界粒子」が
// 実際の運動量として伝える。これが唯一の伝達経路である。
// ============================================================================================
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(FluidBoundary))]
public class FluidCore : MonoBehaviour, IPotionVolumeSource
{
    [Header("Simulation")]
    public int particleCount = 16384;
    // 案A (2026-08-23): 滝のような「落ちて散るだけ」の流体は、非圧縮性 (PBF) を解く必要がない。
    // 弾道モードでは近傍グリッド構築 (6 ディスパッチ) とソルバ反復 (3xN)、法線・粘性・表面張力を
    // すべて省き、1 サブステップを **IntegrateAndBoundary + Finalize の 2 ディスパッチ** にする。
    // 位置は Finalize が PredictedPositions から確定し、速度は IntegrateAndBoundary が
    // 重力積分したものをそのまま使うので、この 2 つだけで閉じている (どのカーネルも
    // 近傍探索を使っていないことを確認済み)。安定性の制約も消えるのでサブステップは
    // CFL だけで決まる。**描画は FluidSurface のまま**なので、色や質感はポーションと同じ。
    // 壺には使わないこと (中身が非圧縮でなくなり、容器の中で潰れる)。
    [Tooltip("弾道モード。近傍相互作用を解かず、重力と地形衝突だけで落とす。滝など「落ちるだけ」の流体用。壺には使わない。")]
    public bool ballisticMode = false;
    [Tooltip("PBF の密度投影反復数 (§7)。")]
    [Range(1, 10)] public int solverIterations = 4;
    // Phase 12 実測: サブステップ数はソルバーの収束にも効く。適応 CFL だけに任せて
    // 3 まで落とすと、静止時の液面が 0.189 -> 0.287、平均速さが 0.005 -> 0.589 m/s に
    // 悪化した（＝落ち着かない）。下限を 6 に固定すると 10 と同等の品質を保ったまま
    // 静止時の物理コストが 17.1 -> 10.4 ms/frame になる。
    [Range(1, 12)] public int minSubSteps = 6;
    // 2026-08-22: 静定安定性はサブステップ刻み sdt = dt/sub で決まる (実測: 0.0037 以下で
    // 安定、0.0074 でシード波が成長)。dt をクランプする代わりにこの刻みを守るよう
    // サブステップ数を動的に足すことで、低 fps でもシミュ時間を実時間で進められる
    // (スローモーション解消)。壺 (spacing 0.036) は 0.0037。粒子間隔が大きい滝は
    // 余裕があるのでシーン側で大きめに設定してよい。
    [Tooltip("サブステップ刻みの上限 (秒)。静定安定性の要。小さいほど安定だが低 fps 時のサブステップ数が増える。")]
    public float stableSubstepDt = 0.0037f;
    // 上限が低いと CFL を満たせないフレームが出て、そこで流体が発散する。
    // 実測（急な往復+回転）: 必要 12 に対し上限 10 で、120 フレーム中 118 が CFL 違反、
    // 流体が速度クランプ 8m/s に張り付いて描画が崩れた。
    // 速度クランプ maxSpeed=8 のとき、dt=1/60 で必要なサブステップは
    // 8 * (1/60) / (0.4 * spacing) ≒ 12。上限はそれを上回っている必要がある。
    // 静かなときは適応 CFL が 6 まで落とすので、常時のコストは増えない。
    [Range(1, 32)] public int maxSubSteps = 20;
    [Tooltip("CFL に使う実測最大速度への安全率。実測値は 1 フレーム前のものなので、急加速に備えて余裕を持たせる。")]
    [Range(1f, 4f)] public float cflSpeedMargin = 1.6f;
    // 追補 38 (近傍グリッドのサブステップ間使い回し) は **撤去した** (2026-08-22 夕方)。
    // 実測: N=2 で Step avg 31.4/32.6ms -> 32.6/33.3ms と速くならず、MeasuredMaxSpeed が
    // 0.4 -> 1.2 (3倍) に跳ね、overflow 102 件。**効果ゼロで品質だけが落ちる**。
    // 1 サブステップの 23 ディスパッチは等価ではなく、効いているのは近傍ループを回す重い
    // カーネル (ComputeDensityLambda / ComputeDeltaP) だけで、グリッド再構築の 6 ディスパッチ
    // は測定限界以下だった。一方この最適化は初回実装でフレームをまたいで使い回し、GPU が
    // 範囲外のセルを読んで **エディタごとクラッシュ (GPU デバイスロスト)** を起こしている。
    // 「効果なし・危険あり」なので注意書きではなくパラメータごと削除する。
    // 近傍グリッドは毎サブステップ必ず再構築する (SubStep)。
    // 追補 32: 容器 (壺そのもの) の移動に許す 1 サブステップあたりの距離。粒子間隔の倍数。
    // 流体側の 0.4 と違って緩いのは、壺内の液体が容器と共動していて相対速度がほぼ 0 だから
    // (CFL が守るのは相対運動)。ここを流体と同じ厳しさにすると、走行中の壺の世界速度
    // (実測 7-12 m/s) だけでシミュ時間が実時間より遅れ、全体がスローモーションになる。
    [Tooltip("容器の移動に許す 1 サブステップあたりの距離 (粒子間隔の倍数)。小さすぎると走行中に全体がスローモーションになる。")]
    [Range(0.4f, 6f)] public float containerTravelBudget = 1.5f;
    [Tooltip("非常用: 1 サブステップでこの距離 (粒子間隔の倍数) を超えて動く場合だけシミュ時間を遅らせる。通常プレイでは発火しない。小さくするとスローモーションが再発する。")]
    [Range(0.5f, 6f)] public float emergencyTravelSpacing = 1.5f;
    [Tooltip("追補 33: 壺から出た液滴に calm (壺内の速度制限) を掛けない。OFF にすると、こぼれた液体が壺の近くにいる間だけゆっくり落ちる旧挙動に戻る。")]
    public bool escapedIgnoreCalm = true;

    [Header("Material")]
    [Range(1.5f, 3f)] public float kernelRadiusScale = 2f;
    [Tooltip("XSPH 粘性。水 < ポーション < シロップ (§8)。")]
    // 0.28 は「サブステップごとに適用」前提で合わせた値だった。ブレンド率を dt 比例に
    // したので、10 サブステップ時に同じ効きになる 2.8 が Phase 6 と同じ見え方になる。
    [Range(0f, 8f)] public float viscosity = 2.8f;
    [Tooltip("境界粘性 (§2 の補正項)。0 = 完全スリップ、大 = ノースリップ。容器の回転が中身に伝わるかを決める。")]
    [Range(0f, 12f)] public float boundaryViscosity = 0.55f;
    [Tooltip("粘性係数の基準時間刻み (s)。粘性のブレンド率 = 係数 * dt / これ。サブステップ数が変わっても実効粘性が変わらないようにするための基準。")]
    public float viscosityRefStep = 1f / 60f;
    [Tooltip("Akinci 凝集力 (§9)。")]
    public float cohesionStrength = 0.3f;
    [Tooltip("Akinci 曲率力 (§9)。表面を平滑化する主役。")]
    public float curvatureStrength = 0.02f;
    [Tooltip("人工圧力の強さ。基準 lambda に対する比。")]
    [Range(0f, 0.5f)] public float artificialPressureFraction = 0.02f;
    public float artificialPressureQ = 0.2f;
    [Tooltip("ソルバー緩和係数。理想格子での sum|gradC|^2 に対する比。")]
    [Range(0.001f, 4f)] public float relaxationFraction = 0.5f;
    [Tooltip("lambda の分母の下限。自由表面の粒子が過大な補正で射出されるのを防ぐ。")]
    [Range(0f, 1f)] public float minDenomFraction = 0.5f;
    [Tooltip("境界からの圧力押し戻しの強さ。1 = Akinci 標準。")]
    // Phase 7 実測で 1.6 を採用。1.0 -> 1.6 で壁の貫通 465 -> 309 個、
    // リム開口の暴れ 2.15 -> 0.58 m/s。2.0 まで上げると壁の斥力が強すぎて
    // リムの堰が高くなる（液面-堰 0.138 -> 0.168m）ので 1.6 が折り合い点。
    [Range(0f, 3f)] public float boundaryPressureScale = 1.6f;
    [Tooltip("位置補正の緩和係数 (SOR)。これが無いと補正が行き過ぎて毎サブステップでエネルギーが注入される。")]
    [Range(0.02f, 1f)] public float solverRelaxation = 0.12f;
    [Range(0.05f, 1f)] public float maxDeltaPPerSpacing = 0.25f;
    // 速度クランプ。跳ね上がる高さの上限を決める (v^2/2g)。
    // 8 m/s だと 3.3m も噴き上がって「発散」に見える。5 m/s なら 1.3m。
    // 壺の直径が 0.9m なので、この程度が運搬中の跳ねとして妥当。
    public float maxSpeed = 5f;
    // 2026-08-22: 壺外 (落下中の液滴など) の速度上限を分離。5 のままだと橋の上 (8m) からの
    // こぼれが本来 18 m/s のところ 5 m/s で落ち、スローモーションに見えていた。
    // 地面バンド (spacing*1.5 ≒ 5.4cm) のトンネリング防止: 10 m/s × sdt(≦0.0037) = 3.7cm < 5.4cm。
    [Tooltip("壺の外の液体 (落下中の液滴・水たまり) の速度上限 (m/s)。落下の見かけ速度を決める。")]
    public float maxSpeedFalling = 14f;   // 2026-08-22: 10 でもまだ遅い (自然落下は 8m で 18m/s)。地面バンド条件 14*0.0037=5.2cm < 5.4cm の上限まで引き上げ
    // 壺内 (容器基準ゲート内) 限定の速度クランプ。負なら maxSpeed と同じ。
    // おろし/拾い/熱い床ジャンプ中に GoblinPotActions が一時的に絞る (calm)。
    // 壺外の液滴・水たまりには適用しないので、こぼれがスローモーションにならない。
    [HideInInspector] public float maxSpeedInPot = -1f;

    // ---- 追補 26: パリー回収 & 着地ジョルト ----
    float recallUntil = -999f, recallStrengthValue;
    Vector3 joltDeltaV;
    int joltFrame = -1;
    /// <summary>パリー成功時: 壺の近くではみ出している粒子を seconds の間、口へ吸い戻す。</summary>
    [Tooltip("回収の届く範囲 (m)。壺の口からこの距離まで。")]
    public float recallRadius = 4.0f;
    // 2026-08-26: 0.6 では壺の 0.6m 下 = 地上 0.9m までしか届かず、落ちきったこぼれは
    // 対象外だった。2.2 なら地面まで届く。ただし **これで回収量が増えることは
    // 確認できていない** (下の注記を参照)。
    [Tooltip("回収の下限 (m)。壺の位置からこれだけ下までを拾う。地面の水たまりまで届かせるには壺の高さ (約 1.5m) より大きくすること。")]
    public float recallMinYDrop = 2.2f;

    // 2026-08-26 の調査メモ。**回収 (RecallSpill) は実質効いていない。**
    // 走りジャンプ/よろけジャンプで着地時に壺の外にある液に対し、戻る量は強さ (6/16/40) や
    // 時間 (0.05〜1.8 秒) を変えても差が出ず、**回収を切った回が一番戻った**ことすらある。
    // 戻っているのは自然落下で壺へ入り直した分。着地時点で外にある液はほぼ「脱出済み」
    // 判定になっており (実測: 空中 177 粒のうち 151 粒)、脱出済みは回収の対象外。
    // 地面の水たまりを回収中だけ拾い直す実装も試したが、目視でも液は戻らなかったので
    // 撤去した (シェーダの脱出・退避まわりは壊れやすいので、動かないものを残さない)。
    // ここを本当に動かすなら、脱出判定そのものの見直しが要る。
    // 2026-08-26: ジャンプ中にこぼれた液の猶予。**踏切で飛び出した分は着地の
    // パリーで拾い直せるべき**なのに、着地する前に地面へ着いて「水たまり」として
    // 凍結され、回収の対象から外れていた (滞空 0.7 秒に対し、壺の高さ 1.5m からの
    // 落下は 0.5 秒)。猶予中は地面に着いても凍結せず、Escaped のまま置いておく。
    float spillGraceUntil;
    /// <summary>この秒数のあいだ、こぼれた液を地面で凍結させない (拾い直せる状態で置く)。</summary>
    public void GrantSpillGrace(float seconds) { spillGraceUntil = Time.time + seconds; }
    /// <summary>猶予を打ち切る。パリー失敗・無押しの着地で呼ぶ = こぼれは確定する。</summary>
    public void EndSpillGrace() { spillGraceUntil = 0f; }
    float restoreUntil;
    [Tooltip("全回収を何秒かけて戻すか。一度に戻すと圧力が暴れて逆に噴き出す。")]
    public float restoreSeconds = 0.5f;
    [Range(0f, 1f)]
    [Tooltip("全回収で 1 フレームに戻す割合。")]
    public float restoreChancePerFrame = 0.15f;
    /// <summary>いま猶予中の (= このジャンプでこぼれた) 液を、次のフレームで壺へ戻す。
    /// ジャストパリーの「全回収」。吸い寄せ (RecallSpill) は最終残量を変えられなかったので、
    /// 物理ではなく明示的に戻す。</summary>
    public void RestoreSpilledToPot() { restoreUntil = Time.time + restoreSeconds; }

    /// <summary>診断用: 猶予が効いているか。</summary>
    public bool SpillGraceActive => Time.time < spillGraceUntil;

    /// <summary>診断用: いま回収が効いているか。</summary>
    public float RecallStrengthNow => Time.time < recallUntil ? recallStrengthValue : 0f;

    public void RecallSpill(float seconds, float strength)
    {
        recallUntil = Time.time + seconds;
        recallStrengthValue = strength;
    }
    /// <summary>パリーなし着地: 次のフレームで壺内の粒子に deltaV (m/s、world) を注入する
    /// (着地の跳ね返り + 前方サージ)。BindAll はサブステップ毎に呼ばれるため、消費フラグ
    /// ではなくフレーム番号一致で「そのフレームの全サブステップ」に適用する。</summary>
    public void JoltPot(Vector3 deltaV)
    {
        joltDeltaV = deltaV;
        joltFrame = Time.frameCount + 1;
    }

    [Header("Fill / region")]
    [Tooltip("容器の内容積に対する初期充填率。")]
    // 満タン。0.95 は「リムの直下まで」で、これ以上入れると静止時から溢れる。
    [Range(0.05f, 0.95f)] public float fillFraction = 0.95f;
    [Tooltip("開始時に容器を静止させたまま液面を釣り合わせておく秒数。0 で無効。種の格子が緩む分の初期こぼれを防ぐ。")]
    [Range(0f, 2f)] public float initialSettleSeconds = 0.7f;
    // ADDED 2026-08-17 (バグ報告「開始直後、何もしていないのにポーションが揺れる」):
    // PreSettle は静定した液面 (全粒子 < 0.25 m/s) を作るが、実行時ループへの引き継ぎで
    // わずかな残渣が増幅され、静止した容器の中で数秒〜数十秒の表面の泡立ちに育っていた
    // (実測: 容器ピーク速度 0.000 のまま流体だけが 5 m/s のクランプまで到達)。
    // 静定状態そのものは安定 (収束後は静かなまま) なので、シード直後の数シミュ秒だけ
    // 壺内クランプを絞って残渣を殺し、静定アトラクタへ確実に落とす。
    // 2026-08-22 改訂: 固定時間では波の山とタイミングが噛み合わず、解除後に波が育って
    // 開始しただけで 13-14% 流出していた。**適応型**にする: 実測最大速度が
    // startupCalmReleaseSpeed を下回る (= 波が本当に死んだ) までクランプを維持し、
    // startupCalmSimSeconds は保険の上限とする。クランプ 0.6 m/s は跳ね上がり高さ
    // v^2/2g ≒ 1.8cm < フリーボード ~3cm でリムを越えられない値。
    [Tooltip("シード後クランプの最大維持時間 (シミュレーション秒)。通常は下の解除条件が先に満ちる。")]
    public float startupCalmSimSeconds = 30f;
    [Tooltip("開始直後の壺内クランプ (m/s)。フリーボードを越えない速度にする。")]
    public float startupCalmClamp = 0.6f;
    [Tooltip("実測最大速度 (壺の中身) がこの値を下回ったらクランプを解除する。クランプ値より十分小さくすること。")]
    public float startupCalmReleaseSpeed = 0.45f;
    [Tooltip("容器 (壺) の速度がこの値 (m/s) を超えたら = プレイヤーが動き出したら、開始クランプを即解除する。歩行 1.5 m/s で確実に超え、静止時の微動では超えない値。")]
    public float startupCalmReleaseContainerSpeed = 0.8f;
    [Tooltip("XSPH ブレンド率の上限 (サブステップあたり)。0.9 で自励振動、0.55 では減衰不足で歩行スロッシュが暴れる。0.75 が本来のチューニングに近い。")]
    [Range(0.3f, 0.85f)] public float xsphBlendCap = 0.75f;
    float simTimeSinceSeed = 1e9f;
    bool startupCalmDone = true;
    [Tooltip("シミュレーション領域の余白 (m)。容器の周囲にこれだけ広げる。")]
    public float simPadding = 0.45f;
    [Tooltip("容器の下へ領域を伸ばす量 (m)。Box モードでのみ使う。壺モードでは領域の底は groundY に固定される。")]
    public float fallZoneBelow = 1.2f;
    [Tooltip("地面の World Y。Overflow した液体はここに着地する。")]
    public float groundY = 0f;
    [Tooltip("注ぎ出した液体が横へ広がる余地 (m)。壺の旋回半径にこれを足した範囲が領域になる。ここが不足すると液体が見えない壁に当たって板状に溜まる。")]
    // 地面の水たまりが見える範囲でもある。密度場が 1 軸 384 voxel を超えない上限が
    // 半径 1.77m 付近（voxel 9.4mm）なので、そこに合わせてある。
    // これ以上広げるには Brick Pool（OI-3）が要る。
    public float lateralSpread = 0.8f;
    [Tooltip("地面より下に確保する余白 (m)。地面 Collision が領域端と重ならないようにする。")]
    public float groundMargin = 0.12f;
    // 揺さぶりで跳ね上がった液体が飛ぶ高さ。ここが足りないと、液体が領域の天井に
    // 当たって平らに潰され、そのまま壺へ落ち戻る。実測: 0.18m のとき、
    // 激しく揺すって空中へ出た 5895 粒子のうち 99.97% が壺に戻り、
    // PotionVolume が 1.000 へ復帰していた（＝こぼれない）。
    [Tooltip("容器の上に確保する余白 (m)。跳ね上がった液体が天井に当たって落ち戻らない高さが必要。")]
    public float topMargin = 1.2f;
    // ADDED 2026-08-15 (バグ報告「上り勾配(ギミック2)の頂上付近で液体が急減し、画面全体が
    // かくかくに重くなる」): 領域の縦の広さは Initialise 時の容器高さから決まるため、
    // 容器が坂で 2m 登ると天井 (BoundsMax.y ≒ 3.37) が追いつかず、上昇する壺の床と
    // 動かない天井の間で液体が薄く圧縮されていた。こうなると (1) 全粒子が互いに近傍に
    // なって近傍探索が実質 O(n^2) 化しフレームが激重になり、(2) 圧壊した液体がリムから
    // 弾き出されて残量が急減する (実測: SafetyCorrection 発動が読み取りあたり
    // 66,013 → 297,641 → 548,740 へ爆発)。天井を容器の必要高さに追従させて防ぐ。
    [Tooltip("容器が登って天井に近づいたとき、領域の天井を引き上げる刻み (m)。0 で無効(旧挙動)。底は地面に固定のまま、天井だけが広がる。")]
    public float regionGrowStep = 0.5f;
    [Tooltip("Rim Opening 領域の高さ (m)。粒子間隔の 2〜3 倍程度。ここを通過した粒子だけが正常な Overflow として数えられる (§11)。")]
    public float rimOpeningHeight = 0.08f;
    [Tooltip("地面に留まった液体が Retired（回収不可能）になるまでの時間 (s)。0 で無効（永久に残る）。Mass は消えず RetiredMass へ移る (§16/§20)。")]
    public float groundLifetime = 10f;
    [Tooltip("壺のふちを越えた液体を壺へ戻さず、そのまま地面へ落とす。跳ね上がった液体が口へ落ち戻って残量が減らないのを防ぐ。")]
    public bool escapeAboveRim = true;
    [Tooltip("リム面からこの高さ(粒子間隔の倍数)を超えたら「ふちを越えた」とみなす。液面の盛り上がりを誤検出しない程度に取る。")]
    [Range(0.5f, 8f)] public float escapeMarginSpacings = 2f;
    // 2026-08-23: 壺が横倒しのとき、口から出た液滴が壺ローカルで「リムより上/底より下」の
    // どちらにも入らず脱出判定を素通りしていた。外形からこれだけ離れていれば、壁厚の中に
    // いる貫通粒子とは区別できるので、姿勢によらず脱出とする。
    [Tooltip("壺の外形からこれだけ (粒子間隔の倍数) 離れたら、壺の姿勢によらずこぼれた扱いにする。")]
    [Range(2f, 24f)] public float escapeFarSpacings = 8f;
    [Range(0f, 0.5f)] public float boundsRestitution = 0.02f;
    [Range(0f, 1f)] public float boundsFriction = 0.15f;

    [Header("Slope Collision (滝専用。未設定なら従来通りGroundYの平面のみ、壺の運搬物理には影響しない)")]
    [Tooltip("崖断面の高さサンプル配列(Z方向に等間隔、slopeZStart側→slopeZEnd側)。null/2要素未満なら無効(従来のGroundY平面のみ)。")]
    public float[] slopeProfileHeights;
    [Tooltip("slopeProfileHeights[0]に対応するワールドZ(池側、より手前)。")]
    public float slopeZStart;
    [Tooltip("slopeProfileHeights[末尾]に対応するワールドZ(水源側、より奥)。")]
    public float slopeZEnd;
    [Range(0f, 1f)] public float slopeRestitution = 0.35f;
    [Range(0f, 1f)] public float slopeFriction = 0.03f;

    [Header("Waterfall Recycle (滝専用。spawnBoxSizeが実質ゼロ(既定)なら従来通りRetiredParkへ待避するだけ、壺の運搬物理には影響しない)")]
    [Tooltip("Retired粒子の再スポーン範囲の最小コーナー。")]
    public Vector3 spawnBoxMin;
    [Tooltip("再スポーン範囲のサイズ。各成分が実質ゼロ(既定値)なら再スポーンせず、従来通りRetiredParkへ待避する。")]
    public Vector3 spawnBoxSize = Vector3.zero;
    [Tooltip("再スポーン直後の初速。")]
    public Vector3 spawnVelocity;
    // 2026-08-22: 従来は Retired の在庫を 1 フレームで全部戻していたため、壺の補充が
    // 「滝に入った瞬間に一気に回復する」挙動になっていた。ここを 1 未満にすると毎フレーム
    // 抽選で少しずつ戻るので、口から注がれて増えていくように見える。滝の水源は 1 のまま。
    [Tooltip("1 フレームに再スポーンしてよい Retired 粒子の割合 (0..1)。1 で従来どおり一括。壺の補充では PotionRefillZone が流量に応じて設定する。")]
    [Range(0f, 1f)] public float spawnChance = 1f;

    // ADDED 2026-08-15 (バグ報告「ギミックのブロックにこぼれたポーションがつかない。
    // 貫通して地面まで落ちている」): 流体の衝突相手は壺の境界粒子・地面平面 (groundY)・
    // 領域外周だけで、ステージの箱はシミュレーションに存在しなかった。GroundSurface 付きの
    // BoxCollider を集めてシェーダに渡し、こぼれた液体が上面に着いたらその場で水たまりに
    // する (Ground 集計に入り、groundLifetime で消える。地面と同じ扱い)。
    [Header("Solid obstacles")]
    [Tooltip("GroundSurface 付きの BoxCollider を流体の衝突対象にする。こぼれた液体がギミックの上に水たまりとして残る。")]
    public bool collideWithGroundSurfaces = true;
    // 2026-08-23: こぼれた液体がコースのアセット (PathLog / PathRock / Seg_* など) を
    // すり抜けて下まで落ちていた。原因は衝突対象が「GroundSurface 付き かつ BoxCollider」
    // だけだったこと。道のアセットはメッシュコライダで GroundSurface も無いので対象外だった。
    // ここではシーン中のコライダを集め、**メッシュのローカル境界から有向ボックス (OBB)** を
    // 作って同じカーネルへ渡す。斜めに置かれた丸太でもワールド AABB より遥かに実形状に近い。
    // 粒子 x 箱の総当たりなので、毎フレーム **シミュレーション領域に近い箱だけ** を
    // maxSolidBoxes 個まで選んで送る (シーン全体では 296 個ある)。
    [Tooltip("道を構成するアセットとも衝突させる。こぼれた液体が道をすり抜けて下へ落ちるのを防ぐ。")]
    public bool collideWithCourseColliders = true;
    [Tooltip("衝突対象にするレイヤー。")]
    public LayerMask courseColliderMask = ~0;
    [Tooltip("1 フレームに GPU へ渡す箱の上限。粒子 x 箱の総当たりなので増やすほど重い。バッファ確保に使うため、変更はプレイ開始時に反映される。")]
    [Range(4, 128)] public int maxSolidBoxes = 32;
    [Tooltip("これより大きいコライダは箱で近似すると実形状から離れすぎるので対象外にする (m)。")]
    public float maxCourseBoxSize = 8f;

    [Header("Refs")]
    public ComputeShader fluidCompute;

    [Tooltip("テストハーネスが明示的な dt で駆動できるようにするためのスイッチ。")]
    public bool autoStep = true;
    // 2026-08-22: N フレームに 1 回だけ Step し、間の dt は積算して渡す (シム時間は実時間の
    // まま)。このシムのコストは粒子数よりディスパッチ固定費が支配的なため、Step 頻度を
    // 落とすとほぼ比例して軽くなる。遠くの滝など「毎フレーム更新する必要のない」コアを
    // FluidSimLOD が距離に応じて間引くのに使う。1 = 毎フレーム (従来)。
    [Tooltip("N フレームに 1 回だけシミュレーションを進める (dt は積算)。遠景の流体の負荷削減用。")]
    [Range(1, 6)] public int stepEveryNFrames = 1;
    float pendingDt;
    int stepFrameCounter;
    // §16 は非同期リードバックを指定しており、実装もしてある（下の false 経路）。
    // ただし **非同期にすると Play 中に FluidCore を無効化→有効化しただけで
    // エディタが固まる**（最小再現で確認）。保留中の読み戻しとバッファ解放の
    // 組み合わせが原因と見ているが、まだ特定できていない。
    // エディタが固まる状態は出荷できないので、既定は同期に戻してある。
    // 非同期の効果自体は実測済み（Step() の CPU コスト 13.2ms -> 0.12ms）なので、
    // 原因を特定したら既定を false に戻す。OPEN_ISSUES.md の OI-4 を参照。
    [Tooltip("領域カウンタを同期読み戻しする。false にすると CPU が GPU を待たなくなるが、現在は再初期化でエディタが固まる不具合がある (OI-4)。")]
    public bool synchronousReadback = true;
    // 2026-08-21 ディスパッチ対策②: 分類の同期読み戻しは「それまでに積んだ GPU 仕事全部の
    // 完了待ち」なので、毎フレーム行うと CPU メインスレッドが GPU と完全直列化する
    // (実測: Step() の CPU 時間 ≒ 自分の GPU 時間)。観測 (ゲージ/統計) 用途なので間引く。
    // CFL 用の実測最大速度も古くなるが、cflSpeedMargin (1.6) が数フレームの遅れを見込む。
    [Tooltip("領域分類 (残量ゲージ・統計の集計) を何フレームに 1 回行うか。1 = 毎フレーム (従来)。増やすとメインスレッドの GPU 完了待ちが減る。")]
    [Range(1, 10)] public int classifyInterval = 3;
    int classifyCountdown;

    // ---- public state ----
    public bool IsReady => positions != null;
    public GraphicsBuffer PositionsBuffer => positions;
    /// <summary>粒子の状態 (0=壺のもの / 1=消滅 / 2=地面の水たまり / 3=こぼれて落下中)。
    /// 追補 36: 描画側がこぼれた液体を実体積に近い大きさで描くのに使う。</summary>
    public GraphicsBuffer RetiredFlagsBuffer => retiredFlags;
    public GraphicsBuffer VelocitiesBuffer => velocities;
    public GraphicsBuffer DensitiesBuffer => densities;
    public GraphicsBuffer BoundaryPositionsBuffer => boundaryPositions;
    public int FluidCount => fluidCount;
    public int BoundaryCount => boundaryCount;
    public float ParticleSpacing => spacing;
    public float KernelRadius => kernelRadius;
    public float RestDensity => restDensity;
    public float ParticleVolume => particleVolume;
    public float RefSumGradSq => refSumGradSq;
    public int LastSubStepCount { get; private set; }
    /// <summary>CFL が本当に必要としたサブステップ数（クランプ前）。
    /// これが LastSubStepCount より大きいフレームは CFL を満たせていない = 発散しうる。</summary>
    public int LastRequiredSubSteps { get; private set; }
    /// <summary>CFL を満たせなかったフレームの累計。0 でなければ発散の原因になる。</summary>
    public int CflLimitedFrames { get; private set; }
    /// <summary>観測開始からの必要サブステップ数のピーク。</summary>
    public int PeakRequiredSubSteps { get; private set; }
    /// <summary>観測開始からの容器速度のピーク (m/s)。</summary>
    public float PeakContainerSpeed { get; private set; }
    /// <summary>観測開始からの流体最大速さのピーク (m/s)。</summary>
    public float PeakFluidSpeed { get; private set; }
    /// <summary>CFL で解けない分を剛体搬送したフレームの累計。</summary>
    public int RigidCarryFrames { get; private set; }
    /// <summary>直近フレームで剛体搬送した割合 (0..1)。</summary>
    public float LastCarryFraction { get; private set; }
    public void ResetPeaks() { PeakRequiredSubSteps = 0; PeakContainerSpeed = 0f; PeakFluidSpeed = 0f; CflLimitedFrames = 0; RigidCarryFrames = 0; }
    /// <summary>直近フレームで境界が進んだ距離 (m)。回転は旋回半径を掛けて距離に直したもの。</summary>
    public float LastBoundaryTravel { get; private set; }
    /// <summary>直近フレームで実測した流体の最大速さ (m/s)。CFL のサブステップ数はこれで決まる。</summary>
    public float MeasuredMaxSpeed { get; private set; }
    /// <summary>直近フレームの Step() に掛かった実時間 (ms)。この流体単体のコスト。</summary>
    public float LastStepMs { get; private set; }
    /// <summary>Step() の平均コスト (ms)。ResetStepCost からの平均。</summary>
    public float AvgStepMs => stepMsCount > 0 ? stepMsAcc / stepMsCount : 0f;
    public int StepCostSamples => stepMsCount;
    public void ResetStepCost() { stepMsAcc = 0f; stepMsCount = 0; }
    readonly System.Diagnostics.Stopwatch stepWatch = new System.Diagnostics.Stopwatch();
    float stepMsAcc; int stepMsCount;

    // ---- GPU 安全装置 (2026-08-22 夕方) ----
    // 2026-08-22 に **同じ日に 2 回** GPU デバイスロストでエディタごと落ちている。
    // クラッシュのスタックはいずれも
    //   GfxDeviceD3D12::GetComputeBufferData -> FlushCommandList -> D3D12Fence::Wait
    //   -> CheckDeviceStatus (device removed)
    // で、**同期リードバックの GPU 待ち中にドライバがデバイスを落としている**。
    // 引き金は毎回「1 フレームの GPU 仕事が異常に膨らんだ状態を投げ続けたこと」で、
    // 直前の Step は 290ms/回 まで悪化していた (通常は 30ms 前後)。
    // コンソールに "Ran out of Graphics Ring Buffer space" も出ており、ディスパッチ数が
    // ドライバの提出上限に迫っていることも分かっている。
    //
    // したがって **異常な負荷は投げる前に自分で止める**。Step の実測時間が
    // watchdogStepMs を watchdogFrames 回連続で超えたら、サブステップ数を
    // watchdogSafeSubSteps に強制的に絞る。絞ると実時間より進みが遅れる (スローモーション)
    // が、**デバイスロストよりはるかにましな失敗の仕方**であり、こぼれ判定などの
    // ゲーム挙動は壊れない。一度発動したらそのまま保持する (振動させない)。
    //
    // **enabled を落として止める実装にしてはいけない**: OnDisable が全 GPU バッファを
    // Release するため、プレイ中にトグルするとエディタがフリーズする (既知の不具合 OI-4)。
    [Header("GPU 安全装置")]
    [Tooltip("Step の実測時間がこれを超えたフレームが続いたら、サブステップ数を強制的に絞る。0 以下で無効。")]
    public float watchdogStepMs = 120f;
    // 5 にしてある理由 (2026-08-22 実測): 走行中に単発で 126ms を記録した。MCP の
    // execute_code は 1 回 0.1-0.3 秒のヒッチを起こすので、計測中は孤立したスパイクが出る。
    // 病的な状態 (実測 290ms) は何十フレームも続くため、連続回数を増やしても取り逃がさない。
    [Tooltip("何フレーム連続で超えたら発動するか。単発のヒッチで誤発動しないよう余裕を持たせる。")]
    [Range(1, 30)] public int watchdogFrames = 5;
    [Tooltip("発動後に許すサブステップ数の上限。")]
    [Range(1, 20)] public int watchdogSafeSubSteps = 4;
    /// <summary>ウォッチドッグが発動しているか。発動中はサブステップが watchdogSafeSubSteps に制限される。</summary>
    public bool WatchdogTripped { get; private set; }
    /// <summary>ウォッチドッグが発動した時点の Step 実測時間 (ms)。</summary>
    public float WatchdogTripMs { get; private set; }
    int watchdogHits;
    /// <summary>ウォッチドッグを解除する。原因を直したうえで呼ぶこと。</summary>
    public void ResetWatchdog() { WatchdogTripped = false; watchdogHits = 0; WatchdogTripMs = 0f; }
    /// <summary>直近フレームで **シミュレーションが消費した時間** (s)。</summary>
    public float LastSimDt { get; private set; }
    /// <summary>直近フレームの実経過時間 (s)。LastSimDt がこれより小さいフレームは
    /// シミュレーションが実時間より遅れて進む = 見た目がスローモーションになる。</summary>
    public float LastWallDt { get; private set; }
    /// <summary>シミュレーション時間 / 実時間 の累積比 (1 未満 = スローモーション)。</summary>
    public float SimTimeRatio => accWallDt > 1e-4f ? accSimDt / accWallDt : 1f;
    float accSimDt, accWallDt;
    public void ResetSimRatio() { accSimDt = 0f; accWallDt = 0f; }
    /// <summary>SafetyCorrection の発動粒子数（直近の読み取り時点）。常態化していたら壁の扱いが破綻しているサイン (§10)。</summary>
    public int SafetyCorrectionCount { get; private set; }
    public int SafetyConsecutiveFrames { get; private set; }
    public int SeededParticles { get; private set; }
    // §14/§16: Overflow は「観測」の結果であり、独立に書ける変数ではない。
    public int InsideCount { get; private set; }
    public int RimCount { get; private set; }
    public int AirborneCount { get; private set; }
    public int GroundCount { get; private set; }
    /// <summary>ゲーム世界から取り除かれた粒子数 (§16 RetiredMass)。</summary>
    public int RetiredCount { get; private set; }
    /// <summary>Rim Opening を通って外へ出た粒子の累計（正常な Overflow）。</summary>
    public int OverflowEvents { get; private set; }
    /// <summary>Rim を通らずに外へ出た粒子の累計（壁抜け/底抜け = 異常）。</summary>
    public int PenetrationEvents { get; private set; }
    /// <summary>リムを越えてもう壺へ戻らない、落下中の液体の粒子数 (§16)。
    /// Airborne の内数。地面に着けば Ground へ移る。</summary>
    public int EscapedCount { get; private set; }
    /// <summary>まだ壺のものである液体の粒子数。跳ね上がって空中にいるだけの分を含む。</summary>
    // 2026-08-23 バグ報告「壺が完全に倒れているのに残量が 1 秒 1% ずつしか減らない」。
    // 空中の液体を「まだ壺のもの」として数えてよいのは、**壺が受け止められる姿勢のとき**だけ。
    // 倒れた壺の口から流れ出て落ちている液滴まで残量に数えていたため、それが着地して
    // Ground になるまでゲージがだらだら減り続けていた (実測: 倒れた後 air が 2400-4900 残り、
    // escaped は 0。倒れきると脱出判定の入口 (リムより上/底より下) にどちらも掛からなくなる)。
    // 傾きで判定するのは、壺を手放したかどうかを FluidCore が知らないため。倒れていれば
    // 手放していようが担いでいようが回収できない、という物理的に正しい基準でもある。
    [Tooltip("空中の液体を残量に数える上限の傾き (度)。これを超えて傾いた壺は空中の液体を回収できないとみなす。")]
    [Range(10f, 90f)] public float recoverTiltLimitDeg = 60f;
    /// <summary>壺が空中の液体を受け止められる姿勢か。</summary>
    public bool CanRecoverAirborne => boundary == null || boundary.Container == null
        || Vector3.Angle(boundary.Container.up, Vector3.up) <= recoverTiltLimitDeg;
    public int RecoverableCount => InsideCount + RimCount
        + (CanRecoverAirborne ? (AirborneCount - EscapedCount) : 0);

    // ---- Fluid Mass (§16) ----
    // 粒子は全て同じ質量なので Mass = 個数 x ParticleMass。
    // どれも「観測から導かれる量」であり、独立に書ける変数ではない。
    public float ParticleMassValue => particleVolume * restDensity;

    // PotMass は「まだ壺のものである液体」。壺の内側にある分だけでなく、
    // **跳ね上がって空中にいるだけの分も含む**。
    //
    // 以前は InsideCount（壺の内側の幾何判定）だけだった。そのため揺れやジャンプで
    // 液面がリムより上へ持ち上がるたびにゲージが大きく落ち込み、液体が戻ると回復する、
    // という挙動になっていた。実測（ジャンプ）: 実際に失われたのは 3.3% の時点で
    // ゲージは 0.998 -> 0.598 まで落ちていた。「こぼれた量と残量がリンクしていない」の正体。
    //
    // 失われたかどうかを決めるのは幾何ではなく **Escaped 判定**（リムの外へ出たか）なので、
    // 残量もそれに合わせる。こうすると、ゲージが減る量 = 実際にこぼれた量になる。
    public float PotMass => RecoverableCount * ParticleMassValue;
    /// <summary>こぼれて落下中の液体 (§16)。地面に着くまでの分。</summary>
    public float AirborneMass => EscapedCount * ParticleMassValue;
    public float GroundMass => GroundCount * ParticleMassValue;
    public float RetiredMass => RetiredCount * ParticleMassValue;
    public float InitialTotalMass => fluidCount * ParticleMassValue;
    public float TotalMass => PotMass + AirborneMass + GroundMass + RetiredMass;
    /// <summary>収支誤差。分類漏れがあると 0 にならない (§16 の Debug 検証)。</summary>
    public int MassBalanceError => fluidCount - (InsideCount + RimCount + AirborneCount + GroundCount + RetiredCount);

    /// <summary>§17: PotionVolume (0..1) = PotMass / InitialTotalMass。
    /// 経路は Fluid -> PotMass -> PotionVolume の一方向だけ。逆方向は存在しない。</summary>
    public float FillFraction01 => InitialTotalMass > 0f ? Mathf.Clamp01(PotMass / InitialTotalMass) : 0f;
    public FluidBoundary Boundary => boundary;

    /// <summary>**壺の** 流体を返す。シーンには滝 (FluidBoundary.Mode.Box) もあるので、
    /// `FindFirstObjectByType&lt;FluidCore&gt;()` は滝を掴むことがある。
    /// 2026-08-22 にゲージが滝の残量を表示する不具合、2026-08-26 に計測が丸ごと滝を
    /// 読んでいた件と、同じ取り違えを 2 度やっている。判断をここに一本化する。
    ///
    /// 壺は PotProfile 境界なのでそれを優先し、見つからなければ最初のものを返す。</summary>
    public static FluidCore FindPotFluid()
    {
        FluidCore fallback = null;
        foreach (var f in FindObjectsByType<FluidCore>(FindObjectsSortMode.None))
        {
            var b = f.GetComponent<FluidBoundary>();
            if (b != null && b.mode == FluidBoundary.Mode.PotProfile) return f;
            if (fallback == null) fallback = f;
        }
        return fallback;
    }
    /// <summary>流体が存在しうる World 空間の領域。容器に追従する。</summary>
    public Bounds SimBounds => new Bounds(regionCenter, regionSize);

    FluidBoundary boundary;
    GraphicsBuffer positions, predicted, velocities, deltaP, normals, safety;
    GraphicsBuffer densities, lambdas;
    GraphicsBuffer boundaryLocal, boundaryPositions, boundaryVelocities, boundaryVolumes;
    GraphicsBuffer sortPositions, cellCounts, cellStart, cellCursor, blockSums, sortedIndices;
    GraphicsBuffer potProfile, potOuterProfile, safetyCounters;
    GraphicsBuffer slopeProfileBuffer;
    GraphicsBuffer solidBoxW2L, solidBoxL2W, solidBoxHalf;
    int[] solidActive;   // GPU へ送っている箱が solidCandidates の何番か (デバッグ用)
    Matrix4x4[] solidW2LArr, solidL2WArr;
    Vector4[] solidHalfArr;
    int solidBoxCount;
    // §16「非同期リードバック」。同期 GetData は CPU が GPU の完了を待つので、
    // 毎フレーム丸ごとパイプラインが止まる。ここで欲しいのは統計値だけで、
    // 1 フレーム遅れても困らない。リング状に持って、書き込み中のバッファを
    // 読み戻さないようにする。
    GraphicsBuffer[] regionCountersRing;
    int regionRingIndex;
    GraphicsBuffer regionFlags, ages, retiredFlags;
    // [0]Inside [1]Rim [2]Airborne [3]Ground [4]Retired [5]Overflow [6]Penetration
    // [7]maxSpeed*1000 [8]Escaped（落下中でもう戻らない分）
    static readonly uint[] ZeroCounters = new uint[10];
    uint[] safetyRead = new uint[4];
    uint[] regionRead = new uint[10];

    int fluidCount, boundaryCount, totalCount;
    float spacing, kernelRadius, restDensity, particleVolume;
    float relaxationEps, artificialPressure, refSumGradSq;
    /// <summary>容器の旋回半径 (m)。回転が境界粒子に与える速度 = |omega| * これ。</summary>
    float containerSwingRadius = 1f;

    bool pendingSeed;
    Vector3 regionCenter, regionSize;
    float regionOffsetY;
    // 壺モードでの領域中心の Y。地面を必ず含むよう world 固定にする。
    float regionAnchorY;
    bool regionYAnchored;
    Vector3Int gridSize;
    Vector3 gridOrigin;
    float cellSize;
    int cellTotal, blockCount;

    // 2026-08-21 ディスパッチ対策③: UpdateBoundary+Integrate / ClearCellCounts+BuildSortPositions /
    // ComputeVelocity+ComputeNormals をそれぞれ 1 カーネルに結合 (計算内容は不変)。
    int kIntegrateBoundary, kClearBuildSort, kCount, kScanLocal, kScanBlocks, kScanAdd, kScatter;
    int kDensityLambda, kDeltaP, kApplyDeltaP, kVelNormals, kViscTension, kFinalize, kClassify;
    int kTeleport, kSolidBoxCollide;

    const int Threads = 256;
    const int ScanBlock = 256;
    // ADDED 2026-08-17 (バグ報告「橋の上でスタート直後、何もしなくてもポーションが揺れ続ける」):
    // ForestStage の開始地点は橋の上 (容器 y≒3.8) で、領域の底は地面 (groundY=0) 固定のため
    // 領域が縦長になり、旧上限 262144 ではセルが kernelRadius (0.0720) より粗い 0.0756m に
    // 粗大化していた。セルが核半径を超えた状態ではソルバが静定せず、静止した容器の中で
    // 液面が沸騰し続ける (対照実験: 同じジョルトを与えても、粗大化なしなら 35 秒で静定、
    // 粗大化ありでは無限に対流が持続。SafetyCorrection も毎秒 3 万件発動していた)。
    // 上限を 16 倍に上げて、実用範囲 (壺が y=10 程度まで登っても ~50 万セル) では
    // 粗大化が起きないようにする。ScanBlockSums は BlockCount を単一スレッドの逐次
    // ループで走査するので、セル数が増えても正しさは保たれる (バッファも blockCount で確保)。
    const int MaxCells = 4194304;

    void OnEnable() { Initialise(); }
    void OnDisable() { Release(); }

    void OnDestroy()
    {
        if (regionCountersRing != null)
        {
            foreach (var b in regionCountersRing) b?.Release();
            regionCountersRing = null;
        }
    }

    void Initialise()
    {
        if (positions != null) return;
        var initWatch = System.Diagnostics.Stopwatch.StartNew();   // 起動時間の内訳計測 (2026-08-23)
        if (fluidCompute == null)
        {
            Debug.LogError("FluidCore: fluidCompute (Assets/Shaders/Fluid/FluidCore.compute) が未割り当てです。", this);
            enabled = false;
            return;
        }
        boundary = GetComponent<FluidBoundary>();

        kClearBuildSort = fluidCompute.FindKernel("ClearAndBuildSort");
        kCount = fluidCompute.FindKernel("CountParticlesPerCell");
        kScanLocal = fluidCompute.FindKernel("ScanLocal");
        kScanBlocks = fluidCompute.FindKernel("ScanBlockSums");
        kScanAdd = fluidCompute.FindKernel("ScanAddOffsets");
        kScatter = fluidCompute.FindKernel("ScatterParticles");
        kIntegrateBoundary = fluidCompute.FindKernel("IntegrateAndBoundary");
        kDensityLambda = fluidCompute.FindKernel("ComputeDensityLambda");
        kDeltaP = fluidCompute.FindKernel("ComputeDeltaP");
        kApplyDeltaP = fluidCompute.FindKernel("ApplyDeltaP");
        kVelNormals = fluidCompute.FindKernel("VelocityAndNormals");
        kViscTension = fluidCompute.FindKernel("ApplyViscosityTension");
        kFinalize = fluidCompute.FindKernel("Finalize");
        kClassify = fluidCompute.FindKernel("ClassifyRegions");
        kTeleport = fluidCompute.FindKernel("TeleportFluid");
        kSolidBoxCollide = fluidCompute.FindKernel("SolidBoxCollide");

        fluidCount = Mathf.Max(Threads, particleCount);
        double tKernels = initWatch.Elapsed.TotalMilliseconds;
        ComputeScales();          double tScales = initWatch.Elapsed.TotalMilliseconds;
        BuildBoundaryBuffers();   double tBoundary = initWatch.Elapsed.TotalMilliseconds;
        AllocateBuffers();        double tAlloc = initWatch.Elapsed.TotalMilliseconds;
        GatherSolidBoxes();       double tBoxes = initWatch.Elapsed.TotalMilliseconds;
        SeedFluid();              double tSeed = initWatch.Elapsed.TotalMilliseconds;
        BuildGrid();              double tGrid = initWatch.Elapsed.TotalMilliseconds;
        // 起動時間の内訳 (2026-08-23)。ComputeScales の中で FluidBoundary.Build が走る。
        CarryStartupProfile.AddDuration($"{name}: カーネル取得", tKernels);
        CarryStartupProfile.AddDuration($"{name}: ComputeScales+境界生成", tScales - tKernels);
        CarryStartupProfile.AddDuration($"{name}: 境界バッファ転送", tBoundary - tScales);
        CarryStartupProfile.AddDuration($"{name}: GPUバッファ確保", tAlloc - tBoundary);
        CarryStartupProfile.AddDuration($"{name}: SolidBox収集", tBoxes - tAlloc);
        CarryStartupProfile.AddDuration($"{name}: 粒子シード", tSeed - tBoxes);
        CarryStartupProfile.AddDuration($"{name}: グリッド構築", tGrid - tSeed);
        CarryStartupProfile.AddDuration($"{name}: Initialise 合計", tGrid);
        // 実際の配置は最初の Step まで待つ。
        // OnEnable の時点では容器がまだシリアライズされた位置にあり、
        // ゴブリンのリグが LateUpdate で手の位置へ動かす前なので、
        // ここで配置すると液体が壺から 0.4m ずれた場所に生まれ、
        // その大半が「壺の外」と判定されて即こぼれる
        // （実測: 起動しただけで PotionVolume が 0.44 まで落ちた）。
        pendingSeed = true;
    }

    // 粒子間隔は「入れたい体積を何個で割るか」から。静止密度はその間隔での理想的な
    // 最密充填格子の密度そのもの（推測値ではなく計算値）。
    void ComputeScales()
    {
        // 内容積を知るために、まず暫定間隔でプロファイルだけ作らせる。
        // **psi は計算させない** (computeVolumes: false)。直後の本番ビルドで作り直されるので
        // 完全に捨てられる計算だが、滝のように箱が大きいと 0.05m 間隔で 150 万点になり、
        // その psi 計算だけで 6.5 秒かかっていた (2026-08-23 実測、起動時間の 55%)。
        if (boundary.LocalPositions == null) boundary.Build(0.05f, 0.1f, computeVolumes: false);

        float fluidVolume = boundary.InteriorVolumeWorld * fillFraction;
        particleVolume = fluidVolume / fluidCount;
        spacing = Mathf.Pow(particleVolume * Mathf.Sqrt(2f), 1f / 3f);
        kernelRadius = spacing * kernelRadiusScale;
        restDensity = particleVolume * IdealLatticeKernelSum(spacing, kernelRadius);
        refSumGradSq = IdealLatticeGradSq(spacing, kernelRadius, particleVolume, restDensity);
        relaxationEps = Mathf.Max(1e-6f, relaxationFraction * refSumGradSq);
        artificialPressure = artificialPressureFraction * (0.1f / Mathf.Max(1e-9f, refSumGradSq));

        // 本番の間隔で境界を作り直す。ここは弾道モードでも psi を計算させること:
        // 使わなくても BuildBoundaryBuffers が Volumes を GPU へ転送するので、
        // 省くと null 参照で初期化ごと失敗する (2026-08-23 に一度これで滝が消えた)。
        boundary.Build(spacing, kernelRadius);

        // ---- シミュレーション領域 ----
        // Sim Bounds は「非常用の最終手段」であって、液体が日常的に当たる壁ではない。
        // Phase 7 の実測で、注ぎ出した液体が Sim Bounds に当たって板状に溜まり、
        // 地面まで落ちないことが分かった。したがって領域は
        //   横: 壺がどの姿勢でも収まる旋回半径 + 液体が広がる余地
        //   縦: 地面の下 groundMargin 〜 壺の上 topMargin
        // として、地面を必ず領域内に含める (§9 / §20)。
        if (boundary.mode == FluidBoundary.Mode.Box)
        {
            Vector3 ext = boundary.boxInnerSize;
            regionSize = new Vector3(ext.x + simPadding * 2f,
                                     ext.y + simPadding * 2f + fallZoneBelow,
                                     ext.z + simPadding * 2f);
            regionOffsetY = -fallZoneBelow * 0.5f;
            regionYAnchored = false;
            containerSwingRadius = ext.magnitude * 0.5f * boundary.ContainerScale;
        }
        else
        {
            // 壺の旋回半径: 容器原点からの内部形状の最遠点。どの傾きでもこの球に収まる。
            float sc = boundary.ContainerScale;
            var prof = boundary.Profile;
            float swingR = 0f;
            for (int s = 0; s < PotInteriorProfile.Samples; s++)
            {
                float y = Mathf.Lerp(prof.FloorY, prof.RimY, s / (float)(PotInteriorProfile.Samples - 1));
                float r = prof.RadiusAt(y);
                swingR = Mathf.Max(swingR, Mathf.Sqrt(r * r + y * y));
            }
            swingR *= sc;
            containerSwingRadius = swingR;

            float containerY = boundary.Container.position.y;
            float top = containerY + swingR + topMargin;
            float bottom = groundY - groundMargin;
            float halfXZ = swingR + lateralSpread;

            regionSize = new Vector3(halfXZ * 2f, Mathf.Max(top - bottom, swingR * 2f), halfXZ * 2f);
            // 領域の底は **地面に固定** する。容器の高さに追従させると、
            // 初期化時より容器が高い位置に置かれたときに底が地面より上へ行き、
            // 落ちた液体が地面に届かず空中で止まる
            // （実測: CastleGtage で領域の底が y=0.31、地面は y=0 で、
            //  こぼれた液体が Ground にならず Airborne のままだった）。
            regionAnchorY = groundY - groundMargin + regionSize.y * 0.5f;
            regionYAnchored = true;
        }
        regionCenter = RegionCentreFor(boundary.SimPosition);
        cellSize = kernelRadius;
    }

    /// <summary>領域の中心。横は容器に追従し、縦は（壺モードでは）地面を含むよう world 固定。</summary>
    Vector3 RegionCentreFor(Vector3 containerPos)
    {
        float y = regionYAnchored ? regionAnchorY : containerPos.y + regionOffsetY;
        return new Vector3(containerPos.x, y, containerPos.z);
    }

    static float IdealLatticeKernelSum(float s, float h)
    {
        float layerDy = s * 0.816f, rowDz = s * 0.866f;
        int ly = Mathf.CeilToInt(h / layerDy) + 1;
        int lz = Mathf.CeilToInt(h / rowDz) + 1;
        int lx = Mathf.CeilToInt(h / s) + 1;
        float sum = Poly6(0f, h);
        for (int a = -ly; a <= ly; a++)
            for (int b = -lz; b <= lz; b++)
                for (int c = -lx; c <= lx; c++)
                {
                    if (a == 0 && b == 0 && c == 0) continue;
                    float y = a * layerDy, z = b * rowDz;
                    float x = c * s + (((b + a) & 1) == 0 ? 0f : s * 0.5f);
                    sum += Poly6(x * x + y * y + z * z, h);
                }
        return Mathf.Max(1e-6f, sum);
    }

    static float IdealLatticeGradSq(float s, float h, float mass, float rho0)
    {
        float layerDy = s * 0.816f, rowDz = s * 0.866f;
        int ly = Mathf.CeilToInt(h / layerDy) + 1;
        int lz = Mathf.CeilToInt(h / rowDz) + 1;
        int lx = Mathf.CeilToInt(h / s) + 1;
        Vector3 gradSelf = Vector3.zero;
        float sumSq = 0f;
        for (int a = -ly; a <= ly; a++)
            for (int b = -lz; b <= lz; b++)
                for (int c = -lx; c <= lx; c++)
                {
                    if (a == 0 && b == 0 && c == 0) continue;
                    float y = a * layerDy, z = b * rowDz;
                    float x = c * s + (((b + a) & 1) == 0 ? 0f : s * 0.5f);
                    Vector3 g = mass * SpikyGrad(new Vector3(x, y, z), h) / Mathf.Max(rho0, 1e-9f);
                    gradSelf += g;
                    sumSq += g.sqrMagnitude;
                }
        return Mathf.Max(1e-9f, sumSq + gradSelf.sqrMagnitude);
    }

    static Vector3 SpikyGrad(Vector3 rij, float h)
    {
        float r = rij.magnitude;
        if (r >= h || r < 1e-7f) return Vector3.zero;
        float h6 = h * h * h * h * h * h;
        float d = h - r;
        return rij * (-45f / (Mathf.PI * h6) * d * d / r);
    }

    static float Poly6(float r2, float h)
    {
        float h2 = h * h;
        if (r2 >= h2) return 0f;
        float d = h2 - r2;
        float h9 = h2 * h2 * h2 * h2 * h;
        return 315f / (64f * Mathf.PI * h9) * d * d * d;
    }

    void BuildBoundaryBuffers()
    {
        boundaryCount = Mathf.Max(1, boundary.Count);
        totalCount = fluidCount + boundaryCount;

        boundaryLocal?.Release(); boundaryPositions?.Release();
        boundaryVelocities?.Release(); boundaryVolumes?.Release();

        boundaryLocal = new GraphicsBuffer(GraphicsBuffer.Target.Structured, boundaryCount, sizeof(float) * 3);
        boundaryPositions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, boundaryCount, sizeof(float) * 3);
        boundaryVelocities = new GraphicsBuffer(GraphicsBuffer.Target.Structured, boundaryCount, sizeof(float) * 3);
        boundaryVolumes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, boundaryCount, sizeof(float));

        boundaryLocal.SetData(boundary.LocalPositions);
        boundaryVolumes.SetData(boundary.Volumes);

        var world = new Vector3[boundaryCount];
        var m = boundary.Container.localToWorldMatrix;
        for (int i = 0; i < boundaryCount; i++) world[i] = m.MultiplyPoint3x4(boundary.LocalPositions[i]);
        boundaryPositions.SetData(world);
        boundaryVelocities.SetData(new Vector3[boundaryCount]);
    }

    void AllocateBuffers()
    {
        positions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(float) * 3);
        predicted = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(float) * 3);
        velocities = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(float) * 3);
        deltaP = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(float) * 3);
        normals = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(float) * 3);
        safety = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(float) * 3);
        densities = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(float));
        lambdas = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(float));
        sortPositions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalCount, sizeof(float) * 3);
        sortedIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalCount, sizeof(uint));

        safetyCounters?.Release();
        safetyCounters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, sizeof(uint));

        regionFlags?.Release();
        regionFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(uint));
        regionFlags.SetData(new uint[fluidCount]);          // 全員 Inside から開始
        if (regionCountersRing == null)
        {
            regionCountersRing = new GraphicsBuffer[3];
            for (int i = 0; i < regionCountersRing.Length; i++)
                regionCountersRing[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 10, sizeof(uint));
        }
        regionRingIndex = 0;

        ages?.Release(); retiredFlags?.Release();
        ages = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(float));
        retiredFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fluidCount, sizeof(uint));
        ages.SetData(new float[fluidCount]);
        retiredFlags.SetData(new uint[fluidCount]);

        potProfile?.Release();
        potOuterProfile?.Release();
        if (boundary.mode == FluidBoundary.Mode.PotProfile && boundary.Profile != null)
        {
            var arr = boundary.Profile.GetProfileArray();
            potProfile = new GraphicsBuffer(GraphicsBuffer.Target.Structured, arr.Length, sizeof(float));
            potProfile.SetData(arr);

            // 外形。こぼれた液体が壺の実体を素通りしないために要る。
            var outer = boundary.Profile.GetOuterProfileArray();
            potOuterProfile = new GraphicsBuffer(GraphicsBuffer.Target.Structured, outer.Length, sizeof(float));
            potOuterProfile.SetData(outer);
        }
        else
        {
            potProfile = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(float));
            potProfile.SetData(new float[] { 1f, 1f });
            potOuterProfile = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(float));
            potOuterProfile.SetData(new float[] { 1f, 1f });
        }

        slopeProfileBuffer?.Release();
        if (slopeProfileHeights != null && slopeProfileHeights.Length >= 2)
        {
            slopeProfileBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, slopeProfileHeights.Length, sizeof(float));
            slopeProfileBuffer.SetData(slopeProfileHeights);
        }
        else
        {
            slopeProfileBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(float));
            slopeProfileBuffer.SetData(new float[] { groundY, groundY }); // flat fallback -- behaves exactly like the old single GroundY plane
        }
    }

    // ギミックの箱コライダを集める (collideWithGroundSurfaces のコメントを参照)。
    // GroundSurface はギミックの床・坂・台に付いているマーカーなので、それを目印にする。
    // Room_Floor は MeshCollider なので自然に対象外になる (地面平面 groundY が担当)。
    /// <summary>衝突に使う箱の候補。コライダのローカル境界を有向ボックスとして持つ。</summary>
    struct SolidCandidate
    {
        public Transform tr;
        public Collider col;
        public Vector3 localCentre;   // コライダのローカル中心
        public Vector3 localHalf;     // コライダのローカル半径 (スケール前)
        public bool settle;           // その上で液体を定着させてよいか
    }
    readonly List<SolidCandidate> solidCandidates = new List<SolidCandidate>();

    void GatherSolidBoxes()
    {
        solidCandidates.Clear();
        if (collideWithGroundSurfaces)
        {
            foreach (var gs in FindObjectsByType<GroundSurface>(FindObjectsSortMode.None))
            {
                var bc = gs.GetComponent<BoxCollider>();
                if (bc == null) continue;
                // 動く床 (揺れる橋) の上で定着させると、凍結した水たまりが床の動きに
                // 置いて行かれて空中に残る。衝突はするが定着はしない。
                AddCandidate(bc, bc.center, bc.size * 0.5f,
                             gs.GetComponentInParent<SwayingBridge>() == null);
            }
        }
        if (collideWithCourseColliders) GatherCourseCandidates();

        int n = Mathf.Max(1, Mathf.Min(maxSolidBoxes, Mathf.Max(1, solidCandidates.Count)));
        solidBoxCount = 0;
        solidActive = new int[n];
        solidW2LArr = new Matrix4x4[n];
        solidL2WArr = new Matrix4x4[n];
        solidHalfArr = new Vector4[n];
        solidBoxW2L?.Release(); solidBoxL2W?.Release(); solidBoxHalf?.Release();
        solidBoxW2L = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(float) * 16);
        solidBoxL2W = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(float) * 16);
        solidBoxHalf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(float) * 4);
        UpdateSolidBoxes();
    }

    void AddCandidate(Collider col, Vector3 localCentre, Vector3 localHalf, bool settle)
    {
        if (col == null) return;
        solidCandidates.Add(new SolidCandidate
        {
            tr = col.transform, col = col,
            localCentre = localCentre, localHalf = localHalf, settle = settle
        });
    }

    /// <summary>コースを構成するコライダを候補に足す。メッシュコライダはローカル境界を箱に使う。</summary>
    void GatherCourseCandidates()
    {
        var self = boundary != null && boundary.Container != null ? boundary.Container : transform;
        foreach (var col in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (col == null || col.isTrigger || !col.enabled) continue;
            if (col is TerrainCollider) continue;              // 地面は groundY / 斜面プロファイルが担当
            if (col is CharacterController) continue;          // ゴブリン本体
            if ((courseColliderMask.value & (1 << col.gameObject.layer)) == 0) continue;
            var t = col.transform;
            if (t == self || t.IsChildOf(self) || self.IsChildOf(t)) continue;   // 容器と自分自身
            if (col.GetComponent<GroundSurface>() != null) continue;             // 上で追加済み

            Vector3 lc, lh;
            if (col is BoxCollider bc) { lc = bc.center; lh = bc.size * 0.5f; }
            else if (col is MeshCollider mc && mc.sharedMesh != null)
            { lc = mc.sharedMesh.bounds.center; lh = mc.sharedMesh.bounds.extents; }
            else if (col is SphereCollider sc) { lc = sc.center; lh = Vector3.one * sc.radius; }
            else if (col is CapsuleCollider cc2) { lc = cc2.center; lh = Vector3.one * cc2.radius; lh[cc2.direction] = cc2.height * 0.5f; }
            else continue;

            // 箱で近似すると実形状から離れすぎるほど大きいものは対象外 (崖の一枚メッシュ等)。
            Vector3 ls = t.lossyScale;
            Vector3 worldHalf = new Vector3(lh.x * Mathf.Abs(ls.x), lh.y * Mathf.Abs(ls.y), lh.z * Mathf.Abs(ls.z));
            if (Mathf.Max(worldHalf.x, Mathf.Max(worldHalf.y, worldHalf.z)) * 2f > maxCourseBoxSize) continue;

            AddCandidate(col, lc, lh, t.GetComponentInParent<SwayingBridge>() == null);
        }
    }

    // 毎フレーム、**シミュレーション領域に近い箱だけ** を選んで GPU へ送る。
    // シーン全体では 296 個あるが、粒子 x 箱の総当たりなので全部は渡せない。
    // 揺れる橋のように動く床もあるので、行列は毎フレーム作り直す。
    void UpdateSolidBoxes()
    {
        if (solidBoxW2L == null || solidActive == null) return;
        int cap = solidActive.Length;
        Vector3 c = regionCenter;
        Vector3 ext = regionSize * 0.5f;
        Vector3 rmin = c - ext, rmax = c + ext;

        solidBoxCount = 0;
        for (int k = 0; k < solidCandidates.Count && solidBoxCount < cap; k++)
        {
            var sc = solidCandidates[k];
            if (sc.col == null) continue;
            var t = sc.tr;
            Vector3 ls = t.lossyScale;
            Vector3 half = new Vector3(sc.localHalf.x * Mathf.Abs(ls.x),
                                       sc.localHalf.y * Mathf.Abs(ls.y),
                                       sc.localHalf.z * Mathf.Abs(ls.z));
            Vector3 centre = t.TransformPoint(sc.localCentre);
            // 回転を含めた保守的な判定: 中心 ± 対角長で領域と重なるかだけ見る。
            float r = half.magnitude;
            if (centre.x + r < rmin.x || centre.x - r > rmax.x) continue;
            if (centre.y + r < rmin.y || centre.y - r > rmax.y) continue;
            if (centre.z + r < rmin.z || centre.z - r > rmax.z) continue;

            // スケールは half extents に畳み、行列は回転+平行移動だけにする。
            // スケール入りの行列で最小貫通軸を選ぶと、軸ごとに距離の尺度が違って
            // 押し出し方向を誤る。
            Matrix4x4 l2w = Matrix4x4.TRS(centre, t.rotation, Vector3.one);
            half += Vector3.one * (spacing * 0.5f);   // 粒子半径ぶんのマージン
            int i = solidBoxCount++;
            solidActive[i] = k;
            solidL2WArr[i] = l2w;
            solidW2LArr[i] = l2w.inverse;
            solidHalfArr[i] = new Vector4(half.x, half.y, half.z, sc.settle ? 1f : 0f);
        }
        for (int i = solidBoxCount; i < cap; i++) solidHalfArr[i] = Vector4.zero;   // half=0 -> 接触なし
        solidBoxW2L.SetData(solidW2LArr);
        solidBoxL2W.SetData(solidL2WArr);
        solidBoxHalf.SetData(solidHalfArr);
    }

    void BuildGrid()
    {
        // 毎回 kernelRadius から求め直す。前回の (粗くした) cellSize を起点にすると、
        // 登坂での領域拡張のたびに BuildGrid が呼ばれてセルが複利で粗くなっていく
        // (実測: 0.0756 → 0.0773)。
        cellSize = kernelRadius;
        Vector3 span = regionSize + Vector3.one * (cellSize * 6f);

        gridSize = new Vector3Int(
            Mathf.Max(1, Mathf.CeilToInt(span.x / cellSize)),
            Mathf.Max(1, Mathf.CeilToInt(span.y / cellSize)),
            Mathf.Max(1, Mathf.CeilToInt(span.z / cellSize)));
        cellTotal = gridSize.x * gridSize.y * gridSize.z;

        if (cellTotal > MaxCells)
        {
            // セル数が上限を超えたらセルを粗くする。近傍探索の正しさ（上限なしの完全ソート）は保たれる。
            float scale = Mathf.Pow(cellTotal / (float)MaxCells, 1f / 3f) * 1.02f;
            cellSize *= scale;
            gridSize = new Vector3Int(
                Mathf.Max(1, Mathf.CeilToInt(span.x / cellSize)),
                Mathf.Max(1, Mathf.CeilToInt(span.y / cellSize)),
                Mathf.Max(1, Mathf.CeilToInt(span.z / cellSize)));
            cellTotal = gridSize.x * gridSize.y * gridSize.z;
            Debug.LogWarning($"FluidCore: グリッドが大きすぎたためセルを {cellSize:F4}m に粗くしました ({cellTotal} セル)。", this);
        }
        blockCount = Mathf.CeilToInt(cellTotal / (float)ScanBlock);

        cellCounts?.Release(); cellStart?.Release(); cellCursor?.Release(); blockSums?.Release();
        cellCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellTotal, sizeof(uint));
        cellStart = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellTotal, sizeof(uint));
        cellCursor = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellTotal, sizeof(uint));
        blockSums = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1024, blockCount), sizeof(uint));
        UpdateGridOrigin();
    }

    // グリッドは容器に追従する。原点はセルサイズ単位に量子化して、
    // 微小移動でセル割り当てがガタつかないようにする。
    void UpdateGridOrigin()
    {
        regionCenter = RegionCentreFor(boundary.SimPosition);
        Vector3 lo = regionCenter - regionSize * 0.5f - Vector3.one * (cellSize * 3f);
        gridOrigin = new Vector3(
            Mathf.Floor(lo.x / cellSize) * cellSize,
            Mathf.Floor(lo.y / cellSize) * cellSize,
            Mathf.Floor(lo.z / cellSize) * cellSize);
    }

    public void SeedFluid()
    {
        if (positions == null) return;
        float targetVolume = boundary.InteriorVolumeWorld * fillFraction;
        var pts = boundary.GenerateSeedPoints(spacing, targetVolume, fluidCount);

        var pos = new Vector3[fluidCount];
        var vel = new Vector3[fluidCount];
        Vector3 fallback = boundary.SimPosition;
        for (int i = 0; i < fluidCount; i++)
        {
            pos[i] = i < pts.Count ? pts[i] : fallback;
            vel[i] = Vector3.zero;
        }
        SeededParticles = Mathf.Min(pts.Count, fluidCount);

        positions.SetData(pos);
        predicted.SetData(pos);
        velocities.SetData(vel);
        boundary.ResyncMotion();
        simTimeSinceSeed = 0f;        // 開始直後の壺内クランプの起点
        startupCalmDone = false;      // 静まる (startupCalmReleaseSpeed) まで維持する
    }

    void Release()
    {

        positions?.Release(); positions = null;
        predicted?.Release(); predicted = null;
        velocities?.Release(); velocities = null;
        deltaP?.Release(); deltaP = null;
        normals?.Release(); normals = null;
        safety?.Release(); safety = null;
        densities?.Release(); densities = null;
        lambdas?.Release(); lambdas = null;
        boundaryLocal?.Release(); boundaryLocal = null;
        boundaryPositions?.Release(); boundaryPositions = null;
        boundaryVelocities?.Release(); boundaryVelocities = null;
        boundaryVolumes?.Release(); boundaryVolumes = null;
        sortPositions?.Release(); sortPositions = null;
        sortedIndices?.Release(); sortedIndices = null;
        cellCounts?.Release(); cellCounts = null;
        cellStart?.Release(); cellStart = null;
        cellCursor?.Release(); cellCursor = null;
        blockSums?.Release(); blockSums = null;
        potProfile?.Release(); potProfile = null;
        potOuterProfile?.Release(); potOuterProfile = null;
        slopeProfileBuffer?.Release(); slopeProfileBuffer = null;
        safetyCounters?.Release(); safetyCounters = null;
        solidBoxW2L?.Release(); solidBoxW2L = null;
        solidBoxL2W?.Release(); solidBoxL2W = null;
        solidBoxHalf?.Release(); solidBoxHalf = null;
        regionFlags?.Release(); regionFlags = null;
        // 領域カウンタのリングバッファはここで解放しない。
        // 非同期リードバックの宛先なので、保留中の要求が完了する前に解放すると
        // 解放済みメモリへの書き戻しになり、エディタごと固まる（実測）。
        // 生存期間はコンポーネント自体に合わせ、OnDestroy でだけ解放する。
        ages?.Release(); ages = null;
        retiredFlags?.Release(); retiredFlags = null;
    }

    void LateUpdate()
    {
        if (!autoStep) return;
        pendingDt += Time.deltaTime;
        if (++stepFrameCounter >= Mathf.Max(1, stepEveryNFrames))
        {
            stepFrameCounter = 0;
            // 流体そのもののコストを測る (2026-08-22)。総フレーム時間はエディタの
            // ノイズが大きすぎて A/B にならないため、Step の実時間を直接記録する。
            // ディスパッチ発行の CPU 費用と、同期リードバックの GPU 待ちが両方入る。
            stepWatch.Restart();
            Step(pendingDt);
            stepWatch.Stop();
            LastStepMs = (float)stepWatch.Elapsed.TotalMilliseconds;
            stepMsAcc += LastStepMs; stepMsCount++;
            pendingDt = 0f;
            UpdateWatchdog();
        }
    }

    /// <summary>Step が異常に重い状態が続いていないか見張る。宣言部の「GPU 安全装置」を参照。</summary>
    void UpdateWatchdog()
    {
        if (watchdogStepMs <= 0f || WatchdogTripped) return;
        if (LastStepMs <= watchdogStepMs) { watchdogHits = 0; return; }
        if (++watchdogHits < Mathf.Max(1, watchdogFrames)) return;
        WatchdogTripped = true;
        WatchdogTripMs = LastStepMs;
        Debug.LogError($"[FluidCore] {name}: Step が {LastStepMs:F0}ms を {watchdogHits} フレーム連続で超えました。" +
                       $" GPU デバイスロストを避けるため、サブステップを {watchdogSafeSubSteps} に制限します" +
                       $" (通常は {LastSubStepCount})。原因を直してから ResetWatchdog() で解除してください。", this);
    }

    public void Step(float dt)
    {
        Initialise();
        if (!IsReady || dt <= 0f) return;
        // 2026-08-22 改訂: dt を固定値 (1/30) でクランプする方式は、低 fps でシミュ時間が
        // 実時間より遅れて「ポーションがスローモーション」になる (実測: 18fps で進行 61%)。
        // 安定性が本当に要求するのは **サブステップ刻み sdt ≤ stableSubstepDt** であって
        // dt そのものではない (1/15 実験の不安定化は sdt 増大が原因だった)。
        // したがって dt は実時間のまま受け取り、下でサブステップ数を足して sdt を保つ。
        // dt を削る (=スローモーション) のは maxSubSteps でも sdt を守れない極端な低 fps
        // (現行値では ~13.5fps 未満) のときだけ。
        float wallDt = dt;              // 切り詰め前の実経過時間 (SampleMotion の速度補正用)
        LastWallDt = wallDt;
        dt = Mathf.Min(dt, 1f / 10f);   // 異常ヒッチの保険
        float sdtCap = Mathf.Max(1e-4f, stableSubstepDt);
        if (dt > maxSubSteps * sdtCap) dt = maxSubSteps * sdtCap;

        // 追補 31 (2026-08-22 QA): wallDt も渡す。ヒッチ (実測 333ms) で dt が 74ms に
        // 切り詰められると、従来は「333ms ぶんの壺の移動 ÷ 74ms」で壁速度が実速度の
        // 4-5 倍 (実測 7.3 m/s、壺の実測最大 5.1 m/s) に膨れ、その 1 フレームで液体が
        // 大量に掬い出されていた。SampleMotion 側で姿勢の前進も dt/wallDt に比例させ、
        // 壁速度を実速度に保つ (遅れは後続フレームで実速度のまま回収 or テレポート)。
        boundary.SampleMotion(dt, wallDt);
        UpdateSolidBoxes();   // 揺れる橋が動くので毎フレーム更新

        // 容器の姿勢が確定してから配置する（上の pendingSeed の注記を参照）。
        if (pendingSeed)
        {
            pendingSeed = false;
            UpdateGridOrigin();
            SeedFluid();
            PreSettle();
        }

        // §21: 容器が瞬間移動したら、中身を同じ剛体変換で連れて行く。
        // これをしないと液体だけ元の場所に取り残され、次のフレームには
        // 「壺の外にある」＝全量こぼれた、と判定されてしまう。
        if (boundary.TeleportedThisStep)
        {
            fluidCompute.SetMatrix("TeleportMatrix", boundary.TeleportDelta);
            fluidCompute.SetInt("FluidCount", fluidCount);
            Bind(kTeleport, ("Positions", positions), ("Velocities", velocities),
                            ("RegionFlagsIn", regionFlags));
            fluidCompute.Dispatch(kTeleport, Mathf.CeilToInt(fluidCount / (float)Threads), 1, 1);
        }

        // 登坂への追従 (regionGrowStep のコメントを参照)。底は地面に固定したまま
        // (こぼれた液体が地面に届く要件 §9/§20 は不変)、天井だけを容器の必要高さ
        // (= 初期化時と同じ式: 容器 Y + 旋回半径 + topMargin) に合わせて刻み単位で
        // 引き上げ、グリッドを作り直す。刻みがヒステリシスになるので登坂中でも
        // 作り直しは数回で済む。下りでは縮めない (毎フレーム作り直さないため。
        // 天井が高い余剰は正しさに影響せず、セルが粗くなった場合のみ near 探索が
        // 少し高くつくが、圧壊の O(n^2) 化に比べれば誤差)。
        if (regionYAnchored && regionGrowStep > 0f)
        {
            float neededTop = boundary.SimPosition.y + containerSwingRadius + topMargin;
            float currentTop = regionAnchorY + regionSize.y * 0.5f;
            if (neededTop > currentTop)
            {
                float newTop = Mathf.Ceil(neededTop / regionGrowStep) * regionGrowStep;
                regionSize.y = newTop - (groundY - groundMargin);
                regionAnchorY = groundY - groundMargin + regionSize.y * 0.5f;
                BuildGrid();
            }
        }

        UpdateGridOrigin();

        // §3 CFL: 壁も流体も、1 サブステップで粒子間隔の一定割合以上動かさない。
        // 壁がそれより速く動くと、境界粒子が流体を貫通して飲み込む。
        //
        // 回転の腕の長さは **容器の旋回半径** であって、シミュレーション領域の
        // 大きさではない。従来は regionSize.magnitude*0.5 (約 2.45m) を使っており、
        // 実際の壺 (約 0.97m) の 2.5 倍を要求していた。過大評価は安全側に見えるが、
        // サブステップ数の上限に早く張り付く分だけ、**本当に必要なときに足りなくなる**。
        float containerSpeed = boundary.LinearVelocity.magnitude
                             + boundary.AngularVelocity.magnitude * containerSwingRadius;
        // 流体側は「速度クランプ値 (maxSpeed) 」ではなく **前フレームの実測最大速さ** を使う。
        // クランプ値は理論上の最悪値なので、それを使うとどんなに静かでも常に
        // maxSubSteps に張り付き、静止状態でも 10 サブステップ回っていた（実測 17ms/frame）。
        // CFL の定義は「1 サブステップで粒子間隔の一定割合以上動かさない」なので、
        // 実測速度を使うのが本来の実装。1 フレーム遅れる分は cflSpeedMargin で見る。
        // 上限は壺内/壺外クランプの大きい方 (壺外の落下は maxSpeedFalling まで出る。2026-08-22)
        float fluidSpeed = Mathf.Min(MeasuredMaxSpeed * cflSpeedMargin, Mathf.Max(maxSpeed, maxSpeedFalling));
        float worstSpeed = Mathf.Max(fluidSpeed, containerSpeed);

        float maxTravel = 0.4f * spacing;

        // サブステップ数を上限まで使っても足りないなら、そのフレームだけ
        // **流体の時間をゆっくり進める**。フレームが引っかかって dt が跳ねたときに効く。
        // 解けないまま進めて発散させるより、一瞬スローになる方がよい。
        //
        // 容器側も対象にする。容器の姿勢は速度制限つきで追従する (§21 の平滑化) ので、
        // dt を削れば容器の移動量も減り、両方まとめて CFL を満たせる。
        // 流体だけを見ていたときは、フレーム落ち時に容器側が上限を超えて
        // CFL 不足が残っていた（実測 4 フレーム）。
        // 追補 32 (2026-08-22 バグ報告「ポーションの落下がスローモーション」):
        // dt を削る条件から **容器速度を実質的に外す**。
        //
        // ここが「落下がスローモーション」の真因だった。従来は容器の世界速度
        // (走行時の実測ピーク 7〜12 m/s) が worstSpeed を占有し、
        //   dt_cap = 0.4*spacing*maxSubSteps / worstSpeed = 0.288 / 12 ≒ 24ms
        // つまり **42fps を下回るフレームは常にシミュ時間が実時間より遅れる**。
        // 実ステージ (30fps 前後) では常時 60-70% 進行 = 全部スローモーション。
        // しかも落下中の液滴も同じ dt で積分されるので、見かけの重力が 0.6^2 = 0.36 倍に
        // なり「蜜の中を落ちる」動きになっていた。
        //
        // 容器の世界速度は本来 CFL の対象ではない。CFL が守るのは **相対運動** で、
        // 壺内の流体は容器と一緒に動くため相対速度はほぼ 0 (実測もその値を使っている)。
        // 容器由来で本当に危ないのは「壁が共動していない液体 (地面の水たまり等) を
        // 掃く」ケースだけで、これは SafetyCorrection (§10) が受け持つ。
        // したがって容器には桁の違う緩い予算 (containerTravelBudget) を与え、
        // 極端なヒッチのときだけ dt を削る。流体側は従来どおり厳密に見る。
        // さらに追補 32 の本体: **CFL 由来の dt 削りそのものを非常用まで緩める**。
        // 実測 (実ステージ 13fps・前方歩行) で simRatio 平均 0.61 / 最悪 0.21。
        // つまりシミュ時間が実時間の 6 割しか進んでおらず、これが「落下がスローモーション」
        // の正体だった。内訳は容器ではなく **流体側の CFL** で、
        //   必要サブステップ = 実測スロッシュ 4 m/s x 1.6(margin) x dt / (0.4*spacing) ≒ 33
        // が上限 20 を超えるため dt が 6 割に削られていた。
        //
        // ここで重要なのは、**1 サブステップあたりの移動量はすでに二重に有界**だという点:
        //   (1) sdt は上の stableSubstepDt で 0.0037s 以下に固定されている
        //       (dt > maxSubSteps*sdtCap のとき dt を削る処理が上にある)
        //   (2) 速度は ClampSpeed で壺内 maxSpeed / 壺外 maxSpeedFalling に固定されている
        // したがって最悪の移動量は maxSpeed*sdtCap = 5*0.0037 = 0.0185m ≒ 0.5 粒子間隔で、
        // PBF が普通に解ける範囲に収まる。壁の貫通は SafetyCorrection (§10) が別途受け持つ。
        // よって CFL 由来の dt 削りは冗長で、副作用 (スローモーション) だけが残っていた。
        // 病的なケースのために「1 サブステップで emergencyTravel 粒子間隔以上動く」ときだけ
        // 削る非常用の網は残す (通常プレイでは発火しない)。
        float sdtNow = dt / Mathf.Max(1, Mathf.Min(maxSubSteps,
                            Mathf.Max(Mathf.CeilToInt(dt / sdtCap), minSubSteps)));
        float emergencyTravel = emergencyTravelSpacing * spacing;
        if (worstSpeed * sdtNow > emergencyTravel)
            dt *= emergencyTravel / Mathf.Max(worstSpeed * sdtNow, 1e-6f);

        LastBoundaryTravel = containerSpeed * dt;
        int need = Mathf.CeilToInt(worstSpeed * dt / maxTravel);
        // 安定性フロア (2026-08-22): sdt = dt/sub が stableSubstepDt を超えないよう
        // サブステップ数を足す。これで dt を実時間のまま進めても静定安定性が保たれる。
        int needStability = Mathf.CeilToInt(dt / sdtCap);
        // 弾道モードは近傍相互作用が無いので静定安定性の制約 (needStability) も
        // 下限 minSubSteps も要らない。移動量 (CFL) だけで決める。
        int sub = ballisticMode
            ? Mathf.Clamp(need, 1, maxSubSteps)
            : Mathf.Clamp(Mathf.Max(need, needStability), minSubSteps, maxSubSteps);
        // GPU 安全装置: 異常に重い状態が続いていたら、実時間性より先に GPU を守る。
        if (WatchdogTripped) sub = Mathf.Min(sub, Mathf.Max(1, watchdogSafeSubSteps));
        LastRequiredSubSteps = need;

        // 剛体搬送は**廃止した**。
        // 「解けない容器の動きを中身ごと運ぶ」対策は CFL は守れるが、
        // 運んだ分だけ相対運動が消えるので **こぼれなくなり**、さらに搬送後の状態が
        // 緩和して「空中で膨らんでから収束する」という不自然な見え方になった。
        // 代わりに FluidBoundary 側で容器の姿勢そのものを平滑化し、
        // 流体が見る速度を最初から解ける範囲に収めている (§21 の「平滑化」)。
        LastCarryFraction = 0f;
        if (need > sub) CflLimitedFrames++;
        if (need > PeakRequiredSubSteps) PeakRequiredSubSteps = need;
        if (containerSpeed > PeakContainerSpeed) PeakContainerSpeed = containerSpeed;
        if (MeasuredMaxSpeed > PeakFluidSpeed) PeakFluidSpeed = MeasuredMaxSpeed;

        LastSubStepCount = sub;
        LastSimDt = dt;
        accSimDt += dt; accWallDt += LastWallDt;

        simTimeSinceSeed += dt;
        float sdt = dt / sub;
        BindAll();   // フレームに 1 回。サブステップで変わる分は SubStep 側で設定する
        for (int s = 0; s < sub; s++) SubStep(sdt, (s + 1) / (float)sub);

        // 領域分類は観測のみで、位置・速度には触れない (§14/追加修正1)。
        // 同期読み戻しが高くつくため classifyInterval フレームに 1 回に間引く (宣言部の注記)。
        if (--classifyCountdown <= 0)
        {
            classifyCountdown = Mathf.Max(1, classifyInterval);
            ClassifyAndRead();
        }

        // 開始クランプの解除判定 (適応型)。実測最大速度がクランプ値より十分下がった =
        // 波が本当に死んだときに解く。保険として startupCalmSimSeconds で強制解除。
        //
        // FIXED 2026-08-22 (バグ報告「ポーションが全くこぼれなくなった」): 「静まったら解除」
        // だけだと、プレイヤーがすぐ動き出した場合に永遠に静まらず、保護クランプが
        // かかりっぱなしになっていた。このクランプはあくまで「開始時に静止したままの壺で
        // シード波が育つのを防ぐ」ためのもの。**容器が動き出したら即解除**する —
        // 以降のスロッシュはプレイヤー起因なので、通常のこぼれ挙動に戻すのが正しい。
        // 容器速度での解除は 2 sim 秒だけ待つ: 開始フレームのリグの壺持ち上げ・
        // エディタヒッチの姿勢キャッチアップが誤発火させるのを防ぐ。
        if (!startupCalmDone
            && ((simTimeSinceSeed > 3f && MeasuredMaxSpeed < startupCalmReleaseSpeed)
                || (simTimeSinceSeed > 2f && containerSpeed > startupCalmReleaseContainerSpeed)
                || simTimeSinceSeed > startupCalmSimSeconds))
            startupCalmDone = true;
    }

    // 種として置いた格子は PBF の密度拘束を満たしていない。そのままゲームを始めると、
    // 最初の数フレームで格子が緩んで液面が一度大きく盛り上がり、リムを越えた分が
    // こぼれる（実測: 開始 0.6 秒で 605 粒子が壺の外に出て PotionVolume が 0.962 へ）。
    // これは運搬のしかたと関係の無い、初期条件だけが原因の損失。
    //
    // ゲームが始まる前に、容器を静止させたまま同じソルバで釣り合うまで進めておく。
    // 別の緩和法に置き換えるのではなく本番と同じ SubStep をそのまま回すので、
    // 得られる状態は「静かに置いておいたときの液面」そのもの。
    void PreSettle()
    {
        if (initialSettleSeconds <= 0f) return;

        // CFL を必ず満たす刻み。ここは 1 回きりなので刻みを細かく取って構わない。
        float sdt = 0.4f * spacing / Mathf.Max(maxSpeed, 1e-6f);
        int steps = Mathf.Clamp(Mathf.CeilToInt(initialSettleSeconds / sdt), 1, 2000);
        settling = true;
        BindAll();   // settling フラグ (EscapeEnabled) を反映してから整定を回す
        for (int i = 0; i < steps; i++) SubStep(sdt, 1f);
        settling = false;
        PreSettleSteps = steps;
    }

    // 整定中フラグ。BindAll が見て「ふちを越えたら出て行く」判定を止める。
    bool settling;

    /// <summary>開始時の整定に使ったサブステップ数（Debug 用）。</summary>
    public int PreSettleSteps { get; private set; }

    /// <summary>壺を満タンに戻す。こぼれた液体・地面の水たまりも全て消える。
    /// デバッグ用のワープなど「その場をリセットしたい」ときに使う。
    /// 容器が動いた直後に呼ぶこと（現在の姿勢で種を置き直すため）。</summary>
    public void ResetFluid()
    {
        Initialise();
        if (!IsReady) return;

        // 逃げた/沈殿した/退避した印を全て消す。これをしないと、種を置き直しても
        // 前回こぼれた粒子が Escaped のまま残り、満タンにならない。
        var zero = new uint[fluidCount];
        retiredFlags.SetData(zero);
        ages.SetData(new float[fluidCount]);
        foreach (var b in regionCountersRing) b?.SetData(ZeroCounters);

        boundary.ResyncMotion();
        UpdateGridOrigin();
        SeedFluid();
        PreSettle();
        ClassifyAndRead();
    }

    void ClassifyAndRead()
    {
        var buf = regionCountersRing[regionRingIndex];
        regionRingIndex = (regionRingIndex + 1) % regionCountersRing.Length;

        buf.SetData(ZeroCounters);
        Bind(kClassify, ("PositionsIn", positions), ("VelocitiesIn", velocities),
                        ("RegionFlags", regionFlags),
                        ("RegionCounters", buf), ("PotProfileBuf", potProfile),
                        ("PotOuterBuf", potOuterProfile),
                        ("RetiredFlagsIn", retiredFlags));
        fluidCompute.Dispatch(kClassify, Mathf.CeilToInt(fluidCount / (float)Threads), 1, 1);

        if (synchronousReadback)
        {
            buf.GetData(regionRead);
            ApplyRegionCounters(regionRead);
        }
        else
        {
            AsyncGPUReadback.Request(buf, req =>
            {
                if (req.hasError || positions == null) return;
                var data = req.GetData<uint>();
                for (int i = 0; i < 10 && i < data.Length; i++) regionRead[i] = data[i];
                ApplyRegionCounters(regionRead);
            });
        }
    }

    void ApplyRegionCounters(uint[] r)
    {
        InsideCount = (int)r[0];
        RimCount = (int)r[1];
        AirborneCount = (int)r[2];
        GroundCount = (int)r[3];
        RetiredCount = (int)r[4];
        OverflowEvents += (int)r[5];
        PenetrationEvents += (int)r[6];
        MeasuredMaxSpeed = r[7] / 1000f;
        EscapedCount = (int)r[8];
    }

    public void ResetOverflowCounters()
    {
        OverflowEvents = 0; PenetrationEvents = 0;
        // RegionFlags には「一度でも外へ出た」ラッチ (FLAG_EVER_OUT) が入っている。
        // これを消さないと、シード直後でも全粒子が「もう数えた」状態になり、
        // Overflow が二度と計上されない。
        regionFlags?.SetData(new uint[fluidCount]);
        ages?.SetData(new float[fluidCount]);
        retiredFlags?.SetData(new uint[fluidCount]);
        RetiredCount = 0;
        if (regionCountersRing != null)
            foreach (var b in regionCountersRing) b?.SetData(ZeroCounters);
    }

    void SubStep(float dt, float lerpT)
    {
        BindPerSubstep(dt, lerpT);

        int fluidGroups = Mathf.CeilToInt(fluidCount / (float)Threads);
        int totalGroups = Mathf.CeilToInt(totalCount / (float)Threads);
        int boundaryGroups = Mathf.CeilToInt(boundaryCount / (float)Threads);
        int cellGroups = Mathf.CeilToInt(cellTotal / (float)Threads);

        if (ballisticMode)
        {
            // 弾道モード: 境界粒子は誰も読まないので流体の範囲だけ回す。
            fluidCompute.Dispatch(kIntegrateBoundary, fluidGroups, 1, 1);
            fluidCompute.Dispatch(kFinalize, fluidGroups, 1, 1);
            if (solidBoxCount > 0)
                fluidCompute.Dispatch(kSolidBoxCollide, fluidGroups, 1, 1);
            return;
        }

        // 結合カーネルはスレッド範囲の広い方でディスパッチする (対策③)
        fluidCompute.Dispatch(kIntegrateBoundary, Mathf.Max(fluidGroups, boundaryGroups), 1, 1);

        // 近傍グリッドは **毎サブステップ必ず再構築する**。使い回し (追補 38) は撤去済み:
        // 効果が測定限界以下である一方、取りこぼした近傍がソルバを壊し、さらに実装を誤ると
        // 範囲外セルを読んで GPU デバイスロストを起こす。宣言部の注記を参照。
        {
            fluidCompute.Dispatch(kClearBuildSort, Mathf.Max(cellGroups, totalGroups), 1, 1);
            fluidCompute.Dispatch(kCount, totalGroups, 1, 1);
            fluidCompute.Dispatch(kScanLocal, blockCount, 1, 1);
            fluidCompute.Dispatch(kScanBlocks, 1, 1, 1);
            fluidCompute.Dispatch(kScanAdd, cellGroups, 1, 1);
            fluidCompute.Dispatch(kScatter, totalGroups, 1, 1);
        }

        for (int it = 0; it < solverIterations; it++)
        {
            fluidCompute.Dispatch(kDensityLambda, fluidGroups, 1, 1);
            fluidCompute.Dispatch(kDeltaP, fluidGroups, 1, 1);
            fluidCompute.Dispatch(kApplyDeltaP, fluidGroups, 1, 1);
        }

        fluidCompute.Dispatch(kVelNormals, fluidGroups, 1, 1);
        fluidCompute.Dispatch(kViscTension, fluidGroups, 1, 1);
        fluidCompute.Dispatch(kFinalize, fluidGroups, 1, 1);
        if (solidBoxCount > 0)
            fluidCompute.Dispatch(kSolidBoxCollide, fluidGroups, 1, 1);
    }

    void Bind(int kernel, params (string name, GraphicsBuffer buf)[] entries)
    {
        for (int i = 0; i < entries.Length; i++)
            fluidCompute.SetBuffer(kernel, entries[i].name, entries[i].buf);
    }

    // サブステップごとに変わるものだけを設定する (2026-08-21 ディスパッチ対策①)。
    // 従来は BindAll 全体 (バッファ束縛 ~60 + スカラー ~50 呼び出し) をサブステップ毎に
    // 繰り返しており、サブステップ 12 のとき毎フレーム ~1300 回のコマンド記録が CPU 側の
    // 主要コストだった。2 コアの LateUpdate は逐次実行で、各 Step が冒頭で BindAll する
    // ため、共有 ComputeShader のままでもフレーム内の束縛は壊れない (Dispatch 時点で捕捉)。
    void BindPerSubstep(float dt, float lerpT)
    {
        fluidCompute.SetFloat("DeltaTime", dt);
        // ADDED 2026-08-17: XSPH のブレンド率 kVisc = 係数 * sdt / 基準刻みが大きすぎると
        // 粘性が減衰源から**加振源**に反転する (実測: シェーダ保険クランプの 0.9 に張り付いた
        // 状態で泡立ちが持続)。dt 依存なのでここで毎サブステップ、実効係数を絞る。
        // 2026-08-22: キャップ 0.55 は過剰で、本来のチューニング (60fps で kVisc ~0.75) より
        // 減衰が弱くなり歩行スロッシュが暴れていた。0.75 へ緩和 (minSubSteps 9 で sdt が
        // 安定圏に入ったため、0.75 は PreSettle と同等の安定条件)。
        float refStep = Mathf.Max(viscosityRefStep, 1e-6f);
        fluidCompute.SetFloat("ViscosityXSPH", Mathf.Min(viscosity, xsphBlendCap * refStep / Mathf.Max(dt, 1e-6f)));

        // 動く境界: サブステップ間で姿勢を補間する (§3)。補間しないと壁が瞬間移動して
        // 流体を弾き飛ばし、エネルギーを注入する。
        Matrix4x4 m = boundary.InterpolatedMatrix(lerpT);
        fluidCompute.SetMatrix("BoundaryToWorld", m);
        fluidCompute.SetVector("ContainerCenter", boundary.InterpolatedCenter(lerpT));
        if (boundary.mode == FluidBoundary.Mode.PotProfile && boundary.Profile != null)
        {
            fluidCompute.SetMatrix("WorldToPotSafety", m.inverse);
            fluidCompute.SetMatrix("PotToWorldSafety", m);
        }
    }

    // カーネルごとに必要なバッファだけをバインドする。読むだけのバッファは SRV 別名で渡し、
    // 同一カーネルに RW/SRV の両方を渡さない（D3D11 の UAV スロット上限 8 に収めるため）。
    // 2026-08-21 ディスパッチ対策①: フレームに 1 回だけ呼ぶ (Step / PreSettle の冒頭)。
    // サブステップで変わる DeltaTime・実効粘性・容器の補間行列は BindPerSubstep が担当。
    void BindAll()
    {
        Bind(kIntegrateBoundary, ("BoundaryLocal", boundaryLocal),
                                 ("BoundaryPositionsRW", boundaryPositions),
                                 ("BoundaryVelocitiesRW", boundaryVelocities),
                                 ("Positions", positions), ("PredictedPositions", predicted),
                                 ("Velocities", velocities), ("SafetyCorrection", safety),
                                 // 追補 33: 脱出済み判定を積分側でも見る (ClampSpeedFor の注記)
                                 ("RetiredFlagsIn", retiredFlags));

        Bind(kClearBuildSort, ("CellCounts", cellCounts),
                              ("SortPositions", sortPositions), ("PredictedPositions", predicted),
                              ("BoundaryPositions", boundaryPositions));
        Bind(kCount, ("SortPositions", sortPositions), ("CellCounts", cellCounts),
                     ("RetiredFlagsIn", retiredFlags));
        Bind(kScanLocal, ("CellCounts", cellCounts), ("CellStart", cellStart), ("BlockSums", blockSums));
        Bind(kScanBlocks, ("BlockSums", blockSums));
        Bind(kScanAdd, ("CellStart", cellStart), ("CellCursor", cellCursor), ("BlockSums", blockSums));
        Bind(kScatter, ("SortPositions", sortPositions), ("CellCursor", cellCursor), ("SortedIndices", sortedIndices),
                       ("RetiredFlagsIn", retiredFlags));

        Bind(kDensityLambda, ("PredictedPositions", predicted), ("Densities", densities), ("Lambdas", lambdas),
                             ("SortPositionsIn", sortPositions), ("CellStartIn", cellStart),
                             ("CellCountsIn", cellCounts), ("SortedIndicesIn", sortedIndices),
                             ("BoundaryVolumes", boundaryVolumes), ("RetiredFlagsIn", retiredFlags));

        Bind(kDeltaP, ("PredictedPositions", predicted), ("DeltaP", deltaP), ("LambdasIn", lambdas),
                      ("SortPositionsIn", sortPositions), ("CellStartIn", cellStart),
                      ("CellCountsIn", cellCounts), ("SortedIndicesIn", sortedIndices),
                      ("BoundaryVolumes", boundaryVolumes), ("RetiredFlagsIn", retiredFlags));

        Bind(kApplyDeltaP, ("PredictedPositions", predicted), ("DeltaP", deltaP));

        // 結合カーネル: 速度式は PredictedPositions (UAV) を読むため、法線側も同じ束縛を
        // 使う (PredictedIn との二重束縛は不可。シェーダ側の注記を参照)。
        Bind(kVelNormals, ("PredictedPositions", predicted), ("Positions", positions),
                          ("SafetyCorrection", safety), ("DeltaP", deltaP),
                          ("Normals", normals), ("DensitiesIn", densities),
                          ("SortPositionsIn", sortPositions), ("CellStartIn", cellStart),
                          ("CellCountsIn", cellCounts), ("SortedIndicesIn", sortedIndices),
                          ("RetiredFlagsIn", retiredFlags));

        Bind(kViscTension, ("Velocities", velocities), ("PredictedIn", predicted), ("VelocityIn", deltaP),
                           ("NormalsIn", normals), ("DensitiesIn", densities),
                           ("SortPositionsIn", sortPositions), ("CellStartIn", cellStart),
                           ("CellCountsIn", cellCounts), ("SortedIndicesIn", sortedIndices),
                           ("BoundaryVelocities", boundaryVelocities), ("BoundaryVolumes", boundaryVolumes),
                           ("RetiredFlagsIn", retiredFlags));

        // UAV: PredictedPositions / Positions / Velocities / SafetyCounters / Ages / RetiredFlags = 6。
        // D3D11 の上限 8 に収まっている。
        Bind(kFinalize, ("PredictedPositions", predicted), ("Positions", positions), ("Velocities", velocities),
                        ("PotProfileBuf", potProfile), ("PotOuterBuf", potOuterProfile),
                        ("SlopeProfileBuf", slopeProfileBuffer),
                        ("SafetyCounters", safetyCounters),
                        ("Ages", ages), ("RetiredFlags", retiredFlags));

        // ギミックの箱衝突は独立カーネル (Finalize に入れると FXC が落ちる)。
        Bind(kSolidBoxCollide, ("Positions", positions), ("Velocities", velocities),
                               ("RetiredFlags", retiredFlags),
                               ("SolidBoxWorldToLocal", solidBoxW2L), ("SolidBoxLocalToWorld", solidBoxL2W),
                               ("SolidBoxHalf", solidBoxHalf));

        fluidCompute.SetInt("FluidCount", fluidCount);
        fluidCompute.SetInt("TotalCount", totalCount);
        fluidCompute.SetInt("BoundaryCount", boundaryCount);
        fluidCompute.SetInt("CellTotal", cellTotal);
        fluidCompute.SetInt("BlockCount", blockCount);
        fluidCompute.SetInt("SolidBoxCount", solidBoxCount);

        fluidCompute.SetVector("Gravity", Physics.gravity);   // §2: これが唯一の外力
        // (DeltaTime / BoundaryToWorld / ContainerCenter はサブステップ依存 → BindPerSubstep)
        fluidCompute.SetVector("ContainerLinearVelocity", boundary.LinearVelocity);
        fluidCompute.SetVector("ContainerAngularVelocity", boundary.AngularVelocity);

        fluidCompute.SetFloat("KernelRadius", kernelRadius);
        fluidCompute.SetFloat("RestDensity", restDensity);
        fluidCompute.SetFloat("ParticleMass", particleVolume);
        fluidCompute.SetFloat("RelaxationEps", relaxationEps);
        fluidCompute.SetFloat("MinDenom", refSumGradSq * minDenomFraction);
        fluidCompute.SetFloat("BoundaryPressureScale", boundaryPressureScale);
        fluidCompute.SetFloat("SolverRelaxation", solverRelaxation);
        fluidCompute.SetFloat("ArtificialPressure", artificialPressure);
        fluidCompute.SetFloat("ArtificialPressureQ", artificialPressureQ);
        fluidCompute.SetFloat("MaxDeltaP", spacing * maxDeltaPPerSpacing);
        // (ViscosityXSPH は dt 依存の実効キャップつき → BindPerSubstep で設定)
        fluidCompute.SetFloat("BoundaryViscosity", boundaryViscosity);
        // 粘性のブレンド率をサブステップ数から独立させるための基準時間刻み。
        // 係数はこの dt のときの 1 ステップ分の効きを表す。
        fluidCompute.SetFloat("ViscosityRefStep", viscosityRefStep);
        fluidCompute.SetFloat("CohesionStrength", cohesionStrength);
        fluidCompute.SetFloat("CurvatureStrength", curvatureStrength);
        fluidCompute.SetFloat("MaxSpeed", maxSpeed);
        fluidCompute.SetFloat("MaxSpeedFalling", Mathf.Max(maxSpeed, maxSpeedFalling));
        // 追補 33: 脱出済みの液滴に calm (MaxSpeedPot) を掛けない。false にすると旧挙動。
        fluidCompute.SetFloat("EscapedIgnoreCalm", escapedIgnoreCalm ? 1f : 0f);
        float potClamp = maxSpeedInPot > 0f ? maxSpeedInPot : maxSpeed;
        if (!startupCalmDone)   // 開始直後の波つぶし (適応型、宣言部の注記)
            potClamp = Mathf.Min(potClamp, startupCalmClamp);
        fluidCompute.SetFloat("MaxSpeedPot", potClamp);
        // 追補 26: パリー回収 & 着地ジョルト
        Vector3 potPos = boundary != null ? boundary.Container.position : Vector3.zero;
        Vector3 potUp = boundary != null ? boundary.Container.up : Vector3.up;
        fluidCompute.SetFloat("RecallStrength", Time.time < recallUntil ? recallStrengthValue : 0f);
        fluidCompute.SetVector("RecallTarget", potPos + potUp * 0.85f);
        fluidCompute.SetFloat("RecallMinY", potPos.y - recallMinYDrop);
        fluidCompute.SetFloat("RecallRadius", recallRadius);
        fluidCompute.SetFloat("SpillGrace", Time.time < spillGraceUntil ? 1f : 0f);
        fluidCompute.SetFloat("RestoreEscaped", Time.time < restoreUntil ? restoreChancePerFrame : 0f);
        // 上+前方に注入する (着地の跳ね返り + 前方サージ)。下向きは圧力ソルバが床境界へ
        // 吸収し、真上だけの噴水は壺内へ落ち戻るため、どちらも実測ほぼ無効だった。
        fluidCompute.SetVector("JoltAccel",
            Time.frameCount == joltFrame ? joltDeltaV / Mathf.Max(Time.deltaTime, 1e-4f) : Vector3.zero);

        fluidCompute.SetVector("GridOrigin", gridOrigin);
        fluidCompute.SetFloat("CellSize", cellSize);
        fluidCompute.SetInts("GridSize", gridSize.x, gridSize.y, gridSize.z);

        Vector3 bmin = gridOrigin + Vector3.one * (cellSize * 0.51f);
        Vector3 bmax = gridOrigin + new Vector3(gridSize.x, gridSize.y, gridSize.z) * cellSize
                     - Vector3.one * (cellSize * 0.51f);
        fluidCompute.SetVector("BoundsMin", bmin);
        fluidCompute.SetVector("BoundsMax", bmax);
        fluidCompute.SetFloat("BoundsRestitution", boundsRestitution);
        fluidCompute.SetFloat("BoundsFriction", boundsFriction);

        // SafetyCorrection (§10)。壺モードでのみ有効。
        bool potMode = boundary.mode == FluidBoundary.Mode.PotProfile && boundary.Profile != null;
        fluidCompute.SetInt("SafetyEnabled", potMode ? 1 : 0);
        if (potMode)
        {
            var prof = boundary.Profile;
            fluidCompute.SetInt("PotProfileCount", PotInteriorProfile.Samples);
            fluidCompute.SetFloat("PotFloorY", prof.FloorY);
            fluidCompute.SetFloat("PotRimY", prof.RimY);
            fluidCompute.SetFloat("PotMeshMinY", prof.MeshMinY);
            fluidCompute.SetFloat("PotMeshMaxY", prof.MeshMaxY);
            // SafetyCorrection は「壁を半径方向に突き抜けた粒子」だけを戻す。
            // リムを越える動きは半径方向ではなく上方向なので、リムまで有効にしても
            // 堰にはならない（実測でリム帯を除外しても堰の高さは不変だった）。
            // 一方、リムフェードで開口端の壁は弱くなるので、そこを守る必要がある。
            fluidCompute.SetFloat("SafetyTopY", prof.RimY);
            fluidCompute.SetFloat("PotMaxRadius", prof.MaxRadius);
            fluidCompute.SetFloat("SafetyMargin", spacing / boundary.ContainerScale * 0.25f);
            // (WorldToPotSafety / PotToWorldSafety は補間行列由来 → BindPerSubstep で設定)
            fluidCompute.SetFloat("PotRimR", prof.RimR);
        }
        fluidCompute.SetFloat("RimOpeningHeight", rimOpeningHeight);
        fluidCompute.SetFloat("GroundY", groundY);
        fluidCompute.SetFloat("GroundBandHeight", spacing * 1.5f);
        bool slopeActive = slopeProfileHeights != null && slopeProfileHeights.Length >= 2;
        fluidCompute.SetInt("SlopeProfileCount", slopeProfileBuffer != null ? slopeProfileBuffer.count : 2);
        fluidCompute.SetFloat("SlopeZStart", slopeActive ? slopeZStart : 0f);
        fluidCompute.SetFloat("SlopeZEnd", slopeActive ? slopeZEnd : -1f);
        fluidCompute.SetFloat("SlopeRestitution", slopeRestitution);
        fluidCompute.SetFloat("SlopeFriction", slopeFriction);
        fluidCompute.SetInt("FrameSeed", Time.frameCount);
        // 開始時の整定中は「ふちを越えたら出て行く」を止める。容器は静止しているので、
        // このとき外へ出るのは種の格子が緩む勢いだけが原因であり、運搬の結果ではない。
        // 止めないと格子の緩みで弾かれた粒子がそのまま地面へ落ち、ゲーム開始の瞬間に
        // 液滴が散らばる（実測 38 粒子）。止めれば SafetyCorrection が内側へ戻す。
        fluidCompute.SetInt("EscapeEnabled", (escapeAboveRim && !settling) ? 1 : 0);
        fluidCompute.SetFloat("EscapeMargin", boundary.mode == FluidBoundary.Mode.PotProfile
            ? spacing * escapeMarginSpacings / Mathf.Max(1e-6f, boundary.ContainerScale) : 1e9f);        fluidCompute.SetFloat("EscapeFarMargin", potMode
            ? spacing * escapeFarSpacings / Mathf.Max(1e-6f, boundary.ContainerScale) : 1e9f);
        fluidCompute.SetFloat("GroundLifetime", groundLifetime);
        // 待避先は領域の外。CellCoord が領域外になるので近傍探索にも密度場にも入らない。
        fluidCompute.SetVector("RetiredPark", regionCenter + Vector3.down * (regionSize.y * 0.5f + 50f));
        fluidCompute.SetVector("SpawnBoxMin", spawnBoxMin);
        fluidCompute.SetVector("SpawnBoxSize", spawnBoxSize);
        fluidCompute.SetVector("SpawnVelocity", spawnVelocity);
        fluidCompute.SetFloat("SpawnChance", Mathf.Clamp01(spawnChance));
        fluidCompute.SetFloat("WallTolerance", boundary.mode == FluidBoundary.Mode.PotProfile
            ? spacing * 0.5f / Mathf.Max(1e-6f, boundary.ContainerScale) : 0f);
        fluidCompute.SetFloat("FloorTolerance", boundary.mode == FluidBoundary.Mode.PotProfile && boundary.Profile != null
            ? (boundary.Profile.RimY - boundary.Profile.FloorY) * 0.35f : 0f);
    }

    /// <summary>SafetyCorrection の発動数を GPU から読み戻す（Debug 用、同期読み取り）。</summary>
    public void ReadSafetyCounters()
    {
        if (safetyCounters == null) return;
        safetyCounters.GetData(safetyRead);
        SafetyCorrectionCount = (int)safetyRead[0];
        SafetyConsecutiveFrames = SafetyCorrectionCount > 0 ? SafetyConsecutiveFrames + 1 : 0;
        safetyCounters.SetData(new uint[4]);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.35f);
        Gizmos.DrawWireCube(regionCenter, regionSize);
    }
#endif
}
