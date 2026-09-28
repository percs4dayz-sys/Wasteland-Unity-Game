using System;

public class Inventory
{
    public const int SIZE = 28;     // base capacity — the standard OSRS-style grid
    public const int MAX  = 34;     // base + the biggest companion saddlebag

    private readonly ItemStack[] _slots = new ItemStack[MAX];
    private int _bonusSlots;

    public event Action OnChanged;

    /// <summary>Extra slots granted by a companion (the shellback's saddlebag).
    /// Shrinking the bonus never deletes items — overflow slots stay readable and
    /// removable, they just refuse NEW items until the beast is back out.</summary>
    public int BonusSlots
    {
        get => _bonusSlots;
        set
        {
            int v = Math.Max(0, Math.Min(value, MAX - SIZE));
            if (v == _bonusSlots) return;
            _bonusSlots = v;
            OnChanged?.Invoke();
        }
    }

    public int Capacity => SIZE + _bonusSlots;

    public ItemStack GetSlot(int i) => (i >= 0 && i < MAX) ? _slots[i] : null;

    /// <summary>Raw slot placement for save loading — bypasses capacity so items
    /// saved in saddlebag slots survive a load where the beast isn't summoned yet.</summary>
    public void SetSlotRaw(int i, ItemStack stack)
    {
        if (i < 0 || i >= MAX) return;
        _slots[i] = stack;
        OnChanged?.Invoke();
    }

    public int FreeSlots()
    {
        int c = 0;
        for (int i = 0; i < Capacity; i++) if (_slots[i] == null) c++;
        return c;
    }

    public bool IsFull() => FreeSlots() == 0;

    public bool Add(int itemId, int qty = 1)
    {
        if (qty <= 0) return false;
        var item = ItemRegistry.Get(itemId);
        if (item == null)
        {
            UnityEngine.Debug.LogWarning("[Inventory] Failed to add item " + itemId + ": Not in registry.");
            return false;
        }

        UnityEngine.Debug.Log("[Inventory] Adding " + qty + "x " + item.name + " (ID: " + itemId + ")");

        if (item.stackable)
        {
            // Merge into an existing stack anywhere, including overflow slots.
            for (int i = 0; i < MAX; i++)
            {
                if (_slots[i]?.itemId == itemId)
                {
                    _slots[i].quantity += qty;
                    OnChanged?.Invoke();
                    return true;
                }
            }
        }

        if (!item.stackable && FreeSlots() < qty) return false;
        int remaining = qty;
        for (int i = 0; i < Capacity; i++)
        {
            if (_slots[i] == null)
            {
                int take = item.stackable ? remaining : 1;
                _slots[i] = new ItemStack(itemId, take);
                remaining -= take;
                if (remaining == 0) { OnChanged?.Invoke(); return true; }
            }
        }
        return false;
    }

    public bool Remove(int itemId, int qty = 1)
    {
        if (qty <= 0) return false;
        // Must remove ACROSS multiple slots — non-stackable items each occupy their own
        // slot (qty 1), so a recipe needing 3 scrap spans 3 slots. (This was the furnace dupe:
        // the old single-slot check found qty 1 < 3, removed nothing, but crafting still ran.)
        if (CountOf(itemId) < qty) return false;

        int remaining = qty;
        for (int i = 0; i < MAX && remaining > 0; i++)
        {
            if (_slots[i]?.itemId != itemId) continue;
            int take = Math.Min(remaining, _slots[i].quantity);
            _slots[i].quantity -= take;
            remaining -= take;
            if (_slots[i].quantity <= 0) _slots[i] = null;
        }
        OnChanged?.Invoke();
        return true;
    }

    public bool Contains(int itemId, int qty = 1)
    {
        int total = 0;
        foreach (var s in _slots)
            if (s?.itemId == itemId) total += s.quantity;
        return total >= qty;
    }

    public bool Add(ItemStack stack)
    {
        if (stack == null || stack.quantity <= 0) return false;
        if (!stack.HasModules) return Add(stack.itemId, stack.quantity);
        if (stack.quantity != 1 || ItemRegistry.Get(stack.itemId) == null) return false;
        for (int i = 0; i < Capacity; i++)
            if (_slots[i] == null) { _slots[i] = stack.Copy(); OnChanged?.Invoke(); return true; }
        return false;
    }

    public bool RemoveAt(int index, int qty = 1)
    {
        var s = GetSlot(index);
        if (s == null || qty <= 0 || s.quantity < qty) return false;
        s.quantity -= qty;
        if (s.quantity == 0) _slots[index] = null;
        OnChanged?.Invoke();
        return true;
    }

    public ItemStack[] Snapshot()
    {
        var copy = new ItemStack[MAX];
        for (int i = 0; i < MAX; i++) copy[i] = _slots[i]?.Copy();
        return copy;
    }

    public void Restore(ItemStack[] snapshot)
    {
        for (int i = 0; i < MAX; i++) _slots[i] = snapshot[i]?.Copy();
        OnChanged?.Invoke();
    }

    public int CountOf(int itemId)
    {
        int total = 0;
        foreach (var s in _slots)
            if (s?.itemId == itemId) total += s.quantity;
        return total;
    }

    public int CountOfAny()
    {
        int total = 0;
        foreach (var s in _slots)
            if (s != null) total += s.quantity;
        return total;
    }

    public ItemStack FindFirst(int itemId)
    {
        foreach (var s in _slots)
            if (s?.itemId == itemId) return s;
        return null;
    }

    public void Clear()
    {
        Array.Clear(_slots, 0, MAX);
        OnChanged?.Invoke();
    }

    public void Swap(int indexA, int indexB)
    {
        if (indexA < 0 || indexA >= MAX || indexB < 0 || indexB >= MAX) return;
        if (indexA == indexB) return;
        // Locked (beyond-capacity) slots can give items up but not receive them.
        if (!SlotUsable(indexA) || !SlotUsable(indexB)) return;

        var temp = _slots[indexA];
        _slots[indexA] = _slots[indexB];
        _slots[indexB] = temp;

        OnChanged?.Invoke();
    }

    private bool SlotUsable(int i) => i < Capacity || _slots[i] != null;
}
