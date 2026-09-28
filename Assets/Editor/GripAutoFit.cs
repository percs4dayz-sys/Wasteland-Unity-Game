using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Kills the slow part of grip tuning: finding the right ROTATION.
///
/// Weapons import at arbitrary orientations — some run along +Z, some +Y, some are rotated 90°.
/// Hunting for that by dragging gizmos is what turns two weapons into two hours. This works it
/// out from the mesh instead: the longest axis of the bounding box is the blade/barrel, so align
/// that down the hand's forward and put the grip end in the palm.
///
/// It won't be pixel-perfect, but it lands within a nudge instead of a hunt. Then:
///   • "Flip" if it guessed the wrong end (blade pointing backwards)
///   • Quick ±90° buttons instead of gizmo dragging
///   • "Copy to items…" — tune one sword, apply it to every other sword
///
/// Menu:  Wasteland ▸ NewPlayer ▸ Grip Auto-Fit
/// </summary>
public class GripAutoFit : EditorWindow
{
    Vector2 _scroll;
    string _copyTargets = "";
    int _sourceItem = 1;

    [MenuItem("Wasteland/NewPlayer/Grip Auto-Fit", false, 1)]
    static void Open() => GetWindow<GripAutoFit>("Grip Auto-Fit").minSize = new Vector2(340, 320);

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.HelpBox(
            "Use the Grip Tuner to spawn a weapon on the hand, then come here.\n\n" +
            "Auto-Fit orients it from the mesh's long axis so it's roughly right immediately. " +
            "Nudge with the buttons, then Capture in the Grip Tuner.",
            MessageType.Info);

        var preview = FindPreview();
        EditorGUILayout.LabelField("Current preview",
            preview != null ? preview.name : "none — spawn one in Grip Tuner first",
            EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(preview == null))
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Orient", EditorStyles.boldLabel);

            if (GUILayout.Button("Auto-Fit  (align long axis down the hand)", GUILayout.Height(30)))
                AutoFit(preview.transform, false);

