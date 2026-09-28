using System;

public class Bank
{
    public const int SIZE = 300;
    private readonly ItemStack[] _slots = new ItemStack[SIZE];

    public event Action OnChanged;

    public ItemStack GetSlot(int i) => _slots[i];

    public int FreeSlots()
    {
        int c = 0;
        foreach (var s in _slots) if (s == null) c++;
        return c;
    }

    public bool IsFull() => FreeSlots() == 0;

    public bool Deposit(int itemId, int qty = 1)
        => Deposit(new ItemStack(itemId, qty));

    public bool Deposit(ItemStack stack)
    {
        if (stack == null || stack.quantity <= 0) return false;
        int itemId = stack.itemId, qty = stack.quantity;
        var item = ItemRegistry.Get(itemId);
        if (item == null) return false;

        // The bank ALWAYS stacks (OSRS-style), even for items that don't stack in the inventory.
        for (int i = 0; i < SIZE; i++)
        {
            if (!stack.HasModules && _slots[i]?.itemId == itemId && !_slots[i].HasModules)
            {
                _slots[i].quantity += qty;
                OnChanged?.Invoke();
                return true;
            }
        }

        for (int i = 0; i < SIZE; i++)
        {
            if (_slots[i] == null)
            {
                _slots[i] = stack.Copy();
                OnChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public bool Withdraw(int itemId, int qty = 1)
    {
        for (int i = 0; i < SIZE; i++)
        {
            if (_slots[i]?.itemId == itemId)
            {
                if (_slots[i].quantity < qty) return false;
                _slots[i].quantity -= qty;
                if (_slots[i].quantity == 0) _slots[i] = null;
                OnChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public bool WithdrawAt(int index, int qty = 1)
    {
        if (index < 0 || index >= SIZE || qty <= 0 || _slots[index] == null || _slots[index].quantity < qty) return false;
        _slots[index].quantity -= qty;
        if (_slots[index].quantity == 0) _slots[index] = null;
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

    public int CountOf(int itemId)
    {
        int total = 0;
        foreach (var s in _slots)
            if (s?.itemId == itemId) total += s.quantity;
        return total;
    }

    public void Clear()
    {
        Array.Clear(_slots, 0, SIZE);
        OnChanged?.Invoke();
    }
}
