using System;

/// <summary>
/// Tiered gatherable resources and the food they cook into — DATA ONLY, registered into
/// ItemRegistry at startup. Tier 1 of each line already exists (Raw Shrimp 22 / Cooked Shrimp 23,
/// Wood Scrap 40); this adds tiers 2-5 from Docs/SKILL_PROGRESSION.md §1.
///
///   • FISH (Fishing) — raw catches from tiered fishing spots, cooked at the Cooking Fire.
///   • FOOD (Sustenance) — cooked meals; healAmount climbs each tier.
///   • WOOD (Woodcutting)  — structural timber; trains Woodcutting and feeds Hearthcraft building later.
///
/// The world nodes that drop the raw items (fishing spots, wood, scrap) are placed by the
/// tiered-node system (see TieredNodes / "Wasteland > Add Tier Nodes").
/// </summary>
public static class TierGatherables
{
    // raw fish ids 200-203, cooked food 210-213, raw wood 220-223
    public static void Register(Action<ItemData> add)
    {
        // ── FISH: raw catch (Fishing) ──
        add(Resource(200, "Mutated Carp",      "A bloated carp from toxic runoff. Tier 2 catch."));
        add(Resource(201, "Glowfin Bass",      "Faintly glowing bass from an irradiated river. Tier 3 catch."));
        add(Resource(202, "Deepwater Lurker",  "A toothy thing dredged from a contaminated lake. Tier 4 catch."));
        add(Resource(203, "Abyssal Specimen",  "Something that should not exist, from flooded ruins. Tier 5 catch."));

        // ── FOOD: cooked meals (Sustenance) ──
        add(Food(210, "Carp Stew",      "Mid heal, a little hearty.",        heal: 8));
        add(Food(211, "Seared Glowfin", "Strong heal — the glow is fine. Probably.", heal: 16));
        add(Food(212, "Lurker Broth",   "High heal, sticks to your ribs.",   heal: 28));
        add(Food(213, "Abyssal Feast",  "Max heal. A meal worth the nightmares.", heal: 45));

        // ── WOOD: structural timber (Woodcutting) ──
        add(Resource(220, "Gnarled Timber",        "Twisted wood from an irradiated thicket. Tier 2."));
        add(Resource(221, "Dense Wood Core",       "Heartwood from fossilized deadwood. Tier 3."));
        add(Resource(222, "Petrified Wood Shard",  "Stone-hard wood from a petrified forest. Tier 4."));
        add(Resource(223, "Ancient Heartwood",     "Prime timber from ancient hardened flora. Tier 5."));
    }

    static ItemData Resource(int id, string name, string desc)
        => new ItemData { id = id, name = name, description = desc, type = ItemType.Resource, stackable = false };

    static ItemData Food(int id, string name, string desc, int heal)
        => new ItemData { id = id, name = name, description = desc, type = ItemType.Consumable, stackable = false, healAmount = heal };
}
