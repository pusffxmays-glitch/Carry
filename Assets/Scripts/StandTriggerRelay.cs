using UnityEngine;

// Sits on CollapsingFoothold's "StandTrigger" child collider and forwards OnTriggerStay up to the
// parent's CollapsingFoothold -- Unity only calls OnTriggerXxx on the GameObject that owns the
// Collider itself, never on an ancestor, so the timer logic can't just live on the parent alone.
[RequireComponent(typeof(Collider))]
public class StandTriggerRelay : MonoBehaviour
{
    CollapsingFoothold target;

    void Awake()
    {
        target = GetComponentInParent<CollapsingFoothold>();
    }

    void OnTriggerStay(Collider other)
    {
        if (target == null) return;
        if (other.GetComponent<GoblinLocomotion>() == null) return;
        target.NotifyGoblinPresent();
    }
}
