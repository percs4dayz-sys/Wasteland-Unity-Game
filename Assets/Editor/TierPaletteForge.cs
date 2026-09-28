using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates the five tier gear palettes, RuneScape-style.
///
/// Synty's ColorMap is a 32x32 swatch atlas — really a 16x16 grid of flat 2x2 colour blocks that
/// every mesh's UVs point at. So recolouring an entire armour tier means editing a handful of
/// swatches, not touching a single mesh. Change one block and every piece using that material
/// recolours at once, across all 25 slots.
///
/// Pick which swatches are "metal" (or leather, or trim), choose a colour per tier, and this
/// writes five ColorMap PNGs plus a material for each. Equipping tier-3 gear then means assigning
/// the tier-3 material — one line in EquipmentVisuals.
///
/// Hue and saturation come from the tier colour; each swatch keeps its own VALUE, so shading and
/// highlight variation inside the armour survives instead of flattening to one colour.
///
/// Menu:  Wasteland ▸ Gear ▸ Tier Palette Forge
/// </summary>
public class TierPaletteForge : EditorWindow
{
    const int Grid = 16;          // 16x16 swatches
    const int Block = 2;          // each swatch is 2x2 pixels in the 32x32 map
    const string OutDir = "Assets/Art/Generated3D/TierPalettes";

    Texture2D _base;
    Material _baseMaterial;
    readonly HashSet<int> _selected = new HashSet<int>();
    Color[] _swatches;
    Vector2 _scroll;

    // Guards against re-running the import. OnGUI fires many times a second, so anything that
    // calls SaveAndReimport() from it will loop forever and take the machine with it.
    Texture2D _loadedFrom;
    string _loadError;

    // Bronze → iron → steel → mithril → rune. Tuned to read as a progression at a glance.
    string[] _tierNames = { "T1_Bronze", "T2_Iron", "T3_Steel", "T4_Mithril", "T5_Rune" };
    Color[] _tierColors =
    {
        new Color(0.55f, 0.35f, 0.16f),   // bronze — warm brown
        new Color(0.38f, 0.38f, 0.40f),   // iron — dull grey
        new Color(0.62f, 0.66f, 0.72f),   // steel — bright cool grey
        new Color(0.42f, 0.62f, 0.82f),   // mithril — blue
        new Color(0.20f, 0.72f, 0.66f),   // rune — cyan-teal
    };

