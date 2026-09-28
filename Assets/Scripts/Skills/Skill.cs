/// <summary>
/// Every skill in the game. Names deliberately match Old School RuneScape wherever the job is the
/// same job — invented names (Bladework, Hearthcraft, Tinkering, Sustenance) proved impossible to
/// remember and made it unclear that e.g. Hearthcraft and Tinkering were two halves of Smithing.
///
/// ORDER IS SERIALIZED DATA. ResourceNode.skill, CraftingRecipe.skill and AFKStation.skill are all
/// stored as the integer index in prefabs and scenes, so reordering silently repoints them at the
/// wrong skill. Renaming a member is free; MOVING or INSERTING one is not. Append only.
///
/// This is why Hearthcraft's slot (8) became Smithing rather than being deleted: 11 scrap-node
/// prefabs sit at index 9, and deleting 8 would have slid them onto the wrong skill. Tinkering's
/// slot (10) was the safe one to drop, because nothing below it is referenced by any prefab.
/// </summary>
public enum Skill
{
    // ── Melee. "Melee" is the CATEGORY these three live in, never a skill itself. ──
    Attack,         // melee accuracy — trained via Precise/Rapid stance   (was Bladework)
    Strength,       // melee max hit — trained via Aggressive/Powerful     (was Brutality)
    Defence,        // melee defence — trained via Defensive stance        (was Hardening)

    Endurance,      // HP. The only skill that passes 99: runs 10-220, and IS your hit points.
    Marksmanship,   // ranged / guns
    Fishing,        // (was "Scavenging")
    Cooking,        // (was "Sustenance")
    Woodcutting,    // (was "Salvaging")

    // Smelting ore into bars AND forging those bars into gear — one job, one skill, like OSRS.
    // Previously split across Hearthcraft (smelting, this slot) and Tinkering (forging, deleted).
    Smithing,       // (was "Hearthcraft", and absorbed "Tinkering")

    Scrapping,      // mining, essentially — scrap nodes, sulphur, golem corpses  (was "Excavation")
    Beastmastery,   // taming & commanding a pack of mutated beasts
    Fission,        // third combat style — the "magic" slot. Trained by FIGHTING with power
                    // gauntlets; its ammunition comes from the golem loop.
    Refinement      // the game's Runecrafting: refines raw fission cores into gauntlet ammo.
                    // Deliberately slow and awkward, deliberately worth it. Gathering the raw
                    // cores off a golem corpse trains Scrapping; only refining trains this.
}

public static class Skills
{
    /// <summary>
    /// Attack, Strength and Defence — the melee/defence line. These were hidden during the
    /// first-person-shooter phase. The game has since gone BACK to tick-based point-and-click combat,
    /// so the melee line is live again: this predicate now returns false, which un-hides the skills in
    /// the panel, counts them toward the combat level, and lets the tier portal wait on them again.
    /// The single predicate is kept as the on/off switch — return the old expression to re-retire.
    /// </summary>
    public static bool IsRetiredMelee(Skill s) => false;
}
