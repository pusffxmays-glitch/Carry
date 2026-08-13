using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// QA helper: renders a handful of fixed camera angles over ForestStage_Greybox
// to PNG files so the stage can be sanity-checked from outside the Editor
// (batchmode has no viewport). Scene changes are never saved.
public static class CarryCaptureStageScreenshots
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Greybox.unity";

    [MenuItem("Carry/Debug/Capture Forest Stage Screenshots")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(projectRoot, "ScreenshotsOut");
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("__ScreenshotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.7f, 0.85f);

            // First few Camera.Render() calls in a freshly opened batchmode scene can land before
            // shader/texture streaming finishes (flat/wrong-colored meshes); warm up several times
            // across a couple of vantage points before trusting any capture.
            for (int i = 0; i < 6; i++)
            {
                Capture(cam, new Vector3(-6f, 3f, -4f), new Vector3(0f, 1f, 12f), Path.Combine(outDir, "00_warmup_discard.png"));
                Capture(cam, new Vector3(6f, 3.5f, 40f), new Vector3(0f, 0.7f, 55f), Path.Combine(outDir, "00_warmup_discard.png"));
            }
            Capture(cam, new Vector3(-6f, 3f, -4f), new Vector3(0f, 1f, 12f), Path.Combine(outDir, "01_start.png"));
            Capture(cam, new Vector3(-6f, 2.5f, 24f), new Vector3(0f, 0.4f, 33f), Path.Combine(outDir, "02b_rocksteps.png"));
            Capture(cam, new Vector3(6f, 3.5f, 40f), new Vector3(0f, 0.7f, 55f), Path.Combine(outDir, "02_bridge_approach.png"));
            Capture(cam, new Vector3(4f, 2f, 65f), new Vector3(0f, 0.7f, 68f), Path.Combine(outDir, "03_bridge_onbridge.png"));
            Capture(cam, new Vector3(-6f, 4f, 90f), new Vector3(0f, 0.7f, 95f), Path.Combine(outDir, "04_restarea.png"));
            Capture(cam, new Vector3(6f, 4f, 98f), new Vector3(0f, 0.7f, 108f), Path.Combine(outDir, "05_steppingstones.png"));
            Capture(cam, new Vector3(-8f, 5f, 128f), new Vector3(0f, 1f, 137f), Path.Combine(outDir, "06_gate.png"));
            Capture(cam, new Vector3(-2f, -1.5f, 50f), new Vector3(0f, -3f, 44f), Path.Combine(outDir, "07_river_from_below.png"));

            UnityEngine.Object.DestroyImmediate(camGo);
            log.AppendLine("Screenshots written to " + outDir);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    static void Capture(Camera cam, Vector3 pos, Vector3 lookAt, string outPath)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(lookAt);

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
