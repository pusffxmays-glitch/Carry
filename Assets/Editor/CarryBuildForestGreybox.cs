using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds the "1. 通常の森" (Normal Forest) greybox stage described in
// Reference/Stage/stage_overview.png as a standalone, re-runnable scene.
// Reuses the already-tuned Goblin/Camera/Light from CastleStage.unity
// (copies them, never touches that scene) so the stage can be playtested
// immediately. Re-running this menu item rebuilds the greybox from scratch.
public static class CarryBuildForestGreybox
{
    private const string SourceScenePath = "Assets/Scenes/CastleStage.unity";
    private const string ForestScenePath = "Assets/Scenes/ForestStage_Greybox.unity";

    // ---- Layout constants (meters). Z = progress axis, X = left/right, Y = up. ----
    const float StartZ0 = 0f, StartZ1 = 10f, StartW = 8f;
    const float Safe1Z0 = 10f, Safe1Z1 = 28f, Safe1W = 6f;
    const float Step1Z0 = 28f, Step1Z1 = 32f, StepW = 5f, Step1Y = 0.35f;
    const float Step2Z0 = 32f, Step2Z1 = 36f, Step2Y = 0.70f;
    const float Flat2Z0 = 36f, Flat2Z1 = 46f, Flat2W = 5f;
    const float Gap1Z0 = 46f, Gap1Z1 = 48f;
    const float ApproachZ0 = 48f, ApproachZ1 = 58f;
    const float BridgeZ0 = 58f, BridgeZ1 = 78f, BridgeW = 1.6f;
    const float Gap2Z0 = 78f, Gap2Z1 = 80f;
    const float Bridge2Z0 = 80f, Bridge2Z1 = 86f;
    const float RestZ0 = 86f, RestZ1 = 100f, RestW = 6f;
    const float StonesZ0 = 100f, StonesZ1 = 116f;
    const float LandingZ0 = 116f, LandingZ1 = 120f, LandingW = 5f;
    const float FinalZ0 = 120f, FinalZ1 = 134f, FinalW = 5f;
    const float GateZ0 = 134f, GateZ1 = 140f, GateW = 6f;
    const float MidY = 0.70f;

    const float RiverZ0 = Safe1Z0; // 10 - river begins just past the safe start
    const float RiverZ1 = GateZ0;  // 134
    const float RiverHalfWidth = 7f;
    const float RiverTriggerTopY = -2.2f;
    const float RiverTriggerBottomY = -6f;
    const float RiverSurfaceY = -4.6f;

