using System;

/// <summary>
/// Legacy generic modules (500-series IDs), retained for saved inventories.
/// Named effects and new drops live in ModuleCatalog. Both kinds install through
/// ModuleCatalog.Socket and persist on individual gear copies. Perfect is retired.
/// </summary>
public static class TierModules
{
    public enum Quality { Common, Refined, Perfect }

    // ids 500-514 = tiers 1-5 x 3 qualities, 515-517 = God tier.
    //
    // These lived at 200-217 until 2026-08-06, which silently collided with the tiered gatherables
    // (raw fish 200-203, cooked food 210-213). ItemRegistry registers gatherables LAST, so eight of
    // the eighteen modules were being overwritten — a T1 Common module drop handed you a Mutated
    // Carp. Moved well clear of the gear/gatherable ranges, which have grown repeatedly; 518-599 is
    // left free as headroom so a new tier or quality never walks into another block.
    public const int FirstId = 500;

    /// <summary>Item id for a module of this tier (1-6, where 6 = God) and quality.</summary>
    public static int IdFor(int tier, Quality q)
    {
        int t = UnityEngine.Mathf.Clamp(tier, 1, 6);
        return FirstId + (t - 1) * 3 + (int)q;
    }

    static readonly string[] TierNames =
        { "Scrap", "Steel", "Alloy", "Titanium", "CyberSteel", "Ascendant" };

    static readonly string[] TierFlavour =
    {
        "Improvised survival tech, lashed together from wreckage.",
        "Military leftovers — mass-produced, dependable.",
        "Recovered old-world tech. Nobody alive knows how it works.",
        "Military endgame hardware, stripped from a commander.",
        "Humanity's last engineering, and it shows.",
        "Torn from the God-Hunter. It is still warm.",
    };

    /// <summary>
    /// Per-module stat contribution, from the module scaling matrix. Note these are FLAT by
    /// quality — a Scrap Perfect gives the same numbers as an Ascendant Perfect. Tier governs
    /// which content drops it (and, later, which named EFFECT it carries per
    /// GEAR_MODULE_SYSTEM.md), not raw stats.
    /// A weapon's 2 sockets therefore give +4/+6 Common, +12/+16 Refined, +30/+36 Perfect.
    /// </summary>
    public static (int atk, int str) StatsFor(Quality q) => q switch
    {
        Quality.Common  => (2, 3),
        Quality.Refined => (6, 8),
        _               => (15, 18),   // Perfect
    };

    public static void Register(Action<ItemData> add)
    {
        for (int t = 1; t <= 6; t++)
        {
            foreach (Quality q in Enum.GetValues(typeof(Quality)))
            {
                // Perfect is retired — only Common and Refined exist in the game now (12 modules,
                // not 18). The enum member and its id stride stay so tier ids don't reshuffle; the
                // Perfect ids (502, 505, …) are simply never registered or dropped.
                if (q == Quality.Perfect) continue;

                int id = IdFor(t, q);
                string qn = q.ToString();
                var (atk, str) = StatsFor(q);
                add(new ItemData
                {
                    id = id,
                    name = $"{TierNames[t - 1]} Module ({qn})",
                    description = $"{TierFlavour[t - 1]} +{atk} accuracy, +{str} power when socketed.",
                    type = ItemType.Resource,
                    stackable = true,
                    attackBonus = atk,
                    strengthBonus = str,
                });
            }
        }
    }
}
