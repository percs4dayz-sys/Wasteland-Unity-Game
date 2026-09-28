using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes gameplay objects physically block the player. The imported/scaled props and enemy
/// models often end up with no collider or a degenerate (collapsed-by-scale) one, so the
/// player's CharacterController walks straight through them. For each enemy, resource node,
/// crafting station, bank, and scattered prop, this ensures there's an enabled, non-trigger
/// collider that actually encloses the object's visible bounds — adding a sized BoxCollider
/// when the existing colliders don't cover it. Click-to-move / E-to-interact still work
/// (they use raycasts + overlap spheres, which hit solid colliders fine). Undoable.
///
/// Menu:  Wasteland ▸ World ▸ Make Content Solid (collision)
/// </summary>
public static class MakeContentSolid
{
    [MenuItem("Wasteland/World/Make Content Solid (collision)")]
    public static void Run()
    {
        var targets = CollectGameplayObjects();
        int added = 0, ok = 0, skipped = 0;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();

        foreach (var go in targets)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { skipped++; continue; }   // nothing visible to enclose

            Bounds vb = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) vb.Encapsulate(rends[i].bounds);

            if (HasSolidColliderCovering(go, vb)) { ok++; continue; }

            // Add a non-trigger BoxCollider sized to the visible bounds (local space).
            var bc = Undo.AddComponent<BoxCollider>(go);
            bc.isTrigger = false;
            Vector3 ls = go.transform.lossyScale;
            bc.center = go.transform.InverseTransformPoint(vb.center);
            bc.size = new Vector3(
                vb.size.x / Mathf.Max(Mathf.Abs(ls.x), 1e-4f),
                vb.size.y / Mathf.Max(Mathf.Abs(ls.y), 1e-4f),
                vb.size.z / Mathf.Max(Mathf.Abs(ls.z), 1e-4f));
            added++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        string msg = $"Checked {targets.Count} gameplay object(s):\n" +
                     $"  • {added} given a new solid BoxCollider\n" +
                     $"  • {ok} already solid\n" +
                     (skipped > 0 ? $"  • {skipped} had no renderer (skipped)\n" : "") +
                     "\nPress Play — props/enemies should now stop the player. Save (Ctrl+S) after.\n" +
                     "If you STILL walk through things, the cause is the Physics layer matrix / CharacterController, not colliders — tell Claude.";
        Debug.Log($"[MakeContentSolid] added={added} alreadySolid={ok} skipped={skipped} of {targets.Count}");
        EditorUtility.DisplayDialog("Make Content Solid", msg, "OK");
    }

    /// <summary>True if the object already has an enabled, non-trigger collider that roughly
    /// covers its visible bounds (so we don't pile a second box on a perfectly good collider).</summary>
    static bool HasSolidColliderCovering(GameObject go, Bounds visible)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            // "Covers it" = collider bounds reach at least ~60% of the visible height. Filters out
            // degenerate near-zero colliders left by extreme scaling.
            if (c.bounds.size.y >= visible.size.y * 0.6f) return true;
        }
        return false;
    }

    static List<GameObject> CollectGameplayObjects()
    {
        var set = new HashSet<GameObject>();

        foreach (var c in Object.FindObjectsByType<CombatTarget>())    set.Add(c.gameObject);
        foreach (var c in Object.FindObjectsByType<ResourceNode>())    set.Add(c.gameObject);
        foreach (var c in Object.FindObjectsByType<CraftingStation>()) set.Add(c.gameObject);
        foreach (var c in Object.FindObjectsByType<BankingCrate>())    set.Add(c.gameObject);

        var scatter = GameObject.Find("ScatteredProps");
        if (scatter != null)
            foreach (Transform child in scatter.transform) set.Add(child.gameObject);

        return new List<GameObject>(set);
    }
}
