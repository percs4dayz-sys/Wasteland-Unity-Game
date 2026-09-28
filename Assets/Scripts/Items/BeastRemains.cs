using System;

/// <summary>
/// Bones &amp; remains — LEGACY. Monsters no longer drop these: Beastmastery now trains Slayer-style
/// through beast tasks (see BeastTasks). They stay registered so old saves load, and bones already in
/// a bag or bank still bury for their XP (PlayerEntity.Bury / InventorySlotUI).
/// Registered into ItemRegistry at startup (IDs 80-86). (Historical notes below.)
///
/// SEVEN kinds, escalating with the toughness of what you killed:
///   • 5 regular tiers (80-84), chosen from the creature's max HP — these line up with the five
///     EnemySpawner difficulty bands (Scrap Gremlin → Rubble Titan), so every roaming mob drops
///     the right tier with no per-enemy setup.
///   • Warden Remains (85)  — dropped by MINIBOSSES   (CombatTarget.isMiniBoss).
///   • Behemoth Remains (86) — dropped by the BIG BOSS (CombatTarget.isBoss).
/// Tougher kill = better bones = more bury XP. Same "self-healing, no tribal knowledge" pattern as
/// the rest of the game.
///
/// New icons needed: Assets/Resources/ItemIcons/80.png … 86.png. Until added, the inventory shows
/// the item NAME in the slot (icon-less fallback) and the ground drop shows a bone-coloured nub, so
/// they're fully usable immediately.
/// </summary>
public static class BeastRemains
{
    // 5 regular tiers (by enemy max HP) …
    public const int CrackedBones   = 80;   // tier 1  (HP < 25)   — Scrap Gremlin
    public const int WastelandBones = 81;   // tier 2  (HP 25-49)  — Rust Rover
    public const int HeavyBones     = 82;   // tier 3  (HP 50-99)  — Shardback
    public const int DenseBones     = 83;   // tier 4  (HP 100-159)— Chrome Stalker
    public const int MassiveBones   = 84;   // tier 5  (HP >= 160) — Rubble Titan
    // … plus the two boss kinds.
    public const int WardenRemains   = 85;  // minibosses
    public const int BehemothRemains = 86;  // the big boss

    public static void Register(Action<ItemData> add)
    {
        add(Bones(CrackedBones,   "Cracked Bones",    "Brittle bones from a small creature. Bury them to train your beast.",     xp: 12));
        add(Bones(WastelandBones, "Wasteland Bones",  "Solid bones from a wasteland predator. Bury them to train your beast.",   xp: 28));
        add(Bones(HeavyBones,     "Heavy Bones",      "Dense, heavy bones from something big. Bury them to train your beast.",   xp: 60));
        add(Bones(DenseBones,     "Dense Bones",      "Thick, marrow-rich bones from a hardened beast. Bury them to train your beast.", xp: 110));
        add(Bones(MassiveBones,   "Massive Bones",    "Enormous bones from an apex horror. Bury them to train your beast.",      xp: 180));
        add(Bones(WardenRemains,  "Warden Remains",   "The grisly remains of a wasteland warden. Burying these teaches your beast plenty.", xp: 350));
        add(Bones(BehemothRemains,"Behemoth Remains", "The colossal remains of a true monster. Burying these is a feast of training.",      xp: 800));
    }

    /// <summary>The remains a dead creature leaves, picked from its rank then its toughness.
    /// Big boss → Behemoth; miniboss → Warden; otherwise one of the five HP tiers.</summary>
    public static int ForEnemy(int maxHP, bool isMiniBoss, bool isBoss)
    {
        if (isBoss)     return BehemothRemains;
        if (isMiniBoss) return WardenRemains;
        if (maxHP >= 160) return MassiveBones;
        if (maxHP >= 100) return DenseBones;
        if (maxHP >= 50)  return HeavyBones;
        if (maxHP >= 25)  return WastelandBones;
        return CrackedBones;
    }

    // Non-stackable, matching the game's OSRS-style rule that gathered resources each take a slot.
    static ItemData Bones(int id, string name, string desc, int xp)
        => new ItemData { id = id, name = name, description = desc, type = ItemType.Resource,
                          stackable = false, buryable = true, beastXP = xp };
}
