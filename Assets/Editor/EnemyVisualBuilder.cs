using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Ties the three Kenney "animated characters" packs to enemies as placeholder visuals
/// (until real enemy art shows up). Mirrors WolfCompanionBuilder.
///
/// What it does — idempotent, safe to re-run:
///   • Sets each pack's characterMedium.fbx + idle/run animations to a Generic rig with
///     looping clips, then builds one Resources/EnemyAnimator_&lt;pack&gt;.controller per pack:
///     a "Speed"-driven idle→run blend (same shape as WolfAnimator). Enemy3D drives Speed.
///   • Builds one skin material per pack from a hostile-looking skin (zombie / raider).
///   • Replaces the placeholder capsule mesh on every Enemy3D in the OPEN scene with a
///     Kenney model — keeps the collider + CombatTarget so clicking/combat still works.
///
/// Menu:  Wasteland ▸ Enemies ▸ Tie Kenney Packs to Enemies (open scene)
///
/// World3DBuilder / TutorialIsland3DBuilder also call AttachVisual(), so freshly built
/// worlds get Kenney enemies too.
/// </summary>
public static class EnemyVisualBuilder
{
    public struct Pack
    {
        public string name;            // enemy display name for fresh spawns
        public string modelPath;       // characterMedium.fbx
        public string idlePath;        // idle.fbx
        public string runPath;         // run.fbx
        public string skinPath;        // skin .png assigned as the base map
        public string controllerPath;  // generated EnemyAnimator_<pack>.controller
        public string materialPath;    // generated skin material
    }

    const string PackRoot  = "Assets/kenneynlassets/kenney_animated-characters-";
    const string MatFolder = "Assets/Art/Generated3D";
    const float  TargetHeight = 1.8f;   // metres — shared by the edit-time preview and the runtime normalizer

    /// <summary>One hostile per pack, using the most enemy-looking skin each ships.</summary>
    public static readonly Pack[] Packs =
    {
        Make("Raider", "protagonists", "criminalMaleA"),
        Make("Ghoul",  "retro",        "zombieMaleA"),
        Make("Walker", "survivors",    "zombieA"),
    };

    static Pack Make(string name, string pack, string skin)
    {
        string root = PackRoot + pack;
        return new Pack
        {
            name           = name,
            modelPath      = root + "/Model/characterMedium.fbx",
            idlePath       = root + "/Animations/idle.fbx",
            runPath        = root + "/Animations/run.fbx",
            skinPath       = root + "/Skins/" + skin + ".png",
            controllerPath = "Assets/Resources/EnemyAnimator_" + pack + ".controller",
            materialPath   = MatFolder + "/Enemy_" + pack + ".mat",
        };
    }

    [MenuItem("Wasteland/Archived/Enemies/Tie Kenney Packs to Enemies (open scene)", false, 9000)]
    public static void TieToEnemies()
    {
        EnsureAssets(force: true);   // menu run rebuilds the rigs/controllers from scratch

        var enemies = Object.FindObjectsByType<Enemy3D>();
        if (enemies.Length == 0)
        {
            EditorUtility.DisplayDialog("Tie Kenney Packs to Enemies",
                "Built the Kenney enemy models / controllers / skins, but found no Enemy3D in the " +
                "open scene to apply them to.\n\nOpen your world scene and run this again.", "OK");
            return;
        }

        for (int i = 0; i < enemies.Length; i++)
            AttachVisual(enemies[i].transform, i);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(enemies[0].gameObject.scene);
        EditorUtility.DisplayDialog("Tie Kenney Packs to Enemies",
            $"Applied Kenney models to {enemies.Length} enem{(enemies.Length == 1 ? "y" : "ies")}.\n\n" +
            "Save the scene (Ctrl+S), then press Play — they idle, and run while chasing.", "Nice");
    }

    // ── asset setup (rigs + controllers + skin materials) ─────────────────────

    /// <summary>Build the per-pack rig, controller, and skin material. Idempotent.
    /// Pass force:true to rebuild even when they already exist.</summary>
    public static void EnsureAssets(bool force = false)
    {
        if (!force && AssetsReady()) return;

        foreach (var p in Packs)
        {
            SetGenericLooping(p.modelPath);
            SetGenericLooping(p.idlePath);
            SetGenericLooping(p.runPath);
            BuildController(p);
            BuildMaterial(p);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static bool AssetsReady()
    {
        foreach (var p in Packs)
        {
            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(p.controllerPath) == null) return false;
            if (AssetDatabase.LoadAssetAtPath<Material>(p.materialPath) == null) return false;
            var mi = AssetImporter.GetAtPath(p.modelPath) as ModelImporter;
            if (mi == null || mi.animationType != ModelImporterAnimationType.Generic) return false;
        }
        return true;
    }

    /// <summary>Set an FBX to a Generic rig with looping clips — only reimports when needed.</summary>
    static void SetGenericLooping(string fbxPath)
    {
        var mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (mi == null) return;

        bool dirty = false;
        if (mi.animationType != ModelImporterAnimationType.Generic)
        {
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
            dirty = true;
        }
        mi.importAnimation = true;

        // defaultClipAnimations always reports loopTime=false, so check the explicit
        // overrides (clipAnimations) to stay idempotent across re-runs.
        var existing = mi.clipAnimations;
        bool alreadyLooping = existing.Length > 0;
        foreach (var c in existing) if (!c.loopTime) alreadyLooping = false;
        if (!alreadyLooping)
        {
            var clips = mi.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++) clips[i].loopTime = true;
            if (clips.Length > 0) { mi.clipAnimations = clips; dirty = true; }
        }

        if (dirty) mi.SaveAndReimport();
    }

