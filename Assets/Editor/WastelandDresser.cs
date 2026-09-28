using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// WastelandDresser — one-click atmospheric overhaul for MainWorld3D.
///
/// Two passes:
///   1. ATMOSPHERE — fog, lighting, skybox, color grading, god rays (post-apoc orange/gray)
///   2. SCATTER    — dead trees, ruined walls, scrap piles, ground detail from premium packs
///
/// Menu:  Wasteland > Dress World (Atmosphere + Scatter)
///        Wasteland > Dress World (Atmosphere Only)
///        Wasteland > Dress World (Scatter Only)
/// </summary>
public static class WastelandDresser
{
    // ── Asset folders (verified to exist) ─────────────────────────────────
    const string DeadTreeFolder    = "Assets/Hivemind/HauntedVillage/URP/Art/Prefabs/Foliage";
    const string RuinFolder        = "Assets/LeartesStudios/BanditsValley/Art/Prefabs";
    const string ScrapNodeFolder   = "Assets/_Wasteland/Nodes";
    const string FoliageFolder     = "Assets/Resources/Foliage";
    const string CyberpunkFolder   = "Assets/Hivemind/CyberpunkCity/URP/Art/Prefabs";
    const string MilitaryFolder    = "Assets/Hivemind/MilitaryCamp/URP/Art/Prefabs";
    const string HospitalFolder    = "Assets/Hivemind/HorrorHospital/URP/Art/Prefabs";

    const string DresserRoot       = "WastelandDressing";

    // Dead tree prefab names (verified)
    static readonly string[] DeadTreeNames =
    {
        "SM_EuropeanBeech_Dead_M_01",
        "SM_EuropeanBeech_Dead_M_02",
        "SM_EuropeanBeech_Dead_XL_01",
        "SM_EuropeanBeech_Stump_01",
        "SM_EuropeanBeech_Log_01",
    };

    // Ruin / wall prefab names (verified)
    static readonly string[] RuinNames =
    {
        "SM_Damage_Wall",
        "SM_Damage_Wall_01",
        "SM_wall_damage03",
        "SM_Wall_Damage_02",
        "SM_Corner_Damage",
        "SM_Roof_Damage",
        "SM_Roof_Damage_01",
        "SM_Roof_Damage_02",
        "SM_House",
        "SM_House_02",
        "SM_House_03",
        "SM_House_04",
        "SM_House_06",
        "SM_WatchTower",
        "SM_Brickpile",
        "SM_Fallen_Brick",
        "SM_Cart",
        "SM_Cart_01",
        "SM_Barrel",
        "SM_Campfire",
    };

    // Scrap node names (verified — but prefabs are 10-25km broken scale, excluded from scatter)
    static readonly string[] ScrapNodeNames =
    {
        // These prefabs have broken scale (10-25km) — excluded until fixed
        // "Junk Pile (T1)",
        // "abandoned car (T2)",
        // "tech dumpster (T3)",
        // "downed drones pile (T4)",
        // "abandoned robotics (T5)",
    };

    // Cyberpunk city ruins (verified — extracted from URP package)
    static readonly string[] CyberpunkNames =
    {
        "SM_Dumpster",
        "SM_DumpsterCap",
        "SM_Beam_Concrete_01",
        "SM_Beam_Concrete_02",
        "SM_Beam_Wood_01",
        "SM_Beam_Wood_02",
    };

    // Military camp assets (verified — extracted from URP package)
    static readonly string[] MilitaryNames =
    {
        "SM_Antenna_a",
        "SM_Antenna_c",
        "SM_Antenna_e",
        "SM_ArmoredVehicle",
        "SM_Barrel_02",
        "SM_AmmoCase_01",
        "SM_AmmoCase_02",
        "SM_AlarmLight",
        "SM_Building_wall_1m_A",
        "SM_Building_wall_2m_A",
        "SM_Building_wall_corner_90d_A",
    };

    // Hospital debris (verified — extracted from URP package)
    static readonly string[] HospitalNames =
    {
        "SM_Hospital_Bed_NN_01a",
        "SM_IV_Stand_01a",
        "SM_Brick_01a",
        "SM_Brick_01b",
        "SM_Crumpled_Paper_01a",
        "SM_Crumpled_Paper_01c",
        "SM_Fluoresent_Light_Tube_01a",
        "SM_Desk_Lamp_01a",
        "SM_Air_Conditioner_01a",
    };

