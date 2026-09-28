using UnityEditor;
using UnityEngine;

/// <summary>
/// Build-time scale helper — gives you something human-sized to eyeball props/terrain against so you
/// aren't guessing map scale. Purely an editor overlay (Scene view only): nothing is added to the
/// scene and nothing ships in a build.
///
///   • Draws a ~1.8 m stick-figure "person" in the Scene view with height ticks (1 m / ref height).
///   • Drag it anywhere with the move handle to hold it up next to a wall, door, tree, crate…
///   • Select ANY object and it reports that object's size in metres AND in "× human heights"
///     (e.g. "door: 5.4 m = 3.0× human → too big"), and can scale the selection to a target
///     number of human-heights in one click.
///
/// Toggle: Wasteland ▸ World ▸ Human Scale Reference (on/off). Reference height + settings persist.
/// </summary>
public static class HumanScaleRef
{
    const string ActiveKey = "Wasteland.HumanScaleRef.Active";
    const string HeightKey = "Wasteland.HumanScaleRef.Height";
    const string PosKey     = "Wasteland.HumanScaleRef.Pos";

    static bool Active { get => EditorPrefs.GetBool(ActiveKey, false); set => EditorPrefs.SetBool(ActiveKey, value); }
    static float RefHeight { get => EditorPrefs.GetFloat(HeightKey, 1.8f); set => EditorPrefs.SetFloat(HeightKey, value); }

    static Vector3 Pos
    {
        get
        {
            var s = EditorPrefs.GetString(PosKey, "0;0;0").Split(';');
            return s.Length == 3 &&
                   float.TryParse(s[0], out var x) && float.TryParse(s[1], out var y) && float.TryParse(s[2], out var z)
                   ? new Vector3(x, y, z) : Vector3.zero;
        }
        set => EditorPrefs.SetString(PosKey, $"{value.x};{value.y};{value.z}");
    }

    [MenuItem("Wasteland/World/Human Scale Reference (toggle)")]
    static void Toggle()
    {
        Active = !Active;
        if (Active)
        {
            SceneView.duringSceneGui -= OnScene;
            SceneView.duringSceneGui += OnScene;
            // Drop the figure at the current Scene-view focus so it's on screen right away.
            if (SceneView.lastActiveSceneView != null) Pos = SceneView.lastActiveSceneView.pivot;
        }
        else SceneView.duringSceneGui -= OnScene;

        SceneView.RepaintAll();
        Debug.Log($"[HumanScaleRef] {(Active ? "ON" : "off")} — reference height {RefHeight:0.0} m. " +
                  "Toggle again from Wasteland ▸ World.");
    }

    [MenuItem("Wasteland/World/Move Human Reference Here (scene view center)")]
    static void MoveHere()
    {
        if (SceneView.lastActiveSceneView != null) { Pos = SceneView.lastActiveSceneView.pivot; SceneView.RepaintAll(); }
    }

    // Re-hook after a domain reload if it was left on.
    [InitializeOnLoadMethod]
    static void Rehook()
    {
        if (Active) { SceneView.duringSceneGui -= OnScene; SceneView.duringSceneGui += OnScene; }
    }

