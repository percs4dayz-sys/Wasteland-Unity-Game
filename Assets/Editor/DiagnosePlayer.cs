using UnityEditor;
using UnityEngine;

/// <summary>
/// Diagnoses and fixes common player character issues:
///   - Missing Visual child with Animator â†’ no animations
///   - Missing PlayerAnimator.controller â†’ no animation clips
///   - Missing Camera.main â†’ no camera-relative movement
///   - Missing terrain collider â†’ CharacterController falls through
///
/// Menu:  Wasteland > Player > Diagnose & Fix Player
/// </summary>
public static class DiagnosePlayer
{
    // QUARANTINED 2026-08-03 — would wreck the fixed player/rig; menu disabled. See memory player-character-pipeline.
    // [MenuItem("Wasteland/Player/Diagnose & Fix Player")]
    public static void Run()
    {
        var pc = Object.FindAnyObjectByType<Player3DController>();
        if (pc == null)
        {
            EditorUtility.DisplayDialog("Diagnose Player",
                "No Player3DController found in the open scene.\n\n" +
                "Open the gameplay scene (dontfuckindelete) first.", "OK");
            return;
        }

        var sb = new System.Text.StringBuilder();
        int issues = 0;

        // â”€â”€ 1. Visual child + Animator â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var vis = pc.transform.Find("Visual");
        var anim = pc.GetComponentInChildren<Animator>();
        var renderer = pc.GetComponentInChildren<SkinnedMeshRenderer>();

        if (vis == null)
            sb.AppendLine("âœ— No 'Visual' child â€” animations won't play.");
        else if (anim == null)
            sb.AppendLine("âœ— Visual child exists but has NO Animator component.");
        else if (anim.runtimeAnimatorController == null)
            sb.AppendLine("âœ— Animator has NO controller assigned.");
        else
            sb.AppendLine("âœ“ Visual + Animator + Controller: OK");

        if (renderer == null)
            sb.AppendLine("âœ— No SkinnedMeshRenderer in children â€” player is invisible.");

        // â”€â”€ 2. Animator controller exists on disk â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var ctrl = Resources.Load<RuntimeAnimatorController>("PlayerAnimator");
        if (ctrl == null)
            sb.AppendLine("âœ— Resources/PlayerAnimator.controller is MISSING. Run 'Set Up Player Animations'.");
        else
            sb.AppendLine("âœ“ PlayerAnimator.controller found in Resources.");

        // â”€â”€ 3. Camera â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var cam = Camera.main;
        if (cam == null)
            sb.AppendLine("âœ— No Camera tagged 'MainCamera'. Camera-relative movement won't work.");
        else
            sb.AppendLine($"âœ“ MainCamera found: {cam.name}");

        // â”€â”€ 4. Orbit camera â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var orbit = Object.FindAnyObjectByType<OrbitCamera3D>();
        if (orbit == null)
            sb.AppendLine("âœ— No OrbitCamera3D in scene â€” camera won't follow player.");
        else
            sb.AppendLine("âœ“ OrbitCamera3D: OK");

        // â”€â”€ 5. Terrain / ground collider â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var ground = GameObject.Find("Ground");
        bool hasCollider = false;
        if (ground != null)
        {
            hasCollider = ground.GetComponent<Terrain>() != null ||
                          ground.GetComponent<Collider>() != null;
        }
        if (!hasCollider)
            hasCollider = Object.FindAnyObjectByType<Collider>() != null;

        if (hasCollider)
            sb.AppendLine("âœ“ Ground collider: found.");
        else
        {
            sb.AppendLine("âœ— No colliders in scene â€” CharacterController will fall through.");
            issues++;
        }

        // â”€â”€ 6. CharacterController stats â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var cc = pc.GetComponent<CharacterController>();
        if (cc != null)
        {
            sb.AppendLine($"âœ“ CharacterController: height={cc.height}, radius={cc.radius}, " +
                          $"center=({cc.center.x:F1},{cc.center.y:F1},{cc.center.z:F1})");
        }
        else
            sb.AppendLine("âœ— No CharacterController â€” movement is IMPOSSIBLE.");

        // â”€â”€ Summary â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        bool fixable = (vis == null || anim == null || (anim != null && anim.runtimeAnimatorController == null));

        string summary = sb.ToString();
        string action = fixable
            ? "\n\nClick 'Fix' to run:\n  1. 'Set Up Player Animations' (builds controller)\n  2. 'Use player.fbx As Player Visual' (adds Visual+Animator)"
            : "\n\nEverything looks OK. If movement still doesn't work, check:\n  â€¢ Are you clicking or using WASD?\n  â€¢ Is there a UI menu open blocking input?\n  â€¢ Is the terrain collider actually walkable?";

        if (fixable)
        {
            if (EditorUtility.DisplayDialog("Player Diagnostic", summary + action, "Fix", "Cancel"))
            {
                // Step 1: Build the animator controller
                EditorUtility.DisplayProgressBar("Fixing Player", "Building animator controller...", 0.3f);
                EditorApplication.ExecuteMenuItem("Wasteland/Player/1. Set Up Rigs + Animator (Mixamo)");

                // Step 2: Set the player.fbx as the Visual child
                EditorUtility.DisplayProgressBar("Fixing Player", "Setting player.fbx as Visual...", 0.7f);
                EditorApplication.ExecuteMenuItem("Wasteland/Player/Use player.fbx As Player Visual");

                EditorUtility.ClearProgressBar();

                EditorUtility.DisplayDialog("Player Fix Applied",
                    "Ran:\n" +
                    "  1. Set Up Rigs + Animator (Mixamo)\n" +
                    "  2. Use player.fbx As Player Visual\n\n" +
                    "The player should now have a Visual child with Animator.\n" +
                    "Press Play and test with WASD or click-to-move.", "OK");
            }
        }
        else
        {
            EditorUtility.DisplayDialog("Player Diagnostic", summary + action, "OK");
        }
    }
}
