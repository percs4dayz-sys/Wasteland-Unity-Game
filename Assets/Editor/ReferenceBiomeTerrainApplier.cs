using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Applies the five visible bands from the supplied Wasteland world-map reference
/// without changing the terrain heightmap or any existing foliage/water objects.
/// </summary>
public static class ReferenceBiomeTerrainApplier
{
    const string ScenePath = "Assets/dontfuckindelete.unity";
    const string OutputFolder = "Assets/Art/Generated3D/WastelandBiomeLayers";

    [MenuItem("Wasteland/Archived/Apply Reference Biome Terrain", false, 9000)]
    public static void ApplyFromMenu() => Apply();

    // Used by Unity batch mode for repeatable, non-interactive application.
    public static void Apply()
    {
        // Do not reopen the scene from an editor menu command: reloading can surface unrelated
        // missing-prefab references and discard the user's in-editor context. Open the target
        // scene yourself, then run this action.
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError("Open Assets/dontfuckindelete.unity, then run Wasteland > Apply Reference Biome Terrain.");
            return;
        }
        var ground = GameObject.Find("Ground");
        var terrain = ground != null ? ground.GetComponent<Terrain>() : null;
        if (terrain == null) throw new System.InvalidOperationException("Ground terrain was not found.");

        var data = terrain.terrainData;
        EnsureFolder(OutputFolder);
        data.terrainLayers = new[]
        {
            MakeLayer("Greenbelt", "Assets/TerrainSampleAssets/Textures/Terrain/Grass_Moss_BaseColor.tif", new Color(0.56f, 0.66f, 0.35f), 6f),
            MakeLayer("Barren_Plains", "Assets/TerrainSampleAssets/Textures/Terrain/Sand_BaseColor.tif", new Color(0.90f, 0.60f, 0.24f), 6f),
            MakeLayer("Scorched_Highlands", "Assets/TerrainSampleAssets/Textures/Terrain/Rock_BaseColor.tif", new Color(0.30f, 0.20f, 0.15f), 7f),
            MakeLayer("Frozen_Wastes", "Assets/TerrainSampleAssets/Textures/Terrain/Snow_BaseColor.tif", new Color(0.72f, 0.87f, 0.95f), 7f),
            MakeLayer("Red_Wasteland", "Assets/TerrainSampleAssets/Textures/Terrain/Rock_BaseColor.tif", new Color(0.72f, 0.27f, 0.14f), 7f),
        };

        PaintHeightBands(data);
        AssignUrpTerrainMaterial(terrain);
        terrain.Flush();
        EditorUtility.SetDirty(data);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Applied reference biomes: greenbelt, barren sand, scorched highlands, frozen wastes, and red wasteland. Existing water surfaces and foliage were preserved.");
    }

    static TerrainLayer MakeLayer(string name, string texturePath, Color tint, float tileSize)
    {
        string assetPath = OutputFolder + "/" + name.Replace(" ", "_") + ".terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(assetPath);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, assetPath);
        }
        layer.name = Path.GetFileNameWithoutExtension(assetPath);
        layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        layer.tileSize = new Vector2(tileSize, tileSize);
        layer.diffuseRemapMin = Color.black;
        layer.diffuseRemapMax = new Color(tint.r, tint.g, tint.b, 1f);
        layer.metallic = 0f;
        layer.smoothness = 0f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static void PaintHeightBands(TerrainData data)
    {
        int res = data.alphamapResolution;
        var alpha = new float[res, res, 5];
        var heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);

        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float u = x / (float)(res - 1);
            float v = y / (float)(res - 1);
            int hx = Mathf.RoundToInt(u * (data.heightmapResolution - 1));
            int hy = Mathf.RoundToInt(v * (data.heightmapResolution - 1));
            float h = heights[hy, hx];
            float noise = (Mathf.PerlinNoise(u * 15.7f + 9.3f, v * 15.7f + 4.1f) - 0.5f) * 0.018f;
            h += noise;

            // Height preserves the artist-authored landform boundaries. The band order is
            // the reference map from inner/low terrain to the high outer wasteland rim.
            float[] thresholds = { 0.15f, 0.34f, 0.54f, 0.74f };
            const float feather = 0.025f;
            int band = h < thresholds[0] ? 0 : h < thresholds[1] ? 1 : h < thresholds[2] ? 2 : h < thresholds[3] ? 3 : 4;
            alpha[y, x, band] = 1f;

            for (int b = 0; b < thresholds.Length; b++)
            {
                float t = Mathf.InverseLerp(thresholds[b] - feather, thresholds[b] + feather, h);
                if (t > 0f && t < 1f)
                {
                    alpha[y, x, b] = 1f - t;
                    alpha[y, x, b + 1] = t;
                    break;
                }
            }
        }
        data.SetAlphamaps(0, 0, alpha);
    }

    // This project uses URP's 3D Forward Renderer. Assign its actual Terrain/Lit shader
    // explicitly; a generic Lit material or a missing fallback renders Terrain magenta.
    static void AssignUrpTerrainMaterial(Terrain terrain)
    {
        const string materialPath = OutputFolder + "/URP_Terrain_Lit.mat";
        var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        if (shader == null) throw new System.InvalidOperationException("URP Terrain/Lit shader is unavailable; check the active URP renderer.");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, materialPath); }
        mat.shader = shader;
        EditorUtility.SetDirty(mat);
        terrain.materialTemplate = mat;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
