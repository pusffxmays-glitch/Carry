using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot: dump terrain material template + alphamap weights at the exact world point the R2
// ring screenshot's brown dome sits at, to find out definitively why 4 rebuild+retint passes
// produced zero visible pixel change there. Temporary tooling.
public static class CarryTempAlphaDiag
{
    [MenuItem("Carry/Debug/Alpha Diag (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        EditorSceneManager.OpenScene("Assets/Scenes/ForestStage_Realistic.unity", OpenSceneMode.Single);
        var terrain = Terrain.activeTerrain;
        var data = terrain.terrainData;

        log.AppendLine("materialTemplate = " + (terrain.materialTemplate != null ? terrain.materialTemplate.name + " shader=" + terrain.materialTemplate.shader.name : "NULL (auto)"));
        log.AppendLine("materialType = " + terrain.materialType);
        log.AppendLine("drawInstanced = " + terrain.drawInstanced);
        log.AppendLine("layers:");
        for (int i = 0; i < data.terrainLayers.Length; i++)
        {
            var l = data.terrainLayers[i];
            log.AppendLine("  [" + i + "] " + l.name + " diffuseTexture=" + (l.diffuseTexture != null ? AssetDatabase.GetAssetPath(l.diffuseTexture) : "NULL"));
        }

        // Reuse the EXACT same camera ray as the R2_ring_ang090 screenshot / raycast diag (vp
        // 0.5,0.55 was the one that hit at distance 46 -- almost certainly the far dome, not the
        // near shore) to get the real world hit point instead of guessing.
        const float LakeCenterX = 0f, LakeCenterZ = -16f, LakeRadiusX = 24f, LakeRadiusZ = 20f;
        float ang = 90f;
        float rad = ang * Mathf.Deg2Rad;
        Vector2 dir2 = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        Vector2 standPt = new Vector2(LakeCenterX, LakeCenterZ) + dir2 * new Vector2(LakeRadiusX, LakeRadiusZ).magnitude * 0.62f;
        float gy = terrain.SampleHeight(new Vector3(standPt.x, 0, standPt.y)) + terrain.transform.position.y;
        Vector3 camPos = new Vector3(standPt.x, gy + 1.6f, standPt.y);
        var rcCam = new GameObject("__RC2").AddComponent<Camera>();
        rcCam.transform.position = camPos;
        rcCam.transform.LookAt(new Vector3(LakeCenterX, gy + 1.6f, LakeCenterZ));
        rcCam.fieldOfView = 60f;
        var worldPts = new System.Collections.Generic.List<Vector3>();
        foreach (var vp in new[] { new Vector2(0.5f, 0.55f), new Vector2(0.65f, 0.5f), new Vector2(0.75f, 0.4f) })
        {
            Ray ray = rcCam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, 200f)) worldPts.Add(hit.point);
        }
        Object.DestroyImmediate(rcCam.gameObject);

        foreach (var wp in worldPts)
        {
            Vector3 local = wp - terrain.transform.position;
            float normX = local.x / data.size.x;
            float normZ = local.z / data.size.z;
            int ax = Mathf.Clamp(Mathf.RoundToInt(normX * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
            int az = Mathf.Clamp(Mathf.RoundToInt(normZ * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
            var alphas = data.GetAlphamaps(ax, az, 1, 1);
            string w = "";
            for (int i = 0; i < data.terrainLayers.Length; i++) w += data.terrainLayers[i].name + "=" + alphas[0, 0, i].ToString("F3") + " ";
            float steepness = data.GetSteepness(normX, normZ);
            float height = data.GetInterpolatedHeight(normX, normZ);
            log.AppendLine("world=" + wp + " norm=(" + normX.ToString("F3") + "," + normZ.ToString("F3") + ") steep=" + steepness.ToString("F1") + " height=" + height.ToString("F2") + " -> " + w);
        }

        Debug.Log(log.ToString());
    }
}
