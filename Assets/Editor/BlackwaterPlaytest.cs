using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Batch-mode traversal test of the saved world, using the actual player controller.</summary>
[InitializeOnLoad]
public static class BlackwaterPlaytest
{
    const string Key="Blackwater.Playtest";
    static double started,legStarted;
    static int leg;
    static bool initialized;
    static Vector2[] route={new Vector2(360,300),new Vector2(453,321),new Vector2(546,300),new Vector2(644,309),new Vector2(760,325)};
    static readonly StringBuilder report=new StringBuilder();
    static readonly HashSet<string> errors=new HashSet<string>();
    static BlackwaterPlaytest(){EditorApplication.update+=Update;Application.logMessageReceived+=Log;}
    public static void Run()
    {
        EditorSceneManager.OpenScene(BlackwaterWorldBuilder.ScenePath);
        string save=SaveManager.SaveFilePath;
        SessionState.SetString(Key+"Save",save);
        SessionState.SetString(Key+"Original",File.Exists(save)?File.ReadAllText(save):"");
        // Testing starts at the authored spawn without consuming the user's saved run.
        if(File.Exists(save))File.Delete(save);
        SessionState.SetBool(Key,true);
        EditorApplication.isPlaying=true;
    }
    static void Log(string message,string stack,LogType type)
    {
        if(SessionState.GetBool(Key,false)&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))errors.Add(message);
    }
    static void Update()
    {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying)return;
        if(!initialized)
        {
            if(PlayerEntity.Instance==null||Time.timeSinceLevelLoad<3)return;
            initialized=true;started=legStarted=EditorApplication.timeSinceStartup;
            Time.timeScale=4; report.AppendLine("Blackwater runtime smoke test");
            report.AppendLine("Player: "+PlayerEntity.Instance.name+" at "+PlayerEntity.Instance.transform.position);
            report.AppendLine("Tick manager: "+(GameTick.Instance!=null));
            report.AppendLine("World mode: "+GameMode.Is3D);
            BlackwaterWorldBuilder.Capture();
            CapturePlayerView();
            BankUI.Instance.Open(PlayerEntity.Instance);BankUI.Instance.Close();
            report.AppendLine("Bank UI opened and closed.");
            var nodes=UnityEngine.Object.FindObjectsByType<ResourceNode>();
            foreach(var node in nodes)if(node.skill==Skill.Scrapping&&node.levelRequired==1)
            {
                var result=node.TryGatherCycle(PlayerEntity.Instance);
                report.AppendLine("Tier 1 scrap gather: "+result.result+" / "+result.message);break;
            }
            SetDestination();
        }
        var player=PlayerEntity.Instance;
        if(player==null){Finish(false,"Player disappeared");return;}
        var p=player.transform.position;
        if(Vector2.Distance(new Vector2(p.x,p.z),route[leg])<1.5f)
        {
            report.AppendLine("Reached waypoint "+leg+" at "+p);
            leg++;
            if(leg==route.Length){Finish(true,"Walked Hearthwick to harbor using Player3DController");return;}
            legStarted=EditorApplication.timeSinceStartup;SetDestination();
        }
        if(EditorApplication.timeSinceStartup-legStarted>75)Finish(false,"Traversal stalled on leg "+leg+" at "+p);
        if(EditorApplication.timeSinceStartup-started>300)Finish(false,"Timeout");
    }
    static void SetDestination()
    {
        Vector2 p=route[leg];var point=new Vector3(p.x,0,p.y);point.y=Terrain.activeTerrain.SampleHeight(point);
        PlayerEntity.Instance.GetComponent<Player3DController>().SetDestination(point);
    }
    static void CapturePlayerView()
    {
        var camera=Camera.main;if(camera==null)return;
        var target=new RenderTexture(1600,1000,24);var previousTarget=camera.targetTexture;
        var previousActive=RenderTexture.active;
        camera.targetTexture=target;camera.Render();RenderTexture.active=target;
        var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();
        File.WriteAllBytes("Docs/Blackwater/06-player-view.png",pixels.EncodeToPNG());
        camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
        UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(target);
    }
    static void Finish(bool success,string message)
    {
        report.AppendLine((success?"PASS: ":"FAIL: ")+message);
        report.AppendLine("Game ticks elapsed: "+(GameTick.Instance!=null?GameTick.Instance.TickCount:0));
        report.AppendLine("Runtime errors: "+errors.Count);foreach(var e in errors)report.AppendLine(e);
        File.WriteAllText("Docs/Blackwater/playtest.txt",report.ToString());
        SessionState.SetBool(Key,false);Time.timeScale=1;
        EditorApplication.isPlaying=false;
        EditorApplication.delayCall+=()=>
        {
            var path=SessionState.GetString(Key+"Save","");var original=SessionState.GetString(Key+"Original","");
            if(original.Length>0)File.WriteAllText(path,original);else if(File.Exists(path))File.Delete(path);
            EditorApplication.Exit(success&&errors.Count==0?0:1);
        };
    }
}
