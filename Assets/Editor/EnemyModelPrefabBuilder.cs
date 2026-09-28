using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Bakes the chosen art-pack creatures (Inca pack, Big_boy, Sci-Fi Robots) into ready-to-spawn
/// enemy prefabs under Assets/Resources/Enemies, so the runtime EnemySpawner uses them by name.
/// Each wraps the source prefab as a "Visual" child (keeping its own materials/animator), under a
/// root with a CapsuleCollider + CombatTarget + Enemy3D + SpawnedEnemy. The visual is scaled to a
/// sensible height from its authored mesh bounds and foot-aligned to the ground. Stats are
/// placeholders — the spawner overrides them per difficulty tier.
///
/// Re-run any time (idempotent — overwrites the prefabs). Menu:
///   Wasteland ▸ Enemies ▸ Build Enemy Prefabs (Inca / BigBoy / Robots)
/// </summary>
public static class EnemyModelPrefabBuilder
{
    struct Src
    {
        public string enemyName, srcPath; public float height;
        // Optional rig FBX to pull an Avatar from, for prefabs whose Animator is missing one
        // (a humanoid/generic controller with no avatar collapses the mesh into a heap on the ground).
        public string avatarFbx;
        // Optional idle/walk/run animation FBXs. When set, the baker builds a clean Speed-driven
        // blend-tree controller from them and replaces whatever controller the source prefab shipped —
        // used to ditch demo-reel controllers that auto-play Idle→Walk→Run→Death and keel over.
        public string idleFbx, walkFbx, runFbx;
        // Optional one-shot attack/death clips → "Attack"/"Die" trigger states that Enemy3D fires
        // (swing telegraph and death linger). Missing clips just skip the state — everything still runs.
        public string attackFbx, deathFbx;
        // Mixamo-sourced sets (the mutant): model + separate clip FBXs share one skeleton, so import
        // them all as GENERIC with their own avatars and the clips drive the mesh by bone path.
        public bool genericRig;
        public Src(string n, string p, float h, string avatar = null,
                   string idle = null, string walk = null, string run = null,
                   string attack = null, string death = null)
        { enemyName = n; srcPath = p; height = h; avatarFbx = avatar;
          idleFbx = idle; walkFbx = walk; runFbx = run; attackFbx = attack; deathFbx = death;
          genericRig = false; }
    }

    // enemy-prefab name (matches EnemySpawner tier .visual) → source art prefab + target height (m)
    static readonly Src[] Sources =
    {
        new("FrogMarauder",   "Assets/AngeloMaN87/Inca Pack (Pixelated Texture)/Prefabs/FrogMarauder.prefab",   1.4f),
        new("BigBoy",         "Assets/Big_boy/Prefab/Big_Boy_Skin1.prefab",                                      2.2f,
            "Assets/Big_boy/Mesh/Big_Boy_Skin1 2.fbx",
            "Assets/Big_boy/Animations/Idle.fbx",
            "Assets/Big_boy/Animations/Walk.fbx",
            "Assets/Big_boy/Animations/Run.fbx",
            "Assets/Big_boy/Animations/Punch_Right_with_Wrench.fbx",
            "Assets/Big_boy/Animations/Death.fbx"),
        new("Paperman",       "Assets/Same Gev Dudios/Sci-Fi Robots Bundle/Prefabs/Paperman.prefab",            1.9f),
        new("Robert",         "Assets/Same Gev Dudios/Sci-Fi Robots Bundle/Prefabs/Robert.prefab",              2.0f),
        new("AndeanColossus", "Assets/AngeloMaN87/Inca Pack (Pixelated Texture)/Prefabs/AndeanColossus.prefab", 3.2f),
        // The Mixamo mutant (Art/newshit/Creature Pack): full real-time set — punch + dying clips
        // drive the Attack/Die telegraph states.
        new("Mutant",         "Assets/Art/newshit/Creature Pack/Man_Mesh.fbx",                                   2.4f, null,
            "Assets/Art/newshit/Creature Pack/mutant breathing idle.fbx",
            "Assets/Art/newshit/Creature Pack/mutant walking.fbx",
            "Assets/Art/newshit/Creature Pack/mutant run.fbx",
            "Assets/Art/newshit/Creature Pack/mutant punch.fbx",
            "Assets/Art/newshit/Creature Pack/mutant dying.fbx") { genericRig = true },
    };

