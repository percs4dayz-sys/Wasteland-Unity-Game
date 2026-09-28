using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(PlayerStats))]
public class PlayerEntity : MonoBehaviour
{
    public static PlayerEntity Instance { get; private set; }

    public PlayerStats Stats { get; private set; }
    public Inventory Inventory { get; private set; } = new();
    public Equipment Equipment { get; private set; } = new();
    public Bank Bank { get; private set; } = new();

    public string PlayerName { get; set; } = "Survivor";
    private readonly HashSet<string> _flags = new();

    /// <summary>Total hostiles killed — incremented when a non-dummy CombatTarget dies.</summary>
    public int KillCount { get; private set; }
    public void IncrementKills() => KillCount++;

    /// <summary>Gender chosen at character select ("Male" / "Female"). Defaults to "Male".</summary>
    public string Gender { get; set; } = "Male";

    void Awake()
    {
        Instance = this;
        Stats = GetComponent<PlayerStats>();
        ModuleEffects.For(this);
        // Death→respawn handler. Auto-added so scenes built before it existed
        // get respawn behaviour without an editor change.
        if (GetComponent<PlayerRespawn>() == null) gameObject.AddComponent<PlayerRespawn>();

        // Restore character-creation choices from PlayerPrefs
        PlayerName = PlayerPrefs.GetString("PlayerName", "Survivor");
        Gender     = PlayerPrefs.GetString("PlayerGender", "Male");
    }

    /// <summary>Rich-text chat label: the player's name with their combat level, e.g.
    /// "Survivor <color=#8FE0FF>[Lv42]</color>". Used by both the 2D and 3D chat.</summary>
    public static string ChatLabel()
    {
        var p = Instance;
        string name = p != null && !string.IsNullOrEmpty(p.PlayerName) ? p.PlayerName : "You";
        int lvl = p != null && p.Stats != null ? p.Stats.GetCombatLevel() : 1;
        return $"<color=white>{name}</color> <color=#8FE0FF>[Lv{lvl}]</color>";
    }

    public bool HasFlag(string flag) => _flags.Contains(flag);
    public void SetFlag(string flag) => _flags.Add(flag);
    public void RemoveFlag(string flag) => _flags.Remove(flag);
    public IEnumerable<string> GetAllFlags() => _flags;
    public void ClearFlags() => _flags.Clear();

    public string Equip(int itemId) => EquipAt(FindInventorySlot(itemId));

    int FindInventorySlot(int id)
    {
        for (int i = 0; i < Inventory.MAX; i++)
            if (Inventory.GetSlot(i)?.itemId == id) return i;
        return -1;
    }

    public string EquipAt(int index)
    {
        var incoming = Inventory.GetSlot(index)?.Copy();
        var item = incoming?.Item;
        if (item == null) return "Not in inventory.";
        string slot = Equipment.SlotFor(item);
        if (slot == null) return "Can't equip that.";
        foreach (var req in item.requirements)
            if (Stats.GetLevel(req.Key) < req.Value)
                return $"Requires {req.Key} level {req.Value}.";

        // Complete the bag transaction before touching equipment. A full bag can swap one
        // item, but cannot silently delete the extra off-hand removed by a two-handed weapon.
        var before = Inventory.Snapshot();
        var old = Equipment.GetStack(slot);
        string conflict = IsTwoHanded(item) ? "Shield"
            : item.type == ItemType.Shield && IsTwoHanded(Equipment.GetItem("Weapon")) ? "Weapon" : null;
        var offhand = conflict == null ? null : Equipment.GetStack(conflict);
        int quantity = item.IsAmmo ? Inventory.CountOf(item.id) : 1;
        if (item.IsAmmo) Inventory.Remove(item.id, quantity);
        else Inventory.RemoveAt(index);
        bool sameAmmo = item.IsAmmo && old?.itemId == item.id;
        if ((!sameAmmo && old != null && !Inventory.Add(old)) ||
            (offhand != null && !Inventory.Add(offhand)))
        {
            Inventory.Restore(before);
            return "Make room in your inventory for the equipment being removed.";
        }
        if (offhand != null) Equipment.Unequip(conflict);
        if (item.IsAmmo) Equipment.EquipAmmo(item.id, quantity + (sameAmmo ? old.quantity : 0));
        else Equipment.Equip(item, incoming.modules);
        UpdateCombatStyle();
        return $"You equip the {item.name}." + (offhand != null ? " (Both hands are required.)" : "");
    }

