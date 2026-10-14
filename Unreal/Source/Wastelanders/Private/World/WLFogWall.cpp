#include "World/WLFogWall.h"
#include "Player/WLPlayerState.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Components/BoxComponent.h"

#define LOCTEXT_NAMESPACE "WLFogWall"

AWLFogWall::AWLFogWall()
{
	RevealMessage = LOCTEXT("Reveal", "The fog parts as you push through...");
	// The fog only hides the view; it never stops anyone (and doesn't fog your own side's water).
	Blocker->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Blocker->SetBoxExtent(FVector(30.f, 600.f, 400.f));
	Trigger->SetBoxExtent(FVector(40.f, 600.f, 400.f));
	bShowPlaceholderBarrier = false;
	OpenDuration = 1.5f; // existing puffs age out
}

bool AWLFogWall::IsOpenFor(const AWLPlayerState* PS) const
{
	const UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	return Flags && Flags->HasFlag(GetFlag());
}

void AWLFogWall::OnPlayerEntered(AWLPlayerState* PS, APawn* Pawn)
{
	UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	if (!Flags || Flags->HasFlag(GetFlag()))
	{
		return;
	}
	Flags->SetFlag(GetFlag());
	if (!RevealMessage.IsEmpty())
	{
		Message(PS, RevealMessage);
	}
}

#undef LOCTEXT_NAMESPACE
