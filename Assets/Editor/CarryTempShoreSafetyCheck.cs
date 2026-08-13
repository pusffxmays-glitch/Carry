using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot geometric gameplay-safety diagnostic for the lake shoreline rework: at a ring of test
// angles around the lake (including the 5 named ShoreZone centers + several unnamed angles),
// raycasts straight down from high above at increasing radii to find where the player would
// actually be blocked, and reports the effective wall height there (rim height minus water
// level) plus whether anything solid bridges across open water at that angle. Temporary tooling.
public static class CarryTempShoreSafetyCheck
{
    private const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float LakeCenterX = 0f, LakeCenterZ = -16f;
    const float LakeWaterY = -4.4f;

    [MenuItem("Carry/Debug/Check Shore Safety (temp)")]
    public static void Run()
    {
        var log = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var lakeCliffCollider = GameObject.Find("LakeCliffCollider");
            var stairsRoot = GameObject.Find("LakeStairs");
            log.AppendLine("LakeCliffCollider found: " + (lakeCliffCollider != null));
            log.AppendLine("LakeStairs found: " + (stairsRoot != null));

            float[] testAngles = { 55f, 110f, 160f, 210f, 260f, 305f, 0f, 135f, 180f, 225f, 315f };
            foreach (float ang in testAngles)
            {
                float rad = ang * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                log.AppendLine("--- angle " + ang + " ---");

                // Sweep outward from the lake center in 1m steps, raycasting straight down from
                // Y=60 at each radius, and record the highest hit Y found (any collider) -- this
                // traces the actual physical profile a player would experience walking outward
                // from the water, independent of which specific object provides the collision.
                float prevY = LakeWaterY;
                float maxRise = 0f;
                bool bridgesToOppositeShore = true;
                for (float r = 5f; r <= 45f; r += 1f)
                {
                    Vector2 p = new Vector2(LakeCenterX, LakeCenterZ) + dir * r;
                    Vector3 origin = new Vector3(p.x, 60f, p.y);
                    if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f))
                    {
                        float rise = hit.point.y - prevY;
                        if (hit.point.y > LakeWaterY + 0.3f) // only count once actually above water
                            maxRise = Mathf.Max(maxRise, rise);
                        prevY = Mathf.Max(prevY, hit.point.y);
                    }
                    else
                    {
                        bridgesToOppositeShore = false; // gap with no collider at all (shouldn't happen over land, fine over open water)
                    }
                }
                log.AppendLine("  max single-step rise (1m radius step) while walking outward: " + maxRise.ToString("F2") + "m");

                // Specific check: is there solid ground/rock significantly ABOVE water level within
                // just 2-4m of the shore (i.e. an easy climb-out point)? Sample the actual highest
                // hit within a tight band just past where water starts.
                // Widened from the original fixed 18-26m band -- that missed the actual shore crest
                // at zones with a large negative radialOffset (coves pull the true shore radius out
                // by several meters, e.g. RootBank at 260deg), which read as a false "below water"
                // result there. 12-34m comfortably covers the shore at every zone's actual (possibly
                // offset) radius.
                float shoreHighPoint = float.MinValue;
                float shoreHighPointR = -1f;
                for (float r = 12f; r <= 34f; r += 0.5f)
                {
                    Vector2 p = new Vector2(LakeCenterX, LakeCenterZ) + dir * r;
                    Vector3 origin = new Vector3(p.x, 60f, p.y);
                    if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f) && hit.point.y > shoreHighPoint)
                    {
                        shoreHighPoint = hit.point.y;
                        shoreHighPointR = r;
                    }
                }
                log.AppendLine("  highest point found within r=12-34m band: " + shoreHighPoint.ToString("F2") + " at r=" + shoreHighPointR.ToString("F1") + " (water=" + LakeWaterY.ToString("F2") + ", diff=" + (shoreHighPoint - LakeWaterY).ToString("F2") + ")");
            }

            // Specifically check the mountainside-bisection concern from the topdown screenshot:
            // does solid geometry span all the way from one side of the lake to the other through
            // the middle (i.e. did the 210deg hero rock formation's footprint reach across open
            // water, creating a walkable "bridge" straight through the lake)?
            log.AppendLine("--- lake-center bisection check ---");
            for (float z = LakeCenterZ - 15f; z <= LakeCenterZ + 15f; z += 2f)
            {
                Vector3 origin = new Vector3(LakeCenterX, 60f, z);
                bool hitSolid = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f) && hit.point.y > LakeWaterY + 0.5f;
                log.AppendLine("  x=0, z=" + z.ToString("F0") + " -> " + (hitSolid ? ("SOLID at y=" + hit.point.y.ToString("F2")) : "open water/no solid"));
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
