using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fills the wasteland at play time with COHERENT SITES placed by BIOME — not by radius. It reads the
/// terrain's painted splatmap and drops each thing into the region it belongs to: salvage yards,
/// deadwood groves and fishing shores for gathering; raider camps and a boss per tier; small towns of
/// buildings; and foliage to green (or deaden) each biome. The world is a spiral of painted bands
/// (green → tan → dark → snow → red), so placement follows the PAINT, whatever shape it is.
///
/// Tier is decided by the terrain layer's NAME (T1_Greenbelt, T2_Barren_Plains, …), so it works no
/// matter which layer set or order is on the terrain. Runs once, only in MainWorld3D, under a single
/// "~LivingWorld" object; re-created fresh each Play. Set POPULATE=false to disable.
/// </summary>
public class LivingWorld : MonoBehaviour
{
    // Disabled while world-building — the populator drops sites/enemies/bosses in the wrong places.
    // Flip back to true to re-enable procedural population. (The separate wildlife/animals that roam
    // the map are NOT part of this system and are unaffected.)
    static readonly bool POPULATE = false;
    const float SeaLevel = 50.3f;

    // ── prefab names in Resources/… , per skill per tier ──
    static readonly string[] Scrap = { "Junk Pile (T1)", "abandoned car (T2)", "tech dumpster (T3)", "downed drones pile (T4)", "abandoned robotics (T5)" };
    static readonly string[] Wood  = { "Dead Scrub & Brush (T1)", "Irradiated Thicket (T2)", "fossilized deadwood (T3)", "Petrified Forest (T4)", "Ancient Gnarlwood (T5)" };
    static readonly string[] Fish  = { "Fishing Spot (T1)", "Fishing Spot (T2)", "Fishing Spot (T3)", "Fishing Spot (T4)", "Fishing Spot (T5)" };

    static readonly int[] NodeLevel = { 1, 10, 25, 45, 70 };
    static readonly int[] NodeXP    = { 25, 45, 80, 140, 250 };
    static readonly int[] YieldMin  = { 1, 2, 3, 5, 8 };
    static readonly int[] YieldMax  = { 3, 5, 8, 12, 20 };
    static readonly int[] ScrapDrop = { 10, 60, 61, 62, 63 };
    static readonly int[] WoodDrop  = { 220, 221, 222, 223, 223 };
    static readonly int[] FishDrop  = { 200, 201, 202, 203, 203 };
    const int PickaxeId = 3, HatchetId = 4, RodId = 5;

    static readonly (int hp, int atk, int def, int dmg, float aggro, float speed)[] Foe =
    {
        (15,  3,  2,  2,  7f, 3.0f), (20,  5,  3,  3,  9f, 3.2f), (40, 10,  6,  5, 11f, 3.8f),
        (70, 18, 10,  8, 14f, 4.5f), (120,30, 18, 14, 18f, 5.5f),
    };
    static readonly string[][] Roster =
    {
        new[] { "ScrapGremlin", "Mutant" }, new[] { "Mutant", "Shardback" },
        new[] { "Shardback", "ChromeStalker", "RustRover" }, new[] { "RustRover", "ChromeStalker" },
        new[] { "RubbleTitan", "RustRover" },
    };

    // per-tier site counts. Towns/foliage taper as biomes get more hostile.
    struct Plan { public int yards, groves, shores, camps, campSize, towns; public bool boss; public int foliage; }
    static readonly Plan[] Plans =
    {
        new Plan { yards=2, groves=2, shores=1, camps=1, campSize=3, towns=1, boss=false, foliage=90 },
        new Plan { yards=2, groves=1, shores=1, camps=2, campSize=4, towns=1, boss=true,  foliage=55 },
        new Plan { yards=2, groves=2, shores=1, camps=2, campSize=4, towns=1, boss=true,  foliage=40 },
        new Plan { yards=2, groves=1, shores=0, camps=2, campSize=5, towns=0, boss=true,  foliage=30 },
        new Plan { yards=2, groves=0, shores=0, camps=2, campSize=6, towns=0, boss=true,  foliage=30 },
    };

