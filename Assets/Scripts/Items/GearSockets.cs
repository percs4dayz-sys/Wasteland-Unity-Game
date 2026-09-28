using System.Collections.Generic;

/// <summary>
/// Legacy standalone socket container, retained for editor/tool compatibility.
/// The live system stores sockets on individual ItemStacks and equipped gear;
/// use ModuleCatalog.Socket to edit player equipment without losing per-copy data.
/// </summary>
public class GearSockets
{
    // slot -> module item ids, one entry per socket (0 = empty)
    readonly Dictionary<string, int[]> _sockets = new Dictionary<string, int[]>();

    public event System.Action OnChanged;

    /// <summary>How many sockets the item currently in this slot has. 0 if empty or socketless.</summary>
    public static int CapacityOf(ItemData item) => item?.socketCount ?? 0;

    /// <summary>The module ids socketed into a slot. Never null; length matches the item's sockets.</summary>
    public int[] Get(string slot, ItemData worn)
    {
        int cap = CapacityOf(worn);
        if (cap <= 0) return System.Array.Empty<int>();

        if (!_sockets.TryGetValue(slot, out var arr) || arr.Length != cap)
        {
            var resized = new int[cap];
            if (arr != null)
                for (int i = 0; i < cap && i < arr.Length; i++) resized[i] = arr[i];
            _sockets[slot] = resized;
            arr = resized;
        }
        return arr;
    }

    /// <summary>Socket a module. Returns the module it displaced, or 0.</summary>
    public int Socket(string slot, ItemData worn, int index, int moduleId)
    {
        var arr = Get(slot, worn);
        if (index < 0 || index >= arr.Length) return 0;
        int old = arr[index];
        arr[index] = moduleId;
        OnChanged?.Invoke();
        return old;
    }

    /// <summary>Pull a module back out. Returns what was there, or 0.</summary>
    public int Unsocket(string slot, ItemData worn, int index)
    {
        var arr = Get(slot, worn);
        if (index < 0 || index >= arr.Length) return 0;
        int old = arr[index];
        arr[index] = 0;
        if (old != 0) OnChanged?.Invoke();
        return old;
    }

    /// <summary>Clear every socket in a slot — used when the gear there is unequipped.</summary>
    public void ClearSlot(string slot)
    {
        if (_sockets.Remove(slot)) OnChanged?.Invoke();
    }

    /// <summary>Total attack bonus from every socketed module across all slots.</summary>
    public int TotalAttackBonus(Equipment eq) => Sum(eq, m => m.attackBonus);

    /// <summary>Total strength bonus from every socketed module across all slots.</summary>
    public int TotalStrengthBonus(Equipment eq) => Sum(eq, m => m.strengthBonus);

    int Sum(Equipment eq, System.Func<ItemData, int> pick)
    {
        if (eq == null) return 0;
        int total = 0;
        foreach (string slot in Equipment.Slots)
        {
            var worn = eq.GetItem(slot);
            if (worn == null || worn.socketCount <= 0) continue;
            foreach (int id in Get(slot, worn))
            {
                if (id == 0) continue;
                var mod = ItemRegistry.Get(id);
                if (mod != null) total += pick(mod);
            }
        }
        return total;
    }

    /// <summary>Every socketed module id, for saving.</summary>
    public Dictionary<string, int[]> Snapshot() => new Dictionary<string, int[]>(_sockets);

    /// <summary>Restore from a save.</summary>
    public void Restore(Dictionary<string, int[]> data)
    {
        _sockets.Clear();
        if (data != null)
            foreach (var kv in data) _sockets[kv.Key] = (int[])kv.Value.Clone();
        OnChanged?.Invoke();
    }
}
