#include "Items/WLItemRules.h"
#include "Data/WLDataTypes.h"

// ---------------------------------------------------------------------------------------------
// Slots (Inventory.cs / Bank.cs)
// ---------------------------------------------------------------------------------------------

int32 FWLItemRules::FreeSlots(const TArray<FWLItemStack>& Slots, int32 Capacity)
{
	int32 Count = 0;
	const int32 Limit = FMath::Min(Capacity, Slots.Num());
	for (int32 i = 0; i < Limit; ++i)
	{
		if (Slots[i].IsEmpty())
		{
			++Count;
		}
	}
	return Count;
}

int32 FWLItemRules::CountOf(const TArray<FWLItemStack>& Slots, int32 ItemId)
{
	int32 Total = 0;
	for (const FWLItemStack& S : Slots)
	{
		if (!S.IsEmpty() && S.ItemId == ItemId)
		{
			Total += S.Quantity;
		}
	}
	return Total;
}

int32 FWLItemRules::FindFirst(const TArray<FWLItemStack>& Slots, int32 ItemId)
{
	for (int32 i = 0; i < Slots.Num(); ++i)
	{
		if (!Slots[i].IsEmpty() && Slots[i].ItemId == ItemId)
		{
			return i;
		}
	}
	return INDEX_NONE;
}

bool FWLItemRules::InventoryAdd(TArray<FWLItemStack>& Slots, int32 Capacity, int32 ItemId, int32 Quantity, FItemLookup Lookup)
{
	if (Quantity <= 0)
	{
		return false;
	}
	const FWLItemRow* Item = Lookup(ItemId);
	if (!Item)
	{
		return false; // not in the item table (removed item / bad id)
	}
	const int32 Limit = FMath::Min(Capacity, Slots.Num());

	if (Item->bStackable)
	{
		// Merge into an existing PLAIN stack anywhere, including saddlebag overflow slots.
		for (FWLItemStack& S : Slots)
		{
			if (!S.IsEmpty() && S.ItemId == ItemId && !S.HasModules())
			{
				S.Quantity += Quantity;
				return true;
			}
		}
	}

	// Non-stackables are all-or-nothing: never hand out half a stack and lose the rest.
	if (!Item->bStackable && FreeSlots(Slots, Capacity) < Quantity)
	{
		return false;
	}

	int32 Remaining = Quantity;
	for (int32 i = 0; i < Limit; ++i)
	{
		if (Slots[i].IsEmpty())
		{
			const int32 Take = Item->bStackable ? Remaining : 1;
			Slots[i] = FWLItemStack(ItemId, Take);
			Remaining -= Take;
			if (Remaining == 0)
			{
				return true;
			}
		}
	}
	return false;
}

bool FWLItemRules::InventoryAddStack(TArray<FWLItemStack>& Slots, int32 Capacity, const FWLItemStack& Stack, FItemLookup Lookup)
{
	if (Stack.IsEmpty())
	{
		return false;
	}
	if (!Stack.HasModules())
	{
		return InventoryAdd(Slots, Capacity, Stack.ItemId, Stack.Quantity, Lookup);
	}
	if (Stack.Quantity != 1 || !Lookup(Stack.ItemId))
	{
		return false;
	}
	const int32 Limit = FMath::Min(Capacity, Slots.Num());
	for (int32 i = 0; i < Limit; ++i)
	{
		if (Slots[i].IsEmpty())
		{
			Slots[i] = Stack;
			return true;
		}
	}
	return false;
}

bool FWLItemRules::InventoryRemove(TArray<FWLItemStack>& Slots, int32 ItemId, int32 Quantity)
{
	if (Quantity <= 0 || CountOf(Slots, ItemId) < Quantity)
	{
		return false;
	}
	int32 Remaining = Quantity;
	for (int32 i = 0; i < Slots.Num() && Remaining > 0; ++i)
	{
		FWLItemStack& S = Slots[i];
		if (S.IsEmpty() || S.ItemId != ItemId)
		{
			continue;
		}
		const int32 Take = FMath::Min(Remaining, S.Quantity);
		S.Quantity -= Take;
		Remaining -= Take;
		if (S.Quantity <= 0)
		{
			S.Reset();
		}
	}
	return true;
}

bool FWLItemRules::RemoveAt(TArray<FWLItemStack>& Slots, int32 Index, int32 Quantity)
{
	if (!Slots.IsValidIndex(Index) || Quantity <= 0 || Slots[Index].IsEmpty() || Slots[Index].Quantity < Quantity)
	{
		return false;
	}
	Slots[Index].Quantity -= Quantity;
	if (Slots[Index].Quantity == 0)
	{
		Slots[Index].Reset();
	}
	return true;
}

bool FWLItemRules::InventorySwap(TArray<FWLItemStack>& Slots, int32 Capacity, int32 IndexA, int32 IndexB)
{
	if (!Slots.IsValidIndex(IndexA) || !Slots.IsValidIndex(IndexB) || IndexA == IndexB)
	{
		return false;
	}
	auto Usable = [&Slots, Capacity](int32 i) { return i < Capacity || !Slots[i].IsEmpty(); };
	if (!Usable(IndexA) || !Usable(IndexB))
	{
		return false;
	}
	Slots.Swap(IndexA, IndexB);
	return true;
}

