using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EDITOR TOOL â€” "make sure the new character models imported properly."
/// Menu: Wasteland â–¸ Audit New Character Models.
///
/// Walks every rigged model under Assets/Art/newshit and reports, for each:
///   â€¢ rig type (None / Legacy / Generic / Humanoid) â€” flags Generic on player/NPC models, since the
///     Mixamo-style humanoid animation library can't retarget onto a Generic rig.
///   â€¢ humanoid avatar validity (a Humanoid rig is useless if the avatar didn't map).
///   â€¢ material slots assigned vs missing (missing = renders magenta / untextured in-game).
///   â€¢ glTF files (Roxy) â€” whether a glTF importer is actually handling them as models.
///
/// Plus a fix: select the FBX(s) in the Project window and run
///   Wasteland â–¸ Fix â†’ Set Selected Models to Humanoid.
///
/// Report-only otherwise â€” it changes nothing until you ask it to. Run it, read the Console.
/// </summary>
public static class CharacterModelAuditor
{
    const string Root = "Assets/Art/newshit";

    [MenuItem("Wasteland/Audit New Character Models")]
    public static void Audit()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Character Model Import Audit  (" + Root + ") ===");

        var modelPaths = AssetDatabase.FindAssets("t:Model", new[] { Root })
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p).ToList();

        int characters = 0;
        foreach (var path in modelPaths)
        {
            // Skip the environment kit and the weapon pack outright â€” they're static set dressing.
            if (path.Contains("/enviroments/") || path.Contains("Fantasy Weapons")) continue;
            if (AssetImporter.GetAtPath(path) is not ModelImporter imp) continue;
            // A real character/creature is SKINNED to a skeleton. Static props & weapons use plain
            // MeshRenderers, so this cleanly excludes them no matter what rig the importer reports.
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null || go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
            characters++;
            sb.Append(AuditModel(path, imp));
        }

        // glTF / glb (Roxy is glTF). These don't import without a glTF package â€” confirm what's handling them.
        foreach (var path in AssetDatabase.GetAllAssetPaths()
                     .Where(p => p.StartsWith(Root) &&
                                 (p.EndsWith(".gltf") || p.EndsWith(".glb")) &&
                                 !p.Contains("/enviroments/") && !p.Contains("Fantasy Weapons"))
                     .OrderBy(p => p))
        {
            var imp = AssetImporter.GetAtPath(path);
            var go  = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            // Skip glTFs that imported fine but are NOT characters (props/weapons). But if it FAILED to
            // import (go == null), keep it â€” that's exactly the broken-import case we want to surface.
            if (go != null && go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
            // GltfImporter is a ScriptedImporter (not a ModelImporter) but still produces a real model.
            // The thing that matters is whether it actually yielded a GameObject.
            bool skinned = go != null && go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
            string note = go == null
                ? (imp != null ? imp.GetType().Name : "none") + "  (âš  did NOT import as a usable model â€” check the Console / glTF importer)"
                : imp.GetType().Name + (skinned ? "  (OK â€” imports as a skinned character âœ“)"
                                                : "  (imports, but no skinned mesh â†’ static)");
            sb.AppendLine($"\n[glTF] {Path.GetFileName(path)}\n   {path}\n   importer: {note}");
        }

        sb.AppendLine($"\nScanned {modelPaths.Count} models; {characters} are rigged (characters/creatures).");
        sb.AppendLine("Fix a Generic rig: select the FBX in Project, then  Wasteland â–¸ Fix â†’ Set Selected Models to Humanoid.");
        Debug.Log(sb.ToString());
    }

    static string AuditModel(string path, ModelImporter imp)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\n[{Path.GetFileName(path)}]");
        sb.AppendLine($"   {path}");

        bool genericWarn = imp.animationType == ModelImporterAnimationType.Generic;
        sb.AppendLine($"   rig: {imp.animationType}" +
            (genericWarn ? "  (âš  Generic â€” humanoid/Mixamo clips won't retarget; set Humanoid if this is a person)" : ""));

        if (imp.animationType == ModelImporterAnimationType.Human)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var anim = go ? go.GetComponentInChildren<Animator>() : null;
            bool valid = anim && anim.avatar && anim.avatar.isValid && anim.avatar.isHuman;
            sb.AppendLine($"   avatar: {(valid ? "valid humanoid âœ“" : "âš  NOT a valid humanoid avatar (rig didn't map â€” check the Rig tab)")}");
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            sb.AppendLine("   âš  could not load the imported prefab â€” the import may have FAILED. Check the Console for errors.");
            return sb.ToString();
        }

        var rends = prefab.GetComponentsInChildren<Renderer>(true);
        int slots = rends.Sum(r => r.sharedMaterials.Length);
        int nulls = rends.Sum(r => r.sharedMaterials.Count(m => m == null));
        sb.AppendLine($"   meshes: {rends.Length} renderer(s)");
        sb.AppendLine($"   materials: {slots - nulls}/{slots} assigned" +
            (nulls > 0 ? $"  (âš  {nulls} missing â†’ magenta/untextured. Use the model's Materials tab â–¸ Extract Materials/Textures)" : " âœ“"));
        return sb.ToString();
    }

    // QUARANTINED 2026-08-03 — would wreck the fixed player/rig; menu disabled. See memory player-character-pipeline.
    // [MenuItem("Wasteland/Fix â†’ Set Selected Models to Humanoid")]
    public static void SelectedToHumanoid()
    {
        int done = 0;
        foreach (var obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (AssetImporter.GetAtPath(path) is ModelImporter imp)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
                done++;
            }
        }

        if (done == 0)
            Debug.LogWarning("[Auditor] Select one or more model (FBX) assets in the Project window first.");
        else
            Debug.Log($"[Auditor] Set {done} model(s) to Humanoid + auto-create avatar. " +
                      "Re-run 'Audit New Character Models' to confirm each avatar came out valid.");
    }
}
