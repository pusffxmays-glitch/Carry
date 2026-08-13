using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot: raycast from the same camera position/direction CarryTempLakeSurvey uses for the
// "R2_ring_ang090" shot, to identify exactly what GameObject/material is filling the frame (looks
// like unchanged brown terrain even after several retexture/relight passes -- need ground truth
// on what's actually being hit before guessing further). Temporary tooling.
public static class CarryTempRaycastWhat
{
    const float LakeCenterX = 0f, LakeCenterZ = -16f, LakeRadiusX = 24f, LakeRadiusZ = 20f;

    [MenuItem("Carry/Debug/Raycast What Is This (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        EditorSceneManager.OpenScene("Assets/Scenes/ForestStage_Realistic.unity", OpenSceneMode.Single);
        var terrain = Terrain.activeTerrain;

        float ang = 90f;
        float rad = ang * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        Vector2 standPt = new Vector2(LakeCenterX, LakeCenterZ) + dir * new Vector2(LakeRadiusX, LakeRadiusZ).magnitude * 0.62f;
        float gy = terrain.SampleHeight(new Vector3(standPt.x, 0, standPt.y)) + terrain.transform.position.y;
        Vector3 camPos = new Vector3(standPt.x, gy + 1.6f, standPt.y);
        Vector3 lookAt = new Vector3(LakeCenterX, gy + 1.6f, LakeCenterZ);
        Vector3 fwd = (lookAt - camPos).normalized;

        log.AppendLine("camPos=" + camPos + " fwd=" + fwd);

        // Cast several rays across roughly where the brown dome fills frame (upper-center area of
        // a 60deg-FOV shot looking slightly up from water level).
        var cam = new GameObject("__RC").AddComponent<Camera>();
        cam.transform.position = camPos;
        cam.transform.LookAt(lookAt);
        cam.fieldOfView = 60f;

        Vector2[] viewportPts = { new Vector2(0.5f, 0.55f), new Vector2(0.65f, 0.5f), new Vector2(0.75f, 0.4f), new Vector2(0.5f, 0.3f), new Vector2(0.35f, 0.5f) };
        foreach (var vp in viewportPts)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
            {
                var rend = hit.collider.GetComponent<Renderer>();
                string matName = rend != null && rend.sharedMaterial != null ? rend.sharedMaterial.name : "(no renderer/mat)";
                string shaderName = rend != null && rend.sharedMaterial != null ? rend.sharedMaterial.shader.name : "?";
                log.AppendLine("vp=" + vp + " -> hit " + FullPath(hit.collider.transform) + " dist=" + hit.distance + " mat=" + matName + " shader=" + shaderName);
            }
            else
            {
                log.AppendLine("vp=" + vp + " -> NO HIT");
            }
        }
        Object.DestroyImmediate(cam.gameObject);
        Debug.Log(log.ToString());
    }

    static string FullPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
