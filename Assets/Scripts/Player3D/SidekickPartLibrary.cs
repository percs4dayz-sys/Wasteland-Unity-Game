using UnityEngine;

/// <summary>
/// Finds a single Sidekick part mesh on demand.
///
/// Deliberately NOT Synty's runtime API. That path opens a SQLite database, loads the entire part
/// catalogue into memory, and rebuilds the whole character from scratch on every change — which is
/// both far more than this game needs and heavy enough to exhaust the editor's memory while
/// browsing options. Sidekick's job here ends at authoring: it supplies the meshes, and we treat
/// them as ordinary assets from then on.
///
/// The path is derived from the part's own name, so nothing has to be scanned or indexed:
///   SK_HUMN_BASE_*  → Resources/Meshes/Species/Humans/&lt;name&gt;
///   everything else → Resources/Meshes/Outfits/Starter/&lt;name&gt;
/// Only the parts actually worn are ever loaded.
/// </summary>
public static class SidekickPartLibrary
{
    const string BaseFolder   = "Meshes/Species/Humans/";
    const string OutfitFolder = "Meshes/Outfits/Starter/";

    /// <summary>Resources path for a part name (no extension).</summary>
    public static string ResourcePath(string partName)
    {
        if (string.IsNullOrEmpty(partName)) return null;
        return (CharacterCreatorConfig.IsSelectable(partName) ? BaseFolder : OutfitFolder) + partName;
    }

    /// <summary>Load the part prefab, or null if the name doesn't resolve. Cheap and cached by
    /// Unity; call it freely.</summary>
    public static GameObject Load(string partName)
    {
        string path = ResourcePath(partName);
        if (path == null) return null;

        var go = Resources.Load<GameObject>(path);
        if (go == null)
            Debug.LogWarning($"[SidekickParts] '{partName}' not found at Resources/{path}.");
        return go;
    }

    /// <summary>The skinned renderer carrying a part's mesh, or null.</summary>
    public static SkinnedMeshRenderer LoadRenderer(string partName)
    {
        var go = Load(partName);
        return go != null ? go.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
    }
}
