using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sandbox Asset Placer  —  Wasteland ▸ World ▸ Sandbox Asset Placer
///
/// A browse-and-place window with a live, interactive 3D viewer:
///   • Left  : a searchable palette of every prefab / model in a chosen folder (thumbnails).
///   • Right : a real 3D preview of the selected asset. Left-drag to spin, scroll to zoom,
///             middle-drag (or Alt+left-drag) to pan.
///   • Drop into the scene by dragging a palette tile OR the "Drag into Scene" bar under the
///     viewer — Unity places it wherever you release, same as dragging from the Project window.
///   • Or hit "Place in Scene" to drop it at the Scene view's focus point.
///
/// Uses PreviewRenderUtility so the viewer is a genuine lit render you can orbit, not a flat icon.
/// </summary>
public class SandboxAssetPlacer : EditorWindow
{
    [MenuItem("Wasteland/World/Sandbox Asset Placer", false, 50)]
    public static void Open()
    {
        var w = GetWindow<SandboxAssetPlacer>("Asset Placer");
        w.minSize = new Vector2(720, 460);
        w.Show();
    }

    // ---- palette state ----
    const string PrefKeyFolder = "SandboxAssetPlacer.Folder";
    string _searchFolder = "Assets";
    string _search = "";
    bool _includePrefabs = true;
    bool _includeModels = true;
    readonly List<string> _guids = new List<string>();   // filtered asset guids
    string _selectedGuid;
    Object _selectedAsset;
    bool _previewTooBig;
    Vector2 _paletteScroll;
    bool _dirtyList = true;

    // Assets above this are dragged/placed but NOT instantiated into the live 3D preview.
    const long PreviewMaxBytes = 128L * 1024 * 1024;

    // ---- viewer state ----
    PreviewRenderUtility _pru;
    GameObject _previewInstance;     // instantiated copy living in the preview scene
    Bounds _previewBounds;
    float _yaw = 130f, _pitch = 18f, _distance = 4f;
    Vector3 _pivot = Vector3.zero;

    // ---- drag tracking ----
    Vector2 _mouseDownPos;
    bool _pendingPaletteDrag;

    const float PaletteWidth = 260f;
    const float TileSize = 76f;

    void OnEnable()
    {
        _searchFolder = EditorPrefs.GetString(PrefKeyFolder, "Assets");
        // Hard-bound the global asset-preview texture cache so browsing can't balloon memory.
        AssetPreview.SetPreviewTextureCacheSize(128);
        _pru = new PreviewRenderUtility();
        _pru.camera.fieldOfView = 30f;
        _pru.camera.nearClipPlane = 0.01f;
        _pru.camera.farClipPlane = 5000f;
        _pru.lights[0].intensity = 1.2f;
        _pru.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
        if (_pru.lights.Length > 1)
        {
            _pru.lights[1].intensity = 0.9f;
            _pru.lights[1].transform.rotation = Quaternion.Euler(-20f, -120f, 0f);
        }
        _dirtyList = true;
    }

    void OnDisable()
    {
        DestroyPreviewInstance();
        _pru?.Cleanup();
        _pru = null;
    }

    // =========================================================================================
    //  GUI
    // =========================================================================================
    void OnGUI()
    {
        if (_dirtyList) RebuildList();

        DrawToolbar();

        var body = new Rect(0, EditorStyles.toolbar.fixedHeight, position.width, position.height - EditorStyles.toolbar.fixedHeight);
        GUILayout.BeginArea(body);
        EditorGUILayout.BeginHorizontal();

        DrawPalette();
        DrawViewer();

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();

        // Keep repainting while thumbnails stream in.
        if (AssetPreview.IsLoadingAssetPreviews())
            Repaint();
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("Folder", EditorStyles.miniLabel, GUILayout.Width(40));
        var newFolder = EditorGUILayout.TextField(_searchFolder, EditorStyles.toolbarTextField, GUILayout.Width(220));
        if (newFolder != _searchFolder) { _searchFolder = newFolder; _dirtyList = true; }

        if (GUILayout.Button("Pick…", EditorStyles.toolbarButton, GUILayout.Width(48)))
        {
            string abs = EditorUtility.OpenFolderPanel("Folder to browse", _searchFolder, "");
            if (!string.IsNullOrEmpty(abs))
            {
                string rel = ToProjectRelative(abs);
                if (rel != null) { _searchFolder = rel; _dirtyList = true; }
                else EditorUtility.DisplayDialog("Sandbox Asset Placer", "Please pick a folder inside this project's Assets/ folder.", "OK");
            }
        }

        GUILayout.Space(8);
        bool p = GUILayout.Toggle(_includePrefabs, "Prefabs", EditorStyles.toolbarButton, GUILayout.Width(64));
        bool m = GUILayout.Toggle(_includeModels, "Models", EditorStyles.toolbarButton, GUILayout.Width(60));
        if (p != _includePrefabs || m != _includeModels) { _includePrefabs = p; _includeModels = m; _dirtyList = true; }

        GUILayout.FlexibleSpace();

        GUILayout.Label(_guids.Count + " items", EditorStyles.miniLabel);
        GUI.SetNextControlName("Search");
        var s = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(180));
        if (s != _search) { _search = s; _dirtyList = true; }

