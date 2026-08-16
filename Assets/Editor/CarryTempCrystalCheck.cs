using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot: verify each placed AzureCrystal instance -- camera stands inside the lake at water
// level (proven-safe standoff pattern from CarryTempZoneCloseup) looking outward at each crystal's
// actual position, plus one shot down through the water at the lakebed cluster. Also dumps each
// instance's transform + bounds vs the terrain surface for a numeric embed check. Temporary tooling.
public static class CarryTempCrystalCheck
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float LakeCenterX = 0f, LakeCenterZ = -16f, LakeWaterY = -4.4f;

    [MenuItem("Carry/Debug/Crystal Check (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var terrain = Terrain.activeTerrain;
        string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ScreenshotsOut", "CrystalCheck");
        Directory.CreateDirectory(outDir);

        var crystalRoot = GameObject.Find("AzureCrystals");
        if (crystalRoot == null) { Debug.Log("AzureCrystals root not found!"); return; }

        var camGo = new GameObject("__CrystalCam");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        cam.clearFlags = CameraClearFlags.Skybox;
        for (int i = 0; i < 6; i++) Warmup(cam);

        foreach (Transform t in crystalRoot.transform)
        {
            var rend = t.GetComponentInChildren<Renderer>();
            if (rend == null) continue;
            Vector3 target = rend.bounds.center;

            // Numeric embed check: how much of the model's bounds is above the terrain surface?
            float groundAtCenter = terrain.SampleHeight(new Vector3(target.x, 0, target.z)) + terrain.transform.position.y;
            log.AppendLine(t.name + ": pos=" + t.position + " boundsCenter=" + target + " boundsMin=" + rend.bounds.min + " boundsMax=" + rend.bounds.max + " groundAtCenter=" + groundAtCenter.ToString("F2"));

            // Camera: stand toward the lake center from the crystal, at a comfortable standoff,
            // slightly above the water, looking at the crystal.
            Vector2 toCenter = new Vector2(LakeCenterX - target.x, LakeCenterZ - target.z);
            float dist = toCenter.magnitude;
            Vector2 dir = toCenter.normalized;
            float standoff = Mathf.Min(12f, dist * 0.6f);
            Vector3 camPos = new Vector3(target.x + dir.x * standoff, Mathf.Max(LakeWaterY + 1.2f, target.y + 1.5f), target.z + dir.y * standoff);
            cam.transform.position = camPos;
            cam.transform.LookAt(target);
            Capture(cam, Path.Combine(outDir, t.name + ".png"));
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
