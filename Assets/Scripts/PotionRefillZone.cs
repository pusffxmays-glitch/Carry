using UnityEngine;

// ============================================================================================
// PotionRefillZone -- ポーションが湧き出ている場所 (滝の落水プール) での壺の補充。
// 2026-08-17 追加 (要望「川のなかでポーションが湧き出ている箇所で、ツボの中に
// ポーションを補充できるようにしてほしい」)。
//
// 仕組み: FluidCore には滝用の再スポーン機構 (spawnBox) が既にある。Retired になった
// 粒子を指定ボックス内へ再配置するもので、質量収支 (§16) を壊さない唯一の「戻し方」。
// このゾーンに壺を担いだゴブリンが立っている間だけ、壺の FluidCore の spawnBox を
// **壺の口の内側** に向けて有効化する。こぼれて Retired になっていた粒子が壺の中へ
// 湧き直し、PotMass が実際に増えるのでゲージ (FillFraction01) も上がる。
// ゾーンを出るか満杯になったら spawnBox を確実に無効へ戻す。
//
// 補充できる量 = それまでに失った量 (Retired の在庫)。地面の水たまりは
// groundLifetime (10s) 経過で Retired になるので、こぼした直後の分は少し遅れて
// 補充可能になる。
// ============================================================================================
public class PotionRefillZone : MonoBehaviour
{
    [Tooltip("補充が効く範囲 (このオブジェクト中心のワールド AABB 半径)。")]
    public Vector3 halfExtents = new Vector3(3f, 1.5f, 4f);
    [Tooltip("この充填率に達したら補充を止める。0.95 (満杯) の少し手前で止めて溢れを防ぐ。")]
    [Range(0.5f, 1f)] public float stopAtFill = 0.98f;
    [Tooltip("湧き直しの初速 (壺ローカルの下向き成分, m/s)。")]
    public float pourSpeed = 1.5f;
    [Tooltip("壺内の再スポーン箱の大きさ (m)。リム半径 (~0.45m) より小さくして壁と重ねない。")]
    public Vector3 spawnBoxSize = new Vector3(0.5f, 0.15f, 0.5f);
    [Tooltip("再スポーン箱の高さ: リム高さに対する割合。1 でリム面、低いほど深い位置。")]
    [Range(0.3f, 0.95f)] public float spawnHeightFraction = 0.75f;

    GoblinPotActions actions;
    FluidCore potFluid;
    bool refilling;

    void Start()
    {
        actions = FindFirstObjectByType<GoblinPotActions>();
        // 壺の FluidCore は開始時ゴブリンの子 (Carry_Pot)。落水などで一時的に
        // 親から外れるので、ここで一度だけ掴んで保持する。
        if (actions != null) potFluid = actions.GetComponentInChildren<FluidCore>();
    }

    void Update()
    {
        bool want = false;
        if (actions != null && potFluid != null && actions.Carrying)
        {
            Vector3 d = actions.transform.position - transform.position;
            bool inside = Mathf.Abs(d.x) <= halfExtents.x
                       && Mathf.Abs(d.y) <= halfExtents.y
                       && Mathf.Abs(d.z) <= halfExtents.z;
            want = inside && potFluid.FillFraction01 < stopAtFill;
        }

        if (want)
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
            refilling = true;
        }
        else if (refilling)
        {
            // ゾーン退出・満杯・壺を手放した、のどれでも確実に無効へ戻す
            potFluid.spawnBoxSize = Vector3.zero;
            potFluid.spawnVelocity = Vector3.zero;
            refilling = false;
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
