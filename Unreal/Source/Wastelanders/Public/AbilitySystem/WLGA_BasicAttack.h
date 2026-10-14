#pragma once

#include "CoreMinimal.h"
#include "AbilitySystem/WLGameplayAbility.h"
#include "WLGA_BasicAttack.generated.h"

/** One swing or shot at the combat component's target (ActionCombat3D.ResolveHit path). Fired by the tick loop when the weapon is off cooldown. */
UCLASS()
class WASTELANDERS_API UWLGA_BasicAttack : public UWLGameplayAbility
{
	GENERATED_BODY()

public:
	virtual void ActivateAbility(const FGameplayAbilitySpecHandle Handle, const FGameplayAbilityActorInfo* ActorInfo,
		const FGameplayAbilityActivationInfo ActivationInfo, const FGameplayEventData* TriggerEventData) override;
};
