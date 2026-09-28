using System.Collections.Generic;

/// <summary>
/// Single source of truth for crafting-station recipe lists. Both World3DBuilder and
/// TutorialIsland3DBuilder bake these into their CraftingStation components (and the
/// "Wasteland > Refresh Crafting Recipes" tool re-pushes them into existing scenes), so
/// every station stays in sync from one place.
///
/// Recipes gate on a skill LEVEL; the CraftingUI shows locked entries until the player
/// qualifies. Levels, materials and unlock order follow Docs/SKILL_PROGRESSION.md §2
/// (Gunsmithing &amp; Tinkering: bars → gear, Armor → Weapons → Ammo within each bar tier).
///
/// Item ids — bars: 11 Iron-Grade, 70 Steel, 71 Wiring/Alloy, 72 Titanium, 73 CyberSteel.
/// Raw ores: 10 Scrap Metal, 60-63 tiered (see TierMaterials). Gear: 2/12/13/30-32 starter +
/// 100-135 tiered (see TierGear). Sulphur 41 is the universal ammo propellant.
///
/// WOOD is deliberately NOT fuel — no smelting or cooking recipe takes a log. Timber
/// (40, 220-223 tiered) is consumed by exactly two things: one log per 5-round ammo batch at the
/// matching tier, and melee-weapon handles.
/// </summary>
public static class CraftingRecipes
{
    // ── Furnace: raw ore → refined bar (trains Hearthcraft) ──────────────────
    public static List<CraftingRecipe> Furnace() => new()
    {
        // No wood in ANY smelt recipe — logs are not fuel. Woodcutting's sinks are ammo pressing
        // (1 tier log per 5 rounds, see Workbench) and melee-weapon handles, nothing else.
        Recipe("Smelt Scrap Bar",         11, 1, Skill.Smithing,  1,  30, (10, 1)),
        Recipe("Smelt Steel-Grade Bar",   70, 1, Skill.Smithing, 20,  60, (60, 2)),
        Recipe("Smelt Wiring/Alloy Bar",  71, 1, Skill.Smithing, 40,  90, (61, 2)),
        Recipe("Smelt Titanium-Alloy Bar",72, 1, Skill.Smithing, 60, 130, (62, 2)),
        Recipe("Smelt CyberSteel Bar",    73, 1, Skill.Smithing, 80, 180, (63, 2)),

        // ── Refinement: raw reactor core → refined gauntlet ammunition ──
        // Trains REFINEMENT, its own production skill — not Hearthcraft (the furnace is just the
        // surface it happens on, the same way the cooking fire hosts Sustenance) and not Fission
        // (that is the combat style, trained by firing the gauntlets). Raw cores come off Elemental
        // Golem corpses, where stripping them pays Scrapping. Gates match the golem tiers.
        Refine(1, 1,   100),
        Refine(2, 20,  220),
        Refine(3, 40,  380),
        Refine(4, 60,  600),
        Refine(5, 80,  900),
    };

    /// <summary>One tier of core refining: 1 raw core → 1 refined core, trains Refinement.</summary>
    static CraftingRecipe Refine(int tier, int lvl, int xp)
    {
        string name = ItemRegistry.Get(FissionItems.RefinedCore(tier))?.name ?? $"Tier {tier} Core";
        return Recipe($"Refine {name}", FissionItems.RefinedCore(tier), 1,
                      Skill.Refinement, lvl, xp, (FissionItems.RawCore(tier), 1));
    }

    // Generic ruined-cooking output (ItemRegistry id 24). Heals nothing, grants no XP.
    const int BURNT_FOOD = 24;

    /// <summary>
    /// Propellant for every grade of ammunition. Ammo is the one recipe line that spans all three
    /// material skills: 5 Sulphur (Scrapping) + 1 bar of the tier (Hearthcraft) + 1 log of the tier
    /// (Woodcutting) → 5 rounds. Sulphur is universal — only the bar and the log climb with tier.
    /// This is what gives the tiered lumber (220-223) a consumer; before it, nothing used it.
    /// </summary>
    static readonly (int id, int qty) SULPHUR = (41, 5);

    // ── Cooking Fire: raw catch → cooked meal (trains Sustenance) ────────────
    // The final number is the "no-burn" level: at/above it the food never burns; below it
    // the burn chance climbs as you approach the recipe's required level (see CraftingManager).
    public static List<CraftingRecipe> Cooking() => new()
    {
        Cook("Cook Raw Shrimp",     23, Skill.Cooking,  1,  15, 34, (22, 1)),
        Cook("Cook Carp Stew",     210, Skill.Cooking, 20,  40, 55, (200, 1)),
        Cook("Cook Seared Glowfin",211, Skill.Cooking, 40,  70, 75, (201, 1)),
        Cook("Cook Lurker Broth",  212, Skill.Cooking, 60, 110, 92, (202, 1)),
        Cook("Cook Abyssal Feast", 213, Skill.Cooking, 80, 160, 99, (203, 1)),
    };

