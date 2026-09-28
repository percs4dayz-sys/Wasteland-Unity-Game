using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Reconciles all gameplay content with the real ground surface.
///
/// The world-builder places the player, enemies, resource nodes, crafting stations,
/// the bank, and scattered props at roughly y=0. When the ground is later replaced by
/// an imported GLB mesh whose surface sits well above y=0, every one of those objects
/// ends up buried under the ground. Seating only the player (FixGroundColliders) leaves
/// everything else underground. This drops EACH gameplay object straight down onto the
/// ground surface beneath it, so the whole scene is walkable no matter how hilly the GLB
/// is. Fully undoable.
///
/// Menu:  Wasteland ▸ World ▸ Seat All Content On Ground
/// </summary>
public static class SeatContentOnGround
{
    [MenuItem("Wasteland/World/Seat All Content On Ground")]
    public static void Seat()
    {
        // 1. Identify the ground: the active object with the largest horizontal footprint
        //    that ISN'T a piece of gameplay content. (In MainWorld3D this is the GLB "World".)
        var groundColliders = FindGroundColliders(out string groundName, out Bounds groundBounds);
        if (groundColliders.Count == 0)
        {
            EditorUtility.DisplayDialog("Seat Content On Ground",
                "Couldn't find a ground collider in the scene. Make sure the world mesh has a MeshCollider " +
                "(run 'Wasteland > World > Fix Ground Colliders + Player' first if needed).", "OK");
            return;
        }

        float rayTop = groundBounds.max.y + 1000f;
        float rayLen = groundBounds.size.y + 2000f;

        // 2. Gather every gameplay object to seat (de-duplicated by GameObject).
        var targets = CollectGameplayObjects();

        int seated = 0, missed = 0;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();

        foreach (var go in targets)
        {
            Vector3 p = go.transform.position;
            if (!GroundYUnder(p, groundColliders, rayTop, rayLen, out float groundY)) { missed++; continue; }

            Undo.RecordObject(go.transform, "Seat Content On Ground");

            var cc = go.GetComponent<CharacterController>();
            if (cc != null)
            {
                // Seat the capsule's bottom on the surface, with a little clearance.
                float bottomOffset = cc.center.y - cc.height * 0.5f;
                go.transform.position = new Vector3(p.x, groundY - bottomOffset + 0.1f, p.z);
            }
            else
            {
                // Seat by the visible base so any pivot lands correctly.
                float baseY = RendererBottom(go, out bool hasRenderer);
                float lift = hasRenderer ? (groundY - baseY) : (groundY - p.y);
                go.transform.position = p + Vector3.up * lift;
            }
            seated++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        string msg = $"Ground: \"{groundName}\" (top y {groundBounds.max.y:0.0}).\n" +
                     $"Seated {seated} object(s) on the surface.\n" +
                     (missed > 0 ? $"{missed} had no ground beneath them (left as-is).\n" : "") +
                     "\nPress Play and Save (Ctrl+S) once it looks right.";
        Debug.Log($"[SeatContent] {msg.Replace('\n', ' ')}");
        EditorUtility.DisplayDialog("Seat Content On Ground", msg, "OK");
    }

    // ── ground detection ────────────────────────────────────────────────────

    /// <summary>The ground is the biggest-footprint active mesh that isn't gameplay content.</summary>
    static HashSet<Collider> FindGroundColliders(out string name, out Bounds bounds)
    {
        name = "(none)"; bounds = default;
        GameObject best = null; float bestArea = 0f; Bounds bestBounds = default;

        foreach (var col in Object.FindObjectsByType<Collider>())
        {
            var go = col.gameObject;
            if (!go.activeInHierarchy) continue;
            if (IsGameplay(go)) continue;                    // never treat content as ground
            var b = col.bounds;
            float area = b.size.x * b.size.z;
            if (area > bestArea) { bestArea = area; best = go; bestBounds = b; }
        }

        var set = new HashSet<Collider>();
        if (best == null) return set;

        name = best.name;
        // Use the whole root hierarchy of the ground so a multi-mesh GLB counts fully.
        var root = best.transform.root.gameObject;
        bounds = bestBounds;
        foreach (var c in root.GetComponentsInChildren<Collider>())
        {
            set.Add(c);
            bounds.Encapsulate(c.bounds);
        }
        return set;
    }

    /// <summary>Closest upward-facing hit on the ground colliders directly below worldPos.</summary>
    static bool GroundYUnder(Vector3 worldPos, HashSet<Collider> ground, float rayTop, float rayLen, out float y)
    {
        y = 0f;
        var origin = new Vector3(worldPos.x, rayTop, worldPos.z);
        var hits = Physics.RaycastAll(new Ray(origin, Vector3.down), rayLen);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (!ground.Contains(h.collider)) continue;      // only land on the actual ground
            if (h.normal.y < 0.2f) continue;                 // skip walls / undersides
            y = h.point.y;
            return true;
        }
        return false;
    }

    // ── content selection ───────────────────────────────────────────────────

    static List<GameObject> CollectGameplayObjects()
    {
        var set = new HashSet<GameObject>();

        var pc = Object.FindAnyObjectByType<Player3DController>();
        if (pc != null) set.Add(pc.gameObject);

        foreach (var c in Object.FindObjectsByType<ResourceNode>())   set.Add(c.gameObject);
        foreach (var c in Object.FindObjectsByType<CraftingStation>()) set.Add(c.gameObject);
        foreach (var c in Object.FindObjectsByType<BankingCrate>())    set.Add(c.gameObject);
        foreach (var c in Object.FindObjectsByType<CombatTarget>())    set.Add(c.gameObject);

        var scatter = GameObject.Find("ScatteredProps");
        if (scatter != null)
            foreach (Transform child in scatter.transform) set.Add(child.gameObject);

        return new List<GameObject>(set);
    }

    /// <summary>True if the object is (part of) gameplay content we seat, not ground/decor terrain.</summary>
    static bool IsGameplay(GameObject go)
    {
        if (go.GetComponentInParent<Player3DController>()  != null) return true;
        if (go.GetComponentInParent<ResourceNode>()        != null) return true;
        if (go.GetComponentInParent<CraftingStation>()     != null) return true;
        if (go.GetComponentInParent<BankingCrate>()        != null) return true;
        if (go.GetComponentInParent<CombatTarget>()        != null) return true;
        var scatter = GameObject.Find("ScatteredProps");
        if (scatter != null && go.transform.IsChildOf(scatter.transform)) return true;
        return false;
    }

    /// <summary>Lowest point of the object's combined render bounds (its visual "feet").</summary>
    static float RendererBottom(GameObject go, out bool hasRenderer)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        hasRenderer = rs.Length > 0;
        if (!hasRenderer) return go.transform.position.y;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b.min.y;
    }
}
