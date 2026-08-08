using UnityEditor;
using UnityEngine;

public static class CarryFixClipLooping
{
    private const string GoblinFbxPath = "Assets/Goblin/Grimfang_Goblin.fbx";

    [MenuItem("Carry/Fix Animation Looping")]
    public static void Run()
    {
        var importer = AssetImporter.GetAtPath(GoblinFbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("Goblin ModelImporter not found.");
            return;
        }

        var clips = importer.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].loopTime = true;
            clips[i].loopPose = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Enabled loopTime/loopPose on " + clips.Length + " goblin animation clips.");
    }
}
