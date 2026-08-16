using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-14: wires up the SPH/PBF fluid solver (ported from the Player branch's potion-in-pot
// system -- see Assets/Scripts/Fluid/*, Assets/Shaders/Fluid/*) onto the waterfall, so it flows as
// the same liquid the goblin carries in its pot. Player branch itself was never touched; every file
// under Assets/Scripts/Fluid and Assets/Shaders/Fluid here is either a verbatim copy of a
// Player-branch file (FluidBoundary, PotInteriorProfile, IPotionVolumeSource, FluidSurface,
// FluidSurface.compute, PotionLiquidSurface.shader -- none of these reference the pot at all, they
// only know about a generic "container" box or a generic particle buffer) or FluidCore.cs /
// FluidCore.compute with two added features (see their own comments), neither present in the
// Player-branch pot system:
//  * "Waterfall Recycle" -- respawns Retired particles at a spawn box instead of parking them out
//    of the world, so a small fixed particle budget can represent a stream that looks like it flows
//    forever. Each particle slot's GroundLifetime is jittered +/-40% so slots don't all retire and
//    respawn in lockstep (that read as a synchronized pulsing "drip" instead of a continuous flow).
//  * "Terrain-hugging slope collision" -- SlopeProfileBuf/SlopeHeightAt() replace the flat GroundY
//    plane with the lake cliff's actual measured height profile along the fall column, so the
//    liquid bounces/slides down the real terrain shape instead of free-falling through open air to
//    a single flat pool plane.
//
// 2026-08-14 revision: Waterfall_0 (the old simple translucent mesh sheet) is deleted -- this SPH
// liquid is now the only visual for the falls. The source was also moved from partway down the
// slope to a small hollow scooped into the terrain further back/up the hill (see
// CarryFixLakeLandmarks2_CarveRecess below), so the potion visibly emerges from a recess in the
// rock rather than starting mid-air, then cascades down hitting the slope on the way to the pool.
public static class CarrySetupWaterfallFluid
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    // Recess ("窪み") carved into the terrain near the top of the slope, where the flow originates.
    // Coordinates match CarveRecess() below.
    const float RecessX = -3.3f, RecessZ = -42.5f;

    // Slope profile sample range along the fall column (x fixed at RecessX, z scanned from the
    // pool front to behind the recess). Matches the values baked into the scene right now.
    // 2026-08-16: SlopeZStart extended from -33 to -28 to cover the fluid boundary's widened
    // near/pool-side edge (see Run()'s boundary.boxInnerSize comment) -- otherwise the last few
    // meters of the extended box would have no real terrain profile to collide against.
    const float SlopeZStart = -28f, SlopeZEnd = -45f;
    const int SlopeSamples = 30;

    [MenuItem("Carry/Setup Waterfall Fluid (SPH, ported from Player branch)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var waterfallsParent = GameObject.Find("ForestStage_Terrain/Waterfalls");
            if (waterfallsParent == null) { Debug.LogError("Waterfalls parent not found."); return; }

            var oldMesh = waterfallsParent.transform.Find("Waterfall_0");
            if (oldMesh != null) { Object.DestroyImmediate(oldMesh.gameObject); log.AppendLine("Deleted old Waterfall_0 mesh."); }
            var stopgap = waterfallsParent.transform.Find("PotionFlow_0");
            if (stopgap != null) { Object.DestroyImmediate(stopgap.gameObject); log.AppendLine("Deleted obsolete PotionFlow_0 stopgap ParticleSystem."); }

            var terrainGO = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGO.GetComponent<Terrain>();
            CarveRecess(terrain, log);

            var existing = waterfallsParent.transform.Find("PotionWaterfallFluid");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            // Build inactive first so AddComponent below doesn't fire OnEnable (which reads the
            // boundary/core settings) before every field is actually configured.
            var go = new GameObject("PotionWaterfallFluid");
            go.SetActive(false);
            go.transform.SetParent(waterfallsParent.transform, true);
            // Center of a tall box spanning the WHOLE slope, from the pool up past the recess
            // (not just the old narrow vertical sheet's footprint).
            // 2026-08-16, per "湖に到達する前に滝が消えてしまっているので湖に到達するように修正": the box's
            // near/pool-side edge used to sit at z=-32, but the real lake water starts at z~-34 (measured
            // live via terrain.SampleHeight) -- close, but Play-mode particle-buffer readback (see
            // conversation) showed the stream's particle density thinning out badly right at that edge
            // (114-152 particles vs 200+ higher up the slope), so the surface reconstruction had too
            // little to work with there and the visible mesh petered out before ever reaching the lake.
            // Shifted the box center +2m in Z and widened it +4m so the near edge now reaches z=-28,
            // comfortably past the lake's actual shoreline, giving the stream room to spread out and
            // stay dense enough to render all the way to open water.
            go.transform.position = new Vector3(-3.3f, 6.5f, -36.5f);

            var boundary = go.AddComponent<FluidBoundary>();
            boundary.mode = FluidBoundary.Mode.Box;
            // BuildBox (verbatim from Player branch) only builds a floor + 4 side walls, never a
            // ceiling -- the top stays an open boundary. The floor sits at the box's bottom, which
            // lines up with the pool (groundY below), so it's redundant with-but-harmless alongside
            // the slope collision (see FluidCore.compute's Finalize kernel).
            boundary.boxInnerSize = new Vector3(6.5f, 24f, 17f);

            var core = go.AddComponent<FluidCore>();
            // 2026-08-16, per "滝の水量を減らしてジャバジャバと水の勢いがわかるように流れている状態にして":
            // fillFraction=0.5 on this box (6.5x24x13 = ~2028m3) was pre-seeding a HUGE standing block
            // of water (the pot system's own "container starts half-full" assumption, never re-tuned for
            // a tall/narrow waterfall shape) -- that's what was rendering as one giant solid blue block
            // instead of a falling stream (confirmed live in Play mode). Dropped it to near-zero so the
            // falling spawn stream is what actually shapes the visible water, not a pre-filled pool.
            // particleCount also reduced (less total water, per the request) and spawnVelocity/
            // boundsRestitution raised so the flow reads as a fast, splashy pour instead of a slow drip.
            core.particleCount = 2400;
            core.fillFraction = 0.03f;
            core.groundY = -4.3f; // matches WaterfallSplash_0's pool surface height
            // Short on purpose: the pot's 45s "puddle sits there" value would look like the pool
            // endlessly filling up and never draining, since nothing else is removing mass here.
            core.groundLifetime = 0.5f;
            // Spawn point: the recess mouth, not partway down the slope -- narrow box, pushed
            // down-and-forward (toward the pool, i.e. +Z) so it visibly pours out of the hollow
            // instead of just appearing mid-air.
            core.spawnBoxMin = new Vector3(-4.0f, 14.8f, -42.9f);
            core.spawnBoxSize = new Vector3(1.6f, 0.3f, 0.5f);
            core.spawnVelocity = new Vector3(0f, -2.6f, 4.2f); // faster pour, more visible "gushing" motion

            // Slope height profile (see FluidCore.compute Finalize -- SlopeHeightAt) sampled fresh
            // from the live Terrain along the fall column, so it always matches the actual mesh
            // (including the recess carved just above) rather than a hand-typed guess.
            var heights = new float[SlopeSamples];
            for (int i = 0; i < SlopeSamples; i++)
            {
                float t = i / (float)(SlopeSamples - 1);
                float z = Mathf.Lerp(SlopeZStart, SlopeZEnd, t);
                heights[i] = terrain.SampleHeight(new Vector3(RecessX, 0f, z)) + terrainGO.transform.position.y;
            }
            core.slopeProfileHeights = heights;
            core.slopeZStart = SlopeZStart;
            core.slopeZEnd = SlopeZEnd;

            // Play-mode tuning (2026-08-14): the pot's defaults (boundaryViscosity 0.55,
            // viscosity 2.8, boundsFriction 0.15) are tuned for a WIDE vessel / a single flat floor.
            // On this ~13m open slope, that much friction/no-slip drag killed the fluid's downhill
            // momentum on every slope collision (which happens almost every sub-step on a long open
            // ramp) and it stalled as a static plug a few meters down -- verified by stepping the
            // sim directly (FluidCore.Step) and reading the Positions buffer. Loosened viscosity AND
            // bounds friction/restitution so the liquid keeps sliding instead of sticking on contact.
            core.boundaryViscosity = 0.05f;
            core.viscosity = 1.0f;
            core.boundsFriction = 0.03f;
            // Restitution raised from 0.06 -- per "ジャバジャバと水の勢いがわかるように", impacts need to
            // visibly splash/bounce off the slope and pool surface rather than just absorbing on contact.
            core.boundsRestitution = 0.22f;
            core.fluidCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Fluid/FluidCore.compute");
            if (core.fluidCompute == null) log.AppendLine("WARNING: FluidCore.compute not found at expected path.");

            var surface = go.AddComponent<FluidSurface>();
            surface.surfaceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Fluid/FluidSurface.compute");
            if (surface.surfaceCompute == null) log.AppendLine("WARNING: FluidSurface.compute not found at expected path.");
            surface.liquidShader = Shader.Find("Custom/PotionLiquidSurface");
            if (surface.liquidShader == null) log.AppendLine("WARNING: Custom/PotionLiquidSurface shader not found.");
            // Leave voxel/maxTriangles at their (pot-tuned) defaults for now -- fine starting point
            // for a background element; can be turned down further if profiling calls for it.

            go.SetActive(true);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("PotionWaterfallFluid rebuilt at " + go.transform.position);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    // Scoops a shallow hollow into the terrain near the top of the slope (close to
    // AncientForestGuardian's hill) so the potion has a visible "source" to emerge from instead of
    // just starting mid-air on an otherwise unbroken slope. Same TerrainData.GetHeights/SetHeights
    // local-patch technique as CarryFixLakeLandmarks.cs's mound sculpt, but carving DOWN instead of
    // building UP, and never carving above the pre-existing surface (only ever digs in).
    static void CarveRecess(Terrain terrain, System.Text.StringBuilder log)
    {
        var data = terrain.terrainData;
        var terrainGO = terrain.gameObject;
        float originX = terrainGO.transform.position.x, originZ = terrainGO.transform.position.z, originY = terrainGO.transform.position.y;
        float sizeX = data.size.x, sizeZ = data.size.z, sizeY = data.size.y;
        int hr = data.heightmapResolution;

        const float coreR = 1.8f, outerR = 4.0f, carveDepth = 1.6f;
        float centerY = terrain.SampleHeight(new Vector3(RecessX, 0f, RecessZ)) + originY;
        float targetDeepY = centerY - carveDepth;

        var heights = data.GetHeights(0, 0, hr, hr);
        int minXi = Mathf.Max(0, Mathf.FloorToInt(((RecessX - outerR) - originX) / sizeX * (hr - 1)));
        int maxXi = Mathf.Min(hr - 1, Mathf.CeilToInt(((RecessX + outerR) - originX) / sizeX * (hr - 1)));
        int minZi = Mathf.Max(0, Mathf.FloorToInt(((RecessZ - outerR) - originZ) / sizeZ * (hr - 1)));
        int maxZi = Mathf.Min(hr - 1, Mathf.CeilToInt(((RecessZ + outerR) - originZ) / sizeZ * (hr - 1)));

        for (int zi = minZi; zi <= maxZi; zi++)
        {
            float worldZ = originZ + (zi / (float)(hr - 1)) * sizeZ;
            for (int xi = minXi; xi <= maxXi; xi++)
            {
                float worldX = originX + (xi / (float)(hr - 1)) * sizeX;
                float d = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(RecessX, RecessZ));
                if (d > outerR) continue;
                float t = Mathf.Clamp01(d / outerR);
                float bowlShape = 0.5f * (1f + Mathf.Cos(t * Mathf.PI));
                float noise = (Mathf.PerlinNoise(worldX * 0.6f + 900f, worldZ * 0.6f - 900f) - 0.5f) * 0.35f;
                float carveHeight = targetDeepY + noise * bowlShape;

                float originalWorldY = originY + heights[zi, xi] * sizeY;
                float newWorldY = Mathf.Min(Mathf.Lerp(originalWorldY, carveHeight, bowlShape), originalWorldY);
                heights[zi, xi] = Mathf.Clamp01((newWorldY - originY) / sizeY);
            }
        }
        data.SetHeights(0, 0, heights);
        Physics.SyncTransforms();
        log.AppendLine("Carved recess at (" + RecessX + "," + RecessZ + "), surroundingY=" + centerY.ToString("F2") + " deepY=" + targetDeepY.ToString("F2"));
    }
}
