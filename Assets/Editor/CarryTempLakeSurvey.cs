using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// TEMPORARY investigation tool (not part of the build pipeline) -- captures a full ring of
// shots around the lake plus close-ups on specific known trouble spots (CliffBoulder / logs /
// LakeCliffLowerMossy / bridge-to-land seams), for a one-off Environment Art audit. Does NOT
// modify or save the scene. Lake constants below are copied from CarryBuildTerrainForest.cs
// (private consts there) -- keep in sync if that file's lake geometry changes.
public static class CarryTempLakeSurvey
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    const float LakeCenterX = 0f;
    const float LakeCenterZ = -16f;
    const float LakeRadiusX = 24f;
    const float LakeRadiusZ = 20f;
    const float BridgeCenterZ = 5f;

    [MenuItem("Carry/Debug/Survey Lake Area (temp)")]
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

            var camGo = new GameObject("__SurveyCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.Skybox;

            // Warmup renders (discarded) so shaders/textures are fully loaded before real shots.
            for (int i = 0; i < 6; i++)
                Capture(cam, terrain, 40f, SampleY(terrain, 40f, -16f) + 8f, -16f, LakeCenterX, LakeCenterZ, "00_warmup_discard", outDir);

            // ---- Ring around the lake shoreline, standing near the rim, looking inward at the water.
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f;
                float rad = ang * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                // Stand a bit outside the nominal radius (approximate shore, not exact FindShoreAtAngle)
                Vector2 standPt = new Vector2(LakeCenterX, LakeCenterZ) + dir * new Vector2(LakeRadiusX, LakeRadiusZ).magnitude * 0.62f;
                float gy = SampleY(terrain, standPt.x, standPt.y);
                string name = string.Format("R{0}_ring_ang{1:000}_lookin", i, (int)ang);
                Capture(cam, terrain, standPt.x, gy + 1.6f, standPt.y, LakeCenterX, LakeCenterZ, name, outDir);
            }

            // ---- Ring standing just outside the shore looking ALONG the shoreline (tangentially),
            // to see how the shore contour reads in profile.
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f;
                float rad = ang * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                Vector2 tangent = new Vector2(-dir.y, dir.x);
                Vector2 standPt = new Vector2(LakeCenterX, LakeCenterZ) + dir * new Vector2(LakeRadiusX, LakeRadiusZ).magnitude * 0.62f;
                float gy = SampleY(terrain, standPt.x, standPt.y);
                Vector2 lookPt = standPt + tangent * 15f;
                string name = string.Format("T{0}_tangent_ang{1:000}", i, (int)ang);
                Capture(cam, terrain, standPt.x, gy + 1.6f, standPt.y, lookPt.x, lookPt.y, name, outDir);
            }

            // ---- Top-down overview of the whole lake.
            {
                float topY = SampleY(terrain, LakeCenterX, LakeCenterZ) + 55f;
                cam.transform.position = new Vector3(LakeCenterX, topY, LakeCenterZ - 0.01f);
                cam.transform.rotation = Quaternion.Euler(89f, 0f, 0f);
                RenderTo(cam, Path.Combine(outDir, "TOPDOWN_lake_overview.png"));
            }

            // ---- Bridge land-connection seams: from just downstream/upstream, and from the side,
            // at the near-bank and far-bank land edges.
            {
                float bx = 0f; // RiverX(BridgeCenterZ) ~ near 0
                float gyNear = SampleY(terrain, bx - 12f, BridgeCenterZ);
                Capture(cam, terrain, bx - 12f, gyNear + 1.6f, BridgeCenterZ, bx, BridgeCenterZ, "BRIDGE_from_west_land", outDir);
                float gyFar = SampleY(terrain, bx + 12f, BridgeCenterZ);
                Capture(cam, terrain, bx + 12f, gyFar + 1.6f, BridgeCenterZ, bx, BridgeCenterZ, "BRIDGE_from_east_land", outDir);
                float gyUp = SampleY(terrain, bx, BridgeCenterZ + 12f);
                Capture(cam, terrain, bx, gyUp + 1.6f, BridgeCenterZ + 12f, bx, BridgeCenterZ, "BRIDGE_from_upstream", outDir);
                float gyDown = SampleY(terrain, bx, BridgeCenterZ - 12f);
                Capture(cam, terrain, bx, gyDown + 1.6f, BridgeCenterZ - 12f, bx, BridgeCenterZ, "BRIDGE_from_downstream", outDir);
                // Close, low, side-on shot right at the west end seam to see the width mismatch directly.
                float gySeam = SampleY(terrain, bx - 8f, BridgeCenterZ);
                Capture(cam, terrain, bx - 8f, gySeam + 1.3f, BridgeCenterZ, bx - 8f, BridgeCenterZ + 8f, "BRIDGE_seam_westend_along", outDir);
            }

            // ---- Close-ups walking the shore at a few specific angles (stairs=55deg, inlet=-10deg,
            // plus two cliff-heavy angles) to inspect CliffBoulder / LakeCliffLowerMossy / logs up close.
            float[] closeAngles = { 55f, -10f, 120f, 200f, 280f };
            foreach (var ang in closeAngles)
            {
                float rad = ang * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                Vector2 standPt = new Vector2(LakeCenterX, LakeCenterZ) + dir * new Vector2(LakeRadiusX, LakeRadiusZ).magnitude * 0.68f;
                float gy = SampleY(terrain, standPt.x, standPt.y);
                Vector2 lookPt = new Vector2(LakeCenterX, LakeCenterZ) + dir * new Vector2(LakeRadiusX, LakeRadiusZ).magnitude * 0.5f;
                string name = string.Format("CLOSE_ang{0:000}", (int)((ang + 360f) % 360f));
                Capture(cam, terrain, standPt.x, gy + 1.5f, standPt.y, lookPt.x, lookPt.y, name, outDir);
            }

            UnityEngine.Object.DestroyImmediate(camGo);
            log.AppendLine("Lake survey screenshots written to " + outDir);
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
