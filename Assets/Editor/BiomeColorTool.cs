using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Repaints the world "Ground" mesh into regional biomes WITHOUT changing its shape — replacing the
/// model's baked (often distorted) texture with the Wasteland/BiomeTerrain shader.
///
/// You steer the biomes by editing one top-down image, Assets/Art/Generated3D/BiomeMask.png:
///   BLACK (unpainted) = Grass    Red = Desert    Green = Swamp    Blue = Snow
/// Steep slopes auto-become rock/cliff. Paint the mask in ANY image editor, save, and the world
/// updates — no in-Unity brush required (those only ever worked on Unity Terrain, not meshes).
///
/// Menus:  Wasteland ▸ Terrain ▸ Apply Biome Coloring to Ground
///         Wasteland ▸ Terrain ▸ Regenerate Starter Biome Map  (overwrites the mask)
/// </summary>
public static class BiomeColorTool
{
    static string MaskPath    => WastelandPaths.Resolve(WastelandPaths.Textures  + "/BiomeMask.png",        WastelandPaths.Legacy + "/BiomeMask.png");
    static string MatPath     => WastelandPaths.Resolve(WastelandPaths.Materials + "/BiomeTerrain.mat",     WastelandPaths.Legacy + "/BiomeTerrain.mat");
    static string CityMatPath => WastelandPaths.Resolve(WastelandPaths.Materials + "/BiomeTerrainCity.mat", WastelandPaths.Legacy + "/BiomeTerrainCity.mat");

    [MenuItem("Wasteland/Archived/Terrain/Apply Biome Coloring to World", false, 9000)]
    public static void Apply()
    {
        var shader = Shader.Find("Wasteland/BiomeTerrain");
        if (shader == null)
        {
            EditorUtility.DisplayDialog("Biome Coloring",
                "Shader 'Wasteland/BiomeTerrain' not found yet. Let Unity finish compiling " +
                "(BiomeTerrain.shader) and re-run this menu.", "OK");
            return;
        }

        // Starter biome map (only if one doesn't already exist — never clobber the user's edits).
        if (!File.Exists(MaskPath)) GenerateStarterMask(MaskPath);
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);

        // Each world piece gets its own material so its biome layout fits its own footprint.
        int ground = ApplyTo("Ground",    MatPath,     shader, mask);
        int city   = ApplyTo("Cityscape", CityMatPath, shader, mask);

        // Give the city a weathered-concrete look on its steep faces (buildings/rubble).
        if (city > 0)
        {
            var cityMat = AssetDatabase.LoadAssetAtPath<Material>(CityMatPath);
            var concrete = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Industrial_Zone_Modular_Pack/Textures/Factory_Old/Factory_Old_Concrete.png");
            if (cityMat != null && concrete != null)
            {
                cityMat.SetTexture("_RockTex", concrete);
                cityMat.SetFloat("_RockTexScale", 0.25f);
                cityMat.SetColor("_RockColor", new Color(0.40f, 0.38f, 0.35f));   // grimy concrete
                cityMat.SetFloat("_RockStart", 0.35f);   // more building faces read as concrete
                EditorUtility.SetDirty(cityMat);
            }
        }

        if (ground == 0 && city == 0)
        {
            EditorUtility.DisplayDialog("Biome Coloring",
                "Found no 'Ground' or 'Cityscape' to recolor. Add them first.", "OK");
            return;
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Biome Coloring",
            "Recolored into biomes (shapes untouched):\n" +
            "   Ground: " + ground + " mesh(es)   Cityscape: " + city + " mesh(es)\n\n" +
            "Both halves now share the same stylized look. Steep faces (incl. buildings) go rocky.\n\n" +
            "Move biomes by painting " + MaskPath + " (BLACK=grass RED=desert GREEN=swamp BLUE=snow).\n" +
            "Tweak colors on BiomeTerrain.mat / BiomeTerrainCity.mat. SAVE the scene (Ctrl+S) when happy.", "OK");
    }

    // Recolors one world piece by name; returns how many renderers were affected (0 if missing).
    static int ApplyTo(string objName, string matPath, Shader shader, Texture2D mask)
    {
        var go = GameObject.Find(objName);
        if (go == null) return 0;
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0;

        // This piece's footprint so its mask maps across its own area.
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            WastelandPaths.EnsureFolder(Path.GetDirectoryName(matPath).Replace('\\', '/'));
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }

        mat.shader = shader;
        mat.SetTexture("_BiomeMask", mask);
        mat.SetVector("_WorldMin",  new Vector4(b.min.x, 0f, b.min.z, 0f));
        mat.SetVector("_WorldSize", new Vector4(Mathf.Max(b.size.x, 0.01f), 1f, Mathf.Max(b.size.z, 0.01f), 0f));
        EditorUtility.SetDirty(mat);

        foreach (var r in renderers)
        {
            int slots = Mathf.Max(1, r.sharedMaterials.Length);
            var mats = new Material[slots];
            for (int i = 0; i < slots; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
        return renderers.Length;
    }

    [MenuItem("Wasteland/Archived/Terrain/Regenerate Starter Biome Map", false, 9000)]
    public static void RegenerateMask()
    {
        GenerateStarterMask(MaskPath);
        EditorUtility.DisplayDialog("Biome Map",
            "Wrote a fresh starter map to:\n" + MaskPath + "\n\n" +
            "Layout: snow top, grass centre, swamp bottom-left, desert bottom-right.\n" +
            "If the biomes land rotated/flipped vs. your view, tell Claude which way and it'll spin them. " +
            "Otherwise paint over it (BLACK=grass RED=desert GREEN=swamp BLUE=snow).", "OK");
    }

    // Starter layout matching the sketch: snow top, grass centre (black), swamp bottom-left,
    // desert bottom-right. y=0 is the bottom row of the image.
    static void GenerateStarterMask(string path)
    {
        const int N = 256;
        var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            var p = new Vector2(x / (N - 1f), y / (N - 1f));   // (0,0)=bottom-left .. (1,1)=top-right
            float snow   = Mathf.Clamp01(1f - Vector2.Distance(p, new Vector2(0.50f, 0.88f)) / 0.55f);
            float swamp  = Mathf.Clamp01(1f - Vector2.Distance(p, new Vector2(0.13f, 0.13f)) / 0.42f);
            float desert = Mathf.Clamp01(1f - Vector2.Distance(p, new Vector2(0.87f, 0.14f)) / 0.44f);
            px[y * N + x] = new Color(desert, swamp, snow, 1f);   // black where all 0 -> grass
        }
        tex.SetPixels(px);
        tex.Apply();
        WastelandPaths.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        if (AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            ti.textureType        = TextureImporterType.Default;
            ti.sRGBTexture        = true;
            ti.wrapMode           = TextureWrapMode.Clamp;
            ti.mipmapEnabled      = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
    }
}