            if (GUILayout.Button("Auto-Fit, flipped  (blade the other way)", GUILayout.Height(24)))
                AutoFit(preview.transform, true);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Nudge rotation", EditorStyles.miniBoldLabel);
            DrawAxisRow(preview.transform, "X", Vector3.right);
            DrawAxisRow(preview.transform, "Y", Vector3.up);
            DrawAxisRow(preview.transform, "Z", Vector3.forward);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Nudge position (cm, in hand space)", EditorStyles.miniBoldLabel);
            DrawMoveRow(preview.transform);
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Batch: copy one grip to many", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Tune ONE sword properly, then copy that grip to every other sword. " +
            "Similar weapons almost always share a grip.", MessageType.None);

        _sourceItem = EditorGUILayout.IntField("Copy grip FROM item id", _sourceItem);
        _copyTargets = EditorGUILayout.TextField("TO item ids (comma sep)", _copyTargets);

        if (GUILayout.Button("Copy grip to those items", GUILayout.Height(26)))
            CopyGrip(_sourceItem, _copyTargets);

        EditorGUILayout.Space(8);
        if (GUILayout.Button("List all tuned grips"))
            ListGrips();

        EditorGUILayout.EndScrollView();
    }

    void DrawAxisRow(Transform t, string label, Vector3 axis)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(16));
        foreach (int d in new[] { -90, -15, -5, 5, 15, 90 })
            if (GUILayout.Button(d > 0 ? "+" + d : d.ToString()))
            {
                Undo.RecordObject(t, "Nudge grip");
                t.Rotate(axis, d, Space.Self);
            }
        EditorGUILayout.EndHorizontal();
    }

    void DrawMoveRow(Transform t)
    {
        EditorGUILayout.BeginHorizontal();
        foreach (var (lbl, dir) in new (string, Vector3)[]
                 { ("-X", Vector3.left), ("+X", Vector3.right),
                   ("-Y", Vector3.down), ("+Y", Vector3.up),
                   ("-Z", Vector3.back), ("+Z", Vector3.forward) })
            if (GUILayout.Button(lbl))
            {
                Undo.RecordObject(t, "Nudge grip");
                t.localPosition += dir * 0.01f;
            }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Orient from the mesh itself. The longest bounding-box axis is the blade or barrel;
    /// point that along the hand's forward. Then slide the model so the grip END sits in the
    /// palm rather than the weapon's centre.
    /// </summary>
    static void AutoFit(Transform t, bool flip)
    {
        var rends = t.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0)
        {
            Debug.LogWarning("[GripAutoFit] Preview has no renderers to measure.");
            return;
        }

        Undo.RecordObject(t, "Auto-fit grip");

        // Measure in the model's own space, ignoring whatever rotation it currently has.
        Quaternion prevRot = t.localRotation;
        Vector3 prevPos = t.localPosition;
        t.localRotation = Quaternion.identity;
        t.localPosition = Vector3.zero;

        Bounds local = LocalBounds(t, rends);
        Vector3 size = local.size;

        // Longest axis = the weapon's length.
        Vector3 longAxis = size.x >= size.y && size.x >= size.z ? Vector3.right
                         : size.y >= size.z ? Vector3.up
                         : Vector3.forward;
        float length = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

        // Point the length down the hand's local forward (+Z). Flip swaps which end leads.
        Vector3 want = flip ? Vector3.back : Vector3.forward;
        t.localRotation = Quaternion.FromToRotation(longAxis, want);

        // Put the grip end at the palm: shift back along the weapon by half its length, so the
        // near end sits at the origin rather than the middle of the blade.
        Vector3 centreOffset = t.localRotation * local.center;
        t.localPosition = -centreOffset + want * (length * 0.5f) * (flip ? -1f : 1f) * -1f;

        Debug.Log($"[GripAutoFit] {t.name}: long axis {longAxis}, length {length:0.00} m" +
                  (flip ? " (flipped)" : "") + " — nudge from here, then Capture in Grip Tuner.");
    }

    static Bounds LocalBounds(Transform root, Renderer[] rends)
    {
        var b = new Bounds();
        bool first = true;
        foreach (var r in rends)
        {
            var mf = r.GetComponent<MeshFilter>();
            Mesh m = mf != null ? mf.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
            if (m == null) continue;
            Bounds mb = m.bounds;
            Vector3 c = root.InverseTransformPoint(r.transform.TransformPoint(mb.center));
            Vector3 e = mb.extents;
            var bb = new Bounds(c, e * 2f);
            if (first) { b = bb; first = false; } else b.Encapsulate(bb);
        }
        return first ? new Bounds(Vector3.zero, Vector3.one * 0.1f) : b;
    }

    static GameObject FindPreview()
    {
        foreach (var go in Object.FindObjectsByType<GameObject>())
            if (go.name.StartsWith("GRIP_TUNE_")) return go;
        return null;
    }

    // ── batch copy ───────────────────────────────────────────────────────────

    static void CopyGrip(int from, string csv)
    {
        var ev = Object.FindAnyObjectByType<EquipmentVisuals>(FindObjectsInactive.Include);
        if (ev == null) { Debug.LogError("[GripAutoFit] No EquipmentVisuals in the open scene."); return; }

        int srcIndex = ev.grips.FindIndex(g => g.itemId == from);
        if (srcIndex < 0)
        {
            Debug.LogError($"[GripAutoFit] No tuned grip for item {from}. Tune that one first.");
            return;
        }
        var src = ev.grips[srcIndex];

        var ids = new List<int>();
        foreach (var part in csv.Split(','))
            if (int.TryParse(part.Trim(), out int id) && id != from) ids.Add(id);

        if (ids.Count == 0) { Debug.LogWarning("[GripAutoFit] No valid target ids."); return; }

        Undo.RecordObject(ev, "Copy grip to items");
        var log = new StringBuilder($"=== Copy grip from item {from} ===\n");
        foreach (int id in ids)
        {
            var g = src;
            g.itemId = id;
            int at = ev.grips.FindIndex(x => x.itemId == id);
            if (at >= 0) { ev.grips[at] = g; log.AppendLine($"   overwrote item {id}"); }
            else { ev.grips.Add(g); log.AppendLine($"   added item {id}"); }
        }
        EditorUtility.SetDirty(ev);
        log.AppendLine($"\n{ids.Count} item(s) now share item {from}'s grip. Save the scene.");
        Debug.Log(log.ToString());
    }

    static void ListGrips()
    {
        var ev = Object.FindAnyObjectByType<EquipmentVisuals>(FindObjectsInactive.Include);
        if (ev == null) { Debug.LogError("[GripAutoFit] No EquipmentVisuals in the open scene."); return; }

        var sb = new StringBuilder($"=== Tuned grips ({ev.grips.Count}) ===\n");
        foreach (var g in ev.grips)
        {
            var item = ItemRegistry.Get(g.itemId);
            sb.AppendLine($"   id {g.itemId,-4} {(item != null ? item.name : "?"),-24} " +
                          $"pos {g.position}  euler {g.euler}  scale {g.scale:0.###}");
        }
        Debug.Log(sb.ToString());
    }
}
