using System.IO;
using System.Text;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BlackwaterReview
{
    static int frame;
    public static void Render()
    {
        EditorSceneManager.OpenScene(BlackwaterWorldBuilder.ScenePath);
        foreach(Transform t in GameObject.Find("BLACKWATER REACH — authored world").transform)
            if(t.name.StartsWith("00 "))t.gameObject.SetActive(false);
        EditorApplication.update+=RenderFrame;
    }
    static void RenderFrame()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        frame++;
        if(frame==10||frame==30||frame==50)BlackwaterWorldBuilder.Capture();
        if(frame>50){EditorApplication.update-=RenderFrame;EditorApplication.Exit(0);}
    }
    public static void Inspect()
    {
        EditorSceneManager.OpenScene(BlackwaterWorldBuilder.ScenePath);
        Physics.SyncTransforms();var report=new StringBuilder();
        foreach(var p in new[]{new Vector3(360,64,300),new Vector3(453,64,321),new Vector3(580,68,510)})
        {
            report.AppendLine("COLLIDERS at "+p);
            foreach(var c in Physics.OverlapCapsule(p+Vector3.up*.5f,p+Vector3.up*1.6f,.28f))report.AppendLine(c.name+" "+c.GetType().Name+" "+c.bounds+" parent="+c.transform.root.name);
        }
        foreach(var path in new[]{"Assets/Old Sea Port/prefabs/tower.prefab","Assets/Old Sea Port/prefabs/workbench.prefab","Assets/Hivemind/RuralTown/URP/Art/Prefabs/SM_Barn_Roof_04.prefab","Assets/Resources/Foliage/Tree1.prefab"})
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);report.AppendLine(path+" rotation="+asset.transform.eulerAngles+" scale="+asset.transform.localScale);
            foreach(var material in asset.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct())
            {report.AppendLine(material.name+" shader="+material.shader.name+" supported="+material.shader.isSupported+" path="+AssetDatabase.GetAssetPath(material));}
        }
        foreach(var l in Terrain.activeTerrain.terrainData.terrainLayers)report.AppendLine(l.name+" texture="+AssetDatabase.GetAssetPath(l.diffuseTexture)+" remap="+l.diffuseRemapMax);
        var alpha=Terrain.activeTerrain.terrainData.GetAlphamaps(180,150,1,1);
        report.AppendLine($"Saved paint: {alpha[0,0,0]}, {alpha[0,0,1]}, {alpha[0,0,2]}, {alpha[0,0,3]}");
        File.WriteAllText("Docs/Blackwater/diagnostics.txt",report.ToString());
    }
}
