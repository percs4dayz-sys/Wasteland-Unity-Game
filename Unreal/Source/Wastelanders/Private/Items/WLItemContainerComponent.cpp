#include "Items/WLItemContainerComponent.h"
#include "Items/WLItemRules.h"
#include "Data/WLDataSubsystem.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Net/UnrealNetwork.h"

void FWLItemSlotArray::PostReplicatedReceive(const FFastArraySerializer::FPostReplicatedReceiveParameters& Parameters)
{
	if (OwnerComponent)
	{
		OwnerComponent->HandleSlotsReplicated();
	}
}

UWLItemContainerComponent::UWLItemContainerComponent()
{
	PrimaryComponentTick.bCanEverTick = false;
	SetIsReplicatedByDefault(true);
	ReplicatedSlots.OwnerComponent = this;
}

void UWLItemContainerComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	// Only the owner needs to see their own bag and bank (MMO bandwidth + privacy).
	DOREPLIFETIME_CONDITION(UWLItemContainerComponent, ReplicatedSlots, COND_OwnerOnly);
}

void UWLItemContainerComponent::InitSlots(int32 Count)
{
	Slots.SetNum(Count);
}

const FWLItemStack& UWLItemContainerComponent::GetSlot(int32 Index) const
{
	static const FWLItemStack Empty;
	return Slots.IsValidIndex(Index) ? Slots[Index] : Empty;
}

int32 UWLItemContainerComponent::CountOf(int32 ItemId) const
{
	return FWLItemRules::CountOf(Slots, ItemId);
}

int32 UWLItemContainerComponent::CountOfAny() const
{
	int32 Total = 0;
	for (const FWLItemStack& S : Slots)
	{
		if (!S.IsEmpty())
		{
			Total += S.Quantity;
		}
	}
	return Total;
}

int32 UWLItemContainerComponent::FindFirstSlot(int32 ItemId) const
{
	return FWLItemRules::FindFirst(Slots, ItemId);
}

void UWLItemContainerComponent::Restore(const TArray<FWLItemStack>& InSnapshot)
{
	for (int32 i = 0; i < Slots.Num(); ++i)
	{
		Slots[i] = InSnapshot.IsValidIndex(i) ? InSnapshot[i] : FWLItemStack();
	}
	Commit();
}

void UWLItemContainerComponent::Clear()
{
	for (FWLItemStack& S : Slots)
	{
		S.Reset();
	}
	Commit();
}

void UWLItemContainerComponent::Commit()
{
	// The owner pointer is not a UPROPERTY, so re-point it in case the struct was copied.
	ReplicatedSlots.OwnerComponent = this;

	bool bChanged = false;
	if (ReplicatedSlots.Entries.Num() != Slots.Num())
	{
		ReplicatedSlots.Entries.SetNum(Slots.Num());
		for (int32 i = 0; i < Slots.Num(); ++i)
		{
			FWLItemSlotEntry& Entry = ReplicatedSlots.Entries[i];
			Entry.SlotIndex = i;
			Entry.Stack = Slots[i];
			ReplicatedSlots.MarkItemDirty(Entry);
		}
		ReplicatedSlots.MarkArrayDirty();
		bChanged = true;
	}
	else
	{
		for (int32 i = 0; i < Slots.Num(); ++i)
		{
			FWLItemSlotEntry& Entry = ReplicatedSlots.Entries[i];
			// Normalise empties so "0 x item 5" and "0 x item 0" never differ.
			FWLItemStack Normalised = Slots[i];
			if (Normalised.IsEmpty())
			{
				Normalised.Reset();
				Slots[i].Reset();
			}
			if (Entry.Stack != Normalised)
			{
				Entry.Stack = Normalised;
				ReplicatedSlots.MarkItemDirty(Entry);
				bChanged = true;
			}
		}
	}

	if (bChanged)
	{
		BroadcastChanged();
	}
}

void UWLItemContainerComponent::HandleSlotsReplicated()
{
	for (FWLItemStack& S : Slots)
	{
		S.Reset();
	}
	for (const FWLItemSlotEntry& Entry : ReplicatedSlots.Entries)
	{
		if (Slots.IsValidIndex(Entry.SlotIndex))
		{
			Slots[Entry.SlotIndex] = Entry.Stack;
		}
	}
	BroadcastChanged();
}

bool UWLItemContainerComponent::IsServer() const
{
	const AActor* Owner = GetOwner();
	return Owner && Owner->HasAuthority();
}

AWLPlayerState* UWLItemContainerComponent::GetOwningPlayerState() const
{
	return Cast<AWLPlayerState>(GetOwner());
}

AWLPlayerController* UWLItemContainerComponent::GetOwningController() const
{
	const AWLPlayerState* PS = GetOwningPlayerState();
	return PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr;
}

const FWLItemRow* UWLItemContainerComponent::FindItemRow(int32 ItemId) const
{
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	return Data ? Data->FindItem(ItemId) : nullptr;
}
