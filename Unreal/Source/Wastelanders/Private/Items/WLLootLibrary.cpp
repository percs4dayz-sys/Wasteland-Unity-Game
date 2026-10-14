#include "Items/WLLootLibrary.h"
#include "Items/WLLootSettings.h"
#include "Items/WLDropTableAsset.h"
#include "Items/WLGroundItem.h"
#include "Items/WLEquipmentComponent.h"
#include "Items/WLItemRules.h"
#include "Items/WLWeaponSkins.h"
#include "Data/WLDataSubsystem.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "GameFramework/Actor.h"
#include "Engine/World.h"
#include "Engine/GameInstance.h"

#define LOCTEXT_NAMESPACE "WLLoot"

int32 FWLEnemyLootParams::GetLootTier() const
{
	if (Tier > 0)
	{
		return FMath::Clamp(Tier, 1, 5);
	}
	// CombatTarget.LootTier: the enemy blueprint's mob HP bands.
	return MaxHP >= 450 ? 5
		: MaxHP >= 280 ? 4
		: MaxHP >= 160 ? 3
		: MaxHP >= 90 ? 2
		: 1;
}

// --- pure rolls --------------------------------------------------------------------------------

namespace
{
	void Loot_AddDrop(TArray<FWLItemQuantity>& Out, int32 ItemId, int32 Quantity)
	{
		if (ItemId > 0 && Quantity > 0)
		{
			FWLItemQuantity& Q = Out.AddDefaulted_GetRef();
			Q.ItemId = ItemId;
			Q.Quantity = Quantity;
		}
	}

	const TArray<int32>* Loot_PoolAt(const TArray<FWLLootIdList>& Pools, int32 Tier)
	{
		const int32 I = UWLLootSettings::TierIndex(Tier);
		return Pools.IsValidIndex(I) ? &Pools[I].ItemIds : nullptr;
	}

	int32 Loot_PickFrom(const TArray<int32>& Pool, FRandomStream& Rng)
	{
		return Pool.Num() > 0 ? Pool[Rng.RandRange(0, Pool.Num() - 1)] : 0;
	}

	FWLDropRoll& Loot_AddRoll(TArray<FWLDropRoll>& Rolls, const TCHAR* Label, float Chance, bool bSalvage = false)
	{
		FWLDropRoll& Roll = Rolls.AddDefaulted_GetRef();
		Roll.Label = Label;
		Roll.Chance = Chance;
		Roll.bSalvageBonus = bSalvage;
		return Roll;
	}

	void Loot_AddOutcome(FWLDropRoll& Roll, int32 ItemId, int32 Min = 1, int32 Max = 1, float Weight = 1.f)
	{
		FWLDropOutcome& O = Roll.Outcomes.AddDefaulted_GetRef();
		O.ItemId = ItemId;
		O.MinQuantity = Min;
		O.MaxQuantity = Max;
		O.Weight = Weight;
	}

	float Loot_Inverse(int32 N) { return N > 0 ? 1.f / N : 0.f; }
}

int32 FWLLootRules::RollModule(const UWLLootSettings& S, int32 Tier, FRandomStream& Rng)
{
	// Perfect is retired: Refined is the top quality, checked first so a lucky roll isn't masked.
	if (OneIn(UWLLootSettings::AtTier(S.ModuleRefinedChance, Tier), Rng))
	{
		return FWLModuleRules::CatalogIdFor(Tier, Rng.RandRange(0, 6), true);
	}
	if (OneIn(UWLLootSettings::AtTier(S.ModuleCommonChance, Tier), Rng))
	{
		return FWLModuleRules::CatalogIdFor(Tier, Rng.RandRange(0, 6), false);
	}
	return 0;
}

int32 FWLLootRules::RollGear(const UWLLootSettings& S, int32 Tier, FRandomStream& Rng)
{
	const TArray<int32>* Pool = Rng.FRand() < 0.5f ? Loot_PoolAt(S.GearMelee, Tier) : Loot_PoolAt(S.GearRanged, Tier);
	return Pool ? Loot_PickFrom(*Pool, Rng) : 0;
}

