using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Weapon specials — one per combat style's endgame identity (all cost special ENERGY, not cooldown):
///   Melee        SiphonStrike     big hit, heals you for half the damage dealt (sustain)
///   Marksmanship DialIn           next 3 shots hit far harder, each still rolling accuracy (chase weapon)
///                StunShot         one accurate shot that pins the target in place (control)
///   Fission      OverloadCascade  the original four-blast claws-style burst (all older gauntlets)
///                RapidBlast       six rapid blasts; the endgame gauntlets also irradiate (damage over time)
/// </summary>
public enum WeaponSpecial { None, OverloadCascade, SiphonStrike, DialIn, StunShot, RapidBlast }

public static class WeaponSpecials
{
    public const int WarbladeId = 159, RailgunId = 160;
    public const int AscendantGauntlets = 162, StasisRifle = 163;

    public static string NameOf(WeaponSpecial s) => s switch
    {
        WeaponSpecial.OverloadCascade => "Overload Cascade",
        WeaponSpecial.SiphonStrike    => "Siphon Strike",
        WeaponSpecial.DialIn          => "Dial In",
        WeaponSpecial.StunShot        => "Stun Shot",
        WeaponSpecial.RapidBlast      => "Rapid Blast",
        _ => "No special",
    };

    /// <summary>Registers the new special weapons and stamps specials onto the existing endgame weapons.
    /// Called from ItemRegistry after TierGear/FissionItems so those items already exist.</summary>
    public static void Register(Action<ItemData> add)
    {
        var warblade = ItemRegistry.Get(WarbladeId);
        if (warblade != null) warblade.special = WeaponSpecial.SiphonStrike;
        var railgun = ItemRegistry.Get(RailgunId);
        if (railgun != null) railgun.special = WeaponSpecial.DialIn;

        // Endgame Fission gauntlets: the "Dragon Claws" moment. Highest accuracy and magazine in the
        // line; the special and the radiation damage-over-time are what set it apart.
        add(new ItemData
        {
            id = AscendantGauntlets, name = "Ascendant Gauntlets", type = ItemType.Weapon, stackable = false,
            weaponStyle = WeaponStyle.Fission, twoHanded = true,
            magazineSize = 6, reloadSeconds = 1.4f, attackSpeed = 4, attackRange = 12,
            attackBonus = 300, ammoSaveChance = 0.30f,
            special = WeaponSpecial.RapidBlast, radiation = true,
            requirements = new Dictionary<Skill, int> { [Skill.Fission] = 90 },
            description = "Endgame reactor gauntlets. Every hit leaves the target irradiated, burning for a few seconds. " +
                          "Special: Rapid Blast — six blasts in a burst, twice from a full bar.",
            heldModel     = "HeldModels/Weapons/InfernoGauntletRight",   // placeholder until its own mesh exists
            heldModelLeft = "HeldModels/Weapons/InfernoGauntletLeft",
        });

        // Marksmanship control weapon: pins enemies in place with its special.
        add(new ItemData
        {
            id = StasisRifle, name = "Stasis Rifle", type = ItemType.Weapon, stackable = false,
            weaponStyle = WeaponStyle.Ranged,
            attackBonus = 300, strengthBonus = 50, attackRange = 6, attackSpeed = 4,
            magazineSize = 12, reloadSeconds = 1.6f,
            special = WeaponSpecial.StunShot,
            requirements = new Dictionary<Skill, int> { [Skill.Marksmanship] = 80 },
            description = "A precision rifle with a gravity-lock round. Special: Stun Shot — the target can't move toward you for several seconds (bosses resist).",
            heldModel = "HeldModels/Weapons/StasisRifle",
        });
    }
}

/// <summary>Radiation damage-over-time on a CombatTarget. Re-applying refreshes the duration and keeps
/// the stronger tick — it never stacks past one burn per enemy.</summary>
public class RadiationDot : MonoBehaviour
{
    public const int Ticks = 5;
    public const float TickSeconds = 0.6f;
    public const float DamageFraction = 0.12f;   // of the hit that applied it, per tick

    int _ticksLeft, _perTick;
    float _nextTick;
    CombatTarget _ct;

    public static void Apply(CombatTarget target, int hitDamage)
    {
        if (target == null || target.IsDead || hitDamage <= 0) return;
        var dot = target.GetComponent<RadiationDot>() ?? target.gameObject.AddComponent<RadiationDot>();
        dot._ct = target;
        dot._perTick = Mathf.Max(dot._ticksLeft > 0 ? dot._perTick : 0, Mathf.Max(1, Mathf.RoundToInt(hitDamage * DamageFraction)));
        dot._ticksLeft = Ticks;
        if (dot._nextTick < Time.time) dot._nextTick = Time.time + TickSeconds;
    }

    void Update()
    {
        if (_ct == null || _ct.IsDead) { _ticksLeft = 0; return; }
        if (_ticksLeft <= 0 || Time.time < _nextTick) return;
        _nextTick = Time.time + TickSeconds;
        _ticksLeft--;
        int dmg = Mathf.Min(_perTick, _ct.CurrentHP);
        _ct.TakeDamage(dmg);
        CombatFeedbackUI.ShowWorldSplat(_ct.transform.position, dmg, true);
        if (_ticksLeft <= 0) _perTick = 0;
    }
}
