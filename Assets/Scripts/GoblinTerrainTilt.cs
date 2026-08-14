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

    const string PivotName = "Goblin_Tilt";

    CharacterController controller;
    Vector3 smoothedNormal = Vector3.up;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        EnsurePivot();
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

        Vector3 target = Vector3.up;
        bool grounded = controller == null || controller.isGrounded;
        if (grounded || !levelWhileAirborne)
        {
            Vector3 n = SampleGroundNormal();
            // 最大角で頭打ちにする。急斜面で体が寝てしまうのを防ぐ。
            float ang = Vector3.Angle(Vector3.up, n);
            if (ang > maxTiltDeg && ang > 1e-3f)
                n = Vector3.Slerp(Vector3.up, n, maxTiltDeg / ang);
            target = Vector3.Slerp(Vector3.up, n, tiltStrength);
        }

        // 時間刻みに依存しない指数追従。フレームレートが変わっても同じ速さで傾く。
        float k = 1f - Mathf.Exp(-responseSpeed * Time.deltaTime);
        smoothedNormal = Vector3.Slerp(smoothedNormal, target, k).normalized;

        // root は yaw だけ。そこへ地面法線ぶんの傾きを world 側から掛ける。
        Pivot.rotation = Quaternion.FromToRotation(Vector3.up, smoothedNormal) * transform.rotation;
    }

    // 足元 5 点の平均法線。1 本だと石畳の目地で跳ねる。
    Vector3 SampleGroundNormal()
    {
        Vector3 f = transform.forward, r = transform.right;
        Vector3 sum = Vector3.zero;
        int hits = 0;

        sum += Probe(Vector3.zero, ref hits);
        sum += Probe(f * probeForward, ref hits);
        sum += Probe(-f * probeForward, ref hits);
        sum += Probe(r * probeSide, ref hits);
        sum += Probe(-r * probeSide, ref hits);

        if (hits == 0) return Vector3.up;
        Vector3 n = sum.normalized;
        return n.sqrMagnitude < 1e-6f ? Vector3.up : n;
    }

    Vector3 Probe(Vector3 offset, ref int hits)
    {
        Vector3 origin = transform.position + offset + Vector3.up * probeUp;
        RaycastHit hit;
        if (Physics.Raycast(origin, Vector3.down, out hit, probeUp + probeDown, groundMask, QueryTriggerInteraction.Ignore))
        {
            // 自分自身のコライダーは無視する
            if (hit.collider.transform.IsChildOf(transform)) return Vector3.zero;
            hits++;
            return hit.normal;
        }
        return Vector3.zero;
    }
}
