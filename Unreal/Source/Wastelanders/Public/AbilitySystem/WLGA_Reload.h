#pragma once

#include "CoreMinimal.h"
#include "AbilitySystem/WLGameplayAbility.h"
#include "WLGA_Reload.generated.h"

/**
 * Magazine reload for guns (WeaponMagazine.TryStartReload). Stays active for the weapon's reload
 * time with State_Reloading owned, then refills the magazine. Cancelled (reload lost) when the
 * weapon is swapped mid-reload or the player dies.
 */
UCLASS()
class WASTELANDERS_API UWLGA_Reload : public UWLGameplayAbility
{
	GENERATED_BODY()

public:
	UWLGA_Reload();

	virtual void ActivateAbility(const FGameplayAbilitySpecHandle Handle, const FGameplayAbilityActorInfo* ActorInfo,
		const FGameplayAbilityActivationInfo ActivationInfo, const FGameplayEventData* TriggerEventData) override;
	virtual void EndAbility(const FGameplayAbilitySpecHandle Handle, const FGameplayAbilityActorInfo* ActorInfo,
		const FGameplayAbilityActivationInfo ActivationInfo, bool bReplicateEndAbility, bool bWasCancelled) override;

protected:
	UFUNCTION()
	void OnReloadFinished();

	bool bCompleted = false;
};