    [MenuItem("Carry/Build Forest Greybox")]
    public static void Run()
    {
        var log = new StringBuilder();
        try
        {
            // Batchmode starts with an unsaved "Untitled" scene loaded, which blocks
            // EditorSceneManager.NewScene(..., Additive) later ("untitled scene unsaved").
            // Load the source additively first, then drop the untitled scene so only
            // saved scenes remain loaded.
            var initialScene = EditorSceneManager.GetActiveScene();
            var sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
            if (string.IsNullOrEmpty(initialScene.path) && initialScene.isLoaded)
            {
                EditorSceneManager.CloseScene(initialScene, true);
            }
            GameObject srcGoblin = null, srcCam = null, srcLight = null;
            foreach (var root in sourceScene.GetRootGameObjects())
            {
                if (root.name == "Goblin") srcGoblin = root;
                else if (root.name == "Main Camera") srcCam = root;
                else if (root.name == "Directional Light") srcLight = root;
            }
            if (srcGoblin == null) throw new Exception("Goblin not found in " + SourceScenePath);
            if (srcCam == null) throw new Exception("Main Camera not found in " + SourceScenePath);

            Scene forestScene;
            bool forestExists = System.IO.File.Exists(ForestScenePath);
            if (forestExists)
            {
                forestScene = EditorSceneManager.OpenScene(ForestScenePath, OpenSceneMode.Additive);
                var roots = forestScene.GetRootGameObjects();
                for (int i = roots.Length - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(roots[i]);
            }
            else
            {
                forestScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }

            Vector3 spawnPos = new Vector3(0f, 0.2f, 4f);

            var goblinCopy = (GameObject)UnityEngine.Object.Instantiate(srcGoblin);
            goblinCopy.name = "Goblin";
            SceneManager.MoveGameObjectToScene(goblinCopy, forestScene);
            goblinCopy.transform.position = spawnPos;
            goblinCopy.transform.rotation = Quaternion.identity;

            var camCopy = (GameObject)UnityEngine.Object.Instantiate(srcCam);
            camCopy.name = "Main Camera";
            SceneManager.MoveGameObjectToScene(camCopy, forestScene);
            var rig = camCopy.GetComponent<CarryCameraRig>();
            if (rig != null) rig.target = goblinCopy.transform;

            if (srcLight != null)
            {
                var lightCopy = (GameObject)UnityEngine.Object.Instantiate(srcLight);
                lightCopy.name = "Directional Light";
                SceneManager.MoveGameObjectToScene(lightCopy, forestScene);
            }

            EditorSceneManager.CloseScene(sourceScene, true);

            BuildGreybox(forestScene, spawnPos, log);

            EditorSceneManager.SetActiveScene(forestScene);
            EditorSceneManager.MarkSceneDirty(forestScene);
            EditorSceneManager.SaveScene(forestScene, ForestScenePath);
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

    static void BuildGreybox(Scene scene, Vector3 spawnPos, StringBuilder log)
    {
        var root = new GameObject("ForestStage_Greybox");
        SceneManager.MoveGameObjectToScene(root, scene);

        var pathRoot = NewChild(root, "Path");
        var riverRoot = NewChild(root, "River");
        var recoveryRoot = NewChild(root, "RecoveryPoints");
        var checkpointRoot = NewChild(root, "Checkpoints");
        var dressingRoot = NewChild(root, "Dressing");

        var matPath = GetMat("Mat_Greybox_Path", new Color(0.72f, 0.72f, 0.68f, 1f));
        var matStart = GetMat("Mat_Greybox_Start", new Color(0.55f, 0.75f, 0.5f, 1f));
        var matNarrow = GetMat("Mat_Greybox_Narrow", new Color(0.75f, 0.65f, 0.45f, 1f));
        var matStone = GetMat("Mat_Greybox_Stone", new Color(0.55f, 0.6f, 0.65f, 1f));
        var matRiver = GetMat("Mat_Greybox_River", new Color(0.25f, 0.45f, 0.75f, 0.55f));
        var matRecovery = GetMat("Mat_Greybox_Recovery", new Color(0.45f, 0.3f, 0.2f, 1f));
        var matCheckpoint = GetMat("Mat_Greybox_Checkpoint", new Color(0.95f, 0.85f, 0.2f, 1f));
        var matGate = GetMat("Mat_Greybox_Gate", new Color(0.35f, 0.35f, 0.4f, 1f));
        var matFog = GetMat("Mat_Greybox_Fog", new Color(0.6f, 0.6f, 0.65f, 0.35f));

        // ---- Main path ----
        Block(pathRoot, "StartPlatform", 0f, StartZ0, StartZ1, StartW, 0f, 1f, matStart);
        Block(pathRoot, "SafePath1", 0f, Safe1Z0, Safe1Z1, Safe1W, 0f, 1f, matPath);
        Block(pathRoot, "Step1", 0f, Step1Z0, Step1Z1, StepW, Step1Y, 1f, matPath);
        Block(pathRoot, "Step2", 0f, Step2Z0, Step2Z1, StepW, Step2Y, 1f, matPath);
        Block(pathRoot, "FlatPath2", 0f, Flat2Z0, Flat2Z1, Flat2W, MidY, 1f, matPath);
        // Gap1 (46-48): intentionally empty, first tutorial jump over the river
        Block(pathRoot, "BridgeApproach", 0f, ApproachZ0, ApproachZ1, 2.8f, MidY, 1f, matNarrow);
        Block(pathRoot, "NarrowBridge", 0f, BridgeZ0, BridgeZ1, BridgeW, MidY, 1f, matNarrow);
        // Gap2 (78-80): intentionally empty, second riskier jump right off the bridge
        Block(pathRoot, "NarrowBridge2", 0f, Bridge2Z0, Bridge2Z1, BridgeW, MidY, 1f, matNarrow);
        Block(pathRoot, "RestArea", 0f, RestZ0, RestZ1, RestW, MidY, 1f, matPath);
        BuildSteppingStones(pathRoot, matStone);
        Block(pathRoot, "Landing", 0f, LandingZ0, LandingZ1, LandingW, MidY, 1f, matPath);
        Block(pathRoot, "FinalApproach", 0f, FinalZ0, FinalZ1, FinalW, MidY, 1f, matPath);
        Block(pathRoot, "GateFloor", 0f, GateZ0, GateZ1, GateW, MidY, 1f, matPath);

        BuildGate(dressingRoot, matGate, matFog);

        // ---- River ----
        float riverCenterZ = (RiverZ0 + RiverZ1) * 0.5f;
        float riverLenZ = RiverZ1 - RiverZ0;

        var riverVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        riverVisual.name = "RiverVisual";
        riverVisual.transform.SetParent(riverRoot.transform, false);
        riverVisual.transform.position = new Vector3(0f, RiverSurfaceY, riverCenterZ);
        riverVisual.transform.localScale = new Vector3(RiverHalfWidth * 2f, 0.2f, riverLenZ);
        UnityEngine.Object.DestroyImmediate(riverVisual.GetComponent<Collider>());
        riverVisual.GetComponent<MeshRenderer>().sharedMaterial = matRiver;

        var riverTrigger = new GameObject("RiverTriggerVolume");
        riverTrigger.transform.SetParent(riverRoot.transform, false);
        riverTrigger.transform.position = new Vector3(0f, (RiverTriggerTopY + RiverTriggerBottomY) * 0.5f, riverCenterZ);
        var box = riverTrigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(RiverHalfWidth * 2f, RiverTriggerTopY - RiverTriggerBottomY, riverLenZ);
        riverTrigger.AddComponent<RiverTriggerZone>();

        var flowGo = new GameObject("RiverFlowController");
        flowGo.transform.SetParent(riverRoot.transform, false);
        var flow = flowGo.AddComponent<RiverFlowController>();
        flow.riverSurfaceY = RiverSurfaceY + 0.3f;
        flow.upstreamLimitZ = RiverZ0;
        flow.riverHalfWidth = RiverHalfWidth - 1f;
        flow.SetInitialCheckpoint(spawnPos);

        // ---- Recovery points (rocks/logs poking up out of the gorge) ----
        RecoveryPointObj(recoveryRoot, "Recovery_1", -2.5f, 44f, matRecovery);
        RecoveryPointObj(recoveryRoot, "Recovery_2", 2.8f, 52f, matRecovery);
        RecoveryPointObj(recoveryRoot, "Recovery_3", -3.0f, 64f, matRecovery);
        RecoveryPointObj(recoveryRoot, "Recovery_4", 3.0f, 72f, matRecovery);
        RecoveryPointObj(recoveryRoot, "Recovery_5", -2.5f, 82f, matRecovery);
        RecoveryPointObj(recoveryRoot, "Recovery_6", 2.8f, 104f, matRecovery);
        RecoveryPointObj(recoveryRoot, "Recovery_7", -2.8f, 112f, matRecovery);

        // ---- Checkpoints ----
        CheckpointObj(checkpointRoot, "Checkpoint_Start", 0f, 5f, StartW, 0f, matCheckpoint);
        CheckpointObj(checkpointRoot, "Checkpoint_RestArea", 0f, 93f, RestW, MidY, matCheckpoint);
        CheckpointObj(checkpointRoot, "Checkpoint_Landing", 0f, 118f, LandingW, MidY, matCheckpoint);

        log.AppendLine("Greybox built: " + pathRoot.transform.childCount + " path pieces, 7 recovery points, 3 checkpoints, river " +
            RiverZ0 + "-" + RiverZ1 + ".");
    }

    static void BuildSteppingStones(GameObject parent, Material mat)
    {
        Vector2[] stones =
        {
            new Vector2(0f, 101f),
            new Vector2(1.6f, 104.5f),
            new Vector2(-1.6f, 108f),
            new Vector2(1.6f, 111.5f),
            new Vector2(0f, 115f),
        };
        for (int i = 0; i < stones.Length; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "SteppingStone_" + (i + 1);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = new Vector3(stones[i].x, MidY - 0.3f, stones[i].y);
            go.transform.localScale = new Vector3(1.6f, 0.3f, 1.6f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
        }
    }

    static void BuildGate(GameObject parent, Material matGate, Material matFog)
    {
        float centerZ = (GateZ0 + GateZ1) * 0.5f;

        var pillarL = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pillarL.name = "GatePillarL";
        pillarL.transform.SetParent(parent.transform, false);
        pillarL.transform.position = new Vector3(-2.5f, MidY + 1.5f, centerZ);
        pillarL.transform.localScale = new Vector3(0.8f, 3f, 0.8f);
        pillarL.GetComponent<MeshRenderer>().sharedMaterial = matGate;
        pillarL.isStatic = true;

        var pillarR = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pillarR.name = "GatePillarR";
        pillarR.transform.SetParent(parent.transform, false);
        pillarR.transform.position = new Vector3(2.5f, MidY + 1.5f, centerZ);
        pillarR.transform.localScale = new Vector3(0.8f, 3f, 0.8f);
        pillarR.GetComponent<MeshRenderer>().sharedMaterial = matGate;
        pillarR.isStatic = true;

        var lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lintel.name = "GateLintel";
        lintel.transform.SetParent(parent.transform, false);
        lintel.transform.position = new Vector3(0f, MidY + 3.2f, centerZ);
        lintel.transform.localScale = new Vector3(6.2f, 0.8f, 0.8f);
        lintel.GetComponent<MeshRenderer>().sharedMaterial = matGate;
        lintel.isStatic = true;

        var fog = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fog.name = "FogForest_Placeholder";
        fog.transform.SetParent(parent.transform, false);
        fog.transform.position = new Vector3(0f, MidY + 2f, GateZ1 + 3f);
        fog.transform.localScale = new Vector3(8f, 4f, 0.5f);
        UnityEngine.Object.DestroyImmediate(fog.GetComponent<Collider>());
        fog.GetComponent<MeshRenderer>().sharedMaterial = matFog;

        // Marks where Stage 2 (霧の森) would attach. Not built out yet.
        var connector = new GameObject("To_FogForest_Connector");
        connector.transform.SetParent(parent.transform, false);
        connector.transform.position = new Vector3(0f, MidY, GateZ1);
    }

    static GameObject NewChild(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    static Material GetMat(string name, Color color)
    {
        string dir = "Assets/Stage/Greybox";
        string path = dir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Stage")) AssetDatabase.CreateFolder("Assets", "Stage");
                AssetDatabase.CreateFolder("Assets/Stage", "Greybox");
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader);
            if (color.a < 1f)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            mat.color = color;
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.color = color;
        }
        return mat;
    }

    static GameObject Block(GameObject parent, string name, float centerX, float z0, float z1, float width, float topY, float thickness, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        float centerZ = (z0 + z1) * 0.5f;
        float lenZ = z1 - z0;
        go.transform.position = new Vector3(centerX, topY - thickness * 0.5f, centerZ);
        go.transform.localScale = new Vector3(width, thickness, lenZ);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        return go;
    }

    static void RecoveryPointObj(GameObject parent, string name, float x, float z, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        const float centerY = -1.6f;
        go.transform.position = new Vector3(x, centerY, z);
        go.transform.localScale = new Vector3(1.2f, 0.5f, 1.2f); // squat rock: ~1.2m diameter, ~1m tall
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        var rp = go.AddComponent<RecoveryPoint>();
        rp.standOffset = new Vector3(0f, 0.65f, 0f); // lands just above the rock's top surface
    }

    static void CheckpointObj(GameObject parent, string name, float centerX, float z, float width, float topY, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.position = new Vector3(centerX, topY + 1f, z);
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(width, 2f, 1f);
        go.AddComponent<CheckpointZone>();

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "Marker";
        marker.transform.SetParent(go.transform, false);
        marker.transform.localPosition = new Vector3(0f, -0.9f, 0f);
        marker.transform.localScale = new Vector3(width, 0.2f, 0.2f);
        UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
