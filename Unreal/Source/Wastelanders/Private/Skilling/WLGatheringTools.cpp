#include "Skilling/WLGatheringTools.h"
#include "Player/WLPlayerState.h"
#include "Items/WLInventoryComponent.h"
#include "Items/WLEquipmentComponent.h"
#include "Items/WLItemTypes.h"
#include "Skills/WLSkillsComponent.h"

int32 FWLGatheringTools::BaseTool(int32 ItemId)
{
	if (ItemId >= 3 && ItemId <= 5)
	{
		return ItemId;
	}
	return IsUpgrade(ItemId) ? 3 + (ItemId - FirstUpgradeId) / 3 : 0;
}

EWLSkill FWLGatheringTools::GatheringSkill(int32 ItemId)
{
	switch (BaseTool(ItemId))
	{
	case 3: return EWLSkill::Scrapping;
	case 4: return EWLSkill::Woodcutting;
	case 5: return EWLSkill::Fishing;
	default: return EWLSkill::Scrapping;
	}
}

float FWLGatheringTools::SpeedForRank(int32 InRank)
{
	static const float Speeds[] = { 1.f, 1.15f, 1.30f, 1.50f };
	return Speeds[FMath::Clamp(InRank, 0, 3)];
}

int32 FWLGatheringTools::BestOf(int32 RequiredToolId, TConstArrayView<int32> CandidateIds, int32 SkillLevel)
{
	const int32 Family = BaseTool(RequiredToolId);
	if (Family == 0)
	{
		// Not a gathering-tool family (e.g. a quest tool): exact item only.
		return CandidateIds.Contains(RequiredToolId) ? RequiredToolId : 0;
	}

	int32 Best = 0;
	for (const int32 Id : CandidateIds)
	{
		if (Id <= 0 || BaseTool(Id) != Family || Rank(Id) < Rank(RequiredToolId))
		{
			continue;
		}
		if (SkillLevel < RequiredLevel(Id))
		{
			continue;
		}
		if (Best == 0 || Rank(Id) > Rank(Best))
		{
			Best = Id;
		}
	}
	return Best;
}

int32 FWLGatheringTools::BestFor(const AWLPlayerState* PlayerState, int32 RequiredToolId)
{
	if (!PlayerState)
	{
		return 0;
	}
	const UWLInventoryComponent* Inv = PlayerState->GetInventory();
	const UWLEquipmentComponent* Equip = PlayerState->GetEquipment();
	const UWLSkillsComponent* Skills = PlayerState->GetSkills();

	TArray<int32, TInlineAllocator<32>> Candidates;
	if (Equip)
	{
		Candidates.Add(Equip->GetItemId(EWLEquipSlot::Tool));
	}
	if (Inv)
	{
		for (int32 i = 0; i < Inv->GetCapacity(); ++i)
		{
			const FWLItemStack& Stack = Inv->GetSlot(i);
			if (Stack.ItemId > 0 && Stack.Quantity > 0)
			{
				Candidates.Add(Stack.ItemId);
			}
		}
	}

	const int32 Family = BaseTool(RequiredToolId);
	const int32 Level = (Skills && Family != 0) ? Skills->GetLevel(GatheringSkill(RequiredToolId)) : 1;
	return BestOf(RequiredToolId, Candidates, Level);
}

bool FWLGatheringTools::CanUse(const AWLPlayerState* PlayerState, int32 RequiredToolId)
{
	return RequiredToolId <= 0 || BestFor(PlayerState, RequiredToolId) != 0;
}

float FWLGatheringTools::CycleSpeed(const AWLPlayerState* PlayerState, int32 RequiredToolId)
{
	if (BaseTool(RequiredToolId) == 0)
	{
		return 1.f;
	}
	return SpeedForRank(Rank(BestFor(PlayerState, RequiredToolId)));
}
