using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// TEMPORARY investigation tool -- supplements CarryTempLakeSurvey's 45-degree ring with shots at
// the exact angles needed to verify the new hero rock formations (110/210/305deg) and the 5
// waterfall angles (165/195/225/255/300deg), which the 45-degree-increment ring never lands on.
// Does NOT modify or save the scene.
public static class CarryTempAngleCheck
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float LakeCenterX = 0f;
    const float LakeCenterZ = -16f;
    const float LakeRadiusX = 24f;
    const float LakeRadiusZ = 20f;

    [MenuItem("Carry/Debug/Survey Lake Angle Check (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = Terrain.activeTerrain;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(projectRoot, "ScreenshotsOut", "LakeSurvey");
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("__AngleCheckCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.Skybox;

            for (int i = 0; i < 6; i++)
                Capture(cam, terrain, 40f, SampleY(terrain, 40f, -16f) + 8f, -16f, LakeCenterX, LakeCenterZ, "00_warmup2_discard", outDir);

            float[] heroAngles = { 110f, 210f, 305f };
            foreach (var ang in heroAngles)
            {
                float rad = ang * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                Vector2 farStand = new Vector2(LakeCenterX, LakeCenterZ) + dir * new Vector2(LakeRadiusX, LakeRadiusZ).magnitude * 0.62f;
                float gyFar = SampleY(terrain, farStand.x, farStand.y);
                Capture(cam, terrain, farStand.x, gyFar + 1.6f, farStand.y, LakeCenterX, LakeCenterZ, "HERO_ang" + (int)ang + "_far", outDir);

                Vector2 nearStand = new Vector2(LakeCenterX, LakeCenterZ) + dir * new Vector2(LakeRadiusX, LakeRadiusZ).magnitude * 0.72f;
                float gyNear = SampleY(terrain, nearStand.x, nearStand.y);
                Vector2 lookPt = new Vector2(LakeCenterX, LakeCenterZ) + dir * new Vector2(LakeRadiusX, LakeRadiusZ).magnitude * 0.5f;
                Capture(cam, terrain, nearStand.x, gyNear + 1.5f, nearStand.y, lookPt.x, lookPt.y, "HERO_ang" + (int)ang + "_near", outDir);
            }

            float[] fallAngles = { 165f, 195f, 225f, 255f, 300f };
            foreach (var ang in fallAngles)
            {
                float rad = ang * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                Vector2 standPt = new Vector2(LakeCenterX, LakeCenterZ) + dir * new Vector2(LakeRadiusX, LakeRadiusZ).magnitude * 0.6f;
                float gy = SampleY(terrain, standPt.x, standPt.y);
                Capture(cam, terrain, standPt.x, gy + 1.6f, standPt.y, LakeCenterX, LakeCenterZ, "FALL_ang" + (int)ang, outDir);
            }

            UnityEngine.Object.DestroyImmediate(camGo);
            log.AppendLine("Angle-check screenshots written to " + outDir);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    static float SampleY(Terrain terrain, float x, float z) =>
        terrain != null ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y : 0f;

    static void Capture(Camera cam, Terrain terrain, float x, float y, float z, float lookX, float lookZ, string name, string outDir)
    {
        float lookY = SampleY(terrain, lookX, lookZ) + 1.3f;
        cam.transform.position = new Vector3(x, y, z);
        cam.transform.LookAt(new Vector3(lookX, lookY, lookZ));
        RenderTo(cam, Path.Combine(outDir, name + ".png"));
    }

    static void RenderTo(Camera cam, string outPath)
    {
        int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
