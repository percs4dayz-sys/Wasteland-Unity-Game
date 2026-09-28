using UnityEditor;
using UnityEngine;

/// <summary>
/// Quick scene-view locking so you stop accidentally clicking/dragging the ground, the player, or an
/// NPC while working. Locked objects still render and still work in-game — they just can't be picked
/// or moved in the Scene view. Uses Unity's built-in pickability (same as the Hierarchy hand icon),
/// so the state is saved with the scene.
///   • Ctrl+Shift+L — lock the selected object(s) (and their children)
///   • Ctrl+Shift+U — unlock everything
/// </summary>
public static class LockObjectsTool
{
    [MenuItem("Wasteland/Lock ▸ Lock Selected (can't click or move)  %#l")]
    static void LockSelected()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        {
            Debug.LogWarning("[Lock] Select object(s) in the Hierarchy or Scene first.");
            return;
        }
        foreach (var go in sel)
            SceneVisibilityManager.instance.DisablePicking(go, true);   // true = include children
        Debug.Log($"[Lock] Locked {sel.Length} object(s) from scene-view clicks. Unlock with Ctrl+Shift+U.");
    }

    [MenuItem("Wasteland/Lock ▸ Unlock All  %#u")]
    static void UnlockAll()
    {
        SceneVisibilityManager.instance.EnableAllPicking();
        Debug.Log("[Lock] Everything is pickable again.");
    }
}
