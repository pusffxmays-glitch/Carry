using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Reusable geometry analysis for the MossyRockPath course-module kit.
// Reads a piece's own imported mesh (already at real-world scale, local origin = entry point,
// baked in Blender) and re-derives, purely from vertex data in Unity's own coordinate frame:
//  - the exit point and length (2D PCA on local X-Z finds the travel axis; no dependency on any
//    Blender/FBX axis-convention assumption)
//  - entry/exit tangent directions, used by the course builder to chain pieces like a spline
//  - a width profile along the path, used to build a WalkableCollision BoxCollider chain that
//    follows the piece's own footprint (narrow at NarrowLink's neck, full width elsewhere) without
//    ever colliding against the bumpy visual mesh directly.
public static class MossyPathAnalysis
{
    public const int Segments = 40;
    const float EndSliceFrac = 0.03f;
    const float NearEndFrac = 0.12f;

    public struct Profile
    {
        public float Length;
        public Vector3 ExitLocalPos;   // local offset from entry (origin) to exit point, top surface
        public Vector2 EntryDirXZ;     // normalized initial tangent (horizontal, local space)
        public Vector2 ExitDirXZ;      // normalized final tangent (horizontal, local space)
        public float MinWidth, MaxWidth;
        public List<Bin> Bins;
    }

    public struct Bin
    {
        public float T;
        public Vector3 Center; // local space, top surface
        public float Width;    // display/gameplay width -- widened at the tapered tips (see taperFixBins) so joints don't read as an artificial pinch
        public float RawWidth; // TRUE measured mesh width at this slice, never widened -- collision must never exceed this or it overhangs past the visual mesh
    }

