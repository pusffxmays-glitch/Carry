using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Ground-up rebuild of the normal-forest stage around the design brief:
// "a real stream is the spine of the stage; the player crosses it by using
// the rocks/dirt/roots/logs that naturally sit in and around it" -- not a
// paved lane with trees along the edges. Builds into a NEW scene
// (ForestStage_Realistic.unity) so ForestStage_Greybox.unity is left
// completely untouched as a backup/reference of the previous pass.
//
// Visual material now comes from Poly Haven's photoreal "pine_forest"
// collection (CC0) instead of the stylized Kenney/KayKit/Quaternius kits --
// see ASSET_LICENSES.md. The river fall/recovery gameplay scripts
// (RiverFlowController etc.) are reused unchanged; only the surrounding
// geometry is new.
public static class CarryBuildRealisticForest
{
    const string SourceScenePath = "Assets/Scenes/CastleStage.unity";
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const string PH = "Assets/ExternalAssets/PolyHaven/";

    const float RiverZ0 = 8f, RiverZ1 = 105f;
    const float RiverHalfWidth = 5f;
    const float RiverSurfaceY = -1.5f;
    const float BankY = 0f; // main route / bank ground height

    [MenuItem("Carry/Build Realistic Forest Stage (New Scene)")]
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
            foreach (var srcRoot in sourceScene.GetRootGameObjects())
            {
                if (srcRoot.name == "Goblin") srcGoblin = srcRoot;
                else if (srcRoot.name == "Main Camera") srcCam = srcRoot;
                else if (srcRoot.name == "Directional Light") srcLight = srcRoot;
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

            Vector3 spawnPos = new Vector3(0f, 0.2f, 4f);
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
                if (l != null) { l.intensity = Mathf.Max(l.intensity, 1.2f); l.color = new Color(1f, 0.96f, 0.85f); }
            }

            EditorSceneManager.CloseScene(sourceScene, true);

            var root = new GameObject("ForestStage_Realistic");
            SceneManager.MoveGameObjectToScene(root, scene);
            var bankRoot = NewChild(root, "Banks");
            var riverRoot = NewChild(root, "River");
            var footholdRoot = NewChild(root, "Footholds");
            var vegRoot = NewChild(root, "Vegetation");
            var recoveryRoot = NewChild(root, "RecoveryPoints");
            var checkpointRoot = NewChild(root, "Checkpoints");

            BuildSkyAndLighting(log);
            BuildBanks(bankRoot, log);
            BuildRiver(riverRoot, log);
            var footholds = BuildFootholds(footholdRoot, log);
            BuildRecoveryPoints(recoveryRoot, footholds, log);
            BuildCheckpoints(checkpointRoot, spawnPos, log);
            BuildVegetation(vegRoot, log);

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

    // ---- Skybox/lighting: Poly Haven's "Mossy Forest" HDRI (tagged river/rock/tree/moss). ----
    static void BuildSkyAndLighting(StringBuilder log)
    {
        var hdri = AssetDatabase.LoadAssetAtPath<Texture2D>(PH + "mossy_forest_hdri/mossy_forest_2k.hdr");
        if (hdri == null) { log.AppendLine("mossy_forest HDRI missing"); return; }

        var importer = AssetImporter.GetAtPath(PH + "mossy_forest_hdri/mossy_forest_2k.hdr") as TextureImporter;
        if (importer != null && importer.textureShape != TextureImporterShape.Texture2D)
        {
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.SaveAndReimport();
        }

        var skyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Stage/Greybox/Mat_Sky_MossyForest.mat");
        if (skyMat == null)
        {
            skyMat = new Material(Shader.Find("Skybox/Panoramic"));
            AssetDatabase.CreateAsset(skyMat, "Assets/Stage/Greybox/Mat_Sky_MossyForest.mat");
        }
        if (skyMat.HasProperty("_MainTex")) skyMat.SetTexture("_MainTex", hdri);
        if (skyMat.HasProperty("_Tex")) skyMat.SetTexture("_Tex", hdri);
        EditorUtility.SetDirty(skyMat);
        RenderSettings.skybox = skyMat;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        log.AppendLine("Skybox set to mossy_forest HDRI.");
    }

