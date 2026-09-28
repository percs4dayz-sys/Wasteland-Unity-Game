using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Swaps the player's visual model for another one â€” e.g. a Synty Sidekick character built with
/// Synty's own Modular Character window â€” without disturbing the Player root.
///
/// The Player root carries everything that matters (PlayerEntity, Player3DController,
/// CharacterController, EquipmentVisuals, PlayerRespawnâ€¦). Only the VISUAL child gets replaced, so
/// nothing needs rewiring afterwards.
///
/// Why this is safe for held items: EquipmentVisuals resolves hand bones from the HUMANOID avatar
/// (with fallback anchors and a retry), so weapons and tools re-attach on their own â€” provided the
/// new model imports as Humanoid. This tool refuses to proceed quietly if it isn't, because a
/// Generic rig silently breaks both the 116-clip animation library and hand attachment.
///
/// Usage: select the character prefab in the Project window â†’
///        Wasteland â–¸ Player â–¸ Swap Player Model To Selected Prefab
/// </summary>
public static class PlayerModelSwapper
{
    const string ControllerResource = "PlayerAnimator";   // Resources/PlayerAnimator.controller

    /// <summary>Path of the rig whose avatar we'd rather use â€” a real model file, so its T-pose is
    /// editable in Rig â–¸ Configure and survives. Null if it isn't in the project.</summary>
    const string PreferredAvatarModel =
        "Assets/Synty/SidekickCharacters/Resources/Meshes/SK_BaseModel.fbx";

    static Avatar PreferredAvatar()
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(PreferredAvatarModel))
            if (a is Avatar av && av.isHuman && av.isValid) return av;
        return null;
    }

    // QUARANTINED 2026-08-03 — would wreck the fixed player/rig; menu disabled. See memory player-character-pipeline.
    // [MenuItem("Wasteland/Player/Swap Player Model To Selected Prefab", false, 200)]
    public static void SwapToSelected()
    {
        var prefab = Selection.activeObject as GameObject;
        if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
        {
            EditorUtility.DisplayDialog("Swap Player Model",
                "Select the character PREFAB in the Project window first.\n\n" +
                "Synty base characters live under:\n" +
                "Synty/SidekickCharacters/Characters/HumanSpecies/â€¦\n\n" +
                "To build a custom modular character from the part library, use Synty's own " +
                "Modular Character window rather than assembling parts by hand.", "OK");
            return;
        }

        var player = Object.FindAnyObjectByType<PlayerEntity>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("Swap Player Model",
                "No PlayerEntity in the open scene.", "OK");
            return;
        }

        // Humanoid check BEFORE touching anything â€” this is the failure that wastes hours later.
        var srcAnim = prefab.GetComponentInChildren<Animator>();
        var srcAvatar = srcAnim != null ? srcAnim.avatar : null;
        if (srcAvatar == null)
        {
            // The prefab may rely on the model asset's avatar rather than carrying one.
            string path = AssetDatabase.GetAssetPath(prefab);
            srcAvatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        }
        if (srcAvatar != null && !srcAvatar.isHuman &&
            !EditorUtility.DisplayDialog("Swap Player Model",
                $"'{prefab.name}' has a GENERIC avatar, not Humanoid.\n\n" +
                "Your animation library and the hand-bone lookup in EquipmentVisuals both need " +
                "Humanoid. The character will likely stand frozen with weapons floating.\n\n" +
                "Set the model's Rig â–¸ Animation Type to Humanoid first.", "Swap anyway", "Cancel"))
            return;

        var root = player.transform;

        // Remove the existing visual: children that carry a renderer or an animator. Anything else
        // (spawn anchors, attach points, UI) is left alone.
        var doomed = new System.Collections.Generic.List<GameObject>();
        foreach (Transform child in root)
        {
            bool isVisual = child.GetComponentInChildren<SkinnedMeshRenderer>(true) != null
                         || child.GetComponentInChildren<MeshRenderer>(true) != null
                         || child.GetComponent<Animator>() != null;
            if (isVisual) doomed.Add(child.gameObject);
        }

        string names = doomed.Count > 0 ? string.Join(", ", doomed.Select(d => d.name)) : "(none)";
        if (!EditorUtility.DisplayDialog("Swap Player Model",
                $"Replace the player's visual with '{prefab.name}'?\n\n" +
                $"Removing: {names}\n" +
                $"Player root '{root.name}' and all its components are kept.", "Swap", "Cancel"))
            return;

        foreach (var d in doomed) Undo.DestroyObjectImmediate(d);

        // Instantiate the new model as a child, keeping the prefab link so Synty updates flow through.
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
        Undo.RegisterCreatedObjectUndo(instance, "Swap player model");
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        // Animator: the project drives this via PlayerAnimator3D, which expects a controller.
        var anim = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();

        // Use the model's OWN avatar. An avatar from a different model â€” even one with identical
        // bone names â€” carries that model's T-pose, and every frame of every clip inherits the
        // difference: bent forearms, feet rotated, while the animation otherwise plays normally.
        // (This tool briefly forced SK_BaseModel's avatar onto every character. Don't do that.)
        if (srcAvatar != null) anim.avatar = srcAvatar;
        if (anim.runtimeAnimatorController == null)
        {
            var ctrl = Resources.Load<RuntimeAnimatorController>(ControllerResource);
            if (ctrl != null) anim.runtimeAnimatorController = ctrl;
            else Debug.LogWarning($"[PlayerModel] Resources/{ControllerResource} not found â€” " +
                                  "assign the player's Animator Controller by hand.");
        }
        anim.applyRootMotion = false;

        // Skinned meshes on a character that can be off-screen while still driving gameplay.
        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.updateWhenOffscreen = true;

        // Stray cameras/lights/listeners are common in imported character prefabs and will fight
        // the real ones (this project already had a scene with no camera at all).
        foreach (var cam in instance.GetComponentsInChildren<Camera>(true))
            Undo.DestroyObjectImmediate(cam.gameObject);
        foreach (var al in instance.GetComponentsInChildren<AudioListener>(true))
            Undo.DestroyObjectImmediate(al);

        Selection.activeGameObject = instance;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        bool human = anim.avatar != null && anim.avatar.isHuman;
        Debug.Log($"[PlayerModel] Swapped player visual to '{prefab.name}'. " +
                  $"Avatar: {(anim.avatar != null ? anim.avatar.name : "none")} " +
                  $"({(human ? "Humanoid" : "NOT Humanoid")}). Controller: " +
                  $"{(anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "none")}.");

        EditorUtility.DisplayDialog("Swap Player Model",
            $"Player visual is now '{prefab.name}'.\n\n" +
            $"Avatar: {(human ? "Humanoid âœ“" : "NOT Humanoid âœ—")}\n" +
            $"Controller: {(anim.runtimeAnimatorController != null ? "assigned âœ“" : "MISSING âœ—")}\n\n" +
            "Press Play to check animation and that weapons land in the hands.\n" +
            "SAVE THE SCENE (Ctrl+S).", "OK");
    }
}