    // foliage prefab menus per tier (green lush → barren rock).
    static readonly string[][] Flora =
    {
        new[] { "Tree1", "Tree2", "Tree1", "Grass1", "Grass1", "Rock1" },
        new[] { "Tree2", "Grass1", "Rock1", "Rocks1", "Rock2" },
        new[] { "Rock1", "Rock2", "Rocks1", "Tree2" },
        new[] { "Rock1", "Rock2", "Rocks1" },
        new[] { "Rock2", "Rocks1", "Rock1" },
    };

    Terrain _terrain;
    TerrainData _td;
    Vector3 _tp;
    int[] _layerTier;     // terrain layer index → tier (1-5), 0 = not a tier (shore/cliff)
    Transform _root;

    /// <summary>Live instance once the terrain + biome map are ready, so other systems (the fission
    /// golem spawner) can place things in the right painted biome instead of a blind ring.</summary>
    public static LivingWorld Instance { get; private set; }
    public bool Ready { get; private set; }

    /// <summary>Counts from the last build, read back by the editor baker for its summary dialog.</summary>
    [System.NonSerialized] public int builtSites;
    [System.NonSerialized] public int builtFlora;

    /// <summary>A valid ground spot inside the given tier's painted biome. False if none found.</summary>
    public bool FindTierSpot(int tier, out Vector3 pos)
    {
        if (!Ready) { pos = default; return false; }
        return BiomeSpot(tier, out pos, 30f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!POPULATE) return;
        if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.StartsWith("MainWorld")) return;
        if (GameObject.Find("~LivingWorld") != null) return;
        new GameObject("~LivingWorld").AddComponent<LivingWorld>();
    }

    IEnumerator Start()
    {
        _root = transform;
        for (int i = 0; i < 30 && _terrain == null; i++)
        {
            _terrain = Terrain.activeTerrain;
            if (_terrain == null) { var g = GameObject.Find("Ground"); if (g) _terrain = g.GetComponent<Terrain>(); }
            if (_terrain != null) break;
            yield return null;
        }
        if (_terrain == null) { Debug.LogWarning("[LivingWorld] No terrain — skipping."); yield break; }

        InitTerrain();
        LogDiagnostics();
        yield return StartCoroutine(BuildAllTiers());
    }

    /// <summary>Grab the terrain data + biome→tier map. Shared by the runtime path and the editor bake.</summary>
    void InitTerrain()
    {
        _td = _terrain.terrainData;
        _tp = _terrain.transform.position;
        BuildLayerTierMap();
        Instance = this;
        Ready = true;
    }

    // Diagnostic: where is the terrain, and does a T1 spot land on its surface? If placement Y is far
    // from the terrain surface (e.g. objects at Y=0 while the ground is at Y~50), that's why nothing
    // is visible.
    void LogDiagnostics()
    {
        var tierList = new System.Text.StringBuilder();
        for (int i = 0; i < _layerTier.Length; i++) tierList.Append($"{(_td.terrainLayers[i] != null ? _td.terrainLayers[i].name : "?")}→T{_layerTier[i]} ");
        Debug.Log($"[LivingWorld] terrain '{_terrain.name}' pos={_tp} size={_td.size} layers[{_layerTier.Length}]: {tierList}");
        if (BiomeSpot(1, out Vector3 probe, 40f))
            Debug.Log($"[LivingWorld] sample T1 site at {probe} (surface Y={_terrain.SampleHeight(probe) + _tp.y:F1}, sea={SeaLevel}).");
        else
            Debug.LogWarning("[LivingWorld] Could NOT find any T1 (green) biome spot — biome layer names may not match the T1/green mapping.");
    }

    /// <summary>Builds every tier's sites + foliage under <see cref="_root"/>. Runs synchronously so the
    /// exact same generation can either run once at Play time (frame-spread so it never hitches) OR be
    /// baked permanently into the scene from the editor, which pumps this to completion synchronously
    /// (see <see cref="EditorBake"/>). The yields are just frame breaks — harmless when pumped.</summary>
    IEnumerator BuildAllTiers()
    {
        // ONLY living things now: raider-camp enemies + one boss per tier. Foliage, props,
        // buildings/towns and resource nodes are intentionally disabled — the user wanted the
        // creatures kept and everything else gone. (Kept as private helpers below for later.)
        int sites = 0, flora = 0;
        for (int tier = 1; tier <= 5; tier++)
        {
            var plan = Plans[tier - 1];
            for (int i = 0; i < plan.camps; i++) if (BuildRaiderCamp(tier, plan.campSize)) sites++;
            if (plan.boss) if (BuildBossLair(tier)) sites++;
            yield return null;
        }
        builtSites = sites; builtFlora = flora;
        Debug.Log($"[LivingWorld] {sites} enemy groups placed (foliage/props/buildings/nodes disabled).");
    }

