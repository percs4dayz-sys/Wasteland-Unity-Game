using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One window for the whole art-completeness picture, plus the fix for the trap that keeps biting:
/// a PNG dropped into ItemIcons/ or SkillIcons/ imports as textureType Default with
/// spriteImportMode None, so Resources.Load&lt;Sprite&gt; returns null and the icon silently never
/// shows. This is exactly what happened to fission/refinement. "Fix icon imports" flips every icon
/// texture to a proper Single Sprite in one click, so you never chase it per-file again.
///
/// Also lists which item icons and held models are still missing, straight from the live registry,
/// so the checklist is never stale.
///
/// Menu:  Wasteland ▸ Audit Item Art
/// </summary>
public class AuditItemArt : EditorWindow
{
    const string ItemIconDir  = "Assets/Resources/ItemIcons";
    const string SkillIconDir = "Assets/Resources/SkillIcons";

    Vector2 _scroll;
    string _report = "Click \"Re-scan\" to audit.";

    [MenuItem("Wasteland/Audit Item Art")]
    static void Open() => GetWindow<AuditItemArt>("Audit Item Art").minSize = new Vector2(460, 480);

    void OnEnable() => Rescan();

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Drop item icons into Resources/ItemIcons as <id>.png (the item's numeric ID).\n" +
            "Skill icons into Resources/SkillIcons as <skill>.png (lowercase skill name).\n\n" +
            "Then hit \"Fix icon imports\" — it makes every icon a real Sprite, which they are NOT " +
            "by default (that's why a freshly-dropped icon shows nothing until fixed).",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Re-scan", GUILayout.Height(28))) Rescan();
            if (GUILayout.Button("Fix icon imports (→ Sprite)", GUILayout.Height(28))) { FixIconImports(); Rescan(); }
        }

        if (GUILayout.Button("Copy report to clipboard"))
            EditorGUIUtility.systemCopyBuffer = _report;

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    // ── the import fix ─────────────────────────────────────────────────────────

    /// <summary>Force every texture in the two icon folders to import as a single Sprite. A no-op on
    /// ones already correct, so it's safe to run repeatedly after each batch of new icons.</summary>
    static int FixIconImports()
    {
        int fixedCount = 0;
        foreach (var dir in new[] { ItemIconDir, SkillIconDir })
        {
            if (!AssetDatabase.IsValidFolder(dir)) continue;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                bool needs = imp.textureType != TextureImporterType.Sprite
                          || imp.spriteImportMode != SpriteImportMode.Single;
                if (!needs) continue;

                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
                fixedCount++;
            }
        }
        if (fixedCount > 0) AssetDatabase.Refresh();
        Debug.Log($"[AuditItemArt] Fixed import settings on {fixedCount} icon texture(s).");
        return fixedCount;
    }

    // ── the audit ──────────────────────────────────────────────────────────────

    void Rescan()
    {
        var sb = new StringBuilder();

        var items = ItemRegistry.All.ToList();

        // Icon files present (by numeric id).
        var iconIds = new HashSet<int>();
        if (AssetDatabase.IsValidFolder(ItemIconDir))
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { ItemIconDir }))
                if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)), out int id))
                    iconIds.Add(id);

        bool HasIcon(ItemData item) => ItemIconLoader.LoadIcon(item.id) != null || item.icon != null;
        int withIcon = items.Count(HasIcon);
        sb.AppendLine($"ITEM ICONS: {withIcon}/{items.Count} resolve in game (including shared artwork)");
        sb.AppendLine("  Named modules reuse existing tier/quality icons unless a dedicated icon is supplied.");
        sb.AppendLine("  Upgraded gathering tools temporarily reuse starter-tool icons.");

        // Any icon files that exist but won't load as a Sprite (the trap).
        int broken = 0;
        foreach (var it in items.Where(x => iconIds.Contains(x.id)))
            if (Resources.Load<Sprite>($"ItemIcons/{it.id}") == null) broken++;
        if (broken > 0)
            sb.AppendLine($"  ⚠ {broken} icon file(s) exist but DON'T load as Sprite — hit \"Fix icon imports\".");

        sb.AppendLine("\nUnresolved item icons (no dedicated, shared or assigned Sprite):");
        foreach (var it in items.Where(x => !HasIcon(x)).OrderBy(x => x.id))
            sb.AppendLine($"   {it.id,3}.png   {it.name}");

        // Skill icons.
        sb.AppendLine("\nSKILL ICONS:");
        foreach (var d in SkillData.GetDefaults())
        {
            string key = d.skill.ToString().ToLower();
            bool ok = Resources.Load<Sprite>($"SkillIcons/{key}") != null;
            if (!ok) sb.AppendLine($"   missing/not-a-sprite: {key}.png ({d.displayName})");
        }

        // Held models (weapons + shields).
        sb.AppendLine("\nHELD MODELS on placeholder (Resources/HeldModels/…):");
        foreach (var it in items.Where(x => x.type == ItemType.Weapon || x.type == ItemType.Shield).OrderBy(x => x.id))
        {
            if (string.IsNullOrEmpty(it.heldModel)) { sb.AppendLine($"   {it.id,3}  {it.name}  — no heldModel path"); continue; }
            if (HeldModels.Load(it.heldModel) == null)
            {
                string leaf = it.heldModel.Contains('/') ? it.heldModel[(it.heldModel.LastIndexOf('/') + 1)..] : it.heldModel;
                sb.AppendLine($"   {it.id,3}  {it.name}  — needs '{leaf}'");
            }
        }

        _report = sb.ToString();
        Repaint();
    }
}
