#include "Items/WLBankComponent.h"
#include "Items/WLInventoryComponent.h"
#include "Data/WLDataSubsystem.h"
#include "Persistence/WLCharacterSave.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "GameFramework/Pawn.h"
#include "Net/UnrealNetwork.h"

#define LOCTEXT_NAMESPACE "WLBank"

UWLBankComponent::UWLBankComponent()
{
	InitSlots(Size);
}

void UWLBankComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME_CONDITION(UWLBankComponent, bBankOpen, COND_OwnerOnly);
}

int32 UWLBankComponent::FreeSlots() const
{
	return FWLItemRules::FreeSlots(Slots, Size);
}

bool UWLBankComponent::Deposit(const FWLItemStack& Stack)
{
	if (!IsServer())
	{
		return false;
	}
	const bool bOk = FWLItemRules::BankDeposit(Slots, Stack, [this](int32 Id) { return FindItemRow(Id); });
	if (bOk)
	{
		Commit();
	}
	return bOk;
}

bool UWLBankComponent::Withdraw(int32 ItemId, int32 Quantity)
{
	if (!IsServer())
	{
		return false;
	}
	const bool bOk = FWLItemRules::BankWithdraw(Slots, ItemId, Quantity);
	if (bOk)
	{
		Commit();
	}
	return bOk;
}

bool UWLBankComponent::WithdrawAt(int32 Index, int32 Quantity)
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

UWLInventoryComponent* UWLBankComponent::GetInventory() const
{
	const AWLPlayerState* PS = GetOwningPlayerState();
	return PS ? PS->GetInventory() : nullptr;
}

void UWLBankComponent::Report(const FText& Message) const
{
	if (!Message.IsEmpty())
	{
		if (AWLPlayerController* PC = GetOwningController())
		{
			PC->ClientGameMessage(Message);
		}
	}
}

// --- session ---------------------------------------------------------------------------------

void UWLBankComponent::OpenBank(AActor* Booth)
{
	if (!IsServer() || !Booth)
	{
		return;
	}
	OpenBooth = Booth;
	bBankOpen = true;
	OnBankOpened.Broadcast();
	ClientOpenBank(Booth);
}

void UWLBankComponent::CloseBank()
{
	if (!IsServer() || !bBankOpen)
	{
		return;
	}
	OpenBooth.Reset();
	bBankOpen = false;
	OnBankClosed.Broadcast();
	ClientCloseBank();
}

bool UWLBankComponent::CanUseBank() const
{
	if (!bBankOpen)
	{
		return false;
	}
	const AActor* Booth = OpenBooth.Get();
	const AWLPlayerState* PS = GetOwningPlayerState();
	const APawn* Pawn = PS ? PS->GetPawn() : nullptr;
	if (!Booth || !Pawn)
	{
		return false;
	}
	if (const UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem())
	{
		if (ASC->IsDead())
		{
			return false;
		}
	}
	return FVector::DistSquared(Pawn->GetActorLocation(), Booth->GetActorLocation()) <= FMath::Square(UseRange);
}

void UWLBankComponent::ClientOpenBank_Implementation(AActor* Booth)
{
	if (!IsServer())
	{
		OnBankOpened.Broadcast();
	}
}

void UWLBankComponent::ClientCloseBank_Implementation()
{
	if (!IsServer())
	{
		OnBankClosed.Broadcast();
	}
}

// --- transfers -------------------------------------------------------------------------------

int32 UWLBankComponent::DepositFromInventory(int32 InventorySlot, int32 Quantity)
{
	UWLInventoryComponent* Inv = GetInventory();
	if (!IsServer() || !Inv)
	{
		return 0;
	}
	const FWLItemStack First = Inv->GetSlot(InventorySlot);
	if (First.IsEmpty())
	{
		return 0;
	}
	const int32 Wanted = Quantity <= 0 ? TNumericLimits<int32>::Max() : Quantity;
	int32 Moved = 0;

	// Move one inventory slot (or part of it) into the bank. Deposit first, then remove, so a full
	// bank never loses the item.
	auto MoveFrom = [&](int32 Index)
	{
		const FWLItemStack S = Inv->GetSlot(Index);
		const int32 Take = FMath::Min(S.Quantity, Wanted - Moved);
		if (Take <= 0 || !Deposit(S.CopyWithQuantity(Take)))
		{
			return false;
		}
		Inv->RemoveAt(Index, Take);
		Moved += Take;
		return true;
	};

	if (!MoveFrom(InventorySlot))
	{
		return 0;
	}
	// "Deposit X" of a non-stackable continues through the other plain copies (one per slot).
	for (int32 i = 0; i < Inv->NumSlots() && Moved < Wanted; ++i)
	{
		const FWLItemStack& S = Inv->GetSlot(i);
		if (i != InventorySlot && !S.IsEmpty() && S.ItemId == First.ItemId && !S.HasModules())
		{
			if (!MoveFrom(i))
			{
				break;
			}
		}
	}
	return Moved;
}

int32 UWLBankComponent::DepositAllFromInventory()
{
	UWLInventoryComponent* Inv = GetInventory();
	if (!IsServer() || !Inv)
	{
		return 0;
	}
	int32 Stacks = 0;
	for (int32 i = 0; i < Inv->NumSlots(); ++i) // includes saddlebag slots
	{
		const FWLItemStack S = Inv->GetSlot(i);
		if (!S.IsEmpty() && Deposit(S))
		{
			Inv->RemoveAt(i, S.Quantity);
			++Stacks;
		}
	}
	return Stacks;
}

