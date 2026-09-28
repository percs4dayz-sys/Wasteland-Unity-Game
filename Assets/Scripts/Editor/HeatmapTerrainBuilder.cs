using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

/// <summary>
/// Editor window that reads a GRAYSCALE heightmap image and builds a Unity Tilemap.
/// Each pixel's brightness (0-255) maps to a height band with a tile assigned.
/// Dark = low (water), bright = high (mountain/peak).
///
/// Usage: Wasteland → Terrain → Build from Heightmap
/// </summary>
public class HeatmapTerrainBuilder : EditorWindow
{
    // ── Input ──────────────────────────────────────────────────────────────
    private Texture2D _heightmap;
    private Tilemap _targetTilemap;

    // ── Height bands ──────────────────────────────────────────────────────
    [System.Serializable]
    public class HeightBand
    {
        public string name;
        [Range(0, 255)] public int minGray = 0;
        [Range(0, 255)] public int maxGray = 255;
        public TileBase tile;
        public bool enabled = true;
    }

    private List<HeightBand> _bands = new List<HeightBand>();
    private Vector2 _scroll;
    private bool _showBands = true;

    // Default wasteland grayscale bands
    private static readonly (string name, int min, int max)[] DefaultBands =
    {
        ("Water / Deep",       0,   40),
        ("Shallow / Shore",   41,   70),
        ("Dirt / Path",       71,  110),
        ("Grass / Ground",   111,  150),
        ("Concrete / Road",  151,  180),
        ("Stone / Rubble",   181,  210),
        ("Metal / Structure",211,  235),
        ("Peak / High",      236,  255),
    };

    [MenuItem("Wasteland/Archived/Terrain/Build from Heightmap", false, 9000)]
    public static void ShowWindow()
    {
        var win = GetWindow<HeatmapTerrainBuilder>("Heightmap → Terrain");
        win.minSize = new Vector2(440, 540);
        win.InitDefaults();
    }

    private void InitDefaults()
    {
        if (_bands.Count > 0) return;
        foreach (var (name, min, max) in DefaultBands)
            _bands.Add(new HeightBand { name = name, minGray = min, maxGray = max });
    }

