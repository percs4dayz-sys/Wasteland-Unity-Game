using System;

/// <summary>
/// XP → level tables. Two curves, because Endurance is not like the other skills.
///
///   • STANDARD skills: levels 1-99, 1,918,756 XP at 99. Same shape as OSRS but with 2^(n/9)
///     instead of 2^(n/7), which makes it roughly 6.8x gentler than the real thing.
///
///   • ENDURANCE: levels 10-220, ~4M XP at 220. Endurance IS your hit points — the level and the
///     HP number are the same thing — so it starts at 10 (OSRS convention: you are never a 1 HP
///     character) and climbs to the 220 ceiling. It costs about DOUBLE a normal skill's 99 on
///     purpose, so max HP deliberately lags the rest of your combat stats and stays a long chase
///     after Melee and Marksmanship are finished.
/// </summary>
public static class XPTable
{
    public const int MaxLevel       = 99;    // every skill except Endurance
    public const int EnduranceStart = 10;    // a fresh character has 10 HP, never 1
    public const int EnduranceMax   = 220;

    // Divisor in the 2^(n/D) term. 9 is the standard curve; 20.25 is tuned so Endurance's full
    // 10 → 220 climb lands near 4M XP — about twice a normal skill's 99.
    const double StandardDivisor  = 9.0;
    const double EnduranceDivisor = 20.25;

    private static readonly int[] _standard  = BuildTable(1, MaxLevel, StandardDivisor);
    private static readonly int[] _endurance = BuildTable(EnduranceStart, EnduranceMax, EnduranceDivisor);

    /// <summary>
    /// Cumulative XP per level: floor each term, sum, then floor(total / 4) — the OSRS shape.
    /// <paramref name="startLevel"/> is the level a fresh skill sits at, and costs 0 XP.
    /// </summary>
    private static int[] BuildTable(int startLevel, int maxLevel, double divisor)
    {
        var t = new int[maxLevel + 1];
        for (int lvl = startLevel; lvl <= maxLevel; lvl++)
        {
            double total = 0;
            for (int n = startLevel; n < lvl; n++)
                total += Math.Floor(n + 300.0 * Math.Pow(2.0, n / divisor));
            t[lvl] = (int)Math.Floor(total / 4.0);
        }
        return t;
    }

    private static bool IsEndurance(Skill skill) => skill == Skill.Endurance;

    /// <summary>Highest level this skill can reach — 220 for Endurance, 99 for everything else.</summary>
    public static int MaxLevelFor(Skill skill) => IsEndurance(skill) ? EnduranceMax : MaxLevel;

    /// <summary>Level a fresh skill starts at — 10 for Endurance, 1 for everything else.</summary>
    public static int MinLevelFor(Skill skill) => IsEndurance(skill) ? EnduranceStart : 1;

    // ── skill-aware API (use these) ──────────────────────────────────────────
    public static int XPForLevel(Skill skill, int level)
    {
        var t = IsEndurance(skill) ? _endurance : _standard;
        return t[Math.Clamp(level, MinLevelFor(skill), MaxLevelFor(skill))];
    }

    // NOTE: the loop starts AT the max level, not one below it. The original version began at 98,
    // so _table[99] was never tested and level 99 was literally unreachable — every skill in the
    // game silently capped at 98 no matter how much XP you banked.
    public static int LevelForXP(Skill skill, int xp)
    {
        var t   = IsEndurance(skill) ? _endurance : _standard;
        int min = MinLevelFor(skill);
        for (int lvl = MaxLevelFor(skill); lvl >= min; lvl--)
            if (xp >= t[lvl]) return lvl;
        return min;
    }

    public static int XPToNextLevel(Skill skill, int currentXP)
    {
        int lvl = LevelForXP(skill, currentXP);
        if (lvl >= MaxLevelFor(skill)) return 0;
        var t = IsEndurance(skill) ? _endurance : _standard;
        return t[lvl + 1] - currentXP;
    }

    // ── legacy 1-99 API ──────────────────────────────────────────────────────
    // Kept for the standard skills. Do NOT call these for Endurance: they read the 1-99 table and
    // would report a level-99 cap for a skill that goes to 220.
    public static int XPForLevel(int level)      => _standard[Math.Clamp(level, 1, MaxLevel)];

    public static int LevelForXP(int xp)
    {
        for (int lvl = MaxLevel; lvl >= 1; lvl--)
            if (xp >= _standard[lvl]) return lvl;
        return 1;
    }

    public static int XPToNextLevel(int currentXP)
    {
        int lvl = LevelForXP(currentXP);
        if (lvl >= MaxLevel) return 0;
        return _standard[lvl + 1] - currentXP;
    }
}
