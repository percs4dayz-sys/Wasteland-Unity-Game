using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click: turns the FantasyEnvironments Ambient-Occlusion-Trees prefabs into configured
/// tiered Woodcutting node PREFABS (saved to _Wasteland/Nodes), reusing the exact wasteland-
/// themed names / levels / drops / XP from CreateTieredNodes' wood line — just driven by the
/// real tree models instead of the placeholder Meshy assets.
///
/// OSRS-style soft→hard species order:
///   T1 Birch (lvl1) · T2 Oak (20) · T3 Willow (40) · T4 Pine (60) · T5 Deciduous (80)
/// All on Woodcutting (Hatchet id4), dropping wood 40/220-223 (see TierGatherables).
///
/// A trunk-sized CapsuleCollider is fitted from the renderer bounds (radius clamped so the
/// canopy doesn't become an invisible wall the player bumps into). Interaction is proximity-
/// based (Interactor3D.GetComponentInParent&lt;ResourceNode&gt;), so collider-on-root + node-on-root
/// is all that's needed.
///
/// Idempotent — overwrites its prefabs on re-run. Run "Fix Tree Materials (URP)" first so the
/// trees aren't magenta.  Menu:  Wasteland ▸ World ▸ Create Tiered TREE Nodes
/// </summary>
public static class CreateTieredTreeNodes
{
    const string PrefabFolder = "Assets/FantasyEnvironments/Environments/Ambient-Occlusion-Trees/Prefabs/";

    struct Def
    {
        public string prefab, niceName; public int level, drop, xp;
        public Def(string p, string nice, int lvl, int drop, int xp)
        { prefab = p; niceName = nice; level = lvl; this.drop = drop; this.xp = xp; }
    }

    // Woodcutting line (Hatchet=4) → wood 40/220-223, names/levels/xp kept from CreateTieredNodes.
    static readonly Def[] Nodes =
    {
        new("Birch_tree1",      "Dead Scrub & Brush",     1,  40,  25),
        new("Oak_tree1",        "Irradiated Thicket",     20, 220, 50),
        new("Willow_tree1",     "Fossilized Deadwood",    40, 221, 80),
        new("Pine_tree1",       "Petrified Forest",       60, 222, 120),
        new("Deciduous_tree1",  "Ancient Hardened Flora", 80, 223, 170),
    };

    [MenuItem("Wasteland/World/Create Tiered TREE Nodes")]
    public static void Create()
    {
        WastelandPaths.EnsureFolder(WastelandPaths.Root + "/Nodes");
        int made = 0, missing = 0;
        var log = new System.Text.StringBuilder();

        foreach (var d in Nodes)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + d.prefab + ".prefab");
            if (model == null) { log.AppendLine("MISSING: " + d.prefab); missing++; continue; }

            var temp = (GameObject)PrefabUtility.InstantiatePrefab(model);
            temp.name = d.prefab + " (T" + Tier(d.level) + ")";

            FitTrunkCollider(temp);

            var node = temp.AddComponent<ResourceNode>();
            node.nodeType = ResourceNodeType.WoodenDebris; node.skill = Skill.Woodcutting;
            node.levelRequired = d.level; node.requiredToolId = 4; node.dropItemId = d.drop;
            node.xpPerAction = d.xp; node.maxHits = 3; node.respawnTicks = 10;
            node.displayNameOverride = d.niceName;

            string nodeName = temp.name;
            string path = WastelandPaths.Root + "/Nodes/" + nodeName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(temp, path);   // overwrites on re-run (idempotent)
            Object.DestroyImmediate(temp);
            log.AppendLine("✓ " + nodeName + "  (Woodcutting lvl " + d.level + " → item " + d.drop + ")");
            made++;
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Create Tiered TREE Nodes",
            made + " tree node prefab(s) created in " + WastelandPaths.Root + "/Nodes" +
            (missing > 0 ? "  (" + missing + " missing — let imports finish, then re-run)" : "") +
            "\n\n" + log + "\n" +
            "Drag these into the World Scatter Brush and paint. If they're magenta, run " +
            "'Fix Tree Materials (URP)' first.", "OK");
    }

    /// <summary>Adds a trunk-sized CapsuleCollider from renderer bounds (radius clamped so the
    /// canopy isn't a walk-blocking wall). Skips if a collider already exists.</summary>
    static void FitTrunkCollider(GameObject root)
    {
        if (root.GetComponentInChildren<Collider>() != null) return;

        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { root.AddComponent<SphereCollider>(); return; }

        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        var cap = root.AddComponent<CapsuleCollider>();
        cap.direction = 1; // Y-axis
        cap.height = Mathf.Max(b.size.y, 0.5f);
        // Trunk radius: a fraction of the horizontal footprint, clamped so a wide canopy
        // doesn't create a huge invisible collider.
        float footprint = Mathf.Min(b.size.x, b.size.z);
        cap.radius = Mathf.Clamp(footprint * 0.15f, 0.3f, 1.0f);
        // Center in the root's local space (root is at origin/identity here).
        cap.center = root.transform.InverseTransformPoint(b.center);
    }

    static int Tier(int level) => level >= 80 ? 5 : level >= 60 ? 4 : level >= 40 ? 3 : level >= 20 ? 2 : 1;
}
