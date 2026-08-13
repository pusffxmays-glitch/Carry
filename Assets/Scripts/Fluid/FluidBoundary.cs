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
    public Vector3 CenterWorld => Container.position;
    public Transform Container => container != null ? container : transform;

    Matrix4x4 prevMatrix;
    Quaternion prevRotation;
    Vector3 prevPosition;
    bool motionPrimed;
    float containerScale = 1f;

    public float ContainerScale => containerScale;

    // ------------------------------------------------------------------------------------
    public void Build(float spacingWorld, float kernelRadiusWorld)
    {
        containerScale = Mathf.Max(1e-4f, Container.lossyScale.x);
        float shell = kernelRadiusWorld * shellThicknessPerKernel;
        int layers = Mathf.Max(2, Mathf.CeilToInt(shell / spacingWorld));

        if (mode == Mode.Box) BuildBox(spacingWorld, layers);
        else BuildPot(spacingWorld, layers);

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
    void BuildPot(float spacing, int layers)
    {
        var mf = (meshSource != null ? meshSource : Container).GetComponentInChildren<MeshFilter>();
        Profile = PotInteriorProfile.FromMesh(mf != null ? mf.sharedMesh : null);

        float s = spacing / containerScale;      // ローカル単位の粒子間隔
        var pts = new List<Vector3>();
        var nrm = new List<Vector3>();

        // --- 内壁シェル: 高さ方向に走査し、各高さで R(y) から外側へ layers 枚 ---
        int rows = Mathf.Max(2, Mathf.CeilToInt((Profile.RimY - Profile.FloorY) / s));
        for (int iy = 0; iy <= rows; iy++)
        {
            float y = Mathf.Lerp(Profile.FloorY, Profile.RimY, iy / (float)rows);
            float rIn = Profile.RadiusAt(y);
            for (int l = 0; l < layers; l++)
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
            Volumes[i] = 1f / Mathf.Max(sum, 1e-6f);
        }
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
        if (!motionPrimed || dt <= 0f)
        {
            ResyncMotion();
            return;
        }

        LinearVelocity = (t.position - prevPosition) / dt;

        Quaternion dq = t.rotation * Quaternion.Inverse(prevRotation);
        dq.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) { axis = Vector3.up; angleDeg = 0f; }
        if (angleDeg > 180f) angleDeg -= 360f;
        AngularVelocity = axis.normalized * (angleDeg * Mathf.Deg2Rad / dt);

        prevMatrix = t.localToWorldMatrix;
        prevPosition = t.position;
        prevRotation = t.rotation;
    }

    /// <summary>容器がテレポートしたことを伝える。差分を運動として扱わない。</summary>
    public void ResyncMotion()
    {
        var t = Container;
        prevMatrix = t.localToWorldMatrix;
        prevPosition = t.position;
        prevRotation = t.rotation;
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
        Vector3 p = Vector3.Lerp(prevPosition, tr.position, t);
        Quaternion q = Quaternion.Slerp(prevRotation, tr.rotation, t);
        return Matrix4x4.TRS(p, q, tr.lossyScale);
    }

    public Vector3 InterpolatedCenter(float t) => Vector3.Lerp(prevPosition, Container.position, t);

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
