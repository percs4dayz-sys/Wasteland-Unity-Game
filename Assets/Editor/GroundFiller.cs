using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor.SceneManagement;

/// <summary>
/// One-click ground. Fills (or re-fills) a Tilemap in the open scene with a floor tile —
/// no hand-painting. It will create a Grid + Tilemap if the scene doesn't have one.
///
/// Tile used (in priority order):
///   1. A Tile asset you've SELECTED in the Project window, else
///   2. the tile already on a TilemapWorldBuilder in the scene, else
///   3. the first floor/ground tile it can find in the project.
///
/// Menu:  Wasteland ▸ Fill Ground (Open Scene)
/// </summary>
public static class GroundFiller
{
    const int SIZE = 64;          // 64×64 tiles, matching Tutorial Island
    const int ORIGIN = 0;

    [MenuItem("Wasteland/Fill Ground (Open Scene)")]
    public static void FillGround()
    {
        TileBase tile = PickTile();
        if (tile == null)
        {
            EditorUtility.DisplayDialog("Fill Ground",
                "Couldn't find a tile to use.\n\nEither select a Tile asset in the Project window first " +
                "(e.g. one of your floor tiles), or import one — then run this again.",
                "OK");
            return;
        }

        var tilemap = Object.FindAnyObjectByType<Tilemap>();
        if (tilemap == null) tilemap = CreateGridAndTilemap();

        tilemap.ClearAllTiles();
        for (int x = 0; x < SIZE; x++)
            for (int y = 0; y < SIZE; y++)
                tilemap.SetTile(new Vector3Int(ORIGIN + x, ORIGIN + y, 0), tile);

        EditorUtility.SetDirty(tilemap);
        EditorSceneManager.MarkSceneDirty(tilemap.gameObject.scene);

        EditorUtility.DisplayDialog("Fill Ground",
            $"Done! Painted a {SIZE}×{SIZE} ground using '{tile.name}'.\n\n" +
            "Save the scene (Ctrl+S) to keep it. To use a different tile, select it in the " +
            "Project window and run this again.",
            "OK");
    }

    static TileBase PickTile()
    {
        // 1. A tile you've selected in the Project window.
        if (Selection.activeObject is TileBase sel) return sel;

        // 2. Auto-find: prefer something that looks like floor/ground/dirt/grass.
        // (The old TilemapWorldBuilder scene component that used to supply a tile here is gone.)
        var guids = AssetDatabase.FindAssets("t:TileBase");
        if (guids.Length == 0) guids = AssetDatabase.FindAssets("t:Tile");
        var tiles = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<TileBase>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(t => t != null)
            .ToList();
        if (tiles.Count == 0) return null;

        string[] prefer = { "floor", "ground", "dirt", "grass", "concrete", "asphalt", "sand", "stone" };
        foreach (var key in prefer)
        {
            var match = tiles.FirstOrDefault(t => t.name.ToLower().Contains(key));
            if (match != null) return match;
        }
        return tiles[0];
    }

    static Tilemap CreateGridAndTilemap()
    {
        var gridGo = new GameObject("Grid", typeof(Grid));
        var tmGo = new GameObject("Ground", typeof(Tilemap), typeof(TilemapRenderer));
        tmGo.transform.SetParent(gridGo.transform, false);

        var renderer = tmGo.GetComponent<TilemapRenderer>();
        renderer.sortingOrder = -100;   // ground sits behind the player & objects

        Undo.RegisterCreatedObjectUndo(gridGo, "Create Ground Grid");
        return tmGo.GetComponent<Tilemap>();
    }
}
