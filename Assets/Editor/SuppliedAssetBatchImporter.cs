using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Reproducible import and preview helpers for the owner's September 27 Meshy delivery.</summary>
public static class SuppliedAssetBatchImporter
{
    public const string Root = "Assets/Art/SuppliedBatch0927";
    public const string QA = "Docs/QA/SuppliedBatch0927";

    public static string Prepare(string id)
    {
        string dir = Root + "/" + id;
        string fbx = Directory.GetFiles(dir, "*.fbx").Single().Replace('\\','/');
        foreach (string path in Directory.GetFiles(dir, "*.png"))
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) continue;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.textureType = path.Contains("_normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !path.Contains("_metallic") && !path.Contains("_roughness") && !path.Contains("_normal") && !path.Contains("Packed");
            imp.SaveAndReimport();
        }
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
        importer.isReadable = false;
        importer.importCameras = false; importer.importLights = false;
        importer.SaveAndReimport();
        string stem = Path.ChangeExtension(fbx, null);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(dir + "/Surface.mat");
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat,dir+"/Surface.mat"); }
        mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(stem+".png"));
        mat.SetColor("_BaseColor",Color.white);
        var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(stem+"_normal.png");
        if(normal!=null){mat.SetTexture("_BumpMap",normal);mat.EnableKeyword("_NORMALMAP");}
        if(File.Exists(stem+"_metallic.png") && File.Exists(stem+"_roughness.png"))
        {
            var metallic=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            var roughness=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try
            {
                metallic.LoadImage(File.ReadAllBytes(stem+"_metallic.png"));roughness.LoadImage(File.ReadAllBytes(stem+"_roughness.png"));
                var pixels=metallic.GetPixels32();var rough=roughness.GetPixels32();
                for(int i=0;i<pixels.Length;i++) pixels[i]=new Color32(pixels[i].r,0,0,(byte)(255-rough[i].r));
                metallic.SetPixels32(pixels);metallic.Apply();
                string packed=dir+"/PackedMetallicSmoothness.png";File.WriteAllBytes(packed,metallic.EncodeToPNG());AssetDatabase.ImportAsset(packed);
                var imp=(TextureImporter)AssetImporter.GetAtPath(packed);imp.sRGBTexture=false;imp.maxTextureSize=2048;imp.SaveAndReimport();
                mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(packed));mat.EnableKeyword("_METALLICSPECGLOSSMAP");mat.SetFloat("_Smoothness",1);
            }
            finally{Object.DestroyImmediate(metallic);Object.DestroyImmediate(roughness);}
        }
        else {mat.SetFloat("_Metallic",.15f);mat.SetFloat("_Smoothness",.3f);}
        var emission=AssetDatabase.LoadAssetAtPath<Texture2D>(stem+"_emission.png");
        if(emission!=null){mat.SetTexture("_EmissionMap",emission);mat.SetColor("_EmissionColor",Color.white);mat.EnableKeyword("_EMISSION");}
        EditorUtility.SetDirty(mat);
        var root=new GameObject(id);
        try
        {
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            model.transform.SetParent(root.transform,false);
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>mat).ToArray();
            Bounds bounds=BoundsOf(root);float longest=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
            model.transform.localScale=Vector3.one/longest;model.transform.localPosition=-bounds.center/longest;
            PrefabUtility.SaveAsPrefabAsset(root,dir+"/Preview.prefab");
            int verts=root.GetComponentsInChildren<MeshFilter>().Sum(x=>x.sharedMesh!=null?x.sharedMesh.vertexCount:0);
            int bones=root.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(x=>x.bones.Length);
            return id+"\t"+verts+"\t"+bones+"\t"+bounds.size;
        }
        finally{Object.DestroyImmediate(root);AssetDatabase.SaveAssets();}
    }

    public static Bounds BoundsOf(GameObject go)
    {
        var renderers=go.GetComponentsInChildren<Renderer>(true);var bounds=renderers[0].bounds;
        foreach(var r in renderers)bounds.Encapsulate(r.bounds);return bounds;
    }

    public static void Render(string prefabPath,string output,Vector3 direction,int size=512,bool transparent=false)
    {
        var preview=new PreviewRenderUtility();
        try
        {
            preview.camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var go=(GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));preview.AddSingleGO(go);
            var b=BoundsOf(go);float radius=b.extents.magnitude;
            preview.camera.orthographic=true;preview.camera.orthographicSize=radius*1.08f;
            preview.camera.transform.position=b.center+direction.normalized*radius*5;
            preview.camera.transform.LookAt(b.center);preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=radius*12;
            preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=transparent?Color.clear:new Color(.12f,.14f,.17f,1);
            preview.lights[0].intensity=1.6f;preview.lights[0].transform.rotation=Quaternion.Euler(40,30,0);
            preview.lights[1].intensity=1.2f;preview.lights[1].transform.rotation=Quaternion.Euler(320,210,0);
            preview.ambientColor=new Color(.45f,.45f,.45f,1);
            preview.BeginStaticPreview(new Rect(0,0,size,size));preview.Render(true);
            var image=preview.EndStaticPreview();Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllBytes(output,image.EncodeToPNG());Object.DestroyImmediate(image);
        }
        finally{preview.Cleanup();}
    }
}
