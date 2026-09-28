using UnityEngine;
using UnityEditor;

/// <summary>
/// One-click fix for swapping in a new player model. Select the new model in the Hierarchy, then run
/// Wasteland â–¸ Player â–¸ Set Selected As Player Visual. It:
///   1. Reimports the source FBX as Humanoid (Create From This Model) and reports if the avatar is valid.
///   2. Reparents the model under the Player root as a child named "Visual", reset to local origin/scale.
///   3. Gives it an Animator with the model's avatar + the canonical Resources/PlayerAnimator controller,
///      root-motion off.
/// PlayerAnimator3D then rescales it to ~1.8 m and plants its feet at runtime (fixes waist-deep).
/// </summary>
public static class FixNewPlayer
{
    /// <summary>One click, no selection needed: make player.fbx THE player. Locates the file wherever
    /// it lives, imports it Humanoid with its own avatar, replaces whatever Visual the scene player
    /// currently has, wires the Animator (avatar + Resources/PlayerAnimator, root motion off).
    /// PlayerAnimator3D sizes and foot-plants it at runtime, and EquipmentVisuals finds its mixamorig
    /// hands for held items.</summary>
    // QUARANTINED 2026-08-03 — would wreck the fixed player/rig; menu disabled. See memory player-character-pipeline.
    // [MenuItem("Wasteland/Player/Use player.fbx As Player Visual")]
    static void UsePlayerFbx()
    {
        string PlayerFbx = FindPlayerFbx();
        if (PlayerFbx == null)
        {
            EditorUtility.DisplayDialog("Fix Player",
                "Couldn't find a file named 'player.fbx' anywhere under Assets.\n\n" +
                "Either drop your main-character model in as 'player.fbx', or select it in the " +
                "Hierarchy and use 'Wasteland â–¸ Player â–¸ Set Selected As Player Visual' instead.", "OK");
            return;
        }
        Debug.Log($"[FixPlayer] Using player model: {PlayerFbx}");

        var imp = AssetImporter.GetAtPath(PlayerFbx) as ModelImporter;
        if (imp == null)
        {
            EditorUtility.DisplayDialog("Fix Player", $"'{PlayerFbx}' isn't an importable model.", "OK");
            return;
        }
        // Guard: a static model (no skeleton/skin â€” e.g. an exported .glb sculpture) can never be an
        // animated player. Installing it would give the exact "all messed up" symptoms: T-pose/frozen
        // mesh, no hand bones, held items flung away. Refuse with the rigging path instead.
        var probe = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFbx);
        if (probe != null && probe.GetComponentInChildren<SkinnedMeshRenderer>() == null)
        {
            EditorUtility.DisplayDialog("Fix Player",
                $"'{PlayerFbx}' is a STATIC model â€” it has no skeleton or skin, so it can't animate as " +
                "the player (this is what's 'messed up').\n\n" +
                "Rig it first: upload the mesh to mixamo.com â†’ Auto-Rig â†’ download as FBX (Humanoid), " +
                "drop it in named 'player.fbx', then run this again.\n\n" +
                "Until then the currently-rigged character stays the player.", "OK");
            return;
        }

        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.SaveAndReimport();

