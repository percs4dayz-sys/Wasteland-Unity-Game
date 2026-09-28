using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Scene authoring, including inactive enemies and golems created by scene spawners.</summary>
public class SceneMonsterEditor : EditorWindow
{
    public sealed class Entry
    {
        public string name, kind;
        public int tier;
        public readonly List<Component> sources = new();
    }
    List<Entry> _entries = new();
    Entry _selected;
    string _search = "";
    int _filter, _instance;
    Vector2 _listScroll, _detailScroll;
    bool _statsOpen = true, _aiOpen, _harvestOpen = true;
    bool _refresh;
    MonsterDropTable _preview;
    string _previewKey;
    MonsterDropTableEditor _tableEditor;

    [MenuItem("Wasteland/Enemies/Scene Monster Editor")]
    public static void Open()
    {
        var window = GetWindow<SceneMonsterEditor>("Scene Monsters");
        window.minSize = new Vector2(980, 620);
        window.Show();
    }

    [MenuItem("CONTEXT/CombatTarget/Edit in Scene Monster Editor")]
    static void OpenFromMonster(MenuCommand command)
    {
        Open(); GetWindow<SceneMonsterEditor>().SelectSource((Component)command.context);
    }

    public void SelectSource(Component source)
    {
        Refresh();
        _selected = _entries.FirstOrDefault(e => e.sources.Contains(source));
        _instance = _selected != null && _selected.sources.Count > 1 ? _selected.sources.IndexOf(source) + 1 : 0;
        Repaint();
    }

    void OnEnable()
    {
        EditorApplication.hierarchyChanged += QueueRefresh;
        EditorApplication.playModeStateChanged += PlayState;
        Undo.undoRedoPerformed += QueueRefresh;
        Refresh();
    }
    void OnDisable()
    {
        EditorApplication.hierarchyChanged -= QueueRefresh;
        EditorApplication.playModeStateChanged -= PlayState;
        Undo.undoRedoPerformed -= QueueRefresh;
        ClearPreview();
    }
    void QueueRefresh() { _refresh = true; Repaint(); }
    void PlayState(PlayModeStateChange state) => QueueRefresh();

