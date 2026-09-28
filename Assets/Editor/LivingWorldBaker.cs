using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bakes the runtime LivingWorld generation into the OPEN SCENE as permanent, saved objects.
///
/// Normally LivingWorld spawns the whole world (salvage yards, groves, fishing shores, raider camps,
/// tier bosses, towns, foliage) fresh every time you enter Play mode, under a "~LivingWorld" object —
/// and it's all destroyed when you leave Play, so none of it sticks.
///
/// "Bake" runs that exact same generator once, right now, in edit mode, and leaves the result in the
/// scene. Because LivingWorld's bootstrap skips when a "~LivingWorld" object already exists, the baked
/// world is NOT regenerated on top of at Play — what you baked is what you get, and it survives Play,
/// Save, and reopen. Everything stays fully editable / deletable by hand afterward.
///
/// Menu:  Wasteland ▸ World ▸ Bake Living World Into Scene (permanent)
///        Wasteland ▸ World ▸ Clear Baked World
/// </summary>
public static class LivingWorldBaker
{
    const string RootName = "~LivingWorld";

    [MenuItem("Wasteland/World/Bake Living World Into Scene (permanent)", false, 60)]
    public static void Bake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Bake Living World",
                "Exit Play mode first, then bake.", "OK");
            return;
        }

        // Replace any existing baked/bootstrapped root so we don't stack two worlds.
        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog("Bake Living World",
                $"A \"{RootName}\" already exists in the scene with {existing.transform.childCount} groups.\n\n" +
                "Replace it with a freshly baked world?", "Replace", "Cancel");
            if (!replace) return;
            Undo.DestroyObjectImmediate(existing);
        }

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Bake Living World");

        // A LivingWorld component drives the generation; we remove it afterward so the baked world is
        // static (and so LivingWorld's Play-time bootstrap treats this as "already built" and skips).
        var lw = root.AddComponent<LivingWorld>();

        int sites = 0, flora = 0;
        bool ok = false;
        try
        {
            ok = lw.EditorBake(root.transform);
            sites = lw.builtSites;
            flora = lw.builtFlora;
        }
        finally
        {
            if (lw != null) Object.DestroyImmediate(lw);
        }

        if (!ok)
        {
            Undo.DestroyObjectImmediate(root);
            EditorUtility.DisplayDialog("Bake Living World",
                "No terrain found in the open scene. Open your world scene (the one with the Terrain / \"Ground\") and bake from there.",
                "OK");
            return;
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        EditorUtility.DisplayDialog("Bake Living World",
            $"Baked {sites} sites + {flora} foliage into the scene under \"{RootName}\".\n\n" +
            "It's permanent now — it survives Play and won't be regenerated.\n\n" +
            "Remember to SAVE the scene (Ctrl+S) to keep it.",
            "Great");
    }

    [MenuItem("Wasteland/World/Clear Baked World", false, 61)]
    public static void Clear()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Clear Baked World", "Exit Play mode first.", "OK");
            return;
        }

        var existing = GameObject.Find(RootName);
        if (existing == null)
        {
            EditorUtility.DisplayDialog("Clear Baked World",
                $"No \"{RootName}\" found in the open scene — nothing to clear.\n\n" +
                "(With no baked world present, LivingWorld will generate a fresh one at Play again.)",
                "OK");
            return;
        }

        int groups = existing.transform.childCount;
        if (!EditorUtility.DisplayDialog("Clear Baked World",
                $"Delete \"{RootName}\" and its {groups} groups?\n\n" +
                "LivingWorld will then regenerate the world at Play as before.", "Delete", "Cancel"))
            return;

        Undo.DestroyObjectImmediate(existing);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