    static void OnScene(SceneView view)
    {
        if (!Active) return;

        DrawSelectionReadout();

        // Movable figure.
        EditorGUI.BeginChangeCheck();
        Vector3 p = Handles.PositionHandle(Pos, Quaternion.identity);
        if (EditorGUI.EndChangeCheck()) Pos = p;

        DrawPerson(Pos, RefHeight);

        // Small overlay panel: reference height + quick scale action.
        Handles.BeginGUI();
        var r = new Rect(12, 12, 250, 118);
        GUILayout.BeginArea(r, GUI.skin.box);
        GUILayout.Label("Human Scale Reference", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Height: {RefHeight:0.00} m", GUILayout.Width(110));
        if (GUILayout.Button("1.8", GUILayout.Width(38))) RefHeight = 1.8f;
        if (GUILayout.Button("−", GUILayout.Width(24))) RefHeight = Mathf.Max(0.5f, RefHeight - 0.1f);
        if (GUILayout.Button("+", GUILayout.Width(24))) RefHeight += 0.1f;
        GUILayout.EndHorizontal();

        var sel = Selection.activeGameObject;
        if (sel != null && TryBounds(sel, out var b))
        {
            GUILayout.Label($"Selected: {b.size.y:0.00} m tall ({b.size.y / RefHeight:0.0}× human)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale to", GUILayout.Width(52));
            if (GUILayout.Button("1×", GUILayout.Width(34))) ScaleSelectionToHumans(1f);
            if (GUILayout.Button("2×", GUILayout.Width(34))) ScaleSelectionToHumans(2f);
            if (GUILayout.Button("3×", GUILayout.Width(34))) ScaleSelectionToHumans(3f);
            GUILayout.EndHorizontal();
        }
        else GUILayout.Label("Select an object to measure it.");
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    // ── selection measurement ──────────────────────────────────────────────
    static void DrawSelectionReadout()
    {
        var sel = Selection.activeGameObject;
        if (sel == null || !TryBounds(sel, out var b)) return;

        Handles.color = new Color(1f, 0.8f, 0.2f, 1f);
        Handles.DrawWireCube(b.center, b.size);
        Handles.Label(b.center + Vector3.up * (b.size.y * 0.5f + 0.2f),
            $"{b.size.x:0.0} × {b.size.y:0.0} × {b.size.z:0.0} m\n{b.size.y / RefHeight:0.0}× human tall");
    }

    static bool TryBounds(GameObject go, out Bounds b)
    {
        b = default; bool has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has)   // no renderers → fall back to colliders
            foreach (var c in go.GetComponentsInChildren<Collider>())
            {
                if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
            }
        return has;
    }

    static void ScaleSelectionToHumans(float humans)
    {
        var go = Selection.activeGameObject;
        if (go == null || !TryBounds(go, out var b) || b.size.y < 1e-4f) return;
        float target = humans * RefHeight;
        float factor = target / b.size.y;
        Undo.RecordObject(go.transform, "Scale To Human Reference");
        go.transform.localScale *= factor;
        Debug.Log($"[HumanScaleRef] Scaled '{go.name}' ×{factor:0.000} → {target:0.0} m ({humans:0.0}× human).");
    }

    // ── the reference figure (a simple, instantly-readable person) ───────────
    static void DrawPerson(Vector3 feet, float h)
    {
        Handles.color = new Color(0.2f, 0.9f, 1f, 1f);

        // Proportions as fractions of total height.
        Vector3 hip      = feet + Vector3.up * (h * 0.50f);
        Vector3 shoulder = feet + Vector3.up * (h * 0.82f);
        Vector3 neck     = feet + Vector3.up * (h * 0.86f);
        Vector3 headC    = feet + Vector3.up * (h * 0.93f);
        float   headR    = h * 0.07f;
        float   halfSh   = h * 0.13f;
        float   halfHip  = h * 0.09f;
        Vector3 handOff  = Vector3.up * (h * 0.48f);   // hands ~hip height
        Vector3 right    = Vector3.right, fwd = Vector3.forward;

        // Spine, shoulders, hips.
        Handles.DrawAAPolyLine(4f, hip, neck);
        Handles.DrawAAPolyLine(4f, shoulder - right * halfSh, shoulder + right * halfSh);
        Handles.DrawAAPolyLine(4f, hip - right * halfHip, hip + right * halfHip);
        // Legs + arms.
        Handles.DrawAAPolyLine(4f, hip - right * halfHip, feet - right * (halfHip * 0.6f));
        Handles.DrawAAPolyLine(4f, hip + right * halfHip, feet + right * (halfHip * 0.6f));
        Handles.DrawAAPolyLine(4f, shoulder - right * halfSh, hip - right * halfHip + handOff * 0f - right * (halfSh * 0.2f));
        Handles.DrawAAPolyLine(4f, shoulder + right * halfSh, hip + right * halfHip - right * 0f + right * (halfSh * 0.2f));
        // Head + a ground footprint disc so it reads as standing on the surface.
        Handles.DrawWireDisc(headC, fwd, headR);
        Handles.DrawWireDisc(headC, right, headR);
        Handles.color = new Color(0.2f, 0.9f, 1f, 0.35f);
        Handles.DrawWireDisc(feet, Vector3.up, halfHip * 1.2f);

        // Height ticks + label.
        Handles.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Handles.DrawDottedLine(feet, feet + Vector3.up * h, 3f);
        for (float m = 1f; m < h; m += 1f)
        {
            Vector3 t = feet + Vector3.up * m;
            Handles.DrawAAPolyLine(2f, t - right * 0.15f, t + right * 0.15f);
            Handles.Label(t + right * 0.2f, $"{m:0} m");
        }
        Handles.Label(feet + Vector3.up * (h + 0.05f), $"{h:0.0} m human");
    }
}
