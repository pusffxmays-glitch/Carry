using UnityEngine;
using System.Collections.Generic;

// Mesh-based flowing "liquid stream" for the potion's Overflow: a wide bulge pooling right at the rim,
// narrowing to a neck as it squeezes over the edge, a steadier body hanging under gravity, and a bulb
// at the tip where a droplet is forming -- then an actual droplet that detaches and falls under
// (reduced) gravity, eventually landing as a ground puddle. Added 2026-08-12 to replace the old
// single-particle-line drip representation, which was rejected outright ("細い緑色の線が壺から垂れて
// いるだけ...単なる細い直線Particleは禁止"); the four-stage bulge/neck/body/tip profile was added in
// a later pass specifically to match the "盛り上がる->乗り越える->伸びる->液滴になる" shape spec
// requested for Overflow, after a plain two-point taper still read as too thin/line-like.
//
// This is a real extruded tube mesh (hexagonal cross-section, tapered radius along its length),
// rebuilt every frame like PotionLiquid's own InsideLiquid mesh -- not a LineRenderer (whose
// auto-generated camera-facing normals don't play well with the liquid shader's lighting) and not a
// particle system (inherently a chain of dots/streaks, not a continuous body of liquid).
//
// Kept as a fully separate component/GameObject from PotionLiquid's InsideLiquid mesh (spec section
// 4: Inside/Overflow must be two independent systems) and from PotionOverflowVFX's splash burst
// (reserved for violent/fast spills only) -- this component owns only the "liquid flowing down from
// the rim, tapering, and forming a droplet" behavior. PotionLiquid calls Feed() once per frame, from
// its single dominant overflow point, while an overflow is actually happening; everything else
// (growth lag, retraction, droplet detach/fall/fade) runs here on its own Update().
public class PotionOverflowStream : MonoBehaviour
{
    [Header("Pool")]
    [Tooltip("Max concurrent flowing streams (one per distinct spill point).")]
    public int maxStreams = 3;
    [Tooltip("If a new Feed() world position is within this distance of an existing active stream's root, it reuses that stream instead of starting a new one.")]
    public float mergeDistance = 0.05f;

    [Header("Flow shape")]
    public float maxStreamLength = 0.22f;
    [Tooltip("Converts overflow flow rate (volume/sec) into the stream's target length.")]
    public float lengthPerVolume = 18f;
    [Tooltip("How fast the visible length eases toward its target while actively fed -- lower = laggier/heavier (viscous) flow.")]
    public float growSpeed = 7f;
    [Tooltip("How fast the stream retracts once no longer being fed.")]
    public float retractSpeed = 3.5f;
    [Tooltip("Seconds without a Feed() call before a stream is considered 'no longer pouring' and starts retracting / detaching its droplet.")]
    public float feedTimeout = 0.12f;
    [Header("Flow shape -- radius profile (2026-08-12 rework: root-bulge / neck / body / tip-bulge, matching the requested \"盛り上がる->乗り越える->伸びる->液滴になる\" silhouette instead of a simple two-point taper, and roughly doubled overall so it reads as a genuinely wide flow instead of \"細い線\")")]
    [Tooltip("Radius of the pooled bulge sitting right at the rim, BEFORE the liquid narrows to go over the edge -- this is driven by overflow intensity, not by how long the stream has grown, so even a just-starting stream immediately shows a visible 'liquid piling up' bulge (spec: リム上で液体が盛り上がる).")]
    public float bulgeRootRadius = 0.045f;
    [Tooltip("Where the liquid narrows as it squeezes over the rim edge, just past the bulge (spec: リムを乗り越える).")]
    public float neckRadius = 0.016f;
    [Tooltip("Radius of the hanging body between the neck and the tip bulge (spec: 重力方向へ伸びる).")]
    public float bodyRadius = 0.02f;
    [Tooltip("Bulge radius at the very tip where a droplet is forming before it detaches (spec: 先端が液滴になる).")]
    public float tipBulgeRadius = 0.03f;
    [Tooltip("Width multiplier at zero flow intensity (a bare-minimum drip should still read as a small but visible trickle, not collapse to nothing).")]
    public float minWidthScale = 0.55f;
    [Tooltip("Width multiplier at maximum flow intensity (targetLength saturating maxStreamLength) -- how much fatter a strong overflow gets compared to a weak drip.")]
    public float maxWidthScale = 2.2f;
    [Range(3, 10)] public int tubeSides = 6;
    [Range(2, 10)] public int lengthSegments = 6;
    [Tooltip("Small sideways sway amplitude (world units) so the stream reads as a hanging/falling body of liquid instead of a rigid straight rod.")]
    public float swayAmplitude = 0.01f;
    public float swaySpeed = 2.2f;

