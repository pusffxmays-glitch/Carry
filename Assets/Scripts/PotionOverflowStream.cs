using UnityEngine;
using System.Collections.Generic;

// Mesh-based flowing "liquid stream" for the potion's Overflow: root thick, body tapering, a small
// bulb near the tip where a droplet is forming, then an actual droplet that detaches and falls under
// (reduced) gravity. Added 2026-08-12 to replace the old single-particle-line drip representation,
// which was rejected outright ("細い緑色の線が壺から垂れているだけ...単なる細い直線Particleは禁止").
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
    [Tooltip("Base radius at the root (rim contact point). Raised 2026-08-12 (\"こぼれる液体が小さい\") -- the actual radius used each frame is this times widthScale below, so a strong overflow reads as a genuinely fat gush, not just a longer thin string.")]
    public float rootRadius = 0.024f;
    public float midRadius = 0.011f;
    [Tooltip("Extra bulge radius near the tip, simulating a droplet forming before it detaches.")]
    public float tipBulgeRadius = 0.02f;
    [Tooltip("Width multiplier at zero flow intensity (a bare-minimum drip should still read as a small but visible trickle, not collapse to nothing).")]
    public float minWidthScale = 0.5f;
    [Tooltip("Width multiplier at maximum flow intensity (targetLength saturating maxStreamLength) -- how much fatter a strong overflow gets compared to a weak drip.")]
    public float maxWidthScale = 2.0f;
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

    public Material liquidMaterial;

    class Stream
    {
        public bool active;
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

    Stream[] streams;
    Droplet[] droplets;
    int[] cachedStreamTriangles;
    bool built;

    void Awake() { EnsureBuilt(); }

    // PotionLiquid.Awake() calls this explicitly (after assigning liquidMaterial) rather than
    // relying on this component's own Awake() ordering: AddComponent<T>() invokes T's Awake()
    // synchronously, before the calling code gets a chance to set fields on the newly-created
    // component -- so the first EnsureBuilt() call (from this component's own Awake()) always runs
    // with liquidMaterial still null. rebuildMaterialsOnly re-applies the real material to every
    // already-built stream/droplet renderer once PotionLiquid has actually set it (same pattern as
    // PotionOverflowVFX.EnsureBuilt()).
    public void EnsureBuilt(bool rebuildMaterialsOnly = false)
    {
        if (built && !rebuildMaterialsOnly) return;

        if (!built)
        {
            built = true;
            cachedStreamTriangles = BuildStreamTriangles();

            streams = new Stream[Mathf.Max(1, maxStreams)];
            for (int i = 0; i < streams.Length; i++) streams[i] = CreateStream(i);

            droplets = new Droplet[Mathf.Max(1, maxDroplets)];
            for (int i = 0; i < droplets.Length; i++) droplets[i] = CreateDroplet(i);
            return;
        }

        if (liquidMaterial == null) return;
        for (int i = 0; i < streams.Length; i++) streams[i].mr.sharedMaterial = liquidMaterial;
        for (int i = 0; i < droplets.Length; i++) droplets[i].mr.sharedMaterial = liquidMaterial;
    }

    Stream CreateStream(int index)
    {
        var go = new GameObject("Stream_" + index);
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (liquidMaterial != null) mr.sharedMaterial = liquidMaterial;
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
        if (liquidMaterial != null) mr.sharedMaterial = liquidMaterial;
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

    // Called once per frame, from PotionLiquid's single dominant overflow point, while liquid is
    // actually spilling. worldRootPos is the rim contact point; dirWorld is the world-gravity-based
    // pour direction; flowRate is spilled volume per second (units match PotionLiquid.PotionVolume).
    public void Feed(Vector3 worldRootPos, Vector3 dirWorld, float flowRate)
    {
        if (flowRate <= 0f) return;
        EnsureBuilt();

        Stream s = FindOrAllocate(worldRootPos);
        if (s == null) return;

        if (!s.active)
        {
            s.active = true;
            s.go.SetActive(true);
            s.currentLength = 0f;
            s.dropletSpawnedForThisPour = false;
            s.wasFed = false;
        }
        s.rootWorld = worldRootPos;
        s.dirWorld = dirWorld.sqrMagnitude > 1e-6f ? dirWorld.normalized : Vector3.down;
        s.targetLength = Mathf.Clamp(flowRate * lengthPerVolume, 0f, maxStreamLength);
        s.timeSinceFed = 0f;
    }

    Stream FindOrAllocate(Vector3 pos)
    {
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
                s.go.SetActive(false);
            }
        }

        for (int i = 0; i < droplets.Length; i++)
        {
            var d = droplets[i];
            if (!d.active) continue;
            d.velocity += Physics.gravity * dropletGravityModifier * dt;
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
        // Stub-thinning while just starting to grow (so it doesn't pop in as an instant fat blob),
        // separate from the intensity-based width scale below (so a strong overflow still reads as
        // fat even a moment after it starts, not just once fully extended).
        float growthFrac = Mathf.Clamp01(s.currentLength / (maxStreamLength * 0.35f));
        // Intensity-based width: how strong the CURRENT feed rate is (targetLength saturating
        // maxStreamLength = a genuinely large overflow), not just how long the stream has grown --
        // links "how much is spilling" to "how fat the stream looks" (2026-08-12,
        // "こぼれる液体が小さいな液体で...波打つ量とこぼれる量がリンクしていなそう"). minWidthScale
        // keeps even a bare-minimum drip visibly present instead of vanishing to a hairline.
        float intensity01 = Mathf.Clamp01(s.targetLength / Mathf.Max(0.0001f, maxStreamLength));
        float widthScale = Mathf.Lerp(minWidthScale, maxWidthScale, intensity01) * Mathf.Lerp(0.55f, 1f, growthFrac);
        for (int ring = 0; ring < rings; ring++)
        {
            float f = ring / (float)lengthSegments;
            float dist = f * s.currentLength;
            float sway = Mathf.Sin(s.swayPhase + f * 3f) * swayAmplitude * f * f;
            Vector3 center = s.rootWorld + dir * dist + perp1 * sway;

            float radius = f < 0.6f
                ? Mathf.Lerp(rootRadius, midRadius, f / 0.6f)
                : Mathf.Lerp(midRadius, tipBulgeRadius, (f - 0.6f) / 0.4f);
            radius *= widthScale;

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