#if UNITY_EDITOR
    /// <summary>Editor-only: run the full generation right now (no Play mode required), parenting
    /// everything under <paramref name="bakeRoot"/> so it can be saved into the scene permanently.
    /// Returns false if the open scene has no terrain. Used by LivingWorldBaker.</summary>
    public bool EditorBake(Transform bakeRoot)
    {
        _root = bakeRoot;
        _terrain = Terrain.activeTerrain;
        if (_terrain == null) { var g = GameObject.Find("Ground"); if (g) _terrain = g.GetComponent<Terrain>(); }
        if (_terrain == null) return false;
        InitTerrain();
        // Pump the build iterator to completion right now — the yields are frame breaks we don't need.
        var it = BuildAllTiers();
        while (it.MoveNext()) { }
        return true;
    }
#endif

    // ── biome layer mapping ─────────────────────────────────────────────────

    void BuildLayerTierMap()
    {
        var layers = _td.terrainLayers;
        _layerTier = new int[layers != null ? layers.Length : 0];
        for (int i = 0; i < _layerTier.Length; i++)
        {
            string n = (layers[i] != null ? layers[i].name : "").ToLowerInvariant();
            if (n.Contains("cliff") || n.Contains("wall")) { _layerTier[i] = 0; continue; }
            if (n.Contains("t5") || n.Contains("wasteland") || n.Contains("red"))                 _layerTier[i] = 5;
            else if (n.Contains("t4") || n.Contains("frozen") || n.Contains("snow"))              _layerTier[i] = 4;
            else if (n.Contains("t3") || n.Contains("scorch") || n.Contains("mud") || n.Contains("high")) _layerTier[i] = 3;
            else if (n.Contains("t2") || n.Contains("barren") || n.Contains("sand") || n.Contains("shore")) _layerTier[i] = 2;
            else if (n.Contains("t1") || n.Contains("green") || n.Contains("grass") || n.Contains("island")) _layerTier[i] = 1;
            else _layerTier[i] = 0;
        }
        // Shore is really tier-1 water edge; if T1 has no dedicated layer, let shore stand in.
    }

    int TierAt(Vector3 xz)
    {
        int mx = Mathf.Clamp(Mathf.RoundToInt((xz.x - _tp.x) / _td.size.x * (_td.alphamapWidth - 1)), 0, _td.alphamapWidth - 1);
        int mz = Mathf.Clamp(Mathf.RoundToInt((xz.z - _tp.z) / _td.size.z * (_td.alphamapHeight - 1)), 0, _td.alphamapHeight - 1);
        float[,,] a = _td.GetAlphamaps(mx, mz, 1, 1);
        int layers = a.GetLength(2);
        int dom = 0; float best = -1f;
        for (int l = 0; l < layers && l < _layerTier.Length; l++)
            if (a[0, 0, l] > best) { best = a[0, 0, l]; dom = l; }
        return dom < _layerTier.Length ? _layerTier[dom] : 0;
    }

    // ── placement ────────────────────────────────────────────────────────────

    const float MinFromPlayer = 45f;   // never drop a site/enemy on top of the player

    /// <summary>Random valid ground whose painted biome matches <paramref name="tier"/>, kept clear
    /// of the player so nothing spawns in your lap and immediately mobs you.</summary>
    bool BiomeSpot(int tier, out Vector3 pos, float maxSteep)
    {
        pos = default;
        var player = PlayerEntity.Instance != null ? PlayerEntity.Instance.transform.position
                   : (Vector3?)null;
        for (int attempt = 0; attempt < 160; attempt++)
        {
            var xz = new Vector3(_tp.x + Random.value * _td.size.x, 0f, _tp.z + Random.value * _td.size.z);
            if (!Ground(xz, out Vector3 g, maxSteep)) continue;
            if (g.y <= SeaLevel + 0.4f) continue;              // in the water
            if (TierAt(xz) != tier) continue;                  // wrong biome
            if (player.HasValue && (g - player.Value).sqrMagnitude < MinFromPlayer * MinFromPlayer) continue;
            pos = g; return true;
        }
        return false;
    }

    bool Ground(Vector3 xz, out Vector3 pos, float maxSteep)
    {
        pos = default;
        float nx = (xz.x - _tp.x) / _td.size.x, nz = (xz.z - _tp.z) / _td.size.z;
        if (nx < 0f || nx > 1f || nz < 0f || nz > 1f) return false;
        if (_td.GetSteepness(nx, nz) > maxSteep) return false;
        pos = new Vector3(xz.x, _terrain.SampleHeight(xz) + _tp.y, xz.z);
        return true;
    }

    // ── site builders ─────────────────────────────────────────────────────

    bool BuildResourceSite(int tier, string[] prefabs, Skill skill, ResourceNodeType type, int toolId,
                           int[] drops, string label, int nodeCount, bool dressWithProps)
    {
        if (!BiomeSpot(tier, out Vector3 c, 28f)) return false;
        var group = NewGroup($"{label} T{tier}", c);
        var prefab = Resources.Load<GameObject>($"Nodes/{prefabs[tier - 1]}");
        int made = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            Vector2 off = Random.insideUnitCircle * Random.Range(2.5f, 7f);
            if (!Ground(c + new Vector3(off.x, 0, off.y), out Vector3 p, 34f)) continue;
            var go = Spawn(prefab, type, p, group);
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            ConfigureNode(go, skill, type, tier, toolId, drops[tier - 1]);
            made++;
        }
        if (dressWithProps) DressProps(group.transform, c, 2);
        if (made == 0) { Kill(group); return false; }
        return true;
    }

    bool BuildFishingShore(int tier)
    {
        // shoreline = ground just above sea level, in this biome (or the shore layer)
        Vector3 c = default; bool found = false;
        for (int attempt = 0; attempt < 200 && !found; attempt++)
        {
            var xz = new Vector3(_tp.x + Random.value * _td.size.x, 0f, _tp.z + Random.value * _td.size.z);
            if (!Ground(xz, out Vector3 g, 45f)) continue;
            if (g.y > SeaLevel - 0.5f && g.y < SeaLevel + 3f) { int bt = TierAt(xz); if (bt == tier || bt == 0) { c = g; found = true; } }
        }
        if (!found) return false;
        var group = NewGroup($"Fishing Shore T{tier}", c);
        var prefab = Resources.Load<GameObject>($"Nodes/{Fish[tier - 1]}");
        int made = 0;
        for (int i = 0; i < 3; i++)
        {
            Vector2 off = Random.insideUnitCircle * Random.Range(2f, 6f);
            if (!Ground(c + new Vector3(off.x, 0, off.y), out Vector3 p, 45f)) continue;
            if (p.y > SeaLevel + 3.5f) continue;
            var go = Spawn(prefab, ResourceNodeType.WaterBarrel, p, group);
            ConfigureNode(go, Skill.Fishing, ResourceNodeType.WaterBarrel, tier, RodId, FishDrop[tier - 1]);
            made++;
        }
        if (made == 0) { Kill(group); return false; }
        return true;
    }

    bool BuildRaiderCamp(int tier, int size)
    {
        if (!BiomeSpot(tier, out Vector3 c, 26f)) return false;
        var group = NewGroup($"Raider Camp T{tier}", c);
        // Tent + loot props removed — just the creatures now.

        var roster = Roster[tier - 1];
        int made = 0;
        for (int i = 0; i < size; i++)
        {
            Vector2 off = Random.insideUnitCircle * Random.Range(3f, 9f);
            if (!Ground(c + new Vector3(off.x, 0, off.y), out Vector3 p, 30f)) continue;
            var e = SpawnEnemy(roster[Random.Range(0, roster.Length)], tier, p, false);
            if (e != null) { e.transform.SetParent(group.transform, true); made++; }
        }
        if (made == 0) { Kill(group); return false; }
        return true;
    }

    bool BuildTown(int tier)
    {
        if (!BiomeSpot(tier, out Vector3 c, 18f)) return false;   // towns want flatter ground
        var group = NewGroup($"Settlement T{tier}", c);
        string[] kinds = { "House1", "House2", "House3", "Shop1", "Shop2" };
        int count = Random.Range(3, 6);
        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            Vector2 off = Random.insideUnitCircle * Random.Range(6f, 16f);
            if (!Ground(c + new Vector3(off.x, 0, off.y), out Vector3 p, 20f)) continue;
            var b = Resources.Load<GameObject>($"Buildings/{kinds[Random.Range(0, kinds.Length)]}");
            if (b == null) continue;
            var go = Instantiate(b, p, Quaternion.LookRotation((c - p).normalized == Vector3.zero ? Vector3.forward : (c - p), Vector3.up)); Sanitize(go);
            go.transform.rotation = Quaternion.Euler(0f, go.transform.eulerAngles.y + Random.Range(-20f, 20f), 0f);
            go.transform.SetParent(group.transform, true);
            placed++;
        }
        DressProps(group.transform, c, 4);
        if (placed == 0) { Kill(group); return false; }
        return true;
    }

    bool BuildBossLair(int tier)
    {
        if (!BiomeSpot(tier, out Vector3 c, 30f)) return false;
        var roster = Roster[tier - 1];
        var boss = SpawnEnemy(roster[roster.Length - 1], tier, c, true);
        if (boss == null) return false;
        boss.name = $"Warlord T{tier}";
        boss.transform.SetParent(_root, true);
        return true;
    }

    // ── spawn/config helpers ────────────────────────────────────────────────

    GameObject NewGroup(string name, Vector3 pos)
    {
        var g = new GameObject(name); g.transform.SetParent(_root); g.transform.position = pos; return g;
    }

    GameObject Spawn(GameObject prefab, ResourceNodeType fallbackType, Vector3 pos, GameObject parent)
    {
        var go = prefab != null ? Instantiate(prefab) : Placeholder(fallbackType);   // lazy — no leak
        Sanitize(go);
        go.transform.SetParent(parent.transform);
        go.transform.position = pos;
        return go;
    }

    void DressProps(Transform parent, Vector3 c, int n)
    {
        string[] props = { "Barrel", "Crate1", "Crate2" };
        for (int i = 0; i < n; i++)
        {
            Vector2 o = Random.insideUnitCircle * Random.Range(1.5f, 5f);
            if (!Ground(c + new Vector3(o.x, 0, o.y), out Vector3 p, 35f)) continue;
            var pr = Resources.Load<GameObject>($"Props/{props[Random.Range(0, props.Length)]}");
            if (pr == null) continue;
            var go = Instantiate(pr, p, Quaternion.Euler(0, Random.value * 360f, 0)); Sanitize(go);
            go.transform.SetParent(parent, true);
        }
    }

    void PlaceFoliage(string name, Vector3 pos, int tier)
    {
        var prefab = Resources.Load<GameObject>($"Foliage/{name}");
        if (prefab == null) return;
        var go = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.value * 360f, 0f)); Sanitize(go);
        float s = name.StartsWith("Tree") ? Random.Range(0.8f, 1.4f) : Random.Range(0.6f, 1.3f);
        if (tier >= 2 && name.StartsWith("Tree")) s *= 0.7f;   // stunted trees in harsher biomes
        go.transform.localScale *= s;
        go.transform.SetParent(_root, true);
    }

    void ConfigureNode(GameObject go, Skill skill, ResourceNodeType type, int tier, int toolId, int dropId)
    {
        var n = go.GetComponent<ResourceNode>() ?? go.AddComponent<ResourceNode>();
        n.skill = skill; n.nodeType = type; n.levelRequired = NodeLevel[tier - 1];
        n.requiredToolId = toolId; n.dropItemId = dropId; n.xpPerAction = NodeXP[tier - 1];
        n.minYield = YieldMin[tier - 1]; n.maxYield = YieldMax[tier - 1];
        n.respawnTicks = tier <= 2 ? 8 : tier <= 4 ? 12 : 18;
        n.attemptsPerCycle = 1; n.successChance = 256;
        if (go.GetComponentInChildren<Collider>() == null)
        {
            var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(1.6f, 1.6f, 1.6f); bc.center = Vector3.up * 0.8f;
        }
    }

    GameObject SpawnEnemy(string prefabName, int tier, Vector3 pos, bool boss)
    {
        var prefab = Resources.Load<GameObject>($"Enemies/{prefabName}");
        var go = prefab != null ? Instantiate(prefab) : Placeholder(ResourceNodeType.RatNest);
        Sanitize(go);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
        var f = Foe[tier - 1];
        var ct = go.GetComponent<CombatTarget>() ?? go.AddComponent<CombatTarget>();
        ct.maxHP = boss ? f.hp * 5 : f.hp; ct.attackLevel = boss ? f.atk + 15 : f.atk;
        ct.defenceLevel = boss ? f.def + 10 : f.def; ct.maxDamage = boss ? f.dmg * 2 : f.dmg;
        ct.isAggressive = true; ct.tier = tier; ct.isBoss = boss && tier == 5; ct.isMiniBoss = boss && tier < 5;
        var e = go.GetComponent<Enemy3D>() ?? go.AddComponent<Enemy3D>();
        e.aggroRange = boss ? f.aggro + 8f : f.aggro; e.moveSpeed = boss ? f.speed + 0.5f : f.speed;
        e.attackCooldown = boss ? 2.4f : 1.8f; e.respawnSeconds = boss ? 120f : (tier <= 3 ? 20f : 30f);
        if (boss) go.transform.localScale *= tier == 5 ? 2.2f : 1.6f;
        return go;
    }

    static GameObject Placeholder(ResourceNodeType t)
    {
        var go = GameObject.CreatePrimitive(t == ResourceNodeType.WaterBarrel ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        go.name = "PLACEHOLDER (prefab missing)";
        return go;
    }

    /// <summary>
    /// Strip the things a spawned prefab drags in that hijack the game: imported FBX cameras (which
    /// steal the view — this is why the camera "moved to the golem"), extra AudioListeners (Unity only
    /// allows one), and stray Lights. Vendor models routinely carry these. Call on everything spawned.
    /// </summary>
    public static void Sanitize(GameObject go)
    {
        if (go == null) return;
        // Destroy just the COMPONENTS (never a GameObject — that could take a mesh with it).
        foreach (var cam in go.GetComponentsInChildren<Camera>(true))
        {
            if (cam == null) continue;
            cam.enabled = false;
            // Imported URP metadata requires Camera. Remove the dependency before the camera.
            var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data != null) Object.DestroyImmediate(data);
            Kill(cam);
        }
        foreach (var al in go.GetComponentsInChildren<AudioListener>(true)) if (al != null) Kill(al);
        foreach (var l in go.GetComponentsInChildren<Light>(true)) if (l != null) Kill(l);
    }

    /// <summary>Destroy that works in BOTH play mode and the editor bake. Object.Destroy is illegal in
    /// edit mode (it defers to the next frame that never comes), so use DestroyImmediate there.</summary>
    static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}
