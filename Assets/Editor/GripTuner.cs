using UnityEditor;
using UnityEngine;

/// <summary>
/// Visual grip tuner. Spawns a held model on the player's hand bone IN EDIT MODE so you can pose it
/// with the normal Move/Rotate/Scale tools, then "Capture" locks its transform into the matching
/// EquipmentVisuals grip. No more typing numbers into fields or fighting Play-mode reverts.
///
/// Menu: Wasteland ▸ NewPlayer ▸ Grip Tuner.
/// </summary>
public class GripTuner : EditorWindow
{
    int _itemId = 12;                 // default: Pipe Pistol
    GameObject _preview;
    float _fit = 1f;                  // auto-fit base scale captured at spawn
    Quaternion _frame = Quaternion.identity;   // authored hand frame → this rig's hand bone (EquipmentVisuals.HandFrame)
    EquipmentVisuals _ev;

    [MenuItem("Wasteland/NewPlayer/Grip Tuner")]
    static void Open() => GetWindow<GripTuner>("Grip Tuner").minSize = new Vector2(300, 230);

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "1. Set the item ID and Spawn.\n2. Pose it on the hand with the Scene Move/Rotate/Scale tools (W/E/R).\n3. Capture — locks it into the grip. Then save the scene.\nEdit mode only.",
            MessageType.Info);

        _itemId = EditorGUILayout.IntField("Item ID", _itemId);
        EditorGUILayout.LabelField("(1 machete · 12 pistol · 3 pickaxe · 4 hatchet · 5 rod · 2 shield · 100-105/110-113 weapons)", EditorStyles.miniLabel);

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Spawn / refresh preview on hand", GUILayout.Height(28))) Spawn();

        using (new EditorGUI.DisabledScope(_preview == null))
            if (GUILayout.Button("Capture → lock into grip", GUILayout.Height(28))) Capture();

        if (GUILayout.Button("Clear preview")) Clear();

        if (_preview != null)
            EditorGUILayout.HelpBox("Posing " + _preview.name + " — use the Scene tools, then Capture.", MessageType.None);
    }

    void OnDisable() => Clear();

    void Spawn()
    {
        Clear();

        _ev = Object.FindAnyObjectByType<EquipmentVisuals>(FindObjectsInactive.Include);
        if (_ev == null) { Fail("No player (EquipmentVisuals) found in the open scene."); return; }

        var anim = _ev.GetComponentInChildren<Animator>();
        if (anim == null) { Fail("The player has no Animator in the open scene. Open your gameplay scene with the player and try again."); return; }

        var item = ItemRegistry.Get(_itemId);
        if (item == null) { Fail($"No item with id {_itemId}."); return; }
        if (string.IsNullOrEmpty(item.heldModel)) { Fail($"'{item.name}' has no heldModel — nothing to hold."); return; }

        bool isShield = item.type == ItemType.Shield;
        var hand = ResolveHand(anim, isShield);
        if (hand == null)
        {
            Fail("Couldn't find a hand bone. The rig isn't Humanoid-mapped and has no bone named like a " +
                 "hand (RightHand / CC_Base_R_Hand / mixamorig:RightHand …). Set the model's Rig to " +
                 "Humanoid (Configure) or check the bone names.");
            return;
        }

        HeldModels.ClearCache();
        var prefab = HeldModels.Load(item.heldModel);
        if (prefab == null) { Fail($"No model found for '{item.name}' — looked for Resources/{item.heldModel} and by filename anywhere under HeldModels."); return; }

        _preview = Instantiate(prefab);
        _preview.name = "GRIP_TUNE_" + _itemId + "_" + item.name;
        _preview.transform.SetParent(hand, false);

        // Same auto-fit base EquipmentVisuals uses (longest dimension → ~0.5 m), so captured scale
        // means the same thing at runtime.
        _preview.transform.localScale = Vector3.one;
        var rends = _preview.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds wb = rends[0].bounds;
            foreach (var r in rends) wb.Encapsulate(r.bounds);
            float longest = Mathf.Max(wb.size.x, Mathf.Max(wb.size.y, wb.size.z));
            if (longest > 1e-4f) _fit = 0.5f / longest;
        }

        // Grips live in the authored hand frame; EquipmentVisuals maps them onto this rig's hand.
        _frame = EquipmentVisuals.HandFrame(anim, hand, isShield);
        var g = CurrentGrip(_itemId);
        _preview.transform.localPosition = _frame * g.position;
        _preview.transform.localRotation = _frame * Quaternion.Euler(g.euler);
        _preview.transform.localScale = Vector3.one * ((g.scale <= 0f ? 1f : g.scale) * _fit);

        Selection.activeGameObject = _preview;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
    }

    /// <summary>Find the hand bone the same way EquipmentVisuals does: humanoid mapping first, then
    /// by common bone names — so the tuner works on Humanoid AND generic rigs.</summary>
    static Transform ResolveHand(Animator anim, bool left)
    {
        if (anim.avatar != null && anim.isHuman)
        {
            var b = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (b != null) return b;
        }
        string[] names = left
            ? new[] { "LeftHand", "mixamorig:LeftHand", "CC_Base_L_Hand", "hand_l", "Hand_L", "L_Hand", "Bip01 L Hand" }
            : new[] { "RightHand", "mixamorig:RightHand", "CC_Base_R_Hand", "hand_r", "Hand_R", "R_Hand", "Bip01 R Hand" };
        foreach (var t in anim.GetComponentsInChildren<Transform>(true))
            foreach (var n in names)
                if (string.Equals(t.name, n, System.StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    void Capture()
    {
        if (_preview == null || _ev == null) return;
        var t = _preview.transform;
        float scale = _fit > 1e-6f ? t.localScale.x / _fit : 1f;

        var toAuthored = Quaternion.Inverse(_frame);
        var grip = new EquipmentVisuals.Grip
        {
            itemId = _itemId, position = toAuthored * t.localPosition,
            euler = (toAuthored * t.localRotation).eulerAngles, scale = scale,
        };

        Undo.RecordObject(_ev, "Capture Grip");
        int idx = _ev.grips.FindIndex(x => x.itemId == _itemId);
        if (idx >= 0) _ev.grips[idx] = grip; else _ev.grips.Add(grip);
        EditorUtility.SetDirty(_ev);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(_ev.gameObject.scene);

        Debug.Log($"[GripTuner] Captured item {_itemId}: pos {t.localPosition}, euler {t.localEulerAngles}, scale {scale:0.###}. Save the scene to keep it.");
        Clear();
    }

    EquipmentVisuals.Grip CurrentGrip(int id)
    {
        if (_ev != null)
        {
            int i = _ev.grips.FindIndex(x => x.itemId == id);
            if (i >= 0) return _ev.grips[i];
            return new EquipmentVisuals.Grip { itemId = id, position = _ev.defaultPosition, euler = _ev.defaultEuler, scale = _ev.defaultScale };
        }
        return new EquipmentVisuals.Grip { itemId = id, scale = 1f };
    }

    void Clear()
    {
        if (_preview != null) DestroyImmediate(_preview);
        _preview = null;
    }

    void Fail(string msg) => EditorUtility.DisplayDialog("Grip Tuner", msg, "OK");
}
