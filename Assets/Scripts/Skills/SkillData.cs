using System;
using System.Collections.Generic;

[Serializable]
public class SkillData
{
    public Skill skill;
    public string displayName;
    public string description;
    public string levelUpFlavour;
    public int xp;

    // Skill-aware throughout: Endurance runs 10-220, every other skill 1-99.
    public int Level => XPTable.LevelForXP(skill, xp);
    public int XPToNext => XPTable.XPToNextLevel(skill, xp);

    public float LevelProgress
    {
        get
        {
            int lvl = Level;
            if (lvl >= XPTable.MaxLevelFor(skill)) return 1f;
            int cur = xp - XPTable.XPForLevel(skill, lvl);
            int total = XPTable.XPForLevel(skill, lvl + 1) - XPTable.XPForLevel(skill, lvl);
            return total > 0 ? (float)cur / total : 0f;
        }
    }

    public void AddXP(int amount, out int oldLevel, out int newLevel)
    {
        oldLevel = Level;
        xp += amount;
        newLevel = Level;
    }

    public static List<SkillData> GetDefaults() => new()
    {
        new() { skill = Skill.Attack,       displayName = "Attack",       description = "Melee accuracy — how reliably your swings land.", levelUpFlavour = "Your strikes grow sharper." },
        new() { skill = Skill.Strength,     displayName = "Strength",     description = "Raw melee power. Trained via Aggressive stance.", levelUpFlavour = "Your blows land with crushing force." },
        new() { skill = Skill.Defence,      displayName = "Defence",      description = "Melee defence and resilience. Trained via Defensive stance.", levelUpFlavour = "Your body hardens against blows." },
        new() { skill = Skill.Endurance,    displayName = "Hit Points",   description = "Your health pool — this level IS your HP, up to 250.", levelUpFlavour = "Your body endures more." },
        new() { skill = Skill.Marksmanship, displayName = "Marksmanship", description = "Ranged combat with firearms.", levelUpFlavour = "Your aim sharpens." },
        new() { skill = Skill.Fishing,      displayName = "Fishing",      description = "Fishing food from hazardous waterways.", levelUpFlavour = "You know where the catches hide." },
        new() { skill = Skill.Cooking,      displayName = "Cooking",      description = "Cooking raw catches into meals.", levelUpFlavour = "You know how to survive." },
        new() { skill = Skill.Woodcutting,  displayName = "Woodcutting",  description = "Harvesting lumber and timber.", levelUpFlavour = "You fell the wastes' dead wood." },
        new() { skill = Skill.Smithing,     displayName = "Smithing",     description = "Smelting ore into bars, and forging bars into weapons, armour and ammo.", levelUpFlavour = "Your work comes out cleaner and stronger." },
        new() { skill = Skill.Scrapping,    displayName = "Scrapping",    description = "Digging scrap metal and ore from junk.", levelUpFlavour = "You dig deeper." },
        new() { skill = Skill.Beastmastery, displayName = "Beastmastery", description = "Taming and commanding mutated beasts.", levelUpFlavour = "The pack trusts you more." },
        new() { skill = Skill.Fission,      displayName = "Fission",      description = "Combat with reactor-fed power gauntlets.", levelUpFlavour = "The reactor's rhythm makes more sense to you." },
        new() { skill = Skill.Refinement,   displayName = "Refinement",   description = "Refining raw fission cores into gauntlet ammunition.", levelUpFlavour = "Your cores come out cleaner and hotter." },
    };
}