    [MenuItem("Wasteland/Gear/Tier Palette Forge", false, 0)]
    static void Open() => GetWindow<TierPaletteForge>("Tier Palettes").minSize = new Vector2(520, 640);

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.HelpBox(
            "1. Drop in the base ColorMap (T_Starter_01ColorMap).\n" +
            "2. Click the swatches that are ARMOUR METAL — those get recoloured per tier.\n" +
            "   Leave skin, eyes and hair unselected or your character changes race per tier.\n" +
            "3. Set a colour per tier, then Generate.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        _base = (Texture2D)EditorGUILayout.ObjectField("Base ColorMap", _base, typeof(Texture2D), false);
        if (EditorGUI.EndChangeCheck()) LoadSwatches();

        _baseMaterial = (Material)EditorGUILayout.ObjectField("Base Material (optional)",
                                                             _baseMaterial, typeof(Material), false);

        if (_base == null)
        {
            if (GUILayout.Button("Find Sidekick's Starter ColorMap automatically", GUILayout.Height(26)))
                AutoFind();
            EditorGUILayout.EndScrollView();
            return;
        }

        // NEVER load from OnGUI — it repaints constantly and LoadSwatches touches the importer.
        if (_swatches == null)
        {
            EditorGUILayout.HelpBox(
                _loadError ?? "Swatches not loaded yet.", MessageType.Warning);
            if (GUILayout.Button("Load swatches from this texture", GUILayout.Height(28)))
                LoadSwatches();
            EditorGUILayout.EndScrollView();
            return;
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"Swatch grid — {_selected.Count} selected", EditorStyles.boldLabel);
        DrawSwatchGrid();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select all greys (likely metal)")) SelectGreys();
        if (GUILayout.Button("Clear selection")) _selected.Clear();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Tiers", EditorStyles.boldLabel);
        for (int i = 0; i < _tierNames.Length; i++)
        {
            EditorGUILayout.BeginHorizontal();
            _tierNames[i] = EditorGUILayout.TextField(_tierNames[i], GUILayout.Width(120));
            _tierColors[i] = EditorGUILayout.ColorField(_tierColors[i]);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(10);
        using (new EditorGUI.DisabledScope(_selected.Count == 0))
            if (GUILayout.Button($"Generate {_tierNames.Length} palettes" +
                                 (_baseMaterial != null ? " + materials" : ""), GUILayout.Height(34)))
                Generate();

        if (_selected.Count == 0)
            EditorGUILayout.HelpBox("Select at least one swatch first.", MessageType.Warning);

        EditorGUILayout.EndScrollView();
    }

    // ── swatches ─────────────────────────────────────────────────────────────

    void AutoFind()
    {
        foreach (string guid in AssetDatabase.FindAssets("T_Starter_01ColorMap t:Texture2D"))
        {
            _base = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            if (_base != null) break;
        }
        foreach (string guid in AssetDatabase.FindAssets("Starter_01 t:Material"))
        {
            _baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (_baseMaterial != null) break;
        }
        LoadSwatches();
    }

    /// <summary>
    /// Explicit, user-triggered only. Reimports the source texture if it isn't readable, which is
    /// why this must never be called from OnGUI — _loadedFrom records the attempt so a failure
    /// can't retry on the next repaint.
    /// </summary>
    void LoadSwatches()
    {
        _swatches = null;
        _selected.Clear();
        _loadError = null;
        _loadedFrom = _base;

        if (_base == null) { _loadError = "No texture assigned."; return; }

        string path = AssetDatabase.GetAssetPath(_base);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) { _loadError = $"'{_base.name}' has no TextureImporter."; return; }

        if (!imp.isReadable || imp.textureCompression != TextureImporterCompression.Uncompressed)
        {
            imp.isReadable = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();                       // once, on demand
            _base = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (_base == null) { _loadError = "Reimport failed."; return; }
        }

        if (_base.width < Grid * Block || _base.height < Grid * Block)
        {
            _loadError = $"Expected at least {Grid * Block}x{Grid * Block}, got {_base.width}x{_base.height}.";
            return;
        }

        try
        {
            var px = new Color[Grid * Grid];
            for (int y = 0; y < Grid; y++)
                for (int x = 0; x < Grid; x++)
                    px[y * Grid + x] = _base.GetPixel(x * Block, y * Block);
            _swatches = px;                              // only assign once it fully succeeded
        }
        catch (UnityException e)
        {
            _loadError = "Texture still not readable: " + e.Message;
        }
    }

