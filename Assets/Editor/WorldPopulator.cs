using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// One-click world population: reads the WorldContent markers placed by WorldContentPlanner
/// and replaces each one with the correct prefab — tiered resource nodes, NPCs, enemies,
/// crafting stations, banks, water surfaces, foliage, and buildings from your asset packs.
///
/// Run WorldContentPlanner first to lay out the markers, then run this to populate them.
///
/// Menu:  Wasteland > Populate World From Content Plan
/// </summary>
public static class WorldPopulator
{
    // ── Config ───────────────────────────────────────────────────────────
    const string WorldContentRoot = "WorldContent";
    const string PopulationRoot  = "WorldPopulation";
    const string NodesFolder     = "Assets/_Wasteland/Nodes";
    const string NpcFolder       = "Assets/_TodaysAssets/NPCs";
    const string FoliageFolder   = "Assets/_TodaysAssets/Trees";
    const string WorkshopFolder  = "Assets/_TodaysAssets/SplitModels/Rusted-Armory-Workshop";

    const float TerrainSampleRadius = 2f;
    const int   FoliagePerDab       = 8;
    const int   FoliageDabs         = 60;
    const float FoliageRadius       = 180f;
    const float WaterYOffset        = 0.15f;

    // ── Prefab name lookup: skill → tier → prefab filename (without .prefab) ──
    static readonly Dictionary<string, Dictionary<int, string>> TieredPrefabs = new()
    {
        ["EXCAVATION"] = new() {
            {1, "Junk Pile (T1)"},
            {2, "abandoned car (T2)"},
            {3, "tech dumpster (T3)"},
            {4, "downed drones pile (T4)"},
            {5, "abandoned robotics (T5)"},
        },
        ["SALVAGING"] = new() {
            {1, "Dead Scrub & Brush (T1)"},
            {2, "Irradiated Thicket (T2)"},
            {3, "fossilized deadwood (T3)"},
            {4, "Petrified Forest (T4)"},
            {5, "Ancient Gnarlwood (T5)"},
        },
        ["SCAVENGING"] = new() {
            {1, "Fishing Spot (T1)"},
            {2, "Fishing Spot (T2)"},
            {3, "Fishing Spot (T3)"},
            {4, "Fishing Spot (T4)"},
            {5, "Fishing Spot (T5)"},
        },
    };

    // ── Tier stat scaling ─────────────────────────────────────────────────
    static readonly Dictionary<int, (int levelReq, int toolId, int dropId, int xp, int maxHits, int minYield, int maxYield)> TierStats = new()
    {
        {1, ( 1,  4, 40,  25,  3, 1, 3)},
        {2, (10, 10, 41,  45,  5, 2, 5)},
        {3, (25, 16, 42,  80,  8, 3, 8)},
        {4, (45, 22, 43, 140, 12, 5, 12)},
        {5, (70, 28, 44, 250, 18, 8, 20)},
    };

    // ── Enemy tier stats ──────────────────────────────────────────────────
    static readonly Dictionary<int, (int hp, int atk, int def, int dmg, float aggro, float speed)> EnemyStats = new()
    {
        {2, (20,  5,  3,  3,  9f, 3.2f)},
        {3, (40, 10,  6,  5, 11f, 3.8f)},
        {4, (70, 18, 10,  8, 14f, 4.5f)},
        {5, (120, 30, 18, 14, 18f, 5.5f)},
    };

    // NPC names for variety
    static readonly string[][] NpcNames = {
        new[] {"Shopkeeper", "Quartermaster", "Scavenger", "Trader", "Survivor", "Mechanic",
               "Doctor", "Gunsmith", "Cook", "Guard"},
        new[] {"Old Mara", "Rusty Joe", "Sister Ash", "Big Hank", "Whisper", "Gearbox",
               "Doc Morrow", "Scrap Dealer", "Flame-Keeper", "Watchman"},
    };

    // ── Entry point ───────────────────────────────────────────────────────
    [MenuItem("Wasteland/Archived/Populate World From Content Plan", false, 9000)]
    public static void Populate()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.GetActiveScene();
        var contentRoot = GameObject.Find(WorldContentRoot);
        if (contentRoot == null)
        {
            EditorUtility.DisplayDialog("No Content Plan Found",
                $"Run 'Wasteland > Plan Main World Content' first to place the markers,\n" +
                "then run this tool to replace them with real prefabs.",
                "OK");
            return;
        }

        // Collect all markers before destroying anything
        var markers = contentRoot.GetComponentsInChildren<Transform>(true)
            .Where(t => t != contentRoot.transform)
            .Select(t => (t, t.name, t.position, t.parent?.name ?? ""))
            .ToList();

        if (markers.Count == 0)
        {
            EditorUtility.DisplayDialog("Empty Content Plan",
                "The WorldContent group has no markers. Run 'Plan Main World Content' first.",
                "OK");
            return;
        }

