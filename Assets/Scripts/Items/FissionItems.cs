using System;

/// <summary>
/// Items for the third combat style: the geiger counter, the raw fission cores you farm from
/// reactor nodes (one per world tier — guarded by the five Elemental Golems), the refined cores you
/// smelt them into, and the power gauntlets that fire refined cores as ammo.
///
/// DATA ONLY, registered into ItemRegistry at startup (see ItemRegistry init). IDs 80-99, a free
/// block (scrap 10-11, ores/bars 60-73, gear 100-161, modules 200-217).
///
/// Content that isn't placed yet — the reactor nodes, the refining recipe, the gauntlet fire logic —
/// references these ids, so the data is ready ahead of the content (same pattern as TierMaterials).
/// </summary>
public static class FissionItems
{
    // id block 400-419 (80-83 taken by BeastRemains, 200-223 by modules/gatherables, 300 journal).
    public const int GeigerCounter = 400;
    public const int GauntletsLeft  = 401;  // reserved: paired gauntlet visual (right is the weapon)
    public const int Gauntlets      = 402;  // the equippable weapon (occupies the weapon slot)

    // Three reactor gauntlets above the starter pair: the same fission guns with a steadier aim, a deeper
    // magazine and a better chance to keep the core. Crafted at a workbench once Roxy has taught Fission.
    public const int NeonGauntlets = 403, SurgeGauntlets = 404, InfernoGauntlets = 415;
    static readonly (int id, string name, string model, int level, int accuracy, int magazine, float save)[] UpgradeGauntlets =
    {
        (NeonGauntlets,    "Neon Gauntlets",    "NeonGauntlet",    25, 180, 5, 0.18f),
        (SurgeGauntlets,   "Surge Gauntlets",   "SurgeGauntlet",   50, 215, 5, 0.21f),
        (InfernoGauntlets, "Inferno Gauntlets", "InfernoGauntlet", 75, 250, 6, 0.25f),
    };

    public const int RawCoreBase     = 405; // 405..409 = tier 1..5 raw fission core
    public const int RefinedCoreBase = 410; // 410..414 = tier 1..5 refined core (gauntlet ammo)

    public static int RawCore(int tier)     => RawCoreBase     + Clamp(tier);
    public static int RefinedCore(int tier) => RefinedCoreBase + Clamp(tier);
    static int Clamp(int tier) => (tier < 1 ? 1 : tier > 5 ? 5 : tier) - 1;

    static readonly string[] TierName = { "Unstable", "Volatile", "Enriched", "Weaponized", "Singularity" };

    /// <summary>
    /// MAX HIT per refined core tier. Fission is the one style where the level buys accuracy ONLY —
    /// all damage comes from the loaded core, so these numbers are the entire power curve.
    ///
    /// They are deliberately steep at both ends. A tier-1 core hitting 10 is enormous early (a
    /// tier-1 blade maxes about 2 at low level), and Singularity's 62 sits between the tier-5
    /// conventional weapons (~34-37) and God tier (~76-80). The cost is the grind: every shot eats
    /// a core, and cores only come off Elemental Golems.
    /// </summary>
    static readonly int[] CoreMaxHit = { 10, 20, 32, 46, 62 };

    /// <summary>Max hit of a refined core of this tier — see <see cref="CoreMaxHit"/>.</summary>
    public static int MaxHitFor(int tier) => CoreMaxHit[Clamp(tier)];

    /// <summary>Accuracy bonus on the gauntlets. There is only ONE gauntlet item rather than a
    /// tiered weapon line, so this is flat and the accuracy CURVE comes purely from Fission level.
    /// Sized between the Alloy (75) and Energy (180) rifles.</summary>
    public const int GauntletAccuracy = 150;

    /// <summary>Chance a shot does not consume its core. Cores are the scarcest ammo in the game.</summary>
    public const float CoreSaveChance = 0.15f;

    public static void Register(Action<ItemData> add)
    {
        // The detector. A Tool: held in inventory, drives GeigerCounter's readout to the nearest node.
        add(new ItemData
        {
            id = GeigerCounter, name = "Geiger Counter", type = ItemType.Tool, stackable = false,
            description = "Clicks toward the nearest active reactor. The faster it ticks, the closer the core.",
            heldModel = "HeldModels/Tools/GeigerCounter"
        });

        // Raw + refined cores, one per tier.
        for (int t = 1; t <= 5; t++)
        {
            add(new ItemData
            {
                id = RawCore(t), name = $"{TierName[t-1]} Fission Core (Raw)", type = ItemType.Resource,
                stackable = false,
                description = $"Tier {t} reactor slag, still hot. Refine it before it's safe to load."
            });
            add(new ItemData
            {
                id = RefinedCore(t), name = $"{TierName[t-1]} Fission Core", type = ItemType.Ammo,
                stackable = true,
                // strengthBonus IS the max hit here, not an OSRS-style bonus fed through the
                // strength formula — Fission reads it directly (see ActionCombat3D.ResolveHit).
                strengthBonus = CoreMaxHit[t - 1],
                description = $"Tier {t} refined core. Loads into power gauntlets — max hit {CoreMaxHit[t-1]}."
            });
        }

        // The power gauntlets — the weapon. Ranged style so it slots beside guns; fires refined cores.
        // Stats are placeholders until the fire behaviour is built.
        add(new ItemData
        {
            id = Gauntlets, name = "Power Gauntlets", type = ItemType.Weapon, stackable = false,
            weaponStyle = WeaponStyle.Fission, twoHanded = true,
            magazineSize = 4, reloadSeconds = 1.6f, attackSpeed = 5, attackRange = 12,
            // Accuracy only — the gauntlets contribute NO strength. Damage is entirely the core's.
            attackBonus = GauntletAccuracy,
            ammoSaveChance = CoreSaveChance,
            requirements = new System.Collections.Generic.Dictionary<Skill, int> { [Skill.Fission] = 1 },
            description = "Reactor-fed gauntlets. Each blast is a fistful of refined fission — few shots, " +
                          "heavy hits. Damage comes from the loaded core; your Fission level is what lands it. " +
                          $"{(int)(CoreSaveChance * 100)}% chance to recover the core.",
            // Worn on BOTH hands — a distinct mesh per side. EquipmentVisuals puts heldModel on the
            // right hand and heldModelLeft on the left.
            heldModel     = "HeldModels/Weapons/GauntletRight",
            heldModelLeft = "HeldModels/Weapons/GauntletLeft"
        });

        foreach (var g in UpgradeGauntlets)
            add(new ItemData
            {
                id = g.id, name = g.name, type = ItemType.Weapon, stackable = false,
                weaponStyle = WeaponStyle.Fission, twoHanded = true,
                magazineSize = g.magazine, reloadSeconds = 1.6f, attackSpeed = 5, attackRange = 12,
                attackBonus = g.accuracy,   // accuracy only, like the starter pair — the core is the damage
                ammoSaveChance = g.save,
                requirements = new System.Collections.Generic.Dictionary<Skill, int> { [Skill.Fission] = g.level },
                description = $"Reactor gauntlets tuned past the starter pair: a steadier aim, a {g.magazine}-core " +
                              $"magazine and a {(int)(g.save * 100)}% chance to recover the core. Damage still comes from the loaded core.",
                heldModel     = "HeldModels/Weapons/" + g.model + "Right",
                heldModelLeft = "HeldModels/Weapons/" + g.model + "Left"
            });
    }
}
