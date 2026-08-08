using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CarryFixPotMaterial
{
    private const string ScenePath = "Assets/Scenes/CastleStage.unity";

    [MenuItem("Carry/Fix Pot Material")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string baseColorPath = "Assets/Pot/Textures/Carry_Pot_BaseColor.png";
            string metallicPath = "Assets/Pot/Textures/Carry_Pot_Metallic.png";

            var metallicImporter = AssetImporter.GetAtPath(metallicPath) as TextureImporter;
            if (metallicImporter != null)
            {
                metallicImporter.sRGBTexture = false;
                metallicImporter.SaveAndReimport();
                log.AppendLine("Set metallic texture to linear (sRGB off).");
            }

            var baseColorTex = AssetDatabase.LoadAssetAtPath<Texture2D>(baseColorPath);
            var metallicTex = AssetDatabase.LoadAssetAtPath<Texture2D>(metallicPath);
            if (baseColorTex == null) throw new System.Exception("BaseColor texture not found.");
            if (metallicTex == null) throw new System.Exception("Metallic texture not found.");

            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Pot/Mat_CarryPot.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, "Assets/Pot/Mat_CarryPot.mat");
                log.AppendLine("Created new Mat_CarryPot.mat asset.");
            }

            mat.SetTexture("_BaseMap", baseColorTex);
            if (mat.HasProperty("_MetallicGlossMap"))
            {
                mat.SetTexture("_MetallicGlossMap", metallicTex);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", 0.4f);

            EditorUtility.SetDirty(mat);

            // point the pot's renderer at this explicit material asset (belt-and-braces,
            // in case the FBX-embedded material slot didn't carry the assignment through)
            var pot = GameObject.Find("Carry_Pot");
            if (pot != null)
            {
                var renderer = pot.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = mat;
                    log.AppendLine("Assigned Mat_CarryPot to the Carry_Pot renderer in the scene.");
                }
            }
            else
            {
                log.AppendLine("Note: Carry_Pot not found in currently open scene (material asset updated regardless).");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
