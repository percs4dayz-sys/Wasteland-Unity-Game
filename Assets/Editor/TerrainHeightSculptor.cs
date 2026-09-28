using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates 3D heightmaps for Unity Terrains. 
/// Use this to create the actual mountains and valleys that the Curvature modifier will then paint.
/// </summary>
public class TerrainHeightSculptor : EditorWindow
{
    [MenuItem("Wasteland/Archived/Terrain/Sculpt Mountains and Valleys", false, 9000)]
    public static void SculptSelectedTerrain()
    {
        Terrain terrain = Selection.activeGameObject?.GetComponent<Terrain>();

        if (terrain == null)
        {
            terrain = Terrain.activeTerrain;
        }

        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a Terrain object in the hierarchy first.", "OK");
            return;
        }

        Generate(terrain.terrainData);
    }

    public static void Generate(TerrainData data)
    {
        int res = data.heightmapResolution;
        float[,] heights = new float[res, res];

        // 1. Ensure the layout PNG is readable (otherwise GetPixelBilinear crashes the script)
        string layoutPath = "Assets/Art/world layout.png";
        Texture2D layout = AssetDatabase.LoadAssetAtPath<Texture2D>(layoutPath);
        
        if (layout == null)
        {
            Debug.LogWarning($"[Terrain] '{layoutPath}' not found. Generating purely procedural tiers.");
        }
        else
        {
            var importer = AssetImporter.GetAtPath(layoutPath) as TextureImporter;
            if (importer != null && (!importer.isReadable || importer.textureCompression != TextureImporterCompression.Uncompressed))
            {
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                layout = AssetDatabase.LoadAssetAtPath<Texture2D>(layoutPath); // Reload
            }
        }

        // Randomize offsets so every generation is slightly different
        float seed = Random.Range(0f, 1000f);

        try
        {
            EditorUtility.DisplayProgressBar("Sculpting Terrain", "Calculating heights...", 0f);
            for (int y = 0; y < res; y++)
            {
                if (y % 32 == 0) EditorUtility.DisplayProgressBar("Sculpting Terrain", $"Row {y}/{res}", (float)y / res);
                for (int x = 0; x < res; x++)
                {
                    heights[y, x] = CalculateHeight(x, y, res, seed, layout);
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        
        // Apply the heights to the terrain
        data.SetHeights(0, 0, heights);
        Debug.Log($"[Terrain] Sculpted {res}x{res} heightmap.");
    }

    private static float CalculateHeight(int x, int y, int res, float seed, Texture2D layout)
    {
        float xCoord = (float)x / (res - 1);
        float yCoord = (float)y / (res - 1);

        // 1. Distance from center (normalized 0-1)
        float distToCenterRaw = Vector2.Distance(new Vector2(xCoord, yCoord), new Vector2(0.5f, 0.5f));
        float distFromCenter = distToCenterRaw * 2f;

        // 2. Flat raised plateau — all tiers sit at the same height; only gullies cut downward
        float tierBase = 0.90f;

        // 3. Tier breaks — lowered gullies between rings, with bridges at N/S/E/W
        float valleyMask = 0f;
        float[] boundaries = { 0.20f, 0.40f, 0.60f, 0.80f };
        // angle from centre: 0°=East, 90°=North, -90°/270°=South, ±180°=West
        float angleDeg = Mathf.Atan2(yCoord - 0.5f, xCoord - 0.5f) * Mathf.Rad2Deg;
        foreach (float b in boundaries)
        {
            float gap = Mathf.Abs(distFromCenter - b);
            if (gap < 0.03f)
            {
                float rawDepth = (1f - (gap / 0.03f)) * 0.3f;

                // Bridge factor: 0 on the cardinal axis (flat bridge), 1 away from it (full gulley)
                float bridgeFactor = 1f;
                float[] cardinals = { 0f, 90f, 180f, -90f }; // E, N, W, S
                foreach (float ca in cardinals)
                {
                    float aDist = Mathf.Abs(Mathf.DeltaAngle(angleDeg, ca));
                    if (aDist < 12f)                        // 12°-wide bridge approach
                        bridgeFactor = Mathf.Min(bridgeFactor, aDist / 12f);
                }

                float depth = rawDepth * bridgeFactor;
                valleyMask = Mathf.Min(valleyMask, -depth);  // cut downward
            }
        }

        // 4. Layout PNG — disabled so hand-drawn marks / text don't bleed into the terrain.
        //    Set layoutAlpha = 0.5f for a neutral mid-grey base (no influence on height).
        float layoutAlpha = 0.5f;

        // 5. Natural Detail Noise
        float detail = Mathf.PerlinNoise(xCoord * 12f + seed, yCoord * 12f + seed) * 0.05f;

        // Final composition — tierBase drives the stepped hill, valleys cut breaks between rings
        float finalHeight = tierBase + valleyMask + (layoutAlpha * 0.25f) + detail;

        // Fade the outermost rim to zero so it meets the perimeter cleanly.
        // Start fade at 98% of radius — keeps corners on the plateau.
        float edgeMask = Mathf.Clamp01((0.98f - distToCenterRaw) * 50f);
        
        return Mathf.Clamp01(finalHeight * edgeMask);
    }
    
    [MenuItem("Wasteland/Archived/Terrain/Flatten Terrain", false, 9000)]
    public static void Flatten()
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return;
        
        float[,] heights = new float[terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution];
        terrain.terrainData.SetHeights(0, 0, heights);
    }
}