using UnityEngine;

// Self-contained potion-liquid simulation for the pot the goblin carries. Does not modify
// GoblinCarryRig or any other existing script -- it only READS Carry_Pot's resulting world
// Transform each frame (after GoblinCarryRig has positioned it) and builds/deforms its own child
// mesh. Runs after GoblinCarryRig's LateUpdate via [DefaultExecutionOrder] so it always sees this
// frame's final pot pose, not last frame's.
//
// Core model: PotionVolume/MaxPotionVolume are the only things that change the liquid's total
// AMOUNT. Tilting/shaking the pot alone never touches PotionVolume -- only Overflow (the surface
// actually rising above the rim, in world-gravity terms) drains it. Liquid SHAPE (surface height +
// tilt + waves) is fully separate from liquid AMOUNT and is recomputed every frame from the current
// PotionVolume plus the pot's instantaneous motion.
//
// REWORKED 2026-08-12 ("粘性のある液体としての表現に達していない" -- the flat sin-phase surface and
// line-particle overflow were rejected outright as a "green plane", not a liquid). Two structural
// changes from the previous version:
//   1) The wave model is now a pool of discrete, DIRECTIONAL "wave impulses" (see StepImpulses/
//      ImpulseHeightAt below) spawned from real motion events (accel spikes, hard turns, landings)
//      instead of one continuous fixed-phase sin field. Each impulse is a Ricker ("Mexican hat")
//      wavelet -- a single rounded mountain flanked by two shallow valleys -- that visibly
//      propagates outward from the pot's center and decays over its own lifetime, so the mesh
//      itself shows real traveling peaks/troughs (see the ASCII art in the request) rather than a
//      uniform standing ripple.
//   2) Overflow no longer talks to a particle system directly. It now feeds a mesh-based
//      PotionOverflowStream (thick root -> tapered body -> droplet bulge, see that file) every
//      frame while actively spilling, and only additionally triggers PotionOverflowVFX's particle
//      burst for genuinely fast/violent spills (sudden stops etc.) -- ordinary pouring is 100% mesh,
//      never a particle line.
[DefaultExecutionOrder(100)]
public class PotionLiquid : MonoBehaviour
{
    [Header("Volume")]
    [Tooltip("Liquid amount at which the pot is considered full. Units match the pot's own measured interior volume (local-space cubic units) -- this pot's actual measured capacity is ~0.044, so values much larger than that get clamped down to it at runtime.")]
    public float maxPotionVolume = 0.044f;
    [Tooltip("Starting liquid amount (same units as maxPotionVolume). Defaults to full -- set below maxPotionVolume in the Inspector if a less-than-full start is wanted.")]
    public float initialPotionVolume = 0.044f;

    [Header("Inertia (spring-damper tilt -- the liquid's steady lean, separate from traveling wave impulses below)")]
    [Tooltip("How strongly the liquid surface chases its target tilt (higher = snappier/stiffer).")]
    public float inertiaSpringStrength = 55f;
    [Tooltip("Per-second damping fraction applied to tilt velocity (higher = settles faster, less overshoot).")]
    public float inertiaDamping = 5.5f;
    [Tooltip("Maximum surface tilt from level, in degrees, regardless of how extreme the effective gravity direction is.")]
    public float maxTiltAngle = 42f;
    [Tooltip("How strongly the pot's own acceleration contributes to effective gravity (1 = physically correct pseudo-force).")]
    public float accelerationSensitivity = 1.15f;
    [Tooltip("Blend-in amount of the ground slope directly beneath the pot (independent of whether the character body itself leans) -- 0 disables.")]
    [Range(0f, 1f)] public float groundSlopeInfluence = 0.35f;

    [Header("Wave Impulses (2026-08-12 rework: discrete propagating wavelets instead of a fixed-phase sin field, so the mesh shows real traveling mountains/valleys)")]
    [Tooltip("Combined tilt-velocity/angular-velocity/acceleration excitation above which a new wave impulse is spawned.")]
    public float impulseSpawnThreshold = 0.05f;
    [Tooltip("Minimum seconds between spawning new impulses, even while excitation stays high -- keeps a sustained shake to a train of distinct waves instead of one smeared blob.")]
    public float impulseSpawnCooldown = 0.1f;
    [Tooltip("How strongly excitation converts into a new impulse's peak height.")]
    public float impulseAmplitudeGain = 0.06f;
    [Tooltip("Hard cap on a single impulse's peak height (m), and the normalization reference used for the mesh's crest vertex-color signal read by the shader.")]
    public float maxImpulseAmplitude = 0.05f;
    [Tooltip("Outward propagation speed of a wave impulse (m/s) -- lower reads as a heavier, more viscous liquid.")]
    public float impulseSpeed = 0.9f;
    [Tooltip("Spatial width of a wave impulse's mountain/valley -- larger = broader, thicker-looking swells instead of a tight ripple.")]
    public float impulseWavelength = 0.09f;
    [Tooltip("Per-second amplitude decay of an individual impulse once spawned -- lower means waves linger longer (viscous).")]
    public float impulseDampingPerSecond = 1.1f;
    [Tooltip("An impulse is discarded once its age exceeds this (seconds), regardless of remaining amplitude.")]
    public float impulseMaxLifetime = 4f;
    [Range(1, 10)] public int maxActiveImpulses = 6;

