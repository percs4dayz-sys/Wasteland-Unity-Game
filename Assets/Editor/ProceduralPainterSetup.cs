using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using sc.terrain.proceduralpainter;

/// <summary>
/// One-click setup: auto-configures the Procedural Terrain Painter with
/// height/slope-based rules that respond to the actual terrain geometry.
///
/// Menu:  Wasteland > Setup Procedural Terrain Painting
/// </summary>
public static class ProceduralPainterSetup
{
    [MenuItem("Wasteland/Archived/Setup Procedural Terrain Painting", false, 9000)]
    public static void Setup()
    {
        // 1. Find terrain
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            var ground = GameObject.Find("Ground");
            if (ground != null) terrain = ground.GetComponent<Terrain>();
        }
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("No Terrain", "Select or create a terrain first.", "OK");
            return;
        }

        float maxHeight = terrain.terrainData.size.y;

        // 2. Get or add TerrainPainter
        var painter = terrain.GetComponent<TerrainPainter>();
        if (painter == null)
            painter = terrain.gameObject.AddComponent<TerrainPainter>();

        painter.SetTargetTerrains(new[] { terrain });
        painter.splatmapResolution = 512;
        painter.colorMapResolution = 512;

        // 3. Load terrain layers
        var layers = LoadTerrainLayers();
        if (layers.Count == 0)
        {
            EditorUtility.DisplayDialog("No Layers",
                "No TerrainLayer assets found in _TerrainAutoUpgrade.\nImport terrain textures first.", "OK");
            return;
        }
        terrain.terrainData.terrainLayers = layers.ToArray();

        // 4. Build layer rules (bottom-up: base layer first, top layer last)
        painter.layerSettings.Clear();

        // --- Find layers by fuzzy name match ---
        var dirt    = FindLayer(layers, "dirt", "sand", "ground", "mud");
        var grass   = FindLayer(layers, "grass", "moss", "green");
        var rock    = FindLayer(layers, "rock", "stone", "cliff", "rubble");
        var snow    = FindLayer(layers, "snow", "ice", "peak");
        var concrete = FindLayer(layers, "concrete", "road", "asphalt", "metal");

        // --- LAYER 1: Dirt/Ground — base, covers everything, no modifiers ---
        AddLayer(painter, dirt, "Dirt (base)");

        // --- LAYER 2: Grass — low & mid elevations, gentle slopes ---
        if (grass != null)
        {
            var ls = AddLayer(painter, grass, "Grass / Moss");
            // Wide height band with HUGE falloff so grass fades out gradually
            ls.modifierStack.Add(new Height { min = 0, minFalloff = 3,
                max = maxHeight * 0.9f, maxFalloff = maxHeight * 0.35f });
            ls.modifierStack.Add(new Noise  { noiseScale = 50, levels = new Vector2(0.5f, 1f) });
        }

        // --- LAYER 3: Rock — steep areas everywhere (any height) ---
        if (rock != null)
        {
            var ls = AddLayer(painter, rock, "Rock / Cliff");
            // Slope-driven, not height. Steep = rock regardless of elevation.
            ls.modifierStack.Add(new Slope  { minMax = new Vector2(25, 90), minFalloff = 15, maxFalloff = 10 });
            ls.modifierStack.Add(new Noise  { noiseScale = 35, levels = new Vector2(0.4f, 1f) });
        }

        // --- LAYER 4: Snow/Peak — highest elevations only ---
        if (snow != null)
        {
            var ls = AddLayer(painter, snow, "Snow / Peak");
            // Height-driven: only kicks in near the top with big falloff
            ls.modifierStack.Add(new Height { min = maxHeight * 0.5f, minFalloff = maxHeight * 0.3f,
                max = maxHeight, maxFalloff = 3 });
            ls.modifierStack.Add(new Noise  { noiseScale = 45, levels = new Vector2(0.4f, 1f) });
        }

        // --- LAYER 5: Concrete/Ruins — flatter mid-elevation (optional) ---
        if (concrete != null)
        {
            var ls = AddLayer(painter, concrete, "Concrete / Ruins");
            ls.modifierStack.Add(new Height { min = maxHeight * 0.1f, minFalloff = maxHeight * 0.2f,
                max = maxHeight * 0.6f, maxFalloff = maxHeight * 0.3f });
            ls.modifierStack.Add(new Slope  { minMax = new Vector2(0, 20), minFalloff = 5, maxFalloff = 15 });
            ls.modifierStack.Add(new Noise  { noiseScale = 20, levels = new Vector2(0.25f, 0.6f) });
        }

        EditorUtility.SetDirty(painter);
        EditorUtility.SetDirty(terrain.terrainData);

        // 5. Repaint
        EditorUtility.DisplayProgressBar("Terrain Painter", "Painting terrain textures...", 0.5f);
        painter.RepaintAll();
        EditorUtility.ClearProgressBar();

        Debug.Log($"[PTP Setup] Configured {painter.layerSettings.Count} layers with height/slope rules. Max terrain height: {maxHeight}m.");
    }

    static LayerSettings AddLayer(TerrainPainter painter, TerrainLayer layer, string label, bool addModifiers = true)
    {
        var ls = new LayerSettings { enabled = true, layer = layer };
        painter.layerSettings.Add(ls);
        return ls;
    }

    static List<TerrainLayer> LoadTerrainLayers()
    {
        var result = new List<TerrainLayer>();
        string[] folders = { "Assets/_TerrainAutoUpgrade", "Assets/TerrainLayers", "Assets/Art" };
        foreach (var folder in folders)
        {
            var guids = AssetDatabase.FindAssets("t:TerrainLayer", new[] { folder });
            foreach (var g in guids)
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(g));
                if (layer != null && !result.Contains(layer))
                    result.Add(layer);
            }
        }
        return result;
    }

    static TerrainLayer FindLayer(List<TerrainLayer> layers, params string[] keywords)
    {
        foreach (var kw in keywords)
        {
            var match = layers.FirstOrDefault(l =>
                l != null && l.name.ToLowerInvariant().Contains(kw.ToLowerInvariant()));
            if (match != null) return match;
        }
        return null;
    }
}
