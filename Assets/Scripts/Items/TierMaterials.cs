using System;

/// <summary>
/// Tiered refining materials — raw ores (gathered) and the bars they smelt into.
/// DATA ONLY, registered into ItemRegistry at startup. Tier 1 (Scrap Metal id 10 → Scrap Bar
/// id 11) already lives in ItemRegistry; this adds tiers 2-5 from Docs/SKILL_PROGRESSION.md §1.
///
/// Raw ores 60-63 come from higher-tier scrap nodes (Abandoned Car, Tech Dumpster, Downed
/// Military Drone, Abandoned Robotics). Those world nodes aren't placed yet, so T2-5 ores
/// aren't obtainable in-game until they are — same "data ready ahead of content" pattern as
/// TierGear. Bars 70-73 are smelted at the Furnace and feed the Workbench gear recipes.
/// </summary>
public static class TierMaterials
{
    public static void Register(Action<ItemData> add)
    {
        // ── raw ores (gathered from tiered scrap nodes; Scrap Metal id 10 is tier 1) ──
        add(Ore(60, "Salvaged Frame Chassis", "Stripped from a rusted-out car. Tier 2 scrap."));
        add(Ore(61, "Circuitry Debris",       "Copper and boards pulled from a tech dumpster. Tier 3 scrap."));
        add(Ore(62, "Heavy Plating",          "Armor panels off a downed military drone. Tier 4 scrap."));
        add(Ore(63, "Ruined Synaptic Core",   "The fried brain of a dead robot. Tier 5 scrap."));

        // ── refined bars (smelt at the Furnace; Scrap Bar id 11 is the tier-1 Iron-Grade bar) ──
        add(Bar(70, "Steel-Grade Bar",    "Tier 2 refined bar."));
        add(Bar(71, "Wiring/Alloy Bar",   "Tier 3 refined bar."));
        add(Bar(72, "Titanium-Alloy Bar", "Tier 4 refined bar."));
        add(Bar(73, "CyberSteel Bar",     "Tier 5 refined bar."));
    }

    // Resources don't stack, matching the OSRS-style rule used for the existing scrap/bars.
    static ItemData Ore(int id, string name, string desc)
        => new ItemData { id = id, name = name, description = desc, type = ItemType.Resource, stackable = false };

    static ItemData Bar(int id, string name, string desc)
        => new ItemData { id = id, name = name, description = desc, type = ItemType.Resource, stackable = false };
}
