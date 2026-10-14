#include "World/WLTierGate.h"
#include "Player/WLPlayerState.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Wastelanders.h"

#define LOCTEXT_NAMESPACE "WLTierGate"

AWLTierGate::AWLTierGate()
{
	BossAliveMessage = LOCTEXT("BossAlive", "The gate will not move. Something in this land still holds it shut.");
	BossDefeatedMessage = LOCTEXT("BossDefeated", "A shudder runs through the gate. Whatever held it is dead.");
	UnlockMessage = LOCTEXT("Unlock", "The oppressive presence recedes. The path is clear.");
	OpenDuration = 3.5f;
}

void AWLTierGate::BeginPlay()
{
	Super::BeginPlay();
	if (HasAuthority() && !HasBoss())
	{
		UE_LOG(LogWastelanders, Warning, TEXT("[TierGate] '%s' (tier %d) has no Required Boss assigned. Opening it for everyone so nobody gets stuck; assign this tier's boss to make it a real gate."),
			*GateId, TargetTier);
	}
}

bool AWLTierGate::IsBossDefeatedFor(const AWLPlayerState* PS) const
{
	const UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	return !HasBoss() || (Flags && Flags->HasFlag(GetBossFlag()));
}

bool AWLTierGate::IsOpenFor(const AWLPlayerState* PS) const
{
	if (!HasBoss())
	{
		return true; // misconfigured gate: never trap anyone behind it
	}
	const UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	return Flags && Flags->HasFlag(GetGateFlag());
}

FText AWLTierGate::Prefix(bool bGood) const
{
	return FText::Format(bGood ? LOCTEXT("PrefixGood", "[TIER {0} GATE]") : LOCTEXT("PrefixBad", "[TIER {0} GATE]"), TargetTier);
}

void AWLTierGate::OnBossDefeatedBy(AWLPlayerState* PS)
{
	UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	if (!Flags || Flags->HasFlag(GetBossFlag()))
	{
		return; // already banked; only the first kill counts
	}
	Flags->SetFlag(GetBossFlag());
	if (!BossDefeatedMessage.IsEmpty())
	{
		Message(PS, FText::Format(LOCTEXT("Line", "{0} {1}"), Prefix(true), BossDefeatedMessage));
	}
}

void AWLTierGate::OnPlayerEntered(AWLPlayerState* PS, APawn* Pawn)
{
	UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	if (!Flags)
	{
		return;
	}
	if (Flags->HasFlag(GetGateFlag()))
	{
		// Saves written before the waypoint network existed only have the per-gate flag.
		if (!Flags->HasFlag(GetTierFlag()))
		{
			Flags->SetFlag(GetTierFlag());
		}
		return;
	}
	if (IsBossDefeatedFor(PS))
	{
		Open(PS);
		return;
	}
	const FText Who = BossName.IsEmpty() ? LOCTEXT("ThisBoss", "this tier's boss") : BossName;
	Message(PS, FText::Format(LOCTEXT("Locked", "{0} {1}\n  - Defeat {2} to open this gate"), Prefix(false), BossAliveMessage, Who));
}

void AWLTierGate::Open(AWLPlayerState* PS)
{
	UWLPlayerFlagsComponent* Flags = PS->GetFlags();
	Flags->SetFlag(GetGateFlag());
	Flags->SetFlag(GetTierFlag()); // unlocks this tier's pad on the waypoint island
	Message(PS, FText::Format(LOCTEXT("Line2", "{0} {1}"), Prefix(true), UnlockMessage));
	if (APawn* Pawn = PS->GetPawn())
	{
		RefreshPawnCollision(Pawn);
	}
}

#undef LOCTEXT_NAMESPACE
