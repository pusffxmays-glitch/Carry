using UnityEngine;

// 足元が細い足場 (NarrowBeamSurface) かどうかを毎フレーム調べるセンサー。
// GoblinCarryRig (歩容の切り替え) と GoblinLocomotion (減速) の両方から読まれる。
public class NarrowBeamSensor : MonoBehaviour
{
    [Tooltip("足元判定のレイ長さ (m)。CharacterController の足元から下へ。")]
    public float rayLength = 0.6f;

    public bool OnBeam { get; private set; }
    public float SpeedMultiplier { get; private set; } = 1f;

    void Update()
    {
        OnBeam = false;
        SpeedMultiplier = 1f;
        // 少し上から下へ。自分の Collider は無視 (CharacterController は Raycast に当たるので回避)。
        Vector3 origin = transform.position + Vector3.up * 0.3f;
        // トリガー (水ボリューム等) は足場ではないので無視する
        var hits = Physics.RaycastAll(origin, Vector3.down, rayLength + 0.3f,
                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        NarrowBeamSurface beam = null;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;   // 自分自身
            if (h.distance < best)
            {
                best = h.distance;
                beam = h.collider.GetComponent<NarrowBeamSurface>();
            }
        }
        if (beam != null)
        {
            OnBeam = true;
            SpeedMultiplier = beam.speedMultiplier;
        }
    }
}