    private void OnEnable() => InitDefaults();

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        GUILayout.Label("Grayscale Heightmap → Terrain", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Uses a GRAYSCALE image where each pixel's brightness (0-255)\n" +
            "maps to a terrain band. 0 = black (low/water), 255 = white (high/peak).\n\n" +
            "1. Drag your grayscale PNG/TIF here (Read/Write enabled)\n" +
            "2. Create a Tilemap: Wasteland → Terrain → Create Tilemap Setup\n" +
            "3. Assign tiles to each height band\n" +
            "4. Click BUILD",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // ── Heightmap ────────────────────────────────────────────────────
        _heightmap = (Texture2D)EditorGUILayout.ObjectField("Heightmap Image", _heightmap, typeof(Texture2D), false);
        _targetTilemap = (Tilemap)EditorGUILayout.ObjectField("Target Tilemap", _targetTilemap, typeof(Tilemap), true);

        if (_heightmap != null)
        {
            string path = AssetDatabase.GetAssetPath(_heightmap);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && !importer.isReadable)
            {
                EditorGUILayout.HelpBox(
                    "⚠ Texture not readable! Select it → enable 'Read/Write Enabled' → Apply.",
                    MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField($"Size: {_heightmap.width} × {_heightmap.height} px");
            }
        }

        // Auto-detect tilemap
        if (_targetTilemap == null)
        {
            var tm = FindAnyObjectByType<Tilemap>();
            if (tm != null) _targetTilemap = tm;
        }

        EditorGUILayout.Space(10);

        // ── Preview ──────────────────────────────────────────────────────
        if (_heightmap != null)
        {
            GUILayout.Label("Heightmap Preview", EditorStyles.boldLabel);
            Rect previewRect = GUILayoutUtility.GetRect(200, 200);
            EditorGUI.DrawPreviewTexture(previewRect, _heightmap, null, ScaleMode.ScaleToFit);
        }

        EditorGUILayout.Space(10);

        // ── Height Bands ─────────────────────────────────────────────────
        _showBands = EditorGUILayout.Foldout(_showBands, $"Height Bands ({_bands.Count})", true);
        if (_showBands)
        {
            EditorGUI.indentLevel++;

            // Draw gradient bar
            Rect gradRect = GUILayoutUtility.GetRect(0, 20);
            DrawGradientBar(gradRect);

            for (int i = 0; i < _bands.Count; i++)
            {
                var band = _bands[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                band.enabled = EditorGUILayout.Toggle(band.enabled, GUILayout.Width(18));
                band.name = EditorGUILayout.TextField(band.name, GUILayout.Width(100));

                EditorGUILayout.LabelField("Gray:", GUILayout.Width(32));
                band.minGray = EditorGUILayout.IntSlider(band.minGray, 0, 255, GUILayout.Width(100));
                band.maxGray = EditorGUILayout.IntSlider(band.maxGray, 0, 255, GUILayout.Width(100));

                band.tile = (TileBase)EditorGUILayout.ObjectField(band.tile, typeof(TileBase), false, GUILayout.Width(60));

                // Delete button
                if (GUILayout.Button("×", GUILayout.Width(22)))
                {
                    _bands.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();

                // Draw the band's gray range as a small color strip
                Rect bandRect = GUILayoutUtility.GetRect(0, 4);
                float gMin = band.minGray / 255f;
                float gMax = band.maxGray / 255f;
                EditorGUI.DrawRect(new Rect(bandRect.x + bandRect.width * gMin, bandRect.y,
                    bandRect.width * (gMax - gMin), bandRect.height), Color.gray);

                EditorGUILayout.EndVertical();
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Band"))
                _bands.Add(new HeightBand { name = "New Band", minGray = 128, maxGray = 160 });
            if (GUILayout.Button("Reset to Defaults"))
            {
                _bands.Clear();
                InitDefaults();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Auto-find tiles ─────────────────────────────────────────────
        EditorGUILayout.Space(5);
        if (GUILayout.Button("Auto-Find Tiles from Art/Tiles", GUILayout.Height(28)))
            AutoAssignTiles();

        EditorGUILayout.Space(10);

        // ── Build ────────────────────────────────────────────────────────
        GUI.enabled = _heightmap != null && _targetTilemap != null;
        if (GUILayout.Button("BUILD TERRAIN", GUILayout.Height(44)))
            BuildTerrain();
        GUI.enabled = true;

        EditorGUILayout.Space(5);
        if (GUILayout.Button("Clear Tilemap"))
        {
            if (_targetTilemap != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(_targetTilemap, "Clear Tilemap");
                _targetTilemap.ClearAllTiles();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawGradientBar(Rect r)
    {
        int steps = 64;
        float w = r.width / steps;
        for (int i = 0; i < steps; i++)
        {
            float g = i / (float)(steps - 1);
            EditorGUI.DrawRect(new Rect(r.x + i * w, r.y, w, r.height), new Color(g, g, g));
        }
    }

    // ── Auto-assign tiles by name matching ───────────────────────────────
    private void AutoAssignTiles()
    {
        string tilesPath = "Assets/Art/Tiles";
        var guids = AssetDatabase.FindAssets("t:Tile t:RuleTile", new[] { tilesPath });
        var allTiles = new List<TileBase>();
        foreach (var g in guids)
        {
            var t = AssetDatabase.LoadAssetAtPath<TileBase>(AssetDatabase.GUIDToAssetPath(g));
            if (t != null) allTiles.Add(t);
        }

        foreach (var band in _bands)
        {
            // Try to match band name keywords to tile names
            var keywords = band.name.ToLowerInvariant().Split('/', ' ');
            foreach (var kw in keywords)
            {
                var match = allTiles.Find(t => t.name.ToLowerInvariant().Contains(kw.Trim()));
                if (match != null) { band.tile = match; break; }
            }
        }

        Debug.Log($"[HeightmapBuilder] Auto-assigned from {allTiles.Count} tiles in Art/Tiles.");
    }

    // ── Build terrain from grayscale ─────────────────────────────────────
    private void BuildTerrain()
    {
        if (_heightmap == null || _targetTilemap == null)
        {
            Debug.LogError("[HeightmapBuilder] Need both a heightmap and Tilemap.");
            return;
        }

        string path = AssetDatabase.GetAssetPath(_heightmap);
        TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null && !imp.isReadable)
        {
            Debug.LogError("[HeightmapBuilder] Enable Read/Write on the texture import settings.");
            return;
        }

        Texture2D readable = GetReadableTexture(_heightmap);
        if (readable == null)
        {
            Debug.LogError("[HeightmapBuilder] Could not read texture.");
            return;
        }

        int w = readable.width;
        int h = readable.height;
        Color[] pixels = readable.GetPixels();

        Undo.RegisterFullObjectHierarchyUndo(_targetTilemap.gameObject, "Build Terrain from Heightmap");
        _targetTilemap.ClearAllTiles();

        // Pre-sort bands by min value for fast lookup
        var activeBands = new List<HeightBand>();
        foreach (var b in _bands)
            if (b.enabled && b.tile != null)
                activeBands.Add(b);

        int placed = 0, skipped = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color px = pixels[y * w + x];

                // Grayscale = average of RGB (perceptual: 0.299R + 0.587G + 0.114B)
                float gray = px.r * 0.299f + px.g * 0.587f + px.b * 0.114f;
                int gVal = Mathf.RoundToInt(gray * 255f);

                // Skip fully transparent pixels
                if (px.a < 0.1f)
                {
                    skipped++;
                    continue;
                }

                TileBase best = null;
                foreach (var band in activeBands)
                {
                    if (gVal >= band.minGray && gVal <= band.maxGray)
                    {
                        best = band.tile;
                        break;
                    }
                }

                if (best != null)
                {
                    // Flip Y so top of image = north in tilemap
                    _targetTilemap.SetTile(new Vector3Int(x, h - 1 - y, 0), best);
                    placed++;
                }
                else
                {
                    skipped++;
                }
            }
        }

        DestroyImmediate(readable);

        Debug.Log($"[HeightmapBuilder] Done! {placed} tiles placed, {skipped} skipped ({w}×{h} map).");
        EditorUtility.SetDirty(_targetTilemap.gameObject);

        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.Frame(
                new Bounds(new Vector3(w / 2f, h / 2f, 0), new Vector3(w, h, 1)), false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static Texture2D GetReadableTexture(Texture2D source)
    {
        RenderTexture rt = RenderTexture.GetTemporary(
            source.width, source.height, 0,
            RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
    }

    /// <summary>Creates a Grid + Tilemap in the scene for the heightmap tiles.</summary>
    [MenuItem("Wasteland/Archived/Terrain/Create Tilemap Setup", false, 9000)]
    public static void CreateTilemapSetup()
    {
        var existing = FindAnyObjectByType<Grid>();
        if (existing != null)
        {
            Debug.Log("[HeightmapBuilder] Grid already in scene. Skipping.");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        var gridGO = new GameObject("TerrainGrid");
        Undo.RegisterCreatedObjectUndo(gridGO, "Create Tilemap Setup");
        var grid = gridGO.AddComponent<Grid>();
        grid.cellSize = new Vector3(1, 1, 1);

        var tmGO = new GameObject("TerrainTilemap");
        Undo.RegisterCreatedObjectUndo(tmGO, "Create Tilemap Setup");
        tmGO.transform.SetParent(gridGO.transform);
        tmGO.AddComponent<Tilemap>();
        var tmr = tmGO.AddComponent<TilemapRenderer>();
        tmr.sortOrder = TilemapRenderer.SortOrder.TopLeft;
        tmr.mode = TilemapRenderer.Mode.Individual;

        // Assign the default sprite material
        var mat = AssetDatabase.LoadAssetAtPath<Material>(
            "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Materials/Sprite-Lit-Default.mat");
        if (mat != null) tmr.material = mat;

        Selection.activeGameObject = gridGO;
        Debug.Log("[HeightmapBuilder] Created Grid + Tilemap in scene.");
    }
}

