using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click: turns the imported Meshy assets in "Assets/Art/new assets nodes etc" into configured
/// tiered gather-node PREFABS (saved to _Wasteland/Nodes), per the Skill Progression doc.
///
/// Scrap → RubblePile/Scrapping (Pickaxe id3).  Trees → WoodenDebris/Woodcutting (Hatchet id4).
/// Tiers gate at levels 1/20/40/60/80 and drop the matching tier item (scrap ores 10/60-63, wood
/// 40/220-223 — see TierMaterials / TierGatherables). The in-game node name comes from `niceName`
/// (ResourceNode.displayNameOverride), so "abandoned car.glb" reads as "Abandoned Car".
///
/// Menu:  Wasteland ▸ World ▸ Create Tiered Node Prefabs
/// </summary>
public static class CreateTieredNodes
{
    const string SrcFolder = "Assets/Art/new assets nodes etc/";

    struct Def
    {
        public string asset, niceName; public ResourceNodeType type; public Skill skill;
        public int level, tool, drop, xp;
        public Def(string a, string nice, ResourceNodeType t, Skill s, int lvl, int tool, int drop, int xp)
        { asset = a; niceName = nice; this.type = t; skill = s; level = lvl; this.tool = tool; this.drop = drop; this.xp = xp; }
    }

    static readonly Def[] Nodes =
    {
        // Scrap line (Scrapping, Pickaxe=3) → ores 10/60-63
        new("Junk Pile",            "Junk Pile",              ResourceNodeType.RubblePile,   Skill.Scrapping, 1,  3, 10, 25),
        new("abandoned car",        "Abandoned Car",          ResourceNodeType.RubblePile,   Skill.Scrapping, 20, 3, 60, 50),
        new("tech dumpster",        "Tech Dumpster",          ResourceNodeType.RubblePile,   Skill.Scrapping, 40, 3, 61, 80),
        new("downed drones pile",   "Downed Military Drone",  ResourceNodeType.RubblePile,   Skill.Scrapping, 60, 3, 62, 120),
        new("abandoned robotics",   "Abandoned Robotics",     ResourceNodeType.RubblePile,   Skill.Scrapping, 80, 3, 63, 170),
        // Tree line (Woodcutting, Hatchet=4) → wood 40/220-223
        new("brush scrub",          "Dead Scrub & Brush",     ResourceNodeType.WoodenDebris, Skill.Woodcutting,  1,  4, 40,  25),
        new("gnarled tree",         "Irradiated Thicket",     ResourceNodeType.WoodenDebris, Skill.Woodcutting,  20, 4, 220, 50),
        new("fossilized deadwood",  "Fossilized Deadwood",    ResourceNodeType.WoodenDebris, Skill.Woodcutting,  40, 4, 221, 80),
        new("petrified tree",       "Petrified Forest",       ResourceNodeType.WoodenDebris, Skill.Woodcutting,  60, 4, 222, 120),
        new("ancient hardened flora","Ancient Hardened Flora",ResourceNodeType.WoodenDebris, Skill.Woodcutting,  80, 4, 223, 170),
    };

    [MenuItem("Wasteland/World/Create Tiered Node Prefabs")]
    public static void Create()
    {
        WastelandPaths.EnsureFolder(WastelandPaths.Root + "/Nodes");
        int made = 0, missing = 0;
        var log = new System.Text.StringBuilder();

        foreach (var d in Nodes)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SrcFolder + d.asset + ".glb");
            if (model == null) { log.AppendLine("MISSING: " + d.asset); missing++; continue; }

            var temp = (GameObject)PrefabUtility.InstantiatePrefab(model);
            temp.name = d.asset + " (T" + Tier(d.level) + ")";

            if (temp.GetComponentInChildren<Collider>() == null)
            {
                var mf = temp.GetComponentInChildren<MeshFilter>();
                if (mf != null) mf.gameObject.AddComponent<MeshCollider>();
                else temp.AddComponent<SphereCollider>();
            }

            var node = temp.AddComponent<ResourceNode>();
            node.nodeType = d.type; node.skill = d.skill;
            node.levelRequired = d.level; node.requiredToolId = d.tool; node.dropItemId = d.drop;
            node.xpPerAction = d.xp; node.maxHits = 3; node.respawnTicks = 10;
            node.displayNameOverride = d.niceName;

            string nodeName = temp.name;                       // capture BEFORE destroying
            string path = WastelandPaths.Root + "/Nodes/" + nodeName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(temp, path);        // overwrites on re-run (idempotent)
            Object.DestroyImmediate(temp);
            log.AppendLine("✓ " + nodeName + "  (" + d.skill + " lvl " + d.level + ")");
            made++;
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Create Tiered Node Prefabs",
            made + " node prefab(s) created in " + WastelandPaths.Root + "/Nodes" +
            (missing > 0 ? "  (" + missing + " missing — let assets finish importing, then re-run)" : "") +
            "\n\n" + log + "\n" +
            "Drag these into the World Scatter Brush and paint. Each tier drops its real item " +
            "(scrap ores 60-63, wood 220-223) and gates on its gathering level.", "OK");
    }

    static int Tier(int level) => level >= 80 ? 5 : level >= 60 ? 4 : level >= 40 ? 3 : level >= 20 ? 2 : 1;
}
