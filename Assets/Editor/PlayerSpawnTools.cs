using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fixes a player spawn that sits off the map or under the terrain.
///
/// There is no spawn-point object in this project: PlayerRespawn captures transform.position in
/// Start(), so wherever the Player GameObject sits in the saved scene IS the spawn. That makes
/// "fix my spawn" the same job as "put the Player somewhere sane and save the scene".
///
/// Two entry points:
///   • Snap Player Spawn To Ground — keeps the player's X/Z and drops them onto the terrain.
///     If the X/Z is outside the terrain entirely (the off-the-map case) it recentres them first.
///   • Move Player Spawn To Scene View — puts the player where the Scene view camera is looking,
///     so you can fly to the spot you want and pin the spawn there.
///
/// Both use terrain.SampleHeight rather than a raycast, so they work whether or not colliders are
/// present, and both are undoable.
/// </summary>
public static class PlayerSpawnTools
{
    const float StandOffset = 0.1f;   // sit just above the surface so the controller settles cleanly

    [MenuItem("Wasteland/Fix/Snap Player Spawn To Ground", false, 100)]
    public static void SnapToGround()
    {
        var player = FindPlayer();
        if (player == null) return;

        var terrain = FindTerrain();
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Snap Player Spawn",
                "No Terrain in this scene, so there's no ground to snap to.", "OK");
            return;
        }

        Vector3 pos = player.position;
        bool recentred = false;

        if (!IsOverTerrain(terrain, pos))
        {
            pos = TerrainCentre(terrain);
            recentred = true;
        }

        Place(player, terrain, pos);

        string where = recentred
            ? "You were outside the terrain bounds, so I recentred you before dropping you down."
            : "Kept your X/Z and dropped you onto the surface.";

        Debug.Log($"[PlayerSpawn] Spawn set to {player.position}. {where}");
        EditorUtility.DisplayDialog("Snap Player Spawn",
            $"Spawn is now {player.position}.\n\n{where}\n\n" +
            "SAVE THE SCENE (Ctrl+S) — the spawn is read from the saved scene at Play.", "OK");
    }

    [MenuItem("Wasteland/Fix/Move Player Spawn To Scene View", false, 101)]
    public static void MoveToSceneView()
    {
        var player = FindPlayer();
        if (player == null) return;

        var view = SceneView.lastActiveSceneView;
        if (view == null)
        {
            EditorUtility.DisplayDialog("Move Player Spawn",
                "Open a Scene view and look at the spot you want first.", "OK");
            return;
        }

        Vector3 pos = view.pivot;   // the point the Scene camera orbits — what you're looking at
        var terrain = FindTerrain();
        if (terrain != null) Place(player, terrain, pos);
        else
        {
            Undo.RecordObject(player, "Move player spawn");
            player.position = pos;
            MarkDirty();
        }

        Debug.Log($"[PlayerSpawn] Spawn moved to the Scene view pivot: {player.position}.");
        EditorUtility.DisplayDialog("Move Player Spawn",
            $"Spawn is now {player.position}.\n\nSAVE THE SCENE (Ctrl+S).", "OK");
    }

    /// <summary>Report where the player is and whether that spot is actually on the map — read-only,
    /// for when you just want to know why you're falling into the void.</summary>
    [MenuItem("Wasteland/Fix/Report Player Spawn", false, 102)]
    public static void Report()
    {
        var player = FindPlayer();
        if (player == null) return;

        var terrain = FindTerrain();
        if (terrain == null) { Debug.Log($"[PlayerSpawn] Player at {player.position}. No terrain in scene."); return; }

        Vector3 tp = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        bool over = IsOverTerrain(terrain, player.position);
        float ground = terrain.SampleHeight(player.position) + tp.y;

        // Tolerance matters: a player standing correctly sits within a few mm of the surface, and
        // float noise puts them on either side of it. Only a real gap means anything.
        const float Tol = 0.25f;
        float delta = player.position.y - ground;
        string verdict =
            delta < -Tol ? $"{-delta:F2}m BELOW the surface — buried / would fall through" :
            delta >  2f  ? $"{delta:F2}m ABOVE the surface — would drop in on spawn" :
                           "resting on the surface (fine)";

        Debug.Log(
            $"[PlayerSpawn] Player at {player.position}\n" +
            $"  Terrain covers X {tp.x:F1} → {tp.x + size.x:F1}, Z {tp.z:F1} → {tp.z + size.z:F1}\n" +
            $"  Over terrain: {over}\n" +
            $"  Ground at that X/Z: {ground:F3}   Player Y: {player.position.y:F3}\n" +
            $"  → {verdict}");
    }

    /// <summary>Create/move the authored WorldSpawnPoint marker to the player's current position.
    /// This is the spot new characters start at and where void/death recovery returns you — it does
    /// NOT affect normal save-and-resume.</summary>
    [MenuItem("Wasteland/Fix/Set World Spawn Point Here", false, 120)]
    public static void SetWorldSpawnHere()
    {
        var player = FindPlayer();
        if (player == null) return;

        var marker = Object.FindAnyObjectByType<WorldSpawnPoint>();
        if (marker == null)
        {
            var go = new GameObject("WorldSpawnPoint");
            Undo.RegisterCreatedObjectUndo(go, "Create world spawn point");
            marker = Undo.AddComponent<WorldSpawnPoint>(go);
        }
        else Undo.RecordObject(marker.transform, "Move world spawn point");

        Vector3 pos = player.position;
        var terrain = FindTerrain();
        if (terrain != null && IsOverTerrain(terrain, pos))
            pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y + StandOffset;

        marker.transform.position = pos;
        marker.transform.rotation = player.rotation;
        Selection.activeGameObject = marker.gameObject;
        MarkDirty();

        Debug.Log($"[PlayerSpawn] World spawn point set to {pos} (facing {player.eulerAngles.y:F1}°).");
        EditorUtility.DisplayDialog("World Spawn Point",
            $"Spawn point set to {pos}.\n\n" +
            "New characters start here, and this is where you're returned if you fall under the " +
            "map or die. Normal save-and-resume is unaffected.\n\nSAVE THE SCENE (Ctrl+S).", "OK");
    }

    /// <summary>Unstick the player DURING PLAY without needing the chat box. Same code path as the
    /// /stuck command, but driven from the menu — useful when the chat field isn't focused and your
    /// keystrokes are going to editor shortcuts instead.</summary>
    [MenuItem("Wasteland/Fix/Unstick Player Now (Play Mode)", false, 130)]
    public static void UnstickNow()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Unstick Player",
                "This one only works while the game is running — press Play first.", "OK");
            return;
        }

        var pr = Object.FindAnyObjectByType<PlayerRespawn>();
        if (pr == null)
        {
            EditorUtility.DisplayDialog("Unstick Player", "No PlayerRespawn in the running scene.", "OK");
            return;
        }

        pr.ReturnToSpawn();
        Debug.Log("[PlayerSpawn] Unstuck — player returned to the spawn point.");
    }

    /// <summary>List every collider sitting outside the world — the stray sky/void geometry that
    /// makes ground-snapping and the out-of-bounds watchdog misbehave. Selects them so you can see
    /// and delete them.</summary>
    [MenuItem("Wasteland/Fix/Find Objects Outside The World", false, 140)]
    public static void FindStrayGeometry()
    {
        var terrain = FindTerrain();
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Find Stray Geometry", "No Terrain in this scene.", "OK");
            return;
        }

        Vector3 tp = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        float ceiling = tp.y + size.y + 150f;   // matches PlayerRespawn's default ceilingMargin
        float floor   = tp.y - 100f;

        var found = new System.Collections.Generic.List<GameObject>();
        var sb = new System.Text.StringBuilder();

        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include))
        {
            if (col == null || col is TerrainCollider) continue;
            Vector3 c = col.bounds.center;
            bool outside = c.y > ceiling || c.y < floor
                        || c.x < tp.x - 5f || c.x > tp.x + size.x + 5f
                        || c.z < tp.z - 5f || c.z > tp.z + size.z + 5f;
            if (!outside) continue;

            found.Add(col.gameObject);
            if (found.Count <= 30)
                sb.AppendLine($"  {col.gameObject.name}  at {c}  (size {col.bounds.size})");
        }

        if (found.Count == 0)
        {
            Debug.Log($"[StrayGeometry] Nothing outside the world. Allowed Y {floor:F0}→{ceiling:F0}.");
            EditorUtility.DisplayDialog("Find Stray Geometry", "Nothing found outside the world.", "OK");
            return;
        }

        Selection.objects = found.ToArray();
        Debug.LogWarning($"[StrayGeometry] {found.Count} collider(s) outside the world " +
                         $"(allowed Y {floor:F0}→{ceiling:F0}):\n{sb}");
        EditorUtility.DisplayDialog("Find Stray Geometry",
            $"Found {found.Count} collider(s) outside the world — they're now selected in the " +
            "Hierarchy, and listed in the Console with positions.\n\n" +
            "These are what the spawn's ground-snap was catching. Delete or move them.", "OK");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    static void Place(Transform player, Terrain terrain, Vector3 pos)
    {
        pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y + StandOffset;
        Undo.RecordObject(player, "Set player spawn");
        player.position = pos;
        MarkDirty();
    }

    static Transform FindPlayer()
    {
        var pe = Object.FindAnyObjectByType<PlayerEntity>();
        if (pe != null) return pe.transform;

        EditorUtility.DisplayDialog("Player Spawn",
            "No PlayerEntity found in the open scene.", "OK");
        return null;
    }

    static Terrain FindTerrain()
    {
        if (Terrain.activeTerrain != null) return Terrain.activeTerrain;
        var all = Object.FindObjectsByType<Terrain>();
        return all.Length > 0 ? all[0] : null;
    }

    static bool IsOverTerrain(Terrain terrain, Vector3 p)
    {
        Vector3 tp = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return p.x >= tp.x && p.x <= tp.x + size.x
            && p.z >= tp.z && p.z <= tp.z + size.z;
    }

    static Vector3 TerrainCentre(Terrain terrain)
    {
        Vector3 tp = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return new Vector3(tp.x + size.x * 0.5f, 0f, tp.z + size.z * 0.5f);
    }

    static void MarkDirty() => EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
}
