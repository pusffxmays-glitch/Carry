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

    GraphicsBuffer densityAccum, vertexBuffer, argsBuffer, counters;
    RenderTexture densityA, densityB;
    Vector3Int voxelDims;
    Vector3 fieldOrigin;
    float voxelSize, splatRadius;
    MaterialPropertyBlock mpb;
    uint[] counterReset = new uint[4];
    uint[] counterRead = new uint[4];

    int kClear, kSplat, kDecode, kBlur, kBuild;
    const int Threads = 256;
    const int Threads3 = 4;

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
        vertexBuffer?.Release(); vertexBuffer = null;
        argsBuffer?.Release(); argsBuffer = null;
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
        kSplat = cs.FindKernel("SplatDensity");
        kDecode = cs.FindKernel("DecodeDensity");
        kBlur = cs.FindKernel("BlurDensity");
        kBuild = cs.FindKernel("BuildSurface");

        BuildField();

        // 頂点は position + normal の 6 float。
        vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append | GraphicsBuffer.Target.Structured,
                                          maxTriangles * 3, sizeof(float) * 6);
        argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 4, sizeof(uint));
        argsBuffer.SetData(new uint[] { 0, 1, 0, 0 });
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

        voxelDims = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt(span.x / voxelSize) + 1, 8, 320),
            Mathf.Clamp(Mathf.CeilToInt(span.y / voxelSize) + 1, 8, 320),
            Mathf.Clamp(Mathf.CeilToInt(span.z / voxelSize) + 1, 8, 320));

        UpdateFieldOrigin();

        int total = voxelDims.x * voxelDims.y * voxelDims.z;
        densityAccum?.Release();
        densityAccum = new GraphicsBuffer(GraphicsBuffer.Target.Structured, total, sizeof(uint));

        densityA = MakeVolume(densityA, "FluidDensityA");
        densityB = MakeVolume(densityB, "FluidDensityB");
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

    void BuildSurface()
    {
        UpdateFieldOrigin();     // 容器が動くので密度場も追従する
        int total = voxelDims.x * voxelDims.y * voxelDims.z;

        cs.SetInts("VoxelDims", voxelDims.x, voxelDims.y, voxelDims.z);
        cs.SetVector("FieldOrigin", fieldOrigin);
        cs.SetFloat("VoxelSize", voxelSize);
        cs.SetFloat("SplatRadius", splatRadius);
        cs.SetFloat("DensityScale", densityFixedPointScale);
        cs.SetFloat("IsoValue", isoValue);
        cs.SetFloat("SurfaceNormalEps", normalEpsVoxels);
        cs.SetInt("ParticleCount", core.FluidCount);
        cs.SetInt("MaxTriangles", maxTriangles);

        // 1. clear
        cs.SetBuffer(kClear, "DensityAccum", densityAccum);
        cs.Dispatch(kClear, Mathf.CeilToInt(total / (float)Threads), 1, 1);

        // 2. splat: 粒子 -> uint 固定小数の atomic 蓄積 (§11/修正3)
        cs.SetBuffer(kSplat, "DensityAccum", densityAccum);
        cs.SetBuffer(kSplat, "Particles", core.PositionsBuffer);
        cs.Dispatch(kSplat, Mathf.CeilToInt(core.FluidCount / (float)Threads), 1, 1);

        // 3. decode: uint -> float 3D texture（Atomic 用と Visual 用の分離）
        cs.SetBuffer(kDecode, "DensityAccum", densityAccum);
        cs.SetTexture(kDecode, "DensityOut", densityA);
        DispatchVolume(kDecode);

        // 4. smoothing (§14)
        RenderTexture src = densityA, dst = densityB;
        for (int p = 0; p < smoothingPasses; p++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                cs.SetInts("BlurAxis", axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0);
                cs.SetTexture(kBlur, "DensitySrc", src);
                cs.SetTexture(kBlur, "DensityOut", dst);
                DispatchVolume(kBlur);
                var tmp = src; src = dst; dst = tmp;
            }
        }

        // 5. iso surface
        counters.SetData(counterReset);
        vertexBuffer.SetCounterValue(0);
        cs.SetTexture(kBuild, "DensitySrc", src);
        cs.SetBuffer(kBuild, "SurfaceVertices", vertexBuffer);
        cs.SetBuffer(kBuild, "SurfaceCounters", counters);
        DispatchVolume(kBuild);

        GraphicsBuffer.CopyCount(vertexBuffer, argsBuffer, 0);
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

    void DispatchVolume(int kernel)
    {
        cs.Dispatch(kernel,
            Mathf.CeilToInt(voxelDims.x / (float)Threads3),
            Mathf.CeilToInt(voxelDims.y / (float)Threads3),
            Mathf.CeilToInt(voxelDims.z / (float)Threads3));
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
