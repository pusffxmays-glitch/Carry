using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot: shoot a specific lake angle from a safe, water-level standoff point (well inside the
// shore radius, guaranteed clear of wall/rock geometry) looking outward/up at the shore -- mirrors
// what a player near the water would actually see. Menu takes no params; edit ANGLES below.
// Temporary tooling.
public static class CarryTempZoneCloseup
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float LakeCenterX = 0f, LakeCenterZ = -16f, LakeWaterY = -4.4f;
    static readonly float[] Angles = { 305f, 295f, 315f };

    [MenuItem("Carry/Debug/Capture Zone Closeup (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ScreenshotsOut", "ZoneCloseup");
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("__ZoneCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;
            for (int i = 0; i < 6; i++) Warmup(cam);

            foreach (var ang in Angles)
            {
                float rad = ang * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                // Stand at 0.8x the lake's nominal radius (well inside the shore -- always open
                // water there, guaranteed clear of any shore/wall/rock geometry) at water level,
                // looking outward and slightly up at the shore/wall/formation.
                float standR = 16f;
                Vector3 campos = new Vector3(LakeCenterX + dir.x * standR, LakeWaterY + 1.5f, LakeCenterZ + dir.y * standR);
                Vector3 lookAt = new Vector3(LakeCenterX + dir.x * 30f, LakeWaterY + 8f, LakeCenterZ + dir.y * 30f);
                cam.transform.position = campos;
                cam.transform.LookAt(lookAt);
                Capture(cam, Path.Combine(outDir, "zone_" + ang.ToString("F0") + ".png"));
                log.AppendLine("angle " + ang + " campos=" + campos);
            }
            UnityEngine.Object.DestroyImmediate(camGo);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    static void Warmup(Camera cam)
    {
        var rt = new RenderTexture(640, 360, 24);
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
    }

    static void Capture(Camera cam, string outPath)
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
