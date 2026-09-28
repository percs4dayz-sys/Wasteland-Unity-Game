using UnityEngine;

/// <summary>
/// A piece of loot lying in the 3D world (bones/remains dropped by slain monsters, but works for
/// any item). You pick it up by CLICKING it, or — for controller — by standing near it and pressing
/// A/E ("Take …"); it plugs into the same ClickToMove3D + Interactor3D flow as nodes and NPCs.
/// There is NO walk-over auto-pickup — collection is always a deliberate click/press.
///
/// On death the drop takes one game tick (0.6s) to hit the ground: it falls in, and the instant it
/// lands it becomes grabbable (its collider switches on). Until then it can't be clicked.
///
/// Fully self-building: spawns its own visual (the item's icon as a billboard, or a small
/// bone-coloured nub if the item has no icon yet) and a trigger collider, so no prefab wiring is
/// needed. Despawns a while after landing so the ground doesn't fill up with loot.
/// </summary>
public class GroundItem : MonoBehaviour, IExaminable
{
    /// <summary>Every drop currently in the world (the minimap marks them red).</summary>
    public static readonly System.Collections.Generic.List<GroundItem> All = new();

    public int itemId;
    public int quantity = 1;
    public int[] modules;
    public float lifeSeconds = 120f;       // despawn (timed from when it lands)

    const float DropRise = 0.55f;          // how far above its resting height it starts before falling in
    const float HoverY   = 0.35f;          // resting height above the ground point

    bool _grabbable;
    long _landTick = long.MaxValue;        // tick to land on (1 after spawn); MaxValue = use time fallback
    float _landAtTime;                     // time fallback when there's no GameTick yet
    float _restY, _spinT, _despawnAt, _landedAt = -1f;
    SpriteRenderer _sr;                    // set when we render the item's icon as a billboard
    Transform _nub;                        // set when we fall back to a primitive (icon-less items)
    Transform _visual;                     // the icon/nub child we scale-punch on landing
    Vector3 _visualBaseScale = Vector3.one;
    SphereCollider _col;
    static Material _lootMat;

