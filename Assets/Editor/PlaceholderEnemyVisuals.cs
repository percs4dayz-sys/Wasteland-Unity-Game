using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Replaces each enemy's animated Kenney model with a simple sized capsule.
///
/// The Kenney rigs' animation clips contain baked scale curves that drive the model back to
/// its authored ~100x scale every frame on Play, so it can't be sized reliably. Until proper
/// enemy art is in, this swaps the giant "Visual" for a plain capsule (no animator, no scale
/// curves) sized to the enemy's collider, and clears the cached Animator so Enemy3D won't poke
/// at it. Combat, clicking, AI and the collider are untouched. Undoable.
///
/// Menu:  Wasteland ▸ World ▸ Use Placeholder Enemy Capsules
/// </summary>
public static class PlaceholderEnemyVisuals
{
    const string MatFolder = "Assets/Art/Generated3D";

    [MenuItem("Wasteland/World/Use Placeholder Enemy Capsules")]
    public static void Run()
    {
        var enemies = Object.FindObjectsByType<CombatTarget>();
        int done = 0;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();

        foreach (var ct in enemies)
        {
            var t = ct.transform;

            // Remove the old animated model(s).
            bool hadVisual = false;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i);
                if (child.name == "Visual") { Undo.DestroyObjectImmediate(child.gameObject); hadVisual = true; }
            }
            if (!hadVisual) continue;   // primitive enemies (e.g. the training dummy) already fine

            // Build a plain capsule sized to the enemy's capsule collider.
            var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cap.name = "Visual";
            Object.DestroyImmediate(cap.GetComponent<Collider>());   // the enemy root keeps the real collider
            Undo.RegisterCreatedObjectUndo(cap, "Placeholder Enemy Capsule");
            cap.transform.SetParent(t, false);

            var col = ct.GetComponent<CapsuleCollider>();
            if (col != null)
            {
                cap.transform.localPosition = col.center;
                // Unity's primitive capsule is 2 m tall, 0.5 m radius at scale 1 — match the collider.
                cap.transform.localScale = new Vector3(col.radius / 0.5f, col.height / 2f, col.radius / 0.5f);
            }
            else
            {
                cap.transform.localPosition = new Vector3(0f, 1f, 0f);
            }

            Color color = ct.isDummy            ? new Color(0.75f, 0.62f, 0.40f)   // tan dummy
                        : ct.isAggressive       ? new Color(0.60f, 0.20f, 0.18f)   // red hostile
                                                : new Color(0.55f, 0.45f, 0.25f);  // ochre passive
            cap.GetComponent<Renderer>().sharedMaterial = Mat(ct.name.Replace(" ", ""), color);

            // Enemy3D caches the Animator in Awake; nothing to clear at edit time, but the new
            // capsule has none so it stays null at runtime.
            done++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        string msg = $"Replaced {done} enemy model(s) with placeholder capsules.\n\n" +
                     "Press Play — enemies are normal-sized now. Save (Ctrl+S).";
        Debug.Log($"[PlaceholderEnemyVisuals] replaced {done}");
        EditorUtility.DisplayDialog("Placeholder Enemy Capsules", msg, "OK");
    }

    static Material Mat(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder(MatFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            AssetDatabase.CreateFolder("Assets/Art", "Generated3D");
        }
        string path = $"{MatFolder}/EnemyCapsule_{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { existing.color = color; return existing; }

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
