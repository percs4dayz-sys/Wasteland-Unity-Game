using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Logs the Humanoid bone mapping for the Crimson-Circuit FBX so you can
/// see exactly which bones are misconfigured (causing twisted limbs).
///
/// Menu:  Wasteland > Player > Debug: Crimson-Circuit Bone Map
/// </summary>
public static class DebugCrimsonCircuitRig
{
    const string FbxPath = "Assets/Art/newshit/finalofficialplayercharacter/Crimson-Circuit-019f196d.fbx";

    [MenuItem("Wasteland/Player/Debug: Crimson-Circuit Bone Map")]
    public static void Run()
    {
        var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (imp == null)
        {
            Debug.LogError($"[RigDebug] FBX not found: {FbxPath}");
            return;
        }

        var hd = imp.humanDescription;
        var map = new Dictionary<string, string>();
        foreach (var hb in hd.human) map[hb.humanName] = hb.boneName;

        // Build skeleton lookup
        var bones = new Dictionary<string, Vector3>();
        foreach (var sb in hd.skeleton) bones[sb.name] = sb.position;

        var av = AssetDatabase.LoadAssetAtPath<Avatar>(FbxPath);
        var sb2 = new System.Text.StringBuilder();
        sb2.AppendLine($"=== CRIMSON-CIRCUIT AVATAR DEBUG ===");
        sb2.AppendLine($"Path: {FbxPath}");
        sb2.AppendLine($"AnimationType: {imp.animationType}");
        sb2.AppendLine($"AvatarSetup: {imp.avatarSetup}");
        sb2.AppendLine($"Avatar valid: {av != null && av.isValid}, human: {av != null && av.isHuman}");
        sb2.AppendLine($"Skeleton bones: {hd.skeleton.Length}");
        sb2.AppendLine();

        // Key body parts to check
        string[] criticalBones = {
            "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
            "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg",
            "LeftFoot", "RightFoot", "LeftToes", "RightToes",
            "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm",
            "LeftHand", "RightHand"
        };

        sb2.AppendLine("Critical bone mapping:");
        sb2.AppendLine("-----------------------");
        int unmapped = 0;
        foreach (var k in criticalBones)
        {
            string bone = map.TryGetValue(k, out var b) ? b : "(UNMAPPED)";
            if (bone == "(UNMAPPED)") unmapped++;
            string status = bone == "(UNMAPPED)" ? " \u274c" : " \u2713";
            sb2.AppendLine($"  {k,-16} -> {bone}{status}");
        }

        sb2.AppendLine();
        sb2.AppendLine($"Unmapped critical bones: {unmapped}");
        sb2.AppendLine();

        if (unmapped > 0)
        {
            sb2.AppendLine("FIX: Select the FBX in Project window -> Rig tab -> Configure.");
            sb2.AppendLine("     Drag the correct bones into each red slot, then click Apply -> Done.");
        }
        else
        {
            sb2.AppendLine("All bones are mapped. If limbs are still twisted, the bone");
            sb2.AppendLine("ORIENTATIONS are wrong. In the Configure window:");
            sb2.AppendLine("  - Look at the model in the Scene view");
            sb2.AppendLine("  - Green guide bones should align with the model's actual bones");
            sb2.AppendLine("  - If a green bone points the wrong way, click it and rotate");
            sb2.AppendLine("    in the Inspector until it matches the model's pose");
            sb2.AppendLine("  - Common issue: UpperLeg bone pointing sideways instead of down");
            sb2.AppendLine("  - Common issue: Foot bone twisted 90 degrees");
        }

        sb2.AppendLine();
        sb2.AppendLine("All skeleton bone names:");
        foreach (var sb in hd.skeleton)
            sb2.AppendLine($"  {sb.name}");

        Debug.Log(sb2.ToString());
    }

    // QUARANTINED 2026-08-03 — would wreck the fixed player/rig; menu disabled. See memory player-character-pipeline.
    // [MenuItem("Wasteland/Player/Auto-Configure Crimson-Circuit Avatar")]
    public static void AutoConfigure()
    {
        var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (imp == null) { Debug.LogError("FBX not found"); return; }

        // Force Humanoid + CreateFromThisModel to trigger Unity's auto-mapping
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.SaveAndReimport();
        AssetDatabase.Refresh();

        var av = AssetDatabase.LoadAssetAtPath<Avatar>(FbxPath);
        if (av != null && av.isValid && av.isHuman)
        {
            var hd = imp.humanDescription;
            int mapped = hd.human.Count(hb => !string.IsNullOrEmpty(hb.boneName));
            Debug.Log($"[AutoConfig] Done. Avatar valid={av.isValid}, human={av.isHuman}, bones mapped={mapped}/{hd.human.Length}");
        }
        else
        {
            Debug.LogWarning("[AutoConfig] Avatar not valid Humanoid after auto-config. Manual setup needed.");
        }

        EditorUtility.DisplayDialog("Avatar Auto-Configured",
            "Re-imported as Humanoid with CreateFromThisModel.\n\n" +
            "Check the Console for mapping results.\n" +
            "If bones are still twisted, select the FBX -> Rig -> Configure to manually fix.", "OK");
    }
}