    [Header("Droplets")]
    public int maxDroplets = 10;
    public float dropletMinLengthToSpawn = 0.05f;
    [Tooltip("Base droplet radius -- scaled by the stream's own width intensity at the moment it detaches (see SpawnDroplet), so a big overflow drops big droplets and a thin drip drops tiny ones.")]
    public float dropletRadius = 0.014f;
    [Tooltip("Droplet radius multiplier range applied on top of dropletRadius, driven by the source stream's flow intensity at detach time.")]
    public float dropletMinSizeScale = 0.6f;
    public float dropletMaxSizeScale = 1.8f;
    public float dropletLifetime = 1.4f;
    public float dropletInitialSpeed = 0.15f;
    [Tooltip("Lower = falls more slowly/heavily (more viscous). 1 = real gravity.")]
    public float dropletGravityModifier = 0.6f;
    [Tooltip("Squash-and-stretch: how much a droplet elongates along its own fall direction per m/s of speed. A plain uniformly-scaled sphere reads as a cheap 'particle ball' (2026-08-12, \"落ちていく液体が粒の表現だと安っぽい\") -- real falling liquid droplets are teardrop-elongated along their velocity, more so the faster they fall.")]
    public float dropletStretchPerSpeed = 0.9f;
    [Tooltip("Cap on the elongation multiplier so a fast-falling droplet doesn't stretch into a thread.")]
    public float dropletMaxStretch = 2.4f;
    [Tooltip("Layers a falling droplet can land on to leave a ground puddle.")]
    public LayerMask groundLayerMask = ~0;

    [Header("Ground Puddles (2026-08-12: spilled liquid stays on the ground instead of just vanishing -- \"こぼれた分は地面に残るようにして\")")]
    public int maxPuddles = 14;
    [Tooltip("A droplet landing within this distance of an existing puddle grows that puddle instead of starting a new overlapping one.")]
    public float puddleMergeDistance = 0.09f;
    public float puddleBaseRadius = 0.03f;
    [Tooltip("Puddle radius multiplier range (like dropletMinSizeScale/MaxSizeScale) driven by the landing droplet's own size.")]
    public float puddleMaxRadiusScale = 2.2f;
    [Tooltip("How much more a puddle grows each time an additional droplet lands on it -- repeated drips build up a visibly bigger stain over time.")]
    public float puddleGrowPerHit = 0.012f;
    public float puddleMaxRadius = 0.16f;
    [Tooltip("How fast a puddle spreads out to its target radius after landing -- a real spill spreads over a fraction of a second, not instantly.")]
    public float puddleSpreadSpeed = 7f;
    [Tooltip("How long a puddle remains before shrinking away (seconds). Kept long/generous since the whole point is that it visibly stays, not that it's permanent.")]
    public float puddleLifetime = 25f;

    [Header("Materials")]
    [Tooltip("Translucent material for the stream/droplet meshes (Custom/PotionLiquidOverflow) -- deliberately separate from the pool's opaque material since these are thin open-air shapes where translucency reads well and safely (nothing behind them that shouldn't show through).")]
    public Material overflowMaterial;
    [Tooltip("Opaque material for ground puddles (same as PotionLiquid's pool material) -- puddles need to read clearly against the ground, same reasoning as the pool itself.")]
    public Material puddleMaterial;

    class Stream
    {
        public bool active;
        // Which rim segment (from PotionLiquid) this stream is currently pouring from, or -1 if fed
        // without a key. Matching by this identity (instead of only raw world-position distance)
        // keeps the same stream alive as the wave crest drifts a segment or two frame to frame,
        // rather than treating small drift as a brand new, disconnected overflow event.
        public int sourceKey = -1;
        public Vector3 rootWorld;
        public Vector3 dirWorld = Vector3.down;
        public float currentLength;
        public float targetLength;
        // Seconds since the last Feed() call -- reset to 0 there, accumulated by dt in Step().
        // Deliberately not an absolute Time.time stamp, so this stays deterministic and testable via
        // manual Step(dt) calls exactly like PotionLiquid.Step() (see that class's own doc comment).
        public float timeSinceFed = 999f;
        public bool wasFed;
        public bool dropletSpawnedForThisPour;
        public GameObject go;
        public MeshRenderer mr;
        public Mesh mesh;
        public Vector3[] verts;
        public float swayPhase;
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

