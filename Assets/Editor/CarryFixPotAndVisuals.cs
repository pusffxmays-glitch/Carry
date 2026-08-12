using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// STEP 1b (2026-08-10): two small, independent fixes reported after Play-testing STEP 1:
//
// 1) "壺が胴体についてしまっている、ちょうどおなかの辺にある" -- the OLD pot placement
//    (CarryAttachPot.cs, parented to Spine02 at a hardcoded chest-height offset) was built for
//    a different, simpler concept and never matched Carry_Neutral_Pose (arms reaching straight
//    up, pot resting on/above the head). Re-anchors the pot to the Head bone using the offset
//    measured directly from Carry_Neutral_Pose in Blender (Pot position minus Head bone
//    position, world axes, Blender Z-up -> Unity Y-up). This is a ONE-TIME static placement,
//    not a per-frame follow system -- matches "少しずつ" (keep dynamic tilt/height control for
//    a later, separate step once arm control itself is confirmed working).
//
// 2) "画質わるくなってない?" -- likely caused by my own earlier `git checkout` on
//    CastleStage.unity (see WORKLOG.md), which reverted to the committed scene state and may
//    have discarded an uncommitted Global Volume / post-processing setup along with the
//    rejected pot-carry system. I cannot recover that exact prior state (it was never
//    committed or stashed). This adds back a Global Volume using the project's own
//    Assets/Settings/SampleSceneProfile.asset (Bloom/Tonemapping/Vignette -- the standard URP
//    template profile already sitting unused in the project) and makes sure the Main Camera
//    has post-processing enabled, which is the most likely concrete cause I could verify from
//    the scene file (no Volume component existed in it at all).
public static class CarryFixPotAndVisuals
{
    private const string ScenePath = "Assets/Scenes/CastleStage.unity";
    private const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";

    // Measured directly from Carry_Neutral_Pose in Blender: Pot world position minus Head bone
    // world position, converted Blender(Z-up,-Y fwd) -> Unity(Y-up,+Z fwd).
    // CORRECTED 2026-08-10: the first measurement was taken while the Blender pose was
    // contaminated by leftover values from an earlier custom pose (see WORKLOG.md) and was
    // wrong by a large margin (0.088/-0.239 instead of the ~0.018/-0.017 below). Re-measured
    // after resetting every pose bone to rest and re-evaluating Carry_Neutral_Pose cleanly.
    static readonly Vector3 PotOffsetFromHead = new Vector3(0.0073f, 0.0181f, -0.0167f);

    [MenuItem("Carry/STEP 1b - Fix Pot Placement + Restore Post-Processing")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            FixPotPlacement(log);
            RestorePostProcessing(log);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    static void FixPotPlacement(System.Text.StringBuilder log)
    {
        var goblin = GameObject.Find("Goblin");
        if (goblin == null) throw new System.Exception("Goblin not found in scene.");

        Transform head = GoblinBoneUtil.FindDeep(goblin.transform, "Head");
        if (head == null) throw new System.Exception("Head bone not found under Goblin.");

        // Find the pot wherever it currently is (old PotSocket under Spine02, or loose in the
        // scene) rather than assuming a specific path.
        Transform pot = null;
        foreach (var t in goblin.GetComponentsInChildren<Transform>(true))
            if (t.name == "Carry_Pot") { pot = t; break; }
        if (pot == null)
        {
            var found = GameObject.Find("Carry_Pot");
            if (found != null) pot = found.transform;
        }
        if (pot == null) throw new System.Exception("Carry_Pot not found in scene or under Goblin.");

        // Drop it directly under Goblin (not under the old PotSocket/Spine02 chest anchor) and
        // give it a plain static world position -- no per-bone parenting yet, so the belly
        // offset can't come back from a stale hierarchy.
        pot.SetParent(goblin.transform, true);
        pot.position = head.position + PotOffsetFromHead;
        pot.rotation = goblin.transform.rotation;
        pot.localScale = Vector3.one;
        log.AppendLine("Moved Carry_Pot to Head + " + PotOffsetFromHead + " (world), parented under Goblin.");

        // Remove the now-empty old chest-hug socket so it doesn't cause confusion later.
        foreach (var t in goblin.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Spine02")
            {
                var oldSocket = t.Find("PotSocket");
                if (oldSocket != null && oldSocket.childCount == 0)
                {
                    Object.DestroyImmediate(oldSocket.gameObject);
                    log.AppendLine("Removed now-empty old PotSocket under Spine02.");
                }
                break;
            }
        }
    }

    static void RestorePostProcessing(System.Text.StringBuilder log)
    {
        var existingVolume = Object.FindFirstObjectByType<Volume>();
        if (existingVolume == null)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                log.AppendLine("WARNING: " + VolumeProfilePath + " not found -- skipped adding a Global Volume.");
            }
            else
            {
                var go = new GameObject("Global Volume");
                var vol = go.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.weight = 1f;
                vol.sharedProfile = profile;
                log.AppendLine("Added Global Volume using " + VolumeProfilePath + " (Bloom/Tonemapping/Vignette).");
            }
        }
        else
        {
            log.AppendLine("A Volume already exists (" + existingVolume.name + ") -- left as-is.");
        }

        var mainCam = GameObject.Find("Main Camera");
        if (mainCam != null)
        {
            var cam = mainCam.GetComponent<Camera>();
            var camData = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
            if (camData != null && !camData.renderPostProcessing)
            {
                camData.renderPostProcessing = true;
                log.AppendLine("Enabled Post Processing on Main Camera.");
            }
        }
        else
        {
            log.AppendLine("WARNING: Main Camera not found -- could not check its Post Processing flag.");
        }
    }
}
