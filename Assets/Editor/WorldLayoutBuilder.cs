using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Stamps the 5-tier concentric-ring world layout onto the scene's existing Terrain.
/// Menu:  Wasteland ▸ Build World Layout (5 Tiers)
///
/// What it builds:
///   • Heightmap  — concentric rings rising from the flat safe hub at center out to the
///                  volcanic Wasteland peak at the edges, matching the concept art layout:
///                  Hub (flat) → Greenbelt → Barren Plains → Scorched Highlands →
///                  Frozen Wastes → Wasteland (highest, most rugged)
///   • Terrain splat — 5 painted layers, one per tier, blended at the ring seams.
///                  Uses terrain layers already on the TerrainData when available;
///                  falls back to generated solid-colour layers so it always works.
///   • TierGates  — BoxCollider trigger GOs at the 4 cardinal crossing points of each
///                  ring boundary (N/S/E/W), with TierGate components pre-configured.
///   • FogWalls   — thin particle-curtain GOs at the same chokepoints, one per gate.
///
/// Safe to re-run: heightmap + splat are always overwritten, but TierGates / FogWalls
/// are skipped if already present (idempotent).
///
/// IMPORTANT: run AFTER "Fix Scene (dontfuckindelete)" so the scene has a Player to
/// compute the world center from.
/// </summary>
public static class WorldLayoutBuilder
{
    // ── tier ring fractions — expressed as 0–1 fraction of terrain half-size ──
    // Scaled at runtime against the actual terrain so this works for any terrain dimensions.
    // Tier 0 = flat hub/spawn  →  Tier 5 = volcanic outer Wasteland edge
    static readonly float[] RingFractions = { 0f, 0.08f, 0.22f, 0.42f, 0.64f, 0.85f, 1.0f };

    // Height (0–1 normalised) at each ring boundary — STRICTLY INCREASING so the
    // terrain rises smoothly outward. The hub is flat; the outer wasteland is the peak.
    static readonly float[] RingHeight = { 0.018f, 0.045f, 0.10f, 0.20f, 0.35f, 0.52f };

    // Noise roughness per tier — zero in the hub, maximum at the outer wasteland
    static readonly float[] RingRoughness = { 0f, 0.004f, 0.010f, 0.022f, 0.038f, 0.065f };

    // Base tint color per tier (shown as generated splat layers if no real texture exists)
    static readonly Color[] TierColors =
    {
        new Color(0.35f, 0.48f, 0.28f),   // 0 – hub: green grass
        new Color(0.30f, 0.45f, 0.25f),   // 1 – Greenbelt: green
        new Color(0.62f, 0.52f, 0.32f),   // 2 – Barren Plains: sandy tan
        new Color(0.48f, 0.35f, 0.22f),   // 3 – Scorched Highlands: scorched brown
        new Color(0.55f, 0.62f, 0.70f),   // 4 – Frozen Wastes: icy blue-grey
        new Color(0.22f, 0.18f, 0.18f),   // 5 – Wasteland: dark volcanic
    };

    static readonly string[] TierNames =
        { "Hub", "Greenbelt", "Barren Plains", "Scorched Highlands", "Frozen Wastes", "Wasteland" };

    // ── gate config per ring seam ─────────────────────────────────────────────
    // Which tier each ring seam leads into. Nothing else: a gate is opened by killing that tier's
    // boss and by nothing else — no level requirement, no kill count.
    static readonly int[] GateConfig = { 2, 3, 4, 5 };

