using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Reproducible, isolated optimization of the completed forest scene.</summary>
public static class CarryForestSkyboxOptimizer
{
    public const string SourceScene = "Assets/Scenes/ForestStage_Realistic.unity";
    public const string OutputScene = "Assets/Scenes/ForestStage_Skybox.unity";
    public const string Folder = "Assets/Stage/Forest/SkyboxOptimized";
    const string TerrainPath = Folder + "/ForestTerrain.asset";
    const string Captures = "ScreenshotsOut/ForestSkybox";
    static readonly float[] Anchors = { -12f, 35f, 80f, 125f };

    [MenuItem("Carry/Optimization/Create Forest Skybox Variant")]
    public static void Run()
    {
        Prepare();
        for (int i = 0; i < Anchors.Length; i++) Bake(i);
        Debug.Log(Apply());
    }

    [Serializable] public class Metrics
    {
        public int originalTrees, remainingTrees, bakedTrees;
        public long originalTreeTriangles, remainingTreeTriangles;
        public long originalVegetationTextureBytes, optimizedVegetationTextureBytes;
        public long originalVegetationBundleBytes, optimizedVegetationBundleBytes;
        public long originalSceneBundleBytes, optimizedSceneBundleBytes;
        public string notes = "Triangle counts sum all terrain instances, not visible triangles or FPS. Texture bytes are loaded texture memory; optimized includes panoramas. Bundle sizes compare terrain vegetation + sky only, using identical Windows LZ4 builds; not a full game build.";
    }