    /// <summary>Cooking recipe (always outputs 1) that can burn into Burnt Food below noBurnLevel.</summary>
    static CraftingRecipe Cook(string name, int outId, Skill skill, int lvl, int xp, int noBurnLevel,
                               params (int id, int qty)[] inputs)
    {
        var r = Recipe(name, outId, 1, skill, lvl, xp, inputs);
        r.burnItemId = BURNT_FOOD;
        r.noBurnLevel = noBurnLevel;
        return r;
    }

    // ── Workbench: bars (+ handles) → weapons, armor & ammo (trains Tinkering) ─
    public static List<CraftingRecipe> Workbench() => CompleteWorkbench(new List<CraftingRecipe>()
    {
        // ── Tier 1 · Iron-Grade (Scrap Bar 11) ──
        Recipe("Craft Scrap Helmet",     30, 1, Skill.Smithing,  1,  40, (11, 2)),
        Recipe("Craft Scrap Chestplate", 31, 1, Skill.Smithing,  1,  60, (11, 3)),
        Recipe("Craft Scrap Leggings",   32, 1, Skill.Smithing,  1,  40, (11, 2)),
        Recipe("Craft Scrap Shield",      2, 1, Skill.Smithing,  1,  40, (11, 2)),
        Recipe("Craft Pipe Melee",      100, 1, Skill.Smithing,  1,  50, (11, 2), (40, 1)),
        Recipe("Craft Pipe Pistol",      12, 1, Skill.Smithing,  1,  75, (11, 3)),
        Recipe("Press Scrap Rounds",     13, 5, Skill.Smithing,  1,  15, SULPHUR, (11, 1), (40, 1)),
        Recipe("Craft Scrap Blade",     101, 1, Skill.Smithing,  5,  60, (11, 2), (40, 1)),
        Recipe("Press Low-Velocity Ammo",115, 5, Skill.Smithing, 10,  20, SULPHUR, (11, 1), (40, 1)),

        // ── Tier 2 · Steel-Grade Bar (70) ──
        Recipe("Craft Riveted Helmet",  120, 1, Skill.Smithing, 20,  90, (70, 2)),
        Recipe("Craft Riveted Vest",    121, 1, Skill.Smithing, 20, 140, (70, 4)),
        Recipe("Craft Riveted Greaves", 122, 1, Skill.Smithing, 20,  90, (70, 2)),
        Recipe("Craft Riveted Buckler", 123, 1, Skill.Smithing, 20, 110, (70, 3)),
        Recipe("Craft Mid-tier Rifle",  110, 1, Skill.Smithing, 25, 150, (70, 4)),
        Recipe("Craft Steel Blade",     102, 1, Skill.Smithing, 25, 130, (70, 3), (220, 1)),
        Recipe("Press Ballistic Ammo",  116, 5, Skill.Smithing, 30,  35, SULPHUR, (70, 1), (220, 1)),

        // ── Tier 3 · Wiring/Alloy Bar (71) ──
        Recipe("Craft Hardened Alloy Helm",    124, 1, Skill.Smithing, 40, 160, (71, 2)),
        Recipe("Craft Hardened Alloy Plate",   125, 1, Skill.Smithing, 40, 240, (71, 4)),
        Recipe("Craft Hardened Alloy Greaves", 126, 1, Skill.Smithing, 40, 160, (71, 2)),
        Recipe("Craft Hardened Alloy Shield",  127, 1, Skill.Smithing, 40, 200, (71, 3)),
        Recipe("Craft Alloy Rifle",            111, 1, Skill.Smithing, 45, 260, (71, 4)),
        Recipe("Craft Energy-Infused Blade",   103, 1, Skill.Smithing, 45, 230, (71, 3), (221, 1)),
        Recipe("Press High-Grain Ammo",        117, 5, Skill.Smithing, 50,  55, SULPHUR, (71, 1), (221, 1)),

        // ── Tier 4 · Titanium-Alloy Bar (72) ──
        Recipe("Craft Tactical Ballistic Helm",    128, 1, Skill.Smithing, 60, 260, (72, 2)),
        Recipe("Craft Tactical Ballistic Armor",   129, 1, Skill.Smithing, 60, 360, (72, 4)),
        Recipe("Craft Tactical Ballistic Greaves", 130, 1, Skill.Smithing, 60, 260, (72, 2)),
        Recipe("Craft Tactical Ballistic Shield",  131, 1, Skill.Smithing, 60, 300, (72, 3)),
        Recipe("Craft Precision Rifle",            112, 1, Skill.Smithing, 65, 400, (72, 4)),
        Recipe("Craft Vibro-Blade",                104, 1, Skill.Smithing, 65, 360, (72, 3), (222, 1)),
        Recipe("Press Armor-Piercing Ammo",        118, 5, Skill.Smithing, 70,  80, SULPHUR, (72, 1), (222, 1)),

        // ── Tier 5 · CyberSteel Bar (73) ──
        Recipe("Craft Power-Assisted Helm",    132, 1, Skill.Smithing, 80, 380, (73, 2)),
        Recipe("Craft Power-Assisted Armor",   133, 1, Skill.Smithing, 80, 520, (73, 4)),
        Recipe("Craft Power-Assisted Greaves", 134, 1, Skill.Smithing, 80, 380, (73, 2)),
        Recipe("Craft Power-Assisted Shield",  135, 1, Skill.Smithing, 80, 440, (73, 3)),
        Recipe("Craft Experimental Energy Rifle",113,1, Skill.Smithing, 85, 600, (73, 4)),
        Recipe("Craft Myomer Blade",           105, 1, Skill.Smithing, 85, 540, (73, 3), (223, 1)),
        Recipe("Press Experimental Ammo",      119, 5, Skill.Smithing, 90, 110, SULPHUR, (73, 1), (223, 1)),
    });


