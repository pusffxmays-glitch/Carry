using UnityEngine;

// ============================================================================================
// GoblinTerrainTilt -- 足元の地形に合わせて体を傾ける。
//
// これが無いと、斜面に立っても体は真っ直ぐ立ったままで、壺も水平のままになる。
//
// 設計上の要点:
//
//  * **root（CharacterController の付いた Goblin）は絶対に傾けない。**
//    GoblinLocomotion は `transform.forward * speed` で移動するので、root を傾けると
//    forward に上下成分が乗り、歩くたびに地面へめり込んだり浮いたりする。
//    代わりに見た目用の子（Goblin_Tilt）を作り、そこだけを傾ける。
//
//  * 傾きは **GoblinCarryRig より先** に適用する必要がある (実行順 -10)。
//    リグは手のボーンの world 位置から壺を置くので、先に体を傾けておけば
//    壺は何もしなくても正しい位置へ付いてくる。壺の「姿勢」だけはリグが
//    root の向きから作っているので、そちらには postureRoot を渡して合わせる。
//
//  * 法線は 1 本の Raycast では取らない。足元・前後・左右の 5 点を撃って平均する。
//    1 本だと石畳の凹凸や斜面の継ぎ目で法線が跳ね、体がガタガタ揺れる。
// ============================================================================================
[DefaultExecutionOrder(-10)]
public class GoblinTerrainTilt : MonoBehaviour
{
    [Header("Tilt")]
    [Tooltip("地形に合わせて傾ける最大角 (deg)。これを超える急斜面でもここで頭打ちにする。")]
    [Range(0f, 60f)] public float maxTiltDeg = 30f;
    [Tooltip("追従の速さ。大きいほど機敏。小さいと斜面に乗ってから傾くまでが緩やかになる。")]
    [Range(1f, 30f)] public float responseSpeed = 8f;
    // 追補 18: 運搬中は傾きの角速度そのものを制限する。指数追従 (responseSpeed 8) は
    // 18° のランプ進入で ~60°/s のピッチ回転になり、満杯の壺から 13-17% を一撃で
    // 捨てていた (プレイヤーに対処不能)。壺なしは従来どおり機敏。
    [Tooltip("運搬中 (gentleMode) の傾き角速度上限 (deg/s)。18° のランプで約 1.2 秒かけて傾く。")]
    public float carryTiltRateDeg = 15f;
    [Tooltip("壺を担いでいる間 true (GoblinPotActions が更新)。")]
    [HideInInspector] public bool gentleMode = true;
    [Tooltip("傾きの強さ。1 で地面と完全に平行、0.5 で半分だけ傾く。")]
    [Range(0f, 1f)] public float tiltStrength = 1f;

    [Header("Ground probe")]
    [Tooltip("前後の探査距離 (m)。歩幅程度。")]
    public float probeForward = 0.35f;
    [Tooltip("左右の探査距離 (m)。肩幅程度。")]
    public float probeSide = 0.30f;
    [Tooltip("Raycast の開始高さ (m)。")]
    public float probeUp = 1.0f;
    [Tooltip("Raycast の長さ (m)。")]
    public float probeDown = 3.0f;
    public LayerMask groundMask = ~0;

    [Tooltip("空中では傾きを水平へ戻す。false にすると滞空中も直前の傾きを保つ。")]
    public bool levelWhileAirborne = true;

    /// <summary>見た目を傾けるための子。root は傾けない。</summary>
    public Transform Pivot { get; private set; }
    /// <summary>直近で採用した地面の法線 (world)。Debug 用。</summary>
    public Vector3 GroundNormal => smoothedNormal;
    /// <summary>現在の傾き角 (deg)。Debug 用。</summary>
    public float TiltAngle => Vector3.Angle(Vector3.up, smoothedNormal);
    /// <summary>足元の地面の摩擦 (1 = 通常, 0 = 氷)。GroundSurface が無い面は 1。
    /// ここで一緒に拾っておくと、滑り処理が自前で Raycast を撃ち直さずに済む。</summary>
    public float GroundFriction { get; private set; } = 1f;
    /// <summary>足元に地面があるか（Raycast が当たったか）。</summary>
    public bool HasGround { get; private set; }
    /// <summary>足元から地面までの距離 (m)。接地判定に使う。
    /// CharacterController.isGrounded は「最後に呼んだ Move の結果」なので、
    /// 滑りのように後から Move を足す処理があると当てにならない。こちらは実測値。</summary>
    public float GroundDistance { get; private set; } = 999f;

