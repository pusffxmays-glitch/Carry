using UnityEngine;

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
public class FluidCore : MonoBehaviour
{
    [Header("Simulation")]
    public int particleCount = 16384;
    [Tooltip("PBF の密度投影反復数 (§7)。")]
    [Range(1, 10)] public int solverIterations = 4;
    [Range(1, 8)] public int minSubSteps = 2;
    [Range(1, 16)] public int maxSubSteps = 10;

    [Header("Material")]
    [Range(1.5f, 3f)] public float kernelRadiusScale = 2f;
    [Tooltip("XSPH 粘性。水 < ポーション < シロップ (§8)。")]
    [Range(0f, 1f)] public float viscosity = 0.28f;
    [Tooltip("境界粘性 (§2 の補正項)。0 = 完全スリップ、大 = ノースリップ。容器の回転が中身に伝わるかを決める。")]
    [Range(0f, 2f)] public float boundaryViscosity = 0.55f;
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
    [Range(0f, 2f)] public float boundaryPressureScale = 1f;
    [Tooltip("位置補正の緩和係数 (SOR)。これが無いと補正が行き過ぎて毎サブステップでエネルギーが注入される。")]
    [Range(0.02f, 1f)] public float solverRelaxation = 0.12f;
    [Range(0.05f, 1f)] public float maxDeltaPPerSpacing = 0.25f;
    public float maxSpeed = 8f;

    [Header("Fill / region")]
    [Tooltip("容器の内容積に対する初期充填率。")]
    [Range(0.05f, 0.95f)] public float fillFraction = 0.45f;
    [Tooltip("シミュレーション領域の余白 (m)。容器の周囲にこれだけ広げる。Overflow の落下先もここに入る必要がある。")]
    public float simPadding = 0.45f;
    [Range(0f, 0.5f)] public float boundsRestitution = 0.02f;
    [Range(0f, 1f)] public float boundsFriction = 0.15f;

    [Header("Refs")]
    public ComputeShader fluidCompute;

    [Tooltip("テストハーネスが明示的な dt で駆動できるようにするためのスイッチ。")]
    public bool autoStep = true;

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
    /// <summary>SafetyCorrection の発動粒子数（直近の読み取り時点）。常態化していたら壁の扱いが破綻しているサイン (§10)。</summary>
    public int SafetyCorrectionCount { get; private set; }
    public int SafetyConsecutiveFrames { get; private set; }
    public int SeededParticles { get; private set; }
    public FluidBoundary Boundary => boundary;
    /// <summary>流体が存在しうる World 空間の領域。容器に追従する。</summary>
    public Bounds SimBounds => new Bounds(regionCenter, regionSize);

    FluidBoundary boundary;
    GraphicsBuffer positions, predicted, velocities, deltaP, normals, safety;
    GraphicsBuffer densities, lambdas;
    GraphicsBuffer boundaryLocal, boundaryPositions, boundaryVelocities, boundaryVolumes;
    GraphicsBuffer sortPositions, cellCounts, cellStart, cellCursor, blockSums, sortedIndices;
    GraphicsBuffer potProfile, safetyCounters;
    uint[] safetyRead = new uint[4];

    int fluidCount, boundaryCount, totalCount;
    float spacing, kernelRadius, restDensity, particleVolume;
    float relaxationEps, artificialPressure, refSumGradSq;

    Vector3 regionCenter, regionSize;
    Vector3Int gridSize;
    Vector3 gridOrigin;
    float cellSize;
    int cellTotal, blockCount;

    int kUpdateBoundary, kClearCounts, kBuildSortPos, kCount, kScanLocal, kScanBlocks, kScanAdd, kScatter;
    int kIntegrate, kDensityLambda, kDeltaP, kApplyDeltaP, kVelocity, kNormals, kViscTension, kFinalize;

    const int Threads = 256;
    const int ScanBlock = 256;
    const int MaxCells = 262144;

    void OnEnable() { Initialise(); }
    void OnDisable() { Release(); }

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

        fluidCount = Mathf.Max(Threads, particleCount);
        ComputeScales();
        BuildBoundaryBuffers();
        AllocateBuffers();
        SeedFluid();
        BuildGrid();
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

        Vector3 ext = Vector3.one;
        if (boundary.mode == FluidBoundary.Mode.Box) ext = boundary.boxInnerSize;
        else if (boundary.Profile != null)
        {
            float sc = boundary.ContainerScale;
            float r = boundary.Profile.MaxRadius * sc;
            float hgt = (boundary.Profile.RimY - boundary.Profile.FloorY) * sc;
            ext = new Vector3(r * 2f, hgt, r * 2f);
        }
        regionSize = ext + Vector3.one * (simPadding * 2f);
        regionCenter = boundary.Container.position;
        cellSize = kernelRadius;
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

