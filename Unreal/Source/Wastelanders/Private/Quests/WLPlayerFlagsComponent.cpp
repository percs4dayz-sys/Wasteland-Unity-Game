#include "Quests/WLPlayerFlagsComponent.h"
#include "Core/WLGameEventsSubsystem.h"
#include "Player/WLPlayerState.h"
#include "Persistence/WLCharacterSave.h"
#include "Net/UnrealNetwork.h"

UWLPlayerFlagsComponent::UWLPlayerFlagsComponent()
{
	PrimaryComponentTick.bCanEverTick = false;
	SetIsReplicatedByDefault(true);
}

void UWLPlayerFlagsComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME_CONDITION(UWLPlayerFlagsComponent, Flags, COND_OwnerOnly);
}

bool UWLPlayerFlagsComponent::HasFlag(const FString& Flag) const
{
	return Flags.Contains(Flag);
}

bool UWLPlayerFlagsComponent::SetFlag(const FString& Flag)
{
	if (!GetOwner() || !GetOwner()->HasAuthority() || Flag.IsEmpty() || Flags.Contains(Flag))
	{
		return false;
	}
	Flags.Add(Flag);
	BroadcastChanged();

	if (UWLGameEventsSubsystem* Events = UWLGameEventsSubsystem::Get(this))
	{
		Events->OnFlagSet.Broadcast(Cast<AWLPlayerState>(GetOwner()), Flag);
	}
	return true;
}

bool UWLPlayerFlagsComponent::RemoveFlag(const FString& Flag)
{
	if (!GetOwner() || !GetOwner()->HasAuthority())
	{
		return false;
	}
	if (Flags.Remove(Flag) == 0)
	{
		return false;
	}
	BroadcastChanged();
	return true;
}

int32 UWLPlayerFlagsComponent::RemoveFlagsWithPrefix(const FString& Prefix)
{
	if (!GetOwner() || !GetOwner()->HasAuthority() || Prefix.IsEmpty())
	{
		return 0;
	}
	const int32 Removed = Flags.RemoveAll([&Prefix](const FString& F) { return F.StartsWith(Prefix, ESearchCase::CaseSensitive); });
	if (Removed > 0)
	{
		BroadcastChanged();
	}
	return Removed;
}

const FString* UWLPlayerFlagsComponent::FindFlagWithPrefix(const FString& Prefix) const
{
	return Flags.FindByPredicate([&Prefix](const FString& F) { return F.StartsWith(Prefix, ESearchCase::CaseSensitive); });
}

void UWLPlayerFlagsComponent::CaptureSave(FWLCharacterSave& Out) const
{
	Out.Flags = Flags;
}

void UWLPlayerFlagsComponent::ApplySave(const FWLCharacterSave& In)
{
	// De-duplicate on load: Unity stored a HashSet, but hand-edited / imported JSON may not be clean.
	Flags.Reset(In.Flags.Num());
	for (const FString& F : In.Flags)
	{
		if (!F.IsEmpty())
		{
			Flags.AddUnique(F);
		}
	}
	BroadcastChanged();
}

void UWLPlayerFlagsComponent::OnRep_Flags()
{
	OnFlagsChanged.Broadcast();
}

void UWLPlayerFlagsComponent::BroadcastChanged()
{
	if (AActor* Owner = GetOwner())
	{
		Owner->ForceNetUpdate();
	}
	OnFlagsChanged.Broadcast();
}
