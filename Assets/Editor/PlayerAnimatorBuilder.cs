using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// One-click setup for the Mixamo player + animations in Assets/Resources/:
///   1. player.fbx → Humanoid, builds the shared Avatar (Create From This Model).
///   2. Every other FBX → Humanoid, Copy From Other Avatar (player.fbx's avatar),
///      with Loop Time on the locomotion/idle/gather clips. This is the workflow that
///      avoids "Required human bone 'Hips' not found" on animation-only files.
///   3. Builds Assets/Resources/PlayerAnimator.controller from those clips.
///
/// Run this whenever you add or re-import animations. Menu:
///   Wasteland ▸ Set Up Player Animations (Mixamo)
/// </summary>
public static class PlayerAnimatorBuilder
{
    const string ResDir       = "Assets/Resources";
    // The curated player animation set. Searched BEFORE Resources so it wins over leftover packs
    // (e.g. the old "Sword and Shield Pack" under Resources that was hijacking the walk).
    const string AnimDir      = "Assets/Art/newshit/newplayershit/playeranimations";
    const string PlayerPath   = "Assets/Resources/player.fbx";
    const string ControllerPath = "Assets/Resources/PlayerAnimator.controller";
    // Level-up celebration clip. Not a mixamorig file, so it can't Copy-From-Other like the rest —
    // it gets its OWN Humanoid avatar (Create-From-This-Model) and retargets onto the player at runtime.
    const string CelebratePath = "Assets/Resources/Fist Pump.fbx";

    // The CURRENT player model (Reallusion/ActorCore "Realistic man"). The scene player Visual uses this
    // model's Humanoid avatar ("Realistic manAvatar"), and Mixamo clips retarget cleanly onto it (that's
    // why the old soldier anims played fine).
    const string PlayerModelPath = "Assets/Art/newshit/newplayershit/Realistic man.fbx";

    // A Mixamo-rigged source avatar. The big locomotion/action FBXs in AnimDir (Idle, Walking,
    // woodcutting, Handgun Aim, …) are Mixamo files, and the PROVEN-good retarget is Copy-From-Other a
    // mixamorig avatar — exactly how the old working anims were set up. Self-building their own avatars
    // is what produced the scarecrow T-pose / splayed feet. This file supplies that mixamorig avatar.
    const string MixamoAvatarPath = "Assets/Resources/otherneededassets/Sword and Shield Pack/player.fbx";

    // Slow (re-imports ~40 FBXs) — run once, or after adding new animations.
    // DISABLED 2026-08-03 — this rebuild picks the CORRUPT clips first by name preference
    // (Clip("Idle","standing idle","unarmed idle") → the 9 MB "Idle" always wins) and targets a model
    // no longer used. Running it silently re-breaks the fixed foot. Re-enable ONLY after the clip
    // selection is rewritten to prefer the verified-symmetric "unarmed" clips. See memory:
    // player-character-pipeline.
    // [MenuItem("Wasteland/Player/1. Set Up Rigs + Animator (Mixamo)")]
    public static void SetUpRigs()
    {
        try
        {
            // ── 1. character model → avatar source ───────────────────────
            var playerImp = AssetImporter.GetAtPath(PlayerPath) as ModelImporter;
            if (playerImp == null)
            {
                EditorUtility.DisplayDialog("Player Animations",
                    $"Couldn't find character model at:\n{PlayerPath}\n\nMake sure the file name is exact.", "OK");
                return;
            }
            playerImp.animationType = ModelImporterAnimationType.Human;
            playerImp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
            playerImp.SaveAndReimport();

            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(PlayerPath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                EditorUtility.DisplayDialog("Player Animations",
                    "player.fbx did not produce a valid Humanoid avatar.\n\n" +
                    "Open its Rig tab, set Animation Type = Humanoid, click Configure and make sure the body maps, " +
                    "then run this again.", "OK");
                return;
            }

            // ── 2. point every animation at that avatar + set loops ──────
            var animPaths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ResDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(".fbx") && p != PlayerPath) animPaths.Add(p);
            }

