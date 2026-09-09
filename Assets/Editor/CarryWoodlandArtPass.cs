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

/// <summary>Forest dressing on an isolated copy of the measured Skybox variant.</summary>
public static class CarryWoodlandArtPass
{
    public const string ScenePath = "Assets/Scenes/ForestStage_Woodland.unity";
    public const string Folder = "Assets/Stage/Forest/Woodland";
    public const string Shots = "ScreenshotsOut/Woodland";
    static readonly float[] Anchors = { -12f, 35f, 80f, 125f };
    static Terrain Terrain => Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single(t=>t.gameObject.scene==SceneManager.GetActiveScene());

    static void RequireWoodland()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path!=ScenePath)
            throw new InvalidOperationException("Open the Woodland scene in Edit mode.");
    }

    public static string Prepare()
    {
        if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Use a saved scene in Edit mode.");
        if (File.Exists(ScenePath) || Directory.Exists(Folder)) throw new IOException("Woodland output already exists.");
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(Shots); AssetDatabase.Refresh();
        if (!AssetDatabase.CopyAsset(CarryForestSkyboxOptimizer.OutputScene, ScenePath)) throw new IOException("Scene copy failed.");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var data = Object.Instantiate(Terrain.terrainData);
        AssetDatabase.CreateAsset(data, Folder + "/Terrain.asset");
        Terrain.terrainData = data; Terrain.GetComponent<TerrainCollider>().terrainData = data;
        var map = new Dictionary<Material,Material>();
        var prototypes = data.treePrototypes;
        for (int p=0;p<prototypes.Length;p++)
        {
            var go=Object.Instantiate(prototypes[p].prefab);
            try {
                foreach(var r in go.GetComponentsInChildren<Renderer>(true)) {
                    var mats=r.sharedMaterials;
                    for(int m=0;m<mats.Length;m++) {
                        var old=mats[m];
                        if(!map.TryGetValue(old,out var mat)) {
                            mat=new Material(old); var tex=mat.GetTexture("_BaseMap");
                            bool leaves=tex && tex.name.Contains("diffAlpha");
                            mat.SetColor("_BaseColor",leaves?new Color(.72f,1f,.63f):new Color(.84f,.91f,.80f));
                            if(leaves)mat.SetFloat("_Cutoff",.28f);
                            AssetDatabase.CreateAsset(mat,Folder+"/TreeMaterial_"+map.Count+".mat");map.Add(old,mat);
                        }
                        mats[m]=mat;
                    }
                    r.sharedMaterials=mats;
                }
                prototypes[p]=new TreePrototype { prefab=PrefabUtility.SaveAsPrefabAsset(go,Folder+"/Tree_"+p+".prefab") };
            } finally { Object.DestroyImmediate(go); }
        }
        data.treePrototypes=prototypes;
        foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true))) {
            var mats=r.sharedMaterials;bool changed=false;
            for(int i=0;i<mats.Length;i++) if(mats[i]&&map.TryGetValue(mats[i],out var mat)){mats[i]=mat;changed=true;}
            if(changed){r.sharedMaterials=mats;PrefabUtility.RecordPrefabInstancePropertyModifications(r);}
        }
        // A real, persistent volume replaces the old profile's missing component references.
        var profile=ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile,Folder+"/WoodlandMood.asset");
        var color=profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(-.35f);color.contrast.Override(12f);color.saturation.Override(14f);
        color.colorFilter.Override(new Color(.92f,1f,.90f));
        var white=profile.Add<WhiteBalance>(true);white.temperature.Override(-4f);white.tint.Override(-5f);
        var vignette=profile.Add<Vignette>(true);vignette.intensity.Override(.16f);vignette.smoothness.Override(.5f);
        foreach(var component in profile.components) AssetDatabase.AddObjectToAsset(component,profile);
        var volume=Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).First(v=>v.name=="ForestMoodVolume");
        volume.sharedProfile=profile;volume.priority=10;volume.weight=1;
        RenderSettings.fogColor=new Color(.19f,.29f,.24f);RenderSettings.fogDensity=.0075f;
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.36f,.48f,.42f);
        RenderSettings.ambientEquatorColor=new Color(.22f,.31f,.22f);
        RenderSettings.ambientGroundColor=new Color(.10f,.16f,.085f);
        foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.type==LightType.Directional)) {
            light.intensity=1.35f;light.color=new Color(1f,.95f,.81f);
        }
        var layers=data.terrainLayers;
        for(int i=0;i<layers.Length;i++) {
            var layer=Object.Instantiate(layers[i]);
            layer.diffuseRemapMin=Vector4.zero;
            layer.diffuseRemapMax=i==3?new Vector4(.76f,1f,.67f,1):new Vector4(.78f,.94f,.69f,1);
            AssetDatabase.CreateAsset(layer,Folder+"/Ground_"+i+".terrainlayer");layers[i]=layer;
        }
        data.terrainLayers=layers;
        var sky=Object.FindFirstObjectByType<ForestSkyboxBlend>();
        var skyMat=new Material(sky.skyboxTemplate);AssetDatabase.CreateAsset(skyMat,Folder+"/Skybox.mat");
        sky.skyboxTemplate=skyMat;RenderSettings.skybox=skyMat;
        EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        return "Woodland copy created with independent terrain, tree materials and mood profile.";
    }

    public static string BakePlant(int index)
    {
        RequireWoodland();
        if(index<0 || index>1)throw new ArgumentOutOfRangeException(nameof(index));
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(index==0?
            "Assets/ExternalAssets/PolyHaven/tree_small_02/tree_small_02_2k.fbx":
            "Assets/ExternalAssets/PolyHaven/fern_02/fern_02_2k.fbx");
        if(!source)throw new InvalidOperationException("Plant source missing");
        int layer=31;
        if(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Any(r=>r.gameObject.layer==layer))throw new InvalidOperationException("Capture layer is occupied");
        var go=Object.Instantiate(source);var cameraGO=new GameObject("__PlantBake");
        var rt=new RenderTexture(1024,1024,24,RenderTextureFormat.ARGB32);
        var image=new Texture2D(1024,1024,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;bool fog=RenderSettings.fog;
        try {
            if(index==0) {
                var materials=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Tree_4.prefab").GetComponentInChildren<Renderer>().sharedMaterials;
                foreach(var renderer in go.GetComponentsInChildren<Renderer>())renderer.sharedMaterials=materials;
            } else {
                foreach(var renderer in go.GetComponentsInChildren<Renderer>())if(renderer.name!="fern_02_b")renderer.gameObject.SetActive(false);
            }
            foreach(var tr in go.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=layer;
            var rs=go.GetComponentsInChildren<Renderer>();var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
            var cam=cameraGO.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;
            cam.orthographicSize=Mathf.Max(bounds.extents.x,bounds.extents.y)*1.12f;
            cam.transform.position=bounds.center+new Vector3(0,index==0?1f:8f,-bounds.extents.z-10f);cam.transform.LookAt(bounds.center);
            cam.cullingMask=1<<layer;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.clear;
            cam.allowHDR=false;cam.targetTexture=rt;RenderSettings.fog=false;
            for(int n=0;n<4;n++)cam.Render();
            RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1024,1024),0,0);image.Apply();
            var pixels=image.GetPixels32();int minX=1024,minY=1024,maxX=0,maxY=0;
            for(int y=0;y<1024;y++)for(int x=0;x<1024;x++)if(pixels[y*1024+x].a>70){minX=Mathf.Min(x,minX);minY=Mathf.Min(y,minY);maxX=Mathf.Max(x,maxX);maxY=Mathf.Max(y,maxY);}
            if(minX>=maxX||minY>=maxY)throw new InvalidOperationException("Plant capture is empty");
            minX=Mathf.Max(0,minX-4);minY=Mathf.Max(0,minY-4);maxX=Mathf.Min(1023,maxX+4);maxY=Mathf.Min(1023,maxY+4);
            // Crown only for the broadleaf card: hide its lower trunk inside the ground/canopy.
            if(index==0)minY+=(maxY-minY)/4;
            var trimmed=new Texture2D(maxX-minX+1,maxY-minY+1,TextureFormat.RGBA32,false);
            trimmed.SetPixels(image.GetPixels(minX,minY,trimmed.width,trimmed.height));trimmed.Apply();
            string path=Folder+"/Plant_"+index+".png";File.WriteAllBytes(path,trimmed.EncodeToPNG());Object.DestroyImmediate(trimmed);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.alphaIsTransparency=true;imp.mipmapEnabled=true;
            imp.mipMapsPreserveCoverage=true;imp.alphaTestReferenceValue=.35f;imp.wrapMode=TextureWrapMode.Clamp;
            imp.maxTextureSize=1024;imp.textureCompression=TextureImporterCompression.Compressed;imp.isReadable=false;imp.SaveAndReimport();
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Plant_"+index+".mat");
            if(!mat){mat=new Material(Shader.Find("Carry/Woodland Foliage"));AssetDatabase.CreateAsset(mat,Folder+"/Plant_"+index+".mat");}
            mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));EditorUtility.SetDirty(mat);AssetDatabase.SaveAssets();
            return "Baked "+path+" from "+bounds;
        } finally {
            RenderSettings.fog=fog;RenderTexture.active=previous;
            Object.DestroyImmediate(go);Object.DestroyImmediate(cameraGO);rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);
        }
    }

    sealed class Cards
    {
        public readonly List<Vector3> vertices=new List<Vector3>();
        public readonly List<Vector2> uvs=new List<Vector2>();
        public readonly List<Color> colors=new List<Color>();
        public readonly List<int> triangles=new List<int>();
        public void Add(Vector3 bottom,float width,float height,float yaw,Color color)
        {
            var right=Quaternion.Euler(0,yaw,0)*Vector3.right*width*.5f;
            int i=vertices.Count;vertices.Add(bottom-right);vertices.Add(bottom+right);
            vertices.Add(bottom+right+Vector3.up*height);vertices.Add(bottom-right+Vector3.up*height);
            uvs.Add(new Vector2(0,0));uvs.Add(new Vector2(1,0));uvs.Add(new Vector2(1,1));uvs.Add(new Vector2(0,1));
            for(int n=0;n<4;n++)colors.Add(color);
            triangles.AddRange(new[]{i,i+2,i+1,i,i+3,i+2});
        }
        public void Save(Transform parent,string name,int material,bool persistent=true)
        {
            if(vertices.Count==0)return;
            var mesh=new Mesh { name=name };mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            if(persistent)AssetDatabase.CreateAsset(mesh,Folder+"/"+name+".asset");
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Plant_"+material+".mat");
            r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;
        }
    }
    public static string Dress()
    {
        RequireWoodland();
        if(GameObject.Find("WoodlandVegetation"))throw new InvalidOperationException("Already dressed");
        var root=new GameObject("WoodlandVegetation");var terrain=Terrain;var collider=terrain.GetComponent<TerrainCollider>();
        var random=new System.Random(9142);int plants=0,crowns=0;
        // Chunked by 20m of course so off-screen banks can be culled independently.
        for(int chunk=0;chunk<7;chunk++)for(int type=0;type<2;type++) {
            var cards=new Cards();
            for(int n=0;n<36;n++) {
                float z=-12f+chunk*20f+(float)random.NextDouble()*20f;
                if(z>116f)continue;
                float x=(n%2==0?-1f:1f)*(11f+(float)random.NextDouble()*17f);
                if(!collider.Raycast(new Ray(new Vector3(x,60,z),Vector3.down),out var hit,100))continue;
                if(hit.normal.y<.7f||hit.point.y<-1.1f)continue;
                float height=1.2f+(float)random.NextDouble()*1.6f;
                // Image has a small transparent border; sink it slightly into the measured surface.
                var bottom=hit.point-Vector3.up*height*.09f;
                float yaw=(float)random.NextDouble()*180;
                var color=Color.Lerp(new Color(.57f,.84f,.45f),new Color(.84f,1.05f,.68f),(float)random.NextDouble());
                cards.Add(bottom,height*1.35f,height,yaw,color);cards.Add(bottom,height*1.35f,height,yaw+90,color);plants++;
            }
            cards.Save(root.transform,"Bank_"+chunk+"_"+type,type);
        }
        for(int chunk=0;chunk<7;chunk++) {
            var cards=new Cards();
            foreach(var tree in terrain.terrainData.treeInstances) {
                var p=terrain.transform.position+Vector3.Scale(tree.position,terrain.terrainData.size);
                if(p.z< -12+chunk*20||p.z>=8+chunk*20||p.z>116)continue;
                var prefab=terrain.terrainData.treePrototypes[tree.prototypeIndex].prefab;
                var bounds=prefab.GetComponentsInChildren<Renderer>().First().bounds;
                float height=bounds.max.y*tree.heightScale;
                float width=Mathf.Max(bounds.size.x,bounds.size.z)*tree.widthScale*1.6f;
                for(int c=0;c<3;c++) {
                    var bottom=p+Vector3.up*(height*(.5f+c*.10f));
                    float crownHeight=width*.72f;
                    cards.Add(bottom,width,crownHeight,tree.rotation*Mathf.Rad2Deg+c*60,new Color(.7f,.95f,.55f));crowns++;
                }
            }
            cards.Save(root.transform,"Canopy_"+chunk,0);
        }
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return "Added "+plants+" bank plants and "+crowns+" canopy cards; 3D terrain tree count="+terrain.terrainData.treeInstanceCount;
    }
    public static string Capture(string name,float z)
    {
        // Existing capture helper is scene-agnostic and applies the matching panorama blend.
        string path=CarryForestSkyboxOptimizer.Capture("woodland_"+name,z);
        string dest=Shots+"/"+name+".png";File.Copy(path,dest,true);return dest;
    }

    public static string BakeSky(int index)
    {
        RequireWoodland();
        if(index<0 || index>=Anchors.Length)throw new ArgumentOutOfRangeException(nameof(index));
        var terrain=Terrain;var existing=terrain.terrainData;
        var original=AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Stage/Terrain/ForestTerrainData.asset");
        var data=Object.Instantiate(existing);
        data.SetTreeInstances(original.treeInstances.Where(t=> {
            var p=terrain.transform.position+Vector3.Scale(t.position,data.size);
            return p.z<120 && (Mathf.Abs(p.x)>30 || p.z< -34);
        }).ToArray(),false);
        var renderers=Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.enabled).ToArray();
        var root=new GameObject("__WoodlandSkyCapture");var cards=new Cards();
        var cameraGO=new GameObject("CaptureCamera");cameraGO.transform.SetParent(root.transform,false);
        var cam=cameraGO.AddComponent<Camera>();cam.enabled=false;
        var cube=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32){dimension=TextureDimension.Cube};
        var pano=new RenderTexture(2048,1024,0,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;var oldSky=RenderSettings.skybox;
        var blend=Object.FindFirstObjectByType<ForestSkyboxBlend>();bool enabled=blend.enabled;
        Texture2D texture=null;
        try {
            blend.enabled=false;
            foreach(var r in renderers)r.enabled=false;
            terrain.terrainData=data;
            RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/Stage/Greybox/Mat_Sky_Procedural.mat");
            // Additional background vegetation exists only during baking, never in the saved scene.
            var random=new System.Random(9124);
            foreach(var tree in data.treeInstances) {
                var p=terrain.transform.position+Vector3.Scale(tree.position,data.size);
                float height=12+(float)random.NextDouble()*12;
                cards.Add(p-Vector3.up,height*.8f,height,tree.rotation*Mathf.Rad2Deg,new Color(.66f,.86f,.59f));
                cards.Add(p-Vector3.up,height*.8f,height,tree.rotation*Mathf.Rad2Deg+90,new Color(.66f,.86f,.59f));
            }
            // Close the empty skyline beyond the forest exit, leaving the playable bridge clear.
            for(int row=0;row<3;row++)for(int n=0;n<22;n++) {
                float x=-52+n*5+(float)random.NextDouble()*3,z=158+row*12+(float)random.NextDouble()*7;
                float y=terrain.SampleHeight(new Vector3(x,0,z))+terrain.transform.position.y;
                float height=20+(float)random.NextDouble()*9;
                cards.Add(new Vector3(x,y-9,z),height*.8f,height+8,(float)random.NextDouble()*50-25,new Color(.58f,.78f,.60f));
                cards.Add(new Vector3(x,y-7,z-3),19,16,0,new Color(.58f,.78f,.60f));
            }
            cards.Save(root.transform,"BakedGrove",0,false);
            cam.clearFlags=CameraClearFlags.Skybox;cam.nearClipPlane=.1f;cam.farClipPlane=400;
            cam.allowHDR=false;cam.cullingMask=~0;cam.transform.position=new Vector3(0,5,Anchors[index]);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            cube.Create();pano.Create();
            for(int n=0;n<4;n++)if(!cam.RenderToCubemap(cube))throw new InvalidOperationException("Sky render failed");
            cube.ConvertToEquirect(pano,Camera.MonoOrStereoscopicEye.Mono);RenderTexture.active=pano;
            texture=new Texture2D(2048,1024,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,2048,1024),0,0);texture.Apply();
            string path=Folder+"/Panorama_"+index+".png";File.WriteAllBytes(path,texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.sRGBTexture=true;imp.mipmapEnabled=true;
            imp.wrapModeU=TextureWrapMode.Repeat;imp.wrapModeV=TextureWrapMode.Clamp;imp.maxTextureSize=2048;
            imp.alphaSource=TextureImporterAlphaSource.None;imp.textureCompression=TextureImporterCompression.Compressed;imp.SaveAndReimport();
            return path;
        } finally {
            RenderTexture.active=previous;terrain.terrainData=existing;
            foreach(var r in renderers)if(r)r.enabled=true;
            RenderSettings.skybox=oldSky;blend.enabled=enabled;
            foreach(var f in root.GetComponentsInChildren<MeshFilter>())Object.DestroyImmediate(f.sharedMesh);
            Object.DestroyImmediate(root);Object.DestroyImmediate(data);
            cube.Release();pano.Release();Object.DestroyImmediate(cube);Object.DestroyImmediate(pano);
            if(texture)Object.DestroyImmediate(texture);
        }
    }

    public static string Finish()
    {
        RequireWoodland();
        var blend=Object.FindFirstObjectByType<ForestSkyboxBlend>();
        blend.panoramas=Enumerable.Range(0,4).Select(i=>AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Panorama_"+i+".png")).ToArray();
        if(blend.panoramas.Any(t=>!t))throw new InvalidOperationException("Bake all four panoramas first");
        blend.skyboxTemplate.SetTexture("_TextureA",blend.panoramas[0]);
        blend.skyboxTemplate.SetTexture("_TextureB",blend.panoramas[1]);blend.skyboxTemplate.SetFloat("_Blend",0);
        RenderSettings.skybox=blend.skyboxTemplate;EditorUtility.SetDirty(blend.skyboxTemplate);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return "Saved Woodland art variant";
    }

    public static string GroundCover()
    {
        RequireWoodland();
        var terrain=Terrain;var data=terrain.terrainData;
        var source=AssetDatabase.LoadAssetAtPath<TerrainData>(CarryForestSkyboxOptimizer.Folder+"/ForestTerrain.asset");
        var map=source.GetAlphamaps(0,0,source.alphamapWidth,source.alphamapHeight);
        int changed=0;
        for(int z=0;z<data.alphamapHeight;z++)for(int x=0;x<data.alphamapWidth;x++) {
            float u=x/(float)(data.alphamapWidth-1),v=z/(float)(data.alphamapHeight-1);
            float wx=terrain.transform.position.x+u*data.size.x,wz=terrain.transform.position.z+v*data.size.z;
            float y=data.GetInterpolatedHeight(u,v)+terrain.transform.position.y;
            if(wz< -8 || wz>116 || Mathf.Abs(wx)<9 || Mathf.Abs(wx)>42 || y< -1.1f)continue;
            float slope=data.GetInterpolatedNormal(u,v).y;
            float amount=Mathf.Clamp01((slope-.55f)*3)*(.35f+.35f*Mathf.PerlinNoise(wx*.13f,wz*.13f));
            for(int layer=0;layer<data.alphamapLayers;layer++)map[z,x,layer]*=1-amount;
            map[z,x,3]+=amount;changed++;
        }
        data.SetAlphamaps(0,0,map);EditorUtility.SetDirty(data);
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Folder+"/WoodlandMood.asset");
        if(profile.TryGet<ColorAdjustments>(out var grading)){grading.postExposure.Override(-.05f);grading.contrast.Override(7);EditorUtility.SetDirty(grading);}
        foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.type==LightType.Directional))light.intensity=1.55f;
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return "Moss blended onto "+changed+" bank splat samples";
    }
}