    static AnimationClip FindClip(string fbxPath, string keyword)
    {
        AnimationClip first = null;
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (!(obj is AnimationClip cl) || cl.name.StartsWith("__preview__")) continue;
            if (first == null) first = cl;
            if (cl.name.ToLowerInvariant().Contains(keyword)) return cl;
        }
        return first;   // fall back to the first real clip in the file
    }

    static void BuildController(Pack p)
    {
        var idle = FindClip(p.idlePath, "idle");
        var run  = FindClip(p.runPath,  "run");
        if (idle == null) idle = run;
        if (idle == null) return;   // nothing to animate with

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(p.controllerPath) != null)
            AssetDatabase.DeleteAsset(p.controllerPath);

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(p.controllerPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);

        var loco = ctrl.CreateBlendTreeInController("Locomotion", out var bt, 0);
        bt.blendType = BlendTreeType.Simple1D;
        bt.blendParameter = "Speed";
        bt.useAutomaticThresholds = false;
        bt.AddChild(idle, 0f);
        if (run != null) bt.AddChild(run, 3.2f);   // ≈ Enemy3D.moveSpeed → full run while chasing
        ctrl.layers[0].stateMachine.defaultState = loco;

        EditorUtility.SetDirty(ctrl);
    }

    static void BuildMaterial(Pack p)
    {
        if (!AssetDatabase.IsValidFolder(MatFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            AssetDatabase.CreateFolder("Assets/Art", "Generated3D");
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(p.materialPath);
        if (mat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, p.materialPath);
        }

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p.skinPath);
        if (tex != null)
        {
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        }
        EditorUtility.SetDirty(mat);
    }

    // ── visual attach (used by the menu + the world builders) ──────────────────

    /// <summary>Replace an enemy's placeholder capsule mesh with a Kenney model. Keeps the
    /// collider so clicking/combat still works. packIndex wraps over the three packs.</summary>
    public static void AttachVisual(Transform enemy, int packIndex)
    {
        var p = Packs[((packIndex % Packs.Length) + Packs.Length) % Packs.Length];

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(p.modelPath);
        if (model == null) return;   // pack missing — leave the capsule as-is

        // drop any previous visual + the capsule's mesh (the collider stays for click targeting)
        for (int i = enemy.childCount - 1; i >= 0; i--)
            if (enemy.GetChild(i).name == "Visual") Object.DestroyImmediate(enemy.GetChild(i).gameObject);
        var mf = enemy.GetComponent<MeshFilter>();   if (mf != null) Object.DestroyImmediate(mf);
        var mr = enemy.GetComponent<MeshRenderer>(); if (mr != null) Object.DestroyImmediate(mr);

        var vis = (GameObject)PrefabUtility.InstantiatePrefab(model);
        vis.name = "Visual";
        vis.transform.SetParent(enemy, false);
        vis.transform.localPosition = Vector3.zero;
        vis.transform.localRotation = Quaternion.identity;

        // Rough edit-time size so the enemy looks right in the Scene view. We measure the mesh's
        // *authored* bounds (not Renderer.bounds — a skinned mesh reports collapsed/near-zero
        // Renderer.bounds in edit mode before the Animator poses it). This is only an
        // approximation and lands each Kenney rig at a slightly different height; the
        // HeightNormalizer3D added below makes them all exactly TargetHeight at runtime.
        var b = MeshBoundsWorld(vis);
        if (b.size.y > 1e-4f)
            vis.transform.localScale *= Mathf.Clamp(TargetHeight / b.size.y, 1e-4f, 1e4f);   // wide clamp: big-import packs (Walker) need <0.02x

        // foot-align: drop the model so its base sits at the collider's bottom (the ground),
        // whether the enemy root is at ground level (fresh build) or capsule-centered (retrofit).
        var col = enemy.GetComponent<Collider>();
        b = MeshBoundsWorld(vis);
        float groundY = col != null ? col.bounds.min.y : enemy.position.y;
        vis.transform.position += Vector3.up * (groundY - b.min.y);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(p.materialPath);
        foreach (var r in vis.GetComponentsInChildren<Renderer>())
        {
            if (mat != null) r.sharedMaterial = mat;
            // retargeted skinned meshes can collapse their bounds and get frustum-culled
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        var anim = vis.GetComponentInChildren<Animator>();
        if (anim == null) anim = vis.AddComponent<Animator>();
        anim.applyRootMotion = false;   // movement is code-driven by Enemy3D
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(p.controllerPath);
        if (ctrl != null) anim.runtimeAnimatorController = ctrl;

        // Size is baked at edit time above (from authored mesh bounds). The runtime normalizer
        // is added but kept DISABLED: these rigs are authored lying down and stood up by a -90°
        // rotation, which makes the runtime height read the wrong axis and inflate the model to
        // ~100x. Leaving it disabled (and present) lets you re-enable/tune it later if desired.
        var norm = vis.GetComponent<HeightNormalizer3D>();
        if (norm == null) norm = vis.AddComponent<HeightNormalizer3D>();
        norm.targetHeight = TargetHeight;
        norm.footAlign = true;
        norm.enabled = false;
    }

    /// <summary>World-space bounds built from each renderer's *authored* mesh bounds
    /// (sharedMesh.bounds × the renderer transform). Reliable in edit mode and pose-independent,
    /// unlike SkinnedMeshRenderer.bounds which can collapse before the Animator runs.</summary>
    static Bounds MeshBoundsWorld(GameObject go)
    {
        bool has = false;
        var total = new Bounds();
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            Add(smr.sharedMesh, smr.transform, ref total, ref has);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            Add(mf.sharedMesh, mf.transform, ref total, ref has);
        return has ? total : new Bounds(go.transform.position, Vector3.zero);
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
}
