using System.IO;
using UnityEditor;
using UnityEngine;

// One-shot (but idempotent/re-runnable) setup for the Meshy-generated "AzureCrystal" set:
// organizes the Crystal folder per the agreed structure, renames Meshy-derived names to the
// AzureCrystal_* convention, generates the crystal-only emission mask, builds the URP material,
// and creates the five placement prefabs. Placement itself lives in CarryBuildTerrainForest
// (BuildAzureCrystals) so the scene stays fully reproducible from the main generator.
public static class CarrySetupAzureCrystals
{
    const string Root = "Assets/Stage/Forest/Crystal/";
    const string SrcDir = Root + "Source";
    const string TexDir = Root + "Textures";
    const string MatDir = Root + "Materials";
    const string PrefabDir = Root + "Prefabs";
    const string SepDir = Root + "Models/Separated";

    // Meshy original file names (as delivered into Models/CrystalSet01/)
    const string OldDir = Root + "Models/CrystalSet01";
    const string MeshyBase = "Meshy_AI_Azure_Crystal_Outcrop_0813121009_texture";

    [MenuItem("Carry/Setup Azure Crystals")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            foreach (var dir in new[] { SrcDir, TexDir, MatDir, PrefabDir })
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
                }
            }

            // ---- 1. Move Meshy originals to Source/ (AssetDatabase.MoveAsset keeps .meta/GUIDs
            // intact -- never a raw filesystem move, per the no-broken-references requirement).
            // Original file names are kept inside Source/ (allowed by spec) so the files remain
            // byte-identical Meshy output.
            MoveIfThere(OldDir + "/Crystal_Outcrop_texture.fbx", SrcDir + "/Crystal_Outcrop_texture.fbx", log);
            MoveIfThere(OldDir + "/" + MeshyBase + ".png", SrcDir + "/" + MeshyBase + ".png", log);
            MoveIfThere(OldDir + "/" + MeshyBase + "_metallic.png", SrcDir + "/" + MeshyBase + "_metallic.png", log);
            MoveIfThere(OldDir + "/" + MeshyBase + "_normal.png", SrcDir + "/" + MeshyBase + "_normal.png", log);
            MoveIfThere(OldDir + "/" + MeshyBase + "_roughness.png", SrcDir + "/" + MeshyBase + "_roughness.png", log);
            // Remove the now-empty CrystalSet01 folder (only if actually empty).
            if (AssetDatabase.IsValidFolder(OldDir) && AssetDatabase.FindAssets("", new[] { OldDir }).Length == 0)
                AssetDatabase.DeleteAsset(OldDir);

            // ---- 2. Clean-named texture copies into Textures/ (copies, so Source stays pristine).
            CopyIfMissing(SrcDir + "/" + MeshyBase + ".png", TexDir + "/AzureCrystal_BaseColor.png", log);
            CopyIfMissing(SrcDir + "/" + MeshyBase + "_normal.png", TexDir + "/AzureCrystal_Normal.png", log);
            CopyIfMissing(SrcDir + "/" + MeshyBase + "_metallic.png", TexDir + "/AzureCrystal_Metallic.png", log);
            CopyIfMissing(SrcDir + "/" + MeshyBase + "_roughness.png", TexDir + "/AzureCrystal_Roughness.png", log);

            var normalImporter = AssetImporter.GetAtPath(TexDir + "/AzureCrystal_Normal.png") as TextureImporter;
            if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }

            // ---- 3. Emission mask: the base color cleanly separates cyan-blue crystal (B >>
            // max(R,G)) from brown rock, so a per-pixel blueness mask * base color gives an
            // emission map that lights ONLY the crystal faces -- exactly the "rock stays dark,
            // crystal glows faintly" requirement, with no manual mask painting needed.
            string emissionPath = TexDir + "/AzureCrystal_Emission.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath) == null)
            {
                var baseImporter = AssetImporter.GetAtPath(TexDir + "/AzureCrystal_BaseColor.png") as TextureImporter;
                bool wasReadable = baseImporter.isReadable;
                if (!wasReadable) { baseImporter.isReadable = true; baseImporter.SaveAndReimport(); }
                var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/AzureCrystal_BaseColor.png");
                var px = baseTex.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    // Blueness: how much B leads the other channels. Crystal pixels (cyan #4?C?FF-ish)
                    // score high; brown rock (R>=G>B) scores ~0. Squared for a tighter falloff so
                    // dim blue-gray transition pixels barely glow.
                    float blueness = Mathf.Clamp01((c.b - Mathf.Max(c.r * 0.9f, c.g * 0.75f)) * 2.2f);
                    blueness *= blueness;
                    px[i] = new Color(c.r * blueness, c.g * blueness, c.b * blueness, 1f);
                }
                var outTex = new Texture2D(baseTex.width, baseTex.height, TextureFormat.RGBA32, false);
                outTex.SetPixels(px);
                outTex.Apply();
                File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, emissionPath), outTex.EncodeToPNG());
                Object.DestroyImmediate(outTex);
                if (!wasReadable) { baseImporter.isReadable = false; baseImporter.SaveAndReimport(); }
                AssetDatabase.ImportAsset(emissionPath, ImportAssetOptions.ForceUpdate);
                log.AppendLine("Generated emission mask: " + emissionPath);
            }

            // ---- 4. Metallic+Smoothness combined map (URP Lit reads metallic from R and
            // smoothness from A of ONE texture; Meshy ships separate metallic/roughness maps).
            string mgPath = TexDir + "/AzureCrystal_MetallicSmoothness.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(mgPath) == null)
            {
                var met = LoadReadable(TexDir + "/AzureCrystal_Metallic.png");
                var rough = LoadReadable(TexDir + "/AzureCrystal_Roughness.png");
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

            // ---- 5. Material.
            string matPath = MatDir + "/MAT_AzureCrystal.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/AzureCrystal_BaseColor.png"));
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/AzureCrystal_Normal.png");
            if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
            var mg = AssetDatabase.LoadAssetAtPath<Texture2D>(mgPath);
            if (mg != null)
            {
                mat.SetTexture("_MetallicGlossMap", mg);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness", 1f); // acts as multiplier on the map's A channel
            }
            var emi = AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath);
            if (emi != null)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetTexture("_EmissionMap", emi);
                // Subtle: the mask itself already carries the crystal color; this is a gentle
                // brightness multiplier, NOT a neon HDR boost ("暗い岩の隙間から青い魔力が静かに
                // 漏れている" -- quiet leak of light, never LED).
                mat.SetColor("_EmissionColor", new Color(0.55f, 0.75f, 1.0f) * 1.1f);
            }
            EditorUtility.SetDirty(mat);
            log.AppendLine("Material ready: " + matPath);

            // ---- 6. Prefabs (idempotent -- skip existing so manual tweaks survive reruns).
            string[] names = { "AzureCrystal_LakeFloor", "AzureCrystal_CliffWall", "AzureCrystal_RockGap", "AzureCrystal_CliffCrack", "AzureCrystal_Rock" };
            foreach (var nm in names)
            {
                string prefabPath = PrefabDir + "/PF_" + nm + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) continue;
                string fbxPath = SepDir + "/" + nm + ".fbx";
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (fbx == null) { log.AppendLine("MISSING separated FBX: " + fbxPath); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
                {
                    var mats = mr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    mr.sharedMaterials = mats;
                }
                // Colliders only where the player can physically reach: the lake-floor cluster
                // (swimming player) and the shore rock. Wall/crack/gap variants sit inside cliff
                // geometry that already has its own colliders.
                if (nm == "AzureCrystal_LakeFloor" || nm == "AzureCrystal_Rock")
                {
                    var mf = inst.GetComponentInChildren<MeshFilter>();
                    if (mf != null)
                    {
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.convex = true; // cheap approximation is fine for a decorative obstacle
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                Object.DestroyImmediate(inst);
                log.AppendLine("Prefab created: " + prefabPath);
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
