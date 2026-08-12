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
// REWORKED 2026-08-12, three passes:
//   1) Wave model v1: a pool of independent Ricker-wavelet "impulses" spawned from motion events.
//   2) Overflow moved off particles onto a mesh-based PotionOverflowStream (see that file).
//   3) Wave model v2 ("液体を粒子の集合ではなく一つの液体として動かす...Node同士の拘束" -- v1's
//      impulses were independently-evaluated formulas with no physical connection to each other,
//      which is NOT the same as "one coupled body" no matter how their outputs are summed). The
//      surface's wave detail is now a StepRingNodes()-driven closed ring of spring-coupled mass
//      nodes around the rim -- a discretized 1D wave equation. Neighboring nodes physically pull on
//      each other (nodeCoupleStrength), so a disturbance at one point propagates around the ring and
//      decays as one connected system, producing naturally rounded crests as an EMERGENT property of
//      the spring coupling rather than a hand-authored wavelet shape. See RingHeightAt/StepRingNodes
//      below, and PotionOverflowStream.cs for the matching node-chain treatment of Overflow.
[DefaultExecutionOrder(100)]
public class PotionLiquid : MonoBehaviour
{
    [Header("Volume")]
    [Tooltip("Liquid amount at which the pot is considered full. Units match the pot's own measured interior volume (local-space cubic units) -- this pot's actual measured capacity is ~0.044, so values much larger than that get clamped down to it at runtime.")]
    public float maxPotionVolume = 0.044f;
    [Tooltip("Starting liquid amount (same units as maxPotionVolume). Defaults to full -- set below maxPotionVolume in the Inspector if a less-than-full start is wanted.")]
    public float initialPotionVolume = 0.044f;

    [Header("Inertia (spring-damper tilt -- the liquid's steady bulk lean, separate from the ring wave detail below)")]
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

    [Header("Wave Ring (2026-08-12 rework #3: replaces the independent-wavelet impulse pool with a CLOSED RING of spring-coupled mass nodes around the rim -- a discretized 1D wave equation, not a sum of separately-evaluated formulas. Neighboring nodes physically pull on each other, so a disturbance at one point propagates around the ring and the whole surface behaves as ONE coupled body -- \"液体を粒子の集合ではなく一つの液体として動かす\" -- with naturally rounded crests (an emergent property of spring coupling, not a hand-shaped curve) instead of a flat plane with an applied waveform.")]
    [Range(6, 32)] public int ringNodeCount = 16;
    [Tooltip("How strongly each node is pulled back toward the flat (tilt-only) baseline -- higher = stiffer/faster-settling liquid.")]
    public float nodeSpringStrength = 40f;
    [Tooltip("How strongly each node is pulled toward its two neighbors' average height -- this IS the cohesion/coupling that makes the ring move as one connected body instead of independent points. Higher = a disturbance propagates faster/sharper around the ring.")]
    public float nodeCoupleStrength = 260f;
    [Tooltip("Per-second velocity damping on each node -- higher = settles faster, less sloshing back and forth (viscosity).")]
    public float nodeDamping = 3.2f;
    [Tooltip("How strongly a sudden shift in tilt (tiltVelocity -- itself already derived from world gravity + pot/character pose + acceleration) excites ring nodes on the side it's swinging toward.")]
    public float nodeForcingGain = 3.5f;
    [Tooltip("Hard cap on any single node's height deviation (m), and the normalization reference for the mesh's crest vertex-color signal read by the shader.")]
    public float maxNodeAmplitude = 0.07f;
    [Tooltip("Radial falloff exponent for how much the rim's wave height carries toward the pot's center -- 1 = linear, higher = flatter near the center with the rise concentrated near the rim (closer to how a real sloshing tank's surface behaves).")]
    public float waveRadialFalloff = 1.5f;

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
    [Tooltip("Translucent material (Custom/PotionLiquidOverflow) for the overflow stream/droplets -- deliberately different from the pool's opaque liquidMaterial, see PotionOverflowStream's own header comment.")]
    public Material overflowMaterial;
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

    // -- wave ring state -- closed loop of spring-coupled mass nodes, indices around the rim
    // starting at local +X and going counter-clockwise (matching the mesh's own theta convention).
    float[] nodeHeight;
    float[] nodeVelocity;

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
        nodeHeight = new float[Mathf.Max(3, ringNodeCount)];
        nodeVelocity = new float[Mathf.Max(3, ringNodeCount)];
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
        if (overflowMaterial != null) overflowStream.overflowMaterial = overflowMaterial;
        if (liquidMaterial != null) overflowStream.puddleMaterial = liquidMaterial;
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

        // Defensive re-init: recompiling scripts while already in Play mode can null out plain
        // (non-[Serializable]) private arrays without Awake() running again -- see WORKLOG.md. Real
        // gameplay never hits this (Awake() always runs before the first Step()), but this guard
        // makes Step() safe regardless.
        if (nodeHeight == null || nodeHeight.Length != Mathf.Max(3, ringNodeCount))
        {
            nodeHeight = new float[Mathf.Max(3, ringNodeCount)];
            nodeVelocity = new float[Mathf.Max(3, ringNodeCount)];
        }

        simTime += dt;
        UpdateKinematics(dt);
        UpdateInertiaTarget();
        StepSpringDamper(dt);
        StepRingNodes(dt);

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

