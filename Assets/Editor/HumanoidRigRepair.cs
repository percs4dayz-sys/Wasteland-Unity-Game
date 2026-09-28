using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Repairs "Copied Avatar Rig Configuration mis-match" errors in bulk.
///
/// The cause: an animation pack's FBXs are set to COPY an avatar from some other model whose
/// bone hierarchy doesn't match theirs, so Unity can't build the rig and the clips are dead.
/// In this project all 50 files in "Sword and Shield Pack" copy from Assets/Resources/player.fbx,
/// while their own skeletons match the pack's own player.fbx â€” 49 errors on every reimport.
///
/// The fix: set every FBX to Humanoid with "Create From This Model". Each then builds an avatar
/// from its OWN skeleton, and Unity retargets between characters through the humanoid rig â€”
/// which is what humanoid is for. No copying, nothing to mismatch.
///
/// Menu:  Wasteland â–¸ Fix â–¸ Repair Humanoid Animation Rigs
/// </summary>
public static class HumanoidRigRepair
{
    const string DefaultFolder = "Assets/Resources/otherneededassets/Sword and Shield Pack";

    // QUARANTINED 2026-08-03 — would wreck the fixed player/rig; menu disabled. See memory player-character-pipeline.
    // [MenuItem("Wasteland/Fix/Repair Humanoid Animation Rigs", false, 0)]
    public static void Repair()
    {
        string folder = ResolveFolder();
        if (folder == null) return;

        var fbx = FindModels(folder);
        if (fbx.Count == 0)
        {
            EditorUtility.DisplayDialog("Nothing Found",
                $"No .fbx files under:\n{folder}", "OK");
            return;
        }

        // Report what's currently wrong before changing anything.
        int copying = 0, notHuman = 0, alreadyOk = 0;
        foreach (string p in fbx)
        {
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;
            if (mi.animationType != ModelImporterAnimationType.Human) notHuman++;
            else if (mi.avatarSetup == ModelImporterAvatarSetup.CopyFromOther) copying++;
            else alreadyOk++;
        }

        if (!EditorUtility.DisplayDialog("Repair Humanoid Animation Rigs",
            $"Folder:\n{folder}\n\n" +
            $"{fbx.Count} model(s) found:\n" +
            $"   {copying} copying an avatar from another model  â† the broken ones\n" +
            $"   {notHuman} not set to Humanoid\n" +
            $"   {alreadyOk} already correct\n\n" +
            "Each will be set to Humanoid + 'Create From This Model', then reimported.\n" +
            "This changes import settings only â€” no files are moved or deleted.\n\n" +
            "Reimporting this many models takes a minute or two. Continue?",
            "Repair", "Cancel")) return;

        var log = new StringBuilder("=== Repair Humanoid Animation Rigs ===\n");
        log.AppendLine($"Folder: {folder}   ({fbx.Count} models)\n");

        int fixedCount = 0, skipped = 0, failed = 0;
        try
        {
            AssetDatabase.StartAssetEditing();     // batch â€” far faster than one at a time
            for (int i = 0; i < fbx.Count; i++)
            {
                string p = fbx[i];
                EditorUtility.DisplayProgressBar("Repairing rigs",
                    Path.GetFileName(p), (i + 1) / (float)fbx.Count);

                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null) { failed++; continue; }

                bool needs = mi.animationType != ModelImporterAnimationType.Human
                          || mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                          || mi.sourceAvatar != null;
                if (!needs) { skipped++; continue; }

                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.sourceAvatar = null;                 // stop copying â€” this is the actual fix
                mi.importAnimation = true;
                EditorUtility.SetDirty(mi);
                mi.SaveAndReimport();

                fixedCount++;
                log.AppendLine($"   fixed  {Path.GetFileName(p)}");
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        log.AppendLine($"\nâœ…  {fixedCount} repaired, {skipped} already correct" +
                       (failed > 0 ? $", {failed} had no ModelImporter." : "."));
        log.AppendLine("\nEach model now builds its own avatar. Unity retargets between them via");
        log.AppendLine("the humanoid rig, so these clips will drive ANY humanoid character â€”");
        log.AppendLine("your player, Miyu, or anything else set to Humanoid.");
        log.AppendLine("\nIf some still error, their skeleton isn't human-shaped enough for Unity to map.");
        log.AppendLine("Select one, Rig tab, click Configure, and check which bones are unassigned.");

        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Rig Repair â€” Done",
            $"{fixedCount} model(s) repaired.\n\n" +
            "Clear the Console and reimport to confirm the errors are gone.\n" +
            "See Console for the full list.", "OK");
    }

    /// <summary>Use the Project-window selection if it's a folder, else the known pack.</summary>
    static string ResolveFolder()
    {
        foreach (var o in Selection.GetFiltered<Object>(SelectionMode.Assets))
        {
            string p = AssetDatabase.GetAssetPath(o);
            if (AssetDatabase.IsValidFolder(p)) return p;
        }

        if (AssetDatabase.IsValidFolder(DefaultFolder)) return DefaultFolder;

        string picked = EditorUtility.OpenFolderPanel("Pick a folder of animation FBX files",
                                                      Application.dataPath, "");
        if (string.IsNullOrEmpty(picked)) return null;
        if (!picked.StartsWith(Application.dataPath))
        {
            EditorUtility.DisplayDialog("Outside Project",
                "Pick a folder inside this project's Assets/ folder.", "OK");
            return null;
        }
        return "Assets" + picked.Substring(Application.dataPath.Length);
    }

    static List<string> FindModels(string folder)
    {
        var found = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                found.Add(p);
        }
        found.Sort();
        return found;
    }

    // â”€â”€ quick read-only survey, safe to run any time â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [MenuItem("Wasteland/Fix/Report Rig Settings (read-only)", false, 1)]
    public static void Report()
    {
        string folder = ResolveFolder();
        if (folder == null) return;

        var fbx = FindModels(folder);
        var log = new StringBuilder($"=== Rig Settings: {folder} ===\n{fbx.Count} model(s)\n\n");

        var byState = new Dictionary<string, int>();
        foreach (string p in fbx)
        {
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;
            string src = mi.sourceAvatar != null ? mi.sourceAvatar.name : "-";
            string state = $"{mi.animationType} / {mi.avatarSetup} / src={src}";
            byState.TryGetValue(state, out int c);
            byState[state] = c + 1;
        }

        foreach (var kv in byState)
            log.AppendLine($"   {kv.Value,4} x   {kv.Key}");
        log.AppendLine("\nWanted for an animation pack: Human / CreateFromThisModel / src=-");
        Debug.Log(log.ToString());
    }
}
