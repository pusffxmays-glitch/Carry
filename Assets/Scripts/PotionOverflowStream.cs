using UnityEngine;
using System.Collections.Generic;

// Overflow liquid: a chain of spring-coupled mass NODES (root pinned to the rim, body nodes, a tip
// node that accumulates mass and forms a droplet) -- not a procedurally-tapered static curve. This is
// the Overflow-side counterpart to PotionLiquid's ring wave system (see that file's header comment):
// both replace "evaluate a formula at each point" with "simulate a small number of physically coupled
// masses and reconstruct a surface/tube from their state," per the 2026-08-12 request to change the
// liquid's REPRESENTATION, not just its parameters ("液体を粒子の集合ではなく一つの液体として動かす").
//
// Each chain has exactly 4 nodes: [0] root (kinematically pinned to the rim contact point every frame
// while fed), [1]/[2] body (free-falling, spring-coupled to their neighbors), [3] tip (free-falling,
// accumulates mass over time and DETACHES into a real droplet -- reusing the same squash-stretch
// sphere droplet system as before -- once its mass or its segment's stretch crosses a threshold, i.e.
// physically motivated detachment rather than a scripted timer). The tube mesh is lofted directly
// through the 4 simulated node positions every frame, so its curve/sway is the actual chain physics,
// not a hand-authored sine wiggle. Per-node radius comes from that node's current mass (radius ~
// sqrt(mass)), so the root-bulge -> neck -> body -> tip-bulge silhouette from the spec is an emergent
// result of how mass is distributed and how much the chain has stretched, not a fixed taper curve.
//
// Droplets and ground Puddles are unchanged from the earlier procedural-stream version -- those two
// systems were never the "looks like a plane/line" complaint, only the flowing body between rim and
// droplet was.
public class PotionOverflowStream : MonoBehaviour
{
    [Header("Pool")]
    [Tooltip("Max concurrent flowing chains (one per distinct spill point).")]
    public int maxChains = 3;
    [Tooltip("If a new Feed() world position is within this distance of an existing active chain's root, it reuses that chain instead of starting a new one.")]
    public float mergeDistance = 0.05f;

    [Header("Chain mass / length")]
    [Tooltip("Converts overflow flow rate (volume/sec) into the chain's target total mass.")]
    public float massPerVolume = 55f;
    public float maxChainMass = 1.1f;
    [Tooltip("How fast total mass eases toward its target while actively fed -- lower = laggier/heavier (viscous) flow.")]
    public float growSpeed = 6f;
    [Tooltip("How fast mass drains back out once no longer being fed.")]
    public float retractSpeed = 3f;
    [Tooltip("Seconds without a Feed() call before a chain is considered 'no longer pouring' and starts retracting.")]
    public float feedTimeout = 0.12f;
    [Tooltip("Rest length each body segment eases toward at full mass -- how far the chain hangs down at maximum overflow.")]
    public float maxSegRestLength = 0.09f;

    [Header("Chain physics")]
    [Tooltip("Spring stiffness pulling each node's neighbors back toward the segment's rest length -- the actual cohesion holding the chain together as ONE body instead of independent masses.")]
    public float chainSpringStrength = 700f;
    public float chainDamping = 6f;
    [Tooltip("Lower = falls more slowly/heavily (more viscous). 1 = real gravity.")]
    public float chainGravityModifier = 0.55f;

    [Header("Radius / mass distribution")]
    [Tooltip("Radius of the pooled bulge sitting right at the rim (node 0), BEFORE the liquid narrows to go over the edge -- driven by overflow intensity directly, not by node mass, so even a just-starting chain immediately shows a visible 'liquid piling up' bulge (spec: リム上で液体が盛り上がる).")]
    public float bulgeRootRadius = 0.045f;
    [Tooltip("Width multiplier range for the root bulge, by overflow intensity (targetMass saturating maxChainMass).")]
    public float minWidthScale = 0.55f;
    public float maxWidthScale = 2.2f;
    [Tooltip("Baseline mass for each body node (1 and 2) -- gives the hanging body a consistent minimum thickness independent of how much has accumulated at the tip.")]
    public float bodyNodeMass = 0.09f;
    [Tooltip("Converts a node's mass into a visible radius: radius = radiusPerSqrtMass * sqrt(mass).")]
    public float radiusPerSqrtMass = 0.05f;
    [Range(3, 10)] public int tubeSides = 6;