        // Remove any previous population
        var oldPop = GameObject.Find(PopulationRoot);
        if (oldPop != null) Object.DestroyImmediate(oldPop);

        var root = new GameObject(PopulationRoot).transform;
        int placed = 0, skipped = 0;
        var summary = new List<string>();

        // Pre-load asset caches
        var npcModels = LoadNpcModels();
        var foliagePrefabs = LoadFoliagePrefabs();
        var buildingPrefabs = FindBuildingPrefabs();
        var waterPrefab = FindWaterPrefab();

        try
        {
            foreach (var (t, name, pos, parentName) in markers)
            {
                if (t == null) continue;

                EditorUtility.DisplayProgressBar("Populating World",
                    $"Placing: {name}", (float)placed / Mathf.Max(markers.Count, 1));

                string cleanName = name.Replace("MARKER - ", "").Trim();
                bool isClusterDot = name.Contains("(cluster node)");
                bool isLabelMarker = name.StartsWith("MARKER - ");

                // Cluster dots are extra nodes in a group
                if (isClusterDot)
                {
                    // Parse the parent group name for tier/skill
                    var groupInfo = ParseParentGroup(parentName);
                    if (groupInfo.skill != null)
                    {
                        PlaceNodeAt(root, pos, groupInfo.skill, groupInfo.tier, isClusterDot);
                        placed++;
                    }
                    else skipped++;
                    continue;
                }

                if (!isLabelMarker) { skipped++; continue; }

                // ── Parse marker type ──
                if (cleanName.Contains("SPAWN") || cleanName.Contains("HOME"))
                {
                    // Spawn point — create a visible marker, don't replace player
                    var go = CreatePillar(root, "SPAWN POINT", pos, new Color(0.35f, 0.85f, 0.40f), 2f);
                    go.name = "SPAWN POINT";
                    placed++;
                    summary.Add("  🏠 Spawn point marked");
                }
                else if (cleanName.StartsWith("BANK"))
                {
                    PlaceBank(root, pos);
                    placed++;
                    summary.Add($"  📦 Bank crate at {FormatPos(pos)}");
                }
                else if (cleanName.StartsWith("FURNACE"))
                {
                    PlaceCraftingStation(root, pos, StationType.Furnace);
                    placed++;
                    summary.Add($"  🔥 Furnace at {FormatPos(pos)}");
                }
                else if (cleanName.Contains("COOKING FIRE") || cleanName.Contains("COOKING"))
                {
                    PlaceCraftingStation(root, pos, StationType.CookingFire);
                    placed++;
                    summary.Add($"  🍳 Cooking Fire at {FormatPos(pos)}");
                }
                else if (cleanName.Contains("BENCH") || cleanName.Contains("GUNSMITHING") || cleanName.Contains("WORKBENCH"))
                {
                    PlaceCraftingStation(root, pos, StationType.Workbench);
                    placed++;
                    summary.Add($"  🔧 Workbench at {FormatPos(pos)}");
                }
                else if (cleanName.StartsWith("NPC"))
                {
                    PlaceNpc(root, pos, cleanName, npcModels);
                    placed++;
                    summary.Add($"  👤 NPC at {FormatPos(pos)}");
                }
                else if (cleanName.StartsWith("AGGRO PACK"))
                {
                    int tier = ExtractTier(cleanName);
                    int count = ExtractGroupCount(cleanName, 3, 5);
                    PlaceEnemyPack(root, pos, tier, count);
                    placed += count;
                    summary.Add($"  💀 Aggro pack T{tier} ({count}) at {FormatPos(pos)}");
                }
                else if (cleanName.Contains("MINIBOSS"))
                {
                    PlaceBoss(root, pos, "MINIBOSS", isMiniBoss: true);
                    placed++;
                    summary.Add($"  👹 Miniboss at {FormatPos(pos)}");
                }
                else if (cleanName.Contains("MAJOR BOSS") || cleanName.Contains("ENDGAME"))
                {
                    PlaceBoss(root, pos, "MAJOR BOSS", isBoss: true);
                    placed++;
                    summary.Add($"  👑 MAJOR BOSS at {FormatPos(pos)}");
                }
                else if (cleanName.StartsWith("EXCAVATION") || cleanName.StartsWith("SALVAGING") || cleanName.StartsWith("SCAVENGING"))
                {
                    var info = ParseSkillMarker(cleanName);
                    int count = ExtractGroupCount(cleanName, 3, 5);
                    var summaryLabel = info.skill switch
                    {
                        "EXCAVATION" => "⛏️",
                        "SALVAGING"  => "🪓",
                        "SCAVENGING" => "🎣",
                        _            => "📦"
                    };

                    for (int i = 0; i < count; i++)
                    {
                        var nodePos = pos + new Vector3(
                            Random.Range(-3.5f, 3.5f), 0f, Random.Range(-3.5f, 3.5f));
                        // Raycast to terrain
                        nodePos = SnapToTerrain(nodePos);
                        PlaceNodeAt(root, nodePos, info.skill, info.tier, true);
                        placed++;

                        // For SCAVENGING (fishing), also place water
                        if (info.skill == "SCAVENGING" && i == 0 && waterPrefab != null)
                        {
                            PlaceWater(root, nodePos + Vector3.up * WaterYOffset, waterPrefab);
                        }
                    }

                    // Place water surface at the marker center for fishing spots
                    if (info.skill == "SCAVENGING" && waterPrefab != null)
                    {
                        PlaceWater(root, pos + Vector3.up * WaterYOffset, waterPrefab);
                    }

                    summary.Add($"  {summaryLabel} {info.skill} T{info.tier} x{count} at {FormatPos(pos)}");
                }
                else
                {
                    skipped++;
                }
            }

            // ── Scatter biome-specific foliage per ring ──
            int foliagePlaced = ScatterBiomeFoliage(root, foliagePrefabs);
            if (foliagePlaced > 0)
                summary.Add($"  🌿 {foliagePlaced} foliage placed across biomes");

            // ── Place biome transition props at ring boundaries ──
            int transPlaced = PlaceBiomeTransitions(root);
            if (transPlaced > 0)
                summary.Add($"  🚙 {transPlaced} transition props (barricades, debris)");

            // ── Place some buildings from packs (near hub and key points) ──
            int buildingsPlaced = ScatterBuildings(root, buildingPrefabs, 8);
            if (buildingsPlaced > 0)
                summary.Add($"  🏗️ {buildingsPlaced} structures placed");

            // ── Place tier gates at bridge chokepoints ──
            int gatesPlaced = PlaceTierGates(root);
            if (gatesPlaced > 0)
                summary.Add($"  🚧 {gatesPlaced} tier gates placed");

            // ── Destroy the marker group ──
            Object.DestroyImmediate(contentRoot);

            EditorSceneManager.MarkSceneDirty(scene);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // Results
        EditorUtility.DisplayDialog("World Populated!",
            $"✅ {placed} objects placed from your asset packs\n" +
            $"⏭️ {skipped} markers skipped\n\n" +
            string.Join("\n", summary) +
            $"\n\nThe marker group has been removed. SAVE (Ctrl+S) to keep this layout.\n\n" +
            $"You can re-run 'Plan Main World Content' and then 'Populate World' anytime.",
            "Let's go!");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PLACEMENT HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    static void PlaceNodeAt(Transform parent, Vector3 pos, string skill, int tier, bool usePrefab)
    {
        GameObject go = null;
        string prefabKey = "";

        if (usePrefab && TieredPrefabs.TryGetValue(skill, out var tiers) && tiers.TryGetValue(tier, out var name))
        {
            prefabKey = name;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NodesFolder}/{name}.prefab");
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.position = SnapToTerrain(pos);
                NeutralizeForeignScripts(go);
            }
        }

