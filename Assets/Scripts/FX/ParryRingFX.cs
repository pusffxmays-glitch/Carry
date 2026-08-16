using UnityEngine;

// ============================================================================================
// ParryRingFX -- 着地クッション (パリー) 成功時の足元リング衝撃波 (2026-08-16 追補 20)。
//
// アセット不要: クアッド 1 枚 + Custom/ParryRing シェーダーをコードから生成し、
// 使い回す (毎回 Instantiate しない)。色は HDR で渡して Bloom に滲ませる。
// グッド = シアン / ジャスト = 金色 は GoblinPotActions 側で指定。
// ============================================================================================
public class ParryRingFX : MonoBehaviour
{
    static ParryRingFX instance;

    const float Duration = 0.45f;
    const float RingSize = 3.2f;   // クアッドの一辺 (m)。リング最大径はこの ~0.95 倍

    Material mat;
    MeshRenderer mr;
    float t = -1f;

    /// <summary>足元にリングを出す。color は HDR (成分 >1) で Bloom が滲む。</summary>
    public static void Spawn(Vector3 groundPos, Color color)
    {
        if (instance == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ParryRingFX";
            Destroy(go.GetComponent<Collider>());
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 水平に寝かせる
            go.transform.localScale = Vector3.one * RingSize;
            instance = go.AddComponent<ParryRingFX>();
            instance.mat = new Material(Shader.Find("Custom/ParryRing")) { hideFlags = HideFlags.HideAndDontSave };
            instance.mr = go.GetComponent<MeshRenderer>();
            instance.mr.sharedMaterial = instance.mat;
            instance.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            instance.mr.receiveShadows = false;
            instance.mr.enabled = false;
        }
        instance.transform.position = groundPos + Vector3.up * 0.05f;
        instance.mat.SetColor("_Color", color);
        instance.mat.SetFloat("_Progress", 0f);
        instance.t = 0f;
        instance.mr.enabled = true;
    }

    void Update()
    {
        if (t < 0f) return;
        t += Time.deltaTime;
        float p = Mathf.Clamp01(t / Duration);
        mat.SetFloat("_Progress", p);
        if (p >= 1f) { t = -1f; mr.enabled = false; }
    }
}
