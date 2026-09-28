using System;

[Serializable]
public class ItemStack
{
    public int itemId;
    public int quantity;
    public int[] modules;
    public bool HasModules => modules != null && System.Array.Exists(modules, id => id > 0);
    public ItemStack Copy(int qty = -1) => new ItemStack(itemId, qty < 0 ? quantity : qty)
        { modules = modules == null ? null : (int[])modules.Clone() };

    public ItemData Item => ItemRegistry.Get(itemId);

    public ItemStack(int itemId, int quantity)
    {
        this.itemId = itemId;
        this.quantity = quantity;
    }
}
