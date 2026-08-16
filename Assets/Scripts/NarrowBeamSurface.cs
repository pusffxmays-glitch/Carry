using UnityEngine;

// 細い足場 (平均台) のマーカー。これが付いた Collider の上では
// ゴブリンが綱渡り歩き (GoblinRopeGait) になり、移動速度が落ちる。
// GroundSurface と同様、挙動そのものは持たないマーカーコンポーネント。
public class NarrowBeamSurface : MonoBehaviour
{
    [Tooltip("この足場の上での移動速度倍率。")]
    [Range(0.2f, 1f)] public float speedMultiplier = 0.55f;
}
