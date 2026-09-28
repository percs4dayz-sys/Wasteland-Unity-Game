using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;
using System.IO;

/// <summary>
/// Turns a generated ground/world image into usable game assets.
///
/// Workflow: drop your image PNG anywhere under Assets/, select it in the Project
/// window, then use one of the menu items below.
///
///   Wasteland > Ground > Backdrop From Selected Image
///       → drops the whole image into the scene as one ground backdrop, sized to
///         fill the world and sorted behind everything. Best for a single big
///         generated map. No tiling, no seams.
///
///   Wasteland > Ground > Tile Asset From Selected Image
///       → makes a drag-and-drop Tile asset (in Assets/Art/Tiles). Assign it to
///         TilemapWorldBuilder's Ground Tile, or drag it into a Tile Palette to
///         paint with. Best for a seamless/tileable texture.
/// </summary>
public static class GroundAssetTools
{
    // Generated art usually looks better smoothed; switch to Point in the import
    // settings if you want crisp pixels instead.
    const FilterMode GROUND_FILTER = FilterMode.Bilinear;

    // ── Backdrop ─────────────────────────────────────────────────────────
    [MenuItem("Wasteland/Ground/Backdrop From Selected Image")]
    public static void BackdropFromSelection()
    {
        var path = GetSelectedTexturePath();
        if (path == null) return;

        Sprite sprite = ImportAsSprite(path, TextureWrapMode.Clamp);
        if (sprite == null) { Debug.LogError("[Ground] Could not create sprite."); return; }

        // Default map size/origin (the old TilemapWorldBuilder that used to override these is gone).
        float w = 32f, h = 24f, ox = 0f, oy = 0f;

        // Reuse an existing backdrop if present, else create one.
        var go = GameObject.Find("GroundBackdrop");
        if (go == null) go = new GameObject("GroundBackdrop");

        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = sprite;
        sr.sortingOrder = -1000;                 // behind tilemap, props, player
        sr.color        = Color.white;

        // Center on the map and stretch to cover it exactly.
        go.transform.position = new Vector3(ox + w / 2f, oy + h / 2f, 0f);
        Vector2 b = sprite.bounds.size;          // world units at scale 1
        go.transform.localScale = new Vector3(
            b.x > 0 ? w / b.x : 1f,
            b.y > 0 ? h / b.y : 1f,
            1f);

        Selection.activeGameObject = go;
        EditorUtility.SetDirty(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[Ground] Backdrop placed, covering {w}×{h} units. " +
                  "It sits behind the tilemap — disable/clear the Tilemap if you want only the image.");
    }

    // ── Tile asset ───────────────────────────────────────────────────────
    [MenuItem("Wasteland/Ground/Tile Asset From Selected Image")]
    public static void TileFromSelection()
    {
        var path = GetSelectedTexturePath();
        if (path == null) return;

        // One full image = one 1×1 tile cell, wrap = Repeat so it tiles edge-to-edge.
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        importer.GetSourceTextureWidthAndHeight(out int tw, out int th);
        int ppu = Mathf.Max(1, Mathf.Max(tw, th));   // whole image spans 1 world unit

        Sprite sprite = ImportAsSprite(path, TextureWrapMode.Repeat, ppu);
        if (sprite == null) { Debug.LogError("[Ground] Could not create sprite."); return; }

        Directory.CreateDirectory(Path.GetFullPath("Assets/Art/Tiles"));
        string tileName = Path.GetFileNameWithoutExtension(path) + "_Tile";
        string tilePath = $"Assets/Art/Tiles/{tileName}.asset";

        var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
        bool isNew = tile == null;
        if (isNew) tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.name   = tileName;
        if (isNew) AssetDatabase.CreateAsset(tile, tilePath);
        else       EditorUtility.SetDirty(tile);
        AssetDatabase.SaveAssets();

        Selection.activeObject = tile;
        EditorGUIUtility.PingObject(tile);
        Debug.Log($"[Ground] Tile asset created: {tilePath}\n" +
                  "Drag it onto TilemapWorldBuilder's Ground Tile, or into a Tile Palette to paint. " +
                  "(Only looks right if the image tiles seamlessly.)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    static string GetSelectedTexturePath()
    {
        var obj = Selection.activeObject;
        if (obj == null)
        {
            EditorUtility.DisplayDialog("No image selected",
                "Select your generated image (a .png/.jpg) in the Project window first.", "OK");
            return null;
        }
        string path = AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrEmpty(path) ||
            !(AssetImporter.GetAtPath(path) is TextureImporter))
        {
            EditorUtility.DisplayDialog("Not an image",
                "The selected asset isn't an importable texture. Select a .png or .jpg.", "OK");
            return null;
        }
        return path;
    }

    static Sprite ImportAsSprite(string path, TextureWrapMode wrap, int ppu = 100)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return null;

        imp.textureType         = TextureImporterType.Sprite;
        imp.spriteImportMode    = SpriteImportMode.Single;
        imp.filterMode          = GROUND_FILTER;
        imp.wrapMode            = wrap;
        imp.spritePixelsPerUnit = ppu;
        imp.textureCompression  = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled       = false;
        imp.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
