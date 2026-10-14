#include "World/WLCraftingStation.h"
#include "Skilling/WLSkillingComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Net/UnrealNetwork.h"

#define LOCTEXT_NAMESPACE "WLCraftingStation"

AWLCraftingStation::AWLCraftingStation()
{
	InteractRange = 200.f;
}

void AWLCraftingStation::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(AWLCraftingStation, Station);
}

void AWLCraftingStation::OnConstruction(const FTransform& Transform)
{
	// Distinct placeholder per station type so a greybox level reads at a glance.
	if (ArtMesh.IsNull())
	{
		switch (Station)
		{
		case EWLCraftingStation::Furnace:
			PlaceholderShape = EWLPlaceholderShape::Cube;
			PlaceholderSize = FVector(140.f, 140.f, 160.f);
			PlaceholderColor = FLinearColor(0.35f, 0.22f, 0.18f);
			break;
		case EWLCraftingStation::CookingFire:
			PlaceholderShape = EWLPlaceholderShape::Cylinder;
			PlaceholderSize = FVector(110.f, 110.f, 30.f);
			PlaceholderColor = FLinearColor(0.5f, 0.28f, 0.12f);
			break;
		case EWLCraftingStation::Workbench:
		default:
			PlaceholderShape = EWLPlaceholderShape::Cube;
			PlaceholderSize = FVector(160.f, 90.f, 110.f);
			PlaceholderColor = FLinearColor(0.3f, 0.3f, 0.34f);
			break;
		}
	}
	Super::OnConstruction(Transform);
}

FText AWLCraftingStation::GetInteractName_Implementation() const
{
	if (!DisplayName.IsEmpty())
	{
		return DisplayName;
	}
	switch (Station)
	{
	case EWLCraftingStation::Furnace:     return LOCTEXT("Furnace", "Furnace");
	case EWLCraftingStation::CookingFire: return LOCTEXT("CookingFire", "Cooking Fire");
	case EWLCraftingStation::Workbench:   return LOCTEXT("Workbench", "Workbench");
	default:                              return LOCTEXT("Station", "Crafting Station");
	}
}

FText AWLCraftingStation::GetExamineText_Implementation() const
{
	if (!ExamineText.IsEmpty())
	{
		return ExamineText;
	}
	switch (Station)
	{
	case EWLCraftingStation::Furnace:     return LOCTEXT("ExFurnace", "A makeshift furnace for smelting scrap into bars.");
	case EWLCraftingStation::CookingFire: return LOCTEXT("ExFire", "A crackling fire. Good for cooking raw food.");
	case EWLCraftingStation::Workbench:   return LOCTEXT("ExBench", "A sturdy workbench for assembling firearms, melee weapons, and armor.");
	default:                              return LOCTEXT("ExStation", "A station for crafting.");
	}
}

bool AWLCraftingStation::Interact_Implementation(AWLPlayerCharacter* Player, FName Verb)
{
	if (!Player || Verb != GetDefaultVerb())
	{
		return false;
	}
	UWLSkillingComponent* Skilling = Player->GetSkilling();
	return Skilling && Skilling->OpenStation(this);
}

#undef LOCTEXT_NAMESPACE
