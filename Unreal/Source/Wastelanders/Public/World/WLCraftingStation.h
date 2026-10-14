#pragma once

#include "CoreMinimal.h"
#include "World/WLInteractableActor.h"
#include "Core/WLTypes.h"
#include "WLCraftingStation.generated.h"

/**
 * A furnace, cooking fire or workbench. Ported from Assets/Scripts/World/CraftingStation.cs.
 *
 * Recipes are not stored on the station: UWLDataSubsystem::GetRecipes(Station) is the single source
 * (Unity's useCatalogRecipes path, which every station used). Using it opens the crafting UI on the
 * player's client via UWLSkillingComponent, which remembers this station so ServerCraft can check
 * the player is still standing at it.
 *
 * Unity's TryCraft fallback (auto-craft the first makeable recipe when no UI exists) is not ported:
 * the UI always exists in this game and an auto-craft would bypass the player's choice.
 */
UCLASS()
class WASTELANDERS_API AWLCraftingStation : public AWLInteractableActor
{
	GENERATED_BODY()

public:
	AWLCraftingStation();

	virtual void OnConstruction(const FTransform& Transform) override;

	// IWLInteractable
	virtual FText GetInteractName_Implementation() const override;
	virtual FText GetExamineText_Implementation() const override;
	virtual bool Interact_Implementation(AWLPlayerCharacter* Player, FName Verb) override;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Crafting")
	EWLCraftingStation GetStation() const { return Station; }

	/** SERVER (build system): configure a freshly spawned deployable before FinishSpawning. */
	void SetStation(EWLCraftingStation InStation) { Station = InStation; }

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Replicated, Category = "Wastelanders|Crafting")
	EWLCraftingStation Station = EWLCraftingStation::Workbench;

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
};