int32 UWLBankComponent::WithdrawToInventory(int32 BankSlot, int32 Quantity)
{
	UWLInventoryComponent* Inv = GetInventory();
	if (!IsServer() || !Inv || GetSlot(BankSlot).IsEmpty())
	{
		return 0;
	}
	const FWLItemStack S = GetSlot(BankSlot);
	const FWLItemRow* Row = FindItemRow(S.ItemId);
	const int32 Wanted = Quantity <= 0 ? S.Quantity : FMath::Min(Quantity, S.Quantity);

	if (Row && Row->bStackable && !S.HasModules())
	{
		// One stack: add then remove (Unity order: the bag must accept it first).
		if (Inv->AddStack(S.CopyWithQuantity(Wanted)))
		{
			WithdrawAt(BankSlot, Wanted);
			return Wanted;
		}
		return 0;
	}

	// Non-stackable (or moduled): one slot per unit, stop at a full bag.
	int32 Moved = 0;
	while (Moved < Wanted && Inv->AddStack(S.CopyWithQuantity(1)))
	{
		WithdrawAt(BankSlot, 1);
		++Moved;
	}
	return Moved;
}

void UWLBankComponent::HandleDepositRequest(int32 InventorySlot, int32 Quantity)
{
	if (!CanUseBank())
	{
		return;
	}
	const UWLInventoryComponent* Inv = GetInventory();
	const int32 ItemId = Inv ? Inv->GetSlot(InventorySlot).ItemId : 0;
	if (ItemId <= 0)
	{
		return;
	}
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	const FText Name = Data ? Data->GetItemName(ItemId) : FText::GetEmpty();
	if (DepositFromInventory(InventorySlot, Quantity) > 0)
	{
		Report(FText::Format(LOCTEXT("Deposited", "[BANK]: Deposited {0}."), Name));
	}
	else
	{
		Report(LOCTEXT("BankFull", "[BANK]: Bank full."));
	}
}

// --- RPCs ------------------------------------------------------------------------------------

void UWLBankComponent::ServerDeposit_Implementation(int32 InventorySlot, int32 Quantity)
{
	HandleDepositRequest(InventorySlot, Quantity);
}

void UWLBankComponent::ServerDepositAll_Implementation()
{
	if (!CanUseBank())
	{
		return;
	}
	DepositAllFromInventory();
	Report(LOCTEXT("DepositedAll", "[BANK]: Deposited everything."));
}

void UWLBankComponent::ServerWithdraw_Implementation(int32 BankSlot, int32 Quantity)
{
	if (!CanUseBank() || GetSlot(BankSlot).IsEmpty())
	{
		return;
	}
	const int32 ItemId = GetSlot(BankSlot).ItemId;
	if (WithdrawToInventory(BankSlot, Quantity) > 0)
	{
		const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
		Report(FText::Format(LOCTEXT("Withdrew", "[BANK]: Withdrew {0}."), Data ? Data->GetItemName(ItemId) : FText::GetEmpty()));
	}
	else
	{
		Report(LOCTEXT("InvFull", "[BANK]: Inventory full."));
	}
}

void UWLBankComponent::ServerSwapBankSlots_Implementation(int32 IndexA, int32 IndexB)
{
	if (CanUseBank() && FWLItemRules::SwapSlots(Slots, IndexA, IndexB))
	{
		Commit();
	}
}

void UWLBankComponent::ServerCloseBank_Implementation()
{
	CloseBank();
}

// --- persistence -----------------------------------------------------------------------------

void UWLBankComponent::CaptureSave(FWLCharacterSave& Out) const
{
	Out.Bank.Reset(Slots.Num());
	for (const FWLItemStack& S : Slots)
	{
		FWLSaveSlot& Entry = Out.Bank.AddDefaulted_GetRef();
		if (!S.IsEmpty())
		{
			Entry.ItemId = S.ItemId;
			Entry.Quantity = S.Quantity;
			Entry.Modules = S.Modules;
		}
	}
}

void UWLBankComponent::ApplySave(const FWLCharacterSave& In)
{
	// Slot-exact for the first 300 entries (keeps a rearranged bank as the player left it). Items
	// no longer in the game are dropped, as in Unity's SaveData.ApplyTo. Anything past slot 300
	// (hand-edited or imported saves) is deposited normally so it is never lost while room remains.
	for (FWLItemStack& S : Slots)
	{
		S.Reset();
	}
	auto IsValid = [this](const FWLSaveSlot& Saved) { return Saved.ItemId > 0 && Saved.Quantity > 0 && FindItemRow(Saved.ItemId) != nullptr; };
	for (int32 i = 0; i < In.Bank.Num() && i < Slots.Num(); ++i)
	{
		if (IsValid(In.Bank[i]))
		{
			Slots[i] = FWLItemStack(In.Bank[i].ItemId, In.Bank[i].Quantity, In.Bank[i].Modules);
		}
	}
	for (int32 i = Slots.Num(); i < In.Bank.Num(); ++i)
	{
		if (IsValid(In.Bank[i]))
		{
			FWLItemRules::BankDeposit(Slots, FWLItemStack(In.Bank[i].ItemId, In.Bank[i].Quantity, In.Bank[i].Modules), [this](int32 Id) { return FindItemRow(Id); });
		}
	}
	Commit();
}

#undef LOCTEXT_NAMESPACE
