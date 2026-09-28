using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Re-pushes the current CraftingRecipes definitions onto the CraftingStation components in
/// every open scene, WITHOUT rebuilding the scene (so terrain, props and manual tweaks survive).
///
/// Use this after editing CraftingRecipes.cs to update existing scenes — running the full
/// World3DBuilder / TutorialIsland3DBuilder would regenerate the scene from scratch and lose
/// hand-authored work. Furnace, Workbench and Cooking Fire stations are all re-pushed;
/// any other station types are left alone.
/// </summary>
public static class RefreshCraftingRecipes
{
    [MenuItem("Wasteland/Refresh Crafting Recipes")]
    public static void Refresh()
    {
        int touched = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
            foreach (var station in root.GetComponentsInChildren<CraftingStation>(true))
            {
                switch (station.stationType)
                {
                    case StationType.Furnace:     station.recipes = CraftingRecipes.Furnace();   break;
                    case StationType.Workbench:   station.recipes = CraftingRecipes.Workbench(); break;
                    case StationType.CookingFire: station.recipes = CraftingRecipes.Cooking();   break;
                    default: continue;   // anything else untouched
                }
                EditorUtility.SetDirty(station);
                touched++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[RefreshCraftingRecipes] Updated recipes on {touched} station(s) across open scenes.");
        EditorUtility.DisplayDialog("Crafting Recipes Refreshed",
            $"Pushed the latest Furnace/Workbench/Cooking Fire recipes onto {touched} station(s) in the open scene(s).",
            "OK");
    }
}
