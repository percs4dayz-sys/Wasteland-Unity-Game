#pragma once

#include "CoreMinimal.h"
#include "WLItemTypes.generated.h"

/**
 * One stack of items in a slot (inventory, bank, ground, equipment). Ported from
 * Assets/Scripts/Items/ItemStack.cs.
 *
 * Modules are socketed module item ids carried on this specific copy of the gear (0 = empty socket).
 * A stack with modules is always a single item: it never merges with plain copies of the same id,
 * so per-copy sockets survive banking, dropping and re-equipping (ModuleCatalog.Socket design).
 */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLItemStack
{
	GENERATED_BODY()

	FWLItemStack() = default;
	FWLItemStack(int32 InItemId, int32 InQuantity) : ItemId(InItemId), Quantity(InQuantity) {}
	FWLItemStack(int32 InItemId, int32 InQuantity, const TArray<int32>& InModules) : ItemId(InItemId), Quantity(InQuantity), Modules(InModules) {}

	/** Stable item id (DT_Items). 0 = empty slot. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
	int32 ItemId = 0;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
	int32 Quantity = 0;

	/** Socketed module ids, one per socket (0 = empty socket). Empty array = no modules. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Item")
	TArray<int32> Modules;

	bool IsEmpty() const { return ItemId <= 0 || Quantity <= 0; }

	/** Unity ItemStack.HasModules: true only if at least one socket is filled. */
	bool HasModules() const
	{
		for (const int32 Id : Modules)
		{
			if (Id > 0)
			{
				return true;
			}
		}
		return false;
	}

	/** Unity ItemStack.Copy(qty): same item and modules, optionally a different quantity. */
	FWLItemStack CopyWithQuantity(int32 NewQuantity) const { return FWLItemStack(ItemId, NewQuantity, Modules); }

	void Reset() { ItemId = 0; Quantity = 0; Modules.Reset(); }

	bool operator==(const FWLItemStack& Other) const
	{
		return ItemId == Other.ItemId && Quantity == Other.Quantity && Modules == Other.Modules;
	}
	bool operator!=(const FWLItemStack& Other) const { return !(*this == Other); }
};
