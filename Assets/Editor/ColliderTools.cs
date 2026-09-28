using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Bulk-adds colliders to whatever you've selected (and all its children) so
/// imported models — city buildings, props, etc. — stop being walk-through.
/// Skips anything that already has a collider, so it's safe to re-run.
///
/// • Box colliders  — cheap, auto-fit to each mesh, no "mesh not readable"
///   issues. Best for cities / lots of buildings. RECOMMENDED.
/// • Mesh colliders — exact shape, but heavier and can error at runtime on
///   GLB meshes imported non-readable (e.g. via glTFast). Use for a few
///   precise props, not a whole city.
///
/// Menu: Tools ▸ Wasteland ▸ Add Box/Mesh Colliders To Selection
/// </summary>
public static class ColliderTools
{
    [MenuItem("Tools/Wasteland/Add Box Colliders To Selection")]
    static void AddBox() => Add(box: true);

    [MenuItem("Tools/Wasteland/Add Mesh Colliders To Selection")]
    static void AddMesh() => Add(box: false);

    [MenuItem("Tools/Wasteland/Remove Colliders From Selection")]
    static void RemoveColliders()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        {
            EditorUtility.DisplayDialog("Remove Colliders",
                "Select the object(s) first — e.g. the city root — then run this again.", "OK");
            return;
        }

        int removed = 0;
        foreach (var root in sel)
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                Undo.DestroyObjectImmediate(col);
                removed++;
            }

        if (removed > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ColliderTools] Removed {removed} collider(s) from the selection. Save the scene to keep it.");
    }

    static void Add(bool box)
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        {
            EditorUtility.DisplayDialog("Add Colliders",
                "Select the object(s) first — e.g. the city root in the Hierarchy — then run this again.", "OK");
            return;
        }

        int added = 0, skipped = 0;
        foreach (var root in sel)
        {
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var go = mr.gameObject;
                if (go.GetComponent<Collider>() != null) { skipped++; continue; }

                if (box) Undo.AddComponent<BoxCollider>(go);          // auto-fits to the mesh bounds
                else     Undo.AddComponent<MeshCollider>(go);          // non-convex = solid static geometry
                added++;
            }
        }

        if (added > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ColliderTools] Added {added} {(box ? "box" : "mesh")} collider(s); skipped {skipped} that already had one. " +
                  "Ctrl+Z undoes it. Save the scene to keep it.");
    }
}
