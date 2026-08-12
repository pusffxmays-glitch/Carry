using UnityEngine;

// Script-driven Overflow VFX (drip / stream / splash) using two built-in Shuriken ParticleSystems.
// Deliberately NOT VFX Graph: this project has neither com.unity.visualeffectgraph nor
// com.unity.shadergraph installed, and hand-authoring a VFX Graph's node structure blind (without
// visually editing it in the Editor) has already been tried and abandoned once in this project's
// history as too risky (see WORKLOG.md, 2026-08-10). Shuriken is fully driveable from script via
// ParticleSystem.Emit(), so every property here is something PotionLiquid actually controls per
// spill event rather than a graph asset we'd have to hand-edit blind.
//
// Kept as a SEPARATE component (child of PotionLiquid's GameObject, auto-created if missing) so the
// "how spilled liquid looks/behaves" concern doesn't get tangled into PotionLiquid's own surface/
// volume simulation -- PotionLiquid only ever calls NotifySpillPoint(), it never touches a
// ParticleSystem directly.
public class PotionOverflowVFX : MonoBehaviour
{
    [Header("Materials (assign the potion liquid material or a similar translucent green one)")]
    public Material dripMaterial;
    public Material splashMaterial;

    [Header("Color")]
    public Color potionColor = new Color(0.22f, 0.75f, 0.28f, 0.9f);

    [Header("Drip / slow pour (small, viscous-looking overflow)")]
    [Tooltip("Particle count spawned per unit of spilled volume, below the splash speed threshold.")]
    public float dripParticlesPerVolume = 320f;
    public float dripLifetime = 1.0f;
    public float dripSize = 0.045f;
    [Tooltip("How much the drip's initial velocity is scaled by the local spill speed.")]
    public float dripSpeedMultiplier = 0.22f;
    [Tooltip("Lower = falls more slowly/heavily (more viscous). 1 = real gravity.")]
    public float dripGravityModifier = 0.5f;

    [Header("Splash (fast/large overflow)")]
    [Tooltip("Surface-rise speed (from PotionLiquid.overflowSplashSpeed) above which splash replaces drip.")]
    public float splashSpeedThreshold = 0.6f;
    [Tooltip("Particle count spawned per unit of spilled volume, at/above the splash speed threshold.")]
    public float splashParticlesPerVolume = 350f;
    public float splashLifetime = 0.55f;
    public float splashSize = 0.03f;
    public float splashSpeedMultiplier = 1.4f;
    [Tooltip("Random cone spread (degrees) applied to splash particle direction.")]
    public float splashSpread = 28f;
    [Tooltip("Lower = heavier/thicker splash droplets, less watery spray. 1 = real gravity.")]
    public float splashGravityModifier = 1.0f;

    [Tooltip("Hard cap on particles spawned by a single NotifySpillPoint call. Raised 2026-08-12 since overflow now emits from one concentrated point per frame instead of many scattered ones, so a single call can afford a fuller burst.")]
    [Range(1, 60)] public int maxParticlesPerEvent = 20;

    ParticleSystem dripPs, splashPs;
    bool built;

    void Awake() { EnsureBuilt(); }

