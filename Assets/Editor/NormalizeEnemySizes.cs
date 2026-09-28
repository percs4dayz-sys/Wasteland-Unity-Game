using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Shrinks oversized enemy models to a normal height and re-arms the runtime normalizer.
///
/// The Kenney enemy models can end up giant (e.g. ~105 m) when their HeightNormalizer3D got
/// disabled (it disables itself after running, and a save can persist that), so the runtime
/// shrink never fires again. This rescales each enemy's "Visual" to TargetHeight right now in
/// edit mode (so it looks right without entering Play), foot-aligns it to the collider bottom,
/// and re-enables HeightNormalizer3D so the authoritative runtime sizing stays on. Undoable.
///
/// Menu:  Wasteland ▸ World ▸ Normalize Enemy Sizes (Now)
/// </summary>
public static class NormalizeEnemySizes
{
    const float TargetHeight = 1.8f;

    [MenuItem("Wasteland/World/Normalize Enemy Sizes (Now)")]
    public static void Run()
    {
        var enemies = Object.FindObjectsByType<CombatTarget>();
        int fixedCount = 0, skipped = 0;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();

        foreach (var ct in enemies)
        {
            var vis = ct.transform.Find("Visual");
            if (vis == null) { skipped++; continue; }   // primitive enemies (e.g. dummy) have no model

            var rends = vis.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) { skipped++; continue; }

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            if (b.size.y <= 1e-4f) { skipped++; continue; }

            Undo.RecordObject(vis, "Normalize Enemy Size");
            vis.localScale *= TargetHeight / b.size.y;

            // foot-align: drop the model so its base sits at the collider's bottom (the ground)
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            var col = ct.GetComponent<Collider>();
            float groundY = col != null ? col.bounds.min.y : ct.transform.position.y;
            vis.position += Vector3.up * (groundY - b.min.y);

            // Turn the runtime normalizer OFF. These Kenney rigs are authored lying down and
            // stood up by a -90° rotation, which makes the runtime height measurement read the
            // wrong axis and inflate the model to ~100x on Play. The edit-time size baked just
            // above (from valid edit-mode bounds) is reliable, so we don't need — and must not
            // run — the runtime pass.
            var norm = vis.GetComponent<HeightNormalizer3D>();
            if (norm != null && norm.enabled)
            {
                Undo.RecordObject(norm, "Disable HeightNormalizer3D");
                norm.enabled = false;
            }
            fixedCount++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        string msg = $"Normalized {fixedCount} enem{(fixedCount == 1 ? "y" : "ies")} to {TargetHeight} m" +
                     (skipped > 0 ? $" ({skipped} skipped — no model/Visual)" : "") +
                     ".\n\nThey should look normal-sized now. Save (Ctrl+S).";
        Debug.Log($"[NormalizeEnemySizes] fixed={fixedCount} skipped={skipped}");
        EditorUtility.DisplayDialog("Normalize Enemy Sizes", msg, "OK");
    }
}
