#include "Items/WLItemActions.h"
#include "Items/WLInventoryComponent.h"
#include "Items/WLEquipmentComponent.h"
#include "Items/WLItemRules.h"
#include "Items/WLGroundItem.h"
#include "Data/WLDataSubsystem.h"
#include "Core/WLGameTickSubsystem.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerCharacter.h"
#include "Skills/WLSkillsComponent.h"
#include "Combat/WLCombatComponent.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Quests/WLQuestComponent.h"

#define LOCTEXT_NAMESPACE "WLItemActions"

namespace
{
	const FWLItemRow* IA_FindRow(const AWLPlayerState* PS, int32 ItemId)
	{
		const UWLDataSubsystem* Data = UWLDataSubsystem::Get(PS);
		return (Data && ItemId > 0) ? Data->FindItem(ItemId) : nullptr;
	}

	bool IA_IsDead(const AWLPlayerState* PS)
	{
		const UWLAbilitySystemComponent* ASC = PS ? PS->GetWLAbilitySystem() : nullptr;
		return ASC && ASC->IsDead();
	}

	bool IA_Valid(const AWLPlayerState* PS)
	{
		return PS && PS->HasAuthority() && PS->GetInventory() && PS->GetEquipment();
	}

	UWLCombatComponent* IA_PawnCombat(const AWLPlayerState* PS)
	{
		const AWLPlayerCharacter* Pawn = PS ? PS->GetPawn<AWLPlayerCharacter>() : nullptr;
		return Pawn ? Pawn->GetCombat() : nullptr;
	}
}

bool FWLItemActions::IsTwoHanded(const FWLItemRow* Item)
{
	return Item && Item->OccupiesBothHands();
}

void FWLItemActions::RefreshCombatStyle(AWLPlayerState* PS)
{
	if (UWLCombatComponent* Combat = IA_PawnCombat(PS))
	{
		Combat->RefreshStyleFromEquipment();
	}
}

FText FWLItemActions::EquipAt(AWLPlayerState* PS, int32 Index)
{
	if (!IA_Valid(PS))
	{
		return FText::GetEmpty();
	}
	UWLInventoryComponent* Inv = PS->GetInventory();
	UWLEquipmentComponent* Eq = PS->GetEquipment();

	const FWLItemStack Incoming = Inv->GetSlot(Index);
	const FWLItemRow* Item = Incoming.IsEmpty() ? nullptr : IA_FindRow(PS, Incoming.ItemId);
	if (!Item)
	{
		return LOCTEXT("NotInInv", "Not in inventory.");
	}
	EWLEquipSlot Slot;
	if (!WLTypes::SlotForItemType(Item->ItemType, Slot))
	{
		return LOCTEXT("CantEquip", "Can't equip that.");
	}
	if (IA_IsDead(PS))
	{
		return LOCTEXT("Down", "You can't do that while down.");
	}
	if (const UWLSkillsComponent* Skills = PS->GetSkills())
	{
		for (const TPair<EWLSkill, int32>& Req : Item->Requirements)
		{
			if (Skills->GetLevel(Req.Key) < Req.Value)
			{
				return FText::Format(LOCTEXT("Requires", "Requires {0} level {1}."), FText::FromString(WLTypes::SkillToString(Req.Key)), Req.Value);
			}
		}
	}

	// Complete the bag transaction before touching equipment. A full bag can swap one item, but
	// cannot silently delete the extra off-hand removed by a two-handed weapon.
	const TArray<FWLItemStack> Before = Inv->Snapshot();
	const FWLItemStack Old = Eq->GetStack(Slot);

	bool bHasConflict = false;
	EWLEquipSlot Conflict = EWLEquipSlot::Shield;
	if (IsTwoHanded(Item))
	{
		bHasConflict = true;
		Conflict = EWLEquipSlot::Shield;
	}
	else if (Item->ItemType == EWLItemType::Shield && IsTwoHanded(Eq->GetItem(EWLEquipSlot::Weapon)))
	{
		bHasConflict = true;
		Conflict = EWLEquipSlot::Weapon;
	}
	const FWLItemStack Offhand = bHasConflict ? Eq->GetStack(Conflict) : FWLItemStack();

	const bool bAmmo = Item->IsAmmo();
	const int32 Quantity = bAmmo ? Inv->CountOf(Incoming.ItemId) : 1;
	const bool bRemoved = bAmmo ? Inv->RemoveItem(Incoming.ItemId, Quantity) : Inv->RemoveAt(Index, 1);
	if (!bRemoved)
	{
		return LOCTEXT("NotInInv", "Not in inventory.");
	}
	const bool bSameAmmo = bAmmo && Old.ItemId == Incoming.ItemId;
	if ((!bSameAmmo && !Old.IsEmpty() && !Inv->AddStack(Old)) || (!Offhand.IsEmpty() && !Inv->AddStack(Offhand)))
	{
		Inv->Restore(Before);
		return LOCTEXT("MakeRoom", "Make room in your inventory for the equipment being removed.");
	}
	if (!Offhand.IsEmpty())
	{
		Eq->Unequip(Conflict);
	}
	if (bAmmo)
	{
		Eq->EquipAmmo(Incoming.ItemId, Quantity + (bSameAmmo ? Old.Quantity : 0));
	}
	else
	{
		Eq->Equip(Slot, Incoming.ItemId, Incoming.Modules);
	}
	RefreshCombatStyle(PS);

	const FText Equipped = FText::Format(LOCTEXT("Equipped", "You equip the {0}."), Item->DisplayName);
	return Offhand.IsEmpty() ? Equipped
		: FText::Format(LOCTEXT("EquippedBoth", "{0} (Both hands are required.)"), Equipped);
}

