using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Project tidy-up + world grouping for the Wasteland-made assets ONLY. Deliberately does NOT
/// touch imported asset packs — moving their files by type breaks their scripts/Resources loading.
///
/// Menus:
///   Wasteland ▸ Tools ▸ Organize My Files Into _Wasteland   — moves OUR assets into a by-type structure
///   Wasteland ▸ World ▸ Group Maps Under One World Object    — parents Ground + Cityscape under "World"
/// </summary>
public static class WastelandOrganizer
{
    // MENU REMOVED — this bulk-moves project files and can scramble the whole project if misclicked.
    // Re-enable the [MenuItem] line only if you deliberately want to run it.
    // [MenuItem("Wasteland/Tools/Organize My Files Into _Wasteland")]
    public static void Organize()
    {
        // Make the by-type folders.
        WastelandPaths.EnsureFolder(WastelandPaths.Models);
        WastelandPaths.EnsureFolder(WastelandPaths.Materials);
        WastelandPaths.EnsureFolder(WastelandPaths.Shaders);
        WastelandPaths.EnsureFolder(WastelandPaths.Textures);

        // (sourceAssetPath, destinationFolder) — only the files WE generated this project.
        var moves = new (string asset, string dest)[]
        {
            ("WorldGround.glb",        WastelandPaths.Models),
            ("Cityscape.glb",          WastelandPaths.Models),
            ("BiomeTerrain.shader",    WastelandPaths.Shaders),
            ("BiomeTerrain.mat",       WastelandPaths.Materials),
            ("BiomeTerrainCity.mat",   WastelandPaths.Materials),
            ("BiomeMask.png",          WastelandPaths.Textures),
        };

        var log = new List<string>();
        foreach (var (asset, dest) in moves)
        {
            string from = WastelandPaths.Legacy + "/" + asset;
            string to   = dest + "/" + asset;
            if (AssetDatabase.LoadAssetAtPath<Object>(from) == null) continue;   // already moved or never existed
            if (AssetDatabase.LoadAssetAtPath<Object>(to) != null) { log.Add(asset + " — already in place"); continue; }

            // AssetDatabase.MoveAsset preserves GUIDs, so every scene/material reference stays intact.
            string err = AssetDatabase.MoveAsset(from, to);
            log.Add(string.IsNullOrEmpty(err) ? ("moved " + asset) : ("FAILED " + asset + ": " + err));
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Organize Wasteland Files",
            "Your generated assets now live under " + WastelandPaths.Root + " (by type):\n\n" +
            string.Join("\n", log) + "\n\n" +
            "Asset packs were left untouched on purpose (moving their files breaks them). " +
            "To FIND anything by type without moving it, type in the Project search bar: " +
            "t:Texture, t:Material, t:Prefab, t:Mesh — and right-click any folder ▸ \"Add to Favorites\".",
            "OK");
    }

    [MenuItem("Wasteland/World/Fix Player Spawn (drop onto ground)")]
    public static void FixPlayerSpawn()
    {
        var pc = Object.FindAnyObjectByType<Player3DController>();
        var player = pc != null ? pc.gameObject : GameObject.Find("Player");
        if (player == null)
        {
            EditorUtility.DisplayDialog("Fix Player Spawn", "Couldn't find the Player in the scene.", "OK");
            return;
        }

        var cc = player.GetComponent<CharacterController>();
        Vector3 p = player.transform.position;

        // Raycast down onto the ground at the player's XZ (then world origin as fallback), skipping
        // the player's own colliders. Spawning the capsule slightly ABOVE the surface stops the
        // CharacterController from ejecting itself out of the terrain ("flying off the map").
        float gx = p.x, gz = p.z;
        if (!GroundHit(p.x, p.z, player.transform, out float surfaceY))
        {
            if (!GroundHit(0f, 0f, player.transform, out surfaceY))
            {
                EditorUtility.DisplayDialog("Fix Player Spawn",
                    "No ground collider found beneath the player. Make sure your Ground has colliders " +
                    "(re-run 'Use GLB Model as Ground' if needed), then try again.", "OK");
                return;
            }
            gx = 0f; gz = 0f;   // fell back to world origin
        }

        float half    = cc != null ? cc.height * 0.5f : 1f;
        float centerY  = cc != null ? cc.center.y : 0f;
        float newY     = surfaceY + half - centerY + 0.25f;   // capsule bottom just above the surface

        Undo.RecordObject(player.transform, "Fix Player Spawn");
        player.transform.position = new Vector3(gx, newY, gz);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = player;
        SceneView.FrameLastActiveSceneView();

        EditorUtility.DisplayDialog("Fix Player Spawn",
            "Player placed at y = " + newY.ToString("0.0") + " (ground surface y = " + surfaceY.ToString("0.0") + ").\n\n" +
            "Press Play — it should stand on the ground instead of flying off.\n\n" +
            "IMPORTANT: SAVE the scene now (Ctrl+S) — a lot of your work is currently unsaved.", "OK");
    }

