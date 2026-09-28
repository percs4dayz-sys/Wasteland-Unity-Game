using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Assigns real terrain textures from Handpainted Grass and Midgard Retrogard packs
/// to empty terrain layers. Picks the best texture per layer by name matching.
///
/// Menu: Wasteland > Fix Terrain Layer Textures
/// </summary>
public static class TerrainLayerFixer
{
    const string HandpaintedRoot = "Assets/Handpainted_Grass_and_Ground_Textures/Textures";
    const string RetrogardFolder = "Assets/LaFinca/Midgard Textures/Retrogard/128";

    [MenuItem("Wasteland/Fix Terrain Layer Textures")]
    public static void Fix()
    {
        // Gather all available textures
        var grassTextures = GatherTextures(HandpaintedRoot + "/Grass");
        var dirtTextures  = GatherTextures(HandpaintedRoot + "/Dirt");
        var snowTextures  = GatherTextures(HandpaintedRoot + "/Snow");
        var retroTextures = GatherTextures(RetrogardFolder);

        var layerGuids = AssetDatabase.FindAssets("t:TerrainLayer", new[] { "Assets/_TerrainAutoUpgrade" });
        int fixed_ = 0;

        foreach (var guid in layerGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null || layer.diffuseTexture != null) continue;

            Texture2D tex = PickTexture(layer.name, grassTextures, dirtTextures, snowTextures, retroTextures);
            if (tex == null) continue;

            layer.diffuseTexture = tex;
            EditorUtility.SetDirty(layer);
            fixed_++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Layers Fixed",
            $"Assigned handpainted textures to {fixed_} terrain layers.\n\n" +
            "Run 'Paint Biome Terrain' to see them on the terrain.",
            "OK");
    }

    static Texture2D PickTexture(string name, List<Texture2D> grass, List<Texture2D> dirt,
                                  List<Texture2D> snow, List<Texture2D> retro)
    {
        string lower = name.ToLowerInvariant();

        if (lower.Contains("grass") || lower.Contains("moss"))
            return PickOne(grass, "Grass_lighted") ?? PickOne(retro);
        if (lower.Contains("dirt"))
            return PickOne(dirt, "dirt_lighted") ?? PickOne(retro);
        if (lower.Contains("gravel") || lower.Contains("rock"))
            return PickOne(dirt, "dirt_desatured_rocks") ?? PickOne(retro);
        if (lower.Contains("cliff"))
            return PickOne(dirt, "dirt_desatured") ?? PickOne(retro);
        if (lower.Contains("charcoal") || lower.Contains("asphalt"))
            return PickOne(dirt, "dirt_corrupted") ?? PickOne(retro);
        if (lower.Contains("snow") || lower.Contains("ice"))
            return PickOne(snow);

        return PickOne(retro);
    }

    static Texture2D PickOne(List<Texture2D> list, string preferred = null)
    {
        if (list.Count == 0) return null;
        if (preferred != null)
        {
            foreach (var t in list)
                if (t.name.ToLowerInvariant().Contains(preferred.ToLowerInvariant()))
                    return t;
        }
        return list[0];
    }

    static List<Texture2D> GatherTextures(string folder)
    {
        var list = new List<Texture2D>();
        if (!AssetDatabase.IsValidFolder(folder) && !Directory.Exists(folder)) return list;

        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) continue;

            // Ensure tiling
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
            list.Add(tex);
        }
        return list;
    }
}