    public static List<Entry> ScanScene(Scene scene)
    {
        var groups = new Dictionary<string, Entry>();
        if (!scene.IsValid() || !scene.isLoaded) return groups.Values.ToList();
        void Add(Component source, string name, int tier, string kind)
        {
            string key = name + "|" + tier + "|" + kind;
            if (!groups.TryGetValue(key, out var entry))
                groups[key] = entry = new Entry { name = name, tier = tier, kind = kind };
            entry.sources.Add(source);
        }
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var monster in root.GetComponentsInChildren<CombatTarget>(true))
                Add(monster, monster.DisplayName, monster.LootTier, monster.isDummy ? "Dummy" : monster.isGodHunter ? "God-Hunter" : monster.isBoss ? "Boss" : monster.isMiniBoss ? "Miniboss" : "Mob");
            foreach (var spawner in root.GetComponentsInChildren<BCEarthGolemSpawner>(true))
                Add(spawner, spawner.MonsterName, spawner.tier, "Golem spawner");
        }
        return groups.Values.OrderBy(e => e.tier).ThenBy(e => e.name).ToList();
    }

    void Refresh()
    {
        var previous = _selected?.sources.FirstOrDefault();
        _entries = ScanScene(SceneManager.GetActiveScene());
        _selected = _entries.FirstOrDefault(e => previous != null && e.sources.Contains(previous)) ?? _entries.FirstOrDefault();
        _instance = 0; _refresh = false; ClearPreview();
    }

    void ClearPreview()
    {
        if (_tableEditor != null) DestroyImmediate(_tableEditor);
        if (_preview != null) DestroyImmediate(_preview);
        _tableEditor = null; _preview = null; _previewKey = null;
    }

    void OnGUI()
    {
        if (_refresh && Event.current.type == EventType.Layout) Refresh();
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label(SceneManager.GetActiveScene().name, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{_entries.Sum(e => e.sources.Count)} sources · {_entries.Count} groups");
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(65))) Refresh();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Save changes", EditorStyles.toolbarButton, GUILayout.Width(105)))
                { EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets(); }
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorGUILayout.HelpBox("Exit Play mode to make permanent changes. The list includes live enemies while the game is running.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawList();
            using (new EditorGUILayout.VerticalScope())
            {
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) DrawDetails();
                EditorGUILayout.EndScrollView();
            }
        }
        GUILayout.Label("Edits support Undo. Save changes writes the scene and drop-table assets for both PC and Android builds.", EditorStyles.helpBox);
    }

    void DrawList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(290)))
        {
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            _filter = EditorGUILayout.Popup(_filter, new[] { "All monsters", "Mobs", "Bosses / minibosses", "Golem spawners", "Dummies" });
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            foreach (var entry in _entries)
            {
                if ((entry.name + " " + entry.kind + " T" + entry.tier).IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (_filter == 1 && entry.kind != "Mob" || _filter == 2 && entry.kind != "Boss" && entry.kind != "Miniboss" && entry.kind != "God-Hunter" || _filter == 3 && entry.kind != "Golem spawner" || _filter == 4 && entry.kind != "Dummy") continue;
                var old = GUI.backgroundColor;
                if (entry == _selected) GUI.backgroundColor = new Color(.48f, .77f, 1);
                if (GUILayout.Button($"{entry.name}   ×{entry.sources.Count}\nT{entry.tier} · {entry.kind}", GUILayout.Height(47)))
                { _selected = entry; _instance = 0; _detailScroll = Vector2.zero; ClearPreview(); }
                GUI.backgroundColor = old;
            }
            EditorGUILayout.EndScrollView();
        }
    }

    Component[] SelectedSources() => _selected == null ? Array.Empty<Component>() :
        _instance == 0 ? _selected.sources.Where(s => s != null).ToArray() :
        _selected.sources.Skip(_instance - 1).Take(1).Where(s => s != null).ToArray();

    void DrawDetails()
    {
        var sources = SelectedSources();
        if (sources.Length == 0) { EditorGUILayout.HelpBox("No monsters found in the active scene. Open your gameplay scene and press Refresh.", MessageType.Info); return; }
        GUILayout.Label(_selected.name, new GUIStyle(EditorStyles.boldLabel) { fontSize = 20 }, GUILayout.Height(30));
        if (_selected.sources.Count > 1)
        {
            var options = new[] { "All " + _selected.sources.Count + " copies of this name / tier" }
                .Concat(_selected.sources.Select((s, i) => (i + 1) + ": " + HierarchyPath(s.transform))).ToArray();
            int choice = EditorGUILayout.Popup("Editing", _instance, options);
            if (choice != _instance) { _instance = choice; ClearPreview(); return; }
        }
        else EditorGUILayout.LabelField("Editing", HierarchyPath(sources[0].transform), EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField($"{sources.Length} selected · {sources.Count(s => !s.gameObject.activeInHierarchy)} inactive (included)", EditorStyles.miniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Select in Hierarchy")) { Selection.objects = sources.Select(s => (Object)s.gameObject).ToArray(); EditorGUIUtility.PingObject(sources[0]); }
            if (GUILayout.Button("Frame in Scene")) { Selection.objects = sources.Select(s => (Object)s.gameObject).ToArray(); SceneView.lastActiveSceneView?.FrameSelected(); }
        }
        if (sources[0] is BCEarthGolemSpawner) DrawSpawner(sources.Cast<BCEarthGolemSpawner>().ToArray());
        else DrawMonster(sources.Cast<CombatTarget>().ToArray());
        EditorGUILayout.Space(12);
        GUILayout.Label("Drop table", EditorStyles.boldLabel);
        DrawDrops(sources);
    }

    static string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;

    void DrawMonster(CombatTarget[] targets)
    {
        _statsOpen = EditorGUILayout.Foldout(_statsOpen, "Combat stats", true);
        if (_statsOpen) Fields(targets, "maxHP", "attackLevel", "defenceLevel", "maxDamage", "isAggressive", "tier", "isMiniBoss", "isBoss", "isGodHunter", "isDummy");
        var ais = targets.Select(t => t.GetComponent<Enemy3D>()).Where(a => a != null).ToArray();
        _aiOpen = EditorGUILayout.Foldout(_aiOpen, "Movement, attacks and respawn", true);
        if (_aiOpen && ais.Length > 0) Fields(ais, "aggroRange", "attackRange", "moveSpeed", "attackCooldown", "respawnSeconds", "windupSeconds", "swingReachBonus", "deathLingerSeconds", "wanderRadius", "wanderSpeedMul", "wanderPauseRange");
        var harvest = targets.Select(t => t.GetComponent<FissionGolem>()).Where(a => a != null).ToArray();
        if (harvest.Length > 0 && (_harvestOpen = EditorGUILayout.Foldout(_harvestOpen, "Corpse harvesting", true)))
            Fields(harvest, "tier", "harvestSkill", "harvestLevelRequired", "minYield", "maxYield", "xpPerCore");
        if (targets.Any(t => t.tier == 0)) EditorGUILayout.HelpBox("Tier 0 derives the loot tier from max HP. Set an explicit tier (1–5) to keep HP edits from changing the default drops.", MessageType.Info);
        if (targets.Any(t => t.isDummy)) EditorGUILayout.HelpBox("Training dummies never drop loot, even when a custom table is assigned.", MessageType.Info);
    }

    void DrawSpawner(BCEarthGolemSpawner[] spawners)
    {
        EditorGUILayout.HelpBox("This is a persistent golem spawner. Changes apply to each new spawn, including respawns. The corpse's raw cores are harvested separately from death drops.", MessageType.Info);
        Fields(spawners, "tier", "respawnDelay");
        if (spawners.Any(s => !s.overrideStats))
        {
            var stats = spawners[0].EffectiveStats;
            EditorGUILayout.LabelField($"Defaults: HP {stats.maxHP} · Attack {stats.attackLevel} · Defence {stats.defenceLevel} · Max hit {stats.maxDamage}");
            if (GUILayout.Button("Enable editable golem stats (start from current defaults)"))
            {
                Undo.RecordObjects(spawners, "Enable golem stat editing");
                foreach (var s in spawners) { if (!s.overrideStats) s.stats = BCEarthGolemSpawner.DefaultStats(s.tier); s.overrideStats = true; Dirty(s); }
            }
        }
        else
        {
            var so = new SerializedObject(spawners); so.Update();
            var property = so.FindProperty("stats");
            EditorGUILayout.PropertyField(property, new GUIContent("Combat, movement and harvest stats"), true);
            so.ApplyModifiedProperties();
            if (GUILayout.Button("Use tier defaults for golem stats"))
            { Undo.RecordObjects(spawners, "Restore golem tier stats"); foreach (var s in spawners) { s.overrideStats = false; Dirty(s); } }
        }
    }

    static void Fields(Object[] objects, params string[] names)
    {
        var so = new SerializedObject(objects); so.Update();
        foreach (var name in names)
        {
            var property = so.FindProperty(name);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(property, true);
            if (!EditorGUI.EndChangeCheck()) continue;
            if (property.propertyType == SerializedPropertyType.Integer)
                property.intValue = Mathf.Max(name == "maxHP" || name == "attackLevel" || name == "defenceLevel" ? 1 : 0, property.intValue);
            if (property.propertyType == SerializedPropertyType.Float)
                property.floatValue = Mathf.Max(name == "attackCooldown" ? .1f : 0, property.floatValue);
        }
        so.ApplyModifiedProperties();
    }

    public static MonsterDropTable GetTable(Component source) => source is CombatTarget c ? c.dropTable : ((BCEarthGolemSpawner)source).dropTable;
    public static void AssignTable(Component[] sources, MonsterDropTable table)
    {
        Undo.RecordObjects(sources, "Change monster drop table");
        foreach (var source in sources)
        {
            if (source is CombatTarget c) c.dropTable = table;
            else ((BCEarthGolemSpawner)source).dropTable = table;
            Dirty(source);
        }
    }
    static void Dirty(Component source)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(source);
        EditorUtility.SetDirty(source); EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
    }

    void DrawDrops(Component[] sources)
    {
        var table = GetTable(sources[0]);
        bool mixed = sources.Any(s => GetTable(s) != table);
        EditorGUI.showMixedValue = mixed;
        EditorGUI.BeginChangeCheck();
        var chosen = (MonsterDropTable)EditorGUILayout.ObjectField("Custom table", table, typeof(MonsterDropTable), false);
        if (EditorGUI.EndChangeCheck()) { AssignTable(sources, chosen); ClearPreview(); }
        EditorGUI.showMixedValue = false;
        if (mixed) { EditorGUILayout.HelpBox("These copies use different tables. Choose one copy above to edit it, or assign a table here to all selected copies.", MessageType.Info); return; }
        table = GetTable(sources[0]);
        if (table != null)
        {
            EditorGUILayout.HelpBox("Editing this asset changes every monster that references it. Make a separate copy below to customize only this selection.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Make separate copy")) SaveCopy(sources, table);
                if (GUILayout.Button("Use default drops")) { AssignTable(sources, null); ClearPreview(); return; }
            }
        }
        else
        {
            var c = sources[0] as CombatTarget;
            var spawner = sources[0] as BCEarthGolemSpawner;
            int tier = c != null ? c.LootTier : spawner.tier;
            bool mini = c == null || c.isMiniBoss || c.isBoss, god = c != null && c.isGodHunter, dummy = c != null && c.isDummy;
            string key = tier + ":" + mini + ":" + god + ":" + dummy;
            if (_preview == null || key != _previewKey)
            { ClearPreview(); _previewKey = key; _preview = EnemyLoot.CreateDefaultTable(tier, mini, god, dummy); _preview.hideFlags = HideFlags.HideAndDontSave; }
            table = _preview;
            EditorGUILayout.LabelField("Using standard drops — expand groups to inspect items and odds.", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Create editable copy of these drops")) SaveCopy(sources, table);
        }
        if (_tableEditor == null || _tableEditor.target != table)
        { if (_tableEditor != null) DestroyImmediate(_tableEditor); _tableEditor = (MonsterDropTableEditor)Editor.CreateEditor(table); }
        _tableEditor.DrawTable(table == _preview);
    }

    void SaveCopy(Component[] sources, MonsterDropTable source)
    {
        string path = EditorUtility.SaveFilePanelInProject("Save monster drop table", _selected.name + " Drops", "asset", "Choose where to save this monster's drop table.");
        if (string.IsNullOrEmpty(path)) return;
        path = AssetDatabase.GenerateUniqueAssetPath(path);
        var copy = Instantiate(source); copy.hideFlags = HideFlags.None;
        AssetDatabase.CreateAsset(copy, path); AssignTable(sources, copy);
        EditorGUIUtility.PingObject(copy);
        // Keep the current preview alive until this OnGUI pass has finished drawing it.
        Repaint();
    }
}
