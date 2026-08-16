using System.Text;
using UnityEditor;
using UnityEngine;

// Fixes two rendering problems found when QA-screenshotting the dressed forest
// stage:
//  1) Kenney Nature Kit ships with NO texture at all (its models rely on baked
//     vertex colors, which URP/Lit does not sample) -- imported materials come
//     in flat white. We extract them to real .mat assets and tint them a flat
//     stone/wood color instead of leaving them white.
//  2) Quaternius tree canopy materials use alpha-cutout leaf textures, but the
//     auto-generated material doesn't enable clipping, so the leaf card shows
//     as a jagged opaque quad. We turn on Alpha Clipping for any material
//     whose texture looks like foliage (Leaf/Flower/Grass in the name).
public static class CarryFixExternalMaterials
{
    const string KenneyMat = "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/Materials/";
    const string QuatMat = "Assets/ExternalAssets/QuaterniusNatureMegaKit/FBX (Unity)/Materials/";

    // Kenney ships no texture at all (color comes from baked vertex colors, which
    // URP/Lit does not sample), so these shared, semantically-named materials
    // come in flat white. Tint them instead.
    static readonly (string file, Color color)[] KenneyColors =
    {
        (KenneyMat + "stone.mat", new Color(0.66f, 0.65f, 0.62f)),
        (KenneyMat + "stoneDark.mat", new Color(0.45f, 0.45f, 0.43f)),
        (KenneyMat + "woodBark.mat", new Color(0.35f, 0.24f, 0.15f)),
        (KenneyMat + "woodInner.mat", new Color(0.55f, 0.40f, 0.25f)),
        (KenneyMat + "grass.mat", new Color(0.35f, 0.55f, 0.25f)),
        (KenneyMat + "dirt.mat", new Color(0.55f, 0.42f, 0.28f)),
    };

    static readonly string[] QuatFoliageMats =
    {
        QuatMat + "Leaves_NormalTree_C.mat",
    };

    [MenuItem("Carry/Debug/Fix External Materials")]
    public static void Run()
    {
        var log = new StringBuilder();

        foreach (var (path, color) in KenneyColors) TintMaterial(path, color, log);
        foreach (var path in QuatFoliageMats) FixFoliageAlphaClip(path, log);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(log.ToString());
    }

    static void TintMaterial(string matPath, Color color, StringBuilder log)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { log.AppendLine(matPath + " => NOT FOUND"); return; }

        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(mat);
        log.AppendLine(matPath + " => tinted to " + color);
    }

    static void FixFoliageAlphaClip(string matPath, StringBuilder log)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { log.AppendLine(matPath + " => NOT FOUND"); return; }

        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.4f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetShaderPassEnabled("ShadowCaster", true);
        EditorUtility.SetDirty(mat);
        log.AppendLine(matPath + " => alpha-clip enabled");
    }
}
