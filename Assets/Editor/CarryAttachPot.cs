using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CarryAttachPot
{
    private const string ScenePath = "Assets/Scenes/CastleStage.unity";
    private const string PotFbxPath = "Assets/Pot/Carry_Pot.fbx";

    [MenuItem("Carry/Attach Pot To Goblin")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var goblin = GameObject.Find("Goblin");
            if (goblin == null) throw new System.Exception("Goblin not found in scene.");

            Transform spine02 = null;
            foreach (var t in goblin.GetComponentsInChildren<Transform>())
            {
                if (t.name == "Spine02") { spine02 = t; break; }
            }
            if (spine02 == null) throw new System.Exception("Spine02 bone not found.");

            // remove any previous attempt
            var existingSocket = spine02.Find("PotSocket");
            if (existingSocket != null) Object.DestroyImmediate(existingSocket.gameObject);

            var socket = new GameObject("PotSocket");
            socket.transform.SetParent(spine02, false);
            // Cradled against the chest: base roughly between hip and chest height, slightly
            // in front of the body (character faces +Z at identity rotation), tilted back a
            // little so it reads as hugged in the arms rather than floating upright.
            socket.transform.position = new Vector3(spine02.position.x, 0.92f, spine02.position.z + 0.14f);
            socket.transform.rotation = Quaternion.Euler(-12f, 0f, 0f);

            var potPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PotFbxPath);
            if (potPrefab == null) throw new System.Exception("Pot FBX not found at " + PotFbxPath);

            var potInstance = (GameObject)PrefabUtility.InstantiatePrefab(potPrefab, scene);
            potInstance.name = "Carry_Pot";
            potInstance.transform.SetParent(socket.transform, false);
            potInstance.transform.localPosition = Vector3.zero;
            potInstance.transform.localRotation = Quaternion.identity;
            potInstance.transform.localScale = Vector3.one;

            log.AppendLine("Attached Carry_Pot to Goblin/Spine02/PotSocket.");
            log.AppendLine("Socket world pos=" + socket.transform.position);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
