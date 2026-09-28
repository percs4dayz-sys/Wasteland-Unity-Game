using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Paint-scatter enemy mobs onto the mesh world — the combat twin of the World Scatter Brush.
/// Drag in the Scene view to drop hostiles; they raycast onto the surface with random yaw/scale so a
/// camp or horde looks natural instead of grid-stamped.
///
/// Stat-aware: tick "Set combat stats" and each painted mob gets the HP / attack / damage / aggression
/// below stamped onto its CombatTarget — so you paint a ring of weak scouts, bump the numbers, paint a
/// tough mini-boss in the middle. Defaults pull prefabs from Resources/Enemies (built by
/// Wasteland ▸ Enemies ▸ Build Enemy Prefabs), but you can point it at any folder of mob prefabs.
///
/// Hand-placed mobs always respawn at home a little while after they die: the brush strips the
/// SpawnedEnemy cull-tag (that belongs to the runtime EnemySpawner, which would just destroy the body)
/// and lets Enemy3D handle the hide → wait Respawn Seconds → revive. Set the delay below.
///
/// Open:  Wasteland ▸ Enemies ▸ Enemy Scatter Brush
/// </summary>
public class EnemyScatterBrush : EditorWindow
{
    [MenuItem("Wasteland/Enemies/Enemy Scatter Brush")]
    public static void Open() => GetWindow<EnemyScatterBrush>("Mob Brush");

    // Palette — chosen prefabs are picked from a folder dropdown (not the whole project).
    readonly List<GameObject> _prefabs = new();
    string _sourceFolder = "Assets/Resources/Enemies";
    int _pickIndex;
    float  _brushRadius = 8f;
    int    _perDab      = 2;
    float  _spacing     = 4f;   // min distance to move before the brush drops again (stops clumping)
    Vector2 _scale      = new(1.0f, 1.0f);   // baked mobs are already sized; leave scale alone by default
    bool   _randomYaw   = true;
    bool   _alignNormal = false;
    string _groupName   = "Mobs";

    // Auto-fill
    int _autoFillCount = 60;

    // Combat options — stamped onto the placed mob's CombatTarget / Enemy3D.
    bool _setStats     = true;
    int  _maxHP        = 12;
    int  _attackLevel  = 3;
    int  _defenceLevel = 2;
    int  _maxDamage    = 2;
    bool _aggressive   = false;
    bool _miniBoss     = false;
    bool _boss         = false;
    float _moveSpeed   = 3.0f;
    float _aggroRange  = 9f;
    float _wanderRadius = 1.5f;   // ~5 ft idle pacing around where it's painted

    // Painted mobs always respawn at home a little while after they die: we strip the SpawnedEnemy
    // cull-tag (that one belongs to the now-disabled runtime spawner) and let Enemy3D handle the
    // hide → wait → revive. This is the delay before they come back.
    float _respawnSeconds = 20f;

    bool _painting;
    Vector3 _lastDab; bool _hasLast;

    // Positions of mobs already placed, so Spacing acts as a real min-gap between MOBS
    // (not just between brush centers) — stops them landing on top of each other.
    readonly List<Vector3> _placed = new();

