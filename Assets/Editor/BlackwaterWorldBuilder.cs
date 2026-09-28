using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Builds a fixed, editable composition. No runtime world generation.</summary>
public static class BlackwaterWorldBuilder
{
    const string Folder = "Assets/Worlds/BlackwaterReach";
    public const string ScenePath = Folder + "/BlackwaterReach.unity";
    const string Rural = "Assets/Hivemind/RuralTown/URP/Art/Prefabs/";
    const string Military = "Assets/Hivemind/MilitaryCamp/URP/Art/Prefabs/";
    const string Port = "Assets/Old Sea Port/prefabs/";
    static Terrain terrain;
    static Transform scenery;
    static string output;
    static int count;
    static readonly List<Bounds> occupied = new List<Bounds>();
    static readonly Dictionary<string,GameObject> assets = new Dictionary<string,GameObject>();
    static readonly Dictionary<Material,Material> converted = new Dictionary<Material,Material>();
    static readonly List<Vector3> interactionPoints = new List<Vector3>();
    static Material foundation, signMaterial, waterMaterial;
    static bool replaceGenerated;
    public static void BuildForReview() { replaceGenerated=true; Build(); }
    [MenuItem("Wasteland/Blackwater Reach/Rebake Navigation", false, 3)]
    public static void RebakeNavigation()
    {
        if(!SceneManager.GetActiveScene().name.StartsWith(BlackwaterAtlas.SceneName,StringComparison.Ordinal))
        { Debug.LogWarning("Open Blackwater Reach before rebaking its navigation."); return; }
        terrain=Object.FindAnyObjectByType<Terrain>();
        scenery=GameObject.Find("BLACKWATER REACH — authored world").transform;
        output=AssetDatabase.GenerateUniqueAssetPath(Folder+"/Navigation");Directory.CreateDirectory(output);AssetDatabase.Refresh();
        BuildNavigation(Object.FindAnyObjectByType<PlayerEntity>().gameObject);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
    }

    [MenuItem("Wasteland/Blackwater Reach/Open World", false, 1)]
    public static void Open()
    {
        if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
    }
    [MenuItem("Wasteland/Blackwater Reach/Build New Copy", false, 2)]
    public static void BuildNewCopy()
    {
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }
    public static void Build()
    {
        count=0; occupied.Clear(); assets.Clear(); converted.Clear(); interactionPoints.Clear();
        Directory.CreateDirectory(Folder);
        output=AssetDatabase.GenerateUniqueAssetPath(Folder+"/Composition");
        Directory.CreateDirectory(output); Directory.CreateDirectory("Docs/Blackwater");
        AssetDatabase.Refresh();
        // Copy the current character rather than replacing the user's appearance/animation rig.
        var original = EditorSceneManager.OpenScene("Assets/Scenes/MainWorld3D.unity",OpenSceneMode.Single);
        var sourcePlayer=Object.FindAnyObjectByType<PlayerEntity>(FindObjectsInactive.Include);
        if(sourcePlayer==null)
        {
            // The finished character rig lives in the user's gameplay scene, not the terrain scene.
            original=EditorSceneManager.OpenScene("Assets/dontfuckindelete.unity",OpenSceneMode.Single);
            sourcePlayer=Object.FindAnyObjectByType<PlayerEntity>(FindObjectsInactive.Include);
        }
        if(sourcePlayer==null)throw new InvalidOperationException("No existing gameplay player found; refusing to substitute an older character rig.");
        var sourceGuide=Object.FindAnyObjectByType<RoxyNPC>(FindObjectsInactive.Include);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var player=Object.Instantiate(sourcePlayer.gameObject);
        player.SetActive(true);
        player.name="Player";
        SceneManager.MoveGameObjectToScene(player,scene);
        GameObject townGuide=sourceGuide!=null ? Object.Instantiate(sourceGuide.gameObject) : null;
        if(townGuide!=null)SceneManager.MoveGameObjectToScene(townGuide,scene);
        EditorSceneManager.CloseScene(original,true);
        scenery=new GameObject("BLACKWATER REACH — authored world").transform;
        foundation=Material("Weathered concrete",new Color(.32f,.30f,.26f));
        signMaterial=Material("Signboard charcoal",new Color(.12f,.17f,.16f));
        Lighting();
        BuildTerrain();
        BuildWater();
        BuildTown(); BuildFarm(); BuildWood(); BuildParish(); BuildFreight(); BuildHarbor(); BuildRelay(); BuildQuarry(); BuildCheckpoint();
        if(townGuide!=null){townGuide.name="Roxy — Hearthwick guide";townGuide.transform.position=Ground(344,329);townGuide.SetActive(true);}
        DressLandscape();
        World3DBuilder.BuildManagers();
        var spawn=new GameObject("World Spawn — Hearthwick market");
        spawn.transform.position=Ground(360,285)+Vector3.up*.2f;
        spawn.AddComponent<WorldSpawnPoint>();
        player.transform.position=spawn.transform.position;
        player.transform.rotation=Quaternion.identity;
        var movement=player.GetComponent<Player3DController>();
        if(movement==null) movement=player.AddComponent<Player3DController>();
        movement.seaLevel=BlackwaterAtlas.Sea; movement.blockWater=true;
        if(player.GetComponent<World3DBootstrap>()==null) player.AddComponent<World3DBootstrap>();
        if(player.GetComponent<ClickToMove3D>()==null) player.AddComponent<ClickToMove3D>();
        var cam=World3DBuilder.BuildCamera(player.transform);
        cam.GetComponent<OrbitCamera3D>().distance=19;
        cam.GetComponent<OrbitCamera3D>().pitch=48;
        cam.GetComponent<OrbitCamera3D>().firstPersonOnly=false;
        cam.GetComponent<Camera>().farClipPlane=1400;
        cam.GetComponent<Camera>().GetUniversalAdditionalCameraData().renderPostProcessing=true;
        var guide=new GameObject("Blackwater atlas — F8"); guide.AddComponent<BlackwaterGuide>();
        guide.AddComponent<BlackwaterArrival>();
        var pipeline=Object.Instantiate(GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset);
        pipeline.name="Blackwater coastal lighting";pipeline.shadowDistance=160;pipeline.shadowCascadeCount=4;
        pipeline.msaaSampleCount=4;pipeline.mainLightShadowmapResolution=4096;
        var pipelineSettings=new SerializedObject(pipeline);
        pipelineSettings.FindProperty("m_SoftShadowsSupported").boolValue=true;
        pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(pipeline,output+"/BlackwaterPipeline.asset");
        guide.AddComponent<BlackwaterPresentation>().pipeline=pipeline;
        BuildNavigation(player);
        Physics.SyncTransforms();
        Validate(player);
        string path=File.Exists(ScenePath)&&!replaceGenerated ? AssetDatabase.GenerateUniqueAssetPath(Folder+"/BlackwaterReach.unity") : ScenePath;
        EditorSceneManager.SaveScene(scene,path);
        var scenes=EditorBuildSettings.scenes.ToList();
        if(!scenes.Any(s=>s.path==path)) { scenes.Add(new EditorBuildSettingsScene(path,true)); EditorBuildSettings.scenes=scenes.ToArray(); }
        AssetDatabase.SaveAssets();
        Capture();
        Debug.Log($"BLACKWATER BUILD COMPLETE: {path}, {count} placed assets.");
    }

