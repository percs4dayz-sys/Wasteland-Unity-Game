using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Synty.SidekickCharacters.Enums;

// Validation uses a preview scene, never Play Mode or the user's inventory/save.
public static class CharacterEquipmentValidation
{
    public const string Output = "Docs/CharacterEquipmentValidation";
    static Scene scene;
    static GameObject root;
    static ArmourVisuals armour;
    static Equipment equipment;

    public static void Open()
    {
        Close();
        Directory.CreateDirectory(Output);
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerEntity>();
        var visual = player.GetComponentInChildren<Animator>(true);
        scene = EditorSceneManager.NewPreviewScene();
        root = new GameObject("Character equipment validation");
        SceneManager.MoveGameObjectToScene(root, scene);
        var clone = UnityEngine.Object.Instantiate(visual.gameObject, root.transform);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        equipment = new Equipment();
        armour = root.AddComponent<ArmourVisuals>();
        armour.Initialize(equipment);
    }

    public static void Close()
    {
        if (root != null) UnityEngine.Object.DestroyImmediate(root);
        if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        root = null;
    }

    public static string ValidateAppearance()
    {
        Open();
        var report = new System.Text.StringBuilder();
        int count = 0, failures = 0;
        try
        {
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[]{"Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans"})
                .Select(AssetDatabase.GUIDToAssetPath))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!CharacterCreatorConfig.TryParse(name, out _, out _, out var type) ||
                    (type != CharacterPartType.Hair && type != CharacterPartType.FacialHair)) continue;
                var parts = new Dictionary<CharacterPartType,string>();
                parts[CharacterPartType.Head] = "SK_HUMN_BASE_01_01HEAD_HU01";
                parts[type] = name;
                armour.SetBaseLoadout(parts);
                var renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name == "Part_"+type);
                var baked = new Mesh();
                renderer.BakeMesh(baked);
                var bounds = baked.bounds;
                bool sane = renderer.enabled && baked.vertexCount > 0 && bounds.size.magnitude < 1.5f &&
                    bounds.center.y > 1.0f && bounds.center.y < 2.1f;
                report.AppendLine((sane ? "PASS " : "FAIL ")+name+" "+bounds);
                count++; if(!sane) failures++;
                UnityEngine.Object.DestroyImmediate(baked);
            }
            armour.SetBaseLoadout(new Dictionary<CharacterPartType,string>{
                [CharacterPartType.Head]="SK_HUMN_BASE_01_01HEAD_HU01",
                [CharacterPartType.Hair]="SK_HUMN_BASE_01_02HAIR_HU01",
                [CharacterPartType.FacialHair]="SK_HUMN_BASE_01_09FCHR_HU01"});
            Capture(root, new Vector3(0,1.6f,0), 0.42f, "hair-beard-front.png", new Vector3(0,0,1));
            Capture(root, new Vector3(0,1.6f,0), 0.42f, "hair-beard-side.png", new Vector3(1,0,0));
            report.AppendLine($"Appearance variants: {count}, failures: {failures}");
            File.WriteAllText(Output+"/appearance.txt", report.ToString());
            return report.ToString();
        }
        finally { Close(); }
    }

    public static string ModelAtlas()
    {
        Directory.CreateDirectory(Output);
        scene = EditorSceneManager.NewPreviewScene();
        root = new GameObject("Held model audit");
        SceneManager.MoveGameObjectToScene(root, scene);
        var report = new System.Text.StringBuilder();
        try
        {
            foreach(var item in ItemRegistry.All.OrderBy(i=>i.id))
            {
                if(string.IsNullOrEmpty(item.heldModel)) continue;
                var prefab=HeldModels.Load(item.heldModel);
                if(prefab==null){report.AppendLine(item.id+" MISSING "+item.name);continue;}
                var model=UnityEngine.Object.Instantiate(prefab, root.transform);
                model.transform.localPosition=Vector3.zero;
                var rends=model.GetComponentsInChildren<Renderer>();
                if(rends.Length==0){UnityEngine.Object.DestroyImmediate(model);continue;}
                var b=rends[0].bounds;foreach(var r in rends)b.Encapsulate(r.bounds);
                report.AppendLine(item.id+" "+item.name+" "+b);
                float size=Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));
                Capture(model,b.center,size*0.65f,item.id+"-front.png",Vector3.forward);
                Capture(model,b.center,size*0.65f,item.id+"-side.png",Vector3.right);
                UnityEngine.Object.DestroyImmediate(model);
            }
            File.WriteAllText(Output+"/models.txt",report.ToString());
            return report.ToString();
        }
        finally{Close();}
    }

    public static string ValidateEquipment()
    {
        Open();
        var report=new System.Text.StringBuilder();
        var held=root.AddComponent<EquipmentVisuals>();held.Initialize(equipment);
        var anim=root.GetComponentInChildren<Animator>();
        anim.Rebind();anim.Update(0);
        var baseParts=new Dictionary<CharacterPartType,string>();
        foreach(var guid in AssetDatabase.FindAssets("t:Model",new[]{"Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans"}))
        {
            string n=Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
            if(CharacterCreatorConfig.TryParse(n,out _,out int set,out var type)&&set==1)baseParts[type]=n;
        }
        // Match the creator's fixed clothing.
        foreach(var guid in AssetDatabase.FindAssets("t:Model",new[]{"Assets/Synty/SidekickCharacters/Resources/Meshes/Outfits/Starter"}))
        {
            string n=Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
            if(n.StartsWith("SK_SCFI_CIVL_09_")&&CharacterCreatorConfig.TryParse(n,out _,out _,out var type)&&
                CharacterCreatorConfig.ClothingSlots.Contains(type))baseParts[type]=n;
        }
        armour.SetBaseLoadout(baseParts);
        try
        {
            int armours=0,weapons=0;
            foreach(var item in ItemRegistry.All.OrderBy(i=>i.id))
            {
                bool worn=item.type==ItemType.Helmet||item.type==ItemType.Chest||item.type==ItemType.Legs;
                bool hand=item.type==ItemType.Weapon||item.type==ItemType.Shield||item.type==ItemType.Tool;
                if(!worn&&!hand)continue;
                equipment.Equip(item);
                if(worn)
                {
                    armours++;
                    var visible=root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled&&r.name.StartsWith("Part_"));
                    int matched=visible.Count(r=>r.sharedMesh.name.StartsWith(item.armourMesh??"<missing>"));
                    report.AppendLine((matched>0?"PASS ":"FAIL ")+item.id+" "+item.name+" armor parts="+matched);
                    foreach(var r in visible)
                    {
                        var mesh=new Mesh();r.BakeMesh(mesh);
                        if(mesh.bounds.size.magnitude>4||float.IsNaN(mesh.bounds.center.x))report.AppendLine("FAIL DEFORMATION "+r.name);
                        UnityEngine.Object.DestroyImmediate(mesh);
                    }
                }
                else
                {
                    weapons++;
                    var objects=root.GetComponentsInChildren<Transform>().Where(t=>t.name=="Held_"+item.name).ToArray();
                    int expected=item.weaponStyle==WeaponStyle.Fission?2:1;
                    bool valid=objects.Length==expected&&objects.All(t=>!t.parent.name.StartsWith("HeldAnchor_"));
                    report.AppendLine((valid?"PASS ":"FAIL ")+item.id+" "+item.name+" held="+objects.Length);
                }
                Capture(root,new Vector3(0,1,0),1.2f,"equipped-"+item.id+"-front.png",new Vector3(0,0,1));
                Capture(root,new Vector3(0,1,0),1.2f,"equipped-"+item.id+"-side.png",new Vector3(1,0,0));
                equipment.Unequip(equipment.SlotFor(item));
            }
            report.AppendLine("Armour items="+armours+" held items="+weapons);
            File.WriteAllText(Output+"/equipment.txt",report.ToString());
            return report.ToString();
        }
        finally{Close();}
    }

    static void Capture(GameObject subject, Vector3 centre, float size, string filename, Vector3 direction)
    {
        var go=new GameObject("Validation camera");SceneManager.MoveGameObjectToScene(go,scene);
        var camera=go.AddComponent<Camera>();camera.scene=scene;
        camera.orthographic=true;camera.orthographicSize=size;
        camera.transform.position=centre+direction*Mathf.Max(5,size*4);
        camera.transform.LookAt(centre);camera.nearClipPlane=0.01f;camera.farClipPlane=Mathf.Max(50,size*10);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.15f,.18f);
        var lightGo=new GameObject("Validation light");SceneManager.MoveGameObjectToScene(lightGo,scene);
        var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;
        lightGo.transform.rotation=Quaternion.Euler(35,-25,0);
        var rt=RenderTexture.GetTemporary(512,512,24);var previous=RenderTexture.active;
        camera.targetTexture=rt;
        try{camera.Render();RenderTexture.active=rt;var tex=new Texture2D(512,512,TextureFormat.RGB24,false);
            tex.ReadPixels(new Rect(0,0,512,512),0,0);tex.Apply();File.WriteAllBytes(Output+"/"+filename,tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);}
        finally{RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(lightGo);}
    }
}
