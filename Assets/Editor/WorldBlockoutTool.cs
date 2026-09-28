using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Sizes the main-world play area in the OPEN scene. Non-destructive — it only resizes the existing
/// "Ground" plane and manages its own "WorldBlockout" group:
///   • grows the ground to cover the play area,
///   • builds a perimeter wall ring (visible + colliders) — the edge barrier that stops you walking off.
/// Content (nodes/NPCs/bosses) is handled by 'Plan Main World Content' (WorldContentPlanner).
/// Keep HALF here in sync with WorldContentPlanner.EDGE so the walls and content line up.
///
/// Menu:  Wasteland ▸ Block Out Main World (ground + barriers)
/// </summary>
public static class WorldBlockoutTool
{
    // Playable half-extent → a HALF*2 square. MUST match WorldContentPlanner.EDGE. Raise to grow the map.
    const float HALF = 300f;

    [MenuItem("Wasteland/Archived/Block Out Main World (ground + barriers)", false, 9000)]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // Grow the ground to cover the play area (a Unity Plane is 10 m at scale 1).
        // Skip if the ground is a sculpted Terrain — Terrains size themselves via TerrainData.
        var ground = GameObject.Find("Ground");
        if (ground != null && ground.GetComponent<Terrain>() == null)
        {
            float s = (HALF * 2f + 40f) / 10f;
            ground.transform.localScale = new Vector3(s, 1f, s);
        }

        // Manage only our own group (safe to re-run).
        var existing = GameObject.Find("WorldBlockout");
        if (existing != null) Object.DestroyImmediate(existing);
        var root = new GameObject("WorldBlockout").transform;

        var walls = new GameObject("EdgeBarriers").transform; walls.SetParent(root);
        float h = 60f, t = 1f, span = HALF * 2f + t;   // tall so they still block on the high terrain edges
        EdgeWall(walls, new Vector3(0, h / 2f,  HALF), new Vector3(span, h, t));
        EdgeWall(walls, new Vector3(0, h / 2f, -HALF), new Vector3(span, h, t));
        EdgeWall(walls, new Vector3( HALF, h / 2f, 0), new Vector3(t, h, span));
        EdgeWall(walls, new Vector3(-HALF, h / 2f, 0), new Vector3(t, h, span));

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root.gameObject;

        EditorUtility.DisplayDialog("World Blockout",
            $"Sized \"{scene.name}\" to a {HALF * 2:0}x{HALF * 2:0} m play area:\n\n" +
            (ground != null ? "• Ground resized to cover it.\n" : "• No 'Ground' object found to resize — make sure one exists.\n") +
            "• Perimeter wall ring (visible + colliders) added as the edge barrier.\n\n" +
            "Run 'Plan Main World Content' for the tiered nodes / NPCs / bosses, then drop your art. " +
            "SAVE the scene (Ctrl+S).",
            "Got it");
    }

    static void EdgeWall(Transform parent, Vector3 pos, Vector3 size)
    {
        var w = GameObject.CreatePrimitive(PrimitiveType.Cube);   // keeps its BoxCollider → blocks
        w.name = "EdgeBarrier";
        w.transform.SetParent(parent);
        w.transform.position = pos;
        w.transform.localScale = size;
        w.GetComponent<Renderer>().sharedMaterial =
            World3DBuilder.MakeMat("WorldBarrier", new Color(0.18f, 0.18f, 0.2f));
    }
}
