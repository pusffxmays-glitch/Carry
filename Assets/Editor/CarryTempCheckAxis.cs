using UnityEditor;
using UnityEngine;

// One-shot: verify a freshly re-exported FBX imports into Unity with a clean identity transform
// (no baked axis-correction rotation), after the Blender export pipeline was changed to
// bake_space_transform=True. Temporary tooling.
public static class CarryTempCheckAxis
{
    [MenuItem("Carry/Debug/Check Axis Fix (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        string[] paths =
        {
            "Assets/ExternalAssets/PolyHaven/rock_face_01/rock_face_01_decimated.fbx",
            "Assets/ExternalAssets/PolyHaven/rock_face_02/rock_face_02_decimated.fbx",
            "Assets/ExternalAssets/PolyHaven/coastal_cliff_01/coastal_cliff_01_decimated.fbx",
            "Assets/ExternalAssets/PolyHaven/island_tree_01/island_tree_01_decimated.fbx",
        };
        foreach (var path in paths)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { log.AppendLine(path + " => NOT FOUND"); continue; }
            log.AppendLine("=== " + path + " ===");
            void Dump(Transform t, string indent)
            {
                log.AppendLine(indent + t.name + " localPos=" + t.localPosition + " localRot=" + t.localRotation.eulerAngles + " localScale=" + t.localScale);
                foreach (Transform c in t) Dump(c, indent + "  ");
            }
            Dump(prefab.transform, "");
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            if (mf != null) log.AppendLine("  mesh.bounds = " + mf.sharedMesh.bounds);
        }
        Debug.Log(log.ToString());
    }
}
