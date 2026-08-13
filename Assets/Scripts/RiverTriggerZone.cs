using UnityEngine;

// Fills the gorge below the main path. Anything that falls in here and
// carries a GoblinLocomotion gets swept toward Start by RiverFlowController
// until it grabs a RecoveryPoint or runs out of river and is returned to the
// last checkpoint.
[RequireComponent(typeof(BoxCollider))]
public class RiverTriggerZone : MonoBehaviour
{
    void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        var locomotion = other.GetComponent<GoblinLocomotion>();
        var controller = other.GetComponent<CharacterController>();
        if (locomotion == null || controller == null) return;
        if (RiverFlowController.Instance == null) return;
        RiverFlowController.Instance.BeginSweep(locomotion, controller, other.transform);
    }
}
