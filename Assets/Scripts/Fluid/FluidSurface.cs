using UnityEngine;
using UnityEngine.Rendering;

// ============================================================================================
// FluidSurface -- Particle -> 3D Density Field -> Iso Surface -> Liquid Mesh (Phase 2/3)
//
// FLUID_DESIGN.md §11 / §12 / §13 / §14。
// 粒子は一切描かない。粒子位置から密度場を作り、その等値面だけをレンダリングする。
//
// Physics Resolution（粒子数）と Visual Resolution（ボクセル解像度）は完全に独立している
// (§13)。voxelsPerSpacing を上げれば、粒子数を変えずに表面だけ精細になる。
// ============================================================================================
[RequireComponent(typeof(FluidCore))]
[DefaultExecutionOrder(200)]     // FluidCore(100) の後
public class FluidSurface : MonoBehaviour
{
    [Header("Visual resolution (§13: 物理解像度とは独立)")]
    [Tooltip("粒子間隔あたりのボクセル数。上げるほど表面が精細になる。液だれ・液滴が消える場合はここを上げる (§14/追加修正3)。")]
    [Range(1.5f, 6f)] public float voxelsPerSpacing = 3f;
    [Tooltip("密度カーネル半径。粒子間隔の倍数。大きいほど滑らかで太い表面になる。")]
    [Range(0.8f, 3f)] public float splatRadiusPerSpacing = 1.5f;
    // 追補 36 (2026-08-22 バグ報告「こぼれた見た目の量とゲージの残量が一致しない」)。
    // 孤立した粒子は 1 個が実体積より大きい球として描かれるため、こぼれた量が
    // 実際の損失より多く見えていた。壺の中の見え方は変えずに、こぼれた分だけ絞る。
    [Tooltip("こぼれた液体 (壺の外) の描画半径倍率。1 = 従来。下げるとこぼれた見た目の量が実際の損失量に近づく。")]
    [Range(0.4f, 1f)] public float escapedSplatScale = 0.75f;
    [Tooltip("等値面のしきい値。下げると液体が太く、上げると細くなる。")]
    [Range(0.02f, 3f)] public float isoValue = 0.45f;
    [Tooltip("密度場の平滑化回数 (§14)。0 で無効。")]
    [Range(0, 4)] public int smoothingPasses = 2;
    [Tooltip("法線を取るときの中央差分の幅。ボクセルサイズの倍数。大きいほど滑らかだが細部が鈍る。")]
    [Range(0.5f, 3f)] public float normalEpsVoxels = 1.2f;
    [Tooltip("固定小数の分解能 (§11/修正3)。小さすぎると量子化で表面が粒状になる。")]
    public float densityFixedPointScale = 16384f;

    [Header("Domain (§14 Sparse Brick Pool)")]
    [Tooltip("密度場のドメインの広さ (m)。この範囲に入る液体はどこにあっても描画される。メモリはドメインの体積ではなく **実際に液体がある Brick の数** に比例するので、広げても VRAM もフレーム時間も増えない。")]
    public Vector3 domainSize = new Vector3(24f, 4.5f, 24f);
    [Tooltip("Brick プールの容量。1 Brick = 8^3 voxel。壺だけなら 3000 程度。地面に広く撒くと増える。超えると液体が虫食いになるので警告を出す。")]
    public int poolBrickCapacity = 16384;

    [Header("Capacity (§13/修正9)")]
    // 地面に散った液滴は 1 粒ずつ独立した閉曲面になるので、こぼすほど三角形が増える。
    // 実測: 走り + 旋回 + ジャンプを 12 秒続けて地面 13532 粒のとき 226 万枚
    // （1 粒あたり約 167 枚）。90 万枚では 52 万枚が捨てられて液滴が虫食いになった。
    // なお枚数はこぼれた量に比例するので、こぼれ過ぎ自体が直れば自然に下がる。
    [Tooltip("生成できる三角形の上限。超過分は書き込まずカウンタに積む。容量不足を理由に簡易表現へ切り替えることはしない。")]
    public int maxTriangles = 2400000;

    [Header("Material")]
    public Material liquidMaterial;
    public Shader liquidShader;

