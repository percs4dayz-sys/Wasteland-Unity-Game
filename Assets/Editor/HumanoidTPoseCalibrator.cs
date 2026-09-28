using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Forces a Humanoid model's avatar into a TRUE T-pose, so animation authored elsewhere (Mixamo,
/// most marketplace libraries) retargets onto it cleanly.
///
/// THE PROBLEM THIS SOLVES â€” and it recurs with every character pack, not just Synty:
/// Humanoid retargeting expresses animation as a deviation from the avatar's T-pose. Mixamo clips
/// are authored against a strict T-pose: arms exactly horizontal, legs straight, feet pointing
/// forward. Most character art is authored in an A-pose. If the avatar's neutral is an A-pose, EVERY
/// frame of EVERY clip inherits that offset â€” which reads in-game as arms held out to the sides,
/// elbows bent, and feet rotated outward.
///
/// Unity's "Enforce T-Pose" button estimates a T-pose from the bind pose and is often not accurate
/// enough on an A-pose rig. This tool does it properly: it instantiates the model, rotates each
/// humanoid limb so it points exactly along the canonical axis, then writes those rotations back
/// into the importer's human description as the avatar's T-pose.
///
/// One calibration fixes the whole animation library at once â€” it is a property of the avatar, not
/// of any individual clip.
///
/// Menu: Wasteland â–¸ Fix â–¸ Calibrate Humanoid T-Pose (Selected Model)
/// </summary>
public static class HumanoidTPoseCalibrator
{
    // Canonical T-pose directions, in the model's own space (character faces +Z, +Y up).
    static readonly Vector3 RightArmDir = Vector3.right;    // +X
    static readonly Vector3 LeftArmDir = Vector3.left;      // -X
    static readonly Vector3 LegDir = Vector3.down;          // -Y
    static readonly Vector3 FootDir = Vector3.forward;      // +Z

    // QUARANTINED 2026-08-03 — would wreck the fixed player/rig; menu disabled. See memory player-character-pipeline.
    // [MenuItem("Wasteland/Fix/Calibrate Humanoid T-Pose (Selected Model)", false, 300)]
    public static void Calibrate()
    {
        var obj = Selection.activeObject;
        string path = obj != null ? AssetDatabase.GetAssetPath(obj) : null;
        var importer = !string.IsNullOrEmpty(path) ? AssetImporter.GetAtPath(path) as ModelImporter : null;

        if (importer == null)
        {
            EditorUtility.DisplayDialog("Calibrate T-Pose",
                "Select the character's MODEL FILE (.fbx) in the Project window.\n\n" +
                "This must be the imported model, not a generated .asset or a prefab â€” the T-pose " +
                "lives on the model's importer.", "OK");
            return;
        }

        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            EditorUtility.DisplayDialog("Calibrate T-Pose",
                $"'{System.IO.Path.GetFileName(path)}' isn't set to Humanoid.\n\n" +
                "Set Rig â–¸ Animation Type to Humanoid first.", "OK");
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Calibrate T-Pose", "Couldn't load the model.", "OK");
            return;
        }

        // Work on a throwaway instance so nothing in a real scene is disturbed.
        var inst = Object.Instantiate(prefab);
        inst.hideFlags = HideFlags.HideAndDontSave;

        try
        {
            var anim = inst.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman)
            {
                EditorUtility.DisplayDialog("Calibrate T-Pose",
                    "The model has no valid humanoid avatar yet. Apply the Humanoid rig first, " +
                    "then run this.", "OK");
                return;
            }

            int fixedCount = 0;
            fixedCount += AlignChain(anim, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, RightArmDir);
            fixedCount += AlignChain(anim, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, RightArmDir);
            fixedCount += AlignChain(anim, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, LeftArmDir);
            fixedCount += AlignChain(anim, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, LeftArmDir);

            fixedCount += AlignChain(anim, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, LegDir);
            fixedCount += AlignChain(anim, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, LegDir);
            fixedCount += AlignChain(anim, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, LegDir);
            fixedCount += AlignChain(anim, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, LegDir);

            fixedCount += AlignChain(anim, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, FootDir);
            fixedCount += AlignChain(anim, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, FootDir);

            if (fixedCount == 0)
            {
                EditorUtility.DisplayDialog("Calibrate T-Pose",
                    "Couldn't resolve the limb bones from the avatar â€” nothing was changed.", "OK");
                return;
            }

            // Capture the posed skeleton back into the importer as the avatar's T-pose.
            var byName = new Dictionary<string, Transform>();
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;

            var desc = importer.humanDescription;
            var skel = desc.skeleton;
            int written = 0;
            for (int i = 0; i < skel.Length; i++)
            {
                if (!byName.TryGetValue(skel[i].name, out var t)) continue;
                skel[i].rotation = t.localRotation;
                skel[i].position = t.localPosition;
                skel[i].scale = t.localScale;
                written++;
            }
            desc.skeleton = skel;
            importer.humanDescription = desc;

            // MenuItem context, never OnGUI â€” a reimport triggered from a repainting callback loops
            // at frame rate and locks the editor.
            importer.SaveAndReimport();

            Debug.Log($"[TPose] Calibrated '{System.IO.Path.GetFileName(path)}': aligned {fixedCount} " +
                      $"limb segments, wrote {written} skeleton bones.");
            EditorUtility.DisplayDialog("Calibrate T-Pose",
                $"'{System.IO.Path.GetFileName(path)}' is now in a true T-pose.\n\n" +
                $"Aligned {fixedCount} limb segments.\n\n" +
                "Press Play and check the walk. If limbs are still off, open Rig â–¸ Configure â–¸ Pose " +
                "to see the result and tell me which joint is wrong.", "OK");
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }
    }

    /// <summary>Rotate <paramref name="from"/> so the bone segment from it to <paramref name="to"/>
    /// points along <paramref name="targetDir"/>. Returns 1 if it did anything.</summary>
    static int AlignChain(Animator anim, HumanBodyBones from, HumanBodyBones to, Vector3 targetDir)
    {
        var a = anim.GetBoneTransform(from);
        var b = anim.GetBoneTransform(to);
        if (a == null || b == null) return 0;

        Vector3 current = b.position - a.position;
        if (current.sqrMagnitude < 1e-8f) return 0;

        // Rotate the parent so the child lands on the target axis. Applied in world space, which
        // keeps it correct regardless of how the rig's local axes happen to be oriented â€” that
        // variation between packs is exactly why hand-editing this is so fiddly.
        Quaternion delta = Quaternion.FromToRotation(current.normalized, targetDir.normalized);
        a.rotation = delta * a.rotation;
        return 1;
    }
}
