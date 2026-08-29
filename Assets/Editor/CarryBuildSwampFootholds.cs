using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Stage 2 (毒沼) footholds: a rickety single-file wooden-plank/log bridge strung along the swamp's
// own meander (SwampX/WaterYAt in CarryBuildTerrainForest.cs). 2026-08-29 SECOND GENERATION: the
// original Kenney bridge_center_wood placeholder planks are replaced with the user's own Meshy AI
// "MosswoodCrossing" set (see ASSET_LICENSES.md #10), split into 3 pieces in Blender
// (Assets/Stage/Swamp/MosswoodCrossing/Models/Separated/):
//   - SwampFoothold_PlankDeck: wide multi-log deck segment -- the MAIN, stable foothold.
//   - SwampFoothold_Log: a single rough log -- stable, mixed in for variety.
//   - SwampFoothold_LogBundle: a short bundled-log segment -- ALWAYS the collapsing kind (user:
//     "SwampFoothold_LogBundle.fbxのみは短時間で渡り切らないと地面が落ちるギミック"), tuned with a
//     short stand time so the player must cross it quickly, not linger.
// Prototype scope (see plan): straight-line placement along the swamp centerline, not the full
// beam-search piece-chaining CarryBuildMossyRockPathCourse.cs uses for Stage 1.
public static class CarryBuildSwampFootholds
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    const string PlankDeckPath = "Assets/Stage/Swamp/MosswoodCrossing/Models/Separated/SwampFoothold_PlankDeck.fbx";
    const string LogPath = "Assets/Stage/Swamp/MosswoodCrossing/Models/Separated/SwampFoothold_Log.fbx";
    const string LogBundlePath = "Assets/Stage/Swamp/MosswoodCrossing/Models/Separated/SwampFoothold_LogBundle.fbx";

    // Same connector anchor validated in the previous pass (2026-08-29 THIRD PASS): the stone
    // course's true walkable end (MossyRockPath_LongCurve_Mirrored_6/WalkableCollision/Coll_39,
    // z=99.50..100.14) plus a small clear buffer so the wood course doesn't ride on top of it.
    const float ConnectorStartZ = 100.6f;
    static readonly Vector3 CourseExitAnchor = new Vector3(3.3f, 2.3f, ConnectorStartZ);

    const float StartZ = 134f;  // SwampZ0(108) + SwampRampLen(26) -- swamp channel fully open; collapsing (LogBundle) pieces only appear from here, easing the player onto the new mechanic on solid footing first
    const float EndZ = 215f;    // prototype run length, unchanged from the previous pass

    const float GapMin = 0f, GapMax = 0f; // 2026-08-29 (user: "コースに隙間はいらないのでアセット同士が接着するようにして"): pieces now sit edge-to-edge, no gap
    const float ContactOverlap = 0.4f; // 2026-08-29: small guaranteed overlap so the added yaw jitter below (which shortens a piece's projected Z-reach by cos(yaw)) can never open a gap at the seam
    const float ClearanceAboveWater = 0.55f;

    // 2026-08-29 (user: "足場が小さすぎる", then "まだ小さい、3倍くらい大きくしたい" after a first
    // pass at 2x): the raw Meshy meshes are real-world log/plank scale (widths 0.37-0.54m),
    // noticeably narrower than the goblin's own CharacterController diameter (radius 0.35 -> 0.7m
    // across). Uniform scale (not a non-uniform stretch, which would distort the log shapes) keeps
    // proportions natural while giving each piece a comfortable stand-on footprint.
    const float FootholdScale = 9f; // 2026-08-29: 3x, then user asked for a further 3x on top of that ("今の状態からさらに3倍")

    // Weighted mix: PlankDeck is the main foothold, Log and LogBundle are mixed in ("ランダムに...織り交ぜる").
    const double WeightPlankDeck = 0.55, WeightLog = 0.25, WeightLogBundle = 0.20;

    const float CollapseStandTime = 2.0f;   // LogBundle: short -- must cross quickly ("短時間で渡り切らないと"); 2026-08-29: +1s per user request (was 1.0f)
    const float CollapseWarningLead = 0.4f;

    // 2026-08-29 (user: "落ちるギミックの地面が一個も配置されていないので...3か所ほど差し込んで"): the
    // weighted-random draw had landed on 0 LogBundle pieces by chance with the current seed/course
    // length. Force roughly this many collapsing insertions, spread across the eligible (z>=StartZ)
    // slots, instead of leaving the count purely up to chance.
    const int TargetCollapsingCount = 3;

    enum FootType { PlankDeck, Log, LogBundle }

    class ManualOverride
    {
        public Vector3 pos;
        public Quaternion rot;
        public FootType expectedType; // safety check -- skip (and warn) if the piece at this placement index isn't what was recorded, e.g. the route/RNG changed
    }

    // 2026-08-29 (user: "SwampPlankDeck_2を手動移動した。この移動をスクリプトとして記録できる？"):
    // same ManualLayoutOverrides pattern as CarryBuildMossyRockPathCourse.cs -- the procedural
    // formula above still computes a reasonable starting position/rotation, this table then
    // corrects specific indices to an exact hand-placed transform captured live from the scene, so
    // re-running Run() reproduces the manual adjustment instead of losing it.
    static readonly System.Collections.Generic.Dictionary<int, ManualOverride> ManualLayoutOverrides =
        new System.Collections.Generic.Dictionary<int, ManualOverride>
        {
            { 2, new ManualOverride {
                pos = new Vector3(-0.38f, -0.94f, 125.87f),
                rot = new Quaternion(0.00259f, -0.72366f, 0.00066f, 0.69015f),
                expectedType = FootType.PlankDeck,
            } },
        };

    [MenuItem("Carry/Build Swamp Footholds (Stage 2)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainRoot = GameObject.Find("ForestStage_Terrain");

            var old = terrainRoot.transform.Find("SwampFootholds");
            if (old != null) Object.DestroyImmediate(old.gameObject); // removes all SwampPlank_Collapsing_0..60 / SwampPlank_N from the previous (Kenney placeholder) pass
            var footRoot = new GameObject("SwampFootholds");
            footRoot.transform.SetParent(terrainRoot.transform, false);

            var prefabs = new System.Collections.Generic.Dictionary<FootType, GameObject>
            {
                { FootType.PlankDeck, AssetDatabase.LoadAssetAtPath<GameObject>(PlankDeckPath) },
                { FootType.Log, AssetDatabase.LoadAssetAtPath<GameObject>(LogPath) },
                { FootType.LogBundle, AssetDatabase.LoadAssetAtPath<GameObject>(LogBundlePath) },
            };
            foreach (var kv in prefabs)
                if (kv.Value == null) { log.AppendLine("FAILED: prefab not found for " + kv.Key); Debug.Log(log); return; }

            // Half-length (along local +X, the piece's travel axis) and full local bounds, read
            // straight from each source mesh so collider sizing stays correct if the assets change.
            var halfLen = new System.Collections.Generic.Dictionary<FootType, float>();
            var meshBounds = new System.Collections.Generic.Dictionary<FootType, Bounds>();
            var meshes = new System.Collections.Generic.Dictionary<FootType, Mesh>();
            foreach (var kv in prefabs)
            {
                var mf = kv.Value.GetComponentInChildren<MeshFilter>();
                meshBounds[kv.Key] = mf.sharedMesh.bounds; // unscaled -- stays in this space so Unity's own transform.localScale multiplies it automatically
                meshes[kv.Key] = mf.sharedMesh; // MeshFilter sits directly on the prefab root with identity local transform (verified live), so this mesh can be assigned straight onto the instance's own MeshCollider below
                halfLen[kv.Key] = mf.sharedMesh.bounds.extents.x * FootholdScale; // scaled -- used for world-space spacing along Z
            }

            var t = typeof(CarryBuildTerrainForest);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            float SwampX(float z) => (float)t.GetMethod("SwampX", flags).Invoke(null, new object[] { z });
            float WaterYAt(float z) => (float)t.GetMethod("WaterYAt", flags).Invoke(null, new object[] { z });

            var rng = new System.Random(20260829);
            int placed = 0, collapsing = 0;
            float zCursor = ConnectorStartZ;
            int eligibleSeen = 0, forcedCollapsing = 0;
            float tutorialX = 0f, tutorialY = 0f, tutorialZ = 0f;
            Quaternion tutorialRot = Quaternion.identity;

            // 2026-08-29 (user: "SwampPlankDeck_2とSwampLogBundle_Collapsing_1はXとYは同じ軸に配置
            // し、Z軸だけSwampLogBundle_Collapsing_1のほうが高い位置になるように配置したい...上空から
            // 見ると重なって見える状態"): the previous pass's safe-landing teleport was rejected
            // outright ("安全着地機能はいらない") in favor of this -- piece 2 sits DIRECTLY beneath
            // piece 1 (same X/Z footprint, lower Y only), not the next step forward in the path. It
            // must NOT consume any path length, so the main zCursor chain skips over it entirely (see
            // the loop bottom below).
            const float StackedNetDrop = 1.0f; // 2026-08-29: 1.5 put the net's own bottom right at the swamp trigger's ceiling here (margin ~0.0005m, caught live) -- reduced for a safe buffer

            while (zCursor <= EndZ)
            {
                bool isStackedNet = placed == 2; // the safety net directly under piece 1 (placed==1) -- not a forward step

                // 2026-08-29 (user: "SwampPlankDeck_0とSwampPlankDeck_1の段差の間に落ちる足場を一つ
                // 差し込んで"): there's a real ~2.2m height drop between the first two pieces (piece
                // 0 sits at y~1.6, piece 1 at y~-0.65 -- WaterYAt drops fast right past the connector
                // zone) -- overrides the normal StartZ=134 gate (which otherwise keeps collapsing
                // pieces out of the early, "ease the player in" stretch) specifically for this one
                // slot, right where that drop happens.
                FootType type = placed == 1 ? FootType.LogBundle
                    : isStackedNet ? FootType.PlankDeck
                    : PickType(rng, zCursor, ref eligibleSeen, ref forcedCollapsing);
                float half = halfLen[type];
                float baseCenterZ = zCursor + half;
                // 2026-08-29 (user: "接触するくらい近くして一切隙間はない状態にする"): no Z jitter --
                // zz sits exactly at the chained contact point so GapMin/Max=0 actually means
                // touching, not "touching plus or minus a few cm of random wobble".
                float zz = isStackedNet ? tutorialZ : baseCenterZ;

                float px, y;
                Quaternion rot;
                if (isStackedNet)
                {
                    px = tutorialX;
                    y = tutorialY - StackedNetDrop;
                    rot = tutorialRot;
                }
                else
                {
                    float cx = SwampX(zz);
                    // 2026-08-29 (user: "アセットはまっすぐの一本道ではなくし少し斜めの状態も交えて自然
                    // なコースにして"): widened well past the old +-0.9 (tuned for the original small,
                    // narrow pieces) now that FootholdScale has grown each piece several meters across
                    // -- the swamp channel itself is ~19-21m half-width through this whole stretch
                    // (checked live), so even the top of this range stays comfortably inside it.
                    float jitterX = ((float)rng.NextDouble() - 0.5f) * 5f;
                    px = cx + jitterX;
                    float waterY = WaterYAt(zz);
                    y = waterY + ClearanceAboveWater;

                    // Connector stretch (ConnectorStartZ..StartZ): blend X/Y from the stone course's
                    // own exit point down to the swamp's natural values (same smoothstep approach
                    // validated in the previous pass).
                    float connectorT = Mathf.Clamp01(Mathf.InverseLerp(ConnectorStartZ, StartZ, zz));
                    float connectorEase = connectorT * connectorT * (3f - 2f * connectorT);
                    px = Mathf.Lerp(CourseExitAnchor.x, px, connectorEase);
                    y = Mathf.Lerp(CourseExitAnchor.y, y, connectorEase);

                    float dz = 0.5f;
                    float headingDx = SwampX(zz + dz) - SwampX(zz - dz);
                    Vector3 dir = new Vector3(headingDx, 0f, dz * 2f).normalized;
                    // Base look direction puts local +Z along dir; the extra -90 deg yaw remaps the
                    // asset's own long axis (local +X, verified empirically) onto dir instead.
                    rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
                    // 2026-08-29 (user: "少し斜めの状態も交えて自然なコースにして"): extra yaw so
                    // pieces aren't all rigidly parallel to the swamp's own tangent -- kept modest (not
                    // the full +-30 deg a truly chaotic pile would want) because yawing a piece
                    // shortens its projected reach along Z (by cos(yaw)), which eats into the zero-gap
                    // contact from below; ContactOverlap's small guaranteed overlap absorbs that here.
                    rot *= Quaternion.Euler(0f, ((float)rng.NextDouble() - 0.5f) * 20f, 0f);
                    // Degrees divided by FootholdScale so the ABSOLUTE vertical dip this wobble causes
                    // at the piece's far end (half-length * sin(angle), and half-length grows with
                    // scale) stays roughly constant instead of growing with it -- at FootholdScale=9 an
                    // unscaled 3 deg pitch on an 8.5m half-length piece dips its end ~0.45m, enough to
                    // punch through the swamp trigger's clearance margin below (caught live, see
                    // below).
                    rot *= Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 6f / FootholdScale, 0f, ((float)rng.NextDouble() - 0.5f) * 5f / FootholdScale);
                }

                if (placed == 1) { tutorialX = px; tutorialY = y; tutorialZ = zz; tutorialRot = rot; }

                var pos = new Vector3(px, y, zz);

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[type], footRoot.transform);
                bool isCollapsing = type == FootType.LogBundle;
                inst.name = (isCollapsing ? "SwampLogBundle_Collapsing_" : "Swamp" + type + "_") + placed;
                inst.transform.SetPositionAndRotation(pos, rot);
                inst.transform.localScale = Vector3.one * FootholdScale;

                if (ManualLayoutOverrides.TryGetValue(placed, out var manualOverride))
                {
                    if (manualOverride.expectedType == type)
                    {
                        inst.transform.SetPositionAndRotation(manualOverride.pos, manualOverride.rot);
                    }
                    else
                    {
                        log.AppendLine($"WARNING: manual override for index {placed} skipped (expected {manualOverride.expectedType}, got {type} -- route/RNG changed).");
                    }
                }

                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
                {
                    var mat = new Material(mr.sharedMaterial);
                    Color baseCol = isCollapsing
                        ? new Color(0.22f, 0.19f, 0.13f) // rotten/darker -- a subtle risk tell, prototype scope (can be made more obvious later)
                        : new Color(0.30f, 0.24f, 0.16f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseCol);
                    mr.sharedMaterial = mat;
                }

                var b = meshBounds[type];
                // 2026-08-29 (user: "アセットに対して物理判定が大きく足場に乗っているときゴブリンが
                // 浮いてしまっている"): these are organic, irregular log/plank meshes, not flat
                // boxes -- a BoxCollider sized to the mesh's AABB sits at the mesh's tallest point
                // (a knot, a raised branch stub, etc.) across its ENTIRE footprint, so most of the
                // visible surface is actually well below the collider's flat top and the goblin
                // stood floating above it. A MeshCollider hugs the real geometry instead. Static,
                // non-convex is fine here -- these pieces never move while solid (CollapsingFoothold
                // disables this collider before the piece starts falling), so there's no dynamic-vs-
                // concave restriction to worry about.
                var meshCol = inst.AddComponent<MeshCollider>();
                meshCol.sharedMesh = meshes[type];

                if (isCollapsing)
                {
                    var triggerGo = new GameObject("StandTrigger");
                    triggerGo.transform.SetParent(inst.transform, false);
                    triggerGo.transform.localPosition = new Vector3(b.center.x, b.center.y, b.center.z);
                    // No non-uniform localScale is applied to these instances (unlike the old Kenney
                    // planks), so the BoxCollider-under-rotation shearing issue from the previous pass
                    // doesn't apply here. Height is still generously tall (2.2m, same fix validated
                    // last pass) so the CharacterController capsule's overlap is never marginal.
                    var triggerBox = triggerGo.AddComponent<BoxCollider>();
                    triggerBox.isTrigger = true;
                    // Sizes here are in the (unscaled) parent-local space like the solid collider
                    // above, but padding/height are meant as fixed WORLD-space amounts regardless of
                    // FootholdScale, so they're pre-divided by it here (world = local * FootholdScale).
                    triggerBox.size = new Vector3(b.size.x + 0.1f / FootholdScale, 2.2f / FootholdScale, b.size.z + 0.1f / FootholdScale);
                    triggerGo.AddComponent<StandTriggerRelay>();
                    var foothold = inst.AddComponent<CollapsingFoothold>();
                    foothold.standTimeBeforeCollapse = CollapseStandTime;
                    foothold.warningLeadTime = CollapseWarningLead;
                    collapsing++;
                }

                placed++;
                // 2026-08-29: the stacked safety net (isStackedNet) sits directly under piece 1 and
                // consumes no path length of its own, so it must NOT advance zCursor -- the next
                // piece (the real forward step) continues from piece 1's own forward edge instead.
                if (!isStackedNet)
                {
                    float gap = Mathf.Lerp(GapMin, GapMax, (float)rng.NextDouble());
                    zCursor = baseCenterZ + half + gap - ContactOverlap;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"Swamp footholds placed: {placed} ({collapsing} collapsing LogBundle, {placed - collapsing} stable), Z={ConnectorStartZ}..{EndZ} (collapsing eligible from Z={StartZ}). SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    static FootType PickType(System.Random rng, float z, ref int eligibleSeen, ref int forcedCollapsing)
    {
        bool allowCollapsing = z >= StartZ;
        if (allowCollapsing)
        {
            eligibleSeen++;
            // Force every other eligible slot to LogBundle until TargetCollapsingCount is reached,
            // spreading the guaranteed insertions across the eligible stretch rather than clustering
            // them at the start; remaining slots stay a weighted PlankDeck/Log draw (with a normal
            // chance of landing on LogBundle too, on top of the forced ones).
            if (forcedCollapsing < TargetCollapsingCount && eligibleSeen % 2 == 1)
            {
                forcedCollapsing++;
                return FootType.LogBundle;
            }
            double r = rng.NextDouble();
            if (r < WeightPlankDeck) return FootType.PlankDeck;
            if (r < WeightPlankDeck + WeightLog) return FootType.Log;
            return FootType.LogBundle;
        }
        // Before the swamp channel is fully open, redistribute LogBundle's share between the two
        // stable types proportionally instead of allowing an early collapsing piece.
        double stableTotal = WeightPlankDeck + WeightLog;
        return rng.NextDouble() < WeightPlankDeck / stableTotal ? FootType.PlankDeck : FootType.Log;
    }
}