        // Fallback — colored primitive
        if (go == null)
        {
            var color = skill switch
            {
                "EXCAVATION" => new Color(0.55f, 0.62f, 0.72f),  // metal grey
                "SALVAGING"  => new Color(0.45f, 0.30f, 0.15f),  // wood brown
                "SCAVENGING" => new Color(0.20f, 0.40f, 0.50f),  // water blue
                _            => Color.grey
            };
            go = World3DBuilderCapsule($"{skill} T{tier} {prefabKey}", SnapToTerrain(pos), color);
            go.transform.SetParent(parent);
        }

        go.name = $"{skill}_T{tier}_{go.name.Replace("(Clone)", "").Trim()}";

        // Add/configure ResourceNode
        var node = go.GetComponent<ResourceNode>();
        if (node == null) node = go.AddComponent<ResourceNode>();

        var stats = TierStats.GetValueOrDefault(tier, TierStats[1]);
        node.skill = skill switch
        {
            "EXCAVATION" => Skill.Scrapping,
            "SALVAGING"  => Skill.Woodcutting,
            "SCAVENGING" => Skill.Fishing,
            _            => Skill.Scrapping
        };
        node.nodeType = skill switch
        {
            "EXCAVATION" => ResourceNodeType.RubblePile,
            "SALVAGING"  => ResourceNodeType.WoodenDebris,
            "SCAVENGING" => ResourceNodeType.WaterBarrel,
            _            => ResourceNodeType.RubblePile
        };
        node.levelRequired = stats.levelReq;
        node.requiredToolId = stats.toolId;
        node.dropItemId = stats.dropId;
        node.xpPerAction = stats.xp;
        node.maxHits = stats.maxHits;
        node.minYield = stats.minYield;
        node.maxYield = stats.maxYield;
        node.respawnTicks = tier <= 2 ? 8 : tier <= 4 ? 12 : 18;

