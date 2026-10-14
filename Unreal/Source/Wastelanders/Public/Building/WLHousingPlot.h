#pragma once

#include "CoreMinimal.h"
#include "World/WLInteractableActor.h"
#include "WLHousingPlot.generated.h"

/**
 * Marks the player's housing plot (placed via the Foundation Shelter buildable). Ported from
 * World/HousingPlot.cs: placing one raises the "housing_plot" flag on the builder. Unity raised it in
 * Start() on the lone player; here the build component calls MarkBuilder on the server because only
 * the builder gets the flag. Not interactable beyond Examine.
 */
UCLASS()
class WASTELANDERS_API AWLHousingPlot : public AWLInteractableActor
{
	GENERATED_BODY()

public:
	AWLHousingPlot();

	/** Exact Unity flag spelling. */
	static const FString PlotFlag;

	/** SERVER. Sets PlotFlag on the builder. */
	static void MarkBuilder(class AWLPlayerState* Builder);

	virtual TArray<FWLInteractOption> GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const override;
};
