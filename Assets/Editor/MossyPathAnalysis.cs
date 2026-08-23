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

        var bins = new List<Bin>();
        float minWidth = float.MaxValue, maxWidth = 0f;
        for (int b = 0; b < Segments; b++)
        {
            float loT = tMin + length * b / Segments;
            float hiT = tMin + length * (b + 1) / Segments;
            var idx = Enumerable.Range(0, n).Where(i => t[i] >= loT && t[i] < hiT).ToList();
            if (idx.Count < 3) continue;
            float x = 0, z = 0;
            var ys = new List<float>();
            var ws = new List<float>();
            foreach (var i in idx)
            {
                x += verts[i].x; z += verts[i].z; ys.Add(verts[i].y);
                float w = (verts[i].x - mean.x) * perp.x + (verts[i].z - mean.y) * perp.y;
                ws.Add(w);
            }
            x /= idx.Count; z /= idx.Count;
            ys.Sort();
            float topY = ys[Mathf.Clamp((int)(ys.Count * 0.85f), 0, ys.Count - 1)];
            float width = ws.Max() - ws.Min();
            minWidth = Mathf.Min(minWidth, width);
            maxWidth = Mathf.Max(maxWidth, width);
            bins.Add(new Bin { T = (loT + hiT) * 0.5f, Center = new Vector3(x, topY, z), Width = width, RawWidth = width });
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
        const float widthMargin = 0.9f; // slight inset from the visual footprint edge

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
            // RawWidth (true measured mesh width), not the taper-fix-widened Width -- otherwise the
            // collider at each piece's tapered tip is sized to the piece's INTERIOR width while the
            // visual mesh there is still narrow, so the collider physically overhangs past the mesh
            // edge at every joint (invisible ledge the goblin can stand on past the visible stone).
            float width = Mathf.Max(0.4f, bins[i].RawWidth * widthMargin);
            box.size = new Vector3(width, thickness, Mathf.Max(segLen * overlap, 0.3f));
            box.center = Vector3.zero;
        }
    }
}