    /// <summary>Drop an item into the world at a position (the monster's corpse, usually). It falls in
    /// over one tick before it can be grabbed.</summary>
    public static GroundItem Spawn(int itemId, int quantity, Vector3 pos)
    {
        if (ItemRegistry.Get(itemId) == null) return null;
        var go = new GameObject("GroundItem_" + itemId);
        // Small random scatter so several drops don't stack on the exact same point.
        go.transform.position = pos + new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f));
        var gi = go.AddComponent<GroundItem>();
        gi.itemId = itemId;
        gi.quantity = Mathf.Max(1, quantity);
        gi.Build(pos.y);
        return gi;
    }

    public string DisplayName => ItemRegistry.Get(itemId)?.name ?? "item";
    public string ExamineText
    {
        get
        {
            var item = ItemRegistry.Get(itemId);
            return item != null && !string.IsNullOrEmpty(item.description) ? item.description : "Something lying on the ground.";
        }
    }

    void OnEnable()  { GameTick.OnTick += OnGameTick; All.Add(this); }
    void OnDisable() { GameTick.OnTick -= OnGameTick; All.Remove(this); }

    void Build(float groundY)
    {
        _restY = groundY + HoverY;
        // Start raised; it falls to _restY over the landing tick.
        transform.position = new Vector3(transform.position.x, _restY + DropRise, transform.position.z);

        // Trigger collider: clickable + overlap-detectable, never blocks movement. OFF until it lands.
        _col = gameObject.AddComponent<SphereCollider>();
        _col.radius = 0.5f;
        _col.isTrigger = true;
        _col.enabled = false;

        // Land exactly one tick after spawning (OSRS-style). Time fallback if the ticker isn't up yet.
        if (GameTick.Instance != null) _landTick = GameTick.Instance.TickCount + 1;
        else _landAtTime = Time.time + GameTick.TICK_DURATION;

        var item = ItemRegistry.Get(itemId);
        if (item != null && item.icon != null)
        {
            var sprGo = new GameObject("Icon");
            sprGo.transform.SetParent(transform, false);
            _sr = sprGo.AddComponent<SpriteRenderer>();
            _sr.sprite = item.icon;
            _sr.sortingOrder = 40;
            // Icons use different pixel sizes / pixels-per-unit. Size the visible sprite in
            // world metres instead of multiplying its arbitrary imported dimensions.
            Vector3 size = item.icon.bounds.size;
            float longestSide = Mathf.Max(size.x, size.y);
            sprGo.transform.localScale = Vector3.one * (0.7f / Mathf.Max(0.001f, longestSide));
            _visual = sprGo.transform;
        }
        else
        {
            // No icon yet → a small bone-coloured nub so the drop is still visible & clickable.
            _nub = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            var stray = _nub.GetComponent<Collider>();
            if (stray != null) Destroy(stray);             // our trigger collider does the work
            _nub.SetParent(transform, false);
            _nub.localScale = new Vector3(0.3f, 0.2f, 0.3f);
            var r = _nub.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = LootMat();
            _visual = _nub;
        }
        if (_visual != null) _visualBaseScale = _visual.localScale;
    }

    void OnGameTick(long tick)
    {
        if (!_grabbable && tick >= _landTick) Land();
    }

    void Land()
    {
        _grabbable = true;
        _col.enabled = true;                   // grabbable the instant it lands
        _despawnAt = Time.time + lifeSeconds;  // lifetime starts now
        _landedAt = Time.time;                 // kicks off the scale-punch "pop"
    }

    /// <summary>A quick overshoot-and-settle on the visual right after landing, so the drop pops.</summary>
    void ApplyLandingPop()
    {
        if (_visual == null) return;
        const float popDur = 0.28f;
        float since = Time.time - _landedAt;
        float k = (_landedAt >= 0f && since < popDur)
            ? 1f + 0.35f * Mathf.Sin(since / popDur * Mathf.PI)   // 1 → 1.35 → 1
            : 1f;
        _visual.localScale = _visualBaseScale * k;
    }

    void Update()
    {
        if (!_grabbable)
        {
            // Time fallback when there was no ticker at spawn.
            if (_landTick == long.MaxValue && Time.time >= _landAtTime) Land();
            // Fall in toward the resting height.
            float fy = Mathf.MoveTowards(transform.position.y, _restY, 4f * Time.deltaTime);
            transform.position = new Vector3(transform.position.x, fy, transform.position.z);
            BillboardIcon();
            return;
        }

        if (Time.time >= _despawnAt) { Destroy(gameObject); return; }

        // Bob and spin so loot reads clearly on the ground.
        _spinT += Time.deltaTime;
        float y = _restY + Mathf.Sin(_spinT * 2f) * 0.08f;
        transform.position = new Vector3(transform.position.x, y, transform.position.z);
        transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
        ApplyLandingPop();
        BillboardIcon();
    }

    void BillboardIcon()
    {
        if (_sr != null && Camera.main != null)
            _sr.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
    }

    /// <summary>Deliberate pickup (click / press-E or controller A). Reports success or a full pack.</summary>
    public bool TryPickUp(bool silentFull = false)
    {
        if (!_grabbable) return false;
        var p = PlayerEntity.Instance;
        if (p == null) return false;
        if (p.Inventory.Add(new ItemStack(itemId, quantity) { modules = modules }))
        {
            var item = ItemRegistry.Get(itemId);
            string qty = quantity > 1 ? quantity + "x " : "";
            HUDController.Emit($"<color=#C9A227>[LOOT]:</color> You pick up {qty}{item?.name}.");
            Destroy(gameObject);
            return true;
        }
        if (!silentFull) HUDController.Emit("<color=#C9A227>[LOOT]:</color> Your pack is full — make room to grab that.");
        return false;
    }

    static Material LootMat()
    {
        if (_lootMat != null) return _lootMat;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        _lootMat = new Material(sh) { name = "LootNub" };
        var bone = new Color(0.86f, 0.82f, 0.72f);
        _lootMat.color = bone;
        if (_lootMat.HasProperty("_BaseColor")) _lootMat.SetColor("_BaseColor", bone);
        return _lootMat;
    }
}
