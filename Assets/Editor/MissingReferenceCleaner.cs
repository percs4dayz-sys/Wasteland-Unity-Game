using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Finds and clears the two kinds of broken reference a scene accumulates when assets are
/// removed from under it:
///
///   • Missing prefab instances — the prefab asset is gone, so the instance renders as an
///     empty red entry in the Hierarchy. Left over here from removing the HDRP variants of
///     the Leartes packs (SM_Fence, SM_Container, SM_Ground_Debris, …).
///   • Missing script components — a MonoBehaviour whose script no longer exists.
///
/// Always REPORT first. The report is read-only and prints the full hierarchy path plus
/// world position of everything it would touch, so you can copy the positions down before
/// deleting if you want to re-place equivalents later.
///
/// Both delete actions are undoable (Ctrl+Z) and never save the scene for you.
///
/// Menu:  Wasteland ▸ Cleanup ▸ …
/// </summary>
public static class MissingReferenceCleaner
{
    // ── report ───────────────────────────────────────────────────────────────

    [MenuItem("Wasteland/Cleanup/1 · Report Broken References (read-only)", priority = 0)]
    public static void Report()
    {
        var scene = SceneManager.GetActiveScene();
        var missingPrefabs = FindMissingPrefabInstances(scene);
        var missingScripts = FindMissingScriptHosts(scene);

        var sb = new StringBuilder($"=== Broken References in '{scene.name}' ===\n");

        sb.AppendLine($"\nMissing prefab instances: {missingPrefabs.Count}");
        foreach (var go in missingPrefabs)
            sb.AppendLine($"    {Path(go.transform),-60} at {go.transform.position}");

        sb.AppendLine($"\nGameObjects with missing script components: {missingScripts.Count}");
        foreach (var go in missingScripts)
            sb.AppendLine($"    {Path(go.transform),-60} " +
                          $"({GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go)} missing)");

        if (missingPrefabs.Count == 0 && missingScripts.Count == 0)
            sb.AppendLine("\n✅  Nothing broken — scene is clean.");
        else
            sb.AppendLine("\nRun step 2 / 3 to remove these. Both are undoable.");

        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("Broken References",
            $"{missingPrefabs.Count} missing prefab instance(s)\n" +
            $"{missingScripts.Count} object(s) with missing scripts\n\n" +
            "Full paths and positions are in the Console.", "OK");
    }

    // ── delete missing prefab instances ──────────────────────────────────────

    [MenuItem("Wasteland/Cleanup/2 · Delete Missing Prefab Instances", priority = 1)]
    public static void DeleteMissingPrefabs()
    {
        var scene = SceneManager.GetActiveScene();
        var found = FindMissingPrefabInstances(scene);

        if (found.Count == 0)
        {
            EditorUtility.DisplayDialog("Nothing To Do", "No missing prefab instances in this scene.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Delete Missing Prefab Instances",
            $"Delete {found.Count} instance(s) whose prefab asset no longer exists?\n\n" +
            "Their paths and positions were printed by step 1 — grab them first if you want " +
            "to re-place equivalents.\n\n" +
            "Undoable with Ctrl+Z. The scene is NOT saved automatically.",
            "Delete", "Cancel")) return;

        var sb = new StringBuilder("=== Deleted missing prefab instances ===\n");
        foreach (var go in found)
        {
            sb.AppendLine($"    {Path(go.transform)}  at {go.transform.position}");
            Undo.DestroyObjectImmediate(go);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine($"\n✅  Removed {found.Count}. Save the scene (Ctrl+S) to keep it.");
        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("Done",
            $"Removed {found.Count} instance(s).\n\nCtrl+Z undoes it. Ctrl+S keeps it.", "OK");
    }

    // ── strip missing script components ──────────────────────────────────────

    [MenuItem("Wasteland/Cleanup/3 · Remove Missing Script Components", priority = 2)]
    public static void RemoveMissingScripts()
    {
        var scene = SceneManager.GetActiveScene();
        var hosts = FindMissingScriptHosts(scene);

        if (hosts.Count == 0)
        {
            EditorUtility.DisplayDialog("Nothing To Do", "No missing script components in this scene.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Remove Missing Scripts",
            $"Strip missing MonoBehaviour components from {hosts.Count} object(s)?\n\n" +
            "The GameObjects themselves are kept — only the dead components go.\n\n" +
            "Undoable with Ctrl+Z.", "Remove", "Cancel")) return;

        int removed = 0;
        foreach (var go in hosts)
        {
            Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Cleanup] Removed {removed} missing script component(s) from {hosts.Count} object(s).");
        EditorUtility.DisplayDialog("Done", $"Removed {removed} dead component(s).\n\nCtrl+S to keep it.", "OK");
    }

    // ── scanning ─────────────────────────────────────────────────────────────

    static List<GameObject> FindMissingPrefabInstances(Scene scene)
    {
        var found = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            WalkPrefabs(root.transform, found);
        return found;
    }

    static void WalkPrefabs(Transform t, List<GameObject> found)
    {
        // A missing instance has no valid children to inspect, so record it and stop.
        if (PrefabUtility.IsPrefabAssetMissing(t.gameObject))
        {
            found.Add(t.gameObject);
            return;
        }
        for (int i = 0; i < t.childCount; i++)
            WalkPrefabs(t.GetChild(i), found);
    }

    static List<GameObject> FindMissingScriptHosts(Scene scene)
    {
        var found = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            WalkScripts(root.transform, found);
        return found;
    }

    static void WalkScripts(Transform t, List<GameObject> found)
    {
        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
            found.Add(t.gameObject);
        for (int i = 0; i < t.childCount; i++)
            WalkScripts(t.GetChild(i), found);
    }

    /// <summary>Full hierarchy path, e.g. "ScatteredProps/SM_Fence".</summary>
    static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent)
            sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
