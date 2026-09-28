using System;
using System.Collections.Generic;

[Serializable]
public class CraftingIngredient
{
    public int itemId;
    public int quantity;
}

[Serializable]
public class CraftingRecipe
{
    public string name;
    public List<CraftingIngredient> inputs = new();
    public int outputItemId;
    public int outputQty = 1;
    public Skill skill;
    public int levelRequired = 1;
    public int xpGranted;
    public string requiredFlag;

    // Cooking burn (0 = never burns). When burnItemId is set, a level-scaled chance
    // produces burnItemId instead of the normal output and grants no XP. The chance
    // falls linearly from levelRequired (highest) to noBurnLevel (zero).
    public int burnItemId = 0;
    public int noBurnLevel = 0;

    public bool CanCraft(Inventory inv)
    {
        foreach (var i in inputs)
            if (!inv.Contains(i.itemId, i.quantity)) return false;
        return true;
    }

    public void Consume(Inventory inv)
    {
        foreach (var i in inputs)
            inv.Remove(i.itemId, i.quantity);
    }
}
