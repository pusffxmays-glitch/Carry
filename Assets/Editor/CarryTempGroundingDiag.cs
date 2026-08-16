using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot numeric diagnostic for the lake-area floating-object / waterfall-embedding / far-shore-
// flatness reports. Measures actual world-space renderer bounds vs terrain height per flagged
// object, dumps transform hierarchies for the two Hero prefabs, checks trees near the lake on
// steep slopes, and checks the waterfall mesh position against the cliff wall collider via
// raycast. Read-only: makes no scene/code changes.
public static class CarryTempGroundingDiag
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float LakeCenterX = 0f, LakeCenterZ = -16f;

    [MenuItem("Carry/Debug/Grounding Diagnostic (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = Terrain.activeTerrain;

            log.AppendLine("==== HeroCliffFace / HeroCoastRocks / CliffBoulder gap measurements ====");
            var allTransforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            var flagged = allTransforms.Where(t =>
                t.name.StartsWith("HeroCliffFace") || t.name == "HeroCoastRocks" || t.name.StartsWith("CliffBoulder_") || t.name.StartsWith("HeroClusterRock_") ||
                t.name.StartsWith("LakeShore_") || t.name.StartsWith("LakebedRock_") ||
                t.name.StartsWith("WaterfallFlankRock_") || t.name.StartsWith("WaterfallSourceRock_") || t.name.StartsWith("WaterfallBaseRock_") ||
                t.name.StartsWith("HeroLeaningTree_") || t.name.EndsWith("_Roots") || t.name == "HeroCoastalCliffBand" || t.name.StartsWith("HeroCoastalCliffBase_") ||
                t.name.StartsWith("HeroClusterRoot_") || t.name.StartsWith("WaterfallFern_") ||
                t.name.StartsWith("Vine_") || t.name.StartsWith("Litter_") || t.name.StartsWith("BankRock_")).ToList();
            log.AppendLine("Flagged object count: " + flagged.Count);

            foreach (var t in flagged)
            {
                var rends = t.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) { log.AppendLine(t.name + ": NO RENDERER"); continue; }
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                float groundCenter = terrain.SampleHeight(new Vector3(b.center.x, 0, b.center.z)) + terrain.transform.position.y;
                float gMinMin = terrain.SampleHeight(new Vector3(b.min.x, 0, b.min.z)) + terrain.transform.position.y;
                float gMinMax = terrain.SampleHeight(new Vector3(b.min.x, 0, b.max.z)) + terrain.transform.position.y;
                float gMaxMin = terrain.SampleHeight(new Vector3(b.max.x, 0, b.min.z)) + terrain.transform.position.y;
                float gMaxMax = terrain.SampleHeight(new Vector3(b.max.x, 0, b.max.z)) + terrain.transform.position.y;
                float groundBest = Mathf.Max(groundCenter, gMinMin, gMinMax, gMaxMin, gMaxMax);

                float gap = b.min.y - groundBest;
                log.AppendLine(string.Format(
                    "{0}: boundsMinY={1:F2} groundAtCenter={2:F2} groundBestOfCorners={3:F2} GAP={4:F2} (pos={5})",
                    t.name, b.min.y, groundCenter, groundBest, gap, t.position));
            }

            log.AppendLine("==== Transform hierarchy dump ====");
            var oneHero = flagged.FirstOrDefault(t => t.name.StartsWith("HeroCliffFace"));
            var oneCoast = flagged.FirstOrDefault(t => t.name == "HeroCoastRocks");
            foreach (var root in new[] { oneHero, oneCoast })
            {
                if (root == null) continue;
                log.AppendLine("--- " + root.name + " ---");
                DumpHierarchy(root, log, 0);
            }

            log.AppendLine("==== Prefab source mesh bounds (for comparison against GetPrefabBottomLocalY) ====");
            foreach (var path in new[] {
                "Assets/ExternalAssets/PolyHaven/mountainside/mountainside_decimated.fbx",
                "Assets/ExternalAssets/PolyHaven/coast_rocks_01/coast_rocks_01_decimated.fbx" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { log.AppendLine(path + " NOT FOUND"); continue; }
                log.AppendLine("--- prefab " + path + " ---");
                DumpHierarchy(prefab.transform, log, 0);
                var mf = prefab.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    log.AppendLine("  MeshFilter on '" + mf.gameObject.name + "' local mesh bounds min.y=" + mf.sharedMesh.bounds.min.y + " max.y=" + mf.sharedMesh.bounds.max.y);
            }

            log.AppendLine("==== Trees near lake on steep slopes ====");
            var data = terrain.terrainData;
            int checkedCount = 0, steepCount = 0;
            foreach (var ti in data.treeInstances)
            {
                float wx = terrain.transform.position.x + ti.position.x * data.size.x;
                float wz = terrain.transform.position.z + ti.position.z * data.size.z;
                float dist = Mathf.Sqrt((wx - LakeCenterX) * (wx - LakeCenterX) + (wz - LakeCenterZ) * (wz - LakeCenterZ));
                if (dist > 40f) continue;
                checkedCount++;
                float normX = ti.position.x, normZ = ti.position.z;
                float steepness = data.GetSteepness(normX, normZ);
                if (steepness > 30f)
                {
                    steepCount++;
                    if (steepCount <= 15)
                    {
                        float wy = terrain.SampleHeight(new Vector3(wx, 0, wz)) + terrain.transform.position.y;
                        log.AppendLine(string.Format("tree at world=({0:F1},{1:F1},{2:F1}) proto={3} steepness={4:F1}deg widthScale={5:F2}",
                            wx, wy, wz, ti.prototypeIndex, steepness, ti.widthScale));
                    }
                }
            }
            log.AppendLine("Trees within 40m of lake center checked: " + checkedCount + ", on slopes >30deg: " + steepCount);

            log.AppendLine("==== Waterfall vs cliff wall surface ====");
            var wfRoot = GameObject.Find("Waterfalls");
            if (wfRoot != null)
            {
                foreach (Transform wf in wfRoot.transform)
                {
                    if (!wf.name.StartsWith("Waterfall_")) continue;
                    var mf = wf.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    Bounds wb = mf.sharedMesh.bounds; // local
                    Vector3 wCenterWorld = wf.TransformPoint(wb.center);
                    // direction from lake center through the waterfall's XZ position
                    Vector2 toWf = new Vector2(wCenterWorld.x - LakeCenterX, wCenterWorld.z - LakeCenterZ);
                    float distFromLakeCenter = toWf.magnitude;
                    Vector2 dirOut = toWf.normalized;

                    // raycast from further out (behind the waterfall, away from lake) toward the lake center,
                    // to find the cliff wall collider surface radius at this angle.
                    Vector3 rayStart = new Vector3(LakeCenterX + dirOut.x * (distFromLakeCenter + 15f), wCenterWorld.y + 2f, LakeCenterZ + dirOut.y * (distFromLakeCenter + 15f));
                    Vector3 rayDir = (new Vector3(LakeCenterX, wCenterWorld.y, LakeCenterZ) - rayStart).normalized;
                    string hitInfo = "NO HIT";
                    float wallDistFromLakeCenter = -1f;
                    if (Physics.Raycast(rayStart, rayDir, out RaycastHit hit, 60f))
                    {
                        Vector2 hitXZ = new Vector2(hit.point.x - LakeCenterX, hit.point.z - LakeCenterZ);
                        wallDistFromLakeCenter = hitXZ.magnitude;
                        hitInfo = "collider='" + hit.collider.name + "' at dist-from-lake-center=" + wallDistFromLakeCenter.ToString("F2");
                    }
                    string relation = wallDistFromLakeCenter < 0 ? "unknown" :
                        (distFromLakeCenter > wallDistFromLakeCenter + 0.3f ? "IN FRONT of wall (further from center than wall surface)" :
                         distFromLakeCenter < wallDistFromLakeCenter - 0.3f ? "BEHIND/EMBEDDED in wall (closer to center than wall surface)" : "LEVEL with wall surface");
                    log.AppendLine(string.Format("{0}: worldCenter={1} distFromLakeCenter={2:F2} wallHit=[{3}] => {4}",
                        wf.name, wCenterWorld, distFromLakeCenter, hitInfo, relation));
                }
            }
            else log.AppendLine("Waterfalls root not found!");

            log.AppendLine("==== Current wall height variation (LakeCliffLowerMossy) ====");
            var wallMossy = allTransforms.FirstOrDefault(t => t.name == "LakeCliffLowerMossy");
            if (wallMossy != null)
            {
                var mf = wallMossy.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var verts = mf.sharedMesh.vertices;
                    var worldYs = verts.Select(v => wallMossy.TransformPoint(v).y).ToArray();
                    log.AppendLine("LakeCliffLowerMossy world Y range: min=" + worldYs.Min().ToString("F2") + " max=" + worldYs.Max().ToString("F2") + " (range=" + (worldYs.Max() - worldYs.Min()).ToString("F2") + ")");
                }
            }
            else log.AppendLine("LakeCliffLowerMossy not found!");

            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    static void DumpHierarchy(Transform t, System.Text.StringBuilder log, int depth)
    {
        string indent = new string(' ', depth * 2);
        var mf = t.GetComponent<MeshFilter>();
        string meshInfo = mf != null && mf.sharedMesh != null ? " [MESH: " + mf.sharedMesh.name + " localBounds.min.y=" + mf.sharedMesh.bounds.min.y + "]" : "";
        log.AppendLine(indent + t.name + " localPos=" + t.localPosition + " localRot=" + t.localRotation.eulerAngles + " localScale=" + t.localScale + meshInfo);
        foreach (Transform child in t) DumpHierarchy(child, log, depth + 1);
    }
}
