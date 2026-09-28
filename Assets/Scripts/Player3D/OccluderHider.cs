using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Anything ENTIRELY above the player's head, near them, goes invisible. Walk into a crypt, a cave
/// or under a bridge and the roof over you is simply gone; walk out and it comes back. No setup, no
/// tagging, no component on the thing being hidden — it works on scene content as it already is.
///
/// THE RULE is deliberately blunt: a renderer hides when its bounds sit completely above head
/// height AND its footprint is within <see cref="overheadRadius"/> of you horizontally.
///
///   • "completely above" (bounds.min.y, not max.y) is what keeps walls, columns, doorframes and
///     pillars visible — they start at the floor, so they never qualify however tall they are.
///     Only things that are purely overhead disappear.
///   • the radius hides the WHOLE local ceiling rather than just the panel directly over your head,
///     which is what stops a tiled roof from turning into a hole you can see sky through.
///
/// It does not care about the camera angle, which is the point — you asked for "anything above the
/// character", not "anything the camera happens to be looking through".
///
/// IMPLEMENTATION NOTES, both forced by real scene content (checked against the crypt in
/// MainWorld3D: 962 renderers, ZERO colliders, ALL inside LODGroups):
///   • Detection is by renderer BOUNDS, never a physics cast — decorative kit props usually have no
///     colliders at all, so a cast would find none of them.
///   • Hiding uses Renderer.forceRenderingOff, never Renderer.enabled, because LODGroup DRIVES
///     `enabled` as it swaps levels and would turn the roof straight back on.
///   • Any collider on a hidden object is disabled too, so what you cannot see also cannot block a
///     click or a walk path. Restored together.
///
/// EXCLUDED ALWAYS: Terrain (it is one Renderer — hiding it would blink the whole landscape, so
/// cave interiors must be mesh geometry), the player, particle systems, and anything carrying
/// <see cref="KeepVisibleOverhead"/>.
///
/// Self-bootstrapping; nothing to place in a scene.
/// </summary>
public class OccluderHider : MonoBehaviour
{
    [Tooltip("How far horizontally from the player overhead geometry still hides. 0 = only things " +
             "directly over your head. Measured against the crypt in MainWorld3D: at 6m it takes " +
             "the ceiling plus its chains and chandeliers; by 25m it also strips foliage on the " +
             "hillside ABOVE the crypt, which is 'above you' but not a ceiling. Raise it if roofs " +
             "leave holes, lower it if distant hillsides start vanishing.")]
    public float overheadRadius = 6f;

    [Tooltip("Extra clearance above the player's feet that still counts as head height.")]
    public float headroom = 2.2f;

    [Tooltip("Seconds between scans. The per-scan work is arithmetic over a cached array.")]
    public float scanInterval = 0.05f;

    [Tooltip("Seconds between renderer-cache rebuilds. Picks up spawned or destroyed geometry.")]
    public float cacheInterval = 5f;

    struct Candidate
    {
        public Renderer  renderer;
        public Collider  collider;      // may be null; most decorative props have none
        public Bounds    bounds;
    }

    Candidate[] _cache = System.Array.Empty<Candidate>();
    readonly Dictionary<Renderer, Collider> _hidden = new Dictionary<Renderer, Collider>();
    readonly HashSet<Renderer> _current = new HashSet<Renderer>();
    readonly List<Renderer> _scratch = new List<Renderer>();

    float _nextScan, _nextCache;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("OccluderHider (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<OccluderHider>();
    }

    void RebuildCache()
    {
        var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
        var list = new List<Candidate>(all.Length);

        foreach (var r in all)
        {
            if (r == null) continue;
            if (r is ParticleSystemRenderer) continue;                        // fog / VFX
            if (r.GetComponent<Terrain>() != null) continue;                  // see class summary
            if (r.GetComponentInParent<PlayerEntity>() != null) continue;     // never hide yourself
            if (r.GetComponentInParent<KeepVisibleOverhead>() != null) continue;   // explicit opt-out

            list.Add(new Candidate
            {
                renderer = r,
                collider = r.GetComponent<Collider>(),
                bounds   = r.bounds
            });
        }

        _cache = list.ToArray();
    }

    void LateUpdate()
    {
        if (Time.time >= _nextCache)
        {
            _nextCache = Time.time + Mathf.Max(0.5f, cacheInterval);
            RebuildCache();
        }

        if (Time.time < _nextScan) return;
        _nextScan = Time.time + Mathf.Max(0f, scanInterval);

        var player = PlayerEntity.Instance;
        if (player == null) { RestoreAll(); return; }

        Vector3 feet  = player.transform.position;
        float headY   = feet.y + headroom;
        float radius2 = overheadRadius * overheadRadius;

        _current.Clear();

        for (int i = 0; i < _cache.Length; i++)
        {
            var c = _cache[i];
            if (c.renderer == null) continue;

            var b = c.bounds;

            // Entirely above the head — this is what spares walls, columns and doorframes, which
            // all start at floor level and so never satisfy it.
            if (b.min.y <= headY) continue;

            // Horizontal distance from the player to the bounds footprint (0 when directly overhead).
            float dx = Mathf.Max(0f, Mathf.Max(b.min.x - feet.x, feet.x - b.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(b.min.z - feet.z, feet.z - b.max.z));
            if (dx * dx + dz * dz > radius2) continue;

            _current.Add(c.renderer);
        }

        // Reveal anything that stopped qualifying.
        _scratch.Clear();
        foreach (var kv in _hidden)
            if (!_current.Contains(kv.Key)) _scratch.Add(kv.Key);
        for (int i = 0; i < _scratch.Count; i++)
        {
            var r = _scratch[i];
            if (r != null) r.forceRenderingOff = false;
            if (_hidden.TryGetValue(r, out var col) && col != null) col.enabled = true;
            _hidden.Remove(r);
        }

        // Hide anything newly qualifying.
        for (int i = 0; i < _cache.Length; i++)
        {
            var c = _cache[i];
            if (c.renderer == null || !_current.Contains(c.renderer)) continue;
            if (_hidden.ContainsKey(c.renderer)) continue;

            c.renderer.forceRenderingOff = true;
            if (c.collider != null) c.collider.enabled = false;   // invisible must also be non-blocking
            _hidden[c.renderer] = c.collider;
        }
    }

    /// <summary>Never leave the world with holes in it, or colliders switched off.</summary>
    void RestoreAll()
    {
        if (_hidden.Count == 0) return;
        foreach (var kv in _hidden)
        {
            if (kv.Key != null) kv.Key.forceRenderingOff = false;
            if (kv.Value != null) kv.Value.enabled = true;
        }
        _hidden.Clear();
    }

    void OnDisable() => RestoreAll();
    void OnDestroy() => RestoreAll();
}