    const string PivotName = "Goblin_Tilt";

    CharacterController controller;
    Vector3 smoothedNormal = Vector3.up;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        EnsurePivot();

        // ADDED 2026-08-17: 傾きを開始前に足元の法線で初期化する。
        // 従来は water から始まり (smoothedNormal = up)、開始直後に carryTiltRateDeg で
        // 実際の法線 (橋のアーチで約 2°) までランプしていた。壺はその間回転し続けるので、
        // 静定済みで注がれた液面が開始直後に再平衡を強いられ、「何もしていないのに
        // ポーションが揺れる」初期スロッシュの一因になっていた (実測: 開始 1.6 秒で
        // 流体がクランプ速度 5 m/s に到達)。最初から正しい傾きで立たせれば、
        // FluidCore の初期整定 (PreSettle) がその姿勢の液面を作るので、揺れが出ない。
        Vector3 n = SampleGroundNormal();
        if (HasGround)
        {
            float ang = Vector3.Angle(Vector3.up, n);
            if (ang > maxTiltDeg && ang > 1e-3f)
                n = Vector3.Slerp(Vector3.up, n, maxTiltDeg / ang);
            smoothedNormal = Vector3.Slerp(Vector3.up, n, tiltStrength).normalized;
            Pivot.rotation = Quaternion.FromToRotation(Vector3.up, smoothedNormal) * transform.rotation;
        }
    }

    // 見た目用の子を用意し、壺以外の子（アーマチュアとメッシュ）をその下へ移す。
    // 壺はリグが毎フレーム world 座標で置き直すので、親を変える必要が無い。
    // 何度呼んでも安全。
    void EnsurePivot()
    {
        if (Pivot != null) return;

        var existing = transform.Find(PivotName);
        if (existing != null) { Pivot = existing; return; }

        var go = new GameObject(PivotName);
        Pivot = go.transform;
        Pivot.SetParent(transform, false);

        // **子の付け替えはしない。**
        // GoblinCarryRig は毎 LateUpdate で全ボーンの **world 位置**を基準姿勢から直接
        // 書き込む (`bone.position = Posture.position + Posture.rotation * ...`)。
        // つまりボーンは親の回転を無視するので、親を傾けても見た目は 1 mm も変わらない。
        // 体を傾けるには「リグが姿勢を組み立てる基準」そのものを差し替える必要がある。
        // このピボットはその基準を運ぶためだけに存在する。
        var rig = GetComponent<GoblinCarryRig>();
        if (rig != null) rig.postureRoot = Pivot;
    }

    void LateUpdate()
    {
        EnsurePivot();

        // **計測は毎フレーム行う** (2026-08-16 修正)。以前は空中でサンプリングを
        // スキップしていたため、GroundDistance が滞空中「接地時の値のまま凍結」し、
        // これを滞空判定に使うパリー (GoblinPotActions) が一度も発動しなかった。
        // 傾きの「適用」だけを接地時に限定する。
        Vector3 n = SampleGroundNormal();
        Vector3 target = Vector3.up;
        bool grounded = controller == null || controller.isGrounded;
        if (grounded || !levelWhileAirborne)
        {
            // 最大角で頭打ちにする。急斜面で体が寝てしまうのを防ぐ。
            float ang = Vector3.Angle(Vector3.up, n);
            if (ang > maxTiltDeg && ang > 1e-3f)
                n = Vector3.Slerp(Vector3.up, n, maxTiltDeg / ang);
            target = Vector3.Slerp(Vector3.up, n, tiltStrength);
        }

        // 時間刻みに依存しない指数追従。フレームレートが変わっても同じ速さで傾く。
        float k = 1f - Mathf.Exp(-responseSpeed * Time.deltaTime);
        Vector3 desired = Vector3.Slerp(smoothedNormal, target, k).normalized;
        // 運搬中は角速度上限をかける (追補 18)。指数追従は差分が大きいほど初速が
        // 速くなる (18° 差で ~60°/s) ので、レートで頭打ちにする。
        if (gentleMode && carryTiltRateDeg > 0f)
        {
            float ang = Vector3.Angle(smoothedNormal, desired);
            float maxAng = carryTiltRateDeg * Time.deltaTime;
            if (ang > maxAng) desired = Vector3.Slerp(smoothedNormal, desired, maxAng / ang).normalized;
        }
        smoothedNormal = desired;

        // root は yaw だけ。そこへ地面法線ぶんの傾きを world 側から掛ける。
        Pivot.rotation = Quaternion.FromToRotation(Vector3.up, smoothedNormal) * transform.rotation;
    }

    // 足元 5 点の平均法線。1 本だと石畳の目地で跳ねる。
    // 摩擦は「真下の 1 点」で決める（平均すると境目で中途半端な値になり、
    // 氷の坂に乗った瞬間がぼやける）。
    Vector3 SampleGroundNormal()
    {
        Vector3 f = transform.forward, r = transform.right;
        Vector3 sum = Vector3.zero;
        int hits = 0;
        float friction = 1f;
        bool got = false;

        float centreDist = 999f;
        sum += Probe(Vector3.zero, ref hits, ref friction, ref got, ref centreDist);
        float ignored = 1f;
        bool gotF = false, gotB = false, gotR = false, gotL = false;
        float dF = 999f, dB = 999f, dR = 999f, dL = 999f;
        sum += Probe(f * probeForward, ref hits, ref ignored, ref gotF, ref dF);
        sum += Probe(-f * probeForward, ref hits, ref ignored, ref gotB, ref dB);
        sum += Probe(r * probeSide, ref hits, ref ignored, ref gotR, ref dR);
        sum += Probe(-r * probeSide, ref hits, ref ignored, ref gotL, ref dL);

        GroundFriction = friction;
        HasGround = hits > 0;
        // 2026-08-22: 足場の縁に立つと中心レイだけが空振りして GroundDistance=999 になり、
        // 「地面に立っているのに滞空扱い」→偽の生着地ジョルトが出ていた (実測: 道の縁で
        // 滞空 30 秒判定)。中心が外れたときは 5 点の最小距離へフォールバックする。
        GroundDistance = Mathf.Min(centreDist, Mathf.Min(Mathf.Min(dF, dB), Mathf.Min(dR, dL)));

        if (hits == 0) return Vector3.up;
        Vector3 n = sum.normalized;
        return n.sqrMagnitude < 1e-6f ? Vector3.up : n;
    }

    Vector3 Probe(Vector3 offset, ref int hits, ref float friction, ref bool gotFriction, ref float dist)
    {
        Vector3 origin = transform.position + offset + Vector3.up * probeUp;
        RaycastHit hit;
        if (Physics.Raycast(origin, Vector3.down, out hit, probeUp + probeDown, groundMask, QueryTriggerInteraction.Ignore))
        {
            // 自分自身のコライダーは無視する
            if (hit.collider.transform.IsChildOf(transform)) return Vector3.zero;
            hits++;
            if (!gotFriction)
            {
                var surf = hit.collider.GetComponentInParent<GroundSurface>();
                friction = surf != null ? surf.friction : 1f;
                gotFriction = true;
                dist = hit.distance - probeUp;   // 足元からの距離
            }
            return hit.normal;
        }
        return Vector3.zero;
    }
}
