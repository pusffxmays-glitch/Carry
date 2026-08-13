using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Replaces some of the Primitive greybox visuals in ForestStage_Greybox.unity
// with external CC0 assets recorded in ASSET_LICENSES.md (KayKit Forest,
// Kenney Nature Kit, Quaternius). The bridge deck, ground material and
// vegetation scatter this script used to build here were superseded by
// CarryBuildRockCourse, which rebuilds the whole route (including the bridge)
// as a real-asset tile mosaic and deletes whatever this script made for those
// -- so this script now only owns the pieces CarryBuildRockCourse leaves alone:
// stepping stones, river recovery points, and the decorative cliff walls.
public static class CarryDressForestStage
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Greybox.unity";

    const string Kenney = "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/";
    const string Quat = "Assets/ExternalAssets/QuaterniusNatureMegaKit/FBX (Unity)/";

    // Layout constants -- must match CarryBuildForestGreybox.
    const float MidY = 0.70f;
    const float RiverZ0 = 10f, RiverZ1 = 134f;
    const float RiverHalfWidth = 7f;

    [MenuItem("Carry/Dress Forest Stage (Replace Greybox Visuals)")]
    public static void Run()
    {
        var log = new StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var root = GameObject.Find("ForestStage_Greybox");
            if (root == null) throw new Exception("ForestStage_Greybox root not found -- run Carry/Build Forest Greybox first.");

            var pathRoot = root.transform.Find("Path").gameObject;
            var recoveryRoot = root.transform.Find("RecoveryPoints").gameObject;
            var dressingRoot = root.transform.Find("Dressing").gameObject;

            var cliffRoot = dressingRoot.transform.Find("CliffWalls");
            if (cliffRoot == null)
            {
                var cliffGo = new GameObject("CliffWalls");
                cliffGo.transform.SetParent(dressingRoot.transform, false);
                cliffRoot = cliffGo.transform;
            }

            DressSteppingStones(pathRoot, log);
            DressRecoveryPoints(recoveryRoot, log);
            DressCliffWalls(cliffRoot.gameObject, log);

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

    // ---- Stepping stones: swap the grey cylinders for real stepping-stone rocks. ----
    static void DressSteppingStones(GameObject pathRoot, StringBuilder log)
    {
        var wide = AssetDatabase.LoadAssetAtPath<GameObject>(Quat + "RockPath_Round_Wide.fbx");
        var small = AssetDatabase.LoadAssetAtPath<GameObject>(Quat + "RockPath_Round_Small_1.fbx");
        if (wide == null || small == null) { log.AppendLine("RockPath prefabs missing"); return; }

        int replaced = 0;
        for (int i = 1; i <= 5; i++)
        {
            var stone = pathRoot.transform.Find("SteppingStone_" + i);
            if (stone == null) continue;
            Vector3 pos = stone.position;
            UnityEngine.Object.DestroyImmediate(stone.gameObject);

            var prefab = (i % 2 == 0) ? small : wide;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pathRoot.transform);
            inst.name = "SteppingStone_" + i;
            // Both RockPath meshes sit ~0.11 tall with their base near local Y=0.
            inst.transform.position = new Vector3(pos.x, MidY - 0.11f, pos.z);

            AddFittedBoxCollider(inst);
            replaced++;
        }
        log.AppendLine("Stepping stones replaced: " + replaced);
    }

    // ---- Recovery points: swap the brown cylinder for a log/stump prop with its own collider. ----
    static void DressRecoveryPoints(GameObject recoveryRoot, StringBuilder log)
    {
        string[] logVariants = { Kenney + "log_large.fbx", Kenney + "stump_round.fbx", Kenney + "log_stack.fbx" };
        int replaced = 0;
        for (int i = 1; i <= 7; i++)
        {
            var point = recoveryRoot.transform.Find("Recovery_" + i);
            if (point == null) continue;
            Vector3 pos = point.position;
            var rp = point.GetComponent<RecoveryPoint>();

            var oldRenderer = point.GetComponent<MeshRenderer>();
            var oldFilter = point.GetComponent<MeshFilter>();
            var oldCollider = point.GetComponent<Collider>();
            if (oldRenderer != null) UnityEngine.Object.DestroyImmediate(oldRenderer);
            if (oldFilter != null) UnityEngine.Object.DestroyImmediate(oldFilter);
            if (oldCollider != null) UnityEngine.Object.DestroyImmediate(oldCollider);

            var prefabPath = logVariants[(i - 1) % logVariants.Length];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { log.AppendLine(prefabPath + " missing"); continue; }

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, point);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0f, (i * 47) % 360, 0f);

            float topY = AddFittedBoxCollider(point.gameObject, visual);
            if (rp != null) rp.standOffset = new Vector3(0f, topY + 0.15f, 0f);

            replaced++;
        }
        log.AppendLine("Recovery points replaced: " + replaced);
    }

    // ---- Cliff walls: cheap decorative stacks flanking the river gorge (no collider needed). ----
    static void DressCliffWalls(GameObject cliffRoot, StringBuilder log)
    {
        var block = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "cliff_block_stone.fbx");
        if (block == null) { log.AppendLine("cliff_block_stone.fbx missing"); return; }

        int count = 0;
        for (float z = RiverZ0; z < RiverZ1; z += 4f)
        {
            foreach (float x in new[] { -RiverHalfWidth - 0.5f, RiverHalfWidth + 0.5f })
            {
                for (int h = 0; h < 2; h++)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(block, cliffRoot.transform);
                    inst.name = "Cliff_" + count;
                    inst.transform.position = new Vector3(x, -1f - h * 1f, z);
                    count++;
                }
            }
        }
        log.AppendLine("Cliff wall blocks placed: " + count);
    }

    // Adds a BoxCollider on `target` fitted to the render bounds of `target` (or an explicit
    // visual child), in target-local space. Returns the local-space top Y of the fitted bounds.
    static float AddFittedBoxCollider(GameObject target, GameObject visual = null)
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

        var box = target.AddComponent<BoxCollider>();
        box.center = localCenter;
        box.size = localSize;
        return localCenter.y + localSize.y * 0.5f;
    }
}