void FWLLootRules::RollTierLoot(const UWLLootSettings& S, int32 InTier, bool bMiniOrBoss, int32 SalvagePower, FRandomStream& Rng, TArray<FWLItemQuantity>& Out)
{
	const int32 Tier = FMath::Clamp(InTier, 1, 5);

	// 1. module roll: one roll, best quality wins.
	Loot_AddDrop(Out, RollModule(S, Tier, Rng), 1);

	// 2. main loot: ore, ammo and a slim gear chance, each independent.
	//    The Salvage module adds a second chance at the ore (10% per power point).
	if (OneIn(UWLLootSettings::AtTier(S.OreChance, Tier), Rng) || Rng.FRand() < S.SalvageChancePerPower * SalvagePower)
	{
		Loot_AddDrop(Out, UWLLootSettings::AtTier(S.OreItemId, Tier), Rng.RandRange(S.OreMin, FMath::Max(S.OreMin, S.OreMax)));
	}

	if (OneIn(S.AmmoChance, Rng))
	{
		const int32 Min = UWLLootSettings::AtTier(S.AmmoMin, Tier);
		Loot_AddDrop(Out, UWLLootSettings::AtTier(S.AmmoItemId, Tier), Rng.RandRange(Min, FMath::Max(Min, UWLLootSettings::AtTier(S.AmmoMax, Tier))));
	}

	const int32 GearChance = UWLLootSettings::AtTier(bMiniOrBoss ? S.GearChanceFromMiniBoss : S.GearChanceFromMob, Tier);
	if (OneIn(GearChance, Rng))
	{
		Loot_AddDrop(Out, RollGear(S, Tier, Rng), 1);
	}
}

int32 FWLLootRules::RollGodHunter(const UWLLootSettings& S, FRandomStream& Rng, TArray<FWLItemQuantity>& Out)
{
	// Never an empty drop: both resource piles are guaranteed; God gear is the 1-in-50 chase.
	if (OneIn(S.GodModuleChance, Rng))
	{
		Loot_AddDrop(Out, S.GodModuleItemId, 1);
	}
	Loot_AddDrop(Out, S.GodOreItemId, Rng.RandRange(S.GodOreMin, FMath::Max(S.GodOreMin, S.GodOreMax)));
	Loot_AddDrop(Out, S.GodAmmoItemId, Rng.RandRange(S.GodAmmoMin, FMath::Max(S.GodAmmoMin, S.GodAmmoMax)));

	if (!OneIn(S.GodGearChance, Rng) || S.GodGearPools.Num() == 0)
	{
		return 0;
	}
	const TArray<int32>& Pool = S.GodGearPools[Rng.RandRange(0, S.GodGearPools.Num() - 1)].ItemIds;
	const int32 Id = Loot_PickFrom(Pool, Rng);
	Loot_AddDrop(Out, Id, 1);
	return Id;
}

TArray<FWLDropRoll> FWLLootRules::BuildDefaultTable(const UWLLootSettings& S, int32 InTier, bool bMiniOrBoss, bool bGodHunter)
{
	TArray<FWLDropRoll> Rolls;
	if (bGodHunter)
	{
		Loot_AddOutcome(Loot_AddRoll(Rolls, TEXT("Refined module"), Loot_Inverse(S.GodModuleChance)), S.GodModuleItemId);
		Loot_AddOutcome(Loot_AddRoll(Rolls, TEXT("Guaranteed ore"), 1.f), S.GodOreItemId, S.GodOreMin, S.GodOreMax);
		Loot_AddOutcome(Loot_AddRoll(Rolls, TEXT("Guaranteed ammunition"), 1.f), S.GodAmmoItemId, S.GodAmmoMin, S.GodAmmoMax);
		FWLDropRoll& Gear = Loot_AddRoll(Rolls, TEXT("God-tier equipment"), Loot_Inverse(S.GodGearChance));
		const float PoolWeight = S.GodGearPools.Num() > 0 ? 1.f / S.GodGearPools.Num() : 0.f;
		for (const FWLLootIdList& Pool : S.GodGearPools)
		{
			for (const int32 Id : Pool.ItemIds)
			{
				Loot_AddOutcome(Gear, Id, 1, 1, PoolWeight / Pool.ItemIds.Num());
			}
		}
		return Rolls;
	}

	const int32 Tier = FMath::Clamp(InTier, 1, 5);
	const float Refined = Loot_Inverse(UWLLootSettings::AtTier(S.ModuleRefinedChance, Tier));
	const float Common = (1.f - Refined) * Loot_Inverse(UWLLootSettings::AtTier(S.ModuleCommonChance, Tier));
	FWLDropRoll& Module = Loot_AddRoll(Rolls, TEXT("Module (one quality per kill)"), Refined + Common);
	for (int32 i = 0; i < 7; ++i)
	{
		Loot_AddOutcome(Module, FWLModuleRules::CatalogIdFor(Tier, i, true), 1, 1, Refined / 7.f);
		Loot_AddOutcome(Module, FWLModuleRules::CatalogIdFor(Tier, i, false), 1, 1, Common / 7.f);
	}
	Loot_AddOutcome(Loot_AddRoll(Rolls, TEXT("Ore"), Loot_Inverse(UWLLootSettings::AtTier(S.OreChance, Tier)), true),
		UWLLootSettings::AtTier(S.OreItemId, Tier), S.OreMin, S.OreMax);
	Loot_AddOutcome(Loot_AddRoll(Rolls, TEXT("Ammunition"), Loot_Inverse(S.AmmoChance)),
		UWLLootSettings::AtTier(S.AmmoItemId, Tier), UWLLootSettings::AtTier(S.AmmoMin, Tier), UWLLootSettings::AtTier(S.AmmoMax, Tier));
	FWLDropRoll& Equipment = Loot_AddRoll(Rolls, TEXT("Equipment (one piece)"),
		Loot_Inverse(UWLLootSettings::AtTier(bMiniOrBoss ? S.GearChanceFromMiniBoss : S.GearChanceFromMob, Tier)));
	for (const TArray<int32>* Pool : { Loot_PoolAt(S.GearMelee, Tier), Loot_PoolAt(S.GearRanged, Tier) })
	{
		if (Pool)
		{
			for (const int32 Id : *Pool)
			{
				Loot_AddOutcome(Equipment, Id, 1, 1, 0.5f / Pool->Num());
			}
		}
	}
	return Rolls;
}

