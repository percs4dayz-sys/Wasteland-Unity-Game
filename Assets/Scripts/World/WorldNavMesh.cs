using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Loads a scene's baked navmesh (the click-to-move routes) while the scene is open. Baked and wired
/// by Wasteland ▸ Broken Crescent ▸ Bake Click-to-Move Navigation (BCNavMeshBaker).
/// </summary>
public class WorldNavMesh : MonoBehaviour
{
    public NavMeshData data;
    NavMeshDataInstance _instance;

    void OnEnable() { if (data != null) _instance = NavMesh.AddNavMeshData(data); }
    void OnDisable() { if (_instance.valid) _instance.Remove(); }
}
