using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

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
/// Named places in the world scene, resolved from real scene objects — no biome map involved.
/// "village" is the south-west harbor town: the scene marker <c>TownSite_SW_Harbor</c>, unless you've
/// stood somewhere and typed <c>/village here</c>, which overrides it (saved per scene).
/// Any other key is the name of a scene object (it may be inactive; only its position is used).
/// </summary>
public static class WorldAnchors
{
    public const string VillageKey = "village";
    const string VillageMarker = "TownSite_SW_Harbor";
    public const float DefaultVillageRadius = 40f;

    /// <summary>Bumps whenever the village anchor is changed at runtime so the spawner rebuilds it.</summary>
    public static int Version { get; private set; }

    static string PrefKey => "village_anchor:" + SceneManager.GetActiveScene().name;

    public static bool TryGet(string key, out Vector3 pos, out float radius)
    {
        radius = 0f;
        if (key == VillageKey)
        {
            var saved = PlayerPrefs.GetString(PrefKey, "").Split(',');
            if (saved.Length == 4 && float.TryParse(saved[0], out float x) && float.TryParse(saved[1], out float y)
                && float.TryParse(saved[2], out float z) && float.TryParse(saved[3], out float r))
            {
                pos = new Vector3(x, y, z); radius = r; return true;
            }
            if (Find(VillageMarker, out pos)) { radius = DefaultVillageRadius; return true; }
            return false;
        }
        return Find(key, out pos);
    }

    // Scanning the whole (very large) scene is slow, so each name is looked up once per scene.
    static readonly Dictionary<string, Vector3?> _found = new();
    static int _foundScene = -1;

    static bool Find(string name, out Vector3 pos)
    {
        int scene = SceneManager.GetActiveScene().handle;
        if (scene != _foundScene) { _found.Clear(); _foundScene = scene; }
        if (!_found.TryGetValue(name, out var hit))
        {
            hit = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t.name == name) { hit = t.position; break; }
            _found[name] = hit;
        }
        pos = hit ?? default;
        return hit.HasValue;
    }

    public static void SetVillage(Vector3 pos, float radius)
    {
        PlayerPrefs.SetString(PrefKey, $"{pos.x},{pos.y},{pos.z},{radius}");
        PlayerPrefs.Save();
        Version++;
    }

    public static void ClearVillage()
    {
        PlayerPrefs.DeleteKey(PrefKey);
        Version++;
    }

    /// <summary>Snap a point onto the ground: terrain height if there's terrain, else a downward raycast.</summary>
    public static Vector3 Ground(Vector3 p)
    {
        // Use the terrain tile actually under this point (the scene may have several, or none).
        foreach (var t in Terrain.activeTerrains)
        {
            var tp = t.transform.position; var size = t.terrainData.size;
            if (p.x < tp.x || p.z < tp.z || p.x > tp.x + size.x || p.z > tp.z + size.z) continue;
            p.y = t.SampleHeight(p) + tp.y;
            return p;
        }
        if (Physics.Raycast(p + Vector3.up * 80f, Vector3.down, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
        return p;
    }

    /// <summary>A repeatable, name-seeded spot inside the village — same place every session.</summary>
    public static Vector3 VillageSpot(Vector3 center, float radius, string seed, float minFrac = 0.3f, float maxFrac = 0.8f)
    {
        uint h = 2166136261;
        foreach (char c in seed) h = (h ^ c) * 16777619;
        float a = (h & 0xFFFF) / 65535f * Mathf.PI * 2f;
        float d = Mathf.Lerp(minFrac, maxFrac, ((h >> 16) & 0xFFFF) / 65535f) * radius;
        return Ground(center + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d));
    }
}

/// <summary>
/// Builds the living village and places the small-quest NPCs and world sites. Runs in whatever world
/// scene has a village anchor (so it works in Overworld_BrokenCrescent), rebuilds when the scene changes
/// or the anchor is moved with <c>/village here</c>, and does nothing where there's no village.
/// NPCs you've already placed by hand (a SideQuestNPC with a matching name) are left alone.
/// </summary>
public class SideQuestSpawner : MonoBehaviour
{
    readonly Dictionary<string, GameObject> _sites = new();
    GameObject _root;
    int _builtScene = -1, _builtVersion = -1;
    bool _noVillage;
    float _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
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
        if (p == null) return;

