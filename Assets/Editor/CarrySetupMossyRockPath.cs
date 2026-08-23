using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Meshy-generated "MossyRockPath" course-module kit: organizes the Source/Textures/Materials
// folders (mirrors the AzureCrystal pattern), builds MAT_MossyRockPath, imports the 5
// Blender-separated FBX pieces (see Models/Separated/, pivot already recentered to each piece's
// entry point by the Blender pass) and builds each into a PF_MossyRockPath_* prefab with a
// Visual child (full-detail decimated mesh) and a WalkableCollision child (smooth BoxCollider
// chain generated from the mesh's own cross-section profile, NOT the bumpy visual mesh -- this
// is the fix for the goblin/pot jitter the old per-rock MeshCollider course caused).
//
// Geometry note: each separated FBX has its local origin at the piece's "entry" point (on the
// walkable top surface), baked in Blender via transform_apply after an origin_set to that point.
// MossyPathAnalysis re-derives the exit point/tangent and the whole width profile straight from
// the imported mesh's own vertices (2D PCA on the local X-Z plane) rather than trusting any
// Blender-side axis-convention assumption, so the result is correct regardless of how the FBX
// exporter/importer rotated axes.
public static class CarrySetupMossyRockPath
{
    const string Root = "Assets/Stage/Forest/Path/MossyRockPath/";
    const string SrcDir = Root + "Source";
    const string TexDir = Root + "Textures";
    const string MatDir = Root + "Materials";
    const string PrefabDir = Root + "Prefabs";
    const string SepDir = Root + "Models/Separated";

    public const float GlobalScale = 12f; // 2026-08-23 reduced from 14 ("少しだけ縮小" feedback) -- keeps NarrowLink's neck ~1.0m (still passable, goblin capsule diameter 0.7m) while the rest of the kit reads less oversized

    public static readonly string[] PieceNames =
    {
        "MossyRockPath_NarrowLink",
        "MossyRockPath_LongCurve",
        "MossyRockPath_GentleCurve_A",
        "MossyRockPath_GentleStraight",
        "MossyRockPath_WideCurve",
    };

