using UnityEngine;

// Marks a safe point on the main path. Passing through it (on foot, not while
// being swept) updates where the player is returned to if a later river
// sweep runs out of river without grabbing a RecoveryPoint.
[RequireComponent(typeof(BoxCollider))]
public class CheckpointZone : MonoBehaviour
{
    void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<GoblinLocomotion>() == null) return;
        if (RiverFlowController.Instance == null) return;
        RiverFlowController.Instance.ReportCheckpoint(transform.position);
    }
}
