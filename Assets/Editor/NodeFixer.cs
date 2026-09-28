using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Re-configures the selected ResourceNode(s) to a gathering type, using the same
/// canonical T1 values as the Assets/_Wasteland/Nodes prefabs. The gather ANIMATION
/// is chosen from the node's skill, so setting this correctly also fixes the
/// "wrong gather animation" problem (a node mis-set to Fishing plays the cast).
///
/// Select your rubble nodes → "Scrapping"; your tree/brush nodes → "Woodcutting";
/// your water nodes → "Fishing". Works on the selection (and its children).
///
/// Menu: Tools ▸ Wasteland ▸ Set Selected Nodes → …
/// </summary>
public static class NodeFixer
{
    [MenuItem("Tools/Wasteland/Set Selected Nodes → Scrapping")]
    static void Scrap()  => Apply(ResourceNodeType.RubblePile,   Skill.Scrapping,   toolId: 3, dropId: 10);

    [MenuItem("Tools/Wasteland/Set Selected Nodes → Woodcutting")]
    static void Wood()   => Apply(ResourceNodeType.WoodenDebris, Skill.Woodcutting, toolId: 4, dropId: 40);

    [MenuItem("Tools/Wasteland/Set Selected Nodes → Fishing")]
    static void Fish()   => Apply(ResourceNodeType.WaterBarrel,  Skill.Fishing,     toolId: 5, dropId: 22);

    static void Apply(ResourceNodeType type, Skill skill, int toolId, int dropId)
    {
        var nodes = Selection.gameObjects
            .SelectMany(go => go.GetComponentsInChildren<ResourceNode>(true))
            .Distinct()
            .ToList();

        if (nodes.Count == 0)
        {
            EditorUtility.DisplayDialog("Set Nodes",
                "Select one or more GameObjects that have a ResourceNode, then run this again.", "OK");
            return;
        }

        foreach (var n in nodes)
        {
            Undo.RecordObject(n, "Set Node Type");
            n.nodeType = type;
            n.skill = skill;
            n.levelRequired = 1;
            n.requiredToolId = toolId;
            n.dropItemId = dropId;
            n.xpPerAction = 25;
            EditorUtility.SetDirty(n);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[NodeFixer] Set {nodes.Count} node(s) to {skill} ({type}). Save the scene to keep it.");
    }
}
