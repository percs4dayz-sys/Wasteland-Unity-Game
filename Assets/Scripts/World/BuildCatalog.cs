using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a placed deployable actually becomes once built.
/// </summary>
public enum BuildKind
{
    CookingFire,   // a deployable cook surface (CraftingStation/CookingFire)
    Furnace,       // a deployable smelter      (CraftingStation/Furnace)
    Workbench,     // a deployable gear bench   (CraftingStation/Workbench)
    BankChest,     // a deployable bank         (BankingCrate)
    AFKStation,    // passive resource / XP generator (AFKStation)
    Turret,        // auto-attacks nearby enemies (Turret)
    HousingPlot    // designates your housing plot (sets a flag; marker only)
}

/// <summary>One buildable in the Hearthcraft construction menu.</summary>
public class Buildable
{
    public string name;
    public string description;
    public int hearthLevel = 1;                    // Hearthcraft level required to build it
    public int xp;                                 // Hearthcraft XP granted on placement
    public List<CraftingIngredient> cost = new();
    public BuildKind kind;

    // AFKStation params (ignored by other kinds).
    public Skill afkSkill;
    public int   afkProduceItemId;  // resource-mode output (0 = none)
    public int   afkConsumeItemId;  // XP-mode input        (0 = none)

    // Turret params (ignored by other kinds).
    public int   turretMaxHit;
    public float turretRange;

    // Placeholder visual.
    public Vector3 size = new(1.2f, 1.2f, 1.2f);
    public Color   color = new(0.5f, 0.5f, 0.55f);
}

/// <summary>
/// The Hearthcraft construction catalog — everything the player can deploy, gated on Hearthcraft
/// level, drawn from Docs/SKILL_PROGRESSION.md (Hearthcraft milestones + AFK station list).
///
/// Deployed crafting stations reuse the existing CraftingStation/BankingCrate components and recipe
/// lists (CraftingRecipes); AFK stations and turrets use the new AFKStation / Turret behaviours.
/// Item ids: wood 40/220-223, bars 11/70-73, raw scrap 10, raw fish 22 (see ItemRegistry/Tier*).
/// </summary>
public static class BuildCatalog
{
    public static List<Buildable> All() => _all;

    static readonly List<Buildable> _all = new()
    {
        // ── L1: the basics ──
        B("Campfire", "A cook surface. Roast raw catches here.", 1, 20, BuildKind.CookingFire,
          new(0.5f, 0.28f, 0.12f), cost: (40, 3)),
        B("Foundation Shelter", "Designates this spot as your housing plot.", 1, 40, BuildKind.HousingPlot,
          new(0.45f, 0.4f, 0.32f), size: new(2.4f, 0.4f, 2.4f), cost: (40, 5), cost2: (11, 3)),

        // ── AFK gathering stations (passive resource OR XP) ──
        AFK("Scrap Sorting Bench", "Sorts scrap on its own, or burns it for Scrapping XP.", 20, 60,
            Skill.Scrapping, produce: 10, consume: 10, new(0.5f, 0.5f, 0.52f), cost: (11, 5)),
        AFK("Chopping Frame", "Strips timber on its own, or processes it for Woodcutting XP.", 30, 80,
            Skill.Woodcutting, produce: 40, consume: 40, new(0.42f, 0.3f, 0.16f), cost: (11, 5), cost2: (40, 3)),
        AFK("Indoor Fishery Tank", "Farms fish on its own, or tends them for Fishing XP.", 35, 90,
            Skill.Fishing, produce: 22, consume: 22, new(0.2f, 0.42f, 0.5f), cost: (11, 5), cost2: (40, 3)),
        AFK("Forge Station", "Smelts scrap into bars, or refines them for Tinkering XP.", 50, 140,
            Skill.Smithing, produce: 11, consume: 10, new(0.4f, 0.28f, 0.22f), cost: (70, 8)),
        AFK("Burn Barrel", "Burns wood for steady Hearthcraft XP.", 60, 160,
            Skill.Smithing, produce: 0, consume: 40, new(0.35f, 0.2f, 0.14f), cost: (70, 6)),

        // ── deployable crafting surfaces ──
        B("Field Kitchen", "A sturdy deployable cook surface.", 40, 120, BuildKind.CookingFire,
          new(0.5f, 0.3f, 0.16f), cost: (70, 6)),
        B("Portable Workbench", "Build gear, weapons and ammo in the field.", 60, 180, BuildKind.Workbench,
          new(0.3f, 0.3f, 0.34f), size: new(1.6f, 1.1f, 0.9f), cost: (70, 8)),
        B("Portable Furnace", "A deployable smelter.", 60, 180, BuildKind.Furnace,
          new(0.35f, 0.22f, 0.18f), size: new(1.4f, 1.6f, 1.4f), cost: (70, 8)),

        // ── defenses ──
        Turret("Basic Turret", "Auto-fires on hostiles that wander close.", 60, 200,
               maxHit: 3, range: 8f, new(0.3f, 0.34f, 0.4f), cost: (70, 10)),
        Turret("Advanced Turret", "A heavier auto-turret with longer reach.", 80, 320,
               maxHit: 8, range: 11f, new(0.28f, 0.3f, 0.45f), cost: (72, 12)),

        // ── endgame utility ──
        B("Deployable Bank Chest", "Stash and withdraw your gear anywhere.", 80, 300, BuildKind.BankChest,
          new(0.3f, 0.34f, 0.2f), cost: (72, 10)),
    };

    // ── builders ──
    static Buildable B(string name, string desc, int lvl, int xp, BuildKind kind, Color color,
                       Vector3 size = default, (int id, int qty) cost = default, (int id, int qty) cost2 = default)
    {
        var b = new Buildable { name = name, description = desc, hearthLevel = lvl, xp = xp, kind = kind, color = color };
        if (size != default) b.size = size;
        AddCost(b, cost, cost2);
        return b;
    }

    static Buildable AFK(string name, string desc, int lvl, int xp, Skill skill, int produce, int consume,
                         Color color, (int id, int qty) cost = default, (int id, int qty) cost2 = default)
    {
        var b = B(name, desc, lvl, xp, BuildKind.AFKStation, color, default, cost, cost2);
        b.afkSkill = skill; b.afkProduceItemId = produce; b.afkConsumeItemId = consume;
        return b;
    }

    static Buildable Turret(string name, string desc, int lvl, int xp, int maxHit, float range,
                            Color color, (int id, int qty) cost = default, (int id, int qty) cost2 = default)
    {
        var b = B(name, desc, lvl, xp, BuildKind.Turret, color, new Vector3(0.8f, 1.6f, 0.8f), cost, cost2);
        b.turretMaxHit = maxHit; b.turretRange = range;
        return b;
    }

    static void AddCost(Buildable b, (int id, int qty) cost, (int id, int qty) cost2)
    {
        if (cost.qty  > 0) b.cost.Add(new CraftingIngredient { itemId = cost.id,  quantity = cost.qty });
        if (cost2.qty > 0) b.cost.Add(new CraftingIngredient { itemId = cost2.id, quantity = cost2.qty });
    }
}
