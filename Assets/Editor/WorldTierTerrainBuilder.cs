using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the 5-tier world directly from the authored heightmap
/// Assets/worldterrainlayout.png, then paints the tier biomes and places the gates.
///
/// The heightmap encodes concentric mountain WALLS with flat basins between them —
/// measured from the source image, with the ring centre at pixel (636, 942):
///
///     r &lt; 80 px      Tutorial Island   (plateau, ringed by a lagoon at 80–100 px)
///     ..312 px       Tier 1 Greenbelt           wall crest at 312 px  = 149 m
///     ..480 px       Tier 2 Barren Plains       wall crest at 480 px  = 230 m
///     ..648 px       Tier 3 Scorched Highlands  wall crest at 648 px  = 310 m
///     ..816 px       Tier 4 Frozen Wastes       wall crest at 816 px  = 390 m
///     beyond         Tier 5 Wasteland, dropping to the coast at ~910 px
///
/// The walls in the source art are continuous, so this tool CUTS a small number of
/// passes through each one and drops a TierGate into each pass. The wall does the
/// funnelling, the gate does the gating — a player cannot walk around a gate.
///
/// Menu:  Wasteland ▸ World Tiers ▸ …
///
/// NOTE: MainWorld3D.unity and dontfuckindelete.unity share one TerrainData
/// (Assets/Art/Generated3D/MainWorldData.asset), so building affects BOTH scenes.
/// A timestamped backup is written to &lt;project&gt;/TerrainBackups/ before any write.
/// </summary>
public static class WorldTierTerrainBuilder
{
    // ════════════════════════════════════════════════════════════════════════
    //  SOURCE HEIGHTMAP
    // ════════════════════════════════════════════════════════════════════════

    const string HeightmapPath = "Assets/worldterrainlayout.png";

    /// <summary>Ring centre in normalised image space. Measured: px (636, 942) of 1254².</summary>
    const float CenterU = 636f / 1254f;          // 0.5072 — east/west
    const float CenterV = 1f - 942f / 1254f;     // 0.2488 — v is measured from the SOUTH edge

    /// <summary>Radii as a fraction of image width, measured from the source art.</summary>
    const float IslandR = 80f / 1254f;
    const float LagoonR = 100f / 1254f;
    static readonly float[] WallR = { 312f / 1254f, 480f / 1254f, 648f / 1254f, 816f / 1254f };

    /// <summary>Raw image value (0–1) that becomes the waterline. Lagoon reads ~0.27, basins ~0.36.</summary>
    const float SeaLevelRaw = 0.305f;

    // Vertical range. The source art's wall faces are ~70-80°, so the walls are impassable at
    // any value here — this is an art-direction dial, not a gameplay one. At 100 m the walls
    // still stand ~45 m over the basins while Tutorial Island reads as an island rather than
    // the 50 m cliff-sided mesa you get at 165 m.
    const float TerrainHeightMetres = 100f;
    const int   Supersample         = 3;      // NxN box filter per heightmap texel

    // ── passes cut through each wall, as compass bearings (deg from north, +east) ──
    // Chosen to sit inside the land for that wall and to stagger the route outward,
    // so the player has to travel around each basin rather than run straight out.
    static readonly float[][] PassBearings =
    {
        new[] { -95f,   0f, 100f },   // into Tier 2 — land spans about -128..132 here
        new[] { -55f,  48f        },  // into Tier 3 — land spans about  -88.. 96
        new[] { -38f,  28f        },  // into Tier 4 — land spans about  -68.. 64
        new[] { -16f,  16f        },  // into Tier 5 — land spans about  -40.. 42
    };

    const float PassHalfDeg    = 4.5f;    // angular half-width of the cut
    const float PassFeatherDeg = 3.0f;    // soften the shoulders
    const float PassRadialFrac = 0.055f;  // radial half-length of the cut, fraction of image width

    // ── waypoint island (the detached landmass west of Tutorial Island) ──────
    // Measured from the source art: ~15,000 m², centred on world (-180, -145), joined to
    // the mainland only by a 5.6 m neck sitting right at the waterline. Carving that neck
    // makes it a true island, so the shortcut network is the only way on or off.
    const float IslandCenterU  = 250f / 1254f;
    const float IslandCenterV  = 1f - 929f / 1254f;
    const float PlazaRadius    = 30f / 1254f;   // flattened hub plaza
    const float PlazaLevelRaw  = 0.44f;         // plaza surface, comfortably above the waterline

    /// <summary>Neck between the waypoint island and the mainland — cut below the waterline.</summary>
    const float NeckU          = 294f / 1254f;
    const float NeckV          = 1f - 813f / 1254f;
    const float NeckRadius     = 22f / 1254f;
    const float NeckLevelRaw   = 0.255f;        // below SeaLevelRaw, so it floods

    /// <summary>Where each tier pad drops you: mid-basin of that tier, due north (deepest land).</summary>
    static readonly float[] ArrivalRadius =
        { 206f / 1254f, 396f / 1254f, 564f / 1254f, 732f / 1254f, 863f / 1254f };
    const float ArrivalBearing = 0f;

    // ── terrain layers ──────────────────────────────────────────────────────
    const string TexDir          = "Assets/TerrainSampleAssets/Textures/Terrain";
    const string LayerDir        = "Assets/Art/Generated3D/WorldTierLayers";
    const string TerrainDataPath = "Assets/Art/Generated3D/MainWorldData.asset";

