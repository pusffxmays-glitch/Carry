using System.Collections.Generic;
using UnityEngine;

// 川 / 水場のボリューム (2026-08-16 ギミック 8)。
// BoxCollider (isTrigger) の範囲が水。上面が水面。transform.forward が流れの向き。
// GoblinSwimmer が毎フレーム All を見て入水判定する (トリガーイベントに頼らない)。
public class WaterVolume : MonoBehaviour
{
    [Tooltip("流れの速さ (m/s)。向きは transform.forward。")]
    public float flowSpeed = 1.2f;

    public static readonly List<WaterVolume> All = new List<WaterVolume>();

    BoxCollider box;

    void OnEnable() { box = GetComponent<BoxCollider>(); All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public Vector3 FlowWorld => transform.forward * flowSpeed;
    public float SurfaceY => box != null ? box.bounds.max.y : transform.position.y;

    public bool ContainsXZ(Vector3 worldPos)
    {
        if (box == null) return false;
        Bounds b = box.bounds;
        return worldPos.x >= b.min.x && worldPos.x <= b.max.x
            && worldPos.z >= b.min.z && worldPos.z <= b.max.z
            && worldPos.y <= b.max.y + 0.2f;   // 水面より大きく上にいるときは対象外
    }
}