    static Terrain SceneTerrain() => Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)
        .Single(t => t.gameObject.scene == SceneManager.GetActiveScene());

    // Keep the lake rim, all playable near banks and the swamp extension in 3D.
    static bool IsBackground(TreeInstance tree, Terrain terrain)
    {
        var p = terrain.transform.position + Vector3.Scale(tree.position, terrain.terrainData.size);
        return p.z < 120f && (Mathf.Abs(p.x) > 30f || p.z < -34f);
    }

    static void RequireScene(string path)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        if (SceneManager.GetActiveScene().path != path) throw new InvalidOperationException("Open " + path + " first.");
    }

    public static string Prepare()
    {
        RequireScene(SourceScene);
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Source has unsaved changes. Save a separate copy before optimizing.");
        if (File.Exists(OutputScene) || AssetDatabase.IsValidFolder(Folder))
            throw new InvalidOperationException("Optimization output already exists; refusing to overwrite it.");
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(Captures);
        AssetDatabase.Refresh();
        if (!AssetDatabase.CopyAsset(SourceScene, OutputScene)) throw new IOException("Scene copy failed.");
        var scene = EditorSceneManager.OpenScene(OutputScene, OpenSceneMode.Single);
        var terrain = SceneTerrain();
        var data = Object.Instantiate(terrain.terrainData);
        data.name = "ForestTerrain_Skybox";
        AssetDatabase.CreateAsset(data, TerrainPath);
        terrain.terrainData = data;
        terrain.GetComponent<TerrainCollider>().terrainData = data;
        EditorSceneManager.SaveScene(scene);
        return "Created isolated scene and TerrainData. Original scene and terrain unchanged.";
    }

    // Run one anchor per call to avoid tying up the editor across all captures.
    public static string Bake(int index)
    {
        RequireScene(OutputScene);
        if (index < 0 || index >= Anchors.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var terrain = SceneTerrain();
        var original = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Stage/Terrain/ForestTerrainData.asset");
        var existing = terrain.terrainData;
        var captureData = Object.Instantiate(original);
        captureData.SetTreeInstances(original.treeInstances.Where(i => IsBackground(i, terrain)).ToArray(), false);
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled).ToArray();
        var go = new GameObject("__ForestSkyCapture") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var cube = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32)
            { dimension = TextureDimension.Cube, antiAliasing = 1 };
        var panorama = new RenderTexture(2048, 1024, 0, RenderTextureFormat.ARGB32);
        var previousRT = RenderTexture.active;
        bool drawHeightmap = terrain.drawHeightmap;
        var oldSky = RenderSettings.skybox;
        var blend = Object.FindFirstObjectByType<ForestSkyboxBlend>();
        bool blendEnabled = blend && blend.enabled;
        Texture2D texture = null;
        try
        {
            if (blendEnabled) blend.enabled = false;
            var sourceSky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Stage/Greybox/Mat_Sky_Procedural.mat");
            if (!sourceSky) throw new InvalidOperationException("Source procedural sky is missing.");
            RenderSettings.skybox = sourceSky;
            foreach (var r in renderers) r.enabled = false;
            terrain.terrainData = captureData;
            // Include the original ground behind the trees: bare trunks in the sky otherwise
            // float above the horizon as the camera moves between high and low river banks.
            terrain.drawHeightmap = true;
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = .1f;
            cam.farClipPlane = 400f;
            cam.allowHDR = false;
            cam.cullingMask = ~0;
            cam.transform.position = new Vector3(0f, 5f, Anchors[index]);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            cube.Create(); panorama.Create();
            for (int i = 0; i < 3; i++)
                if (!cam.RenderToCubemap(cube)) throw new InvalidOperationException("Cubemap render failed.");
            cube.ConvertToEquirect(panorama, Camera.MonoOrStereoscopicEye.Mono);
            RenderTexture.active = panorama;
            texture = new Texture2D(2048, 1024, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 2048, 1024), 0, 0);
            texture.Apply();
            string path = Folder + "/ForestPanorama_" + index + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return "Baked " + path + " (" + captureData.treeInstanceCount + " distant trees).";
        }
        finally
        {
            RenderTexture.active = previousRT;
            terrain.terrainData = existing;
            terrain.drawHeightmap = drawHeightmap;
            foreach (var r in renderers) if (r) r.enabled = true;
            RenderSettings.skybox = oldSky;
            if (blendEnabled) blend.enabled = true;
            Object.DestroyImmediate(go);
            cube.Release(); panorama.Release();
            Object.DestroyImmediate(cube); Object.DestroyImmediate(panorama);
            if (texture) Object.DestroyImmediate(texture);
            Object.DestroyImmediate(captureData);
        }
    }

    static Texture2D CompactTexture(Texture2D source, Dictionary<Texture2D, Texture2D> cache)
    {
        if (cache.TryGetValue(source, out var cached)) return cached;
        string sourcePath = AssetDatabase.GetAssetPath(source);
        var sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
        if (!sourceImporter || !File.Exists(sourcePath)) return source;
        bool normal = sourceImporter.textureType == TextureImporterType.NormalMap;
        int maximum = normal ? 512 : 1024;
        // Decode the source file, rather than copying platform-swizzled normal map GPU pixels.
        var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false, !sourceImporter.sRGBTexture);
        if (!raw.LoadImage(File.ReadAllBytes(sourcePath)))
        {
            Object.DestroyImmediate(raw);
            throw new IOException("Cannot decode " + sourcePath);
        }
        float scale = Mathf.Min(1f, maximum / (float)Mathf.Max(raw.width, raw.height));
        int w = Mathf.Max(1, Mathf.RoundToInt(raw.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(raw.height * scale));
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
            sourceImporter.sRGBTexture ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        var resized = new Texture2D(w, h, TextureFormat.RGBA32, false, !sourceImporter.sRGBTexture);
        string path = Folder + "/Textures/" + Path.GetFileNameWithoutExtension(sourcePath) + "_" + AssetDatabase.AssetPathToGUID(sourcePath).Substring(0, 8) + ".png";
        try
        {
            Graphics.Blit(raw, rt);
            RenderTexture.active = rt;
            resized.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            resized.Apply();
            File.WriteAllBytes(path, resized.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(raw); Object.DestroyImmediate(resized);
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var settings = new TextureImporterSettings();
        sourceImporter.ReadTextureSettings(settings);
        importer.SetTextureSettings(settings);
        importer.maxTextureSize = maximum;
        importer.isReadable = false;
        importer.mipmapEnabled = true;
        if (!sourceImporter.DoesSourceTextureHaveAlpha()) importer.alphaSource = TextureImporterAlphaSource.None;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
        cached = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        cache.Add(source, cached);
        return cached;
    }

    static GameObject CompactPrototype(GameObject source, int index, Dictionary<Texture2D, Texture2D> textures,
        Dictionary<Material, Material> materials, Dictionary<Mesh, Mesh> meshes)
    {
        var go = Object.Instantiate(source);
        go.name = source.name + "_Compact";
        try
        {
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var old = filter.sharedMesh;
                if (!old) continue;
                if (!meshes.TryGetValue(old, out var mesh))
                {
                    mesh = Object.Instantiate(old);
                    mesh.name = old.name + "_Compact";
                    MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
                    MeshUtility.Optimize(mesh);
                    AssetDatabase.CreateAsset(mesh, Folder + "/Meshes/Tree_" + index + "_" + meshes.Count + ".asset");
                    meshes.Add(old, mesh);
                }
                filter.sharedMesh = mesh;
            }
            foreach (var collider in go.GetComponentsInChildren<MeshCollider>(true))
                if (collider.sharedMesh && meshes.TryGetValue(collider.sharedMesh, out var mesh)) collider.sharedMesh = mesh;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var result = renderer.sharedMaterials;
                for (int m = 0; m < result.Length; m++)
                {
                    var old = result[m];
                    if (!old) continue;
                    if (!materials.TryGetValue(old, out var mat))
                    {
                        mat = new Material(old) { name = old.name + "_Compact", enableInstancing = true };
                        foreach (var property in mat.GetTexturePropertyNames())
                            if (mat.GetTexture(property) is Texture2D tex) mat.SetTexture(property, CompactTexture(tex, textures));
                        AssetDatabase.CreateAsset(mat, Folder + "/Materials/TreeMaterial_" + materials.Count + ".mat");
                        materials.Add(old, mat);
                    }
                    result[m] = mat;
                }
                renderer.sharedMaterials = result;
            }
            return PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Tree_" + index + ".prefab");
        }
        finally { Object.DestroyImmediate(go); }
    }

    static long Triangles(TerrainData data)
    {
        var counts = data.treePrototypes.Select(p => p.prefab.GetComponentsInChildren<MeshFilter>(true)
            .Where(m => m.sharedMesh).Sum(m => (long)m.sharedMesh.triangles.Length / 3)).ToArray();
        return data.treeInstances.Sum(t => counts[t.prototypeIndex]);
    }

    static long TextureMemory(IEnumerable<Object> roots)
    {
        return EditorUtility.CollectDependencies(roots.ToArray()).OfType<Texture2D>().Distinct()
            .Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
    }

    public static string Apply()
    {
        RequireScene(OutputScene);
        if (Object.FindFirstObjectByType<ForestSkyboxBlend>()) throw new InvalidOperationException("Already applied.");
        var panoramas = Enumerable.Range(0, Anchors.Length)
            .Select(i => AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/ForestPanorama_" + i + ".png")).ToArray();
        if (panoramas.Any(t => !t)) throw new InvalidOperationException("Bake all four panoramas first.");
        var shader = Shader.Find("Carry/Forest Skybox Blend");
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Sky shader is missing or invalid.");
        foreach (string child in new[] { "Textures", "Materials", "Meshes" }) Directory.CreateDirectory(Folder + "/" + child);
        AssetDatabase.Refresh();
        var terrain = SceneTerrain();
        var data = terrain.terrainData;
        var metrics = new Metrics { originalTrees = data.treeInstanceCount, originalTreeTriangles = Triangles(data),
            originalVegetationTextureBytes = TextureMemory(data.treePrototypes.Select(p => (Object)p.prefab)) };
        var remaining = data.treeInstances.Where(i => !IsBackground(i, terrain)).ToArray();
        var textures = new Dictionary<Texture2D, Texture2D>();
        var materials = new Dictionary<Material, Material>();
        var meshes = new Dictionary<Mesh, Mesh>();
        var prototypes = data.treePrototypes;
        for (int i = 0; i < prototypes.Length; i++)
            prototypes[i] = new TreePrototype { prefab = CompactPrototype(prototypes[i].prefab, i, textures, materials, meshes), bendFactor = prototypes[i].bendFactor };
        data.treePrototypes = prototypes;
        data.SetTreeInstances(remaining, false);
        RemapSceneMaterials();
        // These photogrammetry trees have no valid Terrain billboard; do not re-enable the broken cards.
        terrain.treeDistance = 260f;
        terrain.treeBillboardDistance = 250f;
        terrain.treeCrossFadeLength = 0f;
        terrain.treeMaximumFullLODCount = 1000;
        EditorUtility.SetDirty(data);
        var sky = new Material(shader) { name = "ForestSkyboxBlend" };
        sky.SetTexture("_TextureA", panoramas[0]);
        sky.SetTexture("_TextureB", panoramas[1]);
        AssetDatabase.CreateAsset(sky, Folder + "/ForestSkybox.mat");
        RenderSettings.skybox = sky;
        var controller = new GameObject("ForestSkybox").AddComponent<ForestSkyboxBlend>();
        controller.enabled = false;
        controller.skyboxTemplate = sky;
        controller.panoramas = panoramas;
        controller.anchorZ = (float[])Anchors.Clone();
        controller.viewer = Camera.main.transform;
        // Serialize a persistent material, never the preview instance.
        metrics.remainingTrees = data.treeInstanceCount;
        metrics.bakedTrees = metrics.originalTrees - metrics.remainingTrees;
        metrics.remainingTreeTriangles = Triangles(data);
        metrics.optimizedVegetationTextureBytes = TextureMemory(prototypes.Select(p => (Object)p.prefab).Concat(panoramas));
        File.WriteAllText(Captures + "/metrics.json", JsonUtility.ToJson(metrics, true));
        AssetDatabase.SaveAssets();
        controller.enabled = true;
        Save();
        return JsonUtility.ToJson(metrics, true);
    }

    public static void Save()
    {
        RequireScene(OutputScene);
        var controller = Object.FindFirstObjectByType<ForestSkyboxBlend>();
        // OnEnable creates a hidden runtime/preview material. Persist the template reference.
        RenderSettings.skybox = controller.skyboxTemplate;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }

    public static string RemapSceneMaterials()
    {
        RequireScene(OutputScene);
        var original = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Stage/Terrain/ForestTerrainData.asset");
        var compact = SceneTerrain().terrainData;
        var map = new Dictionary<Material, Material>();
        for (int p = 0; p < original.treePrototypes.Length; p++)
        {
            var a = original.treePrototypes[p].prefab.GetComponentsInChildren<Renderer>(true);
            var b = compact.treePrototypes[p].prefab.GetComponentsInChildren<Renderer>(true);
            if (a.Length != b.Length) throw new InvalidOperationException("Prototype renderer layout changed.");
            for (int r = 0; r < a.Length; r++)
                for (int m = 0; m < a[r].sharedMaterials.Length; m++)
                    if (a[r].sharedMaterials[m]) map[a[r].sharedMaterials[m]] = b[r].sharedMaterials[m];
        }
        int changed = 0;
        foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
        {
            var mats = r.sharedMaterials;
            bool dirty = false;
            for (int m = 0; m < mats.Length; m++)
                if (mats[m] && map.TryGetValue(mats[m], out var replacement) && replacement != mats[m])
                { mats[m] = replacement; dirty = true; }
            if (!dirty) continue;
            r.sharedMaterials = mats;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            changed++;
        }
        return "Updated " + changed + " scene renderers sharing terrain tree materials.";
    }

    public static string Capture(string label, float z, float yaw = 0f)
    {
        Directory.CreateDirectory(Captures);
        var go = new GameObject("__ForestComparison") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(Camera.main);
        cam.enabled = false; cam.cullingMask = ~0;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.transform.position = new Vector3(z < 0f ? -3.2f : 0f, 5.5f, z);
        cam.transform.rotation = Quaternion.Euler(26f, yaw, 0f);
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var blend = Object.FindFirstObjectByType<ForestSkyboxBlend>();
        var previousSky = RenderSettings.skybox;
        Material previewSky = null;
        if (blend)
        {
            previewSky = new Material(blend.skyboxTemplate);
            int a = 0;
            while (a < blend.anchorZ.Length - 1 && z >= blend.anchorZ[a + 1]) a++;
            int b = Mathf.Min(a + 1, blend.anchorZ.Length - 1);
            previewSky.SetTexture("_TextureA", blend.panoramas[a]);
            previewSky.SetTexture("_TextureB", blend.panoramas[b]);
            previewSky.SetFloat("_Blend", a == b ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(blend.anchorZ[a], blend.anchorZ[b], z)));
            RenderSettings.skybox = previewSky;
        }
        var previous = RenderTexture.active;
        var rt = new RenderTexture(1280, 720, 24);
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            cam.targetTexture = rt;
            for (int i = 0; i < 4; i++) cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            string path = Captures + "/" + label + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            return path;
        }
        finally
        {
            cam.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(go); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            RenderSettings.skybox = previousSky;
            if (previewSky) Object.DestroyImmediate(previewSky);
        }
    }

    public static string MeasureBundles()
    {
        var original = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Stage/Terrain/ForestTerrainData.asset");
        var compact = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
        var before = original.treePrototypes.Select(p => AssetDatabase.GetAssetPath(p.prefab)).ToArray();
        var after = compact.treePrototypes.Select(p => AssetDatabase.GetAssetPath(p.prefab))
            .Concat(Enumerable.Range(0, Anchors.Length).Select(i => Folder + "/ForestPanorama_" + i + ".png"))
            .Append(Folder + "/ForestSkybox.mat").ToArray();
        const string output = "Temp/ForestSkyboxSize";
        Directory.CreateDirectory(output);
        var builds = new[] { new AssetBundleBuild { assetBundleName = "forest_original", assetNames = before },
            new AssetBundleBuild { assetBundleName = "forest_optimized", assetNames = after } };
        var manifest = BuildPipeline.BuildAssetBundles(output, builds, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        if (!manifest) throw new InvalidOperationException("Vegetation bundle build failed.");
        var metrics = JsonUtility.FromJson<Metrics>(File.ReadAllText(Captures + "/metrics.json"));
        metrics.optimizedVegetationTextureBytes = TextureMemory(compact.treePrototypes.Select(p => (Object)p.prefab)
            .Concat(Enumerable.Range(0, Anchors.Length).Select(i => (Object)AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/ForestPanorama_" + i + ".png"))));
        metrics.originalVegetationBundleBytes = new FileInfo(output + "/forest_original").Length;
        metrics.optimizedVegetationBundleBytes = new FileInfo(output + "/forest_optimized").Length;
        File.WriteAllText(Captures + "/metrics.json", JsonUtility.ToJson(metrics, true));
        return JsonUtility.ToJson(metrics, true);
    }

    public static string MeasureSceneBundles()
    {
        const string output = "Temp/ForestSkyboxSceneSize";
        Directory.CreateDirectory(output);
        var builds = new[] { new AssetBundleBuild { assetBundleName = "scene_original", assetNames = new[] { SourceScene } },
            new AssetBundleBuild { assetBundleName = "scene_optimized", assetNames = new[] { OutputScene } } };
        if (!BuildPipeline.BuildAssetBundles(output, builds, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64))
            throw new InvalidOperationException("Scene bundle build failed.");
        var metrics = JsonUtility.FromJson<Metrics>(File.ReadAllText(Captures + "/metrics.json"));
        metrics.originalSceneBundleBytes = new FileInfo(output + "/scene_original").Length;
        metrics.optimizedSceneBundleBytes = new FileInfo(output + "/scene_optimized").Length;
        File.WriteAllText(Captures + "/metrics.json", JsonUtility.ToJson(metrics, true));
        return JsonUtility.ToJson(metrics, true);
    }
}
