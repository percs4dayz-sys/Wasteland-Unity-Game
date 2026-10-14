#include "Quests/WLQuestTargets.h"
#include "Quests/WLQuestFlags.h"
#include "World/WLResourceNode.h"
#include "World/WLCraftingStation.h"
#include "EngineUtils.h"
#include "Engine/World.h"

AActor* FWLQuestTargets::NearestNode(const UWorld* World, EWLSkill Skill, const FVector& From, int32 PlayerLevel, int32 ToolId)
{
	if (!World)
	{
		return nullptr;
	}
	AActor* Best = nullptr;
	AActor* Usable = nullptr;
	AActor* Any = nullptr;
	double BestSq = TNumericLimits<double>::Max(), UsableSq = BestSq, AnySq = BestSq;

	for (TActorIterator<AWLResourceNode> It(const_cast<UWorld*>(World)); It; ++It)
	{
		AWLResourceNode* N = *It;
		if (!N || N->GetSkill() != Skill)
		{
			continue;
		}
		const double D = FVector::DistSquared(N->GetActorLocation(), From);
		if (D < AnySq) { AnySq = D; Any = N; }
		if (N->GetLevelRequired() > PlayerLevel || N->IsSulphurVent())
		{
			continue;
		}
		if (D < UsableSq) { UsableSq = D; Usable = N; }
		if (ToolId > 0 && N->GetRequiredToolId() != ToolId)
		{
			continue;
		}
		if (D < BestSq) { BestSq = D; Best = N; }
	}
	return Best ? Best : Usable ? Usable : Any;
}

AActor* FWLQuestTargets::NearestStation(const UWorld* World, EWLCraftingStation Station, const FVector& From)
{
	if (!World)
	{
		return nullptr;
	}
	AActor* Best = nullptr;
	double BestSq = TNumericLimits<double>::Max();
	for (TActorIterator<AWLCraftingStation> It(const_cast<UWorld*>(World)); It; ++It)
	{
		if (*It && It->GetStation() == Station)
		{
			const double D = FVector::DistSquared(It->GetActorLocation(), From);
			if (D < BestSq) { BestSq = D; Best = *It; }
		}
	}
	return Best;
}

AActor* FWLQuestTargets::TargetForSkill(const UWorld* World, EWLSkill Skill, const FVector& From, int32 PlayerLevel, int32 ToolId)
{
	switch (Skill)
	{
	case EWLSkill::Fishing:
	case EWLSkill::Woodcutting:
	case EWLSkill::Scrapping:
		return NearestNode(World, Skill, From, PlayerLevel, ToolId);
	case EWLSkill::Cooking:
		return NearestStation(World, EWLCraftingStation::CookingFire, From);
	case EWLSkill::Smithing:
		// Smithing spans both surfaces: point at the furnace if there is one, else the workbench.
		if (AActor* Furnace = NearestStation(World, EWLCraftingStation::Furnace, From))
		{
			return Furnace;
		}
		return NearestStation(World, EWLCraftingStation::Workbench, From);
	default:
		return nullptr;   // Beastmastery / combat skills: no fixed world spot
	}
}

AActor* FWLQuestTargets::TargetForLesson(const UWorld* World, EWLSkill Skill, int32 GiveItemId, const FVector& From, int32 PlayerLevel)
{
	if (Skill == EWLSkill::Smithing && GiveItemId == WLQuestItems::ScrapBar)
	{
		return NearestStation(World, EWLCraftingStation::Workbench, From);
	}
	if (Skill == EWLSkill::Scrapping && GiveItemId == WLQuestItems::GeigerCounter)
	{
		return nullptr;   // the golems roam; the Geiger counter leads
	}
	switch (Skill)
	{
	case EWLSkill::Refinement:   return NearestStation(World, EWLCraftingStation::Furnace, From);
	case EWLSkill::Fission:
	case EWLSkill::Beastmastery: return nullptr;
	default:                     return TargetForSkill(World, Skill, From, PlayerLevel, GiveItemId);
	}
}
