using UnityEditor;
using UnityEngine;

/// <summary>
/// Fixes "imported art is giant / microscopic" without guessing scale numbers. Select the object(s),
/// type the real-world HEIGHT in metres, and click Normalize — each one is uniformly rescaled so its
/// model height matches, and (optionally) dropped so its feet sit on the ground (y = 0).
///
/// Menu:  Wasteland ▸ Normalize Scale
/// </summary>
public class ScaleNormalizerTool : EditorWindow
{
    float _targetHeight = 1.8f;
    bool  _dropToGround = true;

    [MenuItem("Wasteland/Normalize Scale")]
    static void Open() => GetWindow<ScaleNormalizerTool>("Normalize Scale");

    void OnSelectionChange() => Repaint();

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Select object(s) in the scene, set the real-world HEIGHT in metres, then Normalize.\n" +
            "Reference heights: player ≈ 1.8 m, door ≈ 2.2 m, one storey ≈ 3 m, tree ≈ 6 m.",
            MessageType.Info);

        _targetHeight = EditorGUILayout.FloatField("Target height (m)", _targetHeight);
        _dropToGround = EditorGUILayout.ToggleLeft("Drop bottom to ground (y = 0)", _dropToGround);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Quick presets", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Person 1.8")) _targetHeight = 1.8f;
        if (GUILayout.Button("Door 2.2"))   _targetHeight = 2.2f;
        if (GUILayout.Button("Storey 3"))   _targetHeight = 3f;
        if (GUILayout.Button("Tree 6"))     _targetHeight = 6f;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        int n = Selection.gameObjects.Length;
        using (new EditorGUI.DisabledScope(n == 0))
            if (GUILayout.Button(n > 0 ? $"Normalize {n} selected object(s)" : "Select something first",
                                 GUILayout.Height(32)))
                Normalize();
    }

    void Normalize()
    {
        int done = 0;
        foreach (var go in Selection.gameObjects)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[Normalize] '{go.name}' has no Renderer — skipped.");
                continue;
            }

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            if (b.size.y <= 0.0001f) continue;

            Undo.RecordObject(go.transform, "Normalize Scale");
            go.transform.localScale *= _targetHeight / b.size.y;   // uniform — keeps proportions

            if (_dropToGround)
            {
                Bounds nb = renderers[0].bounds;                    // bounds recompute after the rescale
                for (int i = 1; i < renderers.Length; i++) nb.Encapsulate(renderers[i].bounds);
                var pos = go.transform.position;
                pos.y -= nb.min.y;                                  // lift lowest point to y = 0
                go.transform.position = pos;
            }
            done++;
        }
        Debug.Log($"[Normalize] Rescaled {done} object(s) to {_targetHeight} m tall.");
    }
}
