#include "AbilitySystem/WLGA_Reload.h"
#include "Abilities/Tasks/AbilityTask_WaitDelay.h"
#include "Combat/WLCombatComponent.h"
#include "Core/WLGameplayTags.h"

UWLGA_Reload::UWLGA_Reload()
{
	ActivationOwnedTags.AddTag(WLTags::State_Reloading);
	ActivationBlockedTags.AddTag(WLTags::State_Reloading);
}

void UWLGA_Reload::ActivateAbility(const FGameplayAbilitySpecHandle Handle, const FGameplayAbilityActorInfo* ActorInfo,
	const FGameplayAbilityActivationInfo ActivationInfo, const FGameplayEventData* TriggerEventData)
{
	bCompleted = false;
	UWLCombatComponent* Combat = GetCombat(ActorInfo);
	if (!Combat || !CommitAbility(Handle, ActorInfo, ActivationInfo))
	{
		EndAbility(Handle, ActorInfo, ActivationInfo, true, true);
		return;
	}
	const float Duration = Combat->BeginReload();
	if (Duration <= 0.f)
	{
		EndAbility(Handle, ActorInfo, ActivationInfo, true, true);
		return;
	}
	UAbilityTask_WaitDelay* Wait = UAbilityTask_WaitDelay::WaitDelay(this, Duration);
	Wait->OnFinish.AddDynamic(this, &UWLGA_Reload::OnReloadFinished);
	Wait->ReadyForActivation();
}

void UWLGA_Reload::OnReloadFinished()
{
	if (UWLCombatComponent* Combat = GetCombat(GetCurrentActorInfo()))
	{
		Combat->CompleteReload();
	}
	bCompleted = true;
	EndAbility(GetCurrentAbilitySpecHandle(), GetCurrentActorInfo(), GetCurrentActivationInfo(), true, false);
}

void UWLGA_Reload::EndAbility(const FGameplayAbilitySpecHandle Handle, const FGameplayAbilityActorInfo* ActorInfo,
	const FGameplayAbilityActivationInfo ActivationInfo, bool bReplicateEndAbility, bool bWasCancelled)
{
	if (!bCompleted)
	{
		// Cancelled mid-reload (weapon swapped, died): the reload is lost.
		if (UWLCombatComponent* Combat = GetCombat(ActorInfo))
		{
			Combat->AbortReload();
		}
	}
	Super::EndAbility(Handle, ActorInfo, ActivationInfo, bReplicateEndAbility, bWasCancelled);
}