            for (int i = 0; i < animPaths.Count; i++)
            {
                string p = animPaths[i];
                EditorUtility.DisplayProgressBar("Player Animations",
                    $"Retargeting {Path.GetFileName(p)}", (float)i / animPaths.Count);

                var ai = AssetImporter.GetAtPath(p) as ModelImporter;
                if (ai == null) continue;

                // The celebration clip is handled separately (own Humanoid avatar, below) — it isn't a
                // mixamorig file, so the Copy-From-Other path here would skip or mis-rig it.
                if (p == CelebratePath) continue;

                // Skip any FBX that isn't a Mixamo player clip. The player retarget uses Copy-From-Other
                // against player.fbx's mixamorig avatar, so a file with a different skeleton (e.g. the
                // quadruped wolf companion, which has its own Generic WolfAnimator setup) would fail with
                // "Copied Avatar Rig Configuration mis-match … Transform 'mixamorig:Hips' not found" — and
                // forcing it Humanoid would wreck it. Only mixamorig-rigged files belong here.
                if (!HasTransformNamed(p, "mixamorig:Hips"))
                {
                    Debug.Log($"[PlayerAnimator] Skipping '{Path.GetFileName(p)}' — not a mixamorig player clip " +
                              "(left untouched; e.g. the wolf companion has its own setup).");
                    continue;
                }

                ai.animationType = ModelImporterAnimationType.Human;
                ai.avatarSetup   = ModelImporterAvatarSetup.CopyFromOther;
                ai.sourceAvatar  = avatar;

                bool loop = ShouldLoop(p);
                var clips = ai.defaultClipAnimations;
                for (int c = 0; c < clips.Length; c++)
                {
                    clips[c].loopTime = loop;
                    clips[c].keepOriginalOrientation = true;
                    clips[c].keepOriginalPositionXZ = true;
                    clips[c].lockRootHeightY = true;
                }
                if (clips.Length > 0) ai.clipAnimations = clips;
                ai.SaveAndReimport();
            }
            SetupCelebrationClip();
        }
        finally { EditorUtility.ClearProgressBar(); }

        BuildController();
    }

    /// <summary>Import the level-up "Fist Pump" FBX as a Humanoid clip with its OWN avatar
    /// (Create-From-This-Model). Humanoid clips retarget onto the player's Humanoid avatar at runtime,
    /// so this works even though the file doesn't share the player's mixamorig skeleton.</summary>
    static void SetupCelebrationClip()
    {
        var imp = AssetImporter.GetAtPath(CelebratePath) as ModelImporter;
        if (imp == null)
        {
            Debug.LogWarning($"[PlayerAnimator] Celebration FBX not found at {CelebratePath} — " +
                             "level-up will fall back to the idle pose.");
            return;
        }

        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;

        var clips = imp.defaultClipAnimations;
        for (int c = 0; c < clips.Length; c++)
        {
            clips[c].loopTime = false;                  // a one-shot cheer, not a loop
            clips[c].keepOriginalOrientation = true;
            clips[c].keepOriginalPositionXZ  = true;
            clips[c].lockRootHeightY = true;
        }
        if (clips.Length > 0) imp.clipAnimations = clips;
        imp.SaveAndReimport();

        var av = AssetDatabase.LoadAssetAtPath<Avatar>(CelebratePath);
        if (av == null || !av.isValid || !av.isHuman)
            Debug.LogWarning("[PlayerAnimator] 'Fist Pump.fbx' didn't produce a valid Humanoid avatar. " +
                "Open its Rig tab, set Animation Type = Humanoid, click Configure to auto-map the bones, " +
                "then run this again. Level-up falls back to the idle pose until then.");
    }

    // Fast (rigs already done) — iterate on which clip maps to what, interrupt timing, etc.
    // DISABLED 2026-08-03 — rebuilds PlayerAnimator.controller from the broken-clip selection. Regresses the fix.
    // [MenuItem("Wasteland/Player/2. Rebuild Animator (fast)")]
    public static void RebuildAnimator() => BuildController();

    /// <summary>Path of the rig the player ACTUALLY uses now: the Sidekick base model. ArmourVisuals
    /// builds the character from Sidekick parts at runtime and the Animator gets SK_BaseModelAvatar,
    /// so this — not player.fbx or Realistic man.fbx — is what the clips must retarget through.</summary>
    const string SidekickBasePath = "Assets/Synty/SidekickCharacters/Resources/Meshes/SK_BaseModel.fbx";

    /// <summary>Re-rig every animation to the SIDEKICK avatar, then rebuild the controller.
    ///
    /// The older menu items point at the Mixamo / Realistic-Man pipeline from when the player was a
    /// single imported FBX. The player is now a Sidekick modular character, and a clip whose avatar was
    /// copied from a dead model either fails to import as Humanoid at all ("didn't yield a Humanoid
    /// clip") or imports with a reference pose that doesn't match, which is what threw the feet 90° out.
    ///
    /// Copying SK_BaseModel's avatar works because both skeletons use the same UE-style bone names
    /// (root / pelvis / thigh_l / upperarm_l / ball_l …), and it makes the retarget a pass-through:
    /// each clip is interpreted against the exact rig it will play on.</summary>
    // DISABLED 2026-08-03 — re-rigs animation FBXs and rebuilds the controller. Regresses the fix.
    // [MenuItem("Wasteland/Player/4. Rig Animations to Sidekick + Rebuild")]
    public static void RigAnimationsToSidekick()
    {
        var baseImp = AssetImporter.GetAtPath(SidekickBasePath) as ModelImporter;
        if (baseImp == null)
        {
            EditorUtility.DisplayDialog("Player Animations",
                $"No Sidekick base model at:\n{SidekickBasePath}", "OK");
            return;
        }

        if (baseImp.animationType != ModelImporterAnimationType.Human ||
            baseImp.avatarSetup   != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            baseImp.animationType = ModelImporterAnimationType.Human;
            baseImp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
            baseImp.SaveAndReimport();
        }

        var skAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(SidekickBasePath);
        if (skAvatar == null || !skAvatar.isValid || !skAvatar.isHuman)
        {
            EditorUtility.DisplayDialog("Player Animations",
                $"SK_BaseModel didn't produce a valid Humanoid avatar.\n\n{SidekickBasePath}", "OK");
            return;
        }

        int done = 0, failed = 0;
        var bad = new System.Text.StringBuilder();

        try
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { AnimDir });
            for (int i = 0; i < guids.Length; i++)
            {
                string p = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!p.EndsWith(".fbx")) continue;

                EditorUtility.DisplayProgressBar("Rig Animations to Sidekick",
                    Path.GetFileName(p), (float) i / guids.Length);

                var ai = AssetImporter.GetAtPath(p) as ModelImporter;
                if (ai == null) continue;

                // EACH CLIP GETS ITS OWN AVATAR. Copy-From-Other against SK_BaseModel was the obvious
                // idea — it would make the retarget a pass-through and fix the foot rotation — but it
                // cannot work here: the copy demands the target contain every bone the source maps, at a
                // matching hierarchy path, and SK_BaseModel maps eye bones (eye_l/eye_r) these clips
                // don't line up with. Every one of the 202 failed with "Copied Avatar Rig Configuration
                // mis-match" and produced NO AnimationClip at all. A clip with an imperfect reference
                // pose beats no clip, so this is the setting that stays.
                ai.animationType = ModelImporterAnimationType.Human;
                ai.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
                ai.sourceAvatar  = null;

                bool loop = ShouldLoop(p);
                var clips = ai.defaultClipAnimations;
                for (int c = 0; c < clips.Length; c++)
                {
                    clips[c].loopTime = loop;
                    clips[c].keepOriginalOrientation = true;
                    clips[c].keepOriginalPositionXZ  = true;
                    clips[c].lockRootHeightY         = true;
                }
                if (clips.Length > 0) ai.clipAnimations = clips;

                ai.SaveAndReimport();

                // Always verify the import actually produced a clip. A rig setting that silently yields
                // nothing is exactly how 202 animations went missing without a single failed build.
                if (LoadClipAt(p) == null) { failed++; bad.AppendLine("  " + Path.GetFileName(p)); }
                else done++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[PlayerAnimator] Rigged {done} clip(s) Humanoid with their own avatar" +
                  (failed > 0 ? $", {failed} produced NO clip:\n{bad}" : "."));

        // Give the pipeline a chance to finish before the controller goes looking for the clips —
        // building in the same pass as 200 reimports is why this reported "no Humanoid clip" last time.
        AssetDatabase.Refresh();

        BuildController();
    }

    /// <summary>READ-ONLY diagnostic — touches nothing. Logs how the player model's Humanoid avatar
    /// mapped its bones (which skeleton bone is "Hips", the upper legs, spine, etc.) and the REST-POSE
    /// height of each, so we can see if Hips is sitting up at mid-back (the "legs split halfway up the
    /// back" walk). If Hips' height is near the chest instead of the crotch, the avatar map is the bug.</summary>
    [MenuItem("Wasteland/Player/Debug: Log Player Avatar Bone Map")]
    public static void DebugAvatarMap()
    {
        var imp = AssetImporter.GetAtPath(PlayerModelPath) as ModelImporter;
        if (imp == null) { Debug.LogError($"[RigDebug] No model at {PlayerModelPath}"); return; }

        var hd = imp.humanDescription;
        var map = new Dictionary<string, string>();
        foreach (var hb in hd.human) map[hb.humanName] = hb.boneName;

        // Skeleton bone rest positions are LOCAL to the parent; accumulate to world-ish Y by walking
        // parents. Build name→(parent,localPos) then resolve each bone's summed Y.
        var local = new Dictionary<string, Vector3>();
        var parent = new Dictionary<string, string>();
        var order  = new List<string>();
        foreach (var sb in hd.skeleton) { local[sb.name] = sb.position; order.Add(sb.name); }
        // SkeletonBone[] is depth-first parent-before-child, so parent is the nearest prior bone whose
        // transform contains this one — we can't get hierarchy from the array directly, so just report
        // each mapped bone's LOCAL offset magnitude; the NAME is usually the giveaway anyway.

        string[] keys = { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
                          "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg",
                          "LeftFoot", "RightFoot" };
        var sb2 = new System.Text.StringBuilder();
        sb2.AppendLine($"[RigDebug] {PlayerModelPath}  animationType={imp.animationType}");
        var av = AssetDatabase.LoadAssetAtPath<Avatar>(PlayerModelPath);
        sb2.AppendLine($"[RigDebug] avatar valid={(av != null && av.isValid)} human={(av != null && av.isHuman)}");
        foreach (var k in keys)
        {
            string bone = map.TryGetValue(k, out var b) ? b : "(unmapped)";
            string off  = bone != "(unmapped)" && local.TryGetValue(bone, out var lp)
                        ? $"localPos={lp:F3}" : "";
            sb2.AppendLine($"   {k,-14} → {bone}   {off}");
        }
        Debug.Log(sb2.ToString());
    }

    /// <summary>
    /// The fix for the current "Realistic man" CC/ActorCore player: the clips in AnimDir were imported
    /// as GENERIC, while the model's avatar is HUMANOID — so a generic clip can't drive the Humanoid-
    /// bound bones and walk/run collapses into an arms-out pose (Unity logs "Some generic clips animate
    /// transforms already bound by a Humanoid avatar").
    ///
    /// The folder is a MIX of skeletons (big Mixamo full-mesh clips + small ActorCore anim clips), so a
    /// single Copy-From-Other avatar can't rig them all. Instead we import EACH FBX as Humanoid with its
    /// OWN avatar (Create-From-This-Model) — Unity auto-maps each skeleton — so every clip becomes a
    /// Humanoid clip that retargets onto the player's Humanoid avatar at runtime. Then rebuild the
    /// controller. Self-healing for future drops. Slow (re-imports ~190 FBXs) — one-off.
    /// </summary>
    // DISABLED 2026-08-03 — re-rigs against Realistic Man (unused model) and rebuilds. Regresses the fix.
    // [MenuItem("Wasteland/Player/3. Rig New Animations (Realistic Man) + Rebuild")]
    public static void RigNewAnimations()
    {
        // Get a Mixamo-rigged avatar to Copy-From-Other onto. This is the retarget that actually works
        // for these clips (proven by the old soldier anims). Self-building each clip's own avatar is what
        // produced the scarecrow pose / splayed feet, so we only do that for non-Mixamo files.
        var mixImp = AssetImporter.GetAtPath(MixamoAvatarPath) as ModelImporter;
        if (mixImp != null &&
            (mixImp.animationType != ModelImporterAnimationType.Human ||
             mixImp.avatarSetup   != ModelImporterAvatarSetup.CreateFromThisModel))
        {
            mixImp.animationType = ModelImporterAnimationType.Human;
            mixImp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
            mixImp.SaveAndReimport();
        }
        var mixamoAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(MixamoAvatarPath);
        if (mixamoAvatar == null || !mixamoAvatar.isValid || !mixamoAvatar.isHuman)
        {
            EditorUtility.DisplayDialog("Player Animations",
                $"Couldn't get a valid Mixamo avatar from:\n{MixamoAvatarPath}\n\nFix the path constant " +
                "(it should point at any mixamorig-rigged FBX) and run again.", "OK");
            return;
        }

        try
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { AnimDir });
            int n = 0, mixamo = 0, ownAvatar = 0, failed = 0;
            var bad = new System.Text.StringBuilder();
            foreach (var guid in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(".fbx") || p == PlayerModelPath) continue;

                EditorUtility.DisplayProgressBar("Rig New Animations",
                    $"Rigging {Path.GetFileName(p)}", (float)n++ / guids.Length);

                var ai = AssetImporter.GetAtPath(p) as ModelImporter;
                if (ai == null) continue;

                // Mixamo clips → Copy-From-Other the mixamorig avatar (the proven-clean retarget).
                // Everything else (ActorCore body clips) → its own avatar.
                bool isMixamo = HasTransformNamed(p, "mixamorig:Hips");
                ai.animationType = ModelImporterAnimationType.Human;
                if (isMixamo)
                {
                    ai.avatarSetup  = ModelImporterAvatarSetup.CopyFromOther;
                    ai.sourceAvatar = mixamoAvatar;
                }
                else
                {
                    ai.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                }

                bool loop = ShouldLoop(p);
                var clips = ai.defaultClipAnimations;
                for (int c = 0; c < clips.Length; c++)
                {
                    clips[c].loopTime = loop;
                    clips[c].keepOriginalOrientation = true;
                    clips[c].keepOriginalPositionXZ  = true;
                    clips[c].lockRootHeightY = true;
                }
                if (clips.Length > 0) ai.clipAnimations = clips;
                ai.SaveAndReimport();

                if (LoadClipAt(p) == null) { failed++; bad.AppendLine("   • " + Path.GetFileName(p)); }
                else if (isMixamo) mixamo++;
                else ownAvatar++;
            }
            Debug.Log($"[PlayerAnimator] Rigged AnimDir — {mixamo} Mixamo (Copy-From-Other mixamorig), " +
                      $"{ownAvatar} non-Mixamo (own avatar). {failed} produced no clip:\n{bad}");
        }
        finally { EditorUtility.ClearProgressBar(); }

        // Rebuild the controller from the now-Humanoid clips.
        BuildController();
    }

    // ── animator controller ──────────────────────────────────────────────
    static void BuildController()
    {
        // Clip names map to files in AnimDir (the curated set). First match wins; extras are fallbacks.
        // Swap the first entry to re-pick a clip (e.g. want a sword-stance idle? put "sword and shield
        // idle" first). Forward locomotion uses plain Walking / standing run forward — NOT the angled
        // "Swagger Walk"/strafe clips that read as a diagonal sword-and-shield walk.
        // Locomotion uses the MIXAMO clips (Idle/Walking), retargeted via the mixamorig avatar — the same
        // proven-clean path the old soldier anims used. (There's no Mixamo "run" clip, so run reuses
        // Walking; at run speed it reads as a brisk jog.) ActorCore "standing/unarmed" are fallbacks only.
        var idle  = Clip("standing idle", "unarmed idle", "Idle");
        var walk  = Clip("standing walk forward", "unarmed walk forward", "Walking");
        var run   = Clip("standing run forward", "unarmed run forward", "Walking");
        var aim   = Clip("Handgun Aim", "pistol idle", "rifle aiming idle");
        var shoot = Clip("ShootingHandgun", "firing rifle");
        var melee = Clip("sword and shield slash", "standing melee attack downward", "great sword slash");
        var wood  = Clip("woodcutting");
        var fish  = Clip("Fishing Cast", "Fishing Idle");
        var mine  = Clip("Mining", "woodcutting");
        var hit   = Clip("standing react large gut", "sword and shield impact", "great sword impact");
        var die   = Clip("Falling Back Death", "sword and shield death", "walking to dying");
        var cheer = Clip("Fist Pump");   // played on a skill level-up (Resources/Fist Pump.fbx)
        // Slide/dodge (Space). RollForward (copied from the Blink starter pack) is the current pick;
        // drop a Mixamo "Running Slide.fbx" into AnimDir and rebuild for a lower CoD-style slide.
        var dodge = Clip("Running Slide", "running slide", "RollForward", "sprinting forward roll", "great sword slide attack");
        // Reload (R / auto on empty mag). Drop a Mixamo "Reloading.fbx" into AnimDir and rebuild;
        // until then the handgun-draw reads as working the weapon.
        var reload = Clip("Reloading", "reloading", "reload", "Drawing HandGun");

        if (idle == null)
        {
            EditorUtility.DisplayDialog("Player Animations",
                "Rigs are set, but Idle.fbx didn't yield a Humanoid clip. Check the Console for import errors.", "OK");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Speed",      AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Aiming",     AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Gathering",  AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("GatherType", AnimatorControllerParameterType.Int);
        ctrl.AddParameter("AttackMelee", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Shoot",       AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Hit",         AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Die",         AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Celebrate",   AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Dodge",       AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Reload",      AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;

        var locoState = ctrl.CreateBlendTreeInController("Locomotion", out var bt, 0);
        bt.blendType = BlendTreeType.Simple1D;
        bt.blendParameter = "Speed";
        bt.useAutomaticThresholds = false;
        bt.AddChild(idle, 0f);
        if (walk != null) bt.AddChild(walk, 2.5f);
        if (run  != null) bt.AddChild(run,  5.5f);

        // Set the tree's RANGE to match the children. A new BlendTree defaults to 0..1, and with
        // automatic thresholds off Unity never widens it — so walk (2.5) and run (5.5) sat outside the
        // tree's own range, the blend produced no weight for any child, and the state played nothing at
        // all. The state machine kept running; the character just never moved.
        bt.minThreshold = 0f;
        bt.maxThreshold = run != null ? 5.5f : walk != null ? 2.5f : 1f;

        sm.defaultState = locoState;

        var aimState = sm.AddState("Aim");
        aimState.motion = aim != null ? aim : idle;
        // Stand in the aim pose only when armed AND basically still — moving plays locomotion,
        // so you run to the target instead of floating there frozen in an aim.
        var toAim = locoState.AddTransition(aimState);
        Bool(toAim, "Aiming", true);
        toAim.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        Snap(toAim);
        Cond(aimState.AddTransition(locoState), "Aiming", false);    // lowered the weapon
        var aimMoving = aimState.AddTransition(locoState);           // started moving
        aimMoving.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        Snap(aimMoving);

        // GatherType: 0 = wood (Woodcutting), 1 = fishing (Fishing), 2 = mining (Scrapping).
        var woodState = sm.AddState("GatherWood"); woodState.motion = wood != null ? wood : idle;
        var fishState = sm.AddState("GatherFish"); fishState.motion = fish != null ? fish : idle;
        var mineState = sm.AddState("GatherMine"); mineState.motion = mine != null ? mine : (wood != null ? wood : idle);
        var toWood = locoState.AddTransition(woodState);
        Bool(toWood, "Gathering", true); Int(toWood, "GatherType", 0); Snap(toWood);
        var toFish = locoState.AddTransition(fishState);
        Bool(toFish, "Gathering", true); Int(toFish, "GatherType", 1); Snap(toFish);
        var toMine = locoState.AddTransition(mineState);
        Bool(toMine, "Gathering", true); Int(toMine, "GatherType", 2); Snap(toMine);
        Cond(woodState.AddTransition(locoState), "Gathering", false);
        Cond(fishState.AddTransition(locoState), "Gathering", false);
        Cond(mineState.AddTransition(locoState), "Gathering", false);

        OneShot(sm, locoState, "Melee", melee ?? idle, "AttackMelee", moveCancels: true);
        OneShot(sm, locoState, "Shoot", shoot ?? idle, "Shoot", moveCancels: true);
        OneShot(sm, locoState, "Hit",   hit   ?? idle, "Hit");
        // Reload: NOT move-cancelled — you can jog while working the weapon, CoD-style, and the
        // magazine timer (WeaponMagazine) runs regardless of what the animation does.
        OneShot(sm, locoState, "Reload", reload ?? idle, "Reload");
        // Level-up celebration. Moving cancels it, and the next swing/shot (its own AnyState trigger)
        // overrides it — so it never locks you up mid-fight, just a quick fist pump when you ding.
        OneShot(sm, locoState, "Celebrate", cheer ?? idle, "Celebrate", moveCancels: true);

        // Dodge/slide: exits early (the movement burst is only ~0.35 s, so the clip cuts at ~45%
        // rather than playing out a full slide-and-recover). Speed-cancel is OFF — the dash itself
        // pushes Speed high, which would abort the clip on frame one.
        var dodgeState = sm.AddState("Dodge");
        dodgeState.motion = dodge != null ? dodge : idle;
        var anyDodge = sm.AddAnyStateTransition(dodgeState);
        anyDodge.AddCondition(AnimatorConditionMode.If, 0, "Dodge");
        anyDodge.canTransitionToSelf = false; anyDodge.hasExitTime = false; anyDodge.duration = 0.05f;
        var dodgeBack = dodgeState.AddTransition(locoState);
        dodgeBack.hasExitTime = true; dodgeBack.exitTime = 0.45f; dodgeBack.duration = 0.15f;

        var dieState = sm.AddState("Die");
        dieState.motion = die != null ? die : idle;
        var anyDie = sm.AddAnyStateTransition(dieState);
        anyDie.AddCondition(AnimatorConditionMode.If, 0, "Die");
        anyDie.canTransitionToSelf = false; anyDie.hasExitTime = false; anyDie.duration = 0.1f;

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Player Animations",
            "Rigs retargeted and PlayerAnimator.controller built.\n\n" +
            "Now run Wasteland ▸ Build 3D Tutorial Island (New Scene) and press Play.", "Nice");
    }

    // ── helpers ──────────────────────────────────────────────────────────
    static readonly string[] LoopKeys =
    { "idle", "walk", "run", "strafe", "swagger", "aim", "woodcut", "fishing", "block", "rifle" };

    /// <summary>True if the imported FBX's transform hierarchy contains a bone with this exact name.
    /// Used to tell whether a clip shares the player's mixamorig skeleton (Copy-From-Other) or needs
    /// its own avatar (Create-From-This-Model). Bone transforms are present regardless of rig type.</summary>
    static bool HasTransformNamed(string fbxPath, string boneName)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (go == null) return false;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return true;
        return false;
    }

    // One-shots that must never loop, even when their name also matches a loop key ("RUNning
    // slide", "WALKing to dying") — a looping slide/death replays forever.
    static readonly string[] NoLoopKeys = { "slide", "roll", "dive", "death", "dying" };

    static bool ShouldLoop(string assetPath)
    {
        string n = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
        foreach (var k in NoLoopKeys) if (n.Contains(k)) return false;
        foreach (var k in LoopKeys) if (n.Contains(k)) return true;
        return false;
    }

    static void OneShot(AnimatorStateMachine sm, AnimatorState loco, string name, Motion clip, string trigger,
                        bool moveCancels = false)
    {
        var st = sm.AddState(name);
        st.motion = clip;
        var any = sm.AddAnyStateTransition(st);
        any.AddCondition(AnimatorConditionMode.If, 0, trigger);
        any.canTransitionToSelf = false; any.hasExitTime = false; any.duration = 0.05f;
        // Moving (clicking away / repositioning) instantly aborts the swing back to locomotion.
        if (moveCancels)
        {
            var move = st.AddTransition(loco);
            move.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            move.hasExitTime = false; move.duration = 0.12f;
        }
        var back = st.AddTransition(loco);
        back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.12f;
    }

    static void Cond(AnimatorStateTransition t, string boolParam, bool isTrue) { Bool(t, boolParam, isTrue); Snap(t); }
    static void Bool(AnimatorStateTransition t, string p, bool v) =>
        t.AddCondition(v ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, p);
    static void Int(AnimatorStateTransition t, string p, int v) =>
        t.AddCondition(AnimatorConditionMode.Equals, v, p);
    static void Snap(AnimatorStateTransition t) { t.hasExitTime = false; t.duration = 0.1f; }

    static AnimationClip Clip(params string[] names)
    {
        foreach (var name in names)
        {
            // Curated AnimDir first, then Resources top-level, then a recursive name search.
            var cl = LoadClipAt($"{AnimDir}/{name}.fbx")
                  ?? LoadClipAt($"{ResDir}/{name}.fbx")
                  ?? FindClipByName(name);
            if (cl != null)
            {
                // Tell us when the preferred clip wasn't usable and we fell back — this is how the
                // handgun ended up with rifle animations (Pistol Aim/Shooting weren't resolving).
                if (name != names[0])
                    Debug.LogWarning($"[PlayerAnimator] '{names[0]}' didn't resolve — using fallback '{name}'.");
                else
                    Debug.Log($"[PlayerAnimator] resolved '{name}'.");
                return cl;
            }
        }
        Debug.LogWarning($"[PlayerAnimator] No clip found for any of: {string.Join(", ", names)}");
        return null;
    }

    static AnimationClip LoadClipAt(string path)
    {
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            if (obj is AnimationClip cl && !cl.name.StartsWith("__preview__")) return cl;
        return null;
    }

    /// <summary>Find an FBX named exactly <paramref name="name"/> anywhere under Resources (incl.
    /// subfolders), and return its Humanoid clip. Lets dropped packs live in their own folders.</summary>
    static AnimationClip FindClipByName(string name)
    {
        foreach (var dir in new[] { AnimDir, ResDir })   // curated set wins over Resources packs
            foreach (var guid in AssetDatabase.FindAssets($"\"{name}\" t:Model", new[] { dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) != name) continue;   // exact filename only
                var cl = LoadClipAt(p);
                if (cl != null) return cl;
            }
        return null;
    }
}