    [Header("Ambient micro-ripple (always-present fine shimmer, on top of the mesh-level waves above)")]
    public float smallWaveAmplitude = 0.004f;
    [Tooltip("Speed of the ambient micro-ripple -- lower reads as thicker/heavier liquid.")]
    public float smallWaveSpeed = 1.3f;

    [Header("Overflow (surface rising above the rim, in world-gravity terms, drains PotionVolume)")]
    [Tooltip("How readily liquid actually drains once the surface is over the rim (higher = spills faster for the same excess).")]
    public float overflowRate = 6f;
    [Tooltip("Spill speed (m/s of surface rise, roughly) above which the violent-splash particle burst also fires on top of the always-on mesh stream. Also drives PotionOverflowVFX's own splash threshold.")]
    public float overflowSplashSpeed = 0.45f;

    [Header("Mesh")]
    public int radialSegments = 40;
    [Tooltip("Concentric rings from center to rim on the liquid surface cap -- raised 2026-08-12 so a wave's mountain/valley shape has enough resolution to actually read as curved geometry instead of a few flat facets.")]
    public int capRings = 6;
    [Tooltip("Vertical rings used for the static (non-wavy) side wall between the pot's floor and the liquid surface.")]
    public int sideWallRings = 4;
    public Material liquidMaterial;

    [Header("Overflow VFX (auto-created children)")]
    [Tooltip("Mesh-based flowing stream + droplets -- handles ALL overflow, every frame, regardless of speed.")]
    public PotionOverflowStream overflowStream;
    [Tooltip("Particle burst reserved for fast/violent spills only (see overflowSplashSpeed).")]
    public PotionOverflowVFX overflowVfx;
    public Material overflowSplashMaterial;

    [Header("Refs (auto-found if left empty)")]
    public Transform potMeshSource; // the GameObject whose MeshFilter defines the pot's interior profile

    public float PotionVolume { get; private set; }
    public float SurfaceHeightLocal { get; private set; }
    public float FillFraction01 => Mathf.Clamp01(PotionVolume / Mathf.Max(0.0001f, maxPotionVolume));

    // -- kinematics --
    Vector3 prevPos;
    Quaternion prevRot;
    Vector3 velocity;
    Vector3 acceleration;
    Vector3 angularVelocityLocal;
    bool kinematicsPrimed;

    // -- simulation clock -- driven entirely by Step(dt)'s dt, never Time.time/Time.deltaTime
    // directly, so the whole simulation stays deterministically testable via manual Step() calls
    // (see the class doc on Step() below).
    float simTime;

    // -- inertia (steady lean) state --
    Vector2 tiltVector;      // (x-slope, z-slope), local space, rise-per-run
    Vector2 tiltVelocity;
    Vector2 tiltTarget;

    // -- wave impulse pool --
    struct WaveImpulse
    {
        public bool active;
        public Vector2 dir;       // local-space horizontal unit direction the wave travels
        public float amplitude;   // peak height contribution (m) at spawn
        public float spawnSimTime;
        public float speed;
        public float wavelength;
        public float damping;
    }
    WaveImpulse[] impulses;
    float impulseCooldownTimer;

    // -- interior profile (sampled once from the pot mesh) --
    float[] profileHeights;
    float[] profileRadii;
    float[] cumulativeVolume;
    float rimHeightLocal;
    float rimRadiusLocal;
    float floorHeightLocal;

    // -- mesh --
    Mesh liquidMesh;
    MeshFilter meshFilter;
    Transform liquidTransform;
    Vector3[] vertsBuffer;
    Color[] colorsBuffer;
    float[] ringHeightScratch;
    float[] ringWaveScratch;
    float[] segExcessScratch;

    void Awake()
    {
        if (potMeshSource == null) potMeshSource = transform;
        impulses = new WaveImpulse[Mathf.Max(1, maxActiveImpulses)];
        SampleInteriorProfile();
        maxPotionVolume = Mathf.Min(maxPotionVolume, cumulativeVolume[cumulativeVolume.Length - 1]);
        PotionVolume = Mathf.Clamp(initialPotionVolume, 0f, maxPotionVolume);
        SurfaceHeightLocal = HeightForVolume(PotionVolume);
        BuildLiquidMeshObject();

        if (overflowStream == null) overflowStream = GetComponentInChildren<PotionOverflowStream>();
        if (overflowStream == null)
        {
            var streamGo = new GameObject("PotionOverflowStream");
            streamGo.transform.SetParent(transform, false);
            overflowStream = streamGo.AddComponent<PotionOverflowStream>();
        }
        if (liquidMaterial != null) overflowStream.liquidMaterial = liquidMaterial;
        overflowStream.EnsureBuilt(rebuildMaterialsOnly: true);

        if (overflowVfx == null) overflowVfx = GetComponentInChildren<PotionOverflowVFX>();
        if (overflowVfx == null)
        {
            var vfxGo = new GameObject("PotionOverflowVFX");
            vfxGo.transform.SetParent(transform, false);
            overflowVfx = vfxGo.AddComponent<PotionOverflowVFX>();
        }
        overflowVfx.splashSpeedThreshold = overflowSplashSpeed;
        if (overflowSplashMaterial != null) overflowVfx.splashMaterial = overflowSplashMaterial;
        overflowVfx.EnsureBuilt(rebuildMaterialsOnly: true);
    }