    public static Profile Analyze(Mesh mesh, bool isNarrowLink)
    {
        var verts = mesh.vertices;
        Vector2 mean = Vector2.zero;
        for (int i = 0; i < verts.Length; i++) mean += new Vector2(verts[i].x, verts[i].z);
        mean /= verts.Length;

        double sxx = 0, sxz = 0, szz = 0;
        for (int i = 0; i < verts.Length; i++)
        {
            double dx = verts[i].x - mean.x, dz = verts[i].z - mean.y;
            sxx += dx * dx; sxz += dx * dz; szz += dz * dz;
        }
        int n = verts.Length;
        sxx /= n; sxz /= n; szz /= n;
        double trace = sxx + szz;
        double det = sxx * szz - sxz * sxz;
        double disc = System.Math.Sqrt(System.Math.Max(0, trace * trace / 4 - det));
        double lambda1 = trace / 2 + disc;
        Vector2 primary;
        if (System.Math.Abs(sxz) > 1e-9)
            primary = new Vector2((float)(lambda1 - szz), (float)sxz).normalized;
        else
            primary = sxx >= szz ? Vector2.right : Vector2.up; // Vector2.up == (0,1) i.e. local Z

        float[] t = new float[n];
        for (int i = 0; i < n; i++)
            t[i] = (verts[i].x - mean.x) * primary.x + (verts[i].z - mean.y) * primary.y;

        // origin (0,0,0) is the known entry point (Blender-baked) -- orient primary so origin sits at tMin
        float tOrigin = (0 - mean.x) * primary.x + (0 - mean.y) * primary.y;
        float tMinRaw = t.Min(), tMaxRaw = t.Max();
        if (tOrigin > (tMinRaw + tMaxRaw) * 0.5f)
        {
            primary = -primary;
            for (int i = 0; i < n; i++) t[i] = -t[i];
            tOrigin = -tOrigin;
        }

        float tMin = t.Min(), tMax = t.Max();
        float length = tMax - tMin;
        Vector2 perp = new Vector2(-primary.y, primary.x);

        Vector3 ExitPointFromSlice(float loT, float hiT)
        {
            var idx = Enumerable.Range(0, n).Where(i => t[i] >= loT && t[i] < hiT).ToList();
            if (idx.Count == 0) idx = Enumerable.Range(0, n).OrderBy(i => -t[i]).Take(50).ToList();
            float x = 0, z = 0;
            var ys = new List<float>();
            foreach (var i in idx) { x += verts[i].x; z += verts[i].z; ys.Add(verts[i].y); }
            x /= idx.Count; z /= idx.Count;
            ys.Sort();
            float topY = ys[Mathf.Clamp((int)(ys.Count * 0.85f), 0, ys.Count - 1)];
            return new Vector3(x, topY, z);
        }

        Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        Vector3 exitPos = ExitPointFromSlice(tMax - EndSliceFrac * length, tMax + 0.001f);
        Vector3 nearEntryPos = ExitPointFromSlice(tMin + EndSliceFrac * length, tMin + NearEndFrac * length);
        Vector3 nearExitPos = ExitPointFromSlice(tMax - NearEndFrac * length, tMax - EndSliceFrac * length);

        Vector2 entryDir = (XZ(nearEntryPos) - Vector2.zero).normalized; // from origin(entry) toward interior
        Vector2 exitDir = (XZ(exitPos) - XZ(nearExitPos)).normalized;

        // Pass 1: bin membership (by T, along the whole-piece PCA axis -- fine for "how far along
        // the path" grouping) and each bin's center point only. Width is deferred to pass 2.
        var bins = new List<Bin>();
        var binIdx = new List<List<int>>();
        for (int b = 0; b < Segments; b++)
        {
            float loT = tMin + length * b / Segments;
            float hiT = tMin + length * (b + 1) / Segments;
            var idx = Enumerable.Range(0, n).Where(i => t[i] >= loT && t[i] < hiT).ToList();
            if (idx.Count < 3) continue;
            float x = 0, z = 0;
            var ys = new List<float>();
            foreach (var i in idx) { x += verts[i].x; z += verts[i].z; ys.Add(verts[i].y); }
            x /= idx.Count; z /= idx.Count;
            ys.Sort();
            float topY = ys[Mathf.Clamp((int)(ys.Count * 0.85f), 0, ys.Count - 1)];
            binIdx.Add(idx);
            bins.Add(new Bin { T = (loT + hiT) * 0.5f, Center = new Vector3(x, topY, z), Width = 0f, RawWidth = 0f });
        }

        // A bin near a tapering/curving tip can hold only a handful of vertices, so its raw
        // (x,z) centroid is noisy -- confirmed directly: consecutive bins' centre-to-centre
        // direction was seen swinging ~174deg -> 152deg step to step, far more than the piece's
        // actual curvature over that short a span. That noise otherwise feeds straight into the
        // local tangent/width/box-orientation math below, so smooth the centre line first (light
        // 3-point moving average; T and RawWidth/Width aren't touched here, only Center).
        var smoothed = new List<Vector3>(bins.Count);
        for (int b = 0; b < bins.Count; b++)
        {
            Vector3 c0 = bins[Mathf.Max(0, b - 1)].Center;
            Vector3 c1 = bins[b].Center;
            Vector3 c2 = bins[Mathf.Min(bins.Count - 1, b + 1)].Center;
            smoothed.Add((c0 + c1 + c2) / 3f);
        }
        for (int b = 0; b < bins.Count; b++) { var bin = bins[b]; bin.Center = smoothed[b]; bins[b] = bin; }

        // Pass 2: width per bin, measured perpendicular to that BIN'S OWN local tangent (from its
        // neighbouring bin centers) -- not the single whole-piece PCA axis used above for T-sorting.
        // A piece that curves along its length (e.g. WideCurve, ~91deg total turn) has a local
        // direction of travel near its ends that can differ sharply from that one global axis;
        // projecting cross-section vertices onto the wrong (global) perpendicular measures width
        // along a skewed axis and can overstate it substantially. Confirmed via Scene view: this
        // alone (not the tip/taper fix, already handled by RawWidth) was letting a mid-taper
        // collider segment on a curving mirrored piece fan out past the actual rock edge.
        float minWidth = float.MaxValue, maxWidth = 0f;
        for (int b = 0; b < bins.Count; b++)
        {
            Vector3 prevC = b > 0 ? bins[b - 1].Center : bins[b].Center;
            Vector3 nextC = b < bins.Count - 1 ? bins[b + 1].Center : bins[b].Center;
            Vector2 localTan = new Vector2(nextC.x - prevC.x, nextC.z - prevC.z);
            if (localTan.sqrMagnitude < 1e-8f) localTan = primary;
            localTan.Normalize();
            Vector2 localPerp = new Vector2(-localTan.y, localTan.x);

            float wMin = float.MaxValue, wMax = float.MinValue;
            foreach (var i in binIdx[b])
            {
                float w = (verts[i].x - bins[b].Center.x) * localPerp.x + (verts[i].z - bins[b].Center.z) * localPerp.y;
                wMin = Mathf.Min(wMin, w); wMax = Mathf.Max(wMax, w);
            }
            float width = wMax - wMin;
            minWidth = Mathf.Min(minWidth, width);
            maxWidth = Mathf.Max(maxWidth, width);
            // Recenter on the width span's own midpoint, not the raw vertex-position average -- a
            // skewed vertex distribution at this slice (typical on a tapering/curving connector
            // piece) would otherwise leave the box's true footprint centered off to one side, so a
            // box built symmetric around the average overshoots the mesh on one edge even though
            // its total width is correct.
            float mid = (wMin + wMax) * 0.5f;
            Vector3 recenter = bins[b].Center + new Vector3(localPerp.x, 0f, localPerp.y) * mid;
            var bin = bins[b]; bin.Center = recenter; bin.Width = width; bin.RawWidth = width; bins[b] = bin;
        }

        // Every piece tapers to a near-point tip at both connection ends (Meshy's jigsaw-style
        // tiling geometry) -- that taper is a modeling artifact of the connector, not a gameplay
        // feature, so it must not read as an artificial "narrow squeeze" at every module joint.
        // Widen just the first/last couple of bins to their nearest interior neighbour's width.
        // NarrowLink's real neck sits well inside the piece (not at the very tip) so this never
        // touches it.
        const int taperFixBins = 2;
        if (bins.Count > taperFixBins * 2 + 1)
        {
            float innerStart = bins[taperFixBins].Width;
            float innerEnd = bins[bins.Count - 1 - taperFixBins].Width;
            for (int b = 0; b < taperFixBins; b++)
            {
                var bin = bins[b]; bin.Width = Mathf.Max(bin.Width, innerStart); bins[b] = bin;
                var binE = bins[bins.Count - 1 - b]; binE.Width = Mathf.Max(binE.Width, innerEnd); bins[bins.Count - 1 - b] = binE;
            }
            minWidth = bins.Min(bb => bb.Width);
        }

        return new Profile
        {
            Length = length,
            ExitLocalPos = exitPos,
            EntryDirXZ = entryDir,
            ExitDirXZ = exitDir,
            MinWidth = minWidth,
            MaxWidth = maxWidth,
            Bins = bins,
        };
    }

