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
[DefaultExecutionOrder(100)]
public class PotionLiquid : MonoBehaviour
{
    [Header("Volume")]
    [Tooltip("Liquid amount at which the pot is considered full. Units match the pot's own measured interior volume (local-space cubic units) -- this pot's actual measured capacity is ~0.044, so values much larger than that get clamped down to it at runtime.")]
    public float maxPotionVolume = 0.044f;
    [Tooltip("Starting liquid amount (same units as maxPotionVolume). 2026-08-12 per request (\"液体の初期量は壺満タンで\"): defaults to full -- set below maxPotionVolume in the Inspector if a less-than-full start is wanted.")]
    public float initialPotionVolume = 0.044f;

    [Header("Inertia (spring-damper tilt)")]
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

    [Header("Waves (2026-08-12: more reactive but slower/heavier motion = more viscous feel)")]
    [Tooltip("Amplitude (meters) of the always-present ambient micro-ripple.")]
    public float smallWaveAmplitude = 0.004f;
    [Tooltip("Speed of the ambient micro-ripple -- lower reads as thicker/heavier liquid.")]
    public float smallWaveSpeed = 1.3f;
    [Tooltip("How much a sudden tilt-velocity spike (accel/turn/landing) feeds into big sloshing waves -- raised so waves/spills trigger more easily.")]
    public float largeWaveGain = 0.24f;
    [Tooltip("How fast big-wave energy decays per second once nothing new excites it -- lower means waves linger longer, reading as heavier/more viscous.")]
    public float waveDampingPerSecond = 0.85f;
    [Tooltip("Propagation speed of the big slosh/ripple wave pattern -- lower reads as thicker/heavier liquid.")]
    public float waveSpeed = 1.1f;
    [Tooltip("Spatial frequency of the directional slosh wave -- lower = broader, thicker-looking swells instead of choppy ripples.")]
    public float sloshFrequency = 3.2f;
    [Tooltip("Spatial frequency of the radial impact ripple -- lower = broader, thicker-looking ripples.")]
    public float rippleFrequency = 5f;

    [Header("Overflow (2026-08-12: raised so spilling reacts more readily)")]
    [Tooltip("How readily liquid actually drains once the surface is over the rim (higher = spills faster for the same excess).")]
    public float overflowRate = 6f;
    [Tooltip("Spill speed (m/s of surface rise, roughly) above which splash VFX kicks in instead of just drip/stream. Also drives PotionOverflowVFX's own splash threshold.")]
    public float overflowSplashSpeed = 0.45f;

    [Header("Mesh")]
    public int radialSegments = 32;
    public int capRings = 3;
    [Tooltip("Vertical rings used for the static (non-wavy) side wall between the pot's floor and the liquid surface.")]
    public int sideWallRings = 4;
    public Material liquidMaterial;

    [Header("Overflow VFX materials (passed to the auto-created PotionOverflowVFX child)")]
    public Material overflowDripMaterial;
    public Material overflowSplashMaterial;

    [Header("Refs (auto-found if left empty)")]
    public Transform potMeshSource; // the GameObject whose MeshFilter defines the pot's interior profile
    public PotionOverflowVFX overflowVfx;

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

    // -- inertia state --
    Vector2 tiltVector;      // (x-slope, z-slope), local space, rise-per-run
    Vector2 tiltVelocity;
    float impactEnergy;
    float impactPhase;

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

