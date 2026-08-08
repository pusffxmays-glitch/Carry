using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CarryRebuildRoom
{
    private const string ScenePath = "Assets/Scenes/CastleStage.unity";
    private const string StageFbxPath = "Assets/Stage/Stage.fbx";

    [MenuItem("Carry/Rebuild Room In Scene")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var oldStage = GameObject.Find("Stage");
            if (oldStage != null) Object.DestroyImmediate(oldStage);

            var stagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StageFbxPath);
            if (stagePrefab == null) throw new System.Exception("Stage FBX not found at " + StageFbxPath);

            var stageInstance = (GameObject)PrefabUtility.InstantiatePrefab(stagePrefab, scene);
            stageInstance.name = "Stage";
            stageInstance.transform.position = Vector3.zero;

            int colliderCount = 0;
            foreach (var mf in stageInstance.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.GetComponent<Collider>() == null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    colliderCount++;
                }
            }
            log.AppendLine("New Stage instantiated with " + colliderCount + " MeshColliders.");

            var goblin = GameObject.Find("Goblin");
            if (goblin != null)
            {
                goblin.transform.position = new Vector3(0f, 0.1f, 0f);
                goblin.transform.rotation = Quaternion.identity;
                log.AppendLine("Goblin repositioned to room center.");
            }

            var mainCam = GameObject.Find("Main Camera");
            if (mainCam != null && goblin != null)
            {
                var rig = mainCam.GetComponent<CarryCameraRig>();
                if (rig != null) rig.target = goblin.transform;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
