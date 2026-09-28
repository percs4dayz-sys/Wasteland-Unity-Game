using UnityEngine;

/// <summary>Authored geography shared by the editor builder and the in-game atlas.</summary>
public static class BlackwaterAtlas
{
    public const string SceneName = "BlackwaterReach";
    public const float Size = 1024f;
    public const float Sea = 50.3f;
    public struct Place
    {
        public string name, activity;
        public Vector2 position;
        public float height, radius;
        public Place(string n, string a, float x, float z, float h, float r)
        { name = n; activity = a; position = new Vector2(x,z); height = h; radius = r; }
    }
    public static readonly Place[] Places = {
        new Place("Hearthwick", "SAFE HAVEN / BANK / CRAFTING", 360,300,64,68),
        new Place("Orchard Farm", "BEGINNER WOODCUTTING",230,165,66,40),
        new Place("Mosswood", "WOODCUTTING / FOREST TRAIL",170,440,78,38),
        new Place("Hollow Parish", "ABANDONED TOWN / LEVEL 10",330,575,87,52),
        new Place("Cinder Freight", "SALVAGE / LEVEL 10",580,510,68,53),
        new Place("Blackwater Harbor", "FISHING / BANK / SALVAGE",760,325,55,52),
        new Place("Relay Nine", "MILITARY RIDGE / LEVEL 25",505,780,112,53),
        new Place("The Cut", "QUARRY / LEVEL 25",725,700,87,44),
        new Place("Southwatch", "ROAD CHECKPOINT / BEGINNER COMBAT",490,135,63,35)
    };
    public static readonly Vector2[][] Roads = {
        Path(360,300, 318,268, 277,232, 230,165),
        Path(360,300, 291,332, 241,375, 170,440),
        Path(170,440, 222,483, 270,537, 330,575),
        Path(360,300, 367,388, 346,471, 330,575),
        Path(360,300, 453,321, 546,300, 644,309, 760,325),
        Path(546,300, 552,367, 580,438, 580,510),
        Path(330,575, 417,567, 502,539, 580,510),
        Path(330,575, 368,654, 442,713, 505,780),
        Path(505,780, 590,763, 650,727, 725,700),
        Path(580,510, 638,566, 696,615, 725,700),
        Path(760,325, 754,412, 704,463, 580,510),
        Path(360,300, 412,228, 454,188, 490,135),
        Path(230,165, 319,130, 398,116, 490,135)
    };
    static Vector2[] Path(params float[] xy)
    {
        var result = new Vector2[xy.Length/2];
        for (int i=0;i<result.Length;i++) result[i]=new Vector2(xy[i*2],xy[i*2+1]);
        return result;
    }
}