    [Header("Tip / droplet detachment (physically motivated, not a timer)")]
    [Tooltip("The tip node accumulates mass while fed; once it reaches this, it detaches as a droplet (spec: 液滴が形成される).")]
    public float tipDetachMass = 0.55f;
    [Tooltip("If the tip segment stretches beyond restLength times this factor, it detaches even if under the mass threshold -- a fast/violent flow snaps the strand rather than waiting to fill up (spec: 液滴が切れる).")]
    public float tipDetachStretchFactor = 2.3f;
    [Tooltip("Fraction of the tip's mass that stays behind (redistributed into a fresh, smaller tip) after a detach, simulating an incomplete pinch-off instead of the strand fully emptying out.")]
    [Range(0f, 0.6f)] public float tipMassCarryover = 0.15f;

    [Header("Droplets")]
    public int maxDroplets = 10;
    [Tooltip("Base droplet radius -- scaled by the detaching tip's own mass (see DetachTip), so a big overflow drops big droplets and a thin drip drops tiny ones.")]
    public float dropletRadius = 0.014f;
    public float dropletLifetime = 1.4f;
    public float dropletInitialSpeed = 0.15f;
    [Tooltip("Lower = falls more slowly/heavily (more viscous). 1 = real gravity.")]
    public float dropletGravityModifier = 0.6f;
    [Tooltip("Squash-and-stretch: how much a droplet elongates along its own fall direction per m/s of speed. A plain uniformly-scaled sphere reads as a cheap 'particle ball' -- real falling liquid droplets are teardrop-elongated along their velocity, more so the faster they fall.")]
    public float dropletStretchPerSpeed = 0.9f;
    [Tooltip("Cap on the elongation multiplier so a fast-falling droplet doesn't stretch into a thread.")]
    public float dropletMaxStretch = 2.4f;
    [Tooltip("Layers a falling droplet can land on to leave a ground puddle.")]
    public LayerMask groundLayerMask = ~0;

    [Header("Ground Puddles (spilled liquid stays on the ground instead of just vanishing)")]
    public int maxPuddles = 14;
    [Tooltip("A droplet landing within this distance of an existing puddle grows that puddle instead of starting a new overlapping one.")]
    public float puddleMergeDistance = 0.09f;
    public float puddleBaseRadius = 0.03f;
    public float puddleMaxRadiusScale = 2.2f;
    [Tooltip("How much more a puddle grows each time an additional droplet lands on it -- repeated drips build up a visibly bigger stain over time.")]
    public float puddleGrowPerHit = 0.012f;
    public float puddleMaxRadius = 0.16f;
    public float puddleSpreadSpeed = 7f;
    [Tooltip("How long a puddle remains before shrinking away (seconds). Kept long/generous since the whole point is that it visibly stays, not that it's permanent.")]
    public float puddleLifetime = 25f;

    [Header("Materials")]
    [Tooltip("Translucent material (Custom/PotionLiquidOverflow) for the chain/droplet meshes -- deliberately different from the pool's opaque material, since these are thin open-air shapes where translucency reads well and safely (nothing behind them that shouldn't show through).")]
    public Material overflowMaterial;
    [Tooltip("Opaque material for ground puddles (same as PotionLiquid's pool material) -- puddles need to read clearly against the ground, same reasoning as the pool itself.")]
    public Material puddleMaterial;

    const int NodeCount = 4; // 0=root(pinned), 1,2=body, 3=tip(detachable)

    class Chain
    {
        public bool active;
        public int sourceKey = -1;
        public Vector3[] nodePos = new Vector3[NodeCount];
        public Vector3[] nodeVel = new Vector3[NodeCount];
        public float tipMass;
        public float totalMassTarget;
        public float segRestLength;   // shared rest length per segment, eases with intensity
        public Vector3 dirWorld = Vector3.down; // world-gravity-based pour direction, for the root bulge's outward lean
        public float timeSinceFed = 999f;
        public bool wasFed;
        public bool primed; // false until the first Feed() places all nodes at the root
        public GameObject go;
        public MeshRenderer mr;
        public Mesh mesh;
        public Vector3[] verts;
    }

