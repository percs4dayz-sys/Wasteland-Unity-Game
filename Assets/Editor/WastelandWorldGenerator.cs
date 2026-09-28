using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wasteland RPG ▸ Full World Generator
///
/// Generates a complete 600×600 m terrain from the world layout PNG, sculpting
/// biome-specific heightmaps and painting terrain textures across 6 concentric tiers:
///
///   Tutorial Island  ▸ protected valley, one bridge out
///   Tier 1 Greenbelt ▸ rolling hills, forests, old highways
///   Tier 2 Barren    ▸ badlands, dry riverbeds, quarries
///   Tier 3 Scorched  ▸ volcanic highlands, industrial ruins
///   Tier 4 Frozen    ▸ snow peaks, military bases
///   Tier 5 Core      ▸ impact crater, megacity ruins
///
/// Menu:  Wasteland ▸ Generate Full World (heightmap + biomes)
/// </summary>
public class WastelandWorldGenerator : EditorWindow
{
    const float SIZE      = 600f;
    const float MAX_HEIGHT = 60f;   // taller than MainWorldTerrain to allow craters & peaks
    const int   RES        = 1025;  // higher res for more detail
    const string TERRAIN_DATA_PATH = "Assets/Art/Generated3D/WastelandWorld.asset";
    const string LAYOUT_PATH       = "Assets/Art/world layout.png";

    // ── Tier radii (fraction of half-size, 0=centre, 1=edge) ──
    // Tutorial:         0.00 – 0.08
    // Transition T0→T1: 0.08 – 0.10   (cliff ring + bridge)
    // Tier 1 Greenbelt:  0.10 – 0.26
    // Transition T1→T2: 0.26 – 0.30   (drying land, collapsed highway)
    // Tier 2 Barren:     0.30 – 0.48
    // Transition T2→T3: 0.48 – 0.52   (canyon, dam)
    // Tier 3 Scorched:   0.52 – 0.68
    // Transition T3→T4: 0.68 – 0.72   (mountain range, tunnels)
    // Tier 4 Frozen:     0.72 – 0.86
    // Transition T4→T5: 0.86 – 0.90   (crater rim, broken bridge)
    // Tier 5 Core:       0.90 – 1.00

    bool _paintBiomes = true;
    bool _snapContent = true;
    bool _useLayoutPng = true;

    [MenuItem("Wasteland/Archived/Generate Full World (heightmap + biomes)", false, 9000)]
    public static void Open() => GetWindow<WastelandWorldGenerator>("World Gen").minSize = new Vector2(380, 220);

