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
    [Tooltip("等値面のしきい値。下げると液体が太く、上げると細くなる。")]
    [Range(0.02f, 3f)] public float isoValue = 0.45f;
    [Tooltip("密度場の平滑化回数 (§14)。0 で無効。")]
    [Range(0, 4)] public int smoothingPasses = 2;
    [Tooltip("法線を取るときの中央差分の幅。ボクセルサイズの倍数。大きいほど滑らかだが細部が鈍る。")]
    [Range(0.5f, 3f)] public float normalEpsVoxels = 1.2f;
    [Tooltip("固定小数の分解能 (§11/修正3)。小さすぎると量子化で表面が粒状になる。")]
    public float densityFixedPointScale = 16384f;

    [Header("Capacity (§13/修正9)")]
    [Tooltip("密度場 1 軸あたりのボクセル数上限。これで切り詰められると表面が領域端で欠けるため、警告を出す。")]
    [Range(64, 512)] public int maxVoxelsPerAxis = 384;
    [Tooltip("生成できる三角形の上限。超過分は書き込まずカウンタに積む。容量不足を理由に簡易表現へ切り替えることはしない。")]
    public int maxTriangles = 900000;

    [Header("Material")]
    public Material liquidMaterial;
    public Shader liquidShader;

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

    GraphicsBuffer densityAccum, vertexBuffer, argsBuffer, argsSrc, counters;
    GraphicsBuffer brickMarks, brickResident, activeBricks, brickArgs;
    RenderTexture densityA, densityB;
    Vector3Int voxelDims, brickDims;
    int brickTotal;
    Vector3 fieldOrigin;
    float voxelSize, splatRadius;
    MaterialPropertyBlock mpb;
    uint[] counterReset = new uint[4];
    uint[] counterRead = new uint[4];

    int kClear, kMark, kCollect, kClearBricks, kSplat, kDecode, kBlur, kBuild, kDrawArgs;
    const int Threads = 256;
    const int Threads3 = 4;
    const int Brick = 8;        // FluidSurface.compute の BRICK と一致させること
    const int BrickMargin = 2;  // Splat 半径 + Blur 到達範囲を覆う余裕

    static readonly int IdVerts = Shader.PropertyToID("_SurfaceVertices");
    static readonly int IdDensityTex = Shader.PropertyToID("_DensityField");
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
        densityAccum?.Release(); densityAccum = null;
        brickMarks?.Release(); brickMarks = null;
        brickResident?.Release(); brickResident = null;
        activeBricks?.Release(); activeBricks = null;
        brickArgs?.Release(); brickArgs = null;
        vertexBuffer?.Release(); vertexBuffer = null;
        argsBuffer?.Release(); argsBuffer = null;
        argsSrc?.Release(); argsSrc = null;
        counters?.Release(); counters = null;
        if (densityA != null) { densityA.Release(); DestroyImmediate(densityA); densityA = null; }
        if (densityB != null) { densityB.Release(); DestroyImmediate(densityB); densityB = null; }
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
        kClear = cs.FindKernel("ClearDensity");
        kMark = cs.FindKernel("MarkBricks");
        kCollect = cs.FindKernel("CollectBricks");
        kClearBricks = cs.FindKernel("ClearBricks");
        kSplat = cs.FindKernel("SplatDensity");
        kDecode = cs.FindKernel("DecodeDensity");
        kBlur = cs.FindKernel("BlurDensity");
        kBuild = cs.FindKernel("BuildSurface");
        kDrawArgs = cs.FindKernel("WriteDrawArgs");

        BuildField();

        // 頂点は position + normal の 6 float。
        // Append ではなく通常の Structured。三角形 1 枚を連続 3 スロットへ書くため
        // （Append だと他スレッドの Append が割り込んで頂点が別の三角形に混ざる）。
        vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                                          maxTriangles * 3, sizeof(float) * 6);
        // IndirectArguments のバッファへ Compute から直接書くのは環境依存なので、
        // Structured へ書いてから CopyBuffer で移す。
        argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.CopyDestination,
                                        4, sizeof(uint));
        argsBuffer.SetData(new uint[] { 0, 1, 0, 0 });
        argsSrc = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource,
                                     4, sizeof(uint));
        argsSrc.SetData(new uint[] { 0, 1, 0, 0 });
        counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, sizeof(uint));

        if (liquidShader == null) liquidShader = Shader.Find("Custom/PotionLiquidSurface");
        if (liquidMaterial == null && liquidShader != null)
            liquidMaterial = new Material(liquidShader) { hideFlags = HideFlags.HideAndDontSave };
        if (mpb == null) mpb = new MaterialPropertyBlock();
        return true;
    }

    // 密度場の箱。サイズは一度だけ決め、原点は毎フレーム容器に追従させる (§12/修正7)。
    // 原点はボクセルサイズ単位に量子化する。量子化しないと、原点がサブボクセル単位で
    // 毎フレーム動いて表面がちらつく。
    void BuildField()
    {
        voxelSize = core.ParticleSpacing / Mathf.Max(1.5f, voxelsPerSpacing);
        splatRadius = core.ParticleSpacing * splatRadiusPerSpacing;

        Bounds b = core.SimBounds;
        Vector3 pad = Vector3.one * (splatRadius * 2f + voxelSize * 2f);
        Vector3 span = b.size + pad * 2f;

        // 上限で切り詰めると密度場がシミュレーション領域を覆いきれず、表面が領域端で
        // 平らに欠ける。黙って切り詰めるのは性能を理由に品質を落とすことなので (§36)、
        // 切り詰めが起きたら必ず警告を出す。
        var want = new Vector3Int(
            Mathf.CeilToInt(span.x / voxelSize) + 1,
            Mathf.CeilToInt(span.y / voxelSize) + 1,
            Mathf.CeilToInt(span.z / voxelSize) + 1);
        voxelDims = new Vector3Int(
            Mathf.Clamp(want.x, 8, maxVoxelsPerAxis),
            Mathf.Clamp(want.y, 8, maxVoxelsPerAxis),
            Mathf.Clamp(want.z, 8, maxVoxelsPerAxis));
        if (want.x > voxelDims.x || want.y > voxelDims.y || want.z > voxelDims.z)
            Debug.LogWarning($"FluidSurface: 密度場がシミュレーション領域を覆えていません。必要 {want} / 実際 {voxelDims}。" +
                             "maxVoxelsPerAxis を上げるか FluidCore の領域を狭めてください。表面が領域端で平らに欠けます。", this);

        UpdateFieldOrigin();

        int total = voxelDims.x * voxelDims.y * voxelDims.z;
        densityAccum?.Release();
        densityAccum = new GraphicsBuffer(GraphicsBuffer.Target.Structured, total, sizeof(uint));

        // Sparse Brick (§14)。場は 1 つのまま、毎フレーム触る範囲だけを液体の周囲に限る。
        // Brick は「同じ 1 つの場のどこを計算するか」を決めるだけで、別の場は作らない。
        brickDims = new Vector3Int(
            Mathf.CeilToInt(voxelDims.x / (float)Brick),
            Mathf.CeilToInt(voxelDims.y / (float)Brick),
            Mathf.CeilToInt(voxelDims.z / (float)Brick));
        brickTotal = brickDims.x * brickDims.y * brickDims.z;

        brickMarks?.Release(); brickResident?.Release(); activeBricks?.Release(); brickArgs?.Release();
        brickMarks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, brickTotal, sizeof(uint));
        brickResident = new GraphicsBuffer(GraphicsBuffer.Target.Structured, brickTotal, sizeof(uint));
        activeBricks = new GraphicsBuffer(GraphicsBuffer.Target.Append | GraphicsBuffer.Target.Structured,
                                          brickTotal, sizeof(uint));
        brickArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint));
        brickArgs.SetData(new uint[] { 0, 1, 1 });

        brickResident.SetData(new uint[brickTotal]);
        brickMarks.SetData(new uint[brickTotal]);

        densityA = MakeVolume(densityA, "FluidDensityA");
        densityB = MakeVolume(densityB, "FluidDensityB");

        ClearWholeField(total);

        // 場の広さは毎フレームのコストではなく VRAM のコスト。Sparse Brick により
        // 計算量は液体の体積に比例する。実測値を必ず残しておく。
        float mb = (total * 4f + total * 2f * 2f) / (1024f * 1024f);
        Debug.Log($"FluidSurface: voxel {voxelSize * 1000f:F1}mm / dims {voxelDims} = {total / 1000000f:F1}M voxel, " +
                  $"brick {brickDims} = {brickTotal}, VRAM {mb:F0}MB", this);
    }

    // 場全体を 1 度だけ 0 にする。以後は有効 Brick だけを触る (§14)。
    void ClearWholeField(int total)
    {
        cs.SetInts("VoxelDims", voxelDims.x, voxelDims.y, voxelDims.z);
        int groupsX = Mathf.Min(32768, Mathf.CeilToInt(total / (float)Threads));
        int stride = groupsX * Threads;
        int groupsY = Mathf.CeilToInt(total / (float)stride);
        cs.SetInt("ClearStride", stride);
        cs.SetBuffer(kClear, "DensityAccum", densityAccum);
        cs.SetTexture(kClear, "DensityOut", densityA);
        cs.SetTexture(kClear, "DensityClearB", densityB);
        cs.Dispatch(kClear, groupsX, groupsY, 1);
    }

    void UpdateFieldOrigin()
    {
        Vector3 centre = core.SimBounds.center;
        Vector3 lo = centre - (Vector3)voxelDims * voxelSize * 0.5f;
        fieldOrigin = new Vector3(
            Mathf.Floor(lo.x / voxelSize) * voxelSize,
            Mathf.Floor(lo.y / voxelSize) * voxelSize,
            Mathf.Floor(lo.z / voxelSize) * voxelSize);
    }

    RenderTexture MakeVolume(RenderTexture existing, string name)
    {
        if (existing != null) { existing.Release(); DestroyImmediate(existing); }
        var rt = new RenderTexture(voxelDims.x, voxelDims.y, 0, RenderTextureFormat.RHalf)
        {
            name = name,
            dimension = TextureDimension.Tex3D,
            volumeDepth = voxelDims.z,
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        rt.Create();
        return rt;
    }

    void LateUpdate()
    {
        if (!Initialise()) return;
        BuildSurface();
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
        UpdateFieldOrigin();     // 容器が動くので密度場も追従する

        cs.SetInts("VoxelDims", voxelDims.x, voxelDims.y, voxelDims.z);
        cs.SetInts("BrickDims", brickDims.x, brickDims.y, brickDims.z);
        cs.SetInt("BrickMargin", BrickMargin);
        cs.SetVector("FieldOrigin", fieldOrigin);
        cs.SetFloat("VoxelSize", voxelSize);
        cs.SetFloat("SplatRadius", splatRadius);
        cs.SetFloat("DensityScale", densityFixedPointScale);
        cs.SetFloat("IsoValue", isoValue);
        cs.SetFloat("SurfaceNormalEps", normalEpsVoxels);
        cs.SetInt("ParticleCount", core.FluidCount);
        cs.SetInt("MaxTriangles", maxTriangles);

        // 1. 粒子が居る Brick を立てる (§14)
        cs.SetBuffer(kMark, "BrickMarks", brickMarks);
        cs.SetBuffer(kMark, "Particles", core.PositionsBuffer);
        cs.Dispatch(kMark, Mathf.CeilToInt(core.FluidCount / (float)Threads), 1, 1);

        // 2. 今フレーム分 + 前フレームの残りを集める（液体が去った跡の消し残しを防ぐ）
        activeBricks.SetCounterValue(0);
        cs.SetBuffer(kCollect, "BrickMarks", brickMarks);
        cs.SetBuffer(kCollect, "BrickResident", brickResident);
        cs.SetBuffer(kCollect, "ActiveBricksAppend", activeBricks);
        cs.Dispatch(kCollect, Mathf.CeilToInt(brickTotal / (float)Threads), 1, 1);
        GraphicsBuffer.CopyCount(activeBricks, brickArgs, 0);

        // 3. clear: 有効 Brick だけ。全ドメインを毎フレーム触らない
        cs.SetBuffer(kClearBricks, "ActiveBricks", activeBricks);
        cs.SetBuffer(kClearBricks, "DensityAccum", densityAccum);
        cs.SetTexture(kClearBricks, "DensityOut", densityA);
        cs.SetTexture(kClearBricks, "DensityClearB", densityB);
        cs.DispatchIndirect(kClearBricks, brickArgs);

        // 4. splat: 粒子 -> uint 固定小数の atomic 蓄積 (§11/修正3)
        cs.SetBuffer(kSplat, "DensityAccum", densityAccum);
        cs.SetBuffer(kSplat, "Particles", core.PositionsBuffer);
        cs.Dispatch(kSplat, Mathf.CeilToInt(core.FluidCount / (float)Threads), 1, 1);

        // 5. decode: uint -> float 3D texture（Atomic 用と Visual 用の分離）
        cs.SetBuffer(kDecode, "ActiveBricks", activeBricks);
        cs.SetBuffer(kDecode, "DensityAccum", densityAccum);
        cs.SetTexture(kDecode, "DensityOut", densityA);
        cs.DispatchIndirect(kDecode, brickArgs);

        // 6. smoothing (§14)
        cs.SetBuffer(kBlur, "ActiveBricks", activeBricks);
        RenderTexture src = densityA, dst = densityB;
        for (int p = 0; p < smoothingPasses; p++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                cs.SetInts("BlurAxis", axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0);
                cs.SetTexture(kBlur, "DensitySrc", src);
                cs.SetTexture(kBlur, "DensityOut", dst);
                cs.DispatchIndirect(kBlur, brickArgs);
                var tmp = src; src = dst; dst = tmp;
            }
        }

        // 7. iso surface
        counters.SetData(counterReset);
        cs.SetBuffer(kBuild, "ActiveBricks", activeBricks);
        cs.SetTexture(kBuild, "DensitySrc", src);
        cs.SetBuffer(kBuild, "SurfaceVertices", vertexBuffer);
        cs.SetBuffer(kBuild, "SurfaceCounters", counters);
        cs.DispatchIndirect(kBuild, brickArgs);

        // 8. 描画引数を三角形カウンタから作る
        cs.SetBuffer(kDrawArgs, "SurfaceCounters", counters);
        cs.SetBuffer(kDrawArgs, "DrawArgs", argsSrc);
        cs.Dispatch(kDrawArgs, 1, 1, 1);
        Graphics.CopyBuffer(argsSrc, argsBuffer);
        surfaceSrc = src;

        if (logCapacity)
        {
            counters.GetData(counterRead);
            LastTriangleCount = (int)counterRead[1];
            LastOverflowCount = (int)counterRead[0];
            if (LastOverflowCount > 0)
                Debug.LogWarning($"FluidSurface: 三角形バッファ容量超過 {LastOverflowCount} 個 (上限 {maxTriangles})。maxTriangles を上げてください。");
        }
    }

    RenderTexture surfaceSrc;

    public Vector3Int BrickDims => brickDims;
    public int BrickTotal => brickTotal;

    // Debug 用。CPU 同期が入るので毎フレーム呼ばないこと。
    public int ReadActiveBrickCount()
    {
        if (brickArgs == null) return 0;
        var a = new uint[3];
        brickArgs.GetData(a);
        return (int)a[0];
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (vertexBuffer == null || liquidMaterial == null || surfaceSrc == null) return;

        mpb.Clear();
        mpb.SetBuffer(IdVerts, vertexBuffer);
        mpb.SetTexture(IdDensityTex, surfaceSrc);
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
