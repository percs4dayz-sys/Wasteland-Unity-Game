using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Only what's near the player is switched on. At scene start the world's static content (buildings,
/// props, walls, doors, lanterns, gathering spots, creatures, decals, point lights) is sorted into a
/// grid of cells. Every quarter second, cells within <see cref="radius"/> of the player are switched on
/// and cells beyond it are switched off. Switched-off objects aren't drawn, collided with or updated,
/// which is the big frame-rate win on phones. Everything stays loaded, so memory use doesn't change
/// (that would need true scene streaming).
///
/// Safe by construction:
///  • Only objects that were switched on at load are managed. Hidden-by-design objects stay hidden.
///  • A branch is only switched off if every script in it is on the "safe to pause" list below. Anything
///    else (Roxy, the gate/boss links, tier gates, spawners, post-processing volumes, managers) keeps
///    running, as do the terrain, the sun, cameras and anything bigger than a cell (the sea, the
///    combined border-seam collider).
///  • A big jump (respawn, /stuck, a waypoint) switches the new surroundings on at once, not over frames.
///
/// On phones the radius is tighter, and a linear fog fades the world out just inside it to hide the edge.
/// </summary>
[DefaultExecutionOrder(-50)]
public class WorldCellStreamer : MonoBehaviour
{
    [Tooltip("Metres around the player that stay switched on (desktop).")]
    public float radius = 280f;
    [Tooltip("Metres around the player that stay switched on (phones and tablets).")]
    public float mobileRadius = 140f;
    [Tooltip("Extra distance before a cell switches back off, so the edge doesn't flicker.")]
    public float hysteresis = 25f;
    public float cellSize = 64f;
    [Tooltip("Anything wider than this is left always on: terrain, the sea, huge combined meshes.")]
    public float maxUnitSize = 48f;
    [Tooltip("On phones: fog that fades the world out just inside the radius, hiding where it stops.")]
    public bool mobileFog = true;
    [Tooltip("Objects switched on or off per frame while walking (a jump does them all at once).")]
    public int togglesPerFrame = 400;
    [Tooltip("Extra script types (class names) that are safe to pause when far away.")]
    public List<string> extraSafeScripts = new();

    // Scripts that do nothing useful when you're nowhere near them. Creatures are deliberately NOT here:
    // they move, so they'd be switched off by the cell they spawned in while chasing you somewhere else.
    // There are only a handful, and CreatureMotion already skips its work when nobody can see it.
    static readonly HashSet<string> SafeScripts = new()
    {
        "SwingDoor", "LanternSwing", "BoatWaveMotion", "FishingShimmer", "ResourceNode", "CraftingStation",
        "BankingCrate", "AFKStation", "BCBossArenaMotion", "DecalProjector", "UniversalAdditionalLightData",
    };

    class Cell
    {
        public readonly List<GameObject> units = new();
        public Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        public bool on = true;
    }

    struct Info { public bool safe, content; public Bounds bounds; }

    readonly Dictionary<Vector2Int, Cell> _cells = new();
    readonly Dictionary<Transform, Info> _info = new();
    readonly Queue<(GameObject go, bool on)> _pending = new();
    HashSet<string> _safe;
    Vector3 _lastEval;
    float _nextEval;
    bool _ready;

    public int UnitCount { get; private set; }
    public int CellCount => _cells.Count;
    public int ActiveUnits { get; private set; }
    public float Radius => Application.isMobilePlatform ? mobileRadius : radius;

    void Start()
    {
        _safe = new HashSet<string>(SafeScripts);
        foreach (var s in extraSafeScripts) if (!string.IsNullOrEmpty(s)) _safe.Add(s);

        foreach (var root in gameObject.scene.GetRootGameObjects()) Measure(root.transform);
        foreach (var root in gameObject.scene.GetRootGameObjects()) Assign(root.transform);
        _info.Clear();
        UnitCount = 0;
        foreach (var c in _cells.Values) UnitCount += c.units.Count;
        ActiveUnits = UnitCount;   // everything starts on; the first pass switches the far cells off

        if (Application.isMobilePlatform) ApplyMobileView();
        _ready = true;
        var p = PlayerEntity.Instance;
        if (p != null) Evaluate(p.transform.position, instant: true);   // start with only the neighbourhood on
    }

    // ── sorting the world into cells ─────────────────────────────────────

    /// <summary>Post-order: is this branch safe to pause, does it show or collide anything, and how big is it.</summary>
    Info Measure(Transform t)
    {
        var info = new Info { safe = true };
        // Hidden on purpose (a tree's stump, a disabled blockade): not ours, and no reason to stop its
        // parent being switched as a whole — switching a parent never changes a child's own on/off state.
        if (!t.gameObject.activeSelf) { _info[t] = info; return info; }
        if (t == transform) info.safe = false;
        foreach (var c in t.GetComponents<Component>())
        {
            switch (c)
            {
                case null: break;                                                    // missing script: harmless
                case Terrain _: case Camera _: case AudioListener _: case WindZone _:
                    info.safe = false; break;
                case Light l when l.type == LightType.Directional: info.safe = false; break;
                // Lamps and decals have no mesh of their own but cost a lot on phones — manage them too.
                case Light l when l.enabled: Grow(ref info, new Bounds(l.transform.position, Vector3.one * Mathf.Min(l.range * 2f, 40f))); break;
                case UnityEngine.Rendering.Universal.DecalProjector d when d.enabled:
                    Grow(ref info, new Bounds(d.transform.position, Vector3.one * Mathf.Max(1f, d.size.magnitude))); break;
                case Renderer r when r.enabled: Grow(ref info, r.bounds); break;
                case Collider col when col.enabled: Grow(ref info, col.bounds); break;
                case MonoBehaviour mb when !_safe.Contains(mb.GetType().Name): info.safe = false; break;
            }
        }
        foreach (Transform child in t)
        {
            var ci = Measure(child);
            if (!ci.safe) info.safe = false;
            if (ci.content) Grow(ref info, ci.bounds);
        }
        _info[t] = info;
        return info;
    }

    static void Grow(ref Info info, Bounds b)
    {
        if (!info.content) { info.bounds = b; info.content = true; }
        else info.bounds.Encapsulate(b);
    }

    /// <summary>Top-down: the highest branch that's safe and small enough becomes one unit. Anything too big
    /// or holding a script that must keep running is split, and its children are tried instead.</summary>
    void Assign(Transform t)
    {
        if (!_info.TryGetValue(t, out var info) || !info.content || !t.gameObject.activeInHierarchy) return;
        var size = info.bounds.size;
        if (info.safe && Mathf.Max(size.x, size.z) <= maxUnitSize)
        {
            var b = info.bounds;
            var key = new Vector2Int(Mathf.FloorToInt(b.center.x / cellSize), Mathf.FloorToInt(b.center.z / cellSize));
            if (!_cells.TryGetValue(key, out var cell)) _cells[key] = cell = new Cell();
            cell.units.Add(t.gameObject);
            cell.min = Vector2.Min(cell.min, new Vector2(b.min.x, b.min.z));
            cell.max = Vector2.Max(cell.max, new Vector2(b.max.x, b.max.z));
            return;
        }
        foreach (Transform child in t) Assign(child);
    }

    // ── per frame ────────────────────────────────────────────────────────

    void Update()
    {
        if (!_ready) return;
        var p = PlayerEntity.Instance;
        if (p != null && Time.unscaledTime >= _nextEval)
        {
            _nextEval = Time.unscaledTime + 0.25f;
            Vector3 pos = p.transform.position;
            bool jumped = (pos - _lastEval).sqrMagnitude > Radius * Radius * 0.25f;   // respawn, /stuck, waypoint
            Evaluate(pos, jumped);
        }
        int budget = togglesPerFrame;
        while (budget-- > 0 && _pending.Count > 0) Apply(_pending.Dequeue());
    }

    void Evaluate(Vector3 pos, bool instant)
    {
        _lastEval = pos;
        float r = Radius;
        foreach (var cell in _cells.Values)
        {
            float dx = Mathf.Max(cell.min.x - pos.x, 0f, pos.x - cell.max.x);
            float dz = Mathf.Max(cell.min.y - pos.z, 0f, pos.z - cell.max.y);
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            bool want = cell.on ? d <= r + hysteresis : d <= r;
            if (want == cell.on) continue;
            cell.on = want;
            ActiveUnits += want ? cell.units.Count : -cell.units.Count;
            foreach (var go in cell.units) _pending.Enqueue((go, want));
        }
        if (instant) while (_pending.Count > 0) Apply(_pending.Dequeue());
    }

    static void Apply((GameObject go, bool on) t)
    {
        if (t.go != null && t.go.activeSelf != t.on) t.go.SetActive(t.on);
    }

    void ApplyMobileView()
    {
        // Android holds games to 30 fps unless asked for more, which read as "choppy". Ask for 60; the
        // phone gives what it can.
        Application.targetFrameRate = 60;
        // Spread cell switches thinner so walking over a cell edge doesn't hitch a frame.
        togglesPerFrame = Mathf.Min(togglesPerFrame, 120);
        float r = mobileRadius;
        if (mobileFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = r * 0.55f;
            RenderSettings.fogEndDistance = r * 0.95f;
        }
        var cam = Camera.main;
        if (cam != null) cam.farClipPlane = Mathf.Min(cam.farClipPlane, r + 40f);
        // Models that ship lower-detail versions (the Hivemind buildings, tents, carts) drop to them sooner.
        QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias, 0.5f);
        // Shadows were two-thirds of the triangles drawn; phones get a short shadow reach and a
        // slightly lower render resolution. (Only in a phone build: in the editor this would edit the asset.)
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
        {
            urp.shadowDistance = Mathf.Min(urp.shadowDistance, 30f);
            urp.shadowCascadeCount = 1;
            urp.renderScale = Mathf.Min(urp.renderScale, 0.85f);
            urp.msaaSampleCount = 1;
        }
        foreach (var t in Terrain.activeTerrains)
        {
            t.heightmapPixelError = Mathf.Max(t.heightmapPixelError, 10f);
            t.basemapDistance = Mathf.Min(t.basemapDistance, 120f);
            t.detailObjectDistance = Mathf.Min(t.detailObjectDistance, 60f);
            t.treeDistance = Mathf.Min(t.treeDistance, r);
        }
    }

    void OnDestroy()
    {
        // Leaving the scene: nothing to restore (the objects go with it). In the editor, stopping Play
        // restores the scene as saved, so nothing is left switched off.
        _pending.Clear();
    }
}