    void OnGUI()
    {
        EditorGUILayout.LabelField("Wasteland RPG — Full World Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "Creates or rebuilds the 600×600 m terrain with 6 biomes.\n" +
            "Heightmap is sculpted from the world layout PNG + procedural noise.\n" +
            "Each tier has unique terrain features matching the world design.",
            MessageType.Info);
        EditorGUILayout.Space(4);
        _paintBiomes  = EditorGUILayout.Toggle("Paint Biome Textures", _paintBiomes);
        _snapContent  = EditorGUILayout.Toggle("Snap Existing Content", _snapContent);
        _useLayoutPng = EditorGUILayout.Toggle("Use Layout PNG as Guide", _useLayoutPng);
        EditorGUILayout.Space(8);

        if (GUILayout.Button("Generate Full World", GUILayout.Height(36)))
            Generate();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MAIN ENTRY POINT
    // ═══════════════════════════════════════════════════════════════════
    public void Generate()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // 1. Create or reuse terrain
        Terrain terrain = GetOrCreateTerrain();

        // 2. Sculpt heightmap
        EditorUtility.DisplayProgressBar("World Generator", "Sculpting heightmap...", 0.1f);
        SculptHeightmap(terrain.terrainData);
        terrain.Flush();

        // 3. Paint biome textures
        if (_paintBiomes)
        {
            EditorUtility.DisplayProgressBar("World Generator", "Assigning terrain layers...", 0.6f);
            SetupTerrainLayers(terrain.terrainData);
            EditorUtility.DisplayProgressBar("World Generator", "Painting biomes...", 0.7f);
            PaintBiomes(terrain.terrainData);
        }

        // 4. Snap existing content
        if (_snapContent)
        {
            EditorUtility.DisplayProgressBar("World Generator", "Snapping content...", 0.95f);
            SnapContentToTerrain(terrain);
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = terrain.gameObject;

        EditorUtility.DisplayDialog("World Generated",
            "Full wasteland world generated:\n\n" +
            "• Tutorial Island — protected valley + cliff ring + bridge\n" +
            "• Tier 1 Greenbelt — rolling hills, forests\n" +
            "• Tier 2 Barren — badlands, dry riverbeds\n" +
            "• Tier 3 Scorched — volcanic highlands, industrial scars\n" +
            "• Tier 4 Frozen — mountain peaks, plateau\n" +
            "• Tier 5 Core — impact crater, ruined megacity\n\n" +
            "SAVE the scene (Ctrl+S).", "OK");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TERRAIN SETUP
    // ═══════════════════════════════════════════════════════════════════
    Terrain GetOrCreateTerrain()
    {
        // Reuse existing terrain if there is one
        var ground = GameObject.Find("Ground");
        Terrain terrain = ground != null ? ground.GetComponent<Terrain>() : null;

        if (terrain == null)
        {
            if (ground != null) Object.DestroyImmediate(ground);

            if (!AssetDatabase.IsValidFolder("Assets/Art/Generated3D"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art"))
                    AssetDatabase.CreateFolder("Assets", "Art");
                AssetDatabase.CreateFolder("Assets/Art", "Generated3D");
            }

            if (AssetDatabase.LoadAssetAtPath<TerrainData>(TERRAIN_DATA_PATH) != null)
                AssetDatabase.DeleteAsset(TERRAIN_DATA_PATH);

            var data = new TerrainData
            {
                heightmapResolution = RES,
                size = new Vector3(SIZE, MAX_HEIGHT, SIZE),
                alphamapResolution = 1024,
                baseMapResolution  = 1024
            };
            AssetDatabase.CreateAsset(data, TERRAIN_DATA_PATH);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Ground";
            go.transform.position = new Vector3(-SIZE / 2f, 0f, -SIZE / 2f);
            terrain = go.GetComponent<Terrain>();
        }

        terrain.terrainData.size = new Vector3(SIZE, MAX_HEIGHT, SIZE);
        if (terrain.terrainData.heightmapResolution != RES)
            terrain.terrainData.heightmapResolution = RES;

        return terrain;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HEIGHTMAP SCULPTING
    // ═══════════════════════════════════════════════════════════════════
    void SculptHeightmap(TerrainData data)
    {
        int res = data.heightmapResolution;
        var h = new float[res, res];

        // Load layout PNG if available
        Texture2D layout = null;
        if (_useLayoutPng)
        {
            layout = AssetDatabase.LoadAssetAtPath<Texture2D>(LAYOUT_PATH);
            if (layout != null)
            {
                var importer = AssetImporter.GetAtPath(LAYOUT_PATH) as TextureImporter;
                if (importer != null && (!importer.isReadable || importer.textureCompression != TextureImporterCompression.Uncompressed))
                {
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                    layout = AssetDatabase.LoadAssetAtPath<Texture2D>(LAYOUT_PATH);
                }
            }
        }

        float seed = Random.Range(0f, 10000f);

        try
        {
            for (int y = 0; y < res; y++)
            {
                if (y % 64 == 0)
                    EditorUtility.DisplayProgressBar("World Generator", $"Sculpting row {y}/{res}", (float)y / res * 0.5f + 0.1f);

                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / (res - 1);
                    float v = (float)y / (res - 1);
                    h[y, x] = CalculateHeight(u, v, seed, layout);
                }
            }
        }
        finally { }

        data.SetHeights(0, 0, h);
    }

    float CalculateHeight(float u, float v, float seed, Texture2D layout)
    {
        // Distance from centre (0=centre, 1=far corner)
        float dx = u - 0.5f;
        float dy = v - 0.5f;
        float dist = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 0 centre → ~1.0 corners
        dist = Mathf.Clamp01(dist);

        // Angle from centre (0°=East, +CCW)
        float angle = Mathf.Atan2(dy, dx); // radians, -PI..PI

        // ── Determine which tier ──
        int tier = dist < 0.08f ? 0 :
                   dist < 0.26f ? 1 :
                   dist < 0.48f ? 2 :
                   dist < 0.68f ? 3 :
                   dist < 0.86f ? 4 : 5;

        // ── Build height from tier base + biome features ──
        float height = TierBaseHeight(tier, dist);
        height += BiomeFeatures(tier, dist, u, v, angle, seed);
        height += TransitionFeatures(dist, u, v, angle, seed);

        // ── Layout PNG: color-aware interpretation ──
        if (layout != null && _useLayoutPng)
        {
            Color c = layout.GetPixelBilinear(u, v);

            // --- Water detection (blue-dominant pixels) ---
            // Water = high blue relative to red+green
            float blueness = c.b - (c.r + c.g) * 0.5f;
            if (blueness > 0.15f)
            {
                // Water bodies → carve depressions (rivers/lakes)
                float waterDepth = Mathf.Clamp01((blueness - 0.15f) / 0.7f);
                height -= waterDepth * 0.08f;
            }

            // --- Road/path detection (dark pixels) ---
            // Roads are dark lines — low brightness, not blue
            float brightness = (c.r + c.g + c.b) / 3f;
            if (brightness < 0.25f && blueness < 0.1f)
            {
                // Dark lines → slight path indentation
                float roadDepth = Mathf.Clamp01((0.25f - brightness) / 0.25f);
                height -= roadDepth * 0.02f;
            }

            // --- Zone color height modulation ---
            // Red-ish areas (badlands/scorched) → slightly raised
            float redness = c.r - (c.g + c.b) * 0.5f;
            if (redness > 0.1f)
                height += Mathf.Clamp01((redness - 0.1f) / 0.6f) * 0.03f;

            // Green areas (lush/forest) → slightly lowered (valleys)
            float greenness = c.g - (c.r + c.b) * 0.5f;
            if (greenness > 0.05f)
                height -= Mathf.Clamp01((greenness - 0.05f) / 0.5f) * 0.02f;

            // White/light areas (snow/peaks) → raised
            if (brightness > 0.85f)
                height += Mathf.Clamp01((brightness - 0.85f) / 0.15f) * 0.05f;

            // General brightness as subtle height guide
            float layoutPush = (brightness - 0.5f) * 0.2f;
            height += layoutPush;
        }

        // Global micro-detail noise
        float micro = (Mathf.PerlinNoise(u * 30f + seed, v * 30f + seed) - 0.5f) * 0.015f;
        height += micro;

        // Edge fade to zero
        float edgeFade = Mathf.Clamp01((0.99f - dist) * 100f);

        return Mathf.Clamp01(height * edgeFade + 0.001f);
    }

    /// <summary>Base plateau height for each tier (0–1).</summary>
    float TierBaseHeight(int tier, float dist)
    {
        // Tier boundaries for interpolation
        float[] tierStarts  = { 0.00f, 0.08f, 0.26f, 0.48f, 0.68f, 0.86f };
        float[] tierHeights = { 0.10f, 0.18f, 0.35f, 0.55f, 0.75f, 0.88f };

        // Smoothly interpolate to the next tier's base
        int nextTier = Mathf.Min(tier + 1, 5);
        float t = Mathf.InverseLerp(tierStarts[tier], tierStarts[nextTier], dist);
        t = Mathf.SmoothStep(0f, 1f, t);

        return Mathf.Lerp(tierHeights[tier], tierHeights[nextTier], t);
    }

    /// <summary>Biome-specific terrain features (noise, shapes).</summary>
    float BiomeFeatures(int tier, float dist, float u, float v, float angle, float seed)
    {
        switch (tier)
        {
            case 0: return TutorialIsland(dist, u, v, angle, seed);
            case 1: return GreenbeltFeatures(dist, u, v, angle, seed);
            case 2: return BarrenFeatures(dist, u, v, angle, seed);
            case 3: return ScorchedFeatures(dist, u, v, angle, seed);
            case 4: return FrozenFeatures(dist, u, v, angle, seed);
            case 5: return WastelandCore(dist, u, v, angle, seed);
            default: return 0f;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  TIER 0 — TUTORIAL ISLAND
    // ─────────────────────────────────────────────────────────────────
    float TutorialIsland(float dist, float u, float v, float angle, float seed)
    {
        float h = 0f;

        // Slightly sunken centre (protected valley feel)
        float centreDip = Mathf.Clamp01(1f - dist / 0.08f); // 1 at centre, 0 at 0.08
        h -= centreDip * 0.04f;

        // Gentle rolling hills inside the valley
        float hills = Mathf.PerlinNoise(u * 8f + seed, v * 8f + seed) * 0.03f * centreDip;
        h += hills;

        // River/creek meandering through (small depression)
        float riverLine = Mathf.Abs(v - (0.5f + Mathf.Sin(u * 12f + 2f) * 0.02f));
        float river = Mathf.Clamp01(1f - riverLine / 0.006f) * centreDip;
        h -= river * 0.015f;

        return h;
    }

    // ─────────────────────────────────────────────────────────────────
    //  TIER 1 — GREENBELT FRONTIER
    // ─────────────────────────────────────────────────────────────────
    float GreenbeltFeatures(float dist, float u, float v, float angle, float seed)
    {
        float h = 0f;

        // Rolling hills — larger scale, smooth
        float hills1 = Mathf.PerlinNoise(u * 4f + seed * 1.3f, v * 4f + seed * 1.3f) * 0.04f;
        float hills2 = Mathf.PerlinNoise(u * 7f + seed * 1.7f, v * 7f + seed * 1.7f) * 0.02f;
        h += hills1 + hills2;

        // Creek beds — subtle linear depressions
        for (int i = 0; i < 3; i++)
        {
            float ca = seed * 0.3f + i * 2.1f;
            float cx = 0.5f + Mathf.Cos(ca) * 0.1f;
            float cy = 0.5f + Mathf.Sin(ca) * 0.1f;
            float creekAngle = ca + 1.2f;
            float creekDist = Mathf.Abs(Mathf.Sin((u - cx) * Mathf.Cos(creekAngle) + (v - cy) * Mathf.Sin(creekAngle)) * 3f);
            float creekMask = Mathf.Clamp01(1f - creekDist / 0.12f);
            // Only within Greenbelt ring
            float ringMask = RingMask(dist, 0.10f, 0.26f, 0.02f);
            h -= creekMask * ringMask * 0.008f;
        }

        return h;
    }

    // ─────────────────────────────────────────────────────────────────
    //  TIER 2 — BARREN EXPANSE
    // ─────────────────────────────────────────────────────────────────
    float BarrenFeatures(float dist, float u, float v, float angle, float seed)
    {
        float h = 0f;

        // Flatter terrain — lower amplitude noise
        float flatNoise = Mathf.PerlinNoise(u * 3f + seed * 2f, v * 3f + seed * 2f) * 0.025f;
        h += flatNoise;

        // Rock arches / buttes — sharper isolated bumps
        float ridgeNoise = Mathf.PerlinNoise(u * 5f + seed * 1.5f, v * 5f + seed * 1.5f);
        ridgeNoise = Mathf.Pow(ridgeNoise, 3f); // sharpen peaks
        h += ridgeNoise * 0.03f;

        // Dry riverbeds — deeper, wider channels
        for (int i = 0; i < 4; i++)
        {
            float ra = seed * 0.7f + i * 1.57f;
            float rx = 0.5f + Mathf.Cos(ra) * 0.18f;
            float ry = 0.5f + Mathf.Sin(ra) * 0.18f;
            float rAngle = ra + 0.8f;
            float rDist = Mathf.Abs(Mathf.Sin((u - rx) * Mathf.Cos(rAngle) * 4f + (v - ry) * Mathf.Sin(rAngle) * 4f));
            float rMask = Mathf.Clamp01(1f - rDist / 0.10f);
            float ringMask = RingMask(dist, 0.28f, 0.48f, 0.02f);
            h -= rMask * ringMask * 0.012f;
        }

        // Mining pits / quarries — circular depressions
        for (int i = 0; i < 5; i++)
        {
            float pa = seed * 0.5f + i * 1.26f;
            float pr = 0.28f + (seed * 0.1f + i * 0.15f) % 0.16f;
            float px = 0.5f + Mathf.Cos(pa) * pr;
            float py = 0.5f + Mathf.Sin(pa) * pr;
            float pitDist = Vector2.Distance(new Vector2(u, v), new Vector2(px, py));
            float pit = Mathf.Clamp01(1f - pitDist / 0.015f);
            h -= pit * 0.04f; // deeper pits
        }

        return h;
    }

    // ─────────────────────────────────────────────────────────────────
    //  TIER 3 — SCORCHED HIGHLANDS
    // ─────────────────────────────────────────────────────────────────
    float ScorchedFeatures(float dist, float u, float v, float angle, float seed)
    {
        float h = 0f;

        // Broken/jagged terrain — use ridged noise
        float n1 = Mathf.PerlinNoise(u * 6f + seed * 3f, v * 6f + seed * 3f);
        float n2 = Mathf.PerlinNoise(u * 11f + seed * 4f, v * 11f + seed * 4f);
        float ridged = Mathf.Abs(n1 * 2f - 1f); // ridged
        h += ridged * 0.04f;
        h += n2 * 0.02f;

        // Lava cracks — sharp linear fissures
        for (int i = 0; i < 6; i++)
        {
            float la = seed * 0.4f + i * 1.05f;
            float lx = 0.5f + Mathf.Cos(la) * 0.25f;
            float ly = 0.5f + Mathf.Sin(la) * 0.25f;
            float lAngle = la + seed * 0.2f;
            float lDist = Mathf.Abs(Mathf.Sin((u - lx) * Mathf.Cos(lAngle) * 5f + (v - ly) * Mathf.Sin(lAngle) * 5f));
            float lMask = Mathf.Clamp01(1f - lDist / 0.06f);
            float ringMask = RingMask(dist, 0.50f, 0.68f, 0.02f);
            h -= lMask * ringMask * 0.018f;
        }

        // Industrial flat zones (refineries, factories) — flat plateaus
        for (int i = 0; i < 3; i++)
        {
            float ia = seed * 0.6f + i * 2.09f;
            float ir = 0.52f + i * 0.05f;
            float ix = 0.5f + Mathf.Cos(ia) * ir;
            float iy = 0.5f + Mathf.Sin(ia) * ir;
            float id = Vector2.Distance(new Vector2(u, v), new Vector2(ix, iy));
            // Flat plateau with sharp edges
            float iMask = Mathf.Clamp01(1f - Mathf.Abs(id - 0.02f) / 0.01f);
            h += iMask * 0.03f; // slight raised platform
            // Flatten inside
            float inner = Mathf.Clamp01(1f - id / 0.02f);
            // Push towards a uniform height
            float currentBase = TierBaseHeight(3, ir) + 0.03f;
            h += inner * (currentBase - (TierBaseHeight(3, dist) + h)) * 0.5f;
        }

        return h;
    }

    // ─────────────────────────────────────────────────────────────────
    //  TIER 4 — FROZEN DEADLANDS
    // ─────────────────────────────────────────────────────────────────
    float FrozenFeatures(float dist, float u, float v, float angle, float seed)
    {
        float h = 0f;

        // Mountain peaks — sharper, taller noise
        float m1 = Mathf.PerlinNoise(u * 4f + seed * 5f, v * 4f + seed * 5f);
        float m2 = Mathf.PerlinNoise(u * 8f + seed * 5.5f, v * 8f + seed * 5.5f);
        float mountains = Mathf.Pow(m1, 2.5f) * 0.08f;
        mountains += m2 * 0.03f;
        h += mountains;

        // Plateau/flat military base areas
        float plateauNoise = Mathf.PerlinNoise(u * 2f + seed * 4f, v * 2f + seed * 4f);
        float plateau = Mathf.Clamp01((plateauNoise - 0.55f) * 4f); // ~10% of area
        // Flatten these areas slightly
        h -= plateau * mountains * 0.8f;

        // Ice fissures — sharp cracks
        for (int i = 0; i < 4; i++)
        {
            float fa = seed * 0.9f + i * 1.57f;
            float fx = 0.5f + Mathf.Cos(fa) * 0.3f;
            float fy = 0.5f + Mathf.Sin(fa) * 0.3f;
            float fDist = Mathf.Abs(Vector2.Dot(
                new Vector2(u - fx, v - fy),
                new Vector2(Mathf.Cos(fa + 0.5f), Mathf.Sin(fa + 0.5f))));
            float fMask = Mathf.Clamp01(1f - fDist / 0.04f);
            float ringMask = RingMask(dist, 0.70f, 0.86f, 0.02f);
            h -= fMask * ringMask * 0.01f;
        }

        return h;
    }

    // ─────────────────────────────────────────────────────────────────
    //  TIER 5 — WASTELAND CORE
    // ─────────────────────────────────────────────────────────────────
    float WastelandCore(float dist, float u, float v, float angle, float seed)
    {
        float h = 0f;

        // CRATER — massive depression towards the centre of this ring
        // The crater deepens toward the centrepoint of Tier 5
        float craterCentre = 0.91f; // ~centre of the Tier 5 ring
        float craterDepth = Mathf.Clamp01(1f - Mathf.Abs(dist - craterCentre) / 0.08f);
        h -= craterDepth * 0.12f;

        // Jagged crater rim — raised at the transition boundary
        float rimDist = Mathf.Abs(dist - 0.88f);
        float rim = Mathf.Clamp01(1f - rimDist / 0.03f);
        h += rim * 0.06f; // raised rim

        // Ruined megacity — tall jagged blocks (skyscraper stumps)
        float cityNoise = Mathf.PerlinNoise(u * 10f + seed * 6f, v * 10f + seed * 6f);
        float city = Mathf.Pow(Mathf.Abs(cityNoise - 0.5f) * 2f, 4f); // sparse sharp spikes
        h += city * 0.07f;

        // Radioactive lake depressions
        for (int i = 0; i < 3; i++)
        {
            float la = seed * 0.3f + i * 2.09f;
            float lr = 0.91f + i * 0.02f;
            float lx = 0.5f + Mathf.Cos(la) * lr;
            float ly = 0.5f + Mathf.Sin(la) * lr;
            float ld = Vector2.Distance(new Vector2(u, v), new Vector2(lx, ly));
            float lake = Mathf.Clamp01(1f - ld / 0.025f);
            h -= lake * 0.06f;
        }

        // Alien growth / twisted terrain — warped noise
        float alienWarp = Mathf.PerlinNoise(
            u * 6f + Mathf.PerlinNoise(u * 4f, v * 4f) * 2f,
            v * 6f + Mathf.PerlinNoise(u * 5f, v * 5f) * 2f);
        h += (alienWarp - 0.5f) * 0.04f;

        return h;
    }

    // ─────────────────────────────────────────────────────────────────
    //  TRANSITION FEATURES BETWEEN TIERS
    // ─────────────────────────────────────────────────────────────────
    float TransitionFeatures(float dist, float u, float v, float angle, float seed)
    {
        float h = 0f;

        // ── T0→T1: Cliff ring around Tutorial Island, one bridge at East ──
        {
            float cliffDist = Mathf.Abs(dist - 0.09f);
            float cliff = Mathf.Clamp01(1f - cliffDist / 0.015f);

            // Bridge at East (angle ≈ 0°)
            float bridgeAngle = 0f;
            float bridgeWidth = 10f; // degrees
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, bridgeAngle * Mathf.Rad2Deg));
            float bridgeMask = Mathf.Clamp01(angleDiff / bridgeWidth); // 0 at bridge, 1 elsewhere

            h += cliff * bridgeMask * 0.06f; // raised cliff except at bridge
                                            // Slight dip for the bridge itself
            float bridge = Mathf.Clamp01(1f - angleDiff / bridgeWidth) * Mathf.Clamp01(1f - cliffDist / 0.02f);
            h -= bridge * 0.04f;
        }

        // ── T1→T2: Drying land, collapsed highway chokepoint ──
        {
            float transDist = Mathf.Abs(dist - 0.28f);
            float transMask = Mathf.Clamp01(1f - transDist / 0.04f);

            // Collapsed highway — raised rubble line at North
            float hwyAngle = 90f;
            float hwyWidth = 8f;
            float hwyAngleDiff = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, hwyAngle));
            float hwyMask = Mathf.Clamp01(1f - hwyAngleDiff / hwyWidth);

            // Rubble pile on the highway (blockage)
            float rubble = hwyMask * transMask;
            h += rubble * 0.04f;

            // Slight dip everywhere else (drying land)
            h -= transMask * (1f - hwyMask) * 0.015f;
        }

        // ── T2→T3: Canyon with dam ──
        {
            float canyonDist = Mathf.Abs(dist - 0.50f);
            float canyonMask = Mathf.Clamp01(1f - canyonDist / 0.04f);

            // Canyon walls on both sides
            h += canyonMask * 0.05f;

            // Dam at South (angle ≈ 180° or -180°)
            float damAngle = 180f;
            float damWidth = 6f;
            float damAngleDiff = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, damAngle));
            float damMask = Mathf.Clamp01(1f - damAngleDiff / damWidth);

            // Dam is a flat bridge across the canyon
            float dam = damMask * canyonMask;
            h -= dam * 0.04f; // lower to create passable dam
        }

