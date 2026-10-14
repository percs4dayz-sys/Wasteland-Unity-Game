#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"
#include "Core/WLTypes.h"
#include "WLLootSettings.generated.h"

class UStaticMesh;

/** A list of item ids (UPROPERTY-friendly array of arrays). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLLootIdList
{
	GENERATED_BODY()

	FWLLootIdList() = default;
	FWLLootIdList(std::initializer_list<int32> InIds) : ItemIds(InIds) {}

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Loot")
	TArray<int32> ItemIds;
};

/**
 * A cosmetic weapon finish (WeaponSkins.Skin). Stats never change; it only alters how the held
 * weapon renders. Unity skins were tint-only, so the tint is kept as data; the mesh slot is for
 * new-art-direction skin models and stays empty until those exist.
 */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLWeaponSkinDef
{
	GENERATED_BODY()

	/** Stable id, used in save flags ("skin_<id>"). Never rename. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skin")
	FName Id;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skin")
	FText DisplayName;

	/** Melee or Ranged. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skin")
	EWLWeaponStyle Category = EWLWeaponStyle::Melee;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skin")
	FLinearColor Tint = FLinearColor::White;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skin")
	FText Rarity;

	/** Optional replacement held mesh (new art only). Empty = tint the weapon's own mesh. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Skin")
	TSoftObjectPtr<UStaticMesh> HeldMesh;
};

/**
 * Loot balance tables (Project Settings > Game > Wastelanders Loot), with the Unity numbers from
 * Assets/Scripts/Combat/EnemyLoot.cs and WeaponSkins.cs as defaults. Per-tier arrays hold tiers
 * 1-5 at indices 0-4. "Chance" ints are "1 in N" denominators, like Unity.
 *
 * The retired Perfect module quality (ModulePerfect table) is intentionally not ported: Unity no
 * longer rolls it.
 */
UCLASS(Config = Game, DefaultConfig, meta = (DisplayName = "Wastelanders Loot"))
class WASTELANDERS_API UWLLootSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	UWLLootSettings();

	virtual FName GetCategoryName() const override { return TEXT("Game"); }

	static const UWLLootSettings* Get() { return GetDefault<UWLLootSettings>(); }

	/** Clamp a tier to 1-5 and return the array index. */
	static int32 TierIndex(int32 Tier) { return FMath::Clamp(Tier, 1, 5) - 1; }

	/** Safe per-tier read (0 when the array was shortened in config). */
	static int32 AtTier(const TArray<int32>& Table, int32 Tier) { const int32 I = TierIndex(Tier); return Table.IsValidIndex(I) ? Table[I] : 0; }

	// --- module roll ---
	UPROPERTY(Config, EditAnywhere, Category = "Modules") TArray<int32> ModuleCommonChance;
	UPROPERTY(Config, EditAnywhere, Category = "Modules") TArray<int32> ModuleRefinedChance;

	// --- ore ---
	UPROPERTY(Config, EditAnywhere, Category = "Ore") TArray<int32> OreChance;
	UPROPERTY(Config, EditAnywhere, Category = "Ore") TArray<int32> OreItemId;
	UPROPERTY(Config, EditAnywhere, Category = "Ore") int32 OreMin = 1;
	UPROPERTY(Config, EditAnywhere, Category = "Ore") int32 OreMax = 3;
	/** Salvage module: extra chance per power point when the ore roll (or a salvage-flagged roll) fails. */
	UPROPERTY(Config, EditAnywhere, Category = "Ore") float SalvageChancePerPower = 0.10f;

	// --- ammo (one tier below the enemy; T1 drops Scrap Rounds) ---
	UPROPERTY(Config, EditAnywhere, Category = "Ammo") int32 AmmoChance = 3;
	UPROPERTY(Config, EditAnywhere, Category = "Ammo") TArray<int32> AmmoItemId;
	UPROPERTY(Config, EditAnywhere, Category = "Ammo") TArray<int32> AmmoMin;
	UPROPERTY(Config, EditAnywhere, Category = "Ammo") TArray<int32> AmmoMax;

	// --- gear ---
	UPROPERTY(Config, EditAnywhere, Category = "Gear") TArray<int32> GearChanceFromMob;
	UPROPERTY(Config, EditAnywhere, Category = "Gear") TArray<int32> GearChanceFromMiniBoss;
	UPROPERTY(Config, EditAnywhere, Category = "Gear") TArray<FWLLootIdList> GearMelee;
	UPROPERTY(Config, EditAnywhere, Category = "Gear") TArray<FWLLootIdList> GearRanged;

	// --- God-Hunter ---
	UPROPERTY(Config, EditAnywhere, Category = "God-Hunter") int32 GodModuleChance = 3;
	/** TierModules.IdFor(6, Refined) = 516. */
	UPROPERTY(Config, EditAnywhere, Category = "God-Hunter") int32 GodModuleItemId = 516;
	UPROPERTY(Config, EditAnywhere, Category = "God-Hunter") int32 GodOreItemId = 63;
	UPROPERTY(Config, EditAnywhere, Category = "God-Hunter") int32 GodOreMin = 10;
	UPROPERTY(Config, EditAnywhere, Category = "God-Hunter") int32 GodOreMax = 25;
	UPROPERTY(Config, EditAnywhere, Category = "God-Hunter") int32 GodAmmoItemId = 119;
	UPROPERTY(Config, EditAnywhere, Category = "God-Hunter") int32 GodAmmoMin = 50;
	UPROPERTY(Config, EditAnywhere, Category = "God-Hunter") int32 GodAmmoMax = 100;
	UPROPERTY(Config, EditAnywhere, Category = "God-Hunter") int32 GodGearChance = 50;
	/** Melee weapon, melee armour, ranged weapon, ranged armour: one pool picked evenly, then one item. */
	UPROPERTY(Config, EditAnywhere, Category = "God-Hunter") TArray<FWLLootIdList> GodGearPools;

	// --- cosmetics (WeaponSkins.RollDrop) ---
	UPROPERTY(Config, EditAnywhere, Category = "Cosmetics") float SkinChanceNormal = 0.02f;
	UPROPERTY(Config, EditAnywhere, Category = "Cosmetics") float SkinChanceMiniBoss = 0.10f;
	UPROPERTY(Config, EditAnywhere, Category = "Cosmetics") float SkinChanceBoss = 0.25f;
	UPROPERTY(Config, EditAnywhere, Category = "Cosmetics") TArray<FWLWeaponSkinDef> WeaponSkins;
};
