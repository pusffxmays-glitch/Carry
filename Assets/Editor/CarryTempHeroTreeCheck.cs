using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot: shoot each new hero leaning tree's angle from a safe water-level standoff point well
// inside the lake shore (same proven-safe convention as CarryTempZoneCloseup, avoiding the
// repeated "camera embedded in geometry" bug from earlier tree-relative camera placement
// attempts), looking outward/up at the shore to see whether the tree reads as leaning out over
// the water. Temporary tooling.
public static class CarryTempHeroTreeCheck
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float LakeCenterX = 0f, LakeCenterZ = -16f, LakeWaterY = -4.4f;
    static readonly float[] Angles = { 130f, 195f, 245f, 285f };

    [MenuItem("Carry/Debug/Hero Tree Check (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ScreenshotsOut", "HeroTreeCheck");
        Directory.CreateDirectory(outDir);

        var camGo = new GameObject("__HeroTreeCam");
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
            float standR = 6f;
            Vector3 campos = new Vector3(LakeCenterX + dir.x * standR, LakeWaterY + 3f, LakeCenterZ + dir.y * standR);
            Vector3 lookAt = new Vector3(LakeCenterX + dir.x * 40f, LakeWaterY + 20f, LakeCenterZ + dir.y * 40f);
            cam.transform.position = campos;
            cam.transform.LookAt(lookAt);
            Capture(cam, Path.Combine(outDir, "hero_" + ang.ToString("F0") + ".png"));
            log.AppendLine("angle " + ang + " campos=" + campos);
        }
        Object.DestroyImmediate(camGo);
        log.AppendLine("SUCCESS");
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
