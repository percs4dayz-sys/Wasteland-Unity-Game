#pragma once

#include "CoreMinimal.h"
#include "Abilities/GameplayAbility.h"
#include "WLGameplayAbility.generated.h"

class UWLCombatComponent;
class AWLPlayerCharacter;

/**
 * Base for Wastelanders abilities. Server-only, instanced per actor, and blocked while State_Dead
 * is on the owner (Unity's "Unavailable while down"). Abilities are thin GAS wrappers around the
 * combat component, which keeps the testable rules; the ability layer adds activation blocking,
 * owned tags (State_Reloading) and a hook point for future cues and animation montages.
 */
UCLASS(Abstract)
class WASTELANDERS_API UWLGameplayAbility : public UGameplayAbility
{
	GENERATED_BODY()

public:
	UWLGameplayAbility();

protected:
	AWLPlayerCharacter* GetPlayerCharacter(const FGameplayAbilityActorInfo* ActorInfo) const;
	UWLCombatComponent* GetCombat(const FGameplayAbilityActorInfo* ActorInfo) const;
};
