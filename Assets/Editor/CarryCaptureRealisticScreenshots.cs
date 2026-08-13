using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// QA helper for ForestStage_Realistic.unity -- warms up the renderer, then
// captures a handful of goblin-eye-level vantage points. Camera Y is derived
// from the actual Terrain height at each point (not a fixed constant) since
// the ground is no longer flat. Scene changes are never saved.
public static class CarryCaptureRealisticScreenshots
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    [MenuItem("Carry/Debug/Capture Realistic Forest Screenshots")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = Terrain.activeTerrain;

            GameObject goblin = GameObject.Find("Goblin");
            Vector3 spawnPos = goblin != null ? goblin.transform.position : Vector3.zero;
            float deckY = spawnPos.y;
            float sx = spawnPos.x, sz = spawnPos.z;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(projectRoot, "ScreenshotsOut");
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("__ScreenshotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;

            // Bridge deck now crosses the river in X (long axis) at a fixed Z band, and sits
            // well above the carved lake/river terrain beneath it, so shots at/near the bridge
            // use the goblin's actual spawn position (on the deck) instead of terrain.SampleHeight
            // (which would sample the carved riverbed/lakebed far below the walkable surface).
            // Also shoot explicitly along the bridge's long (X) axis to confirm the crossing.
            (float x, float z, float lookX, float lookZ, string name, bool useDeckY)[] shots =
            {
                (sx, sz, sx, sz + 15f, "L01_bridge_spawn_lookforward", true),
                (sx, sz, sx, sz - 20f, "L02_bridge_spawn_looklake", true),
                (sx, sz - 2f, sx, sz - 20f, "L03_lake_from_bridge_edge", true),
                (sx - 35f, sz, sx, sz, "L04_bridge_from_side", true),
                (sx + 12f, -8f, sx, -16f, "L05_stairs", false),
                (sx + 3f, sz + 13f, sx - 1f, sz + 25f, "R02_river_hop", false),
                (sx, sz + 19f, sx, sz + 50f, "R03_downstream", false),
                (sx - 1f, sz + 39f, sx + 2f, sz + 47f, "R04_waterline", false),
                (sx + 3f, sz + 53f, sx, sz + 63f, "R05_logcrossing", false),
                (sx - 4f, sz + 83f, sx, sz + 95f, "R06_approach_end", false),
                (sx + 7f, sz + 35f, sx - 3f, sz + 41f, "R07_wide_forest", false),
                (sx, sz + 57f, sx + 5f, sz + 57f, "R08_lookleft", false),
            };

            for (int i = 0; i < 6; i++)
                Capture(cam, terrain, deckY, shots[0].x, shots[0].z, shots[0].lookX, shots[0].lookZ, shots[0].useDeckY, Path.Combine(outDir, "00_warmup_discard.png"));

            foreach (var s in shots)
                Capture(cam, terrain, deckY, s.x, s.z, s.lookX, s.lookZ, s.useDeckY, Path.Combine(outDir, s.name + ".png"));

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

    static void Capture(Camera cam, Terrain terrain, float deckY, float x, float z, float lookX, float lookZ, bool useDeckY, string outPath)
    {
        // Terrain.SampleHeight() returns LOCAL height (0..TerrainData.size.y), not world space --
        // must add the terrain GameObject's own Y position to place the camera correctly.
        float terrainY0 = terrain != null ? terrain.transform.position.y : 0f;
        float groundY = useDeckY ? deckY : (terrain != null ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrainY0 : 0f);
        float lookGroundY = useDeckY ? deckY : (terrain != null ? terrain.SampleHeight(new Vector3(lookX, 0f, lookZ)) + terrainY0 : 0f);
        float y = groundY + 1.6f;
        float lookY = lookGroundY + 1.2f;
        cam.transform.position = new Vector3(x, y, z);
        cam.transform.LookAt(new Vector3(lookX, lookY, lookZ));

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
