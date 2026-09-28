using UnityEngine;

/// <summary>
/// Old School RuneScape-style combat math, shared by every combat path so 2D and 3D can't drift.
///
///   Effective level = base level + stance bonus + 8           (no prayer in this game)
///   Attack roll     = effectiveAtk * (equipment attack bonus  + 64)
///   Defence roll    = effectiveDef * (equipment defence bonus + 64)
///   Hit chance      = atk>def ?  1 - (def+2)/(2*(atk+1))  :  atk/(2*(def+1))
///   Max hit         = floor( 0.5 + effectiveStr * (strength bonus + 64) / 640 )
///   Damage roll     = random 0..maxHit on a landed hit
///
/// Levels come from skills (Melee = both melee accuracy AND strength; Marksmanship = ranged
/// accuracy AND strength); defence LEVEL is a flat base now (defence = worn armor bonus only).
/// The +bonuses come from equipped TierGear.
/// </summary>
public static class CombatMath
{
    /// <summary>Global damage tuning knob. 1.0 = pure OSRS. Bump it if fights drag because our
    /// Endurance HP pool (up to 220) scales higher than OSRS's 99.</summary>
    public const float DamageScale = 1f;

    public static int EffectiveLevel(int level, int stanceBonus) => level + stanceBonus + 8;

    public static int AttackRoll(int atkLevel, int equipAttackBonus, int stanceBonus)
        => EffectiveLevel(atkLevel, stanceBonus) * (equipAttackBonus + 64);

    public static int DefenceRoll(int defLevel, int equipDefenceBonus, int stanceBonus)
        => EffectiveLevel(defLevel, stanceBonus) * (equipDefenceBonus + 64);

    public static float HitChance(int attackRoll, int defenceRoll)
    {
        if (attackRoll > defenceRoll)
            return 1f - (defenceRoll + 2f) / (2f * (attackRoll + 1f));
        return attackRoll / (2f * (defenceRoll + 1f));
    }

    public static int MaxHit(int strLevel, int equipStrengthBonus, int stanceBonus)
    {
        int eff = EffectiveLevel(strLevel, stanceBonus);
        int max = Mathf.FloorToInt(0.5f + (float)(eff * (equipStrengthBonus + 64)) / 640f);
        return Mathf.Max(1, Mathf.RoundToInt(max * DamageScale));
    }

    /// <summary>Damage of a landed hit: a uniform roll 0..maxHit (OSRS allows a 0).</summary>
    public static int RollDamage(int maxHit) => Random.Range(0, Mathf.Max(1, maxHit) + 1);
}
