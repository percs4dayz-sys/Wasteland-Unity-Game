using UnityEngine;

/// <summary>
/// One entry in the sandbox catalog. Loads the real prefab from its original
/// path via AssetDatabase (editor-only). Falls back to a primitive only for
/// Hearthcraft items that are procedurally generated.
/// </summary>
public class SandboxAssetEntry
{
    /// <summary>Display name (derived from filename without extension).</summary>
    public string name;

    /// <summary>Category derived from folder path.</summary>
    public string category;

    /// <summary>Full project path e.g. "Assets/_Wasteland/Nodes/Junk Pile (T1).prefab".</summary>
    public string assetPath;

    /// <summary>True for Hearthcraft deployables that use BuildManager.SpawnDeployable
    /// instead of a prefab.</summary>
    public bool isHearthcraft;

    /// <summary>Load the actual prefab from anywhere in the project (editor only).
    /// Returns null in builds or if path is invalid.</summary>
    public GameObject LoadPrefab()
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
#else
        return null;
#endif
    }

    /// <summary>Instantiate the prefab at the given position/rotation.</summary>
    public GameObject Instantiate(Vector3 position, Quaternion rotation)
    {
        var prefab = LoadPrefab();
        if (prefab == null) return null;

        var go = Object.Instantiate(prefab, position, rotation);
        go.name = name;
        return go;
    }

    public override string ToString() => $"{category}/{name}";
}