    // ---- Wide ground banks flanking the river (dirt + leaf-litter, not a paved lane). ----
    static void BuildBanks(GameObject parent, StringBuilder log)
    {
        var mudTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PH + "mud_forest/mud_forest_diff_2k.jpg");
        var groundTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PH + "forrest_ground_01/forrest_ground_01_diff_2k.jpg");

        var matLeft = GetOrCreateMat("Mat_BankLeft", mudTex, new Vector2(9f, 26f));
        var matRight = GetOrCreateMat("Mat_BankRight", groundTex, new Vector2(9f, 26f));

        // Two wide, slightly uneven ground slabs -- these are backdrop only,
        // footholds/logs are what the player actually stands on above the water.
        Block(parent, "BankLeft", -RiverHalfWidth - 9f, (RiverZ0 + RiverZ1) * 0.5f, 18f, RiverZ1 - RiverZ0 + 20f, BankY, matLeft);
        Block(parent, "BankRight", RiverHalfWidth + 9f, (RiverZ0 + RiverZ1) * 0.5f, 18f, RiverZ1 - RiverZ0 + 20f, BankY, matRight);
        Block(parent, "StartGround", 0f, 4f, 14f, 12f, BankY, matLeft);
        Block(parent, "EndGround", 0f, RiverZ1 + 6f, 14f, 14f, BankY, matRight);
        log.AppendLine("Banks built.");
    }

    static void BuildRiver(GameObject parent, StringBuilder log)
    {
        float centerZ = (RiverZ0 + RiverZ1) * 0.5f;
        float lenZ = RiverZ1 - RiverZ0;

        var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
        water.name = "RiverVisual";
        water.transform.SetParent(parent.transform, false);
        water.transform.position = new Vector3(0f, RiverSurfaceY, centerZ);
        water.transform.localScale = new Vector3(RiverHalfWidth * 2f, 0.15f, lenZ);
        UnityEngine.Object.DestroyImmediate(water.GetComponent<Collider>());
        var mat = GetOrCreateMat("Mat_River", null, Vector2.one);
        mat.color = new Color(0.15f, 0.32f, 0.30f, 0.75f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.85f);
        SetTransparent(mat);
        water.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var triggerGo = new GameObject("RiverTriggerVolume");
        triggerGo.transform.SetParent(parent.transform, false);
        triggerGo.transform.position = new Vector3(0f, RiverSurfaceY - 0.8f, centerZ);
        var box = triggerGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(RiverHalfWidth * 2f + 2f, 2.2f, lenZ);
        triggerGo.AddComponent<RiverTriggerZone>();

        var flowGo = new GameObject("RiverFlowController");
        flowGo.transform.SetParent(parent.transform, false);
        var flow = flowGo.AddComponent<RiverFlowController>();
        flow.riverSurfaceY = RiverSurfaceY - 0.3f;
        flow.upstreamLimitZ = RiverZ0;
        flow.riverHalfWidth = RiverHalfWidth + 0.5f;
        flow.SetInitialCheckpoint(new Vector3(0f, BankY, 4f));

        log.AppendLine("River built, Z " + RiverZ0 + "-" + RiverZ1 + ", surface Y=" + RiverSurfaceY);
    }

    // ---- The route: real rocks/logs/dirt clumps weaving back and forth across the stream. ----
    static System.Collections.Generic.List<Vector3> BuildFootholds(GameObject parent, StringBuilder log)
    {
        var points = new System.Collections.Generic.List<Vector3>();
        var rng = new System.Random(777);

        var boulder = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "boulder_01/boulder_01_2k.fbx");
        var mossSet1 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "rock_moss_set_01/rock_moss_set_01_2k.fbx");
        var mossSet2 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "rock_moss_set_02/rock_moss_set_02_2k.fbx");
        var log_ = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "dead_tree_trunk_02/dead_tree_trunk_02_2k.fbx");
        var stump1 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "tree_stump_01/tree_stump_01_2k.fbx");
        var roots = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");

        int n = 16;
        float z = 14f;
        float prevX = 0f;
        int placed = 0;
        for (int i = 0; i < n; i++)
        {
            float x = 2.3f * Mathf.Sin(i * 0.85f) + ((float)rng.NextDouble() - 0.5f) * 1.0f;
            float dz = 5.0f + (float)rng.NextDouble() * 1.6f;
            z += (i == 0) ? 0f : dz;
            float y = BankY - 0.15f + (float)rng.NextDouble() * 0.35f;

            int kind = i % 5;
            GameObject inst;
            if (kind == 4 && log_ != null)
            {
                // Log crossing: orient it from this point toward the next one.
                float nextX = 2.3f * Mathf.Sin((i + 1) * 0.85f);
                Vector3 dir = new Vector3(nextX - x, 0f, dz).normalized;
                inst = (GameObject)PrefabUtility.InstantiatePrefab(log_, parent.transform);
                inst.transform.position = new Vector3(x, y - 0.15f, z);
                inst.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 90f, 0f);
                inst.transform.localScale = Vector3.one * (0.9f + (float)rng.NextDouble() * 0.3f);
            }
            else if (kind == 0 && mossSet1 != null)
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(mossSet1, parent.transform);
                inst.transform.position = new Vector3(x, y - 0.3f, z);
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                inst.transform.localScale = Vector3.one * (0.65f + (float)rng.NextDouble() * 0.25f);
            }
            else if (kind == 1 && mossSet2 != null)
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(mossSet2, parent.transform);
                inst.transform.position = new Vector3(x, y - 0.3f, z);
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                inst.transform.localScale = Vector3.one * (0.4f + (float)rng.NextDouble() * 0.2f);
            }
            else if (kind == 2 && stump1 != null)
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(stump1, parent.transform);
                inst.transform.position = new Vector3(x, y, z);
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                inst.transform.localScale = Vector3.one * 28f; // corrective scale, see ASSET_LICENSES.md
            }
            else if (roots != null)
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(roots, parent.transform);
                inst.transform.position = new Vector3(x, y - 0.05f, z);
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                inst.transform.localScale = Vector3.one * (1.1f + (float)rng.NextDouble() * 0.4f);
            }
            else
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(boulder, parent.transform);
                inst.transform.position = new Vector3(x, y - 0.15f, z);
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                inst.transform.localScale = Vector3.one * (1.0f + (float)rng.NextDouble() * 0.4f);
            }
            inst.name = "Foothold_" + i;
            AddFittedBoxCollider(inst, thin: kind != 4);
            points.Add(new Vector3(x, y, z));
            prevX = x;
            placed++;
        }
        log.AppendLine("Footholds placed: " + placed);
        return points;
    }

    // ---- Recovery points: a handful of the same real rocks/roots, positioned right at the waterline. ----
    static void BuildRecoveryPoints(GameObject parent, System.Collections.Generic.List<Vector3> footholds, StringBuilder log)
    {
        var stump2 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "tree_stump_02/tree_stump_02_2k.fbx");
        var roots = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
        var boulder = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "boulder_01/boulder_01_2k.fbx");

        int placed = 0;
        for (int i = 2; i < footholds.Count; i += 3)
        {
            var fp = footholds[i];
            bool useStump = (i % 2 == 0);
            var prefab = useStump ? stump2 : boulder;
            if (prefab == null) continue;

            var go = new GameObject("Recovery_" + placed);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = new Vector3(fp.x + (i % 2 == 0 ? -1.6f : 1.6f), RiverSurfaceY + 0.2f, fp.z - 1.5f);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * (useStump ? 30f : 0.6f);

            float topY = AddFittedBoxCollider(go, visual: visual, thin: false);
            var rp = go.AddComponent<RecoveryPoint>();
            rp.standOffset = new Vector3(0f, topY + 0.15f, 0f);
            placed++;
        }
        log.AppendLine("Recovery points placed: " + placed);
    }

    static void BuildCheckpoints(GameObject parent, Vector3 spawnPos, StringBuilder log)
    {
        CheckpointObj(parent, "Checkpoint_Start", spawnPos, 10f, BankY);
        CheckpointObj(parent, "Checkpoint_Mid", new Vector3(0f, BankY, (RiverZ0 + RiverZ1) * 0.5f), 8f, BankY);
        CheckpointObj(parent, "Checkpoint_End", new Vector3(0f, BankY, RiverZ1 + 4f), 10f, BankY);
        log.AppendLine("Checkpoints placed: 3");
    }

    static void CheckpointObj(GameObject parent, string name, Vector3 pos, float width, float topY)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.position = new Vector3(pos.x, topY + 1f, pos.z);
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(width, 2f, 3f);
        go.AddComponent<CheckpointZone>();
    }

    // ---- Dense forest scatter on both banks: saplings, ferns, grass, roots, stumps, dry branches, moss. ----
    static void BuildVegetation(GameObject parent, StringBuilder log)
    {
        var fir = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "fir_sapling/fir_sapling_2k.fbx");
        var pine = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_sapling_small/pine_sapling_small_2k.fbx");
        // fir_sapling_medium's needle texture renders washed-out white in-engine (texture
        // itself, not an import-linking bug -- see ASSET_LICENSES.md); dropped from the
        // scatter for now rather than fill the forest with pale trees. Revisit once fixed.
        GameObject firMed = null;
        var fern = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "fern_02/fern_02_2k.fbx");
        var grass = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "grass_medium_01/grass_medium_01_2k.fbx");
        var roots = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "pine_roots/pine_roots_2k.fbx");
        var branches = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "dry_branches_medium_01/dry_branches_medium_01_2k.fbx");
        var stump1 = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "tree_stump_01/tree_stump_01_2k.fbx");
        var moss = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "moss_01/moss_01_2k.fbx");
        var rockSmall = AssetDatabase.LoadAssetAtPath<GameObject>(PH + "rock_moss_set_01/rock_moss_set_01_2k.fbx");

        var rng = new System.Random(2024);
        int placed = 0;
        float z0 = 0f, z1 = RiverZ1 + 14f;

        // Dense tree canopy: three staggered rows per bank, close together for real forest
        // density. A slower, taller fir_sapling_medium is mixed in every few rows so the
        // near-field has genuine height variety, not just knee-high saplings.
        int treeRow = 0;
        for (float z = z0; z < z1; z += 2.6f)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                for (int row = 0; row < 3; row++)
                {
                    float bankEdge = RiverHalfWidth + 1.8f + row * 3.0f;
                    float x = side * (bankEdge + (float)rng.NextDouble() * 2.2f);
                    float zz = z + (float)rng.NextDouble() * 2.6f;
                    if (Mathf.Abs(x) < RiverHalfWidth + 1f) continue;

                    bool bigTree = firMed != null && row == 1 && (treeRow % 3 == 0);
                    var prefab = bigTree ? firMed : (rng.Next(2) == 0 ? fir : pine);
                    if (prefab == null) continue;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                    inst.name = "Tree_" + placed;
                    inst.transform.position = new Vector3(x, BankY, zz);
                    inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    float scale = bigTree ? (1.4f + (float)rng.NextDouble() * 0.8f) : (0.9f + (float)rng.NextDouble() * 0.9f);
                    inst.transform.localScale = Vector3.one * scale;
                    var col = inst.AddComponent<CapsuleCollider>();
                    col.radius = bigTree ? 0.4f : 0.25f;
                    col.height = bigTree ? 3.5f : 2f;
                    col.center = new Vector3(0f, col.height * 0.5f, 0f);
                    placed++;
                }
                treeRow++;
            }
        }

        // Undergrowth: ferns, grass, moss, dry branches, small root mats, stumps -- scattered near the banks.
        for (float z = z0; z < z1; z += 1.6f)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * (RiverHalfWidth + 0.3f + (float)rng.NextDouble() * 4.5f);
                float zz = z + (float)rng.NextDouble() * 1.6f;
                if (Mathf.Abs(x) < RiverHalfWidth - 0.5f) continue;

                int roll = rng.Next(6);
                GameObject prefab = roll switch
                {
                    0 => fern,
                    1 => grass,
                    2 => roots,
                    3 => branches,
                    4 => moss,
                    _ => rockSmall,
                };
                if (prefab == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                inst.name = "Undergrowth_" + placed;
                inst.transform.position = new Vector3(x, BankY, zz);
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float scale = roll == 5 ? 0.25f + (float)rng.NextDouble() * 0.15f : 0.8f + (float)rng.NextDouble() * 0.8f;
                inst.transform.localScale = Vector3.one * scale;
                placed++;
            }
        }

        // A few stumps as bank-side set dressing.
        for (int i = 0; i < 6; i++)
        {
            if (stump1 == null) continue;
            float side = rng.Next(2) == 0 ? -1f : 1f;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(stump1, parent.transform);
            inst.name = "Stump_" + i;
            float x = side * (RiverHalfWidth + 1f + (float)rng.NextDouble() * 3f);
            float z = z0 + (float)rng.NextDouble() * (z1 - z0);
            inst.transform.position = new Vector3(x, BankY, z);
            inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            inst.transform.localScale = Vector3.one * 28f;
            placed++;
        }

        log.AppendLine("Vegetation instances placed: " + placed);
    }

    // ---------------------------------------------------------------- helpers

    static GameObject NewChild(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

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
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    static GameObject Block(GameObject parent, string name, float centerX, float centerZ, float width, float length, float topY, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.position = new Vector3(centerX, topY - 0.5f, centerZ);
        go.transform.localScale = new Vector3(width, 1f, length);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        return go;
    }

    static float AddFittedBoxCollider(GameObject target, GameObject visual = null, bool thin = false)
    {
        var renderers = (visual != null ? visual : target).GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;
        var worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);

        var t = target.transform;
        Vector3 localCenter = t.InverseTransformPoint(worldBounds.center);
        Vector3 localSize = new Vector3(
            worldBounds.size.x / Mathf.Max(t.lossyScale.x, 0.0001f),
            worldBounds.size.y / Mathf.Max(t.lossyScale.y, 0.0001f),
            worldBounds.size.z / Mathf.Max(t.lossyScale.z, 0.0001f));
        if (thin) localSize.y = Mathf.Max(localSize.y, 0.6f / Mathf.Max(t.lossyScale.y, 0.0001f));

        var box = target.AddComponent<BoxCollider>();
        box.center = localCenter;
        box.size = localSize;
        return localCenter.y + localSize.y * 0.5f;
    }
}
