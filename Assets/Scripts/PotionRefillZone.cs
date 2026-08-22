using UnityEngine;

// ============================================================================================
// PotionRefillZone -- 滝の下に壺を差し出している間だけポーションが補充される。
//
// 仕組み: FluidCore には滝用の再スポーン機構 (spawnBox) がある。Retired になった粒子を
// 指定ボックス内へ再配置するもので、質量収支 (§16) を壊さない唯一の「戻し方」。
// このゾーンでは壺の FluidCore の spawnBox を **壺の口の内側** に向けて有効化する。
// こぼれて Retired になっていた粒子が壺の中へ湧き直し、PotMass が実際に増えるので
// ゲージ (FillFraction01) も上がる。
//
// 2026-08-22 改訂 (「滝に到達する前に補充されたり、滝に行っても補充されなかったり。
// 単純に、ツボの口から流れ込んだ分のポーションが補充される仕組みでいい」)。
// 旧実装の問題は 3 つあった:
//   (1) 判定が **ゴブリンの体** の位置で、しかも AABB が z 方向 15m もあった。落水点は
//       z≒-35.5 なのにゾーン中心は z=-31 だったので、滝へ着く手前から補充が始まっていた。
//   (2) 壺の口の位置を見ていないので、壺を滝から外していても補充された (逆も然り)。
//   (3) 再スポーンは Retired の在庫を **1 フレームで全部** 戻していたため、条件を満たした
//       瞬間に一気に回復し、「流れ込んでいる」感触が無かった。
// 現在は **壺の口 (リム中心) が落水の当たる範囲にあり、口が上を向いているときだけ**、
// FluidCore.spawnChance で流量を絞って少しずつ戻す。
//
// 補充できる総量 = それまでに失った量 (Retired の在庫)。地面の水たまりは
// groundLifetime 経過で Retired になるので、こぼした直後の分は少し遅れて補充可能になる。
// ============================================================================================
public class PotionRefillZone : MonoBehaviour
{
    [Tooltip("落水が当たる範囲 (このオブジェクト中心のワールド AABB 半径)。滝の実際の着水点に合わせて置くこと。")]
    public Vector3 halfExtents = new Vector3(1.2f, 1.6f, 1.2f);
    [Tooltip("この充填率に達したら補充を止める。満杯の少し手前で止めて溢れを防ぐ。")]
    [Range(0.5f, 1f)] public float stopAtFill = 0.98f;

    [Header("流量")]
    [Tooltip("満タンに対して毎秒どれだけ入るか (0.25 = 空から満タンまで 4 秒)。")]
    [Range(0.02f, 2f)] public float refillPerSecond = 0.25f;
    [Tooltip("範囲の中心から外れるほど流量を落とす度合い。0 なら範囲内どこでも全開。")]
    [Range(0f, 1f)] public float centreFalloff = 0.7f;
    [Tooltip("壺がこれ以上傾いていたら口に入らないとみなす (度)。")]
    [Range(0f, 90f)] public float maxTiltDeg = 55f;

    [Header("湧き出し位置 (壺の内側)")]
    [Tooltip("湧き直しの初速 (壺ローカルの下向き成分, m/s)。")]
    public float pourSpeed = 1.5f;
    [Tooltip("壺内の再スポーン箱の大きさ (m)。リム半径 (~0.45m) より小さくして壁と重ねない。")]
    public Vector3 spawnBoxSize = new Vector3(0.5f, 0.15f, 0.5f);
    [Tooltip("再スポーン箱の高さ: リム高さに対する割合。1 でリム面、低いほど深い位置。")]
    [Range(0.3f, 0.95f)] public float spawnHeightFraction = 0.75f;

    /// <summary>いま補充中か (デバッグ表示用)。</summary>
    public bool Refilling { get; private set; }
    /// <summary>直近フレームの流量係数 0..1 (デバッグ表示用)。</summary>
    public float FlowFactor { get; private set; }

    GoblinPotActions actions;
    FluidCore potFluid;

    void Start()
    {
        actions = FindFirstObjectByType<GoblinPotActions>();
        // 壺の FluidCore は開始時ゴブリンの子 (Carry_Pot)。落水などで一時的に
        // 親から外れるので、ここで一度だけ掴んで保持する。
        if (actions != null) potFluid = actions.GetComponentInChildren<FluidCore>();
    }

    void Update()
    {
        FlowFactor = 0f;
        if (potFluid != null && potFluid.Boundary != null && potFluid.FillFraction01 < stopAtFill)
        {
            var c = potFluid.Boundary.Container;
            float rimWorld = potFluid.Boundary.Profile != null
                ? potFluid.Boundary.Profile.RimY * potFluid.Boundary.ContainerScale : 0.8f;
            // 判定は **壺の口 (リム中心)**。ゴブリンの体ではない。
            Vector3 mouth = c.position + c.up * rimWorld;
            Vector3 d = mouth - transform.position;
            bool inside = Mathf.Abs(d.x) <= halfExtents.x
                       && Mathf.Abs(d.y) <= halfExtents.y
                       && Mathf.Abs(d.z) <= halfExtents.z;
            // 口が横や下を向いていたら入らない (転倒して転がっている壺は補充しない)
            bool upright = Vector3.Angle(c.up, Vector3.up) <= maxTiltDeg;
            if (inside && upright)
            {
                // 中心から外れるほど細くする。水平方向だけで見る (高さは当たり判定のみ)。
                float rx = halfExtents.x > 1e-4f ? Mathf.Abs(d.x) / halfExtents.x : 0f;
                float rz = halfExtents.z > 1e-4f ? Mathf.Abs(d.z) / halfExtents.z : 0f;
                FlowFactor = Mathf.Clamp01(1f - centreFalloff * Mathf.Clamp01(Mathf.Max(rx, rz)));
            }
        }

        if (FlowFactor > 0f)
        {
            // 箱は毎フレーム壺に追従させる (BindAll がサブステップごとに読む)。
            // 壺は傾くことがあるが、箱の半径はリム半径より十分小さいので
            // ワールド軸平行のままでも壁とは重ならない。
            var c = potFluid.Boundary.Container;
            float rimWorld = potFluid.Boundary.Profile != null
                ? potFluid.Boundary.Profile.RimY * potFluid.Boundary.ContainerScale : 0.8f;
            Vector3 centre = c.position + c.up * (rimWorld * spawnHeightFraction);
            potFluid.spawnBoxMin = centre - spawnBoxSize * 0.5f;
            potFluid.spawnBoxSize = spawnBoxSize;
            potFluid.spawnVelocity = -c.up * pourSpeed;
            // 「流れ込んだ分だけ」= 毎秒 refillPerSecond の割合を Retired の在庫から抽選で戻す。
            // 在庫が少ないほど 1 粒あたりの当選確率を上げないと流量が落ちるので、
            // 欲しい粒子数を在庫数で割って確率にする。
            int stock = Mathf.Max(1, potFluid.RetiredCount);
            float want = potFluid.FluidCount * refillPerSecond * FlowFactor * Time.deltaTime;
            potFluid.spawnChance = Mathf.Clamp01(want / stock);
            Refilling = true;
        }
        else if (Refilling)
        {
            // 範囲外・満杯・壺を手放した、のどれでも確実に無効へ戻す
            potFluid.spawnBoxSize = Vector3.zero;
            potFluid.spawnVelocity = Vector3.zero;
            potFluid.spawnChance = 1f;
            Refilling = false;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.3f, 1f, 0.4f);
        Gizmos.DrawWireCube(transform.position, halfExtents * 2f);
    }
#endif
}
