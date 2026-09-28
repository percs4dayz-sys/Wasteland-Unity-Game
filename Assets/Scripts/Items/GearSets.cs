using System.Collections.Generic;

/// <summary>
/// Full-set bonuses. Wearing every piece of a set turns it from "good armour" into a build —
/// the payoff for a long, low-odds drop chase off the island boss.
///
/// Only the Ascendant sets have bonuses today. Everything else is flat tiers, which is what makes
/// completing one feel different rather than just numerically bigger.
///
/// Damage is multiplicative (it stacks on top of max hit), defence is additive into the roll
/// alongside worn armour. Both only apply to the matching combat style, so the melee set does
/// nothing for shooting and vice versa — you commit to one.
/// </summary>
public static class GearSets
{
    public const string AscendantMelee  = "ascendant_melee";
    public const string AscendantRanged = "ascendant_ranged";

    public class SetDef
    {
        public string id;
        public string displayName;
        public int pieceCount;            // how many distinct pieces complete it
        public WeaponStyle style;         // which combat style the bonus applies to
        public float damageMultiplier;    // 1.5 = +50% max hit
        public int bonusDefence;          // added to the equipment defence bonus
        public string completeMessage;
    }

    static readonly Dictionary<string, SetDef> Defs = new Dictionary<string, SetDef>
    {
        [AscendantMelee] = new SetDef
        {
            id = AscendantMelee,
            displayName = "Ascendant Warplate",
            pieceCount = 4,                       // helm, plate, greaves, bulwark
            style = WeaponStyle.Melee,
            damageMultiplier = 1.60f,             // +60% max hit
            // On top of the set's own 520 defence. Doubles it, putting a completed set far beyond
            // anything reachable by tier progression alone.
            bonusDefence = 520,
            completeMessage = "The Warplate seals around you. Something in it is awake, and it is on your side.",
        },
        [AscendantRanged] = new SetDef
        {
            id = AscendantRanged,
            displayName = "Ascendant Marksuit",
            pieceCount = 3,                       // visor, rig, leggings
            style = WeaponStyle.Ranged,
            damageMultiplier = 1.60f,
            bonusDefence = 416,                   // doubles the ranged set's own 416
            completeMessage = "The Marksuit syncs. Targets resolve like they're standing still.",
        },
    };

    public static SetDef Get(string setId) =>
        setId != null && Defs.TryGetValue(setId, out var d) ? d : null;

    /// <summary>How many distinct pieces of this set are currently worn.</summary>
    public static int WornCount(Equipment eq, string setId)
    {
        if (eq == null || setId == null) return 0;
        int n = 0;
        foreach (string slot in Equipment.Slots)
        {
            var item = eq.GetItem(slot);
            if (item != null && item.setId == setId) n++;
        }
        return n;
    }

    /// <summary>The completed set for this combat style, or null if none is complete.</summary>
    public static SetDef ActiveFor(Equipment eq, WeaponStyle style)
    {
        if (eq == null) return null;
        foreach (var kv in Defs)
        {
            var def = kv.Value;
            if (def.style != style) continue;
            if (WornCount(eq, def.id) >= def.pieceCount) return def;
        }
        return null;
    }

    /// <summary>Damage multiplier from a completed set, or 1 if none. Never returns below 1.</summary>
    public static float DamageMultiplier(Equipment eq, WeaponStyle style)
    {
        var d = ActiveFor(eq, style);
        return d != null ? UnityEngine.Mathf.Max(1f, d.damageMultiplier) : 1f;
    }

    /// <summary>Extra defence from a completed set, for either style — defence applies whichever
    /// set you finished, since you're wearing it regardless of what you're swinging.</summary>
    public static int BonusDefence(Equipment eq)
    {
        int best = 0;
        foreach (var kv in Defs)
        {
            var def = kv.Value;
            if (WornCount(eq, def.id) >= def.pieceCount && def.bonusDefence > best)
                best = def.bonusDefence;
        }
        return best;
    }
}
