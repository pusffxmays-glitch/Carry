using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
// FluidBoundary -- 容器の境界粒子を作り、World 空間での位置と速度を毎フレーム供給する。
//
// FLUID_DESIGN.md §3 / §10 / §21。
//
// 設計上の要点:
//  * 境界は「動く壁」であり、Fluid への容器の運動の伝達経路はこれ **だけ** である (§2)。
//    疑似重力 (-a_container) は存在しない。
//  * 境界粒子は容器ローカルに固定され、World では毎フレーム移動する。
//    V_boundary = v_container + cross(omega_container, p_b - center)
//  * **リムより上には境界粒子を置かない** (§22/修正6)。そこが Open Boundary になる。
//  * 壁シェルは 1 組で内側・外側の両方に効く。SPH の境界は密度で押し返すので、
//    内側の液体も、外壁を伝って落ちる液体も、同じ粒子群が壁として機能する。
// ============================================================================================
public class FluidBoundary : MonoBehaviour
{
    public enum Mode { Box, PotProfile }

    [Header("Shape")]
    public Mode mode = Mode.Box;
    [Tooltip("Box モード: 内寸 (m)。")]
    public Vector3 boxInnerSize = new Vector3(1.0f, 1.2f, 1.0f);
    [Tooltip("PotProfile モード: 内部形状を測るメッシュ。未指定ならこの GameObject。")]
    public Transform meshSource;

    [Header("Container")]
    [Tooltip("動く容器の Transform。未指定ならこの GameObject。")]
    public Transform container;
    [Tooltip("壁シェルの厚み。カーネル半径の倍数。1 未満だと液体がカーネルの穴から染み出す。")]
    [Range(1f, 2.5f)] public float shellThicknessPerKernel = 1.15f;
    [Tooltip("Akinci psi の開口端クランプ。壁内部の sum に対してこの倍率までしか psi を上げない。1 に近いほど強くクランプする。")]
    [Range(1.05f, 4f)] public float edgeVolumeClamp = 1.25f;
    [Tooltip("リム(開口端)から下へ、この本数分のカーネル半径だけ境界の密度寄与をフェードさせる。0 で無効。壁の斥力が開口部で液体を持ち上げ、実際のリムより高い堰を作るのを防ぐ (OI-1)。")]
    [Range(0f, 2.5f)] public float rimFadePerKernel = 1.0f;
    [Tooltip("切り分け用。壺の内径プロファイルを半径一定の円筒に置き換える。0 で無効。本番では使わない (OI-1 解析)。")]
    public float debugForceCylinderRadius = 0f;

    // ---- 生成結果（容器ローカル） ----
    public Vector3[] LocalPositions { get; private set; }
    public Vector3[] LocalNormals { get; private set; }
    public float[] Volumes { get; private set; }
    public int Count => LocalPositions != null ? LocalPositions.Length : 0;

    /// <summary>容器が保持できる内容積 (m^3, World スケール)。</summary>
    public float InteriorVolumeWorld { get; private set; }
    public PotInteriorProfile Profile { get; private set; }

    // ---- 運動 ----
    public Vector3 LinearVelocity { get; private set; }
    public Vector3 AngularVelocity { get; private set; }

