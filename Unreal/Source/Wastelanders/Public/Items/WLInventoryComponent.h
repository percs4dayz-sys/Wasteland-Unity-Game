#pragma once

#include "CoreMinimal.h"
#include "Items/WLItemContainerComponent.h"
#include "Items/WLItemRules.h"
#include "WLInventoryComponent.generated.h"

struct FWLCharacterSave;

/**
 * The player's bag: 28 slots plus up to 6 saddlebag slots granted by a companion. Ported from
 * Assets/Scripts/Items/Inventory.cs; the item verbs (equip, eat, bury, drop) come from
 * Player/PlayerEntity.cs and InventorySlotUI.cs and are implemented in FWLItemActions.
 *
 * Lives on AWLPlayerState, replicated to the owner only. Clients never mutate it: the UI calls the
 * Server RPCs below, which re-validate everything and report back via ClientGameMessage.
 *
 * Saddlebag rule (Unity BonusSlots): shrinking the bonus never deletes items. Items left in slots
 * past the capacity stay readable, countable and removable; they just refuse NEW items.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLInventoryComponent : public UWLItemContainerComponent
{
	GENERATED_BODY()

public:
	UWLInventoryComponent();

	static constexpr int32 BaseSize = FWLItemRules::InventoryBaseSize;
	static constexpr int32 MaxSlots = FWLItemRules::InventoryMaxSize;

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

	// --- queries (server and owning client) ---------------------------------------------------

	/** Slots that accept new items: 28 + saddlebag bonus. */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Inventory")
	int32 GetCapacity() const { return BaseSize + BonusSlots; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Inventory")
	int32 GetBonusSlots() const { return BonusSlots; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Inventory")
	int32 FreeSlots() const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Inventory")
	bool IsFull() const { return FreeSlots() == 0; }

	/** True if this slot can hold items right now (within capacity, or a locked slot still holding something). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Inventory")
	bool IsSlotUsable(int32 Index) const { return Index >= 0 && Index < MaxSlots && (Index < GetCapacity() || !GetSlot(Index).IsEmpty()); }

	// --- SERVER mutation API (other systems call these) --------------------------------------

	/** Inventory.Add(id, qty). Non-stackables: one per slot, all or nothing. No event broadcast. */
	bool AddItem(int32 ItemId, int32 Quantity = 1);

	/** Inventory.Add(ItemStack): keeps the stack's modules (moduled stacks take their own slot). */
	bool AddStack(const FWLItemStack& Stack);

	/**
	 * AddStack + UWLGameEventsSubsystem::OnItemGained(PS, Id, Qty, Source). Use this for real
	 * gains (loot pickup, gathering, crafting, quest rewards) so tasks and quests can count them.
	 */
	bool GiveItem(const FWLItemStack& Stack, FName Source);

	/** Inventory.Remove(id, qty): across slots, never half-removes. */
	bool RemoveItem(int32 ItemId, int32 Quantity = 1);

	/** Inventory.RemoveAt(index, qty). */
	bool RemoveAt(int32 Index, int32 Quantity = 1);

	/** Inventory.Swap with the locked-slot rule. */
	bool Swap(int32 IndexA, int32 IndexB);

	/** Companion saddlebag: 0-6 extra slots. Never deletes items when shrinking. */
	void SetBonusSlots(int32 N);

	/** Raw slot placement for save loading: bypasses capacity so saddlebag items survive a load without the beast. */
	void SetSlotRaw(int32 Index, const FWLItemStack& Stack);

	/** SERVER. Eat-delay bookkeeping for FWLItemActions::Consume (Unity PlayerEntity._nextEatTick). */
	int64 GetNextEatTick() const { return NextEatTick; }
	void SetNextEatTick(int64 Tick) { NextEatTick = Tick; }

	// --- persistence ---------------------------------------------------------------------------

	/** Every slot (incl. saddlebag), slot-exact, empties as ItemId 0. */
	void CaptureSave(FWLCharacterSave& Out) const;

	/** Slot-exact restore; items no longer in the item table are dropped (Unity SaveData.ApplyTo). */
	void ApplySave(const FWLCharacterSave& In);

	// --- client intent (Server RPCs, owning client only) --------------------------------------

	/**
	 * Generic slot verb for the inventory UI / context menu. Verbs: Equip, Use, Eat, Bury, Read,
	 * Drop, Deposit (bank must be open; deposits the whole slot). "Use" runs the item's default
	 * action exactly like a left click (InventorySlotUI.OnPointerClick).
	 */
	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Inventory")
	void ServerInventoryAction(int32 SlotIndex, FName Verb);

	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Inventory")
	void ServerEquipFromSlot(int32 SlotIndex);

	/** Default action of the item in the slot: equip / eat / bury / read. */
	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Inventory")
	void ServerUseItem(int32 SlotIndex);

	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Inventory")
	void ServerDropSlot(int32 SlotIndex);

	/** Drag-and-drop rearrange. */
	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Inventory")
	void ServerSwapSlots(int32 IndexA, int32 IndexB);

	/** Alias of ServerSwapSlots (UI naming). */
	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Inventory")
	void ServerMoveSlot(int32 From, int32 To);

	/** Fires on the server after every change and on the owning client after replication. */
	FWLOnItemContainerChanged OnInventoryChanged;

	/** Verb names accepted by ServerInventoryAction. */
	static const FName VerbEquip;
	static const FName VerbUse;
	static const FName VerbEat;
	static const FName VerbBury;
	static const FName VerbRead;
	static const FName VerbDrop;
	static const FName VerbDeposit;

protected:
	virtual void BroadcastChanged() override { OnInventoryChanged.Broadcast(); }

	UFUNCTION()
	void OnRep_BonusSlots();

	/** Sends a result line to the owner (empty text = silent, e.g. eat-delay clicks). */
	void Report(const FText& Message) const;

	/** Saddlebag slots currently granted (0-6). */
	UPROPERTY(ReplicatedUsing = OnRep_BonusSlots)
	int32 BonusSlots = 0;

	int64 NextEatTick = 0;
};
