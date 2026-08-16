using UnityEngine;

// 熱い床 (マグマ) のマーカー (2026-08-16 ギミック 9)。
// これが付いた Collider の上に着地すると強制的に高くジャンプさせられる。
public class HotFloorSurface : MonoBehaviour
{
    [Tooltip("強制ジャンプの初速 (m/s)。通常ジャンプ 6 より高く。")]
    public float launchSpeed = 8.5f;
}
