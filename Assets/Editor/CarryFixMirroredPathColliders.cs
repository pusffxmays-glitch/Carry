using UnityEditor;
using UnityEngine;

// Fixes a mirroring-specific collider bug on MossyRockPath course pieces placed with
// localScale.x = -1 (see CarryBuildMossyRockPathCourse.cs's mirroring technique). Unity's
// renderer handles a negative-scale ancestor fine for meshes, but PhysX BoxColliders cannot
// represent a reflection (only proper rotations) -- under a mirrored parent, the WalkableCollision
// box chain's orientation comes out distorted, most visibly at each piece's tapered tip, where the
// wireframe was confirmed (via Scene view screenshot) to poke out past the visual rock silhouette
// over open water. The equivalent NON-mirrored piece showed no such overhang, isolating this as a
// mirroring-only issue, not a general width/shape bug (that was already fixed separately via
// MossyPathAnalysis.Bin.RawWidth).
//
// Fix: move the -1 scale onto the "Visual" child only (mesh rendering is unaffected -- same net
// world transform as before), un-mirror the piece root back to (1,1,1), and manually re-derive
// each WalkableCollision box's local position/rotation as a true mirror (negate X position, negate
// the X component of its forward direction before re-deriving rotation) so PhysX only ever sees a
// pure rotation, never a reflection. Root position/rotation (what the user manually placed) are
// never touched.
public static class CarryFixMirroredPathColliders
{
    [MenuItem("Carry/Fix Mirrored Path Colliders")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        var course = GameObject.Find("ForestStage_Terrain/MossyRockPath_Course");
        if (course == null) { Debug.LogError("MossyRockPath_Course not found"); return; }

        int fixedCount = 0;
        foreach (Transform piece in course.transform)
        {
            var visual = piece.Find("Visual");
            var wc = piece.Find("WalkableCollision");
            if (visual == null || wc == null) continue;
            // Detect "is mirrored" via either the root (first-ever run) or the Visual child (a
            // previous run of this same fix already moved the -1 there) -- must handle both so
            // re-running this after a prefab rebuild (which only updates un-overridden properties)
            // is safe and idempotent instead of silently no-op'ing on already-fixed pieces.
            bool mirrored = piece.localScale.x < 0f || visual.localScale.x < 0f;
            if (!mirrored) continue;

            // Always revert any previous instance-level overrides on the collider children first,
            // so this starts from the CURRENT prefab-sourced geometry (e.g. after
            // CarrySetupMossyRockPath.Run() regenerated WalkableCollision with improved width
            // data) rather than re-mirroring stale numbers left over from an earlier run.
            foreach (Transform coll in wc)
                PrefabUtility.RevertObjectOverride(coll, InteractionMode.AutomatedAction);

            visual.localScale = new Vector3(-1f, 1f, 1f);
            piece.localScale = Vector3.one;

            foreach (Transform coll in wc)
            {
                var box = coll.GetComponent<BoxCollider>();
                if (box == null) continue;
                Vector3 p = coll.localPosition;
                coll.localPosition = new Vector3(-p.x, p.y, p.z);
                Vector3 fwd = coll.localRotation * Vector3.forward;
                Vector3 mirroredFwd = new Vector3(-fwd.x, 0f, fwd.z);
                if (mirroredFwd.sqrMagnitude < 1e-8f) mirroredFwd = Vector3.forward;
                coll.localRotation = Quaternion.LookRotation(mirroredFwd.normalized, Vector3.up);
            }

            fixedCount++;
            log.AppendLine("Fixed: " + piece.name + " (pos/rot unchanged: " + piece.position + " / " + piece.rotation.eulerAngles + ")");
        }
        log.AppendLine("Total mirrored pieces fixed: " + fixedCount);
        Debug.Log(log.ToString());

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(course.scene);
    }
}
