using UnityEngine;

// Particle burst reserved for genuinely fast/violent overflow events (sudden stops, hard impacts) --
// ordinary pouring/dripping is handled entirely by PotionOverflowStream's mesh-based flowing tube +
// droplets now (see that file and PotionLiquid.DeformMeshAndHandleOverflow). This component used to
// also drive a continuous "drip" particle stream for slow overflow, but that was exactly the
// "細い緑色の線が壺から垂れているだけ" complaint from the 2026-08-12 rework request -- a chain of
// particles reads as a line/streak no matter how it's tuned, so slow/continuous overflow was moved to
// the real tapered mesh in PotionOverflowStream, and this class now only fires a one-shot splash
// burst for the minority of cases (sudden stop etc.) that should look like an actual splash impact on
// top of the flowing stream, not the whole overflow visual.
//
// Deliberately NOT VFX Graph: this project has neither com.unity.visualeffectgraph nor
// com.unity.shadergraph installed, and hand-authoring a VFX Graph's node structure blind (without
// visually editing it in the Editor) has already been tried and abandoned once in this project's
// history as too risky (see WORKLOG.md, 2026-08-10). Shuriken is fully driveable from script via
// ParticleSystem.Emit(), so every property here is something PotionLiquid actually controls per
// spill event rather than a graph asset we'd have to hand-edit blind.
public class PotionOverflowVFX : MonoBehaviour
{
    [Header("Material (assign the potion liquid material or a similar translucent green one)")]
    public Material splashMaterial;

    [Header("Color")]
    public Color potionColor = new Color(0.22f, 0.75f, 0.28f, 0.9f);

    [Header("Splash (fast/violent overflow only)")]
    [Tooltip("Surface-rise speed (from PotionLiquid.overflowSplashSpeed) above which this burst fires. Ordinary overflow below this speed is 100% handled by PotionOverflowStream.")]
    public float splashSpeedThreshold = 0.6f;
    [Tooltip("Particle count spawned per unit of spilled volume. Lowered 2026-08-12 (spec: \"水のような大量の細かいParticleではなく、粘性のある緑色の液体が跳ねたように見えること\") together with a bigger splashSize -- fewer, bigger blobs read as viscous splashing liquid; lots of tiny particles read as a fine watery mist.")]
    public float splashParticlesPerVolume = 200f;
    public float splashLifetime = 0.6f;
    [Tooltip("Raised 2026-08-12 alongside the lowered particle count -- see splashParticlesPerVolume.")]
    public float splashSize = 0.048f;
    public float splashSpeedMultiplier = 1.3f;
    [Tooltip("Random cone spread (degrees) applied to splash particle direction.")]
    public float splashSpread = 26f;
    [Tooltip("Lower = heavier/thicker splash droplets, less watery spray. 1 = real gravity. Lowered 2026-08-12 alongside the size/count changes for the same 'viscous blob, not water mist' goal.")]
    public float splashGravityModifier = 0.75f;

    [Tooltip("Lowered 2026-08-12 alongside splashParticlesPerVolume -- fewer, chunkier splash blobs.")]
    [Range(1, 60)] public int maxParticlesPerEvent = 14;
    [Tooltip("Spilled volume at which splash particles reach their full up-scaled size (below it they scale down toward the base splashSize) -- so a genuinely huge splash still visibly looks bigger even once particle COUNT has saturated at maxParticlesPerEvent. 2026-08-12 (\"こぼれる量と残量がリンクしていなそう\").")]
    public float splashVolumeReference = 0.002f;

    ParticleSystem splashPs;
    bool built;

    void Awake() { EnsureBuilt(); }

    // PotionLiquid.Awake() calls this explicitly (after possibly assigning splashMaterial) rather
    // than relying on this component's own Awake() ordering: AddComponent<T>() invokes T's Awake()
    // synchronously, before the calling code gets a chance to set fields on the newly-added
    // component -- so building the particle system here unconditionally on first use (guarded by
    // `built`) means it's safe to call this again after the material field is set, and the system
    // gets rebuilt with the correct material instead of silently keeping a null one.
    public void EnsureBuilt(bool rebuildMaterialsOnly = false)
    {
        if (built && !rebuildMaterialsOnly)
            return;

        if (!built)
        {
            splashPs = CreateSystem("Splash", splashMaterial, splashLifetime, splashSize, splashGravityModifier);
            built = true;
            return;
        }

        if (splashPs != null && splashMaterial != null)
            splashPs.GetComponent<ParticleSystemRenderer>().sharedMaterial = splashMaterial;
    }

    ParticleSystem CreateSystem(string goName, Material mat, float lifetime, float size, float gravityModifier)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, false);

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = lifetime;
        main.startSize = size;
        main.startColor = potionColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravityModifier;
        main.maxParticles = 500;

        var emission = ps.emission;
        emission.enabled = false; // every particle is spawned explicitly via Emit()

        var shape = ps.shape;
        shape.enabled = false;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (mat != null)
        {
            renderer.sharedMaterial = mat;
        }

        // Shrinks slightly over its life instead of popping out at a constant size -- reads as the
        // droplet thinning/dissipating rather than a hard-edged sprite blinking off.
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.55f));

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var alphaGrad = new Gradient();
        alphaGrad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = alphaGrad;

        var col = ps.collision;
        col.enabled = false; // v1: no world collision response, keep it cheap; revisit if splashes need to pool/stick

        var trails = ps.trails;
        trails.enabled = false;

        return ps;
    }

    // Called from PotionLiquid only when a spill's surface-rise speed crosses splashSpeedThreshold
    // (sudden stop / hard impact) -- ordinary pouring never reaches here, it's all
    // PotionOverflowStream. volume is the FULL accumulated spilled amount for this frame; speed is
    // the peak local surface rise speed, used to scale initial velocity.
    public void NotifySplash(Vector3 worldPos, Vector3 spillDirWorld, float volume, float speed)
    {
        if (volume <= 0f) return;
        EnsureBuilt();
        if (splashPs == null) return;

        int count = Mathf.Clamp(Mathf.RoundToInt(volume * splashParticlesPerVolume), 1, maxParticlesPerEvent);
        float sizeScale = Mathf.Lerp(0.7f, 1.7f, Mathf.Clamp01(volume / Mathf.Max(0.0001f, splashVolumeReference)));

        Vector3 dir = spillDirWorld.sqrMagnitude > 1e-6f ? spillDirWorld.normalized : Vector3.down;

        var emitParams = new ParticleSystem.EmitParams();
        emitParams.startColor = potionColor;

        for (int i = 0; i < count; i++)
        {
            Vector3 d = dir;
            d = Quaternion.AngleAxis(Random.Range(-splashSpread, splashSpread), Vector3.up) * d;
            d = Quaternion.AngleAxis(Random.Range(-splashSpread * 0.5f, splashSpread * 0.5f), Vector3.Cross(d, Vector3.up)) * d;
            d = Vector3.Slerp(d, Random.onUnitSphere, 0.15f);

            float stagger = Random.Range(0f, 0.06f);
            Vector3 jitterPos = worldPos + Random.insideUnitSphere * 0.015f + dir * stagger;

            emitParams.position = jitterPos;
            emitParams.velocity = d * splashSpeedMultiplier * Mathf.Max(0.25f, speed) * Random.Range(0.8f, 1.2f);
            emitParams.startSize = splashSize * sizeScale * Random.Range(0.75f, 1.25f);
            splashPs.Emit(emitParams, 1);
        }
    }
}
