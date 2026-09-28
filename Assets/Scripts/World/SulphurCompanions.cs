using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wherever there's mining (pickaxe nodes: junk piles, wrecked cars, dumpsters, drone piles, robotics),
/// a few Sulphur Vents sit alongside it. Sulphur is the utility resource every ammo recipe burns: it
/// stacks, and a vent pays ~1 Scrapping XP per crystal (see CreateSulphurVents), so players should find
/// it wherever they're already swinging a pickaxe.
///
/// Rather than relying on whoever places mining nodes to remember the vents, this adds them itself once
/// per scene load: mining nodes are grouped into clusters, and each cluster with no vent nearby gets 2
/// (3 for a big cluster) on open, gently sloped ground around its edge. Placement is seeded from the
/// cluster's position, so vents land in the same spots every session, and vents already in the scene
/// count, so hand-placed ones are never doubled up. Self-bootstrapping — no scene wiring.
/// </summary>
public class SulphurCompanions : MonoBehaviour
{
    static SulphurCompanions _instance;

    const int    PickaxeId     = 3;
    const float  ClusterLink   = 30f;    // nodes closer than this belong to the same cluster
    const float  CoveredRadius = 25f;    // a vent this far past a cluster's edge already serves it
    const float  MinSpacing    = 3f;     // between vents
    const string VentPath      = "Nodes/Sulphur Vent";   // Resources path of the vent prefab

    class Cluster { public Vector3 center; public float radius; public int count; public readonly List<Vector3> members = new(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (_instance != null) return;
        var go = new GameObject("SulphurCompanions (auto)");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<SulphurCompanions>();
    }

    void Awake()  => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this) _instance = null;
    }

    // Give the scene's own nodes a moment to wake up (and any spawners to place theirs) first.
    void Start() => Invoke(nameof(Populate), 1f);
    void OnSceneLoaded(Scene s, LoadSceneMode m) { CancelInvoke(nameof(Populate)); Invoke(nameof(Populate), 1f); }

    void Populate()
    {
        var mining = new List<Vector3>();
        var vents  = new List<Vector3>();
        foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsInactive.Exclude))
        {
            if (n.nodeType == ResourceNodeType.SulphurVent) { vents.Add(n.transform.position); continue; }
            if (n.requiredToolId != PickaxeId) continue;
            if (n.GetComponentInParent<FissionGolem>() != null) continue;   // a golem corpse isn't mining
            mining.Add(n.transform.position);
        }
        if (mining.Count == 0) return;

        var prefab = Resources.Load<GameObject>(VentPath);
        if (prefab == null) { Debug.LogWarning($"[SulphurCompanions] No vent prefab at Resources/{VentPath}."); return; }

        var clusters = BuildClusters(mining);
        Transform parent = null;
        int placed = 0, alreadyServed = 0;
        foreach (var c in clusters)
        {
            if (AnyWithin(vents, c.center, c.radius + CoveredRadius)) { alreadyServed++; continue; }

            int want = c.count >= 4 ? 3 : 2;
            var rng = new System.Random(Seed(c.center));
            for (int i = 0; i < want; i++)
            {
                if (!FindSpot(c, rng, vents, out Vector3 pos)) continue;
                if (parent == null) parent = new GameObject("Sulphur Vents (auto)").transform;   // lives in the active scene
                var yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                Instantiate(prefab, pos, yaw, parent).name = "Sulphur Vent";
                vents.Add(pos);
                placed++;
            }
        }
        Debug.Log($"[SulphurCompanions] {mining.Count} mining node(s) in {clusters.Count} cluster(s): " +
                  $"placed {placed} sulphur vent(s); {alreadyServed} cluster(s) already had one.");
    }

    // Single-linkage grouping: a node joins every cluster it's within ClusterLink of (merging them).
    static List<Cluster> BuildClusters(List<Vector3> points)
    {
        var clusters = new List<Cluster>();
        foreach (var p in points)
        {
            Cluster home = null;
            for (int i = clusters.Count - 1; i >= 0; i--)
            {
                var c = clusters[i];
                if (!AnyWithin(c.members, p, ClusterLink)) continue;
                if (home == null) { home = c; continue; }
                home.members.AddRange(c.members);   // p bridges two clusters → merge
                clusters.RemoveAt(i);
            }
            if (home == null) { home = new Cluster(); clusters.Add(home); }
            home.members.Add(p);
        }
        foreach (var c in clusters)
        {
            var sum = Vector3.zero;
            foreach (var m in c.members) sum += m;
            c.center = sum / c.members.Count;
            c.count = c.members.Count;
            foreach (var m in c.members) c.radius = Mathf.Max(c.radius, FlatDistance(m, c.center));
        }
        return clusters;
    }

    // Open, gently sloped ground just outside the cluster, not inside anything, not in a pit or on a
    // ledge, and not crowding another vent.
    static bool FindSpot(Cluster c, System.Random rng, List<Vector3> vents, out Vector3 pos)
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            float ang  = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = c.radius + 3f + (float)rng.NextDouble() * 6f;
            var probe  = c.center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;

            if (!Physics.Raycast(probe + Vector3.up * 60f, Vector3.down, out var hit, 160f, ~0, QueryTriggerInteraction.Ignore))
                continue;
            if (!(hit.collider is TerrainCollider)) continue;                 // natural ground only (not roofs, roads, props)
            if (hit.normal.y < 0.85f) continue;                               // steeper than ~32°
            if (Mathf.Abs(hit.point.y - c.center.y) > 6f) continue;           // pit or ledge
            if (Blocked(hit.point)) continue;
            if (AnyWithin(vents, hit.point, MinSpacing)) continue;

            pos = hit.point;
            return true;
        }
        pos = default;
        return false;
    }

    // Something solid (rock, wall, building, another node) where the vent would sit.
    static bool Blocked(Vector3 ground)
    {
        foreach (var col in Physics.OverlapSphere(ground + Vector3.up * 0.9f, 0.8f, ~0, QueryTriggerInteraction.Ignore))
            if (!(col is TerrainCollider)) return true;
        return false;
    }

    static bool AnyWithin(List<Vector3> points, Vector3 p, float range)
    {
        foreach (var q in points) if (FlatDistance(p, q) < range) return true;
        return false;
    }

    static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    static int Seed(Vector3 c) => unchecked(Mathf.RoundToInt(c.x) * 73856093 ^ Mathf.RoundToInt(c.z) * 19349663);
}
