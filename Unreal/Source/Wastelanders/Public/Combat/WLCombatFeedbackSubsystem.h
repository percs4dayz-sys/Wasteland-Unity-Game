#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "Combat/WLCombatTypes.h"
#include "WLCombatFeedbackSubsystem.generated.h"

DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnHitsplat, const FWLHitsplat&);

/**
 * Local (per machine) hitsplat bus: the data half of Unity's CombatFeedbackUI. The HUD binds
 * OnHitsplat to draw damage numbers; HP bars read the attribute sets directly. Splats arrive from
 * the unreliable multicasts on AWLEnemyCharacter / UWLCombatComponent, and fire here on every client
 * (and on a listen server). Dedicated servers broadcast nothing: there is no one to draw for.
 */
UCLASS()
class WASTELANDERS_API UWLCombatFeedbackSubsystem : public UWorldSubsystem
{
	GENERATED_BODY()

public:
	static UWLCombatFeedbackSubsystem* Get(const UObject* WorldContextObject);

	FWLOnHitsplat OnHitsplat;

	/** Called by the multicast handlers. */
	void Push(const FWLHitsplat& Splat);
};

/** Server-side entry point every combat path uses (ShowWorldSplat). */
struct WASTELANDERS_API FWLCombatFeedback
{
	/**
	 * SERVER. Show a hitsplat on Target to every nearby client. Target may be an AWLEnemyCharacter or
	 * a player pawn with a UWLCombatComponent; anything else is ignored.
	 */
	static void Hitsplat(AActor* Target, int32 Amount, bool bHit, FName Tag = NAME_None);
};