    // Every piece's mesh tapers to a near-point tip at both connection ends (see taperFixBins note
    // above) -- fine for an intentionally narrow/"corner" joint, but wrong for a normal joint that's
    // supposed to read as two full-width stone slabs meeting edge-to-edge. This returns the
    // position/tangent-direction INSET a fixed number of bins in from either end, at the point
    // where the piece's own width has actually recovered from the taper (empirically ~bin 8 of 40,
    // i.e. about 20% of the length in, across all 5 pieces) -- callers place two pieces' INSET
    // points against each other for a smooth/wide joint, or their raw tip (local origin / ExitLocalPos)
    // against each other for the deliberately narrow "corner" joint.
    public const int InsetBinIndex = 8;

    public static (Vector3 pos, Vector2 dir) GetInset(Profile profile, bool fromEntry)
    {
        var bins = profile.Bins;
        int n = bins.Count;
        int k = fromEntry ? InsetBinIndex : (n - 1 - InsetBinIndex);
        k = Mathf.Clamp(k, 1, n - 2);
        Vector3 pos = bins[k].Center;
        Vector3 d3 = bins[k + 1].Center - bins[k - 1].Center;
        Vector2 dir = new Vector2(d3.x, d3.z).normalized;
        return (pos, dir);
    }

    // Builds a chain of rotated BoxColliders under parent, one per analyzed bin, following the
    // piece's own centerline/width -- deliberately NOT the visual mesh -- so the walkable surface
    // is smooth (no per-stone bumps) while still narrowing exactly where the piece itself narrows
    // (critical for NarrowLink's intentional neck).
    public static void BuildColliderChain(Profile profile, Transform parent)
    {
        var bins = profile.Bins;
        if (bins.Count < 2) return;
        const float thickness = 0.6f;
        const float overlap = 1.15f; // extend each segment's length slightly so adjacent boxes always overlap, never gap
        const float widthMargin = 0.92f; // slight inset from the visual footprint edge

        // Each box physically spans from the midpoint with its PREVIOUS bin to the midpoint with
        // its NEXT bin -- a wider T-range than the single point its own RawWidth was measured at.
        // Near a taper (fastest-changing width in the whole piece) that mismatch is large: sizing
        // the box by its own bin's width alone made it overshoot the true mesh at the box's
        // narrower end while falling short of the true mesh at its wider end -- "sticks out AND
        // isn't wide enough," simultaneously, exactly as reported. Fix: compute the true width at
        // each of a box's own two boundary points (interpolated between adjacent bins) and use the
        // SMALLER of the two for that whole box -- the tightest width that still never exceeds the
        // real mesh anywhere along that specific box's own length. The very first/last boundary
        // (the piece's true connector tip) is a mathematical point of ~zero width, but treating it
        // as exactly 0 would collapse the tip box itself down to a hard-clamped sliver -- the OLD
        // (bin-width-only) approach was already visually confirmed fine at the tip specifically, so
        // keep that: use the tip bin's own RawWidth as its boundary rather than the true zero.
        var boundaryWidth = new float[bins.Count + 1];
        boundaryWidth[0] = bins[0].RawWidth;
        boundaryWidth[bins.Count] = bins[bins.Count - 1].RawWidth;
        for (int k = 1; k < bins.Count; k++)
            boundaryWidth[k] = (bins[k - 1].RawWidth + bins[k].RawWidth) * 0.5f;

        for (int i = 0; i < bins.Count; i++)
        {
            Vector3 c0 = i == 0 ? Vector3.zero : bins[i - 1].Center;
            Vector3 c1 = bins[i].Center;
            Vector3 c2 = i == bins.Count - 1 ? profile.ExitLocalPos : bins[i + 1].Center;

            Vector3 segStart = Vector3.Lerp(c0, c1, 0.5f);
            Vector3 segEnd = Vector3.Lerp(c1, c2, 0.5f);
            Vector3 mid = (segStart + segEnd) * 0.5f;
            Vector3 dir = segEnd - segStart;
            float segLen = dir.magnitude;
            if (segLen < 1e-4f) dir = Vector3.forward; else dir /= segLen;

            var go = new GameObject($"Coll_{i:00}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = mid + Vector3.up * (thickness * -0.35f); // top face sits just under the sampled surface height
            go.transform.localRotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z).normalized, Vector3.up);

            var box = go.AddComponent<BoxCollider>();
            float segWidth = Mathf.Min(boundaryWidth[i], boundaryWidth[i + 1]);
            float width = Mathf.Max(0.4f, segWidth * widthMargin);
            box.size = new Vector3(width, thickness, Mathf.Max(segLen * overlap, 0.3f));
            box.center = Vector3.zero;
        }
    }
}