bool FWLItemRules::BankDeposit(TArray<FWLItemStack>& Slots, const FWLItemStack& Stack, FItemLookup Lookup)
{
	if (Stack.IsEmpty() || !Lookup(Stack.ItemId))
	{
		return false;
	}
	if (!Stack.HasModules())
	{
		for (FWLItemStack& S : Slots)
		{
			if (!S.IsEmpty() && S.ItemId == Stack.ItemId && !S.HasModules())
			{
				S.Quantity += Stack.Quantity;
				return true;
			}
		}
	}
	for (FWLItemStack& S : Slots)
	{
		if (S.IsEmpty())
		{
			S = Stack;
			return true;
		}
	}
	return false;
}

bool FWLItemRules::BankWithdraw(TArray<FWLItemStack>& Slots, int32 ItemId, int32 Quantity)
{
	const int32 Index = FindFirst(Slots, ItemId);
	return Index != INDEX_NONE && RemoveAt(Slots, Index, Quantity);
}

bool FWLItemRules::SwapSlots(TArray<FWLItemStack>& Slots, int32 IndexA, int32 IndexB)
{
	if (!Slots.IsValidIndex(IndexA) || !Slots.IsValidIndex(IndexB) || IndexA == IndexB)
	{
		return false;
	}
	Slots.Swap(IndexA, IndexB);
	return true;
}

// ---------------------------------------------------------------------------------------------
// Modules (ModuleCatalog.cs / TierModules.cs)
// ---------------------------------------------------------------------------------------------

int32 FWLModuleRules::Grade(int32 ItemId, const FWLModuleRow* CatalogRow)
{
	if (CatalogRow)
	{
		return FMath::Clamp(CatalogRow->Grade, 1, 2);
	}
	if (ItemId >= CatalogFirstId && ItemId < CatalogEndId)
	{
		return 1 + (ItemId - CatalogFirstId) % 2;
	}
	return (ItemId - LegacyFirstId) % 3 + 1;
}

bool FWLModuleRules::Fits(int32 ItemId, const FWLModuleRow* CatalogRow, EWLEquipSlot Slot)
{
	if (!IsModule(ItemId, CatalogRow))
	{
		return false;
	}
	if (!CatalogRow)
	{
		return true; // legacy generic modules fit any socket
	}
	EWLEquipSlot ModuleSlot;
	return WLTypes::SlotFromString(CatalogRow->Slot, ModuleSlot) && ModuleSlot == Slot;
}

int32 FWLModuleRules::CatalogIdFor(int32 Tier, int32 Variant, bool bRefined)
{
	const int32 T = FMath::Clamp(Tier, 1, 5);
	const int32 V = FMath::Clamp(Variant, 0, 6);
	return CatalogFirstId + ((T - 1) * 7 + V) * 2 + (bRefined ? 1 : 0);
}

int32 FWLModuleRules::LegacyIdFor(int32 Tier, ELegacyQuality Quality)
{
	const int32 T = FMath::Clamp(Tier, 1, 6);
	return LegacyFirstId + (T - 1) * 3 + static_cast<int32>(Quality);
}

// ---------------------------------------------------------------------------------------------
// Gear sets (GearSets.cs)
// ---------------------------------------------------------------------------------------------

int32 FWLGearSetRules::WornCount(TConstArrayView<const FWLItemRow*> WornItems, FName SetId)
{
	if (SetId.IsNone())
	{
		return 0;
	}
	int32 N = 0;
	for (const FWLItemRow* Item : WornItems)
	{
		if (Item && Item->SetId == SetId)
		{
			++N;
		}
	}
	return N;
}

/** Distinct set ids among the worn items: a set can only be complete if a piece of it is worn. */
static TArray<FName, TInlineAllocator<8>> WornSetIds(TConstArrayView<const FWLItemRow*> WornItems)
{
	TArray<FName, TInlineAllocator<8>> Ids;
	for (const FWLItemRow* Item : WornItems)
	{
		if (Item && !Item->SetId.IsNone())
		{
			Ids.AddUnique(Item->SetId);
		}
	}
	return Ids;
}

const FWLGearSetRow* FWLGearSetRules::ActiveFor(TConstArrayView<const FWLItemRow*> WornItems, EWLWeaponStyle Style, TFunctionRef<const FWLGearSetRow*(FName)> SetLookup)
{
	for (const FName SetId : WornSetIds(WornItems))
	{
		const FWLGearSetRow* Def = SetLookup(SetId);
		if (Def && Def->Style == Style && Def->PieceCount > 0 && WornCount(WornItems, SetId) >= Def->PieceCount)
		{
			return Def;
		}
	}
	return nullptr;
}

float FWLGearSetRules::DamageMultiplier(TConstArrayView<const FWLItemRow*> WornItems, EWLWeaponStyle Style, TFunctionRef<const FWLGearSetRow*(FName)> SetLookup)
{
	const FWLGearSetRow* Def = ActiveFor(WornItems, Style, SetLookup);
	return Def ? FMath::Max(1.f, Def->DamageMultiplier) : 1.f;
}

int32 FWLGearSetRules::BonusDefence(TConstArrayView<const FWLItemRow*> WornItems, TFunctionRef<const FWLGearSetRow*(FName)> SetLookup)
{
	int32 Best = 0;
	for (const FName SetId : WornSetIds(WornItems))
	{
		const FWLGearSetRow* Def = SetLookup(SetId);
		if (Def && Def->PieceCount > 0 && WornCount(WornItems, SetId) >= Def->PieceCount && Def->BonusDefence > Best)
		{
			Best = Def->BonusDefence;
		}
	}
	return Best;
}
