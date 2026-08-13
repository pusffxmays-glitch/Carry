using UnityEditor;
using UnityEngine;

// Poly Haven's fir_sapling/pine_sapling_small ship separate diff/alpha/mask/nor/rough
// textures per material slot (twigs, bark, branches), but Unity's automatic FBX
// material-texture matching failed to link any of them for the "twig" materials,
// leaving flat near-white or untextured olive materials -- the trees render as
// pale ghost-sprigs instead of foliage. Wire the real textures in and enable
// alpha clipping on the needle-card materials.
public static class CarryFixTreeMaterials
{
    const string Fir = "Assets/ExternalAssets/PolyHaven/fir_sapling/";
    const string Pine = "Assets/ExternalAssets/PolyHaven/pine_sapling_small/";
    const string Fern = "Assets/ExternalAssets/PolyHaven/fern_02/";
    const string FirMed = "Assets/ExternalAssets/PolyHaven/fir_sapling_medium/";

    [MenuItem("Carry/Debug/Fix Tree Materials")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();

        ExtractMaterials(FirMed + "fir_sapling_medium_2k.fbx", log);

        WireMaterial(Fir + "Materials/fir_sapling_twigs.mat", Fir + "fir_sapling_twigs_diff_2k.png",
            Fir + "fir_sapling_twigs_alpha_2k.png", true, log);
        WireMaterial(Pine + "Materials/pine_sapling_small_twig.mat", Pine + "pine_sapling_small_twig_diff_2k.png",
            Pine + "pine_sapling_small_twig_alpha_2k.png", true, log);
        WireMaterial(Pine + "Materials/pine_sapling_small_bark.mat", Pine + "pine_sapling_small_bark_diff_2k.png",
            null, false, log);
        WireMaterial(Fern + "Materials/fern_02.mat", Fern + "fern_02_diff_2k.jpg", Fern + "fern_02_alpha_2k.png", true, log);
        WireMaterial(FirMed + "Materials/fir_sapling_medium_twigs_diff_2k.mat", FirMed + "fir_sapling_medium_twigs_diff_2k.png",
            FirMed + "fir_sapling_medium_twigs_alpha_2k.png", true, log);
        WireMaterial(FirMed + "Materials/fir_sapling_medium_branches_diff_2k.mat", FirMed + "fir_sapling_medium_branches_diff_2k.png",
            null, false, log);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(log.ToString());
    }

    static void ExtractMaterials(string fbxPath, System.Text.StringBuilder log)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) { log.AppendLine(fbxPath + " => importer not found"); return; }
        importer.materialLocation = ModelImporterMaterialLocation.External;
        importer.SaveAndReimport();
    }

    static void WireMaterial(string matPath, string diffusePath, string alphaPath, bool clip, System.Text.StringBuilder log)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { log.AppendLine(matPath + " => NOT FOUND"); return; }

        var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath);
        if (diff == null) { log.AppendLine(diffusePath + " => texture NOT FOUND"); return; }
        mat.SetTexture("_BaseMap", diff);
        mat.color = Color.white;

        if (clip)
        {
            // Poly Haven's diffuse PNG for leaf/needle cards usually carries the cutout
            // mask in its own alpha channel already; clip against that directly.
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.35f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetShaderPassEnabled("ShadowCaster", true);
        }

        EditorUtility.SetDirty(mat);
        log.AppendLine(matPath + " => baseMap=" + diff.name + " alphaClip=" + clip);
    }
}