    // ── Menu items ─────────────────────────────────────────────────────────

    [MenuItem("Wasteland/Dress World (Atmosphere + Scatter)")]
    public static void DressAll()
    {
        if (!ConfirmScene()) return;
        AtmospherePass();
        ScatterPass();
        Debug.Log("[WastelandDresser] Full dress complete — atmosphere + scatter.");
    }

    [MenuItem("Wasteland/Dress World (Atmosphere Only)")]
    public static void DressAtmosphere()
    {
        if (!ConfirmScene()) return;
        AtmospherePass();
        Debug.Log("[WastelandDresser] Atmosphere pass complete.");
    }

    [MenuItem("Wasteland/Dress World (Scatter Only)")]
    public static void DressScatter()
    {
        if (!ConfirmScene()) return;
        ScatterPass();
        Debug.Log("[WastelandDresser] Scatter pass complete.");
    }

    [MenuItem("Wasteland/Dress World (Clear All Dressing)")]
    public static void ClearDressing()
    {
        var root = GameObject.Find(DresserRoot);
        if (root != null)
        {
            Object.DestroyImmediate(root);
            Debug.Log("[WastelandDresser] Cleared all dressing.");
        }
        else
        {
            Debug.Log("[WastelandDresser] Nothing to clear.");
        }

        // Also reset atmosphere so you can see the terrain
        RenderSettings.fog = false;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Skybox;
    }

    [MenuItem("Wasteland/Dress World (Toggle Fog)")]
    public static void ToggleFog()
    {
        RenderSettings.fog = !RenderSettings.fog;
        Debug.Log($"[WastelandDresser] Fog {(RenderSettings.fog ? "ON" : "OFF")}");
    }

    // ── Atmosphere pass ────────────────────────────────────────────────────

