#include "Combat/WLCombatFeedbackSubsystem.h"
#include "Combat/WLEnemyCharacter.h"
#include "Combat/WLCombatComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Engine/World.h"

UWLCombatFeedbackSubsystem* UWLCombatFeedbackSubsystem::Get(const UObject* WorldContextObject)
{
	const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
	return World ? World->GetSubsystem<UWLCombatFeedbackSubsystem>() : nullptr;
}

void UWLCombatFeedbackSubsystem::Push(const FWLHitsplat& Splat)
{
	if (GetWorld() && GetWorld()->GetNetMode() == NM_DedicatedServer)
	{
		return;
	}
	OnHitsplat.Broadcast(Splat);
}

void FWLCombatFeedback::Hitsplat(AActor* Target, int32 Amount, bool bHit, FName Tag)
{
	if (!Target || !Target->HasAuthority())
	{
		return;
	}
	if (AWLEnemyCharacter* Enemy = Cast<AWLEnemyCharacter>(Target))
	{
		Enemy->MulticastHitsplat(Amount, bHit, Tag);
	}
	else if (const AWLPlayerCharacter* Player = Cast<AWLPlayerCharacter>(Target))
	{
		if (UWLCombatComponent* Combat = Player->GetCombat())
		{
			Combat->MulticastHitsplat(Amount, bHit, Tag);
		}
	}
}
