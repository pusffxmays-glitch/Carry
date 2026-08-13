using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Replaces every remaining Primitive "foothold" slab in ForestStage_Greybox
// (Start/SafePath1/FlatPath2/BridgeApproach/RestArea/Landing/FinalApproach/
// GateFloor/Step1/Step2, plus the hidden collision block under the bridge)
// with a hand-assembled mosaic of real Kenney ground/rock modules: the walking
// surface itself is now built entirely out of combined assets (dirt trail
// tiles, grass patches, low rock shelves) instead of a textured Cube. Only
// SteppingStones, RecoveryPoints, the river and checkpoints -- already built
// from real assets with their own colliders -- are left alone.
//
// Route height (baseY) still only changes at Step1/Step2 (0 -> 0.35 -> 0.70),
// same as CarryBuildForestGreybox, so jump gaps / bridge width / step climbs
// tuned earlier are unchanged; only how the ground is built and collides is
// different (per-tile colliders instead of one big invisible box).
public static class CarryBuildRockCourse
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Greybox.unity";
    const string Kenney = "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/";

    // name -> (prefab path, height above its own pivot's "ground" reference).
    // Every Kenney piece we use has its pivot at the BOTTOM of the mesh except
    // ground_pathTile/ground_grass, whose pivot sits ~at the top (near-zero
    // thickness); treating all of them the same way (position.y = topY - height)
    // just sinks the two thin ones a few cm into the dirt, which reads fine.
    struct Tile { public string prefab; public float height; }
    static Tile T(string file, float h) => new Tile { prefab = Kenney + file, height = h };

    static readonly Tile[] CenterTiles = { T("ground_pathTile.fbx", 0.05f), T("ground_pathTile.fbx", 0.05f), T("ground_pathTile.fbx", 0.05f), T("ground_grass.fbx", 0.05f) };
    static readonly Tile[] EdgeTiles =
    {
        T("ground_pathTile.fbx", 0.05f), T("ground_grass.fbx", 0.05f),
        T("cliff_blockQuarter_stone.fbx", 0.25f), T("cliff_blockQuarter_stone.fbx", 0.25f),
        T("path_stone.fbx", 0.05f),
    };

    [MenuItem("Carry/Build Rock Course (Replace All Path Footholds)")]
    public static void Run()
    {
        var log = new StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find("ForestStage_Greybox");
            if (root == null) throw new Exception("ForestStage_Greybox root not found -- run Carry/Build Forest Greybox first.");

            var pathRoot = root.transform.Find("Path").gameObject;
            var dressingRoot = root.transform.Find("Dressing").gameObject;

            CleanUpOldFootholds(pathRoot, dressingRoot, log);

            var courseRoot = new GameObject("RockCourse");
            courseRoot.transform.SetParent(pathRoot.transform, false);

            BuildMosaicSegment(courseRoot, "StartPlatform", 0f, 10f, 8f, 0f, log);
            BuildMosaicSegment(courseRoot, "SafePath1", 10f, 28f, 6f, 0f, log);
            BuildRockShelf(courseRoot, "Step1", 28f, 32f, 5f, 0.35f, log);
            BuildRockShelf(courseRoot, "Step2", 32f, 36f, 5f, 0.70f, log);
            BuildMosaicSegment(courseRoot, "FlatPath2", 36f, 46f, 5f, 0.70f, log);
            // Gap1 (46-48): intentionally empty, tutorial jump over the river.
            BuildMosaicSegment(courseRoot, "BridgeApproach", 48f, 58f, 2.8f, 0.70f, log);
            BuildBridgeMosaic(courseRoot, "NarrowBridge", 58f, 78f, 0.70f, log);
            // Gap2 (78-80): intentionally empty, second jump right off the bridge.
            BuildBridgeMosaic(courseRoot, "NarrowBridge2", 80f, 86f, 0.70f, log);
            BuildMosaicSegment(courseRoot, "RestArea", 86f, 100f, 6f, 0.70f, log);
            // SteppingStones (100-116): left as-is, already real RockPath assets.
            BuildMosaicSegment(courseRoot, "Landing", 116f, 120f, 5f, 0.70f, log);
            BuildMosaicSegment(courseRoot, "FinalApproach", 120f, 134f, 5f, 0.70f, log);
            BuildMosaicSegment(courseRoot, "GateFloor", 134f, 140f, 6f, 0.70f, log);

            RebuildVegetation(dressingRoot, log);
            RebuildBoulders(dressingRoot, log);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
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

    static void CleanUpOldFootholds(GameObject pathRoot, GameObject dressingRoot, StringBuilder log)
    {
        string[] oldBlocks =
        {
            "StartPlatform", "SafePath1", "Step1", "Step2", "FlatPath2",
            "BridgeApproach", "NarrowBridge", "NarrowBridge2",
            "RestArea", "Landing", "FinalApproach", "GateFloor",
            "NarrowBridge_Tiles", "NarrowBridge2_Tiles", "Step1_RockTiles", "Step2_RockTiles",
            "RockCourse",
        };
        int removed = 0;
        foreach (var name in oldBlocks)
        {
            var t = pathRoot.transform.Find(name);
            if (t != null) { UnityEngine.Object.DestroyImmediate(t.gameObject); removed++; }
        }
        foreach (var name in new[] { "GroundDetail", "Vegetation", "Boulders" })
        {
            var t = dressingRoot.transform.Find(name);
            if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
        }
        log.AppendLine("Removed " + removed + " old foothold objects.");
    }

    // ---- Dirt/grass/low-rock mosaic: the everyday "walking through the forest" ground. ----
    static void BuildMosaicSegment(GameObject parent, string name, float z0, float z1, float width, float baseY, StringBuilder log)
    {
        var group = new GameObject(name);
        group.transform.SetParent(parent.transform, false);

        var rng = new System.Random(HashName(name));
        int wCells = Mathf.Max(1, Mathf.RoundToInt(width));
        int lCells = Mathf.Max(1, Mathf.RoundToInt(z1 - z0));
        float x0 = -width * 0.5f;
        int placed = 0;

        for (int xi = 0; xi < wCells; xi++)
        {
            float centerDist = Mathf.Abs((xi + 0.5f) - wCells * 0.5f) / (wCells * 0.5f);
            var palette = centerDist < 0.45f ? CenterTiles : EdgeTiles;
            for (int zi = 0; zi < lCells; zi++)
            {
                var tileDef = palette[rng.Next(palette.Length)];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(tileDef.prefab);
                if (prefab == null) continue;

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                inst.name = "Tile_" + xi + "_" + zi;
                inst.transform.position = new Vector3(x0 + xi + 0.5f, baseY - tileDef.height, z0 + zi + 0.5f);
                inst.transform.rotation = Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
                AddSolidGroundCollider(inst, baseY, 1f, 1f);
                placed++;
            }
        }
        log.AppendLine(name + ": " + placed + " ground tiles.");
    }

    // ---- A low rock shelf the player climbs onto (Step1/Step2). ----
    static void BuildRockShelf(GameObject parent, string name, float z0, float z1, float width, float topY, StringBuilder log)
    {
        var group = new GameObject(name);
        group.transform.SetParent(parent.transform, false);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "cliff_block_stone.fbx");
        if (prefab == null) { log.AppendLine(name + ": cliff_block_stone.fbx missing"); return; }

        var rng = new System.Random(HashName(name));
        int wCells = Mathf.Max(1, Mathf.RoundToInt(width));
        int lCells = Mathf.Max(1, Mathf.RoundToInt(z1 - z0));
        float x0 = -width * 0.5f;
        int placed = 0;

        for (int xi = 0; xi < wCells; xi++)
        {
            for (int zi = 0; zi < lCells; zi++)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                inst.name = "Rock_" + xi + "_" + zi;
                inst.transform.position = new Vector3(x0 + xi + 0.5f, topY - 1f, z0 + zi + 0.5f);
                inst.transform.rotation = Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
                AddFittedBoxCollider(inst);
                placed++;
            }
        }
        log.AppendLine(name + ": " + placed + " rock shelf tiles.");
    }

    // ---- Stone bridge deck, tiled 1m at a time with its own colliders (no hidden primitive underneath). ----
    static void BuildBridgeMosaic(GameObject parent, string name, float z0, float z1, float topY, StringBuilder log)
    {
        var group = new GameObject(name);
        group.transform.SetParent(parent.transform, false);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "bridge_center_stone.fbx");
        if (prefab == null) { log.AppendLine(name + ": bridge_center_stone.fbx missing"); return; }

        int count = Mathf.RoundToInt(z1 - z0);
        for (int i = 0; i < count; i++)
        {
            float z = z0 + i + 0.5f;
            var tile = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
            tile.name = "BridgeTile_" + i;
            tile.transform.position = new Vector3(0f, topY - 0.3f, z);
            AddSolidGroundCollider(tile, topY, 1.1f, 1f);
        }
        log.AppendLine(name + ": " + count + " bridge deck tiles.");
    }

    static void RebuildVegetation(GameObject dressingRoot, StringBuilder log)
    {
        const string KayForest = "Assets/ExternalAssets/KayKitForest/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx(unity)/";
        const string Quat = "Assets/ExternalAssets/QuaterniusNatureMegaKit/FBX (Unity)/";

        string[] trees = { KayForest + "Tree_2_C_Color1.fbx", Quat + "CommonTree_1.fbx", Quat + "DeadTree_1.fbx" };
        string[] bushes = { KayForest + "Bush_2_B_Color1.fbx", KayForest + "Bush_4_C_Color1.fbx" };
        string[] rocks = { KayForest + "Rock_2_A_Color1.fbx", KayForest + "Rock_3_N_Color1.fbx", Quat + "Rock_Medium_1.fbx" };
        string[] grass = { KayForest + "Grass_1_A_Color1.fbx" };

        var vegRoot = new GameObject("Vegetation");
        vegRoot.transform.SetParent(dressingRoot.transform, false);

        // (name, z0, z1, halfWidth, baseY) -- baseY must match the segment built above.
        var segments = new (string name, float z0, float z1, float halfWidth, float baseY)[]
        {
            ("StartPlatform", 0f, 10f, 4f, 0f),
            ("SafePath1", 10f, 28f, 3f, 0f),
            ("FlatPath2", 36f, 46f, 2.5f, 0.70f),
            ("RestArea", 86f, 100f, 3f, 0.70f),
            ("Landing", 116f, 120f, 2.5f, 0.70f),
            ("FinalApproach", 120f, 134f, 2.5f, 0.70f),
            ("GateFloor", 134f, 140f, 3f, 0.70f),
        };

        var rng = new System.Random(12345);
        int placed = 0;
        foreach (var seg in segments)
        {
            float len = seg.z1 - seg.z0;
            placed += Scatter(vegRoot, trees, seg.z0, seg.z1, seg.halfWidth, seg.baseY, rng, Mathf.Max(2, Mathf.RoundToInt(len / 6f)), 1.5f, 3.5f, true);
            placed += Scatter(vegRoot, bushes, seg.z0, seg.z1, seg.halfWidth, seg.baseY, rng, Mathf.Max(2, Mathf.RoundToInt(len / 4f)), 1.0f, 3f, false);
            placed += Scatter(vegRoot, rocks, seg.z0, seg.z1, seg.halfWidth, seg.baseY, rng, Mathf.Max(1, Mathf.RoundToInt(len / 8f)), 0.8f, 2.5f, false);
            placed += Scatter(vegRoot, grass, seg.z0, seg.z1, seg.halfWidth, seg.baseY, rng, Mathf.Max(3, Mathf.RoundToInt(len / 2f)), 0.5f, 2.5f, false);
        }
        log.AppendLine("Vegetation instances placed: " + placed);
    }

    static int Scatter(GameObject parent, string[] prefabPaths, float z0, float z1, float halfWidth, float baseY,
        System.Random rng, int count, float marginMin, float marginMax, bool addTrunkCollider)
    {
        int placedCount = 0;
        for (int i = 0; i < count; i++)
        {
            var path = prefabPaths[rng.Next(prefabPaths.Length)];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            float z = z0 + (float)rng.NextDouble() * (z1 - z0);
            float side = rng.Next(2) == 0 ? -1f : 1f;
            float margin = marginMin + (float)rng.NextDouble() * (marginMax - marginMin);
            float x = side * (halfWidth + margin);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            inst.name = System.IO.Path.GetFileNameWithoutExtension(path) + "_" + i + "_" + placedCount;
            inst.transform.position = new Vector3(x, baseY, z);
            inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            inst.transform.localScale = Vector3.one * (0.85f + (float)rng.NextDouble() * 0.3f);

            if (addTrunkCollider)
            {
                var col = inst.AddComponent<CapsuleCollider>();
                col.radius = 0.3f;
                col.height = 3f;
                col.center = new Vector3(0f, 1.5f, 0f);
            }
            placedCount++;
        }
        return placedCount;
    }

    static void RebuildBoulders(GameObject dressingRoot, StringBuilder log)
    {
        const string Quat = "Assets/ExternalAssets/QuaterniusNatureMegaKit/FBX (Unity)/";
        var boulderPrefabs = new[]
        {
            AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "rock_tallA.fbx"),
            AssetDatabase.LoadAssetAtPath<GameObject>(Quat + "Rock_Medium_1.fbx"),
        };

        var boulderRoot = new GameObject("Boulders");
        boulderRoot.transform.SetParent(dressingRoot.transform, false);

        var spots = new[]
        {
            new Vector3(-3.6f, 0.0f, 16f),
            new Vector3(2.6f, 0.70f, 40f),
            new Vector3(-2.6f, 0.70f, 92f),
            new Vector3(2.4f, 0.70f, 122f),
        };

        var rng = new System.Random(4242);
        int placed = 0;
        foreach (var spot in spots)
        {
            var prefab = boulderPrefabs[rng.Next(boulderPrefabs.Length)];
            if (prefab == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, boulderRoot.transform);
            inst.name = "Boulder_" + placed;
            float scale = 0.8f + (float)rng.NextDouble() * 0.5f;
            inst.transform.localScale = Vector3.one * scale;
            inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            var renderers = inst.GetComponentsInChildren<Renderer>();
            float height = renderers.Length > 0 ? renderers[0].bounds.size.y : 1f;
            inst.transform.position = spot - new Vector3(0f, height * 0.3f, 0f);

            AddFittedBoxCollider(inst);
            placed++;
        }
        log.AppendLine("Boulders placed: " + placed);
    }

    static int HashName(string s)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in s) hash = hash * 31 + c;
            return hash;
        }
    }

    // A solid box collider from (topY - depth) to topY, sized to the tile's own
    // footprint -- independent of how thin the visual mesh is, so the
    // CharacterController always gets reliable ground to stand on.
    static void AddSolidGroundCollider(GameObject tile, float topY, float footprintX, float footprintZ, float depth = 0.4f)
    {
        var box = tile.AddComponent<BoxCollider>();
        float localTop = topY - tile.transform.position.y;
        box.center = new Vector3(0f, localTop - depth * 0.5f, 0f);
        box.size = new Vector3(footprintX, depth, footprintZ);
    }

    static void AddFittedBoxCollider(GameObject target)
    {
        var renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
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
    }
}
