using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;

public class TerrainSetup
{
    // THE WORKING TERRAIN TOOL. Sources Assets/Terrain/ChatGPT Image Jul 25, 2026, 01_08_43 PM.png
    // and paints biomes by HEIGHT BAND (not radius). Do not archive this again.
    [MenuItem("Wasteland/Terrain/Setup Wasteland Terrain (heightmap + biomes)", false, 10)]
    public static void SetupTerrain()
    {
        EditorSceneManager.OpenScene("Assets/dontfuckindelete.unity", OpenSceneMode.Single);

        Terrain terrain = Object.FindAnyObjectByType<Terrain>();
        if (terrain == null)
        {
            TerrainData terrainData = new TerrainData();
            terrainData.heightmapResolution = 513;
            terrainData.size = new Vector3(512, 100, 512);
            GameObject terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            terrainGo.name = "Terrain";
            terrain = terrainGo.GetComponent<Terrain>();
        }

        TerrainData data = terrain.terrainData;

        // Ensure the terrain has a valid URP material — the scene has a broken/missing material ref.
        Material terrainMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Generated3D/MainWorldTerrain_Lit.mat");
        if (terrainMat != null)
        {
            terrain.materialTemplate = terrainMat;
            EditorUtility.SetDirty(terrain);
            Debug.Log("[TerrainSetup] Assigned terrain material: MainWorldTerrain_Lit");
        }
        else
            Debug.LogWarning("[TerrainSetup] Could not find MainWorldTerrain_Lit.mat — terrain may render grey.");

        // ── Heightmap ──────────────────────────────────────────────────────────
        Texture2D heightmapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Terrain/ChatGPT Image Jul 25, 2026, 01_08_43 PM.png");
        if (heightmapTexture == null)
        {
            Debug.LogError("[TerrainSetup] Could not load heightmap PNG!");
            return;
        }

        int heightmapRes = data.heightmapResolution;
        float[,] heights = new float[heightmapRes, heightmapRes];
        for (int y = 0; y < heightmapRes; y++)
        {
            for (int x = 0; x < heightmapRes; x++)
            {
                float u = (float)x / (heightmapRes - 1);
                float v = (float)y / (heightmapRes - 1);
                heights[y, x] = heightmapTexture.GetPixelBilinear(u, v).grayscale;
            }
        }
        data.SetHeights(0, 0, heights);

        // Flush so GetInterpolatedHeight/GetSteepness see the new heights below
        terrain.Flush();
        EditorUtility.SetDirty(terrain);
        Debug.Log("[TerrainSetup] Heightmap applied!");

        // ── Terrain Layers ─────────────────────────────────────────────────────
        TerrainLayer layerGrass    = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_TerrainAutoUpgrade/layer_Grass608c3929b87867c.terrainlayer");
        TerrainLayer layerDirt     = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_TerrainAutoUpgrade/layer_Dirt_1_DiffuseDirt_1_Normal2023054508611406.terrainlayer");
        TerrainLayer layerSand     = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_TerrainAutoUpgrade/layer_Sand_DiffuseSand_Normal2023054508611406.terrainlayer");
        TerrainLayer layerCharcoal = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_TerrainAutoUpgrade/layer_charcoal_1_d3d3aba195e4d26ee.terrainlayer");
        TerrainLayer layerSnow     = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_TerrainAutoUpgrade/layer_snow_biome.terrainlayer");
        TerrainLayer layerAsh      = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_TerrainAutoUpgrade/layer_ash_volcanic.terrainlayer");
        TerrainLayer layerCliffs   = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_TerrainAutoUpgrade/layer_Cliffs608c3929b87867c.terrainlayer");

        // Log each one individually so you can see exactly which failed
        if (layerGrass    == null) { Debug.LogError("[TerrainSetup] MISSING: layer_Grass608c3929b87867c.terrainlayer"); return; }
        if (layerDirt     == null) { Debug.LogError("[TerrainSetup] MISSING: layer_Dirt_1_DiffuseDirt_1_Normal2023054508611406.terrainlayer"); return; }
        if (layerSand     == null) { Debug.LogError("[TerrainSetup] MISSING: layer_Sand_DiffuseSand_Normal2023054508611406.terrainlayer"); return; }
        if (layerCharcoal == null) { Debug.LogError("[TerrainSetup] MISSING: layer_charcoal_1_d3d3aba195e4d26ee.terrainlayer"); return; }
        if (layerSnow     == null) { Debug.LogError("[TerrainSetup] MISSING: layer_snow_biome.terrainlayer"); return; }
        if (layerAsh      == null) { Debug.LogError("[TerrainSetup] MISSING: layer_ash_volcanic.terrainlayer"); return; }
        if (layerCliffs   == null) { Debug.LogError("[TerrainSetup] MISSING: layer_Cliffs608c3929b87867c.terrainlayer"); return; }

        Debug.Log("[TerrainSetup] All 7 terrain layers loaded OK.");

        data.terrainLayers = new TerrainLayer[]
        {
            layerGrass,     // 0 – Tutorial Island / Greenbelt  (center, lowest)
            layerDirt,      // 1 – Tier 1 Greenbelt
            layerSand,      // 2 – Tier 2 Barren Plains
            layerCharcoal,  // 3 – Tier 3 Scorched Highlands
            layerSnow,      // 4 – Tier 4 Frozen Wastes
            layerAsh,       // 5 – Tier 5 Wasteland (outer ring)
            layerCliffs     // 6 – Steep slopes across all zones
        };

        // ── Alphamap painting ──────────────────────────────────────────────────
        int alphaRes   = data.alphamapResolution;
        int layerCount = data.terrainLayers.Length; // 7
        float[,,] maps = new float[alphaRes, alphaRes, layerCount];

        // Pre-sample heights from the raw array instead of relying on GetInterpolatedHeight
        // This avoids any terrain-flush timing issues entirely.
        float[,] rawHeights = data.GetHeights(0, 0, heightmapRes, heightmapRes);

        // Remap raw height (0-1) into a normalised zone value (0-1) using the
        // actual height range from the heightmap so all zones are reachable.
        const float hMin = 0.701f;
        const float hMax = 1.000f;

        // Zone thresholds in normalised (remapped) space – 6 equal bands:
        // 0.000–0.167  Grass    Tutorial Island / Greenbelt  (lowest)
        // 0.167–0.333  Dirt     Tier 1 Greenbelt
        // 0.333–0.500  Sand     Tier 2 Barren Plains
        // 0.500–0.667  Charcoal Tier 3 Scorched Highlands
        // 0.667–0.833  Snow     Tier 4 Frozen Wastes
        // 0.833–1.000  Ash      Tier 5 Wasteland (outer)
        float blendWidth = 0.03f;

        for (int y = 0; y < alphaRes; y++)
        {
            for (int x = 0; x < alphaRes; x++)
            {
                // Map alphamap pixel to heightmap pixel
                int hy = Mathf.RoundToInt((float)y / (alphaRes - 1) * (heightmapRes - 1));
                int hx = Mathf.RoundToInt((float)x / (alphaRes - 1) * (heightmapRes - 1));
                float rawH = rawHeights[hy, hx];

                // Remap into 0-1 across the actual height range
                float h = Mathf.Clamp01((rawH - hMin) / (hMax - hMin));

                // Sample slope via GetSteepness (needs 0-1 UV)
                float u     = (float)x / (alphaRes - 1);
                float v     = (float)y / (alphaRes - 1);
                float slope = data.GetSteepness(u, v) / 90f;

                // Height-based zone weights
                float wGrass    = ZoneWeight(h, 0.000f, 0.167f, blendWidth);
                float wDirt     = ZoneWeight(h, 0.167f, 0.333f, blendWidth);
                float wSand     = ZoneWeight(h, 0.333f, 0.500f, blendWidth);
                float wCharcoal = ZoneWeight(h, 0.500f, 0.667f, blendWidth);
                float wSnow     = ZoneWeight(h, 0.667f, 0.833f, blendWidth);
                float wAsh      = ZoneWeight(h, 0.833f, 1.000f, blendWidth);

                // Cliffs blend in on slopes > ~30 degrees
                float cliffBlend = Mathf.Clamp01((slope - 0.33f) / 0.25f);
                float zoneScale  = 1f - cliffBlend;

                maps[y, x, 0] = wGrass    * zoneScale;
                maps[y, x, 1] = wDirt     * zoneScale;
                maps[y, x, 2] = wSand     * zoneScale;
                maps[y, x, 3] = wCharcoal * zoneScale;
                maps[y, x, 4] = wSnow     * zoneScale;
                maps[y, x, 5] = wAsh      * zoneScale;
                maps[y, x, 6] = cliffBlend;
            }
        }

        data.SetAlphamaps(0, 0, maps);

        // Mark the TerrainData asset itself dirty and force a full save.
        // Without this the alphamaps are written in memory but never flushed to the .asset file.
        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);
        terrain.Flush();
        AssetDatabase.SaveAssets();
        Debug.Log("[TerrainSetup] Terrain layers painted!");