    Stream[] streams;
    Droplet[] droplets;
    Puddle[] puddles;
    Mesh sharedDiscMesh;
    int[] cachedStreamTriangles;
    bool built;

    void Awake() { EnsureBuilt(); }

    // PotionLiquid.Awake() calls this explicitly (after assigning overflowMaterial/puddleMaterial)
    // rather than relying on this component's own Awake() ordering: AddComponent<T>() invokes T's
    // Awake() synchronously, before the calling code gets a chance to set fields on the newly-created
    // component -- so the first EnsureBuilt() call (from this component's own Awake()) always runs
    // with both material fields still null. rebuildMaterialsOnly re-applies the real materials to
    // every already-built renderer once PotionLiquid has actually set them (same pattern as
    // PotionOverflowVFX.EnsureBuilt()).
    public void EnsureBuilt(bool rebuildMaterialsOnly = false)
    {
        if (built && !rebuildMaterialsOnly) return;

        if (!built)
        {
            built = true;
            cachedStreamTriangles = BuildStreamTriangles();
            sharedDiscMesh = BuildDiscMesh(16);

            streams = new Stream[Mathf.Max(1, maxStreams)];
            for (int i = 0; i < streams.Length; i++) streams[i] = CreateStream(i);

            droplets = new Droplet[Mathf.Max(1, maxDroplets)];
            for (int i = 0; i < droplets.Length; i++) droplets[i] = CreateDroplet(i);

            puddles = new Puddle[Mathf.Max(1, maxPuddles)];
            for (int i = 0; i < puddles.Length; i++) puddles[i] = CreatePuddle(i);
            return;
        }

        if (overflowMaterial != null)
        {
            for (int i = 0; i < streams.Length; i++) streams[i].mr.sharedMaterial = overflowMaterial;
            for (int i = 0; i < droplets.Length; i++) droplets[i].mr.sharedMaterial = overflowMaterial;
        }
        if (puddleMaterial != null)
            for (int i = 0; i < puddles.Length; i++) puddles[i].mr.sharedMaterial = puddleMaterial;
    }

    // Flat radial fan disc in the LOCAL XZ plane (all vertices at y=0, radius=1) -- shared by every
    // Puddle GameObject, which just non-uniformly scales it in X/Z to size itself. Cheap: no per-
    // puddle geometry rebuilding, only a Transform.localScale write each frame.
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