    [Header("Container clip")]
    // 等値面は最外周の粒子から Splat 半径ぶん外へふくらむ（実測 57.4mm）。壺の壁には
    // それより薄い所があるので、切らないと液体が側面や底を突き抜けて描画される
    // （走ると外側に、ジャンプすると下側にはみ出して見える）。
    // 粒子は内壁から最大 11.4mm しか出ていないので、これは物理ではなく描画側の問題。
    [Tooltip("壺の壁と底の中にある密度を 0 にする。切ると液体が壺を突き抜けて見える。")]
    public bool clipToContainer = true;

    [Header("Debug")]
    public bool logCapacity = false;

    public int LastTriangleCount { get; private set; }
    public int LastOverflowCount { get; private set; }
    public Vector3Int VoxelDims => voxelDims;
    public float VoxelSize => voxelSize;
    public bool IsReady => vertexBuffer != null;

    FluidCore core;
    ComputeShader cs;
    [Header("Refs")]
    public ComputeShader surfaceCompute;

    GraphicsBuffer vertexBuffer, argsBuffer, argsSrc, counters;
    GraphicsBuffer brickSlot, activeBricks;
    GraphicsBuffer allocCounter, brickArgs, brickArgsSrc;
    GraphicsBuffer poolAccum, poolA, poolB;
    GraphicsBuffer potInner, potOuter;
    Vector3Int voxelDims, brickDims;
    int brickTotal;
    uint[] allocRead = new uint[2];
    uint[] allocReset = new uint[2];
    Vector3 fieldOrigin;
    float voxelSize, splatRadius;
    MaterialPropertyBlock mpb;
    uint[] counterReset = new uint[4];
    uint[] counterRead = new uint[4];

    int kResetSlots, kClearSlots, kMark, kBrickArgs, kClearPool, kSplat, kDecode, kBlur, kBuild, kDrawArgs, kNormals, kMaskSolid;
    const int Threads = 256;
    const int Threads3 = 4;
    const int Brick = 8;        // FluidSurface.compute の BRICK と一致させること

    // 粒子の影響が届く voxel 半径。この範囲に触れる Brick だけを実体化する。
    //
    //   Splat 半径     = splatRadiusPerSpacing * voxelsPerSpacing voxel。
    //                    密度カーネルはここで厳密に 0 になるので、これより外に密度は無い。
    //   等値面 + 法線   = Marching Cubes がセルの +1 を読み、法線が中央差分で
    //                    normalEpsVoxels ぶん外を読む。
    //
    // **Blur の到達ぶんは足さない。** Blur は「実体化されていない Brick は密度 0」と
    // して読むが、Splat 半径の外は実際に密度 0 なので、それが正しい値である。
    // 足すと 1 粒あたりの確保数が跳ね上がり、地面に散った液滴でプールを使い切って
    // 液体が虫食いになる（実測: 半径 13 voxel で地面 12232 粒のとき 39851 Brick 不足）。
    //
    // 逆にここを実際より狭くすると、密度を持つ Brick が実体化されず Brick 境界に
    // 継ぎ目が出る。狭めるときは必ず見た目を確認すること。
    int MarkRadiusVoxels =>
        Mathf.CeilToInt(splatRadius / Mathf.Max(voxelSize, 1e-6f))
        + Mathf.CeilToInt(normalEpsVoxels) + 2;

    static readonly int IdVerts = Shader.PropertyToID("_SurfaceVertices");
    static readonly int IdPool = Shader.PropertyToID("_Pool");
    static readonly int IdBrickSlot = Shader.PropertyToID("_BrickSlot");
    static readonly int IdVoxelDims = Shader.PropertyToID("_VoxelDims");
    static readonly int IdBrickDims = Shader.PropertyToID("_BrickDims");
    static readonly int IdVoxelSize = Shader.PropertyToID("_VoxelSize");
    static readonly int IdPoolCapacity = Shader.PropertyToID("_PoolCapacity");
    static readonly int IdFieldOrigin = Shader.PropertyToID("_FieldOrigin");
    static readonly int IdFieldSize = Shader.PropertyToID("_FieldSize");
    static readonly int IdIso = Shader.PropertyToID("_IsoValue");

    void OnEnable()
    {
        core = GetComponent<FluidCore>();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        Release();
    }

