using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Paints the five tier biomes onto the EXISTING terrain. Heights are never written.
///
/// Fixes the two bugs that made the terrain read as one brown mass:
///
///   1. ABSOLUTE THRESHOLDS. ReferenceBiomeTerrainApplier banded on raw height with fixed
///      cuts at 0.15/0.34/0.54/0.74, but this terrain only spans 0.701–1.000, so three of
///      its five bands were unreachable. This measures the terrain's real min/max and
///      splits THAT range, so every band always gets used.
///
///   2. TINT VIA diffuseRemap. That is an HDRP feature; URP's Terrain/Lit ignores it, so
///      Scorched_Highlands and Red_Wasteland both rendered as the same untinted grey rock.
///      This bakes the tint into real PNG textures instead, which works in any pipeline —
///      and makes the palette thumbnails show the true colour, so it can't silently rot.
///
/// Menu:  Wasteland ▸ World Tiers ▸ 1 · Paint Biomes (heights untouched)
/// </summary>
public static class WorldTierBiomePainter
{
    const string SrcDir  = "Assets/TerrainSampleAssets/Textures/Terrain";
    const string OutDir  = "Assets/Art/Generated3D/WastelandBiomeLayers/Baked";
    const int    BakeSize = 1024;    // capped: this project has OOM'd the importer before
    const float  BlendWidth = 0.035f;
    const float  CliffStartDeg = 32f, CliffFullDeg = 55f;

    struct Biome
    {
        public string Name, Source;
        public Color Tint;
        public float Tile;
        public Biome(string n, string s, Color t, float tile) { Name = n; Source = s; Tint = t; Tile = tile; }
    }

    // Five tier bands, low → high, then the cliff layer painted by slope.
    static readonly Biome[] Bands =
    {
        new Biome("T1_Greenbelt",         "Grass_A",  new Color(0.46f, 0.62f, 0.28f),  8f),
        new Biome("T2_Barren_Plains",     "Sand",     new Color(0.88f, 0.68f, 0.32f),  9f),
        new Biome("T3_Scorched_Highlands","Muddy",    new Color(0.34f, 0.23f, 0.17f), 10f),
        new Biome("T4_Frozen_Wastes",     "Snow",     new Color(0.86f, 0.92f, 0.98f), 11f),
        new Biome("T5_Red_Wasteland",     "Rock",     new Color(0.72f, 0.29f, 0.16f), 11f),
    };
    static readonly Biome Cliff =
        new Biome("Cliff_Rock", "Rock", new Color(0.34f, 0.33f, 0.35f), 12f);

    // ── radial mode ──────────────────────────────────────────────────────────
    // Height-banding fails on this terrain because every ring has BOTH a low basin and a
    // high wall, so walls all get the same colour regardless of which tier they belong to.
    // Tier is a function of DISTANCE FROM THE CENTRE here, not elevation.
    //
    // Measured from Assets/Terrain/ChatGPT Image Jul 25, 2026, 01_08_43 PM.png (the image
    // TerrainSetup actually used): centre at world (2.9, -4.3), walls at r = 83 / 162 / 241 m,
    // land out to ~310 m.
    // Explicit band edges, used when AutoEqualBands is false. Measured wall positions.
    static readonly float[] ManualTierOuterRadius = { 83f, 162f, 241f, 285f };

    /// <summary>
    /// Divide the landmass into five EQUAL radial bands instead of using the measured wall
    /// positions. The reference art shows five evenly-progressing rings, but wall detection
    /// only finds three ridges on this heightmap — so equal division reproduces the intended
    /// look reliably, where snapping to walls would leave two tiers with nowhere to go.
    /// </summary>
    static readonly bool AutoEqualBands = true;

    const float RadialBlend = 9f;        // metres of crossfade at each boundary
    const bool  AutoDetectCentre = true; // measure the rings rather than trusting the constant
    static readonly Vector3 FallbackCentre = new Vector3(2.9f, 0f, -4.3f);