        // ── T3→T4: Mountain range, tunnel passes ──
        {
            float mtnDist = Mathf.Abs(dist - 0.70f);
            float mtnMask = Mathf.Clamp01(1f - mtnDist / 0.04f);

            // High mountain wall
            float mtnHeight = mtnMask * 0.10f;
            h += mtnHeight;

            // Two passes (tunnels) at NE and SW
            for (int p = 0; p < 2; p++)
            {
                float passAngle = p == 0 ? 45f : -135f; // NE and SW
                float passWidth = 7f;
                float passDiff = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, passAngle));
                float passMask = Mathf.Clamp01(1f - passDiff / passWidth);
                h -= passMask * mtnMask * 0.08f; // lower pass through mountains
            }
        }

        // ── T4→T5: Crater rim, broken suspension bridge ──
        {
            float rimDist = Mathf.Abs(dist - 0.88f);
            float rimMask = Mathf.Clamp01(1f - rimDist / 0.03f);

            // Crater rim — raised edge
            h += rimMask * 0.08f;

            // Broken bridge at West (angle ≈ 180° or -180°)
            float brdgAngle = -180f;
            float brdgWidth = 5f;
            float brdgDiff = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, brdgAngle));
            float brdgMask = Mathf.Clamp01(1f - brdgDiff / brdgWidth);

            // Bridge crossing (lowered path)
            float brdg = brdgMask * rimMask;
            h -= brdg * 0.07f;

            // "Broken" part — jagged gap mid-bridge
            float gapDist = Mathf.Abs(dist - 0.885f);
            float gap = Mathf.Clamp01(1f - gapDist / 0.005f) * brdgMask;
            h += gap * 0.03f; // small gap in the bridge
        }

        return h;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UTILITY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Smooth ring mask: 1 inside the ring, 0 outside, with soft edges.</summary>
    float RingMask(float dist, float inner, float outer, float blend)
    {
        float inEdge  = Mathf.Clamp01((dist - (inner - blend)) / blend);
        float outEdge = Mathf.Clamp01(((outer + blend) - dist) / blend);
        return inEdge * outEdge;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TERRAIN LAYERS & BIOME PAINTING
    // ═══════════════════════════════════════════════════════════════════
    void SetupTerrainLayers(TerrainData td)
    {
        // Auto-create any missing biome layers first
        CreateMissingBiomeLayers();

        // Gather all terrain layers from _TerrainAutoUpgrade
        const string layerFolder = "Assets/_TerrainAutoUpgrade";
        var guids = AssetDatabase.FindAssets("t:TerrainLayer", new[] { layerFolder });
        var allLayers = new List<TerrainLayer>();
        foreach (var g in guids)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(g));
            if (layer != null) allLayers.Add(layer);
        }

        // Also check TerrainLayers folder
        guids = AssetDatabase.FindAssets("t:TerrainLayer", new[] { "Assets/TerrainLayers" });
        foreach (var g in guids)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(g));
            if (layer != null && !allLayers.Contains(layer)) allLayers.Add(layer);
        }

        if (allLayers.Count == 0)
        {
            Debug.LogWarning("[WorldGen] No terrain layers found. Skipping biome paint.");
            return;
        }

        // Assign textures to layers that are missing them
        AssignLayerTextures(allLayers);

        // Set as terrain layers (preserve any existing that aren't in our list)
        var existing = new List<TerrainLayer>(td.terrainLayers);
        foreach (var l in allLayers)
        {
            if (!existing.Contains(l))
                existing.Add(l);
        }
        td.terrainLayers = existing.ToArray();
        EditorUtility.SetDirty(td);
    }

    void AssignLayerTextures(List<TerrainLayer> layers)
    {
        const string grassFolder = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Grass";
        const string dirtFolder  = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Dirt";
        const string snowFolder  = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Snow";

        var allTextures = new List<Texture2D>();
        foreach (var f in new[] { grassFolder, dirtFolder, snowFolder })
        {
            if (!AssetDatabase.IsValidFolder(f)) continue;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { f }))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (tex != null) allTextures.Add(tex);
            }
        }

        foreach (var layer in layers)
        {
            if (layer == null || layer.diffuseTexture != null) continue;
            string n = layer.name.ToLowerInvariant();

            Texture2D pick = null;
            if (n.Contains("grass") || n.Contains("moss"))
                pick = allTextures.Find(t => t.name.ToLowerInvariant().Contains("grass"));
            if (pick == null && n.Contains("dirt"))
                pick = allTextures.Find(t => t.name.ToLowerInvariant().Contains("dirt"));
            if (pick == null && (n.Contains("gravel") || n.Contains("cliff") || n.Contains("rock")))
                pick = allTextures.Find(t => t.name.ToLowerInvariant().Contains("rock") || t.name.ToLowerInvariant().Contains("gravel"));
            if (pick == null && (n.Contains("snow") || n.Contains("ice")))
                pick = allTextures.Find(t => t.name.ToLowerInvariant().Contains("snow"));
            if (pick == null && (n.Contains("charcoal") || n.Contains("asphalt")))
                pick = allTextures.Find(t => t.name.ToLowerInvariant().Contains("dirt"));
            if (pick == null && allTextures.Count > 0)
                pick = allTextures[0];

            if (pick != null)
            {
                layer.diffuseTexture = pick;
                EditorUtility.SetDirty(layer);
            }
        }
    }

    void PaintBiomes(TerrainData td)
    {
        int res = td.alphamapResolution;
        int numLayers = td.terrainLayers.Length;
        if (numLayers == 0) return;

        // Map biome → best matching layer index
        var biomeLayer = FindBiomeLayers(td);

        // Load layout PNG for color-aware painting
        Texture2D layout = null;
        if (_useLayoutPng)
            layout = AssetDatabase.LoadAssetAtPath<Texture2D>(LAYOUT_PATH);

        float[,,] maps = td.GetAlphamaps(0, 0, res, res);

        try
        {
            for (int y = 0; y < res; y++)
            {
                if (y % 64 == 0)
                    EditorUtility.DisplayProgressBar("World Generator", $"Painting biomes: row {y}/{res}", (float)y / res * 0.2f + 0.7f);

                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / (res - 1);
                    float v = (float)y / (res - 1);
                    float dx = u - 0.5f;
                    float dy = v - 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    dist = Mathf.Clamp01(dist);

                    // Determine biome from distance rings
                    int biome = dist < 0.08f ? 0 :
                                dist < 0.26f ? 1 :
                                dist < 0.48f ? 2 :
                                dist < 0.68f ? 3 :
                                dist < 0.86f ? 4 : 5;

                    // ── Layout PNG color overrides ──
                    if (layout != null)
                    {
                        Color c = layout.GetPixelBilinear(u, v);

                        // Water (blue-dominant) → pull toward rocky/cliff layer if available
                        float blueness = c.b - (c.r + c.g) * 0.5f;
                        bool isWater = blueness > 0.15f;

                        // Snow/white areas → force Tier 4 (Frozen)
                        float brightness = (c.r + c.g + c.b) / 3f;
                        if (brightness > 0.85f && c.r > 0.8f && c.g > 0.8f && c.b > 0.8f)
                            biome = 4;

                        // Green-dominant → bias toward Tier 0/1 (lush)
                        float greenness = c.g - (c.r + c.b) * 0.5f;
                        if (greenness > 0.05f && biome >= 2)
                            biome = Mathf.Max(1, biome - 1);

                        // Red-dominant → bias toward Tier 2/3 (barren/scorched)
                        float redness = c.r - (c.g + c.b) * 0.5f;
                        if (redness > 0.1f && biome <= 1)
                            biome = 2;
                    }

                    // Clear pixel
                    for (int l = 0; l < numLayers; l++)
                        maps[y, x, l] = 0f;

                    // Paint primary biome
                    if (biomeLayer.TryGetValue(biome, out int primary))
                        maps[y, x, primary] = 1f;

                    // Blend at boundaries
                    float blend = 0.03f;
                    float[] boundaries = { 0.08f, 0.26f, 0.48f, 0.68f, 0.86f };
                    for (int b = 0; b < boundaries.Length; b++)
                    {
                        float t = (dist - (boundaries[b] - blend)) / (blend * 2f);
                        t = Mathf.Clamp01(t);
                        if (t > 0f && t < 1f && biomeLayer.TryGetValue(b + 1, out int next))
                        {
                            maps[y, x, primary] = 1f - t;
                            maps[y, x, next] = t;
                        }
                    }

                    // Special: cliff ring around Tutorial uses a rocky/gravel layer
                    float cliffDist = Mathf.Abs(dist - 0.09f);
                    if (cliffDist < 0.015f && biomeLayer.TryGetValue(3, out int rockLayer))
                    {
                        float cliffWeight = Mathf.Clamp01(1f - cliffDist / 0.015f);
                        // Blend rock over whatever is there
                        for (int l = 0; l < numLayers; l++)
                            maps[y, x, l] *= (1f - cliffWeight);
                        maps[y, x, rockLayer] += cliffWeight;
                    }

                    // Water areas from layout → blend in rocky/gravel shore
                    if (layout != null)
                    {
                        Color c = layout.GetPixelBilinear(u, v);
                        float blueness = c.b - (c.r + c.g) * 0.5f;
                        if (blueness > 0.15f && biomeLayer.TryGetValue(3, out int rockLayerShore))
                        {
                            float waterWeight = Mathf.Clamp01((blueness - 0.15f) / 0.5f) * 0.7f;
                            for (int l = 0; l < numLayers; l++)
                                maps[y, x, l] *= (1f - waterWeight);
                            maps[y, x, rockLayerShore] += waterWeight;
                        }
                    }
                }
            }
        }
        finally { }

        td.SetAlphamaps(0, 0, maps);
        EditorUtility.SetDirty(td);
    }

    Dictionary<int, int> FindBiomeLayers(TerrainData td)
    {
        var map = new Dictionary<int, int>();
        var layers = td.terrainLayers;

        (int biome, string[] keywords)[] biomeKeys = {
            (0, new[]{"lush", "moss", "grass_1", "green"}),
            (1, new[]{"grass", "leaf", "forest", "moss"}),
            (2, new[]{"dirt", "dry", "gravel", "sand", "desatured"}),
            (3, new[]{"ash", "volcanic", "cliff", "rockgrassy", "charcoal", "corrupted"}),
            (4, new[]{"snow", "ice", "white", "frost"}),
            (5, new[]{"asphalt", "charcoal_1", "dark", "black", "dragon"}),
        };

        var used = new HashSet<int>();
        foreach (var (biome, keywords) in biomeKeys)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (used.Contains(i)) continue;
                if (layers[i] == null) continue;
                string name = layers[i].name.ToLowerInvariant();
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

        // Fallback
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

    // ═══════════════════════════════════════════════════════════════════
    //  CONTENT SNAPPING
    // ═══════════════════════════════════════════════════════════════════
    void SnapContentToTerrain(Terrain terrain)
    {
        // Find Player3DController using the generic Object.Find... API
        var allMono = Object.FindObjectsByType<MonoBehaviour>();
        foreach (var mb in allMono)
        {
            if (mb != null && mb.GetType().Name == "Player3DController")
            {
                SnapTo(mb.transform, terrain, 0.4f);
                break;
            }
        }

        // WorldContent markers
        var content = GameObject.Find("WorldContent");
        if (content != null)
            foreach (Transform tierGroup in content.transform)
                foreach (Transform marker in tierGroup)
                    SnapTo(marker, terrain, 0.6f);

        // CombatTarget enemies
        var cts = Object.FindObjectsByType<MonoBehaviour>();
        foreach (var ct in cts)
        {
            if (ct != null && ct.GetType().Name == "CombatTarget")
                SnapTo(ct.transform, terrain, 1f);
        }
    }

    void SnapTo(Transform t, Terrain terrain, float lift)
    {
        if (t == null) return;
        Vector3 p = t.position;
        float surface = terrain.SampleHeight(p) + terrain.transform.position.y;
        t.position = new Vector3(p.x, surface + lift, p.z);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ONE-TIME SETUP: create missing biome terrain layers
    // ═══════════════════════════════════════════════════════════════════
    [MenuItem("Wasteland/Archived/Create Missing Biome Terrain Layers", false, 9000)]
    public static void CreateMissingBiomeLayers()
    {
        const string folder = "Assets/_TerrainAutoUpgrade";

        if (!AssetDatabase.IsValidFolder(folder))
        {
            EditorUtility.DisplayDialog("Missing Folder",
                $"Folder '{folder}' not found. Please ensure _TerrainAutoUpgrade exists.", "OK");
            return;
        }

        // ── Snow layer for Tier 4 (Frozen Deadlands) ──
        string snowPath = $"{folder}/layer_snow_biome.terrainlayer";
        if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(snowPath) == null)
        {
            var snowLayer = new TerrainLayer { name = "layer_snow_biome" };
            AssetDatabase.CreateAsset(snowLayer, snowPath);

            // Assign snow texture
            string snowTexPath = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Snow/snow_dark.png";
            var snowTex = AssetDatabase.LoadAssetAtPath<Texture2D>(snowTexPath);
            if (snowTex == null)
                snowTexPath = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Snow/snow_step_001.png";
            snowTex = AssetDatabase.LoadAssetAtPath<Texture2D>(snowTexPath);

            if (snowTex != null)
            {
                snowLayer.diffuseTexture = snowTex;
                snowLayer.tileSize = new Vector2(15, 15);
                EditorUtility.SetDirty(snowLayer);
            }
            Debug.Log("[WorldGen] Created snow biome terrain layer.");
        }
        else
        {
            // Ensure existing snow layer has a texture
            var snowLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(snowPath);
            if (snowLayer != null && snowLayer.diffuseTexture == null)
            {
                string snowTexPath = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Snow/snow_dark.png";
                var snowTex = AssetDatabase.LoadAssetAtPath<Texture2D>(snowTexPath);
                if (snowTex == null)
                    snowTexPath = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Snow/snow_step_001.png";
                snowTex = AssetDatabase.LoadAssetAtPath<Texture2D>(snowTexPath);
                if (snowTex != null)
                {
                    snowLayer.diffuseTexture = snowTex;
                    EditorUtility.SetDirty(snowLayer);
                }
            }
        }

        // ── Ash / volcanic layer for Tier 3 (Scorched Highlands) ──
        string ashPath = $"{folder}/layer_ash_volcanic.terrainlayer";
        if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(ashPath) == null)
        {
            var ashLayer = new TerrainLayer { name = "layer_ash_volcanic" };
            AssetDatabase.CreateAsset(ashLayer, ashPath);

            // Use a dark dirt/corrupted texture for volcanic ash
            string ashTexPath = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Dirt/dirt_corrupted_lighted";
            var ashTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { ashTexPath });
            if (ashTexGuids.Length > 0)
            {
                var ashTex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    AssetDatabase.GUIDToAssetPath(ashTexGuids[0]));
                ashLayer.diffuseTexture = ashTex;
                ashLayer.tileSize = new Vector2(15, 15);
                EditorUtility.SetDirty(ashLayer);
            }
            Debug.Log("[WorldGen] Created volcanic ash terrain layer.");
        }

        // ── Lush grass layer for Tier 0/1 (Tutorial Island + Greenbelt) ──
        string lushPath = $"{folder}/layer_lush_grass.terrainlayer";
        if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(lushPath) == null)
        {
            var lushLayer = new TerrainLayer { name = "layer_lush_grass" };
            AssetDatabase.CreateAsset(lushLayer, lushPath);

            string grassTexPath = "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Grass/Grass_lighted";
            var grassTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { grassTexPath });
            if (grassTexGuids.Length > 0)
            {
                var grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    AssetDatabase.GUIDToAssetPath(grassTexGuids[0]));
                lushLayer.diffuseTexture = grassTex;
                lushLayer.tileSize = new Vector2(15, 15);
                EditorUtility.SetDirty(lushLayer);
            }
            Debug.Log("[WorldGen] Created lush grass terrain layer.");
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Biome Layers Ready",
            "Created/verified biome terrain layers:\n\n" +
            "• layer_snow_biome — Frozen Deadlands (Tier 4)\n" +
            "• layer_ash_volcanic — Scorched Highlands (Tier 3)\n" +
            "• layer_lush_grass — Tutorial Island + Greenbelt (Tiers 0/1)\n\n" +
            "Now run 'Generate Full World' to build the terrain.", "OK");
    }
}