    const int L_Shore = 0, L_T1 = 1, L_T2 = 2, L_T3 = 3, L_T4 = 4, L_T5 = 5, L_Rock = 6, L_Island = 7;
    const int LayerCount = 8;

    struct LayerDef
    {
        public string Name, Texture;
        public Color Tint;
        public float Tile;
        public LayerDef(string n, string t, Color c, float tile) { Name = n; Texture = t; Tint = c; Tile = tile; }
    }

    static readonly LayerDef[] Layers =
    {
        new LayerDef("00_Shore_Sand",       "Sand",       new Color(0.86f, 0.78f, 0.60f),  8f),
        new LayerDef("01_T1_Greenbelt",     "Grass_A",    new Color(0.48f, 0.60f, 0.30f),  9f),
        new LayerDef("02_T2_BarrenPlains",  "Grass_Dry",  new Color(0.88f, 0.65f, 0.22f), 10f),
        new LayerDef("03_T3_ScorchedHighs", "Muddy",      new Color(0.34f, 0.24f, 0.18f), 10f),
        new LayerDef("04_T4_FrozenWastes",  "Snow",       new Color(0.80f, 0.88f, 0.95f), 11f),
        new LayerDef("05_T5_Wasteland",     "Rock",       new Color(0.66f, 0.28f, 0.17f), 11f),
        new LayerDef("06_Wall_Rock",        "Rock",       new Color(0.24f, 0.24f, 0.27f), 12f),
        new LayerDef("07_Island_Grass",     "Grass_Moss", new Color(0.52f, 0.68f, 0.34f),  6f),
    };

    static readonly string[] TierNames =
        { "Tutorial Island", "Greenbelt", "Barren Plains", "Scorched Highlands", "Frozen Wastes", "Wasteland" };

    // ════════════════════════════════════════════════════════════════════════
    //  MENU
    // ════════════════════════════════════════════════════════════════════════

    // RETIRED 2026-07-29. The scene's terrain was already correctly heightmapped from
    // worldterrainlayout.png by an earlier tool, and running this overwrote it with a worse
    // resample. Moved under Archived so it can't be fired by accident. Use step 0 (Probe) to
    // measure the existing terrain, then paint biomes onto it — do not re-stamp heights.
    [MenuItem("Wasteland/Archived/DANGER · Rebuild Terrain From Heightmap (overwrites)", false, 9000)]
    public static void BuildTerrain()
    {
        var terrain = FindGroundTerrain(out string why);
        if (terrain == null) { EditorUtility.DisplayDialog("No Terrain", why, "OK"); return; }

        var src = LoadHeightmapReadable(out string err);
        if (src == null) { EditorUtility.DisplayDialog("No Heightmap", err, "OK"); return; }

        if (!EditorUtility.DisplayDialog("Build Terrain From Heightmap",
            $"Source:  {HeightmapPath}  ({src.width}×{src.height})\n" +
            $"Target:  {AssetDatabase.GetAssetPath(terrain.terrainData)}\n\n" +
            "This OVERWRITES the heightmap and the terrain layers. That TerrainData is shared " +
            "by MainWorld3D and dontfuckindelete, so BOTH scenes change.\n\n" +
            "A timestamped backup goes to <project>/TerrainBackups/ first.\n" +
            "Placed objects are NOT moved — run step 3 afterwards to re-seat them.\n\n" +
            "Continue?", "Build", "Cancel")) return;

        string backup = BackupTerrainData(terrain.terrainData);
        var log = new System.Text.StringBuilder("=== Build Terrain From Heightmap ===\n");
        if (backup != null) log.AppendLine($"Backup: {backup}");

        try
        {
            EditorUtility.DisplayProgressBar("World Tiers", "Preparing…", 0.05f);
            var td = terrain.terrainData;
            td.size = new Vector3(td.size.x, TerrainHeightMetres, td.size.z);

            EditorUtility.DisplayProgressBar("World Tiers", "Sampling heightmap…", 0.15f);
            StampHeightmap(terrain, src, log);

            EditorUtility.DisplayProgressBar("World Tiers", "Building terrain layers…", 0.60f);
            AssignLayers(td, log);

            EditorUtility.DisplayProgressBar("World Tiers", "Painting tier biomes…", 0.70f);
            PaintSplat(terrain, log);

            EditorUtility.DisplayProgressBar("World Tiers", "Assigning URP terrain material…", 0.95f);
            AssignUrpTerrainMaterial(terrain, log);

            terrain.Flush();
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
        }
        finally { EditorUtility.ClearProgressBar(); }

        float seaY = SeaLevelRaw * TerrainHeightMetres;
        log.AppendLine($"\nSea level: y = {seaY:0.0} m — put your water plane there.");
        log.AppendLine("Next: step 2 (gates), step 3 (re-seat objects). Then save the scene.");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("World Tiers — Terrain Built",
            $"Built from {Path.GetFileName(HeightmapPath)}.\n\n" +
            $"Water plane goes at y = {seaY:0.0} m.\n\n" +
            "See the Console for the full report. Save the scene (Ctrl+S).", "OK");
    }

    [MenuItem("Wasteland/World Tiers/2 · Place Tier Gates In Passes", priority = 1)]
    public static void PlaceGatesMenu()
    {
        var terrain = FindGroundTerrain(out string why);
        if (terrain == null) { EditorUtility.DisplayDialog("No Terrain", why, "OK"); return; }

        var log = new System.Text.StringBuilder("=== Place Tier Gates ===\n");
        int placed = BuildGates(terrain, log);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("World Tiers — Gates",
            $"{placed} gate(s) placed in the wall passes.\nSee Console for details.\n\nSave the scene (Ctrl+S).", "OK");
    }