    void Release()
    {
        potInner?.Release(); potInner = null;
        potOuter?.Release(); potOuter = null;
        poolAccum?.Release(); poolAccum = null;
        poolA?.Release(); poolA = null;
        poolB?.Release(); poolB = null;
        brickSlot?.Release(); brickSlot = null;
        activeBricks?.Release(); activeBricks = null;
        allocCounter?.Release(); allocCounter = null;
        brickArgs?.Release(); brickArgs = null;
        brickArgsSrc?.Release(); brickArgsSrc = null;
        vertexBuffer?.Release(); vertexBuffer = null;
        argsBuffer?.Release(); argsBuffer = null;
        argsSrc?.Release(); argsSrc = null;
        counters?.Release(); counters = null;
    }

    bool Initialise()
    {
        if (vertexBuffer != null) return true;
        if (core == null) core = GetComponent<FluidCore>();
        if (surfaceCompute == null)
        {
            Debug.LogError("FluidSurface: surfaceCompute (Assets/Shaders/Fluid/FluidSurface.compute) が未割り当てです。", this);
            enabled = false;
            return false;
        }
        if (!core.IsReady) return false;

        cs = surfaceCompute;
        kResetSlots = cs.FindKernel("ResetSlots");
        kClearSlots = cs.FindKernel("ClearSlots");
        kMark = cs.FindKernel("MarkAndAllocate");
        kBrickArgs = cs.FindKernel("WriteBrickArgs");
        kClearPool = cs.FindKernel("ClearPool");
        kSplat = cs.FindKernel("SplatDensity");
        kDecode = cs.FindKernel("DecodeDensity");
        kBlur = cs.FindKernel("BlurDensity");
        kMaskSolid = cs.FindKernel("MaskSolid");
        kBuild = cs.FindKernel("BuildSurface");
        kDrawArgs = cs.FindKernel("WriteDrawArgs");
        kNormals = cs.FindKernel("ComputeVertexNormals");

        BuildField();

        // 頂点は position + normal の 6 float。
        // Append ではなく通常の Structured。三角形 1 枚を連続 3 スロットへ書くため
        // （Append だと他スレッドの Append が割り込んで頂点が別の三角形に混ざる）。
        vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                                          maxTriangles * 3, sizeof(float) * 6);
        // IndirectArguments のバッファへ Compute から直接書くのは環境依存なので、
        // Structured へ書いてから CopyBuffer で移す。
        // [0..3] = 描画引数 / [4..6] = 法線カーネルの DispatchIndirect 引数
        argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.CopyDestination,
                                        8, sizeof(uint));
        argsBuffer.SetData(new uint[] { 0, 1, 0, 0, 0, 1, 1, 0 });
        argsSrc = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource,
                                     8, sizeof(uint));
        argsSrc.SetData(new uint[] { 0, 1, 0, 0, 0, 1, 1, 0 });
        counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, sizeof(uint));

        if (liquidShader == null) liquidShader = Shader.Find("Custom/PotionLiquidSurface");
        if (liquidMaterial == null && liquidShader != null)
            liquidMaterial = new Material(liquidShader) { hideFlags = HideFlags.HideAndDontSave };
        if (mpb == null) mpb = new MaterialPropertyBlock();
        return true;
    }

    // 密度場のドメイン (§14 Sparse Brick Pool)。
    //
    // 以前は「ドメイン全体ぶんの密な 3D テクスチャ」を持っていた。メモリがドメインの
    // 体積に比例するため、壺の周り半径 1.8m 程度の箱しか持てず、その結果
    // こぼした液体が少し離れると描画されなくなり、箱の縁で等値面がスパッと切れて
    // 四角い境界線として見えていた（OI-3 / OI-5）。
    //
    // プール方式ではメモリが **実際に液体がある Brick の数** に比例する。
    // ドメインを 24m 角へ広げても増えるのは Brick 索引表 (1 Brick あたり 4 バイト) だけで、
    // 毎フレームのコストは液体の量にしか比例しない。
    void BuildField()
    {
        voxelSize = core.ParticleSpacing / Mathf.Max(1.5f, voxelsPerSpacing);
        splatRadius = core.ParticleSpacing * splatRadiusPerSpacing;

        // FIXED 2026-08-17 (バグ報告「ゲージは100%だがツボの中のポーションが見えない」):
        // 縦の広さは domainSize.y (固定 4.5m) ではなく SimBounds (地面〜壺上端) から取る。
        // フィールドの Y 原点は地面 (SimBounds の底) に固定されているため、groundY を
        // ステージの川底 (-4.3) へ下げた際、固定 4.5m ではフィールドが y≈-4.5〜0 になり、
        // 橋の上の壺 (y≈3.8〜4.9) が天井からはみ出して中身が描画されなくなっていた
        // (地面に落ちたこぼれはフィールド内なので見える = まさに報告どおりの症状)。
        // +1m は登坂ヘッドルーム。SimBounds は regionGrowStep (0.5m) 刻みで伸びるので、
        // 余裕を持たせて再確保 (LateUpdate のチェック) の頻度を下げる。
        // 2026-08-27 (実機動画で特定した数秒ヒッチの正体):
        // ヘッドルーム 1m ではジャンプ (壺 +2m + 旋回半径 + 領域の 0.5m 刻み) で
        // 必ず不足し、**ジャンプのたびに BuildField (ブリック索引 ~15M 個 = 59MB の
        // 再確保 + 全クリア) が複数回発火**して数秒のヒッチになる。動画のコンソールに
        // 再構築ログが連発 (brick Y 133→139→144) していた。ジャンプ最大高さを
        // 最初から確保する (増えるのは 4B/ブリックの索引だけ。プールは固定)。
        builtYSpan = core.SimBounds.size.y;
        float ySpan = builtYSpan + 4.5f;

        // Brick の整数倍に切り上げる。中途半端だと端の Brick が半分だけ有効になる。
        voxelDims = new Vector3Int(
            CeilToBrick(domainSize.x / voxelSize),
            CeilToBrick(ySpan / voxelSize),
            CeilToBrick(domainSize.z / voxelSize));

        UpdateFieldOrigin();

        brickDims = new Vector3Int(voxelDims.x / Brick, voxelDims.y / Brick, voxelDims.z / Brick);
        int newBrickTotal = brickDims.x * brickDims.y * brickDims.z;
        // 同一サイズなら再確保しない (原点の更新とスロットのリセットだけで足りる)
        if (brickSlot != null && newBrickTotal == brickTotal)
        {
            ResetAllSlots();
            BuildPotClipProfiles();
            return;
        }
        brickTotal = newBrickTotal;

        brickSlot?.Release(); activeBricks?.Release(); allocCounter?.Release();
        brickArgs?.Release(); brickArgsSrc?.Release();
        poolAccum?.Release(); poolA?.Release(); poolB?.Release();

        brickSlot = new GraphicsBuffer(GraphicsBuffer.Target.Structured, brickTotal, sizeof(uint));
        activeBricks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, poolBrickCapacity, sizeof(uint));
        allocCounter = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(uint));
        allocCounter.SetData(new uint[] { 0, 0 });
        brickArgsSrc = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource,
                                          6, sizeof(uint));
        brickArgsSrc.SetData(new uint[] { 0, 1, 1, 0, 1, 1 });
        brickArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.CopyDestination,
                                       6, sizeof(uint));
        brickArgs.SetData(new uint[] { 0, 1, 1, 0, 1, 1 });

        int poolVoxels = poolBrickCapacity * Brick * Brick * Brick;
        poolAccum = new GraphicsBuffer(GraphicsBuffer.Target.Structured, poolVoxels, sizeof(uint));
        poolA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, poolVoxels, sizeof(float));
        poolB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, poolVoxels, sizeof(float));

        ResetAllSlots();
        BuildPotClipProfiles();

        float mb = (brickTotal * 4f + poolVoxels * 12f) / (1024f * 1024f);
        if (loggedBuild) return;   // 再構築ごとのログ連発はそれ自体が編集負荷になる
        loggedBuild = true;
        Debug.Log($"FluidSurface: voxel {voxelSize * 1000f:F1}mm / domain {domainSize} @ {fieldOrigin} " +
                  $"= {voxelDims} voxel, brick {brickDims} = {brickTotal / 1000f:F0}k, " +
                  $"pool {poolBrickCapacity} brick, markRadius {MarkRadiusVoxels} voxel, VRAM {mb:F0}MB", this);
    }

    static int CeilToBrick(float v) => Mathf.Max(Brick, Mathf.CeilToInt(v / Brick) * Brick);

    // 壺の内側と外周のプロファイルを GPU へ渡す。これが無いと壁の中を判定できない。
    void BuildPotClipProfiles()
    {
        potInner?.Release(); potOuter?.Release();
        var prof = (core.Boundary != null && core.Boundary.mode == FluidBoundary.Mode.PotProfile)
                 ? core.Boundary.Profile : null;
        if (prof == null) { potInner = null; potOuter = null; return; }

        float[] inner = prof.GetProfileArray();
        float[] outer = prof.GetOuterProfileArray();
        potInner = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inner.Length, sizeof(float));
        potOuter = new GraphicsBuffer(GraphicsBuffer.Target.Structured, outer.Length, sizeof(float));
        potInner.SetData(inner);
        potOuter.SetData(outer);
    }

    // ドメインの原点。**Brick 単位に量子化**して容器の XZ を追う。
    //
    // ドメイン自体は 24m 角あるので、こぼした液体は 12m 離れるまで描画され続ける
    // （以前は 1.8m の箱で、少し歩くと地面の液体が消え、箱の縁が四角い線として
    // 見えていた）。プール方式ではメモリが液体の量にしか比例しないので、
    // ここまで広げても VRAM も 1 フレームのコストも増えない。
    //
    // 量子化は Brick 単位。voxel 単位だと、原点が動いたとき Brick の切れ目が
    // ずれて割り当てが毎フレーム総入れ替えになる。Y は地面に固定する
    // （液体は地面より下へ行かないので追従させる意味が無く、固定した方が安定する）。
    void UpdateFieldOrigin()
    {
        float grid = voxelSize * Brick;
        Vector3 c = core.Boundary != null && core.Boundary.Container != null
                  ? core.Boundary.CenterWorld : transform.position;
        float bottom = core.SimBounds.min.y;
        fieldOrigin = new Vector3(
            Mathf.Floor((c.x - voxelDims.x * voxelSize * 0.5f) / grid) * grid,
            Mathf.Floor(bottom / grid) * grid,
            Mathf.Floor((c.z - voxelDims.z * voxelSize * 0.5f) / grid) * grid);
    }

    // 起動時に 1 度だけ、全 Brick を未割当にする。以後は前フレーム分だけ戻す。
    void ResetAllSlots()
    {
        cs.SetInt("BrickTotal", brickTotal);
        int groupsX = Mathf.Min(32768, Mathf.CeilToInt(brickTotal / (float)Threads));
        int stride = groupsX * Threads;
        int groupsY = Mathf.CeilToInt(brickTotal / (float)stride);
        cs.SetInt("ClearStride", stride);
        cs.SetBuffer(kResetSlots, "BrickSlot", brickSlot);
        cs.Dispatch(kResetSlots, groupsX, groupsY, 1);
    }

    float builtYSpan;
    bool loggedBuild;

    // ---- コスト計測 (2026-08-23)。FluidCore.LastStepMs と同じ考え方で、表面生成だけの実時間を測る。
    // フレーム時間の内訳を「ソルバ / 表面生成 / 描画」に切り分けるために要る。コンポーネントを
    // 無効化して測る方法は OnDisable が GPU バッファを解放してしまうので使えない。
    /// <summary>直近フレームの表面生成に掛かった実時間 (ms)。</summary>
    public float LastBuildMs { get; private set; }
    /// <summary>ResetBuildCost からの平均 (ms)。</summary>
    public float AvgBuildMs => buildMsCount > 0 ? buildMsAcc / buildMsCount : 0f;
    public int BuildCostSamples => buildMsCount;
    public void ResetBuildCost() { buildMsAcc = 0f; buildMsCount = 0; }
    readonly System.Diagnostics.Stopwatch buildWatch = new System.Diagnostics.Stopwatch();
    float buildMsAcc; int buildMsCount;

    void LateUpdate()
    {
        if (!Initialise()) return;
        buildWatch.Restart();
        // 登坂で SimBounds が伸びてフィールドの縦が足りなくなったら作り直す
        // (BuildField の注記を参照。ヘッドルーム 1m があるので頻度は低い)。
        if (core.SimBounds.size.y > builtYSpan + 0.01f) BuildField();
        BuildSurface();
        buildWatch.Stop();
        LastBuildMs = (float)buildWatch.Elapsed.TotalMilliseconds;
        buildMsAcc += LastBuildMs; buildMsCount++;
    }

    /// <summary>計測用。1 回だけ表面生成を走らせる (§36 Phase 12)。</summary>
    public void BuildNow()
    {
        if (!Initialise()) return;
        BuildSurface();
    }

    /// <summary>計測用。GPU の完了を待つ（カウンタの読み戻しで同期する）。</summary>
    public void SyncGpu()
    {
        if (counters != null) counters.GetData(counterRead);
    }

    void BuildSurface()
    {
        UpdateFieldOrigin();     // 容器の XZ を Brick 単位で追う
        cs.SetInts("VoxelDims", voxelDims.x, voxelDims.y, voxelDims.z);
        cs.SetInts("BrickDims", brickDims.x, brickDims.y, brickDims.z);
        cs.SetInt("BrickTotal", brickTotal);
        cs.SetInt("MarkRadiusVoxels", MarkRadiusVoxels);
        cs.SetInt("PoolCapacity", poolBrickCapacity);
        cs.SetVector("FieldOrigin", fieldOrigin);
        cs.SetFloat("VoxelSize", voxelSize);
        cs.SetFloat("SplatRadius", splatRadius);
        cs.SetFloat("EscapedSplatScale", escapedSplatScale);
        cs.SetFloat("DensityScale", densityFixedPointScale);
        cs.SetFloat("IsoValue", isoValue);
        cs.SetFloat("SurfaceNormalEps", normalEpsVoxels);
        cs.SetInt("ParticleCount", core.FluidCount);
        cs.SetInt("MaxTriangles", maxTriangles);

        // 1. 前フレームに割り当てた Brick だけを未割当へ戻す。
        //    ドメイン全体は走査しない。だからドメインをいくら広げても重くならない。
        cs.SetBuffer(kClearSlots, "BrickSlot", brickSlot);
        cs.SetBuffer(kClearSlots, "ActiveBricks", activeBricks);
        cs.SetBuffer(kClearSlots, "AllocCounter", allocCounter);
        cs.DispatchIndirect(kClearSlots, brickArgs, sizeof(uint) * 3);

        // 2. 粒子の居る Brick へその場でスロットを配る (§14)
        allocCounter.SetData(allocReset);
        cs.SetBuffer(kMark, "BrickSlot", brickSlot);
        cs.SetBuffer(kMark, "ActiveBricksRW", activeBricks);
        cs.SetBuffer(kMark, "AllocCounter", allocCounter);
        cs.SetBuffer(kMark, "Particles", core.PositionsBuffer);
        cs.Dispatch(kMark, Mathf.CeilToInt(core.FluidCount / (float)Threads), 1, 1);

        // 3. 有効 Brick 数から DispatchIndirect の引数を作る
        cs.SetBuffer(kBrickArgs, "AllocCounter", allocCounter);
        cs.SetBuffer(kBrickArgs, "BrickArgs", brickArgsSrc);
        cs.Dispatch(kBrickArgs, 1, 1, 1);
        Graphics.CopyBuffer(brickArgsSrc, brickArgs);

        // 4. clear: 実体化した Brick だけ。ドメイン全体は決して触らない
        cs.SetBuffer(kClearPool, "PoolAccum", poolAccum);
        cs.SetBuffer(kClearPool, "PoolOut", poolA);
        cs.SetBuffer(kClearPool, "PoolOutB", poolB);
        cs.DispatchIndirect(kClearPool, brickArgs);

        // 5. splat: 粒子 -> uint 固定小数の atomic 蓄積 (§11/修正3)
        cs.SetBuffer(kSplat, "PoolAccum", poolAccum);
        cs.SetBuffer(kSplat, "BrickSlotIn", brickSlot);
        cs.SetBuffer(kSplat, "Particles", core.PositionsBuffer);
        cs.SetBuffer(kSplat, "ParticleStates", core.RetiredFlagsBuffer);
        cs.Dispatch(kSplat, Mathf.CeilToInt(core.FluidCount / (float)Threads), 1, 1);

        // 6. decode: uint -> float（Atomic 用と Visual 用の分離）
        cs.SetBuffer(kDecode, "PoolAccum", poolAccum);
        cs.SetBuffer(kDecode, "PoolOut", poolA);
        cs.DispatchIndirect(kDecode, brickArgs);

        // 7. smoothing (§14)。プール上の ping-pong。
        cs.SetBuffer(kBlur, "ActiveBricks", activeBricks);
        cs.SetBuffer(kBlur, "BrickSlotIn", brickSlot);
        GraphicsBuffer src = poolA, dst = poolB;
        for (int pass = 0; pass < smoothingPasses; pass++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                cs.SetInts("BlurAxis", axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0);
                cs.SetBuffer(kBlur, "PoolSrc", src);
                cs.SetBuffer(kBlur, "PoolOut", dst);
                cs.DispatchIndirect(kBlur, brickArgs);
                var tmp = src; src = dst; dst = tmp;
            }
        }

        // 8. 壺の実体（壁と底）を切り落とす。
        //    等値面は最外周の粒子から Splat 半径ぶん外へふくらむので、そのままだと
        //    壁の薄い所を突き抜けて「壺の外側にポーションがはみ出す」ように見える。
        //    Blur の後に掛けること。前に掛けると Blur が壁の中へ広げ直す。
        SetPotClipUniforms();
        if (potInner != null)
        {
            cs.SetBuffer(kMaskSolid, "PoolMask", src);
            cs.SetBuffer(kMaskSolid, "ActiveBricks", activeBricks);
            cs.SetBuffer(kMaskSolid, "PotInnerBuf", potInner);
            cs.SetBuffer(kMaskSolid, "PotOuterBuf", potOuter);
            cs.DispatchIndirect(kMaskSolid, brickArgs);
        }

        // 9. iso surface
        counters.SetData(counterReset);
        cs.SetBuffer(kBuild, "ActiveBricks", activeBricks);
        cs.SetBuffer(kBuild, "BrickSlotIn", brickSlot);
        cs.SetBuffer(kBuild, "PoolSrc", src);
        cs.SetBuffer(kBuild, "SurfaceVertices", vertexBuffer);
        cs.SetBuffer(kBuild, "SurfaceCounters", counters);
        cs.DispatchIndirect(kBuild, brickArgs);

        // 10. 描画引数と法線カーネルのディスパッチ引数を三角形カウンタから作る
        cs.SetBuffer(kDrawArgs, "SurfaceCounters", counters);
        cs.SetBuffer(kDrawArgs, "DrawArgs", argsSrc);
        cs.Dispatch(kDrawArgs, 1, 1, 1);
        Graphics.CopyBuffer(argsSrc, argsBuffer);

        // 11. 法線。BuildSurface の中で計算すると EmitTriangle の展開先 100 箇所すべてに
        //     ReadField 48 回ぶんが埋め込まれ、FXC が落ちる。出来上がった頂点に対して
        //     1 回だけ計算する。
        cs.SetBuffer(kNormals, "ActiveBricks", activeBricks);
        cs.SetBuffer(kNormals, "BrickSlotIn", brickSlot);
        cs.SetBuffer(kNormals, "PoolSrc", src);
        cs.SetBuffer(kNormals, "SurfaceVertices", vertexBuffer);
        cs.SetBuffer(kNormals, "SurfaceCounters", counters);
        cs.DispatchIndirect(kNormals, argsBuffer, sizeof(uint) * 4);

        surfaceSrc = src;

        if (logCapacity)
        {
            counters.GetData(counterRead);
            LastTriangleCount = (int)counterRead[1];
            LastOverflowCount = (int)counterRead[0];
            if (LastOverflowCount > 0)
                Debug.LogWarning($"FluidSurface: 三角形バッファ容量超過 {LastOverflowCount} 個 (上限 {maxTriangles})。maxTriangles を上げてください。");

            allocCounter.GetData(allocRead);
            LastActiveBricks = (int)Mathf.Min(allocRead[0], poolBrickCapacity);
            LastBrickOverflow = (int)allocRead[1];
            if (LastBrickOverflow > 0)
                Debug.LogWarning($"FluidSurface: Brick プール容量超過 {LastBrickOverflow} 個 (容量 {poolBrickCapacity})。" +
                                 "poolBrickCapacity を上げてください。液体が虫食いになります。");
        }
    }

    // 壺の姿勢とプロファイルの範囲。流体が見ている姿勢 (SimPosition/SimRotation) を使う。
    // 見た目の Transform とは実測でずれが 0 だが、ずれた場合に切り落としだけが
    // 別の場所に掛かるとかえって破綻するので、流体と同じ姿勢に揃えておく。
    void SetPotClipUniforms()
    {
        var b = core.Boundary;
        bool on = clipToContainer && potInner != null && b != null
               && b.mode == FluidBoundary.Mode.PotProfile && b.Profile != null;
        cs.SetInt("PotClipEnabled", on ? 1 : 0);
        if (!on) return;

        var prof = b.Profile;
        Matrix4x4 potToWorld = Matrix4x4.TRS(b.SimPosition, b.SimRotation, b.Container.lossyScale);
        cs.SetMatrix("WorldToPot", potToWorld.inverse);
        cs.SetInt("PotProfileCount", PotInteriorProfile.Samples);
        cs.SetFloat("PotFloorY", prof.FloorY);
        cs.SetFloat("PotRimY", prof.RimY);
        cs.SetFloat("PotMeshMinY", prof.MeshMinY);
        cs.SetFloat("PotMeshMaxY", prof.MeshMaxY);

        // 壺を包む球。ここから外れた voxel は距離判定 1 回で抜ける。
        float midY = (prof.MeshMinY + prof.MeshMaxY) * 0.5f;
        float halfH = (prof.MeshMaxY - prof.MeshMinY) * 0.5f;
        float outerMax = 0f;
        var outer = prof.OuterRadii;
        if (outer != null) for (int i = 0; i < outer.Length; i++) outerMax = Mathf.Max(outerMax, outer[i]);
        cs.SetVector("PotCentreWS", potToWorld.MultiplyPoint3x4(new Vector3(0f, midY, 0f)));
        cs.SetFloat("PotClipRadiusWS", Mathf.Sqrt(outerMax * outerMax + halfH * halfH) * b.ContainerScale * 1.05f);
    }

    GraphicsBuffer surfaceSrc;

    public Vector3Int BrickDims => brickDims;
    public int BrickTotal => brickTotal;
    public int LastActiveBricks { get; private set; }
    public int LastBrickOverflow { get; private set; }

    // Debug 用。CPU 同期が入るので毎フレーム呼ばないこと。
    public int ReadActiveBrickCount()
    {
        if (allocCounter == null) return 0;
        allocCounter.GetData(allocRead);
        return (int)Mathf.Min(allocRead[0], poolBrickCapacity);
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (vertexBuffer == null || liquidMaterial == null || surfaceSrc == null) return;

        mpb.Clear();
        mpb.SetBuffer(IdVerts, vertexBuffer);
        // 厚みの積分もプールを間接参照する。ここを 3D テクスチャのままにすると、
        // 表面だけ広がって厚み（＝色と発光）が壺の周りでしか出なくなる。
        mpb.SetBuffer(IdPool, surfaceSrc);
        mpb.SetBuffer(IdBrickSlot, brickSlot);
        mpb.SetVector(IdVoxelDims, new Vector4(voxelDims.x, voxelDims.y, voxelDims.z, 0f));
        mpb.SetVector(IdBrickDims, new Vector4(brickDims.x, brickDims.y, brickDims.z, 0f));
        mpb.SetFloat(IdVoxelSize, voxelSize);
        mpb.SetFloat(IdPoolCapacity, poolBrickCapacity);
        mpb.SetVector(IdFieldOrigin, fieldOrigin);
        mpb.SetVector(IdFieldSize, new Vector3(voxelDims.x, voxelDims.y, voxelDims.z) * voxelSize);
        mpb.SetFloat(IdIso, isoValue);

        var bounds = new Bounds(fieldOrigin + (Vector3)voxelDims * voxelSize * 0.5f,
                                (Vector3)voxelDims * voxelSize);
        var rp = new RenderParams(liquidMaterial)
        {
            worldBounds = bounds,
            matProps = mpb,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false
        };
        Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, argsBuffer, 1);
    }
}
