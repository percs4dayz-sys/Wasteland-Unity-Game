using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;

/// <summary>
/// Quick scene setup: places Roxy, ensures the player and camera are correct.
/// 
/// Usage: Wasteland → Setup → ...
/// </summary>
public static class SceneSetupTools
{
    [MenuItem("Wasteland/Setup/Place Roxy in Scene")]
    public static void PlaceRoxy()
    {
        // Check if Roxy already exists
        var existing = Object.FindAnyObjectByType<RoxyNPC>();
        if (existing != null)
        {
            Debug.Log($"[Setup] Roxy already in scene at {existing.transform.position}. Selecting her.");
            Selection.activeGameObject = existing.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            return;
        }

        // Find Roxy's glTF model asset
        string roxyPath = "Assets/Art/newshit/Roxy/scene.gltf";
        var roxyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(roxyPath);

        if (roxyAsset == null)
        {
            // Try finding any GameObject in the Roxy folder
            var guids = AssetDatabase.FindAssets("scene", new[] { "Assets/Art/newshit/Roxy" });
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                roxyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (roxyAsset != null) break;
            }
        }

        if (roxyAsset == null)
        {
            Debug.LogError("[Setup] Could not find Roxy's model at Assets/Art/newshit/Roxy/scene.gltf. " +
                           "Make sure the glTF importer (glTFast) is installed.");
            return;
        }

        // Instantiate in scene at origin (or near spawn)
        Vector3 spawnPos = Vector3.zero;
        // Try to find a reasonable spawn point - look for World3DBootstrap
        var bootstrap = Object.FindAnyObjectByType<World3DBootstrap>();
        if (bootstrap != null) spawnPos = bootstrap.transform.position + new Vector3(3, 0, 0);

        var roxyGO = (GameObject)PrefabUtility.InstantiatePrefab(roxyAsset);
        roxyGO.name = "Roxy";  // ← RoxyAutoSetup looks for this name!
        roxyGO.transform.position = spawnPos;

        Undo.RegisterCreatedObjectUndo(roxyGO, "Place Roxy");

        Selection.activeGameObject = roxyGO;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        Debug.Log($"[Setup] Roxy placed at {spawnPos}. Her NPC component, collider, and animation " +
                  "will auto-wire on Play (RoxyAutoSetup).");
    }

    [MenuItem("Wasteland/Setup/Ensure Player + Camera")]
    public static void EnsurePlayerCamera()
    {
        var player = Object.FindAnyObjectByType<PlayerEntity>();
        var camera = Object.FindAnyObjectByType<OrbitCamera3D>();

        string report = "";

        if (player == null)
        {
            report += "⚠ No PlayerEntity found in scene. ";
        }
        else
        {
            report += $"✅ Player found: '{player.name}' at {player.transform.position}. ";
            // Check it has the right components
            if (player.GetComponent<CharacterController>() == null)
                report += "⚠ Missing CharacterController! ";
            if (player.GetComponent<Player3DController>() == null)
                report += "⚠ Missing Player3DController! ";
            if (player.GetComponent<PlayerAnimator3D>() == null)
                report += "⚠ Missing PlayerAnimator3D! ";
        }

        if (camera == null)
        {
            report += "⚠ No OrbitCamera3D found.";
        }
        else
        {
            report += $"✅ Camera found: '{camera.name}'. ";
            if (camera.target == null)
                report += "⚠ Camera target is null (will self-heal on Play).";
            else
                report += $"Following: '{camera.target.name}'.";
        }

        Debug.Log($"[Setup] {report}");

        if (player != null) Selection.activeGameObject = player.gameObject;
    }

    [MenuItem("Wasteland/Setup/Full Scene Check")]
    public static void FullSceneCheck()
    {
        Debug.Log("=== Wasteland Scene Check ===");

        // Player
        var player = Object.FindAnyObjectByType<PlayerEntity>();
        Debug.Log(player != null
            ? $"✅ PlayerEntity: {player.name} at {player.transform.position}"
            : "❌ No PlayerEntity in scene!");

        // Camera
        var cam = Object.FindAnyObjectByType<OrbitCamera3D>();
        if (cam != null)
            Debug.Log(cam.target != null
                ? $"✅ OrbitCamera3D following: {cam.target.name}"
                : "⚠ OrbitCamera3D has no target (self-heals on Play)");
        else
            Debug.Log("❌ No OrbitCamera3D in scene!");

        // Roxy
        var roxy = Object.FindAnyObjectByType<RoxyNPC>();
        Debug.Log(roxy != null
            ? $"✅ Roxy NPC: {roxy.name}"
            : "⚠ No RoxyNPC — run Wasteland → Setup → Place Roxy in Scene");

        // Core systems
        var gt = Object.FindAnyObjectByType<GameTick>();
        Debug.Log(gt != null ? "✅ GameTick" : "⚠ No GameTick (will auto-spawn)");

        var mode = Object.FindAnyObjectByType<World3DBootstrap>();
        Debug.Log(mode != null ? "✅ World3DBootstrap" : "⚠ No World3DBootstrap");

        // Tilemap/terrain
        var grid = Object.FindAnyObjectByType<Grid>();
        var tm = Object.FindAnyObjectByType<Tilemap>();
        Debug.Log(grid != null
            ? $"✅ Tilemap Grid (cells: {grid.cellSize})"
            : "⚠ No Tilemap Grid — run Wasteland → Terrain → Create Tilemap Setup");

        Debug.Log("=== Check complete ===");
    }
}
