using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot: clean shots of each Waterfall_* from a safe standoff distance computed from its own
// world-space bounds (avoids the fixed-radius survey camera ending up embedded in the new,
// larger flanking rock geometry). Temporary tooling.
public static class CarryTempWaterfallCheck
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    [MenuItem("Carry/Debug/Capture Waterfall Shots (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ScreenshotsOut", "WaterfallCheck");
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("__WFCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;
            for (int i = 0; i < 6; i++) Warmup(cam);

            var wfRoot = GameObject.Find("Waterfalls");
            var terrain = Terrain.activeTerrain;
            int found = 0;
            for (int idx = 0; idx < 5; idx++)
            {
                var wf = wfRoot.transform.Find("Waterfall_" + idx);
                if (wf == null) continue;
                var mr = wf.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                var b = mr.bounds;
                // Also encapsulate the flanking/source/base rocks so the standoff distance clears
                // the whole crevice group, not just the thin water mesh.
                foreach (var name in new[] { "WaterfallFlankRock_" + idx + "_-1", "WaterfallFlankRock_" + idx + "_1", "WaterfallSourceRock_" + idx })
                {
                    var t = wfRoot.transform.Find(name);
                    if (t != null)
                    {
                        var r = t.GetComponent<Renderer>();
                        if (r != null) b.Encapsulate(r.bounds);
                    }
                }

                Vector3 lakeCenter = new Vector3(0f, b.center.y, -16f);
                Vector3 outward = (b.center - lakeCenter); outward.y = 0f; outward.Normalize();
                float standoff = Mathf.Max(b.size.x, b.size.z) * 1.6f + 6f;
                Vector3 camPos = b.center - outward * standoff + Vector3.up * (b.size.y * 0.1f);
                // make sure the camera itself is above ground
                float groundY = terrain.SampleHeight(camPos) + terrain.transform.position.y;
                camPos.y = Mathf.Max(camPos.y, groundY + 1.6f);
                cam.transform.position = camPos;
                cam.transform.LookAt(b.center);
                Capture(cam, Path.Combine(outDir, "waterfall_" + idx + ".png"));
                log.AppendLine("Waterfall_" + idx + " bounds center=" + b.center + " size=" + b.size + " camPos=" + camPos);
                found++;
            }
            log.AppendLine("Captured " + found + " waterfall shots.");
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