        // Ensure collider
        if (go.GetComponentInChildren<Collider>() == null)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(1.5f, 1.5f, 1.5f);
            bc.center = Vector3.up * 0.75f;
        }
    }

    static void PlaceBank(Transform parent, Vector3 pos)
    {
        // Try to find a crate prefab from packs
        var cratePrefab = FindPackPrefab("crate", "chest", "container", "box");
        GameObject go;
        if (cratePrefab != null)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(cratePrefab, parent);
            go.transform.position = SnapToTerrain(pos);
            NeutralizeForeignScripts(go);
        }
        else
        {
            go = World3DBuilderBox("Bank Crate", SnapToTerrain(pos),
                new Vector3(1.2f, 1f, 1.2f), new Color(0.3f, 0.34f, 0.2f));
            go.transform.SetParent(parent);
        }
        go.name = "Bank Crate";
        if (go.GetComponent<BankingCrate>() == null)
            go.AddComponent<BankingCrate>();
        SetupCollider(go, new Vector3(1.2f, 1f, 1.2f));
    }

    static void PlaceCraftingStation(Transform parent, Vector3 pos, StationType type)
    {
        string[] keys = type switch
        {
            StationType.Furnace     => new[] { "furnace", "forge", "oven", "kiln" },
            StationType.CookingFire => new[] { "fire", "campfire", "firepit", "bonfire" },
            StationType.Workbench   => new[] { "bench", "workbench", "table", "anvil" },
            _                       => new[] { "table" }
        };
        var prefab = FindPackPrefab(keys) ?? FindInFolder(WorkshopFolder);
        GameObject go;
        if (prefab != null)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = SnapToTerrain(pos);
            NeutralizeForeignScripts(go);
        }
        else
        {
            var (size, color) = type switch
            {
                StationType.Furnace     => (new Vector3(1.4f, 1.6f, 1.4f), new Color(0.35f, 0.22f, 0.18f)),
                StationType.CookingFire => (new Vector3(1.2f, 0.8f, 1.2f), new Color(0.5f, 0.25f, 0.12f)),
                StationType.Workbench   => (new Vector3(1.6f, 1.1f, 0.9f), new Color(0.30f, 0.30f, 0.34f)),
                _                       => (Vector3.one, Color.grey)
            };
            go = World3DBuilderBox(type.ToString(), SnapToTerrain(pos), size, color);
            go.transform.SetParent(parent);
        }
        go.name = type switch
        {
            StationType.Furnace     => "Furnace",
            StationType.CookingFire => "Cooking Fire",
            StationType.Workbench   => "Workbench",
            _                       => "Crafting Station"
        };

        var station = go.GetComponent<CraftingStation>();
        if (station == null) station = go.AddComponent<CraftingStation>();
        station.stationType = type;
        station.recipes = type switch
        {
            StationType.Furnace     => CraftingRecipes.Furnace(),
            StationType.CookingFire => CraftingRecipes.Cooking(),
            StationType.Workbench   => CraftingRecipes.Workbench(),
            _                       => new List<CraftingRecipe>()
        };
        SetupCollider(go, Vector3.one * 1.5f);
    }

    static void PlaceNpc(Transform parent, Vector3 pos, string markerName, List<GameObject> npcModels)
    {
        GameObject go;
        if (npcModels.Count > 0)
        {
            var model = npcModels[Random.Range(0, npcModels.Count)];
            go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.transform.position = SnapToTerrain(pos);
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            // Normalize height like player model
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                if (b.size.y > 1e-4f) go.transform.localScale *= 1.8f / b.size.y;
            }

            // Strip junk
            foreach (var cam in go.GetComponentsInChildren<Camera>(true))
                if (cam != null) Object.DestroyImmediate(cam.gameObject);
            foreach (var lt in go.GetComponentsInChildren<Light>(true))
                if (lt != null) Object.DestroyImmediate(lt.gameObject);
            foreach (var al in go.GetComponentsInChildren<AudioListener>(true))
                if (al != null) Object.DestroyImmediate(al);
        }
        else
        {
            go = World3DBuilderCapsule("NPC", SnapToTerrain(pos), new Color(0.35f, 0.7f, 1.0f));
            go.transform.SetParent(parent);
        }

        // Determine NPC role from marker name
        string npcName = markerName.Contains("Shopkeeper") ? "Shopkeeper" :
                         markerName.Contains("Quartermaster") ? "Quartermaster" :
                         markerName.Contains("Trader") ? "Trader" :
                         "Survivor";

        // Use the NPC name list for variety based on role index
        int nameIdx = Mathf.Abs(npcName.GetHashCode()) % NpcNames[1].Length;
        go.name = $"NPC - {NpcNames[1][nameIdx]} ({npcName})";

        // Add TutorialNPC component and set its private npcName via SerializedObject
        var tnpc = go.AddComponent<TutorialNPC>();
        var so = new SerializedObject(tnpc);
        var nameProp = so.FindProperty("npcName");
        if (nameProp != null)
            nameProp.stringValue = NpcNames[1][nameIdx];
        so.ApplyModifiedProperties();
        SetupCollider(go, new Vector3(1f, 2f, 1f));

        // Add CombatTarget so NPCs can be hit (not aggressive)
        var ct = go.AddComponent<CombatTarget>();
        ct.maxHP = 500;
        ct.isAggressive = false;
    }

    static void PlaceEnemyPack(Transform parent, Vector3 centerPos, int tier, int count)
    {
        tier = Mathf.Clamp(tier, 2, 5);
        var stats = EnemyStats.GetValueOrDefault(tier, EnemyStats[2]);

        for (int i = 0; i < count; i++)
        {
            var pos = centerPos + new Vector3(
                Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
            pos = SnapToTerrain(pos);

            var go = World3DBuilderCapsule($"Wasteland Hostile T{tier}", pos,
                new Color(0.55f, 0.2f, 0.18f));
            go.transform.SetParent(parent, true);
            go.transform.position = pos;

            var ct = go.AddComponent<CombatTarget>();
            ct.maxHP = stats.hp;
            ct.attackLevel = stats.atk;
            ct.defenceLevel = stats.def;
            ct.maxDamage = stats.dmg;
            ct.isAggressive = true;

            var e3d = go.AddComponent<Enemy3D>();
            e3d.aggroRange = stats.aggro;
            e3d.moveSpeed = stats.speed;
            e3d.attackCooldown = 1.8f;
            e3d.respawnSeconds = tier <= 3 ? 15f : 25f;
        }
    }

    static void PlaceBoss(Transform parent, Vector3 pos, string label, bool isMiniBoss = false, bool isBoss = false)
    {
        pos = SnapToTerrain(pos);
        var go = World3DBuilderCapsule(label, pos,
            isBoss ? new Color(0.92f, 0.22f, 0.72f) : new Color(1.0f, 0.32f, 0.26f));
        go.transform.SetParent(parent);
        go.transform.localScale = isBoss ? new Vector3(2.5f, 2.5f, 2.5f) : new Vector3(1.8f, 1.8f, 1.8f);

        var ct = go.AddComponent<CombatTarget>();
        ct.maxHP = isBoss ? 500 : 200;
        ct.attackLevel = isBoss ? 50 : 35;
        ct.defenceLevel = isBoss ? 40 : 25;
        ct.maxDamage = isBoss ? 25 : 15;
        ct.isAggressive = true;
        ct.isMiniBoss = isMiniBoss;
        ct.isBoss = isBoss;

        var e3d = go.AddComponent<Enemy3D>();
        e3d.aggroRange = isBoss ? 22f : 16f;
        e3d.moveSpeed = isBoss ? 4.5f : 3.8f;
        e3d.attackCooldown = isBoss ? 2.5f : 2.0f;
        e3d.respawnSeconds = isBoss ? 120f : 60f;
        e3d.windupSeconds = isBoss ? 0.6f : 0.4f;
    }

    static void PlaceWater(Transform parent, Vector3 pos, GameObject waterPrefab)
    {
        pos = SnapToTerrain(pos);
        // Create a simple flat water plane — SUIMONO/AQUAS cause errors.
        // Replace with Cyberpunk Port City URP water after importing that package.
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Water Surface";
        go.transform.SetParent(parent);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
        Object.DestroyImmediate(go.GetComponent<Collider>());

        // Try to use the BasicToonWaterShader if available
        var waterShader = Shader.Find("BasicToonWater") ?? Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(waterShader);
        mat.color = new Color(0.15f, 0.35f, 0.50f, 0.85f);
        // Make it transparent
        mat.SetFloat("_Surface", 1); // Transparent
        mat.SetFloat("_Blend", 0);   // Alpha
        mat.renderQueue = 3000;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TIER GATES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Place TierGate colliders at N/S/E/W bridge mouths between each tier ring.
    /// Ring radii match WorldContentPlanner.EDGE (300). Gaps are the bridge width (12°).</summary>
    static int PlaceTierGates(Transform parent)
    {
        const float EDGE = 300f;
        // Ring boundaries (matching WorldContentPlanner) + cardinal directions
        (float radius, int fromTier, int toTier)[] rings = {
            (EDGE * 0.33f, 1, 2),
            (EDGE * 0.63f, 2, 3),
            (EDGE * 0.87f, 3, 4),
        };
        string[] dirs = { "east", "north", "west", "south" };
        float[] angles = { 0f, 90f, 180f, 270f };

        int placed = 0;
        foreach (var (r, fromTier, toTier) in rings)
        {
            for (int i = 0; i < 4; i++)
            {
                float a = angles[i] * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                pos = SnapToTerrain(pos);

                var go = new GameObject($"TierGate_T{fromTier}toT{toTier}_{dirs[i]}");
                go.transform.SetParent(parent);
                go.transform.position = pos + Vector3.up * 3f; // mid-height

                var bc = go.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size = new Vector3(8f, 6f, 2f); // wide trigger across the bridge mouth
                bc.center = Vector3.zero;

                // Face the barrier across the path (perpendicular to radial direction)
                go.transform.rotation = Quaternion.Euler(0f, angles[i] - 90f, 0f);

                var gate = go.AddComponent<TierGate>();
                gate.targetTier = toTier;
                gate.gateId = $"t{fromTier}to{toTier}_{dirs[i]}";
                // Boss kill is the only gate — nothing else to configure.
                gate.bossAliveMessage = toTier switch
                {
                    2 => "The grass thins ahead. Beyond this point, the Greenbelt gives way to the Barren Expanse. Something here still holds the way shut.",
                    3 => "Smoke rises on the horizon. The Scorched Highlands lie past this ruined barricade, and something guards it.",
                    4 => "An icy wind cuts through. The Frozen Deadlands stretch beyond — but the gate will not move while its keeper lives.",
                    5 => "The earth itself pulses with corruption. The Wasteland Core awaits, sealed by whatever broke the world.",
                    _ => "Something here still holds the way shut."
                };
                gate.unlockMessage = toTier switch
                {
                    2 => "The path opens. The Barren Expanse stretches before you — stay sharp, survivor.",
                    3 => "The barrier yields. The Scorched Highlands welcome those strong enough to enter.",
                    4 => "The ice cracks. The Frozen Deadlands reveal their secrets to the worthy.",
                    5 => "The gate shatters. Enter the Wasteland Core — and face your destiny.",
                    _ => "The path is clear."
                };

                placed++;
            }
        }
        return placed;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FOLIAGE & BUILDINGS
    // ═══════════════════════════════════════════════════════════════════════

    static int ScatterFoliage(Transform parent, List<GameObject> prefabs, int dabs, int perDab, float radius)
    {
        // Kept for compatibility — use ScatterBiomeFoliage instead.
        return 0;
    }

    /// <summary>Place biome-appropriate foliage per ring radius.</summary>
    static int ScatterBiomeFoliage(Transform parent, List<GameObject> foliagePrefabs)
    {
        if (foliagePrefabs.Count == 0) return 0;
        const float EDGE = 300f;
        int total = 0;

        // Biome foliage density: dense in green rings, sparse in outer, none in snow/wasteland
        (float innerR, float outerR, int dabs, string label)[] biomes = {
            (0f,    EDGE * 0.10f, 10, "Tutorial"),    // dense lush center
            (EDGE * 0.10f, EDGE * 0.28f, 25, "Greenbelt"),  // forested ring
            (EDGE * 0.28f, EDGE * 0.50f, 15, "Barren"),     // sparse dead trees
            (EDGE * 0.50f, EDGE * 0.70f, 8,  "Scorched"),   // very sparse, charred
            // Tier 4 Frozen: no foliage
            // Tier 5 Wasteland: no foliage
        };

        foreach (var (innerR, outerR, dabs, label) in biomes)
        {
            for (int d = 0; d < dabs; d++)
            {
                float r = Random.Range(innerR + 2f, outerR - 2f);
                float ang = Random.Range(0f, Mathf.PI * 2f);
                var center = SnapToTerrain(new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r));

                int perDab = label == "Tutorial" ? 6 : label == "Greenbelt" ? 4 : 2;
                for (int i = 0; i < perDab; i++)
                {
                    var pos = center + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f));
                    pos = SnapToTerrain(pos);

                    var prefab = foliagePrefabs[Random.Range(0, foliagePrefabs.Count)];
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                    go.transform.position = pos;
                    go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    float scale = label == "Greenbelt" ? Random.Range(0.8f, 1.5f) :
                                  label == "Barren" ? Random.Range(0.4f, 0.8f) : Random.Range(0.3f, 0.6f);
                    go.transform.localScale *= scale;
                    go.name = $"Foliage_{label}_{go.name.Replace("(Clone)", "").Trim()}";
                    NeutralizeForeignScripts(go);
                    total++;
                }
            }
        }
        return total;
    }

    /// <summary>Place transitional props at ring boundaries — barricades, debris, ruined vehicles.</summary>
    static int PlaceBiomeTransitions(Transform parent)
    {
        const float EDGE = 300f;
        int placed = 0;
        string[] propKeys = { "barricade", "barrier", "fence", "wall", "car", "truck", "debris", "ruin", "container" };

        // Boundaries: between each tier, place props along the ring at cardinal positions
        float[] boundaries = { EDGE * 0.28f, EDGE * 0.50f, EDGE * 0.70f };
        int[] counts = { 8, 6, 4 };

        for (int b = 0; b < boundaries.Length; b++)
        {
            for (int i = 0; i < counts[b]; i++)
            {
                float ang = (360f / counts[b]) * i + Random.Range(-10f, 10f);
                float r = boundaries[b] + Random.Range(-5f, 5f);
                var pos = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad) * r, 0f, Mathf.Sin(ang * Mathf.Deg2Rad) * r);
                pos = SnapToTerrain(pos);

                var prefab = FindPackPrefab(propKeys);
                if (prefab == null) continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                NeutralizeForeignScripts(go);
                placed++;
            }
        }
        return placed;
    }

    static int ScatterBuildings(Transform parent, List<GameObject> prefabs, int count)
    {
        if (prefabs.Count == 0) return 0;
        int placed = 0;

        // Place buildings in key areas: near hub, at tier borders
        float[] rings = { 20f, 80f, 160f, 240f };

        for (int i = 0; i < count; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float r = rings[Random.Range(0, rings.Length)];
            var pos = SnapToTerrain(new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r));

            var prefab = prefabs[Random.Range(0, prefabs.Count)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            NeutralizeForeignScripts(go);
            placed++;
        }
        return placed;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PARSING
    // ═══════════════════════════════════════════════════════════════════════

    static (string skill, int tier) ParseSkillMarker(string cleanName)
    {
        string skill = null;
        if (cleanName.StartsWith("EXCAVATION")) skill = "EXCAVATION";
        else if (cleanName.StartsWith("SALVAGING"))  skill = "SALVAGING";
        else if (cleanName.StartsWith("SCAVENGING"))  skill = "SCAVENGING";

        int tier = ExtractTier(cleanName);
        return (skill, tier);
    }

    static int ExtractTier(string text)
    {
        var m = Regex.Match(text, @"T(?:ier\s*)?(\d)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int tier))
            return Mathf.Clamp(tier, 1, 5);
        return 1;
    }

    static int ExtractGroupCount(string text, int min, int max)
    {
        var m = Regex.Match(text, @"group\s*x?(\d+)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int count))
            return Mathf.Clamp(count, min, max);
        return Random.Range(min, max + 1);
    }

    // When we encounter a "(cluster node)" dot, its parent group name tells us the skill and tier.
    static (string skill, int tier) ParseParentGroup(string parentName)
    {
        if (string.IsNullOrEmpty(parentName)) return (null, 1);
        string upper = parentName.ToUpperInvariant();
        string skill = null;
        if (upper.Contains("EXCAVATION")) skill = "EXCAVATION";
        else if (upper.Contains("SALVAGING")) skill = "SALVAGING";
        else if (upper.Contains("SCAVENGING")) skill = "SCAVENGING";

        int tier = ExtractTier(parentName);
        return (skill, tier);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ASSET DISCOVERY
    // ═══════════════════════════════════════════════════════════════════════

    static List<GameObject> LoadNpcModels()
    {
        var list = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { NpcFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null && go.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                list.Add(go);
        }
        return list;
    }

    static List<GameObject> LoadFoliagePrefabs()
    {
        var list = new List<GameObject>();
        if (!AssetDatabase.IsValidFolder(FoliageFolder)) return list;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { FoliageFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null && go.GetComponentInChildren<MeshRenderer>() != null)
                list.Add(go);
        }
        return list;
    }

    static List<GameObject> FindBuildingPrefabs()
    {
        var list = new List<GameObject>();
        // Search common building pack folders for structural prefabs
        string[] searchFolders = {
            "Assets/LeartesStudios/MilitaryBase",
            "Assets/LeartesStudios/BanditsValley",
            "Assets/LeartesStudios/Medieval Village",
            "Assets/LeartesStudios/Cyberpunk Port City",
            "Assets/LeartesStudios/RomanStreet",
            "Assets/LeartesStudios/WitchVillage",
            "Assets/LeartesStudios/AzureHillside",
            "Assets/Industrial_Zone_Modular_Pack",
            "Assets/Low Poly Simple Urban City 3D Asset Pack",
            "Assets/LowPoly Environment Pack",
        };

        string[] buildingKeys = { "building", "house", "hut", "shack", "tower", "wall",
                                   "bunker", "shelter", "ruin", "structure", "module" };

        foreach (var folder in searchFolders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            foreach (var key in buildingKeys)
            {
                foreach (var guid in AssetDatabase.FindAssets($"t:Prefab {key}", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("/Editor/")) continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go == null) continue;
                    if (go.GetComponentInChildren<Camera>() != null) continue;
                    if (go.GetComponentInChildren<MeshRenderer>() == null &&
                        go.GetComponentInChildren<SkinnedMeshRenderer>() == null) continue;
                    if (!list.Contains(go)) list.Add(go);
                }
            }
        }
        return list;
    }

    static GameObject FindWaterPrefab()
    {
        // Try SUIMONO first, then AQUAS, then StylisedWater
        string[] waterPaths = {
            "Assets/SUIMONO - WATER SYSTEM 2/PREFABS",
            "Assets/AQUAS-Lite/Prefabs",
            "Assets/Bitgem/StylisedWater/URP/Prefabs",
            "Assets/BasicToonWaterShader",
        };

        foreach (var folder in waterPaths)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                // Look for water-related names
                string lower = go.name.ToLowerInvariant();
                if (lower.Contains("water") || lower.Contains("ocean") || lower.Contains("lake") ||
                    lower.Contains("pond") || lower.Contains("surface"))
                    return go;
            }
        }
        return null;
    }

    static GameObject FindPackPrefab(params string[] keywords)
    {
        foreach (var key in keywords)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:Prefab {key}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Editor/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                if (go.GetComponentInChildren<Camera>() != null) continue;
                if (go.GetComponentInChildren<MeshRenderer>() == null &&
                    go.GetComponentInChildren<SkinnedMeshRenderer>() == null) continue;
                return go;
            }
        }
        return null;
    }

    /// <summary>Pick any prefab from a folder (for known single-source assets).</summary>
    static GameObject FindInFolder(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return null;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Editor/")) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null && go.GetComponentInChildren<MeshRenderer>() != null)
                return go;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TERRAIN / UTILITY
    // ═══════════════════════════════════════════════════════════════════════

    static Vector3 SnapToTerrain(Vector3 pos)
    {
        // Find the terrain by name first (most reliable in editor)
        var ground = GameObject.Find("Ground");
        var terrain = ground != null ? ground.GetComponent<Terrain>() : Terrain.activeTerrain;

        if (terrain != null)
        {
            float h = terrain.SampleHeight(pos);
            return new Vector3(pos.x, h, pos.z);
        }

        // Fallback: raycast
        if (Physics.Raycast(pos + Vector3.up * 500f, Vector3.down, out var hit, 1000f,
                ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point;
        }

        return pos;
    }

    static void NeutralizeForeignScripts(GameObject go)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var t = mb.GetType();
            // Keep our own game scripts alive
            if (t.Namespace != null && t.Namespace.StartsWith("UnityEngine")) continue;
            if (t == typeof(ResourceNode) || t == typeof(CraftingStation) ||
                t == typeof(BankingCrate) || t == typeof(CombatTarget) ||
                t == typeof(Enemy3D) || t == typeof(TutorialNPC) ||
                t == typeof(CraftingRecipes) || t == typeof(CraftingRecipe))
                continue;
            mb.enabled = false;
        }
        foreach (var al in go.GetComponentsInChildren<AudioListener>(true))
            if (al != null) Object.DestroyImmediate(al);
        foreach (var l in go.GetComponentsInChildren<Light>(true))
            if (l != null) l.enabled = false;
    }

    static void SetupCollider(GameObject go, Vector3 size)
    {
        if (go.GetComponentInChildren<Collider>() != null) return;
        var bc = go.AddComponent<BoxCollider>();
        bc.size = size;
        bc.center = Vector3.up * (size.y / 2f);
    }

    static GameObject CreatePillar(Transform parent, string label, Vector3 pos, Color color, float height)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = label;
        go.transform.SetParent(parent);
        go.transform.position = SnapToTerrain(pos) + Vector3.up * (height / 2f);
        go.transform.localScale = new Vector3(1.5f, height / 2f, 1.5f);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = World3DBuilderMakeMat($"Pillar_{label}", color);
        return go;
    }

    static string FormatPos(Vector3 v) => $"({v.x:F0}, {v.z:F0})";

    // ── Replicating World3DBuilder helpers so we don't depend on its statics at compile time ──
    static GameObject World3DBuilderCapsule(string name, Vector3 pos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = pos + Vector3.up * 1f;
        go.GetComponent<Renderer>().sharedMaterial = World3DBuilderMakeMat(name, color);
        return go;
    }

    static GameObject World3DBuilderBox(string name, Vector3 pos, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos + Vector3.up * (size.y / 2f);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = World3DBuilderMakeMat(name, color);
        return go;
    }

    static Material World3DBuilderMakeMat(string name, Color color)
    {
        const string matFolder = "Assets/Art/Generated3D";
        if (!AssetDatabase.IsValidFolder(matFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art"))
                AssetDatabase.CreateFolder("Assets", "Art");
            AssetDatabase.CreateFolder("Assets/Art", "Generated3D");
        }
        string path = $"{matFolder}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { existing.color = color; return existing; }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
