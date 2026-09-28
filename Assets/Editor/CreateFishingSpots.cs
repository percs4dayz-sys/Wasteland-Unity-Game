using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds 5 tiered fishing-spot node prefabs (saved to _Wasteland/Nodes). Each is a shimmering
/// cyan ripple disc + a clickable WaterBarrel ResourceNode (Fishing, Fishing Net id5).
/// Fishing spots have no fish model — per the design doc they're a shimmer in the water.
///
/// HAND-PLACE these into your water ponds (drag in, set Y to the waterline) — don't scatter them,
/// since the brush drops onto terrain (which sits below the pond surface).
///
/// Menu:  Wasteland ▸ World ▸ Create Fishing Spot Prefabs
/// </summary>
public static class CreateFishingSpots
{
    // level, xp, raw-catch drop id, in-game spot name (see TierGatherables for the fish items).
    static readonly (int level, int xp, int drop, string name)[] Tiers =
    {
        (1,  25,  22, "Stagnant Puddle"),
        (20, 50,  200, "Toxic Runoff Stream"),
        (40, 80,  201, "Irradiated River"),
        (60, 120, 202, "Contaminated Lake"),
        (80, 170, 203, "Flooded Ruins"),
    };

    [MenuItem("Wasteland/World/Create Fishing Spot Prefabs")]
    public static void Create()
    {
        WastelandPaths.EnsureFolder(WastelandPaths.Root + "/Nodes");
        var mat = RippleMaterial();

        int made = 0;
        foreach (var (level, xp, drop, niceName) in Tiers)
        {
            int tier = level >= 80 ? 5 : level >= 60 ? 4 : level >= 40 ? 3 : level >= 20 ? 2 : 1;
            string nodeName = "Fishing Spot (T" + tier + ")";

            var root = new GameObject(nodeName);

            // Clickable trigger so the player can select the spot.
            var sc = root.AddComponent<SphereCollider>();
            sc.radius = 0.7f; sc.center = new Vector3(0f, 0.1f, 0f);

            // Shimmering ripple disc (a flattened cylinder), no collider of its own.
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Ripple";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            disc.transform.localScale = new Vector3(0.9f, 0.02f, 0.9f);
            disc.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            disc.GetComponent<Renderer>().sharedMaterial = mat;
            disc.AddComponent<FishingShimmer>();

            var node = root.AddComponent<ResourceNode>();
            node.nodeType = ResourceNodeType.WaterBarrel; node.skill = Skill.Fishing;
            node.levelRequired = level; node.requiredToolId = 5; node.dropItemId = drop;   // Fishing Net → tier catch
            node.xpPerAction = xp; node.maxHits = 3; node.respawnTicks = 8;
            node.displayNameOverride = niceName;

            string path = WastelandPaths.Root + "/Nodes/" + nodeName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            made++;
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Create Fishing Spot Prefabs",
            made + " fishing-spot prefab(s) created in " + WastelandPaths.Root + "/Nodes.\n\n" +
            "Make ponds with 'Wasteland ▸ Add Water Plane', then DRAG a fishing spot into each pond " +
            "and set its Y to the waterline. Each tier now lands its real catch (Raw Shrimp, then " +
            "Mutated Carp / Glowfin Bass / Deepwater Lurker / Abyssal Specimen).", "OK");
    }

    static Material RippleMaterial()
    {
        string path = WastelandPaths.Root + "/Materials/FishingRipple.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { name = "FishingRipple" };
        var cyan = new Color(0.45f, 0.85f, 0.95f, 0.55f);
        mat.color = cyan;
        mat.SetColor("_BaseColor", cyan);
        mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_Smoothness", 0.95f); mat.SetFloat("_ZWrite", 0f);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.3f, 0.7f, 0.85f) * 1.5f);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        mat.renderQueue = (int)RenderQueue.Transparent;

        WastelandPaths.EnsureFolder(WastelandPaths.Root + "/Materials");
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