    // Advances the closed ring of spring-coupled mass nodes by one discretized-wave-equation step:
    // each node is pulled (a) back toward the flat baseline (nodeSpringStrength), (b) toward its two
    // neighbors' average height (nodeCoupleStrength -- the actual cohesion that makes this ONE
    // connected body instead of independent points), (c) by external forcing proportional to how
    // aligned the node's angular position is with the CURRENT direction the effective-gravity tilt is
    // swinging (tiltVelocity) -- a sudden stop/turn shows up as forcing concentrated on one side of
    // the ring, which then propagates around it through the coupling term, exactly like a real slosh.
    // Semi-implicit (symplectic) Euler: velocities are updated from a single snapshot of this step's
    // starting heights (avoiding order-dependent bias across nodes), then heights are integrated from
    // the new velocities -- numerically stable for the stiffness values here at normal frame dt.
    void StepRingNodes(float dt)
    {
        int n = nodeHeight.Length;
        float twoPi = Mathf.PI * 2f;

        for (int i = 0; i < n; i++)
        {
            float angle = i * twoPi / n;
            float forcing = (tiltVelocity.x * Mathf.Cos(angle) + tiltVelocity.y * Mathf.Sin(angle)) * nodeForcingGain;

            int prev = (i - 1 + n) % n;
            int next = (i + 1) % n;
            float coupling = nodeCoupleStrength * (nodeHeight[prev] + nodeHeight[next] - 2f * nodeHeight[i]);
            float restoring = -nodeHeight[i] * nodeSpringStrength;

            float accel = restoring + coupling + forcing;
            nodeVelocity[i] += accel * dt;
            nodeVelocity[i] *= Mathf.Clamp01(1f - nodeDamping * dt);
        }
        for (int i = 0; i < n; i++)
            nodeHeight[i] = Mathf.Clamp(nodeHeight[i] + nodeVelocity[i] * dt, -maxNodeAmplitude, maxNodeAmplitude);
    }

    // Smooth (smoothstep-blended) interpolation between the two ring nodes bounding this angle --
    // C1-continuous so the surface reads as one rounded body between control points rather than
    // faceted linear segments.
    float RingHeightAt(float angleRad)
    {
        int n = nodeHeight.Length;
        float t = angleRad / (Mathf.PI * 2f) * n;
        t = ((t % n) + n) % n;
        int i0 = (int)t;
        int i1 = (i0 + 1) % n;
        float frac = t - i0;
        float blend = frac * frac * (3f - 2f * frac);
        return Mathf.Lerp(nodeHeight[i0], nodeHeight[i1], blend);
    }

    // waveOnly is the ring-only contribution (excludes steady tilt/ambient), used to paint the mesh's
    // per-vertex crest/trough color so the shader can add a highlight right at wave peaks.
    float SurfaceHeightAt(float x, float z, out float waveOnly)
    {
        float tilt = tiltVector.x * x + tiltVector.y * z;

        float r = Mathf.Sqrt(x * x + z * z);
        float radiusFrac = rimRadiusLocal > 0.0001f ? Mathf.Clamp01(r / rimRadiusLocal) : 0f;
        float angle = Mathf.Atan2(z, x);
        waveOnly = RingHeightAt(angle) * Mathf.Pow(radiusFrac, waveRadialFalloff);

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
        float maxNodeAmpSafe = Mathf.Max(0.001f, maxNodeAmplitude);

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
                colorsBuffer[idx] = new Color(Mathf.Clamp01(0.5f + ringWaveScratch[ring] / (2f * maxNodeAmpSafe)), 0f, 0f, 1f);
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

                // Spawn slightly above the flattened rim line, toward where the actual (pre-flatten)
                // bulge crested, so the stream visibly grows out of the wave's own bulge instead of
                // popping in at a flat clamp line just below it -- part of tightening the perceptual
                // link between "wave rises" and "stream pours" (2026-08-12, "波打ちからこぼれのところ
                // がちゃんとリンクするかどうかがポイント").
                float bulgeLift = Mathf.Min(dominantExcess, 0.02f);
                Vector3 rimLocal = new Vector3(dominantDir.x * rimRadiusLocal, rimHeightLocal + bulgeLift, dominantDir.z * rimRadiusLocal);
                Vector3 worldPos = liquidTransform.TransformPoint(rimLocal);
                Vector3 outwardWorld = liquidTransform.TransformDirection(dominantDir).normalized;
                // World-gravity-based pour direction (spec section 7): follows actual effective
                // gravity, never a fixed local-down assumption, blended with a bit of outward push so
                // the stream visibly clears the rim edge before falling.
                Vector3 spillDir = (outwardWorld * 0.4f + gravityDirWorld * 0.9f).normalized;
                float flowRate = (totalOverflowVolume * dominantShare) / dt;
                // sourceKey = the rim segment index -- lets PotionOverflowStream recognize "this is
                // still the same overflow event" as the wave crest drifts a bit frame to frame,
                // instead of re-bucketing by raw world-position distance (which snapped to a brand
                // new stream slot on small jitter, reading as disconnected pops rather than one
                // continuous pour tracking the wave).
                if (overflowStream != null)
                    overflowStream.Feed(worldPos, spillDir, flowRate, dominantSeg);

                if (secondSeg >= 0)
                {
                    float bulgeLift2 = Mathf.Min(secondExcess, 0.02f);
                    Vector3 rimLocal2 = new Vector3(secondDir.x * rimRadiusLocal, rimHeightLocal + bulgeLift2, secondDir.z * rimRadiusLocal);
                    Vector3 worldPos2 = liquidTransform.TransformPoint(rimLocal2);
                    Vector3 outwardWorld2 = liquidTransform.TransformDirection(secondDir).normalized;
                    Vector3 spillDir2 = (outwardWorld2 * 0.4f + gravityDirWorld * 0.9f).normalized;
                    float flowRate2 = (totalOverflowVolume * (1f - dominantShare)) / dt;
                    if (overflowStream != null)
                        overflowStream.Feed(worldPos2, spillDir2, flowRate2, secondSeg);
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
