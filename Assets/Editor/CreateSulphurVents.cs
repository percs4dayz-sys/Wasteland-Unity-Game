using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the Sulphur Vent node prefab — the propellant source that every ammo recipe burns 5 of per
/// batch (see CraftingRecipes.SULPHUR).
///
/// Unlike the scrap/wood/fishing lines this node is NOT tiered: one vent, level 1, found across all
/// five world tiers. It pays ~1 XP per crystal, so it is a pure MATERIAL grind rather than a way to
/// train Scrapping — mining it to 99 would take ~240k swings, which is the point: you go here for
/// ammo, not for levels.
///
/// The prefab lives in a Resources folder because SulphurCompanions loads it at runtime to put a few
/// vents beside every cluster of mining nodes. Its look is the decimated crystal mound
/// (Art/newshit/Sulphur/Sulphur_LP.fbx + M_Sulphur); if that art is missing it falls back to a
/// primitive crust with crystal shards, so the node always builds.
///
/// Menu:  Wasteland ▸ World ▸ Create Sulphur Vent Prefab
/// </summary>
public static class CreateSulphurVents
{
    // One gather = one bundle. Bulk yield exists because ammo needs sulphur in the tens of
    // thousands; at 1 crystal per swing the grind dwarfs every other skill in the game.
    const int DropQuantity = 8;
    const int XpPerAction  = 8;    // = 1 XP per crystal, per the design call
    const int PickaxeId    = 3;
    const int SulphurId    = 41;

    public const string PrefabPath = WastelandPaths.Root + "/Nodes/Resources/Nodes/Sulphur Vent.prefab";   // Resources path "Nodes/Sulphur Vent"
    const string ModelPath    = "Assets/Art/newshit/Sulphur/Sulphur_LP.fbx";
    const string MaterialPath = "Assets/Art/newshit/Sulphur/M_Sulphur.mat";
    const float  Footprint    = 1.4f;   // metres across

    [MenuItem("Wasteland/World/Create Sulphur Vent Prefab")]
    public static void Create()
    {
        string summary = Build();
        EditorUtility.DisplayDialog("Create Sulphur Vent Prefab", summary + "\n\n" +
            "SulphurCompanions places a few of these beside every cluster of mining nodes at runtime, " +
            "so you don't need to scatter them by hand (hand-placed vents are respected).", "OK");
    }

    /// <summary>Builds (or rebuilds in place) the vent prefab. No dialogs, so tools can call it.</summary>
    public static string Build()
    {
        WastelandPaths.EnsureFolder(WastelandPaths.Root + "/Nodes/Resources/Nodes");

        var root = new GameObject("Sulphur Vent");

        // Clickable collider.
        var sc = root.AddComponent<SphereCollider>();
        sc.radius = 0.8f;
        sc.center = new Vector3(0f, 0.4f, 0f);

        bool usedModel = AddModel(root);
        if (!usedModel) AddPrimitiveLook(root);

        var node = root.AddComponent<ResourceNode>();
        node.nodeType        = ResourceNodeType.SulphurVent;
        node.skill           = Skill.Scrapping;
        node.levelRequired   = 1;
        node.requiredToolId  = PickaxeId;
        node.dropItemId      = SulphurId;
        node.dropQuantity    = DropQuantity;
        node.xpPerAction     = XpPerAction;
        node.ticksPerCycle   = 4;
        node.attemptsPerCycle = 1;
        node.successChance   = 256;   // never fails — it's a material tap, not a skill check
        node.minYield        = 4;
        node.maxYield        = 10;
        node.respawnTicks    = 8;
        node.displayNameOverride = "Sulphur Vent";

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        return $"Sulphur Vent prefab {(usedModel ? "(crystal mound model)" : "(primitive fallback look)")} at {PrefabPath}.\n" +
               $"Level 1 · Pickaxe · {DropQuantity} Sulphur per gather · {XpPerAction} Scrapping XP (1 per crystal).";
    }

    // The decimated crystal-mound art, sized to the footprint and sat on its base.
    static bool AddModel(GameObject root)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) return false;

        var vis = (GameObject)PrefabUtility.InstantiatePrefab(model);
        vis.name = "Visual";
        vis.transform.SetParent(root.transform, false);
        foreach (var c in vis.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        var renderers = vis.GetComponentsInChildren<Renderer>();
        if (mat != null)
            foreach (var r in renderers) r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();

        var b = Bounds(renderers);
        float s = Footprint / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z));
        vis.transform.localScale *= s;
        b = Bounds(renderers);
        vis.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        return true;
    }

    // Fallback: a squat rock base with a few crystal shards poking out of it.
    static void AddPrimitiveLook(GameObject root)
    {
        var mat = VentMaterial();

        var baseRock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        baseRock.name = "Crust";
        Object.DestroyImmediate(baseRock.GetComponent<Collider>());
        baseRock.transform.SetParent(root.transform, false);
        baseRock.transform.localScale = new Vector3(1.1f, 0.45f, 1.1f);
        baseRock.transform.localPosition = new Vector3(0f, 0.18f, 0f);
        baseRock.GetComponent<Renderer>().sharedMaterial = mat;

        for (int i = 0; i < 5; i++)
        {
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "Shard" + (i + 1);
            Object.DestroyImmediate(shard.GetComponent<Collider>());
            shard.transform.SetParent(root.transform, false);

            float ang = i * 72f + Random.Range(-14f, 14f);
            float rad = Random.Range(0.16f, 0.42f);
            shard.transform.localPosition = new Vector3(
                Mathf.Cos(ang * Mathf.Deg2Rad) * rad,
                Random.Range(0.26f, 0.46f),
                Mathf.Sin(ang * Mathf.Deg2Rad) * rad);
            shard.transform.localRotation = Quaternion.Euler(
                Random.Range(-26f, 26f), ang, Random.Range(-26f, 26f));
            shard.transform.localScale = new Vector3(0.13f, Random.Range(0.3f, 0.55f), 0.13f);
            shard.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    static Bounds Bounds(Renderer[] rs)
    {
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Acrid yellow, faintly emissive so vents read at a distance.</summary>
    static Material VentMaterial()
    {
        const string path = WastelandPaths.Root + "/Nodes/SulphurVent.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { name = "SulphurVent" };
        var yellow = new Color(0.86f, 0.78f, 0.16f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", yellow);
        if (m.HasProperty("_Color"))     m.SetColor("_Color", yellow);
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", yellow * 0.28f);

        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
