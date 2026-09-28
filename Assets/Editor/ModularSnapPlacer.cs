using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Fast modular level-building. Pick a kit, pick a piece from the palette, then LEFT-CLICK
/// in the Scene to drop it snapped to a grid. Rotate with R, cycle pieces with [ and ],
/// stop with Esc. Built for the Kenney kits in Assets/kenneynlassets (reads the FBX of each).
///
/// Menu:  Wasteland ▸ Modular Snap Placer
/// </summary>
public class ModularSnapPlacer : EditorWindow
{
    [MenuItem("Wasteland/Archived/Modular Snap Placer", false, 9000)]
    static void Open() => GetWindow<ModularSnapPlacer>("Snap Placer");

    const string KitRoot = "Assets/kenneynlassets";

    readonly List<string> _kits = new();
    readonly List<GameObject> _pieces = new();
    string _selectedKit;
    int _active = -1;
    Vector2 _scroll;

    float _grid = 1f;
    float _height = 0f;
    float _yaw = 0f;
    bool _painting;
    Transform _parent;

    void OnEnable()
    {
        RefreshKits();
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    void RefreshKits()
    {
        _kits.Clear();
        if (!Directory.Exists(KitRoot)) return;
        foreach (var d in Directory.GetDirectories(KitRoot))
            _kits.Add(Path.GetFileName(d));
        _kits.Sort();
    }

    void LoadPieces(string kit)
    {
        _selectedKit = kit;
        _active = -1;
        _pieces.Clear();
        string kitPath = $"{KitRoot}/{kit}";

        // FBX only — skip the duplicate obj/gltf/dae/stl/glb copies of every piece.
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { kitPath }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go != null) _pieces.Add(go);
        }
        _pieces.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Modular Snap Placer", EditorStyles.boldLabel);

        // ── kit picker ──
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(_selectedKit ?? "Pick a kit ▾", EditorStyles.popup))
        {
            var menu = new GenericMenu();
            foreach (var k in _kits)
            {
                string kk = k;
                menu.AddItem(new GUIContent(k), k == _selectedKit, () => LoadPieces(kk));
            }
            menu.ShowAsContext();
        }
        if (GUILayout.Button("⟳", GUILayout.Width(26))) RefreshKits();
        EditorGUILayout.EndHorizontal();

        // ── settings ──
        _grid   = EditorGUILayout.FloatField("Grid size", Mathf.Max(0.01f, _grid));
        _height = EditorGUILayout.FloatField("Place height (Y)", _height);
        _parent = (Transform)EditorGUILayout.ObjectField("Parent (optional)", _parent, typeof(Transform), true);

        EditorGUILayout.Space();
        GUI.backgroundColor = _painting ? new Color(0.5f, 1f, 0.5f) : Color.white;
        if (GUILayout.Button(_painting ? "● PAINTING — click here or Esc to stop" : "▶ Start Painting", GUILayout.Height(26)))
            _painting = !_painting;
        GUI.backgroundColor = Color.white;
        EditorGUILayout.HelpBox(
            "LEFT-CLICK in the Scene to drop the selected piece, snapped to the grid.\n" +
            "R = rotate 90°   [ ] = prev / next piece   Esc = stop   (Alt+drag still orbits)",
            MessageType.Info);

        // ── palette ──
        if (_pieces.Count == 0) { EditorGUILayout.LabelField("No pieces — pick a kit above."); return; }
        EditorGUILayout.LabelField($"Pieces: {_pieces.Count}    Selected: {(_active >= 0 ? _pieces[_active].name : "none")}    Yaw: {_yaw:0}°");

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        const int cols = 4;
        int i = 0;
        while (i < _pieces.Count)
        {
            EditorGUILayout.BeginHorizontal();
            for (int c = 0; c < cols && i < _pieces.Count; c++, i++)
            {
                var tex = AssetPreview.GetAssetPreview(_pieces[i]);
                GUI.backgroundColor = (i == _active) ? Color.cyan : Color.white;
                if (GUILayout.Button(new GUIContent(tex, _pieces[i].name), GUILayout.Width(72), GUILayout.Height(72)))
                    _active = i;
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();   // thumbnails stream in
    }

    void OnSceneGUI(SceneView sv)
    {
        if (!_painting || _active < 0 || _active >= _pieces.Count) return;

        var e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive)); // absorb clicks

        // snapped point under the cursor on the placement plane
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        var plane = new Plane(Vector3.up, new Vector3(0f, _height, 0f));
        bool valid = plane.Raycast(ray, out float dist);
        Vector3 pos = Vector3.zero;
        if (valid)
        {
            pos = ray.GetPoint(dist);
            pos.x = Mathf.Round(pos.x / _grid) * _grid;
            pos.z = Mathf.Round(pos.z / _grid) * _grid;
            pos.y = _height;
            Handles.color = Color.cyan;
            Handles.DrawWireCube(pos + Vector3.up * 0.05f, new Vector3(_grid, 0.1f, _grid));
            Handles.ArrowHandleCap(0, pos + Vector3.up * 0.1f, Quaternion.Euler(0f, _yaw, 0f), _grid * 0.5f, EventType.Repaint);
        }

        if (e.type == EventType.KeyDown)
        {
            switch (e.keyCode)
            {
                case KeyCode.R:            _yaw = (_yaw + 90f) % 360f; e.Use(); break;
                case KeyCode.LeftBracket:  _active = (_active - 1 + _pieces.Count) % _pieces.Count; e.Use(); Repaint(); break;
                case KeyCode.RightBracket: _active = (_active + 1) % _pieces.Count; e.Use(); Repaint(); break;
                case KeyCode.Escape:       _painting = false; e.Use(); Repaint(); break;
            }
        }

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && valid)
        {
            Place(pos);
            e.Use();
        }

        sv.Repaint();   // keep the ghost following the cursor
    }

    void Place(Vector3 pos)
    {
        var model = _pieces[_active];
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, _yaw, 0f));
        if (_parent != null) go.transform.SetParent(_parent, true);
        Undo.RegisterCreatedObjectUndo(go, "Place " + model.name);
        Selection.activeGameObject = go;
    }
}