    [MenuItem("Wasteland/Archived/Enemies/Build Enemy Prefabs (Inca / BigBoy / Robots)", false, 9000)]
    public static void Build()
    {
        EnsureFolder("Assets/Resources/Enemies");

        var log = new StringBuilder();
        int made = 0, missing = 0;

        foreach (var s in Sources)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(s.srcPath);
            if (src == null) { log.AppendLine("MISSING: " + s.srcPath); missing++; continue; }

            if (s.genericRig)
            {
                EnsureGenericRig(s.srcPath);
                EnsureGenericRig(s.idleFbx); EnsureGenericRig(s.walkFbx); EnsureGenericRig(s.runFbx);
                EnsureGenericRig(s.attackFbx); EnsureGenericRig(s.deathFbx);
            }

            var root = new GameObject(s.enemyName);
            float h = s.height;
            var cc = root.AddComponent<CapsuleCollider>();
            cc.height = h; cc.center = Vector3.up * (h * 0.5f); cc.radius = Mathf.Clamp(h * 0.18f, 0.3f, 0.9f);
            root.AddComponent<CombatTarget>();
            root.AddComponent<Enemy3D>();
            root.AddComponent<SpawnedEnemy>();

            var vis = (GameObject)PrefabUtility.InstantiatePrefab(src);
            vis.name = "Visual";
            vis.transform.SetParent(root.transform, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;

            // Only the root collider should catch clicks; drop any physics on the art.
            foreach (var c in vis.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var rb in vis.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);

            // A Humanoid Animator Controller with no avatar can't retarget, so the rig collapses
            // and the model ends up "laying on the ground". Assign the rig's avatar if it's missing.
            FixMissingAvatars(vis, s.avatarFbx, log);

            // Replace any demo-reel controller (auto-plays Idle→Walk→Run→Death and stays dead) with a
            // clean Speed-driven idle/walk/run blend tree that Enemy3D already feeds.
            SetupLocomotion(vis, s, log);

            // Swap any built-in/Standard (magenta-under-URP) materials for their URP twins so the
            // robots aren't pink. Inca-pack materials are already URP and pass through untouched.
            FixMaterialsForURP(vis, log);

            // Scale to target height from authored mesh bounds (reliable in edit mode), then foot-align.
            var b = MeshBounds(vis);
            if (b.size.y > 1e-4f) vis.transform.localScale *= Mathf.Clamp(h / b.size.y, 1e-4f, 1e4f);
            b = MeshBounds(vis);
            vis.transform.position += Vector3.up * (root.transform.position.y - b.min.y);

            // Skinned meshes can collapse their bounds and get frustum-culled while posed.
            foreach (var smr in vis.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;

            PrefabUtility.SaveAsPrefabAsset(root, $"Assets/Resources/Enemies/{s.enemyName}.prefab");
            Object.DestroyImmediate(root);
            log.AppendLine("✓ " + s.enemyName + "  (" + h + "m)");
            made++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Build Enemy Prefabs",
            $"Baked {made} enemy prefab(s) into Assets/Resources/Enemies" +
            (missing > 0 ? $"  ({missing} missing — check the paths)" : "") + ".\n\n" + log +
            "\nThe EnemySpawner loads these by name. Press Play on the mainland.", "Nice");
    }

    // ── avatar repair ─────────────────────────────────────────────────────
    // Animators with a Humanoid controller but no avatar collapse the skinned mesh; give them the
    // avatar baked into their rig FBX.
    static void FixMissingAvatars(GameObject vis, string avatarFbx, System.Text.StringBuilder log)
    {
        var animators = vis.GetComponentsInChildren<Animator>(true);
        if (animators.Length == 0) return;

        Avatar avatar = null;
        if (!string.IsNullOrEmpty(avatarFbx))
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(avatarFbx))
                if (o is Avatar av) { avatar = av; break; }
            if (avatar == null) log.AppendLine("   ! no Avatar found in " + avatarFbx);
        }

