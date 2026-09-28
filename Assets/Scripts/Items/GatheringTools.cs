using System;
using System.Collections.Generic;

/// <summary>Three upgrades per starter gathering tool. The best usable carried/equipped tool wins.</summary>
public static class GatheringTools
{
    public const int FirstId = 700, LastId = 708;
    static readonly string[] Metals = { "Steel", "Alloy", "Titanium" };
    static readonly string[] Names = { "Pickaxe", "Hatchet", "Fishing Rod" };
    static readonly Skill[] Skills = { Skill.Scrapping, Skill.Woodcutting, Skill.Fishing };
    static readonly string[] BaseModels = { "HeldModels/pickaxe", "HeldModels/hatchet", "HeldModels/fishing_rod/source/FishingRod" };
    static readonly float[] Speeds = { 1, 1.15f, 1.30f, 1.50f };

    public static bool IsUpgrade(int id) => id >= FirstId && id <= LastId;
    public static int BaseTool(int id) => id >= 3 && id <= 5 ? id : IsUpgrade(id) ? 3 + (id - FirstId) / 3 : 0;
    public static int Rank(int id) => IsUpgrade(id) ? (id - FirstId) % 3 + 1 : 0;
    public static int RequiredLevel(int id) => IsUpgrade(id) ? Rank(id) * 20 : 1;
    public static Skill GatheringSkill(int id) => Skills[BaseTool(id) - 3];
    public static string FallbackModel(int id) => IsUpgrade(id) ? BaseModels[BaseTool(id) - 3] : null;

    public static void Register(Action<ItemData> add)
    {
        for (int kind = 0; kind < 3; kind++)
            for (int grade = 0; grade < 3; grade++)
            {
                int id = FirstId + kind * 3 + grade;
                string name = Metals[grade] + " " + Names[kind];
                int bonus = grade == 0 ? 15 : grade == 1 ? 30 : 50;
                add(new ItemData { id = id, name = name, type = ItemType.Tool, stackable = false,
                    heldModel = "HeldModels/Tools/" + name.Replace(" ", ""),
                    description = $"Gather {bonus}% faster than the basic {Names[kind].ToLower()}. Requires {Skills[kind]} level {(grade + 1) * 20}. Works from your bag or Tool slot; the best usable tool is selected automatically.",
                    requirements = new Dictionary<Skill, int> { [Skills[kind]] = (grade + 1) * 20 } });
            }
    }

    public static int BestFor(PlayerEntity player, int requiredToolId)
    {
        if (player == null) return 0;
        int family = BaseTool(requiredToolId);
        if (family == 0) return player.Inventory.Contains(requiredToolId) || player.Equipment.GetItemId("Tool") == requiredToolId ? requiredToolId : 0;
        int best = 0;
        void Consider(int id)
        {
            if (BaseTool(id) != family || Rank(id) < Rank(requiredToolId)) return;
            if (player.Stats.GetLevel(Skills[family - 3]) < RequiredLevel(id)) return;
            if (best == 0 || Rank(id) > Rank(best)) best = id;
        }
        Consider(player.Equipment.GetItemId("Tool") ?? 0);
        for (int i = 0; i < Inventory.MAX; i++)
        {
            var stack = player.Inventory.GetSlot(i);
            if (stack != null && stack.quantity > 0) Consider(stack.itemId);
        }
        return best;
    }

    public static bool CanUse(PlayerEntity player, int requiredToolId) => requiredToolId <= 0 || BestFor(player, requiredToolId) != 0;
    public static float CycleSpeed(PlayerEntity player, int requiredToolId)
        => BaseTool(requiredToolId) == 0 ? 1 : Speeds[Rank(BestFor(player, requiredToolId))];
}
