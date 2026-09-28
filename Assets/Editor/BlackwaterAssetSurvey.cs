using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BlackwaterAssetSurvey
{
    public static void Run()
    {
        Directory.CreateDirectory("Docs/Blackwater");
        var report = new StringBuilder();
        var roots = new[] { "Assets/Hivemind/RuralTown/URP", "Assets/Hivemind/MilitaryCamp/URP", "Assets/Old Sea Port/prefabs", "Assets/Resources/Buildings", "Assets/Resources/Foliage" };
        foreach (string root in roots)
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            report.AppendLine($"{path}\t{bounds.size}\t{renderers.Length} renderers");
        }
        File.WriteAllText("Docs/Blackwater/asset-survey.tsv", report.ToString());
        report.Clear();
        var scene = EditorSceneManager.OpenScene("Assets/Hivemind/RuralTown/URP/Scenes/LV_Showcase.unity");
        foreach (var root in scene.GetRootGameObjects()) Describe(root.transform, 0, report);
        File.WriteAllText("Docs/Blackwater/rural-hierarchy.txt", report.ToString());
        Debug.Log("BLACKWATER SURVEY COMPLETE");
    }

    static void Describe(Transform t, int depth, StringBuilder output)
    {
        if (depth > 3) return;
        output.AppendLine(new string(' ', depth * 2) + t.name + " | " + t.childCount + " | " + t.position);
        foreach (Transform child in t) Describe(child, depth + 1, output);
    }
}
