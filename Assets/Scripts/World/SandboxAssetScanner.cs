using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Scans project folders for .prefab files and builds the sandbox catalog.
/// Runs in the editor only — at runtime (build), returns an empty list.
/// Also includes Hearthcraft deployables as a special category.
/// </summary>
public static class SandboxAssetScanner
{
    /// <summary>Folders to scan, mapped to their display category.</summary>
    static readonly (string folder, string category)[] ScanRoots = new[]
    {
        // Skill nodes
        ("Assets/_Wasteland/Nodes",                        "Skill Nodes"),

        // Urban city pack
        ("Assets/Low Poly Simple Urban City 3D Asset Pack/Prefabs/Buildings", "Buildings"),
        ("Assets/Low Poly Simple Urban City 3D Asset Pack/Prefabs/Vehicles",  "Vehicles"),
        ("Assets/Low Poly Simple Urban City 3D Asset Pack/Prefabs/Roads",     "Roads"),
        ("Assets/Low Poly Simple Urban City 3D Asset Pack/Prefabs/Props",     "Props"),

        // Fantasy environments
        ("Assets/FantasyEnvironments/Environments/Town/Prefabs",              "Fantasy Town"),
        ("Assets/FantasyEnvironments/Environments/Prefabs",                   "Nature"),
        ("Assets/FantasyEnvironments/Environments/Ambient-Occlusion-Trees/Prefabs", "Trees"),

        // Sea port
        ("Assets/Old Sea Port/prefabs",                    "Sea Port"),

        // Low poly environment
        ("Assets/LowPoly Environment Pack/Prefabs",        "Environment"),

        // Medieval modular (AzureHillside)
        ("Assets/LeartesStudios/AzureHillside/Art/Prefabs","Medieval"),

        // Modular world builder
        ("Assets/PolyOne/Modular World Builder - Lite/Source/Prefabs", "Terrain"),

        // Enemies
        ("Assets/Resources/Enemies",                       "Enemies"),

        // Foliage
        ("Assets/Resources/Foliage",                       "Foliage"),
    };

    static List<SandboxAssetEntry> _cached;
    static bool _scanned;

    /// <summary>Build (or return cached) the full catalog.</summary>
    public static List<SandboxAssetEntry> BuildCatalog()
    {
        if (_scanned) return _cached ?? new List<SandboxAssetEntry>();
        _scanned = true;
        _cached = new List<SandboxAssetEntry>();

#if UNITY_EDITOR
        var seen = new HashSet<string>();

        foreach (var (folder, category) in ScanRoots)
        {
            if (!Directory.Exists(folder)) continue;

            // Recursively find all .prefab files.
            var files = Directory.GetFiles(folder, "*.prefab", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                // Convert backslashes to forward slashes for AssetDatabase.
                var assetPath = file.Replace('\\', '/');
                if (!seen.Add(assetPath)) continue; // skip duplicates

                var name = Path.GetFileNameWithoutExtension(file);

                _cached.Add(new SandboxAssetEntry
                {
                    name = name,
                    category = category,
                    assetPath = assetPath,
                });
            }
        }
#endif

        // Always add Hearthcraft deployables (procedural, no prefab file).
        foreach (var b in global::BuildCatalog.All())
        {
            _cached.Add(new SandboxAssetEntry
            {
                name = b.name,
                category = "Hearthcraft",
                isHearthcraft = true,
            });
        }

        return _cached;
    }

    /// <summary>Rebuild from scratch (e.g. after importing new assets).</summary>
    public static void InvalidateCache() => _scanned = false;
}
