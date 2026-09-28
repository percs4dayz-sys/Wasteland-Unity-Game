using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Places ONE boss arena per tier (not per pass), at a landmark location inside the tier.
/// The existing TierGate system handles the passes — these bosses guard the tier's objective.
///
/// Each arena:
///   - Circular wall ring (BanditsValley walls) with one opening
///   - Boss spawn inside (CombatTarget + Enemy3D, isBoss=true)
///   - Warning trigger at the opening (DialogueUI)
///
/// Menu: Wasteland > Place Boss Arenas
/// </summary>
public static class BossArenaPlacer
{
    const string ArenaRoot = "BossArenas";

    // Tier-appropriate wall folders
    const string MedievalFolder = "Assets/LeartesStudios/Medieval Village/Art/Prefabs";
    const string WitchFolder    = "Assets/LeartesStudios/WitchVillage/Art/Prefabs";
    const string CaveFolder     = "Assets/Hivemind/CaveOfHiddenTomb/URP/Art/Prefabs";
    const string CyberFolder    = "Assets/Hivemind/CyberpunkCity/URP/Art/Prefabs";

    // Tier 1 (Greenbelt): Medieval castle walls
    static readonly string[] T1_Walls = { "SM_Castle_Wall_01", "SM_Castle_Wall_02", "SM_Castle_Wall_Corner" };
    static readonly string[] T1_Gates = { "SM_Castle_Gate_01", "SM_Castle_Gate_02" };

    // Tier 2 (Barren Plains): WitchVillage wooden walls
    static readonly string[] T2_Walls = { "SM_PlankWall_Wall1", "SM_PlankWall_Wall2", "SM_PlankWall_Wall3", "SM_PlankWall_Wall4", "SM_PlankWall_Wall5" };
    static readonly string[] T2_Gates = { "SM_PlankWall_WallDoor1" };

    // Tier 3 (Scorched Highlands): CaveOfHiddenTomb rock/temple walls
    static readonly string[] T3_Walls = { "SM_RockWall_A", "SM_RockWall_B", "SM_RockWall_C", "SM_RockWall_D", "SM_Wall_Aztec_B", "SM_Wall_Aztec_C" };
    static readonly string[] T3_Gates = { "SM_Wall_Aztec_D", "SM_Wall_Aztec_E" };

    // Tier 4 (Frozen Wastes): Cave walls (icy look)
    static readonly string[] T4_Walls = { "SM_RockWall_A", "SM_RockWall_B", "SM_RockWall_C" };
    static readonly string[] T4_Gates = { "SM_RockWall_D" };

    // Tier 5 (Wasteland): Cyberpunk city ruins
    static readonly string[] T5_Walls = { "SM_Building_Column_A", "SM_Building_Column_C", "SM_Beam_Concrete_01", "SM_Beam_Concrete_02" };
    static readonly string[] T5_Gates = { "SM_Building_Column_A_1" };

    // Per-tier asset mapping
    static readonly Dictionary<int, (string folder, string[] walls, string[] gates)> TierAssets = new()
    {
        { 2, (WitchFolder,   T2_Walls, T2_Gates) },
        { 3, (CaveFolder,    T3_Walls, T3_Gates) },
        { 4, (CaveFolder,    T4_Walls, T4_Gates) },
        { 5, (CyberFolder,   T5_Walls, T5_Gates) },
    };

    // Arena config: tier → (radius, bossHP, bossName, color)
    static readonly Dictionary<int, (float radius, int hp, string name, Color color)> ArenaConfig = new()
    {
        { 2, ( 25f,  80, "Greenbelt Warden",  new Color(0.2f, 0.8f, 0.2f)) },
        { 3, ( 30f, 150, "Barren Reaver",     new Color(0.8f, 0.6f, 0.2f)) },
        { 4, ( 35f, 250, "Scorched Tyrant",   new Color(0.8f, 0.3f, 0.1f)) },
        { 5, ( 40f, 400, "Wasteland Behemoth",new Color(0.9f, 0.1f, 0.3f)) },
    };

    // Pass bearings from WorldTierTerrainBuilder (deg from north, +east)
    static readonly float[][] PassBearings =
    {
        new[] { -95f,   0f, 100f },   // into Tier 2
        new[] { -55f,  48f        },  // into Tier 3
        new[] { -38f,  28f        },  // into Tier 4
        new[] { -16f,  16f        },  // into Tier 5
    };

    // Wall radii in meters (from heightmap)
    static readonly float[] WallR = { 149f, 230f, 310f, 390f };

    [MenuItem("Wasteland/Place Boss Arenas")]
    public static void PlaceArenas()
    {
        var terrain = GetTerrain();
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("No Terrain", "Open MainWorld3D scene first.", "OK");
            return;
        }

        // Clear old arenas
        var old = GameObject.Find(ArenaRoot);
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject(ArenaRoot).transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Create Boss Arenas");

        var ringCenter = GetRingCenter(terrain);
        int totalBosses = 0;
        int totalWired = 0;

