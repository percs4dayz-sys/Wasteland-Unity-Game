using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// What does the shipped game actually use? Everything pulled in by the enabled build scenes, every Resources
/// folder (they ship whole), and the project settings (render pipeline + quality levels, URP global settings,
/// always-included shaders, preloaded assets, icons, splash logos). The rest of Assets/ is reported as unused,
/// by folder, with sizes — whole folders that are entirely unused are listed separately (they can go in one
/// move). Scripts, editor folders, plugins and streaming assets are left out: code is judged by what compiles
/// against it, not by asset references. Menu: Wasteland ▸ Cleanup ▸ Audit Unused Assets.
/// </summary>
public static class ShippedAssetAudit
{
    [MenuItem("Wasteland/Cleanup/Audit Unused Assets")]
    static void Menu() => Debug.Log(Run(Path.Combine(Path.GetTempPath(), "WastelandUnusedAssets.txt")));

    static readonly string[] CodeLike = { ".cs", ".asmdef", ".asmref", ".dll", ".rsp", ".jslib", ".aar", ".jar", ".so", ".java", ".kt", ".m", ".mm", ".h", ".cpp", ".c", ".xml", ".gradle", ".properties" };

    public static HashSet<string> UsedAssets(out List<string> roots)
    {
        roots = new List<string>();
        roots.AddRange(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path));
        roots.AddRange(AllAssetPaths().Where(p => p.Contains("/Resources/")));

        var extra = new List<string>();
        void Add(Object o) { if (o == null) return; var p = AssetDatabase.GetAssetPath(o); if (!string.IsNullOrEmpty(p) && p.StartsWith("Assets/")) extra.Add(p); }
        Add(GraphicsSettings.defaultRenderPipeline);
        for (int i = 0; i < QualitySettings.names.Length; i++) Add(QualitySettings.GetRenderPipelineAssetAt(i));
        foreach (var o in PlayerSettings.GetPreloadedAssets()) Add(o);
        foreach (var t in new[] { NamedBuildTarget.Android, NamedBuildTarget.Standalone, NamedBuildTarget.Unknown })
            foreach (var icon in PlayerSettings.GetIcons(t, IconKind.Any)) Add(icon);
        foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            foreach (var icon in PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind))
                foreach (var tex in icon.GetTextures()) Add(tex);
        foreach (var logo in PlayerSettings.SplashScreen.logos) Add(logo.logo);
        foreach (var settingsPath in new[] { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset", "ProjectSettings/ProjectSettings.asset", "ProjectSettings/EditorBuildSettings.asset" })
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(settingsPath))
            {
                if (obj == null) continue;
                var it = new SerializedObject(obj).GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference) Add(it.objectReferenceValue);
            }
        // URP global settings & anything else the pipeline keeps by type rather than by reference
        foreach (var guid in AssetDatabase.FindAssets("t:RenderPipelineGlobalSettings")) extra.Add(AssetDatabase.GUIDToAssetPath(guid));
        roots.AddRange(extra);
        return new HashSet<string>(AssetDatabase.GetDependencies(roots.Distinct().ToArray(), true));
    }

    public static IEnumerable<string> AllAssetPaths() =>
        AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p));

    /// <summary>Not judged by asset references: code, native plugins, editor-only folders, streaming assets.</summary>
    public static bool Exempt(string p)
    {
        string ext = Path.GetExtension(p).ToLowerInvariant();
        return CodeLike.Contains(ext) || p.Contains("/Editor/") || p.StartsWith("Assets/Editor") || p.StartsWith("Assets/Plugins/")
               || p.StartsWith("Assets/StreamingAssets/");
    }

    public static string Run(string reportPath)
    {
        var used = UsedAssets(out var roots);
        var all = AllAssetPaths().ToList();
        var unused = all.Where(p => !used.Contains(p) && !Exempt(p)).ToList();
        long Size(string p) { try { return new FileInfo(p).Length; } catch { return 0; } }

        // Folders where nothing at all is used or exempt → can go whole.
        var folderAll = new Dictionary<string, int>(); var folderUnused = new Dictionary<string, int>();
        foreach (var p in all) foreach (var f in Parents(p)) folderAll[f] = folderAll.TryGetValue(f, out int n) ? n + 1 : 1;
        foreach (var p in unused) foreach (var f in Parents(p)) folderUnused[f] = folderUnused.TryGetValue(f, out int n) ? n + 1 : 1;
        var wholeFolders = folderAll.Keys.Where(f => folderUnused.TryGetValue(f, out int u) && u == folderAll[f])
                                         .OrderBy(f => f.Length).ToList();
        var topWhole = new List<string>();
        foreach (var f in wholeFolders) if (!topWhole.Any(t => f.StartsWith(t + "/"))) topWhole.Add(f);

        var sb = new StringBuilder();
        long total = unused.Sum(Size), totalAll = all.Sum(Size);
        sb.AppendLine($"roots: {roots.Count}  used: {used.Count}  assets: {all.Count}  unused: {unused.Count} ({total / 1048576} MB of {totalAll / 1048576} MB)");
        sb.AppendLine("== unused by top folder (MB, files)");
        foreach (var g in unused.GroupBy(p => Top(p, 2)).Select(g => (g.Key, mb: g.Sum(Size) / 1048576, n: g.Count())).OrderByDescending(x => x.mb))
            sb.AppendLine($"{g.mb,8} MB {g.n,6}  {g.Key}");
        sb.AppendLine("== whole folders with nothing used");
        foreach (var f in topWhole) sb.AppendLine($"{unused.Where(p => p.StartsWith(f + "/")).Sum(Size) / 1048576,8} MB  {f}");
        sb.AppendLine("== every unused file");
        foreach (var p in unused.OrderBy(p => p)) sb.AppendLine(p);
        File.WriteAllText(reportPath, sb.ToString());

        var head = sb.ToString().Split('\n');
        int cut = System.Array.FindIndex(head, l => l.StartsWith("== every unused file"));
        return string.Join("\n", head.Take(cut < 0 ? 80 : cut));
    }

    static IEnumerable<string> Parents(string p)
    {
        for (int i = p.LastIndexOf('/'); i > "Assets".Length; i = p.LastIndexOf('/', i - 1)) yield return p.Substring(0, i);
    }

    static string Top(string p, int depth)
    {
        var parts = p.Split('/');
        return string.Join("/", parts.Take(Mathf.Min(depth + 1, parts.Length - 1)));
    }
}
