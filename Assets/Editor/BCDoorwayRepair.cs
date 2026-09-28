using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Opens up the doorways, arches and gates in the open scene(s).
///
/// Two things walled them off:
///  1. The imported building kits (the Synty packs extracted from Unreal) carry Unreal's simple
///     collision as one "…_ConvexHulls" mesh per piece — several small hulls in one mesh. They were set
///     up as CONVEX MeshColliders, so Unity wrapped all the hulls in a single hull: every doorway,
///     arch, window and porch gap was filled solid (~14k colliders). Using them as plain (non-convex)
///     mesh colliders restores the real shapes. They're tiny hull meshes, so it costs nothing.
///  2. Door and gate leaves ship shut. Each gets a SwingDoor, which swings it open as the player
///     comes near (and back when they've gone). Tier-gate shutters are left alone — those stay
///     locked until the boss dies (BCGateBossLink).
///
/// Menu: Wasteland ▸ Broken Crescent ▸ Repair Doorways. Undoable; run Audit Doorways to check.
/// </summary>
public static class BCDoorwayRepair
{
    static readonly string[] LeafNames = { "sm_door_0", "sm_barn_door_0", "fence_gate_0" };

    [MenuItem("Wasteland/Broken Crescent/Repair Doorways")]
    public static void RepairMenu() => Debug.Log(Repair());

    [MenuItem("Wasteland/Broken Crescent/Audit Doorways")]
    public static void AuditMenu() => Debug.Log(Audit());

