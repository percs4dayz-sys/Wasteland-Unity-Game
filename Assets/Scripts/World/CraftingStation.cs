using System.Collections.Generic;
using UnityEngine;

public enum StationType { Furnace, CookingFire, Workbench }

public class CraftingStation : MonoBehaviour, IExaminable
{
    [SerializeField] public StationType stationType;
    [SerializeField] public List<CraftingRecipe> recipes = new();
    [SerializeField] public bool useCatalogRecipes = true;

    /// <summary>If no recipes were assigned in the scene, pull the full set for this station type from
    /// the central recipe DB. Lets a station be dropped in (e.g. a generated/baked crafting hub) with
    /// only its <see cref="stationType"/> set and still work. Manually-populated stations are untouched.</summary>
    void Awake()
    {
        if (!useCatalogRecipes && recipes != null && recipes.Count > 0) return;
        recipes = stationType switch
        {
            StationType.Furnace     => CraftingRecipes.Furnace(),
            StationType.CookingFire => CraftingRecipes.Cooking(),
            StationType.Workbench   => CraftingRecipes.Workbench(),
            _                       => recipes
        };
    }

    public string DisplayName => stationType switch
    {
        StationType.Furnace          => "Furnace",
        StationType.CookingFire      => "Cooking Fire",
        StationType.Workbench        => "Workbench",
        _                            => "Crafting Station"
    };

    public string ExamineText => stationType switch
    {
        StationType.Furnace          => "A makeshift furnace for smelting scrap into bars.",
        StationType.CookingFire      => "A crackling fire. Good for cooking raw food.",
        StationType.Workbench        => "A sturdy workbench for assembling firearms, melee weapons, and armor.",
        _                            => "A station for crafting."
    };

    public (bool success, string message) TryCraft(PlayerEntity player)
    {
        foreach (var recipe in recipes)
        {
            if (player.Stats.GetLevel(recipe.skill) < recipe.levelRequired) continue;
            if (!recipe.CanCraft(player.Inventory)) continue;
            return CraftingManager.CraftOne(recipe, player);
        }
        return (false, "You can't craft anything here right now.");
    }
}