    /// <summary>A two-handed weapon and a shield/off-hand can't be worn together. Two-handed = the
    /// item's twoHanded flag, OR any ranged gun (always needs both hands). Returns a suffix for the
    /// equip message noting anything that was auto-removed.</summary>
    public static bool IsTwoHanded(ItemData it) =>
        it != null && it.type == ItemType.Weapon &&
        (it.twoHanded || it.weaponStyle == WeaponStyle.Ranged);

    public string Unequip(string slot)
    {
        var stack = Equipment.GetStack(slot);
        if (stack == null) return "Nothing equipped there.";
        if (!Inventory.Add(stack)) return "Inventory is full.";
        Equipment.Unequip(slot);
        UpdateCombatStyle();
        return $"You unequip the {stack.Item?.name}.";
    }

    private void UpdateCombatStyle()
    {
        if (CombatManager.Instance != null)
        {
            var weapon = Equipment.GetItem("Weapon");
            // Fission has its OWN stance set (Accurate/Defensive only) and is the only style with a
            // special attack, so it can't just borrow the ranged one.
            CombatManager.Instance.SetStyle(weapon?.weaponStyle switch
            {
                WeaponStyle.Fission => CombatStyle.Fission,
                WeaponStyle.Ranged  => CombatStyle.Ranged,
                _                   => CombatStyle.Melee,
            });
        }

        // Refresh UI if it exists
        var combatUI = Object.FindAnyObjectByType<CombatStyleUI>(FindObjectsInactive.Include);
        if (combatUI != null) combatUI.Refresh();
    }

    // OSRS-style eat delay: after eating standard food you must wait 3 ticks
    // (1.8s) before eating again, and eating mid-fight pushes your next attack back.
    const int EAT_DELAY_TICKS = 3;
    long _nextEatTick;

    public string Consume(int itemId)
    {
        var item = ItemRegistry.Get(itemId);
        if (item == null || !item.IsConsumable) return "Can't use that.";
        if (Stats.IsDead) return "You can't eat while down.";
        if (!Inventory.Contains(itemId)) return "Not in inventory.";

        // Still on the eat cooldown — silently ignore the click (OSRS behaviour).
        long now = GameTick.Instance != null ? GameTick.Instance.TickCount : 0;
        if (now < _nextEatTick) return null;

        Inventory.Remove(itemId);
        Stats.Heal(item.healAmount);
        _nextEatTick = now + EAT_DELAY_TICKS;

        // Eating while engaged delays the next swing (the 3-tick attack delay).
        GetComponent<ActionCombat3D>()?.DelayAttackAfterEating(EAT_DELAY_TICKS);

        return $"You use the {item.name}. (+{item.healAmount} HP)";
    }

    /// <summary>Bury bones/remains for their Beastmastery XP. Legacy: monsters no longer drop these
    /// (Beastmastery now trains through beast tasks — see BeastTasks), but bones already in a bag or
    /// bank from older saves still bury for their XP, so nothing players stockpiled is wasted.
    /// Consumes one of the item and awards its Beastmastery XP.</summary>
    public string Bury(int itemId)
    {
        var item = ItemRegistry.Get(itemId);
        if (item == null || !item.IsBuryable) return "You can't bury that.";
        if (!Inventory.Remove(itemId)) return "Not in inventory.";
        Stats.AddXP(Skill.Beastmastery, item.beastXP);
        return $"You bury the {item.name}. (+{item.beastXP} Beastmastery XP)";
    }

    public string Drop(int itemId) => DropAt(FindInventorySlot(itemId));

    public string DropAt(int index)
    {
        var stack = Inventory.GetSlot(index)?.Copy(1);
        if (stack == null) return "Not in inventory.";
        var drop = GroundItem.Spawn(stack.itemId, 1, transform.position);
        if (drop == null) return "Can't drop that here.";
        drop.modules = stack.modules;
        Inventory.RemoveAt(index);
        return $"You dropped the {stack.Item.name}.";
    }
}
