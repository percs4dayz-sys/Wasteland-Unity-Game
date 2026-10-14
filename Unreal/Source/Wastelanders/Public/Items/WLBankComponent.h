#pragma once

#include "CoreMinimal.h"
#include "Items/WLItemContainerComponent.h"
#include "Items/WLItemRules.h"
#include "WLBankComponent.generated.h"

class AActor;
class UWLInventoryComponent;
struct FWLCharacterSave;

/**
 * The player's bank: 300 always-stacking slots (Assets/Scripts/Items/Bank.cs, SIZE = 300), plus
 * the server half of UI/BankUI.cs and InventorySlotUI's deposit click.
 *
 * Banking is a session: a bank booth (AWLBankingCrate) opens it on the server, and every bank RPC
 * is refused unless the session is open, the player is alive and still standing near that booth
 * (Unity auto-closed the window when you walked away; here that is also the anti-cheat check).
 * There is no noting system: Unity has none, and the bank stacks everything anyway.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLBankComponent : public UWLItemContainerComponent
{
	GENERATED_BODY()

public:
	UWLBankComponent();

	static constexpr int32 Size = FWLItemRules::BankSize;

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Bank")
	int32 GetCapacity() const { return Size; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Bank")
	int32 FreeSlots() const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Bank")
	bool IsFull() const { return FreeSlots() == 0; }

	/** True while a bank session is open (replicated to the owner). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Bank")
	bool IsBankOpen() const { return bBankOpen; }

	// --- SERVER storage API ---------------------------------------------------------------------

	/** Bank.Deposit(ItemStack): always stacks plain copies, moduled copies take their own slot. */
	bool Deposit(const FWLItemStack& Stack);
	bool Deposit(int32 ItemId, int32 Quantity = 1) { return Deposit(FWLItemStack(ItemId, Quantity)); }

	/** Bank.Withdraw(id, qty): removes from the first slot with that item (no inventory transfer). */
	bool Withdraw(int32 ItemId, int32 Quantity = 1);

	/** Bank.WithdrawAt(index, qty) (no inventory transfer). */
	bool WithdrawAt(int32 Index, int32 Quantity = 1);

	// --- SERVER bank session ------------------------------------------------------------------

	/** Called by the booth's Interact. Opens the session and the owner's bank window. */
	void OpenBank(AActor* Booth);
	void CloseBank();

	/** Validates the open session: booth still there, owner alive and within UseRange of it. */
	bool CanUseBank() const;

	// --- transfers (SERVER; also used by the RPCs) ------------------------------------------------

	/**
	 * Deposit from an inventory slot. Quantity >= 1: up to that many units, starting with the slot
	 * and continuing through other plain slots of the same item (non-stackables are one per slot).
	 * Quantity <= 0: every unit of that item. Returns the units moved.
	 */
	int32 DepositFromInventory(int32 InventorySlot, int32 Quantity);

	/** BankUI.DepositAll: every inventory slot incl. saddlebag. Returns the stacks moved. */
	int32 DepositAllFromInventory();

	/**
	 * Withdraw to the inventory. Quantity <= 0 = all. Stackables move as one stack; non-stackables
	 * take one free slot each and stop when the bag is full. Returns the units moved.
	 */
	int32 WithdrawToInventory(int32 BankSlot, int32 Quantity);

	/** Validated deposit with chat feedback (the ServerDeposit body; the inventory "Deposit" verb uses it too). */
	void HandleDepositRequest(int32 InventorySlot, int32 Quantity);

	void CaptureSave(FWLCharacterSave& Out) const;
	void ApplySave(const FWLCharacterSave& In);

	// --- client intent ------------------------------------------------------------------------

	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Bank")
	void ServerDeposit(int32 InventorySlot, int32 Quantity);

	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Bank")
	void ServerDepositAll();

	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Bank")
	void ServerWithdraw(int32 BankSlot, int32 Quantity);

	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Bank")
	void ServerSwapBankSlots(int32 IndexA, int32 IndexB);

	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Bank")
	void ServerCloseBank();

	/** Server -> owner: show / hide the bank window. */
	UFUNCTION(Client, Reliable)
	void ClientOpenBank(AActor* Booth);

	UFUNCTION(Client, Reliable)
	void ClientCloseBank();

	FWLOnItemContainerChanged OnBankChanged;
	/** Owning client (and server): bank window should open / close. */
	FWLOnItemContainerChanged OnBankOpened;
	FWLOnItemContainerChanged OnBankClosed;

	/** Max distance (uu) between the pawn and the booth for bank operations. Unity closed at ~2 m from where you opened. */
	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Bank")
	float UseRange = 350.f;

protected:
	virtual void BroadcastChanged() override { OnBankChanged.Broadcast(); }

	UWLInventoryComponent* GetInventory() const;
	void Report(const FText& Message) const;

	UPROPERTY(Replicated)
	bool bBankOpen = false;

	TWeakObjectPtr<AActor> OpenBooth;
};
