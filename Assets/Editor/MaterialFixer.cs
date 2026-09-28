using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-converts all materials in the project to URP/Lit.
/// Fixes the magenta/pink material problem in one click.
/// Menu: Wasteland > Fix All Materials (URP)
/// </summary>
public static class MaterialFixer
{
    // QUARANTINED 2026-08-03 — this force-swaps EVERY material to URP/Lit, which clobbers custom
    // ShaderGraph materials (it wiped 448 LeartesStudios + Synty materials to white — recovered from
    // MatBackup-clobber). It cannot tell a genuinely-pink Standard material from a working custom
    // shader. Do NOT re-enable without rewriting it to only touch materials whose shader is literally
    // Standard/missing. See memory: editor-autoscan-hooks / player-character-pipeline.
    // [MenuItem("Wasteland/Fix All Materials (URP)")]
    public static void FixAllMaterials()
    {
        var urpShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpShader == null)
        {
            EditorUtility.DisplayDialog("URP Not Found",
                "Universal Render Pipeline/Lit shader not found. Is the URP package installed?",
                "OK");
            return;
        }

        // SCOPE TO Assets/ ONLY. An unscoped FindAssets returns every material in the project,
        // including the immutable ones inside Packages/ (ProBuilder, URP runtime, the 2D importer) —
        // altering those triggers "unexpectedly altered immutable packages" and the changes get wiped
        // on the next Package Manager operation anyway. Never our job to touch them.
        var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        int fixedCount = 0, skipped = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Packages/")) { skipped++; continue; }   // belt-and-suspenders

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            // A Material Variant inherits its shader from its parent and cannot have one set directly
            // ("Trying to set shader on a Material Variant"). Leave variants alone.
            if (mat.isVariant) { skipped++; continue; }

            // Skip materials that already use a URP shader
            if (mat.shader != null && mat.shader.name.Contains("Universal Render Pipeline"))
            {
                skipped++;
                continue;
            }

            // Skip skybox, sprite, UI, particle materials — they need their own shaders
            if (mat.shader != null && (
                mat.shader.name.Contains("Skybox") ||
                mat.shader.name.Contains("Sprite") ||
                mat.shader.name.Contains("UI/") ||
                mat.shader.name.Contains("Particle") ||
                mat.shader.name.Contains("TextMeshPro") ||
                mat.shader.name.Contains("Water") ||
                mat.shader.name.Contains("Terrain") ||
                mat.shader.name.Contains("Nature")))
            {
                skipped++;
                continue;
            }

            mat.shader = urpShader;
            EditorUtility.SetDirty(mat);
            fixedCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Materials Fixed",
            $"Converted {fixedCount} materials to URP/Lit.\n" +
            $"Skipped {skipped} (already URP or special shader).\n\n" +
            "The magenta should clear in a few seconds.",
            "OK");
    }
}