    class Droplet
    {
        public bool active;
        public Transform t;
        public MeshRenderer mr;
        public Vector3 velocity;
        public float life;
        public float sizeScale = 1f;
    }

    class Puddle
    {
        public bool active;
        public Transform t;
        public MeshRenderer mr;
        public float currentRadius;
        public float targetRadius;
        public float life;
    }

    Chain[] chains;
    Droplet[] droplets;
    Puddle[] puddles;
    Mesh sharedDiscMesh;
    int[] cachedTubeTriangles;
    bool built;

    void Awake() { EnsureBuilt(); }

    // PotionLiquid.Awake() calls this explicitly (after assigning overflowMaterial/puddleMaterial)
    // rather than relying on this component's own Awake() ordering: AddComponent<T>() invokes T's
    // Awake() synchronously, before the calling code gets a chance to set fields on the newly-created
    // component -- so the first EnsureBuilt() call (from this component's own Awake()) always runs
    // with both material fields still null. rebuildMaterialsOnly re-applies the real materials to
    // every already-built renderer once PotionLiquid has actually set them.
    public void EnsureBuilt(bool rebuildMaterialsOnly = false)
    {
        if (built && !rebuildMaterialsOnly) return;

        if (!built)
        {
            built = true;
            cachedTubeTriangles = BuildTubeTriangles();
            sharedDiscMesh = BuildDiscMesh(16);

            chains = new Chain[Mathf.Max(1, maxChains)];
            for (int i = 0; i < chains.Length; i++) chains[i] = CreateChain(i);

            droplets = new Droplet[Mathf.Max(1, maxDroplets)];
            for (int i = 0; i < droplets.Length; i++) droplets[i] = CreateDroplet(i);

            puddles = new Puddle[Mathf.Max(1, maxPuddles)];
            for (int i = 0; i < puddles.Length; i++) puddles[i] = CreatePuddle(i);
            return;
        }

        if (overflowMaterial != null)
        {
            for (int i = 0; i < chains.Length; i++) chains[i].mr.sharedMaterial = overflowMaterial;
            for (int i = 0; i < droplets.Length; i++) droplets[i].mr.sharedMaterial = overflowMaterial;
        }
        if (puddleMaterial != null)
            for (int i = 0; i < puddles.Length; i++) puddles[i].mr.sharedMaterial = puddleMaterial;
    }

    Chain CreateChain(int index)
    {
        var go = new GameObject("OverflowChain_" + index);
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (overflowMaterial != null) mr.sharedMaterial = overflowMaterial;
        var mesh = new Mesh { name = "OverflowChainMesh_" + index };
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;
        go.SetActive(false);
        int vertCount = NodeCount * tubeSides + 2;
        return new Chain { go = go, mr = mr, mesh = mesh, verts = new Vector3[vertCount] };
    }

