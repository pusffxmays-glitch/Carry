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

    // ADDED 2026-08-17 (要望「川に落下した場合はツボを離して、ツボとゴブリンが別々に
    // 流されるようにしてほしい」): 落水時に壺を手放し、壺は壺で川面を漂流する。
    // ゴブリン (flowSpeed) よりずっと遅く流すことで、すぐに二つが離れ離れになる。
    // 速度を流体の追従能力 (maxSpeed 5 の容器相対クランプ + §21 の姿勢平滑化) の
    // 範囲に収めてあるので、漂流中も中身のシミュレーションは破綻しない。
    [Header("Pot drift (落水時の壺の漂流)")]
    [Tooltip("壺が流される速さ (m/s)。ゴブリンの flowSpeed より遅くして二つを引き離す。")]
    public float potDriftSpeed = 3.5f;
    [Tooltip("漂流中の上下の揺れ (m)。")]
    public float potBobAmplitude = 0.06f;
    [Tooltip("上下の揺れの周波数 (Hz)。")]
    public float potBobFrequency = 0.6f;
    [Tooltip("漂流中の傾きの振幅 (度)。")]
    public float potRockDeg = 7f;
    [Tooltip("壺の底が水面からどれだけ沈むか (m)。")]
    public float potFloatDepth = 0.12f;
    // 2026-08-22: 落水しても壺は直立のまま流れていたので中身が 70% も残っていた。
    // 「水に落としたらポーションは失う」がゲーム性として正しいので、漂流に入ったら
    // 壺を転覆させて中身を川へ流し出す。粒子を消すのではなく実際に注ぎ出す。
    // ここで持つ理由: 漂流中の姿勢は UpdatePotDrift が毎フレーム上書きするため、
    // GoblinPotActions 側で傾けても打ち消される。
    [Tooltip("漂流に入ったとき壺が転覆する角度 (度)。90 を超えると口が下を向く。0 で転覆しない。")]
    public float potCapsizeDeg = 125f;
    [Tooltip("転覆しきるまでの時間 (s)。短くしすぎると壺の移動がテレポート扱いになり中身が飛ぶ。")]
    public float potCapsizeSeconds = 0.8f;
    float potBaseYaw;

    // おぼれもがき (2026-08-17): クリップは足の最低点を root y=0 に正規化して再生される。
    // 実測 (z≈60 の開けた川面) では root = riverSurfaceY で頭と掻く腕だけが水面上に出て
    // ちょうど「おぼれ」に見える。見た目の水面はゲームプレイ水面 (riverSurfaceY) と
    // 完全一致ではなく川に沿って変わるので、深すぎたらここを負に、浮きすぎたら正に。
    [Tooltip("流されている間、ゴブリンの root を水面からどれだけ沈めるか (m)。")]
    public float sweepImmersion = 0.15f;

    Transform goblin;
    GoblinLocomotion locomotion;
    CharacterController controller;
    GoblinPotActions potActions;
    bool sweeping;
    Vector3 lastCheckpoint;
    Transform pot;
    FluidCore potFluid;
    bool potDrifting;
    float potPhase;

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

        // 壺を担いだまま落ちたら手放し、壺は壺で流す。Find が直接の子を返すのは
        // まだ手元にあるときだけなので、地面に置いてきた壺を誤って掴むことはない。
        var actions = goblinTransform.GetComponent<GoblinPotActions>();
        potActions = actions;
        if (actions != null) actions.sweptByRiver = true;   // おぼれもがき再生 (2026-08-17)
        Transform heldPot = goblinTransform.Find("Carry_Pot");
        if (actions != null && heldPot != null)
        {
            potFluid = heldPot.GetComponent<FluidCore>();
            actions.ReleasePotForSweep();
            pot = heldPot;
            potDrifting = true;
            potPhase = 0f;
            potBaseYaw = heldPot.eulerAngles.y;   // 転覆を足しても向きが自己参照で暴れないよう固定
        }
    }

    void Update()
    {
        if (sweeping) UpdateGoblinSweep();
        if (potDrifting) UpdatePotDrift();
    }

    void UpdateGoblinSweep()
    {
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
        desired.y = riverSurfaceY - sweepImmersion;
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

    // 壺の漂流。ゴブリンの sweep とは独立に進み、ゴブリンが先に復帰しても
    // 壺は湖まで流れ続ける (「別々に流される」)。
    void UpdatePotDrift()
    {
        if (pot == null) { potDrifting = false; return; }
        potPhase += Time.deltaTime;

        Vector3 p = pot.position;
        p.z -= potDriftSpeed * Time.deltaTime;
        p.x = Mathf.Clamp(p.x, -riverHalfWidth, riverHalfWidth);
        // 落水直後は手の高さから水面まで滑らかに降ろす (瞬間移動は流体が飛ぶ)
        float targetY = riverSurfaceY - potFloatDepth
            + potBobAmplitude * Mathf.Sin(potPhase * potBobFrequency * 2f * Mathf.PI);
        p.y = Mathf.MoveTowards(p.y, targetY, 4f * Time.deltaTime);
        pot.position = p;

        // ぷかぷかと傾く。周波数を軸ごとに変えて単調な往復に見せない。
        float rx = potRockDeg * Mathf.Sin(potPhase * potBobFrequency * 0.9f * 2f * Mathf.PI);
        float rz = potRockDeg * Mathf.Sin(potPhase * potBobFrequency * 0.7f * 2f * Mathf.PI + 1.3f);
        // 転覆をぷかぷかの上に重ねる。基準の向きは potBaseYaw に固定しておくこと
        // (pot.eulerAngles.y を読み直すと、足した転覆が次のフレームの入力になって暴れる)。
        Quaternion bob = Quaternion.Euler(rx, potBaseYaw, rz);
        float capsize = potCapsizeDeg <= 0f ? 0f
            : potCapsizeDeg * Mathf.SmoothStep(0f, 1f, potPhase / Mathf.Max(0.05f, potCapsizeSeconds));
        pot.rotation = capsize > 0f ? Quaternion.AngleAxis(capsize, Vector3.right) * bob : bob;

        if (p.z <= upstreamLimitZ) EndPotDrift();
    }

    [Tooltip("漂流の終点で、底がこの深さ (水面からの距離 m) より浅ければ着底させる。それより深い場所では水面に浮かせたままにする。")]
    public float potSettleMaxDepth = 0.8f;

    void EndPotDrift()
    {
        potDrifting = false;
        if (potFluid != null) potFluid.maxSpeedInPot = -1f;
        if (pot == null) return;
        // 湖に着いたら直立させる
        pot.rotation = Quaternion.Euler(0f, pot.eulerAngles.y, 0f);
        // 浅瀬なら着底させて拾いやすくする。深い場所で底まで沈めると壺が水面下に
        // 消えてしまい (実測: 湖口の底は水面下 4.4m、y=-8.6 まで沈んで見失った)、
        // さらにシム領域の底 (groundY) より下へ出て中身が全損する。深ければ浮かせたまま。
        var hits = Physics.RaycastAll(pot.position + Vector3.up * 0.5f, Vector3.down, 8f,
                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; float bottomY = pot.position.y;
        foreach (var h in hits)
        {
            if (h.collider.transform == pot || h.collider.transform.IsChildOf(pot)) continue;
            if (h.distance < best) { best = h.distance; bottomY = h.point.y; }
        }
        if (best != float.MaxValue && bottomY >= riverSurfaceY - potSettleMaxDepth)
            pot.position = new Vector3(pot.position.x, bottomY, pot.position.z);
        else
            pot.position = new Vector3(pot.position.x, riverSurfaceY - potFloatDepth, pot.position.z);
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
        if (potActions != null) potActions.sweptByRiver = false;   // おぼれもがきを終える
        locomotion.SnapTo(destination);
        locomotion.enabled = true;
    }
}
