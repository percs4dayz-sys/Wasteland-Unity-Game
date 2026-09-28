using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MonsterDropTable))]
public class MonsterDropTableEditor : Editor
{
    readonly HashSet<int> _open = new();
    public override void OnInspectorGUI() => DrawTable(false);

    public void DrawTable(bool readOnly)
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("Each group rolls independently. A successful group drops ONE item from its weighted list. 100% guarantees a drop; 0% disables it. The item percentages below are base chances per kill, before Salvage.", MessageType.Info);
        var rolls = serializedObject.FindProperty("rolls");
        for (int i = 0; i < rolls.arraySize; i++)
        {
            var roll = rolls.GetArrayElementAtIndex(i);
            var label = roll.FindPropertyRelative("label");
            var chance = roll.FindPropertyRelative("chance");
            var outcomes = roll.FindPropertyRelative("outcomes");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool expanded = EditorGUILayout.Foldout(_open.Contains(i), $"{label.stringValue}  —  {chance.floatValue * 100:0.####}%  ·  {outcomes.arraySize} items", true);
            if (expanded) _open.Add(i); else _open.Remove(i);
            if (expanded)
            {
                using (new EditorGUI.DisabledScope(readOnly))
                {
                    EditorGUILayout.PropertyField(label, new GUIContent("Group name"));
                    chance.floatValue = Mathf.Clamp(EditorGUILayout.FloatField("Drop chance (%)", chance.floatValue * 100), 0, 100) / 100;
                    EditorGUILayout.PropertyField(roll.FindPropertyRelative("salvageBonus"), new GUIContent("Salvage module bonus"));
                    float total = 0;
                    for (int j = 0; j < outcomes.arraySize; j++)
                    {
                        var item = outcomes.GetArrayElementAtIndex(j);
                        if (ItemRegistry.Get(item.FindPropertyRelative("itemId").intValue) != null)
                            total += Mathf.Max(0, item.FindPropertyRelative("weight").floatValue);
                    }
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Item", GUILayout.MinWidth(140));
                    GUILayout.Label("Min", GUILayout.Width(45)); GUILayout.Label("Max", GUILayout.Width(45));
                    GUILayout.Label("Weight", GUILayout.Width(60)); GUILayout.Label("Per kill", GUILayout.Width(78)); GUILayout.Space(26);
                    EditorGUILayout.EndHorizontal();
                    int remove = -1;
                    for (int j = 0; j < outcomes.arraySize; j++)
                    {
                        var entry = outcomes.GetArrayElementAtIndex(j);
                        var id = entry.FindPropertyRelative("itemId");
                        var min = entry.FindPropertyRelative("minQuantity"); var max = entry.FindPropertyRelative("maxQuantity");
                        var weight = entry.FindPropertyRelative("weight");
                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button(ItemName(id.intValue), EditorStyles.popup, GUILayout.MinWidth(140)))
                        {
                            string path = id.propertyPath;
                            var asset = target;
                            PopupWindow.Show(GUILayoutUtility.GetLastRect(), new ItemPicker(value =>
                            {
                                if (asset == null) return;
                                var so = new SerializedObject(asset); so.FindProperty(path).intValue = value;
                                so.ApplyModifiedProperties(); Repaint();
                            }));
                        }
                        min.intValue = Mathf.Clamp(EditorGUILayout.IntField(min.intValue, GUILayout.Width(45)), 1, 1000000);
                        max.intValue = Mathf.Clamp(EditorGUILayout.IntField(max.intValue, GUILayout.Width(45)), min.intValue, 1000000);
                        weight.floatValue = Mathf.Max(0, EditorGUILayout.FloatField(weight.floatValue, GUILayout.Width(60)));
                        GUILayout.Label((total > 0 ? chance.floatValue * weight.floatValue / total * 100 : 0).ToString("0.####") + "%", GUILayout.Width(78));
                        if (GUILayout.Button("×", GUILayout.Width(26))) remove = j;
                        EditorGUILayout.EndHorizontal();
                        if (ItemRegistry.Get(id.intValue) == null) EditorGUILayout.HelpBox("Unknown item ID: this outcome will be ignored.", MessageType.Warning);
                    }
                    if (remove >= 0) outcomes.DeleteArrayElementAtIndex(remove);
                    if (GUILayout.Button("+ Add item"))
                    {
                        int j = outcomes.arraySize++; var entry = outcomes.GetArrayElementAtIndex(j);
                        entry.FindPropertyRelative("itemId").intValue = 10;
                        entry.FindPropertyRelative("minQuantity").intValue = 1;
                        entry.FindPropertyRelative("maxQuantity").intValue = 1;
                        entry.FindPropertyRelative("weight").floatValue = 1;
                    }
                    if (outcomes.arraySize == 0 || total <= 0) EditorGUILayout.HelpBox("This group has no weighted items and drops nothing.", MessageType.Warning);
                    if (GUILayout.Button("Remove this group"))
                    { rolls.DeleteArrayElementAtIndex(i); EditorGUILayout.EndVertical(); break; }
                }
            }
            EditorGUILayout.EndVertical();
        }
        using (new EditorGUI.DisabledScope(readOnly))
        {
            if (GUILayout.Button("+ Add drop group"))
            {
                int i = rolls.arraySize++; var roll = rolls.GetArrayElementAtIndex(i);
                roll.FindPropertyRelative("label").stringValue = "New drop";
                roll.FindPropertyRelative("chance").floatValue = 1;
                roll.FindPropertyRelative("salvageBonus").boolValue = false;
                roll.FindPropertyRelative("outcomes").ClearArray(); _open.Add(i);
            }
            EditorGUILayout.Space();
            var cosmetic = serializedObject.FindProperty("cosmeticChance");
            bool useDefault = EditorGUILayout.Toggle("Default skin unlock chance", cosmetic.floatValue < 0);
            if (useDefault) cosmetic.floatValue = -1;
            else cosmetic.floatValue = Mathf.Clamp(EditorGUILayout.FloatField("Skin unlock chance (%)", Mathf.Max(0, cosmetic.floatValue) * 100), 0, 100) / 100;
            EditorGUILayout.LabelField("Skins unlock separately: normal 2%, miniboss 10%, boss 25% by default.", EditorStyles.wordWrappedMiniLabel);
        }
        // A preview is transient and must never become dirty just by drawing it.
        if (!readOnly) serializedObject.ApplyModifiedProperties();
    }

    static string ItemName(int id) => (ItemRegistry.Get(id)?.name ?? "Unknown item") + " [" + id + "]";

    sealed class ItemPicker : PopupWindowContent
    {
        readonly Action<int> _choose;
        string _search = "";
        Vector2 _scroll;
        ItemData[] _items;
        public ItemPicker(Action<int> choose) { _choose = choose; }
        public override Vector2 GetWindowSize() => new(420, 420);
        public override void OnOpen() => _items = ItemRegistry.All.OrderBy(i => i.name).ToArray();
        public override void OnGUI(Rect rect)
        {
            GUI.SetNextControlName("ItemSearch");
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            EditorGUI.FocusTextInControl("ItemSearch");
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var item in _items)
            {
                var name = ItemName(item.id);
                if (name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button(name, EditorStyles.miniButton)) { _choose(item.id); editorWindow.Close(); break; }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
