using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Makes a selected NPC idle-loop from an animation clip baked into its model (e.g. a glTF character
/// like Roxy that ships with animations but no Animator Controller, so it just stands frozen).
/// Select the NPC → Wasteland ▸ NPC ▸ Idle-Animate Selected. Builds a tiny controller from the model's
/// "idle" clip (or the first clip) and assigns it + the avatar.
/// </summary>
public static class NpcIdleAnimator
{
    [MenuItem("Wasteland/NPC ▸ Idle-Animate Selected")]
    static void IdleAnimate()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("Idle-Animate", "Select the NPC in the Hierarchy first.", "OK");
            return;
        }

        var anim = go.GetComponentInChildren<Animator>() ?? go.AddComponent<Animator>();

        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
        string path = (smr && smr.sharedMesh) ? AssetDatabase.GetAssetPath(smr.sharedMesh) : null;
        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("Idle-Animate", "Couldn't find this NPC's model asset (no skinned mesh).", "OK");
            return;
        }

        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        var clips = assets.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
        if (clips.Count == 0)
        {
            EditorUtility.DisplayDialog("Idle-Animate", $"No animation clips found inside {Path.GetFileName(path)}.", "OK");
            return;
        }
        var clip = clips.FirstOrDefault(c => c.name.ToLower().Contains("idle")) ?? clips[0];

        Directory.CreateDirectory("Assets/Animations");
        string ctrlPath = AssetDatabase.GenerateUniqueAssetPath($"Assets/Animations/{go.name}_Idle.controller");
        var controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(ctrlPath, clip);
        anim.runtimeAnimatorController = controller;

        var avatar = assets.OfType<Avatar>().FirstOrDefault();
        if (avatar != null && anim.avatar == null) anim.avatar = avatar;
        anim.applyRootMotion = false;

        EditorUtility.SetDirty(go);
        Debug.Log($"[IdleAnimate] '{go.name}' idles with '{clip.name}' ({clips.Count} clip(s) in the model). Controller: {ctrlPath}");
        EditorUtility.DisplayDialog("Idle-Animate",
            $"Done — '{go.name}' will loop '{clip.name}'.\n\n" +
            $"If she still stands frozen, open {Path.GetFileName(path)} in the Project → Animation tab → tick " +
            "\"Loop Time\" on that clip and Apply.", "OK");
    }
}
