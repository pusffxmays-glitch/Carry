using UnityEngine;

// Stage 2 (毒沼) foothold gimmick: a wooden plank/log that gives way after the goblin has stood
// on it for a while, dropping them through into the swamp below -- reusing the existing
// RiverTriggerZone/RiverFlowController sweep-back-to-checkpoint system (see RiverTriggerVolume in
// CarryBuildTerrainForest.BuildRiverGimmick, extended to cover the swamp area too) rather than
// adding a new fall-consequence system.
//
// Detection: a trigger BoxCollider (separate from the solid walkable collider, added as a child
// named "StandTrigger") sits over the plank's own footprint. OnTriggerStay/Exit only ever SET a
// "last seen" timestamp -- accumulating the stand timer in Update() by comparing against that
// timestamp (rather than a plain bool reset-and-maybe-reset-again each frame) sidesteps the
// classic Update-vs-FixedUpdate ordering race between the two callbacks.
[RequireComponent(typeof(Collider))]
public class CollapsingFoothold : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("乗ってから崩落するまでの秒数")]
    public float standTimeBeforeCollapse = 2.0f;
    [Tooltip("崩落前、警告(揺れ)を始める残り秒数")]
    public float warningLeadTime = 0.7f;
    [Tooltip("崩落後、足場が元に戻るまでの秒数")]
    public float respawnDelay = 8f;
    [Tooltip("乗っていないとき、滞在タイマーがこの速さで回復する(秒/秒)")]
    public float recoverSpeed = 3f;

    [Header("警告の揺れ")]
    public float shakeAmplitudeDeg = 3f;
    public float shakeFrequency = 14f;

    [Header("崩落の見た目")]
    [Tooltip("崩落時に傾く/落下する速さ")]
    public float collapseFallSpeed = 4f;
    public float collapseTiltSpeed = 220f;

    Collider solidCollider;
    Collider standTrigger;
    Vector3 restPosition;
    Quaternion restRotation;
    Vector3 restLocalScaleForShake; // shake perturbs rotation only; kept for clarity, not scaled

    float standTimer;
    float lastGoblinSeenTime = -999f;
    bool collapsed;
    float collapseStartTime;

    const float SeenGraceSeconds = 0.2f; // covers one or two skipped physics steps without falsely "recovering"

    void Awake()
    {
        restPosition = transform.position;
        restRotation = transform.rotation;

        solidCollider = GetComponent<Collider>();

        var triggerGo = transform.Find("StandTrigger");
        if (triggerGo != null) standTrigger = triggerGo.GetComponent<Collider>();
        if (standTrigger != null) standTrigger.isTrigger = true;
    }

    // 2026-08-29 FIX (found by actually testing the mechanic in Play mode -- OnTriggerStay never
    // fired at all, confirmed via a temporary Debug.Log): Unity only delivers OnTriggerXxx to
    // scripts on the SAME GameObject as the Collider involved, never to a parent -- the trigger box
    // lives on the "StandTrigger" child (see BuildSwampFootholds), not on this object, so this
    // component's own OnTriggerStay was simply never called. StandTriggerRelay (below) sits on that
    // child and forwards the event up.
    public void NotifyGoblinPresent()
    {
        if (collapsed) return;
        lastGoblinSeenTime = Time.time;
    }

    void Update()
    {
        if (collapsed)
        {
            UpdateCollapsedFall();
            return;
        }

        bool goblinOn = Time.time - lastGoblinSeenTime <= SeenGraceSeconds;
        if (goblinOn)
        {
            standTimer += Time.deltaTime;
            float remaining = standTimeBeforeCollapse - standTimer;
            if (remaining <= warningLeadTime && remaining > 0f)
            {
                float shakeT = 1f - Mathf.Clamp01(remaining / warningLeadTime); // 0 at warning start -> 1 at collapse
                float wobble = Mathf.Sin(Time.time * shakeFrequency) * shakeAmplitudeDeg * shakeT;
                transform.rotation = restRotation * Quaternion.Euler(wobble, 0f, wobble * 0.6f);
            }
            if (standTimer >= standTimeBeforeCollapse) BeginCollapse();
        }
        else
        {
            standTimer = Mathf.Max(0f, standTimer - Time.deltaTime * recoverSpeed);
            if (standTimer <= 0f) transform.rotation = restRotation; // settle the warning shake back out once fully recovered
        }
    }

    void BeginCollapse()
    {
        collapsed = true;
        collapseStartTime = Time.time;
        if (solidCollider != null) solidCollider.enabled = false;
        if (standTrigger != null) standTrigger.enabled = false;
    }

    void UpdateCollapsedFall()
    {
        // Simple fall-and-tip animation -- purely visual, the goblin has already dropped through
        // (the solid collider was disabled the instant it collapsed) into the swamp's own
        // RiverTriggerVolume below, which hands off to RiverFlowController exactly like falling
        // into the Stage 1 river.
        transform.position += Vector3.down * collapseFallSpeed * Time.deltaTime;
        transform.Rotate(collapseTiltSpeed * Time.deltaTime, 0f, collapseTiltSpeed * 0.4f * Time.deltaTime, Space.Self);

        if (Time.time - collapseStartTime >= respawnDelay) Respawn();
    }

    void Respawn()
    {
        collapsed = false;
        standTimer = 0f;
        lastGoblinSeenTime = -999f;
        transform.position = restPosition;
        transform.rotation = restRotation;
        if (solidCollider != null) solidCollider.enabled = true;
        if (standTrigger != null) standTrigger.enabled = true;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = collapsed ? new Color(1f, 0.2f, 0.2f, 0.6f) : new Color(0.2f, 1f, 0.4f, 0.4f);
        Gizmos.DrawWireCube(transform.position, new Vector3(1f, 0.2f, 1f));
    }
#endif
}
