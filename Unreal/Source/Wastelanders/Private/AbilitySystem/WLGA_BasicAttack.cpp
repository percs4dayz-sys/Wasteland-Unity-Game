#include "AbilitySystem/WLGA_BasicAttack.h"
#include "Combat/WLCombatComponent.h"

void UWLGA_BasicAttack::ActivateAbility(const FGameplayAbilitySpecHandle Handle, const FGameplayAbilityActorInfo* ActorInfo,
	const FGameplayAbilityActivationInfo ActivationInfo, const FGameplayEventData* TriggerEventData)
{
	UWLCombatComponent* Combat = GetCombat(ActorInfo);
	if (!Combat || !CommitAbility(Handle, ActorInfo, ActivationInfo))
	{
		EndAbility(Handle, ActorInfo, ActivationInfo, true, true);
		return;
	}
	const bool bDone = Combat->PerformBasicAttack();
	EndAbility(Handle, ActorInfo, ActivationInfo, true, !bDone);
}
