using UnityEngine;

/// <summary>
/// Combat-skill milestone passives from SkillGuide — the special effects that switch on at level
/// thresholds, layered on top of the base accuracy/damage scaling in CombatMath. All randomness and
/// tuning lives here so the combat paths (ActionCombat3D outgoing, Enemy3D incoming) just ask a
/// yes/no or a damage multiplier. Tune the chances/magnitudes in one place.
///
/// Skill split:
///   Melee      — accuracy (attack roll, armor bypass)
///   Brutality  — raw power (max hit, power attacks, stagger, cleave, boss bonus)
///   Hardening  — defence (damage reduction — applied in Enemy3D's damage calc)
///   Endurance  — HP pool
///   Marksmanship — ranged accuracy + damage + perks
/// </summary>
public static class CombatEffects
{
    // ── Melee (accuracy perks) ───────────────────────────────────────────
    // L60: armor bypass — chance to ignore the target's defence (a near-guaranteed hit).
    public static bool ArmorBypass(int melee) => melee >= 60 && Random.value < 0.10f;

    // ── Brutality (raw power perks) ──────────────────────────────────────
    // L20: power attacks — chance for a heavier swing.
    public static bool PowerAttack(int brutality) => brutality >= 20 && Random.value < 0.12f;
    public const float PowerAttackMult = 1.4f;

    // L40: stagger — chance to delay the target's next attack.
    public static bool Stagger(int brutality) => brutality >= 40 && Random.value < 0.15f;
    public const float StaggerSeconds = 1.2f;

    // L80: cleave — chance to splash damage onto nearby enemies.
    public static bool Cleave(int brutality) => brutality >= 80 && Random.value < 0.20f;
    public const float CleaveRadius   = 2.5f;
    public const float CleaveFraction = 0.5f;

    // L99: bonus damage on the first strike against a freshly-engaged target, and vs bosses.
    public const int   FirstStrikeLevel = 99;
    public const float FirstStrikeMult  = 1.5f;
    public static float FirstStrikeMultiplier(int brutality, bool isFirstStrike)
        => (isFirstStrike && brutality >= FirstStrikeLevel) ? FirstStrikeMult : 1f;
    public static float BossMultiplier(int brutality, bool isBoss)
        => (isBoss && brutality >= 99) ? 1.25f : 1f;

    // ── Hardening (defence perks) ────────────────────────────────────────
    // L20: Thick Skin — 5% passive melee damage reduction.
    public static float DamageReduction(int hardening)
    {
        if (hardening < 20) return 0f;
        float dr = 0.05f;   // L20: 5%
        if (hardening >= 60) dr = 0.15f;   // L60: Battle Scarred — 15% at low HP
        if (hardening >= 99) dr = 0.25f;   // L99: Unbreakable — 25%
        return dr;
    }
    // L40: Iron Will — chance to ignore stagger.
    public static bool IgnoreStagger(int hardening) => hardening >= 40 && Random.value < 0.10f;
    // L60: very small chance to completely negate an incoming hit.
    public static bool NegateHit(int hardening) => hardening >= 60 && Random.value < 0.05f;
    // L80: Juggernaut — cannot be staggered while attacking (handled in Enemy3D).
    // L99: 8% chance to reflect incoming damage back to the attacker.
    public static bool ReflectDamage(int hardening) => hardening >= 99 && Random.value < 0.08f;

    // ── Marksmanship (ranged) ────────────────────────────────────────────
    // L40: headshot — chance for bonus damage.
    public static bool Headshot(int marks) => marks >= 40 && Random.value < 0.12f;
    public const float HeadshotMult = 1.6f;
    // L99: consecutive hits on the same target ramp damage (capped).
    public static float ConsecutiveMultiplier(int marks, int streak)
        => marks >= 99 ? 1f + Mathf.Min(streak, 5) * 0.06f : 1f;

    // ── Fission (power gauntlets) ────────────────────────────────────────
    // Meltdown: a flat chance for a blast to deal DOUBLE the loaded core's max hit. 1% at level 1,
    // +1% per 10 levels, capping at 10% from level 90. Unlike every other style's passives this one
    // is live from level 1 — it is the only thing Fission levels give besides accuracy, since all of
    // its damage comes from the core.
    public const int MeltdownMaxPercent = 10;

    /// <summary>Meltdown proc chance as a PERCENT (1-10) for a Fission level.</summary>
    public static int MeltdownPercent(int fission)
        => Mathf.Clamp(fission / 10 + 1, 1, MeltdownMaxPercent);

    /// <summary>Rolls the Meltdown proc. On success the blast deals exactly 2x the core's max hit.</summary>
    public static bool Meltdown(int fission)
        => Random.value < MeltdownPercent(fission) / 100f;

    public const int MeltdownMultiplier = 2;
}
