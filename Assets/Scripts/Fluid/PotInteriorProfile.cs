using UnityEngine;

// Measures the pot's interior as a surface of revolution -- a radius-vs-height profile sampled from
// the actual Carry_Pot mesh -- and exposes it both to C# (fill levels, seeding, volume) and to the
// GPU (a 1D texture the fluid solver samples for collision).
//
// Extracted from the previous liquid implementation because this part was always correct and is the
// one thing the fluid solver genuinely needs to know about the vessel. Everything else about the old
// system is gone.
public class PotInteriorProfile
{
    public const int Samples = 64;

    public float[] Heights { get; private set; }
    public float[] Radii { get; private set; }
    public float[] CumulativeVolume { get; private set; }

    // ---- 外形（壺の実体がどこまであるか） ----
    //
    // 内側の形だけでは「壺の壁の中」を判定できない。等値面は最外周の粒子から
    // Splat 半径ぶん外側にふくらむので（実測 57.4mm）、壁の厚みより厚くふくらむと
    // 液体が壺の側面や底を突き抜けて描画される。壁の中には液体は存在し得ないので、
    // 密度場をここで 0 にして切り落とす。そのために外形が要る。
    //
    // 高さビンごとの **最大** 半径。内側 (Radii) が最小半径なのと対になる。
    public float[] OuterRadii { get; private set; }
    public float MeshMinY { get; private set; }
    public float MeshMaxY { get; private set; }

    public float FloorY { get; private set; }
    public float RimY { get; private set; }
    public float RimR { get; private set; }
    /// <summary>Widest interior radius anywhere in the profile (the belly).</summary>
    public float MaxRadius { get; private set; }
    /// <summary>Total interior capacity in the pot's own local units cubed.</summary>
    public float CapacityLocal => CumulativeVolume[CumulativeVolume.Length - 1];

    Texture2D profileTex;

    public static PotInteriorProfile FromMesh(Mesh mesh)
    {
        var p = new PotInteriorProfile();
        p.Build(mesh);
        return p;
    }

    /// <summary>切り分け用。半径一定の円筒に置き換える。実際の壺形状に依存しない挙動を
    /// 確認するためだけのもので、本番では使わない (OI-1 解析)。</summary>
    public void ForceCylinder(float radius)
    {
        for (int i = 0; i < Radii.Length; i++) Radii[i] = radius;
        RimR = radius;
        MaxRadius = radius;
        if (OuterRadii != null)
            for (int i = 0; i < OuterRadii.Length; i++) OuterRadii[i] = radius * 1.15f;
        float cum = 0f;
        CumulativeVolume[0] = 0f;
        for (int i = 1; i < Heights.Length; i++)
        {
            float dy = Heights[i] - Heights[i - 1];
            cum += Mathf.PI * radius * radius * dy;
            CumulativeVolume[i] = cum;
        }
        profileTex = null;   // GPU 側は作り直させる
    }

    void Build(Mesh mesh)
    {
        if (mesh == null)
        {
            Heights = new[] { 0f, 0.3f };
            Radii = new[] { 0.15f, 0.15f };
            CumulativeVolume = new[] { 0f, 0.021f };
            FloorY = 0f; RimY = 0.3f; RimR = 0.15f; MaxRadius = 0.15f;
            MeshMinY = 0f; MeshMaxY = 0.3f;
            OuterRadii = new[] { 0.17f, 0.17f };
            return;
        }

        Vector3[] verts = mesh.vertices;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < verts.Length; i++)
        {
            if (verts[i].y < minY) minY = verts[i].y;
            if (verts[i].y > maxY) maxY = verts[i].y;
        }

        const int bins = 24;
        var binMinR = new float[bins + 1];
        var binMaxR = new float[bins + 1];
        var binHas = new bool[bins + 1];
        for (int b = 0; b <= bins; b++) binMinR[b] = float.MaxValue;

        float span = Mathf.Max(0.0001f, maxY - minY);
        for (int i = 0; i < verts.Length; i++)
        {
            int b = Mathf.Clamp(Mathf.RoundToInt((verts[i].y - minY) / span * bins), 0, bins);
            float r = Mathf.Sqrt(verts[i].x * verts[i].x + verts[i].z * verts[i].z);
            if (r < binMinR[b]) binMinR[b] = r;
            if (r > binMaxR[b]) binMaxR[b] = r;
            binHas[b] = true;
        }

        MeshMinY = minY;
        MeshMaxY = maxY;
        BuildOuter(binMaxR, binHas, bins);

        float widest = 0f;
        for (int b = 0; b <= bins; b++) if (binHas[b] && binMinR[b] < 10f) widest = Mathf.Max(widest, binMinR[b]);

        // The lowest bins are the SOLID FOOT, whose measured min-radius is near zero and
        // non-monotonic (0.0015, 0.0613, 0.0239 before the real interior starts at 0.153). Taking
        // them at face value produces a needle-thin funnel under the liquid.
        int startBin = 0;
        for (int b = 0; b <= bins; b++)
        {
            if (!binHas[b]) continue;
            if (binMinR[b] >= widest * 0.55f) { startBin = b; break; }
        }

        int count = bins - startBin + 1;
        Heights = new float[count];
        Radii = new float[count];
        for (int i = 0; i < count; i++)
        {
            int b = startBin + i;
            Heights[i] = minY + (b / (float)bins) * span;
            Radii[i] = binHas[b] ? binMinR[b] : widest;
        }
        if (count >= 3)
        {
            var smoothed = (float[])Radii.Clone();
            for (int i = 1; i < count - 1; i++)
                smoothed[i] = (Radii[i - 1] + 2f * Radii[i] + Radii[i + 1]) * 0.25f;
            Radii = smoothed;
        }