    [MenuItem("Wasteland/World Tiers/3 · Re-seat Placed Objects On Terrain", priority = 2)]
    public static void ReseatObjects()
    {
        var terrain = FindGroundTerrain(out string why);
        if (terrain == null) { EditorUtility.DisplayDialog("No Terrain", why, "OK"); return; }

        if (!EditorUtility.DisplayDialog("Re-seat Objects",
            "Moves the Player, WorldContent markers and every CombatTarget vertically onto the " +
            "new terrain surface. X/Z are unchanged. Undoable.\n\nContinue?", "Re-seat", "Cancel")) return;

        int n = 0;
        var player = Object.FindAnyObjectByType<Player3DController>();
        if (player != null) { SnapY(player.transform, terrain, 0.4f); n++; }

        var content = GameObject.Find("WorldContent");
        if (content != null)
            foreach (Transform group in content.transform)
                foreach (Transform marker in group) { SnapY(marker, terrain, 0.6f); n++; }

        foreach (var ct in Object.FindObjectsByType<CombatTarget>())
        { SnapY(ct.transform, terrain, 1f); n++; }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[World Tiers] Re-seated {n} object(s).");
        EditorUtility.DisplayDialog("World Tiers", $"Re-seated {n} object(s).\n\nSave the scene (Ctrl+S).", "OK");
    }

    [MenuItem("Wasteland/World Tiers/4 · Build Tier Waypoint Hub", priority = 3)]
    public static void BuildWaypointHubMenu()
    {
        var terrain = FindGroundTerrain(out string why);
        if (terrain == null) { EditorUtility.DisplayDialog("No Terrain", why, "OK"); return; }

        var log = new System.Text.StringBuilder("=== Build Tier Waypoint Hub ===\n");
        int pads = BuildWaypointHub(terrain, log);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("World Tiers — Waypoint Hub",
            $"{pads} pad(s) built on the island at world (-180, -145).\n\n" +
            "Tier pads unlock as you open each TierGate; the two access pads unlock once you " +
            "first reach Tier 2.\n\nSee Console for details. Save the scene (Ctrl+S).", "OK");
    }

    [MenuItem("Wasteland/World Tiers/Report Layout Measurements", priority = 20)]
    public static void ReportLayout()
    {
        var terrain = FindGroundTerrain(out string why);
        if (terrain == null) { EditorUtility.DisplayDialog("No Terrain", why, "OK"); return; }

        var td = terrain.terrainData;
        Vector3 c = RingCenter(terrain);
        float w = td.size.x;

        var sb = new System.Text.StringBuilder("=== World Tier Layout ===\n");
        sb.AppendLine($"Terrain      {td.size.x}×{td.size.z} m, height {td.size.y} m, origin {terrain.transform.position}");
        sb.AppendLine($"Ring centre  {c}   (image px 636,942)");
        sb.AppendLine($"Sea level    y = {SeaLevelRaw * td.size.y:0.0} m");
        sb.AppendLine($"Island       r < {IslandR * w:0} m,  lagoon to {LagoonR * w:0} m");
        for (int i = 0; i < WallR.Length; i++)
            sb.AppendLine($"Wall T{i + 1}→T{i + 2}   r = {WallR[i] * w:0} m   passes at " +
                          string.Join(", ", System.Array.ConvertAll(PassBearings[i], b => $"{b:0}°")));
        Debug.Log(sb.ToString());
    }

