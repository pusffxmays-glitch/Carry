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
    [Tooltip("PBF の密度投影反復数 (§7)。")]
    [Range(1, 10)] public int solverIterations = 4;
    // Phase 12 実測: サブステップ数はソルバーの収束にも効く。適応 CFL だけに任せて
    // 3 まで落とすと、静止時の液面が 0.189 -> 0.287、平均速さが 0.005 -> 0.589 m/s に
    // 悪化した（＝落ち着かない）。下限を 6 に固定すると 10 と同等の品質を保ったまま
    // 静止時の物理コストが 17.1 -> 10.4 ms/frame になる。
    [Range(1, 12)] public int minSubSteps = 6;
    // 上限が低いと CFL を満たせないフレームが出て、そこで流体が発散する。
    // 実測（急な往復+回転）: 必要 12 に対し上限 10 で、120 フレーム中 118 が CFL 違反、
    // 流体が速度クランプ 8m/s に張り付いて描画が崩れた。
    // 速度クランプ maxSpeed=8 のとき、dt=1/60 で必要なサブステップは
    // 8 * (1/60) / (0.4 * spacing) ≒ 12。上限はそれを上回っている必要がある。
    // 静かなときは適応 CFL が 6 まで落とすので、常時のコストは増えない。
    [Range(1, 32)] public int maxSubSteps = 20;
    [Tooltip("CFL に使う実測最大速度への安全率。実測値は 1 フレーム前のものなので、急加速に備えて余裕を持たせる。")]
    [Range(1f, 4f)] public float cflSpeedMargin = 1.6f;

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
    // 壺内 (容器基準ゲート内) 限定の速度クランプ。負なら maxSpeed と同じ。
    // おろし/拾い/熱い床ジャンプ中に GoblinPotActions が一時的に絞る (calm)。
    // 壺外の液滴・水たまりには適用しないので、こぼれがスローモーションにならない。
    [HideInInspector] public float maxSpeedInPot = -1f;

    // ---- 追補 26: パリー回収 & 着地ジョルト ----
    float recallUntil = -999f, recallStrengthValue;
    Vector3 joltDeltaV;
    int joltFrame = -1;
    /// <summary>パリー成功時: 壺の近くではみ出している粒子を seconds の間、口へ吸い戻す。</summary>
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

    // ADDED 2026-08-15 (バグ報告「ギミックのブロックにこぼれたポーションがつかない。
    // 貫通して地面まで落ちている」): 流体の衝突相手は壺の境界粒子・地面平面 (groundY)・
    // 領域外周だけで、ステージの箱はシミュレーションに存在しなかった。GroundSurface 付きの
    // BoxCollider を集めてシェーダに渡し、こぼれた液体が上面に着いたらその場で水たまりに
    // する (Ground 集計に入り、groundLifetime で消える。地面と同じ扱い)。
    [Header("Solid obstacles")]
    [Tooltip("GroundSurface 付きの BoxCollider を流体の衝突対象にする。こぼれた液体がギミックの上に水たまりとして残る。")]
    public bool collideWithGroundSurfaces = true;

    [Header("Refs")]
    public ComputeShader fluidCompute;

    [Tooltip("テストハーネスが明示的な dt で駆動できるようにするためのスイッチ。")]
    public bool autoStep = true;
    // §16 は非同期リードバックを指定しており、実装もしてある（下の false 経路）。
    // ただし **非同期にすると Play 中に FluidCore を無効化→有効化しただけで
    // エディタが固まる**（最小再現で確認）。保留中の読み戻しとバッファ解放の
    // 組み合わせが原因と見ているが、まだ特定できていない。
    // エディタが固まる状態は出荷できないので、既定は同期に戻してある。
    // 非同期の効果自体は実測済み（Step() の CPU コスト 13.2ms -> 0.12ms）なので、
    // 原因を特定したら既定を false に戻す。OPEN_ISSUES.md の OI-4 を参照。
    [Tooltip("領域カウンタを同期読み戻しする。false にすると CPU が GPU を待たなくなるが、現在は再初期化でエディタが固まる不具合がある (OI-4)。")]
    public bool synchronousReadback = true;

    // ---- public state ----
    public bool IsReady => positions != null;
    public GraphicsBuffer PositionsBuffer => positions;
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
    public int RecoverableCount => InsideCount + RimCount + (AirborneCount - EscapedCount);

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
    BoxCollider[] solidBoxCols;
    bool[] solidBoxSettle;
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

    int kUpdateBoundary, kClearCounts, kBuildSortPos, kCount, kScanLocal, kScanBlocks, kScanAdd, kScatter;
    int kIntegrate, kDensityLambda, kDeltaP, kApplyDeltaP, kVelocity, kNormals, kViscTension, kFinalize, kClassify;
    int kTeleport, kSolidBoxCollide;

    const int Threads = 256;
    const int ScanBlock = 256;
    const int MaxCells = 262144;

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
        if (fluidCompute == null)
        {
            Debug.LogError("FluidCore: fluidCompute (Assets/Shaders/Fluid/FluidCore.compute) が未割り当てです。", this);
            enabled = false;
            return;
        }
        boundary = GetComponent<FluidBoundary>();

        kUpdateBoundary = fluidCompute.FindKernel("UpdateBoundary");
        kClearCounts = fluidCompute.FindKernel("ClearCellCounts");
        kBuildSortPos = fluidCompute.FindKernel("BuildSortPositions");
        kCount = fluidCompute.FindKernel("CountParticlesPerCell");
        kScanLocal = fluidCompute.FindKernel("ScanLocal");
        kScanBlocks = fluidCompute.FindKernel("ScanBlockSums");
        kScanAdd = fluidCompute.FindKernel("ScanAddOffsets");
        kScatter = fluidCompute.FindKernel("ScatterParticles");
        kIntegrate = fluidCompute.FindKernel("Integrate");
        kDensityLambda = fluidCompute.FindKernel("ComputeDensityLambda");
        kDeltaP = fluidCompute.FindKernel("ComputeDeltaP");
        kApplyDeltaP = fluidCompute.FindKernel("ApplyDeltaP");
        kVelocity = fluidCompute.FindKernel("ComputeVelocity");
        kNormals = fluidCompute.FindKernel("ComputeNormals");
        kViscTension = fluidCompute.FindKernel("ApplyViscosityTension");
        kFinalize = fluidCompute.FindKernel("Finalize");
        kClassify = fluidCompute.FindKernel("ClassifyRegions");
        kTeleport = fluidCompute.FindKernel("TeleportFluid");
        kSolidBoxCollide = fluidCompute.FindKernel("SolidBoxCollide");

        fluidCount = Mathf.Max(Threads, particleCount);
        ComputeScales();
        BuildBoundaryBuffers();
        AllocateBuffers();
        GatherSolidBoxes();
        SeedFluid();
        BuildGrid();
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
        if (boundary.LocalPositions == null) boundary.Build(0.05f, 0.1f);

        float fluidVolume = boundary.InteriorVolumeWorld * fillFraction;
        particleVolume = fluidVolume / fluidCount;
        spacing = Mathf.Pow(particleVolume * Mathf.Sqrt(2f), 1f / 3f);
        kernelRadius = spacing * kernelRadiusScale;
        restDensity = particleVolume * IdealLatticeKernelSum(spacing, kernelRadius);
        refSumGradSq = IdealLatticeGradSq(spacing, kernelRadius, particleVolume, restDensity);
        relaxationEps = Mathf.Max(1e-6f, relaxationFraction * refSumGradSq);
        artificialPressure = artificialPressureFraction * (0.1f / Mathf.Max(1e-9f, refSumGradSq));

        // 本番の間隔で境界を作り直す。
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
    void GatherSolidBoxes()
    {
        var cols = new List<BoxCollider>();
        var settle = new List<bool>();
        if (collideWithGroundSurfaces)
        {
            foreach (var gs in FindObjectsByType<GroundSurface>(FindObjectsSortMode.None))
            {
                var bc = gs.GetComponent<BoxCollider>();
                if (bc == null) continue;
                cols.Add(bc);
                // 動く床 (揺れる橋) の上で定着させると、凍結した水たまりが床の動きに
                // 置いて行かれて空中に残る。衝突はするが定着はしない。
                settle.Add(gs.GetComponentInParent<SwayingBridge>() == null);
            }
        }
        solidBoxCols = cols.ToArray();
        solidBoxSettle = settle.ToArray();
        solidBoxCount = solidBoxCols.Length;

        int n = Mathf.Max(1, solidBoxCount);
        solidW2LArr = new Matrix4x4[n];
        solidL2WArr = new Matrix4x4[n];
        solidHalfArr = new Vector4[n];
        solidBoxW2L?.Release(); solidBoxL2W?.Release(); solidBoxHalf?.Release();
        solidBoxW2L = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(float) * 16);
        solidBoxL2W = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(float) * 16);
        solidBoxHalf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(float) * 4);
        UpdateSolidBoxes();
    }

    // 行列を毎フレーム更新する (揺れる橋が動くため)。~10 箱の SetData なので実測不能なコスト。
    void UpdateSolidBoxes()
    {
        if (solidBoxW2L == null) return;
        for (int i = 0; i < solidBoxCount; i++)
        {
            var bc = solidBoxCols[i];
            if (bc == null)
            {
                solidHalfArr[i] = Vector4.zero;   // half=0 -> シェーダ側で必ず「接触なし」
                continue;
            }
            var t = bc.transform;
            // スケールは half extents に畳み、行列は回転+平行移動だけにする。
            // スケール入りの行列で最小貫通軸を選ぶと、軸ごとに距離の尺度が違って
            // 押し出し方向を誤る。
            Vector3 centre = t.TransformPoint(bc.center);
            Matrix4x4 l2w = Matrix4x4.TRS(centre, t.rotation, Vector3.one);
            Vector3 ls = t.lossyScale;
            Vector3 half = Vector3.Scale(bc.size * 0.5f,
                               new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)))
                         + Vector3.one * (spacing * 0.5f);   // 粒子半径ぶんのマージン
            solidL2WArr[i] = l2w;
            solidW2LArr[i] = l2w.inverse;
            solidHalfArr[i] = new Vector4(half.x, half.y, half.z, solidBoxSettle[i] ? 1f : 0f);
        }
        solidBoxW2L.SetData(solidW2LArr);
        solidBoxL2W.SetData(solidL2WArr);
        solidBoxHalf.SetData(solidHalfArr);
    }

    void BuildGrid()
    {
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

    void LateUpdate() { if (autoStep) Step(Time.deltaTime); }

    public void Step(float dt)
    {
        Initialise();
        if (!IsReady || dt <= 0f) return;
        dt = Mathf.Min(dt, 1f / 20f);

        boundary.SampleMotion(dt);
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
        float fluidSpeed = Mathf.Min(MeasuredMaxSpeed * cflSpeedMargin, maxSpeed);
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
        if (worstSpeed * dt > maxTravel * maxSubSteps)
            dt = maxTravel * maxSubSteps / Mathf.Max(worstSpeed, 1e-6f);

        LastBoundaryTravel = containerSpeed * dt;
        int need = Mathf.CeilToInt(worstSpeed * dt / maxTravel);
        int sub = Mathf.Clamp(need, minSubSteps, maxSubSteps);
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

        float sdt = dt / sub;
        for (int s = 0; s < sub; s++) SubStep(sdt, (s + 1) / (float)sub);

        // 領域分類はフレームに 1 回。観測のみで、位置・速度には触れない (§14/追加修正1)。
        ClassifyAndRead();
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
        BindAll(dt, lerpT);

        int fluidGroups = Mathf.CeilToInt(fluidCount / (float)Threads);
        int totalGroups = Mathf.CeilToInt(totalCount / (float)Threads);
        int boundaryGroups = Mathf.CeilToInt(boundaryCount / (float)Threads);
        int cellGroups = Mathf.CeilToInt(cellTotal / (float)Threads);

        fluidCompute.Dispatch(kUpdateBoundary, boundaryGroups, 1, 1);
        fluidCompute.Dispatch(kIntegrate, fluidGroups, 1, 1);

        fluidCompute.Dispatch(kBuildSortPos, totalGroups, 1, 1);
        fluidCompute.Dispatch(kClearCounts, cellGroups, 1, 1);
        fluidCompute.Dispatch(kCount, totalGroups, 1, 1);
        fluidCompute.Dispatch(kScanLocal, blockCount, 1, 1);
        fluidCompute.Dispatch(kScanBlocks, 1, 1, 1);
        fluidCompute.Dispatch(kScanAdd, cellGroups, 1, 1);
        fluidCompute.Dispatch(kScatter, totalGroups, 1, 1);

        for (int it = 0; it < solverIterations; it++)
        {
            fluidCompute.Dispatch(kDensityLambda, fluidGroups, 1, 1);
            fluidCompute.Dispatch(kDeltaP, fluidGroups, 1, 1);
            fluidCompute.Dispatch(kApplyDeltaP, fluidGroups, 1, 1);
        }

        fluidCompute.Dispatch(kVelocity, fluidGroups, 1, 1);
        fluidCompute.Dispatch(kNormals, fluidGroups, 1, 1);
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

    // カーネルごとに必要なバッファだけをバインドする。読むだけのバッファは SRV 別名で渡し、
    // 同一カーネルに RW/SRV の両方を渡さない（D3D11 の UAV スロット上限 8 に収めるため）。
    void BindAll(float dt, float lerpT)
    {
        Bind(kUpdateBoundary, ("BoundaryLocal", boundaryLocal),
                              ("BoundaryPositionsRW", boundaryPositions),
                              ("BoundaryVelocitiesRW", boundaryVelocities));

        Bind(kClearCounts, ("CellCounts", cellCounts));
        Bind(kBuildSortPos, ("SortPositions", sortPositions), ("PredictedPositions", predicted),
                            ("BoundaryPositions", boundaryPositions));
        Bind(kCount, ("SortPositions", sortPositions), ("CellCounts", cellCounts),
                     ("RetiredFlagsIn", retiredFlags));
        Bind(kScanLocal, ("CellCounts", cellCounts), ("CellStart", cellStart), ("BlockSums", blockSums));
        Bind(kScanBlocks, ("BlockSums", blockSums));
        Bind(kScanAdd, ("CellStart", cellStart), ("CellCursor", cellCursor), ("BlockSums", blockSums));
        Bind(kScatter, ("SortPositions", sortPositions), ("CellCursor", cellCursor), ("SortedIndices", sortedIndices),
                       ("RetiredFlagsIn", retiredFlags));

        Bind(kIntegrate, ("Positions", positions), ("PredictedPositions", predicted),
                         ("Velocities", velocities), ("SafetyCorrection", safety));

        Bind(kDensityLambda, ("PredictedPositions", predicted), ("Densities", densities), ("Lambdas", lambdas),
                             ("SortPositionsIn", sortPositions), ("CellStartIn", cellStart),
                             ("CellCountsIn", cellCounts), ("SortedIndicesIn", sortedIndices),
                             ("BoundaryVolumes", boundaryVolumes), ("RetiredFlagsIn", retiredFlags));

        Bind(kDeltaP, ("PredictedPositions", predicted), ("DeltaP", deltaP), ("LambdasIn", lambdas),
                      ("SortPositionsIn", sortPositions), ("CellStartIn", cellStart),
                      ("CellCountsIn", cellCounts), ("SortedIndicesIn", sortedIndices),
                      ("BoundaryVolumes", boundaryVolumes), ("RetiredFlagsIn", retiredFlags));

        Bind(kApplyDeltaP, ("PredictedPositions", predicted), ("DeltaP", deltaP));

        Bind(kVelocity, ("PredictedPositions", predicted), ("Positions", positions),
                        ("SafetyCorrection", safety), ("DeltaP", deltaP));

        Bind(kNormals, ("Normals", normals), ("PredictedIn", predicted), ("DensitiesIn", densities),
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

        fluidCompute.SetFloat("DeltaTime", dt);
        fluidCompute.SetVector("Gravity", Physics.gravity);   // §2: これが唯一の外力

        // 動く境界: サブステップ間で姿勢を補間する (§3)。補間しないと壁が瞬間移動して
        // 流体を弾き飛ばし、エネルギーを注入する。
        fluidCompute.SetMatrix("BoundaryToWorld", boundary.InterpolatedMatrix(lerpT));
        fluidCompute.SetVector("ContainerLinearVelocity", boundary.LinearVelocity);
        fluidCompute.SetVector("ContainerAngularVelocity", boundary.AngularVelocity);
        fluidCompute.SetVector("ContainerCenter", boundary.InterpolatedCenter(lerpT));

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
        fluidCompute.SetFloat("ViscosityXSPH", viscosity);
        fluidCompute.SetFloat("BoundaryViscosity", boundaryViscosity);
        // 粘性のブレンド率をサブステップ数から独立させるための基準時間刻み。
        // 係数はこの dt のときの 1 ステップ分の効きを表す。
        fluidCompute.SetFloat("ViscosityRefStep", viscosityRefStep);
        fluidCompute.SetFloat("CohesionStrength", cohesionStrength);
        fluidCompute.SetFloat("CurvatureStrength", curvatureStrength);
        fluidCompute.SetFloat("MaxSpeed", maxSpeed);
        fluidCompute.SetFloat("MaxSpeedPot", maxSpeedInPot > 0f ? maxSpeedInPot : maxSpeed);
        // 追補 26: パリー回収 & 着地ジョルト
        Vector3 potPos = boundary != null ? boundary.Container.position : Vector3.zero;
        Vector3 potUp = boundary != null ? boundary.Container.up : Vector3.up;
        fluidCompute.SetFloat("RecallStrength", Time.time < recallUntil ? recallStrengthValue : 0f);
        fluidCompute.SetVector("RecallTarget", potPos + potUp * 0.85f);
        fluidCompute.SetFloat("RecallMinY", potPos.y - 0.6f);
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
            Matrix4x4 m = boundary.InterpolatedMatrix(lerpT);
            fluidCompute.SetMatrix("WorldToPotSafety", m.inverse);
            fluidCompute.SetMatrix("PotToWorldSafety", m);
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
            ? spacing * escapeMarginSpacings / Mathf.Max(1e-6f, boundary.ContainerScale) : 1e9f);
        fluidCompute.SetFloat("GroundLifetime", groundLifetime);
        // 待避先は領域の外。CellCoord が領域外になるので近傍探索にも密度場にも入らない。
        fluidCompute.SetVector("RetiredPark", regionCenter + Vector3.down * (regionSize.y * 0.5f + 50f));
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
