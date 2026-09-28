using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Populates the mainland with roaming mobs so there's always something to fight nearby — keeps a
/// target population in a ring around the player, despawns ones left far behind, and replenishes as
/// they die. Difficulty scales with distance from the world origin (the journal's "the land is
/// divided into layers… creatures grow stronger the farther you travel").
///
/// Self-building & scene-agnostic (auto-spawns after every scene load, only acts on the mainland),
/// so it survives the MainWorld3D rename without any scene wiring. Mobs are tinted capsules by
/// default; if you bake nicer prefabs into Resources/Enemies (Wasteland ▸ Enemies ▸ Build Enemy
/// Prefabs), the spawner uses those automatically.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    public static EnemySpawner Instance { get; private set; }

    // Master switch for the roaming random-mob population. Turned OFF now that mobs are hand-placed
    // with the Enemy Scatter Brush (Wasteland ▸ Enemies ▸ Enemy Scatter Brush) — those persist/respawn
    // in place instead of being randomly spawned around the player. Flip back to true to restore the
    // old auto-populated horde.
    public static readonly bool AutoPopulate = false;

    [Header("Population")]
    public int   targetPopulation = 14;
    public float spawnRingMin = 16f;    // never spawn right on top of the player
    public float spawnRingMax = 44f;    // close enough to find a fight quickly
    public float despawnRange = 80f;    // cull mobs left far behind
    public float checkInterval = 1.5f;

    [Header("Scope")]
    public string mainlandScenePrefix = "MainWorld";   // only populate the mainland

    float _nextCheck;
    readonly List<SpawnedEnemy> _live = new();

    // The "where we arrived" anchor: difficulty scales with distance from here, NOT world origin
    // (the player's mainland spawn is rarely at 0,0). Captured the first time we see the player.
    Vector3 _origin;
    bool _haveOrigin;

    // ── difficulty bands, keyed on distance from the world origin ──
    struct Tier
    {
        public string name, visual; public Color color;
        public int hp, atk, def, dmg; public float speed, scale; public bool aggressive; public float minDist;
    }

    // visual = name of a prefab in Resources/Enemies (baked by "Wasteland ▸ Enemies ▸ Build Enemy
    // Prefabs (Inca / BigBoy / Robots)"). Falls back to a tinted capsule (scale) if the prefab is absent.
    static readonly Tier[] Tiers =
    {
        new() { name="Scrap Gremlin",   visual="ScrapGremlin",   color=new(0.45f,0.5f,0.42f), minDist=0f,
                hp=12,  atk=3,  def=2,  dmg=2,  speed=2.6f, scale=0.85f, aggressive=false },
        new() { name="Rust Rover",          visual="RustRover",         color=new(0.55f,0.25f,0.2f), minDist=55f,
                hp=32,  atk=12, def=8,  dmg=4,  speed=3.0f, scale=1.0f,  aggressive=true },
        new() { name="Shardback",         visual="Shardback",       color=new(0.35f,0.5f,0.25f), minDist=120f,
                hp=64,  atk=25, def=18, dmg=7,  speed=3.2f, scale=1.1f,  aggressive=true },
        new() { name="Mutant",           visual="Mutant",         color=new(0.5f,0.55f,0.35f), minDist=160f,
                hp=85,  atk=32, def=24, dmg=9,  speed=3.4f, scale=1.15f, aggressive=true },
        new() { name="Chrome Stalker",           visual="ChromeStalker",         color=new(0.3f,0.6f,0.35f),  minDist=200f,
                hp=110, atk=42, def=32, dmg=11, speed=3.2f, scale=1.2f,  aggressive=true },
        new() { name="Rubble Titan",  visual="RubbleTitan", color=new(0.35f,0.2f,0.4f),  minDist=290f,
                hp=180, atk=62, def=50, dmg=16, speed=3.5f, scale=1.45f, aggressive=true },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!AutoPopulate) return;   // hand-placed mobs only — no random roaming population
        if (Instance != null) return;
        new GameObject("EnemySpawner (auto)").AddComponent<EnemySpawner>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + checkInterval;

        var player = PlayerEntity.Instance;
        if (player == null) return;
        if (!SceneManager.GetActiveScene().name.StartsWith(mainlandScenePrefix)) return;

        // Anchor difficulty to where the player arrived (first time we see them on the mainland).
        if (!_haveOrigin) { _origin = player.transform.position; _haveOrigin = true; }

        Maintain(player.transform.position);
    }

    void Maintain(Vector3 playerPos)
    {
        // prune destroyed / dead / wandered-too-far
        float despawnSq = despawnRange * despawnRange;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var e = _live[i];
            if (e == null) { _live.RemoveAt(i); continue; }
            if ((e.transform.position - playerPos).sqrMagnitude > despawnSq)
            {
                Destroy(e.gameObject);
                _live.RemoveAt(i);
            }
        }

        int toSpawn = targetPopulation - _live.Count;
        for (int i = 0; i < toSpawn; i++) TrySpawnOne(playerPos);
    }

    void TrySpawnOne(Vector3 playerPos)
    {
        float ang  = Random.value * Mathf.PI * 2f;
        float dist = Random.Range(spawnRingMin, spawnRingMax);
        Vector3 xz = playerPos + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;

        if (!GroundAt(xz, out Vector3 pos)) return;   // no ground under this point — skip
        var tier = TierFor(pos);
        var enemy = BuildBody(tier, pos);
        _live.Add(enemy);
    }

    static bool GroundAt(Vector3 xz, out Vector3 pos)
    {
        var from = new Vector3(xz.x, xz.y + 60f, xz.z);
        if (Physics.Raycast(from, Vector3.down, out var hit, 300f))
        {
            pos = hit.point;
            return true;
        }
        pos = xz;
        return false;
    }

    Tier TierFor(Vector3 pos)
    {
        // Distance from the arrival anchor (not world origin) — "stronger the farther you travel".
        float d = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(_origin.x, _origin.z));
        Tier chosen = Tiers[0];
        foreach (var t in Tiers) if (d >= t.minDist) chosen = t;
        return chosen;
    }

    SpawnedEnemy BuildBody(Tier tier, Vector3 ground)
    {
        GameObject go = null;
        bool fromPrefab = false;

        if (!string.IsNullOrEmpty(tier.visual))
        {
            var prefab = Resources.Load<GameObject>("Enemies/" + tier.visual);
            if (prefab != null) { go = Instantiate(prefab); fromPrefab = true; }
        }
        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Paint(go, tier.color);
        }

        go.name = tier.name;
        // Baked model prefabs are already sized & ground-pivoted; only the capsule fallback needs
        // the tier scale and a half-height lift (a primitive capsule pivots at its centre, height 2).
        if (fromPrefab)
        {
            go.transform.position = ground;
        }
        else
        {
            go.transform.localScale = Vector3.one * tier.scale;
            go.transform.position = ground + Vector3.up * tier.scale;
        }

        var ct = go.GetComponent<CombatTarget>() ?? go.AddComponent<CombatTarget>();
        ct.maxHP = tier.hp; ct.attackLevel = tier.atk; ct.defenceLevel = tier.def;
        ct.maxDamage = tier.dmg; ct.isAggressive = tier.aggressive; ct.isDummy = false; ct.isBoss = false;
        ct.Reset();

        var ai = go.GetComponent<Enemy3D>() ?? go.AddComponent<Enemy3D>();
        ai.moveSpeed = tier.speed;
        ai.respawnSeconds = 999999f;        // spawner owns the lifecycle, not Enemy3D's home-respawn
        ai.SetHome(go.transform.position);

        if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<CapsuleCollider>();
        return go.GetComponent<SpawnedEnemy>() ?? go.AddComponent<SpawnedEnemy>();
    }

    static void Paint(GameObject go, Color color)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        var shader = Shader.Find("Wasteland/Toon (URP)")
                  ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { name = "Mob" };
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        rend.sharedMaterial = mat;
    }
}
