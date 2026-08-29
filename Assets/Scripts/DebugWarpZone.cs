using UnityEngine;

// Debug/dev-only teleport trigger -- not part of the shipped game loop. Placed via an Editor build
// script (e.g. CarryBuildDebugWarpToStage2.cs) as a quick way to jump straight to a stage's start
// point during testing, without walking the whole course leading up to it each time.
[RequireComponent(typeof(Collider))]
public class DebugWarpZone : MonoBehaviour
{
    public Vector3 targetPosition;
    public string label = "Debug Warp";

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        var locomotion = other.GetComponent<GoblinLocomotion>();
        var controller = other.GetComponent<CharacterController>();
        if (locomotion == null || controller == null) return;
        // Disable/reposition/re-enable -- CharacterController ignores a direct transform.position
        // write while enabled, and Physics.autoSyncTransforms is off in this project (confirmed
        // earlier this session), so an explicit SyncTransforms is needed too.
        controller.enabled = false;
        other.transform.position = targetPosition;
        controller.enabled = true;
        Physics.SyncTransforms();
        Debug.Log("[DebugWarpZone] " + label + ": warped to " + targetPosition);
    }
}
