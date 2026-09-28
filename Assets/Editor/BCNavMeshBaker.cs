using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Bakes the click-to-move navmesh for the open world scene, so a click routes round walls, fences
/// and buildings instead of walking straight into them. BlackwaterNavigator on the player follows it.
///
///  • Agent = the player's own capsule (radius, height, step, slope from its CharacterController).
///  • Built from physics colliders (what actually blocks the player): terrain, buildings, walls, the
///    tier borders. Door leaves (SwingDoor) and tier-gate shutters are left out so routes go through
///    doorways and gates — doors swing open as you arrive; a shut tier gate still stops you at it.
///    Creatures, NPCs and the player are left out too (they move).
///  • Everything below the waterline is off-limits.
/// Bakes in the background (a few minutes for the whole island), then saves
/// Worlds/BrokenCrescent/Navigation/BC_ClickToMove.asset and points the scene's "BC_Navigation"
/// (WorldNavMesh) at it. Save the scene afterwards.
///
/// Menu: Wasteland ▸ Broken Crescent ▸ Bake Click-to-Move Navigation. Re-bake after moving buildings,
/// walls or gates.
/// </summary>
public static class BCNavMeshBaker
{
    const string Folder = "Assets/Worlds/BrokenCrescent/Navigation";
    const string AssetPath = Folder + "/BC_ClickToMove.asset";

    static AsyncOperation _op;
    static NavMeshData _data;
    static NavMeshDataInstance _preview;
    static double _startedAt;
    static int _sourceCount;

    /// <summary>What the last bake did (or how far the running one has got).</summary>
    public static string Status { get; private set; } = "not baked this session";
    public static bool Busy => _op != null && !_op.isDone;

    [MenuItem("Wasteland/Broken Crescent/Bake Click-to-Move Navigation")]
    public static void BakeMenu() => Debug.Log(Bake());

    public static string Bake()
    {
        if (Busy) return Status = $"[NavMesh] still baking: {_op.progress:P0}";

        var terrains = Terrain.activeTerrains;
        if (terrains.Length == 0) return Status = "[NavMesh] no terrain in the open scene.";
        var bounds = new Bounds(terrains[0].transform.position + terrains[0].terrainData.size * 0.5f, terrains[0].terrainData.size);
        foreach (var t in terrains)
            bounds.Encapsulate(new Bounds(t.transform.position + t.terrainData.size * 0.5f, t.terrainData.size));
        bounds.Expand(new Vector3(0f, 200f, 0f));

        var cc = Object.FindFirstObjectByType<PlayerEntity>(FindObjectsInactive.Include)?.GetComponent<CharacterController>();
        var settings = NavMesh.GetSettingsByID(0);
        // A touch slimmer than the capsule so ~1 m doorways stay open on the mesh (the capsule itself
        // fits and slides along the frame).
        settings.agentRadius = Mathf.Min(cc != null ? cc.radius : 0.35f, 0.28f);
        settings.agentHeight = cc != null ? cc.height : 1.8f;
        settings.agentClimb  = cc != null ? Mathf.Max(0.25f, cc.stepOffset) : 0.3f;
        settings.agentSlope  = cc != null ? cc.slopeLimit : 45f;
        settings.overrideVoxelSize = true;
        settings.voxelSize = settings.agentRadius / 3f;     // fine enough for 1 m doorways
        settings.overrideTileSize = true;
        settings.tileSize = 256;
        settings.minRegionArea = 4f;

        // Leave out what moves or opens.
        var markups = new List<NavMeshBuildMarkup>();
        void Ignore(Component c) { if (c != null) markups.Add(new NavMeshBuildMarkup { root = c.transform, ignoreFromBuild = true }); }
        foreach (var d in Object.FindObjectsByType<SwingDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Ignore(d);
        foreach (var l in Object.FindObjectsByType<BCGateBossLink>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var s in l.shutters ?? new Transform[0]) Ignore(s);
        foreach (var e in Object.FindObjectsByType<Enemy3D>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Ignore(e);
        foreach (var p in Object.FindObjectsByType<PlayerEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Ignore(p);
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (mb is ITalkableNPC) Ignore(mb);

        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
        sources.RemoveAll(s => s.component is Collider c && (c.isTrigger || !c.enabled));

        // Below the waterline is off-limits (the shore is an invisible wall for the player anyway).
        float sea = SeaLevel();
        sources.Add(new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.ModifierBox,
            area = 1,   // Not Walkable
            transform = Matrix4x4.TRS(new Vector3(bounds.center.x, sea - 250f, bounds.center.z), Quaternion.identity, Vector3.one),
            size = new Vector3(bounds.size.x + 20f, 500f, bounds.size.z + 20f),
        });

        _sourceCount = sources.Count;
        _data = new NavMeshData(settings.agentTypeID) { name = "BC_ClickToMove" };
        _op = NavMeshBuilder.UpdateNavMeshDataAsync(_data, settings, sources, bounds);
        _startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        return Status = $"[NavMesh] baking {_sourceCount} sources over {bounds.size.x:F0} x {bounds.size.z:F0} m " +
                        $"(agent r{settings.agentRadius} h{settings.agentHeight} step {settings.agentClimb} slope {settings.agentSlope}, voxel {settings.voxelSize:F3})…";
    }

    static void Poll()
    {
        if (_op == null) { EditorApplication.update -= Poll; return; }
        if (!_op.isDone)
        {
            Status = $"[NavMesh] baking: {_op.progress:P0} after {EditorApplication.timeSinceStartup - _startedAt:F0} s";
            return;
        }
        EditorApplication.update -= Poll;
        _op = null;
        Finish();
    }

    static void Finish()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Worlds/BrokenCrescent", "Navigation");
        if (AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath) != null) AssetDatabase.DeleteAsset(AssetPath);
        AssetDatabase.CreateAsset(_data, AssetPath);
        AssetDatabase.SaveAssets();

        var host = GameObject.Find("BC_Navigation") ?? new GameObject("BC_Navigation");
        var world = host.GetComponent<WorldNavMesh>() ?? host.AddComponent<WorldNavMesh>();
        world.data = _data;
        EditorUtility.SetDirty(world);
        EditorSceneManager.MarkSceneDirty(host.scene);

        // Load it in the editor too, so path checks work without entering Play.
        if (_preview.valid) _preview.Remove();
        _preview = NavMesh.AddNavMeshData(_data);

        var tri = NavMesh.CalculateTriangulation();
        Status = $"[NavMesh] baked in {EditorApplication.timeSinceStartup - _startedAt:F0} s from {_sourceCount} sources: " +
                 $"{tri.indices.Length / 3} triangles → {AssetPath}. Save the scene to keep the link.";
        Debug.Log(Status);
    }

    // The editor preview must not ride into Play mode on top of the scene's own WorldNavMesh copy.
    [InitializeOnLoadMethod]
    static void DropPreviewOnPlay() => EditorApplication.playModeStateChanged += s =>
    {
        if (s == PlayModeStateChange.ExitingEditMode && _preview.valid) _preview.Remove();
    };

    /// <summary>Loads the saved navmesh into the editor (for path checks outside Play).</summary>
    public static void LoadPreview()
    {
        var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath);
        if (data == null) return;
        if (_preview.valid) _preview.Remove();
        _preview = NavMesh.AddNavMeshData(data);
    }

    static float SeaLevel()
    {
        var sea = GameObject.Find("Sea Level (Water)");
        return sea != null ? sea.transform.position.y : 18f;
    }
}