    static bool GroundHit(float x, float z, Transform skip, out float surfaceY)
    {
        surfaceY = 0f;
        // Start well above any world height; accept only UP-facing tops (not walls or the underside).
        var hits = Physics.RaycastAll(new Ray(new Vector3(x, 100000f, z), Vector3.down), 200000f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(skip)) continue;   // ignore the player's own colliders
            if (h.normal.y < 0.2f) continue;                      // skip vertical faces & the slab underside
            surfaceY = h.point.y;
            return true;
        }
        return false;
    }

    [MenuItem("Wasteland/World/Drop Selected To Ground")]
    public static void DropSelectedToGround()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        {
            EditorUtility.DisplayDialog("Drop To Ground",
                "Select one or more objects in the scene first, then run this to sit them on the terrain below.", "OK");
            return;
        }

        int dropped = 0;
        foreach (var go in sel)
        {
            Vector3 p = go.transform.position;
            var hits = Physics.RaycastAll(new Ray(new Vector3(p.x, 10000f, p.z), Vector3.down), 20000f);
            bool found = false; float bestY = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(go.transform)) continue;   // don't land on our own colliders
                if (h.point.y > bestY) { bestY = h.point.y; found = true; }
            }
            if (found)
            {
                Undo.RecordObject(go.transform, "Drop To Ground");
                go.transform.position = new Vector3(p.x, bestY, p.z);
                dropped++;
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Drop To Ground",
            "Dropped " + dropped + " of " + sel.Length + " object(s) onto the terrain.\n\n" +
            "If a building sinks in, its pivot is at its center — just nudge it up with W. " +
            "(Needs the ground's colliders; the hidden Cityscape mesh won't interfere.)", "OK");
    }

    [MenuItem("Wasteland/World/Group Maps Under One World Object")]
    public static void GroupMaps()
    {
        var ground = GameObject.Find("Ground");
        var city   = GameObject.Find("Cityscape");
        if (ground == null && city == null)
        {
            EditorUtility.DisplayDialog("Group Maps", "No 'Ground' or 'Cityscape' found in the scene.", "OK");
            return;
        }

        var world = GameObject.Find("World");
        if (world == null)
        {
            world = new GameObject("World");
            Undo.RegisterCreatedObjectUndo(world, "Create World");
            world.transform.position = Vector3.zero;
        }

        if (ground != null && ground.transform.parent != world.transform)
            Undo.SetTransformParent(ground.transform, world.transform, "Parent Ground");
        if (city != null && city.transform.parent != world.transform)
            Undo.SetTransformParent(city.transform, world.transform, "Parent Cityscape");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = world;

        EditorUtility.DisplayDialog("Group Maps",
            "Both map halves are now under one 'World' object. Move/rotate/scale 'World' to move the whole " +
            "world as a unit; the pieces stay put relative to each other.\n\n" +
            "(This groups them — it doesn't weld the meshes. For these big AI meshes that's the practical " +
            "'one thing', with no downside. Ask if you ever need a single combined mesh for export.)", "OK");
    }
}
