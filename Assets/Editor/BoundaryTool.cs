using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes the invisible play-area edge walls visible by dropping a "PlayBoundary" gizmo box into the
/// open scene, sized to match the existing EdgeWall/EdgeBarrier colliders. Non-destructive — it only
/// adds/removes its own "PlayBoundary" object. Re-run after rebuilding a scene.
///
/// Menu:  Wasteland ▸ Show Play Boundary  /  Hide Play Boundary
/// </summary>
public static class BoundaryTool
{
    [MenuItem("Wasteland/Show Play Boundary")]
    public static void Show()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // Combine the bounds of every edge collider so the box matches the real walled area.
        bool found = false;
        Bounds b = new Bounds();
        foreach (var col in Object.FindObjectsByType<Collider>())
        {
            string n = col.gameObject.name;
            if (n != "EdgeWall" && n != "EdgeBarrier") continue;
            if (!found) { b = col.bounds; found = true; }
            else        { b.Encapsulate(col.bounds); }
        }

        var existing = GameObject.Find("PlayBoundary");
        if (existing != null) Object.DestroyImmediate(existing);

        var go = new GameObject("PlayBoundary");
        var viz = go.AddComponent<BoundaryVisualizer>();

        if (found)
        {
            go.transform.position = b.center;
            viz.size = b.size;
        }
        else
        {
            go.transform.position = new Vector3(0f, 2f, 0f);
            viz.size = new Vector3(60f, 4f, 60f);
            EditorUtility.DisplayDialog("Play Boundary",
                "No EdgeWall/EdgeBarrier colliders found in this scene, so I dropped a default " +
                "60×60 box at the origin. Move/resize the 'PlayBoundary' object to match your area.",
                "OK");
        }

        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    [MenuItem("Wasteland/Hide Play Boundary")]
    public static void Hide()
    {
        var existing = GameObject.Find("PlayBoundary");
        if (existing == null) return;
        Object.DestroyImmediate(existing);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