        FloorY = Heights[0];
        RimY = Heights[count - 1];
        RimR = Radii[count - 1];
        MaxRadius = 0f;
        for (int i = 0; i < count; i++) MaxRadius = Mathf.Max(MaxRadius, Radii[i]);

        CumulativeVolume = new float[count];
        for (int i = 1; i < count; i++)
        {
            float dy = Heights[i] - Heights[i - 1];
            float a0 = Mathf.PI * Radii[i - 1] * Radii[i - 1];
            float a1 = Mathf.PI * Radii[i] * Radii[i];
            CumulativeVolume[i] = CumulativeVolume[i - 1] + 0.5f * (a0 + a1) * dy;
        }
    }

    // 外形を Samples 個に整えて持つ。空のビンは近傍から埋め、少しだけ滑らかにする。
    // 縄の飾りが張り出している高さでは、その張り出しぶんまで「壺の実体」として扱う。
    // 実体を広めに取る側の誤差は、液体が壺を突き抜けないという目的からは安全側。
    void BuildOuter(float[] binMaxR, bool[] binHas, int bins)
    {
        var raw = new float[bins + 1];
        float last = 0f;
        for (int b = 0; b <= bins; b++)
        {
            if (binHas[b]) last = binMaxR[b];
            raw[b] = last;
        }
        for (int b = bins; b >= 0; b--)
        {
            if (binHas[b]) last = binMaxR[b];
            else raw[b] = Mathf.Max(raw[b], last);
        }

        OuterRadii = new float[Samples];
        for (int i = 0; i < Samples; i++)
        {
            float f = i / (float)(Samples - 1) * bins;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, bins);
            int i1 = Mathf.Min(i0 + 1, bins);
            OuterRadii[i] = Mathf.Lerp(raw[i0], raw[i1], f - i0);
        }
    }

    /// <summary>壺の実体の外周半径。y は MeshMinY..MeshMaxY のローカル高さ。</summary>
    public float OuterRadiusAt(float y)
    {
        if (OuterRadii == null || OuterRadii.Length == 0) return 0f;
        float t = Mathf.InverseLerp(MeshMinY, MeshMaxY, y);
        float f = Mathf.Clamp01(t) * (OuterRadii.Length - 1);
        int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, OuterRadii.Length - 1);
        int i1 = Mathf.Min(i0 + 1, OuterRadii.Length - 1);
        return Mathf.Lerp(OuterRadii[i0], OuterRadii[i1], f - i0);
    }

    /// <summary>外形半径を Samples 個で返す（GPU の切り落とし用）。</summary>
    public float[] GetOuterProfileArray()
    {
        if (OuterRadii == null) return new float[Samples];
        return (float[])OuterRadii.Clone();
    }

    public float RadiusAt(float y)
    {
        int n = Heights.Length;
        if (y <= Heights[0]) return Radii[0];
        if (y >= Heights[n - 1]) return Radii[n - 1];
        for (int i = 1; i < n; i++)
        {
            if (y <= Heights[i])
            {
                float t = (y - Heights[i - 1]) / Mathf.Max(1e-5f, Heights[i] - Heights[i - 1]);
                return Mathf.Lerp(Radii[i - 1], Radii[i], t);
            }
        }
        return Radii[n - 1];
    }

    public float HeightForVolume(float volume)
    {
        int n = CumulativeVolume.Length;
        volume = Mathf.Clamp(volume, 0f, CumulativeVolume[n - 1]);
        if (volume <= 0f) return Heights[0];
        for (int i = 1; i < n; i++)
        {
            if (volume <= CumulativeVolume[i])
            {
                float t = (volume - CumulativeVolume[i - 1]) / Mathf.Max(1e-7f, CumulativeVolume[i] - CumulativeVolume[i - 1]);
                return Mathf.Lerp(Heights[i - 1], Heights[i], t);
            }
        }
        return Heights[n - 1];
    }

    /// <summary>Interior radius sampled at `Samples` evenly spaced heights from floor to rim, for the
    /// collision code in FluidSim.compute.</summary>
    public float[] GetProfileArray()
    {
        var a = new float[Samples];
        for (int i = 0; i < Samples; i++)
            a[i] = RadiusAt(Mathf.Lerp(FloorY, RimY, i / (float)(Samples - 1)));
        return a;
    }

    /// <summary>1D lookup of interior radius against normalised height, for the collision code in
    /// FluidSim.compute.</summary>
    public Texture2D GetProfileTexture()
    {
        if (profileTex == null)
        {
            profileTex = new Texture2D(Samples, 1, TextureFormat.RFloat, false, true)
            {
                name = "PotInteriorProfile",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var px = new Color[Samples];
            for (int i = 0; i < Samples; i++)
            {
                float t = i / (float)(Samples - 1);
                px[i] = new Color(RadiusAt(Mathf.Lerp(FloorY, RimY, t)), 0f, 0f, 0f);
            }
            profileTex.SetPixels(px);
            profileTex.Apply(false, false);
        }
        return profileTex;
    }

    public void Release()
    {
        if (profileTex != null)
        {
            if (Application.isPlaying) Object.Destroy(profileTex); else Object.DestroyImmediate(profileTex);
            profileTex = null;
        }
    }
}
