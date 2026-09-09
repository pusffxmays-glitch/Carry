using UnityEngine;

/// <summary>Blends baked distant forest views along the existing Z-axis course.</summary>
[DisallowMultipleComponent]
public sealed class ForestSkyboxBlend : MonoBehaviour
{
    public Material skyboxTemplate;
    public Texture2D[] panoramas;
    public float[] anchorZ;
    public Transform viewer;
    Material instance;
    Material previousSkybox;
    static readonly int TextureA = Shader.PropertyToID("_TextureA");
    static readonly int TextureB = Shader.PropertyToID("_TextureB");
    static readonly int Blend = Shader.PropertyToID("_Blend");

    void OnEnable()
    {
        if (!skyboxTemplate) return;
        previousSkybox = RenderSettings.skybox;
        instance = new Material(skyboxTemplate) { hideFlags = HideFlags.HideAndDontSave };
        RenderSettings.skybox = instance;
        Refresh();
    }

    void LateUpdate() => Refresh();

    public void Refresh()
    {
        if (!instance || panoramas == null || anchorZ == null ||
            panoramas.Length == 0 || panoramas.Length != anchorZ.Length) return;
        var cameraTransform = viewer ? viewer : (Camera.main ? Camera.main.transform : null);
        if (cameraTransform) SetProgress(cameraTransform.position.z);
    }

    public void SetProgress(float z)
    {
        if (!instance || panoramas == null || anchorZ == null ||
            panoramas.Length == 0 || panoramas.Length != anchorZ.Length) return;
        int a = 0;
        while (a < anchorZ.Length - 1 && z >= anchorZ[a + 1]) a++;
        int b = Mathf.Min(a + 1, anchorZ.Length - 1);
        float blend = a == b ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(anchorZ[a], anchorZ[b], z));
        instance.SetTexture(TextureA, panoramas[a]);
        instance.SetTexture(TextureB, panoramas[b]);
        instance.SetFloat(Blend, blend);
    }

    void OnDisable()
    {
        if (RenderSettings.skybox == instance) RenderSettings.skybox = previousSkybox;
        if (!instance) return;
        if (Application.isPlaying) Destroy(instance);
        else DestroyImmediate(instance);
        instance = null;
    }
}
