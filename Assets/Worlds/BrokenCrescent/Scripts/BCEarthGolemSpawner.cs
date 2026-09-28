using System.Linq;
using UnityEngine;

/// <summary>
/// Broken Crescent T1: keeps one farmable Earth Golem alive at one of this object's child spawn points,
/// so the Geiger Counter ("Hot Stuff") always has something to track. Kill it, strip its corpse
/// (FissionGolem handles that), and a fresh one appears at a different point after a delay.
///
/// Same golem build as FissionGolemSpawner (which only runs in MainWorld scenes and needs LivingWorld's
/// biome map); this one uses authored points instead. The golem is a farmable mini-boss, never a
/// progression boss.
/// </summary>
public class BCEarthGolemSpawner : MonoBehaviour
{
    [Range(1, 5)] public int tier = 1;
    static readonly string[] Elements = { "Earth", "Light", "Fire", "Ice", "Shadow" };
    string GolemPath => $"golems/t{tier}/source/{Elements[tier - 1]}_Golem_FBX";
    const float BodyHeight = 4.5f;

    public float respawnDelay = 20f;

    public bool overrideStats;
    public GolemStats stats = new();
    public MonsterDropTable dropTable;
    public string MonsterName => $"{Elements[Mathf.Clamp(tier, 1, 5) - 1]} Golem (T{tier})";

    [System.Serializable]
    public class GolemStats
    {
        public int maxHP = 150, attackLevel = 12, defenceLevel = 10, maxDamage = 8;
        public bool isAggressive = true;
        public float aggroRange = 14, attackRange = 1.8f, moveSpeed = 2.6f, attackCooldown = 2.6f;
        public float windupSeconds = .4f, wanderRadius = 1.5f;
        public int minYield = 3, maxYield = 15, harvestLevelRequired, xpPerCore;
    }

    public GolemStats EffectiveStats => overrideStats ? stats : DefaultStats(tier);
    public static GolemStats DefaultStats(int tier)
    {
        int i = Mathf.Clamp(tier, 1, 5) - 1;
        return new GolemStats { maxHP = new[] {150,260,420,650,950}[i],
            attackLevel = new[] {12,20,32,46,64}[i], defenceLevel = new[] {10,16,24,34,48}[i],
            maxDamage = new[] {8,12,18,26,38}[i], aggroRange = 14 + i, moveSpeed = 2.6f + i * .2f };
    }

    int _lastPoint = -1;
    float _respawnAt = 1f;

    void Update()
    {
        tier = Mathf.Clamp(tier, 1, 5);
        if (FissionGolem.Active.Any(g => g != null && g.tier == tier)) { _respawnAt = 0f; return; }
        if (_respawnAt <= 0f) { _respawnAt = Time.time + respawnDelay; return; }
        if (Time.time >= _respawnAt) { Spawn(); _respawnAt = 0f; }
    }

    void Spawn()
    {
        int n = transform.childCount;
        if (n == 0) return;
        int i = Random.Range(0, n);
        if (n > 1 && i == _lastPoint) i = (i + 1 + Random.Range(0, n - 1)) % n;
        _lastPoint = i;
        Vector3 pos = transform.GetChild(i).position;

        var prefab = Resources.Load<GameObject>(GolemPath);
        if (prefab == null) { Debug.LogWarning("[BCEarthGolemSpawner] Missing " + GolemPath); return; }

        var go = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.value * 360f, 0f));
        go.name = $"{Elements[tier - 1]} Golem (T{tier})";
        LivingWorld.Sanitize(go);
        FitToGround(go, pos.y);

        if (go.GetComponentInChildren<Collider>() == null)
        {
            var cc = go.AddComponent<CapsuleCollider>();
            cc.height = BodyHeight / go.transform.localScale.y; cc.radius = 1.2f / go.transform.localScale.x;
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
        }

        ConfigureSpawn(go);
        Debug.Log($"[BCEarthGolemSpawner] {Elements[tier - 1]} golem spawned at point {i} {pos}.");
    }

    /// <summary>Apply the authored configuration to each new golem, including respawns.</summary>
    public void ConfigureSpawn(GameObject go)
    {
        tier = Mathf.Clamp(tier, 1, 5);
        var ct = go.GetComponent<CombatTarget>() ?? go.AddComponent<CombatTarget>();
        var settings = EffectiveStats;
        ct.maxHP = Mathf.Max(1, settings.maxHP);
        ct.attackLevel = Mathf.Max(1, settings.attackLevel);
        ct.defenceLevel = Mathf.Max(1, settings.defenceLevel);
        ct.maxDamage = Mathf.Max(0, settings.maxDamage);
        ct.isAggressive = settings.isAggressive; ct.tier = tier; ct.isBoss = tier == 5; ct.isMiniBoss = tier < 5;
        ct.dropTable = dropTable;
        ct.Reset(); // AddComponent initialized HP before the tier stats were assigned.

        var e = go.GetComponent<Enemy3D>() ?? go.AddComponent<Enemy3D>();
        e.aggroRange = settings.aggroRange; e.moveSpeed = settings.moveSpeed;
        e.attackRange = settings.attackRange; e.attackCooldown = Mathf.Max(.1f, settings.attackCooldown);
        e.windupSeconds = settings.windupSeconds; e.wanderRadius = settings.wanderRadius; e.respawnSeconds = 9999f;

        var fg = go.GetComponent<FissionGolem>() ?? go.AddComponent<FissionGolem>();
        fg.tier = tier;
        fg.minYield = Mathf.Max(1, settings.minYield); fg.maxYield = Mathf.Max(fg.minYield, settings.maxYield);
        fg.harvestLevelRequired = settings.harvestLevelRequired; fg.xpPerCore = settings.xpPerCore;
    }

    static void FitToGround(GameObject go, float groundY)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return;
        var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
        if (b.size.y > 0.01f) go.transform.localScale *= BodyHeight / b.size.y;
        b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
        go.transform.position += Vector3.up * (groundY - b.min.y);
    }
}
