using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot: clean, unobstructed frontal shots of every HeroCliffFace/HeroCoastRocks instance,
// positioned well back from the object's own bounds (avoids the camera-embedded-in-geometry
// issue other close-up survey shots hit) to visually confirm upright orientation with certainty.
// Temporary tooling.
public static class CarryTempHeroFrontal
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    [MenuItem("Carry/Debug/Capture Hero Frontal Shots (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ScreenshotsOut", "HeroFrontal");
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("__HeroFrontalCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;
            for (int i = 0; i < 6; i++) Warmup(cam);

            var wallRoot = GameObject.Find("LakeCliffWall");
            int found = 0;
            foreach (Transform child in wallRoot.transform)
            {
                if (!child.name.StartsWith("HeroCliffFace") && !child.name.StartsWith("HeroCoastRocks")) continue;
                var rends = child.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) continue;
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);

                log.AppendLine(child.name + " world bounds center=" + b.center + " size=" + b.size);

                // Stand back a safe multiple of the object's own size, at roughly its mid-height,
                // looking at its center -- far enough to never be inside its collider/mesh.
                float backDist = Mathf.Max(b.size.x, b.size.z) * 1.8f + 5f;
                Vector3 lookAt = b.center;
                // Approach from the lake-center side (the "front" it was rotated to face), i.e. from
                // outside the bounds toward the lake center direction reversed -- simplest robust
                // choice: stand back along the object's own local -forward (which was set to face
                // the lake), so we're looking at the same face a player at the lake would see.
                Vector3 campos = child.position - child.forward * backDist + Vector3.up * (b.size.y * 0.15f);
                cam.transform.position = campos;
                cam.transform.LookAt(lookAt);
                Capture(cam, Path.Combine(outDir, "frontal_" + child.name + ".png"));
                found++;

                // Also a side view (90 deg around) to catch any sideways/lying-down orientation the
                // front view alone might not reveal.
                Vector3 sideDir = Quaternion.Euler(0, 90, 0) * child.forward;
                Vector3 campos2 = b.center - sideDir * backDist + Vector3.up * (b.size.y * 0.15f);
                cam.transform.position = campos2;
                cam.transform.LookAt(lookAt);
                Capture(cam, Path.Combine(outDir, "side_" + child.name + ".png"));
            }
            log.AppendLine("Captured frontal+side shots for " + found + " hero formations.");
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
