using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Paint-scatter prefabs onto the mesh world (Unity's built-in tree/detail painting only works on
/// Unity Terrain, not meshes — this works on your GLB ground). Drag in the Scene view to scatter
/// foliage/rocks; they raycast onto the surface with random yaw/scale so it looks natural.
///
/// Node-aware: tick "Make gather nodes" and each painted object becomes a ResourceNode with the
/// tier fields below — so you paint tier-1 trees, bump the level/drop, paint tier-2, and so on.
/// (WoodenDebris→Woodcutting, RubblePile→Scrapping, WaterBarrel→Fishing, per World3DBuilder.)
///
/// Open:  Wasteland ▸ World ▸ World Scatter Brush
/// </summary>
public class WorldScatterBrush : EditorWindow
{
    [MenuItem("Wasteland/World/World Scatter Brush")]
    public static void Open() => GetWindow<WorldScatterBrush>("Scatter Brush");

    // Palette — chosen prefabs are picked from a folder dropdown (not the whole project).
    readonly List<GameObject> _prefabs = new();
    string _sourceFolder = WastelandPaths.Root + "/Nodes";
    int _pickIndex;
    float  _brushRadius = 8f;
    int    _perDab      = 3;
    float  _spacing     = 5f;   // min distance to move before the brush drops again (stops clumping)
    Vector2 _scale      = new(0.8f, 1.3f);
    bool   _randomYaw   = true;
    bool   _alignNormal = false;
    string _groupName   = "Scatter";

    // Auto-fill
    int _autoFillCount = 300;

    // Node options
    bool _makeNodes = false;
    ResourceNodeType _nodeType = ResourceNodeType.WoodenDebris;
    Skill _skill = Skill.Woodcutting;
    int _levelRequired = 1, _toolId = 4, _dropId = 40, _xp = 25, _maxHits = 3, _respawn = 10;

    bool _painting;
    Vector3 _lastDab; bool _hasLast;

    // Positions of objects already placed, so Spacing acts as a real min-gap between OBJECTS
    // (not just between brush centers) — stops them landing on top of each other.
    readonly List<Vector3> _placed = new();

    void OnEnable()  => SceneView.duringSceneGui += OnScene;
    void OnDisable() => SceneView.duringSceneGui -= OnScene;

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "1) Add prefab(s) below.  2) Click 'Start Painting'.  3) Drag on the ground in the Scene view.\n" +
            "Hold the mouse and drag to scatter. Press Start Painting again (or close) to stop.",
            MessageType.Info);

        // Prefab palette — choose from a folder, not the whole project.
        EditorGUILayout.LabelField("Prefabs to scatter (random among the chosen)", EditorStyles.boldLabel);
        _sourceFolder = EditorGUILayout.TextField("From Folder", _sourceFolder);

        var guids = AssetDatabase.IsValidFolder(_sourceFolder)
            ? AssetDatabase.FindAssets("t:Prefab", new[] { _sourceFolder })
            : new string[0];
        var names = new string[guids.Length];
        var objs  = new GameObject[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            string p = AssetDatabase.GUIDToAssetPath(guids[i]);
            objs[i]  = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            names[i] = System.IO.Path.GetFileNameWithoutExtension(p);
        }

        if (names.Length == 0)
            EditorGUILayout.HelpBox("No prefabs in that folder. Default is your node prefabs " +
                "(run 'Create Tiered Node Prefabs' first), or point it at a foliage folder.", MessageType.Warning);
        else
        {
            _pickIndex = Mathf.Clamp(_pickIndex, 0, names.Length - 1);
            EditorGUILayout.BeginHorizontal();
            _pickIndex = EditorGUILayout.Popup("Pick", _pickIndex, names);
            if (GUILayout.Button("Add", GUILayout.Width(50)) && !_prefabs.Contains(objs[_pickIndex]))
                _prefabs.Add(objs[_pickIndex]);
            if (GUILayout.Button("Add All", GUILayout.Width(70)))
                foreach (var o in objs) if (!_prefabs.Contains(o)) _prefabs.Add(o);
            EditorGUILayout.EndHorizontal();
        }

        // Currently chosen (paint these)
        for (int i = _prefabs.Count - 1; i >= 0; i--)
        {
            if (_prefabs[i] == null) { _prefabs.RemoveAt(i); continue; }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("•  " + _prefabs[i].name);
            if (GUILayout.Button("–", GUILayout.Width(24))) _prefabs.RemoveAt(i);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        _brushRadius = EditorGUILayout.Slider("Brush Radius", _brushRadius, 1f, 60f);
        _perDab      = EditorGUILayout.IntSlider("Per Dab", _perDab, 1, 40);
        _spacing     = EditorGUILayout.Slider("Spacing (min gap between objects)", _spacing, 0.5f, 40f);
        EditorGUILayout.MinMaxSlider(new GUIContent("Scale Range"), ref _scale.x, ref _scale.y, 0.1f, 20f);
        EditorGUILayout.LabelField("   scale", _scale.x.ToString("0.00") + " – " + _scale.y.ToString("0.00"));
        _randomYaw   = EditorGUILayout.Toggle("Random Y Rotation", _randomYaw);
        _alignNormal = EditorGUILayout.Toggle("Align To Slope", _alignNormal);
        _groupName   = EditorGUILayout.TextField("Group Under", _groupName);

        EditorGUILayout.Space();
        _makeNodes = EditorGUILayout.BeginToggleGroup("Make gather nodes — RAW models only (already-built node prefabs keep their own settings)", _makeNodes);
        _nodeType      = (ResourceNodeType)EditorGUILayout.EnumPopup("Node Type", _nodeType);
        _skill         = (Skill)EditorGUILayout.EnumPopup("Skill", _skill);
        _levelRequired = EditorGUILayout.IntField("Level Required (tier)", _levelRequired);
        _toolId        = EditorGUILayout.IntField("Required Tool Id", _toolId);
        _dropId        = EditorGUILayout.IntField("Drop Item Id", _dropId);
        _xp            = EditorGUILayout.IntField("XP Per Action", _xp);
        _maxHits       = EditorGUILayout.IntField("Max Hits", _maxHits);
        _respawn       = EditorGUILayout.IntField("Respawn Ticks", _respawn);
        EditorGUILayout.EndToggleGroup();

        EditorGUILayout.Space();
        GUI.backgroundColor = _painting ? Color.green : Color.white;
        if (GUILayout.Button(_painting ? "● PAINTING — click to stop" : "Start Painting", GUILayout.Height(30)))
        {
            _painting = !_painting;
            if (_painting) RebuildPlaced();   // honour the min-gap against already-painted objects
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rough-fill the whole map at once:", EditorStyles.boldLabel);
        _autoFillCount = EditorGUILayout.IntField("Auto-Fill Count", _autoFillCount);
        if (GUILayout.Button("Auto-Fill Across 'Ground'")) AutoFill();
    }

    void OnScene(SceneView sv)
    {
        if (!_painting) return;
        Event e = Event.current;

        // Keep the scene view from selecting/deselecting while we paint.
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out var hit, 10000f))
        {
            Handles.color = new Color(0.3f, 1f, 0.4f, 1f);
            Handles.DrawWireDisc(hit.point, hit.normal, _brushRadius);
            sv.Repaint();

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
            {
                if (e.type == EventType.MouseDown) _hasLast = false;        // first click always drops
                if (!_hasLast || Vector3.Distance(hit.point, _lastDab) >= _spacing)
                {
                    Dab(hit.point);
                    _lastDab = hit.point;
                    _hasLast = true;
                }
                e.Use();
            }
        }
    }

    void Dab(Vector3 center)
    {
        var live = _prefabs.FindAll(p => p != null);
        if (live.Count == 0) return;

        var parent = GetGroup();
        for (int i = 0; i < _perDab; i++)
        {
            Vector2 r = Random.insideUnitCircle * _brushRadius;
            Vector3 from = new Vector3(center.x + r.x, center.y + 500f, center.z + r.y);
            if (!GroundRaycast(from, parent, out var hit)) continue;
            Place(live[Random.Range(0, live.Count)], hit.point, hit.normal, parent);
        }
    }

    /// <summary>True if a placed object already sits within Spacing of this spot (XZ distance),
    /// so we skip it instead of stacking.</summary>
    bool TooClose(Vector3 pos)
    {
        float sq = _spacing * _spacing;
        for (int i = 0; i < _placed.Count; i++)
        {
            float dx = _placed[i].x - pos.x, dz = _placed[i].z - pos.z;
            if (dx * dx + dz * dz < sq) return true;
        }
        return false;
    }

    /// <summary>Rebuilds the placed-position cache from the current group's top-level children,
    /// so the min-gap respects objects painted earlier (incl. previous sessions).</summary>
    void RebuildPlaced()
    {
        _placed.Clear();
        if (string.IsNullOrEmpty(_groupName)) return;
        var g = GameObject.Find(_groupName);
        if (g == null) return;
        foreach (Transform child in g.transform) _placed.Add(child.position);
    }

    /// <summary>Raycast straight down but IGNORE anything already under the scatter group, so new objects
    /// land on the ground — not on top of a tree we just placed (that was building towers to the sky).</summary>
    static bool GroundRaycast(Vector3 from, Transform group, out RaycastHit ground)
    {
        var hits = Physics.RaycastAll(from, Vector3.down, 10000f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (group != null && h.collider.transform.IsChildOf(group)) continue;   // skip already-placed props
            ground = h; return true;
        }
        ground = default; return false;
    }

    bool Place(GameObject prefab, Vector3 pos, Vector3 normal, Transform parent)
    {
        if (TooClose(pos)) return false;      // keep the min-gap — no stacking
        _placed.Add(pos);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Scatter Paint");
        go.transform.position = pos;
        // Keep the prefab's own orientation (Blender-FBX roots carry a −90° X correction; overwriting it
        // lays the model flat). Layer the yaw / slope-align ON TOP of it instead of replacing it.
        var yaw   = _randomYaw   ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) : Quaternion.identity;
        var align = _alignNormal ? Quaternion.FromToRotation(Vector3.up, normal)    : Quaternion.identity;
        go.transform.rotation = align * yaw * go.transform.rotation;
        go.transform.localScale *= Random.Range(_scale.x, _scale.y);
        if (parent != null) go.transform.SetParent(parent, true);

        if (_makeNodes)
        {
            if (go.GetComponentInChildren<Collider>() == null)
            {
                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf != null) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.convex = false; }
                else go.AddComponent<SphereCollider>();
            }
            // Don't clobber a prefab that's ALREADY a configured node (the tiered _Wasteland/Nodes
            // prefabs come pre-set with their skill/level/drop). Only stamp the panel's settings
            // onto a RAW model that has no ResourceNode yet — otherwise painting "abandoned car (T2)"
            // would reset it to the panel defaults (Woodcutting/lvl1/drop40) and break the tier.
            var node = go.GetComponent<ResourceNode>();
            if (node == null)
            {
                node = go.AddComponent<ResourceNode>();
                node.nodeType = _nodeType; node.skill = _skill;
                node.levelRequired = _levelRequired; node.requiredToolId = _toolId; node.dropItemId = _dropId;
                node.xpPerAction = _xp; node.maxHits = _maxHits; node.respawnTicks = _respawn;
            }
        }
        return true;
    }

    void AutoFill()
    {
        var live = _prefabs.FindAll(p => p != null);
        if (live.Count == 0) { EditorUtility.DisplayDialog("Auto-Fill", "Add at least one prefab first.", "OK"); return; }

        var ground = GameObject.Find("Ground");
        if (ground == null) { EditorUtility.DisplayDialog("Auto-Fill", "No 'Ground' object found.", "OK"); return; }
        var rends = ground.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) { EditorUtility.DisplayDialog("Auto-Fill", "Ground has no renderers.", "OK"); return; }
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        var parent = GetGroup();
        RebuildPlaced();          // so the min-gap respects already-placed objects
        int placed = 0;
        for (int i = 0; i < _autoFillCount; i++)
        {
            float x = Random.Range(b.min.x, b.max.x);
            float z = Random.Range(b.min.z, b.max.z);
            if (GroundRaycast(new Vector3(x, b.max.y + 500f, z), parent, out var hit))
            {
                if (Place(live[Random.Range(0, live.Count)], hit.point, hit.normal, parent)) placed++;
            }
        }
        EditorUtility.DisplayDialog("Auto-Fill", "Scattered " + placed + " object(s) across the ground.", "OK");
    }

    Transform GetGroup()
    {
        if (string.IsNullOrEmpty(_groupName)) return null;
        var g = GameObject.Find(_groupName);
        if (g == null) { g = new GameObject(_groupName); Undo.RegisterCreatedObjectUndo(g, "Create Scatter Group"); }
        return g.transform;
    }
}
