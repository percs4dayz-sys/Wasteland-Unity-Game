#include "World/WLBossShortcut.h"
#include "World/WLTierGate.h"
#include "Player/WLPlayerState.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Components/BoxComponent.h"

#define LOCTEXT_NAMESPACE "WLBossShortcut"

AWLBossShortcut::AWLBossShortcut()
{
	OpenMessage = LOCTEXT("Open", "A path has opened back toward your base.");
	OpenDuration = 2.f;
	bPlacedPoseIsOpen = true;
	// No walk-in behaviour: the trigger is unused.
	Trigger->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Trigger->SetGenerateOverlapEvents(false);
}

void AWLBossShortcut::BeginPlay()
{
	// Resolve before Super::BeginPlay starts the polls that read it. Actor names are stable for
	// placed actors, so the auto flag survives restarts like Unity's "shortcut_" + gameObject.name.
	ResolvedFlag = UnlockFlag.IsEmpty() ? (TEXT("shortcut_") + GetName()) : UnlockFlag;
	Super::BeginPlay();
}

bool AWLBossShortcut::IsOpenFor(const AWLPlayerState* PS) const
{
	const UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	return Flags && !ResolvedFlag.IsEmpty() && Flags->HasFlag(ResolvedFlag);
}

void AWLBossShortcut::OnBossDefeatedBy(AWLPlayerState* PS)
{
	UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	if (!Flags)
	{
		return;
	}
	const bool bAlready = Flags->HasFlag(ResolvedFlag);
	Flags->SetFlag(ResolvedFlag); // remembered and persisted with the character
	if (UnlockedTier >= 2)
	{
		Flags->SetFlag(AWLTierGate::TierFlagFor(UnlockedTier));
	}
	if (!bAlready && !OpenMessage.IsEmpty())
	{
		Message(PS, OpenMessage);
	}
	if (APawn* Pawn = PS->GetPawn())
	{
		RefreshPawnCollision(Pawn);
	}
}

#undef LOCTEXT_NAMESPACE
