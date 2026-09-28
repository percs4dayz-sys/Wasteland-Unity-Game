using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Paints biome-specific terrain textures onto each concentric tier ring.
/// Uses TerrainLayers already in the project. Blends at ring boundaries for
/// natural transitions instead of hard edges.
///
/// Menu: Wasteland > Paint Biome Terrain
/// </summary>
public static class TerrainBiomePainter
{
    [MenuItem("Wasteland/Archived/Paint Biome Terrain", false, 9000)]
    public static void Paint()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            var ground = GameObject.Find("Ground");
            if (ground != null) terrain = ground.GetComponent<Terrain>();
        }
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("No Terrain", "Select the Ground terrain first.", "OK");
            return;
        }

        var td = terrain.terrainData;
        int res = td.alphamapResolution;
        int layers = td.terrainLayers.Length;
        if (layers == 0)
        {
            // Auto-load terrain layers from _TerrainAutoUpgrade
            const string layerFolder = "Assets/_TerrainAutoUpgrade";
            var guids = AssetDatabase.FindAssets("t:TerrainLayer", new[] { layerFolder });
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("No Layers", "No terrain layers found in _TerrainAutoUpgrade.", "OK");
                return;
            }
            var layerList = new List<TerrainLayer>();
            foreach (var g in guids)
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(g));
                if (layer != null) layerList.Add(layer);
            }
            td.terrainLayers = layerList.ToArray();
            layers = td.terrainLayers.Length;
            EditorUtility.SetDirty(td);
        }

        // ── Assign textures to any layers still missing them ──
        AssignLayerTextures(td);

        // ── Biome definitions: ring radius fraction → terrain layer indices ──
        // Layer indices are 0-based positions in terrainData.terrainLayers[].
        // We use fuzzy matching on layer names to find the right texture per biome.
        var biomeLayers = FindBiomeLayers(td);

        float[,,] maps = td.GetAlphamaps(0, 0, res, res);
        float half = res / 2f;
        float maxDist = half; // distance from center to corner in pixel space

        try
        {
            for (int y = 0; y < res; y++)
            {
                if (y % 64 == 0)
                    EditorUtility.DisplayProgressBar("Painting Biomes", $"Row {y}/{res}", (float)y / res);

                for (int x = 0; x < res; x++)
                {
                    // Distance from center, normalized 0–1 (0 = center, 1 = farthest corner)
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy); // 0 at center, ~1.0 at corners
                    // Clamp — corners beyond circle are still within 0–1
                    dist = Mathf.Clamp01(dist);

                    // Which biome ring?
                    // Tutorial island: 0.00 – 0.10
                    // Tier 1 Greenbelt:  0.10 – 0.28
                    // Tier 2 Barren:     0.28 – 0.50
                    // Tier 3 Scorched:   0.50 – 0.70
                    // Tier 4 Frozen:     0.70 – 0.88
                    // Tier 5 Wasteland:  0.88 – 1.00
                    int biome = dist < 0.10f ? 0 :
                                dist < 0.28f ? 1 :
                                dist < 0.50f ? 2 :
                                dist < 0.70f ? 3 :
                                dist < 0.88f ? 4 : 5;

                    // Blend width (smooths the ring boundary — 3% of radius)
                    float blend = 0.03f;

                    // Clear this pixel
                    for (int l = 0; l < layers; l++)
                        maps[y, x, l] = 0f;

                    // Paint current biome with blend to neighboring biome
                    if (biomeLayers.TryGetValue(biome, out int primary))
                    {
                        float weight = 1f;
                        // Blend toward next outer ring
                        float[] boundaries = { 0.10f, 0.28f, 0.50f, 0.70f, 0.88f };
                        for (int b = 0; b < boundaries.Length; b++)
                        {
                            float t = (dist - (boundaries[b] - blend)) / (blend * 2f);
                            t = Mathf.Clamp01(t);
                            if (t > 0f && t < 1f && biomeLayers.TryGetValue(b + 1, out int next))
                            {
                                weight = 1f - t;
                                maps[y, x, next] = t;
                            }
                        }
                        if (weight > 0f)
                            maps[y, x, primary] = Mathf.Max(maps[y, x, primary], weight);
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        td.SetAlphamaps(0, 0, maps);
        EditorUtility.SetDirty(td);
        EditorUtility.DisplayDialog("Biomes Painted",
            "Terrain painted with 6 biomes:\n" +
            "  Tutorial · Greenbelt · Barren · Scorched · Frozen · Wasteland\n\n" +
            "Adjust textures per biome by editing the Terrain Layers in the Inspector.",
            "OK");
    }

    /// <summary>
    /// Match terrain layers to biomes by fuzzy name matching.
    /// Falls back to cycling through available layers if no match.
    /// </summary>
    static Dictionary<int, int> FindBiomeLayers(TerrainData td)
    {
        var map = new Dictionary<int, int>();
        var layers = td.terrainLayers;

        // Biome → preferred layer name keywords
        (int biome, string[] keywords)[] biomeKeys = {
            (0, new[]{"grass", "green", "moss", "lush"}),
            (1, new[]{"grass", "green", "forest", "leaf"}),
            (2, new[]{"dirt", "dry", "sand", "gravel"}),
            (3, new[]{"cliff", "rock", "charcoal", "volcanic", "gravel"}),
            (4, new[]{"snow", "ice", "white"}),
            (5, new[]{"asphalt", "black", "charcoal", "dark"}),
        };

        var used = new HashSet<int>();
        foreach (var (biome, keywords) in biomeKeys)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (used.Contains(i)) continue;
                string name = layers[i] != null ? layers[i].name.ToLowerInvariant() : "";
                foreach (var kw in keywords)
                {
                    if (name.Contains(kw))
                    {
                        map[biome] = i;
                        used.Add(i);
                        break;
                    }
                }
                if (map.ContainsKey(biome)) break;
            }
        }

        // Fallback: assign remaining biomes to unused layers
        for (int biome = 0; biome < 6; biome++)
        {
            if (map.ContainsKey(biome)) continue;
            for (int i = 0; i < layers.Length; i++)
            {
                if (!used.Contains(i))
                {
                    map[biome] = i;
                    used.Add(i);
                    break;
                }
            }
        }

        return map;
    }

    /// <summary>Find real textures from Handpainted Grass / Midgard packs and assign to
    /// any terrain layers that are missing a diffuse texture. Called before painting.</summary>
    static void AssignLayerTextures(TerrainData td)
    {
        const string grassFolder  = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Grass";
        const string dirtFolder   = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Dirt";
        const string snowFolder   = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Snow";
        const string retroFolder  = "Assets/LaFinca/Midgard Textures/Retrogard/128";

        var allTextures = new List<Texture2D>();
        foreach (var f in new[] { grassFolder, dirtFolder, snowFolder, retroFolder })
        {
            if (!AssetDatabase.IsValidFolder(f)) continue;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { f }))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (tex != null) allTextures.Add(tex);
            }
        }

        foreach (var layer in td.terrainLayers)
        {
            if (layer == null || layer.diffuseTexture != null) continue;
            string n = layer.name.ToLowerInvariant();

            // Try to match by name keywords
            Texture2D pick = null;
            if (n.Contains("grass") || n.Contains("moss"))
                pick = allTextures.Find(t => t.name.ToLowerInvariant().Contains("grass") && !t.name.Contains("corrupt"));
            if (pick == null && n.Contains("dirt"))
                pick = allTextures.Find(t => t.name.ToLowerInvariant().Contains("dirt") && t.name.Contains("light"));
            if (pick == null && (n.Contains("gravel") || n.Contains("rock")))
                pick = allTextures.Find(t => t.name.ToLowerInvariant().Contains("rock"));
            if (pick == null && n.Contains("cliff"))
                pick = allTextures.Find(t => t.name.ToLowerInvariant().Contains("dirt") && t.name.Contains("desat"));
            if (pick == null && (n.Contains("charcoal") || n.Contains("asphalt")))
                pick = allTextures.Find(t => t.name.Contains("corrupt") || t.name.Contains("dark"));
            if (pick == null && (n.Contains("snow") || n.Contains("ice")))
                pick = allTextures.Find(t => t.name.ToLowerInvariant().Contains("snow"));

            // Fallback: any texture
            if (pick == null && allTextures.Count > 0)
                pick = allTextures[0];

            if (pick != null)
            {
                layer.diffuseTexture = pick;
                EditorUtility.SetDirty(layer);
            }
        }
    }
}
