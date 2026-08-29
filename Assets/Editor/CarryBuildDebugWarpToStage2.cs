using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Dev-only warp pad near the start bridge that jumps the goblin straight to Stage 2's start, so
// testing the swamp course doesn't require walking all of Stage 1 (bridge -> lake -> stone course)
// every time. Not part of the shipped game -- purely a debug aid (user: "橋付近にステージ2のスタート
// 地点にワープできるデバック用の装置を作って").
public static class CarryBuildDebugWarpToStage2
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";

    [MenuItem("Carry/Build Debug Warp To Stage 2")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainRoot = GameObject.Find("ForestStage_Terrain");

            var old = terrainRoot.transform.Find("DEBUG_WarpToStage2");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            // Target: the top of the first Stage 2 foothold (SwampFootholds' lowest-Z child) with a
            // small safety clearance -- read live rather than hardcoded so it stays correct if
            // CarryBuildSwampFootholds is re-run and the course regenerates differently.
            var footRoot = terrainRoot.transform.Find("SwampFootholds");
            Transform firstPiece = null;
            float minZ = float.MaxValue;
            if (footRoot != null)
                foreach (Transform c in footRoot)
                    if (c.position.z < minZ) { minZ = c.position.z; firstPiece = c; }

            Vector3 targetPos;
            if (firstPiece != null)
            {
                var box = firstPiece.GetComponent<BoxCollider>();
                var b = box != null ? box.bounds : new Bounds(firstPiece.position, Vector3.zero);
                targetPos = new Vector3(b.center.x, b.max.y + 0.6f, b.center.z);
            }
            else
            {
                targetPos = new Vector3(3.3f, 3f, 101f); // fallback near the connector anchor if SwampFootholds hasn't been built yet
                log.AppendLine("WARNING: SwampFootholds not found in scene, using a fallback target position.");
            }

            // Marker placed on the start bridge deck itself, west of the spawn checkpoint (behind
            // it, away from the course).
            // 2026-08-29 FIX #1: originally sampled raw Terrain height a few meters to the side, but
            // the bridge spans a deep gorge here (terrain itself sits ~-3..-4 under the bridge --
            // confirmed live) -- that placed the marker down in the ravine/water, not on the bridge.
            // The checkpoint's own Y is already known-good (it's the goblin's spawn point, sitting
            // right on the deck), so reuse it directly instead of re-sampling terrain.
            // 2026-08-29 FIX #2 (user: "湖からコースに戻る際の同線にワープゾーンを配置しないで"): the
            // first placement (checkpoint.x + 1.8) sat squarely on the diagonal a returning goblin
            // walks -- LakeSlope's own last slab ends at (5.00, 1.30, 5.00), right on this bridge,
            // and MossyRockPath_Course's own first piece starts at (-0.44, 1.83, 6.74) -- so anything
            // between roughly x=-0.44..5 at z~5..7 is exactly that return line. Moved well west of
            // the checkpoint instead (checkpoint.x - 5), clear of both that return line and the
            // initial spawn->course-start walk (which heads the opposite way, east).
            var checkpoint = GameObject.Find("Checkpoint_Start");
            Vector3 bridgeRef = checkpoint != null ? checkpoint.transform.position : new Vector3(-3.16f, 4.51f, 5f);
            float markerX = bridgeRef.x - 5f;
            float markerZ = bridgeRef.z;
            float markerY = bridgeRef.y;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "DEBUG_WarpToStage2";
            go.transform.SetParent(terrainRoot.transform, false);
            go.transform.position = new Vector3(markerX, markerY + 0.5f, markerZ);
            go.transform.localScale = new Vector3(1.2f, 1f, 1.2f);

            var rend = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(1f, 0f, 1f); // bright magenta -- unmissable, clearly not real game art
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(0.8f, 0f, 0.8f));
            }
            rend.sharedMaterial = mat;

            // Cylinder primitives come with a CapsuleCollider by default -- replace with a taller
            // BoxCollider trigger so a walking goblin reliably registers (a marginal-height trigger
            // silently failing to fire was a real, previously-debugged issue this session).
            var capsule = go.GetComponent<CapsuleCollider>();
            if (capsule != null) Object.DestroyImmediate(capsule);
            var triggerBox = go.AddComponent<BoxCollider>();
            triggerBox.isTrigger = true;
            triggerBox.size = new Vector3(1.4f, 2.5f, 1.4f);

            var warp = go.AddComponent<DebugWarpZone>();
            warp.targetPosition = targetPos;
            warp.label = "Stage 2 start";

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("Debug warp pad placed at " + go.transform.position + " -> target " + targetPos + ". SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
