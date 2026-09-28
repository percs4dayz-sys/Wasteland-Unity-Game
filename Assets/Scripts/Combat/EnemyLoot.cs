using UnityEngine;

/// <summary>
/// Enemy drop tables, implementing the spec in Docs/COMBAT_AND_GEAR_MASTER.md §10.
///
/// Two independent steps on every death (the old guaranteed bones are gone — Beastmastery now
/// trains through beast tasks, see BeastTasks):
///   1. MODULE ROLL — one roll for a Common / Refined / Perfect module at tier-specific odds.
///   2. MAIN LOOT   — one roll each for ore, ammo, and a slim chance at a gear piece.
///
/// The steps are independent: a kill can yield everything, or nothing at all. Rates get *rarer* as
/// tiers climb (T1 module 1-in-4 down to T5 1-in-10) because the rewards get proportionally
/// stronger — that's deliberate, not a typo.
///
/// The God-Hunter has its own table: guaranteed resources on every kill so there is never an
/// empty drop, plus a 1-in-50 roll on the God Tier gear table. ~200-250 kills for a full set.
/// </summary>
public static class EnemyLoot
{
    // ── per-tier drop rates (denominators: "1 in N") ─────────────────────────
    // index 0 unused so tier numbers read naturally
    static readonly int[] ModuleCommon  = { 0, 4,   5,   6,   8,   10  };
    static readonly int[] ModuleRefined = { 0, 20,  25,  30,  40,  50  };
    static readonly int[] ModulePerfect = { 0, 100, 120, 150, 200, 250 };

    static readonly int[] OreChance     = { 0, 2, 2, 3, 3, 4 };   // 1 in N
    const int AmmoChance = 3;                                     // 1 in 3, every tier

    static readonly int[] AmmoMin       = { 0, 10, 15, 20, 25, 30 };
    static readonly int[] AmmoMax       = { 0, 20, 30, 40, 50, 60 };

    static readonly int[] GearFromMob   = { 0, 128, 256, 350, 512, 750 };
    static readonly int[] GearFromMini  = { 0, 8,   12,  16,  20,  24  };

    // ── item ids by tier ─────────────────────────────────────────────────────
    // Ore follows the gathering tier, including Scrap Metal at tier 1.
    static readonly int[] OreId  = { 0, 10, 60, 61, 62, 63 };

    /// <summary>
    /// Ammo drops one tier BELOW the enemy that dropped it. A tier-5 mob yields Armor-Piercing, not
    /// Experimental. This is what keeps ammo crafting worth doing: scavenging always keeps you
    /// firing (you are never stranded without rounds), but the top grade of every tier is only ever
    /// pressed at a Workbench — which is what gives Sulphur and the tiered logs a real consumer.
    /// Tier 1 has nothing below it, so it drops the improvised Scrap Rounds (id 13).
    /// </summary>
    static readonly int[] AmmoId = { 0, 13, 115, 116, 117, 118 };

    /// <summary>
    /// The God-Hunter sits above tier 5, so the same one-tier-below rule lands it on Experimental
    /// (the tier-5 grade) rather than the Armor-Piercing its T5 underlings drop.
    /// </summary>
    const int GodHunterAmmoId = 119;

    /// <summary>Gear piece ids per tier — melee and ranged, drawn from evenly.</summary>
    static readonly int[][] GearMelee =
    {
        null,
        new[] { 100, 101, 136, 137, 138, 139 },          // T1: starter weapons + Salvaged set
        new[] { 102, 120, 121, 122, 123 },               // T2: Steel Blade + Riveted
        new[] { 103, 124, 125, 126, 127 },               // T3: Energy Blade + Hardened Alloy
        new[] { 104, 128, 129, 130, 131 },               // T4: Vibro-Blade + Tactical Ballistic
        new[] { 105, 132, 133, 134, 135 },               // T5: Myomer Blade + Power-Assisted
    };
    static readonly int[][] GearRanged =
    {
        null,
        new[] { 12 },                                     // T1: Pipe Pistol
        new[] { 110, 144, 145, 146 },                     // T2: Mid-tier Rifle + Scout
        new[] { 111, 147, 148, 149 },                     // T3: Alloy Rifle + Ranger
        new[] { 112, 150, 151, 152 },                     // T4: Precision Rifle + Marksman
        new[] { 113, 153, 154, 155 },                     // T5: Energy Rifle + Deadeye
    };

    // God Tier table — hit 1-in-50, then a quarter each.
    static readonly int[] GodMeleeWeapon = { 159 };
    static readonly int[] GodMeleeArmour = { 140, 141, 142, 143 };
    static readonly int[] GodRangedWeapon = { 160, 161 };          // railgun or slugs
    static readonly int[] GodRangedArmour = { 156, 157, 158 };

    const int GodGearChance = 50;   // 1 in 50 to reach the God table at all