    // §21 テレポート検出。
    // 容器が 1 フレームで運搬中にはあり得ない距離/角度を飛んだら、それは
    // 「運動」ではなく「瞬間移動」である。差分をそのまま速度にすると、
    // 境界粒子が数十 m/s で動いたことになり中身が吹き飛ぶ。
    // 実際に起きた例: ゲーム開始直後、Carry_Pot がシリアライズ位置から
    // GoblinCarryRig が計算する手の中央へ 1 フレームで移動し、液体が飛び散った。
    // §21「Pot Linear Velocity: Transform 差分（**平滑化**・テレポート検出）」。
    //
    // 流体が見る容器の姿勢は、Transform をそのまま使わず速度制限つきで追従させる。
    // ゲーム中の実測では、ゴブリンのよろけで Carry_Pot の Transform が
    // **一瞬 15.5 m/s** に跳ねる（歩行 1.0 / 走行 3.0 / 旋回 110 deg/s しかないので、
    // これはリグの計算が飛んだ結果であって運搬の動きではない）。
    // その速度をそのまま壁に与えると、CFL が満たせず流体が発散する。
    // 上限は通常の操作を一切削らない値に取ってあるので、普通に動かす分には影響しない。
    [Header("Motion smoothing / teleport (§21)")]
    // ---- ここは「見えている壺の位置に液体を置く」ための設定である ----
    //
    // 上限を実操作に近い値に絞ると、**流体が見る壁が見えている壺より後ろに置かれる**。
    // 速度・加速度を制限した追従は、等速で動いている間も定常的な位置ずれが残るためで、
    // 画面上は「ポーションが壺に遅れてついてくる」ように見える（ユーザー報告）。
    //
    // 実測（SampleMotion を dt=1/60 で手回し、simMaxSpeed=5 / simMaxAccel=70 のとき）:
    //   走り 3.0 m/s ......... 41.7mm 遅れる
    //   ジャンプ v0=6 m/s .... 222mm 遅れる（壺の実速度は 6.56 m/s に達する）
    // 壺の内径が約 460mm なので、222mm は誰の目にも分かるずれ。
    //
    // したがって上限は「運搬でありえる動き」を **一切削らない** 値に取る。
    // ここで削ってよいのは、リグの計算が飛んだときの一瞬の跳ね（実測 15.5 m/s）だけ。
    [Tooltip("流体が見る容器の最大並進速度 (m/s)。ジャンプ時の実測 6.6 を十分上回る値にすること。下げると液体が壺に遅れてついてくる。")]
    public float simMaxSpeed = 12f;
    [Tooltip("流体が見る容器の最大角速度 (deg/s)。旋回 110 を十分上回る値。")]
    public float simMaxAngularSpeed = 720f;
    // **加速度制限は既定で無効**。
    // もともとはジャンプ着地の瞬間停止で液体が噴き上がるのを抑えるために入れたが、
    // その噴き上がりの真因は locomotion の重力 (-20) と Physics.gravity (-9.81) の
    // 食い違いで、DynamicsManager 側を -20 に揃えて解決済み（着地の損失 14.5% → 0.5%）。
    // 一方でこの制限は等速移動中にも位置ずれを残すので、見た目の害の方が大きい。
    [Tooltip("流体が見る容器の最大並進加速度 (m/s^2)。0 で無効（既定）。有効にすると等速移動中も位置ずれが残り、液体が壺に遅れてついてくる。")]
    public float simMaxAccel = 0f;
    [Tooltip("実際の姿勢との位置ずれがこれを超えたらテレポートとみなして追いつく (m)。")]
    public float teleportDistance = 0.6f;
    [Tooltip("実際の姿勢との角度ずれがこれを超えたらテレポートとみなして追いつく (deg)。")]
    public float teleportAngle = 100f;
    /// <summary>直近の SampleMotion でテレポートを検出したか。</summary>
    public bool TeleportedThisStep { get; private set; }
    /// <summary>テレポート前の姿勢から後の姿勢への剛体変換。中身をそのまま連れて行くために使う。</summary>
    public Matrix4x4 TeleportDelta { get; private set; } = Matrix4x4.identity;
    /// <summary>流体が見る容器の位置（平滑化済み）。</summary>
    public Vector3 SimPosition => simPosition;
    /// <summary>流体が見る容器の回転（平滑化済み）。</summary>
    public Quaternion SimRotation => simRotation;
    public Vector3 CenterWorld => simPosition;
    public Transform Container => container != null ? container : transform;

    Matrix4x4 prevMatrix;
    Quaternion prevRotation;
    Vector3 prevPosition;
    bool motionPrimed;
    // サブステップ補間の始点。**prev* を更新する前の値**を控えておく必要がある。
    // 以前は prev* をそのまま補間に使っていたが、SampleMotion の最後で prev* を
    // 現在値に更新しているため、InterpolatedMatrix(t) が t によらず常に現在姿勢を
    // 返していた。つまり壁がサブステップで補間されず、1 サブステップ目で最終姿勢へ
    // 瞬間移動していた。急な動きで壁が流体を薙ぎ払い、発散する原因になっていた。
    Quaternion lerpFromRotation;
    Vector3 lerpFromPosition;
    // 流体が見る容器の姿勢（速度制限つきで実 Transform を追う）
    Quaternion simRotation = Quaternion.identity;
    Vector3 simPosition;
    Vector3 simVelocity;
    float containerScale = 1f;