    /// <summary>Outer radius of the landmass: the furthest point still above the flat surround.</summary>
    static float DetectLandRadius(float[,] h, int res, TerrainData td, Vector3 tp, Vector3 c)
    {
        float lo = 1f, hi = 0f;
        foreach (float v in h) { if (v < lo) lo = v; if (v > hi) hi = v; }
        float land = lo + (hi - lo) * 0.06f;

        float sx = td.size.x / (res - 1), sz = td.size.z / (res - 1);
        const int B = 120;
        float maxR = Mathf.Min(td.size.x, td.size.z) * 0.75f;
        var landHits = new int[B];
        var allHits = new int[B];

        for (int z = 0; z < res; z += 2)
            for (int x = 0; x < res; x += 2)
            {
                float dx = tp.x + x * sx - c.x, dz = tp.z + z * sz - c.z;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                if (r >= maxR) continue;
                int b = (int)(r / maxR * B);
                allHits[b]++;
                if (h[z, x] > land) landHits[b]++;
            }

        // outermost ring where at least a third of the circumference is still land
        float found = maxR * 0.5f;
        for (int b = B - 1; b >= 0; b--)
        {
            if (allHits[b] < 20) continue;
            if (landHits[b] / (float)allHits[b] >= 0.33f) { found = (b + 1) / (float)B * maxR; break; }
        }
        return found;
    }

    static float[] ResolveBandEdges(float[,] h, int res, TerrainData td, Vector3 tp, Vector3 c,
                                    out float landRadius)
    {
        landRadius = DetectLandRadius(h, res, td, tp, c);
        if (!AutoEqualBands) return ManualTierOuterRadius;

        int n = Bands.Length;                      // 5 tiers -> 4 interior edges
        var edges = new float[n - 1];
        for (int i = 0; i < n - 1; i++) edges[i] = landRadius * (i + 1) / (float)n;
        return edges;
    }

    [MenuItem("Wasteland/World Tiers/1b · Paint Biomes Radially (outside-in)", false, 1)]
    public static void PaintRadial()
    {
        var terrain = FindTerrain();
        if (terrain == null) { EditorUtility.DisplayDialog("No Terrain", "No Terrain in the open scene.", "OK"); return; }

        var td = terrain.terrainData;
        var tp = terrain.transform.position;
        int hRes = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, hRes, hRes);

        Vector3 c = AutoDetectCentre ? DetectCentre(heights, hRes, td, tp) : FallbackCentre;

        float landRadius;
        float[] bandEdges = ResolveBandEdges(heights, hRes, td, tp, c, out landRadius);

        var edges = new List<string>();
        foreach (float r in bandEdges) edges.Add(r.ToString("0") + " m");
        string edgeText = string.Join(" / ", edges);

        if (!EditorUtility.DisplayDialog("Paint Biomes Radially",
            $"Ring centre: ({c.x:0.0}, {c.z:0.0})\n" +
            $"Landmass radius: {landRadius:0} m\n" +
            $"Band edges: {edgeText}\n" +
            $"Mode: {(AutoEqualBands ? "5 equal bands" : "measured wall positions")}\n\n" +
            "Tiers run outward from the centre; the outer band reaches the terrain edge.\n" +
            "Heights are NOT modified. A backup is written first.\n\nContinue?", "Paint", "Cancel")) return;

        var log = new StringBuilder("=== Paint Biomes Radially ===\n");
        string bk = Backup(td);
        if (bk != null) log.AppendLine($"Backup: {bk}");
        log.AppendLine($"Ring centre: ({c.x:0.0}, {c.z:0.0})   " +
                       (AutoDetectCentre ? "(auto-detected)" : "(constant)"));

