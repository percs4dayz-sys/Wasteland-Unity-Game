using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves an item's held-model prefab by NAME, so the art can be organized into ANY folder layout
/// under Resources/HeldModels without breaking references. ItemData.heldModel is still a path like
/// "HeldModels/Weapons/ScrapBlade" — we try that exact path first (fast), and if the file has since
/// been moved to a different subfolder we fall back to matching the last path segment ("ScrapBlade")
/// against every model under Resources/HeldModels.
///
/// This is what lets you reorganize the HeldModels folders (Weapons / Tools / Shields / Armor / …)
/// freely — the game finds the model by filename regardless of which folder it lives in.
/// </summary>
public static class HeldModels
{
    static Dictionary<string, GameObject> _byName;   // filename (no path) → prefab, lazy-built

    public static GameObject Load(string heldModelPath)
    {
        if (string.IsNullOrEmpty(heldModelPath)) return null;

        // 1) Exact path (current/normal case).
        var direct = Resources.Load<GameObject>(heldModelPath);
        if (direct != null) return direct;

        // 2) Fallback: match by filename anywhere under Resources/HeldModels (handles moved files).
        int slash = heldModelPath.LastIndexOf('/');
        string leaf = slash >= 0 ? heldModelPath.Substring(slash + 1) : heldModelPath;
        return ByName().TryGetValue(leaf, out var go) ? go : null;
    }

    static Dictionary<string, GameObject> ByName()
    {
        if (_byName != null) return _byName;
        _byName = new Dictionary<string, GameObject>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var go in Resources.LoadAll<GameObject>("HeldModels"))
            if (go != null) _byName[go.name] = go;   // last wins; item filenames are distinct
        return _byName;
    }

    /// <summary>Drop the cache so a freshly-imported/moved model is picked up (called from editor tools).</summary>
    public static void ClearCache() => _byName = null;
}