    void Start()
    {
        prevPos = transform.position;
        prevRot = transform.rotation;
        kinematicsPrimed = true;
    }

    // ---------------------------------------------------------------------
    // Interior geometry: sample the pot mesh's own vertices to build a
    // height -> interior-radius profile, then integrate it into a
    // height -> cumulative-volume table. This makes the whole system adapt
    // automatically to whatever the actual pot mesh shape is (measured as a
    // barrel: narrow foot, wide belly, tapering back in toward the rim).
    // ---------------------------------------------------------------------
    void SampleInteriorProfile()
    {
        var mf = potMeshSource.GetComponentInChildren<MeshFilter>();
        Mesh srcMesh = mf != null ? mf.sharedMesh : null;
        if (srcMesh == null)
        {
            Debug.LogError("PotionLiquid: no MeshFilter found under " + potMeshSource.name + " to sample the pot's interior profile from.");
            profileHeights = new float[] { 0f, 0.3f };
            profileRadii = new float[] { 0.15f, 0.15f };
            cumulativeVolume = new float[] { 0f, 0f };
            rimHeightLocal = 0.3f;
            rimRadiusLocal = 0.15f;
            floorHeightLocal = 0f;
            return;
        }

        Vector3[] verts = srcMesh.vertices; // in the pot mesh's own local space (unscaled)
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < verts.Length; i++)
        {
            if (verts[i].y < minY) minY = verts[i].y;
            if (verts[i].y > maxY) maxY = verts[i].y;
        }

        int bins = 24;
        float[] binMinR = new float[bins + 1];
        bool[] binHas = new bool[bins + 1];
        for (int b = 0; b <= bins; b++) binMinR[b] = float.MaxValue;

        float span = Mathf.Max(0.0001f, maxY - minY);
        for (int i = 0; i < verts.Length; i++)
        {
            float t = (verts[i].y - minY) / span;
            int b = Mathf.Clamp(Mathf.RoundToInt(t * bins), 0, bins);
            float r = Mathf.Sqrt(verts[i].x * verts[i].x + verts[i].z * verts[i].z);
            if (r < binMinR[b]) { binMinR[b] = r; binHas[b] = true; }
        }

        // The lowest bins include the solid foot underside (near-zero radius); skip leading bins
        // with no clear interior opening by starting the profile at the first bin whose min-radius
        // stabilizes (interior wall present), falling back to bin 0 if the mesh is very simple.
        int startBin = 0;
        for (int b = 0; b <= bins; b++) { if (binHas[b]) { startBin = b; break; } }

        var heights = new System.Collections.Generic.List<float>();
        var radii = new System.Collections.Generic.List<float>();
        for (int b = startBin; b <= bins; b++)
        {
            if (!binHas[b]) continue;
            float y = minY + (b / (float)bins) * span;
            heights.Add(y);
            radii.Add(binMinR[b]);
        }
        if (heights.Count < 2)
        {
            heights.Clear(); radii.Clear();
            heights.Add(minY); radii.Add(0.15f);
            heights.Add(maxY); radii.Add(0.15f);
        }

        profileHeights = heights.ToArray();
        profileRadii = radii.ToArray();
        floorHeightLocal = profileHeights[0];
        rimHeightLocal = profileHeights[profileHeights.Length - 1];
        rimRadiusLocal = profileRadii[profileRadii.Length - 1];