FText FWLItemActions::Unequip(AWLPlayerState* PS, EWLEquipSlot Slot)
{
	if (!IA_Valid(PS))
	{
		return FText::GetEmpty();
	}
	UWLInventoryComponent* Inv = PS->GetInventory();
	UWLEquipmentComponent* Eq = PS->GetEquipment();
	const FWLItemStack Stack = Eq->GetStack(Slot);
	if (Stack.IsEmpty())
	{
		return LOCTEXT("NothingThere", "Nothing equipped there.");
	}
	const FWLItemRow* Item = IA_FindRow(PS, Stack.ItemId);
	if (!Inv->AddStack(Stack))
	{
		return LOCTEXT("InvFull", "Inventory is full.");
	}
	Eq->Unequip(Slot);
	RefreshCombatStyle(PS);
	return FText::Format(LOCTEXT("Unequipped", "You unequip the {0}."), Item ? Item->DisplayName : FText::GetEmpty());
}

FText FWLItemActions::Consume(AWLPlayerState* PS, int32 Index)
{
	if (!IA_Valid(PS))
	{
		return FText::GetEmpty();
	}
	UWLInventoryComponent* Inv = PS->GetInventory();
	const FWLItemStack Stack = Inv->GetSlot(Index);
	const FWLItemRow* Item = Stack.IsEmpty() ? nullptr : IA_FindRow(PS, Stack.ItemId);
	if (!Item || !Item->IsConsumable())
	{
		return LOCTEXT("CantUse", "Can't use that.");
	}
	if (IA_IsDead(PS))
	{
		return LOCTEXT("CantEatDown", "You can't eat while down.");
	}

	// Still on the eat cooldown: silently ignore the click (OSRS behaviour).
	const UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(PS);
	const int64 Now = Tick ? Tick->GetTickCount() : 0;
	if (Now < Inv->GetNextEatTick())
	{
		return FText::GetEmpty();
	}

	if (!Inv->RemoveAt(Index, 1))
	{
		return LOCTEXT("NotInInv", "Not in inventory.");
	}
	if (UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem())
	{
		ASC->ApplyHeal(Item->HealAmount);
	}
	Inv->SetNextEatTick(Now + EatDelayTicks);

	// Eating while engaged delays the next swing (the 3-tick attack delay).
	if (UWLCombatComponent* Combat = IA_PawnCombat(PS))
	{
		Combat->DelayAttackAfterEating(EatDelayTicks);
	}
	return FText::Format(LOCTEXT("Ate", "You use the {0}. (+{1} HP)"), Item->DisplayName, Item->HealAmount);
}

FText FWLItemActions::Bury(AWLPlayerState* PS, int32 Index)
{
	if (!IA_Valid(PS))
	{
		return FText::GetEmpty();
	}
	UWLInventoryComponent* Inv = PS->GetInventory();
	const FWLItemStack Stack = Inv->GetSlot(Index);
	const FWLItemRow* Item = Stack.IsEmpty() ? nullptr : IA_FindRow(PS, Stack.ItemId);
	if (!Item || !Item->bBuryable)
	{
		return LOCTEXT("CantBury", "You can't bury that.");
	}
	if (IA_IsDead(PS))
	{
		return LOCTEXT("Down", "You can't do that while down.");
	}
	if (!Inv->RemoveAt(Index, 1))
	{
		return LOCTEXT("NotInInv", "Not in inventory.");
	}
	if (UWLSkillsComponent* Skills = PS->GetSkills())
	{
		Skills->AddXP(EWLSkill::Beastmastery, Item->BeastXP);
	}
	return FText::Format(LOCTEXT("Buried", "You bury the {0}. (+{1} Beastmastery XP)"), Item->DisplayName, Item->BeastXP);
}

