using System;

/// <summary>
/// Items handed out by the small settlement quests (see SideQuests). DATA ONLY, registered into
/// ItemRegistry at startup. Ids 750-759 — 700-708 are gathering tools, 600-669 modules.
/// </summary>
public static class SideQuestItems
{
    public const int CheeseCrackers = 750;   // The Sacred Vending Machine
    public const int TreasureMap    = 751;   // The Treasure Map
    public const int ToiletPaper    = 752;   // The Treasure Map — the legendary pre-war treasure
    public const int RabbitFoot     = 753;   // Pest Control

    public static void Register(Action<ItemData> add)
    {
        add(new ItemData { id = CheeseCrackers, name = "Sacred Cheese Crackers",
            description = "Two hundred years old and still, somehow, orange. Humanity's holiest snack.",
            type = ItemType.Consumable, stackable = false, healAmount = 3 });
        add(new ItemData { id = TreasureMap, name = "Scavenger's Treasure Map",
            description = "Stained, folded and clutched by a dying man. Three locations are circled in shaky pen.",
            type = ItemType.Resource, stackable = false });
        add(new ItemData { id = ToiletPaper, name = "Pre-War Toilet Paper",
            description = "Sealed, dry and two-ply. In the wasteland, this is genuinely priceless.",
            type = ItemType.Resource, stackable = false });
        add(new ItemData { id = RabbitFoot, name = "Enormous Rabbit's Foot",
            description = "The size of a dinner plate. Lucky for the farmer. Less so for the rabbit.",
            type = ItemType.Resource, stackable = false });
    }
}