    /// <summary>Spawn loot at the dead enemy's position. Dummies never drop.</summary>
    public static void Drop(CombatTarget target, Vector3 pos)
    {
        if (target == null || target.isDummy) return;

        if (target.dropTable != null)
        {
            target.dropTable.RollDrops(ModuleCatalog.Power(PlayerEntity.Instance?.Equipment, ModuleCatalog.Effect.Salvage),
                (id, quantity) => GroundItem.Spawn(id, quantity, pos));
            return;
        }

        if (target.isGodHunter) { DropGodHunter(target, pos); return; }

        int tier = Mathf.Clamp(target.LootTier, 1, 5);
        bool mini = target.isMiniBoss || target.isBoss;

        // No bones any more: Beastmastery trains through beast tasks (BeastTasks), not burying.

        // ── 1. module roll — one roll, best quality wins ──
        int moduleId = RollModule(tier);
        if (moduleId > 0) GroundItem.Spawn(moduleId, 1, pos);

        // ── 2. main loot: ore, ammo and a slim gear chance, each independent ──
        if (OneIn(OreChance[tier]) || Random.value < 0.10f * ModuleCatalog.Power(PlayerEntity.Instance?.Equipment, ModuleCatalog.Effect.Salvage))
            GroundItem.Spawn(OreId[tier], Random.Range(1, 4), pos);

        if (OneIn(AmmoChance))
            GroundItem.Spawn(AmmoId[tier], Random.Range(AmmoMin[tier], AmmoMax[tier] + 1), pos);

        int gearChance = mini ? GearFromMini[tier] : GearFromMob[tier];
        if (OneIn(gearChance))
        {
            int gear = RollGear(tier);
            if (gear > 0) GroundItem.Spawn(gear, 1, pos);
        }
    }

    /// <summary>
    /// The God-Hunter. Never an empty drop: both resource piles are guaranteed, the module roll is
    /// generous, and the God Tier gear table is the 1-in-50 chase.
    /// </summary>
    static void DropGodHunter(CombatTarget target, Vector3 pos)
    {
        // guaranteed module roll — Refined (the top quality now that Perfect is retired), generous
        if (OneIn(3)) GroundItem.Spawn(TierModules.IdFor(6, TierModules.Quality.Refined), 1, pos);

        // resource drop 1 — always
        GroundItem.Spawn(OreId[5], Random.Range(10, 26), pos);

        // resource drop 2 — always
        GroundItem.Spawn(GodHunterAmmoId, Random.Range(50, 101), pos);

        // the rare God Tier equipment table
        if (!OneIn(GodGearChance)) return;

        int[] table = Random.Range(0, 4) switch
        {
            0 => GodMeleeWeapon,
            1 => GodMeleeArmour,
            2 => GodRangedWeapon,
            _ => GodRangedArmour,
        };
        int id = table[Random.Range(0, table.Length)];
        GroundItem.Spawn(id, 1, pos);

        var item = ItemRegistry.Get(id);
        HUDController.Emit($"<color=#FFD700>[GOD DROP]</color> The God-Hunter yields " +
                           $"{(item != null ? item.name : "something extraordinary")}!");
    }

    /// <summary>One module roll. Rarest quality is checked first so a lucky roll isn't masked.</summary>
    static int RollModule(int tier)
    {
        // Perfect retired — Refined is now the top module quality.
        if (OneIn(ModuleRefined[tier])) return ModuleCatalog.IdFor(tier, Random.Range(0,7), true);
        if (OneIn(ModuleCommon[tier]))  return ModuleCatalog.IdFor(tier, Random.Range(0,7), false);
        return 0;
    }

    /// <summary>A gear piece for this tier — melee or ranged, evenly split.</summary>
    static int RollGear(int tier)
    {
        int[] pool = Random.value < 0.5f ? GearMelee[tier] : GearRanged[tier];
        if (pool == null || pool.Length == 0) return 0;
        return pool[Random.Range(0, pool.Length)];
    }

    static bool OneIn(int n) => n > 0 && Random.Range(0, n) == 0;

    /// <summary>Editable description of the current default probabilities, including exclusive pools.
    /// Uses the same constants as the original drop path; does not change unedited monsters.</summary>
    public static MonsterDropTable CreateDefaultTable(int tier, bool mini, bool godHunter, bool dummy = false)
    {
        var table = ScriptableObject.CreateInstance<MonsterDropTable>();
        if (dummy) return table;
        tier = Mathf.Clamp(tier, 1, 5);
        MonsterDropTable.Roll Add(string label, float chance, bool salvage = false)
        {
            var roll = new MonsterDropTable.Roll { label = label, chance = chance, salvageBonus = salvage };
            table.rolls.Add(roll); return roll;
        }
        void Item(MonsterDropTable.Roll roll, int id, int min = 1, int max = 1, float weight = 1)
            => roll.outcomes.Add(new MonsterDropTable.Outcome { itemId = id, minQuantity = min, maxQuantity = max, weight = weight });
        if (godHunter)
        {
            Item(Add("Refined module", 1f / 3), TierModules.IdFor(6, TierModules.Quality.Refined));
            Item(Add("Guaranteed ore", 1), OreId[5], 10, 25);
            Item(Add("Guaranteed ammunition", 1), GodHunterAmmoId, 50, 100);
            var gear = Add("God-tier equipment", 1f / GodGearChance);
            foreach (var pool in new[] { GodMeleeWeapon, GodMeleeArmour, GodRangedWeapon, GodRangedArmour })
                foreach (int id in pool) Item(gear, id, weight: .25f / pool.Length);
            return table;
        }
        float refined = 1f / ModuleRefined[tier];
        float common = (1 - refined) / ModuleCommon[tier];
        var module = Add("Module (one quality per kill)", refined + common);
        for (int i = 0; i < 7; i++)
        {
            Item(module, ModuleCatalog.IdFor(tier, i, true), weight: refined / 7);
            Item(module, ModuleCatalog.IdFor(tier, i, false), weight: common / 7);
        }
        Item(Add("Ore", 1f / OreChance[tier], true), OreId[tier], 1, 3);
        Item(Add("Ammunition", 1f / AmmoChance), AmmoId[tier], AmmoMin[tier], AmmoMax[tier]);
        var equipment = Add("Equipment (one piece)", 1f / (mini ? GearFromMini[tier] : GearFromMob[tier]));
        foreach (var pool in new[] { GearMelee[tier], GearRanged[tier] })
            foreach (int id in pool) Item(equipment, id, weight: .5f / pool.Length);
        return table;
    }
}