        EditorGUILayout.EndHorizontal();
    }

    // -----------------------------------------------------------------------------------------
    //  Palette (left)
    // -----------------------------------------------------------------------------------------
    // Assets bigger than this are never loaded just to make a thumbnail — we show a cheap type icon
    // (via GetCachedIcon, which reads the asset DB without loading the mesh). This is the single most
    // important guard against memory-spike crashes when a folder contains huge vendor meshes.
    const long HugeAssetBytes = 48L * 1024 * 1024;

    void DrawPalette()
    {
        float w = PaletteWidth;
        var outer = GUILayoutUtility.GetRect(w, 4000, GUILayout.Width(w), GUILayout.ExpandHeight(true));

        if (_guids.Count == 0)
        {
            GUI.Label(outer, "Nothing found.\nCheck the folder path and the\nPrefabs / Models toggles.",
                EditorStyles.centeredGreyMiniLabel);
            return;
        }

        int cols = Mathf.Max(1, Mathf.FloorToInt((w - 18) / (TileSize + 6)));
        float rowH = TileSize + 6;
        int rows = Mathf.CeilToInt(_guids.Count / (float)cols);
        var content = new Rect(0, 0, w - 18, rows * rowH + 4);

        _paletteScroll = GUI.BeginScrollView(outer, _paletteScroll, content);

        // ── VIEWPORT CULLING ── only the rows actually on screen get drawn, and only those tiles ever
        // request an asset preview. Off-screen tiles cost nothing, so a 10,000-item folder is safe.
        int firstRow = Mathf.Max(0, Mathf.FloorToInt(_paletteScroll.y / rowH) - 1);
        int lastRow  = Mathf.Min(rows - 1, Mathf.CeilToInt((_paletteScroll.y + outer.height) / rowH) + 1);

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int c = 0; c < cols; c++)
            {
                int idx = row * cols + c;
                if (idx >= _guids.Count) break;
                var tileRect = new Rect(4 + c * (TileSize + 6), 2 + row * rowH, TileSize, TileSize);
                DrawTile(_guids[idx], tileRect);
            }
        }

        GUI.EndScrollView();
    }

    void DrawTile(string guid, Rect rect)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);

        if (guid == _selectedGuid)
            EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.90f, 0.35f));

        // Choose the cheapest thumbnail that's safe: a rendered preview for normal assets, but only a
        // type icon for huge ones (GetCachedIcon never loads the asset). Previews for the ~dozen
        // visible tiles are bounded by the 128-texture cache set in OnEnable.
        Texture thumb;
        if (FileSize(path) > HugeAssetBytes)
        {
            thumb = AssetDatabase.GetCachedIcon(path);
        }
        else
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            thumb = AssetPreview.GetAssetPreview(obj) ?? AssetDatabase.GetCachedIcon(path);
        }
        if (thumb != null)
            GUI.DrawTexture(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), thumb, ScaleMode.ScaleToFit);

        GUI.Label(new Rect(rect.x, rect.yMax - 15, rect.width, 14),
            Path.GetFileNameWithoutExtension(path), EditorStyles.centeredGreyMiniLabel);

        HandleTileInput(rect, guid, path);
    }

    // Load the actual asset object ONLY when the user interacts with a tile — never just to draw it.
    void HandleTileInput(Rect rect, string guid, string path)
    {
        var e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0:
                Select(guid, AssetDatabase.LoadMainAssetAtPath(path));
                _mouseDownPos = e.mousePosition;
                _pendingPaletteDrag = true;
                Repaint();
                break;

            case EventType.MouseDrag when _pendingPaletteDrag && e.button == 0:
                if (Vector2.Distance(e.mousePosition, _mouseDownPos) > 6f)
                {
                    StartSceneDrag(AssetDatabase.LoadMainAssetAtPath(path));
                    _pendingPaletteDrag = false;
                    e.Use();
                }
                break;

            case EventType.MouseUp:
                _pendingPaletteDrag = false;
                break;
        }
    }

    static long FileSize(string projectPath)
    {
        try { return new FileInfo(projectPath).Length; } catch { return 0; }
    }

    // -----------------------------------------------------------------------------------------
    //  3D Viewer (right)
    // -----------------------------------------------------------------------------------------
    void DrawViewer()
    {
        EditorGUILayout.BeginVertical();

        var viewRect = GUILayoutUtility.GetRect(200, 4000, 200, 4000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        if (_selectedAsset == null)
        {
            EditorGUI.DrawRect(viewRect, new Color(0.15f, 0.15f, 0.15f));
            var s = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 };
            GUI.Label(viewRect, "Select an asset from the palette to preview it here.\nLeft-drag to spin • scroll to zoom • Alt/middle-drag to pan", s);
        }
        else if (_previewTooBig)
        {
            EditorGUI.DrawRect(viewRect, new Color(0.15f, 0.15f, 0.15f));
            var s = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12, wordWrap = true };
            GUI.Label(viewRect, $"“{_selectedAsset.name}” is very large — live preview skipped to protect memory.\n\nYou can still drag it into the scene or use Place in Scene.", s);
        }
        else
        {
            HandleViewerInput(viewRect);
            RenderPreview(viewRect);
        }

        // --- drag bar + place button ---
        EditorGUILayout.BeginHorizontal(GUILayout.Height(26));
        DrawDragBar();
        using (new EditorGUI.DisabledScope(_selectedAsset == null))
        {
            if (GUILayout.Button("Place in Scene", GUILayout.Width(120), GUILayout.Height(24)))
                PlaceInScene(_selectedAsset as GameObject);
            if (GUILayout.Button("Frame", GUILayout.Width(60), GUILayout.Height(24)))
                FrameSelection();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    void DrawDragBar()
    {
        var bar = GUILayoutUtility.GetRect(100, 4000, 24, 24, GUILayout.ExpandWidth(true));
        var col = _selectedAsset == null ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.24f, 0.40f, 0.62f);
        EditorGUI.DrawRect(bar, col);
        var barLabel = new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        GUI.Label(bar, _selectedAsset == null ? "—" : "⤵  Drag into Scene", barLabel);

        if (_selectedAsset == null) return;
        var e = Event.current;
        if (bar.Contains(e.mousePosition))
        {
            EditorGUIUtility.AddCursorRect(bar, MouseCursor.Pan);
            if (e.type == EventType.MouseDown && e.button == 0) { _mouseDownPos = e.mousePosition; e.Use(); }
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                StartSceneDrag(_selectedAsset);
                e.Use();
            }
        }
    }

    void HandleViewerInput(Rect rect)
    {
        var e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        switch (e.type)
        {
            case EventType.MouseDrag:
                bool pan = e.button == 2 || (e.button == 0 && e.alt);
                if (pan)
                {
                    float s = _distance * 0.0015f + 0.001f;
                    _pivot += _pru.camera.transform.right * -e.delta.x * s;
                    _pivot += _pru.camera.transform.up * e.delta.y * s;
                }
                else if (e.button == 0)
                {
                    _yaw += e.delta.x * 0.5f;
                    _pitch = Mathf.Clamp(_pitch + e.delta.y * 0.5f, -89f, 89f);
                }
                e.Use();
                Repaint();
                break;

            case EventType.ScrollWheel:
                _distance = Mathf.Clamp(_distance * (1f + e.delta.y * 0.05f), 0.05f, 5000f);
                e.Use();
                Repaint();
                break;
        }
    }

    void RenderPreview(Rect rect)
    {
        if (Event.current.type != EventType.Repaint) { GUI.DrawTexture(rect, Texture2D.blackTexture); return; }

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0);
        var cam = _pru.camera;
        cam.transform.rotation = rot;
        cam.transform.position = _pivot - (rot * Vector3.forward) * _distance;

        _pru.BeginPreview(rect, GUIStyle.none);
        _pru.Render(true, true);   // 2nd arg = allow URP so materials render correctly
        Texture tex = _pru.EndPreview();
        GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
    }

    // =========================================================================================
    //  Selection / preview instance
    // =========================================================================================
    void Select(string guid, Object obj)
    {
        _selectedGuid = guid;
        _selectedAsset = obj;
        _previewTooBig = FileSize(AssetDatabase.GUIDToAssetPath(guid)) > PreviewMaxBytes;
        if (_previewTooBig) DestroyPreviewInstance();
        else BuildPreviewInstance(obj as GameObject);
    }

    void BuildPreviewInstance(GameObject prefab)
    {
        DestroyPreviewInstance();
        if (prefab == null) return;

        _previewInstance = Object.Instantiate(prefab);
        _previewInstance.hideFlags = HideFlags.HideAndDontSave;
        // Strip behaviours that might error in the preview scene; we only want the visuals.
        SetLayerRecursive(_previewInstance, 0);
        _pru.AddSingleGO(_previewInstance);

        _previewBounds = ComputeBounds(_previewInstance);
        FrameSelection();
    }

    void FrameSelection()
    {
        _pivot = _previewBounds.center;
        float radius = Mathf.Max(_previewBounds.extents.magnitude, 0.05f);
        // fit sphere of 'radius' into the 30° vertical fov
        _distance = radius / Mathf.Sin(Mathf.Deg2Rad * (_pru.camera.fieldOfView * 0.5f)) * 1.1f;
        _pru.camera.farClipPlane = _distance * 10f + radius * 4f;
        _pru.camera.nearClipPlane = Mathf.Max(0.01f, _distance * 0.01f);
        Repaint();
    }

    void DestroyPreviewInstance()
    {
        if (_previewInstance != null)
        {
            Object.DestroyImmediate(_previewInstance);
            _previewInstance = null;
        }
    }

    // =========================================================================================
    //  Scene placement / drag
    // =========================================================================================
    static void StartSceneDrag(Object asset)
    {
        if (asset == null) return;
        DragAndDrop.PrepareStartDrag();
        DragAndDrop.objectReferences = new[] { asset };
        DragAndDrop.paths = new[] { AssetDatabase.GetAssetPath(asset) };
        DragAndDrop.StartDrag(asset.name);
    }

    static void PlaceInScene(GameObject prefab)
    {
        if (prefab == null) return;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (go == null) return;
        Undo.RegisterCreatedObjectUndo(go, "Place " + prefab.name);

        var sv = SceneView.lastActiveSceneView;
        go.transform.position = sv != null ? sv.pivot : Vector3.zero;
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    // =========================================================================================
    //  Asset list building
    // =========================================================================================
    void RebuildList()
    {
        _dirtyList = false;
        _guids.Clear();
        EditorPrefs.SetString(PrefKeyFolder, _searchFolder);

        string folder = string.IsNullOrEmpty(_searchFolder) ? "Assets" : _searchFolder.Replace('\\', '/').TrimEnd('/');
        if (!AssetDatabase.IsValidFolder(folder)) folder = "Assets";
        var roots = new[] { folder };

        var seen = new HashSet<string>();
        void Add(string filter)
        {
            foreach (var g in AssetDatabase.FindAssets(filter, roots))
                if (seen.Add(g)) _guids.Add(g);
        }

        string q = string.IsNullOrWhiteSpace(_search) ? "" : _search.Trim();
        if (_includePrefabs) Add(("t:Prefab " + q).Trim());
        if (_includeModels)  Add(("t:Model "  + q).Trim());

        // Stable, human-friendly order by filename.
        _guids.Sort((a, b) => string.Compare(
            Path.GetFileName(AssetDatabase.GUIDToAssetPath(a)),
            Path.GetFileName(AssetDatabase.GUIDToAssetPath(b)),
            System.StringComparison.OrdinalIgnoreCase));
    }

    // =========================================================================================
    //  Helpers
    // =========================================================================================
    static Bounds ComputeBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    static string ToProjectRelative(string absolute)
    {
        absolute = absolute.Replace('\\', '/');
        string dataPath = Application.dataPath.Replace('\\', '/'); // ".../Assets"
        if (absolute == dataPath) return "Assets";
        if (absolute.StartsWith(dataPath + "/")) return "Assets" + absolute.Substring(dataPath.Length);
        return null;
    }
}