    Stream CreateStream(int index)
    {
        var go = new GameObject("Stream_" + index);
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (overflowMaterial != null) mr.sharedMaterial = overflowMaterial;
        var mesh = new Mesh { name = "StreamMesh_" + index };
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;
        go.SetActive(false);
        int vertCount = (lengthSegments + 1) * tubeSides + 2;
        return new Stream { go = go, mr = mr, mesh = mesh, verts = new Vector3[vertCount] };
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

    int[] BuildStreamTriangles()
    {
        int rings = lengthSegments + 1;
        var tris = new List<int>();
        for (int ring = 0; ring < rings - 1; ring++)
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
        int rootCapIndex = rings * tubeSides;
        int tipCapIndex = rootCapIndex + 1;
        int lastRing = (rings - 1) * tubeSides;
        for (int side = 0; side < tubeSides; side++)
        {
            int sideNext = (side + 1) % tubeSides;
            // root cap faces back up toward the rim -- reversed winding vs the tip cap
            tris.Add(rootCapIndex); tris.Add(0 * tubeSides + sideNext); tris.Add(0 * tubeSides + side);
            tris.Add(tipCapIndex); tris.Add(lastRing + side); tris.Add(lastRing + sideNext);
        }
        return tris.ToArray();
    }

    // Called once per frame, from PotionLiquid's dominant (and, for a wide crest, second) overflow
    // point, while liquid is actually spilling. worldRootPos is the rim contact point; dirWorld is
    // the world-gravity-based pour direction; flowRate is spilled volume per second (units match
    // PotionLiquid.PotionVolume). sourceKey identifies WHICH rim segment this came from (pass -1 if
    // unknown) -- used to keep pouring from the SAME stream as the wave crest drifts a little, rather
    // than treating small drift as a new disconnected event (see FindOrAllocate).
    public void Feed(Vector3 worldRootPos, Vector3 dirWorld, float flowRate, int sourceKey = -1)
    {
        if (flowRate <= 0f) return;
        EnsureBuilt();

        Stream s = FindOrAllocate(worldRootPos, sourceKey);
        if (s == null) return;

        // Treat this as a fresh pour (reset growth to 0) unless the stream is genuinely still
        // tracking the SAME spot -- either because it was inactive, or because it's continuing via a
        // matching sourceKey/nearby position. Without this check, when the pool is exhausted and
        // FindOrAllocate has to forcibly repurpose an unrelated ACTIVE stream (multiple independent
        // wave impulses competing for "dominant" can make the highest point hop between genuinely
        // different rim locations faster than any small-drift tolerance can track), the hijacked
        // stream would otherwise visually "teleport" -- instantly snapping to the new position while
        // still showing its old length/mesh grown from a completely different spot on the rim.
        bool isSameSpot = Vector3.Distance(s.rootWorld, worldRootPos) <= mergeDistance * 2f;
        if (!s.active || !isSameSpot)
        {
            s.active = true;
            s.go.SetActive(true);
            s.currentLength = 0f;
            s.dropletSpawnedForThisPour = false;
            s.wasFed = false;
        }
        s.sourceKey = sourceKey;
        s.rootWorld = worldRootPos;
        s.dirWorld = dirWorld.sqrMagnitude > 1e-6f ? dirWorld.normalized : Vector3.down;
        s.targetLength = Mathf.Clamp(flowRate * lengthPerVolume, 0f, maxStreamLength);
        s.timeSinceFed = 0f;
    }

    Stream FindOrAllocate(Vector3 pos, int sourceKey)
    {
        // Prefer continuing the SAME logical overflow event: an active stream whose sourceKey is
        // this exact segment, or within a couple segments of it (the wave crest naturally drifts a
        // little frame to frame). This is checked BEFORE raw distance so a stream keeps growing
        // continuously from the wave instead of snapping to a new slot on small jitter -- which
        // previously read as the pour disconnectedly popping in and out rather than tracking the
        // wave's own rise and fall.
        if (sourceKey >= 0)
        {
            int bestKeyKey = -1;
            int bestKeyDist = int.MaxValue;
            for (int i = 0; i < streams.Length; i++)
            {
                if (!streams[i].active || streams[i].sourceKey < 0) continue;
                int d = Mathf.Abs(streams[i].sourceKey - sourceKey);
                if (d <= 2 && d < bestKeyDist) { bestKeyDist = d; bestKeyKey = i; }
            }
            if (bestKeyKey >= 0) return streams[bestKeyKey];
        }

        int best = -1;
        float bestDist = mergeDistance;
        for (int i = 0; i < streams.Length; i++)
        {
            if (!streams[i].active) continue;
            float d = Vector3.Distance(streams[i].rootWorld, pos);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        if (best >= 0) return streams[best];

        for (int i = 0; i < streams.Length; i++)
            if (!streams[i].active) return streams[i];

        // All slots busy -- reuse whichever was fed longest ago (most likely finishing up already).
        int oldest = 0;
        float oldestTimeSinceFed = float.MinValue;
        for (int i = 0; i < streams.Length; i++)
            if (streams[i].timeSinceFed > oldestTimeSinceFed) { oldestTimeSinceFed = streams[i].timeSinceFed; oldest = i; }
        return streams[oldest];
    }

    void Update()
    {
        Step(Time.deltaTime);
    }

    // Split out from Update() so it can be driven with an explicit dt -- same reasoning as
    // PotionLiquid.Step(): Time.deltaTime can't be forced from outside the player loop, so
    // editor/automation testing needs a way to advance this simulation deterministically regardless
    // of actual frame timing. feedTimeout comparisons use each stream's own timeSinceFed counter
    // (accumulated by dt right here, reset to 0 in Feed()) rather than an absolute Time.time stamp,
    // so repeated Step(dt) calls reproduce exactly what real play would show, with no dependency on
    // the engine's actual clock.
    public void Step(float dt)
    {
        if (!built) return;
        if (dt <= 0f) return;

        for (int i = 0; i < streams.Length; i++)
        {
            var s = streams[i];
            if (!s.active) continue;

            s.timeSinceFed += dt;
            bool beingFed = s.timeSinceFed <= feedTimeout;
            float target = beingFed ? s.targetLength : 0f;
            float rate = beingFed ? growSpeed : retractSpeed;
            s.currentLength = Mathf.Lerp(s.currentLength, target, 1f - Mathf.Exp(-rate * dt));
            if (s.currentLength < 0.0008f) s.currentLength = 0f;

            if (s.wasFed && !beingFed && !s.dropletSpawnedForThisPour)
            {
                if (s.currentLength >= dropletMinLengthToSpawn)
                {
                    // Droplet size reflects how strong THIS stream's flow was, not a fixed constant --
                    // links "how much is spilling" to "how big the droplets look" (2026-08-12,
                    // "こぼれる液体が小さいな液体で...量がリンクしていなそう").
                    float intensity01 = Mathf.Clamp01(s.targetLength / Mathf.Max(0.0001f, maxStreamLength));
                    float sizeScale = Mathf.Lerp(dropletMinSizeScale, dropletMaxSizeScale, intensity01);
                    SpawnDroplet(s.rootWorld + s.dirWorld * s.currentLength, s.dirWorld, sizeScale);
                }
                s.dropletSpawnedForThisPour = true;
            }
            if (beingFed) s.dropletSpawnedForThisPour = false;
            s.wasFed = beingFed;

            RebuildStreamMesh(s, dt);

            if (!beingFed && s.currentLength <= 0f)
            {
                s.active = false;
                s.sourceKey = -1;
                s.go.SetActive(false);
            }
        }

        for (int i = 0; i < droplets.Length; i++)
        {
            var d = droplets[i];
            if (!d.active) continue;
            d.velocity += Physics.gravity * dropletGravityModifier * dt;

            // Ground check BEFORE moving -- a short lookahead scaled by this step's own travel
            // distance (with a floor) so a fast-falling droplet doesn't tunnel through the ground in
            // a single big step. On a hit, the droplet becomes a puddle right there instead of
            // fading out in mid-air (2026-08-12, "こぼれた分は地面に残るようにして").
            float travelThisStep = d.velocity.magnitude * dt;
            float lookahead = Mathf.Max(0.04f, travelThisStep * 1.5f);
            if (Physics.Raycast(d.t.position, Vector3.down, out RaycastHit hit, lookahead, groundLayerMask, QueryTriggerInteraction.Ignore))
            {
                float sizeT = Mathf.InverseLerp(dropletMinSizeScale, dropletMaxSizeScale, d.sizeScale);
                SpawnOrGrowPuddle(hit.point, hit.normal, sizeT);
                d.active = false;
                d.t.gameObject.SetActive(false);
                continue;
            }

            d.t.position += d.velocity * dt;
            d.life -= dt;
            float lifeT = Mathf.Clamp01(d.life / dropletLifetime);
            float baseDiameter = dropletRadius * d.sizeScale * 2f * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, lifeT * 3f));

            // Squash-and-stretch along the actual fall direction: a plain uniformly-scaled sphere
            // reads as a cheap "particle ball", real liquid droplets elongate along their velocity,
            // more so the faster they fall. Width is divided by sqrt(stretch) so the droplet stays
            // volume-ish-consistent (deforming, not visibly growing) as it speeds up.
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
            // Only shrinks away in its final 1.5s -- otherwise holds at full size, matching "should
            // stay" rather than continuously fading the whole time it exists.
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
                // Pool exhausted -- reuse whichever puddle has the least life left (closest to
                // disappearing on its own anyway).
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
            // Repeated drips landing on the same spot build the stain up further, rather than each
            // one just resetting to the same base size.
            p.targetRadius = Mathf.Min(puddleMaxRadius, p.targetRadius + puddleGrowPerHit);
        }
        p.life = puddleLifetime;
    }