    static float Smooth(float a,float b,float value) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,value));
    static float Hill(Vector2 p,float x,float z,float rx,float rz,float height)
    { float dx=(p.x-x)/rx,dz=(p.y-z)/rz; return height*Mathf.Exp(-(dx*dx+dz*dz)*2); }
    static float BaseHeight(Vector2 p)
    {
        float h=62+Hill(p,480,820,330,240,48)+Hill(p,110,560,180,360,35)+Hill(p,430,475,90,150,20)
            +Hill(p,650,660,140,130,16)+Hill(p,120,150,140,180,18);
        h+=Mathf.PerlinNoise(p.x*.009f+13,p.y*.009f+41)*4;
        h+=Hill(p,25,210,90,200,68)+Hill(p,48,640,100,220,85)
            +Hill(p,210,960,190,100,95)+Hill(p,505,980,170,125,112)+Hill(p,750,967,130,130,72);
        float coast=820+20*Mathf.Sin(p.y*.011f)+8*Mathf.Sin(p.y*.025f);
        h=Mathf.Lerp(h,42,Smooth(coast-65,coast+22,p.x));
        foreach(var site in BlackwaterAtlas.Places)
            h=Mathf.Lerp(h,site.height,1-Smooth(site.radius,site.radius+40,Vector2.Distance(site.position,p)));
        return h;
    }
    static float RoadDistance(Vector2 p,out float level)
    {
        float nearest=float.MaxValue; level=0;
        foreach(var road in BlackwaterAtlas.Roads)
        for(int i=1;i<road.Length;i++)
        {
            Vector2 a=road[i-1],b=road[i],ab=b-a;
            float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude);
            float d=Vector2.Distance(p,a+ab*t);
            if(d<nearest) { nearest=d; level=Mathf.Lerp(BaseHeight(a),BaseHeight(b),t); }
        }
        return nearest;
    }
    static float Height(Vector2 p)
    {
        float h=BaseHeight(p),d=RoadDistance(p,out float roadHeight);
        return Mathf.Lerp(h,roadHeight,1-Smooth(7,20,d));
    }
    static void BuildTerrain()
    {
        var data=new TerrainData {heightmapResolution=513,size=new Vector3(1024,220,1024),alphamapResolution=512,baseMapResolution=1024};
        AssetDatabase.CreateAsset(data,output+"/BlackwaterTerrain.asset");
        var heights=new float[513,513];
        for(int z=0;z<513;z++) for(int x=0;x<513;x++) heights[z,x]=Height(new Vector2(x*2,z*2))/220;
        data.SetHeights(0,0,heights);
        string[] layerPaths={
            "Assets/Hivemind/MilitaryCamp/URP/Art/Terrain/TL_Grass_01.terrainlayer",
            "Assets/Hivemind/RuralTown/URP/Art/Terrain/TL_Ground.terrainlayer",
            "Assets/Hivemind/RuralTown/URP/Art/Terrain/TL_Rock.terrainlayer",
            "Assets/Hivemind/RuralTown/URP/Art/Terrain/TL_ForestFloor.terrainlayer"};
        var layers=new TerrainLayer[4];
        for(int i=0;i<4;i++)
        {
            var source=AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPaths[i]);
            if(source==null) throw new InvalidOperationException("Missing terrain layer "+layerPaths[i]);
            layers[i]=Object.Instantiate(source); layers[i].name=new[]{"Meadow","Old coast road","Cliff stone","Woodland floor"}[i];
            layers[i].tileSize=new Vector2(i==2 ? 13:5,i==2 ? 13:5); layers[i].normalScale=.7f;
            layers[i].metallic=0; layers[i].smoothness=.1f;
            if(i==0)
            {
                layers[i].diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Hivemind/TownSmith/URP/Art/Terrain/TerrainTextures/T_grass_BaseColor2.png");
                layers[i].normalMapTexture=null;layers[i].maskMapTexture=null;
                layers[i].diffuseRemapMax=new Vector4(.72f,.78f,.65f,1);
            }
            if(i==2)layers[i].diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Hivemind/RuralTown/URP/Art/Textures/T_Rock_Sandstone_D.png");
            layers[i].diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/SurfaceTextures/"+new[]{"CoastalGrass","PackedEarth","RidgeStone","ForestFloor"}[i]+".png");
            layers[i].maskMapTexture=null;
            layers[i].diffuseRemapMin=new Vector4(0,0,0,1);
            layers[i].diffuseRemapMax=Vector4.one;
            AssetDatabase.CreateAsset(layers[i],output+"/Layer"+i+".terrainlayer");
        }
        data.terrainLayers=layers;
        var splat=new float[512,512,4];
        for(int z=0;z<512;z++) for(int x=0;x<512;x++)
        {
            var p=new Vector2(x/511f*1024,z/511f*1024);
            float h=Height(p); float road=RoadDistance(p,out _);
            float steep=data.GetSteepness(x/511f,z/511f);
            float dirt=(1-Smooth(4,9,road))*.95f;
            foreach(var s in BlackwaterAtlas.Places)
                dirt=Mathf.Max(dirt,(1-Smooth(s.radius*.65f,s.radius,Vector2.Distance(s.position,p)))*.74f);
            dirt=Mathf.Max(dirt,1-Smooth(51,57,h));
            float rock=Smooth(23,43,steep);
            float woods=Mathf.Clamp01(Hill(p,150,465,185,210,1.2f)+Hill(p,410,650,190,140,.6f))*(1-dirt)*(1-rock);
            splat[z,x,2]=rock; splat[z,x,1]=dirt*(1-rock); splat[z,x,3]=woods;
            splat[z,x,0]=Mathf.Max(0,1-splat[z,x,1]-rock-woods);
        }
        data.SetAlphamaps(0,0,splat); EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
        File.WriteAllText("Docs/Blackwater/terrain-paint.txt",$"Town road weights: {splat[150,180,0]}, {splat[150,180,1]}, {splat[150,180,2]}, {splat[150,180,3]}");
        terrain=Terrain.CreateTerrainGameObject(data).GetComponent<Terrain>(); terrain.name="Blackwater landform — sculpted routes and coastal shelf";
        terrain.transform.SetParent(scenery); terrain.drawInstanced=true; terrain.heightmapPixelError=5; terrain.basemapDistance=1100;
        var mat=new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")); AssetDatabase.CreateAsset(mat,output+"/Terrain.mat"); terrain.materialTemplate=mat;
    }
    static Vector3 Ground(float x,float z) => new Vector3(x,terrain.SampleHeight(new Vector3(x,0,z)),z);
    static void BuildRoadSurfaces()
    {
        var g=Group("00 COAST ROAD — continuous worn surfaces and settlement square");
        var asphalt=Material("Weathered road",new Color(.72f,.70f,.64f));
        asphalt.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/SurfaceTextures/OldAsphalt.png"));
        var trail=Material("Trail gravel",new Color(.68f,.59f,.43f));
        trail.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Hivemind/RuralTown/URP/Art/Textures/T_Ground_BC.png"));
        for(int r=0;r<BlackwaterAtlas.Roads.Length;r++)
        {
            var road=BlackwaterAtlas.Roads[r];
            for(int i=1;i<road.Length;i++)Ribbon(g,road[i-1],road[i],r<3?3.8f:6.2f,r<3?trail:asphalt);
        }
        Ribbon(g,new Vector2(360,278),new Vector2(360,333),24,trail);
        Ribbon(g,new Vector2(337,301),new Vector2(382,301),8,trail);
        Ribbon(g,new Vector2(374,301),new Vector2(374,335),5,trail);
        Ribbon(g,new Vector2(748,306),new Vector2(748,349),5,trail);
    }
    static void Ribbon(Transform parent,Vector2 a,Vector2 b,float width,Material material)
    {
        int steps=Mathf.CeilToInt(Vector2.Distance(a,b)/2);
        var side=new Vector2(-(b-a).y,(b-a).x).normalized*width/2;
        var verts=new Vector3[(steps+1)*2];var uv=new Vector2[verts.Length];var tris=new int[steps*6];
        for(int i=0;i<=steps;i++)
        {
            var p=Vector2.Lerp(a,b,i/(float)steps);var l=p-side;var r=p+side;
            verts[i*2]=Ground(l.x,l.y)+Vector3.up*.065f;verts[i*2+1]=Ground(r.x,r.y)+Vector3.up*.065f;
            uv[i*2]=new Vector2(0,i*2/7f);uv[i*2+1]=new Vector2(width/7,i*2/7f);
            if(i<steps){int j=i*6,k=i*2;tris[j]=k;tris[j+1]=k+1;tris[j+2]=k+2;tris[j+3]=k+1;tris[j+4]=k+3;tris[j+5]=k+2;}
        }
        var mesh=new Mesh {name="Road surface",vertices=verts,uv=uv,triangles=tris};mesh.RecalculateNormals();mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(output+"/Road.asset"));
        var go=new GameObject("Road",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;
    }
    static Transform Group(string name) {var g=new GameObject(name).transform;g.SetParent(scenery);return g;}
    static Material Material(string name,Color color)
    {
        var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.12f);
        AssetDatabase.CreateAsset(m,output+"/"+name+".mat");return m;
    }
    static Bounds BoundsOf(GameObject go)
    {
        var rs=go.GetComponentsInChildren<Renderer>();
        if(rs.Length==0) throw new InvalidOperationException("No mesh in "+go.name);
        var b=rs[0].bounds; foreach(var r in rs)b.Encapsulate(r.bounds); return b;
    }
    static GameObject Asset(string path)
    {
        if(!assets.TryGetValue(path,out var asset))
        {asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(asset==null)throw new InvalidOperationException("Missing curated asset "+path);assets[path]=asset;}
        return asset;
    }
    static GameObject Place(string path,Transform parent,Vector3 position,float yaw=0,float height=0,bool solid=true,bool ground=true)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(Asset(path)); go.transform.SetParent(parent,false);
        go.transform.rotation=Quaternion.Euler(0,yaw,0)*go.transform.rotation;
        if(height>0) go.transform.localScale*=height/Mathf.Max(.01f,BoundsOf(go).size.y);
        var b=BoundsOf(go); go.transform.position+=position-new Vector3(b.center.x,ground?b.min.y:b.center.y,b.center.z);
        foreach(var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true)) if(behaviour!=null)behaviour.enabled=false;
        foreach(var light in go.GetComponentsInChildren<Light>(true)) light.enabled=false;
        foreach(var audio in go.GetComponentsInChildren<AudioSource>(true)) audio.enabled=false;
        foreach(var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats=r.sharedMaterials;
            for(int i=0;i<mats.Length;i++)
            {
                var m=mats[i]; if(m==null)continue;
                // Several purchased materials were previously assigned incompatible shaders.
                // Read their saved texture slots and make world-local, predictable URP copies.
                if(true)
                {
                    if(!converted.TryGetValue(m,out var copy))
                    {
                        copy=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=m.name+" Blackwater"};
                        var albedo=SavedTexture(m,"_BaseMap","_MainTex","_BaseColorMap","_Albedo","Material_Texture2D_0");
                        copy.SetTexture("_BaseMap",albedo);
                        copy.SetColor("_BaseColor",Color.white);
                        if(m.name=="M_Leaves_A")copy.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/SurfaceTextures/CoastalLeaves.png"));
                        if(m.name=="M_Transparan")copy.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/LeartesStudios/AzureHillside/Art/Textures/T_SpruceBark_B.png"));
                        var normal=SavedTexture(m,"_BumpMap","_NormalMap");
                        if(normal!=null){copy.SetTexture("_BumpMap",normal);copy.EnableKeyword("_NORMALMAP");copy.SetFloat("_BumpScale",.65f);}
                        copy.SetFloat("_Smoothness",.15f);
                        bool leaves=m.name.IndexOf("leav",StringComparison.OrdinalIgnoreCase)>=0||m.name.IndexOf("grass",StringComparison.OrdinalIgnoreCase)>=0;
                        if(leaves){copy.SetFloat("_AlphaClip",1);copy.EnableKeyword("_ALPHATEST_ON");copy.SetFloat("_Cutoff",.42f);copy.SetFloat("_Cull",0);copy.renderQueue=2450;}
                        copy.enableInstancing=true;
                        AssetDatabase.CreateAsset(copy,AssetDatabase.GenerateUniqueAssetPath(output+"/ConvertedMaterial.mat"));converted[m]=copy;
                    }
                    mats[i]=copy;
                }
            }
            r.sharedMaterials=mats;
        }
        foreach(var lod in go.GetComponentsInChildren<LODGroup>()) {lod.fadeMode=LODFadeMode.None;lod.animateCrossFading=false;}
        var visualBounds=BoundsOf(go);
        foreach(var c in go.GetComponentsInChildren<Collider>())
            if(c.bounds.size.magnitude>visualBounds.size.magnitude*2)Object.DestroyImmediate(c);
        if(!solid) foreach(var c in go.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
        else if(go.GetComponentsInChildren<Collider>().Length==0)
        {
            // Mesh colliders retain openings on modular walls. Only the highest LOD needs physics.
            foreach(var filter in go.GetComponentsInChildren<MeshFilter>())
                if(!filter.name.Contains("LOD1")&&!filter.name.Contains("LOD2")&&!filter.name.Contains("LOD3")&&filter.sharedMesh!=null)
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
        }
        count++; return go;
    }
    static Texture SavedTexture(Material material,params string[] keys)
    {
        var property=new SerializedObject(material).FindProperty("m_SavedProperties.m_TexEnvs");
        foreach(string key in keys)
        for(int i=0;i<property.arraySize;i++)
        {
            var element=property.GetArrayElementAtIndex(i);
            if(element.FindPropertyRelative("first").stringValue!=key)continue;
            var tex=element.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
            if(tex!=null)return tex;
        }
        return null;
    }
    static GameObject Prop(string path,Transform parent,float x,float z,float yaw=0,float height=0,bool solid=true)
        => Place(path,parent,Ground(x,z),yaw,height,solid);
    static GameObject Box(string name,Transform parent,Vector3 center,Vector3 size,Material mat,bool solid=true)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent);go.transform.position=center;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;
        if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    static void Building(Transform parent,string name,float x,float z,float yaw=0,bool ruin=false)
    {
        var root=new GameObject(name).transform;root.SetParent(parent);root.position=Ground(x,z);
        // Modules are centered by measured render bounds, then the whole composition rotates.
        for(int side=0;side<4;side++) for(int panel=0;panel<2;panel++)
        {
            if(ruin&&side==2&&panel==1)continue;
            string piece=side==0&&panel==0 ? "SM_Wall_Door_4m" : "SM_Window_Wall_01";
            float along=-2+panel*4;
            var local=side==0?new Vector3(along,0,-4):side==1?new Vector3(4,0,along):side==2?new Vector3(along,0,4):new Vector3(-4,0,along);
            Place(Rural+piece+".prefab",root,root.position+local,side%2==0?90:0);
        }
        Box("Raised foundation",root,root.position+new Vector3(0,-.13f,0),new Vector3(8.3f,.3f,8.3f),foundation);
        if(!ruin)
        {
            for(int side=-1;side<=1;side+=2)
            {
                var pivot=new GameObject("Pitched roof").transform;pivot.SetParent(root);pivot.position=root.position+new Vector3(side*2.05f,4.5f,0);
                var roof=Place(Rural+"SM_Roof_5m_02.prefab",pivot,pivot.position,0,0,false,false);
                var size=BoundsOf(roof).size; pivot.localScale=new Vector3(4.7f/size.x,.22f/size.y,8.8f/size.z);
                pivot.localRotation=Quaternion.Euler(0,0,-side*28);
            }
            var mesh=new Mesh {name="Timber gable"};
            mesh.vertices=new[]{new Vector3(-4.1f,3.48f,-4),new Vector3(4.1f,3.48f,-4),new Vector3(0,5.62f,-4),new Vector3(-4.1f,3.48f,4),new Vector3(4.1f,3.48f,4),new Vector3(0,5.62f,4)};
            mesh.triangles=new[]{0,2,1,0,1,2,3,4,5,3,5,4};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one*.5f,Vector2.zero,Vector2.right,Vector2.one*.5f};mesh.RecalculateNormals();
            AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(output+"/Gable.asset"));
            var gable=new GameObject("Closed timber gables",typeof(MeshFilter),typeof(MeshRenderer));gable.transform.SetParent(root,false);gable.GetComponent<MeshFilter>().sharedMesh=mesh;
            gable.GetComponent<MeshRenderer>().sharedMaterial=root.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        }
        Prop(Rural+"SM_WoodenCrate_01.prefab",root,x-5,z+2,15,.8f);
        Prop(Rural+"SM_Fence_01.prefab",root,x,z+7,90);
        root.rotation=Quaternion.Euler(0,yaw,0);
        occupied.Add(new Bounds(root.position+Vector3.up*4,new Vector3(14,12,14)));
    }
    static void Sign(Transform parent,string text,float x,float z,float yaw=0)
    {
        var root=new GameObject("Wayfinding — "+text.Replace('\n',' ')).transform;root.SetParent(parent);root.position=Ground(x,z);
        Box("Post",root,root.position+Vector3.up*1.2f,new Vector3(.16f,2.4f,.16f),foundation);
        Box("Board",root,root.position+Vector3.up*2.2f,new Vector3(3.2f,1.0f,.15f),signMaterial);
        var label=new GameObject("Lettering");label.transform.SetParent(root);label.transform.position=root.position+new Vector3(0,2.2f,-.085f);
        var tm=label.AddComponent<TextMesh>();tm.text=text;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.characterSize=.09f;tm.fontSize=48;tm.color=new Color(.94f,.85f,.64f);
        root.rotation=Quaternion.Euler(0,yaw,0);
    }
    static void Bank(Transform group,float x,float z)
    {var go=Prop(Military+"SM_MilitaryCrate_01.prefab",group,x,z,0,1);go.AddComponent<BankingCrate>().label="Settlement bank";interactionPoints.Add(Ground(x,z-2));}
    static void Station(Transform group,StationType type,float x,float z)
    {
        string path=type==StationType.Workbench?Port+"workbench.prefab":type==StationType.CookingFire?Port+"fireplace.prefab":Military+"SM_Generator_01.prefab";
        var go=Prop(path,group,x,z,0,type==StationType.CookingFire?.7f:1.15f);
        var station=go.AddComponent<CraftingStation>();station.stationType=type;interactionPoints.Add(Ground(x,z-2));
        Sign(group,type==StationType.Furnace?"SMELTERY":type==StationType.Workbench?"WORKSHOP":"COOKHOUSE",x+2,z+1);
    }
    static void Node(Transform parent,int type,int tier,float x,float z)
    {
        string[][] names={new[]{"Junk Pile (T1)","abandoned car (T2)","tech dumpster (T3)"},new[]{"Dead Scrub & Brush (T1)","Irradiated Thicket (T2)","fossilized deadwood (T3)"},new[]{"Fishing Spot (T1)","Fishing Spot (T2)","Fishing Spot (T3)"}};
        var go=Prop(type==2?Port+"mooring_rope_holder_1.prefab":"Assets/Resources/Nodes/"+names[type][tier-1]+".prefab",parent,x,z,(x*13+z*7)%360,type==0?1.1f:type==1?2.5f:.5f,false);
        var body=new GameObject("Interaction collider").transform;body.SetParent(go.transform);body.position=Ground(x,z)+Vector3.up*.6f;body.rotation=Quaternion.identity;body.localScale=new Vector3(1/go.transform.lossyScale.x,1/go.transform.lossyScale.y,1/go.transform.lossyScale.z);
        body.gameObject.AddComponent<BoxCollider>().size=new Vector3(1.1f,1.2f,1.1f);
        var node=go.GetComponent<ResourceNode>()??go.AddComponent<ResourceNode>();node.enabled=true;
        node.skill=type==0?Skill.Scrapping:type==1?Skill.Woodcutting:Skill.Fishing;
        node.nodeType=type==0?ResourceNodeType.RubblePile:type==1?ResourceNodeType.WoodenDebris:ResourceNodeType.WaterBarrel;
        node.requiredToolId=type==0?3:type==1?4:5;node.levelRequired=new[]{1,10,25}[tier-1];
        node.dropItemId=type==0?new[]{10,60,61}[tier-1]:type==1?new[]{220,221,222}[tier-1]:new[]{200,201,202}[tier-1];
        node.xpPerAction=new[]{25,45,80}[tier-1];node.respawnTicks=18;node.minYield=2;node.maxYield=6;
        if(type==2)node.displayNameOverride="Harbor fishing spot";
        interactionPoints.Add(Ground(x,z-2));occupied.Add(new Bounds(Ground(x,z),new Vector3(5,8,5)));
    }
    static void Enemy(Transform parent,string name,float x,float z,int tier)
    {
        var go=Prop("Assets/Resources/Enemies/Mutant.prefab",parent,x,z,180,tier==3?2.3f:1.85f,false);
        var body=new GameObject("Combat collider").transform;body.SetParent(go.transform);body.position=Ground(x,z)+Vector3.up*.9f;body.rotation=Quaternion.identity;body.localScale=new Vector3(1/go.transform.lossyScale.x,1/go.transform.lossyScale.y,1/go.transform.lossyScale.z);
        var capsule=body.gameObject.AddComponent<CapsuleCollider>();capsule.radius=.4f;capsule.height=1.8f;
        go.name=name;
        var target=go.GetComponent<CombatTarget>()??go.AddComponent<CombatTarget>();target.enabled=true;target.maxHP=new[]{15,30,55}[tier-1];target.attackLevel=tier*4;target.defenceLevel=tier*3;target.maxDamage=tier*2;target.tier=tier;target.isAggressive=true;
        var ai=go.GetComponent<Enemy3D>()??go.AddComponent<Enemy3D>();ai.enabled=true;ai.aggroRange=8;ai.wanderRadius=2;ai.respawnSeconds=35;
        occupied.Add(new Bounds(Ground(x,z),new Vector3(8,8,8)));
    }
    static void BuildTown()
    {
        var g=Group("01 HEARTHWICK — market / safe settlement");
        Building(g,"Bank house",331,292,-90);Building(g,"Workshop",390,316,90);Building(g,"Cookhouse",381,335,180);
        Building(g,"North lodging",352,343,180);Building(g,"Apothecary",331,338,90);
        Building(g,"West homestead",318,290,60);Building(g,"East homestead",397,288,-65);
        Building(g,"South cottages",340,269,15);Building(g,"South cottage",371,254,-15);
        Building(g,"Gate store",308,338,90);Building(g,"Raised chapel",359,360,180);
        Bank(g,336,301);Station(g,StationType.Workbench,374,302);Station(g,StationType.Furnace,397,325);Station(g,StationType.CookingFire,374,329);
        Sign(g,"HEARTHWICK\nBANK • MARKET • WORKSHOP",354,314);
        Sign(g,"← MOSSWOOD\nHARBOR →",359,278);
        Prop(Port+"Wood Canopy.prefab",g,350,320,90,3.1f);
        Prop(Military+"SM_Table.prefab",g,350,320,90,1);
        Prop(Rural+"SM_WoodenCrate_01.prefab",g,347,320,15,1);
        Prop(Port+"barrel.prefab",g,347,317,0,1);
        Prop(Military+"SM_WatchTower_01.prefab",g,400,335,0,14);
        foreach(var p in new[]{new Vector2(326,300),new Vector2(391,299),new Vector2(349,326),new Vector2(370,343),new Vector2(344,278),new Vector2(386,278)})
        {Prop(Rural+"SM_LampPost.prefab",g,p.x,p.y,0,3.4f);}
        for(int i=0;i<6;i++) {Prop(Rural+"SM_Fence_01.prefab",g,319+i*5,255,90);Prop(Rural+"SM_Fence_01.prefab",g,410+i*5,255,90);}
        Node(g,0,1,412,313);Node(g,0,1,417,306);Node(g,1,1,306,276);
    }
    static void BuildFarm()
    {
        var g=Group("02 ORCHARD FARM — beginner supply loop");Building(g,"Farmstead",216,179,30);Building(g,"Barn",278,181,180);
        Sign(g,"ORCHARD FARM\nHEARTHWICK ↗",233,149);
        for(int i=0;i<4;i++)for(int j=0;j<3;j++)
            Prop("Assets/Resources/Foliage/Tree1.prefab",g,208+i*10,140+j*10,(i+j)*47,6,false);
        for(int i=0;i<5;i++)Prop(Rural+"SM_Fence_01.prefab",g,205+i*5,128,90);
        Node(g,1,1,246,151);Node(g,1,1,250,145);Node(g,1,1,260,161);
        Prop(Port+"barrel.prefab",g,220,174,0,1);Prop(Rural+"SM_WoodenCrate_01.prefab",g,223,173,0,1);
    }
    static void BuildWood()
    {
        var g=Group("03 MOSSWOOD — lumber camp and return trail");Building(g,"Forester shelter",154,452,90);
        Prop(Military+"SM_Tent_a.prefab",g,181,460,180,3.4f);
        Sign(g,"MOSSWOOD\nPARISH ↗  •  HOME ↘",173,429);
        for(int i=0;i<6;i++)Node(g,1,i<3?1:2,146+i*7,424+(i%2)*10);
        Station(g,StationType.CookingFire,164,452);
    }
    static void BuildParish()
    {
        var g=Group("04 HOLLOW PARISH — abandoned settlement");
        Building(g,"Roofless row",307,568,65,true);Building(g,"Lost home",347,588,180);Building(g,"Burned home",305,594,90,true);
        Building(g,"Chapel shell",330,608,0,true);Building(g,"Evacuated school",354,556,-90);
        Prop(Port+"tower.prefab",g,325,623,0,20);
        Sign(g,"HOLLOW PARISH\nROAD NORTH: RELAY NINE",324,554);
        Node(g,0,2,356,579);Node(g,1,2,306,609);Node(g,0,2,314,585);
        Enemy(g,"Parish scavenger",340,601,2);Enemy(g,"Parish scavenger",299,583,2);Enemy(g,"Parish scavenger",357,578,2);
    }
    static void BuildFreight()
    {
        var g=Group("05 CINDER FREIGHT — ordered container lanes / salvage");
        for(int row=0;row<3;row++)for(int col=0;col<4;col++)
            if(!(row==2&&col==2))Prop(Military+"SM_Container_a"+(col%2==0?"":"_1")+".prefab",g,new[]{550,563,600,613}[col],487+row*20,0,2.9f);
        Building(g,"Freight office",615,529,180);Prop(Military+"SM_WatchTower_01.prefab",g,615,481,0,15);
        Prop(Military+"SM_ArmoredVehicle.prefab",g,545,512,25);
        Sign(g,"CINDER FREIGHT\nSALVAGE YARD — KEEP WATCH",575,474);
        for(int i=0;i<5;i++)Node(g,0,2,550+i*13,537);
        Enemy(g,"Freight lurker",560,516,2);Enemy(g,"Freight lurker",595,496,2);Enemy(g,"Freight lurker",608,518,2);
    }
    static void BuildHarbor()
    {
        var g=Group("06 BLACKWATER HARBOR — shore hub and working piers");
        Building(g,"Harbor store",740,310,90);Building(g,"Net mender",742,345,90);Building(g,"Dockmaster",777,294,0);
        Bank(g,748,314);Station(g,StationType.CookingFire,747,345);
        Sign(g,"BLACKWATER HARBOR\nBANK • FISHING • COAST ROAD",759,311);
        Prop(Port+"tower.prefab",g,789,358,0,22);
        for(int i=0;i<5;i++)
        {
            // Ground-height quays keep walking compatible with the existing shoreline controller.
            Box("Quayside paving",g,new Vector3(785+i*7,54.85f,325),new Vector3(7.1f,.3f,9),foundation);
            Prop(Port+"post_1.prefab",g,782+i*7,331,0,1.4f);
        }
        for(int i=0;i<3;i++)Node(g,2,i==2?2:1,789,307+i*17);
        Prop(Port+"boat_1.prefab",g,846,326,95,1.5f,false).transform.position=new Vector3(846,BlackwaterAtlas.Sea-.3f,326);
        Prop(Port+"crate_1.prefab",g,778,339,15,1.1f);Prop(Port+"barrel.prefab",g,779,337,0,1.2f);
    }
    static void BuildRelay()
    {
        var g=Group("07 RELAY NINE — military ridge / high risk");
        Prop(Military+"SM_WatchTower_01.prefab",g,478,765,0,19);
        Prop(Military+"SM_WatchTower_01.prefab",g,533,804,180,19);
        Prop(Military+"SM_Tent_a.prefab",g,486,794,90);Prop(Military+"SM_Tent_b.prefab",g,527,825,270);
        Building(g,"Relay control",505,815,180);
        Prop(Military+"SM_SatelliteDish_01.prefab",g,504,828,180,10);
        Prop(Military+"SM_Generator_01.prefab",g,519,816,0,2);
        for(int i=3;i<7;i++)Prop(Military+"SM_HescoBastion_01.prefab",g,476+i*8,751,0,1.5f);
        Sign(g,"RELAY NINE\nRESTRICTED MILITARY ZONE",503,744);
        for(int i=0;i<4;i++)Node(g,0,3,490+i*9,811);
        Enemy(g,"Relay sentry",488,780,3);Enemy(g,"Relay sentry",516,798,3);Enemy(g,"Relay sentry",531,765,3);
    }
    static void BuildQuarry()
    {
        var g=Group("08 THE CUT — extraction yard");
        Prop(Military+"SM_Container_a.prefab",g,748,703,90,3);
        Prop(Military+"SM_Generator_01.prefab",g,742,718,0,2);
        Sign(g,"THE CUT\nINDUSTRIAL SALVAGE",720,679);
        for(int i=0;i<9;i++)Prop(Military+"SM_Rock_01.prefab",g,694+i*7,734+(i%3)*4,i*35,5+i%3);
        for(int i=0;i<5;i++)Node(g,0,3,703+i*10,709+(i%2)*8);
        Enemy(g,"Quarry stalker",716,726,3);Enemy(g,"Quarry stalker",741,691,3);
    }
    static void BuildCheckpoint()
    {
        var g=Group("09 SOUTHWATCH — beginner combat clearing");
        Prop(Military+"SM_Tent_a.prefab",g,470,141,90);Prop(Military+"SM_ArmoredVehicle.prefab",g,509,149,-20);
        Prop(Military+"SM_WatchTower_01.prefab",g,510,120,0,13);
        Sign(g,"SOUTHWATCH\nHEARTHWICK ↖",492,117);
        Node(g,0,1,467,126);Node(g,0,1,472,120);
        Enemy(g,"Roadside scavenger",517,139,1);Enemy(g,"Roadside scavenger",476,157,1);
    }
    static void DressLandscape()
    {
        var trees=Group("10 WOODLAND — composed groves and ridge silhouettes");
        // Fixed grove envelopes; paths, settlement pads, shore and sightlines are excluded.
        var patches=new[]{new Vector4(135,370,72,125),new Vector4(205,530,82,98),new Vector4(265,395,45,85),new Vector4(420,465,46,94),new Vector4(419,653,72,60),new Vector4(650,204,110,65),new Vector4(185,250,70,42),new Vector4(640,828,95,40),new Vector4(105,715,60,112)};
        for(int patch=0;patch<patches.Length;patch++)for(int i=0;i<64;i++)
        {
            var a=patches[patch];float angle=i*2.399963f+patch;float radius=Mathf.Sqrt((i+.5f)/64);
            var p=new Vector2(a.x+Mathf.Cos(angle)*a.z*radius,a.y+Mathf.Sin(angle)*a.w*radius);
            if(Mathf.PerlinNoise(p.x*.07f,p.y*.07f)<.4f)continue;
            if(RoadDistance(p,out _)<12||BlackwaterAtlas.Places.Any(s=>Vector2.Distance(p,s.position)<s.radius+8))continue;
            var point=Ground(p.x,p.y);if(point.y<56||occupied.Any(b=>b.Contains(point+Vector3.up*2)))continue;
            var tree=Place("Assets/Resources/Foliage/Tree"+(i%3==0?2:1)+".prefab",trees,point,i*137,10+(i%5)*1.7f,false);
            var trunk=tree.AddComponent<CapsuleCollider>();var b=BoundsOf(tree);trunk.center=tree.transform.InverseTransformPoint(point+Vector3.up*2);
            trunk.height=4/tree.transform.lossyScale.y;trunk.radius=.3f/tree.transform.lossyScale.x;
        }
        var detail=Group("11 VERGES — stones, grass and roadside remnants");
        foreach(var road in BlackwaterAtlas.Roads)for(int seg=1;seg<road.Length;seg++)
        {
            var a=road[seg-1];var b=road[seg];var side=new Vector2(-(b-a).y,(b-a).x).normalized;
            int steps=Mathf.FloorToInt(Vector2.Distance(a,b)/8);
            for(int i=1;i<steps;i++)for(int edge=-1;edge<=1;edge+=2)
            {
                if(i%5!=0)continue;
                var p=Vector2.Lerp(a,b,i/(float)steps)+side*edge*(14+i%9);
                if(BlackwaterAtlas.Places.Any(s=>Vector2.Distance(p,s.position)<s.radius)||RoadDistance(p,out _)<8)continue;
                Prop("Assets/Resources/Foliage/Rocks1.prefab",detail,p.x,p.y,i*79,.65f,false);
            }
        }
        // Rocky outcrops reinforce the enclosing ridge, never fill the road corridors.
        for(int i=0;i<65;i++)
        {
            float x=80+i*11,z=900+Mathf.Sin(i*.7f)*22;
            Prop(Military+"SM_Rock_0"+(1+i%3)+".prefab",detail,x,z,i*71,9+i%7);
        }
        ArrangeRidgeRocks(detail);
        // Landmark wayfinding at decision points, not a repeated sign at every prop.
        Sign(detail,"← HEARTHWICK\nFREIGHT ↑  •  HARBOR →",545,288);
        Sign(detail,"← PARISH\nQUARRY ↗  •  HARBOR ↘",600,544);
        Sign(detail,"← MOSSWOOD\nPARISH ↑",359,452);
    }
    static void ArrangeRidgeRocks(Transform parent)
    {
        var centers=new[]{new Vector2(92,735),new Vector2(178,916),new Vector2(339,918),new Vector2(485,949),new Vector2(645,899),new Vector2(755,950),new Vector2(65,230)};
        int i=0;
        foreach(Transform rock in parent)
        {
            if(!rock.name.StartsWith("SM_Rock_",StringComparison.Ordinal))continue;
            int cluster=i%centers.Length,ring=i/centers.Length;
            float angle=ring*2.39996f+cluster*.83f, radius=Mathf.Sqrt(ring+1)*4.1f;
            var p=centers[cluster]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
            var b=BoundsOf(rock.gameObject);
            rock.position+=Ground(p.x,p.y)-new Vector3(b.center.x,b.min.y+b.size.y*.28f,b.center.z);
            i++;
        }
    }
    public static void FinishSavedWorld()
    {
        EditorSceneManager.OpenScene(ScenePath);
        terrain=Object.FindAnyObjectByType<Terrain>();scenery=GameObject.Find("BLACKWATER REACH — authored world").transform;
        ArrangeRidgeRocks(scenery.Find("11 VERGES — stones, grass and roadside remnants"));
        count=Object.FindObjectsByType<Transform>().Count(t=>PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)&&t.IsChildOf(scenery));
        interactionPoints.Clear();
        output=AssetDatabase.GenerateUniqueAssetPath(Folder+"/Navigation");Directory.CreateDirectory(output);AssetDatabase.Refresh();
        var player=Object.FindAnyObjectByType<PlayerEntity>();BuildNavigation(player.gameObject);Physics.SyncTransforms();Validate(player.gameObject);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        var dependencies=new HashSet<string>(AssetDatabase.GetDependencies(ScenePath,true));
        string absoluteRoot=Path.GetFullPath(Folder)+Path.DirectorySeparatorChar;
        foreach(string directory in Directory.GetDirectories(Folder,"Composition*"))
        {
            string full=Path.GetFullPath(directory);
            if(!full.StartsWith(absoluteRoot,StringComparison.OrdinalIgnoreCase))throw new IOException("Cleanup outside world folder refused");
            string assetDirectory=directory.Replace('\\','/');
            if(!dependencies.Any(path=>path.StartsWith(assetDirectory+"/",StringComparison.Ordinal)))AssetDatabase.DeleteAsset(assetDirectory);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("BLACKWATER FINALIZED AND UNUSED REVIEW ASSETS REMOVED");
    }
    static void Lighting()
    {
        RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.54f,.65f,.72f);RenderSettings.ambientEquatorColor=new Color(.4f,.45f,.43f);RenderSettings.ambientGroundColor=new Color(.22f,.23f,.19f);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.0013f;RenderSettings.fogColor=new Color(.59f,.68f,.7f);
        var sun=new GameObject("Late afternoon sun").AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(1,.88f,.69f);sun.intensity=1.7f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(38,-35,0);RenderSettings.sun=sun;
        var sky=new Material(Shader.Find("Skybox/Procedural"));sky.SetColor("_SkyTint",new Color(.52f,.59f,.65f));sky.SetFloat("_AtmosphereThickness",.85f);AssetDatabase.CreateAsset(sky,output+"/CoastalSky.mat");RenderSettings.skybox=sky;
        var profile=ScriptableObject.CreateInstance<VolumeProfile>();
        var tone=profile.Add<Tonemapping>();tone.mode.Override(TonemappingMode.ACES);
        var adjust=profile.Add<ColorAdjustments>();adjust.saturation.Override(-8);adjust.contrast.Override(8);
        adjust.postExposure.Override(.35f);
        AssetDatabase.CreateAsset(profile,output+"/CoastalGrade.asset");
        foreach(var c in profile.components)AssetDatabase.AddObjectToAsset(c,profile);
        var volume=new GameObject("Coastal color grade").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=profile;
    }
    static void BuildNavigation(GameObject player)
    {
        var sources=new List<UnityEngine.AI.NavMeshBuildSource>();
        var marks=new List<UnityEngine.AI.NavMeshBuildMarkup>();
        foreach(var enemy in Object.FindObjectsByType<Enemy3D>())
            marks.Add(new UnityEngine.AI.NavMeshBuildMarkup {root=enemy.transform,ignoreFromBuild=true});
        UnityEngine.AI.NavMeshBuilder.CollectSources(scenery,~0,UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders,0,marks,sources);
        // Exclude the sea, following the actual sculpted shoreline rather than a rectangular cutoff.
        for(int z=0;z<1024;z+=8)
        {
            float shore=1024;
            for(int x=740;x<1024;x+=2)if(terrain.SampleHeight(new Vector3(x,0,z+4))<BlackwaterAtlas.Sea+.3f){shore=x;break;}
            if(shore<1024)sources.Add(new UnityEngine.AI.NavMeshBuildSource {shape=UnityEngine.AI.NavMeshBuildSourceShape.ModifierBox,area=1,
                transform=Matrix4x4.TRS(new Vector3((shore+1024)/2,50,z+4),Quaternion.identity,Vector3.one),size=new Vector3(1024-shore,100,8)});
        }
        var settings=UnityEngine.AI.NavMesh.GetSettingsByID(0);settings.agentRadius=.4f;settings.agentHeight=1.8f;settings.agentClimb=.35f;settings.agentSlope=38;
        settings.overrideVoxelSize=true;settings.voxelSize=.2f;settings.overrideTileSize=true;settings.tileSize=256;
        var data=UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(new Vector3(512,110,512),new Vector3(1024,220,1024)),Vector3.zero,Quaternion.identity);
        if(data==null)throw new InvalidOperationException("Navigation bake failed");
        AssetDatabase.CreateAsset(data,output+"/BlackwaterNavigation.asset");
        var navigation=player.GetComponent<BlackwaterNavigator>()??player.AddComponent<BlackwaterNavigator>();navigation.data=data;
    }
    static void BuildWater()
    {
        waterMaterial=Material("Estuary water",new Color(.12f,.29f,.31f));waterMaterial.SetFloat("_Smoothness",.86f);waterMaterial.SetFloat("_Metallic",.25f);
        var g=Group("12 ESTUARY — eastern coastline");
        Box("Blackwater estuary",g,new Vector3(945,BlackwaterAtlas.Sea-.12f,512),new Vector3(370,.2f,1200),waterMaterial,false);
    }
    static void Validate(GameObject player)
    {
        var report=new StringBuilder("BLACKWATER REACH — build validation\n");int failures=0;
        foreach(var road in BlackwaterAtlas.Roads)for(int seg=1;seg<road.Length;seg++)
        {
            int steps=Mathf.CeilToInt(Vector2.Distance(road[seg-1],road[seg])/2);
            for(int i=0;i<=steps;i++)
            {
                Vector2 p=Vector2.Lerp(road[seg-1],road[seg],i/(float)steps);var world=Ground(p.x,p.y);
                if(world.y<BlackwaterAtlas.Sea || terrain.terrainData.GetSteepness(p.x/1024,p.y/1024)>32)
                {failures++;report.AppendLine("FAIL road grade / water at "+world);}
                var hits=Physics.OverlapCapsule(world+Vector3.up*.5f,world+Vector3.up*1.6f,.28f,~0,QueryTriggerInteraction.Ignore);
                if(hits.Length>0)
                {failures++;report.AppendLine("FAIL blocked road at "+world+" by "+string.Join(", ",hits.Select(h=>h.name+" ("+h.GetType().Name+")")));}
            }
        }
        foreach(var pos in interactionPoints)
            if(Physics.CheckCapsule(pos+Vector3.up*.5f,pos+Vector3.up*1.6f,.28f,~0,QueryTriggerInteraction.Ignore))
                report.AppendLine("REVIEW interaction approach: "+pos);
        var navigation=player.GetComponent<BlackwaterNavigator>();
        var navInstance=UnityEngine.AI.NavMesh.AddNavMeshData(navigation.data);
        var navPath=new UnityEngine.AI.NavMeshPath();
        try
        {
            bool startValid=UnityEngine.AI.NavMesh.SamplePosition(player.transform.position,out var start,3,UnityEngine.AI.NavMesh.AllAreas);
            foreach(var site in BlackwaterAtlas.Places)
            {
                bool endValid=UnityEngine.AI.NavMesh.SamplePosition(Ground(site.position.x,site.position.y),out var end,3,UnityEngine.AI.NavMesh.AllAreas);
                bool reachable=startValid&&endValid&&UnityEngine.AI.NavMesh.CalculatePath(start.position,end.position,UnityEngine.AI.NavMesh.AllAreas,navPath)&&navPath.status==UnityEngine.AI.NavMeshPathStatus.PathComplete;
                report.AppendLine((reachable?"PASS connected: ":"FAIL disconnected: ")+site.name);
                if(!reachable)failures++;
            }
        }
        finally {navInstance.Remove();}
        report.AppendLine($"Placed assets: {count}\nRoad failures: {failures}\nNodes: {Object.FindObjectsByType<ResourceNode>().Length}\nEnemies: {Object.FindObjectsByType<Enemy3D>().Length}\nSpawn: {player.transform.position}");
        File.WriteAllText("Docs/Blackwater/validation.txt",report.ToString());
        if(failures>0)Debug.LogWarning("Blackwater roads require review: "+failures);
    }
    public static void Capture()
    {
        if(terrain==null)terrain=Object.FindAnyObjectByType<Terrain>();
        var lods=Object.FindObjectsByType<LODGroup>();
        foreach(var lod in lods)lod.ForceLOD(0);
        var hidden=new List<Renderer>();
        foreach(var lod in lods)foreach(var level in lod.GetLODs().Skip(1))foreach(var renderer in level.renderers)
            if(renderer!=null&&renderer.enabled){renderer.enabled=false;hidden.Add(renderer);}
        Directory.CreateDirectory("Docs/Blackwater");
        var go=new GameObject("Temporary world review camera");var camera=go.AddComponent<Camera>();camera.farClipPlane=2200;camera.fieldOfView=52;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        camera.GetUniversalAdditionalCameraData().antialiasing=AntialiasingMode.FastApproximateAntialiasing;
        Shot(camera,"01-hearthwick",new Vector3(393,90,260),new Vector3(359,67,309));
        Shot(camera,"02-coast-road",new Vector3(440,85,289),new Vector3(620,64,322));
        Shot(camera,"03-harbor",new Vector3(703,92,278),new Vector3(782,55,326));
        Shot(camera,"04-relay",new Vector3(548,145,734),new Vector3(502,114,796));
        camera.orthographic=true;camera.orthographicSize=530;
        bool fog=RenderSettings.fog;RenderSettings.fog=false;
        Shot(camera,"05-world-overview",new Vector3(512,1250,512),new Vector3(512,0,512));
        RenderSettings.fog=fog;
        Object.DestroyImmediate(go);
        foreach(var renderer in hidden)renderer.enabled=true;
        foreach(var lod in lods)lod.ForceLOD(-1);
    }
    static void Shot(Camera camera,string name,Vector3 eye,Vector3 target)
    {
        camera.transform.position=eye;camera.transform.LookAt(target);
        var rt=new RenderTexture(1600,1000,24);camera.targetTexture=rt;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=rt;
        var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();
        File.WriteAllBytes("Docs/Blackwater/"+name+".png",texture.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(texture);Object.DestroyImmediate(rt);
    }
}