    [MenuItem("Wasteland/World Tiers/Restore Terrain Data From Backup…", priority = 21)]
    public static void RestoreBackup()
    {
        if (!Directory.Exists(BackupDir)) { EditorUtility.DisplayDialog("No Backups", $"Nothing in {BackupDir}", "OK"); return; }

        string picked = EditorUtility.OpenFilePanel("Pick a TerrainData backup", BackupDir, "bak");
        if (string.IsNullOrEmpty(picked)) return;

        if (!EditorUtility.DisplayDialog("Restore Terrain",
            $"Overwrite\n  {TerrainDataPath}\nwith\n  {Path.GetFileName(picked)}?\n\n" +
            "This reverts the terrain in BOTH scenes.", "Restore", "Cancel")) return;

        File.Copy(picked, Path.GetFullPath(Path.Combine(Application.dataPath, "..", TerrainDataPath)), true);
        AssetDatabase.ImportAsset(TerrainDataPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[World Tiers] Restored terrain from {picked}");
        EditorUtility.DisplayDialog("World Tiers", "Terrain restored.", "OK");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HEIGHTMAP IMPORT
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Force the source PNG to raw/readable so its values can be sampled as elevation.</summary>
    static Texture2D LoadHeightmapReadable(out string err)
    {
        err = null;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(HeightmapPath);
        if (tex == null) { err = $"Could not find {HeightmapPath}."; return null; }

        var imp = AssetImporter.GetAtPath(HeightmapPath) as TextureImporter;
        if (imp == null) { err = $"{HeightmapPath} has no TextureImporter."; return null; }

        bool dirty = false;
        if (!imp.isReadable)                                     { imp.isReadable = true; dirty = true; }
        if (imp.sRGBTexture)                                     { imp.sRGBTexture = false; dirty = true; }   // raw data, not colour
        if (imp.mipmapEnabled)                                   { imp.mipmapEnabled = false; dirty = true; }
        if (imp.textureCompression != TextureImporterCompression.Uncompressed)
        { imp.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
        if (imp.maxTextureSize < 2048)                           { imp.maxTextureSize = 2048; dirty = true; }
        if (imp.filterMode != FilterMode.Bilinear)               { imp.filterMode = FilterMode.Bilinear; dirty = true; }
        if (imp.wrapMode != TextureWrapMode.Clamp)               { imp.wrapMode = TextureWrapMode.Clamp; dirty = true; }

        if (dirty)
        {
            imp.SaveAndReimport();
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(HeightmapPath);
        }
        return tex;
    }

    /// <summary>Elevation from the red-encoded source: the brightest channel, as in the analysis.</summary>
    static float Raw(Texture2D t, float u, float v)
    {
        Color c = t.GetPixelBilinear(u, v);
        return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
    }

    static void StampHeightmap(Terrain terrain, Texture2D src, System.Text.StringBuilder log)
    {
        var td  = terrain.terrainData;
        int res = td.heightmapResolution;

        var heights = new float[res, res];
        float inv = 1f / (res - 1);
        float step = inv / Supersample;          // box-filter footprint per texel
        float ss   = Supersample * Supersample;

        // Wall/pass geometry works in normalised image space, so radii stay comparable
        // to the measured pixel radii regardless of terrain or heightmap resolution.
        float aspect = td.size.z / td.size.x;

        // A pass's saddle target depends only on (wall, bearing), never on the texel —
        // so resolve the basin floors on either side of every cut once, up front.
        var passInner = new float[WallR.Length][];
        var passOuter = new float[WallR.Length][];
        for (int w = 0; w < WallR.Length; w++)
        {
            passInner[w] = new float[PassBearings[w].Length];
            passOuter[w] = new float[PassBearings[w].Length];
            for (int p = 0; p < PassBearings[w].Length; p++)
            {
                float rad = PassBearings[w][p] * Mathf.Deg2Rad;
                float su = Mathf.Sin(rad), sv = Mathf.Cos(rad);
                passInner[w][p] = BasinRaw(src, WallR[w] - PassRadialFrac, su, sv, aspect);
                passOuter[w][p] = BasinRaw(src, WallR[w] + PassRadialFrac, su, sv, aspect);
            }
        }

        int cut = 0;
        for (int zi = 0; zi < res; zi++)
        {
            float v0 = zi * inv;
            for (int xi = 0; xi < res; xi++)
            {
                float u0 = xi * inv;

                // box filter — the source is 1254², the heightmap is usually 513²
                float sum = 0f;
                for (int sy = 0; sy < Supersample; sy++)
                    for (int sx = 0; sx < Supersample; sx++)
                        sum += Raw(src, u0 + (sx + 0.5f) * step - inv * 0.5f,
                                        v0 + (sy + 0.5f) * step - inv * 0.5f);
                float h = sum / ss;

                // ── cut the passes ──────────────────────────────────────────
                float du = u0 - CenterU;
                float dv = (v0 - CenterV) * aspect;
                float r  = Mathf.Sqrt(du * du + dv * dv);
                float bearing = Mathf.Atan2(du, dv) * Mathf.Rad2Deg;

                for (int w = 0; w < WallR.Length; w++)
                {
                    float dr = Mathf.Abs(r - WallR[w]);
                    if (dr > PassRadialFrac) continue;

                    for (int p = 0; p < PassBearings[w].Length; p++)
                    {
                        float ad = Mathf.Abs(Mathf.DeltaAngle(bearing, PassBearings[w][p]));
                        if (ad > PassHalfDeg + PassFeatherDeg) continue;

                        float kA = Mathf.SmoothStep(0f, 1f,
                                   Mathf.InverseLerp(PassHalfDeg + PassFeatherDeg, PassHalfDeg, ad));
                        float kR = Mathf.SmoothStep(0f, 1f,
                                   Mathf.InverseLerp(PassRadialFrac, PassRadialFrac * 0.45f, dr));
                        float k  = kA * kR;
                        if (k <= 0f) continue;

                        // Ramp from the inner basin up to the outer basin across the cut,
                        // so the pass is a walkable saddle rather than a hole in the wall.
                        float t = Mathf.InverseLerp(WallR[w] - PassRadialFrac, WallR[w] + PassRadialFrac, r);
                        float target = Mathf.Lerp(passInner[w][p], passOuter[w][p],
                                                  Mathf.SmoothStep(0f, 1f, t));

                        if (target < h) { h = Mathf.Lerp(h, target, k); cut++; }
                    }
                }

                // ── flood the neck so the waypoint island is genuinely detached ──
                float dn = Mathf.Sqrt(Sq(u0 - NeckU) + Sq((v0 - NeckV) * aspect));
                if (dn < NeckRadius)
                {
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NeckRadius, NeckRadius * 0.35f, dn));
                    if (NeckLevelRaw < h) h = Mathf.Lerp(h, NeckLevelRaw, k);
                }

                // ── flatten the hub plaza on the island ─────────────────────
                float dp = Mathf.Sqrt(Sq(u0 - IslandCenterU) + Sq((v0 - IslandCenterV) * aspect));
                if (dp < PlazaRadius)
                {
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PlazaRadius, PlazaRadius * 0.55f, dp));
                    h = Mathf.Lerp(h, PlazaLevelRaw, k);
                }

                heights[zi, xi] = Mathf.Clamp01(h);
            }
        }

