using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns one of your models into a configured gather-node PREFAB in one click: wraps it with a
/// ResourceNode (skill/tier/drop), guarantees a collider, and saves it to _Wasteland/Nodes.
///
/// Workflow this enables: make a node prefab ONCE (e.g. a rusted car as a scrap pile), then scatter
/// that prefab with the World Scatter Brush. Every scattered copy stays linked — edit the prefab
/// later and they ALL update. No editing instances one-by-one.
///
/// Open:  Wasteland ▸ World ▸ Make Gather-Node Prefab
/// </summary>
public class MakeNodePrefab : EditorWindow
{
    [MenuItem("Wasteland/World/Make Gather-Node Prefab")]
    public static void Open() => GetWindow<MakeNodePrefab>("Make Node Prefab");

    GameObject _model;
    string _name = "ScrapCar";
    ResourceNodeType _nodeType = ResourceNodeType.RubblePile;
    Skill _skill = Skill.Scrapping;
    int _level = 1, _toolId = 3, _dropId = 10, _xp = 25, _maxHits = 3, _respawn = 10;

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Pick one of your models (e.g. a rusted car or tree), set the node fields, click Create.\n" +
            "You get a configured prefab in _Wasteland/Nodes — drag THAT into the Scatter Brush.",
            MessageType.Info);

        _model = (GameObject)EditorGUILayout.ObjectField("Model (your asset)", _model, typeof(GameObject), false);
        _name  = EditorGUILayout.TextField("Prefab Name", _name);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Node settings", EditorStyles.boldLabel);
        _nodeType = (ResourceNodeType)EditorGUILayout.EnumPopup("Node Type", _nodeType);
        _skill    = (Skill)EditorGUILayout.EnumPopup("Skill", _skill);
        _level    = EditorGUILayout.IntField("Level Required (tier)", _level);
        _toolId   = EditorGUILayout.IntField("Required Tool Id", _toolId);
        _dropId   = EditorGUILayout.IntField("Drop Item Id", _dropId);
        _xp       = EditorGUILayout.IntField("XP Per Action", _xp);
        _maxHits  = EditorGUILayout.IntField("Max Hits", _maxHits);
        _respawn  = EditorGUILayout.IntField("Respawn Ticks", _respawn);

        EditorGUILayout.HelpBox(
            "Reference: Trees → WoodenDebris / Woodcutting (tool 4, drop 40).  " +
            "Scrap/rocks → RubblePile / Scrapping (tool 3, drop 10).  " +
            "Fishing → WaterBarrel / Fishing (tool 5, drop 22).",
            MessageType.None);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(_model == null || string.IsNullOrWhiteSpace(_name)))
            if (GUILayout.Button("Create Node Prefab", GUILayout.Height(28))) Create();
    }

    void Create()
    {
        var temp = (GameObject)PrefabUtility.InstantiatePrefab(_model);
        if (temp == null) { temp = Instantiate(_model); }
        temp.name = _name;

        // Clickable: a node needs a collider (player click-to-gather raycasts against it).
        if (temp.GetComponentInChildren<Collider>() == null)
        {
            var mf = temp.GetComponentInChildren<MeshFilter>();
            if (mf != null) mf.gameObject.AddComponent<MeshCollider>();
            else temp.AddComponent<SphereCollider>();
        }

        var node = temp.GetComponent<ResourceNode>() ?? temp.AddComponent<ResourceNode>();
        node.nodeType = _nodeType; node.skill = _skill;
        node.levelRequired = _level; node.requiredToolId = _toolId; node.dropItemId = _dropId;
        node.xpPerAction = _xp; node.maxHits = _maxHits; node.respawnTicks = _respawn;

        WastelandPaths.EnsureFolder(WastelandPaths.Root + "/Nodes");
        string path = AssetDatabase.GenerateUniqueAssetPath(WastelandPaths.Root + "/Nodes/" + _name + ".prefab");
        var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
        DestroyImmediate(temp);

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        EditorUtility.DisplayDialog("Make Node Prefab",
            "Created node prefab:\n" + path + "\n\n" +
            "Now drag it into the World Scatter Brush palette and paint. Edit this prefab anytime and " +
            "every scattered copy updates.", "OK");
    }
}
