using UnityEngine;

// ============================================================================================
// SwayingBridge -- 左右に揺れる（ローリングする）橋。
//
// **CharacterController は動く床に自動では乗らない。**
// Rigidbody と違って摩擦で運ばれることが無いので、床が動いても足元をすり抜けるだけで
// その場に取り残される。乗っている相手を自分で運ぶ必要がある。
//
// 運び方: 前フレームの姿勢行列と今フレームの姿勢行列で、相手の足元の点がどこへ
// 移動したかを求め、その差分だけ controller.Move する。回転にも並進にも同じ式で対応でき、
// 橋の端に立っているほど大きく振られる（＝実際の板の上と同じ）。
//
// 体の傾き (GoblinTerrainTilt) は足元の法線を毎フレーム測っているので、
// 橋が傾けば体も壺も自動でそれに追従する。ここでは何もしなくてよい。
// ============================================================================================
[DefaultExecutionOrder(-20)]   // 乗っている側の移動処理より先に橋を動かす
public class SwayingBridge : MonoBehaviour
{
    [Header("Sway")]
    [Tooltip("左右の揺れ（ロール）の振幅 (度)。")]
    public float rollAmplitudeDeg = 8f;
    [Tooltip("左右の揺れの周期 (秒)。")]
    public float rollPeriod = 2.6f;
    [Tooltip("上下の揺れの振幅 (m)。")]
    public float bobAmplitude = 0.05f;
    [Tooltip("上下の揺れの周期 (秒)。ロールと違う周期にすると単調な往復に見えない。")]
    public float bobPeriod = 1.7f;
    [Tooltip("揺れの位相オフセット (秒)。複数置くときにずらす。")]
    public float phaseOffset = 0f;

    [Header("Riders")]
    [Tooltip("乗っている相手を運ぶ。切ると橋だけが動いて相手は取り残される。")]
    public bool carryRiders = true;
    [Tooltip("足元判定の Raycast 長 (m)。")]
    public float riderProbeDown = 1.2f;

    Vector3 basePos;
    Quaternion baseRot;
    Matrix4x4 prevMatrix;
    CharacterController[] riders;

    void Start()
    {
        basePos = transform.position;
        baseRot = transform.rotation;
        prevMatrix = transform.localToWorldMatrix;
        riders = FindObjectsOfType<CharacterController>();
    }

    void Update()
    {
        float t = Time.time + phaseOffset;
        float roll = Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.01f, rollPeriod)) * rollAmplitudeDeg;
        float bob = Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.01f, bobPeriod)) * bobAmplitude;

        // ロールは橋の長手方向（local Z）まわり。渡っている間ずっと左右に振られる。
        transform.rotation = baseRot * Quaternion.Euler(0f, 0f, roll);
        transform.position = basePos + Vector3.up * bob;

        if (carryRiders) CarryRiders();

        prevMatrix = transform.localToWorldMatrix;
    }

    void CarryRiders()
    {
        if (riders == null) return;
        Matrix4x4 cur = transform.localToWorldMatrix;
        Matrix4x4 prevInv = prevMatrix.inverse;

        for (int i = 0; i < riders.Length; i++)
        {
            var cc = riders[i];
            if (cc == null || !cc.enabled || !cc.gameObject.activeInHierarchy) continue;
            if (!IsStandingOnMe(cc)) continue;

            Vector3 p = cc.transform.position;
            // 「前フレームの橋の上のどこに居たか」を今フレームの橋へ移す。
            Vector3 moved = cur.MultiplyPoint3x4(prevInv.MultiplyPoint3x4(p));
            Vector3 delta = moved - p;
            if (delta.sqrMagnitude > 1e-10f) cc.Move(delta);
        }
    }

    bool IsStandingOnMe(CharacterController cc)
    {
        Vector3 origin = cc.transform.position + Vector3.up * 0.3f;
        RaycastHit hit;
        if (!Physics.Raycast(origin, Vector3.down, out hit, 0.3f + riderProbeDown,
                             ~0, QueryTriggerInteraction.Ignore)) return false;
        return hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform);
    }
}
