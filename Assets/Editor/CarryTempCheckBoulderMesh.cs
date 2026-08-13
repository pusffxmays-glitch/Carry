using UnityEditor;
using UnityEngine;

// One-shot: dump every MeshFilter in boulder_01's prefab hierarchy (and a couple of the individual
// moss rocks) to find why GetComponentInChildren<MeshFilter>() returns near-zero bounds for some
// of them. Temporary tooling.
public static class CarryTempCheckBoulderMesh
{
    [MenuItem("Carry/Debug/Check Boulder Mesh Structure (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        foreach (var path in new[] {
            "Assets/ExternalAssets/PolyHaven/boulder_01/boulder_01_2k.fbx",
            "Assets/ExternalAssets/PolyHaven/RockMossIndividual/rock_moss_set_01_rock01.fbx",
        })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            log.AppendLine("==== " + path + " ====");
            if (prefab == null) { log.AppendLine("  NOT FOUND"); continue; }
            log.AppendLine("  root name=" + prefab.name);
            var mfs = prefab.GetComponentsInChildren<MeshFilter>(true);
            log.AppendLine("  MeshFilter count: " + mfs.Length);
            foreach (var mf in mfs)
            {
                string meshName = mf.sharedMesh != null ? mf.sharedMesh.name : "NULL";
                Bounds b = mf.sharedMesh != null ? mf.sharedMesh.bounds : default;
                log.AppendLine("    obj='" + mf.gameObject.name + "' localPos=" + mf.transform.localPosition + " localScale=" + mf.transform.localScale +
                    " mesh='" + meshName + "' meshBounds min=" + b.min + " max=" + b.max + " vertCount=" + (mf.sharedMesh != null ? mf.sharedMesh.vertexCount : -1));
            }
            var firstMf = prefab.GetComponentInChildren<MeshFilter>();
            log.AppendLine("  GetComponentInChildren<MeshFilter>() picks: '" + (firstMf != null ? firstMf.gameObject.name : "NULL") + "'");
        }
        Debug.Log(log.ToString());
    }
}
