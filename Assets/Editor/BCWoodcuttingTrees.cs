using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Starter woodcutting that looks like woodcutting: Roxy hands you a hatchet and you chop down a dead
/// tree. Before, the level-1 wood node was a 40 cm pile of "Dead Scrub" that read as loose wood scraps
/// lying on the ground (and it paid out tier-2 Gnarled Timber by mistake).
///
///  • Builds the "Dead Tree (T1)" node prefab: a dead beech from the Hivemind Haunted Village pack,
///    ~6.5 m tall, trunk collider, Woodcutting level 1, needs a Hatchet, gives Wood Scrap (25 XP).
///    Chopped out, it leaves a stump (ResourceNode.depletedVisual) until it grows back.
///  • Swaps every level-1 scrub node in the open scene for a tree on the same spot, and adds a couple
///    more beside each so it reads as a small dead grove.
///
/// Menu: Wasteland ▸ Broken Crescent ▸ Swap Scrub For Dead Trees (undoable, idempotent).
/// </summary>
public static class BCWoodcuttingTrees
{
    public const string PrefabPath = WastelandPaths.Root + "/Nodes/Resources/Nodes/Dead Tree (T1).prefab";
    const string Foliage = "Assets/Hivemind/HauntedVillage/URP/Art/Prefabs/Foliage/";
    // Dead_M_02 is a spindly black sapling — hard to see and hard to click. M_01 reads as "a tree".
    static readonly string[] TreeModels = { "SM_EuropeanBeech_Dead_M_01" };
    const string StumpModel = "SM_EuropeanBeech_Stump_01";
    const float TreeHeight = 6.5f;
    const int HatchetId = 4, WoodScrapId = 40;

    [MenuItem("Wasteland/Broken Crescent/Swap Scrub For Dead Trees")]
    public static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        var prefabs = TreeModels.Select((m, i) => BuildPrefab(i)).Where(p => p != null).ToArray();
        if (prefabs.Length == 0) return "[Dead Trees] tree models missing under " + Foliage;

        Undo.SetCurrentGroupName("Swap Scrub For Dead Trees");
        int group = Undo.GetCurrentGroup();
        var scrub = Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(n => n.skill == Skill.Woodcutting && n.levelRequired <= 1 && PrefabUtility.GetCorrespondingObjectFromSource(n.gameObject) == null
                        && (n.displayNameOverride ?? "").ToLowerInvariant().Contains("scrub"))
            .ToList();
        var placed = new List<Vector3>();
        int swapped = 0, extra = 0;
        foreach (var node in scrub)
        {
            Vector3 at = node.transform.position;
            Transform parent = node.transform.parent;
            Undo.DestroyObjectImmediate(node.gameObject);
            if (Place(prefabs[swapped % prefabs.Length], at, parent, placed) != null) swapped++;

            // A couple more around it, on open ground, so it reads as a grove.
            for (int k = 0; k < 2; k++)
            {
                float ang = (k * 150f + swapped * 37f) * Mathf.Deg2Rad;
                var spot = at + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 6.5f;
                if (Place(prefabs[(swapped + k + 1) % prefabs.Length], spot, parent, placed) != null) extra++;
            }
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        return $"[Dead Trees] {swapped} scrub piles swapped for dead trees, {extra} more trees planted beside them. Save the scene to keep it.";
    }

    static GameObject Place(GameObject prefab, Vector3 near, Transform parent, List<Vector3> placed)
    {
        if (!Ground(near, out var ground)) return null;
        if (placed.Any(p => (p - ground).sqrMagnitude < 16f)) return null;   // 4 m apart
        // Don't plant into a building, wall or prop.
        var hits = Physics.OverlapCapsule(ground + Vector3.up * 0.6f, ground + Vector3.up * 2.5f, 0.7f, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Any(h => !(h is TerrainCollider))) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent != null ? parent.gameObject.scene : EditorSceneManager.GetActiveScene());
        if (parent != null) go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(ground, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        go.name = "Dead Tree (Woodcutting)";
        Undo.RegisterCreatedObjectUndo(go, "Swap Scrub For Dead Trees");
        placed.Add(ground);
        return go;
    }

