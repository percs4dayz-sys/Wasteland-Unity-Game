using UnityEngine;

/// <summary>
/// Where the playable world actually is, derived from the terrain footprint. Shared by everything
/// that needs to ask "is this position a real place?".
///
/// Two callers, deliberately:
///   • SaveData.ApplyTo — refuses to restore a saved position that's outside the world, so a corrupt
///     save can never put you somewhere impossible in the first place.
///   • PlayerRespawn — the running watchdog, for anything that strands you mid-session.
///
/// The vertical test is the one that matters most in practice. A bad saved Y parks you thousands of
/// metres up while X/Z stay perfectly valid, and if any collider exists at that height you can walk
/// around on it — so a footprint check and a "is there ground below me" check both report fine.
/// </summary>
public static class WorldBounds
{
    // Defaults for callers with no inspector of their own (e.g. the save loader).
    public const float DefaultEdgeMargin    = 5f;
    public const float DefaultCeilingMargin = 150f;
    public const float DefaultFloorMargin   = 100f;

    static bool _ready, _has;
    static Vector3 _min, _max;

    /// <summary>Drop the cached bounds — call on scene load if terrain is swapped at runtime.</summary>
    public static void Invalidate() { _ready = false; _has = false; }

    /// <summary>The terrain footprint, unioned across every terrain in the scene.
    /// False when the scene has no terrain at all (nothing to be outside of).</summary>
    public static bool TryGet(out Vector3 min, out Vector3 max)
    {
        if (!_ready) Cache();
        min = _min; max = _max;
        return _has;
    }

    static void Cache()
    {
        _ready = true;
        _has = false;

        var terrains = Object.FindObjectsByType<Terrain>();
        foreach (var t in terrains)
        {
            if (t == null || t.terrainData == null) continue;
            Vector3 lo = t.transform.position;
            Vector3 hi = lo + t.terrainData.size;
            if (!_has) { _min = lo; _max = hi; _has = true; }
            else { _min = Vector3.Min(_min, lo); _max = Vector3.Max(_max, hi); }
        }
    }

    /// <summary>True if the position is outside the world — off the edge horizontally, or stranded
    /// far above/below it. Always false when the scene has no terrain, so terrain-less scenes are
    /// never second-guessed.</summary>
    public static bool IsOutside(Vector3 p,
                                 float edgeMargin    = DefaultEdgeMargin,
                                 float ceilingMargin = DefaultCeilingMargin,
                                 float floorMargin   = DefaultFloorMargin)
    {
        if (!TryGet(out var min, out var max)) return false;

        if (p.x < min.x - edgeMargin || p.x > max.x + edgeMargin) return true;
        if (p.z < min.z - edgeMargin || p.z > max.z + edgeMargin) return true;
        if (p.y > max.y + ceilingMargin || p.y < min.y - floorMargin) return true;
        return false;
    }

    /// <summary>Which test a position fails, and by how much — for logs. Empty string when it's
    /// inside. Guessing at "out of bounds" from a bare boolean is what makes these bugs slow.</summary>
    public static string Explain(Vector3 p,
                                 float edgeMargin    = DefaultEdgeMargin,
                                 float ceilingMargin = DefaultCeilingMargin,
                                 float floorMargin   = DefaultFloorMargin)
    {
        if (!TryGet(out var min, out var max)) return "";

        if (p.x < min.x - edgeMargin) return $"X {p.x:F1} is {min.x - edgeMargin - p.x:F1}m past the west edge ({min.x:F0})";
        if (p.x > max.x + edgeMargin) return $"X {p.x:F1} is {p.x - max.x - edgeMargin:F1}m past the east edge ({max.x:F0})";
        if (p.z < min.z - edgeMargin) return $"Z {p.z:F1} is {min.z - edgeMargin - p.z:F1}m past the south edge ({min.z:F0})";
        if (p.z > max.z + edgeMargin) return $"Z {p.z:F1} is {p.z - max.z - edgeMargin:F1}m past the north edge ({max.z:F0})";
        if (p.y > max.y + ceilingMargin) return $"Y {p.y:F1} is {p.y - max.y - ceilingMargin:F1}m above the ceiling ({max.y + ceilingMargin:F1})";
        if (p.y < min.y - floorMargin) return $"Y {p.y:F1} is {min.y - floorMargin - p.y:F1}m below the floor ({min.y - floorMargin:F1})";
        return "";
    }

    /// <summary>Human-readable bounds for logging.</summary>
    public static string Describe(float edgeMargin = DefaultEdgeMargin,
                                  float ceilingMargin = DefaultCeilingMargin,
                                  float floorMargin = DefaultFloorMargin)
    {
        if (!TryGet(out var min, out var max)) return "no terrain in scene";
        return $"X {min.x:F0}→{max.x:F0}, Z {min.z:F0}→{max.z:F0} (+{edgeMargin}m), " +
               $"Y allowed {min.y - floorMargin:F0}→{max.y + ceilingMargin:F0}";
    }
}
