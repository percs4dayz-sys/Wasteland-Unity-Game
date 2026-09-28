using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds distance fog to the open scene so you can't see clear across the map (and so distant
/// geometry can be culled for performance). URP honours Unity's built-in RenderSettings fog, so
/// this just configures linear fog + tightens every camera's far-clip to a little past the fog end
/// (objects fully fade into the fog before they're culled — no hard pop).
///
/// Fog settings live in the scene's render settings, so this persists once you save. Tweak the
/// constants below, or fine-tune later in Window ▸ Rendering ▸ Lighting ▸ Environment ▸ Fog.
///
/// Menu:  Wasteland ▸ World ▸ Add Distance Fog   /   Remove Distance Fog
/// </summary>
public static class DistanceFogTool
{
    // Visible distance: fully clear up to START, fully fogged by END (world metres).
    const float FogStart = 25f;
    const float FogEnd   = 110f;
    static readonly Color FogColor = new(0.60f, 0.58f, 0.52f);   // dusty wasteland haze — tweak to taste

    [MenuItem("Wasteland/World/Add Distance Fog")]
    public static void AddFog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogStartDistance = FogStart;
        RenderSettings.fogEndDistance = FogEnd;

        int cams = 0;
        foreach (var cam in Object.FindObjectsByType<Camera>())
        {
            cam.farClipPlane = FogEnd + 15f;   // a touch beyond the fog so nothing visibly culls
            EditorUtility.SetDirty(cam);
            cams++;
        }

        SaveActive();
        EditorUtility.DisplayDialog("Distance Fog",
            $"Linear fog on: clear to {FogStart} m, fully fogged by {FogEnd} m.\n" +
            $"Set far-clip to {FogEnd + 15f} m on {cams} camera(s).\n\n" +
            "Tweak in Window ▸ Rendering ▸ Lighting ▸ Environment ▸ Fog. For thicker haze, raise the " +
            "fog color's brightness or lower the end distance.", "OK");
    }

    [MenuItem("Wasteland/World/Remove Distance Fog")]
    public static void RemoveFog()
    {
        RenderSettings.fog = false;
        foreach (var cam in Object.FindObjectsByType<Camera>())
        {
            cam.farClipPlane = 1000f;   // Unity's default
            EditorUtility.SetDirty(cam);
        }
        SaveActive();
        EditorUtility.DisplayDialog("Distance Fog", "Fog disabled and camera far-clip reset to 1000 m.", "OK");
    }

    static void SaveActive()
    {
        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[DistanceFogTool] Updated fog on '{scene.name}'.");
    }
}
