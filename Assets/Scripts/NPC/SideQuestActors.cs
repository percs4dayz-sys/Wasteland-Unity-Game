using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// A settlement NPC who runs part of a small quest (see <see cref="SideQuests"/>). Drop this on any
/// NPC model and set <see cref="npcName"/> to one of the names in SideQuests.Npcs; otherwise
/// <see cref="SideQuestSpawner"/> builds a placeholder figure for each NPC that isn't already in the
/// scene. Needs a collider so the interactor can find it.
/// </summary>
public class SideQuestNPC : MonoBehaviour, ITalkableNPC
{
    public string npcName = "";
    SideQuests.NpcDef _def;

    SideQuests.NpcDef Def => _def ??= System.Array.Find(SideQuests.Npcs, n => n.name == npcName);

    public string DisplayName => npcName;
    public string ExamineText => Def != null ? Def.examine : "A wastelander.";

    public void Interact()
    {
        if (Def == null) return;
        var p = PlayerEntity.Instance;
        if (p != null)
        {
            Vector3 d = p.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d);
        }
        SideQuests.Talk(Def, transform.position);
    }
}

/// <summary>A marked spot for a world-site quest stage. Interact to dig / search.</summary>
public class SideQuestSite : MonoBehaviour, ITalkableNPC
{
    public string questId;
    public int stageIndex;
    public string siteName, examine;

    public string DisplayName => siteName;
    public string ExamineText => examine;
    public void Interact() => SideQuests.Site(questId, stageIndex, transform.position);
}

/// <summary>
/// Places the small-quest NPCs and world sites in the main world, inside the right tier's biome, and
/// keeps them in the same spot between sessions (position cached in PlayerPrefs). Anything you've
/// already placed by hand (a SideQuestNPC with a matching name) is left alone. Also owns the
/// Pest Control rabbit.
/// </summary>
public class SideQuestSpawner : MonoBehaviour
{
    const string PosKey = "sq_pos:";
    readonly Dictionary<string, GameObject> _sites = new();
    float _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.StartsWith("MainWorld")) return;
        var go = new GameObject("SideQuestSpawner (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<SideQuestSpawner>();
    }

    void OnEnable()  => CombatTarget.AnyKilled += OnKilled;
    void OnDisable() => CombatTarget.AnyKilled -= OnKilled;

    static void OnKilled(CombatTarget ct)
    {
        if (ct.name == SideQuests.GiantRabbitName && PlayerEntity.Instance != null)
        {
            PlayerEntity.Instance.SetFlag(SideQuests.PestRabbitDeadFlag);
            HUDController.Emit("<color=#FFD966>The giant rabbit is dead.</color> Farmer Hale will want to hear about this.");
        }
    }

    void Update()
    {
        if (Time.time < _next) return;
        _next = Time.time + 2f;
        var p = PlayerEntity.Instance;
        if (p == null || LivingWorld.Instance == null || !LivingWorld.Instance.Ready) return;

        var present = FindObjectsByType<SideQuestNPC>(FindObjectsSortMode.None).Select(n => n.npcName).ToHashSet();
        foreach (var def in SideQuests.Npcs)
        {
            if (present.Contains(def.name)) continue;
            if (!Place("npc:" + def.name, def.tier, out Vector3 pos)) continue;
            var go = BuildFigure(def.name, def.color, 1.9f);
            go.transform.position = pos;
            go.AddComponent<SideQuestNPC>().npcName = def.name;
        }

        // World sites exist only while their stage is the active one.
        var wanted = new HashSet<string>();
        foreach (var q in SideQuests.All)
        {
            int n = SideQuests.Stage(p, q);
            if (n >= q.stages.Length || q.stages[n].siteName == null) continue;
            string key = $"{q.id}:{n}";
            wanted.Add(key);
            if (_sites.TryGetValue(key, out var existing) && existing != null) continue;
            var s = q.stages[n];
            if (!Place("site:" + key, s.siteTier, out Vector3 pos)) continue;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = s.siteName;
            go.transform.localScale = new Vector3(1.4f, 0.12f, 1.4f);   // a disturbed patch of earth
            go.transform.position = pos + Vector3.up * 0.1f;
            go.GetComponent<Renderer>().material.color = s.siteColor;
            var site = go.AddComponent<SideQuestSite>();
            site.questId = q.id; site.stageIndex = n; site.siteName = s.siteName; site.examine = s.siteExamine;
            _sites[key] = go;
        }
        foreach (var key in _sites.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            if (_sites[key] != null) Destroy(_sites[key]);
            _sites.Remove(key);
        }
    }

    /// <summary>A stable spot in the tier's biome: reuse the cached one, else pick and cache a new one.</summary>
    static bool Place(string key, int tier, out Vector3 pos)
    {
        string saved = PlayerPrefs.GetString(PosKey + key, "");
        var parts = saved.Split(',');
        if (parts.Length == 3 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float y) && float.TryParse(parts[2], out float z))
        {
            pos = new Vector3(x, y, z);
            return true;
        }
        if (!LivingWorld.Instance.FindTierSpot(tier, out pos)) return false;
        PlayerPrefs.SetString(PosKey + key, $"{pos.x},{pos.y},{pos.z}");
        return true;
    }

    /// <summary>Placeholder body (capsule + head) that keeps its capsule collider. Swap for a real model any time
    /// by putting SideQuestNPC on that model instead.</summary>
    static GameObject BuildFigure(string name, Color color, float height)
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = name;
        body.transform.localScale = new Vector3(0.8f, height * 0.5f, 0.8f);
        body.GetComponent<Renderer>().material.color = color;
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(body.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        head.transform.localScale = new Vector3(0.75f, 0.4f, 0.75f);
        head.GetComponent<Renderer>().material.color = new Color(0.9f, 0.75f, 0.6f);
        return body;
    }
}

