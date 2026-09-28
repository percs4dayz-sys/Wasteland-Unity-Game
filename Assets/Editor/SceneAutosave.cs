using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SceneAutosave
{
    const double INTERVAL_MINUTES = 5.0;

    static double s_NextSaveTime;

    static SceneAutosave()
    {
        s_NextSaveTime = EditorApplication.timeSinceStartup + INTERVAL_MINUTES * 60.0;
        EditorApplication.update += OnUpdate;
    }

    static void OnUpdate()
    {
        if (EditorApplication.isPlaying)
            return;

        if (EditorApplication.timeSinceStartup < s_NextSaveTime)
            return;

        s_NextSaveTime = EditorApplication.timeSinceStartup + INTERVAL_MINUTES * 60.0;

        if (!EditorSceneManager.SaveOpenScenes())
            return;

        Debug.Log($"[Autosave] Scene saved at {System.DateTime.Now:HH:mm:ss}");
    }
}