        int scene = SceneManager.GetActiveScene().handle;
        bool stale = scene != _builtScene || WorldAnchors.Version != _builtVersion;
        if (stale)
        {
            if (_root != null) { _root.SetActive(false); Destroy(_root); }   // inactive first so the rebuild doesn't see its NPCs
            _root = null;
            _sites.Clear();
            _builtScene = scene; _builtVersion = WorldAnchors.Version;
            _noVillage = !WorldAnchors.TryGet(WorldAnchors.VillageKey, out Vector3 c, out float r);
            if (!_noVillage) Build(c, r);
        }
        if (_root != null) { UpdateSites(p); KeepRabbit(p); }
    }

    void Build(Vector3 center, float radius)
    {
        center = WorldAnchors.Ground(center);
        _root = new GameObject("~Village");

        VillageLife.Build(_root.transform, center, radius);

        var present = FindObjectsByType<SideQuestNPC>(FindObjectsSortMode.None).Select(n => n.npcName).ToHashSet();
        foreach (var def in SideQuests.Npcs)
        {
            if (present.Contains(def.name)) continue;
            Vector3 pos;
            if (def.anchor == WorldAnchors.VillageKey) pos = WorldAnchors.VillageSpot(center, radius, def.name);
            else if (WorldAnchors.TryGet(def.anchor, out Vector3 a, out _)) pos = WorldAnchors.Ground(a + new Vector3(3f, 0f, 3f));
            else { Debug.LogWarning($"[SideQuests] No anchor '{def.anchor}' for {def.name}; skipped."); continue; }

            var go = SideQuestActors.BuildPerson(def.name, def.color);
            go.transform.SetParent(_root.transform, true);
            go.transform.position = pos;
            go.AddComponent<SideQuestNPC>().npcName = def.name;
        }
    }

    /// <summary>Pest Control: once Farmer Hale has asked, keep the rabbit around (e.g. after a reload) until it dies.</summary>
    void KeepRabbit(PlayerEntity p)
    {
        var q = SideQuests.Find("pest");
        if (q == null || SideQuests.Stage(p, q) != 0 || !p.HasFlag("sq:pest:seen0")) return;
        var hale = FindObjectsByType<SideQuestNPC>(FindObjectsSortMode.None).FirstOrDefault(n => n.npcName == "Farmer Hale");
        if (hale != null) SideQuestActors.EnsureGiantRabbit(p, hale.transform.position);
    }

    /// <summary>World sites exist only while their stage is the active one.</summary>
    void UpdateSites(PlayerEntity p)
    {
        var wanted = new HashSet<string>();
        foreach (var q in SideQuests.All)
        {
            int n = SideQuests.Stage(p, q);
            if (n >= q.stages.Length || q.stages[n].siteName == null) continue;
            string key = $"{q.id}:{n}";
            wanted.Add(key);
            if (_sites.TryGetValue(key, out var existing) && existing != null) continue;
            var s = q.stages[n];
            if (!WorldAnchors.TryGet(s.siteAnchor, out Vector3 a, out _)) continue;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = s.siteName;
            go.transform.SetParent(_root.transform, true);
            go.transform.localScale = new Vector3(1.4f, 0.12f, 1.4f);   // a disturbed patch of earth
            go.transform.position = WorldAnchors.Ground(a + new Vector3(4f, 0f, -3f)) + Vector3.up * 0.1f;
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
}

public static class SideQuestActors
{
    static CombatTarget _rabbit;
    const string RabbitModel = "NPC/GiantRabbit";   // Assets/Resources/NPC/GiantRabbit.glb
    const float RabbitHeight = 5.5f, RabbitHalfHeight = 2.75f, RabbitWidth = 4.5f;

    static Bounds MeasureBounds(GameObject model)
    {
        var rends = model.GetComponentsInChildren<Renderer>(true);
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Scale a model to <paramref name="height"/> metres and sit its feet on the parent capsule's base.
    /// The capsule is non-uniformly scaled, so the model is measured in world space and re-parented cleanly.</summary>
    static void SizeAndSeat(GameObject model, Transform parent, float height)
    {
        if (model.GetComponentInChildren<Renderer>(true) == null) return;
        Bounds b;

        // Undo the capsule's squashed scale so the model keeps its proportions, then size it.
        var ps = parent.lossyScale;
        model.transform.localScale = new Vector3(1f / ps.x, 1f / ps.y, 1f / ps.z);
        b = MeasureBounds(model);
        if (b.size.y < 0.001f) return;
        model.transform.localScale *= height / b.size.y;
        b = MeasureBounds(model);
        float capsuleBottom = parent.position.y - parent.lossyScale.y;   // capsule primitive is 2 units tall
        model.transform.position += Vector3.up * (capsuleBottom - b.min.y);
    }

    /// <summary>A person: a collider root with a separate visual child. The visual is a generated Synty Sidekick
    /// character (see NpcAvatar), or a capsule placeholder if the Sidekick base model isn't available.</summary>
    public static GameObject BuildPerson(string name, Color color, float height = 1.9f, bool kid = false, bool guard = false)
    {
        var root = new GameObject(name);
        var col = root.AddComponent<CapsuleCollider>();
        col.height = height; col.radius = 0.4f; col.center = new Vector3(0f, height * 0.5f, 0f);

        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);

        // A real Synty Sidekick character when the base model is in the project; capsules otherwise.
        var avatar = NpcAvatar.Create(name, NpcAvatar.RandomLook(name, kid, guard));
        if (avatar != null)
        {
            avatar.transform.SetParent(visual, false);
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.identity;
            if (kid) visual.localScale = Vector3.one * 0.75f;
            return root;
        }

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(visual, false);
        body.transform.localPosition = new Vector3(0f, height * 0.45f, 0f);
        body.transform.localScale = new Vector3(0.75f, height * 0.42f, 0.75f);
        body.GetComponent<Renderer>().material.color = color;

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        Object.Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(visual, false);
        head.transform.localPosition = new Vector3(0f, height * 0.92f, 0f);
        head.transform.localScale = Vector3.one * 0.42f;
        head.GetComponent<Renderer>().material.color = new Color(0.9f, 0.75f, 0.6f);
        return root;
    }

    /// <summary>Pest Control: make sure the one enormous rabbit exists near the farmer until it's been killed.</summary>
    public static void EnsureGiantRabbit(PlayerEntity p, Vector3 farmerPos)
    {
        if (p.HasFlag(SideQuests.PestRabbitDeadFlag)) return;
        if (_rabbit != null && !_rabbit.IsDead) return;

        Vector3 spot = WorldAnchors.Ground(farmerPos + new Vector3(22f, 0f, 10f));

        var rabbit = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        rabbit.name = SideQuests.GiantRabbitName;
        // Stupidly big on purpose: roughly half the height of a house.
        rabbit.transform.localScale = new Vector3(RabbitWidth, RabbitHalfHeight, RabbitWidth);
        rabbit.transform.position = spot + Vector3.up * RabbitHalfHeight;
        // Narrow hitbox inside the huge body, so melee (1.9 m reach) can still land.
        rabbit.GetComponent<CapsuleCollider>().radius = 0.25f;

        var model = Resources.Load<GameObject>(RabbitModel);
        if (model != null)
        {
            // The real model. The capsule stays as the collider (and the pivot), invisible.
            rabbit.GetComponent<Renderer>().enabled = false;
            var visual = Object.Instantiate(model, rabbit.transform);
            visual.name = "RabbitModel";
            visual.transform.localRotation = Quaternion.identity;
            SizeAndSeat(visual, rabbit.transform, RabbitHeight);
        }
        else
        {
            // Placeholder until Assets/Resources/NPC/GiantRabbit.glb is in the project.
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
        }

        var ct = rabbit.AddComponent<CombatTarget>();
        ct.maxHP = 80; ct.attackLevel = 8; ct.defenceLevel = 6; ct.maxDamage = 5; ct.tier = 1;
        ct.isAggressive = false; ct.isMiniBoss = true;
        ct.Reset();   // AddComponent ran Awake at the default 10 HP, before maxHP was set
        var e = rabbit.AddComponent<Enemy3D>();
        e.aggroRange = 14f; e.moveSpeed = 3.6f; e.attackCooldown = 2.4f; e.respawnSeconds = 99999f;
        e.attackRange = 3.2f;   // a big body swings from further away
        _rabbit = ct;
    }
}
