using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>
/// Sets up the Crimson-Circuit FBX as the player Visual with Animator,
/// wired to the existing PlayerAnimator.controller.  Humanoid retargeting
/// handles the bone differences automatically as long as both avatars are valid.
///
/// Menu:  Wasteland > Player > Quick: Use Crimson-Circuit FBX
/// </summary>
public static class QuickPlayerVisual
{
    const string FbxPath = "Assets/Art/newshit/finalofficialplayercharacter/Crimson-Circuit-019f196d.fbx";

    // QUARANTINED 2026-08-03 — would wreck the fixed player/rig; menu disabled. See memory player-character-pipeline.
    // [MenuItem("Wasteland/Player/Quick: Use Crimson-Circuit FBX")]
    public static void Run()
    {
        var pc = Object.FindAnyObjectByType<Player3DController>();
        if (pc == null)
        {
            EditorUtility.DisplayDialog("No Player", "Open the dontfuckindelete scene first.", "OK");
            return;
        }

        // 1. Ensure the FBX is imported as Humanoid with its own avatar
        var imp = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (imp == null)
        {
            EditorUtility.DisplayDialog("FBX Not Found", $"Not found:\n{FbxPath}", "OK");
            return;
        }

        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.SaveAndReimport();
        AssetDatabase.Refresh();

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
            .OfType<Avatar>().FirstOrDefault();

        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            EditorUtility.DisplayDialog("Avatar Issue",
                "The FBX didn't produce a valid Humanoid avatar.\n\n" +
                "Select it in the Project window â†’ Rig tab â†’ Configure â†’ make sure all bones are green â†’ Apply.",
                "OK");
            return;
        }

        // 2. Remove old Visual children
        for (int i = pc.transform.childCount - 1; i >= 0; i--)
        {
            var child = pc.transform.GetChild(i);
            if (child.name == "Visual" || child.GetComponent<Renderer>() != null)
                Object.DestroyImmediate(child.gameObject);
        }

        // 3. Instantiate FBX as Visual child
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var vis = (GameObject)PrefabUtility.InstantiatePrefab(src);
        vis.name = "Visual";
        vis.transform.SetParent(pc.transform, false);
        vis.transform.localPosition = Vector3.zero;
        vis.transform.localRotation = Quaternion.identity;
        vis.transform.localScale    = Vector3.one;

        // 4. Animator setup
        var anim = vis.GetComponent<Animator>();
        if (anim == null) anim = vis.AddComponent<Animator>();
        anim.avatar = avatar;
        anim.applyRootMotion = false;

        var ctrl = Resources.Load<RuntimeAnimatorController>("PlayerAnimator");
        if (ctrl != null)
            anim.runtimeAnimatorController = ctrl;
        else
            Debug.LogWarning("[QuickPlayer] PlayerAnimator.controller not in Resources â€” run 'Wasteland > Player > 1. Set Up Rigs + Animator' first.");

        // 5. Ensure PlayerAnimator3D exists
        if (pc.GetComponent<PlayerAnimator3D>() == null)
            pc.gameObject.AddComponent<PlayerAnimator3D>();

        // 6. Resize to human scale
        var bounds = CalcBounds(vis);
        if (bounds.size.y > 0.1f && bounds.size.y < 100f)
        {
            float scale = 1.8f / bounds.size.y;
            if (scale > 0.1f && scale < 10f)
                vis.transform.localScale = Vector3.one * scale;
        }

        EditorSceneManager.MarkSceneDirty(pc.gameObject.scene);

        string msg = avatar.isValid && avatar.isHuman
            ? $"Avatar: valid Humanoid \u2713\nController: {(ctrl != null ? "assigned" : "MISSING")}"
            : "\u26a0 Avatar NOT valid Humanoid";

        EditorUtility.DisplayDialog("Player Visual Set",
            $"Crimson-Circuit FBX is now the player Visual.\n\n{msg}\n\n" +
            "SAVE the scene (Ctrl+S), press Play.\n" +
            "WASD / click-to-move should animate the character.", "OK");
    }

    static Bounds CalcBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds();
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
