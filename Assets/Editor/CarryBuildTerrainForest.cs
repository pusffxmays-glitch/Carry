using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Full rebuild of the normal-forest stage on a Unity Terrain: rolling, uneven
// ground with a naturally meandering, variable-width stream carved into the
// heightmap (not a rectangular trench in a flat Plane), densely planted with
// large trees on both banks via the Terrain tree system so the forest reads
// as continuing indefinitely instead of a decorated flat field. The game
// route (rocks/logs/roots) sits on and in this real terrain, anchored to
// sampled terrain/water height rather than a fixed Y. Reuses the river
// fall/recovery gameplay scripts unchanged.
public static class CarryBuildTerrainForest
{
    const string SourceScenePath = "Assets/Scenes/CastleStage.unity";
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const string PH = "Assets/ExternalAssets/PolyHaven/";
    const string Quat = "Assets/ExternalAssets/QuaterniusNatureMegaKit/FBX (Unity)/";
    const string Kenney = "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/";

    // ---- Terrain extents. Wide enough that panning the camera never shows open sky
    // where the forest should be, and long enough behind the start to fit the lake. ----
    const float TerrainWidth = 100f;
    const float TerrainLength = 190f;
    // Raised from 16 -- the new dramatic cliff rim (see CliffRimElevation) needs real headroom
    // above water (target cliff-top heights of ~+10 to +18m above LakeWaterY, i.e. up to world
    // Y~+22); 16m of range (world Y -8..+8) would have clamped/flattened the tops of anything
    // taller than +8. Terrain heightmaps are 16-bit internally regardless of this range, so this
    // costs no vertical precision.
    const float TerrainHeightRange = 32f;
    const float OriginX = -TerrainWidth * 0.5f;
    const float OriginZ = -46f;
    const float OriginY = -8f;
    const int HeightRes = 257;
    const int AlphaRes = 257;

    // ---- Lake: what the river flows INTO, behind the start bridge. Player faces away
    // from it (+Z); water flows toward it (-Z) -- current and progress point opposite ways.
    // Most of the shoreline is a steep, near-unclimbable cliff (see LakeFactor) -- the only
    // gentle grades are toward the river inlet (so the water flows in naturally) and toward
    // the recovery stairs (the one official way back to land). ----
    const float LakeCenterX = 0f;
    const float LakeCenterZ = -16f;
    const float LakeRadiusX = 24f;
    const float LakeRadiusZ = 20f;
    const float LakeDepth = 5.0f;
    const float LakeWaterY = -4.4f;
    const float InletAngleDeg = -10f; // direction from lake center toward the bridge/river inlet
    // Direction from lake center toward the recovery stairs -- placed close to the bridge's east
    // side (not far around the lake) so a swept-back player has a short, obvious way back: swim to
    // the near shore, climb the stairs, walk a short stretch to the bridge. Both this and the inlet
    // zone widths (see LakeGentleWeight) were narrowed so the two gentle openings stay clearly
    // separated by cliff, rather than merging into one large climbable arc now that they're closer
    // together.
    const float StairsAngleDeg = 55f;

    // ---- Stone bridge: the actual START. Crosses the river perpendicular to its flow (long
    // axis in X), a short walk deep (Z), positioned just upstream of where the river widens
    // into the lake. ----
    const float BridgeCenterZ = 5f;
    const float BridgeDeckDepth = 6f; // walking depth of the deck -- a few steps of room
    const float BridgeZ0 = BridgeCenterZ - BridgeDeckDepth * 0.5f;
    const float BridgeZ1 = BridgeCenterZ + BridgeDeckDepth * 0.5f;

    // River: one continuous channel from deep forest, under the bridge, and into the lake --
    // no gap. Ramps out near RiverZ1 (deep forest, safe far end) and gradually widens again
    // as it nears the lake mouth (south of the bridge) so the two water bodies blend into one.
    const float RiverRampLen = 8f;
    const float RiverZ1 = 120f;
    const float RiverMouthWidenZ0 = BridgeCenterZ - 3f; // widening begins just past the bridge
    const float RiverMouthWidenZ1 = LakeCenterZ + 8f;   // fully widened by here
    const float FootholdStartZ = BridgeZ1 + 8f;         // where the regular foothold route begins
    const float CourseZ0 = BridgeZ1 + 1f;               // fall-detection/course start, just past the bridge
    const float RiverWaterZ0 = -22f;                    // water-mesh render range extends into the lake
    const float RiverDepth = 3.6f;
    const float BankFalloff = 4.5f;

    // Meshy stone bridge geometry -- shared constants so the terrain (approach mound, below) and
    // the bridge builder itself always agree on exactly where the bridge sits, without either one
    // reading back the OTHER's built output (which would create a circular build-order dependency).
    // Measured world-space bounds of the decimated model at identity transform: full size
    // (span=2.0, height=0.62, depth=1.42).
    const float MeshyBridgeModelHalfSpan = 1.00f, MeshyBridgeModelHalfHeight = 0.31f, MeshyBridgeModelHalfDepth = 0.71f;
    const float MeshyBridgeScaleSpanHeight = 8.0f;
    const float MeshyBridgeScaleDepth = 4.5f;
    const float MeshyBridgeWorldHalfSpan = MeshyBridgeModelHalfSpan * MeshyBridgeScaleSpanHeight;     // 8m
    const float MeshyBridgeWorldHalfHeight = MeshyBridgeModelHalfHeight * MeshyBridgeScaleSpanHeight; // 2.48m
    const float MeshyBridgeWorldHalfDepth = MeshyBridgeModelHalfDepth * MeshyBridgeScaleDepth;        // 3.195m

    // The Meshy-made stone bridge model's natural proportions are narrower than the river was
    // previously carved to (a real bridge is often sited at a river's narrowest point anyway) --
    // this pinches the channel width down near the crossing, tapering back to the normal width
    // within a short distance either side, rather than reshaping the river at large.
    const float BridgePinchMaxHalfWidth = 5.2f;
    const float BridgePinchTaperDist = 8f;

    [MenuItem("Carry/Build Terrain Forest Stage")]
    public static void Run()
    {
        var log = new StringBuilder();
        try
        {
            var initialScene = EditorSceneManager.GetActiveScene();
            var sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
            if (string.IsNullOrEmpty(initialScene.path) && initialScene.isLoaded)
                EditorSceneManager.CloseScene(initialScene, true);

            GameObject srcGoblin = null, srcCam = null, srcLight = null;
            foreach (var r in sourceScene.GetRootGameObjects())
            {
                if (r.name == "Goblin") srcGoblin = r;
                else if (r.name == "Main Camera") srcCam = r;
                else if (r.name == "Directional Light") srcLight = r;
            }
            if (srcGoblin == null || srcCam == null) throw new Exception("Goblin/Main Camera not found in " + SourceScenePath);

            Scene scene;
            if (System.IO.File.Exists(ScenePath))
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                foreach (var r in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(r);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }

            var root = new GameObject("ForestStage_Terrain");
            SceneManager.MoveGameObjectToScene(root, scene);

            // CRITICAL: must happen before any RenderSettings.* call (BuildSkyFogLighting is the
            // first one). RenderSettings is a global API that always operates on whatever scene is
            // currently ACTIVE -- and "scene" was only ever opened/created ADDITIVELY above, which
            // does NOT make it active. Without this, every RenderSettings change in this whole
            // pipeline (skybox, ambient, fog) was silently being applied to sourceScene (or whatever
            // was active beforehand) instead of the scene actually being saved -- confirmed by
            // inspecting ForestStage_Realistic.unity directly after a "fixed" skybox rebuild and
            // finding the OLD photographic HDRI skybox and Skybox ambient mode still serialized
            // there, completely unchanged despite the code changes and successful rebuild log.
            EditorSceneManager.SetActiveScene(scene);

            BuildSkyFogLighting(root, log);
            var terrain = BuildTerrain(root, log);

            // 2026-08-14 FIX (user-reported: several CliffBoulder/HeroCoastalCliffBase instances
            // floating 10-20m in mid-air, at heights the current terrain heightmap doesn't reach
            // anywhere nearby -- confirmed via direct re-raycast after the build: querying the SAME
            // xz post-build always returns the correct low height, but the object was placed high at
            // build time). Root cause: this scene's old root GameObjects (including the previous
            // Terrain + TerrainCollider) were just DestroyImmediate'd above and a brand-new Terrain
            // was created via BuildTerrain immediately after, all inside one synchronous call with no
            // frame boundary -- PhysX's broadphase can still be holding stale collision data (from the
            // just-destroyed old terrain, or an uncooked new TerrainCollider heightfield) at the exact
            // moment TryGetTerrainSurface's raycasts run for the very next builders (BuildLakeCliffWall
            // etc.), producing a "successful" raycast hit against the WRONG (old/uncooked) surface.
            // Forcing the terrain collider's heightfield to recook and flushing the physics scene
            // before any raycast-based placement runs eliminates that stale-data window.
            var terrainCollider = terrain.GetComponent<TerrainCollider>();
            if (terrainCollider != null)
            {
                var terrainDataRef = terrainCollider.terrainData;
                terrainCollider.terrainData = null;
                terrainCollider.terrainData = terrainDataRef;
            }
            Physics.SyncTransforms();

            BuildWater(root, terrain, log);
            BuildLakeWater(root, log);
            BuildLakeCliffWall(root, terrain, log);
            BuildWaterfalls(root, terrain, log); // re-enabled now that the cliff face itself is sparse/deliberate rather than a rock pile
            BuildLakeUnderwaterRocks(root, terrain, log);
            BuildLakeShoreDressing(root, terrain, log);
            float deckY = BuildMeshyBridge(root, terrain, log);
            BuildStairs(root, terrain, log);

            // Spawn stands on the bridge deck (which crosses the river sideways), facing +Z
            // (upstream, toward the river/forest course -- NOT along the bridge's own length)
            // with the lake close behind -- the literal start of the game.
            Vector3 spawnPos = new Vector3(RiverX(BridgeCenterZ), deckY + 0.15f, BridgeCenterZ);

            var goblin = (GameObject)UnityEngine.Object.Instantiate(srcGoblin);
            goblin.name = "Goblin";
            SceneManager.MoveGameObjectToScene(goblin, scene);
            goblin.transform.position = spawnPos;
            goblin.transform.rotation = Quaternion.identity;

            var cam = (GameObject)UnityEngine.Object.Instantiate(srcCam);
            cam.name = "Main Camera";
            SceneManager.MoveGameObjectToScene(cam, scene);
            var rig = cam.GetComponent<CarryCameraRig>();
            if (rig != null) rig.target = goblin.transform;

            if (srcLight != null)
            {
                var light = (GameObject)UnityEngine.Object.Instantiate(srcLight);
                light.name = "Directional Light";
                SceneManager.MoveGameObjectToScene(light, scene);
                var l = light.GetComponent<Light>();
                if (l != null)
                {
                    // Raised back up from 1.25 -- with the brighter Trilight ambient added below,
                    // this intensity is what actually creates the "明るい木漏れ日" contrast (strong
                    // direct light against a moderate-not-black ambient base), rather than either
                    // washing out the terrain color (the earlier 1.55 problem) or reading as a dim
                    // overcast forest (the 1.25 "too dark" problem this brief specifically flagged).
                    l.intensity = Mathf.Max(l.intensity, 1.9f);
                    l.color = new Color(1f, 0.95f, 0.83f);
                    l.shadows = LightShadows.Soft;
                    l.shadowStrength = 0.85f;
                }
            }
            EditorSceneManager.CloseScene(sourceScene, true);

            float refWaterY = WaterYAt(CourseZ0 + 30f);
            BuildRiverGimmick(root, terrain, refWaterY, spawnPos, log);
            BuildTrees(terrain, log);
            BuildLakeHeroLeaningTrees(root, terrain, log);
            BuildAncientForestGuardianTree(root, terrain, log);
            BuildGroundVegetation(terrain, log);
            BuildGroundDetail(root, terrain, log);
            BuildForestFloorClutter(root, terrain, log);
            BuildFootholds(root, terrain, log);
            BuildCliffVines(root, terrain, log);
            BuildAzureCrystals(root, terrain, log);

            ValidateNoFloatingObjects(root, log);

            // Bake the lake reflection probe LAST, now that every cliff/rock/tree/vine that should
            // actually show up in the water's reflection has been placed (the probe itself was
            // created early, in BuildLakeWater, but baking then would have captured an empty scene).
            var probeGo = GameObject.Find("LakeReflectionProbe");
            var probeComp = probeGo != null ? probeGo.GetComponent<ReflectionProbe>() : null;
            if (probeComp != null)
            {
                bool baked = UnityEditor.Lightmapping.BakeReflectionProbe(probeComp, "Assets/Stage/Forest/LakeReflectionProbe.exr");
                log.AppendLine("Lake reflection probe baked: " + baked);
            }

            EditorSceneManager.SetActiveScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            log.AppendLine("SUCCESS");
        }
        catch (Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    // ---- Shared river-shape functions: used by terrain carving, the water mesh and foothold
    // placement so they all agree on where the channel actually is. ----
    static float RiverX(float z) => 8f * (Mathf.PerlinNoise(z * 0.016f, 5.2f) - 0.5f) * 2f;

    // Wide (12-24m across) so a fall from almost anywhere on the main route reliably lands in
    // water. Continuous all the way from deep forest through the bridge and into the lake --
    // only fades out near RiverZ1 (deep forest, safe far end) -- and widens gradually south of
    // the bridge so it blends into the lake instead of meeting it at a hard seam.
    static float RiverHalfWidth(float z)
    {
        if (z > RiverZ1) return 0f;
        float endRamp = Mathf.Clamp01(Mathf.InverseLerp(RiverZ1, RiverZ1 - RiverRampLen, z));
        float baseHW = Mathf.Lerp(6f, 12f, Mathf.PerlinNoise(z * 0.035f, 91.7f));
        float hw = baseHW * endRamp;
        float mouthWiden = Mathf.Clamp01(Mathf.InverseLerp(RiverMouthWidenZ0, RiverMouthWidenZ1, z));
        hw += mouthWiden * 14f;

        // The river channel must end entirely once inside the lake's own body -- it was never
        // actually terminated there before (mouthWiden saturates at 1 and stays there for any z
        // below RiverMouthWidenZ1, forever), it just used to be masked by the lake's old, much
        // wider gradual carve. This ramps the whole channel back down to 0 shortly past the mouth
        // (well before the lake's real far shore), a simple Z-based cutoff -- NOT a distance-from-
        // lake-center metric, which can't tell "just south of the lake" apart from "far upstream"
        // since both are far from the lake center.
        float channelEnd = Mathf.Clamp01(Mathf.InverseLerp(RiverMouthWidenZ1 - 10f, RiverMouthWidenZ1, z));
        hw *= channelEnd;

        // Pinch the channel down right at the bridge crossing to match the Meshy bridge model's
        // natural span, relaxing back to the normal width within BridgePinchTaperDist either side
        // -- a real bridge is commonly sited at a river's narrowest point anyway.
        float distFromBridge = Mathf.Abs(z - BridgeCenterZ);
        float pinchT = Mathf.Clamp01(distFromBridge / BridgePinchTaperDist);
        float maxHwHere = Mathf.Lerp(BridgePinchMaxHalfWidth, 999f, pinchT);
        hw = Mathf.Min(hw, maxHwHere);

        return hw;
    }

    // Water surface height at a given Z, following the riverbed and blending smoothly to the
    // lake's fixed flat level as it nears the lake -- shared by the water mesh and anything
    // that needs a representative water height (fall-trigger placement, sweep height).
    static float WaterYAt(float z)
    {
        float rx = RiverX(z);
        float waterY = GroundNoise(rx, z) - RiverDepth + 1.15f;
        float lakeBlend = Mathf.Clamp01(Mathf.InverseLerp(RiverMouthWidenZ0, LakeCenterZ + 2f, z));
        return Mathf.Lerp(waterY, LakeWaterY, lakeBlend);
    }

    // The Meshy bridge's deck height, computable purely from noise functions (no terrain lookup)
    // so it can be referenced from RawHeightAt (which BUILDS the terrain) without any circular
    // "read the terrain to build the terrain" dependency. Must exactly match the wrapperY/deckY
    // math in BuildMeshyBridge.
    static float ComputeBridgeDeckY() => WaterYAt(BridgeCenterZ) + MeshyBridgeWorldHalfHeight * 2f + 0.15f;

    // Measured (2026-08-11, via direct mesh-vertex scan of the decimated FBX) deck-surface height
    // of the Meshy bridge, sampled at 17 evenly-spaced points across the functional span (t=-1 at
    // the west edge .. t=+1 at the east edge), stored as offsets from the crown height
    // (ComputeBridgeDeckY(), which matches the measured crown almost exactly). The arch deck is
    // NOT flat -- it is highest at the crown and dips substantially and asymmetrically toward each
    // end (west edge ~1.36m below crown, east edge ~1.12m below crown). Both the walking Collider
    // and the terrain approach mound must follow this real curve, not a single flat height, or the
    // terrain/collider floats well above the visibly lower stone near the ends.
    static readonly float[] BridgeDeckCurveOffsets =
    {
        -1.357f, -1.207f, -0.890f, -0.834f, -0.639f, -0.439f, -0.322f, -0.320f,
        -0.320f, -0.139f, -0.339f, -0.390f, -0.732f, -0.732f, -0.992f, -1.116f, -1.116f
    };

    // t in [-1,1] across the bridge's functional span (-1 = west edge, +1 = east edge).
    static float BridgeDeckOffsetAtT(float t)
    {
        t = Mathf.Clamp(t, -1f, 1f);
        float f = (t + 1f) * 0.5f * (BridgeDeckCurveOffsets.Length - 1);
        int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, BridgeDeckCurveOffsets.Length - 2);
        float frac = f - i0;
        return Mathf.Lerp(BridgeDeckCurveOffsets[i0], BridgeDeckCurveOffsets[i0 + 1], frac);
    }

    // Real deck surface height (world Y) at a given world X position, following the measured arch
    // curve rather than a flat crown height. Positions beyond the functional span clamp to the
    // curve's end value (the deck doesn't keep curving down past its own edge).
    static float BridgeDeckWorldYAt(float worldX)
    {
        float riverCenterXHere = RiverX(BridgeCenterZ);
        float t = (worldX - riverCenterXHere) / MeshyBridgeWorldHalfSpan;
        return ComputeBridgeDeckY() + BridgeDeckOffsetAtT(t);
    }

    // Raises the terrain right at the bridge's two ends up to meet the deck height, fading back
    // to normal ground within a short distance -- an "approach mound" so the bridge doesn't sit
    // like a platform dropped on the ground with its sides exposed, and so there's no walking step
    // between the terrain and the bridge's own WalkableCollider. Returns a very low value (no
    // effect) outside its zone of influence; combine with Mathf.Max against the base terrain
    // height, never subtract.
    static float BridgeApproachMoundHeight(float worldX, float worldZ)
    {
        // Two land-side ramps at the bridge's two X-ends (west/east, where its span meets the
        // riverbanks), confined in Z so the mound never bulges past the bridge's own Z footprint
        // (which would bury the arch/masonry when viewed from up- or down-stream) and never
        // reaches far into the bridge's own footprint in X (which would bury the deck/center).
        //
        // zLimit previously sat at 0.55x the bridge's real half-depth (~1.76m of a 3.195m
        // half-depth) -- meant to keep the mound "never wider than the bridge," but that
        // logic only bounds the CEILING; leaving so much slack meant the mound only ever
        // backed a ~3.5m-wide central strip of each side edge, leaving the bridge's own
        // front/back corners (near BridgeZ0/BridgeZ1) hanging over unmounded ground. Raising
        // zLimit close to the bridge's actual half-depth (still fractionally short of it, so
        // the mound never pokes past the bridge's own Z extent) backs the FULL side edge,
        // with the zT fade below still tapering it off gently toward the true front/back ends.
        float distZ = Mathf.Abs(worldZ - BridgeCenterZ);
        float zLimit = MeshyBridgeWorldHalfDepth * 0.92f;
        if (distZ > zLimit) return -999f;

        float riverCenterXHere = RiverX(BridgeCenterZ);
        float distX = Mathf.Abs(worldX - riverCenterXHere) - MeshyBridgeWorldHalfSpan; // 0 at the bridge's own edge
        const float rampLen = 5f; // a few meters of land-side slope, widened slightly from 3.5m for a more gradual "forest path -> bridge width" taper
        if (distX < -0.4f || distX > rampLen) return -999f; // only a thin seam overlaps the bridge's own footprint -- the center and most of the span stay untouched

        float xT = Mathf.Clamp01(Mathf.InverseLerp(rampLen, 0f, distX));   // 1 at the bridge edge, 0 at rampLen out
        float zT = Mathf.Clamp01(Mathf.InverseLerp(zLimit, zLimit * 0.55f, distZ)); // full strength near the walking line, soft-fades toward the true front/back ends
        float blend = xT * zT;
        if (blend <= 0f) return -999f;

        // Target the REAL deck curve height at this exact worldX, not the (much higher) crown --
        // the arch dips well below the crown at both ends, so matching the crown here left the
        // terrain floating noticeably above the actual stone surface at the ends.
        float targetY = BridgeDeckWorldYAt(worldX) - 0.12f;
        float naturalY = GroundNoise(worldX, worldZ);
        return Mathf.Lerp(naturalY, targetY, blend);
    }

    static float GroundNoise(float x, float z)
    {
        float h = 0f;
        h += (Mathf.PerlinNoise(x * 0.018f, z * 0.018f) - 0.5f) * 3.0f;
        h += (Mathf.PerlinNoise(x * 0.07f + 50f, z * 0.07f + 50f) - 0.5f) * 1.0f;
        h += (Mathf.PerlinNoise(x * 0.22f + 150f, z * 0.22f + 150f) - 0.5f) * 0.3f;
        return h;
    }
    static float ChannelFactor(float distFromCenter, float halfWidth)
    {
        if (halfWidth <= 0.01f) return 0f; // no river here at all
        float inner = halfWidth * 0.5f;
        float outer = halfWidth + BankFalloff;
        float t = Mathf.InverseLerp(inner, outer, distFromCenter);
        return 1f - Mathf.SmoothStep(0f, 1f, t);
    }

    static float AngleDiffDeg(float a, float b) => Mathf.Abs(Mathf.Repeat(a - b + 180f, 360f) - 180f);

    // 1 = fully gentle grade (river inlet or stairs approach), 0 = full steep cliff. Shared by
    // LakeFactor (controls how narrow/steep the carve's shoreline band is) and by anything that
    // needs to pick angles ON the cliff, away from the two functional gaps.
    static float LakeGentleWeight(float angDeg)
    {
        // Narrowed from the original (14/34 and 14/30) now that the stairs sit much closer to the
        // inlet (55deg vs -10deg) -- without this the two gentle arcs would overlap and merge into
        // one wide climbable stretch of shore instead of two distinct, cliff-separated openings.
        float gentleInlet = 1f - Mathf.Clamp01(Mathf.InverseLerp(10f, 24f, AngleDiffDeg(angDeg, InletAngleDeg)));
        float gentleStairs = 1f - Mathf.Clamp01(Mathf.InverseLerp(8f, 20f, AngleDiffDeg(angDeg, StairsAngleDeg)));
        return Mathf.Max(gentleInlet, gentleStairs);
    }

    // ---- Named shoreline "bank archetypes" -- deliberate, large-scale asymmetric headlands/coves
    // plus per-zone wall-height and prop-density bias, so the lake reads as touring through several
    // distinct kinds of bank (per the "A through G" reference brief) rather than one uniform ring.
    // The old periodic sine "lobe" alone (now kept as fine secondary texture, see LakeFactor) was
    // too weak/high-frequency to read as real coves/points from a top-down view -- these are much
    // larger, angularly localized, and hand-placed clear of the two functional gentle zones (inlet
    // at InletAngleDeg, stairs at StairsAngleDeg). radialOffset follows the same sign convention as
    // LakeFactor's `d`: positive = land pokes further toward the lake center (headland/point,
    // shore radius shrinks), negative = water intrudes further into the bank (cove/bay, shore
    // radius grows). heightMul scales the cliff wall's rise above its grounded base (see
    // BuildLakeCliffWall) -- >1 reads as a taller rock wall, <1 as a low bank. ----
    enum ShoreZoneType { Default, LowMossyBank, BoulderOverhang, RockWall, RootBank }

    static readonly (float ang, float halfWidth, ShoreZoneType type, float radialOffset, float heightMul)[] ShoreZones =
    {
        // radialOffset magnitudes: d is normalized so 1.0 unit sits roughly at the shore, and the
        // lake's average radius is ~22m -- an offset of 0.10 (the original tuning) only shifts the
        // physical shoreline by ~2m, invisible from a top-down view against a 40-48m-diameter lake.
        // Raised to ~0.16-0.22 (roughly 3.5-4.8m of physical push/pull) so the coves/headlands are
        // actually legible in a top-down read, while staying angularly narrow enough (halfWidth
        // 12-18 deg) not to pinch the lake's open water shut anywhere.
        // Pushed further still this round -- user feedback was that even the previous (already-
        // strengthened) pass still read as too uniform/round. Radial offsets now 0.26-0.34
        // (~5.7-7.5m physical push/pull) and heightMul spans a wider 0.60-1.40 range.
        (110f, 14f, ShoreZoneType.BoulderOverhang, 0.28f, 1.10f),  // B: a huge boulder overhangs right to the waterline
        (160f, 16f, ShoreZoneType.LowMossyBank,   -0.26f, 0.60f),  // A: low mossy bank, water eases into a small bay -- shorter than default but still backed by the terrain's own steep LakeFactor carve, so it stays unclimbable
        (180f, 11f, ShoreZoneType.RockWall,        0.26f, 1.40f),  // the far shore directly opposite the bridge -- the single most visible viewpoint (looking across the water from the start), so the tallest, most prominent cliff feature belongs here
        (210f, 18f, ShoreZoneType.RockWall,        0.24f, 1.30f),  // C/G: tall mossy rock wall
        (260f, 16f, ShoreZoneType.RootBank,       -0.22f, 0.75f),  // D: bank undercut by water, tree roots exposed
        (305f, 12f, ShoreZoneType.RockWall,        0.26f, 1.25f),  // another tall cliff point, framing the inlet
    };

    static float ShoreZoneWeight(float angDeg, float centerAng, float halfWidth) =>
        1f - Mathf.Clamp01(Mathf.InverseLerp(halfWidth * 0.35f, halfWidth, AngleDiffDeg(angDeg, centerAng)));

    static (ShoreZoneType type, float radialOffset, float heightMul, float weight) GetShoreZone(float angDeg)
    {
        var best = (type: ShoreZoneType.Default, radialOffset: 0f, heightMul: 1f, weight: 0f);
        foreach (var z in ShoreZones)
        {
            float w = ShoreZoneWeight(angDeg, z.ang, z.halfWidth);
            if (w > best.weight) best = (z.type, z.radialOffset * w, Mathf.Lerp(1f, z.heightMul, w), w);
        }
        return best;
    }

    // 0 outside the lake, 1 well inside it, falling off across the shoreline. Around most of the
    // rim this falloff happens over a very short distance -- a steep, effectively unclimbable
    // cliff -- except in two angular windows (the river inlet and the recovery stairs) where it
    // widens back out into a normal gentle bank. The named ShoreZones above place large asymmetric
    // headlands/coves; a fine periodic "lobe" plus noise add secondary irregularity on top so even
    // the Default arcs between named zones aren't a clean ellipse. Every consumer (heightmap carve,
    // lake water mesh, cliff wall, shore-finding) shares this one function so they all agree on
    // exactly where the edge is.
    static float LakeFactor(float x, float z)
    {
        float angDeg = Mathf.Atan2(x - LakeCenterX, z - LakeCenterZ) * Mathf.Rad2Deg;
        float cliffness = 1f - LakeGentleWeight(angDeg);
        float zoneOffset = GetShoreZone(angDeg).radialOffset;

        float lobe = Mathf.Sin(angDeg * Mathf.Deg2Rad * 2.7f + 1.3f) * 0.05f + Mathf.Sin(angDeg * Mathf.Deg2Rad * 4.3f + 4f) * 0.03f;
        float shoreNoise = (Mathf.PerlinNoise(x * 0.05f + 300f, z * 0.05f + 300f) - 0.5f) * 0.14f;

        float dx = (x - LakeCenterX) / LakeRadiusX;
        float dz = (z - LakeCenterZ) / LakeRadiusZ;
        float d = Mathf.Sqrt(dx * dx + dz * dz) + shoreNoise + lobe + zoneOffset;

        float innerT = Mathf.Lerp(0.55f, 0.95f, cliffness);
        float outerT = Mathf.Lerp(1.05f, 1.015f, cliffness);
        float t = Mathf.Clamp01(Mathf.InverseLerp(innerT, outerT, d));
        return 1f - Mathf.SmoothStep(0f, 1f, t);
    }

    // Binary-searches outward from the lake center along +side X (at a fixed Z) for the
    // exact world X where LakeFactor crosses the shoreline threshold. Used to anchor the
    // stairs and shore dressing precisely at the water's edge regardless of shore noise.
    static float FindShoreX(float z, float side)
    {
        float lo = 0f, hi = (LakeRadiusX + 10f);
        for (int i = 0; i < 22; i++)
        {
            float mid = (lo + hi) * 0.5f;
            float f = LakeFactor(LakeCenterX + side * mid, z);
            if (f > 0.5f) lo = mid; else hi = mid;
        }
        return LakeCenterX + side * ((lo + hi) * 0.5f);
    }

    // Binary-searches outward from the lake center along a given compass angle (0=+Z/north,
    // 90=+X/east) for the shoreline point. Returns the world XZ position.
    static Vector2 FindShoreAtAngle(float angDeg)
    {
        float rad = angDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        float lo = 0f, hi = LakeRadiusX + LakeRadiusZ;
        for (int i = 0; i < 24; i++)
        {
            float mid = (lo + hi) * 0.5f;
            float f = LakeFactor(LakeCenterX + dir.x * mid, LakeCenterZ + dir.y * mid);
            if (f > 0.5f) lo = mid; else hi = mid;
        }
        float r = (lo + hi) * 0.5f;
        return new Vector2(LakeCenterX + dir.x * r, LakeCenterZ + dir.y * r);
    }

    // ---- Dramatic cliff rim: real TERRAIN elevation (not just the decorative LakeCliffWall mesh)
    // rising on the lake's left/right/far shores, so the lake reads as sitting at the bottom of a
    // genuine gorge with high forest above it (per the Shiraito-falls-style reference), instead of
    // a shallow decorative wall sitting on otherwise-flat ground. Suppressed to ~0 across BOTH the
    // existing gentle zones (inlet/stairs) AND a wide arc around the bridge-facing direction (~0
    // deg), generously wider than the bridge's own ~+-21deg angular extent, so the bridge/start
    // area is never touched. Reuses GetShoreZone's heightMul (already driving the decorative wall)
    // so the terrain and the wall agree on which angles are tallest. ----
    static float BridgeArcSuppression(float angDeg) => 1f - Mathf.Clamp01(Mathf.InverseLerp(25f, 45f, AngleDiffDeg(angDeg, 0f)));
    static float CliffRimSuppression(float angDeg) => Mathf.Max(LakeGentleWeight(angDeg), BridgeArcSuppression(angDeg));

    const float CliffRimBaseHeight = 15f; // meters, before per-zone heightMul scaling
    // Meters beyond the shore over which the rise ramps up, then plateaus (real cliff-top, not a
    // hill that comes back down). Kept to 10m rather than a more leisurely ramp because the terrain
    // has limited room on the south (far-shore) side specifically -- only ~10m between the shore
    // (LakeRadiusZ=20 out from LakeCenterZ=-16, i.e. z=-36) and the terrain's own south edge
    // (OriginZ=-46) -- a longer ramp would get cut off by the map boundary before reaching full
    // height there. The terrain's own finite extent bounds the plateau on all sides; no separate
    // outer fade is needed.
    const float CliffRimRampDist = 10f;

    static float CliffRimElevation(float worldX, float worldZ)
    {
        float angDeg = Mathf.Atan2(worldX - LakeCenterX, worldZ - LakeCenterZ) * Mathf.Rad2Deg;
        float suppress = CliffRimSuppression(angDeg);
        if (suppress >= 0.999f) return 0f;

        float dxN = (worldX - LakeCenterX) / LakeRadiusX;
        float dzN = (worldZ - LakeCenterZ) / LakeRadiusZ;
        float normDist = Mathf.Sqrt(dxN * dxN + dzN * dzN); // ~1.0 at the shore
        if (normDist < 0.95f) return 0f; // never touch the lake basin/shore transition itself

        float distBeyondShore = (normDist - 1f) * ((LakeRadiusX + LakeRadiusZ) * 0.5f);
        float zoneHeightMul = GetShoreZone(angDeg).heightMul;
        float riseBlend = Mathf.Clamp01(Mathf.InverseLerp(0f, CliffRimRampDist, distBeyondShore));
        return CliffRimBaseHeight * zoneHeightMul * riseBlend * (1f - suppress);
    }

    static float RawHeightAt(float worldX, float worldZ)
    {
        float baseH = GroundNoise(worldX, worldZ);
        float rx = RiverX(worldZ);
        float hw = RiverHalfWidth(worldZ);
        float d = Mathf.Abs(worldX - rx);
        float riverCarve = RiverDepth * ChannelFactor(d, hw);
        float lakeCarve = LakeDepth * LakeFactor(worldX, worldZ);
        float h = baseH - Mathf.Max(riverCarve, lakeCarve) + CliffRimElevation(worldX, worldZ);

        // Approach mound: raise (never lower) the ground right at the bridge's two ends so it
        // meets the deck without a step and the bridge's exposed underside/sides get buried.
        float moundY = BridgeApproachMoundHeight(worldX, worldZ);
        if (moundY > h) h = moundY;

        return h;
    }

    static void BuildSkyFogLighting(GameObject root, StringBuilder log)
    {
        // Art-direction pass (2026-08-13): dropped the "mossy_forest" HDRI skybox -- a real
        // photographed forest panorama sitting behind the (procedurally-built) trees read as an
        // actual photo backdrop, undermining the "this is all real 3D geometry" illusion whenever
        // it showed through a canopy gap. Replaced with Unity's built-in Skybox/Procedural (a
        // physically-based gradient sky, not a photo) tuned muted/dim so it never competes with the
        // canopy for attention -- the "deep forest with dappled light" mood now has to come entirely
        // from actual asset density (canopy coverage) and light placement (directional light angle/
        // shadows + Trilight ambient), per the brief.
        var skyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Stage/Greybox/Mat_Sky_Procedural.mat");
        if (skyMat == null)
        {
            skyMat = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(skyMat, "Assets/Stage/Greybox/Mat_Sky_Procedural.mat");
        }
        if (skyMat.HasProperty("_SunSize")) skyMat.SetFloat("_SunSize", 0.02f);
        if (skyMat.HasProperty("_SunSizeConvergence")) skyMat.SetFloat("_SunSizeConvergence", 5f);
        if (skyMat.HasProperty("_AtmosphereThickness")) skyMat.SetFloat("_AtmosphereThickness", 0.75f);
        if (skyMat.HasProperty("_SkyTint")) skyMat.SetColor("_SkyTint", new Color(0.55f, 0.68f, 0.62f)); // brighter cool green-white -- was reading as dusk-dim
        if (skyMat.HasProperty("_GroundColor")) skyMat.SetColor("_GroundColor", new Color(0.14f, 0.17f, 0.14f));
        if (skyMat.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", 1.15f);
        EditorUtility.SetDirty(skyMat);
        RenderSettings.skybox = skyMat;

        // Art-direction correction (2026-08-13, second pass): the previous values here ("深い森"
        // brief) made the whole scene read as dim/nighttime rather than "明るい木漏れ日が差し込む神秘
        //的な森" -- per direct feedback this went too dark/horror-forest. Brightened Trilight ambient
        // substantially (this is the fill light for everything NOT in direct sun -- it was crushing
        // shadow areas near-black) while keeping it cool/green-tinted so it still reads as forest-
        // filtered light rather than open daylight. The CONTRAST (dark forest interior vs. bright
        // sunlit patches) now comes from the directional light + shadows being strong against this
        // brighter-but-still-cool base, not from the base itself being dark.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.40f, 0.48f, 0.44f);
        RenderSettings.ambientEquatorColor = new Color(0.26f, 0.34f, 0.27f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.18f, 0.14f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        // Lightened and thinned -- "非常に薄い空気感" per the brief, not a dark haze. Still enough
        // for depth cueing at range, but no longer visibly darkening nearby geometry.
        RenderSettings.fogDensity = 0.011f;
        RenderSettings.fogColor = new Color(0.42f, 0.48f, 0.44f);

        BuildMoodVolume(root, log);
        log.AppendLine("Sky/fog set.");
    }

    // A single global URP Volume for color grading + bloom, so dappled light through the canopy
    // actually reads as bright highlights against darker shadow, and the overall grade leans
    // toward a cooler/desaturated "deep old forest" look instead of flat, uniformly-lit greens.
    // Deliberately mild -- this is meant to still be easy to see by, not a horror-forest look.
    static void BuildMoodVolume(GameObject root, StringBuilder log)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Stage/Forest"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Stage")) AssetDatabase.CreateFolder("Assets", "Stage");
            AssetDatabase.CreateFolder("Assets/Stage", "Forest");
        }

