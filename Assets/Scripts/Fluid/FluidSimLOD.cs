using UnityEngine;

// ============================================================================================
// FluidSimLOD -- 距離に応じて流体シミュレーションの更新頻度を落とす (2026-08-22)。
//
// 滝はプレイヤーが湖の近くにいるときしかよく見えないのに、コース全域で毎フレーム
// フルコスト (壺の約半分 = ~11ms/frame) を払っていた。粒子 2400 個に対してこの重さなのは
// ディスパッチ固定費が支配的なため、Step の頻度を 1/N にするとほぼ比例して軽くなる。
// dt は FluidCore 側で積算されるので、シミュレーションの進みは実時間のまま
// (遠距離では更新が 2-3 フレームに 1 回のコマ送りになるが、距離があるので視認できない)。
// ============================================================================================
[RequireComponent(typeof(FluidCore))]
public class FluidSimLOD : MonoBehaviour
{
    [Tooltip("距離を測る相手。未設定ならゴブリンを自動で掴む。")]
    public Transform target;
    [Tooltip("この距離 (m) 以内では毎フレーム更新する。")]
    public float nearDistance = 30f;
    [Tooltip("遠いときの更新間隔 (N フレームに 1 回)。")]
    [Range(1, 6)] public int farStepInterval = 3;

    FluidCore core;

    void Awake()
    {
        core = GetComponent<FluidCore>();
        if (target == null)
        {
            var loco = FindFirstObjectByType<GoblinLocomotion>();
            if (loco != null) target = loco.transform;
        }
    }

    void Update()
    {
        if (core == null || target == null) return;
        float d = Vector3.Distance(target.position, transform.position);
        core.stepEveryNFrames = d > nearDistance ? farStepInterval : 1;
    }
}