    public static string Repair()
    {
        Undo.SetCurrentGroupName("Repair Doorways");
        int group = Undo.GetCurrentGroup();

        // 1. Convex-wrapped hull meshes back to their real shapes.
        int hulls = 0;
        foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!mc.convex || mc.isTrigger || mc.attachedRigidbody != null) continue;
            if (mc.sharedMesh == null || !mc.sharedMesh.name.Contains("ConvexHull")) continue;
            Undo.RecordObject(mc, "Repair Doorways");
            mc.convex = false;
            hulls++;
        }

        // 2. Door and gate leaves swing open for the player.
        var shutters = Object.FindObjectsByType<BCGateBossLink>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .SelectMany(l => l.shutters ?? new Transform[0]).Where(t => t != null).ToList();
        int doors = 0, already = 0;
        foreach (var t in DoorLeaves(shutters))
        {
            if (t.GetComponent<SwingDoor>() != null) { already++; continue; }
            Undo.AddComponent<SwingDoor>(t.gameObject);
            // A static-batched mesh can't move at runtime — let the leaf (and its LOD children) swing.
            foreach (var child in t.GetComponentsInChildren<Transform>(true))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(child.gameObject);
                if ((flags & StaticEditorFlags.BatchingStatic) == 0) continue;
                Undo.RecordObject(child.gameObject, "Repair Doorways");
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
            }
            doors++;
        }

        // 3. Invisible boxes left in the openings (the export's "Collider" boxes for a shut door, or a
        //    lintel low enough to catch the player's head): any box that fills much of a leaf's opening.
        int boxes = 0;
        foreach (var leaf in Object.FindObjectsByType<SwingDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var rs = leaf.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var opening = rs[0].bounds; foreach (var r in rs) opening.Encapsulate(r.bounds);
            float vol = opening.size.x * opening.size.y * opening.size.z;
            foreach (var bc in Physics.OverlapBox(opening.center, opening.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).OfType<BoxCollider>())
            {
                if (bc.transform.IsChildOf(leaf.transform) || !bc.enabled) continue;
                var cb = bc.bounds;
                Vector3 lo = Vector3.Max(cb.min, opening.min), hi = Vector3.Min(cb.max, opening.max);
                Vector3 s = Vector3.Max(Vector3.zero, hi - lo);
                if (s.x * s.y * s.z < vol * 0.25f) continue;
                Undo.RecordObject(bc, "Repair Doorways");
                bc.enabled = false;
                boxes++;
            }
        }

        Undo.CollapseUndoOperations(group);
        for (int i = 0; i < EditorSceneManager.sceneCount; i++) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetSceneAt(i));
        return $"[Repair Doorways] {hulls} convex-wrapped hull colliders restored to their real shapes; " +
               $"{doors} door/gate leaves now swing open ({already} already did); {boxes} invisible boxes cleared out of openings. Save the scene to keep it.";
    }

    /// <summary>Top-most door/gate-leaf objects that carry a renderer, excluding tier-gate shutters
    /// and the chains hung on them.</summary>
    public static List<Transform> DoorLeaves(List<Transform> shutters = null)
    {
        shutters ??= Object.FindObjectsByType<BCGateBossLink>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .SelectMany(l => l.shutters ?? new Transform[0]).Where(t => t != null).ToList();
        var result = new List<Transform>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!IsLeaf(t.name) || t.GetComponentInChildren<Renderer>() == null) continue;
            bool nested = false;
            for (var p = t.parent; p != null; p = p.parent) if (IsLeaf(p.name)) { nested = true; break; }
            if (nested) continue;
            if (shutters.Any(s => t.IsChildOf(s) || s.IsChildOf(t))) continue;
            result.Add(t);
        }
        return result;
    }

    static bool IsLeaf(string name)
    {
        string n = name.ToLowerInvariant();
        return !n.Contains("chain") && LeafNames.Any(n.Contains);
    }

    /// <summary>Sweeps a player-sized capsule through every door, arch and gate opening (door leaves
    /// count as open) and reports the ones still blocked, with what blocks them.</summary>
    public static string Audit()
    {
        string[] keys = { "door", "arch", "gate", "entrance" };
        var leaves = new HashSet<Transform>(DoorLeaves());
        int tested = 0, blocked = 0;
        var lines = new List<string>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            string n = t.name.ToLowerInvariant();
            if (!keys.Any(n.Contains) || n.Contains("rib") || n.Contains("shutter") || n.Contains("checkpoint")) continue;
            bool nested = false;
            for (var p = t.parent; p != null; p = p.parent) if (keys.Any(p.name.ToLowerInvariant().Contains)) { nested = true; break; }
            if (nested) continue;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            Vector3 ax = t.right, az = t.forward; ax.y = 0; az.y = 0; ax.Normalize(); az.Normalize();
            float ex = Mathf.Abs(b.extents.x * ax.x) + Mathf.Abs(b.extents.z * ax.z);
            float ez = Mathf.Abs(b.extents.x * az.x) + Mathf.Abs(b.extents.z * az.z);
            Vector3 thin = ez <= ex ? az : ax;
            float half = Mathf.Min(ex, ez) + 1.2f;
            Vector3 a = b.center - thin * half, e = b.center + thin * half;
            float g = Mathf.Min(GroundY(a, b), GroundY(e, b));
            if (float.IsNaN(g)) continue;
            tested++;
            Vector3 p1 = new Vector3(a.x, g + 0.45f, a.z), p2 = p1 + Vector3.up * 1.1f;
            Vector3 dir = e - a; dir.y = 0f; float dist = dir.magnitude; dir /= dist;
            var hit = Physics.CapsuleCastAll(p1, p2, 0.3f, dir, dist, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => !(h.collider is TerrainCollider) && h.distance > 0f)
                .Where(h => !leaves.Any(l => h.collider.transform.IsChildOf(l)))   // swings open for you
                .OrderBy(h => h.distance).FirstOrDefault();
            if (hit.collider == null) continue;
            blocked++;
            if (lines.Count < 30)
                lines.Add($"  {t.name} at {b.center:F0} ← {hit.collider.GetType().Name}{(hit.collider is MeshCollider m && m.convex ? " (convex)" : "")} '{hit.collider.name}'");
        }
        return $"[Audit Doorways] {tested} openings swept, {blocked} still blocked" + (lines.Count > 0 ? ":\n" + string.Join("\n", lines) : ".");
    }

    static float GroundY(Vector3 p, Bounds b)
    {
        float best = float.NaN;
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, b.max.y + 5f, p.z), Vector3.down, b.size.y + 60f, ~0, QueryTriggerInteraction.Ignore))
            if (h.normal.y > 0.6f && h.point.y > b.min.y - 3f && (float.IsNaN(best) || h.point.y < best)) best = h.point.y;
        return best;
    }
}
