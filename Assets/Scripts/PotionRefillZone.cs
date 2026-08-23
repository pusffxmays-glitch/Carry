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
    [Tooltip("落水が当たる範囲 (このオブジェクト中心のワールド AABB 半径)。autoFit が有効なら X/Z は実測で上書きされる。")]
    public Vector3 halfExtents = new Vector3(1.2f, 1.6f, 1.2f);

    // 2026-08-23: 範囲を目測で置いていたため「明らかに滝の下なのに補充されず、滝の手前で補充される」
    // 状態になっていた。滝は崖面を伝って落ちるので高さによって位置が大きく変わり、実測すると
    // 壺の口の高さ (y≒-3) では中心 x=0.55 / z=-34.9 の**幅 8.8m・厚み 1.0m のカーテン**だった。
    // 手で置いた範囲は x が 4.35m ずれていた。二度とずれないよう、滝の粒子そのものから合わせる。
    [Header("滝の位置に自動で合わせる")]
    [Tooltip("滝の粒子の実測位置から、範囲の中心と幅 (X/Z) を自動で決める。高さ (Y) はこのオブジェクトの位置と halfExtents.y をそのまま使う。")]
    public bool autoFitToWaterfall = true;
    [Tooltip("滝の FluidCore。未設定なら壺以外の FluidCore を自動で拾う。")]
    public FluidCore waterfall;
    [Tooltip("測り始めるまでの待ち時間 (s)。滝の水が下まで届いてから測る。")]
    public float autoFitDelay = 5f;
    [Tooltip("測り直す間隔 (s)。1 回では滝の一部しか捉えられないので、間を空けて複数回測り、結果を重ね合わせる。")]
    public float autoFitInterval = 2f;
    [Tooltip("測る回数。多いほど滝の広がりを取りこぼさない。")]
    public int autoFitSamples = 8;
    [Tooltip("落下中とみなす下向き速度 (m/s)。滝つぼに溜まった水を除いて、落ちている water だけを測るため。")]
    public float fallingSpeed = 2f;
    [Tooltip("落水の有無を記録する升目の大きさ (m)。壺の口の直径 (~0.9m) と同程度にする。")]
    public float autoFitCellSize = 0.7f;
    [Tooltip("升目を「落水あり」とみなす最小粒子数。")]
    public int autoFitMinPerCell = 3;

    // 滝は 1 本とは限らない。実測ではこのステージの滝は **3 本に分かれて** おり
    // (x≒-5.7 に 63 粒子 / x≒-4.0 に 102 / x≒+0.7 に 269)、中央値で 1 点に寄せると
    // 本流と支流の谷間 (x=-3.49) を指してしまう。手で置いた範囲もたまたま支流の上にあり、
    // 「滝の手前で補充されるのに本流の下では補充されない」状態になっていた。
    // そこで中心を 1 点に決めるのをやめ、**落水がある升目を記録して、壺の口がその上に
    // あるかを見る**。滝が何本あっても、途中で形が変わっても正しく効く。
    // 升目はワールド原点に固定した格子。こうしておくと、複数回の計測結果をそのまま
    // 重ね合わせられる (1 回だけの計測では滝の一部しか捉えられず、実際に本流を取りこぼした)。
    bool fitted;
    readonly System.Collections.Generic.HashSet<long> waterCells = new System.Collections.Generic.HashSet<long>();
    int fitRuns;
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
        if (waterfall == null)
            foreach (var fc in FindObjectsByType<FluidCore>(FindObjectsSortMode.None))
                if (fc != potFluid) { waterfall = fc; break; }
        if (autoFitToWaterfall) StartCoroutine(AutoFitLoop());
    }

    /// <summary>滝の粒子の実測位置から範囲の中心と幅 (X/Z) を決める。宣言部の注記を参照。</summary>
    System.Collections.IEnumerator AutoFitLoop()
    {
        yield return new WaitForSeconds(autoFitDelay);
        for (int k = 0; k < Mathf.Max(1, autoFitSamples); k++)
        {
            FitOnce();
            if (autoFitInterval <= 0f) yield break;
            yield return new WaitForSeconds(autoFitInterval);
        }
    }

    void FitOnce()
    {
        if (waterfall == null || waterfall.PositionsBuffer == null || waterfall.VelocitiesBuffer == null) return;
        int n = waterfall.PositionsBuffer.count;
        var pos = new Vector3[n];
        var vel = new Vector3[n];
        var flg = new uint[waterfall.RetiredFlagsBuffer.count];
        // 1 回だけの読み戻しなので同期で済ませる (毎フレームやると GPU を待たせる)。
        waterfall.PositionsBuffer.GetData(pos);
        waterfall.VelocitiesBuffer.GetData(vel);
        waterfall.RetiredFlagsBuffer.GetData(flg);

        float y0 = transform.position.y - halfExtents.y;
        float y1 = transform.position.y + halfExtents.y;
        var xs = new System.Collections.Generic.List<float>();
        var zs = new System.Collections.Generic.List<float>();
        for (int i = 0; i < n; i++)
        {
            if (i < flg.Length && flg[i] != 0u) continue;      // 消滅/定着した粒子は除く
            if (vel[i].y > -fallingSpeed) continue;            // 滝つぼに溜まった水を除く
            var p = pos[i];
            if (p.y < y0 || p.y > y1) continue;
            xs.Add(p.x); zs.Add(p.z);
        }
        if (xs.Count < 20)
        {
            Debug.LogWarning($"PotionRefillZone: 滝の粒子が高さ {y0:F1}〜{y1:F1} に {xs.Count} 個しか無く、自動調整できません。" +
                             " このオブジェクトの Y か halfExtents.y を滝に合わせてください。", this);
            return;
        }
        // 升目に落として「落水あり」の場所を記録する。滝が複数本でもそのまま拾える。
        // 格子はワールド原点に固定しているので、複数回の計測をそのまま重ねられる。
        float cs = Mathf.Max(0.2f, autoFitCellSize);
        var counts = new System.Collections.Generic.Dictionary<long, int>(xs.Count);
        for (int i = 0; i < xs.Count; i++)
        {
            long key = CellKey(xs[i], zs[i], cs);
            counts[key] = counts.TryGetValue(key, out var v) ? v + 1 : 1;
        }
        int added = 0;
        foreach (var kv in counts)
            if (kv.Value >= Mathf.Max(1, autoFitMinPerCell) && waterCells.Add(kv.Key)) added++;
        fitRuns++;
        fitted = waterCells.Count > 0;
        Debug.Log($"PotionRefillZone: 滝を計測 ({fitRuns} 回目) 標本 {xs.Count} 粒子 → " +
                  $"落水のある升目 +{added} (累計 {waterCells.Count}, 升目 {cs:F2}m, 高さ {y0:F1}〜{y1:F1})", this);
    }

    static long CellKey(float x, float z, float cs)
    {
        int cx = Mathf.FloorToInt(x / cs);
        int cz = Mathf.FloorToInt(z / cs);
        return ((long)cx << 32) ^ (uint)cz;
    }

    /// <summary>その位置に落水があるか。無ければ 0、真上なら 1、隣の升目まではなだらかに落ちる。</summary>
    float WaterAt(Vector3 world)
    {
        if (!fitted) return 0f;
        float cs = Mathf.Max(0.2f, autoFitCellSize);
        int cx = Mathf.FloorToInt(world.x / cs);
        int cz = Mathf.FloorToInt(world.z / cs);
        float best = 0f;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int ax = cx + dx, az = cz + dz;
                if (!waterCells.Contains(((long)ax << 32) ^ (uint)az)) continue;
                // 升目の中心からの距離でなだらかに落とす (端に立つと細くなる)。
                Vector2 c = new Vector2((ax + 0.5f) * cs, (az + 0.5f) * cs);
                float d = Vector2.Distance(new Vector2(world.x, world.z), c);
                best = Mathf.Max(best, Mathf.Clamp01(1f - Mathf.Max(0f, d - cs * 0.5f) / cs));
            }
        return best;
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
            // 高さは常に「このオブジェクトの Y ± halfExtents.y」で見る。水平は実測に合わせた
            // 升目 (fitted) を使い、まだ測れていなければ従来どおり halfExtents の箱で見る。
            Vector3 d = mouth - transform.position;
            bool inside = Mathf.Abs(d.y) <= halfExtents.y;
            float water = 0f;
            if (inside)
            {
                water = fitted ? WaterAt(mouth)
                      : (Mathf.Abs(d.x) <= halfExtents.x && Mathf.Abs(d.z) <= halfExtents.z ? 1f : 0f);
                inside = water > 0f;
            }
            // 口が横や下を向いていたら入らない (転倒して転がっている壺は補充しない)
            bool upright = Vector3.Angle(c.up, Vector3.up) <= maxTiltDeg;
            if (inside && upright)
            {
                // 落水の真下ほど太く、端に立つほど細くする。
                FlowFactor = Mathf.Clamp01(1f - centreFalloff * (1f - water));
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
        if (fitted)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.8f);   // 落水があると実測した升目
            float cs = Mathf.Max(0.2f, autoFitCellSize);
            foreach (var key in waterCells)
            {
                int cx = (int)(key >> 32);
                int cz = (int)(uint)key;
                Gizmos.DrawWireCube(new Vector3((cx + 0.5f) * cs, transform.position.y, (cz + 0.5f) * cs),
                                    new Vector3(cs, halfExtents.y * 2f, cs));
            }
        }
    }
#endif
}