        td.SetHeights(0, 0, heights);
        log.AppendLine($"✅  Heightmap sampled from {Path.GetFileName(HeightmapPath)} " +
                       $"({src.width}×{src.height}) into {res}×{res}, {Supersample}×{Supersample} box filter.");
        log.AppendLine($"✅  Cut {PassCount()} passes through {WallR.Length} walls ({cut} texels lowered).");
        log.AppendLine($"✅  Flooded the island neck and flattened the hub plaza " +
                       $"(r={PlazaRadius * td.size.x:0} m at y={PlazaLevelRaw * td.size.y:0.0} m).");
    }

    /// <summary>Average source value on a small arc at a given radius — the basin floor next to a pass.</summary>
    static float BasinRaw(Texture2D src, float radius, float sinB, float cosB, float aspect)
    {
        float acc = 0f; int n = 0;
        for (int i = -2; i <= 2; i++)
        {
            float a = Mathf.Atan2(sinB, cosB) + i * 3f * Mathf.Deg2Rad;
            float u = CenterU + Mathf.Sin(a) * radius;
            float v = CenterV + Mathf.Cos(a) * radius / aspect;
            acc += Raw(src, u, v); n++;
        }
        return acc / n;
    }

    static int PassCount()
    {
        int n = 0;
        foreach (var arr in PassBearings) n += arr.Length;
        return n;
    }

    static float Sq(float x) => x * x;

    // ════════════════════════════════════════════════════════════════════════
    //  BIOME PAINT
    // ════════════════════════════════════════════════════════════════════════

    static void PaintSplat(Terrain terrain, System.Text.StringBuilder log)
    {
        var td  = terrain.terrainData;
        int res = td.alphamapResolution;
        float aspect = td.size.z / td.size.x;

        var splat = new float[res, res, LayerCount];
        var area  = new float[6];
        int ocean = 0;

        for (int zi = 0; zi < res; zi++)
        {
            float v = (zi + 0.5f) / res;
            for (int xi = 0; xi < res; xi++)
            {
                float u = (xi + 0.5f) / res;

                float du = u - CenterU;
                float dv = (v - CenterV) * aspect;
                float r  = Mathf.Sqrt(du * du + dv * dv);

                int tier = TierAt(r);
                float h  = td.GetInterpolatedHeight(u, v) / td.size.y;   // normalised surface height
                float slope = td.GetSteepness(u, v);

                var w = new float[LayerCount];
                int baseLayer = tier switch
                {
                    0 => r < IslandR ? L_Island : L_Shore,
                    1 => L_T1, 2 => L_T2, 3 => L_T3, 4 => L_T4, _ => L_T5,
                };
                w[baseLayer] = 1f;
                area[Mathf.Clamp(tier, 0, 5)]++;

                // beach / lakebed wherever the surface sits near the waterline
                float shore = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(SeaLevelRaw + 0.030f, SeaLevelRaw - 0.010f, h));
                if (h < SeaLevelRaw) ocean++;

                // the mountain walls and any steep face are bare rock
                float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(26f, 48f, slope));

                for (int l = 0; l < LayerCount; l++) w[l] *= (1f - shore) * (1f - rock);
                w[L_Shore] += shore * (1f - rock);
                w[L_Rock]  += rock;

                float sum = 0f;
                for (int l = 0; l < LayerCount; l++) sum += w[l];
                if (sum <= 0.0001f) { splat[zi, xi, baseLayer] = 1f; continue; }
                for (int l = 0; l < LayerCount; l++) splat[zi, xi, l] = w[l] / sum;
            }
        }

        td.SetAlphamaps(0, 0, splat);

        float total = res * (float)res;
        log.AppendLine($"✅  Biomes painted {res}×{res} across {LayerCount} layers.");
        for (int t = 0; t <= 5; t++)
            log.AppendLine($"      {TierNames[t],-20} {area[t] / total * 100f,5:0.0}% of the map");
        log.AppendLine($"      {"below waterline",-20} {ocean / total * 100f,5:0.0}%");
    }

    static int TierAt(float r)
    {
        if (r < LagoonR) return 0;
        for (int i = 0; i < WallR.Length; i++)
            if (r < WallR[i]) return i + 1;
        return 5;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TERRAIN LAYERS
    // ════════════════════════════════════════════════════════════════════════

    static void AssignLayers(TerrainData td, System.Text.StringBuilder log)
    {
        EnsureFolder(LayerDir);

        var made = new List<TerrainLayer>(LayerCount);
        var missing = new List<string>();

        foreach (var def in Layers)
        {
            string path = $"{LayerDir}/{def.Name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{def.Texture}_BaseColor.tif");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{def.Texture}_Normal.tif");
            var mask   = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{def.Texture}_MaskMap.tif");
            if (albedo == null) missing.Add(def.Texture);

            layer.name             = def.Name;
            layer.diffuseTexture   = albedo;
            layer.normalMapTexture = normal;
            layer.maskMapTexture   = mask;
            layer.normalScale      = 0.8f;
            layer.tileSize         = new Vector2(def.Tile, def.Tile);
            layer.tileOffset       = Vector2.zero;
            layer.metallic         = 0f;
            layer.smoothness       = 0f;
            // tint the source texture toward the concept-art colour for this tier
            layer.diffuseRemapMin  = Color.black;
            layer.diffuseRemapMax  = new Color(def.Tint.r, def.Tint.g, def.Tint.b, 1f);

            EditorUtility.SetDirty(layer);
            made.Add(layer);
        }

        td.terrainLayers = made.ToArray();
        AssetDatabase.SaveAssets();

        log.AppendLine($"✅  Assigned {made.Count} terrain layers in {LayerDir}.");
        if (missing.Count > 0)
            log.AppendLine($"⚠  Missing source textures: {string.Join(", ", missing)}");
    }

    static void AssignUrpTerrainMaterial(Terrain terrain, System.Text.StringBuilder log)
    {
        var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        if (shader == null)
        {
            log.AppendLine("⚠  URP Terrain/Lit shader not found — left the existing terrain material alone.");
            return;
        }

        const string matPath = LayerDir + "/URP_WorldTier_Terrain.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
        mat.shader = shader;
        EditorUtility.SetDirty(mat);
        terrain.materialTemplate = mat;
        log.AppendLine("✅  URP Terrain/Lit material assigned.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GATES
    // ════════════════════════════════════════════════════════════════════════

    static int BuildGates(Terrain terrain, System.Text.StringBuilder log)
    {
        var td = terrain.terrainData;
        Vector3 c = RingCenter(terrain);
        float w = td.size.x;

        var root = GameObject.Find("WorldTierGates");
        if (root == null)
        {
            root = new GameObject("WorldTierGates");
            Undo.RegisterCreatedObjectUndo(root, "Create WorldTierGates");
        }

        int placed = 0, skipped = 0;

        for (int i = 0; i < WallR.Length; i++)
        {
            int targetTier = i + 2;
            float radius = WallR[i] * w;

            foreach (float bearing in PassBearings[i])
            {
                string label = Compass(bearing);
                string name  = $"TierGate_T{targetTier}_{label}";
                if (root.transform.Find(name) != null) { skipped++; continue; }

                float rad = bearing * Mathf.Deg2Rad;
                Vector3 outward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 pos = c + outward * radius;
                pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;

                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Place TierGate");
                go.transform.SetParent(root.transform, true);
                go.transform.position = pos + Vector3.up * 3f;
                go.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);

                // Span the full cut so the player cannot slip along the wall face.
                float passWidth = 2f * Mathf.Tan((PassHalfDeg + PassFeatherDeg) * Mathf.Deg2Rad) * radius;
                passWidth = Mathf.Max(passWidth, 16f);

                var bc = go.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size      = new Vector3(passWidth, 16f, 3f);
                bc.center    = Vector3.zero;

                // Boss kill is the only gate — assign Required Boss on the gate to arm it.
                var gate = go.AddComponent<TierGate>();
                gate.targetTier        = targetTier;
                gate.gateId            = $"t{targetTier}_{label.ToLower()}";
                gate.bossAliveMessage  = LockedText(targetTier);
                gate.unlockMessage     = UnlockText(targetTier);
                gate.fogVisual         = BuildFogWall(go, gate.gateId, targetTier, passWidth);

                placed++;
                log.AppendLine($"      {name,-22} r={radius,6:0} m  bearing {bearing,6:0}°  " +
                               $"width {passWidth,4:0} m  — assign this tier's boss to arm it");
            }
        }

        log.AppendLine($"✅  {placed} gate(s) placed" +
                       (skipped > 0 ? $", {skipped} already existed and were skipped." : "."));
        return placed;
    }

    static GameObject BuildFogWall(GameObject gateGO, string gateId, int tier, float width)
    {
        var fog = new GameObject("FogWall");
        Undo.RegisterCreatedObjectUndo(fog, "Place FogWall");
        fog.transform.SetParent(gateGO.transform, false);
        fog.transform.localPosition = Vector3.zero;

        var fw = fog.AddComponent<FogWall>();
        fw.id = gateId + "_fog";

        var ps   = fog.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = 4f;
        main.startSpeed      = 0.25f;
        main.startSize       = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startColor      = FogColor(tier);
        main.maxParticles    = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission; em.rateOverTime = 24f;
        var sh = ps.shape;
        sh.enabled   = true;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale     = new Vector3(width, 14f, 2f);

        ps.GetComponent<ParticleSystemRenderer>().material = FogMaterial(tier);
        return fog;
    }

    static Color FogColor(int tier) => tier switch
    {
        2 => new Color(0.72f, 0.62f, 0.30f, 0.38f),   // dust off the Barren Plains
        3 => new Color(0.42f, 0.26f, 0.20f, 0.44f),   // scorched smoke
        4 => new Color(0.74f, 0.84f, 0.92f, 0.42f),   // ice haze
        5 => new Color(0.66f, 0.24f, 0.15f, 0.48f),   // volcanic ash
        _ => new Color(0.55f, 0.65f, 0.45f, 0.35f),
    };

    static Material FogMaterial(int tier)
    {
        EnsureFolder(LayerDir);
        string path = $"{LayerDir}/FogWall_T{tier}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Particles/Standard Unlit")
                  ?? Shader.Find("Sprites/Default");
        mat = new Material(shader) { name = $"FogWall_T{tier}" };
        Color col = FogColor(tier);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", col);
        if (mat.HasProperty("_Surface"))   mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend"))     mat.SetFloat("_Blend", 0f);
        mat.renderQueue = 3000;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static string LockedText(int tier) => tier switch
    {
        2 => "The grass thins ahead. Past this wall the Greenbelt gives way to the Barren Plains — dust, heat and worse. Train harder.",
        3 => "Smoke rises beyond the pass. The Scorched Highlands take the unprepared first. Turn back and get stronger.",
        4 => "An icy wind funnels down through the gap. The Frozen Wastes bury their dead standing. Not yet.",
        5 => "The rock itself is bleeding light. The Wasteland is what broke the world — and it is still hungry.",
        _ => "You feel unprepared for what lies ahead.",
    };

    static string UnlockText(int tier) => tier switch
    {
        2 => "The pass opens. The Barren Plains stretch out ahead — stay sharp, survivor.",
        3 => "The smoke parts. The Scorched Highlands accept those hard enough to climb.",
        4 => "The wind drops. The Frozen Wastes lie open, and the old world's secrets with them.",
        5 => "The ground stops shaking. The Wasteland has been waiting for you.",
        _ => "The way is clear.",
    };

    static string Compass(float deg)
    {
        string[] pts = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
                         "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
        return pts[Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 22.5f) % 16];
    }

    // ════════════════════════════════════════════════════════════════════════
    //  WAYPOINT HUB
    //
    //  The detached island west of Tutorial Island becomes the shortcut network:
    //  one pad per tier, each locked behind that tier's "tier_reached_<N>" flag, plus
    //  two access pads (Tutorial Island + Greenbelt) that unlock on first reaching Tier 2.
    // ════════════════════════════════════════════════════════════════════════

    static int BuildWaypointHub(Terrain terrain, System.Text.StringBuilder log)
    {
        var td = terrain.terrainData;
        var tp = terrain.transform.position;
        float w = td.size.x;

        var root = GameObject.Find("WorldTierWaypoints");
        if (root == null)
        {
            root = new GameObject("WorldTierWaypoints");
            Undo.RegisterCreatedObjectUndo(root, "Create WorldTierWaypoints");
        }

        var destRoot = FindOrCreateChild(root.transform, "Destinations");
        var hubRoot  = FindOrCreateChild(root.transform, "Hub_Island");
        var accRoot  = FindOrCreateChild(root.transform, "AccessPads");

        Vector3 hubCenter = new Vector3(tp.x + IslandCenterU * td.size.x, 0f,
                                        tp.z + IslandCenterV * td.size.z);
        hubCenter.y = terrain.SampleHeight(hubCenter) + tp.y;

        Vector3 ringCenter = RingCenter(terrain);
        int pads = 0;

        // ── destination markers: mid-basin of each tier, plus the hub and Tutorial Island ──
        var tierDest = new Transform[5];
        for (int t = 0; t < 5; t++)
        {
            float rad = ArrivalBearing * Mathf.Deg2Rad;
            Vector3 p = ringCenter + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * (ArrivalRadius[t] * w);
            p.y = terrain.SampleHeight(p) + tp.y + 1f;
            tierDest[t] = MakeMarker(destRoot, $"Arrive_T{t + 1}_{TierNames[t + 1].Replace(" ", "")}", p);
            log.AppendLine($"      arrival T{t + 1,-2} {TierNames[t + 1],-20} at {p}");
        }

        Vector3 hubArrive = hubCenter + new Vector3(0f, 1f, -10f);
        hubArrive.y = terrain.SampleHeight(hubArrive) + tp.y + 1f;
        var hubDest = MakeMarker(destRoot, "Arrive_WaypointIsland", hubArrive);

        Vector3 tut = new Vector3(tp.x + CenterU * td.size.x, 0f, tp.z + CenterV * td.size.z);
        tut.y = terrain.SampleHeight(tut) + tp.y + 1f;
        var tutDest = MakeMarker(destRoot, "Arrive_TutorialIsland", tut);

        // ── the five tier pads, arranged in an arc around the plaza ──────────
        for (int t = 0; t < 5; t++)
        {
            int tier = t + 1;
            // spread across 200° so the arc opens toward Tutorial Island (east)
            float a = Mathf.Lerp(-100f, 100f, t / 4f) * Mathf.Deg2Rad;
            Vector3 p = hubCenter + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 16f;
            p.y = terrain.SampleHeight(p) + tp.y;

            // Tier 1 is always available — it's where you came from.
            string flag = tier == 1 ? "" : $"tier_reached_{tier}";
            if (MakePad(hubRoot, $"Pad_T{tier}_{TierNames[tier].Replace(" ", "")}", p,
                        tierDest[t], TierNames[tier], flag, TierPadColor(tier),
                        $"That region is still sealed to you. Open the Tier {tier} gate on foot first.") != null)
            { pads++; log.AppendLine($"      pad  T{tier,-2} {TierNames[tier],-20} flag '{(flag == "" ? "<always open>" : flag)}'"); }
        }

        // ── return pad on the hub, back to Tutorial Island ───────────────────
        Vector3 rp = hubCenter + new Vector3(0f, 0f, -18f);
        rp.y = terrain.SampleHeight(rp) + tp.y;
        if (MakePad(hubRoot, "Pad_Return_TutorialIsland", rp, tutDest, "Tutorial Island",
                    "", new Color(0.55f, 0.85f, 1f), "") != null)
        { pads++; log.AppendLine("      pad  return to Tutorial Island (always open)"); }

        // ── access pads: Tutorial Island + Greenbelt → the hub ───────────────
        Vector3 ap1 = tut + new Vector3(-14f, 0f, 0f);
        ap1.y = terrain.SampleHeight(ap1) + tp.y;
        if (MakePad(accRoot, "Pad_Access_TutorialIsland", ap1, hubDest, "the Waypoint Island",
                    "tier_reached_2", new Color(0.75f, 0.6f, 1f),
                    "The pad is dark. It stirs only for those who have crossed into the Barren Plains.") != null)
        { pads++; log.AppendLine("      pad  access from Tutorial Island (needs tier_reached_2)"); }

        Vector3 ap2 = tierDest[0].position + new Vector3(10f, 0f, 0f);
        ap2.y = terrain.SampleHeight(ap2) + tp.y;
        if (MakePad(accRoot, "Pad_Access_Greenbelt", ap2, hubDest, "the Waypoint Island",
                    "tier_reached_2", new Color(0.75f, 0.6f, 1f),
                    "The pad is dark. It stirs only for those who have crossed into the Barren Plains.") != null)
        { pads++; log.AppendLine("      pad  access from Greenbelt (needs tier_reached_2)"); }

        log.AppendLine($"✅  {pads} pad(s) built. Hub plaza centre {hubCenter}.");
        log.AppendLine("      Tier pads unlock via TierGate → 'tier_reached_<N>'.");
        return pads;
    }

    static Transform FindOrCreateChild(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Transform MakeMarker(Transform parent, string name, Vector3 pos)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, true);
            t = go.transform;
        }
        t.position = pos;
        return t;
    }

    /// <summary>A disc pad with a trigger, a lit ring when usable and a dim one when locked.</summary>
    static GameObject MakePad(Transform parent, string name, Vector3 pos, Transform dest,
                              string destName, string requiredFlag, Color tint, string lockedMsg)
    {
        if (parent.Find(name) != null) return null;   // idempotent

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, true);
        go.transform.position = pos;

        var trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size      = new Vector3(4.5f, 4f, 4.5f);
        trigger.center    = new Vector3(0f, 2f, 0f);

        var unlocked = MakeDisc(go.transform, "Unlocked", tint, 1f);
        var locked   = MakeDisc(go.transform, "Locked", new Color(0.28f, 0.28f, 0.32f), 0f);

        var pad = go.AddComponent<TierWaypointPad>();
        pad.destination     = dest;
        pad.destinationName = destName;
        pad.requiredFlag    = requiredFlag;
        pad.unlockedVisual  = unlocked;
        pad.lockedVisual    = locked;
        if (!string.IsNullOrEmpty(lockedMsg)) pad.lockedMessage = lockedMsg;

        return go;
    }

    static GameObject MakeDisc(Transform parent, string name, Color color, float emission)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Undo.RegisterCreatedObjectUndo(disc, "Create pad disc");
        disc.name = name;
        disc.transform.SetParent(parent, false);
        disc.transform.localPosition = new Vector3(0f, 0.1f, 0f);
        disc.transform.localScale    = new Vector3(3.4f, 0.1f, 3.4f);
        Object.DestroyImmediate(disc.GetComponent<Collider>());   // the pad's trigger does the work

        var mr = disc.GetComponent<MeshRenderer>();
        mr.sharedMaterial = PadMaterial(color, emission);
        return disc;
    }

    static Material PadMaterial(Color color, float emission)
    {
        EnsureFolder(LayerDir);
        string key = ColorUtility.ToHtmlStringRGB(color) + (emission > 0f ? "_lit" : "_dim");
        string path = $"{LayerDir}/Pad_{key}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { name = $"Pad_{key}" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
        if (emission > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 2.2f);
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Color TierPadColor(int tier) => tier switch
    {
        1 => new Color(0.48f, 0.80f, 0.35f),   // Greenbelt
        2 => new Color(0.92f, 0.72f, 0.28f),   // Barren Plains
        3 => new Color(0.72f, 0.38f, 0.22f),   // Scorched Highlands
        4 => new Color(0.70f, 0.88f, 0.98f),   // Frozen Wastes
        _ => new Color(0.85f, 0.25f, 0.18f),   // Wasteland
    };

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Ring centre in world space — the image's centre pixel mapped onto the terrain.</summary>
    static Vector3 RingCenter(Terrain terrain)
    {
        var tp = terrain.transform.position;
        var sz = terrain.terrainData.size;
        return new Vector3(tp.x + CenterU * sz.x, 0f, tp.z + CenterV * sz.z);
    }

    static Terrain FindGroundTerrain(out string why)
    {
        why = null;
        var ground = GameObject.Find("Ground");
        var t = ground != null ? ground.GetComponent<Terrain>() : null;
        if (t == null) t = Terrain.activeTerrain;
        if (t == null)
        {
            why = "No Terrain in the open scene.\n\nOpen Assets/Scenes/MainWorld3D.unity or " +
                  "Assets/dontfuckindelete.unity first — both use the 'Ground' terrain.";
            return null;
        }
        if (t.terrainData == null) { why = "The Terrain has no TerrainData assigned."; return null; }
        return t;
    }

    static void SnapY(Transform t, Terrain terrain, float lift)
    {
        Vector3 p = t.position;
        p.y = terrain.SampleHeight(p) + terrain.transform.position.y + lift;
        Undo.RecordObject(t, "Re-seat on terrain");
        t.position = p;
    }

    static string BackupDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TerrainBackups"));

    static string BackupTerrainData(TerrainData td)
    {
        string src = AssetDatabase.GetAssetPath(td);
        if (string.IsNullOrEmpty(src)) return null;

        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", src));
        if (!File.Exists(full)) return null;

        Directory.CreateDirectory(BackupDir);
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string dest  = Path.Combine(BackupDir, $"{Path.GetFileNameWithoutExtension(src)}_{stamp}.bak");
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
