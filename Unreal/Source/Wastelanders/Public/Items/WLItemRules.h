#pragma once

#include "CoreMinimal.h"
#include "Core/WLTypes.h"
#include "Items/WLItemTypes.h"

struct FWLItemRow;
struct FWLModuleRow;
struct FWLGearSetRow;

/**
 * Pure slot / module / gear-set rules shared by the inventory, bank and equipment components.
 * Kept free of UObjects (item rows come in through a lookup function) so the automation tests can
 * exercise the exact production logic without a world or DataTables.
 *
 * Every function here is a 1:1 port of a Unity method; the source is cited on each.
 */
struct WASTELANDERS_API FWLItemRules
{
	using FItemLookup = TFunctionRef<const FWLItemRow*(int32)>;

	/** Inventory.SIZE: the base OSRS-style grid. */
	static constexpr int32 InventoryBaseSize = 28;
	/** Inventory.MAX: base + the biggest companion saddlebag (6). */
	static constexpr int32 InventoryMaxSize = 34;
	/** Bank.SIZE. */
	static constexpr int32 BankSize = 300;

	// --- shared slot queries ----------------------------------------------------------------

	/** Empty slots among the first Capacity slots (Inventory.FreeSlots). */
	static int32 FreeSlots(const TArray<FWLItemStack>& Slots, int32 Capacity);

	/** Total quantity of an item across ALL slots, including locked saddlebag overflow. */
	static int32 CountOf(const TArray<FWLItemStack>& Slots, int32 ItemId);

	/** First slot holding the item, or INDEX_NONE. */
	static int32 FindFirst(const TArray<FWLItemStack>& Slots, int32 ItemId);

	// --- inventory (Inventory.cs) -----------------------------------------------------------

	/**
	 * Inventory.Add(id, qty). Stackables merge into an existing stack anywhere (overflow slots
	 * included), else take one free slot; non-stackables need qty free slots or nothing is added.
	 */
	static bool InventoryAdd(TArray<FWLItemStack>& Slots, int32 Capacity, int32 ItemId, int32 Quantity, FItemLookup Lookup);

	/** Inventory.Add(ItemStack). A stack with modules goes into its own free slot, quantity 1 only. */
	static bool InventoryAddStack(TArray<FWLItemStack>& Slots, int32 Capacity, const FWLItemStack& Stack, FItemLookup Lookup);

	/**
	 * Inventory.Remove(id, qty): removes ACROSS slots (non-stackables are one per slot). Checks the
	 * total first so it never half-removes (the old furnace dupe: a single-slot check failed but
	 * crafting still ran).
	 */
	static bool InventoryRemove(TArray<FWLItemStack>& Slots, int32 ItemId, int32 Quantity);

	/** Inventory.RemoveAt / Bank.WithdrawAt. */
	static bool RemoveAt(TArray<FWLItemStack>& Slots, int32 Index, int32 Quantity);

	/**
	 * Inventory.Swap. Locked (beyond-capacity) slots may give items up but never receive them, so
	 * both slots must be within capacity or currently occupied.
	 */
	static bool InventorySwap(TArray<FWLItemStack>& Slots, int32 Capacity, int32 IndexA, int32 IndexB);

	// --- bank (Bank.cs) -----------------------------------------------------------------------

	/** Bank.Deposit(ItemStack): the bank ALWAYS stacks plain copies (OSRS), moduled copies keep their own slot. */
	static bool BankDeposit(TArray<FWLItemStack>& Slots, const FWLItemStack& Stack, FItemLookup Lookup);

	/** Bank.Withdraw(id, qty): from the FIRST slot holding the item; fails if that slot has too few. */
	static bool BankWithdraw(TArray<FWLItemStack>& Slots, int32 ItemId, int32 Quantity);

	/** Plain slot swap (bank rearranging). Both indices must be valid. */
	static bool SwapSlots(TArray<FWLItemStack>& Slots, int32 IndexA, int32 IndexB);
};

/**
 * Module rules. Ported from ModuleCatalog.cs (named modules, ids 600-669, even = Common, odd =
 * Refined) and TierModules.cs (legacy generic modules 500-517, still usable; Perfect retired).
 * Catalogue data itself lives in DT_Modules; the module stat bonuses live on the module's item row.
 */
struct WASTELANDERS_API FWLModuleRules
{
	enum class ELegacyQuality : uint8 { Common = 0, Refined = 1, Perfect = 2 };

	static constexpr int32 LegacyFirstId = 500;
	static constexpr int32 CatalogFirstId = 600;
	static constexpr int32 CatalogEndId = 670;

	/** Legacy 500-series module (Perfect ids are never registered, so (id-500)%3 must be 0 or 1). */
	static bool IsLegacyModule(int32 ItemId) { return ItemId >= 500 && ItemId <= 516 && (ItemId - 500) % 3 < 2; }

	/** ModuleCatalog.IsModule. CatalogRow = UWLDataSubsystem::FindModule(ItemId). */
	static bool IsModule(int32 ItemId, const FWLModuleRow* CatalogRow) { return CatalogRow != nullptr || IsLegacyModule(ItemId); }

	/** ModuleCatalog.Grade: 1 = Common, 2 = Refined (legacy: quality + 1). */
	static int32 Grade(int32 ItemId, const FWLModuleRow* CatalogRow);

	/** ModuleCatalog.Fits: named modules fit only their slot, legacy modules fit anything socketable. */
	static bool Fits(int32 ItemId, const FWLModuleRow* CatalogRow, EWLEquipSlot Slot);

	/** ModuleCatalog.IdFor(tier 1-5, variant 0-6, refined). */
	static int32 CatalogIdFor(int32 Tier, int32 Variant, bool bRefined);

	/** TierModules.IdFor(tier 1-6, quality). */
	static int32 LegacyIdFor(int32 Tier, ELegacyQuality Quality);

	/** ModuleCatalog.Power caps the summed grades at 4. */
	static constexpr int32 MaxPower = 4;
};

/** Full-set bonus rules (GearSets.cs). Rows come from DT_GearSets. */
struct WASTELANDERS_API FWLGearSetRules
{
	/** GearSets.WornCount: distinct worn pieces (one per slot) carrying this set id. */
	static int32 WornCount(TConstArrayView<const FWLItemRow*> WornItems, FName SetId);

	/**
	 * GearSets.ActiveFor: the completed set whose style matches, or null. SetLookup resolves a
	 * set id to its row (UWLDataSubsystem::FindGearSet).
	 */
	static const FWLGearSetRow* ActiveFor(TConstArrayView<const FWLItemRow*> WornItems, EWLWeaponStyle Style, TFunctionRef<const FWLGearSetRow*(FName)> SetLookup);

	/** GearSets.DamageMultiplier: never below 1. */
	static float DamageMultiplier(TConstArrayView<const FWLItemRow*> WornItems, EWLWeaponStyle Style, TFunctionRef<const FWLGearSetRow*(FName)> SetLookup);

	/** GearSets.BonusDefence: best bonus among ANY completed set, whatever style you swing. */
	static int32 BonusDefence(TConstArrayView<const FWLItemRow*> WornItems, TFunctionRef<const FWLGearSetRow*(FName)> SetLookup);
};