        foreach (var a in animators)
        {
            if (a.runtimeAnimatorController == null) continue;
            if (a.avatar != null && a.avatar.isValid) continue;     // already fine
            if (avatar == null) continue;
            a.avatar = avatar;
            log.AppendLine("   ↳ assigned avatar '" + avatar.name + "' to Animator");
        }
    }

    // ── locomotion controller (replaces demo-reel controllers) ────────────
    static void SetupLocomotion(GameObject vis, in Src s, StringBuilder log)
    {
        if (string.IsNullOrEmpty(s.idleFbx)) return;

        EnsureLoop(s.idleFbx, true); EnsureLoop(s.walkFbx, true); EnsureLoop(s.runFbx, true);
        EnsureLoop(s.attackFbx, false); EnsureLoop(s.deathFbx, false);   // one-shots must NOT loop

        var idle = LoadClip(s.idleFbx);
        var walk = LoadClip(s.walkFbx);
        var run  = LoadClip(s.runFbx);
        if (idle == null) { log.AppendLine("   ! no idle clip in " + s.idleFbx); return; }

        string path = $"Assets/Resources/Enemies/{s.enemyName}_AI.controller";
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);

        var state = ctrl.CreateBlendTreeInController("Locomotion", out var tree);
        tree.blendType            = BlendTreeType.Simple1D;
        tree.blendParameter       = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0f);
        if (walk != null) tree.AddChild(walk, 2.5f);
        if (run  != null) tree.AddChild(run,  5f);
        ctrl.layers[0].stateMachine.defaultState = state;

        // One-shot Attack state: Enemy3D fires the trigger when a swing telegraphs, and the state
        // flows back to Locomotion when the clip ends.
        var attack = LoadClip(s.attackFbx);
        if (attack != null)
        {
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            var st = ctrl.layers[0].stateMachine.AddState("Attack");
            st.motion = attack;
            var into = ctrl.layers[0].stateMachine.AddAnyStateTransition(st);
            into.AddCondition(AnimatorConditionMode.If, 0, "Attack");
            into.canTransitionToSelf = false;
            into.duration = 0.08f;
            var back = st.AddTransition(state);
            back.hasExitTime = true; back.exitTime = 1f; back.duration = 0.15f;
        }

        // Death state: no exit — the corpse holds the final pose until Enemy3D hides it and
        // Rebind()s the animator on respawn.
        var death = LoadClip(s.deathFbx);
        if (death != null)
        {
            ctrl.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            var st = ctrl.layers[0].stateMachine.AddState("Die");
            st.motion = death;
            var into = ctrl.layers[0].stateMachine.AddAnyStateTransition(st);
            into.AddCondition(AnimatorConditionMode.If, 0, "Die");
            into.canTransitionToSelf = false;
            into.duration = 0.1f;
        }

        int n = 0;
        foreach (var a in vis.GetComponentsInChildren<Animator>(true))
        { a.runtimeAnimatorController = ctrl; n++; }
        log.AppendLine($"   ↳ built controller (locomotion{(attack != null ? " + attack" : "")}" +
                       $"{(death != null ? " + death" : "")}), assigned to {n} animator(s)");
    }

    static AnimationClip LoadClip(string fbxPath)
    {
        if (string.IsNullOrEmpty(fbxPath)) return null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        return null;
    }

    /// <summary>Import an FBX as a Generic rig with its own avatar. A same-skeleton family (Mixamo
    /// model + separate clip files) then animates by matching bone paths — no humanoid mapping to
    /// go wrong.</summary>
    static void EnsureGenericRig(string fbxPath)
    {
        if (string.IsNullOrEmpty(fbxPath)) return;
        if (!(AssetImporter.GetAtPath(fbxPath) is ModelImporter imp)) return;
        if (imp.animationType == ModelImporterAnimationType.Generic &&
            imp.avatarSetup   == ModelImporterAvatarSetup.CreateFromThisModel) return;
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.SaveAndReimport();
    }

    // Single-take FBX clips often import with looping off, so an idle plays once and freezes —
    // force loopTime ON for locomotion clips. One-shots (attack, death) need it OFF, or the
    // enemy punches forever / dies on repeat.
    static void EnsureLoop(string fbxPath, bool loop)
    {
        if (string.IsNullOrEmpty(fbxPath)) return;
        if (!(AssetImporter.GetAtPath(fbxPath) is ModelImporter imp)) return;

        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;

        bool changed = false;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i].loopTime != loop) { clips[i].loopTime = loop; changed = true; }
        if (changed) { imp.clipAnimations = clips; imp.SaveAndReimport(); }
    }

    // ── material repair (built-in/Standard → URP) ─────────────────────────
    static void FixMaterialsForURP(GameObject vis, System.Text.StringBuilder log)
    {
        foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var twin = URPTwin(mats[i]);
                if (twin != null && twin != mats[i]) { mats[i] = twin; changed = true; }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    // Returns a URP-shaded replacement for a material that would render magenta under URP, or null
    // to leave it as-is. Strategy: only touch materials whose shader is the built-in Standard shader,
    // and look for a same-named sibling under a "URP" folder (the robot pack ships SRP + URP twins).
    static Material URPTwin(Material m)
    {
        if (m == null || m.shader == null) return null;
        if (m.shader.name != "Standard" && m.shader.name != "Standard (Specular setup)") return null;

        string path = AssetDatabase.GetAssetPath(m);
        if (string.IsNullOrEmpty(path)) return null;

        // .../Materials/SRP/Name.mat  →  .../Materials/URP/Name.mat
        string twinPath = path.Replace("/SRP/", "/URP/");
        if (twinPath != path)
        {
            var twin = AssetDatabase.LoadAssetAtPath<Material>(twinPath);
            if (twin != null) return twin;
        }
        return null;   // unknown layout — leave it (re-importing the pack as URP is the real fix)
    }

    // ── authored-mesh world bounds (pose-independent, unlike SkinnedMeshRenderer.bounds) ──
    static Bounds MeshBounds(GameObject go)
    {
        bool has = false;
        var total = new Bounds();
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            Add(smr.sharedMesh, smr.transform, ref total, ref has);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            Add(mf.sharedMesh, mf.transform, ref total, ref has);
        return has ? total : new Bounds(go.transform.position, Vector3.one);
    }

    static void Add(Mesh mesh, Transform t, ref Bounds total, ref bool has)
    {
        if (mesh == null) return;
        Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;
        for (int i = 0; i < 8; i++)
        {
            var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                         (i & 2) == 0 ? -e.y : e.y,
                                         (i & 4) == 0 ? -e.z : e.z);
            var w = t.localToWorldMatrix.MultiplyPoint3x4(corner);
            if (!has) { total = new Bounds(w, Vector3.zero); has = true; }
            else total.Encapsulate(w);
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash), leaf = path.Substring(slash + 1);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
