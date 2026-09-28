using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns YOUR 4 separated trees (Assets/_TodaysAssets/Trees/Tree1–4) into configured tiered
/// Woodcutting node PREFABS in _Wasteland/Nodes — same wasteland names / levels / drops / XP as
/// CreateTieredTreeNodes' wood line, just driven by your own tree models. Then paint them with the
/// World Scatter Brush to fill the world.
///
/// Tiers (Woodcutting, Hatchet id4):
///   T1 Tree1 (lvl 1)  → Wood Scrap (40)
///   T2 Tree2 (lvl 20) → Gnarled Timber (220)
///   T3 Tree3 (lvl 40) → Dense Wood Core (221)
///   T4 Tree4 (lvl 60) → Petrified Wood Shard (222)
///   T5 Tree5 (lvl 80) → Ancient Heartwood (223)   ← the ancient gnarled tree
///
/// A trunk-sized CapsuleCollider is fitted from renderer bounds so the player can interact without
/// the canopy becoming an invisible wall. Idempotent — overwrites its prefabs on re-run.
/// Menu:  Wasteland ▸ World ▸ Create Tiered Nodes from My Trees
/// </summary>
public static class CreateMyTreeNodes
{
    const string TreeFolder = "Assets/_TodaysAssets/Trees/";

    struct Def
    {
        public string file, niceName; public int level, drop, xp; public float scale;
        public Def(string f, string nice, int lvl, int drop, int xp, float scale = 1f)
        { file = f; niceName = nice; level = lvl; this.drop = drop; this.xp = xp; this.scale = scale; }
    }

    // Map each tree file to a Woodcutting tier. Swap the file names to re-assign which tree is which tier.
    // T5 reuses Tree4 at 1.6× scale as a placeholder "ancient giant" — point it at a real 5th tree when
    // you have one (just change "Tree4" and set scale back to 1f).
    static readonly Def[] Nodes =
    {
        new("Tree1", "Dead Scrub & Brush",      1,  40,  25),
        new("Tree2", "Irradiated Thicket",      20, 220, 50),
        new("Tree3", "Fossilized Deadwood",     40, 221, 80),
        new("Tree4", "Petrified Forest",        60, 222, 120),
        new("Tree5", "Ancient Gnarlwood",       80, 223, 170),        // T5 — the real ancient gnarled tree
    };

    [MenuItem("Wasteland/World/Create Tiered Nodes from My Trees")]
    public static void Create()
    {
        WastelandPaths.EnsureFolder(WastelandPaths.Root + "/Nodes");
        int made = 0, missing = 0;
        var log = new System.Text.StringBuilder();

        foreach (var d in Nodes)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(TreeFolder + d.file + ".fbx");
            if (model == null) { log.AppendLine("MISSING: " + d.file + ".fbx"); missing++; continue; }

            var temp = (GameObject)PrefabUtility.InstantiatePrefab(model);
            int tier = Tier(d.level);
            temp.name = d.niceName + " (T" + tier + ")";

            FitTrunkCollider(temp);
            if (d.scale != 1f) temp.transform.localScale *= d.scale;   // bigger = higher tier reads well

            var node = temp.AddComponent<ResourceNode>();
            node.nodeType = ResourceNodeType.WoodenDebris; node.skill = Skill.Woodcutting;
            node.levelRequired = d.level; node.requiredToolId = 4; node.dropItemId = d.drop;
            node.xpPerAction = d.xp; node.maxHits = 3; node.respawnTicks = 10;
            node.ticksPerCycle = 4;                    // Woodcutting cadence
            node.displayNameOverride = d.niceName;

            string nodeName = temp.name;
            string path = WastelandPaths.Root + "/Nodes/" + nodeName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(temp, path);   // overwrites on re-run (idempotent)
            Object.DestroyImmediate(temp);
            log.AppendLine("✓ " + nodeName + "  (Woodcutting lvl " + d.level + " → item " + d.drop + ", " + d.xp + " XP)");
            made++;
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Create Tiered Nodes from My Trees",
            made + " tree node prefab(s) created in " + WastelandPaths.Root + "/Nodes" +
            (missing > 0 ? "  (" + missing + " missing — let imports finish, then re-run)" : "") +
            "\n\n" + log + "\n" +
            "Now open Wasteland ▸ World ▸ World Scatter Brush, point 'From Folder' at " +
            WastelandPaths.Root + "/Nodes, Add these, and paint (or Auto-Fill) to plant them across the world.", "OK");
    }

    /// <summary>Trunk-sized CapsuleCollider from renderer bounds (radius clamped so a wide canopy
    /// isn't a walk-blocking wall). Skips if a collider already exists.</summary>
    static void FitTrunkCollider(GameObject root)
    {
        if (root.GetComponentInChildren<Collider>() != null) return;
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { root.AddComponent<SphereCollider>(); return; }

        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        var cap = root.AddComponent<CapsuleCollider>();
        cap.direction = 1; // Y
        cap.height = Mathf.Max(b.size.y, 0.5f);
        float footprint = Mathf.Min(b.size.x, b.size.z);
        cap.radius = Mathf.Clamp(footprint * 0.15f, 0.3f, 1.0f);
        cap.center = root.transform.InverseTransformPoint(b.center);
    }

    static int Tier(int level) => level >= 80 ? 5 : level >= 60 ? 4 : level >= 40 ? 3 : level >= 20 ? 2 : 1;
}
