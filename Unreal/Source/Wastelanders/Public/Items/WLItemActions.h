#pragma once

#include "CoreMinimal.h"
#include "Core/WLTypes.h"

class AWLPlayerState;
struct FWLItemRow;

/**
 * Server-side item verbs that span several components (inventory, equipment, skills, health,
 * combat). Ported from Assets/Scripts/Player/PlayerEntity.cs (EquipAt, Unequip, Consume, Bury,
 * DropAt) and ModuleCatalog.Socket.
 *
 * Each returns the chat line Unity printed (empty = say nothing). Every call re-validates against
 * the server's own state, so the RPC wrappers can pass client input straight in.
 * All functions require authority over the PlayerState.
 */
struct WASTELANDERS_API FWLItemActions
{
	/** Unity EAT_DELAY_TICKS: 3 ticks (1.8 s) between bites, and it pushes the next swing back. */
	static constexpr int32 EatDelayTicks = 3;

	/**
	 * PlayerEntity.EquipAt. Completes the bag transaction before touching equipment: a full bag can
	 * swap one item, but never silently deletes the off-hand a two-handed weapon pushes out. Ammo
	 * equips the whole carried quantity and merges into the same ammo already worn. On any failure
	 * the inventory is rolled back from a snapshot.
	 */
	static FText EquipAt(AWLPlayerState* PS, int32 InventoryIndex);

	/** PlayerEntity.Unequip: needs room in the bag (keeps the copy's modules). */
	static FText Unequip(AWLPlayerState* PS, EWLEquipSlot Slot);

	/** PlayerEntity.Consume (eat): 3-tick eat delay, heal, and delay the next attack. */
	static FText Consume(AWLPlayerState* PS, int32 InventoryIndex);

	/** PlayerEntity.Bury: legacy bones -> Beastmastery XP. */
	static FText Bury(AWLPlayerState* PS, int32 InventoryIndex);

	/** PlayerEntity.DropAt: drops ONE of the stack at the pawn's feet as a ground item owned by the dropper. */
	static FText DropAt(AWLPlayerState* PS, int32 InventoryIndex);

	/** ModuleCatalog.Socket: install (ModuleId > 0) or remove (0) a module in a worn item's socket. */
	static FText Socket(AWLPlayerState* PS, EWLEquipSlot Slot, int32 SocketIndex, int32 ModuleId);

	/** Readable items (journal pages): handed to the quest component, which records and shows the page. */
	static FText Read(AWLPlayerState* PS, int32 InventoryIndex);

	/** InventorySlotUI default click: equip / eat / bury / read by item type. */
	static FText UseDefault(AWLPlayerState* PS, int32 InventoryIndex);

	/** PlayerEntity.IsTwoHanded (guns are always two-handed). */
	static bool IsTwoHanded(const FWLItemRow* Item);

	/** Unity UpdateCombatStyle: tell the pawn's combat component the weapon changed. */
	static void RefreshCombatStyle(AWLPlayerState* PS);
};
