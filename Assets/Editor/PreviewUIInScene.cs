using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Spawns the self-building runtime UI panels into the OPEN SCENE so you can look at them, measure
/// them and iterate on layout without entering play mode.
///
/// READ THIS BEFORE EDITING WHAT IT SPAWNS: these panels construct themselves in code
/// (CombatStyleUI.BuildUI, SkillsPanelUI.BuildUI) every time the game starts. Anything you drag,
/// recolour or resize on the spawned objects is therefore PREVIEW ONLY — it is thrown away and
/// rebuilt from the C# on the next play. To change the panels for real, change the build methods.
///
/// The one thing you CAN change from outside the code is skill icons: SkillsPanelUI loads them with
///     Resources.Load&lt;Sprite&gt;($"SkillIcons/{skill.ToLower()}")
/// so dropping a correctly-named sprite into Assets/Resources/SkillIcons/ is picked up with no code
/// change at all. Missing ones are skipped silently, which is why a skill with no icon still works.
///
/// Menu:  Wasteland ▸ UI ▸ Preview Panels In Scene   /   Remove Previews
/// </summary>
public static class PreviewUIInScene
{
    const string PreviewRootName = "~UI PREVIEW (editor only, not saved logic)";

    [MenuItem("Wasteland/UI/Preview Panels In Scene")]
    public static void Spawn()
    {
        Remove();   // never stack duplicates

        var root = new GameObject(PreviewRootName);
        Undo.RegisterCreatedObjectUndo(root, "Preview UI Panels");

        int built = 0;
        built += Build<CombatStyleUI>(root, "CombatStyleUI (preview)") ? 1 : 0;
        built += Build<SkillsPanelUI>(root, "SkillsPanelUI (preview)") ? 1 : 0;

        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        EditorUtility.DisplayDialog("UI Preview",
            $"Spawned {built} panel(s) under '{PreviewRootName}'.\n\n" +
            "These are PREVIEW ONLY — the panels rebuild themselves from code on play, so edits " +
            "to these objects are discarded.\n\n" +
            "To change skill icons for real, drop sprites into Assets/Resources/SkillIcons/ named " +
            "after the skill in lowercase (fission.png, refinement.png, melee.png).\n\n" +
            "Use Wasteland > UI > Remove Previews when you're done.", "OK");
    }

    [MenuItem("Wasteland/UI/Remove Previews")]
    public static void Remove()
    {
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            if (go != null && go.name == PreviewRootName)
                Undo.DestroyObjectImmediate(go);
    }

    /// <summary>
    /// Add the component and run its private BuildUI. We call BuildUI rather than Awake on purpose:
    /// Awake also assigns the singleton Instance and calls DontDestroyOnLoad, neither of which is
    /// meaningful in edit mode and both of which leave junk behind.
    /// </summary>
    static bool Build<T>(GameObject parent, string name) where T : MonoBehaviour
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var comp = go.AddComponent<T>();

        var build = typeof(T).GetMethod("BuildUI", BindingFlags.NonPublic | BindingFlags.Instance);
        if (build == null)
        {
            Debug.LogWarning($"[PreviewUIInScene] {typeof(T).Name} has no BuildUI() — skipped.", go);
            return false;
        }

        build.Invoke(comp, null);

        // BuildUI leaves the panel hidden (it starts closed at runtime). Show it, or the preview is
        // an empty object and the whole exercise is pointless.
        foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == "Backdrop") rt.gameObject.SetActive(true);

        return true;
    }
}
