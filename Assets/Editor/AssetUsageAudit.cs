using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Finds third-party asset-pack folders that nothing in your BUILD actually uses, so you can
/// move them out of the project (faster imports, smaller git, no risk of accidental inclusion).
///
/// "Used" = anything reachable from your enabled Build Settings scenes, PLUS everything under any
/// Resources/ folder (those always ship — e.g. Assets/Resources/HeldModels that ItemRegistry loads
/// by string), PLUS your render-pipeline assets. A top-level folder is flagged only when NONE of
/// its files are in that set.
///
/// Step 1 "Audit" is read-only — it writes a full breakdown (with sizes) to the Console.
/// Step 2 "Move" relocates the flagged folders to a sibling "_UnusedAssets" folder OUTSIDE the
/// project. It's reversible (move a folder back into Assets/ and Refresh). COMMIT TO GIT FIRST.
///
/// Caveat: assets loaded only via Addressables or editor-only tools can't be detected by
/// dependency analysis — eyeball the flagged list before moving.
///
/// Menu:  Wasteland ▸ Cleanup ▸ 1. Audit / 2. Move
/// </summary>
public static class AssetUsageAudit
{
    // Never flagged — engine/code/game-critical folders.
    static readonly string[] Protected =
    {
        "Scripts", "Editor", "Resources", "Scenes", "Settings", "_Wasteland",
        "StreamingAssets", "Plugins", "Gizmos", "TextMesh Pro", "UI Toolkit"
    };

    const string MoveTargetName = "_UnusedAssets";
    const long GB = 1L << 30, MB = 1L << 20, KB = 1L << 10;

    [MenuItem("Wasteland/Cleanup/1. Audit Unused Asset Folders (report)")]
    public static void Audit()
    {
        var used = ComputeUsedSet();
        var (candidates, report) = Analyze(used);
        Debug.Log(report);
        EditorUtility.DisplayDialog("Asset Usage Audit",
            $"{candidates.Count} top-level folder(s) look 100% UNUSED by your build scenes / Resources.\n\n" +
            "Full breakdown (with sizes + used/total counts) is in the Console.\n\n" +
            "Review it, then run '2. Move Unused Folders Out'.\n\n" +
            "Caveat: assets loaded only via Addressables or editor tools can't be auto-detected — " +
            "sanity-check the flagged list first.", "OK");
    }

    // MENU REMOVED — bulk-moves asset folders; too risky to have one click away. The read-only
    // "1. Audit" report above still works. Re-enable the [MenuItem] line only if you mean to run it.
    // [MenuItem("Wasteland/Cleanup/2. Move Unused Folders Out (after review)")]
    public static void MoveUnused()
    {
        var used = ComputeUsedSet();
        var (candidates, _) = Analyze(used);
        if (candidates.Count == 0)
        {
            EditorUtility.DisplayDialog("Move Unused Folders Out", "Nothing is flagged as 100% unused.", "OK");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string target = Path.Combine(Directory.GetParent(projectRoot).FullName, MoveTargetName);

        if (!EditorUtility.DisplayDialog("Move Unused Folders Out",
            $"Move these {candidates.Count} folder(s) OUT of the project to:\n{target}\n\n" +
            string.Join("\n", candidates.Select(c => "• " + c)) +
            "\n\n⚠ COMMIT TO GIT FIRST. Reversible: move a folder back into Assets/ and Refresh.",
            "Move them out", "Cancel"))
            return;

        Directory.CreateDirectory(target);
        int moved = 0;
        var log = new StringBuilder("=== Moved unused folders ===\n");
        foreach (var rel in candidates)
        {
            string abs = Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            string dst = Path.Combine(target, Path.GetFileName(rel));
            try
            {
                if (Directory.Exists(abs)) Directory.Move(abs, dst);
                if (File.Exists(abs + ".meta")) File.Move(abs + ".meta", dst + ".meta");
                log.AppendLine("✓ " + rel);
                moved++;
            }
            catch (System.Exception e) { log.AppendLine("✗ " + rel + " — " + e.Message); }
        }
        Debug.Log(log);
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Move Unused Folders Out",
            $"Moved {moved}/{candidates.Count} folder(s) to:\n{target}\n\n" +
            "To restore one, move it back into Assets/ and let Unity reimport.", "OK");
    }

    static HashSet<string> ComputeUsedSet()
    {
        var roots = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled && !string.IsNullOrEmpty(s.path)) roots.Add(s.path);

        foreach (var p in AssetDatabase.GetAllAssetPaths())
            if (p.StartsWith("Assets/") && p.Contains("/Resources/")) roots.Add(p);   // always shipped

        foreach (var g in AssetDatabase.FindAssets("t:RenderPipelineAsset"))
            roots.Add(AssetDatabase.GUIDToAssetPath(g));

        return new HashSet<string>(AssetDatabase.GetDependencies(roots.Distinct().ToArray(), true));
    }

    static (List<string> candidates, string report) Analyze(HashSet<string> used)
    {
        // Build the file list once (folders excluded), bucketed by top-level folder.
        var allFiles = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p))
            .ToList();

        var sb = new StringBuilder("=== Asset Usage Audit (used / total, size) ===\n");
        var candidates = new List<string>();
        long unusedBytes = 0;

        foreach (var dir in Directory.GetDirectories(Application.dataPath).OrderBy(d => d))
        {
            string name = Path.GetFileName(dir);
            string rel = "Assets/" + name;
            var files = allFiles.Where(p => p.StartsWith(rel + "/")).ToList();
            int total = files.Count;
            int usedCount = files.Count(used.Contains);
            long bytes = DirSize(dir);
            bool prot = Protected.Contains(name);

            string tag = prot ? "[protected]"
                       : total == 0 ? "[empty]"
                       : usedCount == 0 ? "[UNUSED → move]"
                       : usedCount < total ? "[partial]"
                       : "[used]";

            sb.AppendLine($"{tag,-18} {Human(bytes),9}  {usedCount,4}/{total,-4} used  {rel}");
            if (!prot && total > 0 && usedCount == 0) { candidates.Add(rel); unusedBytes += bytes; }
        }

        sb.AppendLine($"\n{candidates.Count} folder(s) flagged 100% unused — {Human(unusedBytes)} reclaimable.");
        return (candidates, sb.ToString());
    }

    static long DirSize(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }

    static string Human(long b) =>
        b >= GB ? $"{b / (double)GB:0.0} GB" : b >= MB ? $"{b / (double)MB:0.0} MB" : $"{b / KB} KB";
}
