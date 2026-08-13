using System.IO;
using UnityEditor;
using UnityEngine;

// One-shot (but idempotent/re-runnable) setup for the user's Meshy-generated "Ancient Forest
// Guardian" tree, dropped by the user directly into Assets/Stage/Forest/Trees/tree/ under its raw
// Meshy export name. Organizes it into its own asset folder per the same structure already
// established for the AzureCrystal set (CarrySetupAzureCrystals.cs): Source/ keeps the untouched
// Meshy originals, Textures/ gets clean-named copies, Materials/ gets a real URP/Lit material
// (built directly rather than relying on Unity's auto material-texture linking, which per this
// project's own known pitfalls silently fails for externally-textured FBX imports), and Prefabs/
// gets the final placeable prefab.
public static class CarrySetupAncientForestGuardianTree
{
    const string Root = "Assets/Stage/Forest/Trees/AncientForestGuardian/";
    const string SrcDir = Root + "Source";
    const string TexDir = Root + "Textures";
    const string MatDir = Root + "Materials";
    const string PrefabDir = Root + "Prefabs";

    // User-dropped location + Meshy original file names (as delivered).
    const string OldDir = "Assets/Stage/Forest/Trees/tree";
    const string MeshyBase = "Meshy_AI_Ancient_Forest_Guardi_0813134833_texture";

