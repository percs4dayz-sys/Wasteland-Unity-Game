#include "Items/WLInventoryComponent.h"
#include "Items/WLItemActions.h"
#include "Items/WLBankComponent.h"
#include "Core/WLGameEventsSubsystem.h"
#include "Data/WLDataTypes.h"
#include "Persistence/WLCharacterSave.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Net/UnrealNetwork.h"

#define LOCTEXT_NAMESPACE "WLInventory"

const FName UWLInventoryComponent::VerbEquip(TEXT("Equip"));
const FName UWLInventoryComponent::VerbUse(TEXT("Use"));
const FName UWLInventoryComponent::VerbEat(TEXT("Eat"));
const FName UWLInventoryComponent::VerbBury(TEXT("Bury"));
const FName UWLInventoryComponent::VerbRead(TEXT("Read"));
const FName UWLInventoryComponent::VerbDrop(TEXT("Drop"));
const FName UWLInventoryComponent::VerbDeposit(TEXT("Deposit"));

UWLInventoryComponent::UWLInventoryComponent()
{
	InitSlots(MaxSlots);
}

void UWLInventoryComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME_CONDITION(UWLInventoryComponent, BonusSlots, COND_OwnerOnly);
}

int32 UWLInventoryComponent::FreeSlots() const
{
	return FWLItemRules::FreeSlots(Slots, GetCapacity());
}

bool UWLInventoryComponent::AddItem(int32 ItemId, int32 Quantity)
{
	if (!IsServer())
	{
		return false;
	}
	const bool bOk = FWLItemRules::InventoryAdd(Slots, GetCapacity(), ItemId, Quantity, [this](int32 Id) { return FindItemRow(Id); });
	if (bOk)
	{
		Commit();
	}
	return bOk;
}

bool UWLInventoryComponent::AddStack(const FWLItemStack& Stack)
{
	if (!IsServer())
	{
		return false;
	}
	const bool bOk = FWLItemRules::InventoryAddStack(Slots, GetCapacity(), Stack, [this](int32 Id) { return FindItemRow(Id); });
	if (bOk)
	{
		Commit();
	}
	return bOk;
}

bool UWLInventoryComponent::GiveItem(const FWLItemStack& Stack, FName Source)
{
	if (!AddStack(Stack))
	{
		return false;
	}
	if (UWLGameEventsSubsystem* Events = UWLGameEventsSubsystem::Get(this))
	{
		Events->OnItemGained.Broadcast(GetOwningPlayerState(), Stack.ItemId, Stack.Quantity, Source);
	}
	return true;
}

bool UWLInventoryComponent::RemoveItem(int32 ItemId, int32 Quantity)
{
	if (!IsServer())
	{
		return false;
	}
	const bool bOk = FWLItemRules::InventoryRemove(Slots, ItemId, Quantity);
	if (bOk)
	{
		Commit();
	}
	return bOk;
}

bool UWLInventoryComponent::RemoveAt(int32 Index, int32 Quantity)
{
	if (!IsServer())
	{
		return false;
	}
	const bool bOk = FWLItemRules::RemoveAt(Slots, Index, Quantity);
	if (bOk)
	{
		Commit();
	}
	return bOk;
}

bool UWLInventoryComponent::Swap(int32 IndexA, int32 IndexB)
{
	if (!IsServer())
	{
		return false;
	}
	const bool bOk = FWLItemRules::InventorySwap(Slots, GetCapacity(), IndexA, IndexB);
	if (bOk)
	{
		Commit();
	}
	return bOk;
}

void UWLInventoryComponent::SetBonusSlots(int32 N)
{
	if (!IsServer())
	{
		return;
	}
	const int32 Clamped = FMath::Clamp(N, 0, MaxSlots - BaseSize);
	if (Clamped == BonusSlots)
	{
		return;
	}
	BonusSlots = Clamped;
	OnInventoryChanged.Broadcast();
}

void UWLInventoryComponent::OnRep_BonusSlots()
{
	OnInventoryChanged.Broadcast();
}

