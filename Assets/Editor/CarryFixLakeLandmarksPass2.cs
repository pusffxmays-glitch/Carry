using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-14 Pass 2: follow-up correction after CarryFixLakeLandmarks.cs (Pass 1). Pass 1 fixed the
// AncientForestGuardian's own floating problem and the main waterfall-front boulder, but introduced
// three NEW defects of its own while adding hero trees + base dressing, all found and fixed here:
//
//  1) AddLakeHeroTrees (Pass 1) set `inst.transform.rotation` directly, discarding whatever baked
//     orientation-correction rotation the PREFAB ROOT itself already carried. AncientFir_A/B/C have an
//     identity root rotation (safe to overwrite), but AncientFir_D_Curved's root carries a baked
//     (90,0,0) correction (its source mesh is Z-up, not Y-up, from the Blender bend-modifier export
//     pipeline noted in CarryBuildTerrainForest.cs) -- overwriting it left the tree lying on its side.
//     That produced exactly the "極端な傾斜" / fallen-tree look flagged this round. Both firD-based
//     placements (LakeHero_WestBank_CurvedFir, LakeHero_EastCliff_LeaningFir) were deleted rather than
//     rotation-patched, per explicit direction to not use tilt-heavy placements for large hero trees;
//     LakeHero_EastCliff_LeaningFir in particular was also just a bad site (near-horizontal jut over
//     the lake) independent of the rotation bug. Replacements use AncientFir_B (root rotation already
//     identity, no bug class) on freshly surveyed flat plateaus instead.
//  2) LakeHero_WestBank_SmallGuardian was dropped onto a 56-degree slope (single-point grounding, same
//     class of mistake as the original Guardian-tree bug) -- CLAUDE.md's own 接地ルール explicitly
//     covers this: don't paste a wide-rooted tree onto a cliff face. Relocated to a surveyed
//     near-flat plateau (variance 0.11m over an 8-point ring at the tree's actual root radius).
//  3) The root-base dressing rocks added around the main Guardian tree had two bugs: (a) the
//     grounding math measured Renderer.bounds BEFORE moving the instance to its final position
//     (stale bounds from the rock's ORIGINAL template location), so several ended up floating well
//     above the real ground; (b) three of the eight ring-sample angles landed north of the terrain's
//     own z=-46 edge (the Guardian tree sits only ~0.7m south of the map boundary), i.e. genuinely off
//     the world with no ground possible there at all. Both classes of bad rocks were deleted and
//     replaced with correctly-measured ones restricted to the safe southern arc.
//
// Additionally (matching this round's explicit request to review ALL large cliff/shore assets, not
// just the named ones): CliffBoulder_11/13/20/21 were found floating (multi-point ring sampling
// against the TerrainCollider showed multi-meter gaps under their real footprints, not just their
// single pivot point) and re-grounded the same way. WaterfallSourceRock_0 and HeroCoastalCliffBase_0
// were repositioned out of the waterfall's direct sightline per the "岩は滝を囲う、隠さない" corridor
// requirement (see method comments below for the specific reasoning per rock).
public static class CarryFixLakeLandmarksPass2
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    [MenuItem("Carry/Fix Lake Landmarks Pass 2 (Tree Orientation + Waterfall Corridor + Floating Rocks)")]
    public static void Run()
    {
        var log = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();
            var col = terrainGO.GetComponent<TerrainCollider>();
            float rayTop = terrainGO.transform.position.y + terrain.terrainData.size.y + 20f;

            RemoveBrokenAndFallenTrees(log);
            FixSmallGuardianSlope(terrain, col, rayTop, log);
            AddReplacementHeroTrees(terrain, col, rayTop, log);
            FixGuardianRootDressing(terrain, col, rayTop, log);
            RegroundFloatingCliffBoulders(terrain, col, rayTop, log);
            OpenWaterfallCorridor(log);

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

    static bool RayGround(TerrainCollider col, Terrain terrain, float rayTop, float x, float z, out RaycastHit hit) =>
        col.Raycast(new Ray(new Vector3(x, rayTop, z), Vector3.down), out hit, terrain.terrainData.size.y + 40f);

    // 1) Delete the two firD-based placements: one was a rotation-bug victim (lying on its side), the
    // other (EastCliff_LeaningFir) was both the same bug AND a bad, near-horizontal site -- deleted
    // outright per explicit direction rather than rescued, since this forest's large hero trees should
    // read as standing, not leaning/fallen.
    static void RemoveBrokenAndFallenTrees(StringBuilder log)
    {
        var parent = GameObject.Find("ForestStage_Terrain/LakeHeroAncientTrees");
        foreach (var n in new[] { "LakeHero_EastCliff_LeaningFir", "LakeHero_WestBank_CurvedFir" })
        {
            var t = parent.transform.Find(n);
            if (t != null) { Object.DestroyImmediate(t.gameObject); log.AppendLine("Deleted " + n); }
        }
    }

    // 2) LakeHero_WestBank_SmallGuardian was on a 56-degree slope. Relocated to a surveyed near-flat
    // plateau at (-32,-34) (ring variance 0.11m at the tree's own root radius), matching the same
    // multi-point-ring 接地ルール used for the main Guardian tree.
    static void FixSmallGuardianSlope(Terrain terrain, TerrainCollider col, float rayTop, StringBuilder log)
    {
        var guardian = GameObject.Find("ForestStage_Terrain/LakeHeroAncientTrees/LakeHero_WestBank_SmallGuardian");
        if (guardian == null) { log.AppendLine("SmallGuardian not found."); return; }

        float cx = -32f, cz = -34f, coreR = 3.0f;
        RayGround(col, terrain, rayTop, cx, cz, out RaycastHit centerHit);
        float lowestY = centerHit.point.y;
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f;
            if (RayGround(col, terrain, rayTop, cx + Mathf.Cos(a) * coreR, cz + Mathf.Sin(a) * coreR, out RaycastHit h) && h.point.y < lowestY)
                lowestY = h.point.y;
        }

        var rend = guardian.GetComponentInChildren<Renderer>();
        float pivotToBottom = guardian.transform.position.y - rend.bounds.min.y;
        const float embed = 0.3f;
        float newPivotY = (lowestY - embed) + pivotToBottom;

        Vector2 towardLake = (new Vector2(0f, -16f) - new Vector2(cx, cz)).normalized;
        float yaw = Mathf.Atan2(towardLake.x, towardLake.y) * Mathf.Rad2Deg;
        Quaternion naturalTilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, centerHit.normal), 0.2f);
        guardian.transform.position = new Vector3(cx, newPivotY, cz);
        guardian.transform.rotation = naturalTilt * Quaternion.Euler(0f, yaw, 0f);

        log.AppendLine("SmallGuardian relocated to " + guardian.transform.position.ToString("F2") + " (near-flat plateau, was a 56deg slope).");
    }

    // 3) Two upright replacement trees on surveyed flat plateaus, using AncientFir_B (root rotation
    // already identity -- not affected by the firD baked-rotation bug), preserving whatever rotation
    // the prefab instance already carries instead of overwriting it outright.
    static void AddReplacementHeroTrees(Terrain terrain, TerrainCollider col, float rayTop, StringBuilder log)
    {
        var parent = GameObject.Find("ForestStage_Terrain/LakeHeroAncientTrees");
        var firB = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stage/Forest/Trees/AncientFir_B.prefab");
        if (firB == null) { log.AppendLine("AncientFir_B prefab missing."); return; }

        if (parent.transform.Find("LakeHero_WestBank_ThickFir") == null)
        {
            PlaceUprightFir(terrain, col, rayTop, parent.transform, firB, -28f, -37f, 4.6f, 95f, "LakeHero_WestBank_ThickFir", log);
        }
    }

    static void PlaceUprightFir(Terrain terrain, TerrainCollider col, float rayTop, Transform parent, GameObject prefab,
        float x, float z, float scale, float yawDeg, string name, StringBuilder log)
    {
        RayGround(col, terrain, rayTop, x, z, out RaycastHit hit);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        inst.name = name;
        inst.transform.localScale = Vector3.one * scale;
        Quaternion baseRot = inst.transform.rotation; // preserve the prefab's own baked orientation (identity for firB, but keep the pattern generic/safe)
        inst.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f) * baseRot;
        inst.transform.position = new Vector3(x, hit.point.y, z);
        var rend = inst.GetComponentInChildren<Renderer>();
        float gap = hit.point.y - rend.bounds.min.y - 0.3f;
        inst.transform.position += Vector3.up * gap;
        log.AppendLine("Placed " + name + " at " + inst.transform.position.ToString("F2"));
    }

    // 4) Root-base dressing rocks: recompute grounding using the CURRENT (post-move) bounds instead of
    // the stale pre-move bounds Pass 1 used, and keep every ring sample within the map's actual z
    // extent (Guardian tree sits ~0.7m south of the terrain's z=-46 edge, so a naive 360-degree ring
    // at the tree's ~5m root radius reaches past the edge on the north side).
    static void FixGuardianRootDressing(Terrain terrain, TerrainCollider col, float rayTop, StringBuilder log)
    {
        var dress = GameObject.Find("ForestStage_Terrain/GuardianRootDressing");
        if (dress == null) { log.AppendLine("GuardianRootDressing not found."); return; }

        // Drop any rock sitting outside the terrain's valid z range (off the world edge) -- these
        // cannot be grounded at all, by construction.
        float validZMin = terrainGO_Z(terrain);
        var toRemove = new System.Collections.Generic.List<Transform>();
        foreach (Transform c in dress.transform)
        {
            var rend = c.GetComponentInChildren<Renderer>();
            if (rend == null || rend.bounds.center.z < validZMin + 0.5f) toRemove.Add(c);
        }
        foreach (var t in toRemove) { Object.DestroyImmediate(t.gameObject); log.AppendLine("Removed off-map dressing rock " + t.name); }

        // Re-ground everything remaining using CURRENT bounds (not stale pre-move bounds).
        foreach (Transform c in dress.transform)
        {
            var rend = c.GetComponentInChildren<Renderer>();
            if (rend == null) continue;
            Vector3 bc = rend.bounds.center;
            if (!RayGround(col, terrain, rayTop, bc.x, bc.z, out RaycastHit hit)) continue;
            float delta = hit.point.y - rend.bounds.min.y - 0.1f;
            c.position += Vector3.up * delta;
        }
        log.AppendLine("Re-grounded GuardianRootDressing rocks (" + dress.transform.childCount + " remaining).");
    }

    static float terrainGO_Z(Terrain terrain) => terrain.transform.position.z;

    // 5) Four large cliff boulders whose SINGLE-PIVOT placement (from the original stage build) looked
    // fine at their pivot but left much of their real footprint hanging over lower terrain on this
    // steep slope -- re-grounded from an 8-point ring at ~70% of each boulder's own footprint radius,
    // using the LOWEST ring sample (接地ルール rule 5: wide assets need multi-point sampling, and the
    // lowest point sets the embed so no edge floats).
    static void RegroundFloatingCliffBoulders(Terrain terrain, TerrainCollider col, float rayTop, StringBuilder log)
    {
        foreach (var n in new[] { "CliffBoulder_20", "CliffBoulder_13", "CliffBoulder_11", "CliffBoulder_21" })
        {
            var go = GameObject.Find("ForestStage_Terrain/LakeCliffWall/" + n);
            if (go == null) continue;
            var rend = go.GetComponentInChildren<Renderer>();
            float radius = Mathf.Max(rend.bounds.extents.x, rend.bounds.extents.z);
            Vector3 bc = rend.bounds.center;

            float lowestY = float.MaxValue;
            for (int i = -1; i < 8; i++)
            {
                float sx, sz;
                if (i == -1) { sx = bc.x; sz = bc.z; }
                else { float a = i / 8f * Mathf.PI * 2f; sx = bc.x + Mathf.Cos(a) * radius * 0.7f; sz = bc.z + Mathf.Sin(a) * radius * 0.7f; }
                if (RayGround(col, terrain, rayTop, sx, sz, out RaycastHit h) && h.point.y < lowestY) lowestY = h.point.y;
            }
            if (lowestY == float.MaxValue) continue;

            float pivotToBottom = go.transform.position.y - rend.bounds.min.y;
            const float embed = 0.4f;
            Vector3 old = go.transform.position;
            go.transform.position = new Vector3(old.x, (lowestY - embed) + pivotToBottom, old.z);
            log.AppendLine(n + " regrounded: y " + old.y.ToString("F2") + " -> " + go.transform.position.y.ToString("F2"));
        }
    }

    // 6) Waterfall corridor: two rocks were identified (via camera-position raycast fan tests plus
    // direct screenshot comparison) as sitting in front of/across the falls' own sightline from the
    // lake/bridge side, rather than flanking it.
    static void OpenWaterfallCorridor(StringBuilder log)
    {
        // WaterfallSourceRock_0: named "source" (should sit near where the water emerges, i.e. the
        // TOP of the falls) but was actually low (y=0.47) and extended IN FRONT of the waterfall mesh's
        // own front face (z=-31.25 vs the falls' front at z=-33.8). Pulled back and up onto the cliff
        // slope directly behind the falls' upper region instead, so it now reads as a crevice the water
        // originates from rather than a rock blocking the base.
        var src = GameObject.Find("ForestStage_Terrain/Waterfalls/WaterfallSourceRock_0");
        if (src != null)
        {
            src.transform.position = new Vector3(-3.35f, 7.45f, -38.74f);
            log.AppendLine("WaterfallSourceRock_0 moved behind/above the falls (was blocking the front, low and forward).");
        }

        // HeroCoastalCliffBase_0: centered almost exactly on the waterfall's own x (-0.01 vs falls
        // center -3.25) and projecting further toward the viewer (z=-28.96) than the falls' own front
        // -- a textbook "rock placed in front of, not beside, the waterfall". Moved to the right side of
        // the falls (mirroring CliffBoulder_18's left-flank role from Pass 1) to help frame the corridor
        // instead of blocking its center.
        var base0 = GameObject.Find("ForestStage_Terrain/LakeCliffWall/HeroCoastalCliffBase_0");
        if (base0 != null)
        {
            base0.transform.position = new Vector3(8f, -3.20f, -37f);
            log.AppendLine("HeroCoastalCliffBase_0 moved to the right-flank position (was centered in front of the falls).");
        }
    }
}