        try
        {
            EditorUtility.DisplayProgressBar("Paint Biomes", "Baking tinted textures…", 0.1f);
            var layers = BuildLayers(log);
            if (layers == null) return;
            td.terrainLayers = layers;

            EditorUtility.DisplayProgressBar("Paint Biomes", "Painting radial bands…", 0.5f);
            PaintRadialBands(terrain, c, bandEdges, log);

            terrain.Flush();
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Paint Biomes — Done",
            "Radial bands painted. Heights untouched.\n\nRun '0b · Report Terrain Layer Coverage' to verify.", "OK");
    }

    /// <summary>Find the centre the concentric rings are built around, by radial variance.</summary>
    static Vector3 DetectCentre(float[,] h, int res, TerrainData td, Vector3 tp)
    {
        float lo = 1f, hi = 0f;
        foreach (float v in h) { if (v < lo) lo = v; if (v > hi) hi = v; }
        float land = lo + (hi - lo) * 0.06f;

        float sx = td.size.x / (res - 1), sz = td.size.z / (res - 1);
        Vector3 guess = new Vector3(tp.x + td.size.x * 0.5f, 0f, tp.z + td.size.z * 0.5f);
        Vector2 best = new Vector2(guess.x, guess.z);
        float bestScore = float.MaxValue;
        float range = Mathf.Min(td.size.x, td.size.z) * 0.18f;

        for (int gz = 0; gz <= 10; gz++)
            for (int gx = 0; gx <= 10; gx++)
            {
                float cx = guess.x + Mathf.Lerp(-range, range, gx / 10f);
                float cz = guess.z + Mathf.Lerp(-range, range, gz / 10f);

                const int B = 48;
                var s = new float[B]; var s2 = new float[B]; var n = new int[B];
                float maxR = Mathf.Min(td.size.x, td.size.z) * 0.5f;

                for (int z = 0; z < res; z += 4)
                    for (int x = 0; x < res; x += 4)
                    {
                        float v = h[z, x];
                        if (v <= land) continue;
                        float dx = tp.x + x * sx - cx, dz = tp.z + z * sz - cz;
                        float r = Mathf.Sqrt(dx * dx + dz * dz);
                        if (r >= maxR) continue;
                        int b = (int)(r / maxR * B);
                        if (b >= B) continue;
                        s[b] += v; s2[b] += v * v; n[b]++;
                    }

                float sc = 0f; int used = 0;
                for (int b = 0; b < B; b++)
                {
                    if (n[b] < 30) continue;
                    float m = s[b] / n[b];
                    sc += Mathf.Max(0f, s2[b] / n[b] - m * m); used++;
                }
                if (used < 10) continue;
                sc /= used;
                if (sc < bestScore) { bestScore = sc; best = new Vector2(cx, cz); }
            }

        return new Vector3(best.x, 0f, best.y);
    }

    static void PaintRadialBands(Terrain terrain, Vector3 c, float[] TierOuterRadius, StringBuilder log)
    {
        var td = terrain.terrainData;
        var tp = terrain.transform.position;
        int aRes = td.alphamapResolution;
        int n = Bands.Length + 1;
        int cliffIdx = Bands.Length;

        var maps = new float[aRes, aRes, n];
        var area = new int[n];

        for (int y = 0; y < aRes; y++)
        {
            for (int x = 0; x < aRes; x++)
            {
                float u = (float)x / (aRes - 1);
                float v = (float)y / (aRes - 1);
                float wx = tp.x + u * td.size.x;
                float wz = tp.z + v * td.size.z;
                float r = Mathf.Sqrt((wx - c.x) * (wx - c.x) + (wz - c.z) * (wz - c.z));

                float slope = td.GetSteepness(u, v);
                float cliff = Mathf.Clamp01((slope - CliffStartDeg) / (CliffFullDeg - CliffStartDeg));
                float rest = 1f - cliff;

                for (int b = 0; b < Bands.Length; b++)
                {
                    float inner = b == 0 ? -RadialBlend : TierOuterRadius[b - 1];
                    float outer = b < TierOuterRadius.Length ? TierOuterRadius[b] : float.MaxValue;
                    float wIn  = Mathf.Clamp01((r - inner) / RadialBlend);
                    float wOut = outer == float.MaxValue ? 1f : Mathf.Clamp01((outer - r) / RadialBlend);
                    maps[y, x, b] = Mathf.Min(wIn, wOut) * rest;
                }
                maps[y, x, cliffIdx] = cliff;

                float sum = 0f;
                for (int l = 0; l < n; l++) sum += maps[y, x, l];
                if (sum <= 0.0001f) maps[y, x, Bands.Length - 1] = 1f;
                else for (int l = 0; l < n; l++) maps[y, x, l] /= sum;

                int dom = 0; float dw = -1f;
                for (int l = 0; l < n; l++) if (maps[y, x, l] > dw) { dw = maps[y, x, l]; dom = l; }
                area[dom]++;
            }
        }

        td.SetAlphamaps(0, 0, maps);

        int total = aRes * aRes;
        log.AppendLine($"✅  Radial bands painted {aRes}×{aRes}, {n} layers.");
        for (int b = 0; b < Bands.Length; b++)
        {
            string inner = b == 0 ? "0" : TierOuterRadius[b - 1].ToString("0");
            string outer = b < TierOuterRadius.Length ? TierOuterRadius[b].ToString("0") : "edge";
            log.AppendLine($"      {Bands[b].Name,-24} r {inner,4} – {outer,-5} m   {area[b] * 100f / total,5:0.0}%");
        }
        log.AppendLine($"      {Cliff.Name,-24} slopes {CliffStartDeg:0}°–{CliffFullDeg:0}°   " +
                       $"{area[cliffIdx] * 100f / total,5:0.0}%");
    }

    [MenuItem("Wasteland/World Tiers/1 · Paint Biomes By Height (heights untouched)", false, 0)]
    public static void Paint()
    {
        var terrain = FindTerrain();
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("No Terrain", "No Terrain in the open scene.", "OK");
            return;
        }

        var td = terrain.terrainData;
        int hRes = td.heightmapResolution;
        float[,] heights = td.GetHeights(0, 0, hRes, hRes);

        // ── measure the terrain's real height range ──────────────────────────
        float lo = 1f, hi = 0f;
        foreach (float v in heights) { if (v < lo) lo = v; if (v > hi) hi = v; }
        if (hi - lo < 0.0001f)
        {
            EditorUtility.DisplayDialog("Flat Terrain",
                "The terrain has no height variation, so it can't be banded by elevation.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Paint Biomes",
            $"Terrain height range: {lo:0.000} – {hi:0.000}\n" +
            $"({lo * td.size.y:0.0} – {hi * td.size.y:0.0} m)\n\n" +
            "Five equal bands will be cut across THAT range, plus a slope-based cliff layer.\n\n" +
            "Heights are NOT modified. Terrain layers and alphamaps are replaced.\n" +
            "A backup of the TerrainData is written first.\n\nContinue?", "Paint", "Cancel")) return;

        var log = new StringBuilder("=== Paint Biomes ===\n");
        string backup = Backup(td);
        if (backup != null) log.AppendLine($"Backup: {backup}");
        log.AppendLine($"Measured height range: {lo:0.000} – {hi:0.000}  " +
                       $"({lo * td.size.y:0.0} – {hi * td.size.y:0.0} m)");

        try
        {
            EditorUtility.DisplayProgressBar("Paint Biomes", "Baking tinted textures…", 0.1f);
            var layers = BuildLayers(log);
            if (layers == null) return;
            td.terrainLayers = layers;

            EditorUtility.DisplayProgressBar("Paint Biomes", "Painting alphamaps…", 0.5f);
            PaintAlphamaps(terrain, heights, lo, hi, log);

            terrain.Flush();
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Paint Biomes — Done",
            "Biomes painted. Heights untouched.\n\n" +
            "Run '0b · Report Terrain Layer Coverage' to confirm all 6 layers are visible.", "OK");
    }

    // ── layers ───────────────────────────────────────────────────────────────

    static TerrainLayer[] BuildLayers(StringBuilder log)
    {
        EnsureFolder(OutDir);
        var list = new List<TerrainLayer>();

        var all = new List<Biome>(Bands) { Cliff };
        foreach (var b in all)
        {
            var tex = BakeTinted(b, log);
            if (tex == null)
            {
                log.AppendLine($"!!  aborted — could not bake {b.Source}");
                Debug.LogError(log.ToString());
                EditorUtility.DisplayDialog("Missing Texture",
                    $"Could not load {SrcDir}/{b.Source}_BaseColor.tif", "OK");
                return null;
            }

            string path = $"{OutDir}/{b.Name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }

            layer.name            = b.Name;
            layer.diffuseTexture  = tex;
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{SrcDir}/{b.Source}_Normal.tif");
            layer.normalScale     = 0.7f;
            layer.tileSize        = new Vector2(b.Tile, b.Tile);
            layer.tileOffset      = Vector2.zero;
            layer.metallic        = 0f;
            layer.smoothness      = 0f;
            // Tint is baked into the texture, so keep the remap neutral. URP ignores it anyway.
            layer.diffuseRemapMin = Color.black;
            layer.diffuseRemapMax = Color.white;

            EditorUtility.SetDirty(layer);
            list.Add(layer);
        }

        AssetDatabase.SaveAssets();
        log.AppendLine($"✅  {list.Count} terrain layers built in {OutDir} (tints baked into the textures).");
        return list.ToArray();
    }

    /// <summary>Multiply a source texture by the biome tint and write it out as a real PNG.</summary>
    static Texture2D BakeTinted(Biome b, StringBuilder log)
    {
        string outPath = $"{OutDir}/{b.Name}_Albedo.png";
        string srcPath = $"{SrcDir}/{b.Source}_BaseColor.tif";

        var imp = AssetImporter.GetAtPath(srcPath) as TextureImporter;
        if (imp == null) return null;
        if (!imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }

        var src = AssetDatabase.LoadAssetAtPath<Texture2D>(srcPath);
        if (src == null) return null;

        // Read the source directly. Blitting into a temporary RenderTexture and calling
        // ReadPixels produced solid-colour garbage here (uninitialised buffer), so this
        // does an explicit box downsample from the real pixels instead — deterministic,
        // and cheap enough at these sizes.
        int sw = src.width, sh = src.height;
        Color32[] srcPx;
        try
        {
            srcPx = src.GetPixels32();
        }
        catch (UnityException e)
        {
            log.AppendLine($"!!  {b.Source}_BaseColor is not readable: {e.Message}");
            return null;
        }

        int w = Mathf.Min(sw, BakeSize), h = Mathf.Min(sh, BakeSize);
        var outPx = new Color32[w * h];

        double avgR = 0, avgG = 0, avgB = 0;
        for (int y = 0; y < h; y++)
        {
            int sy = (int)((long)y * sh / h);
            for (int x = 0; x < w; x++)
            {
                int sx = (int)((long)x * sw / w);
                Color32 s = srcPx[sy * sw + sx];
                avgR += s.r; avgG += s.g; avgB += s.b;

                // multiply by the tint, lifted so dark tints keep some texture detail
                outPx[y * w + x] = new Color32(
                    (byte)Mathf.Clamp(s.r * b.Tint.r * 1.35f, 0f, 255f),
                    (byte)Mathf.Clamp(s.g * b.Tint.g * 1.35f, 0f, 255f),
                    (byte)Mathf.Clamp(s.b * b.Tint.b * 1.35f, 0f, 255f),
                    255);
            }
        }

        long n = (long)w * h;
        var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tmp.SetPixels32(outPx);
        tmp.Apply();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", outPath), tmp.EncodeToPNG());
        Object.DestroyImmediate(tmp);
        AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);

        // Log the source average so a bad read is obvious instead of silent.
        log.AppendLine($"      baked {b.Name,-24} from {b.Source}_BaseColor  {sw}×{sh} → {w}×{h}   " +
                       $"src avg RGB ({avgR / n:0},{avgG / n:0},{avgB / n:0})");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    // ── alphamaps ────────────────────────────────────────────────────────────

    static void PaintAlphamaps(Terrain terrain, float[,] heights, float lo, float hi, StringBuilder log)
    {
        var td = terrain.terrainData;
        int aRes = td.alphamapResolution;
        int hRes = td.heightmapResolution;
        int n = Bands.Length + 1;                 // + cliff
        int cliffIdx = Bands.Length;

        var maps = new float[aRes, aRes, n];
        float span = hi - lo;
        float bandSize = 1f / Bands.Length;

        for (int y = 0; y < aRes; y++)
        {
            for (int x = 0; x < aRes; x++)
            {
                int hy = Mathf.RoundToInt((float)y / (aRes - 1) * (hRes - 1));
                int hx = Mathf.RoundToInt((float)x / (aRes - 1) * (hRes - 1));

                // remap into 0-1 across the terrain's ACTUAL range - this is the fix
                float h = Mathf.Clamp01((heights[hy, hx] - lo) / span);

                float u = (float)x / (aRes - 1);
                float v = (float)y / (aRes - 1);
                float slope = td.GetSteepness(u, v);
                float cliff = Mathf.Clamp01((slope - CliffStartDeg) / (CliffFullDeg - CliffStartDeg));
                float rest = 1f - cliff;

                for (int b = 0; b < Bands.Length; b++)
                {
                    float bLo = b * bandSize;
                    float bHi = (b + 1) * bandSize;
                    float wIn  = Mathf.Clamp01((h - bLo) / BlendWidth);
                    float wOut = Mathf.Clamp01((bHi - h) / BlendWidth);
                    maps[y, x, b] = Mathf.Min(wIn, wOut) * rest;
                }
                maps[y, x, cliffIdx] = cliff;

                float sum = 0f;
                for (int l = 0; l < n; l++) sum += maps[y, x, l];
                if (sum <= 0.0001f) maps[y, x, Mathf.Clamp((int)(h / bandSize), 0, Bands.Length - 1)] = 1f;
                else for (int l = 0; l < n; l++) maps[y, x, l] /= sum;
            }
        }

        td.SetAlphamaps(0, 0, maps);

        log.AppendLine($"✅  Alphamaps painted {aRes}×{aRes}, {n} layers.");
        log.AppendLine("      band boundaries in world height:");
        for (int b = 0; b < Bands.Length; b++)
            log.AppendLine($"        {Bands[b].Name,-24} " +
                           $"{(lo + span * b * bandSize) * td.size.y,6:0.0} – " +
                           $"{(lo + span * (b + 1) * bandSize) * td.size.y,6:0.0} m");
        log.AppendLine($"        {Cliff.Name,-24} slopes {CliffStartDeg:0}°–{CliffFullDeg:0}°");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    static Terrain FindTerrain()
    {
        var g = GameObject.Find("Ground");
        var t = g != null ? g.GetComponent<Terrain>() : null;
        return t != null ? t : Terrain.activeTerrain;
    }

    /// <summary>Timestamped TerrainData backup. Shared with WorldTierElevationStepper.</summary>
    public static string BackupPublic(TerrainData td) => Backup(td);

    static string Backup(TerrainData td)
    {
        string src = AssetDatabase.GetAssetPath(td);
        if (string.IsNullOrEmpty(src)) return null;
        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", src));
        if (!File.Exists(full)) return null;

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TerrainBackups"));
        Directory.CreateDirectory(dir);
        string dest = Path.Combine(dir,
            $"{Path.GetFileNameWithoutExtension(src)}_{System.DateTime.Now:yyyyMMdd_HHmmss}.bak");
        File.Copy(full, dest, true);
        return dest;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
