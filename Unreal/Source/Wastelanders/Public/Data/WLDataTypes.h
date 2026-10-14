#pragma once

#include "CoreMinimal.h"
#include "Engine/DataTable.h"
#include "Core/WLTypes.h"
#include "WLDataTypes.generated.h"

class UStaticMesh;
class USkeletalMesh;
class UTexture2D;

/**
 * DataTable row structs. Property names MUST match the keys written by
 * Tools/UnityDataExport/Program.cs, because the JSON in Content/Data is imported straight into these.
 */

/** One item definition. Ported from Assets/Scripts/Items/ItemData.cs. Row name: "Item_<ItemId>". */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLItemRow : public FTableRowBase
{
	GENERATED_BODY()

	/** Stable numeric id. Saves, recipes and loot tables refer to items by this, never by row name. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Item")
	int32 ItemId = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Item")
	FText DisplayName;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Item", meta = (MultiLine = true))
	FText Description;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Item")
	EWLItemType ItemType = EWLItemType::Resource;

	/** Only ammo, sulphur and a few others stack (OSRS-style: each resource takes a slot). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Item")
	bool bStackable = false;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	EWLWeaponStyle WeaponStyle = EWLWeaponStyle::None;

	/** Armour line: picks the mesh set (Melee heavy plate, Ranged light kit). None = style-neutral. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	EWLWeaponStyle ArmourStyle = EWLWeaponStyle::None;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	int32 AttackBonus = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	int32 StrengthBonus = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	int32 DefenceBonus = 0;

	/** Reach in tiles (1 tile = 1 m in Unity = 100 uu here). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	int32 AttackRange = 0;

	/** Attack interval in game ticks (0.6 s each). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	int32 AttackSpeedTicks = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	bool bTwoHanded = false;

	/** Ranged: shots per magazine. 0 = default (8). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	int32 MagazineSize = 0;

	/** Ranged: reload seconds. 0 = default (1.5). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	float ReloadSeconds = 0.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Consumable")
	int32 HealAmount = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Consumable")
	bool bBuryable = false;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Consumable")
	int32 BeastXP = 0;

	/** Chance (0-1) that firing does NOT consume ammo. Power gauntlets only. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	float AmmoSaveChance = 0.f;

	/** Tier palette (1-6) used to recolour shared meshes. 0 = mesh's own material. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Visual")
	int32 PaletteTier = 0;

	/** Gear set id (see DT_GearSets). Empty = not part of a set. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	FName SetId;

	/** Module sockets: Weapon 2, Chest 2, Helmet/Legs/Shield 1. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Combat")
	int32 SocketCount = 0;

	/**
	 * New-art-direction assets. Unity art is deliberately NOT ported; these stay empty (code falls back
	 * to placeholder meshes/icons) until new assets are authored.
	 */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Visual")
	TSoftObjectPtr<UStaticMesh> HeldMesh;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Visual")
	TSoftObjectPtr<UStaticMesh> HeldMeshLeft;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Visual")
	TSoftObjectPtr<USkeletalMesh> ArmourMesh;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Visual")
	TSoftObjectPtr<UTexture2D> Icon;

	/** Skill level gates to equip. Combat level gates nothing. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Item")
	TMap<EWLSkill, int32> Requirements;

	bool IsWeapon() const { return ItemType == EWLItemType::Weapon; }
	bool IsArmour() const { return ItemType == EWLItemType::Shield || ItemType == EWLItemType::Helmet || ItemType == EWLItemType::Chest || ItemType == EWLItemType::Legs; }
	bool IsTool() const { return ItemType == EWLItemType::Tool; }
	bool IsConsumable() const { return ItemType == EWLItemType::Consumable; }
	bool IsAmmo() const { return ItemType == EWLItemType::Ammo; }
	bool IsReadable() const { return ItemType == EWLItemType::Readable; }
	bool IsEquippable() const { return IsWeapon() || IsArmour() || IsTool() || IsAmmo(); }
	/** Guns are always two-handed; otherwise the flag decides. */
	bool OccupiesBothHands() const { return IsWeapon() && (bTwoHanded || WeaponStyle == EWLWeaponStyle::Ranged); }
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLItemQuantity
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
	int32 ItemId = 0;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
	int32 Quantity = 1;
};

/** One crafting recipe. Ported from CraftingRecipe.cs + CraftingRecipes.cs. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLRecipeRow : public FTableRowBase
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	FText DisplayName;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	EWLCraftingStation Station = EWLCraftingStation::Workbench;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	TArray<FWLItemQuantity> Inputs;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	int32 OutputItemId = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	int32 OutputQuantity = 1;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	EWLSkill Skill = EWLSkill::Smithing;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	int32 LevelRequired = 1;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	int32 XPGranted = 0;

	/** Player flag that must be set (e.g. "roxy_fission_given"). Empty = none. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	FString RequiredFlag;

	/** Cooking: item produced on a burn (0 = never burns). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	int32 BurnItemId = 0;

	/** Cooking: at or above this level the food never burns. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Recipe")
	int32 NoBurnLevel = 0;
};

/** Skill presentation data. Row name = the EWLSkill name. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSkillRow : public FTableRowBase
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skill")
	EWLSkill Skill = EWLSkill::Attack;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skill")
	FText DisplayName;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skill")
	FText Description;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skill")
	FText LevelUpFlavour;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skill")
	int32 MinLevel = 1;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skill")
	int32 MaxLevel = 99;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skill")
	TSoftObjectPtr<UTexture2D> Icon;
};

/** Named combat module (ModuleCatalog.cs). Ids 600-669: even = Common, odd = Refined. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLModuleRow : public FTableRowBase
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Module")
	int32 ItemId = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Module")
	int32 CatalogIndex = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Module")
	FText DisplayName;

	/** Equipment slot name the module fits ("Weapon", "Helmet", ...). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Module")
	FString Slot;

	/** ModuleCatalog.Effect name (Bleed, HasteProc, ...). Mapped to a GameplayEffect by the combat code. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Module")
	FName Effect;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Module")
	int32 Tier = 1;

	/** 1 = Common, 2 = Refined. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Module")
	int32 Grade = 1;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Module", meta = (MultiLine = true))
	FText Description;
};

/** Full-set bonus (GearSets.cs). Row name = set id. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLGearSetRow : public FTableRowBase
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Set")
	FText DisplayName;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Set")
	int32 PieceCount = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Set")
	EWLWeaponStyle Style = EWLWeaponStyle::None;

	/** Multiplies max hit for the matching style (1.6 = +60%). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Set")
	float DamageMultiplier = 1.f;

	/** Added to the equipment defence bonus for the matching style. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Set")
	int32 BonusDefence = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Set", meta = (MultiLine = true))
	FText CompleteMessage;
};
