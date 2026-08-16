using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot: deep-dive diagnostic for 3 specifically user-reported floating objects
// (CliffBoulder_3, CliffBoulder_5, LakeShore_19) -- dumps transform, renderer bounds, local mesh
// bounds, and a fresh downward raycast against Terrain to see the real gap and, for the
// CliffBoulder ones, compares against what the OLD SampleWorldHeightConservative-based approach
// would have produced. Read-only. Temporary tooling.
public static class CarryTempSpecificFloatCheck
{
    [MenuItem("Carry/Debug/Specific Float Check (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        EditorSceneManager.OpenScene("Assets/Scenes/ForestStage_Realistic.unity", OpenSceneMode.Single);
        var terrain = Terrain.activeTerrain;
        var all = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);

        foreach (var name in new[] { "CliffBoulder_3", "CliffBoulder_5", "LakeShore_19" })
        {
            var t = all.FirstOrDefault(x => x.name == name);
            if (t == null) { log.AppendLine(name + ": NOT FOUND"); continue; }
            log.AppendLine("==== " + name + " ====");
            log.AppendLine("  worldPos=" + t.position + " worldRot(euler)=" + t.rotation.eulerAngles + " localScale=" + t.localScale);

            var mf = t.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                log.AppendLine("  local mesh bounds: min=" + mf.sharedMesh.bounds.min + " max=" + mf.sharedMesh.bounds.max);

            var rend = t.GetComponentInChildren<Renderer>();
            if (rend != null)
                log.AppendLine("  world renderer bounds: min=" + rend.bounds.min + " max=" + rend.bounds.max + " center=" + rend.bounds.center);

            // Fresh straight-down raycast from above the object's position
            var origin = new Vector3(t.position.x, terrain.transform.position.y + terrain.terrainData.size.y + 20f, t.position.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 500f))
                log.AppendLine("  straight-down raycast from pos.xz: hit=" + hit.point + " normal=" + hit.normal + " collider=" + hit.collider.name);
            else
                log.AppendLine("  straight-down raycast from pos.xz: NO HIT");

            // What SampleWorldHeightConservative-equivalent would give (ring sample, radius=1.5)
            float best = terrain.SampleHeight(new Vector3(t.position.x, 0, t.position.z)) + terrain.transform.position.y;
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                float x = t.position.x + Mathf.Cos(a) * 1.5f;
                float z = t.position.z + Mathf.Sin(a) * 1.5f;
                float h = terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;
                if (h > best) best = h;
            }
            log.AppendLine("  SampleWorldHeightConservative(radius=1.5) equivalent: " + best);

            // Terrain steepness at this normalized position
            float normX = (t.position.x - terrain.transform.position.x) / terrain.terrainData.size.x;
            float normZ = (t.position.z - terrain.transform.position.z) / terrain.terrainData.size.z;
            log.AppendLine("  terrain steepness here: " + terrain.terrainData.GetSteepness(normX, normZ) + " deg");
        }

        Debug.Log(log.ToString());
    }
}
