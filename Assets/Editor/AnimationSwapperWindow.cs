using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Wasteland ▸ Animation Swapper — browse every animation state on a model's Animator Controller and
/// swap any clip for another one from the project with a click. Works for the player, Roxy, enemies,
/// the boss — anything with an Animator Controller.
///
/// Handles plain states (one clip each) and Blend Trees (lists the idle/walk/run children so you can
/// swap those too). Changes are written straight to the controller and saved.
/// </summary>
public class AnimationSwapperWindow : EditorWindow
{
    [MenuItem("Wasteland/Animation Swapper")]
    static void Open() => GetWindow<AnimationSwapperWindow>("Anim Swapper").minSize = new Vector2(460, 320);

    AnimatorController _controller;
    Vector2 _scroll;
    string _filter = "";

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "1) Drop in a Controller — or select a model/NPC in the scene and click 'From Selection'.\n" +
            "2) Each state shows its current clip. Click the ⦿ picker to swap it for any animation in the project.\n" +
            "Blend Trees (like Locomotion) expand so you can swap idle/walk/run individually.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            _controller = (AnimatorController)EditorGUILayout.ObjectField(
                "Controller", _controller, typeof(AnimatorController), false);
            if (GUILayout.Button("From Selection", GUILayout.Width(120)))
            {
                var c = ControllerFromSelection();
                if (c != null) _controller = c;
                else ShowNotification(new GUIContent("Select a model/NPC with an Animator, or a .controller"));
            }
        }

        if (_controller == null) return;
        _filter = EditorGUILayout.TextField("Filter", _filter);
        EditorGUILayout.Space(4);

        var states = new List<AnimatorState>();
        foreach (var layer in _controller.layers) Collect(layer.stateMachine, states);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var st in states)
        {
            if (!string.IsNullOrEmpty(_filter) &&
                st.name.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (st.motion is BlendTree bt)
            {
                EditorGUILayout.LabelField(st.name + "  (Blend Tree)", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                var kids = bt.children;
                for (int i = 0; i < kids.Length; i++)
                {
                    var oldClip = kids[i].motion as AnimationClip;
                    var newClip = (AnimationClip)EditorGUILayout.ObjectField(
                        $"• {(oldClip ? oldClip.name : "slot " + i)}", oldClip, typeof(AnimationClip), false);
                    if (newClip != oldClip)
                    {
                        Undo.RecordObject(bt, "Swap Blend Clip");
                        kids[i].motion = newClip;
                        bt.children = kids;              // reassign the modified array back
                        Save();
                        break;
                    }
                }
                EditorGUI.indentLevel--;
            }
            else
            {
                var oldClip = st.motion as AnimationClip;
                var newClip = (AnimationClip)EditorGUILayout.ObjectField(st.name, oldClip, typeof(AnimationClip), false);
                if (newClip != oldClip)
                {
                    Undo.RecordObject(st, "Swap Clip");
                    st.motion = newClip;
                    Save();
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    void Save()
    {
        EditorUtility.SetDirty(_controller);
        AssetDatabase.SaveAssets();
    }

    static void Collect(AnimatorStateMachine sm, List<AnimatorState> outList)
    {
        foreach (var cs in sm.states) outList.Add(cs.state);
        foreach (var ssm in sm.stateMachines) Collect(ssm.stateMachine, outList);
    }

    static AnimatorController ControllerFromSelection()
    {
        if (Selection.activeObject is AnimatorController c) return c;
        var go = Selection.activeGameObject;
        var anim = go ? go.GetComponentInChildren<Animator>() : null;
        return anim ? anim.runtimeAnimatorController as AnimatorController : null;
    }
}