    // PotionLiquid.Awake() calls this explicitly (after possibly assigning dripMaterial/
    // splashMaterial) rather than relying on this component's own Awake() ordering: AddComponent<T>()
    // invokes T's Awake() synchronously, before the calling code gets a chance to set fields on the
    // newly-added component -- so building the particle systems here unconditionally on first use
    // (guarded by `built`) means it's safe to call this again after the material fields are set, and
    // the systems get rebuilt with the correct materials instead of silently keeping null ones.
    public void EnsureBuilt(bool rebuildMaterialsOnly = false)
    {
        if (built && !rebuildMaterialsOnly)
            return;

        if (!built)
        {
            dripPs = CreateSystem("Drip", dripMaterial, dripLifetime, dripSize, dripGravityModifier);
            splashPs = CreateSystem("Splash", splashMaterial, splashLifetime, splashSize, splashGravityModifier);
            built = true;
            return;
        }

        // rebuildMaterialsOnly: systems already exist, just re-apply materials. CreateSystem runs
        // during the FIRST EnsureBuilt() call from Awake(), before PotionLiquid has assigned real
        // materials (AddComponent<T>() invokes T.Awake() synchronously before the caller can set
        // fields on it), so `mat` is null there -- this rebuild path is what actually applies the
        // real materials once PotionLiquid sets dripMaterial/splashMaterial and calls back in.
        if (dripPs != null && dripMaterial != null)
            dripPs.GetComponent<ParticleSystemRenderer>().sharedMaterial = dripMaterial;
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
        // FIXED 2026-08-12, second attempt (bug report: "still just lines, no reality to it" --
        // Stretched Billboard was ALSO wrong: stretching a quad along its own velocity is
        // definitionally a thin streak, so it hit the exact same "looks like a line" complaint via
        // a different mechanism than the Trails ribbon it replaced). Back to plain round Billboard
        // for BOTH systems -- the round soft-alpha sprite (T_PotionDropSoft) rendered at a real
        // blob size, unstretched, with gentle velocity (see NotifySpillPoint) is what actually
        // reads as a drop instead of a streak.
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (mat != null)
        {
            renderer.sharedMaterial = mat;
        }

        // Shrinks slightly over its life instead of popping out at a constant size -- reads as the
        // drop thinning/dissipating rather than a hard-edged sprite blinking off.
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
        col.enabled = false; // v1: no world collision response, keep it cheap; revisit if drips need to pool/stick

        var trails = ps.trails;
        trails.enabled = false;

        return ps;
    }

    // Called ONCE per frame (from the single currently-dominant overflow point -- see
    // PotionLiquid.DeformMeshAndHandleOverflow) rather than once per overflowing rim segment. volume
    // is the FULL accumulated spilled amount for this frame; speed is the peak local surface rise
    // speed, used only to pick drip-vs-splash and scale initial velocity.
    public void NotifySpillPoint(Vector3 worldPos, Vector3 spillDirWorld, float volume, float speed)
    {
        if (volume <= 0f) return;
        EnsureBuilt();

        bool splash = speed >= splashSpeedThreshold;
        ParticleSystem ps = splash ? splashPs : dripPs;
        if (ps == null) return;

        float perVolume = splash ? splashParticlesPerVolume : dripParticlesPerVolume;
        int count = Mathf.Clamp(Mathf.RoundToInt(volume * perVolume), 1, maxParticlesPerEvent);

        Vector3 dir = spillDirWorld.sqrMagnitude > 1e-6f ? spillDirWorld.normalized : Vector3.down;
        float baseSpeed = splash ? splashSpeedMultiplier : dripSpeedMultiplier;

        var emitParams = new ParticleSystem.EmitParams();
        emitParams.startColor = potionColor;

        for (int i = 0; i < count; i++)
        {
            Vector3 d = dir;
            // Jittered per-particle 2026-08-12: since every particle in this burst now comes from
            // the SAME single dominant point (see call site), emitting them all with identical
            // direction/position would just redraw one particle repeatedly -- i.e. still a line, one
            // dot at a time. A small random cone spread (even for plain drips, not just splash) plus
            // a staggered starting offset along the fall direction spreads the burst into a loose
            // cluster of drops falling together, which is what actually reads as "liquid pouring"
            // rather than a single-file streak.
            float coneDeg = splash ? splashSpread : 10f;
            d = Quaternion.AngleAxis(Random.Range(-coneDeg, coneDeg), Vector3.up) * d;
            d = Quaternion.AngleAxis(Random.Range(-coneDeg * 0.5f, coneDeg * 0.5f), Vector3.Cross(d, Vector3.up)) * d;
            if (splash) d = Vector3.Slerp(d, Random.onUnitSphere, 0.15f);

            float stagger = Random.Range(0f, 0.06f);
            Vector3 jitterPos = worldPos + Random.insideUnitSphere * 0.015f + dir * stagger;

            emitParams.position = jitterPos;
            emitParams.velocity = d * baseSpeed * Mathf.Max(0.25f, speed) * Random.Range(0.8f, 1.2f);
            emitParams.startSize = (splash ? splashSize : dripSize) * Random.Range(0.75f, 1.25f);
            ps.Emit(emitParams, 1);
        }
    }
}
