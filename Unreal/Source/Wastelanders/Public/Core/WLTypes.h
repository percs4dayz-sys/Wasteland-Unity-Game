#pragma once

#include "CoreMinimal.h"
#include "WLTypes.generated.h"

/**
 * Every skill in the game. Ported 1:1 from Assets/Scripts/Skills/Skill.cs.
 *
 * ORDER IS SERIALIZED DATA, exactly as in Unity: recipes, resource nodes and saves refer to skills
 * by this order. Renaming is free; moving or inserting is not. Append only.
 */
UENUM(BlueprintType)
enum class EWLSkill : uint8
{
	Attack,        // melee accuracy (Accurate stance)
	Strength,      // melee max hit (Aggressive stance)
	Defence,       // melee defence (Defensive stance)
	Endurance,     // hit points: runs 10-220 and IS the HP pool
	Marksmanship,  // ranged / guns
	Fishing,
	Cooking,
	Woodcutting,
	Smithing,      // smelting ore into bars AND forging bars into gear
	Scrapping,     // mining: scrap nodes, sulphur, golem corpses
	Beastmastery,  // taming and commanding mutated beasts
	Fission,       // third combat style (the "magic" slot): power gauntlets
	Refinement,    // refines raw fission cores into gauntlet ammo
	Count UMETA(Hidden)
};
ENUM_RANGE_BY_COUNT(EWLSkill, EWLSkill::Count);

/** Item category. Ported from ItemData.cs. Append only. */
UENUM(BlueprintType)
enum class EWLItemType : uint8
{
	Weapon,
	Shield,
	Helmet,
	Chest,
	Legs,
	Ammo,
	Tool,
	Resource,
	Consumable,
	Readable
};

/** Weapon / armour line. Append only (armour style serialises this). */
UENUM(BlueprintType)
enum class EWLWeaponStyle : uint8
{
	None,
	Melee,
	Ranged,
	Fission
};

/** The player's active combat style. Ported from CombatManager.cs. */
UENUM(BlueprintType)
enum class EWLCombatStyle : uint8
{
	Melee,
	Ranged,
	Fission
};

/** OSRS-style attack option. Controlled is retired but kept for value stability. */
UENUM(BlueprintType)
enum class EWLCombatStance : uint8
{
	Accurate,
	Aggressive,
	Defensive,
	Controlled UMETA(Hidden),
	Rapid,
	Longrange UMETA(DisplayName = "Distance")
};

/**
 * Equipment slots. Unity keyed these by string ("Weapon", "Shield", ...); the save format still
 * uses those strings, see UWLEquipmentComponent / FWLCharacterSave for the mapping.
 */
UENUM(BlueprintType)
enum class EWLEquipSlot : uint8
{
	Weapon,
	Shield,
	Helmet,
	Chest,
	Legs,
	Ammo,
	Tool,
	Count UMETA(Hidden)
};
ENUM_RANGE_BY_COUNT(EWLEquipSlot, EWLEquipSlot::Count);

/** Which crafting surface a recipe belongs to. */
UENUM(BlueprintType)
enum class EWLCraftingStation : uint8
{
	Furnace,
	CookingFire,
	Workbench
};

namespace WLTypes
{
	/** "Weapon", "Shield", ... exactly as the Unity Equipment.Slots strings (used by saves). */
	WASTELANDERS_API FString SlotToString(EWLEquipSlot Slot);
	WASTELANDERS_API bool SlotFromString(const FString& Str, EWLEquipSlot& OutSlot);

	/** The equipment slot an item type goes in, or false for non-equippable types. */
	WASTELANDERS_API bool SlotForItemType(EWLItemType Type, EWLEquipSlot& OutSlot);

	WASTELANDERS_API FString SkillToString(EWLSkill Skill);
	/** Parses a skill name, including every legacy Unity name (Bladework, Hearthcraft, ...). */
	WASTELANDERS_API bool SkillFromString(const FString& Str, EWLSkill& OutSkill);
}
