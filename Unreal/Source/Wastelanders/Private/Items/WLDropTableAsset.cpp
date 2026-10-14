#include "Items/WLDropTableAsset.h"
#include "Items/WLLootSettings.h"
#include "Items/WLLootLibrary.h"

namespace
{
	bool Drop_IsValidOutcome(const FWLDropOutcome& Entry, TFunctionRef<bool(int32)> IsValidItem)
	{
		return Entry.Weight > 0.f && FMath::IsFinite(Entry.Weight) && IsValidItem(Entry.ItemId);
	}

	/** Unity: chance >= 1 always wins, chance > 0 rolls, otherwise never. */
	bool Drop_RollChance(float Chance, FRandomStream& Rng)
	{
		return Chance >= 1.f || (Chance > 0.f && Rng.FRand() < Chance);
	}
}

void UWLDropTableAsset::RollDrops(float SalvagePower, FRandomStream& Rng, TFunctionRef<bool(int32)> IsValidItem, TArray<FWLItemQuantity>& Out) const
{
	RollTable(Rolls, SalvagePower, UWLLootSettings::Get()->SalvageChancePerPower, Rng, IsValidItem, Out);
}

void UWLDropTableAsset::RollTable(const TArray<FWLDropRoll>& InRolls, float SalvagePower, float SalvageChancePerPower, FRandomStream& Rng, TFunctionRef<bool(int32)> IsValidItem, TArray<FWLItemQuantity>& Out)
{
	for (const FWLDropRoll& Roll : InRolls)
	{
		bool bWon = Drop_RollChance(FMath::Clamp(Roll.Chance, 0.f, 1.f), Rng);
		if (!bWon && Roll.bSalvageBonus)
		{
			bWon = Drop_RollChance(FMath::Clamp(SalvageChancePerPower * SalvagePower, 0.f, 1.f), Rng);
		}
		if (!bWon)
		{
			continue;
		}

		float Total = 0.f;
		for (const FWLDropOutcome& Entry : Roll.Outcomes)
		{
			if (Drop_IsValidOutcome(Entry, IsValidItem))
			{
				Total += Entry.Weight;
			}
		}
		if (Total <= 0.f)
		{
			continue;
		}

		// Weighted pick; falls back to the last valid entry on float round-off (as Unity does).
		float Pick = Rng.FRand() * Total;
		const FWLDropOutcome* Selected = nullptr;
		for (const FWLDropOutcome& Entry : Roll.Outcomes)
		{
			if (!Drop_IsValidOutcome(Entry, IsValidItem))
			{
				continue;
			}
			Selected = &Entry;
			Pick -= Entry.Weight;
			if (Pick <= 0.f)
			{
				break;
			}
		}
		if (Selected)
		{
			const int32 Min = FMath::Clamp(Selected->MinQuantity, 1, 1000000);
			const int32 Max = FMath::Clamp(Selected->MaxQuantity, Min, 1000000);
			FWLItemQuantity& Drop = Out.AddDefaulted_GetRef();
			Drop.ItemId = Selected->ItemId;
			Drop.Quantity = Rng.RandRange(Min, Max);
		}
	}
}

void UWLDropTableAsset::FillFromStandardTable()
{
	Modify();
	Rolls = FWLLootRules::BuildDefaultTable(*UWLLootSettings::Get(), DefaultsTier, bDefaultsMiniBoss, bDefaultsGodHunter);
}
