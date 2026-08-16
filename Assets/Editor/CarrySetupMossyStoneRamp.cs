using System.IO;
using UnityEditor;
using UnityEngine;

// 2026-08-15: organizes the Meshy-generated "MossyStoneRamp" asset (delivered into
// Assets/Stage/Lake/Models/MossyStoneRamp/ as a single raw "_texture.fbx" + 4 PNGs, Meshy's default
// naming). This is the SAME Meshy PBR export pipeline already used for the Bridge and AzureCrystal
// assets (see CarrySetupAzureCrystals.cs / Mat_MeshyStoneBridge.mat) -- verified by sampling actual
// pixel data before writing any of this: BaseColor avg RGB=(0.29,0.29,0.19) (dark mossy stone),
// Normal avg=(0.51,0.48,0.83) (a real tangent-space normal map, ~flat neutral), Metallic avgR=0.71 and
// Roughness avgR=0.75 -- which LOOKS suspiciously high for "metallic", but the already-shipped Bridge
// material has Metallic avgR=0.78/Roughness avgR=0.78 and the Crystal set has Metallic avgR=0.84, i.e.
// this is just how Meshy's own export always scores these channels on this project's assets, not a
// mislabeling of this particular file. So the 4 textures are used literally as named, packed the exact
// same way as the other two (never re-guessed).
//
// Also reproduces the "fileScale 0.01" import bug already known from the Bridge/Guardian-tree FBX
// (CLAUDE.md 既知の落とし穴): raw mesh.bounds after import is (0.02,0.02,0.011) -- i.e. a real ~2m
// asset shrunk 100x. PF_AncientForestGuardian's own prefab fixes this with a child Transform at
// localScale=100 (not by touching ModelImporter.globalScale, which the Guardian setup never touched
// either) -- MossyStoneRamp.prefab does the same here for consistency with that precedent.
public static class CarrySetupMossyStoneRamp
{
    const string Root = "Assets/Stage/Lake/Models/MossyStoneRamp/";
    const string PrefabDir = Root + "Prefabs";

    const string OldBase = "Meshy_AI_Mossy_Stone_Bridge_0814100609_texture";
    const string NewBase = "MossyStoneRamp";

    // Raw mesh.bounds extents at ModelImporter defaults (globalScale=1, fileScale=0.01 auto-detected)
    // -- confirmed live via execute_code before writing this script, matches the same fileScale=0.01
    // bug documented for PF_AncientForestGuardian's source FBX.
    const float FileScaleCorrection = 100f;

    [MenuItem("Carry/Setup Mossy Stone Ramp")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Prefabs");

            // ---- 1. Rename in place (AssetDatabase.RenameAsset keeps the GUID/.meta, so nothing that
            // already references these assets -- there is nothing yet, but future scene refs stay
            // intact regardless -- breaks). Per explicit request: rename the ACTUAL files, not copies.
            RenameIfNeeded(Root + OldBase + ".fbx", NewBase, log);
            RenameIfNeeded(Root + OldBase + ".png", NewBase + "_BaseColor", log);
            RenameIfNeeded(Root + OldBase + "_normal.png", NewBase + "_Normal", log);
            RenameIfNeeded(Root + OldBase + "_metallic.png", NewBase + "_Metallic", log);
            RenameIfNeeded(Root + OldBase + "_roughness.png", NewBase + "_Roughness", log);

            string fbxPath = Root + NewBase + ".fbx";
            string baseColorPath = Root + NewBase + "_BaseColor.png";
            string normalPath = Root + NewBase + "_Normal.png";
            string metallicPath = Root + NewBase + "_Metallic.png";
            string roughnessPath = Root + NewBase + "_Roughness.png";
            string mgPath = Root + NewBase + "_MetallicSmoothness.png";

            // ---- 2. Fix texture import types/color-space to match the established convention
            // (BaseColor = sRGB color; Normal/Metallic/Roughness = linear data maps; Normal also needs
            // textureType=NormalMap so Unity stores/decodes it correctly).
            SetSRGB(baseColorPath, true, log);
            var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
                log.AppendLine("Set " + normalPath + " textureType=NormalMap");
            }
            SetSRGB(metallicPath, false, log);
            SetSRGB(roughnessPath, false, log);