public static class SideQuestActors
{
    static CombatTarget _rabbit;

    /// <summary>Pest Control: make sure the one enormous rabbit exists near the farmer until it's been killed.</summary>
    public static void EnsureGiantRabbit(PlayerEntity p, Vector3 farmerPos)
    {
        if (p.HasFlag(SideQuests.PestRabbitDeadFlag)) return;
        if (_rabbit != null && !_rabbit.IsDead) return;

        Vector3 spot = farmerPos + new Vector3(14f, 0f, 6f);
        if (Physics.Raycast(spot + Vector3.up * 60f, Vector3.down, out var hit, 200f)) spot = hit.point;

        var rabbit = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        rabbit.name = SideQuests.GiantRabbitName;
        rabbit.transform.localScale = new Vector3(2.4f, 1.6f, 2.4f);
        rabbit.transform.position = spot + Vector3.up * 1.6f;
        var white = new Color(0.95f, 0.93f, 0.9f);
        rabbit.GetComponent<Renderer>().material.color = white;
        foreach (float side in new[] { -0.25f, 0.25f })
        {
            var ear = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            ear.name = "Ear";
            Object.Destroy(ear.GetComponent<Collider>());
            ear.transform.SetParent(rabbit.transform, false);
            ear.transform.localPosition = new Vector3(side, 1.35f, 0f);
            ear.transform.localScale = new Vector3(0.18f, 0.55f, 0.18f);
            ear.GetComponent<Renderer>().material.color = white;
        }

        var ct = rabbit.AddComponent<CombatTarget>();
        ct.maxHP = 60; ct.attackLevel = 8; ct.defenceLevel = 6; ct.maxDamage = 4; ct.tier = 1;
        ct.isAggressive = false; ct.isMiniBoss = true;
        var e = rabbit.AddComponent<Enemy3D>();
        e.aggroRange = 10f; e.moveSpeed = 3.6f; e.attackCooldown = 2.2f; e.respawnSeconds = 99999f;
        _rabbit = ct;
    }
}
