using System;
using System.Collections.Generic;

public class Equipment
{
    public static readonly string[] Slots = { "Weapon", "Shield", "Helmet", "Chest", "Legs", "Ammo", "Tool" };

    private readonly Dictionary<string, int> _equipped = new();
    private readonly Dictionary<string, int[]> _modules = new();

    public int[] GetModules(string slot) => _modules.TryGetValue(slot, out var ids) ? (int[])ids.Clone() : Array.Empty<int>();
    public ItemStack GetStack(string slot) => GetItemId(slot) is int id
        ? new ItemStack(id, slot == "Ammo" ? AmmoQuantity : 1) { modules = GetModules(slot) } : null;
    public void SetModules(string slot, int[] ids) { _modules[slot] = (int[])ids.Clone(); RaiseChanged(); }
    public void Clear() { _equipped.Clear(); _modules.Clear(); AmmoQuantity = 0; RaiseChanged(); }

    // The Ammo slot tracks a stack count (every other slot is a single item).
    public int AmmoQuantity { get; private set; }

    public event Action OnChanged;

    /// <summary>Force listeners (EquipmentVisuals) to rebuild — e.g. when a cosmetic weapon skin
    /// changes but the equipped item id did not.</summary>
    public void RaiseChanged() => OnChanged?.Invoke();

    public int? GetItemId(string slot) => _equipped.TryGetValue(slot, out var id) ? id : null;
    public ItemData GetItem(string slot) => _equipped.TryGetValue(slot, out var id) ? ItemRegistry.Get(id) : null;
    public bool IsEquipped(string slot) => _equipped.ContainsKey(slot);

    public string SlotFor(ItemData item) => item.type switch
    {
        ItemType.Weapon => "Weapon",
        ItemType.Shield => "Shield",
        ItemType.Helmet => "Helmet",
        ItemType.Chest  => "Chest",
        ItemType.Legs  => "Legs",
        ItemType.Ammo   => "Ammo",
        ItemType.Tool   => "Tool",
        _ => null
    };

    public (bool success, int? unequippedId) Equip(ItemData item, int[] modules = null)
    {
        var slot = SlotFor(item);
        if (slot == null) return (false, null);
        int? old = GetItemId(slot);
        _equipped[slot] = item.id;
        _modules[slot] = modules == null ? Array.Empty<int>() : (int[])modules.Clone();
        OnChanged?.Invoke();
        return (true, old);
    }

    public int? Unequip(string slot)
    {
        if (!_equipped.TryGetValue(slot, out var id)) return null;
        _equipped.Remove(slot);
        _modules.Remove(slot);
        if (slot == "Ammo") AmmoQuantity = 0;
        OnChanged?.Invoke();
        return id;
    }

    // ── Ammo slot (carries a quantity) ───────────────────────────────────
    /// <summary>Equip a fresh ammo stack (replaces whatever was there).</summary>
    public void EquipAmmo(int itemId, int qty)
    {
        _equipped["Ammo"] = itemId;
        AmmoQuantity = qty;
        OnChanged?.Invoke();
    }

    /// <summary>Add to the currently-equipped ammo stack.</summary>
    public void AddAmmo(int qty) { AmmoQuantity += qty; OnChanged?.Invoke(); }

    /// <summary>Spend ammo when firing. Returns false if there isn't enough.</summary>
    public bool ConsumeAmmo(int qty = 1)
    {
        if (!_equipped.ContainsKey("Ammo") || AmmoQuantity < qty) return false;
        AmmoQuantity -= qty;
        if (AmmoQuantity <= 0) { _equipped.Remove("Ammo"); AmmoQuantity = 0; }
        OnChanged?.Invoke();
        return true;
    }

    // ── combat bonus totals (WORN gear only — excludes Ammo & Tool; feed the OSRS-style math) ──
    public int TotalAttackBonus()   => SumWorn(i => i.attackBonus) + ModuleBonus(i => i.attackBonus);

    /// <summary>
    /// Accuracy for a specific combat style. Weapons and tools always count; ARMOUR only counts
    /// when its line matches what you're doing — so a Marksman rig sharpens your shooting without
    /// also sharpening a sword you happen to be holding.
    /// Armour with no style (the starter set) counts for everything.
    /// </summary>
    public int TotalAttackBonusFor(WeaponStyle style) => SumWorn(i =>
    {
        if (i.attackBonus == 0) return 0;
        if (!i.IsArmor) return i.attackBonus;                       // weapons/tools: always
        if (i.armourStyle == WeaponStyle.None) return i.attackBonus; // style-neutral gear
        return i.armourStyle == style ? i.attackBonus : 0;
    }) + ModuleBonus(i => i.attackBonus);
    public int TotalStrengthBonus() => SumWorn(i => i.strengthBonus) + ModuleBonus(i => i.strengthBonus);
    public int TotalDefenceBonus()  => SumWorn(i => i.defenceBonus) + ModuleBonus(i => i.defenceBonus);

    int ModuleBonus(Func<ItemData, int> select)
    {
        int total = 0;
        foreach (var slot in Slots)
        {
            if (!IsEquipped(slot)) continue;
            foreach (int id in GetModules(slot))
                if (id > 0 && ItemRegistry.Get(id) is ItemData module) total += select(module);
        }
        return total;
    }

    /// <summary>Ranged strength from the equipped ammo (OSRS-style: ammo carries ranged strength).
    /// Add this to ranged max hit ONLY — never to melee.</summary>
    public int AmmoStrengthBonus()
    {
        var ammo = GetItem("Ammo");
        return ammo != null ? ammo.strengthBonus : 0;
    }

    private int SumWorn(Func<ItemData, int> select)
    {
        int total = 0;
        foreach (var kv in _equipped)
        {
            if (kv.Key == "Ammo" || kv.Key == "Tool") continue;   // worn combat gear only
            var item = ItemRegistry.Get(kv.Value);
            if (item != null) total += select(item);
        }
        return total;
    }

    public Dictionary<string, int> GetAll() => new(_equipped);
}
