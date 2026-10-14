#pragma once

#include "CoreMinimal.h"
#include "AbilitySystem/WLGameplayAbility.h"
#include "WLGA_SpecialAttack.generated.h"

/** The Fission Overload Cascade (ActionCombat3D.TrySpecial). Spends 50 special energy and four cores; requested by the client through UWLCombatComponent::RequestSpecialAttack. */
UCLASS()
class WASTELANDERS_API UWLGA_SpecialAttack : public UWLGameplayAbility
{
	GENERATED_BODY()

public:
	virtual void ActivateAbility(const FGameplayAbilitySpecHandle Handle, const FGameplayAbilityActorInfo* ActorInfo,
		const FGameplayAbilityActivationInfo ActivationInfo, const FGameplayEventData* TriggerEventData) override;
};