    [MenuItem("Carry/Setup Ancient Forest Guardian Tree")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            if (!AssetDatabase.IsValidFolder(Root.TrimEnd('/')))
                AssetDatabase.CreateFolder("Assets/Stage/Forest/Trees", "AncientForestGuardian");
            foreach (var dir in new[] { SrcDir, TexDir, MatDir, PrefabDir })
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
                }
            }

            // ---- 1. Move Meshy originals into Source/ (AssetDatabase.MoveAsset keeps .meta/GUIDs
            // intact -- never a raw filesystem move). Original file names are kept inside Source/ so
            // the files remain byte-identical Meshy output, same convention as the Crystal set.
            MoveIfThere(OldDir + "/" + MeshyBase + ".fbx", SrcDir + "/" + MeshyBase + ".fbx", log);
            MoveIfThere(OldDir + "/" + MeshyBase + ".png", SrcDir + "/" + MeshyBase + ".png", log);
            MoveIfThere(OldDir + "/" + MeshyBase + "_metallic.png", SrcDir + "/" + MeshyBase + "_metallic.png", log);
            MoveIfThere(OldDir + "/" + MeshyBase + "_normal.png", SrcDir + "/" + MeshyBase + "_normal.png", log);
            MoveIfThere(OldDir + "/" + MeshyBase + "_roughness.png", SrcDir + "/" + MeshyBase + "_roughness.png", log);
            // Remove the now-empty user-dropped folder (only if actually empty).
            if (AssetDatabase.IsValidFolder(OldDir) && AssetDatabase.FindAssets("", new[] { OldDir }).Length == 0)
                AssetDatabase.DeleteAsset(OldDir);

            // ---- 2. Clean-named texture copies into Textures/ (copies, so Source stays pristine).
            CopyIfMissing(SrcDir + "/" + MeshyBase + ".png", TexDir + "/AncientForestGuardian_BaseColor.png", log);
            CopyIfMissing(SrcDir + "/" + MeshyBase + "_normal.png", TexDir + "/AncientForestGuardian_Normal.png", log);
            CopyIfMissing(SrcDir + "/" + MeshyBase + "_metallic.png", TexDir + "/AncientForestGuardian_Metallic.png", log);
            CopyIfMissing(SrcDir + "/" + MeshyBase + "_roughness.png", TexDir + "/AncientForestGuardian_Roughness.png", log);

            var normalImporter = AssetImporter.GetAtPath(TexDir + "/AncientForestGuardian_Normal.png") as TextureImporter;
            if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }

            // ---- 3. Metallic+Smoothness combined map (URP Lit reads metallic from R and
            // smoothness from A of ONE texture; Meshy ships separate metallic/roughness maps) --
            // same technique as CarrySetupAzureCrystals.
            string mgPath = TexDir + "/AncientForestGuardian_MetallicSmoothness.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(mgPath) == null)
            {
                var met = LoadReadable(TexDir + "/AncientForestGuardian_Metallic.png");
                var rough = LoadReadable(TexDir + "/AncientForestGuardian_Roughness.png");
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
                    if (mgImp != null) { mgImp.sRGBTexture = false; mgImp.SaveAndReimport(); } // data map, not color
                    log.AppendLine("Generated metallic+smoothness map: " + mgPath);
                }
            }

            // ---- 4. Material.
            string matPath = MatDir + "/MAT_AncientForestGuardian.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/AncientForestGuardian_BaseColor.png"));
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/AncientForestGuardian_Normal.png");
            if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
            var mg = AssetDatabase.LoadAssetAtPath<Texture2D>(mgPath);
            if (mg != null)
            {
                mat.SetTexture("_MetallicGlossMap", mg);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness", 1f); // acts as multiplier on the map's A channel
            }
            EditorUtility.SetDirty(mat);
            log.AppendLine("Material ready: " + matPath);

            // ---- 5. Prefab (idempotent -- skip if it already exists so manual tweaks survive
            // reruns). Assigned directly rather than relying on Unity's auto FBX material-texture
            // matching, which per CLAUDE.md's own known pitfalls silently fails to link externally-
            // supplied textures and leaves the model flat white/pale.
            string prefabPath = PrefabDir + "/PF_AncientForestGuardian.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                string fbxPath = SrcDir + "/" + MeshyBase + ".fbx";
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (fbx == null) { log.AppendLine("MISSING source FBX: " + fbxPath); }
                else
                {
                    // This Meshy FBX's mesh sits directly on the FBX's OWN root (no separate
                    // sub-object), and that root already carries a baked-in localScale=(100,100,100)
                    // from the unit-conversion Unity applies on import (raw mesh data is ~0.01 units,
                    // matching the same unit-mismatch pitfall CLAUDE.md documents elsewhere). Saving
                    // that root directly as the prefab would make the PREFAB's own root carry that
                    // 100x baked in, which breaks every placement call site in this project -- they
                    // all do `instance.transform.localScale = Vector3.one * scale` (a hard overwrite,
                    // not multiplicative), which would blow away the 100x and leave a nearly
                    // invisible ~2cm tree. Wrap the FBX instance under a plain neutral parent at
                    // scale=1 instead (same pattern already used for boulder_01's 100x-scaled LOD
                    // children) so the prefab's OWN root is a normal scale=1 object and
                    // GetPrefabLocalBounds/placement code work exactly like every other prefab here.
                    var fbxInst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                    foreach (var mr in fbxInst.GetComponentsInChildren<MeshRenderer>())
                    {
                        var mats = mr.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                        mr.sharedMaterials = mats;
                    }
                    var wrapper = new GameObject("AncientForestGuardian");
                    fbxInst.transform.SetParent(wrapper.transform, true); // keep world transform (its baked 100x scale) as local values under the new neutral root
                    PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
                    Object.DestroyImmediate(wrapper);
                    log.AppendLine("Prefab created: " + prefabPath);
                }
            }

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

    static void MoveIfThere(string from, string to, System.Text.StringBuilder log)
    {
        if (AssetDatabase.LoadMainAssetAtPath(from) == null) return;
        if (AssetDatabase.LoadMainAssetAtPath(to) != null) return;
        string err = AssetDatabase.MoveAsset(from, to);
        log.AppendLine(string.IsNullOrEmpty(err) ? ("Moved " + from + " -> " + to) : ("MOVE FAILED " + from + ": " + err));
    }

    static void CopyIfMissing(string from, string to, System.Text.StringBuilder log)
    {
        if (AssetDatabase.LoadMainAssetAtPath(to) != null) return;
        if (AssetDatabase.LoadMainAssetAtPath(from) == null) { log.AppendLine("COPY SOURCE MISSING: " + from); return; }
        bool ok = AssetDatabase.CopyAsset(from, to);
        log.AppendLine(ok ? ("Copied " + from + " -> " + to) : ("COPY FAILED " + from));
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
