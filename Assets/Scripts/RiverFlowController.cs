using UnityEngine;
using UnityEngine.InputSystem;

// Drives the fall-into-the-river loop from Reference/Stage/stage_overview.png:
// fall off the path -> swept back toward Start, with limited sideways steering
// -> grab a RecoveryPoint to stop early, or run out of river and get returned
// to the last checkpoint. One instance lives in the stage scene; RiverTriggerZone
// hands control to it when the goblin falls in.
public class RiverFlowController : MonoBehaviour
{
    public static RiverFlowController Instance { get; private set; }

    [Header("Flow")]
    public float flowSpeed = 4f;
    public float riverSurfaceY = -4.3f;
    public float upstreamLimitZ = 10f; // matches the river's start; sweeping ends here at the latest
    public float riverHalfWidth = 6f;

    [Header("Steering while swept")]
    public float swimSpeed = 2.5f;

    [Header("Recovery")]
    public float grabRadius = 1.4f;
    public Key grabKey = Key.E;

    Transform goblin;
    GoblinLocomotion locomotion;
    CharacterController controller;
    bool sweeping;
    Vector3 lastCheckpoint;

    void Awake()
    {
        Instance = this;
    }

    public void SetInitialCheckpoint(Vector3 pos)
    {
        lastCheckpoint = pos;
    }

    public void ReportCheckpoint(Vector3 pos)
    {
        if (!sweeping) lastCheckpoint = pos;
    }

    public void BeginSweep(GoblinLocomotion loco, CharacterController cc, Transform goblinTransform)
    {
        if (sweeping) return;
        sweeping = true;
        locomotion = loco;
        controller = cc;
        goblin = goblinTransform;
        locomotion.enabled = false;
    }

    void Update()
    {
        if (!sweeping) return;

        var kb = Keyboard.current;
        float steer = 0f;
        if (kb != null)
        {
            if (kb.aKey.isPressed) steer -= 1f;
            if (kb.dKey.isPressed) steer += 1f;
        }

        Vector3 current = goblin.position;
        Vector3 desired = current;
        desired.x = Mathf.Clamp(current.x + steer * swimSpeed * Time.deltaTime, -riverHalfWidth, riverHalfWidth);
        desired.z -= flowSpeed * Time.deltaTime;
        desired.y = riverSurfaceY;
        controller.Move(desired - current);

        if (kb != null && kb[grabKey].wasPressedThisFrame)
        {
            var point = FindNearbyRecoveryPoint(goblin.position);
            if (point != null)
            {
                EndSweep(point.StandPosition);
                return;
            }
        }

        if (goblin.position.z <= upstreamLimitZ)
        {
            // The river empties into a lake here -- hand control back right where the sweep
            // stopped (in the lake) instead of teleporting to a checkpoint, so the player has
            // to actually swim/walk to the shore and climb the stairs back to the bridge.
            EndSweep(goblin.position);
        }
    }

    RecoveryPoint FindNearbyRecoveryPoint(Vector3 pos)
    {
        RecoveryPoint best = null;
        float bestDist = grabRadius;
        foreach (var p in RecoveryPoint.All)
        {
            float d = Vector3.Distance(pos, p.transform.position);
            if (d <= bestDist)
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }

    void EndSweep(Vector3 destination)
    {
        sweeping = false;
        locomotion.SnapTo(destination);
        locomotion.enabled = true;
    }
}