        Avatar avatar = null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(PlayerFbx))
            if (a is Avatar av) avatar = av;
        string avatarMsg = avatar == null ? "NO avatar generated"
            : (avatar.isHuman && avatar.isValid) ? "valid Humanoid avatar âœ“"
            : "âš  avatar is NOT a valid Humanoid (Rig tab â†’ Configure)";

        var pc = Object.FindAnyObjectByType<Player3DController>();
        if (pc == null)
        {
            EditorUtility.DisplayDialog("Fix Player",
                "No Player3DController in the open scene â€” open the gameplay scene first.\n\nAvatar: " + avatarMsg, "OK");
            return;
        }

        // Out with the old Visual(s): the named child plus any direct child carrying a renderer/animator.
        for (int i = pc.transform.childCount - 1; i >= 0; i--)
        {
            var child = pc.transform.GetChild(i);
            bool looksLikeModel = child.name == "Visual"
                || child.GetComponentInChildren<SkinnedMeshRenderer>() != null
                || child.GetComponentInChildren<Animator>() != null;
            if (looksLikeModel) Undo.DestroyObjectImmediate(child.gameObject);
        }

        var src = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFbx);
        var vis = (GameObject)PrefabUtility.InstantiatePrefab(src);
        Undo.RegisterCreatedObjectUndo(vis, "Player Visual");
        vis.name = "Visual";
        vis.transform.SetParent(pc.transform, false);
        vis.transform.localPosition = Vector3.zero;
        vis.transform.localRotation = Quaternion.identity;
        vis.transform.localScale    = Vector3.one;

        if (pc.GetComponent<PlayerAnimator3D>() == null) Undo.AddComponent<PlayerAnimator3D>(pc.gameObject);

        var anim = vis.GetComponentInChildren<Animator>();
        if (anim == null) anim = vis.AddComponent<Animator>();
        if (avatar != null) anim.avatar = avatar;
        var rc = Resources.Load<RuntimeAnimatorController>("PlayerAnimator");
        if (rc != null) anim.runtimeAnimatorController = rc;
        anim.applyRootMotion = false;

        // A Mixamo-rigged FBX ships untextured. If a texture-donor model sits alongside it (the
        // ImageToStl/glb export that kept the baked maps), pull its base-color map onto the player so
        // he isn't grey/magenta out of the gate.
        string texMsg = ApplyDonorTextures(vis);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pc.gameObject.scene);
        EditorUtility.DisplayDialog("Fix Player",
            $"player.fbx is now the Player Visual.\nAvatar: {avatarMsg}\nController: {(rc != null ? "assigned" : "MISSING â€” run the animator build")}\nTextures: {texMsg}\n\n" +
            "Save the scene (Ctrl+S), then press Play â€” PlayerAnimator3D sizes and foot-plants him.", "OK");
    }

    // The GLB imports with its baked maps (BaseColor/Metallic/Normal/Emit) correctly wired â€” reuse
    // its material rather than guessing from the ImageToStl FBX (whose maps are renamed image0/1/2,
    // which made the old "largest texture = albedo" guess pick a metallic map â†’ scrambled look).
    const string GlbDonor    = "Assets/Resources/player.fbx.glb";
    const string TextureDonor = "Assets/Resources/player_textured_donor.fbx";   // fallback texture source
    const string TexOutDir    = "Assets/Resources/PlayerTextures";

    static string ApplyDonorTextures(GameObject vis)
    {
        // Best path: the GLB is already correctly textured (skin + cyborg arm + clothing). Reuse its
        // material directly, so the rigged FBX â€” same mesh/UVs from Mixamo â€” looks identical.
        var glbMat = FirstMaterial(GlbDonor);
        if (glbMat != null)
        {
            int n = AssignToAll(vis, glbMat);
            Debug.Log($"[FixPlayer] Reused GLB material '{glbMat.name}' on {n} renderer(s).");
            return $"reused GLB material on {n} renderer(s)";
        }

        // Fallback: build a material from the correctly-NAMED base-color texture (prefer name match
        // over size â€” the scrambled look came from picking by size). Try the GLB's textures, then the
        // FBX donor's (extracted from embedded).
        var baseMap = FindBaseColor(GlbDonor) ?? FindBaseColorFromFbx();
        if (baseMap == null) return "no textured donor found (player is untextured)";

        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(sh) { name = "PlayerSkin" };
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", baseMap);
        mat.mainTexture = baseMap;
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Materials"))
            AssetDatabase.CreateFolder("Assets/Resources", "Materials");
        AssetDatabase.CreateAsset(mat, "Assets/Resources/Materials/PlayerSkin.mat");
        int n2 = AssignToAll(vis, mat);
        Debug.Log($"[FixPlayer] Built PlayerSkin from '{baseMap.name}', applied to {n2} renderer(s).");
        return $"applied base map '{baseMap.name}' to {n2} renderer(s)";
    }

    /// <summary>First Material sub-asset of an imported model/glb (its authored, correctly-mapped mat).</summary>
    static Material FirstMaterial(string assetPath)
    {
        if (AssetImporter.GetAtPath(assetPath) == null) return null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            if (a is Material m) return m;
        return null;
    }

    /// <summary>A base-color texture from an asset's sub-assets, chosen by NAME (base/albedo/color/
    /// diffuse), else the largest as a last resort.</summary>
    static Texture2D FindBaseColor(string assetPath)
    {
        if (AssetImporter.GetAtPath(assetPath) == null) return null;
        Texture2D named = null, biggest = null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (a is not Texture2D t) continue;
            string n = t.name.ToLowerInvariant();
            if (n.Contains("base") || n.Contains("albedo") || n.Contains("color") || n.Contains("diffuse")) named = t;
            if (biggest == null || t.width * t.height > biggest.width * biggest.height) biggest = t;
        }
        return named ?? biggest;
    }

    /// <summary>Extract the FBX donor's embedded textures to disk, then pick the base-color one.</summary>
    static Texture2D FindBaseColorFromFbx()
    {
        if (AssetImporter.GetAtPath(TextureDonor) is not ModelImporter imp) return null;
        if (!AssetDatabase.IsValidFolder(TexOutDir))
            AssetDatabase.CreateFolder("Assets/Resources", "PlayerTextures");
        imp.ExtractTextures(TexOutDir);
        AssetDatabase.Refresh();

        Texture2D named = null, biggest = null;
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexOutDir }))
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            if (t == null) continue;
            string n = t.name.ToLowerInvariant();
            if (n.Contains("base") || n.Contains("albedo") || n.Contains("color") || n.Contains("diffuse")) named = t;
            if (biggest == null || t.width * t.height > biggest.width * biggest.height) biggest = t;
        }
        return named ?? biggest;
    }

    static int AssignToAll(GameObject vis, Material mat)
    {
        int n = 0;
        foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats; n++;
        }
        return n;
    }

    /// <summary>Find the single file literally named "player.fbx" anywhere under Assets. Prefers a
    /// Resources copy if two ever exist, else takes the first exact match.</summary>
    static string FindPlayerFbx()
    {
        const string preferred = "Assets/Resources/player.fbx";
        if (AssetImporter.GetAtPath(preferred) is ModelImporter) return preferred;

        string fallback = null;
        foreach (var guid in AssetDatabase.FindAssets("player t:Model"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileName(p).ToLowerInvariant() != "player.fbx") continue;
            if (p.Contains("/Resources/")) return p;   // prefer a Resources copy
            fallback ??= p;
        }
        return fallback;
    }

    // QUARANTINED 2026-08-03 — would wreck the fixed player/rig; menu disabled. See memory player-character-pipeline.
    // [MenuItem("Wasteland/Player â–¸ Set Selected As Player Visual")]
    static void SetSelectedAsVisual()
    {
        var model = Selection.activeGameObject;
        if (model == null)
        {
            EditorUtility.DisplayDialog("Fix Player", "Select the new player model in the Hierarchy first, then run this again.", "OK");
            return;
        }

        // Locate the source FBX (via a skinned mesh) so we can force the import + grab the avatar.
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        string fbxPath = (smr && smr.sharedMesh) ? AssetDatabase.GetAssetPath(smr.sharedMesh) : null;

        Debug.Log($"[FixPlayer] Visual mesh source file: {fbxPath ?? "NONE (not a skinned model!)"}");

        Avatar avatar = null;
        if (!string.IsNullOrEmpty(fbxPath) && AssetImporter.GetAtPath(fbxPath) is ModelImporter imp)
        {
            // ALWAYS force a clean Humanoid build â€” an external .meta edit sets the flag but doesn't
            // make Unity actually generate the avatar, which is why none existed.
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
            AssetDatabase.Refresh();
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (a is Avatar av) avatar = av;
        }

        string avatarMsg = avatar == null ? "NO avatar found on the model"
            : (avatar.isHuman && avatar.isValid) ? "valid Humanoid avatar âœ“"
            : "âš  avatar is NOT a valid Humanoid (the rig didn't map â€” the FBX needs re-rigging)";

        // Find the player root.
        var pc = Object.FindAnyObjectByType<Player3DController>();
        if (pc == null)
        {
            EditorUtility.DisplayDialog("Fix Player",
                "No Player3DController found in the open scene. Open the gameplay scene with the player and try again.\n\n"
                + "Avatar status: " + avatarMsg, "OK");
            return;
        }
        var player = pc.transform;

        // The auto-rescale + foot-plant lives in PlayerAnimator3D. If the player root doesn't have it
        // (common on a duplicated "Player (1)"), the model never gets sized to 1.8 m and stays tiny.
        bool hadAnimator3D = pc.GetComponent<PlayerAnimator3D>() != null;
        if (!hadAnimator3D) Undo.AddComponent<PlayerAnimator3D>(pc.gameObject);
        Debug.Log($"[FixPlayer] PlayerAnimator3D on '{pc.name}': {(hadAnimator3D ? "already present" : "ADDED (was missing â†’ that's the 'tiny' cause)")}");

        // Reparent + normalise.
        Undo.SetTransformParent(model.transform, player, "Set Player Visual");
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale    = Vector3.one;
        Undo.RecordObject(model, "Rename Visual");
        model.name = "Visual";

        // Animator + avatar + controller.
        var anim = model.GetComponent<Animator>();
        if (anim == null) anim = Undo.AddComponent<Animator>(model);
        if (avatar != null) anim.avatar = avatar;
        var rc = Resources.Load<RuntimeAnimatorController>("PlayerAnimator");
        if (rc != null) anim.runtimeAnimatorController = rc;
        anim.applyRootMotion = false;

        EditorUtility.SetDirty(model);
        Debug.Log($"[FixPlayer] '{model.name}' is now the Player Visual under '{player.name}'. " +
                  $"Avatar: {avatarMsg}. Controller: {(rc != null ? "PlayerAnimator assigned" : "MISSING (Resources/PlayerAnimator)")}. " +
                  "Press Play â€” PlayerAnimator3D will rescale + foot-plant him.");

        EditorUtility.DisplayDialog("Fix Player",
            $"Done.\n\nVisual parented under the Player.\nAvatar: {avatarMsg}\nController: {(rc != null ? "assigned" : "MISSING")}\n\n"
            + "Press Play. If he still T-poses, the avatar line above will say why.", "OK");
    }
}
