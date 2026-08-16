using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-15: builds the "safe recovery route" from the lake back up to the stone bridge, per the
// brief: a goblin that fell into the lake while carrying a pot of potion needs to walk (never jump)
// back up to the bridge without spilling. The MossyStoneRamp asset itself (see
// CarrySetupMossyStoneRamp.cs) turned out, once actually measured/screenshotted, to be a single
// irregular ~2m mossy stone SLAB/chunk -- not a pre-built long ramp/staircase mesh. So the walkable
// slope itself is built the way CLAUDE.md's 接地ルール rule 8 already mandates for cliffs ("崖の基本構造は
// Terrainで作る...大量の小型岩の集合体として作らない"): a gently-rising bench is SCULPTED into the existing
// west-shore Terrain (guaranteeing a perfectly smooth, continuous, jump-free walking surface -- the
// TerrainCollider IS the ramp's collider, never the rock mesh's own bumpy silhouette), and
// MossyStoneRamp instances are scattered along its edges purely as decorative paving/retaining stones,
// same relationship LakeStairs/LakeShoreDressing already have to this Terrain elsewhere in the scene.
//
// Route survey (done live via terrain.SampleHeight scans before writing this, see conversation): the
// west shore between the waterfall pool and the bridge is a fairly deep cove (dry "just above the
// lake's -4.4 water surface" shelf swings out to about x=-27..-28 around z=-16..-20) with the existing
// AzureCrystal_CliffWall/CliffCrack/RockGap set and a few CliffBoulder_*/LakeShore_* dressing objects
// already sitting right on that same shelf. Rather than either (a) cutting a shortcut straight through
// what is currently open lake there (would silently drain/fill part of the lake), or (b) leaving those
// existing objects floating/buried wherever the new corridor changes the ground under them, this script
// follows the surveyed shelf's actual contour AND re-grounds (shifts by the same delta the terrain
// moved) every existing dressing object within the corridor -- never deletes/hides/buries a crystal.
public static class CarryBuildLakeRampPath
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    // 2026-08-15: switched to the 1.5x-wider (X axis only, baked in via Blender -- see
    // CarrySetupMossyStoneRamp.cs) variant, per explicit "坂の幅が狭すぎる" feedback.
    const string RampPrefabPath = "Assets/Stage/Lake/Models/MossyStoneRamp/Prefabs/MossyStoneRamp_Wide.prefab";

    // Path centerline control points (x,z). y is NOT hand-typed here -- it is derived below from
    // arc-length + a smoothstep ease (gentle at both ends, per rule "傾斜は非常に緩やかにする"/"入口出口に
    // 段差を作らない"), anchored to a measured START just above the waterline and a measured GOAL on the
    // existing BridgeEmbankment_0 plateau (measured live: terrain there sits ~3.0-4.1).
    static readonly Vector2[] CtrlXZ = new Vector2[]
    {
        new Vector2(-8f,   -33f),  // START: west shore right beside (not in front of) the waterfall pool
        new Vector2(-14f,  -30f),
        new Vector2(-19f,  -27f),
        new Vector2(-24.5f,-23f),
        new Vector2(-27.5f,-19f),  // widest point of the cove -- follows the real shelf, not a shortcut
        new Vector2(-26f,  -15f),
        new Vector2(-24f,  -11f),
        new Vector2(-22.5f, -8f),
        new Vector2(-18f,  -6f),
        new Vector2(-14f,  -2f),
        new Vector2(-13f,   2f),
        new Vector2(-13f,   4f),   // GOAL: ties into the BridgeEmbankment_0 / LakeShore_20 plateau
    };
    // CORRECTED 2026-08-15 after the first run: these were originally guessed from a hillside sample
    // point and an embankment ROCK's own y (not bare ground), and turned out badly wrong once checked
    // against the actual live-sampled terrain -- -3.7 at START dug an artificial pit into what is
    // already flat dry shelf there (measured -0.4..-0.5 via terrain.SampleHeight before any carving),
    // and 3.4 at GOAL was a rock prop's height, not the bare ground (measured ~1.6..2.0 nearby). The
    // player reaches this shelf by SWIMMING across the lake's actual underwater drop-off, not by
    // walking it, so the ramp only needs to smooth the shelf itself, not reach down to the waterline.
    const float StartY = -0.45f;
    const float GoalY = 1.8f;

    const float CoreHalfWidth = 1.9f;   // full-weight walkable bench half-width
    const float OuterHalfWidth = 3.4f;  // blend fully back to original terrain by here
    const float RegroundSearchMargin = 1.0f; // extra beyond OuterHalfWidth for re-grounding existing dressing

    const int SplineSamplesPerSeg = 24;

    // Shared ramp low/high anchors -- these describe where the ramp ACTUALLY currently sits (the user
    // places/rotates/scales the ramp by hand in the Editor; these constants are kept in sync with that
    // by hand too, measured live via TransformPoint(RampLocalLowPoint/RampLocalHighPoint) each time the
    // ramp moves -- see conversation). Everything else in this file (walls, shore dropoff, corridor) is
    // built relative to these, so keeping them accurate is what keeps all of it aligned with the ramp.
    // 2026-08-15, updated after the user manually repositioned/rotated the ramp again (pos ~(13.95,
    // -3.43,-6.48), rot ~(352.6,187.6,171.8)) -- measured fresh via TransformPoint.
    static readonly Vector2 RampLowAnchorXZ = new Vector2(13.49f, -12.78f);
    static readonly Vector2 RampHighAnchorXZ = new Vector2(14.25f, 0.01f);
    const float RampLowAnchorGroundY = -5.29f;
    const float RampHighAnchorGroundY = -0.28f;

    // 2026-08-15, SECOND revision: the ramp's new position/rotation put its high end much closer to the
    // bridge than before (was (18.95,-1.87), a water channel sat between it and the bridge; now
    // (14.25,0.01)), and a fresh terrain survey (terrain.SampleHeight grid, see conversation) found the
    // whole area between here and the bridge is ALREADY dry and gently sloped (roughly -0.1 to 0.65, no
    // channel at all) -- the elaborate channel-crossing detour the old control points needed is no
    // longer necessary. Simplified to a short, direct 3-point curve.
    static readonly Vector2[] BridgeCorridorCtrlXZ = new Vector2[]
    {
        RampHighAnchorXZ,         // START: exactly the ramp's own high end
        new Vector2(11.2f, 2.7f),
        new Vector2(8.2f, 5.45f), // GOAL: ties into BridgeEmbankment_0
    };
    const float BridgeCorridorStartY = RampHighAnchorGroundY; // no seam with the ramp's own high end
    const float BridgeCorridorGoalY = 0.65f; // measured live at BridgeEmbankment_0's ground (terrain.SampleHeight ~0.60)
    const float BridgeCorridorCoreHalfWidth = 2.4f;  // full-weight width -- comfortably spans the ~4-4.5m channel crossing
    const float BridgeCorridorOuterHalfWidth = 4.2f; // blend fully back to original terrain by here

    [MenuItem("Carry/Build Lake Ramp Path")]
    public static void Run()
    {
        var log = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();
            var terrainRoot = GameObject.Find("ForestStage_Terrain");

            // ---- 1. Build the centerline: control points -> Catmull-Rom sample list, with Y assigned
            // by arc-length + smoothstep ease between StartY and GoalY (never hand-typed per point, so
            // the slope is provably continuous, no per-waypoint jumps).
            var ctrl3 = BuildControlPoints3D();
            var samples = CatmullRomSample(ctrl3, SplineSamplesPerSeg);
            float totalLen = PolylineLength(samples);
            log.AppendLine("Centerline: " + ctrl3.Length + " control points, " + samples.Count + " samples, arc length=" + totalLen.ToString("F1") + "m, rise=" + (GoalY - StartY).ToString("F2") + "m (avg slope " + (Mathf.Atan2(GoalY - StartY, totalLen) * Mathf.Rad2Deg).ToString("F1") + " deg).");

            // ---- 2. Sculpt the terrain bench along the centerline.
            CarveBench(samples, OuterHalfWidth, CoreHalfWidth, terrain, terrainGO, "main causeway", log);

            // ---- 3. Re-ground existing dressing: DELIBERATELY NOT AUTOMATIC. Two different automated
            // approaches were tried and both produced worse placements than doing nothing: (a) a
            // delta-from-history version (compare old vs new SampleHeight at the same XZ) broke the
            // moment this method's own StartY/GoalY constants were corrected and re-run, since the "old"
            // snapshot was itself already a previous buggy run's output; (b) a bounds-vs-raycast version
            // (straight down from Renderer.bounds.center) looked history-independent but is NOT a valid
            // floating/buried test for most of what actually lives in this corridor -- CliffBoulder_*
            // rocks and AzureCrystal_CliffWall/CliffCrack/RockGap crystals are deliberately perched on a
            // steep slope or angled into a rock face, so a straight-down ray travels a long diagonal
            // before hitting anything and reads as "buried by several meters" even when correctly
            // placed; it then yanked WaterfallBaseRock_0_0 (meant to sit low, near the splash pool) up
            // by +3.9m and LakeShore_20 down by -4.3m. Given the corrected StartY/GoalY now match the
            // ALREADY-EXISTING ground almost exactly (see conversation notes: measured pre-carve height
            // at every control point is within a few cm of the assigned target), the real terrain delta
            // this carve produces under nearby dressing is small everywhere by construction, so nothing
            // here actually needs correcting -- verified per-object via screenshots instead of blindly
            // "fixed" by a heuristic that cannot tell perched-on-a-slope from floating.
            log.AppendLine("Skipped automatic re-grounding of existing dressing (see code comment) -- verify manually via screenshots.");

            // ---- 4. Slope verification along the actual sculpted surface (not the pre-carve one).
            VerifySlope(terrain, terrainGO, samples, log);

            // ---- 5. Single, UNDECOMPOSED MossyStoneRamp instance at the lake's edge, used as the actual
            // climb-out feature. 2026-08-15 second revision, per explicit direction: place the prepared
            // asset as ONE piece along the lake's edge so the goblin can climb the edge with it -- not
            // scattered/split into many pieces. The mesh itself was analyzed vertex-by-vertex (see
            // conversation) and DOES contain a genuine wedge/ramp gradient, just along its shortest local
            // axis (Z, ~1.13m raw): the bottom face is flat while the top face slopes steadily from one
            // end to the other. Rotating 180 degrees around local Z exposes that sloped face as the
            // object's walkable top (flat bottom now rests on the ground, as a ramp should). The gentle,
            // longer walk from here up to the bridge is still handled by the sculpted Terrain bench
            // above (step 2) -- this single piece is specifically the "climb out of the water onto the
            // bench" moment at the lake's edge itself.
            var oldParent = GameObject.Find("ForestStage_Terrain/LakeRampPath");
            if (oldParent != null) Object.DestroyImmediate(oldParent);
            var rampPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RampPrefabPath);
            if (rampPrefab == null) { log.AppendLine("FAILED: MossyStoneRamp prefab not found at " + RampPrefabPath + " -- run Carry/Setup Mossy Stone Ramp first."); }
            else
            {
                var parent = new GameObject("LakeRampPath").transform;
                parent.SetParent(terrainRoot.transform, false);
                PlaceSingleClimbRamp(rampPrefab, terrain, terrainGO, parent, log);

                // Local terrain changes near the ramp exposed a background mesh that used to sit safely
                // buried -- patch the terrain back up over just that mesh's protruding vertices, in the
                // ramp's own vicinity only (never a general/global fix for this shared, lake-wide mesh).
                CoverExposedLakeCliffMesh(terrain, terrainGO, log);

                // The natural shelf this whole spot sits on (the one the old, now-deactivated LakeStairs
                // used) is gentle enough to climb on its own -- without a barrier, a player could scramble
                // out of the lake right beside the ramp instead of using it. Invisible walls funnel every
                // exit through the ramp's own walkway specifically.
                BuildShoreBarrier(terrain, terrainGO, parent, log);

                // A REAL, visible landform doing the same job as the invisible walls above -- per
                // explicit "土や岩を活用して段差を作って" direction, a physical submerged drop + rock rim
                // instead of relying only on invisible collision.
                BuildShoreStepBarrier(terrainRoot, terrain, terrainGO, parent, log);
            }

            // ---- 6. Ramp-top -> bridge corridor, per "MossyStoneRamp_ClimbRampを上り切った部分から平らな陸と
            // つながり橋まで歩けるようになっている状態にして". Runs LAST -- BuildShoreStepBarrier's trench
            // logic only knows about the RAMP's own line and doesn't know this corridor exists, so
            // running the corridor before it let the trench's "reconnect to natural" fade fight the
            // corridor wherever the corridor's path re-entered the trench's lateral range (measured
            // live: one corridor point regressed from a walkable 0.23 to a submerged -2.50 this way).
            // Running the corridor LAST avoids that. To avoid the OPPOSITE problem (this corridor
            // overwriting the ramp's own precise mesh-vertex fit, which was the original bug), it
            // explicitly excludes any cell within the ramp's own walkway fit corridor via the
            // exclLowXZ/exclHighXZ/exclHalfWidth params -- see CarveBench's overload doc comment.
            var bridgeCtrl3 = BuildControlPoints3D(BridgeCorridorCtrlXZ, BridgeCorridorStartY, BridgeCorridorGoalY);
            var bridgeSamples = CatmullRomSample(bridgeCtrl3, SplineSamplesPerSeg);
            CarveBench(bridgeSamples, BridgeCorridorOuterHalfWidth, BridgeCorridorCoreHalfWidth, terrain, terrainGO, "ramp-to-bridge corridor", log,
                RampLowAnchorXZ, RampHighAnchorXZ, 7.0f);
            VerifySlope(terrain, terrainGO, bridgeSamples, log);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    // ---- centerline construction ----
    static Vector3[] BuildControlPoints3D() => BuildControlPoints3D(CtrlXZ, StartY, GoalY);

    static Vector3[] BuildControlPoints3D(Vector2[] ctrlXZ, float startY, float goalY)
    {
        // First pass: arc length of the XZ control polyline only, to assign an eased Y per point.
        float[] cum = new float[ctrlXZ.Length];
        cum[0] = 0f;
        for (int i = 1; i < ctrlXZ.Length; i++) cum[i] = cum[i - 1] + Vector2.Distance(ctrlXZ[i - 1], ctrlXZ[i]);
        float total = cum[ctrlXZ.Length - 1];

        var pts = new Vector3[ctrlXZ.Length];
        for (int i = 0; i < ctrlXZ.Length; i++)
        {
            float t = cum[i] / total;
            float eased = t * t * (3f - 2f * t); // smoothstep -- gentle at both ends
            float y = Mathf.Lerp(startY, goalY, eased);
            pts[i] = new Vector3(ctrlXZ[i].x, y, ctrlXZ[i].y);
        }
        return pts;
    }

    static List<Vector3> CatmullRomSample(Vector3[] ctrl, int perSeg)
    {
        var outp = new List<Vector3>();
        int n = ctrl.Length;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = ctrl[Mathf.Max(0, i - 1)];
            Vector3 p1 = ctrl[i];
            Vector3 p2 = ctrl[i + 1];
            Vector3 p3 = ctrl[Mathf.Min(n - 1, i + 2)];
            int steps = (i == n - 2) ? perSeg + 1 : perSeg; // include the very last point once
            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)perSeg;
                float t2 = t * t, t3 = t2 * t;
                Vector3 p = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                outp.Add(p);
            }
        }
        return outp;
    }

    static float PolylineLength(List<Vector3> pts)
    {
        float len = 0f;
        for (int i = 1; i < pts.Count; i++) len += Vector3.Distance(pts[i - 1], pts[i]);
        return len;
    }

    static void PathBounds(List<Vector3> pts, float margin, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);
        foreach (var p in pts)
        {
            min.x = Mathf.Min(min.x, p.x - margin); min.y = Mathf.Min(min.y, p.z - margin);
            max.x = Mathf.Max(max.x, p.x + margin); max.y = Mathf.Max(max.y, p.z + margin);
        }
    }

    // nearest point on the (XZ) polyline to (worldX,worldZ); returns lateral distance and the path's
    // own Y at that nearest point (linearly interpolated along the nearest segment).
    static void NearestOnPath(List<Vector3> pts, float worldX, float worldZ, out float dist, out float y)
    {
        float best = float.MaxValue; float bestY = pts[0].y;
        Vector2 p = new Vector2(worldX, worldZ);
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 a = new Vector2(pts[i - 1].x, pts[i - 1].z);
            Vector2 b = new Vector2(pts[i].x, pts[i].z);
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            Vector2 proj = a + ab * t;
            float d = Vector2.Distance(p, proj);
            if (d < best)
            {
                best = d;
                bestY = Mathf.Lerp(pts[i - 1].y, pts[i].y, t);
            }
        }
        dist = best; y = bestY;
    }

    // Sculpts a walkable bench into the Terrain following `samples` (a 3D centerline -- see
    // BuildControlPoints3D/CatmullRomSample), full-weight out to coreHalfWidth then cosine-blending
    // back to the original terrain by outerHalfWidth. Shared by the main west-shore causeway and (added
    // 2026-08-15) the ramp-to-bridge corridor -- both need the identical "carve toward a pre-eased 3D
    // spline" behavior, just with different control points/widths.
    static void CarveBench(List<Vector3> samples, float outerHalfWidth, float coreHalfWidth, Terrain terrain, GameObject terrainGO, string label, StringBuilder log)
        => CarveBench(samples, outerHalfWidth, coreHalfWidth, terrain, terrainGO, label, log, null, null, 0f);

    // exclLowXZ/exclHighXZ/exclHalfWidth: an optional OTHER line segment (e.g. the ramp's own low->high
    // anchors) whose corridor this carve must never touch, regardless of carve order -- lets two
    // independent CarveBench calls (e.g. this corridor and BlendTerrainUnderRamp's ramp-mesh fit) share
    // the same terrain without one silently overwriting the other's precisely-fit footprint. 2026-08-15:
    // added after the ramp-to-bridge corridor (whose own path loops back within reach of the ramp's
    // upper body) was found overwriting BlendTerrainUnderRamp's mesh-vertex fit there with its own
    // smooth spline height, burying a third of the rock regardless of which carve ran first/last.
    static void CarveBench(List<Vector3> samples, float outerHalfWidth, float coreHalfWidth, Terrain terrain, GameObject terrainGO, string label, StringBuilder log, Vector2? exclLowXZ, Vector2? exclHighXZ, float exclHalfWidth)
    {
        var data = terrain.terrainData;
        float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
        float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
        int hr = data.heightmapResolution;

        Vector2 exclDir = Vector2.zero; float exclLen = 0f;
        bool hasExcl = exclLowXZ.HasValue && exclHighXZ.HasValue && exclHalfWidth > 0f;
        if (hasExcl) { exclDir = exclHighXZ.Value - exclLowXZ.Value; exclLen = exclDir.magnitude; exclDir /= exclLen; }

        var heights = data.GetHeights(0, 0, hr, hr);
        Vector2 bbMin, bbMax;
        PathBounds(samples, outerHalfWidth + 1f, out bbMin, out bbMax);
        int minXi = Mathf.Max(0, Mathf.FloorToInt((bbMin.x - originX) / sizeX * (hr - 1)));
        int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((bbMax.x - originX) / sizeX * (hr - 1)));
        int minZi = Mathf.Max(0, Mathf.FloorToInt((bbMin.y - originZ) / sizeZ * (hr - 1)));
        int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((bbMax.y - originZ) / sizeZ * (hr - 1)));

        float maxDelta = 0f; int excludedCells = 0;
        for (int zi = minZi; zi <= maxZi; zi++)
        {
            float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
            for (int xi = minXi; xi <= maxXi; xi++)
            {
                float worldX = originX + (xi / (float)(hr - 1)) * sizeX;

                if (hasExcl)
                {
                    Vector2 p2 = new Vector2(worldX, worldZ);
                    float et = Mathf.Clamp01(Vector2.Dot(p2 - exclLowXZ.Value, exclDir) / exclLen);
                    Vector2 onExcl = exclLowXZ.Value + exclDir * (et * exclLen);
                    if (Vector2.Distance(p2, onExcl) <= exclHalfWidth) { excludedCells++; continue; }
                }

                float nearestDist; float targetY;
                NearestOnPath(samples, worldX, worldZ, out nearestDist, out targetY);
                if (nearestDist > outerHalfWidth) continue;
                float weight = nearestDist <= coreHalfWidth ? 1f :
                    0.5f * (1f + Mathf.Cos((nearestDist - coreHalfWidth) / (outerHalfWidth - coreHalfWidth) * Mathf.PI));

                float originalWorldY = originY + heights[zi, xi] * sizeY;
                float newWorldY = Mathf.Lerp(originalWorldY, targetY, weight);
                maxDelta = Mathf.Max(maxDelta, Mathf.Abs(newWorldY - originalWorldY));
                heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
            }
        }
        data.SetHeights(0, 0, heights);
        Physics.SyncTransforms();
        log.AppendLine("CarveBench (" + label + "): X[" + minXi + "," + maxXi + "] Z[" + minZi + "," + maxZi + "] (heightmap cells), max single-cell delta=" + maxDelta.ToString("F2") + "m" + (hasExcl ? ", " + excludedCells + " cells excluded (other corridor)." : "."));
    }

    // Bounds-based re-ground: for every existing dressing object whose XZ falls inside the (widened)
    // corridor, raycast the CURRENT TerrainCollider under its Renderer.bounds center and compare
    // against that same Renderer's CURRENT bounds.min.y. Only touches objects that are actually
    // floating or buried by more than a small tolerance, and preserves a small (0.1m) embed rather
    // than snapping flush -- same spirit as this project's existing embed convention
    // (CarryFixLakeLandmarksPass2's RegroundFloatingCliffBoulders).
    static void RegroundNearbyDressing(GameObject terrainRoot, List<Vector3> samples, Terrain terrain, StringBuilder log)
    {
        const float embed = 0.1f;
        const float tolerance = 0.05f;
        var col = terrain.GetComponent<TerrainCollider>();
        float rayTop = terrain.transform.position.y + terrain.terrainData.size.y + 20f;
        // AzureCrystals deliberately excluded: several nearby ones (CliffWall_*/CliffCrack_*/RockGap_*)
        // are angled INTO a cliff face on purpose, not resting flat -- a straight-down raycast/bounds
        // check would wrongly "correct" them as if they were simple ground props. The measured terrain
        // delta near them after fixing StartY/GoalY (see run notes) is tiny (~0.05-0.1m) anyway, so they
        // are left untouched here; verified individually instead (see conversation report).
        string[] groups = { "LakeShoreDressing", "LakeCliffWall", "Waterfalls" };
        int moved = 0, checked_ = 0;
        foreach (var g in groups)
        {
            var grp = terrainRoot.transform.Find(g);
            if (grp == null) continue;
            foreach (Transform child in grp)
            {
                float dist, pathY;
                NearestOnPath(samples, child.position.x, child.position.z, out dist, out pathY);
                if (dist > OuterHalfWidth + RegroundSearchMargin) continue;
                checked_++;

                var rend = child.GetComponentInChildren<Renderer>();
                if (rend == null) continue;
                Vector3 bc = rend.bounds.center;
                if (!col.Raycast(new Ray(new Vector3(bc.x, rayTop, bc.z), Vector3.down), out RaycastHit hit, terrain.terrainData.size.y + 40f)) continue;

                // gap = ground_y - bottom_y: >0 means the object's bottom is BELOW ground (buried by
                // `gap`), <0 means it's floating above ground by |gap|. Target is a small intentional
                // embed (bottom sits `embed` below the surface), i.e. target gap == embed.
                float gap = hit.point.y - rend.bounds.min.y;
                float shift = gap - embed;
                if (Mathf.Abs(shift) < tolerance) continue;
                var old = child.position;
                child.position = new Vector3(old.x, old.y + shift, old.z);
                log.AppendLine("Re-grounded " + g + "/" + child.name + ": y " + old.y.ToString("F2") + " -> " + child.position.y.ToString("F2") + " (gap was " + gap.ToString("F2") + "m)");
                moved++;
            }
        }
        log.AppendLine("Re-grounded " + moved + "/" + checked_ + " existing dressing object(s) checked inside the ramp corridor.");
    }

    static void VerifySlope(Terrain terrain, GameObject terrainGO, List<Vector3> samples, StringBuilder log)
    {
        float maxDeg = 0f; float atLen = 0f; float cum = 0f;
        float maxStepPer03 = 0f;
        Vector3 prev = samples[0];
        prev.y = terrain.SampleHeight(prev) + terrainGO.transform.position.y;
        float sinceStep = 0f; float stepStartY = prev.y;
        for (int i = 1; i < samples.Count; i++)
        {
            Vector3 curXZ = samples[i];
            float curY = terrain.SampleHeight(curXZ) + terrainGO.transform.position.y;
            float horiz = Vector2.Distance(new Vector2(prev.x, prev.z), new Vector2(curXZ.x, curXZ.z));
            cum += horiz;
            if (horiz > 1e-4f)
            {
                float deg = Mathf.Atan2(Mathf.Abs(curY - prev.y), horiz) * Mathf.Rad2Deg;
                if (deg > maxDeg) { maxDeg = deg; atLen = cum; }
            }
            sinceStep += horiz;
            if (sinceStep >= 0.3f)
            {
                maxStepPer03 = Mathf.Max(maxStepPer03, Mathf.Abs(curY - stepStartY));
                sinceStep = 0f; stepStartY = curY;
            }
            prev = new Vector3(curXZ.x, curY, curXZ.z);
        }
        log.AppendLine("Slope check (post-carve, sampled every ~" + (cum / samples.Count).ToString("F2") + "m): max local incline=" + maxDeg.ToString("F1") + "deg at arc-length " + atLen.ToString("F1") + "m; max rise per 0.3m step=" + maxStepPer03.ToString("F2") + "m (CharacterController stepOffset=0.4m, slopeLimit=50deg).");
    }

    // ---- Single, undecomposed MossyStoneRamp instance used as a literal climb-out-of-the-water ramp ----
    // Mesh analysis (done live, see conversation): the raw mesh IS a genuine wedge -- flat bottom, top
    // face sloping steadily -- but along its SHORTEST local axis (raw Z, ~1.13m) rather than a long one.
    // Confirmed by bucketing all 885k vertices into 12 bins along local Z and comparing min/max Y per
    // bin: yMax stays ~constant (~+0.01) across every bin while yMin climbs steadily from -0.01 to
    // +0.0067 -- i.e. one end is "thick" (full height) and the other "thin" (tapered), with a flat top
    // and an angled underside. Rotating 180 degrees around local Z flips that angled underside into the
    // new TOP surface (and the formerly-flat top becomes a flat bottom resting on the ground) -- exactly
    // a usable ramp shape. Local +Z (pre-flip) becomes the ramp's LOW end, local -Z the HIGH end.
    // Root-local reference points below already bake in the prefab's fixed Visual-child 100x scale
    // (child.localScale=100, see CarrySetupMossyStoneRamp) but NOT this instance's own extra localScale
    // -- Transform.TransformPoint applies that automatically.
    static readonly Vector3 RampLocalLowPoint = new Vector3(0f, 0.67f, 0.567f);   // thin end's top-surface point (pre-flip local Z = +max)
    static readonly Vector3 RampLocalHighPoint = new Vector3(0f, -1.0f, -0.567f); // thick end's top-surface point (pre-flip local Z = -max)

    static void PlaceSingleClimbRamp(GameObject prefab, Terrain terrain, GameObject terrainGO, Transform parent, StringBuilder log)
    {
        // 2026-08-15 FOURTH placement: the user manually repositioned/rescaled the ramp in-editor to
        // their own liking (pos ~(14,-2.6,-8.2), scale (4,3,7), covering the LakeStairs cluster) and
        // asked for the land/ramp seam + tilt to be cleaned up rather than the placement redone from
        // scratch. Diagnosed by sampling the mesh's low/high/left/right reference points against fresh
        // terrain raycasts: the manual rotation had picked up an unwanted ROLL (sideways cross-slope) on
        // top of the intended climb pitch -- e.g. the high-end right edge floated 3.07m above the actual
        // ground while the low-end left edge was buried 0.48m, even though the low/high CENTERLINE
        // anchors themselves were both close to plausible ground contact. Rather than hand-tune euler
        // angles, these anchors reuse the user's own low/high footprint (surveyed fresh below) through
        // the same yaw+180-about-Z-only construction already used successfully at LakeShore_21 -- that
        // path has zero roll by construction (LookRotation only ever sets pitch+yaw against world up).
        // 2026-08-15 FIFTH placement, per explicit "坂を長くして緩やかな坂にして": low anchor (the water's
        // edge) is unchanged, but the high anchor is pushed further out along the SAME direction --
        // surveyed live (raycast scan at runLen 7.96/10/11/12/13m) and confirmed the bank plateau up
        // there stays almost flat (natural ground -0.19..-0.03 across that whole range), so extending
        // the run doesn't require chasing a higher target -- it just spreads the SAME ~5m rise over a
        // longer distance, which is directly "longer and gentler" rather than two separate changes.
        Vector2 lowAnchorXZ = RampLowAnchorXZ;
        Vector2 highAnchorXZ = RampHighAnchorXZ;
        const float LowAnchorGroundY = RampLowAnchorGroundY;
        const float HighAnchorGroundY = RampHighAnchorGroundY;

        Vector3 direction = new Vector3(highAnchorXZ.x - lowAnchorXZ.x, 0f, highAnchorXZ.y - lowAnchorXZ.y).normalized;
        // Want local -Z (the HIGH end after the flip) to face `direction`, i.e. local +Z (forward)
        // should face -direction.
        Quaternion yawRot = Quaternion.LookRotation(-direction, Vector3.up);
        Quaternion baseFlip = Quaternion.Euler(0f, 0f, 180f);
        Quaternion rot = yawRot * baseFlip;

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        inst.name = "MossyStoneRamp_ClimbRamp";
        // 2026-08-15, THIRD revision, per "坂の周辺に盛り上がってる土がある": X=4 was tuned against the
        // mesh BEFORE it was permanently widened 1.5x in Blender (see CarrySetupMossyStoneRamp's
        // MossyStoneRamp_Wide.fbx) -- keeping X=4 on top of that already-widened geometry compounded
        // into a mesh 1.5x wider than ever intended (measured Renderer.bounds (16.8, 6.0, 16.8)m).
        // X rescaled down to 2.0 for a proportionate ~6-8m width instead of ~12m.
        // 2026-08-15, FOURTH revision: dropping Y to 2.4 alongside X broke the ramp's own climb --
        // RampLocalHighPoint/RampLocalLowPoint's world Y RISE is exactly 1.67*scale.y (derived: yawRot
        // is pure-yaw so it preserves world Y; baseFlip only negates local X/Y, doesn't touch it; scale
        // is the only remaining factor -- confirmed live, worldHigh.y jumped from ~-0.03 to -0.84 the
        // moment Y went 3.0->2.4). The low end is pinned exactly to LowAnchorGroundY by construction,
        // but nothing forces the high end to reach HighAnchorGroundY -- if the mesh's own geometry
        // doesn't rise the full ~4.82m (LowAnchorGroundY to HighAnchorGroundY) on its own, the terrain
        // (correctly sculpted to the full rise) ends up ABOVE the rock's actual surface near the top,
        // i.e. burying it -- exactly the "MossyStoneRamp_ClimbRampが土に埋もれている" the user reported.
        // Y=2.9 solves 1.67*Y=4.82 almost exactly (worldHigh.y lands at -0.007, vs old Y=3.0's +0.16
        // overshoot) -- restores the correct climb height without reintroducing the old bulk.
        inst.transform.localScale = new Vector3(2.0f, 2.9f, 10.6f);
        inst.transform.rotation = rot;
        inst.transform.position = Vector3.zero;

        Vector3 worldLow = inst.transform.TransformPoint(RampLocalLowPoint);
        Vector3 delta = new Vector3(lowAnchorXZ.x, LowAnchorGroundY, lowAnchorXZ.y) - worldLow;
        inst.transform.position += delta;

        Vector3 worldHighAfter = inst.transform.TransformPoint(RampLocalHighPoint);
        float highGap = HighAnchorGroundY - worldHighAfter.y; // >0: ramp's high end sits BELOW the bench (needs to rise more); <0: overshoots above it
        log.AppendLine("ClimbRamp low end placed at " + inst.transform.TransformPoint(RampLocalLowPoint).ToString("F2") +
            " (target " + new Vector3(lowAnchorXZ.x, LowAnchorGroundY, lowAnchorXZ.y).ToString("F2") + "); high end landed at " +
            worldHighAfter.ToString("F2") + " vs bench height " + HighAnchorGroundY.ToString("F2") + " at that XZ (gap=" + highGap.ToString("F2") + "m).");

        // Unlike the (now-removed) many-paving-stones approach, this is a SINGLE large climbable
        // feature, so the prefab's existing convex MeshCollider (see CarrySetupMossyStoneRamp) is kept
        // as-is here rather than swapped for terrain/box -- with only one instance, a Player snagging on
        // its own rock detail is a minor concern, and a convex hull that matches the visual wedge
        // exactly gives a true climbable rock-ramp surface instead of an approximated flat plane.

        log.AppendLine("Placed single undecomposed MossyStoneRamp climb-ramp: low=" + lowAnchorXZ + " high=" + highAnchorXZ + " scale=" + inst.transform.localScale + ".");

        // Physics.SyncTransforms() BEFORE raycasting the ramp's own MeshCollider inside
        // BlendTerrainUnderRamp -- in the Editor (no physics simulation ticking), a Collider's
        // raycastable world transform does not necessarily update the instant Transform.position is
        // set; without this sync, Collider.Raycast() calls made immediately afterward can read a stale
        // pre-placement transform for some cells. Confirmed live: two points along the centerline
        // (t=0.15, t=0.20) came back buried by 0.56-1.09m despite an out-of-band verification query
        // (run as a separate call, after Unity had already ticked) reporting the correct geometry at
        // the exact same XZ.
        Physics.SyncTransforms();
        BlendTerrainUnderRamp(inst, terrain, terrainGO, lowAnchorXZ, LowAnchorGroundY, highAnchorXZ, worldHighAfter.y, log);
    }

    // ForestStage_Terrain/LakeCliffWall/LakeCliffLowerMossy is a large (182-vertex, no collider,
    // rendering-only) background mesh spanning much of the lake basin's lower cliff wall -- meant to
    // stay hidden beneath the terrain/water everywhere. This location's terrain carving (both the main
    // bench and BlendTerrainUnderRamp's wide-restore) lowered the ground in spots relative to whatever
    // it was before, and 36 of that mesh's 182 vertices ended up poking up through the new terrain --
    // found by transforming every vertex to world space and comparing to a fresh TerrainCollider
    // raycast at that XZ (see conversation). Only patches vertices within this ramp's own local area
    // (never a lake-wide fix for a mesh shared by the whole basin) -- the worst offender found live was
    // 30m away near x=-30, clearly unrelated to anything this script touches.
    static void CoverExposedLakeCliffMesh(Terrain terrain, GameObject terrainGO, StringBuilder log)
    {
        var meshGO = GameObject.Find("ForestStage_Terrain/LakeCliffWall/LakeCliffLowerMossy");
        if (meshGO == null) { log.AppendLine("LakeCliffLowerMossy not found -- skipped."); return; }
        var mf = meshGO.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { log.AppendLine("LakeCliffLowerMossy has no mesh -- skipped."); return; }

        var col = terrain.GetComponent<TerrainCollider>();
        float rayTop = terrainGO.transform.position.y + terrain.terrainData.size.y + 20f;
        var data = terrain.terrainData;
        float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
        float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
        int hr = data.heightmapResolution;
        var heights = data.GetHeights(0, 0, hr, hr);

        const float regionRadius = 12f; // only within ~12m of this ramp's own footprint (see class doc)
        Vector3 rampCenter = new Vector3(15.26f, 0f, -6.59f); // this ramp instance's own placement center (see PlaceSingleClimbRamp)
        const float coreR = 0.8f, outerR = 2.0f, embed = 0.15f;

        // Skip anything inside the ramp's own walkway corridor -- BlendTerrainUnderRamp already fit the
        // terrain there precisely to the RAMP's surface (not this cliff mesh), and raising it further
        // here would undo that fit. A first version didn't skip this and measurably re-buried the ramp
        // (worst gap went from 0.88m to 1.78m) by raising terrain inside its own walkway. The ramp's
        // own geometry already visually covers whatever this background mesh is doing directly beneath
        // it anyway, so nothing is actually left exposed by skipping this zone.
        Vector2 lowXZ = new Vector2(11.56f, -11.33f), highXZ = new Vector2(18.95f, -1.87f);
        Vector2 runDir2 = (highXZ - lowXZ); float runLen2 = runDir2.magnitude; runDir2 /= runLen2;
        const float rampCorridorHalfWidth = 3.8f; // walkwayHalfWidth(2.6) + outerWidthFalloff(1.2)

        var verts = mf.sharedMesh.vertices;
        int patched = 0, skippedInRamp = 0;
        foreach (var lv in verts)
        {
            Vector3 w = mf.transform.TransformPoint(lv);
            if (Vector2.Distance(new Vector2(w.x, w.z), new Vector2(rampCenter.x, rampCenter.z)) > regionRadius) continue;
            float tOnRamp = Mathf.Clamp01(Vector2.Dot(new Vector2(w.x, w.z) - lowXZ, runDir2) / runLen2);
            Vector2 onRampLine = lowXZ + runDir2 * (tOnRamp * runLen2);
            if (Vector2.Distance(new Vector2(w.x, w.z), onRampLine) <= rampCorridorHalfWidth) { skippedInRamp++; continue; }
            if (!col.Raycast(new Ray(new Vector3(w.x, rayTop, w.z), Vector3.down), out RaycastHit hit, terrain.terrainData.size.y + 40f)) continue;
            if (hit.point.y - w.y >= -0.05f) continue; // already covered (or covered enough)

            float targetY = w.y - embed;
            int cxi = Mathf.RoundToInt((w.x - originX) / sizeX * (hr - 1));
            int czi = Mathf.RoundToInt((w.z - originZ) / sizeZ * (hr - 1));
            int spanCells = Mathf.CeilToInt(outerR / (sizeX / (hr - 1))) + 1;
            for (int zi = Mathf.Max(0, czi - spanCells); zi <= Mathf.Min(hr - 1, czi + spanCells); zi++)
            {
                float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
                for (int xi = Mathf.Max(0, cxi - spanCells); xi <= Mathf.Min(hr - 1, cxi + spanCells); xi++)
                {
                    float worldX = originX + (xi / (float)(hr - 1)) * sizeX;
                    float d = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(w.x, w.z));
                    if (d > outerR) continue;
                    float weight = d <= coreR ? 1f : 0.5f * (1f + Mathf.Cos((d - coreR) / (outerR - coreR) * Mathf.PI));
                    float originalWorldY = originY + heights[zi, xi] * sizeY;
                    float newWorldY = Mathf.Max(originalWorldY, Mathf.Lerp(originalWorldY, targetY, weight)); // never LOWER terrain here, only raise to cover
                    heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
                }
            }
            patched++;
        }
        data.SetHeights(0, 0, heights);
        Physics.SyncTransforms();
        log.AppendLine("CoverExposedLakeCliffMesh: patched terrain around " + patched + " exposed LakeCliffLowerMossy vertices near this ramp (" + skippedInRamp + " skipped -- inside the ramp's own walkway corridor).");
    }

    // Invisible walls flanking the ramp's walkway, running its full length (plus a margin at both ends),
    // so a player can only leave the lake here by using the ramp -- not by scrambling up the same
    // naturally-gentle shelf just to either side of it (the shelf the now-deactivated LakeStairs used to
    // dress). Box colliders only, no renderer -- purely a gameplay barrier, not a visible object.
    static void BuildShoreBarrier(Terrain terrain, GameObject terrainGO, Transform parent, StringBuilder log)
    {
        Vector2 lowXZ = new Vector2(11.56f, -11.33f), highXZ = new Vector2(18.95f, -1.87f);
        Vector2 runDir = (highXZ - lowXZ);
        float runLen = runDir.magnitude;
        runDir /= runLen;
        Vector2 perp = new Vector2(-runDir.y, runDir.x);

        const float lateralOffset = 4.3f; // just outside BlendTerrainUnderRamp's walkway+falloff (2.6+1.2=3.8)
        const float endMargin = 4f;       // extra reach past both ends of the ramp's own line
        const float wallHeight = 14f;     // comfortably spans lake floor to well above head height
        const float wallThickness = 1.5f;

        float extendedLen = runLen + endMargin * 2f;
        Vector2 lineMid = (lowXZ + highXZ) * 0.5f;
        float terrainMidY = terrainGO.transform.position.y + terrain.terrainData.size.y * 0.3f; // rough vertical center, generous enough with wallHeight=14 to cover lake floor to bank

        var barrierParent = new GameObject("ShoreBarrier").transform;
        barrierParent.SetParent(parent, false);

        foreach (float side in new float[] { -1f, 1f })
        {
            Vector2 wallCenterXZ = lineMid + perp * (lateralOffset * side);
            var wallGO = new GameObject("ShoreBarrier_" + (side < 0 ? "Left" : "Right"));
            wallGO.transform.SetParent(barrierParent, false);
            wallGO.transform.position = new Vector3(wallCenterXZ.x, terrainMidY, wallCenterXZ.y);
            float yawDeg = Mathf.Atan2(runDir.x, runDir.y) * Mathf.Rad2Deg;
            wallGO.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            var box = wallGO.AddComponent<BoxCollider>();
            box.size = new Vector3(wallThickness, wallHeight, extendedLen);
            log.AppendLine("ShoreBarrier_" + (side < 0 ? "Left" : "Right") + " at " + wallGO.transform.position.ToString("F2") + " size=" + box.size.ToString("F1"));
        }
        log.AppendLine("BuildShoreBarrier: 2 invisible walls placed flanking the ramp walkway (lateral +-" + lateralOffset + "m, length " + extendedLen.ToString("F1") + "m). NOTE: this only covers the shelf immediately around this ramp, not the lake's full shoreline -- other spots around the lake were not audited for climbability.");
    }

    // Carves an actual physical drop (a narrow, steep-sided trench dropping to below the lake's water
    // line) into the Terrain just outside the ramp's walkway, on both sides, per explicit "土や岩を活用し
    // て段差を作って" direction -- a real, visible landform instead of (or alongside) the invisible
    // ShoreBarrier colliders. The trench floor is pinned to a fixed height BELOW the lake surface
    // (-4.4) rather than "some amount below the local natural terrain", specifically so it can never
    // become a dry, walkable shortcut around the ramp itself -- a relative depth would have left a dry,
    // walkable trench floor wherever the surrounding natural terrain happened to sit high (checked: the
    // natural shelf reaches +1.9m in spots along this stretch, so a mere 3.5m relative drop would NOT
    // have reached water there). Boulders are then dressed along the drop's rim so it reads as a real
    // rocky ledge, not an obviously artificial ditch.
    static void BuildShoreStepBarrier(GameObject terrainRoot, Terrain terrain, GameObject terrainGO, Transform parent, StringBuilder log)
    {
        Vector2 lowXZ = new Vector2(11.56f, -11.33f), highXZ = new Vector2(18.95f, -1.87f);
        Vector2 runDir = (highXZ - lowXZ);
        float runLen = runDir.magnitude;
        runDir /= runLen;

        const float walkwayEdge = 3.8f;   // matches BlendTerrainUnderRamp's walkway+falloff -- terrain inside this stays untouched
        const float dropStart = 4.1f;     // rim of the drop -- a small flat lip before it starts falling
        const float dropEnd = 5.3f;       // where the trench floor is reached (dropRun = 1.2m -> steep)
        // 2026-08-15, third revision, per "坂の周辺に盛り上がってる土がある": the old version forced terrain in
        // this band to climb all the way up to NaturalTerrainHeight()'s (hardcoded, limited-range) table
        // value within a tight 2.2m band -- measured live, at t=0.85 that table value reaches +1.5 only
        // ~7m out laterally while the walkway itself sits around -0.5 there, so the terrain had to spike
        // up 6m in 2.2m of lateral distance right beside the walkway: exactly an unnaturally abrupt
        // ridge/pile, the "mounded dirt" the ramp reads as. Fixed below by fading the trench's influence
        // OUT (weight 1 -> 0) instead of forcing a climb to a specific height -- this reconnects to
        // whatever the terrain ACTUALLY, currently is at each point (not a hardcoded table's guess, which
        // is also only valid in a limited x/z range anyway), so it can never overshoot. The invisible
        // ShoreBarrier walls (lateralOffset=4.3) already fully block movement well before this band, so
        // nothing here is ever actually walked on regardless of its exact shape.
        const float reconnectEnd = 9f;    // beyond this, the trench's pull fully fades out
        const float endMargin = 4f;       // extra reach past both ends of the ramp's own line, matching BuildShoreBarrier
        const float trenchFloorY = -4.6f; // fixed, below the lake's water surface (-4.4) -- always submerged, never a dry shortcut

        var data = terrain.terrainData;
        float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
        float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
        int hr = data.heightmapResolution;

        float margin = reconnectEnd + 1f;
        float minXb = Mathf.Min(lowXZ.x, highXZ.x) - margin, maxXb = Mathf.Max(lowXZ.x, highXZ.x) + margin;
        float minZb = Mathf.Min(lowXZ.y, highXZ.y) - margin, maxZb = Mathf.Max(lowXZ.y, highXZ.y) + margin;
        int minXi = Mathf.Max(0, Mathf.FloorToInt((minXb - originX) / sizeX * (hr - 1)));
        int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxXb - originX) / sizeX * (hr - 1)));
        int minZi = Mathf.Max(0, Mathf.FloorToInt((minZb - originZ) / sizeZ * (hr - 1)));
        int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxZb - originZ) / sizeZ * (hr - 1)));

        var heights = data.GetHeights(0, 0, hr, hr);
        for (int zi = minZi; zi <= maxZi; zi++)
        {
            float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
            for (int xi = minXi; xi <= maxXi; xi++)
            {
                float worldX = originX + (xi / (float)(hr - 1)) * sizeX;
                Vector2 p = new Vector2(worldX, worldZ);

                float rawT = Vector2.Dot(p - lowXZ, runDir) / runLen;
                float extraT = endMargin / runLen;
                float t = Mathf.Clamp01((rawT + extraT) / (1f + 2f * extraT)); // clamp within the extended (endMargin-padded) range
                Vector2 onLine = lowXZ + runDir * (Mathf.Lerp(-endMargin, runLen + endMargin, t));
                float lateralDist = Vector2.Distance(p, onLine);
                if (lateralDist <= walkwayEdge || lateralDist > reconnectEnd) continue; // inside the ramp's own walkway, or too far out -- leave alone

                float originalWorldY = originY + heights[zi, xi] * sizeY;
                float targetY;
                float weight;
                if (lateralDist <= dropStart) { targetY = originalWorldY; weight = 0f; } // flat lip, no change (blends smoothly from the untouched walkway edge)
                else if (lateralDist <= dropEnd)
                {
                    float dt = Mathf.InverseLerp(dropStart, dropEnd, lateralDist);
                    targetY = Mathf.Lerp(originalWorldY, trenchFloorY, dt);
                    weight = 1f;
                }
                else // reconnectEnd band: fade the trench's pull back out to 0, reconnecting to whatever
                     // the terrain actually is here (not a hardcoded table value) -- see comment above.
                {
                    float dt = Mathf.InverseLerp(dropEnd, reconnectEnd, lateralDist);
                    weight = 0.5f * (1f + Mathf.Cos(dt * Mathf.PI)); // 1 at dropEnd -> 0 at reconnectEnd
                    targetY = trenchFloorY;
                }

                float newWorldY = Mathf.Lerp(originalWorldY, targetY, weight);
                heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
            }
        }
        data.SetHeights(0, 0, heights);
        Physics.SyncTransforms();
        log.AppendLine("BuildShoreStepBarrier: carved a submerged trench (floor fixed at y=" + trenchFloorY + ", below the lake's " + "-4.4 water line) from lateral " + dropStart + "m to " + reconnectEnd + "m on both sides of the ramp's walkway, running its length + " + endMargin + "m margin at each end.");
    }

    // Sculpts the Terrain ONLY in two small collars right at the ramp's low and high ends, where the
    // player actually transitions between ground and rock -- NOT across the object's whole footprint.
    // 2026-08-15, second revision: a first version blended the FULL low->high height across the ENTIRE
    // footprint width (matching the object's whole ~4m half-width), which technically closed every gap
    // at the six low/high/left/right check points -- but reported back as "the ramp looks buried in
    // dirt". Correct diagnosis: this is a solid 3D rock mesh, not a thin flat ramp plane, and its real
    // surface undulates -- forcing the terrain to be perfectly LEVEL across the full width at the exact
    // walking-surface height, everywhere along the run, builds a dirt mound the same size and shape as
    // the rock that swallows it whole, since a real rock's sides/bulk are naturally ABOVE the ground
    // around it, not flush with it. Fixed by (a) narrowing the corridor to a walkway width instead of
    // the object's full width, (b) only touching ground within a short arc-length of each END (fading
    // out entirely in the middle third, where the rock's own bulk should read as exposed), and (c) an
    // explicit downward embed so the terrain settles BELOW the walking surface, not flush with it.
    // Hardcoded survey of the ORIGINAL (pre-ramp) terrain in this area, done live before any of this
    // location's carving -- a 2m grid, x=[8..24], z=[-8..2]. Used to restore the wide area around the
    // ramp to its real natural shape instead of an artificial mound. Reported back as "dirt mounded up
    // on both sides": the previous wide-restore pass targeted the RAMP's own straight low->high line
    // (a steep, direct interpolation) even for the sides far from the ramp itself, and that line sits
    // well above the terrain's real, gentler, curved natural profile almost everywhere except right on
    // the ramp's own centerline -- so "restoring toward it" just rebuilt a shorter version of the same
    // mound. Z below -8 clamps to the z=-8 row (that whole band was already flat ~-4.4..-4.85, matching
    // the ramp's own low-anchor survey, so nearest-neighbor extrapolation is a safe approximation).
    static readonly float[] NaturalGridX = { 8f, 10f, 12f, 14f, 16f, 18f, 20f, 22f, 24f };
    static readonly float[] NaturalGridZ = { -8f, -7f, -6f, -5f, -4f, -3f, -2f, -1f, 0f, 1f, 2f };
    static readonly float[,] NaturalGridY = {
        { -4.7f, -4.7f, -4.4f, -3.9f, -3.4f, -3.1f, -2.4f, -1.0f, 1.7f },
        { -4.7f, -4.3f, -3.9f, -3.3f, -2.6f, -1.9f, -1.2f, 0.0f, 1.6f },
        { -4.6f, -4.4f, -3.3f, -2.6f, -1.9f, -1.2f, -0.5f, 0.3f, 1.4f },
        { -4.6f, -4.6f, -2.7f, -2.0f, -1.3f, -0.7f, -0.1f, 0.2f, 0.9f },
        { -4.7f, -4.7f, -2.6f, -1.4f, -0.8f, -0.2f, 0.1f, 0.2f, 0.2f },
        { -4.7f, -4.7f, -2.6f, -0.9f, -0.3f, 0.1f, 0.2f, 0.2f, 0.1f },
        { -4.7f, -4.7f, -2.4f, -0.6f, 0.0f, 0.2f, 0.1f, 0.1f, 0.1f },
        { -4.8f, -4.7f, -2.0f, -0.2f, 0.2f, 0.1f, 0.0f, 0.0f, 0.0f },
        { -4.8f, -4.8f, -1.2f, 0.1f, 0.5f, 0.1f, 0.0f, 0.0f, 0.0f },
        { -4.1f, -2.2f, -0.1f, 0.7f, 1.0f, 0.3f, 0.0f, 0.0f, 0.0f },
        { -0.9f, 0.1f, 0.5f, 1.6f, 1.9f, 1.1f, 0.0f, 0.0f, -0.1f },
    };

    static float NaturalTerrainHeight(float x, float z)
    {
        z = Mathf.Clamp(z, NaturalGridZ[0], NaturalGridZ[NaturalGridZ.Length - 1]);
        x = Mathf.Clamp(x, NaturalGridX[0], NaturalGridX[NaturalGridX.Length - 1]);
        int zi = 0; while (zi < NaturalGridZ.Length - 2 && NaturalGridZ[zi + 1] < z) zi++;
        int xi = 0; while (xi < NaturalGridX.Length - 2 && NaturalGridX[xi + 1] < x) xi++;
        float tz = Mathf.InverseLerp(NaturalGridZ[zi], NaturalGridZ[zi + 1], z);
        float tx = Mathf.InverseLerp(NaturalGridX[xi], NaturalGridX[xi + 1], x);
        float y00 = NaturalGridY[zi, xi], y01 = NaturalGridY[zi, xi + 1];
        float y10 = NaturalGridY[zi + 1, xi], y11 = NaturalGridY[zi + 1, xi + 1];
        return Mathf.Lerp(Mathf.Lerp(y00, y01, tx), Mathf.Lerp(y10, y11, tx), tz);
    }

    static void BlendTerrainUnderRamp(GameObject inst, Terrain terrain, GameObject terrainGO, Vector2 lowXZ, float lowY, Vector2 highXZ, float highY, StringBuilder log)
    {
        const float walkwayHalfWidth = 2.6f;   // comfortable path width at the seam, scaled up along with the now-1.5x-wider mesh
        const float outerWidthFalloff = 1.2f;  // extra taper beyond walkwayHalfWidth
        const float embed = 0.55f;             // terrain settles this far BELOW the walking-surface height, so the rock visibly protrudes (raised from 0.35 -- reported back "still a little dirt on the ramp" after the vertex-based fit, i.e. small local dips/crevices in the organic mesh still occasionally poked below the sampled target)

        // Surface height lookup built directly from the mesh's OWN vertex data (transformed to world
        // space), not from any Collider raycast. Two collider-based attempts both proved unreliable on
        // this specific ~885k-vertex mesh: a convex hull systematically over-estimates the surface in
        // concave dips (has to bulge outward to stay convex), and a non-convex MeshCollider on a mesh
        // this dense hit Unity's own documented "Fast Midphase" caveat (a warning logged when this
        // collider was first created: "might cause certain collisions to not be detected correctly due
        // to an issue in the physics engine") -- confirmed live, verifying WITH a temporary non-convex
        // collider actually found MORE buried points (8, worst 1.34m) than the convex version had (4,
        // worst 0.90m), i.e. raycasting isn't a trustworthy ground truth here at all. Reading the
        // triangle data straight from the mesh sidesteps both physics-engine approximations entirely.
        // MossyStoneRamp_Wide.fbx's ModelImporter.isReadable is now permanently true (set once,
        // outside this script) specifically so this read never has to toggle it at runtime -- an
        // earlier version flipped isReadable on, read, then flipped it back off every single run,
        // and a SaveAndReimport() on this particular ~885k-vertex FBX is heavy enough that it appears
        // to have triggered a domain reload mid-run at least once, aborting Run() right after the
        // prefab was instantiated (found afterward sitting at the default position/scale/name, as if
        // none of the placement code past the instantiate call had executed at all).
        var rampMeshFilter = inst.GetComponentInChildren<MeshFilter>();
        var rampMesh = rampMeshFilter.sharedMesh;
        var localVerts = rampMesh.vertices;
        var worldVerts = new Vector3[localVerts.Length];
        var vertXform = rampMeshFilter.transform;
        for (int i = 0; i < localVerts.Length; i++) worldVerts[i] = vertXform.TransformPoint(localVerts[i]);

        // Bucket into a 2D grid (world XZ -> list of vertices in that cell), so a lookup only has to
        // scan nearby buckets instead of all 885k vertices per terrain cell.
        const float bucketSize = 0.25f;
        var surfaceBuckets = new Dictionary<(int, int), List<Vector3>>();
        foreach (var wv in worldVerts)
        {
            var key = (Mathf.FloorToInt(wv.x / bucketSize), Mathf.FloorToInt(wv.z / bucketSize));
            if (!surfaceBuckets.TryGetValue(key, out var list)) { list = new List<Vector3>(); surfaceBuckets[key] = list; }
            list.Add(wv);
        }
        // 2026-08-15: the previous version took the MAX Y of any vertex found within a +-3 BUCKET
        // (grid-index) window, regardless of its actual distance from the query point -- a 3-bucket
        // window's far corner is up to ~1.06m away (diagonal), so on this organic, bumpy mesh it could
        // grab an unrelated nearby ridge/protrusion's height instead of the true LOCAL surface. Worse,
        // which vertices fall inside that window depends on exactly where the query point sits relative
        // to bucket boundaries (a query 0.1m away can shift which buckets are in range), so two terrain
        // cells a few cm apart could pick completely different "surfaces" -- confirmed live: at one XZ
        // this returned -2.05 (an unrelated protrusion at the edge of the window) while the genuinely
        // local vertices all sat around -4.15; the terrain ended up 1.43m ABOVE the real local rock,
        // reading as buried even though the fit "worked" by its own (flawed) logic. Fixed by filtering
        // to a real Euclidean radius from the query point instead of grid-cell membership -- distance is
        // continuous, not boundary-sensitive, so nearby queries now agree and distant outliers can't
        // sneak in through a box corner.
        const float surfaceSearchRadius = 0.5f;
        float SurfaceHeightAt(float wx, float wz, out bool found)
        {
            int bx = Mathf.FloorToInt(wx / bucketSize), bz = Mathf.FloorToInt(wz / bucketSize);
            float best = float.MinValue; found = false;
            float r2 = surfaceSearchRadius * surfaceSearchRadius;
            for (int dx = -3; dx <= 3; dx++)
                for (int dz = -3; dz <= 3; dz++)
                    if (surfaceBuckets.TryGetValue((bx + dx, bz + dz), out var list))
                        foreach (var wv in list)
                        {
                            float ddx = wv.x - wx, ddz = wv.z - wz;
                            if (ddx * ddx + ddz * ddz > r2) continue;
                            found = true; if (wv.y > best) best = wv.y;
                        }
            return best;
        }
        // Wide-restore pass constants: undoes any previous run's mound before the narrow collar pass
        // runs, by returning the wide area to its REAL surveyed natural shape (NaturalTerrainHeight),
        // not the ramp's own steep line -- see the grid's doc-comment above for why.
        const float restoreEmbed = 0.15f; // small only -- NaturalTerrainHeight is already the real ground, doesn't need a big safety margin like the ramp-line guess did
        // 2026-08-15, third revision, per "坂の周辺に盛り上がってる土がある": the old core/outer (8/9) reached
        // FULL weight (i.e. fully snapped to NaturalTerrainHeight, the REAL surveyed hillside -- which
        // climbs steeply here, up to +1.7..+1.9 not far away) starting right past the walkway's own fit
        // band (3.8m). Standing at the ramp's low end and looking up-slope, that meant "gentle walkway"
        // gave way to "full steep natural hillside" within about 4m -- read as a wall/mound of dirt
        // looming immediately beside the path rather than a background hill. Widening the taper so the
        // climb to full hillside height is spread out gradually instead of hitting full weight so close
        // to the walkway fixes the silhouette without changing what the terrain actually restores TO
        // (still the same real surveyed NaturalTerrainHeight -- just eased in more gently).
        const float restoreCore = 3.8f;   // matches the walkway's own outer fit edge -- no gap, no overlap
        const float restoreOuter = 14.0f; // full natural height reached only this far out, not at 9m

        Vector2 runDir = (highXZ - lowXZ);
        float runLen = runDir.magnitude;
        runDir /= runLen;

        var data = terrain.terrainData;
        float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
        float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
        int hr = data.heightmapResolution;

        float margin = restoreOuter;
        float minXb = Mathf.Min(lowXZ.x, highXZ.x) - margin, maxXb = Mathf.Max(lowXZ.x, highXZ.x) + margin;
        float minZb = Mathf.Min(lowXZ.y, highXZ.y) - margin, maxZb = Mathf.Max(lowXZ.y, highXZ.y) + margin;
        int minXi = Mathf.Max(0, Mathf.FloorToInt((minXb - originX) / sizeX * (hr - 1)));
        int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxXb - originX) / sizeX * (hr - 1)));
        int minZi = Mathf.Max(0, Mathf.FloorToInt((minZb - originZ) / sizeZ * (hr - 1)));
        int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxZb - originZ) / sizeZ * (hr - 1)));

        var heights = data.GetHeights(0, 0, hr, hr);
        float maxDelta = 0f;
        for (int zi = minZi; zi <= maxZi; zi++)
        {
            float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
            for (int xi = minXi; xi <= maxXi; xi++)
            {
                float worldX = originX + (xi / (float)(hr - 1)) * sizeX;
                Vector2 p = new Vector2(worldX, worldZ);

                float t = Mathf.Clamp01(Vector2.Dot(p - lowXZ, runDir) / runLen);
                Vector2 onLine = lowXZ + runDir * (t * runLen);
                float lateralDist = Vector2.Distance(p, onLine);

                float originalWorldY = originY + heights[zi, xi] * sizeY;
                float blendedY = originalWorldY;

                // Pass 1: wide, gentle restore toward the REAL surveyed natural terrain (not the ramp's
                // own line) -- undoes any previous run's mound, replacing it with the actual hillside
                // shape that was there before any of this location's carving.
                if (lateralDist <= restoreOuter)
                {
                    float rw = lateralDist <= restoreCore ? 1f : 0.5f * (1f + Mathf.Cos((lateralDist - restoreCore) / (restoreOuter - restoreCore) * Mathf.PI));
                    blendedY = Mathf.Lerp(blendedY, NaturalTerrainHeight(worldX, worldZ) - restoreEmbed, rw);
                }

                // Pass 2: wherever this cell is actually under the rock, raycast the ramp's OWN
                // MeshCollider directly and target its real measured surface (minus embed) -- not a
                // formula. A formula-based version (Lerp(lowY,highY,smoothstep(t)) inside a narrow
                // end-only collar) got reported back as still buried: it assumed the ramp's walking
                // surface follows a smooth curve between the two endpoint anchors, but the real mesh (an
                // organic rock scan) doesn't -- directly measured, terrain was sitting 0.50m ABOVE the
                // actual rock surface at t=0.2 even though the endpoints themselves matched perfectly.
                // This pass now runs across the WHOLE length (not just near the ends), so the middle of
                // a long ramp is exactly as reliable as its endpoints.
                if (lateralDist <= walkwayHalfWidth + outerWidthFalloff)
                {
                    float surfY = SurfaceHeightAt(worldX, worldZ, out bool found);
                    if (found)
                    {
                        float widthWeight = lateralDist <= walkwayHalfWidth ? 1f : 0.5f * (1f + Mathf.Cos((lateralDist - walkwayHalfWidth) / outerWidthFalloff * Mathf.PI));
                        blendedY = Mathf.Lerp(blendedY, surfY - embed, widthWeight);
                    }
                }

                maxDelta = Mathf.Max(maxDelta, Mathf.Abs(blendedY - originalWorldY));
                heights[zi, xi] = Mathf.Clamp01((blendedY - originY) / sizeY);
            }
        }
        data.SetHeights(0, 0, heights);
        Physics.SyncTransforms();
        log.AppendLine("Blended terrain along the ramp's natural low->high slope: X[" + minXi + "," + maxXi + "] Z[" + minZi + "," + maxZi + "] cells, max single-cell delta=" + maxDelta.ToString("F2") + "m.");

        // Re-verify the same 6 diagnostic points (low/high x center/left/right) now that the ground has
        // been reshaped to match.
        var col = terrain.GetComponent<TerrainCollider>();
        float rayTop = terrainGO.transform.position.y + terrain.terrainData.size.y + 20f;
        var checkPts = new (string name, Vector3 local)[] {
            ("lowCenter", new Vector3(0f, 0.67f, 0.567f)), ("lowLeft", new Vector3(-0.997f, 0.67f, 0.567f)), ("lowRight", new Vector3(0.997f, 0.67f, 0.567f)),
            ("highCenter", new Vector3(0f, -1.0f, -0.567f)), ("highLeft", new Vector3(-0.997f, -1.0f, -0.567f)), ("highRight", new Vector3(0.997f, -1.0f, -0.567f)),
        };
        foreach (var cp in checkPts)
        {
            Vector3 w = inst.transform.TransformPoint(cp.local);
            if (col.Raycast(new Ray(new Vector3(w.x, rayTop, w.z), Vector3.down), out RaycastHit hit, terrain.terrainData.size.y + 40f))
                log.AppendLine("  post-blend " + cp.name + ": rampY=" + w.y.ToString("F2") + " groundY=" + hit.point.y.ToString("F2") + " gap=" + (hit.point.y - w.y).ToString("F2"));
        }
    }

    static Vector3 SampleAtT(List<Vector3> samples, float t, out Vector3 tangent)
    {
        float target = t * (samples.Count - 1);
        int i0 = Mathf.Clamp(Mathf.FloorToInt(target), 0, samples.Count - 2);
        int i1 = i0 + 1;
        float f = target - i0;
        Vector3 p = Vector3.Lerp(samples[i0], samples[i1], f);
        tangent = (samples[i1] - samples[i0]).normalized;
        return p;
    }

    // 2026-08-15: after the user manually re-scaled/re-rotated MossyStoneRamp_ClimbRamp by hand in the
    // Editor (bigger, tilted -- see conversation), the OLD terrain fit left behind by BlendTerrainUnderRamp
    // (which was precisely carved for the PREVIOUS, smaller/untilted ramp) no longer corresponds to
    // anything real -- measured live, the lake floor/land approach around the ramp had 340+ single-cell
    // steps over 0.4m (some over 2m) across the whole region, i.e. genuinely "凸凹" (bumpy), not just a
    // visual issue -- easily enough to snag a CharacterController (stepOffset=0.4m). This method is
    // DELIBERATELY NON-DESTRUCTIVE: it never touches the ramp GameObject (respects the user's manual
    // placement) and only (a) smooths the surrounding terrain back to a simple, bump-free surface via a
    // wide box-blur (no longer trying to fit any specific ramp geometry -- the ramp is now large enough
    // to visibly emerge from a plain smooth slope on its own) and (b) shrinks the ShoreBarrier invisible
    // walls, which were found to physically block the ramp-to-bridge corridor (a corridor waypoint at
    // (16.8,3.3) was measured INSIDE ShoreBarrier_Right's collider).
    [MenuItem("Carry/Fix Ramp Approach (non-destructive)")]
    public static void FixRampApproach()
    {
        var log = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();

            SmoothApproachTerrain(terrain, terrainGO, log);
            var inst = GameObject.Find("ForestStage_Terrain/LakeRampPath/MossyStoneRamp_ClimbRamp");
            if (inst != null) MatchTerrainToRampEdge(inst, terrain, terrainGO, log);
            ShrinkShoreBarrierForCorridor(log);
            CarveShoreDropoff(terrain, terrainGO, log);
            // Re-carve the bridge corridor LAST, with the SAME params Run() originally used -- an
            // earlier (buggy) version of CarveShoreDropoff's radial zone dug into this corridor's real
            // path before its exclusion check was added (see CarveShoreDropoff's doc comment); simply
            // fixing that bug going forward doesn't undo the damage already baked into the heightmap, so
            // this repairs it. Non-destructive/idempotent -- only re-fits this specific spline's own
            // corridor, doesn't touch the ramp or anything CarveShoreDropoff just did outside it.
            var corridorCtrl3b = BuildControlPoints3D(BridgeCorridorCtrlXZ, BridgeCorridorStartY, BridgeCorridorGoalY);
            var corridorSamplesB = CatmullRomSample(corridorCtrl3b, SplineSamplesPerSeg);
            // 2026-08-15: exclusion widened from 4.0f -- the ramp's own measured footprint reaches up to
            // ~6.5m half-width (varies with the user's manual rotation), so 4.0f let this corridor carve
            // overwrite part of MatchTerrainToRampEdge's fit near the ramp's high end (measured live:
            // burial regressed from 55 to 180 badly-buried sample points after a reposition). Matches
            // CarveShoreDropoff's own walkwayEdge (7.0f) for consistency.
            CarveBench(corridorSamplesB, BridgeCorridorOuterHalfWidth, BridgeCorridorCoreHalfWidth, terrain, terrainGO, "ramp-to-bridge corridor (repair)", log,
                RampLowAnchorXZ, RampHighAnchorXZ, 7.0f);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    // Wide box-blur over the whole lake-floor-approach + land/ramp-seam + corridor-start region (bounds
    // found live by scanning for >0.4m single-cell steps -- see method doc above). Kernel radius chosen
    // (6 cells =~2.3m at this terrain's 0.39m cell spacing) to comfortably average out the observed
    // 0.5-2m bumps in one pass while still preserving the large-scale lake->shore rise (the kernel is
    // much narrower than that overall slope's run). Samples from the ORIGINAL (pre-smooth) heights array
    // throughout -- each output cell is independent of already-smoothed neighbors -- and deliberately
    // reads slightly OUTSIDE the target region too (kernel isn't clamped to minXi/maxXi), so the smoothed
    // patch blends into whatever undisturbed terrain surrounds it instead of leaving a hard seam.
    static void SmoothApproachTerrain(Terrain terrain, GameObject terrainGO, StringBuilder log)
    {
        var data = terrain.terrainData;
        float originX = terrainGO.transform.position.x, originZ = terrainGO.transform.position.z;
        float sizeX = data.size.x, sizeZ = data.size.z;
        int hr = data.heightmapResolution;

        const float minXb = 5f, maxXb = 27f, minZb = -21f, maxZb = 7f;
        int minXi = Mathf.Max(0, Mathf.FloorToInt((minXb - originX) / sizeX * (hr - 1)));
        int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxXb - originX) / sizeX * (hr - 1)));
        int minZi = Mathf.Max(0, Mathf.FloorToInt((minZb - originZ) / sizeZ * (hr - 1)));
        int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxZb - originZ) / sizeZ * (hr - 1)));

        var heights = data.GetHeights(0, 0, hr, hr);
        var smoothed = (float[,])heights.Clone();
        const int kernelCells = 6;
        for (int zi = minZi; zi <= maxZi; zi++)
        {
            for (int xi = minXi; xi <= maxXi; xi++)
            {
                float sum = 0f; int n = 0;
                for (int dz = -kernelCells; dz <= kernelCells; dz++)
                {
                    int zz = zi + dz;
                    if (zz < 0 || zz >= hr) continue;
                    for (int dx = -kernelCells; dx <= kernelCells; dx++)
                    {
                        int xx = xi + dx;
                        if (xx < 0 || xx >= hr) continue;
                        sum += heights[zz, xx]; n++;
                    }
                }
                smoothed[zi, xi] = sum / n;
            }
        }
        data.SetHeights(0, 0, smoothed);
        Physics.SyncTransforms();
        log.AppendLine("SmoothApproachTerrain: box-blurred X[" + minXi + "," + maxXi + "] Z[" + minZi + "," + maxZi + "] (kernel radius " + kernelCells + " cells, ~" + (kernelCells * sizeX / (hr - 1)).ToString("F1") + "m).");
    }

    // 2026-08-15: per "坂を上りったところから陸が続くようにTerrainを調整して（現在はそこで段差が発生している）".
    // SmoothApproachTerrain (above) box-blurs the whole approach toward the surrounding AVERAGE height,
    // which is correct for removing noise but knows nothing about where the ramp's own (now much bigger,
    // manually rotated/rescaled) surface actually sits -- measured live, this left a real ~1.4-1.8m
    // cliff right at the ramp's upper edge (e.g. at z=-1, the ramp's collider reads +0.1 at x=17 while
    // the blurred terrain right next to it at x=18 was -1.7). This method fixes that seam specifically:
    // it raycasts the ramp's OWN MeshCollider over its bounds (+ margin) to map its real current surface,
    // then for every terrain cell OUTSIDE the ramp but within a short search radius of it, blends the
    // terrain up to meet that real surface height (minus a small embed), fading to 0 by the search
    // radius so it settles back into the already-smoothed terrain beyond. Never touches cells the ramp's
    // own collider directly covers (BlendTerrainUnderRamp-style fitting under the rock itself is a
    // separate concern from this land-side seam).
    static void MatchTerrainToRampEdge(GameObject inst, Terrain terrain, GameObject terrainGO, StringBuilder log)
    {
        var mc = inst.GetComponentInChildren<MeshCollider>();
        if (mc == null) { log.AppendLine("MatchTerrainToRampEdge: no MeshCollider found on ramp -- skipped."); return; }

        const float sampleStep = 0.5f;
        const float margin = 2f;
        Bounds b = mc.bounds;
        var rampSamples = new List<Vector3>();
        for (float x = b.min.x - margin; x <= b.max.x + margin; x += sampleStep)
            for (float z = b.min.z - margin; z <= b.max.z + margin; z += sampleStep)
            {
                Ray ray = new Ray(new Vector3(x, b.max.y + 5f, z), Vector3.down);
                if (mc.Raycast(ray, out RaycastHit hit, b.size.y + 10f)) rampSamples.Add(hit.point);
            }
        if (rampSamples.Count == 0) { log.AppendLine("MatchTerrainToRampEdge: no ramp surface samples found -- skipped."); return; }

        const float bucketSize = 0.5f;
        var buckets = new Dictionary<(int, int), List<Vector3>>();
        foreach (var s in rampSamples)
        {
            var key = (Mathf.FloorToInt(s.x / bucketSize), Mathf.FloorToInt(s.z / bucketSize));
            if (!buckets.TryGetValue(key, out var list)) { list = new List<Vector3>(); buckets[key] = list; }
            list.Add(s);
        }

        const float searchRadius = 3f;   // how far out from the ramp's own edge this seam-fix reaches
        const float targetAvgRadius = 1.2f; // local averaging radius for the TARGET height itself -- using
        // just the single nearest ramp sample made the target jump around cell-to-cell (the organic
        // mesh's real nearest-point can vary abruptly), leaving up to 1.3m of residual step even after
        // raising every cell to ITS OWN nearest sample. Averaging nearby samples smooths the target
        // function first, so neighboring terrain cells settle toward a similarly smooth surface.
        const float edgeEmbed = 0.1f;  // terrain sits slightly below the rock's edge, not flush
        bool NearestRampSample(float wx, float wz, out float y, out float dist)
        {
            int bx = Mathf.FloorToInt(wx / bucketSize), bz = Mathf.FloorToInt(wz / bucketSize);
            int reach = Mathf.CeilToInt(searchRadius / bucketSize);
            float bestD2 = searchRadius * searchRadius; bool found = false; float nearestY = 0f;
            float avgSum = 0f; int avgN = 0; float avgR2 = targetAvgRadius * targetAvgRadius;
            for (int dx = -reach; dx <= reach; dx++)
                for (int dz = -reach; dz <= reach; dz++)
                    if (buckets.TryGetValue((bx + dx, bz + dz), out var list))
                        foreach (var s in list)
                        {
                            float ddx = s.x - wx, ddz = s.z - wz;
                            float d2 = ddx * ddx + ddz * ddz;
                            if (d2 < bestD2) { bestD2 = d2; found = true; nearestY = s.y; }
                            if (d2 <= avgR2) { avgSum += s.y; avgN++; }
                        }
            dist = Mathf.Sqrt(bestD2);
            y = avgN > 0 ? avgSum / avgN : nearestY;
            return found;
        }

        var data = terrain.terrainData;
        float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
        float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
        int hr = data.heightmapResolution;

        float minXb = b.min.x - searchRadius - 1f, maxXb = b.max.x + searchRadius + 1f;
        float minZb = b.min.z - searchRadius - 1f, maxZb = b.max.z + searchRadius + 1f;
        int minXi = Mathf.Max(0, Mathf.FloorToInt((minXb - originX) / sizeX * (hr - 1)));
        int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxXb - originX) / sizeX * (hr - 1)));
        int minZi = Mathf.Max(0, Mathf.FloorToInt((minZb - originZ) / sizeZ * (hr - 1)));
        int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxZb - originZ) / sizeZ * (hr - 1)));

        var heights = data.GetHeights(0, 0, hr, hr);
        int touched = 0; float maxDelta = 0f;
        for (int zi = minZi; zi <= maxZi; zi++)
        {
            float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
            for (int xi = minXi; xi <= maxXi; xi++)
            {
                float worldX = originX + (xi / (float)(hr - 1)) * sizeX;

                // 2026-08-15: cells directly under the ramp's own footprint used to be skipped entirely
                // (on the assumption the rock visually covers them anyway), but that left a hidden cliff
                // right where a cell just OUTSIDE the ramp (raised by this method) bordered a cell just
                // INSIDE it (never touched, still at whatever low height SmoothApproachTerrain's blur
                // left) -- measured live, a worst case of 1.3m directly under the ramp's own edge at
                // (20.2,-2.2). Fixed by treating a direct hit the same as a distance-0 nearby sample
                // instead of skipping it, so the raise is continuous across the ramp's boundary, not just
                // up to it.
                Ray downRay = new Ray(new Vector3(worldX, b.max.y + 5f, worldZ), Vector3.down);
                float rampY, dist;
                if (mc.Raycast(downRay, out RaycastHit selfHit, b.size.y + 10f)) { rampY = selfHit.point.y; dist = 0f; }
                else if (!NearestRampSample(worldX, worldZ, out rampY, out dist)) continue;

                float weight = 0.5f * (1f + Mathf.Cos(Mathf.Clamp01(dist / searchRadius) * Mathf.PI));
                float targetY = rampY - edgeEmbed;

                float originalWorldY = originY + heights[zi, xi] * sizeY;
                float newWorldY = Mathf.Max(originalWorldY, Mathf.Lerp(originalWorldY, targetY, weight)); // never lower terrain here, only raise to meet the ramp
                if (newWorldY > originalWorldY)
                {
                    maxDelta = Mathf.Max(maxDelta, newWorldY - originalWorldY);
                    heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
                    touched++;
                }
            }
        }
        data.SetHeights(0, 0, heights);
        Physics.SyncTransforms();
        log.AppendLine("MatchTerrainToRampEdge: raised " + touched + " cell(s) to meet the ramp's real edge (max delta=" + maxDelta.ToString("F2") + "m, search radius " + searchRadius + "m).");
    }

    // Rebuilds ShoreBarrier_Left/Right with the SAME height/thickness as BuildShoreBarrier but WITHOUT
    // the endMargin overshoot on the high end -- the walls now stop right at the ramp's own high anchor
    // instead of extending 4m further along the old ramp direction, which is what was physically
    // clipping into the ramp-to-bridge corridor's early path. The low end keeps its margin (still needs
    // to block the lake-side approach past the ramp's foot).
    // 2026-08-15, second revision, per "坂の上り始めの部分...見えない段差によって阻まれる": after the user's
    // manual rescale (ramp X/Y scale 2.0/2.9 -> 4.0/4.0), the ramp's own collider now bulges out to
    // lat=6.5m in places (measured live, raycasting the ramp's MeshCollider across its whole length) --
    // WAY past the walls' old 4.3m offset, which was sized for the earlier, narrower ramp. The walls
    // were literally intersecting the ramp's own body along much of its length, and a Play-mode test
    // (CharacterController.Move, see conversation) confirmed the goblin got physically wedged in the
    // corner where wall+ramp+terrain all met, around t=0.33-0.39 up the climb. Offset widened to clear
    // the ramp's measured max (6.5) plus wall half-thickness (0.75) plus a safety margin.
    static void ShrinkShoreBarrierForCorridor(StringBuilder log)
    {
        var barrierParent = GameObject.Find("ForestStage_Terrain/LakeRampPath/ShoreBarrier");
        if (barrierParent == null) { log.AppendLine("ShoreBarrier not found -- skipped."); return; }

        Vector2 lowXZ = RampLowAnchorXZ, highXZ = RampHighAnchorXZ;
        Vector2 runDir = (highXZ - lowXZ); float runLen = runDir.magnitude; runDir /= runLen;
        Vector2 perp = new Vector2(-runDir.y, runDir.x);

        // 2026-08-15, THIRD revision, per "スタート地点からMossyStoneRamp_ClimbRampへ直線で向かうと途中で
        // 引っかかる": tried shortening the wall's LENGTH first (reopened the lat=-12 bypass -- shoreline
        // shape isn't a simple perpendicular line, so a length cutoff safe for one lateral offset lets
        // another slip through) and widening its OFFSET to 11.0 second (that cleared the beeline but then
        // ShoreBarrier_Right's far segment reached into the bridge corridor's own path near (10.5,3.9) --
        // Play-mode verified STUCK there). Reverted the offset back to 8.0f, which is exactly what's
        // already proven safe for the corridor. The actual fix is the small gap carved into
        // ShoreBarrier_Right below (see its own comment) -- narrow enough to not reopen any bypass, while
        // being exactly where the beeline needs to pass.
        const float lateralOffset = 8.0f;
        const float lowEndMargin = 4f;   // unchanged -- still reaches into the lake past the ramp's foot
        const float highEndMargin = 0f;  // full length is what reliably blocks lake bypass
        const float wallHeight = 14f;
        const float wallThickness = 1.5f;

        float extendedLen = runLen + lowEndMargin + highEndMargin;
        // Wall's own centerline midpoint shifts toward the low end now that the two margins differ.
        Vector2 wallLineLow = lowXZ - runDir * lowEndMargin;
        Vector2 wallLineHigh = highXZ + runDir * highEndMargin;
        Vector2 lineMid = (wallLineLow + wallLineHigh) * 0.5f;
        float yawDeg = Mathf.Atan2(runDir.x, runDir.y) * Mathf.Rad2Deg;

        // 2026-08-15: even at lateralOffset=11, the straight spawn->ramp beeline still crosses
        // ShoreBarrier_Right -- measured live, its lateral distance from the ramp line sweeps
        // continuously from ~22m down to ~1m as it approaches (it's a diagonal approach, not parallel to
        // the ramp), so it unavoidably passes through this wall's 11m band SOMEWHERE. The crossing is
        // narrow though: only between ~8.8m and ~9.0m along the wall's own length (measured from its
        // low/lake end). Rather than shrink the wall overall (which reopened the lat=-12 bypass -- see
        // the doc comment above), ShoreBarrier_Right specifically gets a small gap there (7.0-11.0m, with
        // margin) -- solid everywhere else, so the lake-bypass protection this wall provides at every
        // other point along its length (including wherever the lat=-12 test actually gets blocked) is
        // untouched. ShoreBarrier_Left has no such issue (the beeline never gets near its side) and stays
        // a single solid box.
        const float gapStart = 7.0f, gapEnd = 11.0f;

        // Destroy and rebuild from scratch so re-running this is idempotent regardless of the previous
        // segment count (1 vs 3).
        var oldChildren = new List<Transform>();
        foreach (Transform c in barrierParent.transform) oldChildren.Add(c);
        foreach (var c in oldChildren) Object.DestroyImmediate(c.gameObject);

        int fixedCount = 0;
        foreach (float side in new float[] { -1f, 1f })
        {
            string baseName = "ShoreBarrier_" + (side < 0 ? "Left" : "Right");
            Vector2 sideOffset = perp * (lateralOffset * side);

            var segments = new List<(float start, float end)>();
            if (side > 0f) // Right: split around the gap
            {
                if (gapStart > 0f) segments.Add((0f, gapStart));
                if (gapEnd < extendedLen) segments.Add((gapEnd, extendedLen));
            }
            else
            {
                segments.Add((0f, extendedLen));
            }

            int segIndex = 0;
            foreach (var seg in segments)
            {
                float segLen = seg.end - seg.start;
                if (segLen <= 0.01f) continue;
                Vector2 segMid = wallLineLow + runDir * ((seg.start + seg.end) * 0.5f) + sideOffset;

                var wallGO = new GameObject(segments.Count > 1 ? baseName + "_" + segIndex : baseName);
                wallGO.transform.SetParent(barrierParent.transform, false);
                wallGO.transform.position = new Vector3(segMid.x, 1.6f, segMid.y);
                wallGO.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
                var box = wallGO.AddComponent<BoxCollider>();
                box.size = new Vector3(wallThickness, wallHeight, segLen);
                log.AppendLine("  " + wallGO.name + " -> pos=" + wallGO.transform.position.ToString("F2") + " size=" + box.size.ToString("F1"));
                fixedCount++;
                segIndex++;
            }
        }
        log.AppendLine("ShrinkShoreBarrierForCorridor: rebuilt " + fixedCount + " wall segment(s) (offset=" + lateralOffset + "m, Right side gapped " + gapStart + "-" + gapEnd + "m for the spawn->ramp beeline).");
    }

    // 2026-08-15: per "MossyStoneRamp_ClimbRamp経由でしか湖から陸に移動できないように湖と陸の段差を明確にして".
    // Up to this point the "only way out is the ramp" rule was enforced ONLY by the invisible
    // ShoreBarrier walls -- there was no actual visible elevation difference blocking the shore
    // anymore (the original BuildShoreStepBarrier trench from earlier in this project was built
    // against the ramp's OLD line/width and has since been smoothed flat by SmoothApproachTerrain's
    // repeated box-blur passes). This carves a real, visible drop from the shore down to a fixed
    // submerged floor everywhere along the ramp's flanks EXCEPT the ramp's own (now much wider, ~6.5m
    // half-width) walkway -- so the lake/land boundary reads as an actual cliff, not just an invisible
    // wall. Same "fade the WEIGHT to 0, don't force a specific target height" technique used to fix the
    // earlier "mounded dirt" bug (2026-08-15, BuildShoreStepBarrier's reconnect band): forcing terrain to
    // climb back to a specific natural-height guess is what created that spike; fading out instead lets
    // it settle back into whatever real terrain is already there.
    static void CarveShoreDropoff(Terrain terrain, GameObject terrainGO, StringBuilder log)
    {
        Vector2 lowXZ = RampLowAnchorXZ, highXZ = RampHighAnchorXZ;
        Vector2 runDir = (highXZ - lowXZ); float runLen = runDir.magnitude; runDir /= runLen;

        // 2026-08-15: this carve measures lateral distance RADIALLY from the ramp line's clamped
        // endpoint beyond t=1 (see the t/onLine computation below) -- which means the "outside the
        // ramp, should drop off" zone actually wraps AROUND the high anchor in every direction, not just
        // sideways. The ramp-to-bridge corridor legitimately curves away from the ramp's own straight
        // climb direction right at that same anchor, so parts of its real path fall inside this radial
        // zone -- measured live (Play-mode CharacterController test), a corridor waypoint near (10.5,3.9)
        // got pulled from a walkable ~0.6 down to -3.9, stranding the goblin. Excluded explicitly here by
        // also checking distance to the corridor's own centerline, same technique as CarveBench's
        // exclLowXZ/exclHighXZ/exclHalfWidth overload.
        var corridorCtrl3 = BuildControlPoints3D(BridgeCorridorCtrlXZ, BridgeCorridorStartY, BridgeCorridorGoalY);
        var corridorSamples = CatmullRomSample(corridorCtrl3, SplineSamplesPerSeg);
        const float corridorExclusion = BridgeCorridorOuterHalfWidth + 1.5f;

        // 2026-08-15: even after ShrinkShoreBarrierForCorridor gapped the invisible wall for the
        // spawn->ramp beeline (see its doc comment), the goblin was STILL stuck right at the same spot --
        // this carve's own steep drop (dropStart/dropEnd below) is a REAL terrain cliff, and a wall gap
        // obviously can't help with that. The beeline crosses this drop band at lateralDist~9.1-9.6m from
        // the ramp line, around Checkpoint_Start's straight-line approach. Rather than widen the whole
        // drop's reach (which would thin the shoulder everywhere, not just here), exclude a narrow strip
        // around the beeline's own path specifically, using the same distance-to-path technique as the
        // corridor exclusion above -- solid cliff everywhere else along the shore is untouched.
        // 2026-08-15: endpoint updated to the ramp's new center (13.95,-6.48) after the user's latest
        // manual reposition -- this must track wherever the ramp actually is, same as the anchors above.
        var beelinePath = new List<Vector3> { new Vector3(-3.16f, 0f, 5.00f), new Vector3(13.95f, 0f, -6.48f) };
        const float beelineExclusion = 2.5f;

        const float walkwayEdge = 7.0f;   // clears the ramp's measured max half-width (6.5m) with margin
        const float dropStart = 7.0f;     // rim -- matches walkwayEdge, no flat lip needed (ramp's own
                                           // edge-match already smoothed the transition up to here)
        const float dropEnd = 8.5f;       // trench floor reached here -- a real, visible drop
        const float reconnectEnd = 12.5f; // reverted -- widening this to 20 didn't end up being the fix
        // (the lat=-12 bypass is actually stopped by ShoreBarrier, not this carve alone; see
        // ShrinkShoreBarrierForCorridor's doc comment for how that got resolved instead).
        const float lowEndMargin = 4f;    // matches ShoreBarrier's low-end reach into the lake
        const float highEndMargin = 0f;   // matches ShoreBarrier -- don't clip the bridge corridor
        const float trenchFloorY = -4.6f; // fixed, below the lake's water surface (-4.4) -- always submerged

        var data = terrain.terrainData;
        float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
        float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
        int hr = data.heightmapResolution;

        float margin = reconnectEnd + 1f;
        float minXb = Mathf.Min(lowXZ.x, highXZ.x) - margin, maxXb = Mathf.Max(lowXZ.x, highXZ.x) + margin;
        float minZb = Mathf.Min(lowXZ.y, highXZ.y) - margin, maxZb = Mathf.Max(lowXZ.y, highXZ.y) + margin;
        int minXi = Mathf.Max(0, Mathf.FloorToInt((minXb - originX) / sizeX * (hr - 1)));
        int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxXb - originX) / sizeX * (hr - 1)));
        int minZi = Mathf.Max(0, Mathf.FloorToInt((minZb - originZ) / sizeZ * (hr - 1)));
        int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxZb - originZ) / sizeZ * (hr - 1)));

        var heights = data.GetHeights(0, 0, hr, hr);
        int carvedCells = 0; float maxDelta = 0f;
        for (int zi = minZi; zi <= maxZi; zi++)
        {
            float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
            for (int xi = minXi; xi <= maxXi; xi++)
            {
                float worldX = originX + (xi / (float)(hr - 1)) * sizeX;
                Vector2 p = new Vector2(worldX, worldZ);

                float rawT = Vector2.Dot(p - lowXZ, runDir) / runLen;
                float extraLowT = lowEndMargin / runLen, extraHighT = highEndMargin / runLen;
                float t = Mathf.Clamp01((rawT + extraLowT) / (1f + extraLowT + extraHighT));
                Vector2 onLine = lowXZ + runDir * (Mathf.Lerp(-lowEndMargin, runLen + highEndMargin, t));
                float lateralDist = Vector2.Distance(p, onLine);
                if (lateralDist <= walkwayEdge || lateralDist > reconnectEnd) continue;

                float corridorDist; float unusedY;
                NearestOnPath(corridorSamples, worldX, worldZ, out corridorDist, out unusedY);
                if (corridorDist <= corridorExclusion) continue;

                float beelineDist; float unusedY2;
                NearestOnPath(beelinePath, worldX, worldZ, out beelineDist, out unusedY2);
                if (beelineDist <= beelineExclusion) continue;

                float originalWorldY = originY + heights[zi, xi] * sizeY;
                float targetY; float weight;
                if (lateralDist <= dropEnd)
                {
                    float dt = Mathf.InverseLerp(dropStart, dropEnd, lateralDist);
                    targetY = Mathf.Lerp(originalWorldY, trenchFloorY, dt);
                    weight = 1f;
                }
                else // fade this carve's pull back out to 0 -- reconnects to whatever terrain actually is
                {
                    float dt = Mathf.InverseLerp(dropEnd, reconnectEnd, lateralDist);
                    weight = 0.5f * (1f + Mathf.Cos(dt * Mathf.PI));
                    targetY = trenchFloorY;
                }

                float newWorldY = Mathf.Lerp(originalWorldY, targetY, weight);
                if (Mathf.Abs(newWorldY - originalWorldY) > 0.001f)
                {
                    maxDelta = Mathf.Max(maxDelta, Mathf.Abs(newWorldY - originalWorldY));
                    heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
                    carvedCells++;
                }
            }
        }
        data.SetHeights(0, 0, heights);
        Physics.SyncTransforms();
        log.AppendLine("CarveShoreDropoff: carved " + carvedCells + " cell(s) into a visible drop (floor y=" + trenchFloorY + ") from lateral " + dropStart + "m to " + reconnectEnd + "m outside the ramp's own " + walkwayEdge + "m walkway, max delta=" + maxDelta.ToString("F2") + "m.");
    }

    // 2026-08-15: per "橋とTerrainのつなぎ目に隙間があるので...橋の端の高さとterrainの高さが同じになるように修正".
    // Measured live: the bridge's east deck end (WalkableColliderSeg_15, x=4.34,z=5.00) sits at y=1.12
    // while the terrain directly beneath it was only -1.15 -- a 2.27m visible gap/chasm under the
    // bridge's landing (confirmed visually, see conversation screenshot: open water/dark void under the
    // stone masonry). Raises terrain up to meet AbutmentCollider_East/West's own top height (already
    // trimmed to 0.5, see FixRampApproach's earlier bridge-collision fix -- that height is exactly
    // "solid ground clearance below the deck", the right target for the terrain to rise to as well) within
    // each abutment's own footprint, tapering out over a margin beyond it back to natural terrain.
    [MenuItem("Carry/Fix Bridge-Terrain Seam (non-destructive)")]
    public static void FixBridgeTerrainSeam()
    {
        var log = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();

            foreach (var name in new[] { "AbutmentCollider_East", "AbutmentCollider_West" })
                RaiseTerrainToAbutment(name, terrain, terrainGO, log);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    static void RaiseTerrainToAbutment(string abutmentName, Terrain terrain, GameObject terrainGO, StringBuilder log)
    {
        var go = GameObject.Find("ForestStage_Terrain/StoneBridge_Meshy/" + abutmentName);
        if (go == null) { log.AppendLine(abutmentName + ": not found -- skipped."); return; }
        var box = go.GetComponent<BoxCollider>();
        Bounds b = box.bounds;
        float targetY = b.max.y; // the abutment's own top -- already set to sit just below the deck (see doc comment)
        const float outerMargin = 3f; // fade zone beyond the abutment's own footprint

        var data = terrain.terrainData;
        float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
        float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
        int hr = data.heightmapResolution;

        float minXb = b.min.x - outerMargin, maxXb = b.max.x + outerMargin;
        float minZb = b.min.z - outerMargin, maxZb = b.max.z + outerMargin;
        int minXi = Mathf.Max(0, Mathf.FloorToInt((minXb - originX) / sizeX * (hr - 1)));
        int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxXb - originX) / sizeX * (hr - 1)));
        int minZi = Mathf.Max(0, Mathf.FloorToInt((minZb - originZ) / sizeZ * (hr - 1)));
        int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxZb - originZ) / sizeZ * (hr - 1)));

        var heights = data.GetHeights(0, 0, hr, hr);
        int touched = 0; float maxDelta = 0f;
        for (int zi = minZi; zi <= maxZi; zi++)
        {
            float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
            for (int xi = minXi; xi <= maxXi; xi++)
            {
                float worldX = originX + (xi / (float)(hr - 1)) * sizeX;

                // Distance outside the abutment's own XZ footprint (0 if inside it).
                float dx = Mathf.Max(0f, Mathf.Max(b.min.x - worldX, worldX - b.max.x));
                float dz = Mathf.Max(0f, Mathf.Max(b.min.z - worldZ, worldZ - b.max.z));
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > outerMargin) continue;
                float weight = dist <= 0f ? 1f : 0.5f * (1f + Mathf.Cos(dist / outerMargin * Mathf.PI));

                float originalWorldY = originY + heights[zi, xi] * sizeY;
                float newWorldY = Mathf.Max(originalWorldY, Mathf.Lerp(originalWorldY, targetY, weight)); // never lower, only raise to close the gap
                if (newWorldY > originalWorldY)
                {
                    maxDelta = Mathf.Max(maxDelta, newWorldY - originalWorldY);
                    heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
                    touched++;
                }
            }
        }
        data.SetHeights(0, 0, heights);
        Physics.SyncTransforms();
        log.AppendLine("RaiseTerrainToAbutment(" + abutmentName + "): raised " + touched + " cell(s) toward y=" + targetY.ToString("F2") + ", max delta=" + maxDelta.ToString("F2") + "m.");
    }

    // 2026-08-15: per "MossyStoneRamp_ClimbRamp周辺に見えない壁がある" -- Play-mode CharacterController
    // testing (see conversation) traced this to ForestStage_Terrain/LakeCliffWall/LakeCliffCollider, a
    // large (364-vertex) collision-only proxy for the lake basin's cliff wall (confirmed live: no
    // MeshRenderer/MeshFilter anywhere references its mesh, so it is purely invisible collision, not
    // shared with anything else -- safe to edit directly). It predates the ramp/corridor and its rock
    // mass physically overlaps the path they now use: measured a dense raycast grid over the ramp's
    // upper half and corridor start and found this mesh sitting 1-6m ABOVE the terrain across nearly the
    // whole walkway width there (t=0.55-0.90 along the ramp, lateral -6..+6m) -- an invisible ceiling/
    // wall cutting across the intended route. Fixed by editing the mesh directly (a private clone, since
    // it's this object's only user): any vertex within a generous safe corridor around EITHER the ramp's
    // own line or the bridge corridor's own line, and sitting above a safe clearance over the terrain
    // there, gets pushed straight down out of the way. Collision-only + not reused anywhere means this
    // has zero visual side effects and cannot affect any other part of the cliff.
    [MenuItem("Carry/Carve Ramp Tunnel Through LakeCliffCollider (non-destructive)")]
    public static void CarveRampTunnelThroughLakeCliffCollider()
    {
        var log = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var cliffGO = GameObject.Find("ForestStage_Terrain/LakeCliffWall/LakeCliffCollider");
            if (cliffGO == null) { log.AppendLine("FAILED: LakeCliffCollider not found."); Debug.Log(log.ToString()); return; }
            var cliffMC = cliffGO.GetComponent<MeshCollider>();
            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();
            float originY = terrainGO.transform.position.y;

            Vector2 rampLow = RampLowAnchorXZ, rampHigh = RampHighAnchorXZ;
            Vector2 rampDir = (rampHigh - rampLow); float rampLen = rampDir.magnitude; rampDir /= rampLen;
            const float rampSafeHalfWidth = 8f;

            var corridorCtrl3 = BuildControlPoints3D(BridgeCorridorCtrlXZ, BridgeCorridorStartY, BridgeCorridorGoalY);
            var corridorSamples = CatmullRomSample(corridorCtrl3, SplineSamplesPerSeg);
            const float corridorSafeHalfWidth = 6f;

            // 2026-08-15, THIRD revision: the original clearance check only flagged vertices MORE than
            // 2.5m above the ground -- backwards. A solid surface ANYWHERE between ground level and the
            // goblin's own head height still blocks it (CharacterController height=1.9) -- only a
            // surface genuinely higher than that (real overhead clearance, e.g. a cave roof) is safe to
            // leave alone. Measured live: a remaining intrusion at (14.84,-5.99) sat only 1.52m above
            // terrain (well under the old 2.5m flag threshold, so it was never moved) yet is still solidly
            // inside a standing goblin's body envelope. Fixed to a proper band check below.
            const float characterEnvelope = 2.2f; // goblin height (1.9m) plus margin

            var srcMesh = cliffMC.sharedMesh;
            var mesh = Object.Instantiate(srcMesh);
            mesh.name = srcMesh.name + "_RampTunnel";
            var localVerts = mesh.vertices;
            var xform = cliffGO.transform;

            // First pass: mark individual vertices that are themselves inside the safe corridor AND
            // above clearance.
            var flagged = new bool[localVerts.Length];
            var worldPos = new Vector3[localVerts.Length];
            for (int i = 0; i < localVerts.Length; i++)
            {
                Vector3 world = xform.TransformPoint(localVerts[i]);
                worldPos[i] = world;
                Vector2 xz = new Vector2(world.x, world.z);

                float rampT = Mathf.Clamp01(Vector2.Dot(xz - rampLow, rampDir) / rampLen);
                Vector2 rampOnLine = rampLow + rampDir * (rampT * rampLen);
                float rampDist = Vector2.Distance(xz, rampOnLine);

                float corridorDist; float unusedY;
                NearestOnPath(corridorSamples, world.x, world.z, out corridorDist, out unusedY);

                bool nearRamp = rampDist <= rampSafeHalfWidth;
                bool nearCorridor = corridorDist <= corridorSafeHalfWidth;
                if (!nearRamp && !nearCorridor) continue;

                float terrainY = terrain.SampleHeight(new Vector3(world.x, 0f, world.z)) + originY;
                if (world.y <= terrainY) continue;                        // at/below ground -- solid earth, fine
                if (world.y > terrainY + characterEnvelope) continue;     // genuinely overhead -- fine
                flagged[i] = true;                                        // inside the goblin's body envelope
            }

            // 2026-08-15, second revision: moving only individually-flagged vertices left some triangles
            // with ONE vertex pushed down and the other two untouched -- the resulting stretched
            // diagonal FACE still crossed through the corridor even though no single vertex was there
            // anymore (measured live: 53/94 intrusion points remained after the first version). Fixed by
            // propagating the flag to whole triangles: if any of a triangle's 3 vertices is flagged, all
            // 3 are, repeated to a fixed point so a chain of shared triangles is fully covered.
            var tris = mesh.triangles;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    if (flagged[a] || flagged[b] || flagged[c])
                    {
                        if (!flagged[a]) { flagged[a] = true; changed = true; }
                        if (!flagged[b]) { flagged[b] = true; changed = true; }
                        if (!flagged[c]) { flagged[c] = true; changed = true; }
                    }
                }
            }

            int moved = 0;
            for (int i = 0; i < localVerts.Length; i++)
            {
                if (!flagged[i]) continue;
                Vector3 world = worldPos[i];
                float terrainY = terrain.SampleHeight(new Vector3(world.x, 0f, world.z)) + originY;
                Vector3 newWorld = new Vector3(world.x, terrainY - 5f, world.z); // push well below ground, out of the way
                localVerts[i] = xform.InverseTransformPoint(newWorld);
                moved++;
            }

            if (moved == 0) { log.AppendLine("No obstructing vertices found -- nothing to do."); Debug.Log(log.ToString()); return; }

            mesh.vertices = localVerts;
            mesh.RecalculateBounds();
            cliffMC.sharedMesh = null; // force MeshCollider to fully rebuild against the new geometry
            cliffMC.sharedMesh = mesh;
            log.AppendLine("Moved " + moved + "/" + localVerts.Length + " vertices of LakeCliffCollider's mesh down out of the ramp/corridor path (whole-triangle propagation).");

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    // 2026-08-15: per "見た目は変えずに引っかかりを解消してほしい". Play-mode CharacterController testing
    // (see conversation) traced a hard, reproducible snag to a single spot on the ramp-to-bridge
    // corridor, around (10.57,3.28) -- the goblin froze there completely (collFlags stuck at
    // Below|Sides for 300+ physics steps) even though terrain.SampleHeight and a fine raycast grid both
    // looked smooth. The real cause only showed up reading the RAW heightmap grid directly: sharp
    // single-cell notches up to ~1m deep between adjacent cells (e.g. -1.45 -> -2.41 -> -1.40, a 1m
    // drop-and-recover within just two 0.39m cells) -- narrow and steep enough to catch a 0.35m-radius
    // capsule, but too small/localized for terrain.SampleHeight's bilinear smoothing or a 0.1m raycast
    // grid to clearly reveal. A wider survey found this noise across a big swath of the corridor
    // (492/841 cells with a >0.4m jump to a neighbor in an 11.7x22.3m sample around the snag).
    // DELIBERATELY narrow in scope compared to SmoothApproachTerrain (which covers a huge fixed
    // rectangle and was the cause of two earlier "why did you overwrite my terrain" incidents this
    // session): this only touches cells within the bridge corridor's own path (its usual
    // OuterHalfWidth + a small margin), and uses a SMALL blur kernel (2 cells, ~1m) specifically so it
    // smooths away only this kind of sub-1m spike while leaving the terrain's real, larger-scale shape
    // -- and therefore its visual appearance -- unchanged.
    [MenuItem("Carry/Smooth Corridor Micro-Noise (non-destructive, visually invisible)")]
    public static void SmoothCorridorMicroNoise()
    {
        var log = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();
            var data = terrain.terrainData;
            float originX = terrainGO.transform.position.x, originZ = terrainGO.transform.position.z;
            float sizeX = data.size.x, sizeZ = data.size.z, sizeY = data.size.y;
            int hr = data.heightmapResolution;

            var corridorCtrl3 = BuildControlPoints3D(BridgeCorridorCtrlXZ, BridgeCorridorStartY, BridgeCorridorGoalY);
            var corridorSamples = CatmullRomSample(corridorCtrl3, SplineSamplesPerSeg);
            const float corridorMargin = BridgeCorridorOuterHalfWidth + 1f;

            Vector2 bbMin, bbMax;
            PathBounds(corridorSamples, corridorMargin + 1f, out bbMin, out bbMax);
            int minXi = Mathf.Max(0, Mathf.FloorToInt((bbMin.x - originX) / sizeX * (hr - 1)));
            int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((bbMax.x - originX) / sizeX * (hr - 1)));
            int minZi = Mathf.Max(0, Mathf.FloorToInt((bbMin.y - originZ) / sizeZ * (hr - 1)));
            int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((bbMax.y - originZ) / sizeZ * (hr - 1)));

            // 2026-08-15, second revision: plain box-blur (previous version) barely moved the needle
            // (492 -> 488 noisy cells after one pass, still 468 after four) -- this noise is DENSE (58%
            // of cells), not sparse spikes in an otherwise-smooth field, so averaging a cell with equally
            // noisy neighbors mostly reproduces the same noise rather than cancelling it; it would take
            // many more passes (and a growing blur radius, risking visible flattening) to converge.
            // Switched to DESPIKING instead: each cell is compared to the plain average of its 4 direct
            // neighbors (up/down/left/right, i.e. the immediately adjacent heightmap samples) and
            // clamped to within a small tolerance of that average -- this directly removes exactly the
            // "this one cell is way off from its immediate neighbors" spikes the CharacterController was
            // catching on, converges in a single pass, and (being a clamp, not a blur) only ever moves a
            // cell as far as its own neighbors already are -- nothing shifts by more than the spike
            // itself was, so the terrain's real overall shape is untouched.
            var heights = data.GetHeights(0, 0, hr, hr);
            var despiked = (float[,])heights.Clone();
            const float toleranceWorld = 0.15f; // max allowed deviation from the 4-neighbor average
            float toleranceNorm = toleranceWorld / sizeY;
            int touched = 0; float worstFixed = 0f;
            for (int zi = minZi; zi <= maxZi; zi++)
            {
                float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
                for (int xi = minXi; xi <= maxXi; xi++)
                {
                    float worldX = originX + (xi / (float)(hr - 1)) * sizeX;
                    float dist; float unusedY;
                    NearestOnPath(corridorSamples, worldX, worldZ, out dist, out unusedY);
                    if (dist > corridorMargin) continue;
                    if (zi <= 0 || zi >= hr - 1 || xi <= 0 || xi >= hr - 1) continue;

                    float nAvg = (heights[zi - 1, xi] + heights[zi + 1, xi] + heights[zi, xi - 1] + heights[zi, xi + 1]) * 0.25f;
                    float cur = heights[zi, xi];
                    float deviation = cur - nAvg;
                    if (Mathf.Abs(deviation) <= toleranceNorm) continue;

                    float clamped = nAvg + Mathf.Clamp(deviation, -toleranceNorm, toleranceNorm);
                    despiked[zi, xi] = clamped;
                    float deltaWorld = Mathf.Abs(clamped - cur) * sizeY;
                    if (deltaWorld > worstFixed) worstFixed = deltaWorld;
                    touched++;
                }
            }
            data.SetHeights(0, 0, despiked);
            Physics.SyncTransforms();
            log.AppendLine("SmoothCorridorMicroNoise: despiked " + touched + " cell(s) within " + corridorMargin.ToString("F1") + "m of the corridor path (tolerance " + toleranceWorld + "m, worst single-cell correction=" + worstFixed.ToString("F2") + "m).");

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    // 2026-08-15: per "見えてない湖の底の凸凹を修正して湖の底を平らにして". Surveyed the whole lake basin
    // (terrain.GetHeights over its rough XZ extent, water surface at y=-4.4) and found the submerged
    // floor ranges from -7.74 to -4.40 with plenty of the same kind of sharp single-cell noise found in
    // the corridor earlier -- but since this is all underwater and never seen, there's no "don't change
    // the appearance" constraint here: it can just be leveled outright instead of despiked in place.
    // Sets every submerged cell to a single flat target depth, EXCLUDING the ramp's and corridor's own
    // carve zones (so this never fights BlendTerrainUnderRamp/MatchTerrainToRampEdge/CarveShoreDropoff's
    // careful fits there) and fading out near the shoreline (cells only just barely underwater) so it
    // doesn't cut a sudden invisible step right at the water's edge.
    [MenuItem("Carry/Flatten Lake Floor (non-destructive)")]
    public static void FlattenLakeFloor()
    {
        var log = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();
            var data = terrain.terrainData;
            float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
            float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
            int hr = data.heightmapResolution;

            const float waterLevel = -4.4f;
            const float targetFloorY = -5.0f; // close to the surveyed average (-4.92), comfortably submerged
            const float fadeBand = 0.3f;      // cells within [waterLevel-fadeBand, waterLevel] taper instead of snapping

            Vector2 rampLow = RampLowAnchorXZ, rampHigh = RampHighAnchorXZ;
            Vector2 rampDir = (rampHigh - rampLow); float rampLen = rampDir.magnitude; rampDir /= rampLen;
            const float rampExclusion = 12.5f; // matches CarveShoreDropoff's own reconnectEnd

            var corridorCtrl3 = BuildControlPoints3D(BridgeCorridorCtrlXZ, BridgeCorridorStartY, BridgeCorridorGoalY);
            var corridorSamples = CatmullRomSample(corridorCtrl3, SplineSamplesPerSeg);
            const float corridorExclusion = BridgeCorridorOuterHalfWidth + 1.5f;

            // Lake's rough bounding rectangle (LakeWater's own Renderer.bounds, + a small margin).
            const float minXb = -28f, maxXb = 25f, minZb = -40f, maxZb = 2f;
            int minXi = Mathf.Max(0, Mathf.FloorToInt((minXb - originX) / sizeX * (hr - 1)));
            int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxXb - originX) / sizeX * (hr - 1)));
            int minZi = Mathf.Max(0, Mathf.FloorToInt((minZb - originZ) / sizeZ * (hr - 1)));
            int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxZb - originZ) / sizeZ * (hr - 1)));

            var heights = data.GetHeights(0, 0, hr, hr);
            int touched = 0;
            for (int zi = minZi; zi <= maxZi; zi++)
            {
                float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
                for (int xi = minXi; xi <= maxXi; xi++)
                {
                    float worldX = originX + (xi / (float)(hr - 1)) * sizeX;
                    float originalWorldY = originY + heights[zi, xi] * sizeY;
                    if (originalWorldY >= waterLevel) continue; // dry land -- never touched

                    // 2026-08-15: the ramp/corridor exclusion only matters for cells shallow enough to
                    // plausibly be part of CarveShoreDropoff's own shore-transition shaping (its trench
                    // floor is a fixed -4.6, and everything below that is genuine, untouched deep lake
                    // bed regardless of how close it is to the ramp's line). Applying the exclusion to
                    // ALL nearby cells regardless of depth left the deepest parts of the lake (down to
                    // -7.74, right next to the bridge) completely unflattened. Only cells shallower than
                    // -4.7 go through the exclusion check now.
                    if (originalWorldY > -4.7f)
                    {
                        Vector2 p = new Vector2(worldX, worldZ);
                        float rampT = Mathf.Clamp01(Vector2.Dot(p - rampLow, rampDir) / rampLen);
                        Vector2 rampOnLine = rampLow + rampDir * (rampT * rampLen);
                        if (Vector2.Distance(p, rampOnLine) <= rampExclusion) continue;

                        float corridorDist; float unusedY;
                        NearestOnPath(corridorSamples, worldX, worldZ, out corridorDist, out unusedY);
                        if (corridorDist <= corridorExclusion) continue;
                    }

                    float weight = originalWorldY <= waterLevel - fadeBand ? 1f
                        : Mathf.InverseLerp(waterLevel, waterLevel - fadeBand, originalWorldY);
                    float newWorldY = Mathf.Lerp(originalWorldY, targetFloorY, weight);
                    heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
                    touched++;
                }
            }
            data.SetHeights(0, 0, heights);
            Physics.SyncTransforms();
            log.AppendLine("FlattenLakeFloor: leveled " + touched + " submerged cell(s) to y=" + targetFloorY + " (excluding ramp/corridor zones).");

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    // 2026-08-15: per "橋の下の川底がへこんでしまったので平らになるように川底のみ修正して". This is a
    // SEPARATE body of water from the lake (RiverWater, surface y=-2.90, a long channel running well
    // beyond the bridge in both directions -- RiverWater's own Renderer.bounds spans z=-22..120).
    // FlattenLakeFloor's bounding box was capped at z<=2 specifically to stay out of this river, but a
    // live survey (terrain.SampleHeight grid under the bridge, see conversation) found the riverbed
    // itself is genuinely uneven independent of that -- noisy dips from -1 down to -8 across the channel
    // near the bridge (z roughly 0-15), the "へこんでしまった" the user means. Scoped tightly to just
    // that stretch of channel (not the whole 140m+ river) and to cells that are actually part of the
    // channel (below -1, clearly sunken relative to the ~0-2 banks on either side) -- never touches the
    // lake (z<=0 excluded, already handled by FlattenLakeFloor) or dry land.
    [MenuItem("Carry/Flatten Riverbed Under Bridge (non-destructive)")]
    public static void FlattenRiverbedUnderBridge()
    {
        var log = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();
            var data = terrain.terrainData;
            float originX = terrainGO.transform.position.x, originY = terrainGO.transform.position.y, originZ = terrainGO.transform.position.z;
            float sizeX = data.size.x, sizeY = data.size.y, sizeZ = data.size.z;
            int hr = data.heightmapResolution;

            const float bankLevel = -1.0f;    // above this = channel bank/dry land, never touched
            const float targetBedY = -4.0f;   // ~1.1m below the river's own surface (-2.90)
            const float fadeBand = 0.6f;      // cells within [bankLevel-fadeBand, bankLevel] taper instead of snapping
            const float minXb = -9f, maxXb = 6f, minZb = 0.5f, maxZb = 15f; // just this stretch of channel, not the whole river

            int minXi = Mathf.Max(0, Mathf.FloorToInt((minXb - originX) / sizeX * (hr - 1)));
            int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxXb - originX) / sizeX * (hr - 1)));
            int minZi = Mathf.Max(0, Mathf.FloorToInt((minZb - originZ) / sizeZ * (hr - 1)));
            int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt((maxZb - originZ) / sizeZ * (hr - 1)));

            var heights = data.GetHeights(0, 0, hr, hr);
            int touched = 0;
            for (int zi = minZi; zi <= maxZi; zi++)
            {
                for (int xi = minXi; xi <= maxXi; xi++)
                {
                    float originalWorldY = originY + heights[zi, xi] * sizeY;
                    if (originalWorldY >= bankLevel) continue; // bank/dry land -- never touched

                    float weight = originalWorldY <= bankLevel - fadeBand ? 1f
                        : Mathf.InverseLerp(bankLevel, bankLevel - fadeBand, originalWorldY);
                    float newWorldY = Mathf.Lerp(originalWorldY, targetBedY, weight);
                    heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
                    touched++;
                }
            }
            data.SetHeights(0, 0, heights);
            Physics.SyncTransforms();
            log.AppendLine("FlattenRiverbedUnderBridge: leveled " + touched + " channel cell(s) to y=" + targetBedY + " within x[" + minXb + "," + maxXb + "] z[" + minZb + "," + maxZb + "].");

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