float FWLLootRules::CosmeticChance(const UWLLootSettings& S, bool bBoss, bool bMiniBoss, float TableOverride)
{
	if (TableOverride >= 0.f)
	{
		return FMath::Clamp(TableOverride, 0.f, 1.f);
	}
	return bBoss ? S.SkinChanceBoss : bMiniBoss ? S.SkinChanceMiniBoss : S.SkinChanceNormal;
}

// --- world entry point -------------------------------------------------------------------------

void UWLLootLibrary::DropEnemyLoot(const FWLEnemyLootParams& Params, const FVector& Location, AWLPlayerState* Killer)
{
	if (Params.bDummy)
	{
		return; // dummies never drop
	}
	UWorld* World = Params.Enemy ? Params.Enemy->GetWorld() : (Killer ? Killer->GetWorld() : nullptr);
	if (!World || World->GetNetMode() == NM_Client)
	{
		return;
	}

	const UWLLootSettings& Settings = *UWLLootSettings::Get();
	const UWLDataSubsystem* Data = World->GetGameInstance() ? World->GetGameInstance()->GetSubsystem<UWLDataSubsystem>() : nullptr;
	FRandomStream Rng(FMath::Rand()); // server-side randomness only

	int32 SalvagePower = Params.SalvagePower;
	if (SalvagePower < 0)
	{
		const UWLEquipmentComponent* Equipment = Killer ? Killer->GetEquipment() : nullptr;
		SalvagePower = Equipment ? Equipment->ModulePower(TEXT("Salvage")) : 0;
	}

	// Rare cosmetic finish: independent of the loot table, never touches the economy.
	if (Killer)
	{
		const float TableChance = Params.DropTable ? Params.DropTable->CosmeticChance : -1.f;
		FWLWeaponSkins::RollDrop(Killer, FWLLootRules::CosmeticChance(Settings, Params.bBoss, Params.bMiniBoss, TableChance), Rng);
	}

	TArray<FWLItemQuantity> Drops;
	int32 GodDropId = 0;
	if (Params.DropTable)
	{
		Params.DropTable->RollDrops(static_cast<float>(SalvagePower), Rng,
			[Data](int32 Id) { return Data && Data->FindItem(Id) != nullptr; }, Drops);
	}
	else if (Params.bGodHunter)
	{
		GodDropId = FWLLootRules::RollGodHunter(Settings, Rng, Drops);
	}
	else
	{
		FWLLootRules::RollTierLoot(Settings, Params.GetLootTier(), Params.bMiniBoss || Params.bBoss, SalvagePower, Rng, Drops);
	}

	for (const FWLItemQuantity& Drop : Drops)
	{
		AWLGroundItem::Spawn(World, FWLItemStack(Drop.ItemId, Drop.Quantity), Location, Killer);
	}

	if (GodDropId > 0 && Killer)
	{
		if (AWLPlayerController* PC = Cast<AWLPlayerController>(Killer->GetOwningController()))
		{
			const FText Name = Data && Data->FindItem(GodDropId) ? Data->GetItemName(GodDropId) : LOCTEXT("Extraordinary", "something extraordinary");
			PC->ClientGameMessage(FText::Format(LOCTEXT("GodDrop", "[GOD DROP] The God-Hunter yields {0}!"), Name));
		}
	}
}

#undef LOCTEXT_NAMESPACE
