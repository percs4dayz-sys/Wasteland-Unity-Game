using System.Collections.Generic;

/// <summary>
/// Progression reference for every skill — the OSRS-style "skill guide" content.
/// Data is taken directly from WastelandRPG_SkillReference, mapped to the game's
/// 11-skill enum by theme.
/// </summary>
public static class SkillGuide
{
    public readonly struct Milestone
    {
        public readonly int level;
        public readonly string unlock;
        public Milestone(int level, string unlock) { this.level = level; this.unlock = unlock; }
    }

    /// <summary>One-line description shown under the guide title.</summary>
    public static string Subtitle(Skill skill) => _subtitles.TryGetValue(skill, out var s) ? s : "";

    /// <summary>One-line "how / where to train this" hint (for the skills-panel tooltip).</summary>
    public static string TrainedBy(Skill skill) => _trainedBy.TryGetValue(skill, out var s) ? s : "";

    /// <summary>Milestone unlocks for a skill (ascending level).</summary>
    public static Milestone[] For(Skill skill)
    {
        var result = new List<Milestone>(_guides.TryGetValue(skill, out var m) ? m : System.Array.Empty<Milestone>());
        for (int id = GatheringTools.FirstId; id <= GatheringTools.LastId; id++)
        {
            if (skill != Skill.Smithing && GatheringTools.GatheringSkill(id) != skill) continue;
            int bonus = GatheringTools.Rank(id) == 1 ? 15 : GatheringTools.Rank(id) == 2 ? 30 : 50;
            result.Add(new Milestone(GatheringTools.RequiredLevel(id),
                skill == Skill.Smithing ? "Craft " + ItemRegistry.Get(id).name + " at a Workbench."
                    : "Use " + ItemRegistry.Get(id).name + $" — {bonus}% faster gathering."));
        }
        result.Sort((a, b) => a.level.CompareTo(b.level));
        return result.ToArray();
    }

    private static readonly Dictionary<Skill, string> _subtitles = new()
    {
        [Skill.Attack]       = "Melee accuracy — how reliably your swings land. Gates the melee weapon tiers.",
        [Skill.Strength]     = "Raw melee power. Trained via Powerful (Aggressive) stance. Determines max hit.",
        [Skill.Defence]      = "Melee defence and durability. Trained via Defensive stance. Reduces damage taken.",
        [Skill.Endurance]    = "Your hit-point pool — this level IS your HP, up to 250. Trains off any combat.",
        [Skill.Marksmanship] = "Ranged accuracy and damage. Gates all firearm tiers.",
        [Skill.Fishing]   = "Food fished from hazardous waterways. Feeds Cooking.",
        [Skill.Cooking]   = "Cooking raw catches into healing meals and buff foods.",
        [Skill.Woodcutting]    = "Harvesting lumber and structural timber. Feeds ammo pressing.",
        [Skill.Smithing]     = "Smelt scrap into bars at a Furnace, then forge those bars into all armor, weapons and ammo at a Workbench.",
        [Skill.Scrapping]   = "Digging scrap metal and components. Smelted into bars for Smithing.",
        [Skill.Beastmastery] = "Slayer-style hunting: your beast always has a task — kill a set number of one creature, matched to your tier. Higher levels = meaner prey and a stronger beast.",
        [Skill.Fission]      = "The third combat style. Power gauntlets fed by refined reactor cores — few shots, heavy hits.",
        [Skill.Refinement]   = "Refines raw fission cores into the charged cores that load into power gauntlets.",
    };

    // Concise "how/where to train it" hints — surfaced in the skills-panel hover tooltip.
    private static readonly Dictionary<Skill, string> _trainedBy = new()
    {
        [Skill.Attack]       = "Attack enemies with a melee weapon in Accurate stance.",
        [Skill.Strength]     = "Attack enemies in Powerful (Aggressive) stance with a melee weapon.",
        [Skill.Defence]      = "Attack enemies in Defensive stance with a melee weapon.",
        [Skill.Endurance]    = "Deal damage in any fight — it levels passively.",
        [Skill.Marksmanship] = "Shoot enemies with a gun (forge one at the Workbench).",
        [Skill.Fishing]   = "Fish at a Fishing Spot.",
        [Skill.Cooking]   = "Cook raw catches at a Cooking Fire.",
        [Skill.Woodcutting]    = "Strip wood from Wooden Debris.",
        [Skill.Smithing]     = "Smelt scrap into bars at a Furnace, then forge gear at a Workbench.",
        [Skill.Scrapping]   = "Break Rubble Piles for scrap.",
        [Skill.Beastmastery] = "Kill your beast task's creature with your beast out (B). Each kill gives XP equal to its max HP; finishing a task adds a 10% bonus.",
        [Skill.Fission]      = "Fight with Power Gauntlets loaded with refined fission cores.",
        [Skill.Refinement]   = "Refine raw fission cores at a Furnace. Get them by stripping an Elemental Golem's corpse (that part trains Scrapping).",
    };