    void Awake()
    {
        if (potMeshSource == null) potMeshSource = transform;
        SampleInteriorProfile();
        maxPotionVolume = Mathf.Min(maxPotionVolume, cumulativeVolume[cumulativeVolume.Length - 1]);
        PotionVolume = Mathf.Clamp(initialPotionVolume, 0f, maxPotionVolume);
        SurfaceHeightLocal = HeightForVolume(PotionVolume);
        BuildLiquidMeshObject();

        if (overflowVfx == null) overflowVfx = GetComponentInChildren<PotionOverflowVFX>();
        if (overflowVfx == null)
        {
            var vfxGo = new GameObject("PotionOverflowVFX");
            vfxGo.transform.SetParent(transform, false);
            overflowVfx = vfxGo.AddComponent<PotionOverflowVFX>();
        }
        overflowVfx.splashSpeedThreshold = overflowSplashSpeed;
        if (overflowDripMaterial != null) overflowVfx.dripMaterial = overflowDripMaterial;
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

        // maxPotionVolume defaults to the pot's actual measured interior volume the first time this
        // runs with the inspector value left at its default-ish 1 -- but we don't silently override
        // an intentionally-tuned Inspector value, so only rescale if it looks untouched.
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
    // frame timing.
    public void Step(float dt)
    {
        if (dt <= 0f || !kinematicsPrimed) { prevPos = transform.position; prevRot = transform.rotation; kinematicsPrimed = true; return; }

        UpdateKinematics(dt);
        UpdateInertiaTarget();
        StepSpringDamper(dt);
        StepWaveEnergy(dt);

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
    Vector2 tiltTarget;

    void StepSpringDamper(float dt)
    {
        Vector2 diff = tiltTarget - tiltVector;
        tiltVelocity += diff * inertiaSpringStrength * dt;
        tiltVelocity *= Mathf.Clamp01(1f - inertiaDamping * dt);
        tiltVector += tiltVelocity * dt;

        // Sudden changes in tilt velocity (accel spikes, hard turns, landings) pump energy into the
        // big-wave system; steady holding of a tilt does not keep re-exciting it.
        float excitation = tiltVelocity.magnitude + angularVelocityLocal.magnitude * 0.3f + Mathf.Max(0f, acceleration.magnitude - 3f) * 0.15f;
        impactEnergy = Mathf.Min(impactEnergy + excitation * largeWaveGain * dt * 10f, 1.5f);
    }

    void StepWaveEnergy(float dt)
    {
        impactPhase += dt * waveSpeed;
        impactEnergy = Mathf.Max(0f, impactEnergy - waveDampingPerSecond * dt);
    }

    float SurfaceHeightAt(float x, float z)
    {
        float tilt = tiltVector.x * x + tiltVector.y * z;

        // 2026-08-12: lower spatial frequencies (sloshFrequency/rippleFrequency, tunable) + lower
        // waveSpeed = broader, slower-moving swells instead of choppy short ripples -- reads as a
        // thicker/heavier liquid ("もう少しとろみがある感じに") while impactEnergy's amplitude
        // (fed by largeWaveGain, raised for sensitivity) still responds readily to motion.
        float tiltDirAngle = Mathf.Atan2(tiltVector.y, tiltVector.x);
        float alongTiltAxis = x * Mathf.Cos(tiltDirAngle) + z * Mathf.Sin(tiltDirAngle);
        // Amplitude coefficients raised 2026-08-12 ("波打ちやこぼれはもう少し過敏に"), twice: the
        // first pass (0.028->0.05, 0.02->0.038) still couldn't reliably reach the rim in practice --
        // the ripple's exp(-r*falloff) term was measured to cut its amplitude by ~27% by the time it
        // reaches this pot's actual rim radius (~0.195 local units), so the real achievable combined
        // amplitude at the rim was only ~0.078/unit energy against a ~0.095 headroom gap at a
        // typical fill level -- technically possible only at near-max energy with perfect phase
        // alignment, which is why sudden-stop tests kept measuring zero spill even at
        // impactEnergy>1.4. Amplitudes raised further AND the falloff slowed so a strong impact
        // reliably clears the rim instead of needing a lucky phase alignment at the ceiling.
        float slosh = impactEnergy * Mathf.Sin(alongTiltAxis * sloshFrequency - impactPhase * waveSpeed * 1.4f) * 0.075f;

        float r = Mathf.Sqrt(x * x + z * z);
        float ripple = impactEnergy * Mathf.Sin(r * rippleFrequency - impactPhase * waveSpeed * 1.8f) * Mathf.Exp(-r * 0.8f) * 0.055f;

        float ambient = smallWaveAmplitude * 0.5f * (Mathf.Sin(x * 6f + Time.time * smallWaveSpeed) + Mathf.Sin(z * 4.3f - Time.time * smallWaveSpeed * 0.77f));

        return SurfaceHeightLocal + tilt + slosh + ripple + ambient;
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

    float pendingOverflowVolume;

    void DeformMeshAndHandleOverflow(float dt)
    {
        int capVertCount = (capRings + 1) * radialSegments + 1;
        int centerIndex = capVertCount - 1;

        float totalOverflowVolume = 0f;
        float wedgeAngle = Mathf.PI * 2f / radialSegments;

        float maxSurfaceSpillSpeed = 0f;

        // 2026-08-12 (bug report: "spilling from several unrelated points, not from the wave crest
        // -- looks like unrelated straight lines, no reality to it"): previously every overflowing
        // segment (up to `radialSegments`=32 of them) independently called NotifySpillPoint every
        // frame. Real liquid pours from roughly ONE place at a time -- wherever the surface is
        // currently highest above the rim -- not from a dozen scattered points simultaneously. Now
        // this loop only RECORDS which single segment has the most excess; the actual VFX call
        // happens once, after the loop, at that one dominant point, carrying the FULL accumulated
        // totalOverflowVolume (so the visual amount still reflects the whole spill, just
        // concentrated where the wave is actually cresting instead of scattered).
        int dominantSeg = -1;
        float dominantExcess = 0f;
        Vector3 dominantDir = Vector3.zero;

        // Cap rings (0 = innermost .. capRings = outer/rim ring)
        for (int seg = 0; seg < radialSegments; seg++)
        {
            float theta = seg * wedgeAngle;
            float dirX = Mathf.Cos(theta), dirZ = Mathf.Sin(theta);

            for (int ring = 0; ring <= capRings; ring++)
            {
                float t = (ring + 1) / (float)(capRings + 1); // >0, reaches 1 at the outer/rim ring
                // Outer ring sits exactly at the wall; interior rings scale toward the center.
                float nominalRadius = ring == capRings ? rimRadiusLocal : rimRadiusLocal * t;
                float x = dirX * nominalRadius;
                float z = dirZ * nominalRadius;
                float h = SurfaceHeightAt(x, z);

                if (ring == capRings)
                {
                    // Clamp to the wall's actual interior radius AT this deformed height so the mesh
                    // can never poke through the taper -- this is the hard "stay inside the pot" rule.
                    float wallR = RadiusAtHeight(Mathf.Min(h, rimHeightLocal));
                    x = dirX * wallR; z = dirZ * wallR;

                    if (h > rimHeightLocal)
                    {
                        float excess = h - rimHeightLocal;
                        float wedgeArea = 0.5f * rimRadiusLocal * rimRadiusLocal * wedgeAngle;
                        float wedgeVolume = excess * wedgeArea;
                        totalOverflowVolume += wedgeVolume;
                        // Clamped 2026-08-12: excess/dt is unbounded and a single unusually large
                        // dt (a stutter frame, or -- as found during testing -- many manual dt=0.02
                        // steps applied back-to-back to simulate a fast tilt ramp) could produce an
                        // absurdly large "speed", launching drip/splash particles fast enough to
                        // rocket into the sky instead of falling like liquid. A real single-frame
                        // spill speed has no business being much faster than this regardless.
                        float spillSpeed = Mathf.Min(excess / Mathf.Max(dt, 0.0001f), 2.5f);
                        maxSurfaceSpillSpeed = Mathf.Max(maxSurfaceSpillSpeed, spillSpeed);

                        if (excess > dominantExcess)
                        {
                            dominantExcess = excess;
                            dominantSeg = seg;
                            dominantDir = new Vector3(dirX, 0f, dirZ);
                        }
                    }

                    h = Mathf.Min(h, rimHeightLocal + 0.002f); // visually never rises meaningfully above the rim; the excess became Overflow instead
                }

                int idx = ring * radialSegments + seg;
                vertsBuffer[idx] = new Vector3(x, h, z);
            }
        }

        // center vertex: average of innermost ring for a smooth apex
        Vector3 centerAvg = Vector3.zero;
        for (int seg = 0; seg < radialSegments; seg++) centerAvg += vertsBuffer[0 * radialSegments + seg];
        centerAvg /= radialSegments;
        vertsBuffer[centerIndex] = new Vector3(0f, centerAvg.y, 0f);

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
            }
        }

        liquidMesh.vertices = vertsBuffer;
        if (liquidMesh.triangles.Length != cachedTriangles.Length) liquidMesh.triangles = cachedTriangles;
        else liquidMesh.SetTriangles(cachedTriangles, 0);
        liquidMesh.RecalculateNormals();
        liquidMesh.RecalculateBounds();

        if (totalOverflowVolume > 0f && dt > 0f)
        {
            float spill = Mathf.Min(totalOverflowVolume * overflowRate * dt, PotionVolume);
            PotionVolume = Mathf.Max(0f, PotionVolume - spill);

            if (overflowVfx != null && dominantSeg >= 0)
            {
                float spillSpeedForVfx = Mathf.Min(maxSurfaceSpillSpeed, 2.5f);
                Vector3 rimLocal = new Vector3(dominantDir.x * rimRadiusLocal, rimHeightLocal, dominantDir.z * rimRadiusLocal);
                Vector3 worldPos = liquidTransform.TransformPoint(rimLocal);
                Vector3 outwardWorld = liquidTransform.TransformDirection(dominantDir).normalized;
                Vector3 spillDir = (outwardWorld * 0.5f + Vector3.down * 0.85f).normalized;
                // Carries the FULL totalOverflowVolume (not just the dominant segment's own
                // wedge), so a wide-arc spill still looks proportionally bigger even though it's
                // now visually concentrated at the single highest point.
                overflowVfx.NotifySpillPoint(worldPos, spillDir, totalOverflowVolume, spillSpeedForVfx);
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