FText FWLItemActions::DropAt(AWLPlayerState* PS, int32 Index)
{
	if (!IA_Valid(PS))
	{
		return FText::GetEmpty();
	}
	UWLInventoryComponent* Inv = PS->GetInventory();
	const FWLItemStack Slot = Inv->GetSlot(Index);
	const FWLItemRow* Item = Slot.IsEmpty() ? nullptr : IA_FindRow(PS, Slot.ItemId);
	if (!Item)
	{
		return LOCTEXT("NotInInv", "Not in inventory.");
	}
	const APawn* Pawn = PS->GetPawn();
	if (!Pawn || IA_IsDead(PS))
	{
		return LOCTEXT("CantDrop", "Can't drop that here.");
	}
	// Unity drops ONE of the stack (Copy(1) + RemoveAt(index)), modules travel with the copy.
	const FWLItemStack Drop = Slot.CopyWithQuantity(1);
	if (!AWLGroundItem::Spawn(PS->GetWorld(), Drop, Pawn->GetActorLocation(), PS))
	{
		return LOCTEXT("CantDrop", "Can't drop that here.");
	}
	Inv->RemoveAt(Index, 1);
	return FText::Format(LOCTEXT("Dropped", "You dropped the {0}."), Item->DisplayName);
}

FText FWLItemActions::Socket(AWLPlayerState* PS, EWLEquipSlot Slot, int32 SocketIndex, int32 ModuleId)
{
	if (!IA_Valid(PS))
	{
		return FText::GetEmpty();
	}
	UWLInventoryComponent* Inv = PS->GetInventory();
	UWLEquipmentComponent* Eq = PS->GetEquipment();
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(PS);
	if (!Data)
	{
		return FText::GetEmpty();
	}

	const FWLItemRow* Gear = Eq->GetItem(Slot);
	const int32 Count = Gear ? Gear->SocketCount : 0;
	if (SocketIndex < 0 || SocketIndex >= Count)
	{
		return LOCTEXT("EquipFirst", "Equip a compatible piece of gear first.");
	}
	if (ModuleId != 0 && !FWLModuleRules::Fits(ModuleId, Data->FindModule(ModuleId), Slot))
	{
		return LOCTEXT("NoFit", "That module does not fit this gear slot.");
	}

	TArray<int32> Ids;
	Ids.SetNumZeroed(Count);
	const TArray<int32> Old = Eq->GetModules(Slot);
	for (int32 i = 0; i < Count && i < Old.Num(); ++i)
	{
		Ids[i] = Old[i];
	}
	if (Ids[SocketIndex] == ModuleId)
	{
		return LOCTEXT("Already", "That module is already installed.");
	}

	const TArray<FWLItemStack> Before = Inv->Snapshot();
	if (ModuleId != 0 && !Inv->RemoveItem(ModuleId, 1))
	{
		return LOCTEXT("NoModule", "That module is not in your inventory.");
	}
	if (Ids[SocketIndex] != 0 && !Inv->AddItem(Ids[SocketIndex], 1))
	{
		Inv->Restore(Before);
		return LOCTEXT("RoomForModule", "Make room in your inventory for the removed module.");
	}
	Ids[SocketIndex] = ModuleId;
	Eq->SetModules(Slot, Ids);
	return ModuleId == 0 ? LOCTEXT("Removed", "Module removed.") : LOCTEXT("Installed", "Module installed.");
}

FText FWLItemActions::Read(AWLPlayerState* PS, int32 Index)
{
	if (!IA_Valid(PS))
	{
		return FText::GetEmpty();
	}
	const FWLItemStack Stack = PS->GetInventory()->GetSlot(Index);
	const FWLItemRow* Item = Stack.IsEmpty() ? nullptr : IA_FindRow(PS, Stack.ItemId);
	if (!Item || !Item->IsReadable())
	{
		return LOCTEXT("CantRead", "There's nothing to read.");
	}
	// The quest component validates, records the page and sends it to the owning client's journal.
	if (UWLQuestComponent* Quests = PS->GetQuests())
	{
		Quests->ReadJournalPage(Stack.ItemId);
	}
	return FText::GetEmpty();
}

FText FWLItemActions::UseDefault(AWLPlayerState* PS, int32 Index)
{
	if (!IA_Valid(PS))
	{
		return FText::GetEmpty();
	}
	const FWLItemStack Stack = PS->GetInventory()->GetSlot(Index);
	const FWLItemRow* Item = Stack.IsEmpty() ? nullptr : IA_FindRow(PS, Stack.ItemId);
	if (!Item)
	{
		return FText::GetEmpty();
	}
	// Same priority as InventorySlotUI's click.
	if (Item->IsEquippable()) return EquipAt(PS, Index);
	if (Item->IsConsumable()) return Consume(PS, Index);
	if (Item->bBuryable)      return Bury(PS, Index);
	if (Item->IsReadable())   return Read(PS, Index);
	return FText::GetEmpty();
}

#undef LOCTEXT_NAMESPACE
