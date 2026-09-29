using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    private readonly Dictionary<Skill, SkillData> _skills = new();

    public event Action<Skill, int> OnLevelUp;
    public event Action<Skill, int> OnXPGained;
    public event Action<int, int> OnHPChanged;
    public event Action OnPlayerDied;

    public int CurrentHP { get; private set; }

    /// <summary>Endurance IS hit points — the level and the HP number are the same value, 10 to 250.</summary>
    public int MaxHP => GetLevel(Skill.Endurance);

    /// <summary>True from the moment HP hits 0 until <see cref="Revive"/> — blocks further damage,
    /// re-death spam, and eating while down (PlayerRespawn handles getting back up).</summary>
    public bool IsDead { get; private set; }

    /// <summary>
    /// Hit points for an Endurance level. Now a straight identity — the skill IS the HP pool, so
    /// level 10 is 10 HP and level 220 is 220 HP. This replaced an interpolation table that mapped
    /// levels 1-99 onto 10-220 HP; with Endurance itself running 10-220 that indirection is gone,
    /// and there is only one number to reason about.
    /// </summary>
    public static int HPForLevel(int level)
        => Mathf.Clamp(level, XPTable.EnduranceStart, XPTable.EnduranceMax);

    void Awake()
    {
        foreach (var sd in SkillData.GetDefaults())
            _skills[sd.skill] = sd;
        CurrentHP = MaxHP;
    }

    public void AddXP(Skill skill, int amount)
    {
        if (amount <= 0) return;
        if (!_skills.TryGetValue(skill, out var sd)) return;   // retired/unknown skill → no-op
        int oldLvl = sd.Level;
        sd.xp += amount;

        int gained = amount;
        int newLvl = sd.Level;
        OnXPGained?.Invoke(skill, gained);
        if (newLvl > oldLvl)
        {
            if (skill == Skill.Endurance)
            {
                int delta = HPForLevel(newLvl) - HPForLevel(oldLvl);
                CurrentHP = Mathf.Min(CurrentHP + delta, MaxHP);
            }
            OnLevelUp?.Invoke(skill, newLvl);
        }
    }

    /// <summary>Sum of every skill's level — the OSRS-style "total level".</summary>
    public int TotalLevel()
    {
        int total = 0;
        foreach (var sd in _skills.Values) total += sd.Level;
        return total;
    }

    // Null-safe: an unknown skill returns that skill's MINIMUM level rather than a bare 1, so a
    // missing Endurance entry reports 10 (and therefore 10 HP), never 1 HP. Stray loops over all
    // enum values still work without throwing.
    public int GetLevel(Skill skill) =>
        _skills.TryGetValue(skill, out var sd) ? sd.Level : XPTable.MinLevelFor(skill);
    public int GetXP(Skill skill) => _skills.TryGetValue(skill, out var sd) ? sd.xp : 0;
    public SkillData GetSkillData(Skill skill) => _skills.TryGetValue(skill, out var sd) ? sd : null;
    public IEnumerable<SkillData> AllSkills() => _skills.Values;

    public void SetXP(Skill skill, int xp)
    {
        if (_skills.TryGetValue(skill, out var sd)) sd.xp = xp;
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;                       // already down — wait for respawn
        CurrentHP = Mathf.Max(0, CurrentHP - amount);
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        if (CurrentHP == 0) { IsDead = true; OnPlayerDied?.Invoke(); }
    }

    public void Heal(int amount)
    {
        if (IsDead) return;                       // no eating your way out of death
        CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
    }

    /// <summary>Bring the player back to life at full HP. Called by PlayerRespawn after the
    /// death animation, alongside teleporting to the spawn point.</summary>
    public void Revive()
    {
        IsDead = false;
        CurrentHP = MaxHP;
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
    }

    // Beastmastery (needs taming), Fission (needs power gauntlets) and Refinement (needs raw cores
    // off an Elemental Golem) are advanced skills with no tutorial-island content. None of them is
    // part of the tutorial requirement, or the portal could never open.
    private static bool IsTutorialSkill(Skill s) =>
        !Skills.IsRetiredMelee(s) &&   // melee/defence are retired — the portal must not wait on them
        s != Skill.Beastmastery && s != Skill.Fission && s != Skill.Refinement;

    public bool IsReadyToLeaveTutorial()
    {
        foreach (var sd in _skills.Values)
            if (IsTutorialSkill(sd.skill) && sd.Level < 2) return false;
        return true;
    }

    public List<string> GetIncompleteSkills()
    {
        var list = new List<string>();
        foreach (var sd in _skills.Values)
            if (IsTutorialSkill(sd.skill) && sd.Level < 2) list.Add(sd.displayName);
        return list;
    }

    /// <summary>
    /// Combat level for the shooter. Melee (Attack/Strength) and Defence are retired, so only the two
    /// live combat styles and the HP pool feed it:
    ///   Marksmanship → ranged term        Fission → "magic" term        Endurance → base (HP)
    ///
    ///   base   = 0.25  * Endurance
    ///   ranged = 0.325 * (floor(Marksmanship / 2) + Marksmanship)
    ///   magic  = 0.325 * (floor(Fission / 2)      + Fission)
    ///   level  = floor(base + max(ranged, magic))
    ///
    /// Only the single BEST style counts, so a pure gunslinger is never punished for ignoring the
    /// gauntlets, and vice-versa. Endurance running 10-220 (rather than 99) lifts the ceiling: the
    /// base term alone reaches 0.25 * 220 = 55. Nothing is gated on this number — it is a display /
    /// prestige stat — so there is no clamp.
    ///
    /// Arithmetic is double, not float — the components land on exact .25 / .325 boundaries and float
    /// rounding could drop a level right at the edge.
    /// </summary>
    public int GetCombatLevel()
    {
        int endurance = GetLevel(Skill.Endurance);
        int attack    = GetLevel(Skill.Attack);
        int strength  = GetLevel(Skill.Strength);
        int defence   = GetLevel(Skill.Defence);
        int marks     = GetLevel(Skill.Marksmanship);
        int fission   = GetLevel(Skill.Fission);

        // OSRS structure with our Endurance-as-HP model: base = HP + Defence; then the best of the
        // three offensive styles. Melee returned with the tick-combat revert, so it's back in the max.
        double baseComp  = 0.25  * (endurance + defence);
        double meleeComp = 0.325 * (attack + strength);
        double rangeComp = 0.325 * (marks   / 2 + marks);     // int division IS the floor
        double magicComp = 0.325 * (fission / 2 + fission);

        double best = System.Math.Max(meleeComp, System.Math.Max(rangeComp, magicComp));
        return System.Math.Max(1, (int)System.Math.Floor(baseComp + best));
    }
}