    void SpawnDroplet(Vector3 pos, Vector3 dir, float sizeScale)
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
            d.velocity = dir * dropletInitialSpeed * Mathf.Lerp(0.8f, 1.3f, Mathf.InverseLerp(dropletMinSizeScale, dropletMaxSizeScale, sizeScale));
            d.life = dropletLifetime;
            return;
        }
        // Pool exhausted -- silently drop; this is a transient polish visual, not something the
        // volume/overflow accounting depends on.
    }

    void RebuildStreamMesh(Stream s, float dt)
    {
        if (s.currentLength <= 0.0008f)
        {
            if (s.mesh.vertexCount > 0) s.mesh.Clear();
            return;
        }

        Vector3 dir = s.dirWorld;
        Vector3 perp1 = Vector3.Cross(dir, Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 perp2 = Vector3.Cross(dir, perp1).normalized;

        s.swayPhase += dt * swaySpeed;

        int rings = lengthSegments + 1;
        var verts = s.verts;
        int vi = 0;
        // Stub-thinning while just starting to grow (so the hanging BODY doesn't pop in as an
        // instant fat blob), separate from the intensity-based width scale below (so a strong
        // overflow still reads as fat even a moment after it starts, not just once fully extended).
        float growthFrac = Mathf.Clamp01(s.currentLength / (maxStreamLength * 0.35f));
        // Intensity-based width: how strong the CURRENT feed rate is (targetLength saturating
        // maxStreamLength = a genuinely large overflow), not just how long the stream has grown --
        // links "how much is spilling" to "how fat the stream looks" (2026-08-12,
        // "こぼれる液体が小さいな液体で...波打つ量とこぼれる量がリンクしていなそう"). minWidthScale
        // keeps even a bare-minimum drip visibly present instead of vanishing to a hairline.
        float intensity01 = Mathf.Clamp01(s.targetLength / Mathf.Max(0.0001f, maxStreamLength));
        float widthScale = Mathf.Lerp(minWidthScale, maxWidthScale, intensity01) * Mathf.Lerp(0.55f, 1f, growthFrac);
        // The rim BULGE (spec section 6 ①) is scaled by intensity only, NOT growthFrac -- it should
        // read as "liquid piling up" immediately, even in the very first instant of a pour, not ease
        // in over the same ~0.35*maxStreamLength ramp as the hanging body below it.
        float bulgeScale = Mathf.Lerp(minWidthScale, maxWidthScale, intensity01);
        for (int ring = 0; ring < rings; ring++)
        {
            float f = ring / (float)lengthSegments;
            float dist = f * s.currentLength;
            float sway = Mathf.Sin(s.swayPhase + f * 3f) * swayAmplitude * f * f;
            Vector3 center = s.rootWorld + dir * dist + perp1 * sway;

            // Four-stage silhouette matching spec section 6's ASCII progression: ① a wide bulge
            // sitting right at the rim (liquid piling up before it can escape) -> ② narrowing to a
            // neck as it squeezes over the rim edge -> ③ a steadier body hanging down under gravity
            // -> ④ bulging again at the very tip where a droplet is forming.
            float radius;
            if (f < 0.12f)
                radius = Mathf.Lerp(bulgeRootRadius * bulgeScale, neckRadius * widthScale, f / 0.12f);
            else if (f < 0.65f)
                radius = Mathf.Lerp(neckRadius, bodyRadius, (f - 0.12f) / 0.53f) * widthScale;
            else
                radius = Mathf.Lerp(bodyRadius, tipBulgeRadius, (f - 0.65f) / 0.35f) * widthScale;

            for (int side = 0; side < tubeSides; side++)
            {
                float ang = side / (float)tubeSides * Mathf.PI * 2f;
                Vector3 offset = (perp1 * Mathf.Cos(ang) + perp2 * Mathf.Sin(ang)) * radius;
                verts[vi++] = center + offset;
            }
        }
        verts[vi++] = s.rootWorld; // root cap center
        verts[vi++] = s.rootWorld + dir * s.currentLength + perp1 * Mathf.Sin(s.swayPhase + 3f) * swayAmplitude * growthFrac * growthFrac; // tip cap center

        s.mesh.vertices = verts;
        if (s.mesh.triangles.Length != cachedStreamTriangles.Length) s.mesh.triangles = cachedStreamTriangles;
        else s.mesh.SetTriangles(cachedStreamTriangles, 0);
        s.mesh.RecalculateNormals();
        s.mesh.RecalculateBounds();
    }
}