            // ---- 3. Metallic+Smoothness combined map (URP Lit reads metallic from R and smoothness
            // from A of ONE texture) -- identical recipe to CarrySetupAzureCrystals.
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(mgPath) == null)
            {
                var met = LoadReadable(metallicPath);
                var rough = LoadReadable(roughnessPath);
                if (met.tex != null && rough.tex != null)
                {
                    var mp = met.tex.GetPixels();
                    var rp = rough.tex.GetPixels();
                    int n = Mathf.Min(mp.Length, rp.Length);
                    var op = new Color[n];
                    for (int i = 0; i < n; i++)
                        op[i] = new Color(mp[i].r, mp[i].r, mp[i].r, 1f - rp[i].r); // A = smoothness = 1 - roughness
                    var outTex = new Texture2D(met.tex.width, met.tex.height, TextureFormat.RGBA32, false);
                    outTex.SetPixels(op);
                    outTex.Apply();
                    File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, mgPath), outTex.EncodeToPNG());
                    Object.DestroyImmediate(outTex);
                    met.Restore(); rough.Restore();
                    AssetDatabase.ImportAsset(mgPath, ImportAssetOptions.ForceUpdate);
                    var mgImp = AssetImporter.GetAtPath(mgPath) as TextureImporter;
                    if (mgImp != null) { mgImp.sRGBTexture = false; mgImp.SaveAndReimport(); }
                    log.AppendLine("Generated metallic+smoothness map: " + mgPath);
                }
                else log.AppendLine("FAILED to generate MetallicSmoothness -- metallic or roughness texture missing/unreadable.");
            }

            // ---- 4. Material. Matte mossy stone: NOT brightened, NOT tinted -- the BaseColor texture
            // (already a dark, weathered mossy rock photo-scan) drives the look directly, same as
            // Mat_MeshyStoneBridge (which also leaves _BaseColor at white/1,1,1,1).
            string matPath = Root + "M_MossyStoneRamp.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(baseColorPath));
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
            var mg = AssetDatabase.LoadAssetAtPath<Texture2D>(mgPath);
            if (mg != null)
            {
                mat.SetTexture("_MetallicGlossMap", mg);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness", 1f); // pure multiplier on the map's A channel, same as Bridge/Crystal
            }
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            log.AppendLine("Material ready: " + matPath);

            // ---- 5. Prefab: raw FBX wrapped in a corrective-scale child (fixes the 0.01 fileScale
            // import bug, same fix PF_AncientForestGuardian already uses), material assigned, and a
            // convex MeshCollider so the decorative stone itself is solid to the player -- the actual
            // WALKING surface for the ramp path is the sculpted Terrain (see CarryBuildLakeRampPath),
            // never this rock's own bumpy silhouette, so no slope-limit/step tuning is needed here.
            CreatePrefabIfMissing(PrefabDir + "/MossyStoneRamp.prefab", "MossyStoneRamp", fbxPath, mat, log);

            // 2026-08-15: wider variant, per explicit request ("坂の幅が狭すぎるのでブレンダーで坂の幅を広くして").
            // Widened 1.5x on X ONLY (width) in Blender itself (see conversation) -- a real mesh
            // re-export rather than a more non-uniform Unity Transform.localScale on top of the already
            // non-uniform (X,Y,Z) placement scale, so the extra width doesn't compound extra visual
            // stretching on top of what CarryBuildLakeRampPath.cs already applies for the climb/rise.
            const string wideFbxPath = Root + "MossyStoneRamp_Wide.fbx";
            CreatePrefabIfMissing(PrefabDir + "/MossyStoneRamp_Wide.prefab", "MossyStoneRamp_Wide", wideFbxPath, mat, log);

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

    static void CreatePrefabIfMissing(string prefabPath, string rootName, string sourceFbxPath, Material mat, System.Text.StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) { log.AppendLine("Prefab already exists, left untouched: " + prefabPath); return; }
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(sourceFbxPath);
        if (fbx == null) { log.AppendLine("FAILED: FBX not found at " + sourceFbxPath); return; }

        var root = new GameObject(rootName);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one * FileScaleCorrection; // same fileScale=0.01 import bug on every Meshy export from this pipeline, including a fresh Blender re-export

        foreach (var mr in visual.GetComponentsInChildren<MeshRenderer>())
            mr.sharedMaterial = mat;

        var mf = visual.GetComponentInChildren<MeshFilter>();
        if (mf != null)
        {
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.convex = true; // cheap decorative-solid collider; NOT the ramp's walking surface
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        log.AppendLine("Prefab created: " + prefabPath);
    }

    static void RenameIfNeeded(string path, string newName, System.Text.StringBuilder log)
    {
        if (AssetDatabase.LoadMainAssetAtPath(path) == null)
        {
            log.AppendLine("Rename skip (not found, likely already renamed): " + path);
            return;
        }
        string err = AssetDatabase.RenameAsset(path, newName);
        log.AppendLine(string.IsNullOrEmpty(err) ? ("Renamed " + path + " -> " + newName) : ("RENAME FAILED " + path + ": " + err));
    }

    static void SetSRGB(string path, bool srgb, System.Text.StringBuilder log)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) { log.AppendLine("SetSRGB: importer not found for " + path); return; }
        if (imp.sRGBTexture != srgb)
        {
            imp.sRGBTexture = srgb;
            imp.SaveAndReimport();
            log.AppendLine("Set " + path + " sRGBTexture=" + srgb);
        }
    }

    struct ReadableTex { public Texture2D tex; public TextureImporter imp; public bool wasReadable; public void Restore() { if (imp != null && !wasReadable) { imp.isReadable = false; imp.SaveAndReimport(); } } }
    static ReadableTex LoadReadable(string path)
    {
        var r = new ReadableTex();
        r.imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (r.imp == null) return r;
        r.wasReadable = r.imp.isReadable;
        if (!r.wasReadable) { r.imp.isReadable = true; r.imp.SaveAndReimport(); }
        r.tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        return r;
    }
}