        potProfile?.Release();
        if (boundary.mode == FluidBoundary.Mode.PotProfile && boundary.Profile != null)
        {
            var arr = boundary.Profile.GetProfileArray();
            potProfile = new GraphicsBuffer(GraphicsBuffer.Target.Structured, arr.Length, sizeof(float));
            potProfile.SetData(arr);
        }
        else
        {
            potProfile = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(float));
            potProfile.SetData(new float[] { 1f, 1f });
        }
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
        regionCenter = boundary.Container.position;
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
        Vector3 fallback = boundary.Container.position;
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
        safetyCounters?.Release(); safetyCounters = null;
    }

    void LateUpdate() { if (autoStep) Step(Time.deltaTime); }

    public void Step(float dt)
    {
        Initialise();
        if (!IsReady || dt <= 0f) return;
        dt = Mathf.Min(dt, 1f / 20f);

        boundary.SampleMotion(dt);
        UpdateGridOrigin();

        // §3 CFL: 壁も流体も、1 サブステップで粒子間隔の一定割合以上動かさない。
        // 壁がそれより速く動くと、境界粒子が流体を貫通して飲み込む。
        float containerSpeed = boundary.LinearVelocity.magnitude
                             + boundary.AngularVelocity.magnitude * regionSize.magnitude * 0.5f;
        float worstSpeed = Mathf.Max(maxSpeed, containerSpeed);
        int sub = Mathf.Clamp(Mathf.CeilToInt(worstSpeed * dt / (0.4f * spacing)), minSubSteps, maxSubSteps);
        LastSubStepCount = sub;

        float sdt = dt / sub;
        for (int s = 0; s < sub; s++) SubStep(sdt, (s + 1) / (float)sub);
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
        Bind(kCount, ("SortPositions", sortPositions), ("CellCounts", cellCounts));
        Bind(kScanLocal, ("CellCounts", cellCounts), ("CellStart", cellStart), ("BlockSums", blockSums));
        Bind(kScanBlocks, ("BlockSums", blockSums));
        Bind(kScanAdd, ("CellStart", cellStart), ("CellCursor", cellCursor), ("BlockSums", blockSums));
        Bind(kScatter, ("SortPositions", sortPositions), ("CellCursor", cellCursor), ("SortedIndices", sortedIndices));

        Bind(kIntegrate, ("Positions", positions), ("PredictedPositions", predicted),
                         ("Velocities", velocities), ("SafetyCorrection", safety));

        Bind(kDensityLambda, ("PredictedPositions", predicted), ("Densities", densities), ("Lambdas", lambdas),
                             ("SortPositionsIn", sortPositions), ("CellStartIn", cellStart),
                             ("CellCountsIn", cellCounts), ("SortedIndicesIn", sortedIndices),
                             ("BoundaryVolumes", boundaryVolumes));

        Bind(kDeltaP, ("PredictedPositions", predicted), ("DeltaP", deltaP), ("LambdasIn", lambdas),
                      ("SortPositionsIn", sortPositions), ("CellStartIn", cellStart),
                      ("CellCountsIn", cellCounts), ("SortedIndicesIn", sortedIndices),
                      ("BoundaryVolumes", boundaryVolumes));

        Bind(kApplyDeltaP, ("PredictedPositions", predicted), ("DeltaP", deltaP));

        Bind(kVelocity, ("PredictedPositions", predicted), ("Positions", positions),
                        ("SafetyCorrection", safety), ("DeltaP", deltaP));

        Bind(kNormals, ("Normals", normals), ("PredictedIn", predicted), ("DensitiesIn", densities),
                       ("SortPositionsIn", sortPositions), ("CellStartIn", cellStart),
                       ("CellCountsIn", cellCounts), ("SortedIndicesIn", sortedIndices));

        Bind(kViscTension, ("Velocities", velocities), ("PredictedIn", predicted), ("VelocityIn", deltaP),
                           ("NormalsIn", normals), ("DensitiesIn", densities),
                           ("SortPositionsIn", sortPositions), ("CellStartIn", cellStart),
                           ("CellCountsIn", cellCounts), ("SortedIndicesIn", sortedIndices),
                           ("BoundaryVelocities", boundaryVelocities), ("BoundaryVolumes", boundaryVolumes));

        Bind(kFinalize, ("PredictedPositions", predicted), ("Positions", positions), ("Velocities", velocities),
                        ("PotProfileBuf", potProfile), ("SafetyCounters", safetyCounters));

        fluidCompute.SetInt("FluidCount", fluidCount);
        fluidCompute.SetInt("TotalCount", totalCount);
        fluidCompute.SetInt("BoundaryCount", boundaryCount);
        fluidCompute.SetInt("CellTotal", cellTotal);
        fluidCompute.SetInt("BlockCount", blockCount);

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
        fluidCompute.SetFloat("CohesionStrength", cohesionStrength);
        fluidCompute.SetFloat("CurvatureStrength", curvatureStrength);
        fluidCompute.SetFloat("MaxSpeed", maxSpeed);

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
            fluidCompute.SetFloat("PotMaxRadius", prof.MaxRadius);
            fluidCompute.SetFloat("SafetyMargin", spacing / boundary.ContainerScale * 0.25f);
            Matrix4x4 m = boundary.InterpolatedMatrix(lerpT);
            fluidCompute.SetMatrix("WorldToPotSafety", m.inverse);
            fluidCompute.SetMatrix("PotToWorldSafety", m);
        }
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
