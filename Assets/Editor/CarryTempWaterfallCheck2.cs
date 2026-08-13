using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Supplementary waterfall check: the existing far-raycast method (15m inland -> lake center) tests
// whether ANY solid terrain exists between the waterfall and 15m further inland, which is true for
// basically any waterfall in front of a naturally-rising hillside backdrop (that's expected/correct,
// not a sign of embedding). This instead does a SHORT raycast starting 1.5m LAKE-SIDE of the
// waterfall's own position, aimed AT the waterfall's position, to test whether solid geometry
// exists immediately at/behind the waterfall's own exact spot (real embedding) vs. just having a
// hillside somewhere further back (expected/correct backdrop).
public static class CarryTempWaterfallCheck2
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float LakeCenterX = 0f, LakeCenterZ = -16f;

    [MenuItem("Carry/Debug/Waterfall Check 2 (temp)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var wfRoot = GameObject.Find("Waterfalls");
            if (wfRoot == null) { log.AppendLine("Waterfalls root not found!"); }
            else
            {
                foreach (Transform wf in wfRoot.transform)
                {
                    if (!wf.name.StartsWith("Waterfall_")) continue;
                    var mf = wf.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    Bounds wb = mf.sharedMesh.bounds;
                    Vector3 wCenterWorld = wf.TransformPoint(wb.center);
                    Vector2 toWf = new Vector2(wCenterWorld.x - LakeCenterX, wCenterWorld.z - LakeCenterZ);
                    Vector2 dirOut = toWf.normalized;

                    // Short probe: start 1.5m closer to the lake than the waterfall, aim outward
                    // (away from center) directly at/through the waterfall's own position, max
                    // distance 3m -- if something solid is hit within that short span, the waterfall
                    // mesh's own immediate position has solid geometry overlapping it.
                    Vector3 probeStart = new Vector3(
                        LakeCenterX + dirOut.x * (toWf.magnitude - 1.5f), wCenterWorld.y, LakeCenterZ + dirOut.y * (toWf.magnitude - 1.5f));
                    Vector3 probeDir = new Vector3(dirOut.x, 0f, dirOut.y);
                    string result;
                    if (Physics.Raycast(probeStart, probeDir, out RaycastHit hit, 3f))
                    {
                        float hitDistFromWf = Vector3.Distance(hit.point, wCenterWorld);
                        result = "HIT '" + hit.collider.name + "' at " + hit.distance.ToString("F2") + "m into the short probe (i.e. " +
                            (hit.distance - 1.5f).ToString("F2") + "m relative to the waterfall's own position -- negative=before it/in front, positive=past it/behind)";
                    }
                    else result = "NO HIT within 3m short probe -- clear immediately in front of/at the waterfall's own position";
                    log.AppendLine(wf.name + " @ " + wCenterWorld + ": " + result);
                }
            }
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