        for (int tier = 2; tier <= 5; tier++)
        {
            int wallIdx = tier - 2;
            float wallRadius = WallR[wallIdx];
            var config = ArenaConfig[tier];

            // One boss per tier, placed INSIDE its own tier's basin (not the next tier up)
            // wallRadius is the OUTER wall of this tier — we want to be just inside it
            float basinMid = wallRadius - 40f; // 40m INSIDE the tier's basin from its outer wall
            float landmarkBearing = 0f; // Due north — the "deepest" part of each tier

            var arena = BuildArena(root, terrain, ringCenter, basinMid, landmarkBearing, tier, config);
            if (arena != null)
            {
                totalBosses++;
                // Wire this tier's boss to ALL existing TierGates for this tier
                totalWired += WireGatesToBoss(tier, arena.GetComponentInChildren<CombatTarget>());
            }
        }

        Debug.Log($"[BossArenaPlacer] Placed {totalBosses} boss arenas, wired {totalWired} gates.");
    }

    [MenuItem("Wasteland/Clear Boss Arenas")]
    public static void ClearArenas()
    {
        var root = GameObject.Find(ArenaRoot);
        if (root != null)
        {
            Object.DestroyImmediate(root);
            Debug.Log("[BossArenaPlacer] Cleared all arenas.");
        }
    }

    static GameObject BuildArena(Transform parent, Terrain terrain, Vector3 center, float wallRadius, float bearingDeg, int tier, (float radius, int hp, string name, Color color) config)
    {
        // Position: on the wall at the pass bearing
        float rad = bearingDeg * Mathf.Deg2Rad;
        var arenaCenter = new Vector3(
            center.x + Mathf.Sin(rad) * wallRadius,
            0f,
            center.z + Mathf.Cos(rad) * wallRadius
        );
        arenaCenter = SnapToTerrain(arenaCenter);

        var arenaGo = new GameObject($"Arena_T{tier}_{bearingDeg:0}deg");
        arenaGo.transform.SetParent(parent);
        arenaGo.transform.position = arenaCenter;

        // 1. Build wall ring with opening facing outward (toward previous tier)
        BuildWallRing(arenaGo.transform, terrain, config.radius, bearingDeg, tier);

        // 2. Spawn boss inside
        var boss = SpawnBoss(arenaGo.transform, terrain, config, tier, bearingDeg);

        // 3. Place warning trigger at the opening (no TierGate — bosses are landmarks, gates stay at passes)
        if (boss != null)
        {
            PlaceWarning(arenaGo.transform, terrain, boss, tier, bearingDeg, config);
        }

        return arenaGo;
    }

    static void BuildWallRing(Transform parent, Terrain terrain, float radius, float openingBearing, int tier)
    {
        // Load tier-appropriate wall prefabs
        if (!TierAssets.TryGetValue(tier, out var assets))
        {
            Debug.LogWarning($"[BossArenaPlacer] No assets defined for tier {tier}");
            return;
        }

        var wallPrefabs = LoadPrefabsFrom(assets.folder, assets.walls);
        if (wallPrefabs.Count == 0)
        {
            Debug.LogWarning($"[BossArenaPlacer] No wall prefabs loaded for tier {tier} from {assets.folder}");
            return;
        }

        // Wall count: enough to form a solid ring with minimal gaps
        // Assume each wall piece is ~4m wide when normalized
        int wallCount = Mathf.RoundToInt(radius * 1.6f); // ~1.6 walls per meter = solid ring
        float angleStep = 360f / wallCount;

        for (int i = 0; i < wallCount; i++)
        {
            float angle = i * angleStep;
            // Skip the opening (±15° around the pass bearing) — narrower gap, more imposing
            float diff = Mathf.Abs(Mathf.DeltaAngle(angle, openingBearing));
            if (diff < 15f) continue;

            float rad = angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(rad) * radius, 0f, Mathf.Cos(rad) * radius);
            pos = parent.TransformPoint(pos);
            pos = SnapToTerrain(pos);

            var prefab = wallPrefabs[Random.Range(0, wallPrefabs.Count)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = $"ArenaWall_T{tier}_{i:D2}";
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            // Face TANGENT to the circle (perpendicular to radius), not inward
            // This makes walls form a proper ring instead of facing the center
            var tangent = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));
            go.transform.rotation = Quaternion.LookRotation(tangent);

            // Normalize scale — walls should be ~4m tall, ~4m wide segments
            NormalizeHeight(go, 4f);
        }
    }

    static GameObject SpawnBoss(Transform parent, Terrain terrain, (float radius, int hp, string name, Color color) config, int tier, float bearing)
    {
        // Boss spawns slightly off-center toward the back of the arena
        float backRad = (bearing + 180f) * Mathf.Deg2Rad;
        var bossPos = new Vector3(Mathf.Sin(backRad) * config.radius * 0.3f, 0f, Mathf.Cos(backRad) * config.radius * 0.3f);
        bossPos = parent.TransformPoint(bossPos);
        bossPos = SnapToTerrain(bossPos);

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = $"BOSS_{config.name}";
        go.transform.SetParent(parent, false);
        go.transform.position = bossPos + Vector3.up * 1f;
        go.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f);

        // Color the boss
        var renderer = go.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = config.color;
        renderer.sharedMaterial = mat;

        // Combat stats
        var ct = go.AddComponent<CombatTarget>();
        ct.maxHP = config.hp;
        ct.attackLevel = 20 + tier * 10;
        ct.defenceLevel = 15 + tier * 8;
        ct.maxDamage = 8 + tier * 4;
        ct.isAggressive = true;
        ct.isBoss = true;

        var e3d = go.AddComponent<Enemy3D>();
        e3d.aggroRange = 25f;
        e3d.moveSpeed = 4.5f;
        e3d.attackCooldown = 2.5f;
        e3d.respawnSeconds = 120f;
        e3d.windupSeconds = 0.6f;

        return go;
    }

    static void PlaceWarning(Transform parent, Terrain terrain, GameObject boss, int tier, float bearing, (float radius, int hp, string name, Color color) config)
    {
        // Warning trigger sits at the opening, facing outward
        float rad = bearing * Mathf.Deg2Rad;
        var warningPos = new Vector3(Mathf.Sin(rad) * config.radius, 0f, Mathf.Cos(rad) * config.radius);
        warningPos = parent.TransformPoint(warningPos);
        warningPos = SnapToTerrain(warningPos) + Vector3.up * 2f;

        var warningGo = new GameObject($"BossWarning_T{tier}");
        warningGo.transform.SetParent(parent, false);
        warningGo.transform.position = warningPos;

        // Face outward (away from arena center)
        var lookDir = warningPos - parent.position;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.01f)
            warningGo.transform.rotation = Quaternion.LookRotation(lookDir);

        var warningBc = warningGo.AddComponent<BoxCollider>();
        warningBc.isTrigger = true;
        warningBc.size = new Vector3(12f, 6f, 4f);

        var warning = warningGo.AddComponent<BossWarningTrigger>();
        warning.bossName = config.name;
        warning.tier = tier;
    }

    static int WireGatesToBoss(int tier, CombatTarget boss)
    {
        if (boss == null) return 0;

        // Find all TierGates in the scene that lead into this tier
        var allGates = Object.FindObjectsByType<TierGate>(FindObjectsInactive.Include);
        int wired = 0;

        foreach (var gate in allGates)
        {
            if (gate.targetTier != tier) continue;

            gate.requiredBoss = boss;
            gate.bossAliveMessage = $"The gate will not move. The {boss.gameObject.name} still draws breath.";
            gate.bossDefeatedMessage = $"The {boss.gameObject.name} is dead. The gate releases its hold.";
            EditorUtility.SetDirty(gate);
            wired++;
        }

        if (wired > 0)
            Debug.Log($"[BossArenaPlacer] Wired {wired} gates to {boss.gameObject.name} (Tier {tier})");

        return wired;
    }

    static List<GameObject> LoadPrefabsFrom(string folder, string[] names)
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

    static void NormalizeHeight(GameObject go, float targetSize)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxDim > targetSize * 2f || maxDim < targetSize * 0.3f)
        {
            float factor = targetSize / Mathf.Max(maxDim, 0.01f);
            go.transform.localScale *= factor;
        }
    }

    static Terrain GetTerrain()
    {
        var ground = GameObject.Find("Ground");
        return ground != null ? ground.GetComponent<Terrain>() : Terrain.activeTerrain;
    }

    static Vector3 GetRingCenter(Terrain terrain)
    {
        var tp = terrain.transform.position;
        var sz = terrain.terrainData.size;
        return new Vector3(tp.x + 0.507f * sz.x, 0f, tp.z + 0.249f * sz.z);
    }

    static Vector3 SnapToTerrain(Vector3 pos)
    {
        var terrain = GetTerrain();
        if (terrain != null)
        {
            float h = terrain.SampleHeight(pos) + terrain.transform.position.y;
            return new Vector3(pos.x, h, pos.z);
        }
        return pos;
    }
}

/// <summary>
/// Warning trigger that shows a confirmation before entering a boss arena.
/// Uses the game's existing message UI — click to dismiss, then walk in or turn back.
/// </summary>
public class BossWarningTrigger : MonoBehaviour
{
    public string bossName = "Unknown";
    public int tier = 2;
    public TierGate gate;

    bool _shown;

    void OnTriggerEnter(Collider other)
    {
        if (_shown) return;
        var player = other.GetComponentInParent<PlayerEntity>();
        if (player == null) return;

        _shown = true;

        // Use the game's existing dialogue system — one line per click, same as NPC chat
        if (DialogueUI.Instance != null)
        {
            DialogueUI.Instance.StartDialogue(
                "⚠️ Boss Warning",
                $"You approach the arena of the {bossName}.",
                $"This Tier {tier} guardian will not let you pass easily.",
                $"Step forward to fight, or turn back to prepare."
            );
        }
        else
        {
            Debug.Log($"[BossWarning] {bossName} (Tier {tier}) — approach with caution.");
        }
    }

    void OnTriggerExit(Collider other)
    {
        // Reset so the warning shows again if the player leaves and returns
        var player = other.GetComponentInParent<PlayerEntity>();
        if (player != null)
            _shown = false;
    }
}
