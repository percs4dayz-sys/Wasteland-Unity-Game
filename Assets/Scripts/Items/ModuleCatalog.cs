using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Named module catalogue. Stable IDs 600-669; legacy 500-series modules remain usable.</summary>
public static class ModuleCatalog
{
    public enum Effect { Bleed, HasteProc, Salvage, Visor, Plates, Gather, Reflect, Critical, Power, Serrated, Targeting, HeavyGuard, EatRecovery, Block, Capacitor, Overclock, DoubleStrike, Scanner, Reactive, Magnet, Barrier, Resonance, MissAdapt, Leech, Processor, Ballistic, Recovery, Deflect, Adaptive, Combo, Burst, Tactical, Emergency, Servo, Nano }
    public sealed class Definition
    {
        public int index; public string name, slot, description; public Effect effect;
        public int Tier => 1 + index / 7;
    }
    public static readonly Definition[] All = {
        new Definition { index=0, name="Jury-Rigged Edge", slot="Weapon", effect=Effect.Bleed, description="5/10% chance to bleed for 3 ticks." },
        new Definition { index=1, name="Loose Gear Mechanism", slot="Weapon", effect=Effect.HasteProc, description="10/15% chance to shorten an attack by one tick." },
        new Definition { index=2, name="Salvager Grip", slot="Weapon", effect=Effect.Salvage, description="10/20% chance for extra ore from defeated enemies." },
        new Definition { index=3, name="Cracked Visor", slot="Helmet", effect=Effect.Visor, description="Extra 3/6 accuracy and 0/4 defence." },
        new Definition { index=4, name="Reinforced Plates", slot="Chest", effect=Effect.Plates, description="Reduce incoming damage by 3/6%." },
        new Definition { index=5, name="Salvager Boots", slot="Legs", effect=Effect.Gather, description="10/20% chance for an extra gathered resource." },
        new Definition { index=6, name="Welding Shield", slot="Shield", effect=Effect.Reflect, description="Reflect 5/10% of incoming damage." },
        new Definition { index=7, name="Balanced Core", slot="Weapon", effect=Effect.Critical, description="Extra 3/6 accuracy; 5/10% critical chance for 50% bonus damage." },
        new Definition { index=8, name="Hardened Edge", slot="Weapon", effect=Effect.Power, description="Increase hit damage by 4/8%." },
        new Definition { index=9, name="Serrated Edge", slot="Weapon", effect=Effect.Serrated, description="10/20% chance to bleed; Refined bleeds stack twice." },
        new Definition { index=10, name="Targeting HUD", slot="Helmet", effect=Effect.Targeting, description="Extra 4/8 ranged accuracy; Refined adds 5% headshot chance." },
        new Definition { index=11, name="Shock Absorber", slot="Chest", effect=Effect.HeavyGuard, description="Reduce hits of at least 10 damage by 10/20%." },
        new Definition { index=12, name="Reinforced Servos", slot="Legs", effect=Effect.EatRecovery, description="Reduce the attack delay after eating by 1/2 ticks." },
        new Definition { index=13, name="Riot Plating", slot="Shield", effect=Effect.Block, description="5/10% block chance; Refined counters for 25% of the blocked hit." },
        new Definition { index=14, name="Capacitor Cell", slot="Weapon", effect=Effect.Capacitor, description="Every 5/3 successful hits discharge 30% bonus lightning damage." },
        new Definition { index=15, name="Overclock Chip", slot="Weapon", effect=Effect.Overclock, description="Consecutive hits increase attack speed by 2/4%, up to 5 stacks." },
        new Definition { index=16, name="Phase Stabilizer", slot="Weapon", effect=Effect.DoubleStrike, description="5/10% chance for an extra strike at 40% damage." },
        new Definition { index=17, name="Tactical Scanner", slot="Helmet", effect=Effect.Scanner, description="Scan your target; successive hits gain 1/2% damage, up to 5 stacks." },
        new Definition { index=18, name="Reactive Armor", slot="Chest", effect=Effect.Reactive, description="Taking a hit grants 8/16 defence for 4 seconds; Refined also grants 5% damage." },
        new Definition { index=19, name="Magnetic Boots", slot="Legs", effect=Effect.Magnet, description="Automatically collect material drops within 2/4 metres." },
        new Definition { index=20, name="Energy Barrier", slot="Shield", effect=Effect.Barrier, description="Absorb up to 6/12 damage; recharge after 12/8 seconds without taking damage." },
        new Definition { index=21, name="Resonance Core", slot="Weapon", effect=Effect.Resonance, description="Consecutive hits ignore 2/4% enemy defence per hit, up to 5 stacks." },
        new Definition { index=22, name="Combat AI Chip", slot="Weapon", effect=Effect.MissAdapt, description="Each miss grants 5/10 accuracy, up to 3 stacks; Refined also grants 3% damage per stack." },
        new Definition { index=23, name="Blood Reactor", slot="Weapon", effect=Effect.Leech, description="Heal for 3/6% of damage dealt, minimum 1 HP on a successful hit." },
        new Definition { index=24, name="Combat Processor", slot="Helmet", effect=Effect.Processor, description="Attack cooldown is 3/6% shorter; Refined also grants 5 accuracy." },
        new Definition { index=25, name="Ballistic Matrix", slot="Chest", effect=Effect.Ballistic, description="Reduce ranged damage by 10/20%; Refined has 5% chance to negate it." },
        new Definition { index=26, name="Sprint Actuators", slot="Legs", effect=Effect.Recovery, description="Attack cooldown is 3/6% shorter; Refined doubles this for 5 seconds after a kill." },
        new Definition { index=27, name="Deflection Field", slot="Shield", effect=Effect.Deflect, description="8/16% chance to reflect half the incoming hit." },
        new Definition { index=28, name="Adaptive Combat Matrix", slot="Weapon", effect=Effect.Adaptive, description="Consecutive hits grant 2/3% attack speed; Refined also grants 2% damage per stack, up to 5." },
        new Definition { index=29, name="Neural Link", slot="Weapon", effect=Effect.Combo, description="Consecutive hits grant 2/3% damage, up to 5/8 stacks; resets after 6/10 seconds." },
        new Definition { index=30, name="Reactor Core", slot="Weapon", effect=Effect.Burst, description="8/16% chance for a 50% bonus energy burst." },
        new Definition { index=31, name="Tactical AI", slot="Helmet", effect=Effect.Tactical, description="Extra 4/8 accuracy, power and defence; Refined adds 1% damage per target hit, up to 5." },
        new Definition { index=32, name="Emergency Protocol", slot="Chest", effect=Effect.Emergency, description="Below 30% HP, reduce damage by 10/20%; Refined also grants 10% damage." },
        new Definition { index=33, name="Servo Overdrive", slot="Legs", effect=Effect.Servo, description="After a hit, gain 8% attack speed for 3/6 seconds." },
        new Definition { index=34, name="Nanofiber Barrier", slot="Shield", effect=Effect.Nano, description="Absorb up to 10/20 damage; regenerate 1/2 shield per second." },
    };
    public static bool IsModule(int id) => Get(id) != null || (id >= 500 && id <= 516 && (id - 500) % 3 < 2);
    public static Definition Get(int id) => id >= 600 && id < 670 ? All[(id - 600) / 2] : null;
    public static int Grade(int id) => Get(id) != null ? 1 + (id - 600) % 2 : (id - 500) % 3 + 1;
    public static int IdFor(int tier, int variant, bool refined) => 600 + ((Mathf.Clamp(tier,1,5)-1)*7+Mathf.Clamp(variant,0,6))*2 + (refined ? 1 : 0);
    public static int Capacity(ItemData item) => item == null ? 0 : item.type switch {
        ItemType.Weapon or ItemType.Chest => 2,
        ItemType.Helmet or ItemType.Legs or ItemType.Shield => 1, _ => 0 };
    public static bool Fits(int id, string slot) => IsModule(id) && (Get(id) == null || Get(id).slot == slot);
    public static void Register(Action<ItemData> add)
    {
        foreach (var d in All)
        for (int q = 0; q < 2; q++)
        {
            int grade = q + 1;
            var stats = TierModules.StatsFor(q == 0 ? TierModules.Quality.Common : TierModules.Quality.Refined);
            int atk = stats.atk, str = stats.str, def = 0;
            if (d.effect == Effect.Visor) { atk += 3 * grade; def += q * 4; }
            if (d.effect == Effect.Critical) atk += 3 * grade;
            if (d.effect == Effect.Processor) atk += q * 5;
            if (d.effect == Effect.Tactical) { atk += 4 * grade; str += 4 * grade; def += 4 * grade; }
            add(new ItemData { id=600+d.index*2+q, name=d.name+" ("+(q==0?"Common":"Refined")+")",
                description=$"Tier {d.Tier} • {d.slot} module. {d.description} Common/Refined values. +{atk} accuracy, +{str} power, +{def} defence.",
                type=ItemType.Resource, stackable=true, attackBonus=atk, strengthBonus=str, defenceBonus=def });
        }
    }
    public static int Power(Equipment eq, Effect effect)
    {
        if (eq == null) return 0;
        int result=0;
        foreach(var slot in Equipment.Slots)
            if(eq.IsEquipped(slot))
                foreach(int id in eq.GetModules(slot))
                    if(Get(id)?.effect == effect) result += Grade(id);
        return Mathf.Min(result,4);
    }
    public static string Socket(PlayerEntity player, string slot, int index, int moduleId)
    {
        if (player == null) return "No player.";
        var gear = player.Equipment.GetItem(slot);
        int count = Capacity(gear);
        if (index < 0 || index >= count) return "Equip a compatible piece of gear first.";
        if (moduleId != 0 && !Fits(moduleId,slot)) return "That module does not fit this gear slot.";
        var ids = new int[count];
        var old = player.Equipment.GetModules(slot);
        Array.Copy(old,ids,Math.Min(old.Length,ids.Length));
        if (ids[index] == moduleId) return "That module is already installed.";
        var before = player.Inventory.Snapshot();
        if (moduleId != 0 && !player.Inventory.Remove(moduleId)) return "That module is not in your inventory.";
        if (ids[index] != 0 && !player.Inventory.Add(ids[index]))
        {
            player.Inventory.Restore(before);
            return "Make room in your inventory for the removed module.";
        }
        ids[index] = moduleId;
        player.Equipment.SetModules(slot,ids);
        return moduleId == 0 ? "Module removed." : "Module installed.";
    }
}