    private static readonly Dictionary<Skill, Milestone[]> _guides = new()
    {
        // ── Combat ───────────────────────────────────────────────────────
        [Skill.Attack] = new[]   // melee accuracy
        {
            new Milestone(1,  "Equip Scrap Blade & Pipe Melee."),
            new Milestone(20, "Equip Steel Blade; unlock power attacks (chance for a heavier swing)."),
            new Milestone(40, "Equip Energy-Infused Blade; stagger chance delays the enemy's next hit."),
            new Milestone(60, "Equip Vibro-Blade; chance to bypass enemy defence."),
            new Milestone(80, "Equip Myomer Blade; devastating cleave to nearby enemies."),
            new Milestone(99, "Max melee - bonus first-strike damage and extra damage vs bosses."),
        },
        [Skill.Strength] = new[]   // raw melee power (Aggressive stance)
        {
            new Milestone(1,  "Base max-hit bonus from strength."),
            new Milestone(20, "Unlock heavy swings — chance for bonus damage on each hit."),
            new Milestone(40, "Stagger strikes — delay enemy attacks with crushing blows."),
            new Milestone(60, "Sunder — attacks have a chance to reduce enemy defence."),
            new Milestone(80, "Cleave — melee hits splash to nearby enemies."),
            new Milestone(99, "Maximum carnage — bonus damage vs bosses and guaranteed stagger."),
        },
        [Skill.Defence] = new[]   // melee defence (Defensive stance)
        {
            new Milestone(1,  "Base damage reduction from melee attacks."),
            new Milestone(20, "Thick Skin — 5% passive melee damage reduction."),
            new Milestone(40, "Iron Will — 10% chance to ignore stagger."),
            new Milestone(60, "Battle Scarred — 15% damage reduction at low HP."),
            new Milestone(80, "Juggernaut — cannot be staggered while attacking."),
            new Milestone(99, "Unbreakable — 25% passive melee damage reduction."),
        },
        // Endurance IS your hit points — the level and the HP number are one and the same, so the
        // milestones are just notable points on the 10 → 220 climb. It is the only skill that goes
        // past 99, and costs roughly double a normal skill's 99 to finish.
        [Skill.Endurance] = new[]
        {
            new Milestone(10,  "Starting vitality — 10 HP."),
            new Milestone(25,  "25 HP — you survive a second mistake."),
            new Milestone(50,  "50 HP."),
            new Milestone(75,  "75 HP."),
            new Milestone(99,  "99 HP — where every other skill in the wasteland ends. You keep going."),
            new Milestone(125, "125 HP."),
            new Milestone(150, "150 HP — most things can no longer burst you down."),
            new Milestone(175, "175 HP."),
            new Milestone(200, "200 HP."),
            new Milestone(220, "220 HP — the old ceiling. Thirty more to go."),
            new Milestone(250, "250 HP — the ceiling. Nothing in the wasteland out-lasts you."),
        },
[Skill.Marksmanship] = new[]
        {
            new Milestone(1,  "Equip Pipe Pistol - base ranged accuracy."),
            new Milestone(20, "Equip Mid-tier Rifle - accuracy boost tier 1."),
            new Milestone(40, "Equip Alloy Rifle - accuracy boost tier 2, headshot chance."),
            new Milestone(60, "Equip Precision Rifle - long-range damage bonus."),
            new Milestone(80, "Equip Experimental Energy Rifle - accuracy boost tier 3."),
            new Milestone(99, "Max ranged accuracy - bonus damage on consecutive hits."),
        },

        // ── Gathering ────────────────────────────────────────────────────
        [Skill.Fishing] = new[]  // Fishing (food/water)
        {
            new Milestone(1,  "Stagnant Puddles: Sludge Minnow (basic low-heal food)."),
            new Milestone(20, "Toxic Runoff Streams: Mutated Carp (mid-tier meals)."),
            new Milestone(40, "Irradiated River: Glowfin Bass (strong healing meals)."),
            new Milestone(60, "Contaminated Lake: Deepwater Lurker (high-tier meals)."),
            new Milestone(80, "Flooded Ruins: Abyssal Specimen (endgame food)."),
        },
        [Skill.Scrapping] = new[]  // Scrapping (metal/electronic)
        {
            new Milestone(1,  "Junk Piles: Raw Scrap Metal -> Iron-Grade Bar."),
            new Milestone(20, "Abandoned Car: Salvaged Chassis -> Steel-Grade Bar."),
            new Milestone(40, "Tech Dumpster: Circuitry & Copper -> Wiring/Alloy Bar."),
            new Milestone(60, "Downed Military Drone: Heavy Plating -> Titanium-Alloy Bar."),
            new Milestone(80, "Abandoned Robotics: Synaptic Core -> Cyber-Steel/Myomer Bar."),
        },
        [Skill.Woodcutting] = new[]   // Woodcutting (structural material)
        {
            new Milestone(1,  "Dead Scrub & Brush: Dry Kindling -> Rough Lumber."),
            new Milestone(20, "Irradiated Thicket: Gnarled Timber -> Treated Lumber."),
            new Milestone(40, "Fossilized Deadwood: Dense Wood Core -> Hardened Lumber."),
            new Milestone(60, "Petrified Forest: Wood Shard -> Petrified Lumber."),
            new Milestone(80, "Ancient Hardened Flora: Heartwood -> Prime Lumber."),
        },

        // ── Production & utility ──────────────────────────────────────────
        [Skill.Cooking] = new[]  // Cooking
        {
            new Milestone(1,  "Grilled Minnow - low heal, removes minor toxin."),
            new Milestone(20, "Carp Stew - mid heal, small endurance buff."),
            new Milestone(40, "Seared Glowfin - strong heal, minor combat buff."),
            new Milestone(60, "Lurker Broth - high heal, sustained stat buffs."),
            new Milestone(80, "Abyssal Feast - max heal, powerful multi-stat buff."),
        },
        // ── Smithing (smelting ore → bars, then forging bars → gear) ──────
        [Skill.Smithing] = new[]
        {
            new Milestone(1,  "Smelt Iron-Grade Bars; forge Scrap Armor."),
            new Milestone(5,  "Iron-Grade Bar: Pipe Pistol, Scrap Blade."),
            new Milestone(10, "Iron-Grade Bar: Low-Velocity Lead Ammo."),
            new Milestone(20, "Smelt Steel-Grade Bars; forge Riveted Vest & Helmet."),
            new Milestone(25, "Steel-Grade Bar: Mid-tier Rifle, Steel Blade."),
            new Milestone(30, "Steel-Grade Bar: Standard Ballistic Ammo."),
            new Milestone(40, "Smelt Wiring/Alloy Bars; forge Hardened Alloy Plate Armor."),
            new Milestone(45, "Wiring/Alloy Bar: Alloy Rifle, Energy-Infused Blade."),
            new Milestone(50, "Wiring/Alloy Bar: High-Grain Ammo."),
            new Milestone(60, "Smelt Titanium-Alloy Bars; forge Tactical Ballistic Armor."),
            new Milestone(65, "Titanium-Alloy Bar: Precision Rifle, Vibro-Blade."),
            new Milestone(70, "Titanium-Alloy Bar: Armor-Piercing Ammo."),
            new Milestone(80, "Smelt CyberSteel Bars; forge Power-Assisted Armor."),
            new Milestone(85, "CyberSteel Bar: Energy Rifle, Myomer Blade."),
            new Milestone(90, "CyberSteel Bar: Heavy Ordnance & Experimental Ammo."),
        },

        // ── Beastmastery ─────────────────────────────────────────────────
        [Skill.Beastmastery] = new[]
        {
            new Milestone(1,  "Pick your beast at Roxy. Tier 1 beast tasks: 10-15 kills of one tier-1 creature."),
            new Milestone(20, "Tier 2 beast tasks: 12-20 kills of tougher tier-2 prey."),
            new Milestone(30, "Your beast grows into its second stage - bigger, tougher, harder-hitting."),
            new Milestone(40, "Tier 3 beast tasks: 15-25 kills of tier-3 prey."),
            new Milestone(60, "Tier 4 beast tasks: 18-30 kills. Your beast reaches its final stage."),
            new Milestone(80, "Tier 5 beast tasks: 20-35 kills of the wasteland's worst."),
            new Milestone(99, "Master Beastmaster - you and your beast have hunted it all."),
        },

        // ── Fission (third combat style) ─────────────────────────────────
        // Trained by FIGHTING with power gauntlets. Each tier of refined core is a heavier charge.
        [Skill.Fission] = new[]
        {
            new Milestone(1,  "Equip Power Gauntlets. Meltdown: 1% chance a blast deals DOUBLE the core's max hit."),
            new Milestone(10, "Meltdown 2%. (Levels buy accuracy — damage is whatever core you load.)"),
            new Milestone(20, "Meltdown 3%. Volatile Cores hit for 20."),
            new Milestone(40, "Meltdown 5%. Enriched Cores hit for 32."),
            new Milestone(60, "Meltdown 7%. Weaponized Cores hit for 46."),
            new Milestone(80, "Meltdown 9%. Singularity Cores hit for 62 - the endgame charge."),
            new Milestone(90, "Meltdown 10% - the cap. One blast in ten lands double."),
            new Milestone(99, "Max Fission - 95% to hit even a tier-5 target."),
        },

        // ── Refinement ───────────────────────────────────────────────────
        // The production half of the golem loop. Strip a corpse for raw cores (that pays Scrapping),
        // then refine them at a Furnace — refining is the only thing that trains this skill.
        [Skill.Refinement] = new[]
        {
            new Milestone(1,  "Refine Unstable Cores (Tier 1, Earth Golem)."),
            new Milestone(20, "Refine Volatile Cores (Tier 2, Light Golem)."),
            new Milestone(40, "Refine Enriched Cores (Tier 3, Fire Golem)."),
            new Milestone(60, "Refine Weaponized Cores (Tier 4, Ice Golem)."),
            new Milestone(80, "Refine Singularity Cores (Tier 5, Shadow Golem)."),
            new Milestone(99, "Max Refinement - nothing is wasted, nothing goes critical."),
        },
    };
}
