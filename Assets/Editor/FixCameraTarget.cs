using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Re-links the OrbitCamera3D's follow Target to the Player in the open scene.
/// The target reference gets cleared when the Player's visual is swapped, which
/// leaves the camera drifting free of the character. Run this to re-attach it.
///
/// Menu: Tools ▸ Wasteland ▸ Fix Camera Follow Target
/// </summary>
public static class FixCameraTarget
{
    [MenuItem("Tools/Wasteland/Fix Camera Follow Target")]
    public static void Fix()
    {
        var cam = Object.FindAnyObjectByType<OrbitCamera3D>(FindObjectsInactive.Include);
        if (cam == null)
        {
            EditorUtility.DisplayDialog("Fix Camera Target", "No OrbitCamera3D found in this scene.", "OK");
            return;
        }

        var player = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)
            .FirstOrDefault(m => m != null && (m.GetType().Name == "PlayerEntity" || m.GetType().Name == "Player3DController"))
            ?.transform;

        if (player == null)
        {
            EditorUtility.DisplayDialog("Fix Camera Target", "No Player (PlayerEntity / Player3DController) found in this scene.", "OK");
            return;
        }

        Undo.RecordObject(cam, "Fix Camera Target");
        cam.target = player;
        EditorUtility.SetDirty(cam);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[FixCameraTarget] OrbitCamera3D target set to '{player.name}'. Save the scene to keep it.");
    }
}