    Droplet CreateDroplet(int index)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Droplet_" + index;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * dropletRadius * 2f;
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (overflowMaterial != null) mr.sharedMaterial = overflowMaterial;
        go.SetActive(false);
        return new Droplet { t = go.transform, mr = mr };
    }

    Puddle CreatePuddle(int index)
    {
        var go = new GameObject("Puddle_" + index);
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = sharedDiscMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (puddleMaterial != null) mr.sharedMaterial = puddleMaterial;
        go.SetActive(false);
        return new Puddle { t = go.transform, mr = mr };
    }

    // Flat radial fan disc in the LOCAL XZ plane (all vertices at y=0, radius=1) -- shared by every
    // Puddle GameObject, which just non-uniformly scales it in X/Z to size itself.
    static Mesh BuildDiscMesh(int segments)
    {
        var verts = new Vector3[segments + 1];
        var normals = new Vector3[segments + 1];
        verts[segments] = Vector3.zero;
        normals[segments] = Vector3.up;
        for (int i = 0; i < segments; i++)
        {
            float ang = i / (float)segments * Mathf.PI * 2f;
            verts[i] = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            normals[i] = Vector3.up;
        }
        var tris = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            tris[i * 3 + 0] = segments; tris[i * 3 + 1] = i; tris[i * 3 + 2] = next;
        }
        var m = new Mesh { name = "PuddleDisc" };
        m.vertices = verts; m.normals = normals; m.triangles = tris;
        m.RecalculateBounds();
        return m;
    }

    int[] BuildTubeTriangles()
    {
        var tris = new List<int>();
        for (int ring = 0; ring < NodeCount - 1; ring++)
        {
            for (int side = 0; side < tubeSides; side++)
            {
                int sideNext = (side + 1) % tubeSides;
                int a0 = ring * tubeSides + side;
                int a1 = ring * tubeSides + sideNext;
                int b0 = (ring + 1) * tubeSides + side;
                int b1 = (ring + 1) * tubeSides + sideNext;
                tris.Add(a0); tris.Add(b0); tris.Add(b1);
                tris.Add(a0); tris.Add(b1); tris.Add(a1);
            }
        }
        int rootCapIndex = NodeCount * tubeSides;
        int tipCapIndex = rootCapIndex + 1;
        int lastRing = (NodeCount - 1) * tubeSides;
        for (int side = 0; side < tubeSides; side++)
        {
            int sideNext = (side + 1) % tubeSides;
            tris.Add(rootCapIndex); tris.Add(0 * tubeSides + sideNext); tris.Add(0 * tubeSides + side);
            tris.Add(tipCapIndex); tris.Add(lastRing + side); tris.Add(lastRing + sideNext);
        }
        return tris.ToArray();
    }

    // Called once per frame, from PotionLiquid's dominant (and, for a wide crest, second) overflow
    // point, while liquid is actually spilling. worldRootPos is the rim contact point; dirWorld is
    // the world-gravity-based pour direction; flowRate is spilled volume per second. sourceKey
    // identifies WHICH rim segment this came from (pass -1 if unknown) -- used to keep pouring from
    // the SAME chain as the wave crest drifts a little, rather than treating small drift as a new
    // disconnected event.
    public void Feed(Vector3 worldRootPos, Vector3 dirWorld, float flowRate, int sourceKey = -1)
    {
        if (flowRate <= 0f) return;
        EnsureBuilt();

        Chain c = FindOrAllocate(worldRootPos, sourceKey);
        if (c == null) return;

        bool isSameSpot = c.primed && Vector3.Distance(c.nodePos[0], worldRootPos) <= mergeDistance * 2f;
        if (!c.active || !isSameSpot)
        {
            c.active = true;
            c.go.SetActive(true);
            c.primed = false; // Step() will snap every node to the root on the next tick
            c.totalMassTarget = 0f;
            c.tipMass = 0f;
            c.segRestLength = 0f;
            c.wasFed = false;
        }
        c.sourceKey = sourceKey;
        c.nodePos[0] = worldRootPos;
        c.dirWorld = dirWorld.sqrMagnitude > 1e-6f ? dirWorld.normalized : Vector3.down;
        c.totalMassTarget = Mathf.Clamp(flowRate * massPerVolume, 0f, maxChainMass);
        c.timeSinceFed = 0f;
    }

    Chain FindOrAllocate(Vector3 pos, int sourceKey)
    {
        if (sourceKey >= 0)
        {
            int bestKeyIdx = -1;
            int bestKeyDist = int.MaxValue;
            for (int i = 0; i < chains.Length; i++)
            {
                if (!chains[i].active || chains[i].sourceKey < 0) continue;
                int d = Mathf.Abs(chains[i].sourceKey - sourceKey);
                if (d <= 2 && d < bestKeyDist) { bestKeyDist = d; bestKeyIdx = i; }
            }
            if (bestKeyIdx >= 0) return chains[bestKeyIdx];
        }

        int best = -1;
        float bestDist = mergeDistance;
        for (int i = 0; i < chains.Length; i++)
        {
            if (!chains[i].active) continue;
            float d = Vector3.Distance(chains[i].nodePos[0], pos);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        if (best >= 0) return chains[best];

        for (int i = 0; i < chains.Length; i++)
            if (!chains[i].active) return chains[i];

        int oldest = 0;
        float oldestTimeSinceFed = float.MinValue;
        for (int i = 0; i < chains.Length; i++)
            if (chains[i].timeSinceFed > oldestTimeSinceFed) { oldestTimeSinceFed = chains[i].timeSinceFed; oldest = i; }
        return chains[oldest];
    }

    void Update()
    {
        Step(Time.deltaTime);
    }

    // Split out from Update() so it can be driven with an explicit dt -- same reasoning as
    // PotionLiquid.Step(): deterministic and testable via manual Step(dt) calls, no dependency on the
    // engine's actual clock (feedTimeout uses each chain's own timeSinceFed counter, accumulated by
    // dt and reset to 0 in Feed()).
    public void Step(float dt)
    {
        if (!built) return;
        if (dt <= 0f) return;

        for (int i = 0; i < chains.Length; i++) StepChain(chains[i], dt);
        StepDropletsAndPuddles(dt);
    }

    void StepChain(Chain c, float dt)
    {
        if (!c.active) return;

        c.timeSinceFed += dt;
        bool beingFed = c.timeSinceFed <= feedTimeout;

        if (!c.primed)
        {
            // First tick after (re)activating -- collapse every node onto the root so growth always
            // starts from a single point, never a stale shape inherited from a previous, unrelated
            // spill (the same "no teleporting" guarantee the old procedural stream had).
            for (int i = 1; i < NodeCount; i++) { c.nodePos[i] = c.nodePos[0]; c.nodeVel[i] = Vector3.zero; }
            c.primed = true;
        }

        float massTarget = beingFed ? c.totalMassTarget : 0f;
        float rate = beingFed ? growSpeed : retractSpeed;
        float restLenTarget = Mathf.Lerp(0f, maxSegRestLength, Mathf.Clamp01(massTarget / Mathf.Max(0.0001f, maxChainMass)));
        c.segRestLength = Mathf.Lerp(c.segRestLength, restLenTarget, 1f - Mathf.Exp(-rate * dt));

        if (beingFed)
            c.tipMass += (massTarget / Mathf.Max(0.0001f, maxChainMass)) * bodyNodeMass * 1.4f * dt * 4f;
        else
            c.tipMass = Mathf.Max(0f, c.tipMass - retractSpeed * bodyNodeMass * dt);

        // -- physics: nodes 1,2,3 are free, spring-coupled to their neighbors around segRestLength.
        // node 0 stays pinned to wherever Feed() last placed it (or holds position while retracting).
        Vector3[] accel = new Vector3[NodeCount];
        for (int i = 1; i < NodeCount; i++)
        {
            Vector3 force = Physics.gravity * chainGravityModifier;

            Vector3 toPrev = c.nodePos[i - 1] - c.nodePos[i];
            float distPrev = toPrev.magnitude;
            if (distPrev > 1e-5f)
                force += (toPrev / distPrev) * (distPrev - c.segRestLength) * chainSpringStrength;

            if (i < NodeCount - 1)
            {
                Vector3 toNext = c.nodePos[i + 1] - c.nodePos[i];
                float distNext = toNext.magnitude;
                if (distNext > 1e-5f)
                    force += (toNext / distNext) * (distNext - c.segRestLength) * chainSpringStrength;
            }
            accel[i] = force;
        }
        for (int i = 1; i < NodeCount; i++)
        {
            c.nodeVel[i] += accel[i] * dt;
            c.nodeVel[i] *= Mathf.Clamp01(1f - chainDamping * dt);
        }
        for (int i = 1; i < NodeCount; i++)
            c.nodePos[i] += c.nodeVel[i] * dt;

        // -- tip detachment: physically motivated (mass filled up, or the strand got stretched past
        // its limit), not a scripted timer.
        float tipSegStretch = c.segRestLength > 0.0001f
            ? Vector3.Distance(c.nodePos[NodeCount - 2], c.nodePos[NodeCount - 1]) / c.segRestLength
            : 0f;
        if (c.tipMass >= tipDetachMass || (c.segRestLength > 0.001f && tipSegStretch >= tipDetachStretchFactor))
            DetachTip(c);

        RebuildChainMesh(c);

        if (!beingFed && c.segRestLength < 0.001f && c.tipMass < 0.001f)
        {
            c.active = false;
            c.sourceKey = -1;
            c.go.SetActive(false);
        }
    }

    void DetachTip(Chain c)
    {
        if (c.tipMass < 0.02f) return; // not enough to bother forming a visible droplet
        float sizeScale = Mathf.Clamp(Mathf.Sqrt(c.tipMass / Mathf.Max(0.02f, bodyNodeMass)), 0.5f, 2.2f);
        Vector3 fallDir = (c.nodePos[NodeCount - 1] - c.nodePos[NodeCount - 2]);
        Vector3 dir = fallDir.sqrMagnitude > 1e-6f ? fallDir.normalized : Vector3.down;
        SpawnDroplet(c.nodePos[NodeCount - 1], c.nodeVel[NodeCount - 1] + dir * dropletInitialSpeed, sizeScale);

        // Incomplete pinch-off: a little mass stays behind so the strand doesn't just vanish, and the
        // tip node is pulled back toward the previous node so the NEXT droplet has to visibly reform
        // rather than starting already stretched out.
        c.tipMass *= tipMassCarryover;
        c.nodePos[NodeCount - 1] = Vector3.Lerp(c.nodePos[NodeCount - 1], c.nodePos[NodeCount - 2], 0.6f);
        c.nodeVel[NodeCount - 1] *= 0.2f;
    }

    void RebuildChainMesh(Chain c)
    {
        bool anyMass = c.segRestLength > 0.0008f || c.tipMass > 0.001f;
        if (!anyMass)
        {
            if (c.mesh.vertexCount > 0) c.mesh.Clear();
            return;
        }

        float intensity01 = Mathf.Clamp01(c.totalMassTarget / Mathf.Max(0.0001f, maxChainMass));
        float widthScale = Mathf.Lerp(minWidthScale, maxWidthScale, intensity01);
        float bulgeR = bulgeRootRadius * widthScale;
        float bodyR = radiusPerSqrtMass * Mathf.Sqrt(bodyNodeMass) * widthScale;
        float tipR = radiusPerSqrtMass * Mathf.Sqrt(Mathf.Max(0.0001f, c.tipMass));

        float[] nodeRadius = { bulgeR, bodyR, bodyR, tipR };

        var verts = c.verts;
        int vi = 0;
        for (int ring = 0; ring < NodeCount; ring++)
        {
            Vector3 center = c.nodePos[ring];
            Vector3 fwd;
            if (ring < NodeCount - 1) fwd = c.nodePos[ring + 1] - c.nodePos[ring];
            else fwd = c.nodePos[ring] - c.nodePos[ring - 1];
            fwd = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.down;

            Vector3 perp1 = Vector3.Cross(fwd, Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 perp2 = Vector3.Cross(fwd, perp1).normalized;
            float radius = nodeRadius[ring];

            for (int side = 0; side < tubeSides; side++)
            {
                float ang = side / (float)tubeSides * Mathf.PI * 2f;
                Vector3 offset = (perp1 * Mathf.Cos(ang) + perp2 * Mathf.Sin(ang)) * radius;
                verts[vi++] = center + offset;
            }
        }
        verts[vi++] = c.nodePos[0];                 // root cap center
        verts[vi++] = c.nodePos[NodeCount - 1];      // tip cap center

        c.mesh.vertices = verts;
        if (c.mesh.triangles.Length != cachedTubeTriangles.Length) c.mesh.triangles = cachedTubeTriangles;
        else c.mesh.SetTriangles(cachedTubeTriangles, 0);
        c.mesh.RecalculateNormals();
        c.mesh.RecalculateBounds();
    }

    void StepDropletsAndPuddles(float dt)
    {
        for (int i = 0; i < droplets.Length; i++)
        {
            var d = droplets[i];
            if (!d.active) continue;
            d.velocity += Physics.gravity * dropletGravityModifier * dt;

            float travelThisStep = d.velocity.magnitude * dt;
            float lookahead = Mathf.Max(0.04f, travelThisStep * 1.5f);
            if (Physics.Raycast(d.t.position, Vector3.down, out RaycastHit hit, lookahead, groundLayerMask, QueryTriggerInteraction.Ignore))
            {
                SpawnOrGrowPuddle(hit.point, hit.normal, Mathf.Clamp01((d.sizeScale - 0.5f) / 1.7f));
                d.active = false;
                d.t.gameObject.SetActive(false);
                continue;
            }

            d.t.position += d.velocity * dt;
            d.life -= dt;
            float lifeT = Mathf.Clamp01(d.life / dropletLifetime);
            float baseDiameter = dropletRadius * d.sizeScale * 2f * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, lifeT * 3f));

            float speed = d.velocity.magnitude;
            float stretch = Mathf.Clamp(1f + speed * dropletStretchPerSpeed, 1f, dropletMaxStretch);
            float widthFactor = 1f / Mathf.Sqrt(stretch);
            if (speed > 0.001f) d.t.rotation = Quaternion.FromToRotation(Vector3.up, d.velocity / speed);
            d.t.localScale = new Vector3(baseDiameter * widthFactor, baseDiameter * stretch, baseDiameter * widthFactor);
            if (d.life <= 0f)
            {
                d.active = false;
                d.t.gameObject.SetActive(false);
            }
        }

        for (int i = 0; i < puddles.Length; i++)
        {
            var p = puddles[i];
            if (!p.active) continue;
            p.life -= dt;
            p.currentRadius = Mathf.Lerp(p.currentRadius, p.targetRadius, 1f - Mathf.Exp(-puddleSpreadSpeed * dt));
            float endShrink = p.life < 1.5f ? Mathf.Clamp01(p.life / 1.5f) : 1f;
            float visR = p.currentRadius * endShrink;
            p.t.localScale = new Vector3(visR, 1f, visR);
            if (p.life <= 0f)
            {
                p.active = false;
                p.t.gameObject.SetActive(false);
            }
        }
    }

    void SpawnOrGrowPuddle(Vector3 pos, Vector3 normal, float sizeT01)
    {
        Puddle p = null;
        float bestDist = puddleMergeDistance;
        for (int i = 0; i < puddles.Length; i++)
        {
            if (!puddles[i].active) continue;
            float d = Vector3.Distance(puddles[i].t.position, pos);
            if (d < bestDist) { bestDist = d; p = puddles[i]; }
        }

        bool isNew = p == null;
        if (isNew)
        {
            for (int i = 0; i < puddles.Length; i++)
                if (!puddles[i].active) { p = puddles[i]; break; }
            if (p == null)
            {
                float minLife = float.MaxValue;
                for (int i = 0; i < puddles.Length; i++)
                    if (puddles[i].life < minLife) { minLife = puddles[i].life; p = puddles[i]; }
            }
        }
        if (p == null) return;

        p.active = true;
        p.t.gameObject.SetActive(true);
        p.t.rotation = Quaternion.FromToRotation(Vector3.up, normal.sqrMagnitude > 1e-6f ? normal : Vector3.up);
        if (isNew)
        {
            p.t.position = pos;
            p.currentRadius = 0f;
            p.targetRadius = puddleBaseRadius * Mathf.Lerp(1f, puddleMaxRadiusScale, sizeT01);
        }
        else
        {
            p.targetRadius = Mathf.Min(puddleMaxRadius, p.targetRadius + puddleGrowPerHit);
        }
        p.life = puddleLifetime;
    }

    void SpawnDroplet(Vector3 pos, Vector3 velocity, float sizeScale)
    {
        for (int i = 0; i < droplets.Length; i++)
        {
            var d = droplets[i];
            if (d.active) continue;
            d.active = true;
            d.sizeScale = sizeScale;
            d.t.gameObject.SetActive(true);
            d.t.position = pos;
            d.t.localScale = Vector3.one * dropletRadius * sizeScale * 2f;
            d.velocity = velocity;
            d.life = dropletLifetime;
            return;
        }
        // Pool exhausted -- silently drop; this is a transient polish visual, not something the
        // volume/overflow accounting depends on.
    }
}