    public float ContainerScale => containerScale;

    // ------------------------------------------------------------------------------------
    public void Build(float spacingWorld, float kernelRadiusWorld)
    {
        containerScale = Mathf.Max(1e-4f, Container.lossyScale.x);
        float shell = kernelRadiusWorld * shellThicknessPerKernel;
        int layers = Mathf.Max(2, Mathf.CeilToInt(shell / spacingWorld));

        if (mode == Mode.Box) BuildBox(spacingWorld, layers);
        else BuildPot(spacingWorld, layers, kernelRadiusWorld);

        ComputeVolumes(kernelRadiusWorld);
        ResyncMotion();
    }

    // ------------------------------------------------------------------------------------
    void BuildBox(float spacing, int layers)
    {
        var pts = new List<Vector3>();
        var nrm = new List<Vector3>();
        Vector3 half = boxInnerSize * 0.5f;
        int nx = Mathf.CeilToInt(boxInnerSize.x / spacing) + 1;
        int ny = Mathf.CeilToInt(boxInnerSize.y / spacing) + 1;
        int nz = Mathf.CeilToInt(boxInnerSize.z / spacing) + 1;

        for (int l = 0; l < layers; l++)
        {
            float off = (l + 0.5f) * spacing;
            for (int ix = 0; ix <= nx; ix++)
                for (int iz = 0; iz <= nz; iz++)
                {
                    float x = Mathf.Lerp(-half.x - off, half.x + off, ix / (float)nx);
                    float z = Mathf.Lerp(-half.z - off, half.z + off, iz / (float)nz);
                    pts.Add(new Vector3(x, -half.y - off, z)); nrm.Add(Vector3.up);
                }
            for (int iy = 0; iy <= ny; iy++)
                for (int ix = 0; ix <= nx; ix++)
                {
                    float x = Mathf.Lerp(-half.x - off, half.x + off, ix / (float)nx);
                    float y = Mathf.Lerp(-half.y, half.y, iy / (float)ny);
                    pts.Add(new Vector3(x, y, -half.z - off)); nrm.Add(Vector3.forward);
                    pts.Add(new Vector3(x, y, half.z + off)); nrm.Add(Vector3.back);
                }
            for (int iy = 0; iy <= ny; iy++)
                for (int iz = 0; iz <= nz; iz++)
                {
                    float z = Mathf.Lerp(-half.z - off, half.z + off, iz / (float)nz);
                    float y = Mathf.Lerp(-half.y, half.y, iy / (float)ny);
                    pts.Add(new Vector3(-half.x - off, y, z)); nrm.Add(Vector3.right);
                    pts.Add(new Vector3(half.x + off, y, z)); nrm.Add(Vector3.left);
                }
        }

        LocalPositions = pts.ToArray();
        LocalNormals = nrm.ToArray();
        InteriorVolumeWorld = boxInnerSize.x * boxInnerSize.y * boxInnerSize.z;
    }