        // Integrate cross-sectional disc area (pi r^2) via trapezoidal rule for a height->volume table.
        cumulativeVolume = new float[profileHeights.Length];
        cumulativeVolume[0] = 0f;
        for (int i = 1; i < profileHeights.Length; i++)
        {
            float dy = profileHeights[i] - profileHeights[i - 1];
            float a0 = Mathf.PI * profileRadii[i - 1] * profileRadii[i - 1];
            float a1 = Mathf.PI * profileRadii[i] * profileRadii[i];
            cumulativeVolume[i] = cumulativeVolume[i - 1] + 0.5f * (a0 + a1) * dy;
        }
    }

    float RadiusAtHeight(float y)
    {
        if (y <= profileHeights[0]) return profileRadii[0];
        int n = profileHeights.Length;
        if (y >= profileHeights[n - 1]) return profileRadii[n - 1];
        for (int i = 1; i < n; i++)
        {
            if (y <= profileHeights[i])
            {
                float t = (y - profileHeights[i - 1]) / Mathf.Max(0.0001f, profileHeights[i] - profileHeights[i - 1]);
                return Mathf.Lerp(profileRadii[i - 1], profileRadii[i], t);
            }
        }
        return profileRadii[n - 1];
    }

    float HeightForVolume(float volume)
    {
        int n = cumulativeVolume.Length;
        float total = cumulativeVolume[n - 1];
        volume = Mathf.Clamp(volume, 0f, total);
        if (volume <= 0f) return profileHeights[0];
        for (int i = 1; i < n; i++)
        {
            if (volume <= cumulativeVolume[i])
            {
                float t = (volume - cumulativeVolume[i - 1]) / Mathf.Max(0.0001f, cumulativeVolume[i] - cumulativeVolume[i - 1]);
                return Mathf.Lerp(profileHeights[i - 1], profileHeights[i], t);
            }
        }
        return profileHeights[n - 1];
    }

    // ---------------------------------------------------------------------
    // Per-frame update
    // ---------------------------------------------------------------------
    void LateUpdate()
    {
        Step(Time.deltaTime);
    }

    // Split out from LateUpdate so it can be driven with an explicit dt -- both for normal per-frame
    // play (LateUpdate above) and, since Time.deltaTime can't be forced from outside the player loop,
    // for editor/automation testing that steps the simulation deterministically regardless of actual
    // frame timing. Every time-dependent quantity in this class derives from `simTime`/dt, never
    // Time.time, so repeated Step(dt) calls with a synthetic dt reproduce exactly what real play
    // would show at that elapsed time.
    public void Step(float dt)
    {
        if (dt <= 0f || !kinematicsPrimed) { prevPos = transform.position; prevRot = transform.rotation; kinematicsPrimed = true; return; }

        // Defensive re-init: `impulses` is a private array of a plain (non-[Serializable]) struct,
        // which Unity's domain-reload state preservation cannot round-trip -- recompiling scripts
        // while already in Play mode nulls it out without Awake() running again. Real gameplay never
        // hits this (Awake() always runs before the first Step()), but this guard makes Step() safe
        // regardless.
        if (impulses == null || impulses.Length != Mathf.Max(1, maxActiveImpulses))
            impulses = new WaveImpulse[Mathf.Max(1, maxActiveImpulses)];

        simTime += dt;
        UpdateKinematics(dt);
        UpdateInertiaTarget();
        StepSpringDamper(dt);
        StepImpulses(dt);

        SurfaceHeightLocal = HeightForVolume(PotionVolume);
        DeformMeshAndHandleOverflow(dt);
    }

    void UpdateKinematics(float dt)
    {
        Vector3 newVel = (transform.position - prevPos) / dt;
        acceleration = (newVel - velocity) / dt;
        velocity = newVel;

        Quaternion deltaRot = transform.rotation * Quaternion.Inverse(prevRot);
        deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axisWorld);
        if (float.IsNaN(axisWorld.x)) axisWorld = Vector3.up;
        if (angleDeg > 180f) angleDeg -= 360f;
        Vector3 angularVelWorld = axisWorld * (angleDeg * Mathf.Deg2Rad / dt);
        angularVelocityLocal = Quaternion.Inverse(transform.rotation) * angularVelWorld;

        prevPos = transform.position;
        prevRot = transform.rotation;
    }

    // World-gravity-first: EffectiveGravity = WorldGravity - PotAcceleration (D'Alembert pseudo-force,
    // same reason coffee piles up on the far side of the cup when you suddenly accelerate forward),
    // optionally blended with the ground slope sampled directly beneath the pot. Everything is
    // expressed in WORLD space and only converted into the pot's local frame at the very end via
    // the pot's actual current world rotation -- so pot tilt, goblin body lean, and (via the ground
    // sample) slope all flow through the same single calculation, never an assumption that local +Y
    // is "up".
    void UpdateInertiaTarget()
    {
        Vector3 effGravityWorld = Physics.gravity - acceleration * accelerationSensitivity;

        if (groundSlopeInfluence > 0f)
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                Vector3 slopeGravity = Vector3.ProjectOnPlane(Physics.gravity, hit.normal).normalized * Physics.gravity.magnitude;
                // Blend only the horizontal deflection the slope would add, not a full replacement.
                effGravityWorld = Vector3.Lerp(effGravityWorld, effGravityWorld + (slopeGravity - Vector3.down * Physics.gravity.magnitude) * 0.6f, groundSlopeInfluence);
            }
        }

        Vector3 effGravityLocal = Quaternion.Inverse(transform.rotation) * effGravityWorld;
        Vector3 downLocal = effGravityLocal.sqrMagnitude > 1e-6f ? effGravityLocal.normalized : Vector3.down;

        float verticalComponent = Mathf.Max(0.15f, -downLocal.y);
        Vector2 rawTilt = new Vector2(downLocal.x, downLocal.z) / verticalComponent;

        float maxTiltTan = Mathf.Tan(maxTiltAngle * Mathf.Deg2Rad);
        if (rawTilt.magnitude > maxTiltTan) rawTilt = rawTilt.normalized * maxTiltTan;

        tiltTarget = rawTilt;
    }

    void StepSpringDamper(float dt)
    {
        Vector2 diff = tiltTarget - tiltVector;
        tiltVelocity += diff * inertiaSpringStrength * dt;
        tiltVelocity *= Mathf.Clamp01(1f - inertiaDamping * dt);
        tiltVector += tiltVelocity * dt;
    }

    // Spawns/ages/retires the traveling wave-impulse pool. A new impulse is only spawned when
    // excitation (tilt-velocity + angular-velocity + acceleration spike) crosses a threshold AND the
    // cooldown has elapsed -- this is what makes a sudden stop/turn read as a distinct, countable
    // wave event instead of one continuous smear, matching "急加速・急停止・方向転換では明確に大きな
    // 波が発生すること".
    void StepImpulses(float dt)
    {
        impulseCooldownTimer -= dt;

        float excitation = tiltVelocity.magnitude + angularVelocityLocal.magnitude * 0.3f + Mathf.Max(0f, acceleration.magnitude - 3f) * 0.15f;

        if (excitation > impulseSpawnThreshold && impulseCooldownTimer <= 0f)
        {
            Vector2 dir = tiltVelocity.sqrMagnitude > 0.0001f ? tiltVelocity.normalized : Vector2.up;
            SpawnImpulse(dir, Mathf.Min(excitation * impulseAmplitudeGain, maxImpulseAmplitude));
            impulseCooldownTimer = impulseSpawnCooldown;
        }

        for (int i = 0; i < impulses.Length; i++)
        {
            if (!impulses[i].active) continue;
            float age = simTime - impulses[i].spawnSimTime;
            float remaining = impulses[i].amplitude * Mathf.Exp(-impulses[i].damping * age);
            if (age > impulseMaxLifetime || remaining < 0.0005f)
                impulses[i].active = false;
        }
    }

    void SpawnImpulse(Vector2 dirLocal, float amplitude)
    {
        int slot = -1;
        float weakestRemaining = float.MaxValue;
        for (int i = 0; i < impulses.Length; i++)
        {
            if (!impulses[i].active) { slot = i; break; }
            float remaining = impulses[i].amplitude * Mathf.Exp(-impulses[i].damping * (simTime - impulses[i].spawnSimTime));
            if (remaining < weakestRemaining) { weakestRemaining = remaining; slot = i; }
        }
        if (slot < 0) slot = 0;

        impulses[slot] = new WaveImpulse
        {
            active = true,
            dir = dirLocal,
            amplitude = amplitude,
            spawnSimTime = simTime,
            speed = impulseSpeed,
            wavelength = Mathf.Max(0.02f, impulseWavelength),
            damping = Mathf.Max(0.05f, impulseDampingPerSecond),
        };
    }

    // One impulse's contribution at local point (x,z): a Ricker ("Mexican hat") wavelet centered on
    // a wavefront that moves outward from the pot's center at `speed` as the impulse ages -- a single
    // rounded mountain flanked by two shallow valleys that visibly travels and fades, rather than an
    // infinite standing sine train.
    float ImpulseHeightAt(in WaveImpulse imp, float x, float z)
    {
        float age = simTime - imp.spawnSimTime;
        if (age < 0f) return 0f;

        float dist = x * imp.dir.x + z * imp.dir.y;
        float front = imp.speed * age;
        float u = dist - front;
        float sigma = imp.wavelength;
        float u2 = (u * u) / (sigma * sigma);
        float ricker = (1f - u2) * Mathf.Exp(-u2 * 0.5f);

        float ampNow = imp.amplitude * Mathf.Exp(-imp.damping * age);
        return ampNow * ricker;
    }

    float ImpulsesHeightAt(float x, float z)
    {
        float h = 0f;
        for (int i = 0; i < impulses.Length; i++)
            if (impulses[i].active) h += ImpulseHeightAt(impulses[i], x, z);
        return h;
    }

    // waveOnly is the impulse-only contribution (excludes steady tilt/ambient), used to paint the
    // mesh's per-vertex crest/trough color so the shader can add a highlight right at wave peaks.
    float SurfaceHeightAt(float x, float z, out float waveOnly)
    {
        float tilt = tiltVector.x * x + tiltVector.y * z;
        waveOnly = ImpulsesHeightAt(x, z);
        float ambient = smallWaveAmplitude * 0.5f * (Mathf.Sin(x * 6f + simTime * smallWaveSpeed) + Mathf.Sin(z * 4.3f - simTime * smallWaveSpeed * 0.77f));
        return SurfaceHeightLocal + tilt + waveOnly + ambient;
    }

    // ---------------------------------------------------------------------
    // Mesh build / deform + overflow
    // ---------------------------------------------------------------------
    void BuildLiquidMeshObject()
    {
        var go = new GameObject("InsideLiquid");
        go.transform.SetParent(transform, false);
        liquidTransform = go.transform;
        meshFilter = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (liquidMaterial != null) mr.sharedMaterial = liquidMaterial;

        liquidMesh = new Mesh();
        liquidMesh.name = "InsideLiquidMesh";
        liquidMesh.MarkDynamic();
        meshFilter.sharedMesh = liquidMesh;

        int capVertCount = (capRings + 1) * radialSegments + 1;
        int sideVertCount = (sideWallRings + 1) * radialSegments;
        vertsBuffer = new Vector3[capVertCount + sideVertCount];
        colorsBuffer = new Color[capVertCount + sideVertCount];
        ringHeightScratch = new float[capRings + 1];
        ringWaveScratch = new float[capRings + 1];
        segExcessScratch = new float[radialSegments];

        RebuildTriangles(capVertCount, sideVertCount);
        DeformMeshAndHandleOverflow(0f);
    }

    int[] cachedTriangles;
    void RebuildTriangles(int capVertCount, int sideVertCount)
    {
        var tris = new System.Collections.Generic.List<int>();
        int centerIndex = capVertCount - 1;

        // Cap: capRings concentric rings (index 0 = innermost) + center fan, using radialSegments per ring.
        for (int seg = 0; seg < radialSegments; seg++)
        {
            int segNext = (seg + 1) % radialSegments;
            // innermost ring to center
            int a = 0 * radialSegments + seg;
            int b = 0 * radialSegments + segNext;
            tris.Add(centerIndex); tris.Add(a); tris.Add(b);
        }
        for (int ring = 0; ring < capRings; ring++)
        {
            for (int seg = 0; seg < radialSegments; seg++)
            {
                int segNext = (seg + 1) % radialSegments;
                int a0 = ring * radialSegments + seg;
                int a1 = ring * radialSegments + segNext;
                int b0 = (ring + 1) * radialSegments + seg;
                int b1 = (ring + 1) * radialSegments + segNext;
                tris.Add(a0); tris.Add(b0); tris.Add(b1);
                tris.Add(a0); tris.Add(b1); tris.Add(a1);
            }
        }

        // Side wall: sideWallRings+1 rings from floor (0) up to just under the surface ring, then
        // connect the topmost side ring to the cap's outer ring (index capRings*radialSegments..).
        int sideBase = capVertCount;
        int capOuterRingBase = capRings * radialSegments;
        for (int ring = 0; ring < sideWallRings; ring++)
        {
            for (int seg = 0; seg < radialSegments; seg++)
            {
                int segNext = (seg + 1) % radialSegments;
                int a0 = sideBase + ring * radialSegments + seg;
                int a1 = sideBase + ring * radialSegments + segNext;
                int b0 = sideBase + (ring + 1) * radialSegments + seg;
                int b1 = sideBase + (ring + 1) * radialSegments + segNext;
                tris.Add(a0); tris.Add(b1); tris.Add(b0);
                tris.Add(a0); tris.Add(a1); tris.Add(b1);
            }
        }
        // topmost side ring (last row) connects to the cap's outer ring
        for (int seg = 0; seg < radialSegments; seg++)
        {
            int segNext = (seg + 1) % radialSegments;
            int a0 = sideBase + sideWallRings * radialSegments + seg;
            int a1 = sideBase + sideWallRings * radialSegments + segNext;
            int b0 = capOuterRingBase + seg;
            int b1 = capOuterRingBase + segNext;
            tris.Add(a0); tris.Add(b1); tris.Add(b0);
            tris.Add(a0); tris.Add(a1); tris.Add(b1);
        }

        cachedTriangles = tris.ToArray();
    }

    void DeformMeshAndHandleOverflow(float dt)
    {
        int capVertCount = (capRings + 1) * radialSegments + 1;
        int centerIndex = capVertCount - 1;

        float totalOverflowVolume = 0f;
        float wedgeAngle = Mathf.PI * 2f / radialSegments;

        float maxSurfaceSpillSpeed = 0f;
        float maxImpulseAmpSafe = Mathf.Max(0.001f, maxImpulseAmplitude);

        // Real liquid pours from a small number of places at a time -- wherever the surface is
        // currently highest above the rim -- not from a dozen scattered points simultaneously. This
        // loop only RECORDS the single best (dominant) and second-best excess segments; the actual
        // overflow feed happens once, after the loop, splitting the FULL accumulated
        // totalOverflowVolume between them proportionally to how much each is actually overflowing
        // (a narrow crest feeds one point only -- the second slot never qualifies -- while a wide
        // crest spanning a broad arc of the rim visibly pours from two points at once, reading as
        // proportionally bigger instead of always funneling through one thin point regardless of how
        // much of the rim is actually overflowing).
        int dominantSeg = -1;
        float dominantExcess = 0f;
        Vector3 dominantDir = Vector3.zero;
        int secondSeg = -1;
        float secondExcess = 0f;
        Vector3 secondDir = Vector3.zero;

        // Cap rings (0 = innermost .. capRings = outer/rim ring). Three passes per segment:
        //  1) raw (unclamped) wave height at every ring -- a traveling wave impulse can crest well
        //     inland, not just right at the wall, so height has to be sampled everywhere first.
        //  2) integrate excess-above-rim volume across the WHOLE disk via trapezoidal annuli (not
        //     just the outer edge -- an earlier version only counted the outer ring's own excess,
        //     which let an inland crest sit indefinitely above the rim line without draining
        //     PotionVolume or ever being flattened, i.e. liquid visibly mounded up above the opening
        //     with nothing accounted for). Every ring whose raw height exceeds the rim gets flattened
        //     back down here -- the excess becomes Overflow instead.
        //  3) write out the (now-flattened) heights, with the outer ring additionally radius-clamped
        //     to the wall's true interior radius at its own height (the hard "never pokes through the
        //     pot" containment rule -- spec section 4/7).
        for (int seg = 0; seg < radialSegments; seg++)
        {
            float theta = seg * wedgeAngle;
            float dirX = Mathf.Cos(theta), dirZ = Mathf.Sin(theta);

            for (int ring = 0; ring <= capRings; ring++)
            {
                float t = (ring + 1) / (float)(capRings + 1); // >0, reaches 1 at the outer/rim ring
                float nominalRadius = ring == capRings ? rimRadiusLocal : rimRadiusLocal * t;
                float h = SurfaceHeightAt(dirX * nominalRadius, dirZ * nominalRadius, out float waveOnly);
                ringHeightScratch[ring] = h;
                ringWaveScratch[ring] = waveOnly;
            }

            float prevR = 0f;
            float prevExcess = Mathf.Max(0f, ringHeightScratch[0] - rimHeightLocal);
            for (int ring = 0; ring <= capRings; ring++)
            {
                float r = ring == capRings ? rimRadiusLocal : rimRadiusLocal * ((ring + 1) / (float)(capRings + 1));
                float excess = Mathf.Max(0f, ringHeightScratch[ring] - rimHeightLocal);

                if (ring > 0)
                {
                    float avgExcess = 0.5f * (excess + prevExcess);
                    float area = 0.5f * wedgeAngle * (r * r - prevR * prevR);
                    totalOverflowVolume += avgExcess * area;
                }
                prevR = r; prevExcess = excess;

                if (excess > 0f)
                {
                    // Clamped: excess/dt is unbounded and a single unusually large dt (a stutter
                    // frame, or many manual dt steps applied back-to-back during testing) could
                    // otherwise produce an absurd "speed". A real single-frame spill speed has no
                    // business being much faster than this regardless.
                    float spillSpeed = Mathf.Min(excess / Mathf.Max(dt, 0.0001f), 2.5f);
                    maxSurfaceSpillSpeed = Mathf.Max(maxSurfaceSpillSpeed, spillSpeed);
                    ringHeightScratch[ring] = rimHeightLocal + 0.002f; // flatten the bulge back down
                }

                if (ring == capRings) segExcessScratch[seg] = excess;
            }

            for (int ring = 0; ring <= capRings; ring++)
            {
                float t = (ring + 1) / (float)(capRings + 1);
                float nominalRadius = ring == capRings ? rimRadiusLocal : rimRadiusLocal * t;
                float x = dirX * nominalRadius;
                float z = dirZ * nominalRadius;
                float h = ringHeightScratch[ring];

                if (ring == capRings)
                {
                    // Clamp to the wall's actual interior radius AT this (already-flattened) height
                    // so the outer edge can never poke through the taper.
                    float wallR = RadiusAtHeight(Mathf.Min(h, rimHeightLocal));
                    x = dirX * wallR; z = dirZ * wallR;
                }

                int idx = ring * radialSegments + seg;
                vertsBuffer[idx] = new Vector3(x, h, z);
                colorsBuffer[idx] = new Color(Mathf.Clamp01(0.5f + ringWaveScratch[ring] / (2f * maxImpulseAmpSafe)), 0f, 0f, 1f);
            }
        }

        // Pick the dominant overflow point, then a second point ONLY if it's both a comparable
        // excess (so a wide crest genuinely pours from two places) and angularly well separated from
        // the first (so two adjacent segments of the same single crest never get treated as two
        // separate pour points, which would just be the old scattered-line bug again in miniature).
        for (int seg = 0; seg < radialSegments; seg++)
        {
            if (segExcessScratch[seg] > dominantExcess)
            {
                dominantExcess = segExcessScratch[seg];
                dominantSeg = seg;
            }
        }
        if (dominantSeg >= 0)
        {
            float theta = dominantSeg * wedgeAngle;
            dominantDir = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));

            int minSeparation = Mathf.Max(2, radialSegments / 8);
            for (int seg = 0; seg < radialSegments; seg++)
            {
                if (seg == dominantSeg) continue;
                int diff = Mathf.Abs(seg - dominantSeg);
                diff = Mathf.Min(diff, radialSegments - diff);
                if (diff < minSeparation) continue;
                if (segExcessScratch[seg] > secondExcess)
                {
                    secondExcess = segExcessScratch[seg];
                    secondSeg = seg;
                }
            }
            if (secondSeg >= 0 && secondExcess >= dominantExcess * 0.4f)
            {
                float theta2 = secondSeg * wedgeAngle;
                secondDir = new Vector3(Mathf.Cos(theta2), 0f, Mathf.Sin(theta2));
            }
            else
            {
                secondSeg = -1;
            }
        }

        // center vertex: average of innermost ring for a smooth apex
        Vector3 centerAvg = Vector3.zero;
        Color centerColorAvg = Color.clear;
        for (int seg = 0; seg < radialSegments; seg++)
        {
            centerAvg += vertsBuffer[0 * radialSegments + seg];
            centerColorAvg += colorsBuffer[0 * radialSegments + seg];
        }
        centerAvg /= radialSegments;
        centerColorAvg /= radialSegments;
        vertsBuffer[centerIndex] = new Vector3(0f, centerAvg.y, 0f);
        colorsBuffer[centerIndex] = centerColorAvg;

        // Side wall: static profile rings from floor up to (just under) the surface -- always
        // strictly following the measured interior wall, so it can never bulge outside the pot.
        int sideBase = capVertCount;
        for (int seg = 0; seg < radialSegments; seg++)
        {
            float theta = seg * wedgeAngle;
            float dirX = Mathf.Cos(theta), dirZ = Mathf.Sin(theta);
            for (int ring = 0; ring <= sideWallRings; ring++)
            {
                float t = ring / (float)sideWallRings;
                float y = Mathf.Lerp(floorHeightLocal + 0.001f, SurfaceHeightLocal, t);
                float wallR = RadiusAtHeight(y);
                int idx = sideBase + ring * radialSegments + seg;
                vertsBuffer[idx] = new Vector3(dirX * wallR, y, dirZ * wallR);
                colorsBuffer[idx] = new Color(0.5f, 0f, 0f, 1f);
            }
        }

        liquidMesh.vertices = vertsBuffer;
        liquidMesh.colors = colorsBuffer;
        if (liquidMesh.triangles.Length != cachedTriangles.Length) liquidMesh.triangles = cachedTriangles;
        else liquidMesh.SetTriangles(cachedTriangles, 0);
        liquidMesh.RecalculateNormals();
        liquidMesh.RecalculateBounds();

        if (totalOverflowVolume > 0f && dt > 0f)
        {
            float spill = Mathf.Min(totalOverflowVolume * overflowRate * dt, PotionVolume);
            PotionVolume = Mathf.Max(0f, PotionVolume - spill);

            if (dominantSeg >= 0)
            {
                Vector3 gravityDirWorld = Physics.gravity.sqrMagnitude > 1e-6f ? Physics.gravity.normalized : Vector3.down;
                float spillSpeedForVfx = Mathf.Min(maxSurfaceSpillSpeed, 2.5f);

                // Split the FULL accumulated totalOverflowVolume between the one or two active pour
                // points, proportional to each one's own excess -- so a wide overflow (both points
                // active) still carries its whole true volume total between them, rather than each
                // point acting as if it alone were the entire spill.
                float weightSum = dominantExcess + (secondSeg >= 0 ? secondExcess : 0f);
                float dominantShare = weightSum > 0f ? dominantExcess / weightSum : 1f;

                Vector3 rimLocal = new Vector3(dominantDir.x * rimRadiusLocal, rimHeightLocal, dominantDir.z * rimRadiusLocal);
                Vector3 worldPos = liquidTransform.TransformPoint(rimLocal);
                Vector3 outwardWorld = liquidTransform.TransformDirection(dominantDir).normalized;
                // World-gravity-based pour direction (spec section 7): follows actual effective
                // gravity, never a fixed local-down assumption, blended with a bit of outward push so
                // the stream visibly clears the rim edge before falling.
                Vector3 spillDir = (outwardWorld * 0.4f + gravityDirWorld * 0.9f).normalized;
                float flowRate = (totalOverflowVolume * dominantShare) / dt;
                if (overflowStream != null)
                    overflowStream.Feed(worldPos, spillDir, flowRate);

                if (secondSeg >= 0)
                {
                    Vector3 rimLocal2 = new Vector3(secondDir.x * rimRadiusLocal, rimHeightLocal, secondDir.z * rimRadiusLocal);
                    Vector3 worldPos2 = liquidTransform.TransformPoint(rimLocal2);
                    Vector3 outwardWorld2 = liquidTransform.TransformDirection(secondDir).normalized;
                    Vector3 spillDir2 = (outwardWorld2 * 0.4f + gravityDirWorld * 0.9f).normalized;
                    float flowRate2 = (totalOverflowVolume * (1f - dominantShare)) / dt;
                    if (overflowStream != null)
                        overflowStream.Feed(worldPos2, spillDir2, flowRate2);
                }

                // Splash burst reserved for genuinely violent/fast spills (sudden stop, hard impact)
                // -- ordinary pouring/dripping is handled entirely by the mesh-based stream(s) above.
                // Always from the single dominant point, even when two streams are pouring, so the
                // splash itself never re-introduces the old "scattered" look.
                if (overflowVfx != null && spillSpeedForVfx >= overflowSplashSpeed)
                    overflowVfx.NotifySplash(worldPos, spillDir, totalOverflowVolume, spillSpeedForVfx);
            }
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (profileHeights == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < profileHeights.Length; i++)
        {
            Vector3 c = transform.TransformPoint(new Vector3(0f, profileHeights[i], 0f));
            Gizmos.DrawWireSphere(c + transform.TransformDirection(Vector3.right) * profileRadii[i] * transform.lossyScale.x, 0.005f);
        }
    }
#endif
}
