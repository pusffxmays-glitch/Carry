using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot: scan every Renderer in the lake area for materials with no BaseMap texture (the
// "white rock" symptom), and report exactly which GameObject + material + shader so the real
// cause can be identified rather than guessed from a screenshot. Temporary tooling.
public static class CarryTempFindWhiteMats
{
    [MenuItem("Carry/Debug/Find White Materials (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        EditorSceneManager.OpenScene("Assets/Scenes/ForestStage_Realistic.unity", OpenSceneMode.Single);
        var root = GameObject.Find("ForestStage_Terrain");
        int total = 0, whiteCount = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            total++;
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null) { log.AppendLine("NULL MATERIAL on " + FullPath(r.transform)); whiteCount++; continue; }
                bool hasBase = mat.HasProperty("_BaseMap");
                var tex = hasBase ? mat.GetTexture("_BaseMap") : null;
                bool looksWhite = tex == null && (!mat.HasProperty("_BaseColor") || IsNearWhite(mat.GetColor("_BaseColor"))) && (mat.HasProperty("_BaseColor") || IsNearWhite(mat.color));
                if (tex == null)
                {
                    whiteCount++;
                    log.AppendLine("NO TEXTURE: obj=" + FullPath(r.transform) + " mat=" + mat.name + " shader=" + mat.shader.name + " color=" + (mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString() : mat.color.ToString()));
                }
            }
        }
        log.AppendLine("Total renderers: " + total + "  flagged (no _BaseMap texture): " + whiteCount);
        Debug.Log(log.ToString());
    }

    static bool IsNearWhite(Color c) => c.r > 0.85f && c.g > 0.85f && c.b > 0.85f;

    static string FullPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