    public static void BuildHeadless()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            Debug.LogError("[WorldLayoutBuilderHeadless] No active Terrain found.");
            return;
        }

        var log = new System.Text.StringBuilder();
        log.AppendLine("=== Build World Layout Headless ===\n");

        Vector3 worldCenter = ComputeWorldCenter(terrain);
        log.AppendLine($"World centre: {worldCenter}");

        StampHeightmap(terrain, worldCenter, log);
        PaintSplat(terrain, worldCenter, log);
        PlaceGates(terrain, worldCenter, log);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        log.AppendLine("\n✅  World layout built and scene saved.");
        Debug.Log(log.ToString());
    }

    // ── entry point ──────────────────────────────────────────────────────────
    [MenuItem("Wasteland/Archived/Build World Layout (5 Tiers)", false, 9000)]
    public static void Build()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("No Terrain", "No active Terrain in this scene. Add one first.", "OK");
            return;
        }

        bool go = EditorUtility.DisplayDialog(
            "Build World Layout",
            "This will:\n" +
            "  1. OVERWRITE the terrain heightmap with 5 concentric rings\n" +
            "  2. Paint tier splat textures on the terrain\n" +
            "  3. Place TierGate + FogWall GOs at ring boundaries\n\n" +
            "TierGates/FogWalls that already exist are skipped.\n" +
            "Heightmap + splat are always rebuilt.\n\n" +
            "Continue?",
            "Build", "Cancel");
        if (!go) return;

        var log = new System.Text.StringBuilder();
        log.AppendLine("=== Build World Layout ===\n");

        Vector3 worldCenter = ComputeWorldCenter(terrain);
        log.AppendLine($"World centre: {worldCenter}");

        StampHeightmap(terrain, worldCenter, log);
        PaintSplat(terrain, worldCenter, log);
        PlaceGates(terrain, worldCenter, log);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        log.AppendLine("\n✅  World layout built and scene saved.");
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("World Layout — Done",
            "5-tier world stamped.\nSee Console for details.", "OK");
    }

    // ── world centre ─────────────────────────────────────────────────────────
    /// <summary>Use the Player spawn position if available; otherwise the terrain centre.</summary>
    // Compute actual ring radii in world-space metres from the terrain's real size
    static float[] ComputeRingRadii(Terrain terrain)
    {
        var td = terrain.terrainData;
        float halfX = td.size.x * 0.5f;
        float halfZ = td.size.z * 0.5f;
        float halfSize = Mathf.Min(halfX, halfZ);   // use the smaller half so rings fit on all sides
        var radii = new float[RingFractions.Length];
        for (int i = 0; i < RingFractions.Length; i++)
            radii[i] = RingFractions[i] * halfSize;
        return radii;
    }

    static Vector3 ComputeWorldCenter(Terrain terrain)
    {
        var td  = terrain.terrainData;
        var tp  = terrain.transform.position;
        // Default to exact terrain centre
        float cx = tp.x + td.size.x * 0.5f;
        float cz = tp.z + td.size.z * 0.5f;

        var player = GameObject.Find("Player");
        if (player != null)
        {
            // Use player spawn, clamped so rings stay well inside the terrain bounds
            float margin = Mathf.Min(td.size.x, td.size.z) * 0.1f;
            cx = Mathf.Clamp(player.transform.position.x, tp.x + margin, tp.x + td.size.x - margin);
            cz = Mathf.Clamp(player.transform.position.z, tp.z + margin, tp.z + td.size.z - margin);
        }
        return new Vector3(cx, 0f, cz);
    }

    // ── heightmap ─────────────────────────────────────────────────────────────
    static void StampHeightmap(Terrain terrain, Vector3 worldCenter, System.Text.StringBuilder log)
    {
        var td   = terrain.terrainData;
        int res  = td.heightmapResolution;
        var tp   = terrain.transform.position;

        float[,] heights = new float[res, res];

        // Terrain-space scale factors
        float scaleX = td.size.x / (res - 1);
        float scaleZ = td.size.z / (res - 1);

        float[] RingRadii = ComputeRingRadii(terrain);
        float HalfSize = RingRadii[RingRadii.Length - 1];

        for (int zi = 0; zi < res; zi++)
        {
            for (int xi = 0; xi < res; xi++)
            {
                // World-space XZ of this heightmap texel
                float wx = tp.x + xi * scaleX;
                float wz = tp.z + zi * scaleZ;

                float dx = wx - worldCenter.x;
                float dz = wz - worldCenter.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float angle = Mathf.Atan2(dx, dz); // -pi to pi. 0 is North, pi/-pi is South

                // Crescent distortion: outer rings curve inward towards South
                float crescentFactor = 0.22f * (1f - Mathf.Cos(angle));
                float distShifted = dist * (1f + crescentFactor);

                // Which tier band does this texel fall in?
                int tier = TierIndexAt(distShifted, RingRadii);

                // Base plateau height
                float baseH = RingHeight[Mathf.Min(tier, RingHeight.Length - 1)];

                // Smooth chasm dips at boundaries to separate different tiers
                float chasmDepth = 1.0f;
                float chasmWidth = 16f; // world units
                for (int j = 1; j < RingRadii.Length - 1; j++)
                {
                    float boundaryR = RingRadii[j];
                    float distToBoundary = Mathf.Abs(distShifted - boundaryR);
                    if (distToBoundary < chasmWidth)
                    {
                        float t = distToBoundary / chasmWidth;
                        float dip = Mathf.SmoothStep(0.015f, 1.0f, t);
                        if (dip < chasmDepth) chasmDepth = dip;
                    }
                }
                baseH = Mathf.Lerp(0.008f, baseH, chasmDepth);

                // Angle-dependent modifier (suppresses peaks/cliffs at the South)
                float angleFactor = (1f + Mathf.Cos(angle)) * 0.5f; // 1 at North, 0 at South

                // Add ridge-line noise that scales with tier roughness
                float roughness = RingRoughness[Mathf.Min(tier, RingRoughness.Length - 1)] * angleFactor;
                float noise = Mathf.PerlinNoise(wx * 0.015f + 0.3f, wz * 0.015f + 0.7f) - 0.5f;
                float noise2 = Mathf.PerlinNoise(wx * 0.05f + 1.1f, wz * 0.05f + 2.3f) - 0.5f;
                float combined = baseH + noise * roughness + noise2 * roughness * 0.4f;

                // Outer mountain walls (flanking North, East, West)
                float edgeT = dist / HalfSize; // 0 to 1+
                if (edgeT > 0.72f)
                {
                    float wallFactor = Mathf.InverseLerp(0.72f, 1.0f, edgeT);
                    // Mountain peak heights scaled by wallFactor and angleFactor
                    float mountainHeight = 0.55f * wallFactor * angleFactor;
                    
                    // Rugged mountain noise
                    float mountainNoise = Mathf.PerlinNoise(wx * 0.025f, wz * 0.025f);
                    float mountainNoise2 = Mathf.PerlinNoise(wx * 0.06f, wz * 0.06f);
                    
                    combined = Mathf.Lerp(combined, mountainHeight + mountainNoise * 0.15f + mountainNoise2 * 0.06f, wallFactor);
                }

                heights[zi, xi] = Mathf.Clamp01(combined);
            }
        }

        td.SetHeights(0, 0, heights);
        log.AppendLine($"✅  Crescent Heightmap stamped ({res}×{res}).");
    }

    static int TierIndexAt(float distShifted, float[] RingRadii)
    {
        for (int i = RingRadii.Length - 1; i >= 0; i--)
            if (distShifted >= RingRadii[i]) return i;
        return 0;
    }

    // ── splat / texture paint ─────────────────────────────────────────────────
    static void PaintSplat(Terrain terrain, Vector3 worldCenter, System.Text.StringBuilder log)
    {
        var td  = terrain.terrainData;
        int res = td.alphamapResolution;
        var tp  = terrain.transform.position;

        // Retrieve existing high-fidelity layers
        int layerCount = td.terrainLayers.Length;
        if (layerCount == 0)
        {
            log.AppendLine("⚠  No terrain layers — skipping splat paint.");
            return;
        }

        float[] radii = ComputeRingRadii(terrain);
        float halfSize = radii[radii.Length - 1];

        float scaleX = td.size.x / res;
        float scaleZ = td.size.z / res;

        float[,,] splatMap = new float[res, res, layerCount];

        // Retrieve existing high-fidelity layer mappings or clamp
        int layerCliff = 1;  // Rock_003_Albedo (steep cliffs)
        int layerTier0 = 5;  // Grass (Tutorial Island)
        int layerTier1 = 11; // DirtyGrass_Base_Color_autumn (Greenbelt)
        int layerTier2 = 3;  // Sand_BaseColor (Barren Plains)
        int layerTier3 = 10; // dirt_claydarked_down (Scorched Highlands)
        int layerTier4 = 12; // Snow_BaseColor (Frozen Wastes)
        int layerTier5 = 14; // DarkRock_Base_Color_rainny (Wasteland)

        // Ensure indices are within valid range
        layerCliff = Mathf.Clamp(layerCliff, 0, layerCount - 1);
        layerTier0 = Mathf.Clamp(layerTier0, 0, layerCount - 1);
        layerTier1 = Mathf.Clamp(layerTier1, 0, layerCount - 1);
        layerTier2 = Mathf.Clamp(layerTier2, 0, layerCount - 1);
        layerTier3 = Mathf.Clamp(layerTier3, 0, layerCount - 1);
        layerTier4 = Mathf.Clamp(layerTier4, 0, layerCount - 1);
        layerTier5 = Mathf.Clamp(layerTier5, 0, layerCount - 1);

        for (int zi = 0; zi < res; zi++)
        {
            for (int xi = 0; xi < res; xi++)
            {
                float wx = tp.x + xi * scaleX;
                float wz = tp.z + zi * scaleZ;

                float dx = wx - worldCenter.x;
                float dz = wz - worldCenter.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float angle = Mathf.Atan2(dx, dz); // -pi to pi

                // Crescent distortion
                float crescentFactor = 0.22f * (1f - Mathf.Cos(angle));
                float distShifted = dist * (1f + crescentFactor);

                // Find tier
                int tier = TierIndexAt(distShifted, radii);

                // Compute terrain slope at this point (steepness) in degrees (0 to 90)
                float normalizedX = (float)xi / res;
                float normalizedZ = (float)zi / res;
                float slope = td.GetSteepness(normalizedX, normalizedZ);

                float[] weights = new float[layerCount];

                // Determine base tier layer
                int baseLayer = tier switch
                {
                    0 => layerTier0,
                    1 => layerTier1,
                    2 => layerTier2,
                    3 => layerTier3,
                    4 => layerTier4,
                    5 => layerTier5,
                    _ => layerTier5
                };

                // Blend between base tier and cliffs based on slope (steepness)
                // Cliffs start appearing at 20 degrees, and completely dominate at 42 degrees
                float cliffWeight = Mathf.InverseLerp(20f, 42f, slope);
                
                weights[baseLayer] = 1f - cliffWeight;
                weights[layerCliff] = cliffWeight;

                // Normalize weights
                float sum = 0f;
                for (int l = 0; l < layerCount; l++) sum += weights[l];
                if (sum > 0f)
                {
                    for (int l = 0; l < layerCount; l++)
                        splatMap[zi, xi, l] = weights[l] / sum;
                }
                else
                {
                    splatMap[zi, xi, baseLayer] = 1f;
                }
            }
        }

        td.SetAlphamaps(0, 0, splatMap);
        log.AppendLine("✅  Crescent Terrain splat painted (6 tiers blended + auto cliff rendering).");
    }

    static void EnsureTerrainLayers(TerrainData td, System.Text.StringBuilder log)
    {
        const string LayerFolder = "Assets/Art/Generated3D/TerrainLayers";

        if (td.terrainLayers.Length >= 6)
        {
            log.AppendLine($"ℹ  Terrain already has {td.terrainLayers.Length} layers — keeping them.");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Art"))
            AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder("Assets/Art/Generated3D"))
            AssetDatabase.CreateFolder("Assets/Art", "Generated3D");
        if (!AssetDatabase.IsValidFolder(LayerFolder))
            AssetDatabase.CreateFolder("Assets/Art/Generated3D", "TerrainLayers");

        var layers = new List<TerrainLayer>(td.terrainLayers);
        while (layers.Count < 6)
        {
            int idx = layers.Count;
            string layerPath = $"{LayerFolder}/TierLayer_{idx}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer
                {
                    name            = $"Tier{idx}_{TierNames[idx]}",
                    diffuseTexture  = GenerateSolidTexture(TierColors[idx], $"TierTex_{idx}"),
                    tileSize        = new Vector2(8f, 8f),
                };
                AssetDatabase.CreateAsset(layer, layerPath);
            }
            layers.Add(layer);
        }

        td.terrainLayers = layers.ToArray();
        AssetDatabase.SaveAssets();
        log.AppendLine($"✅  Ensured 6 terrain layers (generated missing ones).");
    }

    static Texture2D GenerateSolidTexture(Color color, string name)
    {
        const string TexFolder = "Assets/Art/Generated3D/TerrainLayers";
        string path = $"{TexFolder}/{name}.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var pixels = new Color[64 * 64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();

        System.IO.File.WriteAllBytes(
            System.IO.Path.Combine(Application.dataPath, "..", path),
            tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        Object.DestroyImmediate(tex);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ── tier gates + fog walls ────────────────────────────────────────────────
    static void PlaceGates(Terrain terrain, Vector3 worldCenter, System.Text.StringBuilder log)
    {
        // Gates at cardinal directions for each ring seam (tier 1→2, 2→3, 3→4, 4→5)
        // A ring seam sits at the OUTER radius of that ring = RingRadii[tier+1]
        var cardinals = new (Vector3 dir, string label)[]
        {
            (Vector3.forward,  "N"),
            (Vector3.back,     "S"),
            (Vector3.right,    "E"),
            (Vector3.left,     "W"),
        };

        var gatesRoot = GameObject.Find("TierGates");
        if (gatesRoot == null)
        {
            gatesRoot = new GameObject("TierGates");
            Undo.RegisterCreatedObjectUndo(gatesRoot, "Create TierGates root");
        }

        int gatesPlaced = 0;

        float[] RingRadii = ComputeRingRadii(terrain);

        for (int gi = 0; gi < GateConfig.Length; gi++)
        {
            int targetTier = GateConfig[gi];

            foreach (var (dir, label) in cardinals)
            {
                string gateId   = $"t{targetTier}_{label.ToLower()}";
                string gateName = $"TierGate_T{targetTier}_{label}";

                // Skip if already placed
                if (gatesRoot.transform.Find(gateName) != null) continue;

                // Adjust seam distance to account for crescent layout warping
                float angle = Mathf.Atan2(dir.x, dir.z);
                float crescentFactor = 0.22f * (1f - Mathf.Cos(angle));
                float seam = RingRadii[Mathf.Min(gi + 2, RingRadii.Length - 1)] / (1f + crescentFactor);

                Vector3 pos = worldCenter + dir * seam;
                SnapToTerrainY(terrain, ref pos);

                // ── TierGate GO ───────────────────────────────────────────────
                var gateGO = new GameObject(gateName);
                Undo.RegisterCreatedObjectUndo(gateGO, $"Place {gateName}");
                gateGO.transform.SetParent(gatesRoot.transform, true);
                gateGO.transform.position = pos + Vector3.up * 1.5f;
                gateGO.transform.rotation = Quaternion.LookRotation(dir);

                var bc = gateGO.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size      = new Vector3(12f, 6f, 1.5f);  // wide enough to catch the player
                bc.center    = Vector3.zero;

                // Boss kill is the only gate — assign Required Boss on the gate to arm it.
                var gate = gateGO.AddComponent<TierGate>();
                gate.targetTier = targetTier;
                gate.gateId     = gateId;

                // ── FogWall child ─────────────────────────────────────────────
                var fogGO = new GameObject("FogWall");
                Undo.RegisterCreatedObjectUndo(fogGO, "Place FogWall");
                fogGO.transform.SetParent(gateGO.transform, false);
                fogGO.transform.localPosition = Vector3.zero;

                var fogBox = fogGO.AddComponent<BoxCollider>();
                fogBox.isTrigger = true;
                fogBox.size      = new Vector3(12f, 6f, 1.5f);

                var fogWall = fogGO.AddComponent<FogWall>();
                fogWall.id = gateId + "_fog";

                // Particle curtain — simple fog-puff cloud that blocks the view
                var psFog = fogGO.AddComponent<ParticleSystem>();
                var main  = psFog.main;
                main.loop             = true;
                main.startLifetime    = 4f;
                main.startSpeed       = 0.3f;
                main.startSize        = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
                main.startColor       = TierFogColor(targetTier);
                main.maxParticles     = 80;
                main.simulationSpace  = ParticleSystemSimulationSpace.World;
                var em  = psFog.emission; em.rateOverTime = 18f;
                var sh  = psFog.shape;
                sh.enabled   = true;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale     = new Vector3(12f, 6f, 1.5f);
                var psRend = psFog.GetComponent<ParticleSystemRenderer>();
                psRend.material = FogMaterial(targetTier);

                // Link the fog visual to the gate so it auto-hides on open
                gate.fogVisual = fogGO;

                gatesPlaced++;
            }
        }

        log.AppendLine(gatesPlaced > 0
            ? $"✅  Placed {gatesPlaced} TierGate + FogWall pairs at ring seams."
            : "ℹ  All TierGates already present — skipped.");
    }

    static void SnapToTerrainY(Terrain terrain, ref Vector3 pos)
    {
        pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;
    }

    static Color TierFogColor(int tier) => tier switch
    {
        2 => new Color(0.55f, 0.65f, 0.45f, 0.35f),   // Greenbelt boundary: greenish mist
        3 => new Color(0.60f, 0.48f, 0.32f, 0.40f),   // Barren boundary: dust
        4 => new Color(0.45f, 0.30f, 0.22f, 0.45f),   // Scorched boundary: smoke
        5 => new Color(0.62f, 0.68f, 0.75f, 0.40f),   // Frozen boundary: icy haze
        _ => new Color(0.20f, 0.18f, 0.18f, 0.50f),   // Wasteland: ash
    };

    // Lightweight unlit fog material — no textures needed, just a translucent colour
    static Material FogMaterial(int tier)
    {
        const string FogMatFolder = "Assets/Art/Generated3D";
        string path = $"{FogMatFolder}/FogMat_T{tier}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        if (!AssetDatabase.IsValidFolder("Assets/Art"))         AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder(FogMatFolder))         AssetDatabase.CreateFolder("Assets/Art", "Generated3D");

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Particles/Standard Unlit")
                  ?? Shader.Find("Standard");
        mat = new Material(shader) { name = $"FogMat_T{tier}" };
        Color c = TierFogColor(tier);
        mat.color = c;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     c);
        // Enable transparency
        mat.SetFloat("_Surface", 1f);   // URP Transparent
        mat.SetFloat("_Blend",   0f);   // Alpha
        mat.renderQueue = 3000;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