void UWLInventoryComponent::SetSlotRaw(int32 Index, const FWLItemStack& Stack)
{
	if (!IsServer() || !Slots.IsValidIndex(Index))
	{
		return;
	}
	Slots[Index] = Stack;
	Commit();
}

void UWLInventoryComponent::CaptureSave(FWLCharacterSave& Out) const
{
	Out.Inventory.Reset(Slots.Num());
	for (const FWLItemStack& S : Slots)
	{
		FWLSaveSlot& Entry = Out.Inventory.AddDefaulted_GetRef();
		if (!S.IsEmpty())
		{
			Entry.ItemId = S.ItemId;
			Entry.Quantity = S.Quantity;
			Entry.Modules = S.Modules;
		}
	}
}

void UWLInventoryComponent::ApplySave(const FWLCharacterSave& In)
{
	// Slot-exact: keeps the player's arrangement, and items saved in saddlebag slots survive even
	// though the companion isn't summoned at load. Items removed from the game are dropped rather
	// than left as broken slots.
	for (int32 i = 0; i < Slots.Num(); ++i)
	{
		Slots[i].Reset();
		if (!In.Inventory.IsValidIndex(i))
		{
			continue;
		}
		const FWLSaveSlot& Saved = In.Inventory[i];
		if (Saved.ItemId > 0 && Saved.Quantity > 0 && FindItemRow(Saved.ItemId))
		{
			Slots[i] = FWLItemStack(Saved.ItemId, Saved.Quantity, Saved.Modules);
		}
	}
	Commit();
}

void UWLInventoryComponent::Report(const FText& Message) const
{
	if (Message.IsEmpty())
	{
		return;
	}
	if (AWLPlayerController* PC = GetOwningController())
	{
		PC->ClientGameMessage(Message);
	}
}

void UWLInventoryComponent::ServerInventoryAction_Implementation(int32 SlotIndex, FName Verb)
{
	AWLPlayerState* PS = GetOwningPlayerState();
	if (!PS || !Slots.IsValidIndex(SlotIndex))
	{
		return;
	}
	if (Verb == VerbEquip)
	{
		Report(FWLItemActions::EquipAt(PS, SlotIndex));
	}
	else if (Verb == VerbUse || Verb == VerbEat)
	{
		Report(FWLItemActions::UseDefault(PS, SlotIndex));
	}
	else if (Verb == VerbBury)
	{
		Report(FWLItemActions::Bury(PS, SlotIndex));
	}
	else if (Verb == VerbRead)
	{
		Report(FWLItemActions::Read(PS, SlotIndex));
	}
	else if (Verb == VerbDrop)
	{
		Report(FWLItemActions::DropAt(PS, SlotIndex));
	}
	else if (Verb == VerbDeposit)
	{
		if (UWLBankComponent* Bank = PS->GetBank())
		{
			Bank->HandleDepositRequest(SlotIndex, GetSlot(SlotIndex).Quantity);
		}
	}
}

void UWLInventoryComponent::ServerEquipFromSlot_Implementation(int32 SlotIndex)
{
	if (AWLPlayerState* PS = GetOwningPlayerState())
	{
		Report(FWLItemActions::EquipAt(PS, SlotIndex));
	}
}

void UWLInventoryComponent::ServerUseItem_Implementation(int32 SlotIndex)
{
	if (AWLPlayerState* PS = GetOwningPlayerState())
	{
		Report(FWLItemActions::UseDefault(PS, SlotIndex));
	}
}

void UWLInventoryComponent::ServerDropSlot_Implementation(int32 SlotIndex)
{
	if (AWLPlayerState* PS = GetOwningPlayerState())
	{
		Report(FWLItemActions::DropAt(PS, SlotIndex));
	}
}

void UWLInventoryComponent::ServerSwapSlots_Implementation(int32 IndexA, int32 IndexB)
{
	Swap(IndexA, IndexB); // FWLItemRules validates indices and the locked-slot rule
}

void UWLInventoryComponent::ServerMoveSlot_Implementation(int32 From, int32 To)
{
	Swap(From, To);
}

#undef LOCTEXT_NAMESPACE