    // 壺の実測内径プロファイルから、内壁・底のシェルを作る。
    // リム高さで打ち切るのが Open Boundary (§22)。
    void BuildPot(float spacing, int layers, float kernelRadiusWorld)
    {
        var mf = (meshSource != null ? meshSource : Container).GetComponentInChildren<MeshFilter>();
        Profile = PotInteriorProfile.FromMesh(mf != null ? mf.sharedMesh : null);
        if (debugForceCylinderRadius > 0f) Profile.ForceCylinder(debugForceCylinderRadius);

        float s = spacing / containerScale;      // ローカル単位の粒子間隔
        var pts = new List<Vector3>();
        var nrm = new List<Vector3>();

        // --- 内壁シェル: 高さ方向に走査し、各高さで R(y) から外側へ layers 枚 ---
        int rows = Mathf.Max(2, Mathf.CeilToInt((Profile.RimY - Profile.FloorY) / s));
        // リム上端では層数を 1 まで絞る。ここで層を外側へ並べたままにすると、
        // リム高さに **水平な環状の棚** ができ、その棚と、棚から h だけ上まで届く斥力が
        // 合わさって「実際のリムより高い堰」になる。実測ではこの堰のせいで液面が
        // 最低リム点より +0.118m 高いところで静止し、傾け続けても流れ出さなかった。
        // 実際の壺の縁も水平な棚ではなく丸い縁なので、テーパーの方が形状としても近い。
        float taperY = Profile.RimY - kernelRadiusWorld / containerScale;
        for (int iy = 0; iy <= rows; iy++)
        {
            float y = Mathf.Lerp(Profile.FloorY, Profile.RimY, iy / (float)rows);
            float rIn = Profile.RadiusAt(y);
            int layersHere = layers;
            if (y > taperY && Profile.RimY > taperY)
            {
                float t = Mathf.InverseLerp(taperY, Profile.RimY, y);
                layersHere = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(layers, 1f, t)));
            }
            for (int l = 0; l < layersHere; l++)
            {
                float r = rIn + (l + 0.5f) * s;
                int n = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * r / s));
                float phase = (iy + l) * 0.5f;       // 層ごとに位相をずらして格子縞を防ぐ
                for (int k = 0; k < n; k++)
                {
                    float a = (k + phase) / n * Mathf.PI * 2f;
                    pts.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    nrm.Add(new Vector3(-Mathf.Cos(a), 0f, -Mathf.Sin(a)));   // 内向き
                }
            }
        }

        // --- 底シェル: 床の下へ layers 枚。半径は最外シェルまで覆う ---
        float floorR = Profile.RadiusAt(Profile.FloorY) + layers * s;
        for (int l = 0; l < layers; l++)
        {
            float y = Profile.FloorY - (l + 0.5f) * s;
            int rings = Mathf.Max(1, Mathf.CeilToInt(floorR / s));
            pts.Add(new Vector3(0f, y, 0f)); nrm.Add(Vector3.up);
            for (int ir = 1; ir <= rings; ir++)
            {
                float r = ir * s;
                int n = Mathf.Max(6, Mathf.RoundToInt(2f * Mathf.PI * r / s));
                for (int k = 0; k < n; k++)
                {
                    float a = (k + l * 0.5f) / n * Mathf.PI * 2f;
                    pts.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    nrm.Add(Vector3.up);
                }
            }
        }

        LocalPositions = pts.ToArray();
        LocalNormals = nrm.ToArray();

        float capLocal = Profile.CapacityLocal;
        InteriorVolumeWorld = capLocal * containerScale * containerScale * containerScale;
    }

    // Akinci の境界体積 psi = 1 / sum_b' W。層の重なり方が不均一でも密度が暴れないための項。
    void ComputeVolumes(float h)
    {
        int n = Count;
        Volumes = new float[n];
        var world = new Vector3[n];
        var m = Container.localToWorldMatrix;
        for (int i = 0; i < n; i++) world[i] = m.MultiplyPoint3x4(LocalPositions[i]);

        float cell = h;
        var buckets = new Dictionary<Vector3Int, List<int>>(n);
        for (int i = 0; i < n; i++)
        {
            var key = new Vector3Int(Mathf.FloorToInt(world[i].x / cell), Mathf.FloorToInt(world[i].y / cell), Mathf.FloorToInt(world[i].z / cell));
            if (!buckets.TryGetValue(key, out var list)) { list = new List<int>(); buckets[key] = list; }
            list.Add(i);
        }
        var sums = new float[n];
        for (int i = 0; i < n; i++)
        {
            var key = new Vector3Int(Mathf.FloorToInt(world[i].x / cell), Mathf.FloorToInt(world[i].y / cell), Mathf.FloorToInt(world[i].z / cell));
            float sum = 0f;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!buckets.TryGetValue(key + new Vector3Int(dx, dy, dz), out var list)) continue;
                        for (int k = 0; k < list.Count; k++)
                            sum += Poly6((world[i] - world[list[k]]).sqrMagnitude, h);
                    }
            sums[i] = sum;
        }

        // psi = 1/sum は「層の重なりが不均一でも密度が暴れない」ための補正だが、
        // **開口端（リム）では別の意味を持ってしまう**。リム上の境界粒子は隣の境界粒子が
        // 少ないので sum が小さくなり、psi が跳ね上がって、出口のちょうどそこに
        // 強い斥力の壁ができる。これは「壁の形」ではなく「壁の端」に由来する数値的な産物で、
        // 液体がリムを越えられなくなる原因になる（Phase 7 実測: 最低リム点より 15cm 上に
        // 水平な液面が止まり、傾け続けても流れ出さなかった）。
        //
        // 壁の内部（層の真ん中）の sum を基準として、sum の下限をそこに合わせる。
        // これで壁としての振る舞いは変えずに、開口端だけの発散を止める。
        var sorted = (float[])sums.Clone();
        System.Array.Sort(sorted);
        float bulkSum = sorted[Mathf.Clamp(Mathf.RoundToInt(0.5f * (n - 1)), 0, n - 1)];
        float minSum = bulkSum / Mathf.Max(1.0001f, edgeVolumeClamp);

        // 開口端(リム)のフェード (OI-1)。
        // 壁は密度計算に参加することで斥力を生むが、**開口部でもそれが働く**ため、
        // リムを越えようとする液体が持ち上げられ、実際のリムより高い位置に
        // 「越えられない堰」ができる。実測では堰の高さが boundaryPressureScale に
        // 正比例した（1.0 -> 0.124m / 1.6 -> 0.138m / 2.0 -> 0.168m）ので、
        // 堰の正体は壁の斥力そのものである。
        // 壁としての形も、リムから離れた場所の斥力も変えずに、
        // 開口端の帯だけ寄与を落とす。
        float fadeBand = 0f;
        if (Profile != null && rimFadePerKernel > 0f)
            fadeBand = rimFadePerKernel * h / Mathf.Max(1e-6f, containerScale);

        float psiMin = float.MaxValue, psiMax = 0f;
        int clamped = 0, faded = 0;
        for (int i = 0; i < n; i++)
        {
            if (sums[i] < minSum) clamped++;
            float psi = 1f / Mathf.Max(sums[i], Mathf.Max(minSum, 1e-6f));
            if (fadeBand > 0f)
            {
                float d = Profile.RimY - LocalPositions[i].y;      // リム面からの深さ
                if (d < fadeBand)
                {
                    float t = Mathf.Clamp01(d / fadeBand);
                    psi *= t * t * (3f - 2f * t);                  // smoothstep
                    faded++;
                }
            }
            Volumes[i] = psi;
            psiMin = Mathf.Min(psiMin, Volumes[i]); psiMax = Mathf.Max(psiMax, Volumes[i]);
        }
        Debug.Log($"FluidBoundary: psi bulk={1f / bulkSum:F6} min={psiMin:F6} max={psiMax:F6} " +
                  $"（クランプ {clamped}/{n}, リムフェード {faded}/{n}, 帯 {fadeBand:F4} local）", this);
    }

    static float Poly6(float r2, float h)
    {
        float h2 = h * h;
        if (r2 >= h2) return 0f;
        float d = h2 - r2;
        float h9 = h2 * h2 * h2 * h2 * h;
        return 315f / (64f * Mathf.PI * h9) * d * d * d;
    }

    // ------------------------------------------------------------------------------------
    // 流体の初期配置。容器の内部を最密充填で満たす。
    public List<Vector3> GenerateSeedPoints(float spacingWorld, float targetVolumeWorld, int maxCount)
    {
        var result = new List<Vector3>(maxCount);
        var m = Container.localToWorldMatrix;

        if (mode == Mode.Box)
        {
            float s = spacingWorld;
            Vector3 half = boxInnerSize * 0.5f;
            float layerDy = s * 0.816f, rowDz = s * 0.866f;
            int layer = 0;
            for (float y = -half.y + s * 0.5f; y < half.y - s * 0.4f && result.Count < maxCount; y += layerDy, layer++)
                for (int rz = 0; result.Count < maxCount; rz++)
                {
                    float z = -half.z + s * 0.5f + rz * rowDz;
                    if (z > half.z - s * 0.4f) break;
                    float xoff = ((rz + layer) & 1) == 0 ? 0f : s * 0.5f;
                    for (int cx = 0; result.Count < maxCount; cx++)
                    {
                        float x = -half.x + s * 0.5f + cx * s + xoff;
                        if (x > half.x - s * 0.4f) break;
                        result.Add(m.MultiplyPoint3x4(new Vector3(x, y, z)));
                    }
                }
            return result;
        }

        // 壺: 目標体積に対応する液面高さまで、内径に収まるように最密充填で満たす。
        float sLocal = spacingWorld / containerScale;
        float targetLocalVol = targetVolumeWorld / (containerScale * containerScale * containerScale);
        float topY = Profile.HeightForVolume(targetLocalVol);
        float dy = sLocal * 0.816f, dz = sLocal * 0.866f;
        int lay = 0;
        // 目標高さまで詰めても粒子が余る場合は、リムまで積み増して埋める。
        // 余りを容器の原点に置くと壺の床より下になり、「漏れ」として計上されてしまう
        // （実測 842 粒子）。粒子は必ず内部の有効な位置に置く。
        float fillTop = Mathf.Min(Profile.RimY - sLocal, Mathf.Max(topY, Profile.FloorY + sLocal));
        for (float y = Profile.FloorY + sLocal * 0.5f; y <= fillTop && result.Count < maxCount; y += dy, lay++)
        {
            float maxR = Profile.RadiusAt(y) - sLocal * 0.5f;
            if (maxR <= 0f) continue;
            int rows = Mathf.CeilToInt(maxR / dz);
            for (int rz = -rows; rz <= rows && result.Count < maxCount; rz++)
            {
                float z = rz * dz;
                float off = ((rz + lay) & 1) == 0 ? 0f : sLocal * 0.5f;
                int cols = Mathf.CeilToInt(maxR / sLocal) + 1;
                for (int cx = -cols; cx <= cols && result.Count < maxCount; cx++)
                {
                    float x = cx * sLocal + off;
                    if (x * x + z * z > maxR * maxR) continue;
                    result.Add(m.MultiplyPoint3x4(new Vector3(x, y, z)));
                }
            }
        }

        // それでも足りない場合（間隔が僅かに粗い）、内部の安全な位置で埋める。
        int guard = 0;
        while (result.Count < maxCount && guard++ < maxCount * 4)
        {
            float y = Mathf.Lerp(Profile.FloorY + sLocal, fillTop, (guard * 0.6180339f) % 1f);
            float maxR = Mathf.Max(0f, Profile.RadiusAt(y) - sLocal);
            float a = guard * 2.399963f;
            float rr = maxR * Mathf.Sqrt((guard * 0.7548777f) % 1f);
            result.Add(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr)));
        }
        return result;
    }

    // ------------------------------------------------------------------------------------
    // 運動計測。Transform 差分から線速度・角速度を出す。テレポートは弾く。
    public void SampleMotion(float dt)
    {
        var t = Container;
        TeleportedThisStep = false;
        TeleportDelta = Matrix4x4.identity;

        if (!motionPrimed || dt <= 0f)
        {
            ResyncMotion();
            return;
        }

        // 補間の始点は「このフレームの直前に流体が見ていた姿勢」。
        lerpFromPosition = simPosition;
        lerpFromRotation = simRotation;

        // 実 Transform とのずれ。大きすぎるならテレポート扱いで追いつく。
        float gapDist = Vector3.Distance(t.position, simPosition);
        float gapAngle = Quaternion.Angle(t.rotation, simRotation);

        if (gapDist > teleportDistance || gapAngle > teleportAngle)
        {
            Matrix4x4 from = Matrix4x4.TRS(simPosition, simRotation, t.lossyScale);
            simPosition = t.position;
            simRotation = t.rotation;
            TeleportDelta = Matrix4x4.TRS(simPosition, simRotation, t.lossyScale) * from.inverse;
            TeleportedThisStep = true;
            LinearVelocity = Vector3.zero;
            AngularVelocity = Vector3.zero;
            // 瞬間移動した分は補間しない
            lerpFromPosition = simPosition;
            lerpFromRotation = simRotation;
            simVelocity = Vector3.zero;
        }
        else
        {
            // 速度制限つきで追従する。ここで削られるのは、運搬ではありえない
            // 一瞬の跳ねだけ。通常の歩行・走行・旋回・ジャンプは上限に届かないので、
            // MoveTowards は毎フレーム実 Transform に **ぴったり追いつく**（ずれ 0）。
            //
            // 加速度制限を入れると、上限に届いていなくても simVelocity が目標速度に
            // 遅れて追従するため、等速移動中ですら位置ずれが残り続ける。
            // それが「ポーションが壺に遅れてついてくる」の原因だったので既定で無効。
            simPosition = Vector3.MoveTowards(simPosition, t.position, simMaxSpeed * dt);
            if (simMaxAccel > 0f)
            {
                // 明示的に有効化されたときだけ、加速度でも頭を押さえる。
                Vector3 desiredVel = (simPosition - lerpFromPosition) / dt;
                simVelocity = Vector3.MoveTowards(simVelocity, desiredVel, simMaxAccel * dt);
                simPosition = lerpFromPosition + simVelocity * dt;
            }
            else
            {
                simVelocity = (simPosition - lerpFromPosition) / dt;
            }
            simRotation = Quaternion.RotateTowards(simRotation, t.rotation, simMaxAngularSpeed * dt);

            LinearVelocity = (simPosition - lerpFromPosition) / dt;
            Quaternion dq = simRotation * Quaternion.Inverse(lerpFromRotation);
            dq.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) { axis = Vector3.up; angleDeg = 0f; }
            if (angleDeg > 180f) angleDeg -= 360f;
            AngularVelocity = axis.normalized * (angleDeg * Mathf.Deg2Rad / dt);
        }

        prevMatrix = Matrix4x4.TRS(simPosition, simRotation, t.lossyScale);
        prevPosition = simPosition;
        prevRotation = simRotation;
    }

    /// <summary>容器がテレポートしたことを伝える。差分を運動として扱わない。</summary>
    public void ResyncMotion()
    {
        var t = Container;
        simPosition = t.position;
        simRotation = t.rotation;
        simVelocity = Vector3.zero;
        prevMatrix = t.localToWorldMatrix;
        prevPosition = t.position;
        prevRotation = t.rotation;
        lerpFromPosition = t.position;
        lerpFromRotation = t.rotation;
        LinearVelocity = Vector3.zero;
        AngularVelocity = Vector3.zero;
        motionPrimed = true;
    }

    /// <summary>前フレーム姿勢と現フレーム姿勢の間を補間した行列。
    ///
    /// 容器の姿勢はフレーム境界でしか分からない。サブステップごとに壁が瞬間移動すると、
    /// 壁が流体を弾き飛ばしてエネルギーを注入する。t は 0..1 のサブステップ進行度 (§3)。</summary>
    public Matrix4x4 InterpolatedMatrix(float t)
    {
        var tr = Container;
        // 流体が見る姿勢（平滑化済み）へ向かって補間する。
        Vector3 p = Vector3.Lerp(lerpFromPosition, simPosition, t);
        Quaternion q = Quaternion.Slerp(lerpFromRotation, simRotation, t);
        return Matrix4x4.TRS(p, q, tr.lossyScale);
    }

    public Vector3 InterpolatedCenter(float t) => Vector3.Lerp(lerpFromPosition, simPosition, t);

    /// <summary>フレーム開始姿勢から u まで進めたときの剛体変換。
    /// 容器が 1 フレームで「サブステップでは解けない量」動いたときに、
    /// その解けない分だけ中身を相対運動なしで運ぶために使う (§3 CFL の最終手段)。</summary>
    public Matrix4x4 CarryDelta(float u)
    {
        var tr = Container;
        Matrix4x4 from = Matrix4x4.TRS(lerpFromPosition, lerpFromRotation, tr.lossyScale);
        return InterpolatedMatrix(u) * from.inverse;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (mode == Mode.Box)
        {
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.6f);
            Gizmos.matrix = Container.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, boxInnerSize);
        }
        else if (Profile != null)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.7f);
            Gizmos.matrix = Container.localToWorldMatrix;
            for (int i = 0; i < Profile.Heights.Length; i++)
                Gizmos.DrawWireSphere(new Vector3(Profile.Radii[i], Profile.Heights[i], 0f), 0.01f);
        }
    }
#endif
}