    void DrawSwatchGrid()
    {
        const float cell = 26f;
        var rect = GUILayoutUtility.GetRect(Grid * cell, Grid * cell);
        for (int y = 0; y < Grid; y++)
        {
            for (int x = 0; x < Grid; x++)
            {
                int i = y * Grid + x;
                var r = new Rect(rect.x + x * cell, rect.y + y * cell, cell - 2, cell - 2);
                EditorGUI.DrawRect(r, _swatches[i]);
                if (_selected.Contains(i))
                {
                    // white ring so a selected dark swatch is still obviously selected
                    EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2), Color.white);
                    EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), Color.white);
                    EditorGUI.DrawRect(new Rect(r.x, r.y, 2, r.height), Color.white);
                    EditorGUI.DrawRect(new Rect(r.xMax - 2, r.y, 2, r.height), Color.white);
                }
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    if (_selected.Contains(i)) _selected.Remove(i); else _selected.Add(i);
                    Event.current.Use();
                    Repaint();
                }
            }
        }
    }

    /// <summary>Low-saturation swatches are almost always the metal/armour ones.</summary>
    void SelectGreys()
    {
        _selected.Clear();
        for (int i = 0; i < _swatches.Length; i++)
        {
            Color.RGBToHSV(_swatches[i], out _, out float s, out float v);
            if (s < 0.22f && v > 0.10f && v < 0.95f) _selected.Add(i);
        }
    }

    // ── generation ───────────────────────────────────────────────────────────

    void Generate()
    {
        EnsureFolder(OutDir);
        var log = new StringBuilder("=== Tier Palette Forge ===\n");
        log.AppendLine($"base: {AssetDatabase.GetAssetPath(_base)}   {_selected.Count} swatch(es) recoloured\n");

        // Value range across the selected swatches, so each tier keeps the same light/dark spread.
        float lo = 1f, hi = 0f;
        foreach (int i in _selected)
        {
            Color.RGBToHSV(_swatches[i], out _, out _, out float v);
            lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
        }
        if (hi - lo < 0.01f) { lo = 0.25f; hi = 0.75f; }

        var written = new List<string>();
        for (int t = 0; t < _tierNames.Length; t++)
        {
            var tex = new Texture2D(_base.width, _base.height, TextureFormat.RGBA32, false);
            tex.SetPixels(_base.GetPixels());

            Color.RGBToHSV(_tierColors[t], out float th, out float ts, out float tv);

            foreach (int i in _selected)
            {
                int sx = (i % Grid) * Block, sy = (i / Grid) * Block;
                Color.RGBToHSV(_swatches[i], out _, out _, out float v);

                // keep this swatch's position in the light/dark range, take the tier's hue
                float k = Mathf.InverseLerp(lo, hi, v);
                float outV = Mathf.Clamp01(tv * Mathf.Lerp(0.55f, 1.35f, k));
                Color c = Color.HSVToRGB(th, ts, outV);

                for (int dy = 0; dy < Block; dy++)
                    for (int dx = 0; dx < Block; dx++)
                        tex.SetPixel(sx + dx, sy + dy, c);
            }
            tex.Apply();

            string png = $"{OutDir}/T_{_tierNames[t]}_ColorMap.png";
            File.WriteAllBytes(Path.Combine(Application.dataPath, "..", png), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            written.Add(png);

            log.AppendLine($"   {_tierNames[t],-12} -> {png}");

        }

        // Import all five in ONE batch. Reimporting inside the loop meant five synchronous
        // reimports back to back, which is what was hammering the machine.
        AssetDatabase.Refresh();
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (string png in written)
            {
                // Palettes are flat colour lookups — filtering or mips bleed swatches together.
                var imp = AssetImporter.GetAtPath(png) as TextureImporter;
                if (imp == null) continue;
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }

        // Materials last, once their textures exist and are imported.
        if (_baseMaterial != null)
        {
            string srcMat = AssetDatabase.GetAssetPath(_baseMaterial);
            for (int t = 0; t < _tierNames.Length; t++)
            {
                string matPath = $"{OutDir}/M_{_tierNames[t]}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(matPath) == null)
                    AssetDatabase.CopyAsset(srcMat, matPath);

                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                var newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(written[t]);
                if (mat == null || newTex == null) continue;
                if (mat.HasProperty("_ColorMap")) mat.SetTexture("_ColorMap", newTex);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", newTex);
                EditorUtility.SetDirty(mat);
                log.AppendLine($"   material {matPath}");
            }
        }

        AssetDatabase.SaveAssets();
        log.AppendLine($"\nDone. Assign M_<tier> to gear meshes to switch a whole tier's look.");
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Tier Palettes",
            $"{_tierNames.Length} palettes written to\n{OutDir}\n\nSee Console for the list.", "OK");
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(OutDir);
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
