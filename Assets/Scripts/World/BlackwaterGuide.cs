using UnityEngine;

/// <summary>Scene-local navigation guide. All scenery is authored and saved in the scene.</summary>
public class BlackwaterGuide : MonoBehaviour
{
    bool showMap;
    int region = -1;
    GUIStyle title, small, mapLabel;
    void Update()
    {
        if (!ChatInput.IsTyping && Input.GetKeyDown(KeyCode.F8)) showMap = !showMap;
        if (PlayerEntity.Instance == null) return;
        var p = PlayerEntity.Instance.transform.position;
        var pos = new Vector2(p.x,p.z);
        int nearest = -1;
        float distance = 110f;
        for(int i=0;i<BlackwaterAtlas.Places.Length;i++)
        {
            float d=Vector2.Distance(pos,BlackwaterAtlas.Places[i].position);
            if(d<distance) { distance=d; nearest=i; }
        }
        region=nearest;
    }
    void OnGUI()
    {
        if(title==null)
        {
            title=new GUIStyle(GUI.skin.label) {fontSize=21,alignment=TextAnchor.UpperCenter};
            title.normal.textColor=new Color(.94f,.87f,.69f);
            small=new GUIStyle(GUI.skin.label) {fontSize=11,alignment=TextAnchor.UpperCenter};
            small.normal.textColor=new Color(.78f,.81f,.75f);
            mapLabel=new GUIStyle(GUI.skin.label) {fontSize=12,alignment=TextAnchor.UpperCenter};
            mapLabel.normal.textColor=new Color(.95f,.9f,.75f);
        }
        GUI.Label(new Rect(Screen.width/2-210,16,420,29),region<0 ? "BLACKWATER REACH" : BlackwaterAtlas.Places[region].name.ToUpperInvariant(),title);
        GUI.Label(new Rect(Screen.width/2-240,44,480,22),region<0 ? "THE OLD COAST ROAD  /  F8: ATLAS" : BlackwaterAtlas.Places[region].activity+"  /  F8: ATLAS",small);
        if(!showMap) return;
        float side=Mathf.Min(Screen.height-140,720);
        Rect box=new Rect((Screen.width-side)/2,90,side,side);
        GUI.color=new Color(.08f,.13f,.14f,.98f); GUI.DrawTexture(box,Texture2D.whiteTexture); GUI.color=Color.white;
        Vector2 At(Vector2 p) => new Vector2(box.x+p.x/1024*side,box.y+(1-p.y/1024)*side);
        foreach(var road in BlackwaterAtlas.Roads)
            for(int i=1;i<road.Length;i++) Line(At(road[i-1]),At(road[i]),new Color(.48f,.45f,.32f),3);
        foreach(var place in BlackwaterAtlas.Places)
        {
            var p=At(place.position);
            GUI.color=new Color(.83f,.64f,.34f); GUI.DrawTexture(new Rect(p.x-4,p.y-4,8,8),Texture2D.whiteTexture); GUI.color=Color.white;
            GUI.Label(new Rect(p.x-80,p.y+5,160,24),place.name,mapLabel);
        }
        if(PlayerEntity.Instance!=null)
        {
            var world=PlayerEntity.Instance.transform.position; var p=At(new Vector2(world.x,world.z));
            GUI.color=new Color(.47f,1,.81f); GUI.DrawTexture(new Rect(p.x-4,p.y-4,8,8),Texture2D.whiteTexture); GUI.color=Color.white;
        }
        GUI.Label(new Rect(box.x,box.y+12,side,30),"BLACKWATER REACH  /  N ↑",title);
        GUI.Label(new Rect(box.x,box.yMax-27,side,24),"Green marker: you     •     F8 to close",small);
    }
    static void Line(Vector2 a, Vector2 b, Color color,float width)
    {
        var old=GUI.matrix; GUI.color=color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a);
        GUI.DrawTexture(new Rect(a.x,a.y-width/2,Vector2.Distance(a,b),width),Texture2D.whiteTexture);
        GUI.matrix=old; GUI.color=Color.white;
    }
}
