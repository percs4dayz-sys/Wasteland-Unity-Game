#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Net/Serialization/FastArraySerializer.h"
#include "Items/WLItemTypes.h"
#include "WLItemContainerComponent.generated.h"

class UWLItemContainerComponent;
class AWLPlayerState;
class AWLPlayerController;
struct FWLItemRow;

DECLARE_MULTICAST_DELEGATE(FWLOnItemContainerChanged);

/** One replicated slot. SlotIndex is the stable slot number (fast-array order is not guaranteed on clients). */
USTRUCT()
struct WASTELANDERS_API FWLItemSlotEntry : public FFastArraySerializerItem
{
	GENERATED_BODY()

	UPROPERTY()
	int32 SlotIndex = INDEX_NONE;

	UPROPERTY()
	FWLItemStack Stack;
};

/**
 * Delta-replicated slot grid. The server keeps exactly one entry per slot (never removes or
 * reorders), so a change to one slot sends only that slot.
 */
USTRUCT()
struct WASTELANDERS_API FWLItemSlotArray : public FFastArraySerializer
{
	GENERATED_BODY()

	UPROPERTY()
	TArray<FWLItemSlotEntry> Entries;

	/** Not replicated: the component to notify after a replication bunch lands on a client. */
	UWLItemContainerComponent* OwnerComponent = nullptr;

	void PostReplicatedReceive(const FFastArraySerializer::FPostReplicatedReceiveParameters& Parameters);

	bool NetDeltaSerialize(FNetDeltaSerializeInfo& DeltaParms)
	{
		return FFastArraySerializer::FastArrayDeltaSerialize<FWLItemSlotEntry, FWLItemSlotArray>(Entries, DeltaParms, *this);
	}
};

template <>
struct TStructOpsTypeTraits<FWLItemSlotArray> : public TStructOpsTypeTraitsBase2<FWLItemSlotArray>
{
	enum { WithNetDeltaSerializer = true };
};

/**
 * Shared base for the inventory and bank: a fixed number of stable slots (slot-exact like Unity's
 * ItemStack[] arrays), replicated to the owning client only.
 *
 * Server pattern: mutate Slots through FWLItemRules, then Commit(), which diffs Slots against the
 * replicated entries, marks only changed slots dirty and fires the change delegate. Clients rebuild
 * Slots from the replicated entries and fire the same delegate, so UI binds once for both.
 */
UCLASS(Abstract)
class WASTELANDERS_API UWLItemContainerComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLItemContainerComponent();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

	/** The slot (an empty stack, ItemId 0, when out of range or empty). Never a pointer. */
	const FWLItemStack& GetSlot(int32 Index) const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Items")
	FWLItemStack GetSlotStack(int32 Index) const { return GetSlot(Index); }

	/** Number of slot entries (inventory: 34 incl. saddlebag, bank: 300). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Items")
	int32 NumSlots() const { return Slots.Num(); }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Items")
	int32 CountOf(int32 ItemId) const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Items")
	bool Contains(int32 ItemId, int32 Quantity = 1) const { return CountOf(ItemId) >= Quantity; }

	/** Total quantity of everything held (Inventory.CountOfAny). */
	int32 CountOfAny() const;

	/** First slot holding the item, or INDEX_NONE. */
	int32 FindFirstSlot(int32 ItemId) const;

	/** Copy of every slot, for all-or-nothing transactions (Unity Snapshot/Restore). */
	TArray<FWLItemStack> Snapshot() const { return Slots; }

	/** SERVER. Roll back to a snapshot taken earlier in the same transaction. */
	void Restore(const TArray<FWLItemStack>& InSnapshot);

	/** SERVER. Empty every slot. */
	void Clear();

	/** The read-only slot array (UI convenience). */
	const TArray<FWLItemStack>& GetSlots() const { return Slots; }

	/** Called by FWLItemSlotArray on clients. */
	void HandleSlotsReplicated();

protected:
	/** Subclasses fire their contract-named delegate (OnInventoryChanged / OnBankChanged). */
	virtual void BroadcastChanged() {}

	/** Sets the fixed slot count (constructor only). */
	void InitSlots(int32 Count);

	/** SERVER. Push changed slots into the replicated array and notify. */
	void Commit();

	bool IsServer() const;
	AWLPlayerState* GetOwningPlayerState() const;
	AWLPlayerController* GetOwningController() const;

	/** Item lookup for FWLItemRules (null if the data subsystem is not up). */
	const FWLItemRow* FindItemRow(int32 ItemId) const;

	/** Authoritative on the server; a mirror of ReplicatedSlots on clients. */
	TArray<FWLItemStack> Slots;

	UPROPERTY(Replicated)
	FWLItemSlotArray ReplicatedSlots;
};