    static bool Ground(Vector3 p, out Vector3 ground)
    {
        ground = p;
        var hits = Physics.RaycastAll(new Vector3(p.x, p.y + 40f, p.z), Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore);
        var terrain = hits.Where(h => h.collider is TerrainCollider).OrderBy(h => h.distance).ToArray();
        if (terrain.Length == 0) return false;
        if (terrain[0].normal.y < 0.8f) return false;   // too steep for a tree you walk up to
        ground = terrain[0].point;
        return true;
    }

    /// <summary>Builds (or rebuilds in place) the tree node prefab; variant picks the tree model.</summary>
    public static GameObject BuildPrefab(int variant)
    {
        string model = TreeModels[Mathf.Clamp(variant, 0, TreeModels.Length - 1)];
        var treeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Foliage + model + ".prefab");
        var stumpAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Foliage + StumpModel + ".prefab");
        if (treeAsset == null) return null;
        string path = variant == 0 ? PrefabPath : PrefabPath.Replace("(T1)", "(T1) B");
        WastelandPaths.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));

        var root = new GameObject("Dead Tree (T1)");
        try
        {
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(treeAsset);
            tree.name = "Visual";
            tree.transform.SetParent(root.transform, false);
            StripPhysics(tree);
            var b = Bounds(tree);
            float scale = TreeHeight / Mathf.Max(0.1f, b.size.y);
            tree.transform.localScale *= scale;
            b = Bounds(tree);
            // The trunk is where the tree meets the ground: centre on the base, not the canopy.
            Vector3 trunk = TrunkBase(tree, b);
            tree.transform.localPosition -= new Vector3(trunk.x, b.min.y, trunk.z);

            if (stumpAsset != null)
            {
                var stump = (GameObject)PrefabUtility.InstantiatePrefab(stumpAsset);
                stump.name = "Stump";
                stump.transform.SetParent(root.transform, false);
                StripPhysics(stump);
                stump.transform.localScale *= scale;   // same pack, same proportions as the trunk
                var sb = Bounds(stump);
                stump.transform.localPosition -= new Vector3(sb.center.x, sb.min.y, sb.center.z);
                stump.SetActive(false);
            }

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.55f; col.height = 3.2f; col.center = new Vector3(0f, 1.6f, 0f);

            var node = root.AddComponent<ResourceNode>();
            node.nodeType = ResourceNodeType.WoodenDebris;
            node.skill = Skill.Woodcutting;
            node.levelRequired = 1;
            node.requiredToolId = HatchetId;
            node.dropItemId = WoodScrapId;
            node.dropQuantity = 1;
            node.xpPerAction = 25;
            node.ticksPerCycle = 4;
            node.attemptsPerCycle = 1;
            node.successChance = 256;
            node.minYield = 2;
            node.maxYield = 5;
            node.respawnTicks = 15;
            node.displayNameOverride = "Dead Tree";
            node.examineOverride = "A dead, sun-bleached tree. Dry enough to split into wood scraps.";
            node.depletedVisual = root.transform.Find("Stump")?.gameObject;

            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // The lowest slice of the mesh is the trunk; its centre is where the tree stands.
    static Vector3 TrunkBase(GameObject tree, Bounds b)
    {
        var mf = tree.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null && f.sharedMesh.isReadable).ToArray();
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var f in mf)
        {
            var m = f.transform.localToWorldMatrix;
            foreach (var v in f.sharedMesh.vertices)
            {
                var w = m.MultiplyPoint3x4(v);
                if (w.y < b.min.y + b.size.y * 0.05f) { sum += w; n++; }
            }
        }
        return n > 0 ? sum / n : b.center;
    }

    // The node's own trunk capsule is the only collider; the model's would block the stump swap.
    static void StripPhysics(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
