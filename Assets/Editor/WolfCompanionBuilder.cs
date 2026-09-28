using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// One-click setup for the rigged wolf companion (Assets/Resources/wolf.fbx):
///   1. Imports it as a Generic rig with looping locomotion clips (idle/walk/run/creep).
///   2. Builds Assets/Resources/WolfAnimator.controller — an Idle→Walk→Run blend on a
///      "Speed" float that CompanionManager drives from the wolf's movement.
///
/// The wolf has no attack clip, so combat just shows it running/idling — still miles better
/// than the static slide. Menu:  Wasteland ▸ Build Wolf Companion
/// </summary>
public static class WolfCompanionBuilder
{
    const string FbxPath        = "Assets/Resources/wolf.fbx";
    const string ControllerPath = "Assets/Resources/WolfAnimator.controller";

    [MenuItem("Wasteland/Build Wolf Companion")]
    public static void Build()
    {
        var mi = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (mi == null)
        {
            EditorUtility.DisplayDialog("Wolf Companion",
                "Couldn't find Assets/Resources/wolf.fbx.", "OK");
            return;
        }

        // Generic rig (quadruped — NOT humanoid), with its own animations, locomotion looping.
        mi.animationType   = ModelImporterAnimationType.Generic;
        mi.avatarSetup     = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true;
        var clips = mi.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
            clips[i].loopTime = true;   // idle/walk/run/creep/sit all read fine as loops
        if (clips.Length > 0) mi.clipAnimations = clips;
        mi.SaveAndReimport();

        // Pull the clips back out by name.
        AnimationClip idle = null, walk = null, run = null;
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
        {
            if (!(obj is AnimationClip cl) || cl.name.StartsWith("__preview__")) continue;
            string n = cl.name.ToLowerInvariant();
            if (idle == null && n.Contains("idle")) idle = cl;
            else if (run == null && n.Contains("run")) run = cl;
            else if (walk == null && n.Contains("walk")) walk = cl;
        }
        if (idle == null) idle = walk ?? run;   // ensure a default
        if (idle == null)
        {
            EditorUtility.DisplayDialog("Wolf Companion",
                "wolf.fbx imported but no animation clips were found. Check its Animation tab.", "OK");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);

        var loco = ctrl.CreateBlendTreeInController("Locomotion", out var bt, 0);
        bt.blendType = BlendTreeType.Simple1D;
        bt.blendParameter = "Speed";
        bt.useAutomaticThresholds = false;
        bt.AddChild(idle, 0f);
        if (walk != null) bt.AddChild(walk, 1.5f);
        if (run  != null) bt.AddChild(run,  4f);
        ctrl.layers[0].stateMachine.defaultState = loco;

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Wolf Companion",
            "wolf.fbx set to Generic + WolfAnimator.controller built.\n\n" +
            "Press Play and F9 to summon the wolf — it should idle/walk/run as it follows.", "Nice");
    }
}