    static List<CraftingRecipe> CompleteWorkbench(List<CraftingRecipe> recipes)
    {
        int[] bars={11,70,71,72,73};
        int[] wood={40,220,221,222,223};
        for(int tier=2;tier<=5;tier++)
            for(int part=0;part<3;part++)
            {
                int id=144+(tier-2)*3+part;
                recipes.Add(Recipe("Craft "+ItemRegistry.Get(id).name,id,1,Skill.Smithing,(tier-1)*20,
                    tier*(part==1?70:45),(bars[tier-1],part==1?4:2)));
            }
        foreach(int id in new[]{1,3,4,5,136,137,138,139})
        {
            var item=ItemRegistry.Get(id);
            var recipe=Recipe("Craft "+item.name,id,1,Skill.Smithing,1,40,(11,item.type==ItemType.Chest?3:2));
            if(item.IsTool || item.IsWeapon)recipe.inputs.Add(new CraftingIngredient{itemId=40,quantity=1});
            recipes.Add(recipe);
        }
        foreach(int id in new[]{400,402})
        {
            var recipe=Recipe("Replace "+ItemRegistry.Get(id).name,id,1,Skill.Smithing,1,50,(11,id==400?3:5));
            recipe.requiredFlag=RoxyNPC.FissionGivenFlag;
            recipes.Add(recipe);
        }
        // The higher reactor gauntlets: bars of their tier plus two refined cores, once Roxy has taught Fission.
        foreach (var (gid, tier, lvl) in new[] { (FissionItems.NeonGauntlets, 2, 25), (FissionItems.SurgeGauntlets, 3, 50), (FissionItems.InfernoGauntlets, 4, 75) })
        {
            var recipe = Recipe("Craft " + ItemRegistry.Get(gid).name, gid, 1, Skill.Smithing, lvl, 120 * tier,
                (bars[tier - 1], 4), (FissionItems.RefinedCore(tier), 2));
            recipe.requiredFlag = RoxyNPC.FissionGivenFlag;
            recipes.Add(recipe);
        }
        for (int id = GatheringTools.FirstId; id <= GatheringTools.LastId; id++)
        {
            int rank = GatheringTools.Rank(id);
            recipes.Add(Recipe("Craft " + ItemRegistry.Get(id).name, id, 1, Skill.Smithing, rank * 20,
                rank * 90, (bars[rank], 3), (wood[rank], 1)));
        }
        foreach(var d in ModuleCatalog.All)
        {
            int common=600+d.index*2;
            recipes.Add(Recipe("Refine "+d.name,common+1,1,Skill.Smithing,System.Math.Max(1,(d.Tier-1)*20),
                50*d.Tier,(common,3),(bars[d.Tier-1],2)));
        }
        // Named weapon/shield models already registered by the game also need an acquisition path.
        ItemRegistry.AutoRegisterModelItems();
        foreach(var item in ItemRegistry.All)
        {
            if(item.id<4000 || (!item.IsWeapon && item.type!=ItemType.Shield))continue;
            int level=1;
            foreach(var gate in item.requirements)level=System.Math.Max(level,gate.Value);
            int tier=UnityEngine.Mathf.Clamp(1+level/20,1,5);
            var recipe=Recipe("Craft "+item.name,item.id,1,Skill.Smithing,level,60*tier,
                (bars[tier-1],item.twoHanded?4:3));
            if(item.IsWeapon && item.weaponStyle==WeaponStyle.Melee)
                recipe.inputs.Add(new CraftingIngredient{itemId=wood[tier-1],quantity=1});
            recipes.Add(recipe);
        }
        recipes.Sort((a,b)=>a.levelRequired!=b.levelRequired?a.levelRequired.CompareTo(b.levelRequired):string.CompareOrdinal(a.name,b.name));
        return recipes;
    }

    static CraftingRecipe Recipe(string name, int outId, int outQty, Skill skill, int lvl, int xp,
                                 params (int id, int qty)[] inputs)
    {
        var r = new CraftingRecipe
        {
            name = name, outputItemId = outId, outputQty = outQty,
            skill = skill, levelRequired = lvl, xpGranted = xp
        };
        foreach (var (id, qty) in inputs)
            r.inputs.Add(new CraftingIngredient { itemId = id, quantity = qty });
        return r;
    }
}
