using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot diagnostic: find a real AncientFir tree instance (Terrain TreeInstance with
// prototypeIndex >= 3) near the lake and shoot it point-blank to settle whether it's a
// placement bug (tree never actually placed/visible) or just bad luck with camera framing in
// the regular ring/tangent survey. Temporary tooling, not part of the build pipeline.
public static class CarryTempFirCheck
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    [MenuItem("Carry/Debug/Check Real Fir Rendering (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = Terrain.activeTerrain;
            var data = terrain.terrainData;
            var protos = data.treePrototypes;
            for (int i = 0; i < protos.Length; i++)
                log.AppendLine("proto " + i + " = " + (protos[i].prefab != null ? protos[i].prefab.name : "NULL"));

            var instances = data.treeInstances;
            log.AppendLine("total tree instances: " + instances.Length);

            int found = 0;
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ScreenshotsOut", "FirCheck");
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("__FirCheckCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;
            for (int i = 0; i < 6; i++) WarmupRender(cam); // discard warmup frames

            foreach (var ti in instances)
            {
                if (ti.prototypeIndex < 3) continue; // only real fir prototypes (3,4,5)
                float wx = terrain.transform.position.x + ti.position.x * data.size.x;
                float wz = terrain.transform.position.z + ti.position.z * data.size.z;
                // Only ones actually near the lake this time (lake center approx (0,-16), radius ~24/20) -- the
                // first pass happened to grab outer-terrain-edge-triggered instances instead (array order).
                float distToLakeCenter = Mathf.Sqrt(wx * wx + (wz + 16f) * (wz + 16f));
                if (distToLakeCenter > 40f) continue;
                float wy = terrain.SampleHeight(new Vector3(wx, 0, wz)) + terrain.transform.position.y;
                log.AppendLine("Real fir instance at world (" + wx.ToString("F1") + ", " + wy.ToString("F1") + ", " + wz.ToString("F1") + ") proto=" + ti.prototypeIndex + " widthScale=" + ti.widthScale + " heightScale=" + ti.heightScale);

                // Shoot it from 8m away at eye height, looking directly at trunk base+mid height.
                Vector3 lookAt = new Vector3(wx, wy + 3f, wz);
                Vector3 camPos = new Vector3(wx + 8f, wy + 1.8f, wz);
                cam.transform.position = camPos;
                cam.transform.LookAt(lookAt);
                Capture(cam, Path.Combine(outDir, "fircheck_" + found + ".png"));
                found++;
                if (found >= 5) break; // a handful is enough to settle the question
            }
            log.AppendLine("Captured " + found + " close-up shots of real fir instances.");
            UnityEngine.Object.DestroyImmediate(camGo);
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    static void WarmupRender(Camera cam)
    {
        int w = 640, h = 360;
        var rt = new RenderTexture(w, h, 24);
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