        string profPath = "Assets/Stage/Forest/VP_ForestMood.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
        bool isNewProfile = profile == null;
        if (isNewProfile)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profPath);
        }

        // Art-direction correction (2026-08-13, second pass): direct feedback was that the previous
        // -0.45 exposure / contrast 20 combination read as a "dark/horror forest", not the intended
        // "厳かでありながら明るい木漏れ日が差し込む神秘的な森". Brought exposure back up close to
        // neutral and eased contrast -- the dark-vs-bright CONTRAST this brief wants should come from
        // the directional light + shadows hitting a moderately-lit base (see the ambient/light
        // changes above), not from crushing the whole image dark in post.
        var colorAdj = profile.Has<ColorAdjustments>() ? profile.components.Find(c => c is ColorAdjustments) as ColorAdjustments : profile.Add<ColorAdjustments>();
        colorAdj.postExposure.Override(-0.1f);
        colorAdj.contrast.Override(12f);
        colorAdj.saturation.Override(-2f);
        colorAdj.colorFilter.Override(new Color(0.96f, 1f, 0.98f)); // faint cool-green filter, not a heavy tint

        // Bloom threshold lowered / intensity raised -- this is what actually sells "光の筋" (god-ray-
        // like glow) on sunlit water/moss/leaf highlights within URP's standard post stack, since a
        // true volumetric light-shaft effect isn't reliably scriptable here. Kept modest so it reads
        // as a soft glow around bright highlights, not a blown-out haze.
        var bloom = profile.Has<Bloom>() ? profile.components.Find(c => c is Bloom) as Bloom : profile.Add<Bloom>();
        bloom.threshold.Override(0.75f);
        bloom.intensity.Override(0.55f);
        bloom.scatter.Override(0.65f);

        // ShadowsMidtonesHighlights: much gentler shadow-darkening than the previous pass (-0.15 was
        // part of what crushed shadow areas toward black) -- shadows stay a bit cool-tinted for mood
        // but no longer nearly black, and highlights get a small warm lift so directly-sunlit patches
        // (the god-ray landing spots) pop.
        var smh = profile.Has<ShadowsMidtonesHighlights>() ? profile.components.Find(c => c is ShadowsMidtonesHighlights) as ShadowsMidtonesHighlights : profile.Add<ShadowsMidtonesHighlights>();
        smh.shadows.Override(new Vector4(0.90f, 0.95f, 1.0f, -0.04f));
        smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
        smh.highlights.Override(new Vector4(1.06f, 1.03f, 0.92f, 0.10f));

        EditorUtility.SetDirty(profile);
        if (isNewProfile) AssetDatabase.SaveAssets();

        var go = new GameObject("ForestMoodVolume");
        go.transform.SetParent(root.transform, false);
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.weight = 1f;
        volume.sharedProfile = profile;

        log.AppendLine("Mood volume (ColorAdjustments + Bloom) set up.");
    }

    static Terrain BuildTerrain(GameObject root, StringBuilder log)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Stage/Terrain"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Stage")) AssetDatabase.CreateFolder("Assets", "Stage");
            AssetDatabase.CreateFolder("Assets/Stage", "Terrain");
        }

        string dataPath = "Assets/Stage/Terrain/ForestTerrainData.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, dataPath);
        }
        data.heightmapResolution = HeightRes;
        data.size = new Vector3(TerrainWidth, TerrainHeightRange, TerrainLength);
        data.alphamapResolution = AlphaRes;

        // ---- Heightmap: rolling ground + carved winding channel. ----
        int hr = data.heightmapResolution;
        var heights = new float[hr, hr];
        for (int zi = 0; zi < hr; zi++)
        {
            float worldZ = OriginZ + (zi / (float)(hr - 1)) * TerrainLength;
            for (int xi = 0; xi < hr; xi++)
            {
                float worldX = OriginX + (xi / (float)(hr - 1)) * TerrainWidth;
                float raw = RawHeightAt(worldX, worldZ);
                heights[zi, xi] = Mathf.Clamp01((raw - OriginY) / TerrainHeightRange);
            }
        }
        data.SetHeights(0, 0, heights);

        // ---- Texture layers: dirt (bare-ground fallback), wet mud (right at the water's edge),
        // bare rock (steep/riverbank), mossy forest floor, and dry leaf litter. Five layers instead
        // of three so no single texture reads as a uniform carpet across a large area -- distributed
        // by an environmental rule (not pure randomness): rock on slopes/water edges, mud right at
        // the wet line, moss in damp/shaded low-lying "zones" (a slow-varying noise field, not per-
        // pixel noise, so moss reads as a patch of ground rather than static), leaf litter in the
        // complementary drier zones, with fine-grained noise breaking up each zone's own edge. ----
        var dirtTexRaw = PH + "forrest_ground_01/forrest_ground_01_diff_2k.jpg";
        var mudTexRaw = PH + "mud_forest/mud_forest_diff_2k.jpg";
        var rockTexRaw = PH + "dry_riverbed_rock/dry_riverbed_rock_diff_2k.jpg";
        var mossTexRaw = PH + "forest_ground_04/forest_ground_04_diff_2k.jpg";
        var leavesTexRaw = PH + "forest_leaves_04/forest_leaves_04_diff_2k.jpg";

        // Art-direction pass (2026-08-13): the source textures are all sunlit/dry-toned by
        // themselves, which is what made the whole Terrain read as "dry brown Unity forest" rather
        // than "深い古代森林の湿った地面". Recolored darker and cooler across the board -- dirt/mud
        // toward a damp near-black brown, and critically the rock layer (used on every steep
        // lake-adjacent slope, i.e. most of what the player actually sees as "the cliff") pulled
        // from tan/brown toward a cool dark wet gray, since that's the single biggest visible brown
        // surface in the scene. Baked into real texture copies (GetOrCreateTintedTexture) rather
        // than TerrainLayer.diffuseRemapMin/Max, which set without visible effect in this project's
        // render pipeline when tested.
        var dirtTex = GetOrCreateTintedTexture(dirtTexRaw, "Dirt_Tinted", new Color(0.01f, 0.01f, 0.01f), new Color(0.30f, 0.25f, 0.18f));
        var mudTex = GetOrCreateTintedTexture(mudTexRaw, "Mud_Tinted", new Color(0.01f, 0.01f, 0.01f), new Color(0.34f, 0.30f, 0.26f));
        // Confirmed via a pure-black diagnostic bake that the render pipeline DOES read layer
        // texture changes correctly (the wall went fully black as expected) -- the earlier
        // (0.40,0.42,0.45) max was simply not dark/distinct enough to read as different from the
        // original brown under this scene's strong direct sunlight. Pushed further down into a real
        // dark slate gray.
        var rockTex = GetOrCreateTintedTexture(rockTexRaw, "Rock_Tinted", new Color(0.01f, 0.012f, 0.015f), new Color(0.22f, 0.24f, 0.27f));
        var mossTex = GetOrCreateTintedTexture(mossTexRaw, "Moss_Tinted", new Color(0.01f, 0.015f, 0.01f), new Color(0.22f, 0.42f, 0.22f));
        var leavesTex = GetOrCreateTintedTexture(leavesTexRaw, "Leaves_Tinted", new Color(0.01f, 0.01f, 0.01f), new Color(0.34f, 0.28f, 0.20f));

        var dirtLayer = GetOrCreateLayer("TerrainLayer_Dirt", dirtTex, 6f);
        var mudLayer = GetOrCreateLayer("TerrainLayer_Mud", mudTex, 5f);
        var rockLayer = GetOrCreateLayer("TerrainLayer_Rock", rockTex, 7f);
        var mossLayer = GetOrCreateLayer("TerrainLayer_Moss", mossTex, 5.5f);
        var leavesLayer = GetOrCreateLayer("TerrainLayer_Leaves", leavesTex, 4.5f);
        data.terrainLayers = new[] { dirtLayer, mudLayer, rockLayer, mossLayer, leavesLayer };

        int ar = data.alphamapResolution;
        var alphas = new float[ar, ar, 5];
        for (int zi = 0; zi < ar; zi++)
        {
            float normZ = zi / (float)(ar - 1);
            float worldZ = OriginZ + normZ * TerrainLength;
            for (int xi = 0; xi < ar; xi++)
            {
                float normX = xi / (float)(ar - 1);
                float worldX = OriginX + normX * TerrainWidth;

                float slope = data.GetSteepness(normX, normZ);
                float rx = RiverX(worldZ);
                float hw = RiverHalfWidth(worldZ);
                float d = Mathf.Abs(worldX - rx);

                float riverCloseness = hw > 0.01f ? Mathf.Clamp01(1f - d / (hw + BankFalloff)) : 0f;
                float lakeCloseness = LakeFactor(worldX, worldZ);
                float waterCloseness = Mathf.Max(riverCloseness, lakeCloseness);

                // Threshold lowered from /28f -- moderate slopes (the rounded cliff-rim mounds
                // around the lake, ~18-25deg) were staying dirt-dominant and, in direct sun, still
                // read as a warm tan dome even after the dirt texture itself was darkened. Rock (now
                // a dark cool tint) taking over earlier on any real slope keeps that "brown dome"
                // look from reappearing purely from geometry that isn't quite steep enough to hit
                // the old threshold.
                float rockW = Mathf.Clamp01(slope / 20f);
                rockW = Mathf.Clamp01(rockW + waterCloseness * 0.28f);

                // Slow-varying "zone" noise: which ground cover dominates this general area.
                float zoneNoise = Mathf.PerlinNoise(worldX * 0.012f + 500f, worldZ * 0.012f + 500f);
                float mossZone = 1f - Mathf.Clamp01(Mathf.InverseLerp(0.35f, 0.65f, zoneNoise));
                float leavesZone = Mathf.Clamp01(Mathf.InverseLerp(0.45f, 0.75f, zoneNoise));

                float mossW = Mathf.Clamp01((mossZone * 0.6f + waterCloseness * 0.5f) * (1f - rockW));
                float leavesW = Mathf.Clamp01(leavesZone * (1f - rockW) * (1f - waterCloseness * 0.6f) * 0.85f);
                float mudW = Mathf.Clamp01(waterCloseness * (1f - rockW) * 0.4f);

                // Fine noise nudges the moss/leaves boundary so it isn't a clean zone edge.
                float fineNoise = Mathf.PerlinNoise(worldX * 0.09f + 1000f, worldZ * 0.09f + 1000f);
                float patch = (fineNoise - 0.5f) * 0.25f;
                mossW = Mathf.Clamp01(mossW + patch);
                leavesW = Mathf.Clamp01(leavesW - patch);

                // "美しい岩肌" pass: the lake-adjacent steep slopes are where rockW is pinned near
                // 1.0 (slope>=28 already maxes it, then +waterCloseness*0.28 on top), which used to
                // mean mossW was multiplied by (1-rockW)~=0 there -- i.e. the single largest visible
                // rock surface in the whole scene was pure bare rock with zero moss breakup. Carve a
                // patchy fraction of that rock weight over to moss (not uniformly -- a mid-frequency
                // noise field so it reads as moss growing in damp pockets, with plenty of bare rock
                // left between patches, never full coverage).
                if (lakeCloseness > 0.02f && rockW > 0.15f)
                {
                    float rockMossNoise = Mathf.PerlinNoise(worldX * 0.05f + 700f, worldZ * 0.05f + 700f);
                    float rockMossPatch = Mathf.Clamp01(rockMossNoise * 1.4f - 0.35f) * 0.55f * Mathf.Clamp01(lakeCloseness * 3f);
                    float takeFromRock = Mathf.Min(rockW * 0.7f, rockMossPatch); // never take more than 70% -- bare rock must stay visible
                    rockW -= takeFromRock;
                    mossW = Mathf.Clamp01(mossW + takeFromRock);
                }

                float usedSum = rockW + mudW + mossW + leavesW;
                float dirtW = Mathf.Clamp01(1f - usedSum); // bare-ground fallback fills whatever's left

                float sum = rockW + mudW + mossW + leavesW + dirtW;
                alphas[zi, xi, 0] = dirtW / sum;
                alphas[zi, xi, 1] = mudW / sum;
                alphas[zi, xi, 2] = rockW / sum;
                alphas[zi, xi, 3] = mossW / sum;
                alphas[zi, xi, 4] = leavesW / sum;
            }
        }
        data.SetAlphamaps(0, 0, alphas);

        EditorUtility.SetDirty(data);

        var terrainGO = Terrain.CreateTerrainGameObject(data);
        terrainGO.name = "Terrain";
        terrainGO.transform.SetParent(root.transform, false);
        terrainGO.transform.position = new Vector3(OriginX, OriginY, OriginZ);
        var terrain = terrainGO.GetComponent<Terrain>();
        terrain.detailObjectDistance = 0f;

        log.AppendLine("Terrain built: " + TerrainWidth + "x" + TerrainLength + ", heightmap " + hr + "^2.");
        return terrain;
    }

    // Bakes a recolored COPY of a source texture to disk and returns it, caching by output path.
    // TerrainLayer.diffuseRemapMin/Max exists but its effect on the actual rendered result in this
    // project's batchmode-rendered screenshots was NOT visible after setting it (verified via a
    // before/after re-screenshot of the same lake-ring angle -- the cliff wall was still exactly as
    // brown as before) -- rather than trust an API that isn't visibly taking effect, this bakes the
    // recolor directly into new texture pixels so it's guaranteed correct regardless of which
    // terrain shader/pipeline path actually reads (or ignores) the remap fields.
    static Texture2D GetOrCreateTintedTexture(string srcAssetPath, string outName, Color remapMin, Color remapMax)
    {
        string outPath = "Assets/Stage/Terrain/TintedTextures/" + outName + ".png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        if (existing != null) return existing;

        var importer = AssetImporter.GetAtPath(srcAssetPath) as TextureImporter;
        bool wasReadable = importer != null && importer.isReadable;
        if (importer != null && !wasReadable) { importer.isReadable = true; importer.SaveAndReimport(); }

        var src = AssetDatabase.LoadAssetAtPath<Texture2D>(srcAssetPath);
        var pixels = src.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            pixels[i] = new Color(
                Mathf.Lerp(remapMin.r, remapMax.r, c.r),
                Mathf.Lerp(remapMin.g, remapMax.g, c.g),
                Mathf.Lerp(remapMin.b, remapMax.b, c.b),
                c.a);
        }
        var outTex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        outTex.SetPixels(pixels);
        outTex.Apply();

        string outDirFull = Path.Combine(Application.dataPath, "Stage", "Terrain", "TintedTextures");
        Directory.CreateDirectory(outDirFull);
        File.WriteAllBytes(Path.Combine(outDirFull, outName + ".png"), outTex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(outTex);

        if (importer != null && !wasReadable) { importer.isReadable = false; importer.SaveAndReimport(); }

        AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
        var resultImporter = AssetImporter.GetAtPath(outPath) as TextureImporter;
        if (resultImporter != null) { resultImporter.sRGBTexture = true; resultImporter.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    static TerrainLayer GetOrCreateLayer(string name, Texture2D tex, float tileSize, Texture2D normalTex = null, Color? remapMin = null, Color? remapMax = null)
    {
        string path = "Assets/Stage/Terrain/" + name + ".terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = tex;
        layer.tileSize = new Vector2(tileSize, tileSize);
        if (normalTex != null)
        {
            SetTextureImporterType(normalTex, TextureImporterType.NormalMap);
            layer.normalMapTexture = normalTex;
        }
        // diffuseRemapMin/Max recolors the source texture's tonal range without needing a new
        // asset -- used to pull the whole terrain palette from "dry sunlit brown" toward "wet dark
        // forest floor" (cooler, darker, less saturated orange) per the art-direction brief. Left
        // at Unity's default (0,0,0)-(1,1,1) i.e. no change when not specified.
        layer.diffuseRemapMin = remapMin ?? new Color(0f, 0f, 0f, 0f);
        layer.diffuseRemapMax = remapMax ?? new Color(1f, 1f, 1f, 1f);
        EditorUtility.SetDirty(layer);
        return layer;
    }

    // ---- Water: a winding ribbon mesh following the same channel shape as the terrain carve.
    // Extends from deep forest all the way down past the bridge and into the lake (RiverWaterZ0
    // is deep inside the lake's radius) so it visually overlaps the lake water mesh with no gap
    // -- the river and lake read as one continuous body of water. ----
    static void BuildWater(GameObject root, Terrain terrain, StringBuilder log)
    {
        var mesh = new Mesh { name = "RiverWaterMesh" };
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var uvs = new List<Vector2>();

        float step = 2f;
        int stationCount = 0;
        float v = 0f;
        for (float z = RiverWaterZ0; z <= RiverZ1; z += step)
        {
            float rx = RiverX(z);
            float hw = RiverHalfWidth(z) * 0.82f;
            float waterY = WaterYAt(z);

            verts.Add(new Vector3(rx - hw, waterY, z));
            verts.Add(new Vector3(rx + hw, waterY, z));
            uvs.Add(new Vector2(0f, v));
            uvs.Add(new Vector2(1f, v));
            v += step * 0.3f;

            if (stationCount > 0)
            {
                int b = (stationCount - 1) * 2;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
            stationCount++;
        }
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("RiverWater");
        go.transform.SetParent(root.transform, false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = GetOrCreateMat("Mat_River", null, Vector2.one);
        mat.color = new Color(0.14f, 0.30f, 0.28f, 0.72f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.85f);
        SetTransparent(mat);
        mr.sharedMaterial = mat;

        log.AppendLine("Water ribbon built, " + stationCount + " stations.");
    }

    // ---- Lake water: irregular polygon fan, edge found via FindShoreAtAngle so it lines up
    // exactly with the terrain's carved shoreline. Two layers: a deeper, more saturated main
    // body, and a lighter, more transparent "shallow shelf" ring near the surface/shore so the
    // lakebed shows through at the edges the way the reference photo's clear shallows do. ----
    static void BuildLakeWater(GameObject root, StringBuilder log)
    {
        var mesh = new Mesh { name = "LakeWaterMesh" };
        var verts = new List<Vector3> { new Vector3(LakeCenterX, LakeWaterY, LakeCenterZ) };
        var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };

        int n = 64;
        for (int i = 0; i <= n; i++)
        {
            float ang = i / (float)n * 360f;
            Vector2 shore = FindShoreAtAngle(ang);
            Vector2 dir = (shore - new Vector2(LakeCenterX, LakeCenterZ)).normalized;
            float r = Vector2.Distance(shore, new Vector2(LakeCenterX, LakeCenterZ)) * 0.94f; // inset so water sits inside the bank
            float x = LakeCenterX + dir.x * r;
            float z = LakeCenterZ + dir.y * r;
            verts.Add(new Vector3(x, LakeWaterY, z));
            uvs.Add(new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.y * 0.5f));
        }
        var tris = new List<int>();
        for (int i = 1; i <= n; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("LakeWater");
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mat = GetOrCreateMat("Mat_Lake", null, Vector2.one);
        // Sapphire blue with a little teal, not the previous emerald/teal-dominant mix -- per the
        // art-direction brief the lake needs to read as blue first, not green, so it visually
        // separates from the surrounding green forest instead of blending into it. High smoothness
        // is what actually sells "glowing" here (crisp sky/canopy reflection + dark forest
        // surroundings), not an emissive color -- an emissive lake was explicitly ruled out.
        mat.color = new Color(0.035f, 0.24f, 0.52f, 0.88f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.95f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.15f);
        SetTransparent(mat);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;

        // Shallow shelf: a second, lighter/more transparent ring hugging the shoreline just
        // above the main water so the (sparse) lakebed rocks show through near the edges.
        var shelfMesh = new Mesh { name = "LakeShallowShelfMesh" };
        var sVerts = new List<Vector3>(); var sTris = new List<int>(); var sUVs = new List<Vector2>();
        var innerRing = new Vector3[n + 1]; var outerRing = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float ang = i / (float)n * 360f;
            Vector2 shore = FindShoreAtAngle(ang);
            Vector2 center = new Vector2(LakeCenterX, LakeCenterZ);
            Vector2 dir = (shore - center).normalized;
            float shoreR = Vector2.Distance(shore, center);
            outerRing[i] = new Vector3(center.x + dir.x * shoreR * 0.98f, LakeWaterY + 0.05f, center.y + dir.y * shoreR * 0.98f);
            innerRing[i] = new Vector3(center.x + dir.x * shoreR * 0.80f, LakeWaterY + 0.03f, center.y + dir.y * shoreR * 0.80f);
        }
        AddRibbon(sVerts, sTris, sUVs, innerRing, outerRing, 4f);
        shelfMesh.SetVertices(sVerts); shelfMesh.SetTriangles(sTris, 0); shelfMesh.SetUVs(0, sUVs);
        shelfMesh.RecalculateNormals(); shelfMesh.RecalculateBounds();
        var shelfGo = new GameObject("LakeShallowShelf");
        shelfGo.transform.SetParent(root.transform, false);
        shelfGo.AddComponent<MeshFilter>().sharedMesh = shelfMesh;
        var shelfMat = GetOrCreateMat("Mat_LakeShallow", null, Vector2.one);
        shelfMat.color = new Color(0.18f, 0.48f, 0.58f, 0.55f); // lighter blue-teal shallows, still cool not sandy
        if (shelfMat.HasProperty("_Smoothness")) shelfMat.SetFloat("_Smoothness", 0.85f);
        SetTransparent(shelfMat);
        shelfMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        shelfGo.AddComponent<MeshRenderer>().sharedMaterial = shelfMat;

        // Reflection Probe centered over the lake -- this is what actually puts the surrounding dark
        // cliffs/canopy and sky into the water's specular reflection (a flat _Smoothness-only
        // material only picks up the skybox by default without one). Baked once at build time; the
        // scene is static so a realtime probe isn't needed. This is the "水面Reflection" half of the
        // "湖が光っているような印象" brief -- combined with the water's own high smoothness and dark
        // surrounding palette, not an emissive material.
        var probeGo = new GameObject("LakeReflectionProbe");
        probeGo.transform.SetParent(root.transform, false);
        probeGo.transform.position = new Vector3(LakeCenterX, LakeWaterY + 3f, LakeCenterZ);
        var probe = probeGo.AddComponent<ReflectionProbe>();
        probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
        probe.size = new Vector3((LakeRadiusX + 10f) * 2f, 30f, (LakeRadiusZ + 10f) * 2f);
        probe.center = Vector3.zero;
        probe.resolution = 128;
        probe.importance = 1;
        probe.intensity = 1f;
        // NOT baked here -- this runs early in the build order, before the cliff walls/trees/rocks
        // that should actually APPEAR in the reflection exist yet. The bake call is deferred to the
        // very end of Run(), after every other Build* call, so the probe captures the finished scene.

        log.AppendLine("Lake water mesh built (main + shallow shelf + reflection probe placed).");
    }

    // ---- Lake cliff wall: a continuous rock wall following the shoreline, rising from the
    // water up to the rim. This is what makes most of the lake genuinely unclimbable -- both
    // visually (a real rock wall, not a dirt slope with a texture) and physically (a MeshCollider
    // matching it, on top of the terrain's own steep slope there). Naturally low/absent across
    // the two gentle zones (inlet, stairs) since it's built from the same shore/rim points that
    // LakeFactor already treats gently there. Lower band is mossy/damp, upper band drier bare
    // rock, plus scattered big boulder accents for "岩の張り出し" variety. ----
    static void BuildLakeCliffWall(GameObject root, Terrain terrain, StringBuilder log)
    {
        var wallRoot = new GameObject("LakeCliffWall");
        wallRoot.transform.SetParent(root.transform, false);
        // Same baked dark/cool recolor as the Terrain's own rock layer (see GetOrCreateTintedTexture
        // in BuildTerrainTextures) so the wall's waterline band and the Terrain slope above/around it
        // read as one consistent wet dark rock material instead of two different rock tones.
        var rockTex = GetOrCreateTintedTexture(PH + "dry_riverbed_rock/dry_riverbed_rock_diff_2k.jpg", "Rock_Tinted", new Color(0.015f, 0.017f, 0.02f), new Color(0.40f, 0.42f, 0.45f));
        var center = new Vector2(LakeCenterX, LakeCenterZ);

        int n = 90;
        var botPts = new Vector3[n + 1];
        var midPts = new Vector3[n + 1];
        var topPts = new Vector3[n + 1];
        // StoneNoise runs at a 0.45/world-unit frequency (shared helper, used elsewhere too), but
        // adjacent ring stations here are only ~1.5m apart -- more than half a Perlin period --
        // so the raw jitter could swing by close to its full +-0.35 range station-to-station,
        // folding the ribbon into a near-degenerate sliver at the fold. RecalculateNormals then
        // gives that sliver a wildly different (often near-light-facing) normal, which read as a
        // bright/white jagged triangle cutting across the wall's top edge in QA screenshots. A
        // simple per-station rate clamp keeps the irregular-rock look while forbidding folds sharp
        // enough to self-intersect.
        float prevJitter = 0f;
        const float maxJitterDeltaPerStation = 0.05f; // tightened from 0.12f -- that still left visible white fold-artifact triangles in a QA re-screenshot (T6_tangent_ang270.png), just fewer/smaller than before
        for (int i = 0; i <= n; i++)
        {
            float ang = i / (float)n * 360f;
            Vector2 shore = FindShoreAtAngle(ang);
            Vector2 dir = (shore - center).normalized;
            float shoreR = Vector2.Distance(shore, center);
            // Rim pushed further out specifically in RockWall zones (1.14x -> up to 1.24x) so the
            // wall's own geometry reaches further inland there too, consistent with those zones
            // reading as a taller, more substantial rock mass than the default arcs.
            var stationZone = GetShoreZone(ang);
            float rimMul = stationZone.type == ShoreZoneType.RockWall ? 1.24f : 1.14f;
            Vector2 rim = center + dir * (shoreR * rimMul);
            float rimY = SampleWorldHeight(terrain, rim.x, rim.y);
            float rawJitter = StoneNoise(shore.x, shore.y, 61f) * 0.28f;
            float jitter = Mathf.Clamp(rawJitter, prevJitter - maxJitterDeltaPerStation, prevJitter + maxJitterDeltaPerStation);
            prevJitter = jitter;

            // Sample the ACTUAL terrain at the midpoint (rather than linearly interpolating
            // between shore and rim) so this wall hugs the real heightmap shape -- otherwise the
            // wall mesh and the terrain surface disagree slightly and rocks/props anchored to the
            // terrain can appear to float relative to the (more prominent) wall mesh.
            Vector2 midXZ = Vector2.Lerp(shore, rim, 0.4f);
            float midY = SampleWorldHeight(terrain, midXZ.x, midXZ.y);

            // Collapse the wall down to a low, unobtrusive ridge across the two gentle zones (the
            // river inlet and the stairs) so it never towers up and hides the stairs or blocks the
            // inlet view -- the terrain's own gentle slope already does the real work there.
            float gentle = LakeGentleWeight(ang);
            float suppress = Mathf.Clamp01(Mathf.InverseLerp(0.25f, 0.65f, gentle));
            float lowRidgeY = Mathf.Max(LakeWaterY + 0.15f, midY);

            // Apply the SAME suppress blend to all three bands (bottom included -- previously only
            // mid/top were suppressed, so even at full suppression the wall's bottom edge stayed
            // pinned at the deep water level and the "suppressed" wall was still several meters
            // tall, fully hiding the stairs behind it). At full suppression all three collapse to
            // nearly the same height -- the wall shrinks to a negligible sliver, not just a shorter
            // version of itself.
            // Scale how far mid/top rise ABOVE the grounded ridge base by the named shore zone's
            // heightMul (bottom left untouched -- it's already pinned near water level) -- taller
            // for RockWall zones, shorter for LowMossyBank, so the wall's silhouette itself varies
            // by bank type, not just the props scattered along it.
            float zoneHeightMul = GetShoreZone(ang).heightMul;
            // Continuous secondary relief (low-frequency, ~2-3 cycles/360 deg) layered on top of the
            // named zones so the ARCS BETWEEN them aren't perfectly flat either -- per spec, the
            // shore should read as irregularly undulating all the way around (low rock bank -> rise
            // -> big outcrop -> cliff -> low bank again), not just at the 6 discrete named zones.
            // Suppressed by the same `suppress` blend the wall's own height already uses, so the
            // gameplay-critical inlet/stairs gentle zones stay untouched.
            // Strengthened further (0.12/0.07 -> 0.20/0.12, plus a third higher-frequency term for
            // finer irregularity) -- feedback was that even the previous pass still read as too
            // uniform between the named zones.
            float reliefWave = Mathf.Sin(ang * Mathf.Deg2Rad * 2.3f + 0.7f) * 0.20f + Mathf.Sin(ang * Mathf.Deg2Rad * 3.7f + 2.1f) * 0.12f + Mathf.Sin(ang * Mathf.Deg2Rad * 6.1f + 4.4f) * 0.06f;
            zoneHeightMul = Mathf.Clamp(zoneHeightMul * (1f + reliefWave * (1f - suppress)), 0.50f, 1.55f);
            float botFinalY = Mathf.Lerp(LakeWaterY - 0.8f, lowRidgeY - 0.3f, suppress);
            float midFinalY = lowRidgeY + (Mathf.Lerp(midY, lowRidgeY, suppress) - lowRidgeY) * zoneHeightMul;
            float topFinalY = lowRidgeY + (Mathf.Lerp(rimY + 0.25f, lowRidgeY, suppress) - lowRidgeY) * zoneHeightMul;

            botPts[i] = new Vector3(shore.x, botFinalY, shore.y);
            midPts[i] = new Vector3(midXZ.x + jitter * (1f - suppress), midFinalY, midXZ.y + jitter * (1f - suppress));
            topPts[i] = new Vector3(rim.x, topFinalY, rim.y);
        }

        var lVerts = new List<Vector3>(); var lTris = new List<int>(); var lUVs = new List<Vector2>();
        AddRibbon(lVerts, lTris, lUVs, botPts, midPts, 10f);
        var lowerMesh = new Mesh { name = "LakeCliffLowerMesh" };
        lowerMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        lowerMesh.SetVertices(lVerts); lowerMesh.SetTriangles(lTris, 0); lowerMesh.SetUVs(0, lUVs);
        lowerMesh.RecalculateNormals(); lowerMesh.RecalculateBounds();

        var uVerts = new List<Vector3>(); var uTris = new List<int>(); var uUVs = new List<Vector2>();
        AddRibbon(uVerts, uTris, uUVs, midPts, topPts, 10f);
        var upperMesh = new Mesh { name = "LakeCliffUpperMesh" };
        upperMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        upperMesh.SetVertices(uVerts); upperMesh.SetTriangles(uTris, 0); upperMesh.SetUVs(0, uUVs);
        upperMesh.RecalculateNormals(); upperMesh.RecalculateBounds();

        var lowerGo = new GameObject("LakeCliffLowerMossy");
        lowerGo.transform.SetParent(wallRoot.transform, false);
        lowerGo.AddComponent<MeshFilter>().sharedMesh = lowerMesh;
        var lowerMat = GetOrCreateMat("Mat_LakeCliffLower", rockTex, new Vector2(1f, 1f));
        // rockTex is now the already-dark/cool tinted copy -- this multiply just adds a faint mossy
        // push and wet sheen on top, not a second full darkening pass (double-darkening the already-
        // tinted texture would crush it toward black).
        lowerMat.color = new Color(0.55f, 0.62f, 0.55f);
        if (lowerMat.HasProperty("_Smoothness")) lowerMat.SetFloat("_Smoothness", 0.55f);
        lowerMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        lowerGo.AddComponent<MeshRenderer>().sharedMaterial = lowerMat;

        // NOTE: the upper-band visual mesh ("LakeCliffUpperRock") was removed 2026-08-12 -- it read
        // as visually out of place (flat gray band) against the terrain slope beneath it. The rim
        // side of the wall is now just the terrain's own steep slope. upperMesh's DATA is still
        // generated and still feeds the collider below, so the cliff stays just as unclimbable as
        // before -- only its own separate visual rendering was dropped.

        // Physical collider matching the wall -- belt-and-suspenders alongside the terrain's own
        // steep slope so the cliff is solid even at grazing angles.
        var colGo = new GameObject("LakeCliffCollider");
        colGo.transform.SetParent(wallRoot.transform, false);
        var combined = new[]
        {
            new CombineInstance { mesh = lowerMesh, transform = Matrix4x4.identity },
            new CombineInstance { mesh = upperMesh, transform = Matrix4x4.identity },
        };
        var colMesh = new Mesh { name = "LakeCliffColliderMesh" };
        colMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        colMesh.CombineMeshes(combined, true, true);
        var mc = colGo.AddComponent<MeshCollider>();
        mc.sharedMesh = colMesh;
        mc.convex = false;

        // Sparse, deliberate rock accents -- NOT wall coverage. A prior pass covered the entire
        // cliff face edge-to-edge with hundreds of overlapping rocks per direct feedback that it
        // read as a landslide/quarry/rock pile, crowded the lake, and buried the bridge ends. The
        // brief now is the opposite: Terrain + a few large geologically-justified rock accents +
        // visible soil/moss, with the lake reading as OPEN. Rocks are placed only where there's a
        // reason for them (undercut banks, boulder-overhang points, waterfall flanks handled
        // separately in BuildWaterfalls) rather than blanketing every angle/radius, and pulled back
        // from the immediate shoreline so the water's edge stays mostly clear, open ground/soil --
        // except at the single dedicated BoulderOverhang zone, which is deliberately allowed right
        // to the waterline as the one "岩が水面まで張り出している" accent point.
        var boulder = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "boulder_01/boulder_01_2k.fbx");
        // see LoadIndividualMossRocks() for why this is no longer the raw rock_moss_set_01/02 FBX
        var mossSets = LoadIndividualMossRocks();
        // lichen_rock (Poly Haven) is a texture-only asset (no mesh) -- downloaded earlier but never
        // used anywhere in the stage. Applied here as an alternate retexture for surface variety.
        var lichenTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PH + "lichen_rock/lichen_rock_diff_2k.jpg");
        var lichenMat = lichenTex != null ? GetOrCreateMat("Mat_LichenRock", lichenTex, Vector2.one * 1.5f) : null;
        const float boulderTopLocal = 0.930f;
        // Weathered root cluster, used below at the base of each hero rock face/HeroCoastRocks so
        // those formations read as "岩を掴む木の根" (roots gripping the rock) rather than a bare
        // rock standing alone -- previously roots only appeared as flat ground litter.
        var rootClusterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "root_cluster_02/root_cluster_02_decimated.fbx");
        float rootClusterBottomY = GetPrefabBottomLocalY(rootClusterPrefab);
        var rng = new System.Random(7711);
        int placed = 0;
        int cliffBoulderIdx = 0;

        // Explicit world-space bridge keep-clear box (angle-based exclusion alone left occasional
        // rocks reaching the bridge's own corners) -- generous margin beyond the deck's actual
        // footprint so the approach, arch, and both ends stay completely unobstructed.
        float bridgeCenterXForBoulders = RiverX(BridgeCenterZ);
        float bridgeKeepClearHalfX = MeshyBridgeWorldHalfSpan + 6f;
        float bridgeKeepClearZ0 = BridgeZ0 - 6f, bridgeKeepClearZ1 = BridgeZ1 + 6f;

        for (int i = 0; i < 70; i++)
        {
            float ang = (float)rng.NextDouble() * 360f;
            if (LakeGentleWeight(ang) > 0.25f) continue;
            var zone = GetShoreZone(ang);
            if (zone.type == ShoreZoneType.RootBank && rng.NextDouble() < 0.7) continue; // roots dominate this zone via LakeShoreDressing instead
            if (zone.type != ShoreZoneType.BoulderOverhang && rng.NextDouble() < 0.35) continue; // thin the Default arcs further -- soil/terrain/moss should read as the majority surface there, rocks as accents
            Vector2 shore = FindShoreAtAngle(ang);
            Vector2 dir = (shore - center).normalized;
            float shoreR = Vector2.Distance(shore, center);

            // Pulled back from the water's edge (was 1.02-1.6x) so the shoreline itself stays open
            // -- except the BoulderOverhang zone, which keeps a dedicated close-to-water reach as
            // the one deliberate "rock overhangs the water" landmark point.
            bool atWaterline = zone.type == ShoreZoneType.BoulderOverhang && rng.NextDouble() < 0.5;
            float radiusT = (float)rng.NextDouble();
            float anchorR = atWaterline ? shoreR * (1.02f + (float)rng.NextDouble() * 0.1f) : shoreR * Mathf.Lerp(1.2f, 1.65f, radiusT);
            Vector2 anchorP = center + dir * anchorR;

            if (Mathf.Abs(anchorP.x - bridgeCenterXForBoulders) < bridgeKeepClearHalfX && anchorP.y > bridgeKeepClearZ0 && anchorP.y < bridgeKeepClearZ1) continue;

            float bigChance = zone.type == ShoreZoneType.BoulderOverhang ? 0.8f : zone.type == ShoreZoneType.LowMossyBank ? 0.2f : 0.45f;
            bool big = rng.NextDouble() < bigChance;
            var prefab = big ? boulder : mossSets[rng.Next(mossSets.Length)];
            if (prefab == null) continue;
            // Fewer, bigger, more individually striking rocks -- quality over quantity now that
            // density itself no longer does the work of "looking like a real formation".
            float sizeBoost = zone.type == ShoreZoneType.BoulderOverhang ? 1.3f : 1f;
            float scale = (big ? (3.0f + (float)rng.NextDouble() * 2.6f) : (1.1f + (float)rng.NextDouble() * 1.3f)) * sizeBoost;
            float topLocal = big ? boulderTopLocal : GetPrefabTopLocalY(prefab); // individual moss rocks vary in size now, so this is measured per-instance rather than one shared constant

            // Raycast-based placement (PlaceBoulderEmbedded, 接地ルール) -- tilts to match the real
            // local surface normal, so a rock embedded in a steep section of wall follows that
            // slope's actual angle instead of always standing perfectly upright regardless of what
            // it's resting against.
            float exposeFrac = 1f - (0.25f + (float)rng.NextDouble() * 0.15f); // embedFrac is "how much is buried", the old exposeFrac was "how much shows" -- same 25-40%-exposed target, inverted
            var inst = PlaceBoulderEmbedded(prefab, wallRoot.transform, terrain, anchorP.x, anchorP.y, scale, exposeFrac, rng, "CliffBoulder_" + cliffBoulderIdx++);
            if (inst == null) continue;
            if (zone.type == ShoreZoneType.RockWall && lichenMat != null && rng.NextDouble() < 0.5)
                foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.sharedMaterial = lichenMat;
            var col = inst.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.5f * topLocal, 0f);
            col.size = new Vector3(1.2f * topLocal, topLocal, 1.2f * topLocal);
            placed++;
        }

        // NOTE: rock_face_01/rock_face_02 ("HeroRockFace_210"/"HeroRockFace_305") were REMOVED
        // 2026-08-14 per explicit instruction -- repeated placement failures (backwards-facing,
        // floating off the terrain, never reading as naturally embedded) made them unusable despite
        // several rounds of fixes. Do NOT reintroduce rock_face_01/rock_face_02 into any Environment
        // placement code, and do NOT include them as candidates in any future
        // Rock/Cliff/RockFace/Boulder auto-placement search. The source FBX/textures are still on
        // disk (Assets/ExternalAssets/PolyHaven/rock_face_01/, rock_face_02/) and are NOT deleted --
        // only their placement in this generator was removed. See ASSET_LICENSES.md 注記3 for the
        // updated status.
        var coastalCliffPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "coastal_cliff_01/coastal_cliff_01_decimated.fbx");
        float coastalCliffBottomY = GetPrefabBottomLocalY(coastalCliffPrefab);

        // coastal_cliff_01: a genuinely wide (92m native) exposed cliff-strata band, used as the
        // backdrop for the single most important viewpoint -- directly across the water from the
        // bridge (180 deg). Scaled down to a still-substantial ~40m-wide band (not full native
        // size, which would dwarf the whole lake) and embedded well into the existing terrain slope
        // there so it reads as the terrain's own rock stratum showing through, not a dropped-in prop.
        if (coastalCliffPrefab != null)
        {
            Vector2 shoreC = FindShoreAtAngle(180f);
            Vector2 dirC = (shoreC - center).normalized;
            float shoreRC = Vector2.Distance(shoreC, center);
            Vector2 anchorC = center + dirC * (shoreRC * 1.28f);
            float scaleC = 0.44f;
            Vector2 tangentC = new Vector2(-dirC.y, dirC.x);
            float halfWidthWorld = 92f * scaleC * 0.5f; // native width * scale

            // This band is ~40m wide -- a single raycast/sample at its center can't guarantee the
            // whole footprint is grounded (the far left/right extremities may sit over different
            // local terrain than the center). Raycast at several points along its actual width span
            // and anchor to the LOWEST hit, per CLAUDE.md 接地ルール #5 (wide assets need multi-point
            // sampling, not one point).
            float lowestY = float.MaxValue;
            Vector3 lowestNormal = Vector3.up;
            for (int wi = -3; wi <= 3; wi++)
            {
                float t = wi / 3f * halfWidthWorld * 0.85f; // stay a bit inside the true extremities
                Vector2 samplePt = anchorC + tangentC * t;
                TryGetTerrainSurface(terrain, samplePt.x, samplePt.y, out Vector3 hp, out Vector3 hn);
                if (hp.y < lowestY) { lowestY = hp.y; lowestNormal = hn; }
            }

            var instC = PlaceCliffEmbedded(coastalCliffPrefab, wallRoot.transform, terrain, anchorC.x, anchorC.y, true, scaleC, 0.5f, "HeroCoastalCliffBand");
            if (instC != null)
            {
                // Override Y with the span-wide lowest point (PlaceCliffEmbedded only sampled the
                // center) so the whole width is guaranteed grounded, not just the center.
                var p = instC.transform.position;
                p.y = Mathf.Min(p.y, lowestY - coastalCliffBottomY * scaleC * 0.3f);
                instC.transform.position = p;
                placed++;
            }

            // The source asset is a naturally flat/plank-shaped scanned cliff STRIP (92m wide x only
            // 11m tall) -- even fully grounded at its lowest point, its front (lake-facing) edge can
            // still read as an overhang with visible open space beneath when viewed from near water
            // level looking up (confirmed via a FALL_ang165 screenshot). Bridge the visual gap
            // directly with a row of large boulders along its lower-front edge, each individually
            // raycast-placed (not sharing one guessed height).
            for (int bi = -2; bi <= 2; bi++)
            {
                if (boulder == null) continue;
                float bt = bi / 2f * halfWidthWorld * 0.75f;
                Vector2 bXZ = anchorC + tangentC * bt - dirC * (1f + (float)rng.NextDouble() * 1.5f); // pulled slightly toward the lake, in front of the band's base
                float bScale = 2.0f + (float)rng.NextDouble() * 1.3f;
                var bInst = PlaceBoulderEmbedded(boulder, wallRoot.transform, terrain, bXZ.x, bXZ.y, bScale, 0.4f, rng, "HeroCoastalCliffBase_" + bi);
                if (bInst != null) placed++;
            }
        }

        var coastRocksPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "coast_rocks_01/coast_rocks_01_decimated.fbx");
        if (coastRocksPrefab != null)
        {
            int boIdx = System.Array.FindIndex(ShoreZones, z2 => z2.type == ShoreZoneType.BoulderOverhang);
            if (boIdx >= 0)
            {
                var bo = ShoreZones[boIdx];
                Vector2 shoreB = FindShoreAtAngle(bo.ang);
                Vector2 dirB = (shoreB - center).normalized;

                var instB = (GameObject)PrefabUtility.InstantiatePrefab(coastRocksPrefab, wallRoot.transform);
                instB.name = "HeroCoastRocks";
                // FIXED to the user's own manual placement in the Editor (previously the auto-
                // computed anchor left part of it jutting unnaturally over open water) -- captured
                // directly from the scene file's PrefabInstance override values on 2026-08-13 and
                // must NEVER be replaced back with a procedural anchor/height computation, or the
                // next rebuild silently discards the fix.
                instB.transform.localPosition = new Vector3(26.91f, 0f, -21.7f);
                instB.transform.localRotation = new Quaternion(-0.032216746f, -0.47832957f, -0.0028999303f, 0.8775845f);
                instB.transform.localScale = Vector3.one * 0.34f;
                placed++;

                placed += PlaceSupportCluster(wallRoot, terrain, boulder, mossSets, lichenMat, new Vector2(instB.transform.position.x, instB.transform.position.z), dirB, 6f, rng, rootClusterPrefab, rootClusterBottomY);
            }
        }

        log.AppendLine("Lake cliff wall built (" + n + " stations, " + placed + " boulder accents).");
    }

    // Small supporting cluster of mid-rocks + moss accents scattered tightly around a hero
    // formation's own anchor point -- "one big rock + surrounding mid rocks/pebbles/moss", per
    // spec, rather than the hero formation standing alone. clusterRadius is roughly the hero
    // formation's own footprint size, so the cluster hugs its base instead of spreading wide.
    static int PlaceSupportCluster(GameObject parent, Terrain terrain, GameObject boulder, GameObject[] mossSets, Material lichenMat, Vector2 anchor, Vector2 outDir, float clusterRadius, System.Random rng, GameObject rootPrefab = null, float rootBottomY = 0f)
    {
        int n = 4 + rng.Next(3); // 4-6 accents
        int placed = 0;
        for (int i = 0; i < n; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = clusterRadius * (0.3f + (float)rng.NextDouble() * 0.7f);
            Vector2 p = anchor + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            // One root instance per cluster reads as "roots gripping the rock" at the formation's
            // own base (spec: "岩を掴む木の根") -- kept rare (one slot out of the batch, not every
            // accent) so it stays a detail rather than crowding out the rock/moss accents.
            bool useRoot = rootPrefab != null && i == 0 && rng.NextDouble() < 0.55;
            bool big = !useRoot && rng.NextDouble() < 0.35;
            var prefab = useRoot ? rootPrefab : (big ? boulder : mossSets[rng.Next(mossSets.Length)]);
            if (prefab == null) continue;
            float topLocal = big ? 0.930f : GetPrefabTopLocalY(prefab);
            float scale = useRoot ? (0.6f + (float)rng.NextDouble() * 0.5f) : big ? (0.5f + (float)rng.NextDouble() * 0.6f) : (0.18f + (float)rng.NextDouble() * 0.35f);
            float baseY = SampleWorldHeightConservative(terrain, p.x, p.y, 0.5f * scale);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            if (useRoot)
            {
                inst.name = "HeroClusterRoot_" + i;
                inst.transform.localScale = Vector3.one * scale;
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                inst.transform.position = new Vector3(p.x, baseY - rootBottomY * scale, p.y);
                placed++;
                continue;
            }
            // Sink the pivot noticeably below the sampled ground point (rather than resting the
            // object's measured "top" right at it) -- these read as broken-off/settled debris around
            // the formation's base, not neatly placed rocks.
            float embed = topLocal * scale * 0.3f;
            inst.name = "HeroClusterRock_" + i;
            inst.transform.localScale = Vector3.one * scale;
            inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            inst.transform.position = new Vector3(p.x, baseY - embed, p.y);
            if (!big && lichenMat != null && rng.NextDouble() < 0.4)
                foreach (var r2 in inst.GetComponentsInChildren<Renderer>()) r2.sharedMaterial = lichenMat;
            placed++;
        }
        return placed;
    }

    // ---- Waterfalls cascading down the cliff into the lake -- one wider main fall plus several
    // thin ones, only where the cliff is actually steep (never over the inlet/stairs). Each is a
    // simple vertical ribbon with horizontal noise wobble (not a straight sheet) plus a flattened
    // white disc where it meets the water for foam/splash. ----
    static void BuildWaterfalls(GameObject root, Terrain terrain, StringBuilder log)
    {
        var wfRoot = new GameObject("Waterfalls");
        wfRoot.transform.SetParent(root.transform, false);
        var center = new Vector2(LakeCenterX, LakeCenterZ);

        // 2026-08-14 REDESIGN (user request): replaced the previous 5 scattered falls with a single
        // grand "sacred" waterfall -- this is planned as the game's potion-source landmark (ゴブリン
        // がポーションを汲みに戻ってくる場所), so it needs to read as ONE unmistakable, important
        // focal point rather than ambient cliff decoration. Placed at 190deg, matching
        // HeroCoastalCliffBand's own 180deg backdrop (already documented as "the single most
        // important viewpoint -- directly across the water from the bridge") so the fall pours down
        // the face of that existing hero cliff rather than competing with it for a separate spot.
        var falls = new (float ang, float width)[]
        {
            (190f, 6.0f),
        };

        var mat = GetOrCreateMat("Mat_Waterfall", null, Vector2.one);
        // Brighter and slightly warm-white rather than the old plain pale-blue, plus a soft emissive
        // glow -- reads as "水そのものが淡く光る神聖な滝" (the water gently glowing) instead of
        // ordinary falling water, echoing the same quiet-glow language already used for the
        // AzureCrystal veins elsewhere on this cliff, but warm/white here to stay visually distinct
        // as this fall's own thing (the potion source), not just another crystal vein.
        mat.color = new Color(0.90f, 0.95f, 0.92f, 0.6f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.95f);
        SetTransparent(mat);
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", new Color(0.65f, 0.70f, 0.60f) * 0.5f);
        }

        var splashMat = GetOrCreateMat("Mat_WaterfallSplash", null, Vector2.one);
        splashMat.color = new Color(0.92f, 0.97f, 0.97f, 0.7f);
        SetTransparent(splashMat);
        splashMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        // Rock geometry flanking each fall -- without this the waterfall reads as "a water plane
        // stuck to the wall." Two big rock masses frame the crevice sides (and are embedded well
        // into the wall, not just resting against it), plus a rock/moss cap positioned just above
        // and slightly in front of the fall's own top point specifically to break up and partially
        // hide the exact spot the water mesh starts from (per "岩の割れ目から水が現れる").
        var boulderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "boulder_01/boulder_01_2k.fbx");
        var flankMossRocks = LoadIndividualMossRocks();
        var fernPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "fern_02/fern_02_2k.fbx");
        const float boulderTopLocalWF = 0.930f;
        var wfRng = new System.Random(5533);

        int placed = 0;
        foreach (var f in falls)
        {
            if (LakeGentleWeight(f.ang) > 0.15f) continue; // stay off the inlet/stairs gentle zones
            Vector2 shore = FindShoreAtAngle(f.ang);
            Vector2 dir = (shore - center).normalized;
            float shoreR = Vector2.Distance(shore, center);
            // Sample well past the shore (1.5x, into the new dramatic cliff-top plateau -- see
            // CliffRimElevation) rather than the old 1.1x (which only caught the base of the climb)
            // so the fall's source sits high on the actual new cliff, not just at the old modest
            // wall height.
            Vector2 rim = center + dir * (shoreR * 1.5f);
            float rimY = SampleWorldHeight(terrain, rim.x, rim.y);
            // The cliff wall ribbon RECEDES (moves to larger radius) as it rises -- its bottom edge
            // sits at shore radius (1.0x) but its surface reaches out to ~1.06-1.14x further up (see
            // BuildLakeCliffWall: botPts at 1.0x shore, midPts ~1.06x, topPts at 1.14x rim). The
            // previous fixed 1.02x radius was smaller than the wall's own MID/TOP radius, so for
            // most of the fall's height the waterfall mesh sat radially INSIDE (behind) the wall's
            // actual surface there -- measured as 3.4-11.7m embedded via a QA raycast diagnostic.
            // Staying clearly under the wall's MINIMUM radius (1.0x, at its bottom edge) guarantees
            // the fall is in front of the wall's surface at every height, not just near the water.
            Vector2 fallPos = center + dir * (shoreR * 0.97f);

            int rows = 20; // doubled from 10 -- the fall now spans a much taller cliff (source raised to 1.5x shore radius), needs more segments to stay smooth
            var leftPts = new Vector3[rows + 1]; var rightPts = new Vector3[rows + 1];
            Vector2 side = new Vector2(-dir.y, dir.x) * (f.width * 0.5f);
            for (int r = 0; r <= rows; r++)
            {
                float u = r / (float)rows;
                float y = Mathf.Lerp(rimY - 0.3f, LakeWaterY + 0.1f, u);
                float wobble = StoneNoise(f.ang * 3f + r * 5f, y, 91f) * 0.25f;
                Vector2 p = fallPos + dir * (wobble * 0.4f);
                leftPts[r] = new Vector3(p.x - side.x, y, p.y - side.y);
                rightPts[r] = new Vector3(p.x + side.x, y, p.y + side.y);
            }
            var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
            AddRibbon(verts, tris, uvs, leftPts, rightPts, 3f);
            var mesh = new Mesh { name = "WaterfallMesh_" + placed };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Waterfall_" + placed);
            go.transform.SetParent(wfRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            var splash = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            splash.name = "WaterfallSplash_" + placed;
            UnityEngine.Object.DestroyImmediate(splash.GetComponent<Collider>());
            splash.transform.SetParent(wfRoot.transform, false);
            splash.transform.position = new Vector3(fallPos.x, LakeWaterY + 0.08f, fallPos.y);
            splash.transform.localScale = new Vector3(f.width * 2.0f, 0.06f, f.width * 1.5f);
            splash.GetComponent<MeshRenderer>().sharedMaterial = splashMat;

            // Soft warm glow at the landing pool -- reinforces "神聖な滝" as a lit, important
            // landmark visible from across the lake at night/dusk, not just a bright material.
            // Deliberately gentle (per this project's established "quiet glow, not a searchlight"
            // convention for every other magical light source) and warm/white to read as distinct
            // from the AzureCrystal veins' cool blue glow elsewhere on the same cliff.
            var glowGo = new GameObject("Waterfall_" + placed + "_SacredGlow");
            glowGo.transform.SetParent(wfRoot.transform, false);
            glowGo.transform.position = new Vector3(fallPos.x, LakeWaterY + 1.0f, fallPos.y);
            var glowLight = glowGo.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = new Color(1.0f, 0.96f, 0.85f);
            glowLight.intensity = 1.8f;
            glowLight.range = f.width * 2.5f;
            glowLight.shadows = LightShadows.None;

            // ---- Flanking rock crevice: two big rock masses embedded into the wall on either
            // side of the fall, so the water reads as emerging from a gap BETWEEN rocks rather
            // than a plane stuck to bare cliff. ----
            // BUG FIX (2026-08-13): these previously used `midY`/`rimY` -- heights sampled ONLY at
            // the fall's own centerline -- reused verbatim at flankXZ/capXZ positions that are
            // offset sideways/forward by up to a couple meters. On the steep dramatic cliff terrain
            // here, the real ground height at that offset position can differ substantially from
            // the centerline sample, so the rock ended up floating clear of the actual slope --
            // this is almost certainly the "浮遊岩から滝が出ている" the user reported. Each rock now
            // re-samples the real terrain height AT ITS OWN position instead of reusing a
            // centerline value.
            Vector2 sideDir = new Vector2(-dir.y, dir.x);
            for (int side2 = -1; side2 <= 1; side2 += 2)
            {
                if (boulderPrefab == null) continue;
                // Rock size capped against a reference width (not the fall's own, now much larger,
                // width) -- 2026-08-14: at the new 6m hero-fall width the old `1.6+width*0.8` formula
                // grew the flanking rocks to ~5.4-7.4 scale, big enough that the two rocks' own bulk
                // met in the middle and nearly swallowed the opening (confirmed via screenshot -- the
                // fall was almost entirely hidden behind rock). Flanking rocks should scale with how
                // DEEP/tall a crevice needs to look, not grow unbounded with how wide the water is.
                float flankRefWidth = Mathf.Min(f.width, 2.4f);
                float flankScale = (1.6f + flankRefWidth * 0.8f) * (0.85f + (float)wfRng.NextDouble() * 0.3f);
                Vector2 flankXZ = fallPos + dir * (0.3f + (float)wfRng.NextDouble() * 0.3f) + sideDir * side2 * (f.width * 0.55f + flankScale * 0.35f);
                float flankGroundY = SampleWorldHeightConservative(terrain, flankXZ.x, flankXZ.y, flankScale * 0.5f);
                var flankInst = (GameObject)PrefabUtility.InstantiatePrefab(boulderPrefab, wfRoot.transform);
                flankInst.name = "WaterfallFlankRock_" + placed + "_" + side2;
                flankInst.transform.localScale = Vector3.one * flankScale;
                flankInst.transform.rotation = Quaternion.Euler(0f, (float)wfRng.NextDouble() * 360f, 0f);
                // Grounded at the REAL local terrain height, embedded by a third of the rock's own
                // height so it reads as emerging from the slope rather than resting on top of it.
                flankInst.transform.position = new Vector3(flankXZ.x, flankGroundY - boulderTopLocalWF * flankScale * 0.35f, flankXZ.y);
            }

            // Source rock: sits right at the fall's own top point, mostly buried into the bank with
            // only a knob exposed, so the water appears to emerge from behind/beneath it rather than
            // starting in open air (per "岩の割れ目から水が現れる" / "水源を少し隠す").
            var capPrefab = flankMossRocks.Length > 0 ? flankMossRocks[wfRng.Next(flankMossRocks.Length)] : null;
            if (capPrefab != null)
            {
                // Same width-cap reasoning as the flanking rocks above -- this cap is meant to
                // partially hide the water's own source point, not grow into a boulder that hangs
                // over and obscures the whole opening.
                float capScale = 1.0f + Mathf.Min(f.width, 2.4f) * 0.5f;
                Vector2 capXZ = fallPos + dir * (0.5f + (float)wfRng.NextDouble() * 0.4f);
                float capGroundY = SampleWorldHeightConservative(terrain, capXZ.x, capXZ.y, capScale * 0.6f);
                float capBottomY = GetPrefabBottomLocalY(capPrefab);
                var capInst = (GameObject)PrefabUtility.InstantiatePrefab(capPrefab, wfRoot.transform);
                capInst.name = "WaterfallSourceRock_" + placed;
                capInst.transform.localScale = Vector3.one * capScale;
                capInst.transform.rotation = Quaternion.Euler(0f, (float)wfRng.NextDouble() * 360f, 0f);
                capInst.transform.position = new Vector3(capXZ.x, capGroundY - capBottomY * capScale - Mathf.Abs(capBottomY) * capScale * 0.7f, capXZ.y);
            }

            // A couple of small wet rocks right at the landing point, so the fall doesn't meet the
            // lake as a bare splash decal against open water -- a natural rock-to-water transition.
            for (int wi = 0; wi < 2; wi++)
            {
                var smallPrefab = flankMossRocks.Length > 0 ? flankMossRocks[wfRng.Next(flankMossRocks.Length)] : null;
                if (smallPrefab == null) continue;
                float smallScale = 0.35f + (float)wfRng.NextDouble() * 0.25f;
                Vector2 smallXZ = fallPos + sideDir * (wi == 0 ? -1f : 1f) * (f.width * 0.5f + 0.3f) + dir * 0.2f;
                float smallBottomY = GetPrefabBottomLocalY(smallPrefab);
                var smallInst = (GameObject)PrefabUtility.InstantiatePrefab(smallPrefab, wfRoot.transform);
                smallInst.name = "WaterfallBaseRock_" + placed + "_" + wi;
                smallInst.transform.localScale = Vector3.one * smallScale;
                smallInst.transform.rotation = Quaternion.Euler(0f, (float)wfRng.NextDouble() * 360f, 0f);
                smallInst.transform.position = new Vector3(smallXZ.x, LakeWaterY + 0.05f - smallBottomY * smallScale * 0.5f, smallXZ.y);
            }

            // Waterfalls are the wettest spot on the whole shore -- concentrate moss/fern here
            // specifically (per "滝周辺だけ植生を濃くする"), clustered around the flank rocks and
            // landing point rather than evenly across the wall, and NOT covering the whole rock face
            // (bare wet rock stays visible between clumps).
            if (fernPrefab != null)
            {
                // Now the single hero waterfall (not one of five), so its own surroundings can
                // afford to read as noticeably lusher than an ordinary fall.
                int fernCount = 8 + wfRng.Next(5);
                for (int fi = 0; fi < fernCount; fi++)
                {
                    float fa = (float)wfRng.NextDouble() * Mathf.PI * 2f;
                    float fr = 0.8f + (float)wfRng.NextDouble() * (f.width + 1.5f);
                    Vector2 fXZ = fallPos + new Vector2(Mathf.Cos(fa), Mathf.Sin(fa)) * fr;
                    float fGroundY = SampleWorldHeightConservative(terrain, fXZ.x, fXZ.y, 0.3f);
                    float fScale = 0.4f + (float)wfRng.NextDouble() * 0.4f;
                    var fInst = (GameObject)PrefabUtility.InstantiatePrefab(fernPrefab, wfRoot.transform);
                    fInst.name = "WaterfallFern_" + placed + "_" + fi;
                    fInst.transform.position = new Vector3(fXZ.x, fGroundY, fXZ.y);
                    fInst.transform.rotation = Quaternion.Euler(0f, (float)wfRng.NextDouble() * 360f, 0f);
                    fInst.transform.localScale = Vector3.one * fScale;
                }
            }

            placed++;
        }
        log.AppendLine("Waterfalls built: " + placed);
    }

    // ---- Lake shore dressing: rocks/roots/logs/moss around the water's edge (mainly the two
    // gentle zones, where the player actually walks) plus scattered accents at the cliff base,
    // using the fixed SampleWorldHeight so nothing floats. ----
    static void BuildLakeShoreDressing(GameObject root, Terrain terrain, StringBuilder log)
    {
        var shoreRoot = new GameObject("LakeShoreDressing");
        shoreRoot.transform.SetParent(root.transform, false);
        var center = new Vector2(LakeCenterX, LakeCenterZ);

        // see LoadIndividualMossRocks() for why this is no longer the raw rock_moss_set_01/02 FBX
        var rootsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
        var rootCluster2Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "root_cluster_02/root_cluster_02_decimated.fbx");
        float rootCluster2BottomY = GetPrefabBottomLocalY(rootCluster2Prefab);
        var boulder = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "boulder_01/boulder_01_2k.fbx");
        var logPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "dead_tree_trunk_02/dead_tree_trunk_02_2k.fbx");
        var mossSets = LoadIndividualMossRocks();
        const float rootsTopLocal = 0.122f;
        const float boulderTopLocal = 0.930f;
        const float logTopLocal = 0.727f;

        var rng = new System.Random(1212);
        int placed = 0;
        float bridgeCenterXForShore = RiverX(BridgeCenterZ);
        for (int i = 0; i < 45; i++) // thinned from 64 -- part of the general de-crowding pass (was reading as too dense/cluttered overall)
        {
            float ang = (float)rng.NextDouble() * 360f;
            // Bias toward the gentle zones (where the player actually walks along the shore);
            // still scatter some accents around the rest of the rim at the cliff's base.
            if (LakeGentleWeight(ang) < 0.15f && rng.NextDouble() < 0.55) continue;

            Vector2 shore = FindShoreAtAngle(ang);
            Vector2 dir = (shore - center).normalized;
            float shoreR = Vector2.Distance(shore, center);
            float gentleHere = LakeGentleWeight(ang);

            // Gentle zones: ground is flat right past the shore, so place normally, close in.
            // Steep cliff zones: anchor a bit further out on the flatter rim, then pull the actual
            // XZ position back toward the edge for the visual overhang -- height is now sampled at
            // the REAL placement point `p` below (not the anchor), see the bug-fix comment there.
            Vector2 p;
            if (gentleHere > 0.3f)
            {
                float r = shoreR + 0.4f + (float)rng.NextDouble() * Mathf.Lerp(1.2f, 3.0f, gentleHere);
                p = center + dir * r;
            }
            else
            {
                float anchorR = shoreR * (1.12f + (float)rng.NextDouble() * 0.2f);
                Vector2 anchorP = center + dir * anchorR;
                p = anchorP - dir * ((float)rng.NextDouble() * 0.8f);
            }

            // Bias which prop type rolls by the named ShoreZone so each bank type reads distinctly:
            // RootBank leans heavily on exposed roots, LowMossyBank away from boulders/logs (softer,
            // low profile), everywhere else close to the original even mix.
            var zone = GetShoreZone(ang);
            int roll;
            if (zone.type == ShoreZoneType.RootBank && rng.NextDouble() < 0.55) roll = 0;
            else if (zone.type == ShoreZoneType.LowMossyBank && rng.NextDouble() < 0.5) roll = 3;
            else roll = rng.Next(4);
            bool useRootCluster2 = roll == 0 && rootCluster2Prefab != null && rng.NextDouble() < 0.4;
            GameObject prefab; float topLocal; float scale; float emerge;
            switch (roll)
            {
                case 0: prefab = useRootCluster2 ? rootCluster2Prefab : rootsPrefab; topLocal = rootsTopLocal; scale = useRootCluster2 ? (2.0f + (float)rng.NextDouble() * 1.8f) : (1.3f + (float)rng.NextDouble() * 1.2f); emerge = 0.15f; break;
                case 1: prefab = boulder; topLocal = boulderTopLocal; scale = 0.7f + (float)rng.NextDouble() * 0.9f; emerge = 0.12f * scale; break;
                case 2: prefab = logPrefab; topLocal = logTopLocal; scale = 1.3f + (float)rng.NextDouble() * 1.1f; emerge = 0f; break; // log uses its own lie-flat placement below, not the generic emerge formula
                default: prefab = mossSets[rng.Next(mossSets.Length)]; topLocal = GetPrefabTopLocalY(prefab); scale = 0.5f + (float)rng.NextDouble() * 0.6f; emerge = 0.1f * scale; break;
            }
            if (prefab == null) continue;
            // BUG FIX 2026-08-14 (user-reported: LakeShore_19 floating): in the steep-zone branch,
            // height used to be sampled at `heightSamplePt` (anchorP, the flatter rim point) but the
            // object is actually PLACED at `p`, which is pulled toward the lake from anchorP by up
            // to 0.8m -- on a steep slope (LakeShore_19 sat on a 46deg slope) that's enough distance
            // for the real ground to be meaningfully lower than the sample, leaving the object
            // floating relative to its own true position. Sample at the real placement point `p`
            // instead, and use the conservative ring-sample (not a single point) for the same
            // steep-terrain robustness already used everywhere else in this file.
            float groundY = SampleWorldHeightConservative(terrain, p.x, p.y, 0.5f * scale);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, shoreRoot.transform);
            inst.name = "LakeShore_" + placed;
            inst.transform.localScale = Vector3.one * scale;
            if (useRootCluster2)
            {
                // root_cluster_02's pivot isn't at a measured "top" offset like pine_roots -- rest
                // it directly on its own measured mesh-bottom instead (a second free root variant
                // for shoreline variety, per the RootBank zone bias above).
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                inst.transform.position = new Vector3(p.x, groundY - rootCluster2BottomY * scale, p.y);
            }
            else if (roll == 2)
            {
                // dead_tree_trunk_02's identity pose stands vertical (topLocal is measured up
                // its trunk axis, same as the standing/embedded case) -- BuildFootholds already
                // solved "make this read as a fallen log" for the river-crossing case via
                // LookRotation+90de-yaw to lay it on its side; replicate that here instead of the
                // generic Y-only rotation, which was previously planting it upright like a stump
                // and burying ~70% of its trunk radius (the actual "unnaturally buried" bug).
                Vector3 logDir = new Vector3(-dir.y, 0f, dir.x); // tangent to the shoreline, not radial -- reads as having fallen along the bank
                if (rng.NextDouble() < 0.4) logDir = -logDir; // randomize which way it "fell"
                inst.transform.rotation = Quaternion.LookRotation(logDir) * Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler((float)rng.NextDouble() * 10f - 5f, 0f, 0f);
                float logTopY = groundY + 0.42f * scale; // same exposure ratio as the river-crossing log (BuildFootholds), which already reads correctly as lying on the ground rather than buried
                inst.transform.position = new Vector3(p.x, logTopY - topLocal * scale, p.y);
            }
            else
            {
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float topY = groundY + emerge;
                inst.transform.position = new Vector3(p.x, topY - topLocal * scale, p.y);
            }
            placed++;
        }
        log.AppendLine("Lake shore dressing placed: " + placed);
    }

    // ---- Underwater lakebed detail: a modest scatter of small rocks resting on the actual lake
    // floor, biased toward the shallow area near the stairs so the bed is visible through the
    // lighter shallow-shelf water there. No colliders (purely decorative, keeps physics cheap). ----
    static void BuildLakeUnderwaterRocks(GameObject root, Terrain terrain, StringBuilder log)
    {
        var underRoot = new GameObject("LakeUnderwaterRocks");
        underRoot.transform.SetParent(root.transform, false);
        // see LoadIndividualMossRocks() for why this is no longer the raw rock_moss_set_01/02 FBX
        var mossSets = LoadIndividualMossRocks();
        var center = new Vector2(LakeCenterX, LakeCenterZ);
        var rng = new System.Random(3131);
        int placed = 0;
        for (int i = 0; i < 22; i++)
        {
            float ang = (float)rng.NextDouble() < 0.65 ? StairsAngleDeg + ((float)rng.NextDouble() - 0.5f) * 90f : (float)rng.NextDouble() * 360f;
            Vector2 shore = FindShoreAtAngle(ang);
            Vector2 dir = (shore - center).normalized;
            float shoreR = Vector2.Distance(shore, center);
            float r = shoreR * (0.4f + (float)rng.NextDouble() * 0.5f);
            Vector2 p = center + dir * r;
            float floorY = SampleWorldHeight(terrain, p.x, p.y);
            if (floorY > LakeWaterY - 0.1f) continue; // only place where it's actually underwater

            var prefab = mossSets[rng.Next(mossSets.Length)];
            if (prefab == null) continue;
            float scale = 0.22f + (float)rng.NextDouble() * 0.35f;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, underRoot.transform);
            inst.name = "LakebedRock_" + placed;
            inst.transform.localScale = Vector3.one * scale;
            inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            inst.transform.position = new Vector3(p.x, floorY - GetPrefabTopLocalY(prefab) * scale * 0.3f, p.y);
            placed++;
        }
        log.AppendLine("Lake underwater rocks placed: " + placed);
    }

    // ---- Meshy-authored stone bridge: the literal spawn point. Replaces the procedural arch
    // bridge (BuildBridge, below -- kept in source, just no longer called) with the imported
    // Meshy_AI_Mossy_Stone_Bridge model (decimated in Blender to a clean identity transform).
    // The prefab is instantiated UNCHANGED as a child; all placement (scale to match the river's
    // width, position, walking/blocking colliders) happens on a separate wrapper GameObject, so
    // the imported asset's own transform is never touched. ----
    static float BuildMeshyBridge(GameObject root, Terrain terrain, StringBuilder log)
    {
        const string ModelFolder = "Assets/Stage/Forest/Bridge/Models/StoneBridge/";
        // Decimated in Blender (757,396 -> 24,998 triangles) with consistent outward normals
        // recalculated -- see decimate_bridge.py. Bounds are essentially unchanged from the
        // original high-poly source, so the placement math below still applies directly.
        const string FbxPath = ModelFolder + "Meshy_AI_Mossy_Stone_Bridge_decimated.fbx";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (asset == null)
        {
            log.AppendLine("Meshy bridge FBX not found at " + FbxPath + " -- falling back to the procedural bridge.");
            return BuildBridge(root, terrain, log);
        }

        // Top-level, UNSCALED container -- everything else (colliders, embankment dressing) is
        // computed directly in world space and parented here, never under the non-uniformly
        // scaled visual wrapper below (a scaled+rotated parent would silently re-transform any
        // world-space numbers assigned as "local" to its children).
        var bridgeRoot = new GameObject("StoneBridge_Meshy");
        bridgeRoot.transform.SetParent(root.transform, false);

        // The scaled/positioned wrapper holds ONLY the visual model.
        var visualWrapper = new GameObject("VisualWrapper");
        visualWrapper.transform.SetParent(bridgeRoot.transform, false);
        var modelInst = (GameObject)PrefabUtility.InstantiatePrefab(asset, visualWrapper.transform);
        modelInst.name = "Meshy_AI_Mossy_Stone_Bridge";
        // The decimated model has a clean identity transform (baked flat during the Blender
        // decimation pass), already aligned with the model's long axis on world X (span), up on
        // world Y, and depth on world Z -- so it's left untouched here too, same as before.

        var mat = GetOrCreateMeshyBridgeMaterial(ModelFolder, log);
        foreach (var mr in modelInst.GetComponentsInChildren<MeshRenderer>())
            mr.sharedMaterial = mat;

        // Scale/size constants live at class level (MeshyBridgeWorldHalfSpan etc.) so the terrain's
        // approach-mound function (BridgeApproachMoundHeight, in RawHeightAt) can agree with this
        // placement exactly without either one depending on the other's build output.
        float worldHalfSpan = MeshyBridgeWorldHalfSpan;
        float worldHalfHeight = MeshyBridgeWorldHalfHeight;
        float worldHalfDepth = MeshyBridgeWorldHalfDepth;

        float riverCenterX = RiverX(BridgeCenterZ);
        float waterYHere = WaterYAt(BridgeCenterZ);
        // Bottom of the model (worldHalfHeight below the wrapper's Y) sits right at the water's
        // surface -- the model's own arch/pier mass provides the "goes down to the water" look;
        // the deck (top of the model) ends up a natural-feeling clearance above that.
        float wrapperY = waterYHere + worldHalfHeight + 0.15f;
        float deckY = wrapperY + worldHalfHeight; // top of the model -- matches ComputeBridgeDeckY()

        visualWrapper.transform.localScale = new Vector3(MeshyBridgeScaleSpanHeight, MeshyBridgeScaleSpanHeight, MeshyBridgeScaleDepth);
        visualWrapper.transform.position = new Vector3(riverCenterX, wrapperY, BridgeCenterZ);

        // ---- Walking collider: a chain of simplified angled box segments following the deck's
        // REAL arch curve (BridgeDeckCurveOffsets), NOT the 750k-triangle visual mesh (unusable as
        // a MeshCollider) and NOT a single flat box (the deck is highest at the crown and dips
        // well below that at both ends -- a flat collider at crown height floats noticeably above
        // the visibly lower stone near the ends). Each segment is rotated to match the local slope
        // between two consecutive measured curve points, with a slight length overlap between
        // segments so there's no seam gap. Slightly inset from the full span in depth so the
        // player is never standing right at the unsupported edge vs. the curved visual mesh. ----
        float colHalfDepth = worldHalfDepth * 0.95f;
        var walkCol = new GameObject("WalkableCollider");
        walkCol.transform.SetParent(bridgeRoot.transform, false);
        int segCount = BridgeDeckCurveOffsets.Length - 1;
        for (int si = 0; si < segCount; si++)
        {
            float t0 = -1f + 2f * si / segCount;
            float t1 = -1f + 2f * (si + 1) / segCount;
            float x0 = riverCenterX + t0 * worldHalfSpan;
            float x1 = riverCenterX + t1 * worldHalfSpan;
            float y0 = deckY + BridgeDeckCurveOffsets[si] - 0.12f;
            float y1 = deckY + BridgeDeckCurveOffsets[si + 1] - 0.12f;
            Vector3 p0 = new Vector3(x0, y0, BridgeCenterZ);
            Vector3 p1 = new Vector3(x1, y1, BridgeCenterZ);
            Vector3 mid = (p0 + p1) * 0.5f;
            Vector3 dir = (p1 - p0);
            float segLen = dir.magnitude;

            var seg = new GameObject("WalkableColliderSeg_" + si);
            seg.transform.SetParent(walkCol.transform, false);
            seg.transform.position = mid;
            seg.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir.normalized);
            var segBox = seg.AddComponent<BoxCollider>();
            segBox.size = new Vector3(segLen + 0.5f, 0.5f, colHalfDepth * 2f); // +0.5 length overlap seals the seam between segments
        }

        // ---- Solid abutment colliders: block swimming/walking straight through the bridge's
        // mass at either end (previously the exact bug that let players bypass the lake stairs).
        // The middle third is left uncollided -- that's the arch opening the river flows through. ----
        foreach (float side in new[] { -1f, 1f })
        {
            var abut = new GameObject("AbutmentCollider_" + (side < 0 ? "West" : "East"));
            abut.transform.SetParent(bridgeRoot.transform, false);
            var abutBox = abut.AddComponent<BoxCollider>();
            float abutHalfSpan = worldHalfSpan * 0.32f;
            float centerX = riverCenterX + side * (worldHalfSpan - abutHalfSpan);
            abut.transform.position = new Vector3(centerX, wrapperY, BridgeCenterZ);
            abutBox.size = new Vector3(abutHalfSpan * 2f, worldHalfHeight * 2f + 1.0f, worldHalfDepth * 2f);
        }

        // ---- Embankment blending at both ends -- same proven (measured-topLocal) technique used
        // throughout this stage, so nothing floats. Parented to bridgeRoot (unscaled), NOT the
        // visual wrapper, so their own uniform scale isn't distorted by the wrapper's non-uniform one. ----
        // see LoadIndividualMossRocks() for why this is no longer the raw rock_moss_set_01/02 FBX
        var rootsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
        var mossSets = LoadIndividualMossRocks();
        const float rootsTopLocal = 0.122f;
        var embRng = new System.Random(3344);
        foreach (float side in new[] { -1f, 1f })
        {
            float endX = riverCenterX + side * worldHalfSpan;
            // Kept tight and sparse -- only accenting the short land-side ramp (matches the
            // narrowed BridgeApproachMoundHeight zone), NOT spread across the bridge's own width,
            // so the masonry sides and arch stay clearly visible instead of getting walled in.
            for (int k = 0; k < 5; k++)
            {
                float ox = endX + side * (0.2f + (float)embRng.NextDouble() * 3.2f);
                float oz = BridgeCenterZ + ((float)embRng.NextDouble() - 0.5f) * (MeshyBridgeWorldHalfDepth * 0.9f);
                float groundY = SampleWorldHeightConservative(terrain, ox, oz, 0.6f);
                bool useRoot = embRng.Next(3) == 0;
                var prefab = useRoot ? rootsPrefab : mossSets[embRng.Next(mossSets.Length)];
                if (prefab == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, bridgeRoot.transform);
                inst.name = "BridgeEmbankment_" + k;
                float scale = useRoot ? (1.2f + (float)embRng.NextDouble() * 0.8f) : (0.35f + (float)embRng.NextDouble() * 0.4f);
                float topLocal = useRoot ? rootsTopLocal : GetPrefabTopLocalY(prefab);
                float topY = groundY + (useRoot ? 0.3f : 0.25f * scale);
                inst.transform.localScale = Vector3.one * scale;
                inst.transform.rotation = Quaternion.Euler(0f, (float)embRng.NextDouble() * 360f, 0f);
                inst.transform.position = new Vector3(ox, topY - topLocal * scale, oz);
            }
        }

        // Save a reusable Prefab of the fully-assembled bridge (visual + colliders + embankment
        // dressing). The live scene copy stays as-is (this stage is always rebuilt from scratch by
        // this script anyway); the Prefab asset is for reuse elsewhere / inspection in the Project.
        if (!AssetDatabase.IsValidFolder("Assets/Stage/Forest/Bridge/Prefabs"))
            AssetDatabase.CreateFolder("Assets/Stage/Forest/Bridge", "Prefabs");
        PrefabUtility.SaveAsPrefabAsset(bridgeRoot, "Assets/Stage/Forest/Bridge/Prefabs/StoneBridge.prefab");

        log.AppendLine("Meshy stone bridge placed at X=" + riverCenterX.ToString("F1") + ", Z=" + BridgeCenterZ.ToString("F1") +
            ", span=" + (worldHalfSpan * 2f).ToString("F1") + "m, deckY=" + deckY.ToString("F2") + ", waterY=" + waterYHere.ToString("F2") + ".");
        return deckY;
    }

    static Material GetOrCreateMeshyBridgeMaterial(string modelFolder, StringBuilder log)
    {
        string matPath = "Assets/Stage/Forest/Bridge/Mat_MeshyStoneBridge.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, matPath);
        }

        var baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(modelFolder + "Meshy_AI_Mossy_Stone_Bridge_0811073607_texture.png");
        var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(modelFolder + "Meshy_AI_Mossy_Stone_Bridge_0811073607_texture_normal.png");
        var roughnessTex = AssetDatabase.LoadAssetAtPath<Texture2D>(modelFolder + "Meshy_AI_Mossy_Stone_Bridge_0811073607_texture_roughness.png");
        var metallicTex = AssetDatabase.LoadAssetAtPath<Texture2D>(modelFolder + "Meshy_AI_Mossy_Stone_Bridge_0811073607_texture_metallic.png");

        if (baseColor != null) mat.SetTexture("_BaseMap", baseColor);

        if (normalTex != null)
        {
            SetTextureImporterType(normalTex, TextureImporterType.NormalMap);
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(normalTex)));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_BumpScale", 1f);
        }

        // URP Lit expects a single packed map (R=metallic, A=smoothness); Meshy exports metallic
        // and roughness as separate grayscale textures, so pack them into one new texture rather
        // than losing either map to a flat slider value.
        if (metallicTex != null && roughnessTex != null)
        {
            var packed = PackMetallicSmoothness(metallicTex, roughnessTex, "Assets/Stage/Forest/Bridge/Tex_MeshyStoneBridge_MetallicSmoothness.png");
            if (packed != null)
            {
                mat.SetTexture("_MetallicGlossMap", packed);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Metallic", 1f); // let the map drive it fully
                mat.SetFloat("_Smoothness", 1f);
            }
        }

        // Meshy-generated meshes are prone to inconsistent/inverted normals (and the FBX's baked
        // 270-degree conversion is another common source of a handedness flip) -- double-sided
        // rendering sidesteps the whole question rather than risking the "visible surface" being
        // backface-culled away from the exact angle the player actually views it from.
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        EditorUtility.SetDirty(mat);
        log.AppendLine("Meshy bridge material set up (BaseMap=" + (baseColor != null) + ", Normal=" + (normalTex != null) + ", MetallicSmoothness packed=" + (metallicTex != null && roughnessTex != null) + ").");
        return mat;
    }

    static void SetTextureImporterType(Texture2D tex, TextureImporterType type)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.textureType == type) return;
        importer.textureType = type;
        importer.SaveAndReimport();
    }

    static void SetTextureReadable(Texture2D tex)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.isReadable) return;
        importer.isReadable = true;
        importer.SaveAndReimport();
    }

    static Texture2D PackMetallicSmoothness(Texture2D metallicTex, Texture2D roughnessTex, string savePath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(savePath);
        if (existing != null) return existing;

        SetTextureReadable(metallicTex);
        SetTextureReadable(roughnessTex);

        int w = Mathf.Min(metallicTex.width, roughnessTex.width);
        int h = Mathf.Min(metallicTex.height, roughnessTex.height);
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                float metallic = metallicTex.GetPixelBilinear(u, v).r;
                float roughness = roughnessTex.GetPixelBilinear(u, v).r;
                outTex.SetPixel(x, y, new Color(metallic, metallic, metallic, 1f - roughness));
            }
        }
        outTex.Apply();
        System.IO.File.WriteAllBytes(savePath, outTex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(outTex);
        AssetDatabase.ImportAsset(savePath);
        var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(savePath);
        SetTextureImporterType(imported, TextureImporterType.Default);
        var imp = AssetImporter.GetAtPath(savePath) as TextureImporter;
        if (imp != null) { imp.sRGBTexture = false; imp.SaveAndReimport(); }
        return imported;
    }

    static Texture2D CombineDiffuseAlpha(Texture2D diffuseTex, Texture2D alphaMaskTex, string savePath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(savePath);
        if (existing != null) return existing;

        SetTextureReadable(diffuseTex);
        SetTextureReadable(alphaMaskTex);

        int w = diffuseTex.width, h = diffuseTex.height;
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                Color c = diffuseTex.GetPixelBilinear(u, v);
                float a = alphaMaskTex.GetPixelBilinear(u, v).r;
                outTex.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
            }
        }
        outTex.Apply();
        System.IO.File.WriteAllBytes(savePath, outTex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(outTex);
        AssetDatabase.ImportAsset(savePath);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(savePath);
    }

    // ---- OLD procedural stone bridge: kept in source (not deleted) as the pre-Meshy fallback --
    // see BuildMeshyBridge above, which is what Run() actually calls now. The literal spawn point.
    // An old, mossy, gently-arched stone ARCH bridge -- procedurally modeled (not flat tiles on
    // piers) so it reads as a real masonry structure: the road surface rises gently toward the
    // center, and underneath it a large stone arch opening lets the river pass through and
    // continue into the lake. The whole silhouette (per X-slice across the crossing) is a solid
    // stone volume between a "top" curve (road) and a "bottom" curve (natural ground at the
    // abutments, or the arch's underside inside the opening) -- no separate piers, the arch itself
    // is the support. ----
    static float BuildBridge(GameObject root, Terrain terrain, StringBuilder log)
    {
        var bridgeRoot = new GameObject("StoneBridge");
        bridgeRoot.transform.SetParent(root.transform, false);

        float riverCenterX = RiverX(BridgeCenterZ);
        float hw = RiverHalfWidth(BridgeCenterZ);
        float halfSpanX = hw + BankFalloff + 2f; // solid landing on both banks
        float spanX0 = riverCenterX - halfSpanX;
        float spanX1 = riverCenterX + halfSpanX;

        // The arch opening: comfortably wider than the actual water channel so the river
        // always has room to pass through, never pinched by stonework.
        float archHalfWidth = hw + 2.0f;
        float archX0 = riverCenterX - archHalfWidth;
        float archX1 = riverCenterX + archHalfWidth;
        float archZoneT0 = Mathf.Clamp01((archX0 - spanX0) / (spanX1 - spanX0));
        float archZoneT1 = Mathf.Clamp01((archX1 - spanX0) / (spanX1 - spanX0));
        float archZoneCenter = (archZoneT0 + archZoneT1) * 0.5f;
        float archZoneHalf = (archZoneT1 - archZoneT0) * 0.5f;

        // Spring height: natural ground level right at the arch's edges -- the abutments sit
        // on real ground, and the arch springs from there.
        float springY = (SampleWorldHeight(terrain, archX0, BridgeCenterZ) +
                          SampleWorldHeight(terrain, archX1, BridgeCenterZ)) * 0.5f;

        const float deckThicknessAtEnds = 2.6f; // the deck already sits well above spring level at the ends
        const float archRoadRise = 1.4f;        // gentle rise of the WALKABLE road toward the center
        const float archRiseUnderside = 3.2f;   // a genuinely large arch opening underneath
        const float transitionT = 0.05f;
        float deckBaseY = springY + deckThicknessAtEnds;

        float z0 = BridgeZ0, z1 = BridgeZ1, zMid = BridgeCenterZ;

        float TopY(float t) => deckBaseY + archRoadRise * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
        float BottomY(float t)
        {
            float x = spanX0 + t * (spanX1 - spanX0);
            float terrainY = SampleWorldHeight(terrain, x, BridgeCenterZ);
            float dist = Mathf.Abs(t - archZoneCenter);
            float archBlend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(archZoneHalf, archZoneHalf + transitionT, dist));
            float u = Mathf.Clamp01(Mathf.InverseLerp(archZoneT0, archZoneT1, t));
            float archShape = Mathf.Sin(u * Mathf.PI);
            float archFormulaY = springY + archRiseUnderside * archShape;
            return Mathf.Lerp(terrainY, archFormulaY, archBlend);
        }

        float deckY = TopY(0.5f); // goblin spawns here -- the crown, dead center of the span

        int N = 56;
        var xs = new float[N + 1];
        var topYs = new float[N + 1];
        var botYs = new float[N + 1];
        for (int i = 0; i <= N; i++)
        {
            float t = i / (float)N;
            xs[i] = spanX0 + t * (spanX1 - spanX0);
            topYs[i] = TopY(t);
            botYs[i] = BottomY(t);
        }

        var rockTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PH + "dry_riverbed_rock/dry_riverbed_rock_diff_2k.jpg");

        // ---- Road top: a single continuous (but noise-roughened) surface -- the reference photo's
        // deck reads as packed worn gravel, not individual flagstones, so this stays a ribbon. ----
        var uVerts = new List<Vector3>(); var uTris = new List<int>(); var uUVs = new List<Vector2>();
        var rowZ0 = new Vector3[N + 1]; var rowZmid = new Vector3[N + 1]; var rowZ1 = new Vector3[N + 1];
        for (int i = 0; i <= N; i++)
        {
            rowZ0[i] = new Vector3(xs[i], topYs[i] + StoneNoise(xs[i], z0, 11f) * 0.08f, z0);
            rowZmid[i] = new Vector3(xs[i], topYs[i] + StoneNoise(xs[i], zMid, 13f) * 0.08f, zMid);
            rowZ1[i] = new Vector3(xs[i], topYs[i] + StoneNoise(xs[i], z1, 17f) * 0.08f, z1);
        }
        AddRibbon(uVerts, uTris, uUVs, rowZ0, rowZmid, 8f);
        AddRibbon(uVerts, uTris, uUVs, rowZmid, rowZ1, 8f);
        var roadMesh = new Mesh { name = "BridgeRoadTopMesh" };
        roadMesh.SetVertices(uVerts); roadMesh.SetTriangles(uTris, 0); roadMesh.SetUVs(0, uUVs);
        roadMesh.RecalculateNormals(); roadMesh.RecalculateBounds();

        // ---- Arch underside / abutment base: the visible "ceiling" of the arch tunnel and the
        // buried base of the abutments, kept as a smooth surface (rarely seen up close). ----
        var lVerts = new List<Vector3>(); var lTris = new List<int>(); var lUVs = new List<Vector2>();
        var botZ0 = new Vector3[N + 1]; var botZ1 = new Vector3[N + 1];
        for (int i = 0; i <= N; i++)
        {
            botZ0[i] = new Vector3(xs[i], botYs[i] + StoneNoise(xs[i], z0, 41f) * 0.08f, z0);
            botZ1[i] = new Vector3(xs[i], botYs[i] + StoneNoise(xs[i], z1, 47f) * 0.08f, z1);
        }
        AddRibbon(lVerts, lTris, lUVs, botZ1, botZ0, 8f); // underside faces down, args swapped
        var undersideMesh = new Mesh { name = "BridgeArchUndersideMesh" };
        undersideMesh.SetVertices(lVerts); undersideMesh.SetTriangles(lTris, 0); undersideMesh.SetUVs(0, lUVs);
        undersideMesh.RecalculateNormals(); undersideMesh.RecalculateBounds();

        // ---- Side walls: THIS is the part the reference photo is about -- genuinely separate,
        // individually-shaped stacked stone blocks (not a smooth bent surface), built by stacking
        // irregular blocks column-by-column from BottomY(t) up to TopY(t) on both visible faces.
        // Reusing BottomY(t) here means the arch opening emerges naturally: columns inside the arch
        // zone start high (near the arch curve) so far fewer courses fit there, while columns in
        // the abutment zone start at ground level and stack much higher -- exactly like a real
        // voussoir arch approximated by coursed stone. ----
        var cubeTmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var cubeSrcMesh = cubeTmp.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(cubeTmp);

        var upperInstances = new List<CombineInstance> { new CombineInstance { mesh = roadMesh, transform = Matrix4x4.identity } };
        var lowerInstances = new List<CombineInstance> { new CombineInstance { mesh = undersideMesh, transform = Matrix4x4.identity } };

        int wallCols = 34;
        var wallRng = new System.Random(2025);
        foreach (float zFace in new[] { z0 - 0.05f, z1 + 0.05f })
        {
            bool isZ0Side = zFace < zMid;
            for (int c = 0; c < wallCols; c++)
            {
                float t = (c + 0.5f) / wallCols;
                float xCol = spanX0 + t * (spanX1 - spanX0);
                float bY = BottomY(t);
                float tY = TopY(t);
                float y = bY;
                int guard = 0;
                while (y < tY - 0.05f && guard < 30)
                {
                    guard++;
                    float rowH = 0.32f + (float)wallRng.NextDouble() * 0.22f;
                    float stoneW = 0.7f + (float)wallRng.NextDouble() * 0.65f;
                    float stoneD = 0.35f + (float)wallRng.NextDouble() * 0.3f;
                    float jitterX = ((float)wallRng.NextDouble() - 0.5f) * 0.3f;
                    float jitterY = ((float)wallRng.NextDouble() - 0.5f) * 0.08f;

                    float centerY = y + rowH * 0.5f + jitterY;
                    float heightFrac = Mathf.InverseLerp(bY, tY, centerY);
                    bool mossy = heightFrac < 0.4f;

                    var stoneMesh = CreateStoneBlockMesh(cubeSrcMesh, new Vector3(stoneW, rowH * 0.9f, stoneD), (float)wallRng.NextDouble() * 1000f, 0.34f);
                    var m = Matrix4x4.TRS(
                        new Vector3(xCol + jitterX, centerY, zFace + (isZ0Side ? -stoneD * 0.2f : stoneD * 0.2f)),
                        Quaternion.Euler(((float)wallRng.NextDouble() - 0.5f) * 14f, ((float)wallRng.NextDouble() - 0.5f) * 22f, ((float)wallRng.NextDouble() - 0.5f) * 12f),
                        Vector3.one);
                    (mossy ? lowerInstances : upperInstances).Add(new CombineInstance { mesh = stoneMesh, transform = m });

                    y += rowH;
                }
            }
        }

        var upperMesh = new Mesh { name = "BridgeStoneUpperMesh" };
        upperMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        upperMesh.CombineMeshes(upperInstances.ToArray(), true, true);
        upperMesh.RecalculateBounds();
        var upperGo = new GameObject("BridgeStoneUpper");
        upperGo.transform.SetParent(bridgeRoot.transform, false);
        upperGo.AddComponent<MeshFilter>().sharedMesh = upperMesh;
        var upperMat = GetOrCreateMat("Mat_BridgeStoneUpper", rockTex, new Vector2(1f, 1f));
        upperMat.color = new Color(0.66f, 0.66f, 0.66f); // neutral worn-gray stone, not brown wood
        upperMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        if (upperMat.HasProperty("_Smoothness")) upperMat.SetFloat("_Smoothness", 0.1f);
        upperGo.AddComponent<MeshRenderer>().sharedMaterial = upperMat;

        var lowerMesh = new Mesh { name = "BridgeStoneLowerMesh" };
        lowerMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        lowerMesh.CombineMeshes(lowerInstances.ToArray(), true, true);
        lowerMesh.RecalculateBounds();
        var lowerGo = new GameObject("BridgeStoneLowerMossy");
        lowerGo.transform.SetParent(bridgeRoot.transform, false);
        lowerGo.AddComponent<MeshFilter>().sharedMesh = lowerMesh;
        var lowerMat = GetOrCreateMat("Mat_BridgeStoneLower", rockTex, new Vector2(1f, 1f));
        lowerMat.color = new Color(0.34f, 0.40f, 0.32f); // damp mossy gray-green, not brown
        lowerMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        if (lowerMat.HasProperty("_Smoothness")) lowerMat.SetFloat("_Smoothness", 0.18f);
        lowerGo.AddComponent<MeshRenderer>().sharedMaterial = lowerMat;

        // ---- Physical collider for the stone masonry itself (walls + underside + road, combined)
        // -- the visual meshes above had NO collider at all, which let players swim straight
        // through the "solid" abutments/walls and pop out on land, bypassing the lake entirely.
        // The arch opening stays open (there's simply no stone geometry there to collide with),
        // exactly matching the visual gap the river flows through. ----
        var bridgeSolidCol = new[]
        {
            new CombineInstance { mesh = upperMesh, transform = Matrix4x4.identity },
            new CombineInstance { mesh = lowerMesh, transform = Matrix4x4.identity },
        };
        var bridgeSolidColMesh = new Mesh { name = "BridgeStoneColliderMesh" };
        bridgeSolidColMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        bridgeSolidColMesh.CombineMeshes(bridgeSolidCol, true, true);
        var bridgeSolidColGo = new GameObject("BridgeStoneCollider");
        bridgeSolidColGo.transform.SetParent(bridgeRoot.transform, false);
        var bridgeSolidMc = bridgeSolidColGo.AddComponent<MeshCollider>();
        bridgeSolidMc.sharedMesh = bridgeSolidColMesh;
        bridgeSolidMc.convex = false;

        // ---- Smooth, simplified collider for the walkable deck -- no noise, no parapets, just
        // the clean top curve, slightly wider than the visual road so edges never catch. ----
        var colTopZ0 = new Vector3[N + 1]; var colTopZ1 = new Vector3[N + 1];
        for (int i = 0; i <= N; i++)
        {
            colTopZ0[i] = new Vector3(xs[i], topYs[i], z0 - 0.4f);
            colTopZ1[i] = new Vector3(xs[i], topYs[i], z1 + 0.4f);
        }
        var colVerts = new List<Vector3>(); var colTris = new List<int>(); var colUVs = new List<Vector2>();
        AddRibbon(colVerts, colTris, colUVs, colTopZ0, colTopZ1, 1f);
        var colliderMesh = new Mesh { name = "BridgeDeckColliderMesh" };
        colliderMesh.SetVertices(colVerts); colliderMesh.SetTriangles(colTris, 0);
        colliderMesh.RecalculateNormals(); colliderMesh.RecalculateBounds();
        var colliderGo = new GameObject("BridgeDeckCollider");
        colliderGo.transform.SetParent(bridgeRoot.transform, false);
        var mc = colliderGo.AddComponent<MeshCollider>();
        mc.sharedMesh = colliderMesh;
        mc.convex = false;

        // ---- Embankment blending: mossy rock clumps + roots at both ends, using the
        // measured-topLocal placement technique (proven not to float) so the bridge's footprint
        // reads as embedded in the bank rather than a prefab dropped on top of the terrain. ----
        // see LoadIndividualMossRocks() for why this is no longer the raw rock_moss_set_01/02 FBX
        var rootsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
        var mossSets = LoadIndividualMossRocks();
        const float rootsTopLocal = 0.122f;
        var embRng = new System.Random(9911);
        foreach (float endX in new[] { spanX0, spanX1 })
        {
            float outward = endX == spanX0 ? -1f : 1f;
            for (int k = 0; k < 4; k++)
            {
                // Placed BEYOND the approach ramp's own footprint (ramp reaches rampLen=5.5 from
                // endX), purely on natural terrain -- the ramp mesh itself already handles the
                // deck-to-ground transition, so this dressing only needs plain ground sampling and
                // never has to guess the ramp's height at some offset Z it doesn't actually cover.
                float ox = endX + outward * (6f + (float)embRng.NextDouble() * 3f);
                float oz = BridgeCenterZ + ((float)embRng.NextDouble() - 0.5f) * (BridgeDeckDepth + 3f);
                float groundY = SampleWorldHeightConservative(terrain, ox, oz, 0.6f);
                bool useRoot = embRng.Next(3) == 0;
                var prefab = useRoot ? rootsPrefab : mossSets[embRng.Next(mossSets.Length)];
                if (prefab == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, bridgeRoot.transform);
                inst.name = "BridgeEmbankment_" + k;
                float scale = useRoot ? (1.2f + (float)embRng.NextDouble() * 0.8f) : (0.35f + (float)embRng.NextDouble() * 0.35f);
                float topLocal = useRoot ? rootsTopLocal : GetPrefabTopLocalY(prefab);
                float topY = groundY + (useRoot ? 0.35f : 0.3f * scale);
                inst.transform.localScale = Vector3.one * scale;
                inst.transform.rotation = Quaternion.Euler(0f, (float)embRng.NextDouble() * 360f, 0f);
                inst.transform.position = new Vector3(ox, topY - topLocal * scale, oz);
            }
        }

        // ---- Approach ramps: the deck sits deckThicknessAtEnds above natural ground even at its
        // very ends (needed for the arch's headroom), which otherwise reads as a floating platform
        // with a hard step where the player would catch on the edge. A sloped ramp (visual + its
        // own matching collider) closes the gap at both ends, blended into real per-point terrain
        // height rather than a single flat ground sample. ----
        var mudTexForRamp = AssetDatabase.LoadAssetAtPath<Texture2D>(PH + "mud_forest/mud_forest_diff_2k.jpg");
        var rampMat = GetOrCreateMat("Mat_BridgeRamp", mudTexForRamp, new Vector2(2f, 2f));
        rampMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        foreach (float endX in new[] { spanX0, spanX1 })
        {
            float outward = endX == spanX0 ? -1f : 1f;
            float rampLen = 5.5f;
            float nearY = deckBaseY; // matches TopY at this end exactly -- no seam against the deck

            int rampSegs = 6;
            var rampZ0 = new Vector3[rampSegs + 1];
            var rampZ1 = new Vector3[rampSegs + 1];
            for (int s = 0; s <= rampSegs; s++)
            {
                float u = s / (float)rampSegs;
                float x = endX + outward * rampLen * u;
                float groundHere = SampleWorldHeight(terrain, x, BridgeCenterZ);
                float y = Mathf.Lerp(nearY, groundHere, u);
                float noise = StoneNoise(x, BridgeCenterZ, 77f) * 0.12f * u;
                rampZ0[s] = new Vector3(x, y + noise, z0);
                rampZ1[s] = new Vector3(x, y + noise, z1);
            }
            var rVerts = new List<Vector3>(); var rTris = new List<int>(); var rUVs = new List<Vector2>();
            AddRibbon(rVerts, rTris, rUVs, rampZ0, rampZ1, 4f);
            var rampMesh = new Mesh { name = "BridgeRampMesh_" + (endX == spanX0 ? "West" : "East") };
            rampMesh.SetVertices(rVerts); rampMesh.SetTriangles(rTris, 0); rampMesh.SetUVs(0, rUVs);
            rampMesh.RecalculateNormals(); rampMesh.RecalculateBounds();

            var rampGo = new GameObject("BridgeApproachRamp_" + (endX == spanX0 ? "West" : "East"));
            rampGo.transform.SetParent(bridgeRoot.transform, false);
            rampGo.AddComponent<MeshFilter>().sharedMesh = rampMesh;
            rampGo.AddComponent<MeshRenderer>().sharedMaterial = rampMat;

            var rampColGo = new GameObject("BridgeApproachRampCollider_" + (endX == spanX0 ? "West" : "East"));
            rampColGo.transform.SetParent(bridgeRoot.transform, false);
            var rampMc = rampColGo.AddComponent<MeshCollider>();
            rampMc.sharedMesh = rampMesh;
            rampMc.convex = false;

            // Moss/rock accents right along the ramp's own edges, reusing the exact per-segment
            // points just computed -- guarantees perfect alignment with the ramp surface (the
            // "橋->古い石->苔->土->森" transition), instead of guessing the ramp's height from a
            // separate formula.
            for (int s = 1; s < rampSegs; s++)
            {
                if (wallRng.NextDouble() < 0.45) continue;
                bool leftSide = wallRng.Next(2) == 0;
                Vector3 basePt = leftSide ? rampZ0[s] : rampZ1[s];
                float edgeOffset = leftSide ? -0.5f : 0.5f;
                var prefab = mossSets[wallRng.Next(mossSets.Length)];
                if (prefab == null) continue;
                float scale = 0.3f + (float)wallRng.NextDouble() * 0.35f;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, bridgeRoot.transform);
                inst.name = "RampAccent";
                inst.transform.localScale = Vector3.one * scale;
                inst.transform.rotation = Quaternion.Euler(0f, (float)wallRng.NextDouble() * 360f, 0f);
                float topY = basePt.y + 0.15f * scale;
                inst.transform.position = new Vector3(basePt.x, topY - GetPrefabTopLocalY(prefab) * scale, basePt.z + edgeOffset);
            }
        }

        log.AppendLine("Stone arch bridge built crossing the river at Z=" + BridgeCenterZ.ToString("F1") +
            ", X span [" + spanX0.ToString("F1") + ", " + spanX1.ToString("F1") + "], deckY=" + deckY.ToString("F2") +
            ", arch clearance=" + (deckY - (springY + archRiseUnderside)).ToString("F2") + "m.");
        return deckY;
    }

    static void AddRibbon(List<Vector3> verts, List<int> tris, List<Vector2> uvs, Vector3[] lineA, Vector3[] lineB, float vTile)
    {
        int baseIdx = verts.Count;
        int n = lineA.Length;
        for (int i = 0; i < n; i++)
        {
            verts.Add(lineA[i]);
            verts.Add(lineB[i]);
            float u = i / (float)(n - 1) * vTile;
            uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f));
        }
        for (int i = 0; i < n - 1; i++)
        {
            int b = baseIdx + i * 2;
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b + 2); tris.Add(b + 1); tris.Add(b + 3);
        }
    }

    static float StoneNoise(float x, float z, float seed) => Mathf.PerlinNoise(x * 0.45f + seed, z * 0.45f - seed) - 0.5f;

    // A single irregular masonry block: a cube primitive with each vertex displaced along its
    // own direction from center by 3D noise, so no two stones come out the same shape -- this is
    // what makes the bridge's walls read as individually stacked stones rather than a smooth
    // bent surface. Cheap (24 verts, reuses the shared cube source mesh) since dozens of these
    // get combined into one draw call afterward.
    static Mesh CreateStoneBlockMesh(Mesh cubeSrc, Vector3 size, float seed, float roughness)
    {
        var mesh = UnityEngine.Object.Instantiate(cubeSrc);
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i]; // primitive cube verts sit at +-0.5 per axis
            Vector3 dir = v.normalized;
            float n1 = Mathf.PerlinNoise(dir.x * 2.5f + seed, dir.y * 2.5f - seed);
            float n2 = Mathf.PerlinNoise(dir.z * 2.5f + seed * 1.4f, dir.x * 2.5f + seed * 0.6f);
            float disp = 1f + (n1 - 0.5f) * roughness + (n2 - 0.5f) * roughness * 0.6f;
            Vector3 vv = v * disp;
            vv.Scale(size);
            verts[i] = vv;
        }
        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Terrain.SampleHeight() returns height in the terrain's LOCAL frame (0..TerrainData.size.y),
    // not world space -- callers must add the terrain GameObject's own Y position to place
    // anything correctly in world space. Use this everywhere instead of calling SampleHeight
    // directly.
    static float SampleWorldHeight(Terrain terrain, float worldX, float worldZ) =>
        terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrain.transform.position.y;

    // On steep slopes (the lake cliff), a single height sample at an object's pivot can land
    // meaningfully lower than the ground under the rest of its footprint, reading as "floating"
    // even with a generous sink margin. Sampling a small ring around the point and taking the
    // highest value is a cheap, robust fix -- erring toward "slightly buried" rather than
    // "floating" is always the safer direction visually.
    static float SampleWorldHeightConservative(Terrain terrain, float worldX, float worldZ, float radius)
    {
        float best = SampleWorldHeight(terrain, worldX, worldZ);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.PI * 2f;
            float x = worldX + Mathf.Cos(a) * radius;
            float z = worldZ + Mathf.Sin(a) * radius;
            best = Mathf.Max(best, SampleWorldHeight(terrain, x, z));
        }
        return best;
    }

    // Local-space bottom-Y of a prefab's mesh (assumes a single MeshFilter at/near the root with
    // an unrotated identity transform, true for the straightforward single-LOD Poly Haven imports
    // this is used for) -- lets large hero-formation props (mountainside, coast_rocks_01) be
    // grounded from their actual measured mesh bounds instead of a hand-guessed constant.
    // BUG FIX 2026-08-14: previously read `mf.sharedMesh.bounds.min.y` directly from the FIRST
    // MeshFilter found, ignoring that MeshFilter's own local transform relative to the prefab root
    // (see GetPrefabLocalBounds for the full story -- boulder_01's LOD children have a 100x local
    // scale that this naive read silently ignored, returning a near-zero value). Both helpers now
    // delegate to the transform-aware GetPrefabLocalBounds so every caller gets the fix.
    static float GetPrefabBottomLocalY(GameObject prefab) => GetPrefabLocalBounds(prefab).min.y;

    // Local-space TOP-Y of a prefab's mesh -- companion to GetPrefabBottomLocalY, for the many
    // existing call sites already built around a "topLocal" (pivot-to-top) placement convention
    // rather than a bottom-offset one.
    static float GetPrefabTopLocalY(GameObject prefab) => GetPrefabLocalBounds(prefab).max.y;

    // ==== 接地ルール (2026-08-14, CLAUDE.md "Rock / Cliff / Boulder / Tree の接地ルール") ====
    // The standard way to place any Rock/Cliff/Boulder asset in this project from now on: get the
    // REAL surface point+normal via a raycast against the Terrain's own collider (never assume a
    // facing direction from lake-center radial geometry, and never sample height at only the
    // object's pivot). This directly replaces the old pattern (used throughout this file up to
    // 2026-08-13) of computing an "outward" direction from FindShoreAtAngle and sampling height in
    // a small ring -- that pattern was the root cause of the repeated "cliff floating off the
    // terrain" reports, because the assumed radial direction can diverge from the real local
    // surface normal on complex terrain, and a narrow height sample doesn't cover a wide asset's
    // full footprint.
    static bool TryGetTerrainSurface(Terrain terrain, float worldX, float worldZ, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        var col = terrain.GetComponent<TerrainCollider>();
        float rayTopY = terrain.transform.position.y + terrain.terrainData.size.y + 20f;
        var ray = new Ray(new Vector3(worldX, rayTopY, worldZ), Vector3.down);
        if (col != null && col.Raycast(ray, out RaycastHit hit, terrain.terrainData.size.y + 40f))
        {
            // Sanity check against the always-reliable heightmap sample (terrain.SampleHeight reads
            // TerrainData directly, no PhysX involved) -- guards against the PhysX heightfield
            // collider occasionally returning a stale hit (e.g. right after a same-frame
            // destroy+recreate of the Terrain GameObject, before its broadphase data is flushed; see
            // the Physics.SyncTransforms() fix in Run()). A real terrain hit should land within a
            // couple meters of the direct heightmap sample at the same xz; a bigger mismatch means
            // the raycast found the wrong surface, so fall back to the trusted sample instead.
            float sampledY = SampleWorldHeight(terrain, worldX, worldZ);
            if (Mathf.Abs(hit.point.y - sampledY) > 2f)
            {
                hitPoint = new Vector3(worldX, sampledY, worldZ);
                hitNormal = Vector3.up;
                return true;
            }
            hitPoint = hit.point;
            hitNormal = hit.normal;
            return true;
        }
        // Should essentially never happen (Terrain always has a TerrainCollider covering its full
        // extent) -- fall back to the old height-sample method rather than silently placing at
        // origin, but this path is a bug signal if it's ever actually hit.
        hitPoint = new Vector3(worldX, SampleWorldHeight(terrain, worldX, worldZ), worldZ);
        hitNormal = Vector3.up;
        return false;
    }

    // Full LOCAL-space mesh bounds, relative to the PREFAB ROOT (not just min/max Y like
    // GetPrefabBottomLocalY/GetPrefabTopLocalY, and NOT a naive read of the first MeshFilter's raw
    // `sharedMesh.bounds`). BUG FOUND 2026-08-14: boulder_01_2k.fbx bundles 4 LOD child meshes,
    // each on a child GameObject with localScale=(100,100,100) relative to the prefab root -- the
    // mesh DATA itself is authored in tiny pre-scale units (bounds ~0.01), so reading
    // `mf.sharedMesh.bounds` directly (ignoring that child's own 100x local transform) returned a
    // near-zero size, which silently produced almost no embedding depth in PlaceBoulderEmbedded --
    // this was the actual cause of CliffBoulder_3/5 (and others) floating after the 接地ルール
    // rewrite. Fixed by transforming each MeshFilter's mesh bounds through its FULL local-to-
    // prefab-root matrix (not just reading raw mesh-space numbers), and unioning across every
    // MeshFilter in the hierarchy (safe even for the boulder_01 case -- all 4 LODs occupy
    // essentially the same space, so the union just matches the real single-rock footprint).
    static Bounds GetPrefabLocalBounds(GameObject prefab)
    {
        if (prefab == null) return new Bounds(Vector3.zero, Vector3.one);
        var mfs = prefab.GetComponentsInChildren<MeshFilter>();
        Matrix4x4 rootWorldToLocal = prefab.transform.worldToLocalMatrix;
        bool any = false;
        Bounds combined = default;
        foreach (var mf in mfs)
        {
            if (mf.sharedMesh == null) continue;
            Matrix4x4 relative = rootWorldToLocal * mf.transform.localToWorldMatrix;
            var mb = mf.sharedMesh.bounds;
            Vector3 c = mb.center, e = mb.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 worldCorner = relative.MultiplyPoint3x4(corner);
                if (!any) { combined = new Bounds(worldCorner, Vector3.zero); any = true; }
                else combined.Encapsulate(worldCorner);
            }
        }
        return any ? combined : new Bounds(Vector3.zero, Vector3.one);
    }

    // Places a large, mostly-flat Cliff/Rock-Face asset flush against the real terrain surface at
    // (worldX,worldZ), yaw-aligned to the HORIZONTAL projection of the real surface normal (kept
    // upright -- large hero rock faces read as standing walls, not tilted slabs, and full-normal
    // tilt risks looking broken if the sample point lands on a small local bump). The object's own
    // measured depth (along whichever local axis is its "front", per faceIsPlusZ) is used to push
    // it backward into the terrain by embedFrac of that depth, so its back is genuinely buried
    // rather than resting flush against a surface it might not perfectly match.
    static GameObject PlaceCliffEmbedded(GameObject prefab, Transform parent, Terrain terrain, float worldX, float worldZ, bool faceIsPlusZ, float scale, float embedFrac, string name)
    {
        if (prefab == null) return null;
        TryGetTerrainSurface(terrain, worldX, worldZ, out Vector3 hitPoint, out Vector3 hitNormal);

        Vector3 flatNormal = new Vector3(hitNormal.x, 0f, hitNormal.z);
        if (flatNormal.sqrMagnitude < 0.0001f) flatNormal = Vector3.forward; // near-flat ground (normal ~straight up) -- arbitrary but stable facing
        flatNormal.Normalize();

        var bounds = GetPrefabLocalBounds(prefab);
        // depthLocal: the object's full extent along its own facing axis (local Z), which becomes
        // its world-space depth once scaled -- this is what "how far to push it back" is measured
        // against, not a guessed constant.
        float depthLocal = bounds.size.z;
        float bottomLocalY = bounds.min.y;

        // faceIsPlusZ: local+Z is the measured true front -> that face should point OUT of the
        // terrain (along flatNormal, away from the solid ground). If local-Z is the true front,
        // local+Z is the back, so world-forward (which LookRotation points local+Z at) must be
        // -flatNormal instead.
        Vector3 worldForward = faceIsPlusZ ? flatNormal : -flatNormal;
        Quaternion rot = Quaternion.LookRotation(worldForward, Vector3.up);

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        inst.name = name;
        inst.transform.rotation = rot;
        inst.transform.localScale = Vector3.one * scale;

        float embedWorld = depthLocal * scale * embedFrac;
        // Anchor at the real hit point, then push backward (into the terrain, opposite the
        // direction the front now faces) by the embed amount, and drop by the object's own
        // measured bottom offset so its base sits at the surface rather than floating at hit.point
        // (which is a surface point, not necessarily where this object's pivot should sit).
        Vector3 pos = hitPoint - worldForward * embedWorld;
        pos.y = hitPoint.y - bottomLocalY * scale * 0.3f; // slight extra sink on top of the backward embed, consistent with every other grounded prop in this file
        inst.transform.position = pos;
        return inst;
    }

    // Places a small/mid Boulder/rock asset embedded into the real local surface, WITH full
    // normal-following tilt (pitch+roll, not just yaw) -- unlike PlaceCliffEmbedded, a boulder
    // naturally sitting on a local slope looks MORE organic tilted to match that slope, not less.
    static GameObject PlaceBoulderEmbedded(GameObject prefab, Transform parent, Terrain terrain, float worldX, float worldZ, float scale, float embedFrac, System.Random rng, string name)
    {
        if (prefab == null) return null;
        TryGetTerrainSurface(terrain, worldX, worldZ, out Vector3 hitPoint, out Vector3 hitNormal);

        var bounds = GetPrefabLocalBounds(prefab);
        float heightLocal = bounds.size.y;
        float bottomLocalY = bounds.min.y;

        // Random yaw around the normal, then tilt the whole thing so local-up follows hitNormal --
        // this is what makes a boulder rest naturally against a sloped rock face instead of
        // standing perfectly vertical regardless of the surface it's on.
        Quaternion yaw = Quaternion.AngleAxis((float)rng.NextDouble() * 360f, Vector3.up);
        Quaternion tiltToNormal = Quaternion.FromToRotation(Vector3.up, hitNormal);
        Quaternion rot = tiltToNormal * yaw;

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        inst.name = name;
        inst.transform.rotation = rot;
        inst.transform.localScale = Vector3.one * scale;

        // Push the pivot down from the hit point along the surface normal (now the object's local
        // "up", post-tilt) by the embed amount, then compensate for the prefab's own bottom offset
        // so the exposed portion is consistent regardless of each rock's individual pivot convention.
        float embedWorld = heightLocal * scale * embedFrac;
        inst.transform.position = hitPoint - hitNormal * embedWorld - hitNormal * (bottomLocalY * scale);
        return inst;
    }

    // ---- Floating-object validation: raycasts from each flagged object's renderer bounds toward
    // its expected support surface (Terrain, or another Rock/Cliff collider) and reports any gap
    // exceeding a small tolerance. Meant to be run at the end of every build as a standard check
    // (per CLAUDE.md's 接地ルール #6), not just an ad-hoc debug tool. ----
    static void ValidateNoFloatingObjects(GameObject root, StringBuilder log)
    {
        string[] prefixes = {
            "HeroCliffFace", "HeroCoastRocks", "CliffBoulder_", "HeroClusterRock_", "HeroClusterRoot_",
            "LakeShore_", "LakebedRock_", "WaterfallFlankRock_", "WaterfallSourceRock_", "WaterfallBaseRock_",
            "HeroLeaningTree_", "HeroCoastalCliffBand", "HeroCoastalCliffBase_", "WaterfallFern_",
            "AncientForestGuardian",
        };
        var all = root.GetComponentsInChildren<Transform>(true);
        int checkedCount = 0, flaggedCount = 0;
        const float tolerance = 0.75f; // meters -- small gaps from bounds/embed rounding are fine, this catches genuine floating
        foreach (var t in all)
        {
            bool matches = false;
            foreach (var p in prefixes) { if (t.name.StartsWith(p) || t.name == p) { matches = true; break; } }
            if (!matches) continue;
            var rend = t.GetComponentInChildren<Renderer>();
            if (rend == null) continue;
            checkedCount++;

            Vector3 origin = rend.bounds.center + Vector3.up * 0.1f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rend.bounds.size.y + 50f))
            {
                float gap = hit.point.y - rend.bounds.min.y;
                // gap is negative-or-small when the bounds already overlap/touch the hit surface;
                // large positive gap means the raycast found ground FAR below the object's own
                // lowest point in a straight-down direction, which for a roughly-vertical asset can
                // be a false positive (e.g. a horizontal branch over open water) -- cross-check with
                // a horizontal-ish probe isn't done here to keep this a fast standard pass; treat as
                // a candidate for manual review, not an automatic failure.
                if (gap > tolerance)
                {
                    flaggedCount++;
                    log.AppendLine("  [FLOATING?] " + t.name + " boundsMinY=" + rend.bounds.min.y.ToString("F2") + " groundBelow=" + hit.point.y.ToString("F2") + " gap=" + gap.ToString("F2"));
                }
            }
        }
        log.AppendLine("Floating-object validation: " + checkedCount + " objects checked, " + flaggedCount + " flagged for review.");
    }

    // ---- rock_moss_set_01/02 are each a "display shelf" of 6-7 UNRELATED individual rocks laid
    // out side by side in one FBX (confirmed via a Blender connected-components scan: each rock is
    // its own disconnected mesh island at its own grid position, not one sculpted rock cluster).
    // Every placement system in this file that used `AssetDatabase.LoadAssetAtPath<GameObject>` on
    // the whole FBX was therefore instantiating all 6-7 rocks together as one rigid prop -- when
    // grounded at a single point, only whichever rock happened to be nearest the anchor read as
    // correctly placed, while the others (spread up to ~6m away in local space) sat wherever the
    // real (non-flat) terrain happened to be under their offset position: floating, buried, or
    // occasionally fine, depending on local terrain shape. This is the confirmed root cause behind
    // the "some rocks in a CliffBoulder/LakeShore instance float while others don't" reports.
    //
    // Fixed by decomposing both sets into 13 standalone single-rock FBX files in Blender (each
    // re-centered to its own geometry bounds and axis-corrected the same way the mountainside/
    // coast_rocks_01 fix used earlier), stored under RockMossIndividual/. This loader replaces the
    // old 2-element {mossSet1, mossSet2} array with all 13 individual rocks; every existing
    // `mossSets[rng.Next(mossSets.Length)]` call site keeps working unchanged (still picks ONE
    // rock at random), it's just picking from real standalone rocks now instead of a bundled set.
    static GameObject[] _individualMossRocksCache;
    static GameObject[] LoadIndividualMossRocks()
    {
        if (_individualMossRocksCache != null) return _individualMossRocksCache;
        const string dir = PH + "RockMossIndividual/";
        string[] names =
        {
            "rock_moss_set_01_rock01", "rock_moss_set_01_rock02", "rock_moss_set_01_rock03",
            "rock_moss_set_01_rock04", "rock_moss_set_01_rock05", "rock_moss_set_01_rock06",
            "rock_moss_set_02_rock07", "rock_moss_set_02_rock08", "rock_moss_set_02_rock09",
            "rock_moss_set_02_rock10", "rock_moss_set_02_rock11", "rock_moss_set_02_rock12",
            "rock_moss_set_02_rock13",
        };
        var list = new List<GameObject>();
        foreach (var n in names)
        {
            FixMossRockMaterial(dir + n + ".fbx", n);
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(dir + n + ".fbx");
            if (p != null) list.Add(p);
        }
        _individualMossRocksCache = list.ToArray();
        return _individualMossRocksCache;
    }

    // The Blender decomposition step (extract_lod3.py, see comment above) exported each rock into
    // RockMossIndividual/ -- a DIFFERENT folder from the original rock_moss_set_01/rock_moss_set_02
    // texture files. Unity's automatic FBX material-texture matching only searches near the FBX's
    // own folder, so it silently failed to link any texture for these 13 EMBEDDED materials,
    // leaving them flat white -- same root-cause pattern as CarryFixTreeMaterials.cs/
    // CarryFixExternalMaterials.cs elsewhere in this project ("外部FBXはUnityの自動マテリアル生成で
    // テクスチャがリンクされず白...に表示される" per CLAUDE.md's own known-pitfalls list).
    //
    // A first attempt just called SetTexture+SetDirty directly on the embedded material sub-asset
    // -- this silently did NOT stick (confirmed via a full-scene material scan: still flat
    // RGBA(0.906,0.906,0.906,1) after rebuild, Unity's default "no texture" gray). Embedded FBX
    // materials aren't reliably persistable that way. CLAUDE.md's own documented fix for exactly
    // this bug class is used instead: force `ModelImporter.materialLocation = External` (extracts
    // the material to a REAL, independently-persistent .mat asset next to the FBX) before wiring
    // textures onto it -- the same pattern CarryFixTreeMaterials.cs already uses successfully.
    static readonly HashSet<string> _mossRockMaterialFixDone = new HashSet<string>();
    static void FixMossRockMaterial(string fbxPath, string rockName)
    {
        if (_mossRockMaterialFixDone.Contains(fbxPath)) return; // idempotent across the many LoadIndividualMossRocks() calls in one build
        _mossRockMaterialFixDone.Add(fbxPath);

        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;
        if (importer.materialLocation != ModelImporterMaterialLocation.External)
        {
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
        }

        string setFolder = rockName.StartsWith("rock_moss_set_01") ? PH + "rock_moss_set_01/" : PH + "rock_moss_set_02/";
        string setName = rockName.StartsWith("rock_moss_set_01") ? "rock_moss_set_01" : "rock_moss_set_02";
        var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(setFolder + setName + "_diff_2k.jpg");
        var rough = AssetDatabase.LoadAssetAtPath<Texture2D>(setFolder + setName + "_rough_2k.jpg");
        var nor = AssetDatabase.LoadAssetAtPath<Texture2D>(setFolder + setName + "_nor_gl_2k.exr");
        if (diff == null) return; // report, don't fabricate a color -- see final report to user

        // Don't guess where/what Unity named the extracted .mat (turned out to be a
        // "Materials/" subfolder, named after the diffuse texture rather than the FBX's own
        // material name) -- read the real reference straight off the reimported prefab instead.
        var reimportedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var mr2 = reimportedPrefab != null ? reimportedPrefab.GetComponentInChildren<MeshRenderer>() : null;
        var mat = mr2 != null ? mr2.sharedMaterial : null;
        if (mat == null) return;

        mat.SetTexture("_BaseMap", diff);
        mat.color = Color.white;
        if (nor != null) { mat.SetTexture("_BumpMap", nor); mat.EnableKeyword("_NORMALMAP"); }
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
    }

    // Some of this project's multi-material Poly Haven tree FBX imports (trunk/branch(es)/leaves)
    // do NOT auto-link their co-located textures the way island_tree_01's did (confirmed for
    // island_tree_03 via CarryTempFindWhiteMats -- all three materials came up flat white despite
    // the diff/nor/rough files sitting right next to the FBX) -- same root-cause pattern CLAUDE.md
    // already documents for external FBX imports, just inconsistent between otherwise-identical
    // Poly Haven downloads, so it must be checked/fixed per-asset rather than assumed.
    //
    // 2026-08-13 correction: this was ALSO the real cause of the reported "white leaves" -- the
    // white-material scan that supposedly cleared tree_small_02 only walked Renderers under the
    // root GameObject hierarchy, but tree_small_02 is used as a TERRAIN TREE PROTOTYPE (referenced
    // via TerrainData.treePrototypes, rendered directly by the Terrain component), which never
    // appears as a child Renderer at all -- so its materials were never actually checked, and were
    // white the whole time. Fixed with the same established technique: force
    // materialLocation=External, then read the real material reference back off the REIMPORTED
    // prefab (not a guessed .mat path) before wiring textures.
    static readonly HashSet<string> _multiMatTreeFixDone = new HashSet<string>();
    static void FixMultiMatTreeMaterials(string fbxPath, string texFolder, string trunkPrefix, string branchPrefix, string leavesPrefix, StringBuilder log)
    {
        if (_multiMatTreeFixDone.Contains(fbxPath)) return;
        _multiMatTreeFixDone.Add(fbxPath);

        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;
        if (importer.materialLocation != ModelImporterMaterialLocation.External)
        {
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
        }

        var trunkDiff = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + trunkPrefix + "_diff_2k.jpg");
        var trunkNor = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + trunkPrefix + "_nor_gl_2k.jpg");
        var branchDiff = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + branchPrefix + "_diff_2k.jpg");
        var branchNor = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + branchPrefix + "_nor_gl_2k.jpg");
        var leavesDiffRaw = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + leavesPrefix + "_diff_2k.jpg");
        var leavesAlpha = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + leavesPrefix + "_alpha_2k.jpg");
        var leavesNor = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + leavesPrefix + "_nor_gl_2k.jpg");
        Texture2D leavesDiff = leavesDiffRaw;
        if (leavesDiffRaw != null && leavesAlpha != null)
            leavesDiff = CombineDiffuseAlpha(leavesDiffRaw, leavesAlpha, "Assets/Stage/Forest/Trees/Tex_" + leavesPrefix + "_diffAlpha.png");

        var reimportedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (reimportedPrefab == null) return;
        int fixedCount = 0;
        foreach (var mr in reimportedPrefab.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) continue;
                bool isLeaves = mat.name.Contains("leaves");
                bool isBranches = mat.name.Contains("branch"); // matches both "branch" and "branches"
                var diff = isLeaves ? leavesDiff : isBranches ? branchDiff : trunkDiff;
                var nor = isLeaves ? leavesNor : isBranches ? branchNor : trunkNor;
                if (diff == null) continue;
                // Force the shader explicitly rather than trusting whatever Unity's auto-import
                // picked -- a Terrain-tree console warning ("must use the Nature/Soft Occlusion
                // shader") seen for tree_small_02 specifically suggested its auto-generated
                // materials may not even be on a URP-compatible shader, which would make a plain
                // _BaseMap texture assignment silently do nothing (wrong property name entirely on
                // a non-URP shader) regardless of whether the texture itself was found.
                if (mat.shader == null || mat.shader.name != "Universal Render Pipeline/Lit")
                    mat.shader = Shader.Find("Universal Render Pipeline/Lit");
                mat.SetTexture("_BaseMap", diff);
                mat.color = Color.white;
                if (nor != null) { mat.SetTexture("_BumpMap", nor); mat.EnableKeyword("_NORMALMAP"); }
                if (isLeaves && leavesAlpha != null)
                {
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cutoff", 0.5f);
                    mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                }
                EditorUtility.SetDirty(mat);
                fixedCount++;
            }
        }
        AssetDatabase.SaveAssets();
        log.AppendLine(fbxPath + " materials fixed: " + fixedCount);
    }

    // ---- Stone staircase: the ONE official way back onto land from the lake. Positioned at
    // StairsAngleDeg, the only other angular window (besides the river inlet) where LakeFactor
    // leaves a gentle grade -- everywhere else around the rim is cliff. Steps are old, mossy,
    // and irregular (varied size/rotation/riser), not uniform blocks, per the reference photo's
    // "long-abandoned, forest-reclaimed" feel. Climbs from the water up to the actual local rim
    // height (not an arbitrary fixed height), so it always reaches real, walkable land. ----
    static void BuildStairs(GameObject root, Terrain terrain, StringBuilder log)
    {
        var stairsRoot = new GameObject("LakeStairs");
        stairsRoot.transform.SetParent(root.transform, false);
        var step = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "cliff_blockQuarter_stone.fbx");
        if (step == null) { log.AppendLine("Stairs: cliff_blockQuarter_stone.fbx not found, skipped."); return; }

        Vector2 center = new Vector2(LakeCenterX, LakeCenterZ);
        Vector2 shorePt = FindShoreAtAngle(StairsAngleDeg);
        Vector2 climbDir = (shorePt - center).normalized;
        float climbCompassAngle = Mathf.Atan2(climbDir.x, climbDir.y) * Mathf.Rad2Deg;
        float baseRotY = climbCompassAngle - 180f; // risers face back down toward the lake

        // Land height: sample well past the shore, out on solid rim ground, so the stairs climb
        // to wherever the actual gentle slope tops out (rather than an arbitrary fixed height).
        Vector2 landPt = center + climbDir * (Vector2.Distance(shorePt, center) + 9f);
        float landY = SampleWorldHeight(terrain, landPt.x, landPt.y);

        const float stepRiserLocal = 0.25f; // measured height of cliff_blockQuarter_stone at scale 1
        float riser = 0.30f;
        float yScale = riser / stepRiserLocal;

        // The FBX's own imported material reference resolved to Kenney's flat-green "grass.mat"
        // instead of "stone.mat" (a stale/incorrect name-matched link from a much earlier import),
        // which made every step render as a flat green box. Force the correct stone material here,
        // darkened and slightly green-tinted so the old flat Kenney toon-stone reads as aged/mossy
        // rather than clashing with the realistic PolyHaven rock around it.
        var kenneyStoneMat = AssetDatabase.LoadAssetAtPath<Material>(Kenney + "Materials/stone.mat");
        Material oldMossyStoneMat = null;
        if (kenneyStoneMat != null)
        {
            oldMossyStoneMat = new Material(kenneyStoneMat);
            oldMossyStoneMat.name = "OldMossyStairStone";
            Color baseCol = new Color(0.26f, 0.27f, 0.22f); // dark, slightly green-gray "old mossy stone"
            if (oldMossyStoneMat.HasProperty("_BaseColor")) oldMossyStoneMat.SetColor("_BaseColor", baseCol);
            if (oldMossyStoneMat.HasProperty("_Color")) oldMossyStoneMat.SetColor("_Color", baseCol);
            if (oldMossyStoneMat.HasProperty("_Smoothness")) oldMossyStoneMat.SetFloat("_Smoothness", 0.05f);
        }

        var rng = new System.Random(4477);
        int i = 0;
        int maxSteps = 26;
        float y = LakeWaterY - 0.2f;
        while (y < landY + 0.1f && i < maxSteps)
        {
            Vector2 p = shorePt + climbDir * (i * 0.6f - 0.5f);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(step, stairsRoot.transform);
            inst.name = "LakeStair_" + i;
            float wobble = ((float)rng.NextDouble() - 0.5f) * 10f;
            float scaleJ = 1f + ((float)rng.NextDouble() - 0.5f) * 0.25f;
            inst.transform.localScale = new Vector3(1.5f * scaleJ, yScale * (0.85f + (float)rng.NextDouble() * 0.3f), 1.5f * scaleJ);
            inst.transform.position = new Vector3(p.x, y + ((float)rng.NextDouble() - 0.5f) * 0.04f, p.y);
            inst.transform.rotation = Quaternion.Euler(0f, baseRotY + wobble, 0f);
            inst.AddComponent<BoxCollider>(); // auto-fits to the mesh bounds
            if (oldMossyStoneMat != null)
            {
                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
                    mr.sharedMaterial = oldMossyStoneMat;
            }

            y += riser;
            i++;
        }

        // Supplementary smooth ramp Collider underneath the visual steps (permitted explicitly --
        // guarantees reliable climbing regardless of how the individual auto-fit step boxes line
        // up with each other; the visual stones stay irregular, only the collision is smoothed).
        if (i > 0)
        {
            Vector2 startP = shorePt + climbDir * -0.5f;
            Vector2 endP = shorePt + climbDir * ((i - 1) * 0.6f + 0.1f);
            Vector3 startPos = new Vector3(startP.x, LakeWaterY - 0.2f, startP.y);
            Vector3 endPos = new Vector3(endP.x, LakeWaterY - 0.2f + i * riser, endP.y);
            var ramp = new GameObject("StairsRampCollider");
            ramp.transform.SetParent(stairsRoot.transform, false);
            ramp.transform.position = (startPos + endPos) * 0.5f;
            ramp.transform.rotation = Quaternion.LookRotation((endPos - startPos).normalized);
            var rampBox = ramp.AddComponent<BoxCollider>();
            rampBox.size = new Vector3(2.4f, 0.35f, Vector3.Distance(startPos, endPos) + 0.6f);
        }

        // Moss/rock/root dressing flanking the stairs so they read as embedded in the cliff,
        // not stuck on top of it, using the same proven (fixed-height) placement as the bridge.
        // see LoadIndividualMossRocks() for why this is no longer the raw rock_moss_set_01/02 FBX
        var rootsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
        var mossSets = LoadIndividualMossRocks();
        const float rootsTopLocal = 0.122f;
        Vector2 sideDir = new Vector2(-climbDir.y, climbDir.x);
        for (int k = 0; k < 10; k++)
        {
            float along = (float)rng.NextDouble() * (i * 0.6f + 3f);
            float side = ((float)rng.NextDouble() - 0.5f) * 6f;
            if (Mathf.Abs(side) < 1.1f) continue; // keep the steps themselves clear
            Vector2 p = shorePt + climbDir * along + sideDir * side;
            float groundY = SampleWorldHeight(terrain, p.x, p.y);
            bool useRoot = rng.Next(3) == 0;
            var prefab = useRoot ? rootsPrefab : mossSets[rng.Next(mossSets.Length)];
            if (prefab == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stairsRoot.transform);
            inst.name = "StairsDressing_" + k;
            float scale = useRoot ? (1.0f + (float)rng.NextDouble() * 0.8f) : (0.4f + (float)rng.NextDouble() * 0.5f);
            float topLocal = useRoot ? rootsTopLocal : GetPrefabTopLocalY(prefab);
            float topY = groundY + (useRoot ? 0.3f : 0.3f * scale);
            inst.transform.localScale = Vector3.one * scale;
            inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            inst.transform.position = new Vector3(p.x, topY - topLocal * scale, p.y);
        }

        log.AppendLine("Lake stairs built: " + i + " steps at angle=" + StairsAngleDeg.ToString("F0") +
            ", shore=(" + shorePt.x.ToString("F1") + "," + shorePt.y.ToString("F1") + "), landY=" + landY.ToString("F2") + ".");
    }


    enum FootKind { GiantBoulder, MossBoulder, Log, RootSpan, DirtMoundSafe, DirtMound, SmallRockCluster, RuinSlab }

    static List<Vector3> BuildFootholds(GameObject root, Terrain terrain, StringBuilder log)
    {
        var footRoot = new GameObject("Footholds");
        footRoot.transform.SetParent(root.transform, false);

        var boulder = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "boulder_01/boulder_01_2k.fbx");
        // see LoadIndividualMossRocks() for why this is no longer the raw rock_moss_set_01/02 FBX
        var logPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "dead_tree_trunk_02/dead_tree_trunk_02_2k.fbx");
        var roots = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
        var ruinSlab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/path_stone.fbx");
        var mossSets = LoadIndividualMossRocks();
        var dirtMat = GetOrCreateMat("Mat_DirtMound", AssetDatabase.LoadAssetAtPath<Texture2D>(PH + "mud_forest/mud_forest_diff_2k.jpg"), new Vector2(2f, 2f));

        // Beat sequence: (kind, gap from previous beat's Z). Deliberately not a repeating
        // pattern -- safe rest stops alternate with big single-rock jumps, a log walk, a
        // root crossing, small-rock accents (used sparingly) and the occasional ruin.
        var beats = new (FootKind kind, float gap)[]
        {
            (FootKind.DirtMoundSafe, 6f),
            (FootKind.GiantBoulder, 4.5f),
            (FootKind.SmallRockCluster, 4f),
            (FootKind.Log, 5f),
            (FootKind.MossBoulder, 5f),
            (FootKind.RootSpan, 4.5f),
            (FootKind.DirtMoundSafe, 5.5f),
            (FootKind.GiantBoulder, 5f),
            (FootKind.Log, 5.5f),
            (FootKind.RuinSlab, 5f),
            (FootKind.MossBoulder, 5.5f),
            (FootKind.DirtMound, 5f),
            (FootKind.RootSpan, 4.5f),
            (FootKind.GiantBoulder, 5.5f),
            (FootKind.SmallRockCluster, 4f),
            (FootKind.Log, 5f),
            (FootKind.DirtMoundSafe, 5.5f),
            (FootKind.MossBoulder, 5f),
            (FootKind.RuinSlab, 4.5f),
            (FootKind.DirtMoundSafe, 5f),
        };

        var rng = new System.Random(555);
        var points = new List<Vector3>();

        // The bridge crosses the river sideways, so walking straight off its far edge doesn't
        // land on the regular route -- a single giant boulder jutting from the river gives a
        // natural first step from the bridge's exit into the course, before the beat sequence
        // (which starts a bit further on) takes over.
        {
            float ez = BridgeZ1 + 3f;
            float erx = RiverX(ez);
            float ewaterY = GroundNoise(erx, ez) - RiverDepth + 1.15f;
            var einst = (GameObject)PrefabUtility.InstantiatePrefab(boulder, footRoot.transform);
            float escale = 2.6f;
            const float etopLocal = 0.930f;
            float etopY = ewaterY + 0.35f * escale;
            einst.transform.localScale = Vector3.one * escale;
            einst.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
            einst.transform.position = new Vector3(erx, etopY - etopLocal * escale, ez);
            einst.name = "BridgeExitBoulder";
            AddSolidCollider(einst, etopLocal * escale);
        }

        float z = FootholdStartZ;
        float prevX = 0f, prevZ = z;

        for (int i = 0; i < beats.Length; i++)
        {
            if (i > 0) z += beats[i].gap;
            float rx = RiverX(z);
            float hw = RiverHalfWidth(z);
            // Weave across the width of the (now much wider) river instead of hugging the centerline.
            float weave = hw * 0.55f * Mathf.Sin(i * 0.8f + 1.3f) + ((float)rng.NextDouble() - 0.5f) * 1.6f;
            float x = rx + weave;
            float waterY = GroundNoise(rx, z) - RiverDepth + 1.15f;

            GameObject inst = null;
            float topLocalHeight = 0f;
            Vector3 placedPos;

            switch (beats[i].kind)
            {
                // Every river-based case below places the object by its known, measured
                // local top-of-mesh offset (from CarryInspectAssetBounds) so the visible top
                // surface lands exactly at "topY", with the rest of the mesh naturally
                // extending down into/under the water -- rather than guessing an offset from
                // the pivot, which is how earlier passes ended up with rocks that read as
                // floating from some angles.
                case FootKind.GiantBoulder:
                {
                    inst = (GameObject)PrefabUtility.InstantiatePrefab(boulder, footRoot.transform);
                    float scale = 2.2f + (float)rng.NextDouble() * 1.0f; // genuinely multiple-goblin-sized
                    const float topLocal = 0.930f;
                    float topY = waterY + 0.35f * scale;
                    inst.transform.localScale = Vector3.one * scale;
                    inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    inst.transform.position = new Vector3(x, topY - topLocal * scale, z);
                    topLocalHeight = topLocal * scale;
                    inst.name = "GiantBoulder_" + i;
                    AddSolidCollider(inst, topLocalHeight);
                    placedPos = new Vector3(x, topY, z);
                    break;
                }
                case FootKind.MossBoulder:
                {
                    var prefab = mossSets[rng.Next(mossSets.Length)];
                    inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, footRoot.transform);
                    float scale = 0.75f + (float)rng.NextDouble() * 0.35f; // rock_moss_set is already huge at scale 1
                    float topLocal = GetPrefabTopLocalY(prefab);
                    float topY = waterY + 0.3f * scale;
                    inst.transform.localScale = Vector3.one * scale;
                    inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    inst.transform.position = new Vector3(x, topY - topLocal * scale, z);
                    topLocalHeight = topLocal * scale;
                    inst.name = "MossBoulder_" + i;
                    AddSolidCollider(inst, topLocalHeight);
                    placedPos = new Vector3(x, topY, z);
                    break;
                }
                case FootKind.Log:
                {
                    float nextZ = z + (i + 1 < beats.Length ? beats[i + 1].gap : 5f);
                    float nextRx = RiverX(nextZ);
                    float nextHw = RiverHalfWidth(nextZ);
                    float nextX = nextRx + nextHw * 0.55f * Mathf.Sin((i + 1) * 0.8f + 1.3f);
                    Vector3 dir = new Vector3(nextX - x, 0f, nextZ - z).normalized;
                    float span = Vector3.Distance(new Vector3(x, 0f, z), new Vector3(nextX, 0f, nextZ));
                    float scale = Mathf.Clamp(span / 4.0f, 2.2f, 5.5f); // a genuinely huge fallen trunk, not a twig
                    const float topLocal = 0.727f;
                    float topY = waterY + 0.4f * scale;
                    inst = (GameObject)PrefabUtility.InstantiatePrefab(logPrefab, footRoot.transform);
                    inst.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler((float)rng.NextDouble() * 6f - 3f, 0f, 0f);
                    inst.transform.localScale = Vector3.one * scale;
                    topLocalHeight = topLocal * scale;
                    inst.transform.position = new Vector3(x, topY - topLocal * scale, z);
                    inst.name = "Log_" + i;
                    AddSolidCollider(inst, topLocalHeight);
                    placedPos = new Vector3(x, topY, z);
                    break;
                }
                case FootKind.RootSpan:
                {
                    inst = (GameObject)PrefabUtility.InstantiatePrefab(roots, footRoot.transform);
                    float scale = 2.2f + (float)rng.NextDouble() * 1.3f; // scaled up into a real crossable root, not ground texture
                    const float topLocal = 0.122f;
                    float topY = waterY + 0.4f;
                    inst.transform.localScale = Vector3.one * scale;
                    inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    inst.transform.position = new Vector3(x, topY - topLocal * scale, z);
                    topLocalHeight = topLocal * scale;
                    inst.name = "RootSpan_" + i;
                    AddSolidCollider(inst, topLocalHeight);
                    placedPos = new Vector3(x, topY, z);
                    break;
                }
                case FootKind.DirtMoundSafe:
                case FootKind.DirtMound:
                {
                    bool safe = beats[i].kind == FootKind.DirtMoundSafe;
                    float radius = safe ? (2.1f + (float)rng.NextDouble() * 0.7f) : (1.3f + (float)rng.NextDouble() * 0.5f);
                    float flatten = 0.42f + (float)rng.NextDouble() * 0.1f;
                    Vector3 topCenter = new Vector3(x, waterY + (safe ? 0.55f : 0.35f), z);
                    inst = CreateDirtMound(footRoot.transform, (safe ? "DirtMoundSafe_" : "DirtMound_") + i, topCenter, radius, flatten, dirtMat, rng);
                    placedPos = topCenter;
                    break;
                }
                case FootKind.SmallRockCluster:
                {
                    // Two small rocks close together -- kept rare, used only as a brief accent.
                    // Each is an independently-chosen individual rock now, so each needs its own
                    // measured topLocal rather than one shared constant.
                    var prefabA = mossSets[rng.Next(mossSets.Length)];
                    var a = (GameObject)PrefabUtility.InstantiatePrefab(prefabA, footRoot.transform);
                    float topLocalA = GetPrefabTopLocalY(prefabA);
                    float sa = 0.22f + (float)rng.NextDouble() * 0.1f;
                    float topYa = waterY + 0.3f * sa;
                    a.transform.localScale = Vector3.one * sa;
                    a.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    a.transform.position = new Vector3(x - 0.9f, topYa - topLocalA * sa, z);
                    a.name = "SmallRockA_" + i;
                    AddSolidCollider(a, topLocalA * sa);

                    var prefabB = mossSets[rng.Next(mossSets.Length)];
                    var bObj = (GameObject)PrefabUtility.InstantiatePrefab(prefabB, footRoot.transform);
                    float topLocalB = GetPrefabTopLocalY(prefabB);
                    float sb = 0.22f + (float)rng.NextDouble() * 0.1f;
                    float topYb = waterY + 0.3f * sb;
                    bObj.transform.localScale = Vector3.one * sb;
                    bObj.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    bObj.transform.position = new Vector3(x + 1.3f, topYb - topLocalB * sb, z + 1.4f);
                    bObj.name = "SmallRockB_" + i;
                    AddSolidCollider(bObj, topLocalB * sb);

                    inst = bObj;
                    placedPos = new Vector3(x + 1.3f, topYb, z + 1.4f);
                    break;
                }
                case FootKind.RuinSlab:
                default:
                {
                    if (ruinSlab != null)
                    {
                        inst = (GameObject)PrefabUtility.InstantiatePrefab(ruinSlab, footRoot.transform);
                        float scale = 2.6f + (float)rng.NextDouble() * 0.8f;
                        const float topLocal = 0.05f;
                        float topY = waterY + 0.1f;
                        inst.transform.localScale = new Vector3(scale, scale, scale * 0.7f);
                        inst.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 8f - 4f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 6f - 3f);
                        inst.transform.position = new Vector3(x, topY - topLocal * scale, z);
                        topLocalHeight = topLocal * scale;
                        inst.name = "RuinSlab_" + i;
                        AddSolidCollider(inst, topLocalHeight);
                        placedPos = new Vector3(x, topY, z);
                    }
                    else
                    {
                        goto case FootKind.GiantBoulder;
                    }
                    break;
                }
            }

            points.Add(placedPos);
            prevX = x; prevZ = z;
        }
        log.AppendLine("Footholds placed: " + beats.Length + " beats (" + string.Join(",", Array.ConvertAll(beats, b => b.kind.ToString())) + ")");
        return points;
    }

    // Procedurally-displaced flattened sphere so "dirt mound" footholds read as an
    // irregular clump of earth, not a brown primitive.
    static GameObject CreateDirtMound(Transform parent, string name, Vector3 topCenter, float radius, float flatten, Material mat, System.Random rng)
    {
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var srcMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        var mesh = UnityEngine.Object.Instantiate(srcMesh);
        UnityEngine.Object.DestroyImmediate(tmp);

        float seed = (float)rng.NextDouble() * 1000f;
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 dir = verts[i].normalized;
            float n1 = Mathf.PerlinNoise(dir.x * 1.6f + seed, dir.z * 1.6f + seed * 1.7f);
            float n2 = Mathf.PerlinNoise(dir.y * 2.4f - seed, dir.x * 2.4f + seed);
            float disp = 1f + (n1 - 0.5f) * 0.55f + (n2 - 0.5f) * 0.28f;
            Vector3 v = dir * disp;
            v.y *= flatten;
            verts[i] = v;
        }
        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;

        float meshTopLocal = mesh.bounds.max.y;
        go.transform.localScale = Vector3.one * radius;
        go.transform.position = topCenter - Vector3.up * (meshTopLocal * radius);

        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = false;
        return go;
    }

    // Recovery points partway down the river are deliberately not placed yet -- per the
    // current spec, falling anywhere on the first route sweeps all the way back to the lake,
    // and the player recovers via the stairs. Mid-river recovery points are future work once
    // this base loop (fall -> river -> lake -> stairs -> bridge) is confirmed solid.
    static void BuildRiverGimmick(GameObject root, Terrain terrain, float refWaterY, Vector3 spawnPos, StringBuilder log)
    {
        var riverRoot = new GameObject("RiverGimmick");
        riverRoot.transform.SetParent(root.transform, false);

        var triggerGo = new GameObject("RiverTriggerVolume");
        triggerGo.transform.SetParent(riverRoot.transform, false);
        float centerZ = (CourseZ0 + RiverZ1) * 0.5f;
        triggerGo.transform.position = new Vector3(0f, refWaterY - 1.3f, centerZ);
        var box = triggerGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(TerrainWidth - 4f, 2.4f, RiverZ1 - CourseZ0);
        triggerGo.AddComponent<RiverTriggerZone>();

        var flowGo = new GameObject("RiverFlowController");
        flowGo.transform.SetParent(riverRoot.transform, false);
        var flow = flowGo.AddComponent<RiverFlowController>();
        // The sweep carries the goblin at a fixed height along the water's surface; near the
        // lake that surface is the lake's own (flat, fixed) level, matching BuildWater's blend.
        flow.riverSurfaceY = LakeWaterY + 0.25f;
        // Sweep stops right at the inlet under the bridge -- from there the player is standing
        // in the lake and must swim/walk to the stairs, not teleported back to the bridge.
        flow.upstreamLimitZ = BridgeZ0 - 2f;
        flow.riverHalfWidth = TerrainWidth * 0.5f - 3f;
        flow.SetInitialCheckpoint(spawnPos);

        var checkpointRoot = new GameObject("Checkpoints");
        checkpointRoot.transform.SetParent(root.transform, false);
        CheckpointObj(checkpointRoot, "Checkpoint_Start", spawnPos, 10f);

        log.AppendLine("River gimmick set up (sweep ends in the lake at Z=" + flow.upstreamLimitZ.ToString("F1") + ").");
    }

    static void CheckpointObj(GameObject parent, string name, Vector3 pos, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.position = pos + Vector3.up * 1f;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(width, 2f, 4f);
        go.AddComponent<CheckpointZone>();
    }

    // ---- Ancient fir tree materials (Poly Haven "fir_tree_01", Blender-decimated 4.2M/2.3M/0.5M
    // tris -> ~30k tris each). The FBX was exported without embedded textures, so Unity's
    // auto-generated per-submesh materials carry the right NAME (matching the original FBX
    // material, e.g. "fir_tree_01_bark") but no texture -- this looks them up by that name and
    // swaps in a real material with the actual downloaded diffuse/normal maps. "dead_branches" has
    // no texture set of its own on Poly Haven, so it reuses the bark material (both plain wood). ----
    static readonly Dictionary<string, Material> _firTreeMatCache = new Dictionary<string, Material>();
    static Material GetOrCreateFirTreeMaterial(string matKey, StringBuilder log)
    {
        if (_firTreeMatCache.TryGetValue(matKey, out var cached)) return cached;

        const string texFolder = "Assets/ExternalAssets/PolyHaven/fir_tree_01/Textures/";
        string lookupKey = matKey == "fir_tree_01_dead_branches" ? "fir_tree_01_bark" : matKey;
        string matPath = "Assets/Stage/Forest/Trees/Mat_" + matKey + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Stage/Forest/Trees"))
                AssetDatabase.CreateFolder("Assets/Stage/Forest", "Trees");
            bool isTwig = lookupKey.EndsWith("_twig");
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + lookupKey + "_diff_1k.png");
            var norm = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + lookupKey + "_nor_gl_1k.png");
            if (isTwig)
            {
                // The diffuse PNG has no alpha channel of its own -- Poly Haven ships transparency
                // as a separate grayscale mask. Bake it into the diffuse texture's alpha channel so
                // a single _BaseMap can drive both color and cutout, same combine technique used for
                // the bridge's packed metallic/smoothness map.
                var alphaMask = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + lookupKey + "_alpha_1k.png");
                if (diff != null && alphaMask != null)
                    diff = CombineDiffuseAlpha(diff, alphaMask, "Assets/Stage/Forest/Trees/Tex_" + lookupKey + "_diffAlpha.png");
            }
            if (diff != null) mat.SetTexture("_BaseMap", diff);
            if (norm != null)
            {
                SetTextureImporterType(norm, TextureImporterType.NormalMap);
                mat.SetTexture("_BumpMap", norm);
                mat.EnableKeyword("_NORMALMAP");
                mat.SetFloat("_BumpScale", 1f);
            }
            mat.SetFloat("_Smoothness", 0.12f);
            if (isTwig)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cutoff", 0.5f);
                mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }
            AssetDatabase.CreateAsset(mat, matPath);
            log.AppendLine("Fir tree material created: " + matKey + " (diff=" + (diff != null) + ", normal=" + (norm != null) + ").");
        }
        _firTreeMatCache[matKey] = mat;
        return mat;
    }

    static GameObject SetupFirTreePrefab(string fbxPath, string name, StringBuilder log)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (src == null) { log.AppendLine("Fir tree FBX not found: " + fbxPath); return null; }

        string prefabPath = "Assets/Stage/Forest/Trees/" + name + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null) return existing;

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null) mats[i] = GetOrCreateFirTreeMaterial(mats[i].name, log);
            mr.sharedMaterials = mats;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Stage/Forest/Trees"))
            AssetDatabase.CreateFolder("Assets/Stage/Forest", "Trees");
        var prefab = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
        UnityEngine.Object.DestroyImmediate(inst);
        log.AppendLine("Ancient fir tree prefab built: " + name);
        return prefab;
    }

    // ---- Shrub materials: the raw shrub_01/02 FBX downloads have no embedded textures (same
    // situation as the ancient fir trees and, earlier, the lake stairs) -- their auto-generated
    // Unity materials have no diffuse texture assigned and render solid white. This wires up the
    // real downloaded diffuse/normal/alpha maps explicitly. ----
    static Material GetOrCreateShrubMaterial(string shrubName, StringBuilder log)
    {
        string matPath = "Assets/Stage/Forest/Trees/Mat_" + shrubName + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat != null) return mat;

        string texFolder = "Assets/ExternalAssets/PolyHaven/" + shrubName + "/Textures/";
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + shrubName + "_diff_1k.jpg");
        var norm = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + shrubName + "_nor_gl_1k.jpg");
        var alphaMask = AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + shrubName + "_alpha_1k.jpg");
        if (diff != null && alphaMask != null)
            diff = CombineDiffuseAlpha(diff, alphaMask, "Assets/Stage/Forest/Trees/Tex_" + shrubName + "_diffAlpha.png");
        if (diff != null) mat.SetTexture("_BaseMap", diff);
        if (norm != null)
        {
            SetTextureImporterType(norm, TextureImporterType.NormalMap);
            mat.SetTexture("_BumpMap", norm);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_BumpScale", 1f);
        }
        mat.SetFloat("_Smoothness", 0.15f);
        mat.SetFloat("_AlphaClip", 1f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetFloat("_Cutoff", 0.4f);
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        if (!AssetDatabase.IsValidFolder("Assets/Stage/Forest/Trees"))
            AssetDatabase.CreateFolder("Assets/Stage/Forest", "Trees");
        AssetDatabase.CreateAsset(mat, matPath);
        log.AppendLine("Shrub material created: " + shrubName + " (diff=" + (diff != null) + ", normal=" + (norm != null) + ").");
        return mat;
    }

    static GameObject SetupShrubPrefab(string fbxPath, string shrubName, StringBuilder log)
    {
        string prefabPath = "Assets/Stage/Forest/Trees/" + shrubName + "_Textured.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null) return existing;

        var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (src == null) { log.AppendLine("Shrub FBX not found: " + fbxPath); return null; }

        var mat = GetOrCreateShrubMaterial(shrubName, log);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            mr.sharedMaterial = mat;

        if (!AssetDatabase.IsValidFolder("Assets/Stage/Forest/Trees"))
            AssetDatabase.CreateFolder("Assets/Stage/Forest", "Trees");
        var prefab = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
        UnityEngine.Object.DestroyImmediate(inst);
        log.AppendLine("Shrub prefab built: " + shrubName);
        return prefab;
    }

    // ---- Mass forest: Terrain tree instances so the sides of the valley read as forest that
    // keeps going, not a wall of individually-placed props. ----
    static void BuildTrees(Terrain terrain, StringBuilder log)
    {
        // Art-direction pass (2026-08-13): removed the Quaternius stylized trees (CommonTree_1,
        // DeadTree_1, DeadTree_2) entirely per direct feedback -- "テイストの違う木が混在してる".
        // CommonTree_1 alone was ~78% of every non-giant tree across the WHOLE forest (giants only
        // covered the lake rim/outer edge), so the low-poly cartoon style was actually the dominant
        // visual identity of the forest, not a minor accent. The whole bulk population now draws
        // from the same realistic Poly Haven photoscan species already used for giants/hero
        // specimens, so there is only one visual language across the entire forest. No realistic
        // "dead/bare standing tree" asset exists in the project or was found free elsewhere (the
        // downloaded dead_tree_trunk is a fallen LOG, not a standing trunk) -- that silhouette
        // variety is dropped rather than faked with a mismatched asset; a real one would be a good
        // future Meshy candidate if that variety is wanted back.
        //
        // Real photoscanned ancient-fir prototypes (Poly Haven fir_tree_01, Blender-decimated to
        // ~30k tris each) -- now the PRIMARY species for the whole forest, not just outer-rim giants.
        const string FirDecFolder = "Assets/ExternalAssets/PolyHaven/fir_tree_01/Decimated/";
        var firA = SetupFirTreePrefab(FirDecFolder + "fir_tree_01_a_decimated.fbx", "AncientFir_A", log);
        var firB = SetupFirTreePrefab(FirDecFolder + "fir_tree_01_b_decimated.fbx", "AncientFir_B", log);
        var firC = SetupFirTreePrefab(FirDecFolder + "fir_tree_01_c_decimated.fbx", "AncientFir_C", log);
        // D variant: same source mesh, but with a Blender "Simple Deform: Bend" modifier applied and
        // baked in (38 deg along the trunk's own axis) before decimation/export -- a genuinely
        // curved-trunk silhouette ("幹そのものが湾曲している古木"), not just the same straight tree
        // rotated. Silhouette variety was the single biggest complaint about the lake surround
        // reading as monotonous/artificial.
        var firD = SetupFirTreePrefab(FirDecFolder + "fir_tree_01_curved_decimated.fbx", "AncientFir_D_Curved", log);

        // tree_small_02 (Poly Haven CC0, 2026-08-13): a dense-leafed canopy tree, added specifically
        // to answer the art-direction brief's core complaint -- "幹より枝葉が目立たない" / "重要なのは
        // 木の本数ではなく樹冠のボリューム". Unlike the fir/CommonTree species (columnar, sparse
        // canopy), this one's own scan geometry has a broad leaf mass, so scaling it up to giant size
        // reads as real foliage volume rather than a taller version of the same thin silhouette.
        // CORRECTED 2026-08-13: originally assumed to auto-link its co-located textures like
        // island_tree_01 did, and "confirmed" via CarryTempFindWhiteMats -- but that scan only walks
        // Renderers parented under the scene root, and this prefab is used purely as a
        // TerrainData.treePrototypes entry (rendered by the Terrain component itself, never
        // instantiated as a child GameObject), so it was never actually checked and its leaves WERE
        // white this whole time -- this is what the user actually saw as "白く見えている木の葉".
        // Fixed the same way island_tree_03 was.
        string treeSmall02Folder = PH + "tree_small_02/";
        FixMultiMatTreeMaterials(treeSmall02Folder + "tree_small_02_decimated.fbx", treeSmall02Folder, "tree_small_02", "tree_small_02_branch", "tree_small_02_leaves", log);
        var leafyGiant = AssetDatabase.LoadAssetAtPath<GameObject>(treeSmall02Folder + "tree_small_02_decimated.fbx");

        var prototypes = new List<TreePrototype>();
        foreach (var p in new[] { firA, firB, firC, firD, leafyGiant })
        {
            if (p == null) continue;
            prototypes.Add(new TreePrototype { prefab = p });
        }
        terrain.terrainData.treePrototypes = prototypes.ToArray();
        int firProtoStart = 0; // firs are now the first prototypes
        int firVariantCount = firD != null ? 4 : 3;
        bool hasFirProtos = firA != null && firB != null && firC != null;
        int leafyGiantProto = leafyGiant != null ? prototypes.FindIndex(p => p.prefab == leafyGiant) : -1;

        var rng = new System.Random(31337);
        var instances = new List<TreeInstance>();
        int nearLakeCandidateCount = 0, nearLakeGiantCount = 0, nearLakeRealFirCount = 0; // temp diagnostics for the lake-proximity giant/fir gating
        // Widened from 3.2m and base placement chance thinned below (0.72 -> 0.58) -- now that EVERY
        // tree is a 30-90k-tri realistic scan instead of mostly-cheap stylized meshes, raw instance
        // count needed to come down somewhat to keep the total triangle budget reasonable, even
        // though the forest should still read as dense (each real tree is visually "worth" more than
        // the small stylized ones it replaced).
        float spacing = 3.5f;

        float bridgeCenterX = RiverX(BridgeCenterZ);
        float bridgeHalfSpanX = RiverHalfWidth(BridgeCenterZ) + BankFalloff + 2f + 2f; // + margin
        for (float z = OriginZ + 3f; z < OriginZ + TerrainLength - 3f; z += spacing)
        {
            for (float x = OriginX + 3f; x < OriginX + TerrainWidth - 3f; x += spacing)
            {
                float jx = x + ((float)rng.NextDouble() - 0.5f) * spacing * 0.9f;
                float jz = z + ((float)rng.NextDouble() - 0.5f) * spacing * 0.9f;

                // The exclusion margin around the river/path used to be a flat constant, so the
                // nearest surviving trees all lined up along a smooth parallel curve -- reading as
                // "planted in two rows" rather than a natural treeline. Perturbing the margin itself
                // with noise lets that front edge wobble in and out irregularly instead.
                float marginNoise = StoneNoise(jx, jz, 211f) * 3.2f; // StoneNoise is already zero-centered (-0.5..0.5)

                float rx = RiverX(jz);
                float hw = RiverHalfWidth(jz);
                if (hw > 0.01f && Mathf.Abs(jx - rx) < hw + BankFalloff + 2.5f + marginNoise) continue; // keep the channel + banks clear
                float lakeF = LakeFactor(jx, jz);
                if (lakeF > 0.08f) continue; // keep the lake and its shore clear
                if (jz > BridgeZ0 - 3f && jz < BridgeZ1 + 3f && Mathf.Abs(jx - bridgeCenterX) < bridgeHalfSpanX) continue; // keep the (now wide) bridge crossing clear

                float normX = Mathf.Clamp01((jx - OriginX) / TerrainWidth);
                float normZ = Mathf.Clamp01((jz - OriginZ) / TerrainLength);
                // Terrain trees can only rotate around Y (no tilt), so a full-size trunk planted on a
                // steep slope always reads as vertically wrong -- floating away from the ground on
                // the downhill side. Real forests don't grow full trees on near-vertical faces either.
                // Skip placement entirely above a real-world plausible slope angle instead of trying
                // to fake a tilt the tree system can't express -- this is what was producing the
                // "floating tree" reports near the lake cliff (confirmed via diagnostic: several
                // lake-adjacent trees sat on 39-67 degree slopes).
                if (terrain.terrainData.GetSteepness(normX, normZ) > 38f) continue;

                // Rough WORLD-UNIT distance from the lake shore (not lakeF, which is nearly useless
                // for this: its falloff band is only ~1-2m wide across the steep cliff sections by
                // design, for a sharp unclimbable edge -- far narrower than this loop's 3.2m tree
                // grid spacing, so almost no grid points ever actually sample a mid-range lakeF
                // value there). Approximated from the same normalized elliptical distance LakeFactor
                // itself uses, without the (expensive, per-candidate binary-search) exact
                // FindShoreAtAngle call -- accurate enough for a "is this tree near the lake" gate.
                float lakeDxN = (jx - LakeCenterX) / LakeRadiusX;
                float lakeDzN = (jz - LakeCenterZ) / LakeRadiusZ;
                float lakeDistFromShore = (Mathf.Sqrt(lakeDxN * lakeDxN + lakeDzN * lakeDzN) - 1f) * ((LakeRadiusX + LakeRadiusZ) * 0.5f);
                bool nearLakeRim = lakeDistFromShore < 22f; // widened from 16m -- per feedback the leafy-canopy treatment needs to cover more of what's actually visible from the lake/bridge, not just the immediate rim

                // Denser canopy right along the lake's cliff rim (big trees crowding the top of
                // the cliff, per the reference photo) -- but not over the river inlet or the
                // stairs approach, which need to stay open.
                float rimBoost = 0f;
                if (lakeF > 0.015f)
                {
                    float angDeg = Mathf.Atan2(jx - LakeCenterX, jz - LakeCenterZ) * Mathf.Rad2Deg;
                    rimBoost = (1f - LakeGentleWeight(angDeg)) * 0.24f;
                }

                // Denser toward the terrain's outer edge too -- per spec, the forest's outer
                // boundary should not be easy to see through/past.
                float distToEdgeX = Mathf.Min(jx - OriginX, OriginX + TerrainWidth - jx);
                float distToEdgeZ = Mathf.Min(jz - OriginZ, OriginZ + TerrainLength - jz);
                float distToEdge = Mathf.Min(distToEdgeX, distToEdgeZ);
                float edgeBoost = Mathf.Clamp01(Mathf.InverseLerp(30f, 6f, distToEdge)) * 0.22f;

                if ((float)rng.NextDouble() > 0.58f + rimBoost + edgeBoost) continue; // thinned for the heavier realistic meshes (was 0.72 when most trees were cheap stylized ones)

                // Every tree is now one of the realistic photoscan species -- no more style split
                // between an "ordinary" cheap species and "special" realistic giants. isGiant now
                // purely controls SIZE TIER (mature vs. younger-scale specimen of the same real
                // species), and useLeafyGiant picks which real species within that tier.
                bool isGiant = (distToEdge < 10f || nearLakeRim) ? (float)rng.NextDouble() < 0.55f : (float)rng.NextDouble() < 0.15f;

                bool useLeafyGiant = leafyGiantProto >= 0 && (float)rng.NextDouble() < 0.3f; // tree_small_02 as ~30% of the population for canopy-shape variety against the fir-dominant majority
                int proto = useLeafyGiant ? leafyGiantProto : (hasFirProtos ? firProtoStart + rng.Next(firVariantCount) : 0);
                if (proto >= prototypes.Count) proto = 0;

                // fir_tree_01's own native scale is already a mature/tall tree (14-19m), unlike the
                // old stylized CommonTree_1 which needed a large multiplier to reach forest-canopy
                // size -- so "ordinary" here means scaling a bit DOWN from full size (younger-
                // reading specimen), and "giant" means at or above full native size, not a further
                // multiplier on top of an already-giant mesh.
                float scale = useLeafyGiant
                    ? (isGiant ? 3.0f + (float)rng.NextDouble() * 1.3f : 1.4f + (float)rng.NextDouble() * 1.0f) // native ~4.6m -> giant 14-19m / ordinary 6.4-9.6m
                    : (isGiant ? 1.0f + (float)rng.NextDouble() * 0.45f : 0.55f + (float)rng.NextDouble() * 0.4f); // native 14-19m -> giant full-to-larger / ordinary a smaller/younger specimen

                // Both species are real photoscans -- keep their own texture color, just a faint
                // per-instance brightness jitter (not a hue tint) so repeated use of only 5 source
                // variants across the whole forest doesn't look stamped.
                float tone = (float)rng.NextDouble();
                Color treeColor = Color.white * (0.88f + tone * 0.2f);

                instances.Add(new TreeInstance
                {
                    position = new Vector3(normX, 0f, normZ),
                    prototypeIndex = proto,
                    widthScale = scale,
                    heightScale = scale,
                    color = treeColor,
                    lightmapColor = Color.white,
                    rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                });
                if (nearLakeRim) nearLakeCandidateCount++;
                if (nearLakeRim && isGiant) nearLakeGiantCount++;
                if (nearLakeRim && !useLeafyGiant) nearLakeRealFirCount++;
            }
        }
        terrain.terrainData.SetTreeInstances(instances.ToArray(), true);
        log.AppendLine("Lake-adjacent trees: " + nearLakeCandidateCount + " candidates, " + nearLakeGiantCount + " giants, " + nearLakeRealFirCount + " firs.");

        // The realistic photoscan species have no baked billboard atlas either, so Unity's
        // auto-generated billboard LOD (used once trees are far enough away) rendered as
        // flat, wrong-colored rectangular cards floating among the trees. Force every tree
        // to render at full mesh LOD regardless of distance instead.
        terrain.treeDistance = 260f;
        terrain.treeBillboardDistance = 250f;
        terrain.treeCrossFadeLength = 0f;
        terrain.treeMaximumFullLODCount = Mathf.Max(1000, instances.Count + 50);

        log.AppendLine("Terrain tree instances: " + instances.Count);
    }

    // ---- AzureCrystal (Meshy-generated, 2026-08-14): the "魔力を帯びたポーションの源" -- the
    // environmental explanation for WHY this lake's water carries magic. Placed as a sparse,
    // vein-like concentration (NOT even distribution): the waterfall crevices get the crack/gap
    // variants so the "underground crystal vein -> magic water seeps from the rock -> waterfall ->
    // sapphire lake" causal chain reads in the landscape itself, the lakebed gets a faint
    // underwater glow, and a couple of accents sit among ordinary shore rocks. All placement is
    // raycast-based per CLAUDE.md 接地ルール; the crystals' pivots are at their rock-base
    // bottom-center (set in Blender during the 5-way split), and each grows along its local +Y, so
    // orienting +Y along the surface normal and pushing back along -normal embeds the rock base
    // into the host surface with only the blue crystal tips exposed. ----
    static void BuildAzureCrystals(GameObject root, Terrain terrain, StringBuilder log)
    {
        const string PrefabDir = "Assets/Stage/Forest/Crystal/Prefabs/";
        var pfLakeFloor = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "PF_AzureCrystal_LakeFloor.prefab");
        var pfCliffWall = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "PF_AzureCrystal_CliffWall.prefab");
        var pfRockGap = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "PF_AzureCrystal_RockGap.prefab");
        var pfCliffCrack = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "PF_AzureCrystal_CliffCrack.prefab");
        var pfRock = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "PF_AzureCrystal_Rock.prefab");
        if (pfLakeFloor == null && pfCliffCrack == null)
        {
            log.AppendLine("AzureCrystal prefabs not found -- run Carry/Setup Azure Crystals first. Skipped.");
            return;
        }

        var crystalRoot = new GameObject("AzureCrystals");
        crystalRoot.transform.SetParent(root.transform, false);
        var center = new Vector2(LakeCenterX, LakeCenterZ);
        var rng = new System.Random(4242);
        int placed = 0;

        // Embed a crystal into the surface at (worldX, worldZ): local +Y (growth axis) follows the
        // real surface normal, random spin around that axis, rock base buried by embedFrac of the
        // model's own scaled height.
        GameObject PlaceCrystal(GameObject prefab, float worldX, float worldZ, float scale, float embedFrac, string name)
        {
            if (prefab == null) return null;
            TryGetTerrainSurface(terrain, worldX, worldZ, out Vector3 hitPoint, out Vector3 hitNormal);
            var bounds = GetPrefabLocalBounds(prefab);
            float heightWorld = bounds.size.y * scale;

            Quaternion spin = Quaternion.AngleAxis((float)rng.NextDouble() * 360f, Vector3.up);
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, hitNormal);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, crystalRoot.transform);
            inst.name = name;
            inst.transform.rotation = tilt * spin;
            inst.transform.localScale = Vector3.one * scale;
            inst.transform.position = hitPoint - hitNormal * (heightWorld * embedFrac);
            placed++;
            return inst;
        }

        // ---- Lakebed cluster: 2 formations on the lake floor, off-center toward the far shore so
        // they read from the bridge as a faint blue glow through the water, not a centerpiece.
        // Scale is capped so the TIP stays safely below the water surface -- the spec explicitly
        // bans crystals bursting up out of the lake.
        if (pfLakeFloor != null)
        {
            // NOTE: a "move them deeper toward the center" attempt (rFrac 0.34/0.30) actually
            // SKIPPED both formations -- this lake's carved bed turns out to be shallowest near
            // the center (flat-bottomed carve + a slight central mound from GroundNoise), so the
            // mid-radius band is the deepest available water. The formations stay modest (~0.8m)
            // by physical necessity; that also happens to match the brief ("湖底から淡い青色の魔力
            // が見える程度" -- a faint glow, not a centerpiece).
            // Mixed formation sizes: 2 statement clusters (as before) plus 3 smaller satellite
            // formations, so the bed reads as a vein spilling out at varying scale rather than two
            // uniform blobs.
            var bedSpots = new[]
            {
                (ang: 200f, rFrac: 0.55f, s: 5.5f),
                (ang: 155f, rFrac: 0.45f, s: 4.0f),
                (ang: 175f, rFrac: 0.50f, s: 2.3f),
                (ang: 220f, rFrac: 0.42f, s: 1.3f),
                (ang: 165f, rFrac: 0.38f, s: 0.8f),
            };
            foreach (var spot in bedSpots)
            {
                Vector2 shore = FindShoreAtAngle(spot.ang);
                float shoreR = Vector2.Distance(shore, center);
                Vector2 dir = (shore - center).normalized;
                Vector2 p = center + dir * (shoreR * spot.rFrac);
                TryGetTerrainSurface(terrain, p.x, p.y, out Vector3 bedPt, out _);
                float nativeH = GetPrefabLocalBounds(pfLakeFloor).size.y;
                float maxScale = (LakeWaterY - 0.35f - bedPt.y) / Mathf.Max(0.01f, nativeH); // tip >= 0.35m under the surface
                if (maxScale <= 0.15f) continue; // bed at/above waterline here -- skip rather than poke out of the water
                float s = Mathf.Min(spot.s, maxScale);
                var inst = PlaceCrystal(pfLakeFloor, p.x, p.y, s, 0.12f, "AzureCrystal_LakeFloor_" + (int)spot.ang);
                // One very soft blue point light on the main formation only -- "湖底から淡い青色の
                // 魔力が見える程度",範囲も強度も控えめ (NOT a searchlight).
                if (inst != null && spot.ang == 200f)
                {
                    var lightGo = new GameObject("AzureCrystal_GlowLight");
                    lightGo.transform.SetParent(inst.transform, false);
                    lightGo.transform.localPosition = new Vector3(0f, nativeH * 0.5f, 0f);
                    var l = lightGo.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(0.45f, 0.72f, 1f);
                    l.intensity = 1.1f;
                    l.range = 6f;
                    l.shadows = LightShadows.None;
                }
            }
        }

        // ---- Waterfall crevice vein: the story centerpiece. CliffCrack embedded in the rock right
        // beside the two most prominent falls (195=main, 225), where the flanking crevice rocks
        // already are, plus RockGap tucked between those flank rocks -- the "地下鉱脈から魔力水が
        // 湧く" concentration point. Buried deep (only crystal tips out of the rock).
        var veinSpots = new (GameObject pf, float ang, float rMul, float scale, float embed, string nm)[]
        {
            (pfCliffCrack, 191f, 1.06f, 3.6f, 0.42f, "AzureCrystal_CliffCrack_Fall195"),
            (pfCliffCrack, 228f, 1.07f, 3.0f, 0.45f, "AzureCrystal_CliffCrack_Fall225"),
            (pfRockGap,    198f, 1.04f, 2.6f, 0.38f, "AzureCrystal_RockGap_Fall195"),
            (pfRockGap,    252f, 1.05f, 2.4f, 0.40f, "AzureCrystal_RockGap_Fall255"),
            // Cliff wall accents away from the falls -- sparse single spots, not a coating.
            (pfCliffWall,  210f, 1.10f, 3.2f, 0.30f, "AzureCrystal_CliffWall_210"),
            (pfCliffWall,  115f, 1.09f, 2.7f, 0.32f, "AzureCrystal_CliffWall_115"),
            (pfCliffCrack, 303f, 1.09f, 2.8f, 0.45f, "AzureCrystal_CliffCrack_305"),
            // Small satellite fragments around the same crevices/walls -- the vein "spilling"
            // outward at a smaller scale, not just uniform-sized statement pieces.
            (pfRockGap,    205f, 1.03f, 1.1f, 0.35f, "AzureCrystal_RockGap_Fall205_Small"),
            (pfRock,       194f, 1.05f, 0.9f, 0.35f, "AzureCrystal_Rock_Fall194_Small"),
            (pfRock,       231f, 1.06f, 0.7f, 0.35f, "AzureCrystal_Rock_Fall231_Small"),
            (pfCliffWall,  245f, 1.08f, 1.4f, 0.28f, "AzureCrystal_CliffWall_245_Small"),
            (pfCliffWall,  190f, 1.11f, 0.9f, 0.28f, "AzureCrystal_CliffWall_190_Tiny"),
            (pfCliffCrack, 218f, 1.06f, 1.6f, 0.40f, "AzureCrystal_CliffCrack_218_Small"),
        };
        foreach (var v in veinSpots)
        {
            if (v.pf == null) continue;
            Vector2 shore = FindShoreAtAngle(v.ang);
            float shoreR = Vector2.Distance(shore, center);
            Vector2 dir = (shore - center).normalized;
            Vector2 p = center + dir * (shoreR * v.rMul);
            PlaceCrystal(v.pf, p.x, p.y, v.scale, v.embed, v.nm);
        }

        // ---- Shore rock accents: crystallized rocks mixed in among the ordinary boulders of the
        // BoulderOverhang zone and the mossy-bank arc -- not standalone gems on open ground.
        if (pfRock != null)
        {
            var rockSpots = new[]
            {
                (ang: 108f, rMul: 1.12f, s: 2.6f),
                (ang: 132f, rMul: 1.15f, s: 2.0f),
                (ang: 95f,  rMul: 1.10f, s: 1.0f),
                (ang: 120f, rMul: 1.18f, s: 1.6f),
                (ang: 148f, rMul: 1.14f, s: 0.7f),
                (ang: 142f, rMul: 1.09f, s: 3.0f),
            };
            foreach (var spot in rockSpots)
            {
                Vector2 shore = FindShoreAtAngle(spot.ang);
                float shoreR = Vector2.Distance(shore, center);
                Vector2 dir = (shore - center).normalized;
                Vector2 p = center + dir * (shoreR * spot.rMul);
                PlaceCrystal(pfRock, p.x, p.y, spot.s, 0.25f, "AzureCrystal_Rock_" + (int)spot.ang);
            }
        }

        log.AppendLine("Azure crystals placed: " + placed);
    }

    // ---- Vines on the cliff face -- no free/CC0 vine or ivy asset exists on Poly Haven, Quaternius,
    // Kenney, or itch.io after two separate research passes this project (confirmed 2026-08-13), so
    // rather than leave this gap or fake it with an unrelated asset, this builds simple procedural
    // hanging vine ribbons directly (same technique already used for the waterfalls: a thin curved
    // ribbon mesh with noise-driven sideways wobble). Sparse and clustered near crevices/rock
    // formations/waterfalls specifically, not a uniform coating -- per the brief's own emphasis that
    // ground cover needs "生える理由", these hang from the same spots the hero rock faces and
    // waterfalls already anchor to. ----
    static void BuildCliffVines(GameObject root, Terrain terrain, StringBuilder log)
    {
        var vineRoot = new GameObject("CliffVines");
        vineRoot.transform.SetParent(root.transform, false);
        var center = new Vector2(LakeCenterX, LakeCenterZ);

        var mat = GetOrCreateMat("Mat_Vine", null, Vector2.one);
        // Pushed more saturated/opaque than a first pass -- at 0.92 alpha with a dim color the
        // strand was reading as a washed-out gray sliver against the dark rock rather than a
        // visible green vine (confirmed via a lake-ring screenshot).
        mat.color = new Color(0.10f, 0.30f, 0.12f, 1f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
        SetTransparent(mat);
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        var fern = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "fern_02/fern_02_2k.fbx");
        var rng = new System.Random(24680);
        int vineCount = 0;

        void PlaceVineCluster(float ang, float radiusMul, int strandCount)
        {
            Vector2 shore = FindShoreAtAngle(ang);
            Vector2 dir = (shore - center).normalized;
            float shoreR = Vector2.Distance(shore, center);
            Vector2 anchor = center + dir * (shoreR * radiusMul);
            float topY = SampleWorldHeightConservative(terrain, anchor.x, anchor.y, 2f);

            var clusterGo = new GameObject("VineCluster_" + (int)ang);
            clusterGo.transform.SetParent(vineRoot.transform, false);

            for (int s = 0; s < strandCount; s++)
            {
                float lateral = ((float)rng.NextDouble() - 0.5f) * 3.5f; // spread strands out along the rock face
                float startY = topY - (float)rng.NextDouble() * 1.5f; // start just below the true rim, as if emerging from a crevice/ledge, not the exact top edge
                float length = 3.5f + (float)rng.NextDouble() * 4.5f;
                float width = 0.22f + (float)rng.NextDouble() * 0.12f;

                int seg = 10;
                var verts = new List<Vector3>();
                var tris = new List<int>();
                var uvs = new List<Vector2>();
                for (int i = 0; i <= seg; i++)
                {
                    float t = i / (float)seg;
                    float y = startY - t * length;
                    // Perpendicular-to-radial lateral wobble (not toward/away from the wall, which
                    // would clip through it) plus a slight outward droop near the bottom.
                    Vector2 tangent = new Vector2(-dir.y, dir.x);
                    float wobble = (StoneNoise(anchor.x + lateral, y, 40f + s * 13f)) * 0.6f * t;
                    Vector2 p2 = anchor + tangent * (lateral + wobble) + dir * (0.15f * t); // droop slightly outward from the wall as it hangs
                    float taper = Mathf.Lerp(1f, 0.35f, t); // tapers to a thin tip
                    Vector3 left = new Vector3(p2.x, y, p2.y) - new Vector3(tangent.x, 0, tangent.y) * (width * taper * 0.5f);
                    Vector3 right = new Vector3(p2.x, y, p2.y) + new Vector3(tangent.x, 0, tangent.y) * (width * taper * 0.5f);
                    verts.Add(left); verts.Add(right);
                    uvs.Add(new Vector2(0f, t)); uvs.Add(new Vector2(1f, t));
                    if (i > 0)
                    {
                        int b = (i - 1) * 2;
                        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                        tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                    }
                }
                var mesh = new Mesh { name = "Vine" };
                mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.SetUVs(0, uvs);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var go = new GameObject("Vine_" + s);
                go.transform.SetParent(clusterGo.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                vineCount++;
            }

            // A couple of ferns at the base, where the vines meet the rock/water line -- reinforces
            // "植物が生える理由" (shaded, damp crevice) rather than the vines hanging in isolation.
            if (fern != null)
            {
                for (int f = 0; f < 2; f++)
                {
                    float fx = anchor.x + ((float)rng.NextDouble() - 0.5f) * 2f;
                    float fz = anchor.y + ((float)rng.NextDouble() - 0.5f) * 2f;
                    float fy = SampleWorldHeightConservative(terrain, fx, fz, 0.3f);
                    var finst = (GameObject)PrefabUtility.InstantiatePrefab(fern, clusterGo.transform);
                    finst.transform.position = new Vector3(fx, fy, fz);
                    finst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    finst.transform.localScale = Vector3.one * (0.5f + (float)rng.NextDouble() * 0.4f);
                }
            }
        }

        // Sparse -- a handful of spots near existing rock-face/waterfall anchors, not a coating
        // around the whole rim. Deliberately skips angles near the bridge (0 deg) entirely.
        PlaceVineCluster(150f, 1.12f, 3);
        PlaceVineCluster(195f, 1.10f, 2);
        PlaceVineCluster(215f, 1.16f, 3);
        PlaceVineCluster(255f, 1.10f, 2);
        PlaceVineCluster(300f, 1.14f, 3);

        log.AppendLine("Cliff vines placed: " + vineCount + " strands.");
    }

    // ---- Hand-placed "hero" leaning trees reaching from the cliff top toward the lake --
    // Unity Terrain Trees can only rotate around Y (no tilt), so the genuinely tilted/curved
    // silhouette the brief specifically calls out ("崖から湖へ伸びる木") can only come from real
    // individual GameObjects, not the bulk terrain-tree pass above. Uses island_tree_01 (Poly
    // Haven CC0, a real photoscanned coastal tree that already has a pronounced natural lean
    // baked into its scan data -- no synthetic bend needed). The lean direction was measured in
    // Blender (area-weighted canopy-centroid minus trunk-base-centroid, in the same Y-up local
    // space this project's FBX export pipeline always bakes) rather than assumed, following the
    // lesson from the HeroCliffFace orientation bug earlier this project. ----
    static void BuildLakeHeroLeaningTrees(GameObject root, Terrain terrain, StringBuilder log)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "island_tree_01/island_tree_01_decimated.fbx");
        if (prefab == null) { log.AppendLine("Lake hero leaning trees: island_tree_01 prefab not found, skipped."); return; }
        // island_tree_03 (Poly Haven CC0, 2026-08-13): a second gnarled/curved-trunk species so the
        // 5 hero specimens aren't all the exact same prefab (spec explicitly prohibits "同じ木
        // Prefabを同じScaleで大量配置する"). Its measured top/bottom centroid lean is much weaker
        // than island_tree_01's (an S-curved rather than one-directional lean, so the two ends
        // partly cancel in this simple metric) -- still yaw-aligned the same way for consistency,
        // it just reads as a more upright gnarled specimen rather than a dramatic reach.
        FixMultiMatTreeMaterials(PH + "island_tree_03/island_tree_03_decimated.fbx", PH + "island_tree_03/", "island_tree_03", "island_tree_03_branches", "island_tree_03_leaves", log);
        var prefab2 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "island_tree_03/island_tree_03_decimated.fbx");

        var heroRoot = new GameObject("LakeHeroLeaningTrees");
        heroRoot.transform.SetParent(root.transform, false);

        float bottomLocalY = GetPrefabBottomLocalY(prefab);
        float bottomLocalY2 = GetPrefabBottomLocalY(prefab2);
        // Measured via Blender (top-15%-of-height canopy centroid minus bottom-10% trunk-base
        // centroid, local XZ only): the model's own natural lean points this way in its own local
        // space. Whatever local direction this is, yaw-rotating the whole tree aims that same
        // lean at any desired world direction -- here, back toward the lake.
        Vector2 localLeanDir = new Vector2(0.461f, -0.888f);
        float localLeanAngle = Mathf.Atan2(localLeanDir.x, localLeanDir.y) * Mathf.Rad2Deg;
        Vector2 localLeanDir2 = new Vector2(0.139f, -0.042f);
        float localLeanAngle2 = Mathf.Atan2(localLeanDir2.x, localLeanDir2.y) * Mathf.Rad2Deg;

        // A root cluster at each hero tree's own base -- "岩を掴む木の根" growing out from under
        // the trunk over the rock, rather than the tree just standing on bare soil.
        var rootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "root_cluster_02/root_cluster_02_decimated.fbx");
        float rootBottomY = GetPrefabBottomLocalY(rootPrefab);

        var center = new Vector2(LakeCenterX, LakeCenterZ);
        var rng = new System.Random(9001);
        int placed = 0;

        void PlaceHero(float zoneAng, float radiusMul, float scale, float yawJitterDeg, string name, bool useSpecies2 = false)
        {
            var thisPrefab = useSpecies2 ? prefab2 : prefab;
            if (thisPrefab == null) return;
            float thisBottomY = useSpecies2 ? bottomLocalY2 : bottomLocalY;
            float thisLeanAngle = useSpecies2 ? localLeanAngle2 : localLeanAngle;

            Vector2 shore = FindShoreAtAngle(zoneAng);
            Vector2 dir = (shore - center).normalized; // outward, shore -> land
            float shoreR = Vector2.Distance(shore, center);
            Vector2 anchor = center + dir * (shoreR * radiusMul); // cliff-top, back from the edge

            // 2026-08-14 FIX (user-reported: HeroLeaningTree_245 etc. reading as "parallel to the
            // wall" instead of rooted into it): these anchors sit on the steep cliff shoulder, not
            // flat ground -- e.g. HeroLeaningTree_245's surface normal there is (0.59,0.57,0.57), a
            // ~55deg slope. The old code only ever applied a yaw (world Y-up trunk regardless of
            // slope), so on a slope that steep the trunk ran visually alongside the rock face rather
            // than growing OUT of it. A real tree rooted in a crevice on a slope like this grows
            // roughly perpendicular to the surface at its base (then often curves toward the light --
            // which is exactly what this species' own baked-in lean already represents once yawed
            // toward the lake), so tilt the trunk's base to the real local surface normal the same
            // way PlaceBoulderEmbedded does for rocks, instead of forcing it to stay world-vertical.
            TryGetTerrainSurface(terrain, anchor.x, anchor.y, out Vector3 hitPoint, out Vector3 hitNormal);

            Vector2 towardLake = -dir;
            float desiredAngle = Mathf.Atan2(towardLake.x, towardLake.y) * Mathf.Rad2Deg;
            float yaw = desiredAngle - thisLeanAngle + yawJitterDeg;

            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            Quaternion tiltToNormal = Quaternion.FromToRotation(Vector3.up, hitNormal);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(thisPrefab, heroRoot.transform);
            inst.name = name;
            inst.transform.localScale = Vector3.one * scale;
            inst.transform.rotation = tiltToNormal * yawRot;
            // Small embed so the exposed root flare isn't a hard seam against the slope, same
            // convention as every other grounded hero prop in this file.
            float embed = 0.15f * scale;
            inst.transform.position = hitPoint - hitNormal * (thisBottomY * scale) - hitNormal * embed;
            placed++;

            if (rootPrefab != null)
            {
                float rootScale = scale * (0.35f + (float)rng.NextDouble() * 0.2f);
                Vector2 rootOff = new Vector2((float)(rng.NextDouble() - 0.5) * 0.8f, (float)(rng.NextDouble() - 0.5) * 0.8f);
                Vector2 rp = anchor + rootOff;
                // Same slope-following tilt as the trunk above (rather than a flat Y-only placement)
                // so the root flare hugs the same sloped/rock surface the trunk is actually rooted
                // into, instead of sitting flat while the trunk emerges at an angle above it.
                TryGetTerrainSurface(terrain, rp.x, rp.y, out Vector3 rootHitPoint, out Vector3 rootHitNormal);
                Quaternion rootTilt = Quaternion.FromToRotation(Vector3.up, rootHitNormal);
                Quaternion rootYaw = Quaternion.Euler(0f, yaw + 90f * (rng.Next(2) == 0 ? 1 : -1), 0f);
                var rootInst = (GameObject)PrefabUtility.InstantiatePrefab(rootPrefab, heroRoot.transform);
                rootInst.name = name + "_Roots";
                rootInst.transform.localScale = Vector3.one * rootScale;
                rootInst.transform.rotation = rootTilt * rootYaw;
                rootInst.transform.position = rootHitPoint - rootHitNormal * (rootBottomY * rootScale);
                placed++;
            }
        }

        // Five specimens spread across both the left and right cliff shoulders (not clustered in
        // one spot), each offset a little from the existing hero rock-face anchors (180/210/305
        // deg) so the tree reads as growing alongside/out-of that rock rather than through it.
        // Scaled up from source size for real old-growth presence at the cliff top, per "湖側へ力強く
        // 伸びる古木". Species alternated (island_tree_01 / island_tree_03) so no two adjacent
        // specimens are the same prefab.
        PlaceHero(130f, 1.18f, 1.9f, -8f, "HeroLeaningTree_130");
        PlaceHero(195f, 1.16f, 2.1f, 6f, "HeroLeaningTree_195", useSpecies2: true);
        PlaceHero(245f, 1.20f, 1.75f, 10f, "HeroLeaningTree_245");
        PlaceHero(285f, 1.17f, 2.0f, -5f, "HeroLeaningTree_285", useSpecies2: true);
        PlaceHero(160f, 1.14f, 4.2f, 20f, "HeroLeaningTree_160", useSpecies2: true); // island_tree_03 is only ~2.6m native, needs a bigger multiplier to read as a mature specimen

        log.AppendLine("Lake hero leaning trees placed: " + placed);
    }

    // ---- Ancient Forest Guardian (2026-08-14, user-supplied Meshy model, see
    // CarrySetupAncientForestGuardianTree.cs / ASSET_LICENSES.md #8): a single, unique hero
    // specimen -- not part of the mass-placed forest or the 5-tree leaning-tree set. Placed on the
    // clifftop directly above/behind the new sacred waterfall (190deg, the potion-source landmark),
    // standing watch over it from the rim -- the "guardian" the model's own name implies, visually
    // tying the game's central gameplay landmark (goblin returns here for potions) to a deliberately
    // singular, unmistakable tree rather than blending into the ordinary tree cover. ----
    static void BuildAncientForestGuardianTree(GameObject root, Terrain terrain, StringBuilder log)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stage/Forest/Trees/AncientForestGuardian/Prefabs/PF_AncientForestGuardian.prefab");
        if (prefab == null) { log.AppendLine("AncientForestGuardian prefab not found -- run Carry/Setup Ancient Forest Guardian Tree first. Skipped."); return; }

        var center = new Vector2(LakeCenterX, LakeCenterZ);
        // 2026-08-14: the first attempt used radiusMul=1.62, which turned out to sit on the steep
        // MID-climb of the cliff shoulder (surface normal.y ~0.4, a ~65deg slope) rather than the
        // clifftop -- the tree ended up tilted almost sideways, reading as another leaning tree
        // rather than a dignified standing guardian. A normal-probe sweep along this angle found the
        // climb only actually levels out into a real plateau around radiusMul=2.0 (normal.y=0.99,
        // height ~20.7) -- moved there instead so it stands upright on genuinely flat ground.
        const float ang = 183f;
        const float radiusMul = 2.0f; // the real clifftop plateau above the sacred waterfall, not the mid-slope
        const float scale = 4.5f; // native bounds ~2m -> ~9m tall, an old-growth "guardian" presence

        Vector2 shore = FindShoreAtAngle(ang);
        Vector2 dir = (shore - center).normalized;
        float shoreR = Vector2.Distance(shore, center);
        Vector2 anchor = center + dir * (shoreR * radiusMul);

        // Same raycast-based grounding + normal-tilt convention as PlaceBoulderEmbedded/PlaceHero
        // (接地ルール) -- the clifftop here is fairly flat, but this keeps the tree correctly
        // grounded even if the rim happens to be locally sloped at this exact spot.
        TryGetTerrainSurface(terrain, anchor.x, anchor.y, out Vector3 hitPoint, out Vector3 hitNormal);
        var bounds = GetPrefabLocalBounds(prefab);
        float bottomLocalY = bounds.min.y;

        // Face generally back toward the lake/waterfall it's watching over, not a random spin.
        Vector2 towardLake = -dir;
        float yaw = Mathf.Atan2(towardLake.x, towardLake.y) * Mathf.Rad2Deg;
        Quaternion tiltToNormal = Quaternion.FromToRotation(Vector3.up, hitNormal);
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
        inst.name = "AncientForestGuardian";
        inst.transform.localScale = Vector3.one * scale;
        inst.transform.rotation = tiltToNormal * yawRot;
        float embed = 0.15f * scale; // same modest root-flare burial as every other grounded hero prop here
        inst.transform.position = hitPoint - hitNormal * (bottomLocalY * scale) - hitNormal * embed;

        log.AppendLine("Ancient Forest Guardian tree placed at " + inst.transform.position);
    }

    // ---- Ground vegetation (grass/fern/moss) via Unity's Terrain Detail system instead of
    // individually-placed GameObjects. This sidesteps the whole "floating small plant" bug class
    // that ground-litter props kept hitting -- detail instances are painted directly onto the
    // terrain surface by Unity itself, so they always sit exactly on the heightmap, no matter how
    // steep or uneven. Density is boosted near the lake shore, bridge and stairs (highest-traffic,
    // most-visible areas) and kept moderate everywhere else walkable, so the forest floor reads as
    // covered rather than a bare Terrain material. ----
    static void BuildGroundVegetation(Terrain terrain, StringBuilder log)
    {
        var data = terrain.terrainData;
        var fern = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "fern_02/fern_02_2k.fbx");
        var grass = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "grass_medium_01/grass_medium_01_2k.fbx");
        var moss = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "moss_01/moss_01_2k.fbx");
        var grass2 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "grass_medium_02/grass_medium_02_1k.fbx");
        var candidates = new[] { fern, grass, moss, grass2 };

        var protos = new List<DetailPrototype>();
        var protoIndex = new List<int>(); // maps proto list index -> 0=fern,1=grass,2=moss,3=grass2
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] == null) continue;
            protos.Add(new DetailPrototype
            {
                prototype = candidates[i],
                usePrototypeMesh = true,
                renderMode = DetailRenderMode.VertexLit,
                useInstancing = true,
                minWidth = 0.7f,
                maxWidth = 1.4f,
                minHeight = 0.6f,
                maxHeight = 1.2f,
                useDensityScaling = true,
                noiseSeed = 17 + i,
            });
            protoIndex.Add(i);
        }
        if (protos.Count == 0) { log.AppendLine("Ground vegetation: no detail prototypes found, skipped."); return; }
        data.detailPrototypes = protos.ToArray();

        const int detailRes = 256;
        data.SetDetailResolution(detailRes, 16);
        terrain.detailObjectDistance = 90f;
        terrain.detailObjectDensity = 1.0f;

        var maps = new int[protos.Count][,];
        for (int L = 0; L < protos.Count; L++) maps[L] = new int[detailRes, detailRes];

        var rng = new System.Random(6060);
        float bridgeCenterX = RiverX(BridgeCenterZ);
        for (int zi = 0; zi < detailRes; zi++)
        {
            float normZ = zi / (float)(detailRes - 1);
            float worldZ = OriginZ + normZ * TerrainLength;
            for (int xi = 0; xi < detailRes; xi++)
            {
                float normX = xi / (float)(detailRes - 1);
                float worldX = OriginX + normX * TerrainWidth;

                float rx = RiverX(worldZ);
                float hw = RiverHalfWidth(worldZ);
                if (hw > 0.01f && Mathf.Abs(worldX - rx) < hw + 0.8f) continue; // never in the water itself
                float lakeF = LakeFactor(worldX, worldZ);
                if (lakeF > 0.05f) continue; // never in the lake itself

                // Never on a steep slope -- grass/fern/moss shouldn't grow on a near-vertical rock
                // face, and this was the actual cause of small bright/white triangular flecks seen
                // in QA screenshots along the lake cliff top edge AND the bridge approach mound:
                // the lakeF>0.001 shoreline density boost below and the distBridge<16f boost both
                // fire well INTO the steep cliff/mound band (lakeF's transition there is only ~1-2m
                // wide, and the bridge approach mound climbs steeply right at its edges), so Terrain
                // Detail was painting grass onto near-vertical terrain, which reads as thin cards
                // sticking out sideways catching hard grazing light. A blanket slope gate fixes both
                // at once without needing to hand-tune either boost's falloff band.
                if (data.GetSteepness(normX, normZ) > 42f) continue;

                // Base forest-floor coverage everywhere walkable, boosted near the lake shore,
                // river bank, and bridge -- the highest-traffic, most-visible ground.
                float density = 0.24f;
                float distToRiverBank = hw > 0.01f ? Mathf.Abs(Mathf.Abs(worldX - rx) - hw) : 999f;
                if (distToRiverBank < 6f) density += 0.22f * (1f - distToRiverBank / 6f);
                if (lakeF > 0.001f) density += 0.35f; // right at the lake's shoreline band
                else
                {
                    float distBridge = Mathf.Sqrt((worldX - bridgeCenterX) * (worldX - bridgeCenterX) + (worldZ - BridgeCenterZ) * (worldZ - BridgeCenterZ));
                    if (distBridge < 16f) density += 0.18f * (1f - distBridge / 16f);
                }

                // Natural clumping (patches, not a uniform lawn) via low-frequency noise, plus a
                // little high-frequency jitter so it doesn't look like a repeating pattern.
                float clump = Mathf.PerlinNoise(worldX * 0.06f + 500f, worldZ * 0.06f + 500f);
                density *= Mathf.Lerp(0.35f, 1.15f, clump);
                density = Mathf.Clamp01(density + ((float)rng.NextDouble() - 0.5f) * 0.08f);
                if (density <= 0.01f) continue;

                // Pick a species mix appropriate to the spot: moss-heavy near the lake/river (damp),
                // fern in general forest shade, grass a lighter accent throughout.
                for (int L = 0; L < protos.Count; L++)
                {
                    int kind = protoIndex[L]; // 0=fern,1=grass,2=moss,3=grass2
                    float weight = kind == 2 ? (lakeF > 0.001f || distToRiverBank < 6f ? 1.3f : 0.5f)
                                 : kind == 0 ? 1.0f
                                 : kind == 3 ? 0.55f // second grass species: sparser accent, breaks up grass_medium_01 repeating uniformly
                                 : 0.7f;
                    int count = Mathf.RoundToInt(density * weight * 5f);
                    if (count > 0) maps[L][zi, xi] = count;
                }
            }
        }
        for (int L = 0; L < protos.Count; L++) data.SetDetailLayer(0, 0, L, maps[L]);

        log.AppendLine("Ground vegetation detail layers painted: " + protos.Count + " species, resolution " + detailRes + "^2.");
    }

    // ---- Ground litter: real, individually-scattered assets close to the camera/route
    // (roots, dry branches, small rocks). No sapling trees here -- the terrain tree layer
    // (BuildTrees) already covers near-camera canopy density. ----
    static void BuildGroundDetail(GameObject root, Terrain terrain, StringBuilder log)
    {
        var detailRoot = new GameObject("GroundDetail");
        detailRoot.transform.SetParent(root.transform, false);

        var roots = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
        var branches = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "dry_branches_medium_01/dry_branches_medium_01_2k.fbx");
        var mossRocks = LoadIndividualMossRocks(); // see LoadIndividualMossRocks() -- was rock_moss_set_01_2k.fbx directly (a 6-rock bundle placed as one rigid prop)
        var rootCluster = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "root_cluster_01/root_cluster_01_1k.fbx");
        // fern_02/grass_medium_01/moss_01 removed from ground litter -- these small plants
        // were showing up visibly floating above the ground from some camera angles.
        // fir_sapling/pine_sapling_small ("Sapling_*") removed entirely per feedback.

        var rng = new System.Random(2468);
        int placed = 0;

        // Ground litter band close to the river on both banks.
        for (float z = CourseZ0 - 4f; z < RiverZ1 + 4f; z += 1.4f)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                float rx = RiverX(z);
                float hw = RiverHalfWidth(z);
                float x = rx + side * (hw + 0.3f + (float)rng.NextDouble() * 5f);
                float groundY = SampleWorldHeight(terrain, x, z);

                int roll = rng.Next(4);
                GameObject prefab = roll switch { 0 => roots, 1 => branches, 2 => mossRocks[rng.Next(mossRocks.Length)], _ => rootCluster ?? roots };
                if (prefab == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, detailRoot.transform);
                inst.name = "Litter_" + placed;
                float scale = roll == 2 ? 0.25f + (float)rng.NextDouble() * 0.2f : 0.8f + (float)rng.NextDouble() * 0.9f;
                float posY = groundY;
                if (roll == 2)
                {
                    // Individual rocks are pivoted at their own bounds center now (not a "sits at
                    // pivot" convention) -- rest the true bottom at groundY, then sink it in ~30%
                    // of its own half-height so it reads as embedded rather than perched exactly on
                    // the surface.
                    float bottomLocalY = GetPrefabBottomLocalY(prefab);
                    posY = groundY - bottomLocalY * scale - Mathf.Abs(bottomLocalY) * scale * 0.3f;
                }
                inst.transform.position = new Vector3(x, posY, z);
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                inst.transform.localScale = Vector3.one * scale;
                placed++;
            }
        }

        // tree_stump_01/02 render as a low flat disc rather than an upright stump at any
        // scale correction found so far, so bank-side "big rock" set dressing uses the
        // mossy rock cluster instead (also fixes the "floating platform" look it had here).
        for (int i = 0; i < 10; i++)
        {
            var rockPrefab = mossRocks[rng.Next(mossRocks.Length)];
            if (rockPrefab == null) continue;
            float z = CourseZ0 + (float)rng.NextDouble() * (RiverZ1 - CourseZ0);
            float rx = RiverX(z);
            float hw = RiverHalfWidth(z);
            float side = rng.Next(2) == 0 ? -1f : 1f;
            float x = rx + side * (hw + 1.5f + (float)rng.NextDouble() * 3f);
            float groundY = SampleWorldHeight(terrain, x, z);
            float scale = 0.35f + (float)rng.NextDouble() * 0.25f;
            float bottomLocalY = GetPrefabBottomLocalY(rockPrefab);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(rockPrefab, detailRoot.transform);
            inst.name = "BankRock_" + i;
            inst.transform.position = new Vector3(x, groundY - bottomLocalY * scale - Mathf.Abs(bottomLocalY) * scale * 0.3f, z);
            inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            inst.transform.localScale = Vector3.one * scale;
            placed++;
        }

        log.AppendLine("Ground detail instances: " + placed);
    }

    // ---- General forest-floor clutter across the WHOLE stage (not just the narrow riverbank band
    // BuildGroundDetail covers) -- fallen logs, roots, and mossy rock outcrops scattered through
    // the open forest floor so it doesn't read as bare ground between tree trunks. Density follows
    // the same "older growth" environmental signal as the tree canopy (denser near the lake rim and
    // the terrain's outer edge) rather than being uniform-random everywhere. Uses only prefabs with
    // an already-confirmed correct scale/grounding elsewhere in this file (tree_stump_01/02 are
    // deliberately excluded -- noted above as rendering as a flat disc, not an upright stump, at
    // any scale tried). ----
    static void BuildForestFloorClutter(GameObject root, Terrain terrain, StringBuilder log)
    {
        var clutterRoot = new GameObject("ForestFloorClutter");
        clutterRoot.transform.SetParent(root.transform, false);

        var roots = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
        var branches = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "dry_branches_medium_01/dry_branches_medium_01_2k.fbx");
        var barkDebris = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "bark_debris_01/bark_debris_01_2k.fbx");
        // see LoadIndividualMossRocks() for why this is no longer the raw rock_moss_set_01/02 FBX
        var boulder = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "boulder_01/boulder_01_2k.fbx");
        var logPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "dead_tree_trunk_02/dead_tree_trunk_02_2k.fbx");
        // shrub_01/02 are each a *pack* of several separate bush variants side by side (not one
        // single bush), used deliberately as-is here -- planting the whole pack as one clump reads
        // as a natural thicket, which is exactly the "Layer 3: 低木" ground-cover variety this stage
        // was missing, without needing a Blender split-per-variant pass. Their own pivot sits near
        // vertical CENTER of the combined pack (measured via Blender), not at the base like the
        // other props here, so placement below anchors off the pack's measured bottom-Y instead of
        // a "topLocal" offset.
        var shrub1 = SetupShrubPrefab(PH + "shrub_01/shrub_01_1k.fbx", "shrub_01", log);
        var shrub2 = SetupShrubPrefab(PH + "shrub_02/shrub_02_1k.fbx", "shrub_02", log);
        const float shrub1BottomY = -0.105f;
        const float shrub2BottomY = -0.939f;
        var mossSets = LoadIndividualMossRocks();
        const float rootsTopLocal = 0.122f;
        const float boulderTopLocal = 0.930f;
        const float logTopLocal = 0.727f;

        var rng = new System.Random(9944);
        float bridgeCenterX = RiverX(BridgeCenterZ);
        float bridgeHalfSpanX = RiverHalfWidth(BridgeCenterZ) + BankFalloff + 2f + 2f;
        int placed = 0;
        float spacing = 5f;
        for (float z = OriginZ + 4f; z < OriginZ + TerrainLength - 4f; z += spacing)
        {
            for (float x = OriginX + 4f; x < OriginX + TerrainWidth - 4f; x += spacing)
            {
                float jx = x + ((float)rng.NextDouble() - 0.5f) * spacing * 0.85f;
                float jz = z + ((float)rng.NextDouble() - 0.5f) * spacing * 0.85f;

                float rx = RiverX(jz);
                float hw = RiverHalfWidth(jz);
                // Stay off the walked embankment band itself -- BuildGroundDetail already dresses
                // right at the bank -- so this pass only fills the wider forest floor beyond it.
                if (hw > 0.01f && Mathf.Abs(jx - rx) < hw + BankFalloff + 2f) continue;
                float lakeF = LakeFactor(jx, jz);
                if (lakeF > 0.02f) continue; // lake shore has its own dedicated dressing pass
                if (jz > BridgeZ0 - 3f && jz < BridgeZ1 + 3f && Mathf.Abs(jx - bridgeCenterX) < bridgeHalfSpanX) continue;

                float rimBoost = 0f;
                if (lakeF > 0.001f)
                {
                    float angDeg = Mathf.Atan2(jx - LakeCenterX, jz - LakeCenterZ) * Mathf.Rad2Deg;
                    rimBoost = (1f - LakeGentleWeight(angDeg)) * 0.2f;
                }
                float distToEdgeX = Mathf.Min(jx - OriginX, OriginX + TerrainWidth - jx);
                float distToEdgeZ = Mathf.Min(jz - OriginZ, OriginZ + TerrainLength - jz);
                float distToEdge = Mathf.Min(distToEdgeX, distToEdgeZ);
                float edgeBoost = Mathf.Clamp01(Mathf.InverseLerp(30f, 6f, distToEdge)) * 0.2f;

                float clump = Mathf.PerlinNoise(jx * 0.05f + 900f, jz * 0.05f + 900f);
                float chance = Mathf.Clamp01((0.30f + rimBoost + edgeBoost) * Mathf.Lerp(0.4f, 1.3f, clump));
                if ((float)rng.NextDouble() > chance) continue;

                float groundY = SampleWorldHeight(terrain, jx, jz);
                int roll = rng.Next(100);
                GameObject prefab; float scale; float topLocal; float emerge;
                if (roll < 20 && logPrefab != null)
                {
                    // dead_tree_trunk_02's identity pose stands vertical -- lay it on its side
                    // (same LookRotation+90deg-yaw pattern as the river-crossing log in
                    // BuildFootholds and the shore log in BuildLakeShoreDressing) instead of the
                    // generic Y-only rotation the shared tail block below uses for roots/boulder/
                    // moss, which was planting this one upright like a stump and burying ~70%+ of
                    // its trunk radius.
                    float logScale = 1.1f + (float)rng.NextDouble() * 1.0f;
                    float fallAng = (float)rng.NextDouble() * 360f;
                    Vector3 logDir = new Vector3(Mathf.Sin(fallAng * Mathf.Deg2Rad), 0f, Mathf.Cos(fallAng * Mathf.Deg2Rad));
                    var linst = (GameObject)PrefabUtility.InstantiatePrefab(logPrefab, clutterRoot.transform);
                    linst.name = "FloorClutter_" + placed;
                    linst.transform.localScale = Vector3.one * logScale;
                    linst.transform.rotation = Quaternion.LookRotation(logDir) * Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler((float)rng.NextDouble() * 10f - 5f, 0f, 0f);
                    float logTopY = groundY + 0.42f * logScale; // same exposure ratio as the already-correct river-crossing log
                    linst.transform.position = new Vector3(jx, logTopY - logTopLocal * logScale, jz);
                    placed++;
                    continue;
                }
                else if (roll < 42 && roots != null)
                {
                    prefab = roots; topLocal = rootsTopLocal;
                    scale = 0.9f + (float)rng.NextDouble() * 1.0f; emerge = 0.12f;
                }
                else if (roll < 57 && boulder != null)
                {
                    prefab = boulder; topLocal = boulderTopLocal;
                    scale = 0.4f + (float)rng.NextDouble() * 0.6f; emerge = 0.1f * scale;
                }
                else if (roll < 77)
                {
                    prefab = mossSets[rng.Next(mossSets.Length)]; topLocal = GetPrefabTopLocalY(prefab);
                    scale = 0.3f + (float)rng.NextDouble() * 0.45f; emerge = 0.08f * scale;
                }
                else if (roll < 90 && (shrub1 != null || shrub2 != null))
                {
                    bool useShrub2 = shrub2 != null && (shrub1 == null || rng.Next(2) == 0);
                    var shrubPrefab = useShrub2 ? shrub2 : shrub1;
                    float bottomY = useShrub2 ? shrub2BottomY : shrub1BottomY;
                    // shrub_01's individual bushes are quite small at the source scale (~0.1-0.4m),
                    // which read as sparse weeds rather than a proper mid-height shrub layer at the
                    // original 0.45-0.8 range -- boosted so it reads as genuine low undergrowth.
                    float shrubScale = useShrub2 ? (0.25f + (float)rng.NextDouble() * 0.2f) : (1.0f + (float)rng.NextDouble() * 0.7f);
                    var sinst = (GameObject)PrefabUtility.InstantiatePrefab(shrubPrefab, clutterRoot.transform);
                    sinst.name = "FloorClutter_" + placed;
                    sinst.transform.position = new Vector3(jx, groundY - bottomY * shrubScale, jz);
                    sinst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    sinst.transform.localScale = Vector3.one * shrubScale;
                    placed++;
                    continue;
                }
                else if (branches != null || barkDebris != null)
                {
                    // Flat ground-hugging branch/bark litter -- no measured topLocal needed, sits
                    // directly on the surface like BuildGroundDetail's riverbank litter does.
                    // bark_debris_01 (newly added) is a small pile of several photoreal bark/twig
                    // pieces together -- alternated in for variety against the single dry branch.
                    bool useBark = barkDebris != null && (branches == null || rng.Next(2) == 0);
                    var bDebris = useBark ? barkDebris : branches;
                    var binst = (GameObject)PrefabUtility.InstantiatePrefab(bDebris, clutterRoot.transform);
                    binst.name = "FloorClutter_" + placed;
                    binst.transform.position = new Vector3(jx, groundY, jz);
                    binst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    binst.transform.localScale = Vector3.one * (useBark ? (0.8f + (float)rng.NextDouble() * 0.9f) : (0.6f + (float)rng.NextDouble() * 0.7f));
                    placed++;
                    continue;
                }
                else continue;

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, clutterRoot.transform);
                inst.name = "FloorClutter_" + placed;
                float topY = groundY + emerge;
                inst.transform.position = new Vector3(jx, topY - topLocal * scale, jz);
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                inst.transform.localScale = Vector3.one * scale;
                placed++;
            }
        }

        log.AppendLine("Forest floor clutter instances: " + placed);
    }

    // ---------------------------------------------------------------- helpers

    static Material GetOrCreateMat(string name, Texture2D tex, Vector2 tiling)
    {
        string path = "Assets/Stage/Greybox/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        if (tex != null)
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.color = Color.white;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void SetTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    // Solid box collider sitting under the given local top height, independent of the
    // visual mesh shape -- gives predictable CharacterController footing on lumpy rocks.
    // BUGFIX 2026-08-16 (found while auditing stray colliders): every call site passes
    // topLocalHeight already pre-multiplied by the object's own scale (e.g. "topLocal * scale",
    // meant as a WORLD-space offset above the pivot, matching how each caller also positions the
    // instance's transform.position). But BoxCollider.center is a LOCAL-space value that Unity
    // multiplies by the transform's lossyScale AGAIN when computing world bounds -- so passing an
    // already-scaled value here silently squared the scale factor (scale=1 hides this completely,
    // which is why it went unnoticed for most props; at GiantBoulder's scale~2.2-3.2 it floated the
    // collider 3.5-6m above the actual mesh top -- see GiantBoulder_7/13 in Footholds). Dividing by
    // the transform's own lossyScale.y here un-does the caller's premultiplication before it's
    // stored as a local coordinate, so the world result matches the intended (single) scale.
    static void AddSolidCollider(GameObject target, float topLocalHeight)
    {
        var renderers = target.GetComponentsInChildren<Renderer>();
        Vector3 footprint = new Vector3(1.5f, 1f, 1.5f);
        var t = target.transform;
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            footprint = new Vector3(b.size.x / Mathf.Max(t.lossyScale.x, 0.0001f), 0.8f, b.size.z / Mathf.Max(t.lossyScale.z, 0.0001f));
        }
        var box = target.AddComponent<BoxCollider>();
        float scaleY = Mathf.Max(Mathf.Abs(t.lossyScale.y), 0.0001f);
        box.center = new Vector3(0f, (topLocalHeight - 0.4f) / scaleY, 0f);
        box.size = new Vector3(footprint.x, 0.8f, footprint.z);
    }

    static float AddFittedBoxCollider(GameObject target, GameObject visual)
    {
        var renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;
        var worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);

        var t = target.transform;
        Vector3 localCenter = t.InverseTransformPoint(worldBounds.center);
        Vector3 localSize = new Vector3(
            worldBounds.size.x / Mathf.Max(t.lossyScale.x, 0.0001f),
            worldBounds.size.y / Mathf.Max(t.lossyScale.y, 0.0001f),
            worldBounds.size.z / Mathf.Max(t.lossyScale.z, 0.0001f));

        var box = target.AddComponent<BoxCollider>();
        box.center = localCenter;
        box.size = localSize;
        return localCenter.y + localSize.y * 0.5f;
    }
}
