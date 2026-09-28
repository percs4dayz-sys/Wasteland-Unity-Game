using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drops an imported .glb model into the OPEN scene and makes it the walkable world GROUND.
///
/// Why this exists: Unity has NO built-in .glb importer, so dropping a Meshy/AI .glb into Assets
/// does nothing on its own. After the com.unity.cloud.gltfast package is installed, the .glb
/// imports as a normal model prefab — and THIS tool wires it in as the ground in one click:
///   • instantiates the model as "Ground"
///   • scales it to fit the existing world footprint (≈600 m) so it isn't tiny/giant
///   • drops it so its lowest point sits at y = 0
///   • adds a MeshCollider to every piece so the player's CharacterController + click-to-move work
///   • hides (keeps, doesn't delete) the old sculpted Terrain "Ground"
///   • snaps the player + WorldContent markers + enemies onto the new surface
///
/// Menu:  Wasteland ▸ Terrain ▸ Use GLB Model as Ground
/// Re-run any time. Tweak TARGET_SIZE below if you want it bigger/smaller.
/// </summary>
public static class GlbGroundTool
{
    // The GLB that was copied into the project. Change this if you import a different world model.
    static string GlbAssetPath => WastelandPaths.Resolve(
        WastelandPaths.Models + "/WorldGround.glb", "Assets/Art/Generated3D/WorldGround.glb");

    // Roughly how many metres wide the world should be (matches the existing 600 m terrain).
    // Set to 0 to keep the model's native size and skip auto-scaling.
    const float TARGET_SIZE = 600f;

    [MenuItem("Wasteland/Archived/Terrain/Use GLB Model as Ground", false, 9000)]
    public static void Build()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GlbAssetPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("GLB Ground",
                "Couldn't load the model at:\n" + GlbAssetPath + "\n\n" +
                "Make sure the gltfast package finished installing (Window ▸ Package Manager) and that " +
                "the .glb shows up in the Project window with a little model/triangle icon. If it still " +
                "shows as a plain file, the importer isn't active yet — let Unity finish compiling, then re-run.",
                "OK");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();

        // Handle whatever is currently called "Ground":
        //  • a sculpted Terrain / flat plane  → hide it (rename + disable), don't delete, so nothing is lost
        //  • a GLB ground from a previous run → destroy it so re-running doesn't pile up copies
        var current = GameObject.Find("Ground");
        if (current != null)
        {
            bool isTerrainOrPlane = current.GetComponent<Terrain>() != null
                                 || current.GetComponent<MeshFilter>() != null;   // primitive plane
            if (isTerrainOrPlane && PrefabUtility.GetCorrespondingObjectFromSource(current) == null)
            {
                current.name = "Ground (old terrain - hidden)";
                current.SetActive(false);
            }
            else
            {
                Object.DestroyImmediate(current);   // a prior GLB ground instance
            }
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "Ground";
        Undo.RegisterCreatedObjectUndo(go, "Create GLB Ground");
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        // --- Fit to the world footprint ---
        Bounds b = WorldBounds(go);
        if (TARGET_SIZE > 0f && b.size.x > 0.0001f && b.size.z > 0.0001f)
        {
            float widest = Mathf.Max(b.size.x, b.size.z);
            float scale = TARGET_SIZE / widest;
            go.transform.localScale = Vector3.one * scale;
            b = WorldBounds(go);   // recompute after scaling
        }

        // Centre it on the world origin (X/Z) and sit its lowest point on y = 0.
        Vector3 offset = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        go.transform.position += offset;

        // --- Make it walkable: a MeshCollider on every mesh piece ---
        int colliders = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mc = mf.GetComponent<MeshCollider>();
            if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            colliders++;
        }

        SnapContent(go);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = go;

        EditorUtility.DisplayDialog("GLB Ground",
            "Set '" + prefab.name + "' as the world Ground.\n\n" +
            "• Fit to ~" + TARGET_SIZE + " m and dropped onto y = 0\n" +
            "• Added " + colliders + " mesh collider(s) so you can walk/click on it\n" +
            "• Old terrain was hidden (not deleted)\n" +
            "• Player + markers + enemies snapped onto the surface\n\n" +
            "Press Play to walk on it. If it's too big/small or floating, tell Claude or tweak " +
            "TARGET_SIZE in GlbGroundTool.cs and re-run. SAVE the scene (Ctrl+S) when happy.",
            "OK");
    }

    /// <summary>Combined world-space bounds of every renderer under the object.</summary>
    static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static void SnapContent(GameObject ground)
    {
        var pc = Object.FindAnyObjectByType<Player3DController>();
        if (pc != null) SnapDown(pc.transform, 0.4f);

        var content = GameObject.Find("WorldContent");
        if (content != null)
            foreach (Transform tierGroup in content.transform)
                foreach (Transform marker in tierGroup)
                    SnapDown(marker, 0.6f);

        foreach (var ct in Object.FindObjectsByType<CombatTarget>())
            SnapDown(ct.transform, 1f);
    }

    // Raycast straight down from high above onto the new ground and lift slightly.
    static void SnapDown(Transform t, float lift)
    {
        Vector3 from = new Vector3(t.position.x, 5000f, t.position.z);
        if (Physics.Raycast(from, Vector3.down, out var hit, 10000f))
            t.position = new Vector3(t.position.x, hit.point.y + lift, t.position.z);
    }
}
