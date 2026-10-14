#include "AbilitySystem/WLGameplayAbility.h"
#include "Combat/WLCombatComponent.h"
#include "Core/WLGameplayTags.h"
#include "Player/WLPlayerCharacter.h"

UWLGameplayAbility::UWLGameplayAbility()
{
	InstancingPolicy = EGameplayAbilityInstancingPolicy::InstancedPerActor;
	NetExecutionPolicy = EGameplayAbilityNetExecutionPolicy::ServerOnly;
	ActivationBlockedTags.AddTag(WLTags::State_Dead);
}

AWLPlayerCharacter* UWLGameplayAbility::GetPlayerCharacter(const FGameplayAbilityActorInfo* ActorInfo) const
{
	return ActorInfo ? Cast<AWLPlayerCharacter>(ActorInfo->AvatarActor.Get()) : nullptr;
}

UWLCombatComponent* UWLGameplayAbility::GetCombat(const FGameplayAbilityActorInfo* ActorInfo) const
{
	const AWLPlayerCharacter* Character = GetPlayerCharacter(ActorInfo);
	return Character ? Character->GetCombat() : nullptr;
}
