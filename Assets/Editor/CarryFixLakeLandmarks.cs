using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-14: three-part landmark fix for the lake area, requested together because they're really
// one problem (the AncientForestGuardian tree, the waterfall, and the surrounding rocks/trees need to
// read as ONE natural environment, not independently-placed props):
//
//  1) AncientForestGuardian was floating -- BuildAncientForestGuardianTree (CarryBuildTerrainForest.cs)
//     grounds it with a SINGLE raycast sample at the tree's own pivot (the project's own 接地ルール
//     rule 5 explicitly warns this fails for wide assets: "横に広いアセットは複数点サンプリングする").
//     The tree's root disc is ~9-10m across, but it was standing on a genuinely pointed terrain peak
//     (height drops from ~20.7 at the summit to ~7 just 10m south, toward the waterfall) -- a single
//     center sample found the (locally flat) summit itself and never noticed the footprint's edges
//     hang over open air on every side. Fixed here by actually sculpting a broad rounded knoll under
//     the tree first (so the ground genuinely matches the root spread, not just papering over the
//     mismatch with a bigger embed offset), then re-grounding from a multi-point ring sample.
//  2) CliffBoulder_18 (LakeCliffWall) sat almost dead-center in front of the waterfall's base/impact
//     pool, big enough (pre-fix scale 4.42) to read as "a rock hiding the falls" from the lake/bridge
//     view. Moved to properly frame the LEFT side instead (matching WaterfallFlankRock_0_1's existing
//     role on the right), so the falls open up through the middle per the brief's "岩が滝を囲う、隠さない"
//     goal.
//  3) Added a handful of additional large old-growth trees around the lake shore for canopy density,
//     mixing AncientFir_A/B/C/D_Curved (already-approved thick-trunk realistic fir prototypes used
//     elsewhere in this forest) with one more AncientForestGuardian at a different scale, each with
//     individually-varied scale/rotation/lean and its own grounding pass -- never an evenly-spaced
//     row of identical clones.
public static class CarryFixLakeLandmarks
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    [MenuItem("Carry/Fix Lake Landmarks (Guardian Tree + Waterfall + Hero Trees)")]
    public static void Run()
    {
        var log = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainRoot = GameObject.Find("ForestStage_Terrain");
            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();

            SculptAndGroundGuardianTree(terrain, terrainRoot, log);
            FixWaterfallFrontRocks(log);
            AddLakeHeroTrees(terrain, terrainRoot, log);

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

    // ==== shared grounding helper (same TerrainCollider-raycast convention as CarryBuildTerrainForest
    // 接地ルール, reimplemented locally -- the original is a private method on a different class). ====
    static bool TryGetTerrainSurfaceLocal(Terrain terrain, float worldX, float worldZ, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        var col = terrain.GetComponent<TerrainCollider>();
        float rayTopY = terrain.transform.position.y + terrain.terrainData.size.y + 20f;
        var ray = new Ray(new Vector3(worldX, rayTopY, worldZ), Vector3.down);
        if (col != null && col.Raycast(ray, out RaycastHit hit, terrain.terrainData.size.y + 40f))
        {
            hitPoint = hit.point;
            hitNormal = hit.normal;
            return true;
        }
        float sampledY = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrain.transform.position.y;
        hitPoint = new Vector3(worldX, sampledY, worldZ);
        hitNormal = Vector3.up;
        return false;
    }

    // ---- 1) Sculpt a natural rounded knoll under the guardian tree, then re-ground it on the
    // result using a multi-point ring sample instead of a single pivot sample. ----
    static void SculptAndGroundGuardianTree(Terrain terrain, GameObject terrainRoot, StringBuilder log)
    {
        var tree = terrainRoot.transform.Find("AncientForestGuardian");
        if (tree == null) { log.AppendLine("AncientForestGuardian not found -- skipped sculpt/reground."); return; }

        float cx = tree.position.x;
        float cz = tree.position.z;

        var data = terrain.terrainData;
        float originX = terrain.transform.position.x;
        float originZ = terrain.transform.position.z;
        float originY = terrain.transform.position.y;
        float sizeX = data.size.x, sizeZ = data.size.z, sizeY = data.size.y;
        int hr = data.heightmapResolution;

        // Measured root-disc world bounds are ~9.3 x 10.0m -> ~5.0m half-extent. coreRadius covers
        // the actual footprint with a rounded dome; outerRadius is where the sculpt fully fades back
        // to the untouched original terrain, so it blends into the surrounding slope instead of
        // reading as a artificial disc dropped onto the hillside.
        const float coreRadius = 5.2f;
        const float outerRadius = 9.5f;

        float apexY = terrain.SampleHeight(new Vector3(cx, 0f, cz)) + originY;

        // Baseline = average height of the untouched terrain right at the blend boundary, so the
        // knoll settles back into whatever the surrounding slope already is (never an arbitrary flat
        // value, and never a fully human-flat plateau -- this is the "自然な受け皿" the brief asked for,
        // not a construction pad).
        float baseY = 0f;
        const int baseSamples = 12;
        for (int i = 0; i < baseSamples; i++)
        {
            float a = i / (float)baseSamples * Mathf.PI * 2f;
            float sx = cx + Mathf.Cos(a) * outerRadius;
            float sz = cz + Mathf.Sin(a) * outerRadius;
            baseY += terrain.SampleHeight(new Vector3(sx, 0f, sz)) + originY;
        }
        baseY /= baseSamples;

        var heights = data.GetHeights(0, 0, hr, hr);

        int minXi = Mathf.Max(0, Mathf.FloorToInt(((cx - outerRadius) - originX) / sizeX * (hr - 1)));
        int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt(((cx + outerRadius) - originX) / sizeX * (hr - 1)));
        int minZi = Mathf.Max(0, Mathf.FloorToInt(((cz - outerRadius) - originZ) / sizeZ * (hr - 1)));
        int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt(((cz + outerRadius) - originZ) / sizeZ * (hr - 1)));

        for (int zi = minZi; zi <= maxZi; zi++)
        {
            float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
            for (int xi = minXi; xi <= maxXi; xi++)
            {
                float worldX = originX + (xi / (float)(hr - 1)) * sizeX;
                float d = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(cx, cz));
                if (d > outerRadius) continue;

                float t = Mathf.Clamp01(d / outerRadius);
                float domeShape = 0.5f * (1f + Mathf.Cos(t * Mathf.PI)); // 1 at center, 0 at outerRadius, smooth (cosine) falloff
                float domeHeight = baseY + (apexY - baseY) * domeShape;

                // Gentle natural irregularity (roots/rocks/soil undulation), fading out with the same
                // weight so it never creates a seam at the blend boundary.
                float noise = (Mathf.PerlinNoise(worldX * 0.25f + 500f, worldZ * 0.25f - 500f) - 0.5f) * 0.5f;
                domeHeight += noise * domeShape;

                float originalWorldY = originY + heights[zi, xi] * sizeY;
                float newWorldY = Mathf.Lerp(originalWorldY, domeHeight, domeShape);
                heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
            }
        }
        data.SetHeights(0, 0, heights);
        Physics.SyncTransforms(); // make sure the TerrainCollider reflects the new heights before raycasting below

        log.AppendLine("Sculpted rounded knoll under AncientForestGuardian at (" + cx.ToString("F1") + "," + cz.ToString("F1") +
            "), apex=" + apexY.ToString("F2") + " edgeBaseline=" + baseY.ToString("F2") +
            " coreR=" + coreRadius + " outerR=" + outerRadius);

        // ---- Re-ground: sample a ring at the tree's actual root radius (not just the pivot) and use
        // the LOWEST point so no edge of the root disc ends up floating (接地ルール rule 5). ----
        TryGetTerrainSurfaceLocal(terrain, cx, cz, out Vector3 centerHit, out Vector3 centerNormal);
        float lowestRingY = centerHit.y;
        const int ringSamples = 8;
        for (int i = 0; i < ringSamples; i++)
        {
            float a = i / (float)ringSamples * Mathf.PI * 2f;
            TryGetTerrainSurfaceLocal(terrain, cx + Mathf.Cos(a) * coreRadius, cz + Mathf.Sin(a) * coreRadius, out Vector3 h, out _);
            if (h.y < lowestRingY) lowestRingY = h.y;
        }

        var rend = tree.GetComponentInChildren<Renderer>();
        float pivotToBottom = tree.position.y - rend.bounds.min.y; // world-space offset from pivot down to the mesh's real bottom

        const float embed = 0.35f; // modest root-flare burial, same spirit as this project's boulder embed convention
        float targetBottomY = lowestRingY - embed;
        float newPivotY = targetBottomY + pivotToBottom;

        // Now that the ground under it is a broad rounded knoll (not a spike), the tree should stand
        // close to upright -- keep the existing yaw (already facing the lake/waterfall) and blend only
        // a small fraction of the center-point normal tilt in, for a touch of natural lean rather than
        // a perfectly vertical mast.
        Quaternion naturalTilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, centerNormal), 0.25f);
        float existingYaw = tree.eulerAngles.y;

        Vector3 oldPos = tree.position;
        tree.position = new Vector3(oldPos.x, newPivotY, oldPos.z);
        tree.rotation = naturalTilt * Quaternion.Euler(0f, existingYaw, 0f);

        log.AppendLine("Guardian tree regrounded: y " + oldPos.y.ToString("F2") + " -> " + tree.position.y.ToString("F2") +
            " (lowestRingSampleY=" + lowestRingY.ToString("F2") + ", embed=" + embed + ")");

        // ---- Base dressing: hide the tree/terrain seam with a few small rocks duplicated from the
        // existing shore-dressing rock population already used elsewhere in this scene, each
        // individually grounded -- never a fabricated flat ring. ----
        var rockTemplate = GameObject.Find("ForestStage_Terrain/LakeShoreDressing")?.transform.GetChild(0);
        if (rockTemplate != null)
        {
            var dressParent = new GameObject("GuardianRootDressing").transform;
            dressParent.SetParent(terrainRoot.transform, false);
            var rng = new System.Random(90210);
            const int dressCount = 6;
            for (int i = 0; i < dressCount; i++)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float r = coreRadius * (0.75f + (float)rng.NextDouble() * 0.5f);
                float rx = cx + Mathf.Cos(a) * r;
                float rz = cz + Mathf.Sin(a) * r;
                TryGetTerrainSurfaceLocal(terrain, rx, rz, out Vector3 rHit, out Vector3 rNormal);

                var inst = Object.Instantiate(rockTemplate.gameObject, dressParent);
                inst.name = "GuardianRootRock_" + i;
                float s = 0.5f + (float)rng.NextDouble() * 0.6f;
                inst.transform.localScale = Vector3.one * s;
                inst.transform.rotation = Quaternion.FromToRotation(Vector3.up, rNormal) * Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                var irend = inst.GetComponentInChildren<Renderer>();
                float bottomOffset = inst.transform.position.y - (irend != null ? irend.bounds.min.y : inst.transform.position.y);
                inst.transform.position = rHit - rNormal * 0.15f; // slight embed
                if (irend != null)
                {
                    // re-measure after the transform change and nudge up so it doesn't float
                    float gap = rHit.y - irend.bounds.min.y;
                    inst.transform.position += Vector3.up * Mathf.Max(0f, gap);
                }
            }
            log.AppendLine("Added " + dressCount + " root-base dressing rocks around the guardian tree.");
        }
        else
        {
            log.AppendLine("No LakeShoreDressing rock template found -- skipped base dressing.");
        }
    }

    // ---- 2) Move the boulder that was sitting in front of the waterfall's base so it frames the
    // left side instead (matching WaterfallFlankRock_0_1's existing right-side framing role). ----
    static void FixWaterfallFrontRocks(StringBuilder log)
    {
        var boulder = GameObject.Find("ForestStage_Terrain/LakeCliffWall/CliffBoulder_18");
        if (boulder == null) { log.AppendLine("CliffBoulder_18 not found -- skipped waterfall front-rock fix."); return; }

        Vector3 oldPos = boulder.transform.position;
        float oldScale = boulder.transform.localScale.x;

        // Values below were derived live via TerrainCollider raycasts at the new spot (surface
        // y=3.25-4.66 depending on exact sample point, normal ~(0.3,0.43,0.85)) and verified with
        // manage_camera screenshots showing the falls' base opening up once this boulder moved out of
        // the direct lake-side sightline. Left mostly at its existing rotation (already a natural
        // resting tilt) -- only position/scale change so it reads as a left-flank boulder alongside
        // the existing CliffBoulder_16/28 cluster on that same slope, instead of a lone rock centered
        // on the falls.
        boulder.transform.position = new Vector3(-9.612f, 3.528f, -36.627f);
        boulder.transform.localScale = Vector3.one * 3.4f;

        log.AppendLine("CliffBoulder_18 moved from " + oldPos + " (scale " + oldScale.ToString("F2") +
            ") to " + boulder.transform.position + " (scale 3.4) -- out of the waterfall's direct sightline, now flanking left.");
    }

    // ---- 3) A handful of additional large old-growth trees around the lake shore, mixing species,
    // scale, rotation and lean so they read as a natural stand rather than duplicated props. ----
    static void AddLakeHeroTrees(Terrain terrain, GameObject terrainRoot, StringBuilder log)
    {
        var firA = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stage/Forest/Trees/AncientFir_A.prefab");
        var firB = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stage/Forest/Trees/AncientFir_B.prefab");
        var firC = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stage/Forest/Trees/AncientFir_C.prefab");
        var firD = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stage/Forest/Trees/AncientFir_D_Curved.prefab");
        var guardian = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stage/Forest/Trees/AncientForestGuardian/Prefabs/PF_AncientForestGuardian.prefab");

        var parent = new GameObject("LakeHeroAncientTrees").transform;
        parent.SetParent(terrainRoot.transform, false);

        // x,z hand-picked from the surveyed shoreline/boulder layout to stay clear of: the bridge
        // crossing (z around 0-10, x around -9..2), the waterfall centerline (x -6..0, z -30..-40),
        // every recorded AzureCrystal position, and each other (no even spacing/grid).
        var specs = new[]
        {
            // (prefab, x, z, scale, yawDeg, leanDeg, name, description)
            new HeroTreeSpec(firD, -24.5f, -19f, 3.4f, 55f, 10f, "LakeHero_WestBank_CurvedFir", "湖岸左側・やや小さい古木、湖側へ緩く傾く"),
            new HeroTreeSpec(firB,  27.5f, -29f, 4.6f, 200f, 0f, "LakeHero_EastBank_ThickFir", "湖岸右奥・太い古木"),
            new HeroTreeSpec(firD,  17.0f, -25f, 3.9f, 250f, 16f, "LakeHero_EastCliff_LeaningFir", "崖上・湖側へ枝を伸ばした古木"),
            new HeroTreeSpec(firC,   9.0f, -55f, 3.2f, 20f, 0f, "LakeHero_FarForest_Fir", "奥の森・一部だけ見える大型古木"),
            new HeroTreeSpec(guardian, -20.5f, -30f, 2.6f, 140f, 4f, "LakeHero_WestBank_SmallGuardian", "湖岸左側奥・やや小さめの2本目のAncientForestGuardian系統"),
        };

        int placed = 0;
        foreach (var s in specs)
        {
            if (s.prefab == null) { log.AppendLine("Skipped " + s.name + " -- prefab missing."); continue; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(s.prefab, parent);
            inst.name = s.name;
            inst.transform.localScale = Vector3.one * s.scale;

            TryGetTerrainSurfaceLocal(terrain, s.x, s.z, out Vector3 hit, out Vector3 normal);

            Quaternion lean = s.leanDeg != 0f ? Quaternion.Euler(s.leanDeg, 0f, 0f) : Quaternion.identity;
            inst.transform.rotation = Quaternion.Euler(0f, s.yawDeg, 0f) * lean;
            inst.transform.position = new Vector3(s.x, hit.y, s.z); // provisional, refine below from measured bounds

            var rend = inst.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                const float embed = 0.3f;
                float gap = hit.y - rend.bounds.min.y - embed;
                inst.transform.position += Vector3.up * gap;
            }

            log.AppendLine("Placed " + s.name + " (" + s.desc + ") at " + inst.transform.position.ToString("F2") +
                " scale=" + s.scale + " yaw=" + s.yawDeg + " lean=" + s.leanDeg);
            placed++;
        }
        log.AppendLine("AddLakeHeroTrees: placed " + placed + "/" + specs.Length + " large trees around the lake.");
    }

    struct HeroTreeSpec
    {
        public GameObject prefab; public float x, z, scale, yawDeg, leanDeg; public string name, desc;
        public HeroTreeSpec(GameObject prefab, float x, float z, float scale, float yawDeg, float leanDeg, string name, string desc)
        { this.prefab = prefab; this.x = x; this.z = z; this.scale = scale; this.yawDeg = yawDeg; this.leanDeg = leanDeg; this.name = name; this.desc = desc; }
    }
}
