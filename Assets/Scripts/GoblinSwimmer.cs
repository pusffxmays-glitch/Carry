using UnityEngine;

// 入水判定と浮きの目標高さ (2026-08-16 川ギミック)。
// GoblinLocomotion (浮力・流れ・泳ぎ速度) と GoblinCarryRig (バタ足歩容) が読む。
public class GoblinSwimmer : MonoBehaviour
{
    [Tooltip("水面からどれだけ沈んで浮くか (m)。0.75 で胸まで浸かる。")]
    public float immersionDepth = 0.75f;
    [Tooltip("ぷかぷかの振幅 (m)。")]
    public float bobAmplitude = 0.05f;
    [Tooltip("ぷかぷかの周波数 (Hz)。")]
    public float bobFrequency = 0.5f;
    [Tooltip("水中の移動入力の速度倍率。")]
    public float swimSpeedMultiplier = 0.5f;
    [Tooltip("この深さ以上の水でだけ浮く (浅瀬は歩ける)。")]
    public float minFloatDepth = 0.5f;

    public bool InWater { get; private set; }
    public float SurfaceY { get; private set; }
    public Vector3 Flow { get; private set; }
    public float FloatTargetY => SurfaceY - immersionDepth
        + bobAmplitude * Mathf.Sin(Time.time * bobFrequency * 2f * Mathf.PI);

    void Update()
    {
        InWater = false;
        Flow = Vector3.zero;
        foreach (var w in WaterVolume.All)
        {
            if (w == null || !w.ContainsXZ(transform.position)) continue;
            // 足元 (root) が水面より十分下がれる深さか
            float depth = w.SurfaceY - transform.position.y;
            if (depth < -0.2f) continue;              // まだ水面よりずっと上 (落下中は対象)
            InWater = true;
            SurfaceY = w.SurfaceY;
            Flow = w.FlowWorld;
            break;
        }
    }
}
