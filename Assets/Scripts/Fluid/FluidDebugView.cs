using UnityEngine;
using UnityEngine.Rendering;

// PHASE 1 DEBUG ONLY.
//
// 仕様 §10 は「Particle を直接表示しない」と定めている。これはその禁止に反するものではなく、
// Phase 1（物理が安定して動くか）を目視で確かめるための開発用ビューである。Phase 2 で
// Density Field + Marching Cubes による Surface が入った時点で `showParticles` を既定 false にし、
// 以後は Surface だけを見る。完成形にこの表示は含まれない。
[RequireComponent(typeof(FluidCore))]
[ExecuteAlways]
public class FluidDebugView : MonoBehaviour
{
    [Tooltip("Phase 1 検証用。Phase 2 以降は false にする。")]
    public bool showParticles = false;
    public bool showBoundary = false;
    [Range(0.1f, 1.5f)] public float fluidRadiusScale = 0.55f;
    [Range(0.1f, 1.5f)] public float boundaryRadiusScale = 0.35f;
    public Color fluidColor = new Color(0.25f, 0.85f, 0.35f);
    public Color boundaryColor = new Color(0.85f, 0.45f, 0.2f);
    public Shader debugShader;

    FluidCore core;
    Material mat;
    MaterialPropertyBlock mpb;

    static readonly int IdPoints = Shader.PropertyToID("_Points");
    static readonly int IdRadius = Shader.PropertyToID("_PointRadius");
    static readonly int IdCount = Shader.PropertyToID("_PointCount");
    static readonly int IdColor = Shader.PropertyToID("_PointColor");

    void OnEnable()
    {
        // LateUpdate から Graphics.RenderPrimitives を呼ぶと、Player Loop が回っていない状況
        // （エディタ非フォーカス、あるいは検証スクリプトからの cam.Render()）では描画命令が
        // 一度も発行されず、スクリーンショットが真っ黒になる。カメラ描画イベントで発行する。
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        core = GetComponent<FluidCore>();
        if (debugShader == null) debugShader = Shader.Find("Hidden/Fluid/DebugParticles");
        if (debugShader != null && mat == null) mat = new Material(debugShader) { hideFlags = HideFlags.HideAndDontSave };
        if (mpb == null) mpb = new MaterialPropertyBlock();
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (mat != null) { DestroyImmediate(mat); mat = null; }
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam) { Submit(); }

    void Submit()
    {
        if (core == null || mat == null || !core.IsReady) return;
        var bounds = core.SimBounds;
        bounds.Expand(1f);

        if (showParticles && core.PositionsBuffer != null)
            Draw(core.PositionsBuffer, core.FluidCount, core.ParticleSpacing * fluidRadiusScale, fluidColor, bounds);

        if (showBoundary && core.BoundaryPositionsBuffer != null)
            Draw(core.BoundaryPositionsBuffer, core.BoundaryCount, core.ParticleSpacing * boundaryRadiusScale, boundaryColor, bounds);
    }

    void Draw(GraphicsBuffer buffer, int count, float radius, Color color, Bounds bounds)
    {
        mpb.Clear();
        mpb.SetBuffer(IdPoints, buffer);
        mpb.SetFloat(IdRadius, radius);
        mpb.SetInt(IdCount, count);
        mpb.SetColor(IdColor, color);

        var rp = new RenderParams(mat)
        {
            worldBounds = bounds,
            matProps = mpb,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false
        };
        Graphics.RenderPrimitives(rp, MeshTopology.Triangles, count * 6, 1);
    }
}
