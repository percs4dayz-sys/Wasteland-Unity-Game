using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds a SECOND imported .glb model (e.g. the cityscape half of a two-piece map) into the scene
/// next to the existing "Ground", with walkable mesh colliders, ready to nudge into place.
///
/// It keeps the piece's own imported textures (no biome recoloring) — so the city looks as authored.
/// Placement of two separately-generated AI meshes can't be auto-perfect, so this just drops it
/// beside the ground at a matching size and selects it; use Unity's Move/Rotate/Scale gizmos
/// (W / E / R) to slide it snug against the ground.
///
/// Menu:  Wasteland ▸ World ▸ Add Cityscape Piece
/// </summary>
public static class WorldPieceTool
{
    // GLB with WebP textures pre-converted to PNG (gltfast can't read WebP). This keeps the
    // model's correct UVs/texture mapping — unlike the OBJ conversion, which scrambled them (camo).
    static string CityscapePath => WastelandPaths.Resolve(
        WastelandPaths.Models + "/Cityscape.glb", "Assets/Art/Generated3D/Cityscape.glb");

    [MenuItem("Wasteland/Archived/World/Add Cityscape Piece", false, 9000)]
    public static void AddCityscape() => AddPiece(CityscapePath, "Cityscape");

    static void AddPiece(string assetPath, string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Add World Piece",
                "Couldn't load the model at:\n" + assetPath + "\n\n" +
                "Let Unity finish importing it (it should show a model thumbnail with a ▶ arrow), then re-run.",
                "OK");
            return;
        }

        // Remove a previous copy so re-running doesn't stack duplicates.
        var prev = GameObject.Find(name);
        if (prev != null) Object.DestroyImmediate(prev);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "Add " + name);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        Bounds pieceB = WorldBounds(go);

        // Scale the piece to roughly match the ground's footprint, then sit it beside the ground.
        var ground = GameObject.Find("Ground");
        if (ground != null && pieceB.size.x > 0.0001f)
        {
            Bounds gb = WorldBounds(ground);
            float groundW = Mathf.Max(gb.size.x, gb.size.z);
            float pieceW  = Mathf.Max(pieceB.size.x, pieceB.size.z);
            if (groundW > 0.01f && pieceW > 0.01f)
            {
                go.transform.localScale = Vector3.one * (groundW / pieceW);
                pieceB = WorldBounds(go);
            }

            // Place it just to the +X side of the ground, bases level.
            float gap = 2f;
            Vector3 target = new Vector3(gb.max.x + gap + pieceB.extents.x, 0f, gb.center.z);
            Vector3 offset = target - new Vector3(pieceB.center.x, pieceB.min.y, pieceB.center.z);
            go.transform.position += offset;
        }

        // Walkable colliders.
        int colliders = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<MeshCollider>() == null)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
            colliders++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = go;
        SceneView.FrameLastActiveSceneView();

        EditorUtility.DisplayDialog("Add World Piece",
            "Added '" + name + "' beside the Ground (" + colliders + " collider(s), original textures kept).\n\n" +
            "It's selected and framed in the Scene view. To line it up with the ground:\n" +
            "  W = move,  E = rotate,  R = scale\n" +
            "Drag it until it sits where you want, then SAVE (Ctrl+S).\n\n" +
            "Too big/small? Use the scale gizmo (R) or set the Transform Scale in the Inspector.", "OK");
    }

    static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