    void OnEnable()  => SceneView.duringSceneGui += OnScene;
    void OnDisable() => SceneView.duringSceneGui -= OnScene;

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "1) Add mob prefab(s) below.  2) Click 'Start Painting'.  3) Drag on the ground in the Scene view.\n" +
            "Hold the mouse and drag to scatter a pack. Press Start Painting again (or close) to stop.",
            MessageType.Info);

        // Prefab palette — choose from a folder, not the whole project.
        EditorGUILayout.LabelField("Mobs to scatter (random among the chosen)", EditorStyles.boldLabel);
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
            EditorGUILayout.HelpBox("No prefabs in that folder. Default is your baked enemy prefabs " +
                "(run 'Build Enemy Prefabs' first), or point it at any folder of mob prefabs.", MessageType.Warning);
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
        _perDab      = EditorGUILayout.IntSlider("Per Dab", _perDab, 1, 20);
        _spacing     = EditorGUILayout.Slider("Spacing (min gap between mobs)", _spacing, 0.5f, 40f);
        EditorGUILayout.MinMaxSlider(new GUIContent("Scale Range"), ref _scale.x, ref _scale.y, 0.1f, 5f);
        EditorGUILayout.LabelField("   scale", _scale.x.ToString("0.00") + " – " + _scale.y.ToString("0.00"));
        _randomYaw   = EditorGUILayout.Toggle("Random Y Rotation", _randomYaw);
        _alignNormal = EditorGUILayout.Toggle("Align To Slope", _alignNormal);
        _groupName   = EditorGUILayout.TextField("Group Under", _groupName);

        EditorGUILayout.Space();
        _setStats = EditorGUILayout.BeginToggleGroup("Set combat stats (the baked prefab's stats are placeholders — stamp these instead)", _setStats);
        _maxHP        = EditorGUILayout.IntField("Max HP", _maxHP);
        _attackLevel  = EditorGUILayout.IntField("Attack Level", _attackLevel);
        _defenceLevel = EditorGUILayout.IntField("Defence Level", _defenceLevel);
        _maxDamage    = EditorGUILayout.IntField("Max Damage", _maxDamage);
        _moveSpeed    = EditorGUILayout.FloatField("Move Speed", _moveSpeed);
        _aggroRange   = EditorGUILayout.FloatField("Aggro Range", _aggroRange);
        _wanderRadius = EditorGUILayout.FloatField(
            new GUIContent("Wander Radius", "How far the mob idly paces around where you paint it (~1.5 = 5 ft). 0 = stands still."),
            _wanderRadius);
        _aggressive   = EditorGUILayout.Toggle("Aggressive (hunts on sight)", _aggressive);
        _miniBoss     = EditorGUILayout.Toggle("Mini-Boss (Warden Remains)", _miniBoss);
        _boss         = EditorGUILayout.Toggle("Boss (Behemoth Remains)", _boss);
        EditorGUILayout.EndToggleGroup();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Painted mobs respawn at home after they die:", EditorStyles.boldLabel);
        _respawnSeconds = EditorGUILayout.FloatField(
            new GUIContent("Respawn Seconds", "Delay before a killed mob revives back where you painted it."),
            _respawnSeconds);
        if (_respawnSeconds < 0f) _respawnSeconds = 0f;

        EditorGUILayout.Space();
        GUI.backgroundColor = _painting ? Color.green : Color.white;
        if (GUILayout.Button(_painting ? "● PAINTING — click to stop" : "Start Painting", GUILayout.Height(30)))
        {
            _painting = !_painting;
            if (_painting) RebuildPlaced();   // honour the min-gap against already-painted mobs
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
            Handles.color = new Color(1f, 0.35f, 0.3f, 1f);   // red disc — it's hostiles, not foliage
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
            if (!Physics.Raycast(from, Vector3.down, out var hit, 5000f)) continue;
            Place(live[Random.Range(0, live.Count)], hit.point, hit.normal, parent);
        }
    }

    /// <summary>True if a placed mob already sits within Spacing of this spot (XZ distance),
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
    /// so the min-gap respects mobs painted earlier (incl. previous sessions).</summary>
    void RebuildPlaced()
    {
        _placed.Clear();
        if (string.IsNullOrEmpty(_groupName)) return;
        var g = GameObject.Find(_groupName);
        if (g == null) return;
        foreach (Transform child in g.transform) _placed.Add(child.position);
    }

    bool Place(GameObject prefab, Vector3 pos, Vector3 normal, Transform parent)
    {
        if (TooClose(pos)) return false;      // keep the min-gap — no stacking
        _placed.Add(pos);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Scatter Mob");
        go.transform.position = pos;
        go.transform.rotation = (_alignNormal ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity)
                              * (_randomYaw ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) : Quaternion.identity);
        go.transform.localScale *= Random.Range(_scale.x, _scale.y);
        if (parent != null) go.transform.SetParent(parent, true);

        // Make sure it's actually a fightable mob, even if you pointed the brush at a raw art prefab.
        var ct = go.GetComponent<CombatTarget>() ?? go.AddComponent<CombatTarget>();
        var ai = go.GetComponent<Enemy3D>()      ?? go.AddComponent<Enemy3D>();
        if (go.GetComponentInChildren<Collider>() == null)
        {
            var cc = go.AddComponent<CapsuleCollider>();
            cc.height = 2f; cc.center = Vector3.up; cc.radius = 0.4f;
        }

        if (_setStats)
        {
            ct.maxHP = _maxHP; ct.attackLevel = _attackLevel; ct.defenceLevel = _defenceLevel;
            ct.maxDamage = _maxDamage; ct.isAggressive = _aggressive;
            ct.isMiniBoss = _miniBoss; ct.isBoss = _boss; ct.isDummy = false;
            ai.moveSpeed = _moveSpeed; ai.aggroRange = _aggroRange; ai.wanderRadius = _wanderRadius;
        }

        // Hand-placed mobs aren't owned by the runtime EnemySpawner — strip its cull-tag (it would
        // destroy the body on death) so Enemy3D revives the mob at home after _respawnSeconds instead.
        var tag = go.GetComponent<SpawnedEnemy>();
        if (tag != null) Object.DestroyImmediate(tag);
        ai.respawnSeconds = _respawnSeconds;

        return true;
    }

    void AutoFill()
    {
        var live = _prefabs.FindAll(p => p != null);
        if (live.Count == 0) { EditorUtility.DisplayDialog("Auto-Fill", "Add at least one mob prefab first.", "OK"); return; }

        var ground = GameObject.Find("Ground");
        if (ground == null) { EditorUtility.DisplayDialog("Auto-Fill", "No 'Ground' object found.", "OK"); return; }
        var rends = ground.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) { EditorUtility.DisplayDialog("Auto-Fill", "Ground has no renderers.", "OK"); return; }
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        var parent = GetGroup();
        RebuildPlaced();          // so the min-gap respects already-placed mobs
        int placed = 0;
        for (int i = 0; i < _autoFillCount; i++)
        {
            float x = Random.Range(b.min.x, b.max.x);
            float z = Random.Range(b.min.z, b.max.z);
            if (Physics.Raycast(new Vector3(x, b.max.y + 500f, z), Vector3.down, out var hit, 10000f))
            {
                if (Place(live[Random.Range(0, live.Count)], hit.point, hit.normal, parent)) placed++;
            }
        }
        EditorUtility.DisplayDialog("Auto-Fill", "Scattered " + placed + " mob(s) across the ground.", "OK");
    }

    Transform GetGroup()
    {
        if (string.IsNullOrEmpty(_groupName)) return null;
        var g = GameObject.Find(_groupName);
        if (g == null) { g = new GameObject(_groupName); Undo.RegisterCreatedObjectUndo(g, "Create Mob Group"); }
        return g.transform;
    }
}