    static void AtmospherePass()
    {
        // 1. Render settings — fog, ambient, skybox
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.45f, 0.42f, 0.38f); // dusty tan-gray
        RenderSettings.fogDensity = 0.012f;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.52f, 0.48f, 0.42f);    // warm gray
        RenderSettings.ambientEquatorColor = new Color(0.38f, 0.35f, 0.30f); // dusty brown
        RenderSettings.ambientGroundColor = new Color(0.18f, 0.16f, 0.14f);  // dark earth

        // 2. Directional light — low, warm, long shadows
        var sun = FindOrCreateSun();
        sun.color = new Color(1.0f, 0.85f, 0.65f); // warm orange
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.transform.rotation = Quaternion.Euler(35f, -30f, 0f); // low angle

        // 3. Skybox — procedural wasteland sky
        var skyMat = new Material(Shader.Find("Skybox/Procedural"));
        skyMat.SetFloat("_SunSize", 0.08f);
        skyMat.SetFloat("_SunSizeConvergence", 3f);
        skyMat.SetFloat("_AtmosphereThickness", 1.4f); // thicker = more orange
        skyMat.SetFloat("_Exposure", 1.1f);
        skyMat.SetColor("_SkyTint", new Color(0.55f, 0.5f, 0.45f)); // dusty tint
        skyMat.SetColor("_GroundColor", new Color(0.25f, 0.22f, 0.18f));
        RenderSettings.skybox = skyMat;

        // 4. Post-processing volume — color grading + bloom + vignette
        SetupPostProcessing();

        // 5. Additional fill lights for god-ray feel
        CreateFillLights();

        Debug.Log("[WastelandDresser] Atmosphere pass done.");
    }

    static Light FindOrCreateSun()
    {
        // Look for existing directional light
        var existing = Object.FindObjectsByType<Light>(FindObjectsInactive.Include)
            .FirstOrDefault(l => l.type == LightType.Directional);
        if (existing != null) return existing;

        var go = new GameObject("Wasteland Sun");
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        return light;
    }

    static void SetupPostProcessing()
    {
        // Find or create global volume
        var volume = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include)
            .FirstOrDefault(v => v.isGlobal);
        if (volume == null)
        {
            var go = new GameObject("Wasteland Post Volume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
        }

        var profile = volume.sharedProfile;
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;
        }

        // Color Adjustments — desaturate, warm shift
        if (!profile.TryGet<ColorAdjustments>(out var colorAdj))
            colorAdj = profile.Add<ColorAdjustments>();
        colorAdj.postExposure.Override(0.1f);
        colorAdj.contrast.Override(15f);
        colorAdj.saturation.Override(-25f); // desaturated wasteland
        colorAdj.colorFilter.Override(new Color(1.0f, 0.92f, 0.82f)); // warm tint

        // Bloom — subtle glow
        if (!profile.TryGet<Bloom>(out var bloom))
            bloom = profile.Add<Bloom>();
        bloom.intensity.Override(0.3f);
        bloom.threshold.Override(0.9f);
        bloom.scatter.Override(0.5f);

        // Vignette — dark corners
        if (!profile.TryGet<Vignette>(out var vignette))
            vignette = profile.Add<Vignette>();
        vignette.intensity.Override(0.35f);
        vignette.smoothness.Override(0.4f);

        // Tonemapping — ACES for filmic look
        if (!profile.TryGet<Tonemapping>(out var tonemap))
            tonemap = profile.Add<Tonemapping>();
        tonemap.mode.Override(TonemappingMode.ACES);

        // Lift Gamma Gain — crush shadows, lift midtones slightly
        if (!profile.TryGet<LiftGammaGain>(out var lgg))
            lgg = profile.Add<LiftGammaGain>();
        lgg.lift.Override(new Vector4(0.02f, 0.01f, 0.0f, 0f));
        lgg.gamma.Override(new Vector4(0.0f, 0.0f, 0.0f, 5f));
        lgg.gain.Override(new Vector4(0.05f, 0.03f, 0.0f, 0f));

        // Depth of Field — slight background blur
        if (!profile.TryGet<DepthOfField>(out var dof))
            dof = profile.Add<DepthOfField>();
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(30f);
        dof.gaussianEnd.Override(80f);
        dof.gaussianMaxRadius.Override(1.0f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }

    static void CreateFillLights()
    {
        // Remove old fill lights
        var old = GameObject.Find("WastelandFillLights");
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject("WastelandFillLights");
        root.transform.SetParent(GetOrCreateRoot(), false);

        // 4 low-intensity point lights at cardinal points for ambient bounce
        var terrain = GetTerrain();
        float cx = terrain != null ? terrain.GetPosition().x + terrain.terrainData.size.x * 0.5f : 300f;
        float cz = terrain != null ? terrain.GetPosition().z + terrain.terrainData.size.z * 0.5f : 300f;

        for (int i = 0; i < 4; i++)
        {
            float ang = i * 90f * Mathf.Deg2Rad;
            var pos = new Vector3(cx + Mathf.Cos(ang) * 150f, 0f, cz + Mathf.Sin(ang) * 150f);
            pos = SnapToTerrain(pos) + Vector3.up * 25f;

            var go = new GameObject($"FillLight_{i}");
            go.transform.SetParent(root.transform, false);
            go.transform.position = pos;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.9f, 0.75f, 0.55f); // warm bounce
            light.intensity = 0.4f;
            light.range = 120f;
            light.shadows = LightShadows.None;
        }
    }

    // ── Scatter pass ───────────────────────────────────────────────────────

    static void ScatterPass()
    {
        var root = GetOrCreateRoot();

        // Clear old scatter
        var old = GameObject.Find("WastelandScatter");
        if (old != null) Object.DestroyImmediate(old);
        var scatterRoot = new GameObject("WastelandScatter").transform;
        scatterRoot.SetParent(root, false);

        // 1. Dead trees — Tier 2+ basins (Greenbelt → Wasteland)
        ScatterPrefabs(
            LoadPrefabs(DeadTreeFolder, DeadTreeNames),
            count: 45,
            minRadius: 170f, maxRadius: 500f,
            parent: scatterRoot,
            namePrefix: "DeadTree",
            scaleRange: (0.8f, 1.4f),
            alignToTerrain: true
        );

        // 2. Ruined walls / buildings — Tier 2-3 basins (Barren → Scorched)
        ScatterPrefabs(
            LoadPrefabs(RuinFolder, RuinNames),
            count: 30,
            minRadius: 170f, maxRadius: 320f,
            parent: scatterRoot,
            namePrefix: "Ruin",
            scaleRange: (0.7f, 1.3f),
            alignToTerrain: true
        );

        // 3. Scrap nodes — EXCLUDED (broken prefab references)
        // Skipping entirely — the prefabs point to missing source models

        // 4. Cyberpunk ruins — Tier 2-3 basins (post-apoc city feel)
        ScatterPrefabs(
            LoadPrefabs(CyberpunkFolder, CyberpunkNames),
            count: 20,
            minRadius: 170f, maxRadius: 300f,
            parent: scatterRoot,
            namePrefix: "CyberRuin",
            scaleRange: (0.8f, 1.5f),
            alignToTerrain: true
        );

        // 5. Military camp — Tier 2-3 basins (outposts)
        ScatterPrefabs(
            LoadPrefabs(MilitaryFolder, MilitaryNames),
            count: 15,
            minRadius: 180f, maxRadius: 280f,
            parent: scatterRoot,
            namePrefix: "Military",
            scaleRange: (0.9f, 1.3f),
            alignToTerrain: true
        );

        // 6. Hospital debris — Tier 1-2 basins (near center but not in town)
        ScatterPrefabs(
            LoadPrefabs(HospitalFolder, HospitalNames),
            count: 12,
            minRadius: 70f, maxRadius: 160f,
            parent: scatterRoot,
            namePrefix: "Hospital",
            scaleRange: (0.7f, 1.2f),
            alignToTerrain: true
        );

        // 7. Ground detail — Tier 1-2 basins (near center, light clutter)
        var foliage = LoadFoliageFBX();
        if (foliage.Count > 0)
        {
            ScatterPrefabs(
                foliage,
                count: 60,
                minRadius: 70f, maxRadius: 200f,
                parent: scatterRoot,
                namePrefix: "GroundDetail",
                scaleRange: (0.5f, 1.0f),
                alignToTerrain: true
            );
        }

        Debug.Log("[WastelandDresser] Scatter pass done.");
    }

    static List<GameObject> LoadPrefabs(string folder, string[] names)
    {
        var list = new List<GameObject>();
        foreach (var name in names)
        {
            var path = $"{folder}/{name}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) list.Add(prefab);
        }
        return list;
    }

    static List<GameObject> LoadFoliageFBX()
    {
        var list = new List<GameObject>();
        var guids = AssetDatabase.FindAssets("t:GameObject", new[] { FoliageFolder });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".fbx") || path.EndsWith(".FBX"))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) list.Add(go);
            }
        }
        return list;
    }

    static void ScatterPrefabs(
        List<GameObject> prefabs,
        int count,
        float minRadius,
        float maxRadius,
        Transform parent,
        string namePrefix,
        (float min, float max) scaleRange,
        bool alignToTerrain,
        bool stripScripts = false)
    {
        if (prefabs == null || prefabs.Count == 0)
        {
            // Silently skip empty categories (e.g., excluded scrap nodes)
            return;
        }

        for (int i = 0; i < count; i++)
        {
            var prefab = prefabs[Random.Range(0, prefabs.Count)];
            var pos = RandomRingPosition(minRadius, maxRadius);
            pos = SnapToTerrain(pos);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = $"{namePrefix}_{i:D3}";
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            float scale = Random.Range(scaleRange.min, scaleRange.max);
            go.transform.localScale = Vector3.one * scale;

            if (alignToTerrain)
                AlignToTerrain(go);

            if (stripScripts)
                StripScripts(go);

            // Normalize height if the model is wildly off-scale
            NormalizeHeight(go, namePrefix);
        }
    }

    static Vector3 RandomRingPosition(float minRadius, float maxRadius)
    {
        // Center on the ACTUAL ring center from the heightmap, not geometric center
        var terrain = GetTerrain();
        if (terrain == null) return Vector3.zero;

        var tp = terrain.transform.position;
        var sz = terrain.terrainData.size;
        // Ring center from WorldTierTerrainBuilder: CenterU=636/1254=0.507, CenterV=1-942/1254=0.249
        float centerX = tp.x + 0.507f * sz.x;
        float centerZ = tp.z + 0.249f * sz.z;

        // Wall radii in meters (from heightmap fractions × terrain width ~600m)
        // Wall T1→T2: 149m, T2→T3: 230m, T3→T4: 310m, T4→T5: 390m
        // Island (tutorial): < 48m, Lagoon: < 60m
        float[] wallRadii = { 149f, 230f, 310f, 390f };
        float lagoonRadius = 60f;

        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = Random.Range(minRadius, maxRadius);

        // Keep objects OUT of the tutorial island (center) and OUT of walls
        // If radius falls inside a wall band, push it to the nearest basin
        foreach (var wallR in wallRadii)
        {
            float wallThickness = 15f; // approximate wall band width
            if (Mathf.Abs(radius - wallR) < wallThickness)
            {
                // Push to nearest basin (inside or outside)
                radius = radius < wallR ? wallR - wallThickness - 5f : wallR + wallThickness + 5f;
            }
        }

        // Never spawn in the tutorial island or lagoon
        if (radius < lagoonRadius)
            radius = lagoonRadius + 10f;

        return new Vector3(centerX + Mathf.Cos(angle) * radius, 0f, centerZ + Mathf.Sin(angle) * radius);
    }

    static Terrain GetTerrain()
    {
        var ground = GameObject.Find("Ground");
        return ground != null ? ground.GetComponent<Terrain>() : Terrain.activeTerrain;
    }

    static void AlignToTerrain(GameObject go)
    {
        // Use renderer bounds to sit object on terrain surface
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        float bottomOffset = bounds.min.y - go.transform.position.y;
        go.transform.position = new Vector3(
            go.transform.position.x,
            go.transform.position.y - bottomOffset,
            go.transform.position.z
        );
    }

    static void NormalizeHeight(GameObject go, string category = "")
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        float height = bounds.size.y;
        float width = bounds.size.x;
        float depth = bounds.size.z;
        float maxDim = Mathf.Max(height, width, depth);

        // Different target sizes per category
        float targetSize = category switch
        {
            "Ruin" => 50f,      // Castle walls / buildings — keep them big
            "DeadTree" => 12f,  // Trees — 8-15m is realistic
            "Military" => 8f,   // Military structures — moderate size
            "CyberRuin" => 6f,  // Cyberpunk debris — small to medium
            "Hospital" => 4f,   // Hospital props — small interior items
            "Scrap" => 5f,      // Scrap nodes — small visual filler
            "GroundDetail" => 2f, // Ground clutter — tiny
            _ => 5f
        };

        if (maxDim > targetSize * 2f || maxDim < targetSize * 0.3f)
        {
            float factor = targetSize / Mathf.Max(maxDim, 0.01f);
            go.transform.localScale *= factor;
            // Only warn for extremely oversized objects (likely broken scale)
            if (maxDim > 1000f)
                Debug.LogWarning($"[WastelandDresser] Scaled down broken-scale object: {go.name} (was {maxDim:F0}m)");
        }
    }

    static void StripScripts(GameObject go)
    {
        // Remove all MonoBehaviours so scrap nodes are purely visual
        var behaviours = go.GetComponentsInChildren<MonoBehaviour>();
        foreach (var b in behaviours)
        {
            if (b != null) Object.DestroyImmediate(b);
        }
        // Remove colliders so player doesn't get stuck on visual scrap
        var colliders = go.GetComponentsInChildren<Collider>();
        foreach (var c in colliders)
        {
            if (c != null) Object.DestroyImmediate(c);
        }
    }

    // ── Utility ────────────────────────────────────────────────────────────

    static Transform GetOrCreateRoot()
    {
        var root = GameObject.Find(DresserRoot);
        if (root == null)
        {
            root = new GameObject(DresserRoot);
            Undo.RegisterCreatedObjectUndo(root, "Create WastelandDressing");
        }
        return root.transform;
    }

    static Vector3 SnapToTerrain(Vector3 pos)
    {
        var terrain = GetTerrain();
        if (terrain != null)
        {
            // SampleHeight returns local height — must add terrain's world Y position
            float h = terrain.SampleHeight(pos) + terrain.transform.position.y;
            return new Vector3(pos.x, h, pos.z);
        }
        if (Physics.Raycast(pos + Vector3.up * 500f, Vector3.down, out var hit, 1000f,
                ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point;
        }
        return pos;
    }

    static bool ConfirmScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name != "MainWorld3D")
        {
            Debug.LogWarning($"[WastelandDresser] Active scene is '{scene.name}', expected 'MainWorld3D'. Run anyway? (Check console)");
            return EditorUtility.DisplayDialog(
                "WastelandDresser",
                $"Active scene is '{scene.name}', not 'MainWorld3D'.\n\nContinue anyway?",
                "Yes", "No");
        }
        return true;
    }
}
