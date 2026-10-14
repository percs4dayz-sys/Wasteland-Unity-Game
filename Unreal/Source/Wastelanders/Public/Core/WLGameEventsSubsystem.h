#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "Engine/World.h"
#include "Core/WLTypes.h"
#include "WLGameEventsSubsystem.generated.h"

class AActor;
class AWLPlayerState;

DECLARE_MULTICAST_DELEGATE_TwoParams(FWLOnEnemyKilled, AActor* /*Enemy*/, AWLPlayerState* /*Killer, may be null*/);
DECLARE_MULTICAST_DELEGATE_ThreeParams(FWLOnXPGained, AWLPlayerState*, EWLSkill, int32 /*Amount*/);
DECLARE_MULTICAST_DELEGATE_ThreeParams(FWLOnLevelUp, AWLPlayerState*, EWLSkill, int32 /*NewLevel*/);
DECLARE_MULTICAST_DELEGATE_FourParams(FWLOnItemGained, AWLPlayerState*, int32 /*ItemId*/, int32 /*Quantity*/, FName /*Source: Gather, Craft, Loot, Quest...*/);
DECLARE_MULTICAST_DELEGATE_TwoParams(FWLOnFlagSet, AWLPlayerState*, const FString& /*Flag*/);
DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnPlayerDied, AWLPlayerState*);

/**
 * Server-side gameplay event bus. Replaces Unity's static events (CombatTarget.AnyKilled,
 * PlayerStats.OnLevelUp...) so systems such as beast tasks, quests and achievements can listen
 * without hard references. Broadcasts happen on the authority only.
 */
UCLASS()
class WASTELANDERS_API UWLGameEventsSubsystem : public UWorldSubsystem
{
	GENERATED_BODY()

public:
	static UWLGameEventsSubsystem* Get(const UObject* WorldContextObject)
	{
		const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
		return World ? World->GetSubsystem<UWLGameEventsSubsystem>() : nullptr;
	}

	FWLOnEnemyKilled OnEnemyKilled;
	FWLOnXPGained OnXPGained;
	FWLOnLevelUp OnLevelUp;
	FWLOnItemGained OnItemGained;
	FWLOnFlagSet OnFlagSet;
	FWLOnPlayerDied OnPlayerDied;
};