    [MenuItem("Carry/Setup Mossy Rock Path")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            foreach (var dir in new[] { SrcDir, TexDir, MatDir, PrefabDir })
                if (!AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(dir).Replace('\\', '/'), Path.GetFileName(dir));

            // ---- 1. Texture import settings ----
            SetupTexture(TexDir + "/MossyRockPath_BaseColor.png", TextureImporterType.Default, true);
            SetupTexture(TexDir + "/MossyRockPath_Normal.png", TextureImporterType.NormalMap, false);
            SetupTexture(TexDir + "/MossyRockPath_Metallic.png", TextureImporterType.Default, false);
            SetupTexture(TexDir + "/MossyRockPath_Roughness.png", TextureImporterType.Default, false);

            // ---- 2. Combined Metallic(R) + Smoothness(A=1-roughness) map (URP Lit convention) ----
            string mgPath = TexDir + "/MossyRockPath_MetallicSmoothness.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(mgPath) == null)
            {
                var met = LoadReadable(TexDir + "/MossyRockPath_Metallic.png");
                var rough = LoadReadable(TexDir + "/MossyRockPath_Roughness.png");
                var mp = met.tex.GetPixels();
                var rp = rough.tex.GetPixels();
                int n = Mathf.Min(mp.Length, rp.Length);
                var op = new Color[n];
                for (int i = 0; i < n; i++) op[i] = new Color(mp[i].r, mp[i].r, mp[i].r, 1f - rp[i].r);
                var outTex = new Texture2D(met.tex.width, met.tex.height, TextureFormat.RGBA32, false);
                outTex.SetPixels(op);
                outTex.Apply();
                File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, mgPath), outTex.EncodeToPNG());
                Object.DestroyImmediate(outTex);
                met.Restore(); rough.Restore();
                AssetDatabase.ImportAsset(mgPath, ImportAssetOptions.ForceUpdate);
                var mgImp = AssetImporter.GetAtPath(mgPath) as TextureImporter;
                if (mgImp != null) { mgImp.sRGBTexture = false; mgImp.SaveAndReimport(); }
                log.AppendLine("Generated " + mgPath);
            }

            // ---- 3. Material ----
            string matPath = MatDir + "/MAT_MossyRockPath.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/MossyRockPath_BaseColor.png"));
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/MossyRockPath_Normal.png");
            if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
            var mg = AssetDatabase.LoadAssetAtPath<Texture2D>(mgPath);
            if (mg != null)
            {
                mat.SetTexture("_MetallicGlossMap", mg);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness", 1f);
                mat.SetFloat("_Metallic", 1f);
            }
            EditorUtility.SetDirty(mat);
            log.AppendLine("Material ready: " + matPath);
            AssetDatabase.SaveAssets();

            // ---- 4. Import FBX pieces + apply global scale ----
            foreach (var nm in PieceNames)
            {
                string fbxPath = SepDir + "/" + nm + ".fbx";
                var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer == null) { log.AppendLine("MISSING FBX: " + fbxPath); continue; }
                importer.globalScale = GlobalScale;
                importer.materialImportMode = ModelImporterMaterialImportMode.None; // we assign MAT_MossyRockPath ourselves; avoids the known white/external-material Meshy pitfall
                importer.SaveAndReimport();
            }

            // ---- 5. Build prefabs ----
            foreach (var nm in PieceNames)
            {
                BuildPrefab(nm, mat, log);
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

    static void BuildPrefab(string nm, Material mat, System.Text.StringBuilder log)
    {
        string fbxPath = SepDir + "/" + nm + ".fbx";
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbx == null) { log.AppendLine("MISSING FBX asset: " + fbxPath); return; }

        var root = new GameObject(nm);
        var visualRoot = new GameObject("Visual");
        visualRoot.transform.SetParent(root.transform, false);
        var meshInst = (GameObject)PrefabUtility.InstantiatePrefab(fbx, visualRoot.transform);
        meshInst.transform.localPosition = Vector3.zero;
        meshInst.transform.localRotation = Quaternion.identity;
        meshInst.transform.localScale = Vector3.one;
        foreach (var mr in meshInst.GetComponentsInChildren<MeshRenderer>())
            mr.sharedMaterials = new[] { mat };
        // strip any collider Unity may have auto-added to the visual mesh -- collision lives only on WalkableCollision
        foreach (var col in meshInst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

        var mf = meshInst.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { log.AppendLine(nm + ": no mesh found after import"); Object.DestroyImmediate(root); return; }

        var profile = MossyPathAnalysis.Analyze(mf.sharedMesh, isNarrowLink: nm == "MossyRockPath_NarrowLink");

        var collRoot = new GameObject("WalkableCollision");
        collRoot.transform.SetParent(root.transform, false);
        MossyPathAnalysis.BuildColliderChain(profile, collRoot.transform);

        string prefabPath = PrefabDir + "/PF_" + nm + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        log.AppendLine("Prefab built: " + prefabPath + $" (length={profile.Length:F2}m, entryDir=({profile.EntryDirXZ.x:F2},{profile.EntryDirXZ.y:F2}), exitLocalPos={profile.ExitLocalPos:F2}, exitDir=({profile.ExitDirXZ.x:F2},{profile.ExitDirXZ.y:F2}), minWidth={profile.MinWidth:F2}, maxWidth={profile.MaxWidth:F2})");
    }

    static void SetupTexture(string path, TextureImporterType type, bool sRGB)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;
        bool dirty = false;
        if (imp.textureType != type) { imp.textureType = type; dirty = true; }
        if (imp.sRGBTexture != sRGB) { imp.sRGBTexture = sRGB; dirty = true; }
        if (dirty) imp.SaveAndReimport();
    }

    struct ReadableTex { public Texture2D tex; public TextureImporter imp; public bool wasReadable; public void Restore() { if (imp != null && !wasReadable) { imp.isReadable = false; imp.SaveAndReimport(); } } }
    static ReadableTex LoadReadable(string path)
    {
        var r = new ReadableTex();
        r.imp = AssetImporter.GetAtPath(path) as TextureImporter;
        r.wasReadable = r.imp.isReadable;
        if (!r.wasReadable) { r.imp.isReadable = true; r.imp.SaveAndReimport(); }
        r.tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        return r;
    }
}