        // Disable any Procedural Terrain Painter components so they can't overwrite our alphamaps.
        var painters = terrain.GetComponents<MonoBehaviour>()
            .Where(mb => mb != null && mb.GetType().Name == "TerrainPainter")
            .ToArray();
        foreach (var p in painters)
        {
            p.enabled = false;
            EditorUtility.SetDirty(p);
            Debug.Log($"[TerrainSetup] Disabled TerrainPainter: {p.GetType().FullName}");
        }

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[TerrainSetup] Done!");
    }

    [MenuItem("Wasteland/Terrain/Diagnose Wasteland Terrain (read-only)", false, 11)]
    public static void DiagnoseTerrain()
    {
        Terrain terrain = Object.FindAnyObjectByType<Terrain>();
        if (terrain == null) { Debug.LogError("[Diagnose] No Terrain found in scene!"); return; }

        TerrainData data = terrain.terrainData;
        int alphaRes    = data.alphamapResolution;
        int heightmapRes = data.heightmapResolution;
        int layerCount  = data.terrainLayers.Length;

        Debug.Log($"[Diagnose] alphamapResolution={alphaRes}  heightmapResolution={heightmapRes}  terrainLayers={layerCount}  terrainSize={data.size}");

        // Print each layer name
        for (int i = 0; i < layerCount; i++)
            Debug.Log($"[Diagnose]   Layer[{i}] = {(data.terrainLayers[i] != null ? data.terrainLayers[i].name : "NULL")}");

        // Sample the alphamap at 5 points across the terrain and print weights
        float[,,] maps = data.GetAlphamaps(0, 0, alphaRes, alphaRes);
        int[] samplePixels = new int[] { 0, alphaRes/4, alphaRes/2, 3*alphaRes/4, alphaRes-1 };
        foreach (int p in samplePixels)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append($"[Diagnose] Alphamap [{p},{p}]: ");
            for (int l = 0; l < layerCount; l++)
                sb.Append($"L{l}={maps[p, p, l]:F3}  ");
            Debug.Log(sb.ToString());
        }

        // Sample height range
        float[,] heights = data.GetHeights(0, 0, heightmapRes, heightmapRes);
        float minH = float.MaxValue, maxH = float.MinValue;
        for (int y = 0; y < heightmapRes; y++)
            for (int x = 0; x < heightmapRes; x++)
            { if (heights[y,x] < minH) minH = heights[y,x]; if (heights[y,x] > maxH) maxH = heights[y,x]; }
        Debug.Log($"[Diagnose] Height range: min={minH:F4}  max={maxH:F4}");
    }

    // Returns weight 1.0 inside [lo, hi], soft crossfade of width `blend` at each edge.
    private static float ZoneWeight(float h, float lo, float hi, float blend)
    {
        float fadeIn  = Mathf.Clamp01((h - lo) / blend);
        float fadeOut = Mathf.Clamp01((hi - h) / blend);
        return Mathf.Min(fadeIn, fadeOut);
    }
}
