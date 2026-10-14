#include "UI/WLViewModels.h"
#include "UI/WLUISubsystem.h"
#include "UI/WLGameUserSettings.h"
#include "Audio/WLAudioEvents.h"
#include "Core/WLXPTable.h"
#include "Data/WLDataSubsystem.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLCharacterMovementComponent.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Skills/WLSkillsComponent.h"
#include "Items/WLInventoryComponent.h"
#include "Items/WLEquipmentComponent.h"
#include "Items/WLBankComponent.h"
#include "Items/WLItemTypes.h"
#include "Skilling/WLSkillingComponent.h"
#include "Quests/WLQuestComponent.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Companions/WLCompanionComponent.h"

#define LOCTEXT_NAMESPACE "WLViewModels"

/** FText has no operator==, so text fields compare with EqualTo before notifying. */
#define WL_SET_TEXT(Member, NewText) \
	[&]() -> bool { const FText WLNew_ = (NewText); if (Member.EqualTo(WLNew_)) { return false; } Member = WLNew_; UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Member); return true; }()

// =================================================================================================
// Base
// =================================================================================================

void UWLViewModelBase::BindContext(UWLUISubsystem* InUI, AWLPlayerController* InPC, AWLPlayerState* InPS, AWLPlayerCharacter* InPawn)
{
	Unbind();
	UI = InUI;
	PC = InPC;
	PS = InPS;
	Pawn = InPawn;
	OnBind();
	NotifyChanged();
}

void UWLViewModelBase::Unbind()
{
	OnUnbind();
	PC.Reset();
	PS.Reset();
	Pawn.Reset();
}

// =================================================================================================
// HUD
// =================================================================================================

void UWLHudViewModel::OnBind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		if (UWLQuestComponent* Quests = P->GetQuests())
		{
			Quests->OnBanner.AddUObject(this, &UWLHudViewModel::HandleBanner);
		}
	}
	TickViewModel(0.f);
}

void UWLHudViewModel::OnUnbind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		if (UWLQuestComponent* Quests = P->GetQuests())
		{
			Quests->OnBanner.RemoveAll(this);
		}
	}
}

void UWLHudViewModel::HandleBanner(const FText& Title, const FText& Subtitle, float Seconds)
{
	WL_SET_TEXT(BannerTitle, Title);
	WL_SET_TEXT(BannerSubtitle, Subtitle);
	UE_MVVM_SET_PROPERTY_VALUE(BannerTimeLeft, FMath::Max(0.5f, Seconds));
	NotifyChanged();
}

void UWLHudViewModel::TickViewModel(float DeltaSeconds)
{
	// Unity read these every frame ("so it's always accurate no matter what changed it"); they are cheap
	// getters on replicated state, so polling keeps the HUD correct without depending on event order.
	AWLPlayerState* P = PS.Get();
	if (!P)
	{
		return;
	}
	bool bDirty = false;

	const FString Name = P->GetPlayerName();
	bDirty |= WL_SET_TEXT(PlayerName, FText::FromString(Name.IsEmpty() ? FString(TEXT("Survivor")) : Name));
	bDirty |= UE_MVVM_SET_PROPERTY_VALUE(CombatLevel, P->GetCombatLevel());

	if (const UWLAbilitySystemComponent* ASC = P->GetWLAbilitySystem())
	{
		const int32 Cur = ASC->GetCurrentHealthInt();
		const int32 Max = ASC->GetMaxHealthInt();
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(Health, Cur);
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(MaxHealth, Max);
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bHealthLow, Max > 0 && Cur <= Max * FWLUILogic::LowHealthFraction);
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bDead, ASC->IsDead());
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(SpecialEnergy, ASC->GetSpecialEnergyValue());
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(MaxSpecialEnergy, FMath::Max(1.f, ASC->GetMaxSpecialEnergyValue()));
	}

	if (const UWLSkillsComponent* Sk = P->GetSkills())
	{
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(TotalLevel, Sk->GetTotalLevel());
		const UWLGameUserSettings* Settings = UWLGameUserSettings::GetWLSettings();
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bTrackingSkill, Settings->bHasTrackedSkill);
		if (Settings->bHasTrackedSkill)
		{
			const EWLSkill S = Settings->TrackedSkill;
			bDirty |= WL_SET_TEXT(TrackedSkillName, Sk->GetSkillDisplayName(S));
			bDirty |= UE_MVVM_SET_PROPERTY_VALUE(TrackedSkillLevel, Sk->GetLevel(S));
			bDirty |= UE_MVVM_SET_PROPERTY_VALUE(TrackedSkillProgress, Sk->GetLevelProgress(S));
		}
	}

	if (const UWLCompanionComponent* Comp = P->GetCompanions())
	{
		FText Line;
		float Fill = 0.f;
		const bool bLine = Comp->GetStatusLine(Line, Fill);
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bOwnsBeast, bLine);
		if (bLine)
		{
			bDirty |= WL_SET_TEXT(BeastStatusText, Line);
			bDirty |= UE_MVVM_SET_PROPERTY_VALUE(BeastStatusFill, Fill);
		}
	}

	if (const UWLQuestComponent* Quests = P->GetQuests())
	{
		FText Label, Value;
		float Fill = 0.f;
		bool bAlert = false;
		const bool bTask = Quests->GetBeastTaskHud(Label, Value, Fill, bAlert);
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bShowBeastTask, bTask);
		if (bTask)
		{
			bDirty |= WL_SET_TEXT(BeastTaskLabel, Label);
			bDirty |= WL_SET_TEXT(BeastTaskValue, Value);
			bDirty |= UE_MVVM_SET_PROPERTY_VALUE(BeastTaskFill, Fill);
			bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bBeastTaskAlert, bAlert);
		}
		const FWLQuestObjective& Obj = Quests->GetObjective();
		bDirty |= WL_SET_TEXT(ObjectiveText, Obj.bActive ? Obj.Text : FText::GetEmpty());
	}

	if (const AWLPlayerCharacter* Body = Pawn.Get())
	{
		if (const UWLCharacterMovementComponent* Move = Body->GetWLMovement())
		{
			bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bRunning, Move->IsRunning());
			bDirty |= UE_MVVM_SET_PROPERTY_VALUE(DodgeCooldownRemaining, Move->GetDodgeCooldownRemaining());
			bDirty |= UE_MVVM_SET_PROPERTY_VALUE(DodgeCooldown, Move->DodgeCooldown);
		}
	}

	if (BannerTimeLeft > 0.f)
	{
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(BannerTimeLeft, FMath::Max(0.f, BannerTimeLeft - DeltaSeconds));
	}

	if (bDirty)
	{
		NotifyChanged();
	}
}

// =================================================================================================
// Inventory
// =================================================================================================

void UWLInventoryViewModel::OnBind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetInventory()->OnInventoryChanged.AddUObject(this, &UWLInventoryViewModel::Refresh);
		// Deposit-vs-use default depends on the bank being open.
		P->GetBank()->OnBankOpened.AddUObject(this, &UWLInventoryViewModel::Refresh);
		P->GetBank()->OnBankClosed.AddUObject(this, &UWLInventoryViewModel::Refresh);
	}
	Refresh();
}

void UWLInventoryViewModel::OnUnbind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetInventory()->OnInventoryChanged.RemoveAll(this);
		P->GetBank()->OnBankOpened.RemoveAll(this);
		P->GetBank()->OnBankClosed.RemoveAll(this);
	}
}

void UWLInventoryViewModel::Refresh()
{
	Slots.Reset();
	const AWLPlayerState* P = PS.Get();
	const UWLInventoryComponent* Inv = P ? P->GetInventory() : nullptr;
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(P);
	if (Inv)
	{
		const int32 Cap = Inv->GetCapacity();
		for (int32 i = 0; i < UWLInventoryComponent::MaxSlots; ++i)
		{
			const FWLItemStack& Stack = Inv->GetSlot(i);
			// Saddlebag overflow slots past capacity are shown only while they still hold something.
			if (i >= Cap && Stack.IsEmpty())
			{
				continue;
			}
			FWLItemSlotView& View = Slots.AddDefaulted_GetRef();
			View.SlotIndex = i;
			View.bOverflow = i >= Cap;
			if (!Stack.IsEmpty())
			{
				View.ItemId = Stack.ItemId;
				View.Quantity = Stack.Quantity;
				View.ModuleCount = Stack.Modules.FilterByPredicate([](int32 M) { return M > 0; }).Num();
				View.DisplayName = Data ? Data->GetItemName(Stack.ItemId) : FText::AsNumber(Stack.ItemId);
			}
		}
		UE_MVVM_SET_PROPERTY_VALUE(Capacity, Cap);
		UE_MVVM_SET_PROPERTY_VALUE(FreeSlots, Inv->FreeSlots());
	}
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Slots);
	NotifyChanged();
}

void UWLInventoryViewModel::SetDropMode(bool bOn)
{
	if (UE_MVVM_SET_PROPERTY_VALUE(bDropMode, bOn))
	{
		NotifyChanged();
	}
}

void UWLInventoryViewModel::ActivateSlot(int32 SlotIndex)
{
	const AWLPlayerState* P = PS.Get();
	if (!P)
	{
		return;
	}
	const FWLItemStack& Stack = P->GetInventory()->GetSlot(SlotIndex);
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(P);
	const FWLItemRow* Item = (Data && !Stack.IsEmpty()) ? Data->FindItem(Stack.ItemId) : nullptr;
	if (!Item)
	{
		return;
	}
	if (bDropMode)
	{
		PerformVerb(SlotIndex, WLItemVerbs::Drop);
		return;
	}

	const bool bBankOpen = P->GetBank()->IsBankOpen();
	const FName Verb = FWLUILogic::DefaultItemVerb(*Item, bBankOpen, Data->FindModule(Stack.ItemId) != nullptr);
	if (Verb == WLItemVerbs::Deposit)
	{
		// Unity InventorySlotUI: a left click while banking deposits ONE.
		P->GetBank()->ServerDeposit(SlotIndex, 1);
	}
	else if (Verb == TEXT("Modules"))
	{
		if (UWLUISubsystem* Root = UI.Get())
		{
			Root->SetPanelOpen(EWLUIPanel::Equipment, true);
		}
	}
	else if (Verb.IsNone())
	{
		if (UWLUISubsystem* Root = UI.Get())
		{
			Root->GetChat()->AddLocalLine(LOCTEXT("CantUse", "You can't use this item."));
		}
	}
	else
	{
		PerformVerb(SlotIndex, Verb);
	}
}

void UWLInventoryViewModel::PerformVerb(int32 SlotIndex, FName Verb)
{
	AWLPlayerState* P = PS.Get();
	if (!P || Verb.IsNone() || Verb == WLItemVerbs::Cancel)
	{
		return;
	}
	const FWLItemStack& Stack = P->GetInventory()->GetSlot(SlotIndex);
	if (Stack.IsEmpty())
	{
		return;
	}
	if (Verb == WLItemVerbs::Examine)
	{
		Examine(Stack.ItemId);
		return;
	}
	if (Verb == WLItemVerbs::Deposit)
	{
		P->GetBank()->ServerDeposit(SlotIndex, 1);
		return;
	}
	// The server re-validates the item, the verb and the player's state; results come back as chat lines.
	P->GetInventory()->ServerInventoryAction(SlotIndex, Verb);
	if (Verb == WLItemVerbs::Drop)
	{
		UWLAudioEvents::PlayEvent(P, TEXT("Drop"));
	}
	else if (Verb == WLItemVerbs::Equip)
	{
		UWLAudioEvents::PlayEvent(P, TEXT("Equip"));
	}
}

void UWLInventoryViewModel::MoveSlot(int32 From, int32 To)
{
	if (AWLPlayerState* P = PS.Get())
	{
		if (From != To)
		{
			P->GetInventory()->ServerMoveSlot(From, To);
		}
	}
}

TArray<FWLMenuEntry> UWLInventoryViewModel::GetMenuForSlot(int32 SlotIndex) const
{
	TArray<FWLMenuEntry> Out;
	const AWLPlayerState* P = PS.Get();
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(P);
	if (!P || !Data)
	{
		return Out;
	}
	const FWLItemStack& Stack = P->GetInventory()->GetSlot(SlotIndex);
	const FWLItemRow* Item = Stack.IsEmpty() ? nullptr : Data->FindItem(Stack.ItemId);
	if (!Item)
	{
		return Out;
	}
	TArray<FName> Verbs = FWLUILogic::ItemVerbs(*Item, P->GetBank()->IsBankOpen());
	Verbs.Add(WLItemVerbs::Examine);
	Verbs.Add(WLItemVerbs::Cancel);
	for (const FName V : Verbs)
	{
		FWLMenuEntry& E = Out.AddDefaulted_GetRef();
		E.Verb = V;
		E.Label = FWLUILogic::VerbLabel(V);
	}
	return Out;
}

FText UWLInventoryViewModel::GetSlotTitle(int32 SlotIndex) const
{
	const AWLPlayerState* P = PS.Get();
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(P);
	if (!P || !Data)
	{
		return FText::GetEmpty();
	}
	const FWLItemStack& Stack = P->GetInventory()->GetSlot(SlotIndex);
	return Stack.IsEmpty() ? FText::GetEmpty() : Data->GetItemName(Stack.ItemId);
}

void UWLInventoryViewModel::Examine(int32 ItemId) const
{
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(PS.Get());
	const FWLItemRow* Item = Data ? Data->FindItem(ItemId) : nullptr;
	UWLUISubsystem* Root = UI.Get();
	if (Item && Root)
	{
		// Examine is local and cosmetic, like OSRS.
		Root->GetChat()->AddLocalLine(Item->Description.IsEmpty() ? Item->DisplayName : Item->Description);
	}
}

// =================================================================================================
// Equipment
// =================================================================================================

void UWLEquipmentViewModel::OnBind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetEquipment()->OnEquipmentChanged.AddUObject(this, &UWLEquipmentViewModel::Refresh);
	}
	Refresh();
}

void UWLEquipmentViewModel::OnUnbind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetEquipment()->OnEquipmentChanged.RemoveAll(this);
	}
}

void UWLEquipmentViewModel::Refresh()
{
	Slots.Reset();
	const AWLPlayerState* P = PS.Get();
	const UWLEquipmentComponent* Eq = P ? P->GetEquipment() : nullptr;
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(P);
	for (EWLEquipSlot Slot : TEnumRange<EWLEquipSlot>())
	{
		FWLEquipSlotView& View = Slots.AddDefaulted_GetRef();
		View.Slot = Slot;
		View.SlotLabel = FText::FromString(WLTypes::SlotToString(Slot));
		if (!Eq)
		{
			continue;
		}
		View.ItemId = Eq->GetItemId(Slot);
		if (View.ItemId > 0)
		{
			View.Modules = Eq->GetModules(Slot);
			const FText Name = Data ? Data->GetItemName(View.ItemId) : FText::AsNumber(View.ItemId);
			// Unity EquipmentPanelUI: the ammo slot shows "Name xN".
			View.DisplayName = Slot == EWLEquipSlot::Ammo
				? FText::Format(LOCTEXT("AmmoName", "{0} x{1}"), Name, FText::AsNumber(Eq->GetAmmoQuantity()))
				: Name;
		}
	}
	if (Eq)
	{
		UE_MVVM_SET_PROPERTY_VALUE(AttackBonus, Eq->TotalAttackBonus());
		UE_MVVM_SET_PROPERTY_VALUE(StrengthBonus, Eq->TotalStrengthBonus());
		UE_MVVM_SET_PROPERTY_VALUE(DefenceBonus, Eq->TotalDefenceBonus());
	}
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Slots);
	NotifyChanged();
}

void UWLEquipmentViewModel::Unequip(EWLEquipSlot Slot)
{
	if (AWLPlayerState* P = PS.Get())
	{
		if (P->GetEquipment()->GetItemId(Slot) > 0)
		{
			P->GetEquipment()->ServerUnequip(Slot);
		}
	}
}

void UWLEquipmentViewModel::SocketModule(EWLEquipSlot Slot, int32 SocketIndex, int32 ModuleItemId)
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetEquipment()->ServerSocketModule(Slot, SocketIndex, ModuleItemId);
	}
}

// =================================================================================================
// Bank
// =================================================================================================

void UWLBankViewModel::OnBind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		UWLBankComponent* B = P->GetBank();
		B->OnBankChanged.AddUObject(this, &UWLBankViewModel::Refresh);
		B->OnBankOpened.AddUObject(this, &UWLBankViewModel::HandleOpened);
		B->OnBankClosed.AddUObject(this, &UWLBankViewModel::HandleClosed);
		// The bank window also shows the inventory (deposit side).
		P->GetInventory()->OnInventoryChanged.AddUObject(this, &UWLBankViewModel::Refresh);
		if (B->IsBankOpen())
		{
			HandleOpened();
			return;
		}
	}
	UE_MVVM_SET_PROPERTY_VALUE(bOpen, false);
	Refresh();
}

void UWLBankViewModel::OnUnbind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		UWLBankComponent* B = P->GetBank();
		B->OnBankChanged.RemoveAll(this);
		B->OnBankOpened.RemoveAll(this);
		B->OnBankClosed.RemoveAll(this);
		P->GetInventory()->OnInventoryChanged.RemoveAll(this);
	}
}

void UWLBankViewModel::HandleOpened()
{
	OpenedAt = Pawn.IsValid() ? Pawn->GetActorLocation() : FVector::ZeroVector;
	UE_MVVM_SET_PROPERTY_VALUE(bOpen, true);
	UWLAudioEvents::PlayEvent(PS.Get(), TEXT("UIOpen"));
	Refresh();
}

void UWLBankViewModel::HandleClosed()
{
	UE_MVVM_SET_PROPERTY_VALUE(bOpen, false);
	Refresh();
}

void UWLBankViewModel::Refresh()
{
	Items.Reset();
	const AWLPlayerState* P = PS.Get();
	const UWLBankComponent* B = P ? P->GetBank() : nullptr;
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(P);
	if (B)
	{
		for (int32 i = 0; i < B->GetCapacity(); ++i)
		{
			const FWLItemStack& Stack = B->GetSlot(i);
			if (Stack.IsEmpty())
			{
				continue;
			}
			FWLItemSlotView& View = Items.AddDefaulted_GetRef();
			View.SlotIndex = i;
			View.ItemId = Stack.ItemId;
			View.Quantity = Stack.Quantity;
			View.ModuleCount = Stack.Modules.FilterByPredicate([](int32 M) { return M > 0; }).Num();
			View.DisplayName = Data ? Data->GetItemName(Stack.ItemId) : FText::AsNumber(Stack.ItemId);
		}
		UE_MVVM_SET_PROPERTY_VALUE(Capacity, B->GetCapacity());
	}
	UE_MVVM_SET_PROPERTY_VALUE(UsedSlots, Items.Num());
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Items);
	NotifyChanged();
}

void UWLBankViewModel::TickViewModel(float DeltaSeconds)
{
	// Walked away from the crate -> close (Unity BankUI failsafe). The server enforces range anyway.
	if (bOpen && Pawn.IsValid() && FVector::DistSquared2D(Pawn->GetActorLocation(), OpenedAt) > FMath::Square(FWLUILogic::BankAutoCloseDistance))
	{
		Close();
	}
}

void UWLBankViewModel::Withdraw(int32 BankSlot, int32 Quantity)
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetBank()->ServerWithdraw(BankSlot, Quantity);
	}
}

void UWLBankViewModel::Deposit(int32 InventorySlot, int32 Quantity)
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetBank()->ServerDeposit(InventorySlot, Quantity);
	}
}

void UWLBankViewModel::DepositAll()
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetBank()->ServerDepositAll();
	}
}

void UWLBankViewModel::Close()
{
	if (AWLPlayerState* P = PS.Get())
	{
		if (bOpen)
		{
			P->GetBank()->ServerCloseBank();
		}
	}
	// Close locally at once; OnBankClosed from the server is then a no-op.
	UE_MVVM_SET_PROPERTY_VALUE(bOpen, false);
	NotifyChanged();
}

// =================================================================================================
// Crafting
// =================================================================================================

const TArray<int32>& UWLCraftingViewModel::QuantityOptions()
{
	static const TArray<int32> Options = { 1, 5, 10, -1 };
	return Options;
}

void UWLCraftingViewModel::OnBind()
{
	if (AWLPlayerCharacter* Body = Pawn.Get())
	{
		if (UWLSkillingComponent* Sk = Body->GetSkilling())
		{
			Sk->OnStationChanged.AddUObject(this, &UWLCraftingViewModel::HandleStationChanged);
			Sk->OnCraftedOne.AddUObject(this, &UWLCraftingViewModel::HandleCraftedOne);
			Sk->OnActionChanged.AddWeakLambda(this, [this](EWLSkillingAction Action)
			{
				UE_MVVM_SET_PROPERTY_VALUE(bBusy, Action != EWLSkillingAction::None);
				NotifyChanged();
			});
			EWLCraftingStation Open;
			if (Sk->GetOpenStation(Open))
			{
				HandleStationChanged(true, Open);
			}
		}
	}
	BindInventorySide();
}

void UWLCraftingViewModel::BindInventorySide()
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetInventory()->OnInventoryChanged.AddUObject(this, &UWLCraftingViewModel::Refresh);
		P->GetSkills()->OnSkillsChanged.AddWeakLambda(this, [this](UWLSkillsComponent*) { Refresh(); });
		P->GetFlags()->OnFlagsChanged.AddUObject(this, &UWLCraftingViewModel::Refresh);
	}
}

void UWLCraftingViewModel::OnUnbind()
{
	if (AWLPlayerCharacter* Body = Pawn.Get())
	{
		if (UWLSkillingComponent* Sk = Body->GetSkilling())
		{
			Sk->OnStationChanged.RemoveAll(this);
			Sk->OnCraftedOne.RemoveAll(this);
			Sk->OnActionChanged.RemoveAll(this);
		}
	}
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetInventory()->OnInventoryChanged.RemoveAll(this);
		P->GetSkills()->OnSkillsChanged.RemoveAll(this);
		P->GetFlags()->OnFlagsChanged.RemoveAll(this);
	}
}

static FText StationDisplayName(EWLCraftingStation Station)
{
	switch (Station)
	{
	case EWLCraftingStation::Furnace: return LOCTEXT("Furnace", "Furnace");
	case EWLCraftingStation::CookingFire: return LOCTEXT("CookingFire", "Cooking Fire");
	default: return LOCTEXT("Workbench", "Workbench");
	}
}

void UWLCraftingViewModel::HandleStationChanged(bool bInOpen, EWLCraftingStation InStation)
{
	const bool bStationSwitched = InStation != Station;
	UE_MVVM_SET_PROPERTY_VALUE(bOpen, bInOpen);
	UE_MVVM_SET_PROPERTY_VALUE(Station, InStation);
	WL_SET_TEXT(StationName, StationDisplayName(InStation));
	if (bInOpen && bStationSwitched)
	{
		UE_MVVM_SET_PROPERTY_VALUE(SelectedIndex, INDEX_NONE);
	}
	if (bInOpen)
	{
		UWLAudioEvents::PlayEvent(PS.Get(), TEXT("UIOpen"));
	}
	Refresh();
}

void UWLCraftingViewModel::HandleCraftedOne(FName RecipeRowName)
{
	// Unity CraftingUI.RefreshAfterCraft: counts and colours update live during a batch.
	Refresh();
}

void UWLCraftingViewModel::Refresh()
{
	Recipes.Reset();
	const AWLPlayerState* P = PS.Get();
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(P);
	if (P && Data && bOpen)
	{
		const UWLInventoryComponent* Inv = P->GetInventory();
		const UWLSkillsComponent* Sk = P->GetSkills();
		for (const FWLRecipeRow* Recipe : Data->GetRecipes(Station))
		{
			if (!Recipe)
			{
				continue;
			}
			FWLRecipeView& View = Recipes.AddDefaulted_GetRef();
			View.RecipeRowName = Data->GetRecipeRowName(Recipe);
			View.OutputItemId = Recipe->OutputItemId;
			const FText OutName = Data->ItemExists(Recipe->OutputItemId) ? Data->GetItemName(Recipe->OutputItemId) : Recipe->DisplayName;
			View.DisplayName = Recipe->OutputQuantity > 1
				? FText::Format(LOCTEXT("RecipeQty", "{0} x{1}"), OutName, FText::AsNumber(Recipe->OutputQuantity))
				: OutName;
			View.Skill = Recipe->Skill;
			View.LevelRequired = Recipe->LevelRequired;
			View.bLevelMet = Sk && Sk->GetLevel(Recipe->Skill) >= Recipe->LevelRequired;
			View.XPEach = Recipe->XPGranted;
			View.MaxCraftable = UWLSkillingComponent::MaxCraftable(*Recipe, Inv);
			View.bCanMake = UWLSkillingComponent::CanCraft(*Recipe, P, View.Reason);
			for (const FWLItemQuantity& In : Recipe->Inputs)
			{
				const int32 Have = Inv ? Inv->CountOf(In.ItemId) : 0;
				View.InputLines.Add(FText::Format(LOCTEXT("Ingredient", "{0}: {1}/{2}"),
					Data->ItemExists(In.ItemId) ? Data->GetItemName(In.ItemId) : FText::Format(LOCTEXT("ItemN", "Item {0}"), In.ItemId),
					FText::AsNumber(Have), FText::AsNumber(In.Quantity)));
				View.InputMet.Add(Have >= In.Quantity);
			}
		}
	}
	if (!Recipes.IsValidIndex(SelectedIndex))
	{
		UE_MVVM_SET_PROPERTY_VALUE(SelectedIndex, INDEX_NONE);
	}
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Recipes);
	NotifyChanged();
}

void UWLCraftingViewModel::Select(int32 RecipeIndex)
{
	if (UE_MVVM_SET_PROPERTY_VALUE(SelectedIndex, Recipes.IsValidIndex(RecipeIndex) ? RecipeIndex : INDEX_NONE))
	{
		NotifyChanged();
	}
}

void UWLCraftingViewModel::SetQuantity(int32 NewQuantity)
{
	if (UE_MVVM_SET_PROPERTY_VALUE(Quantity, NewQuantity < 0 ? -1 : FMath::Max(1, NewQuantity)))
	{
		NotifyChanged();
	}
}

FText UWLCraftingViewModel::GetCraftButtonLabel() const
{
	const int32 Max = Recipes.IsValidIndex(SelectedIndex) ? Recipes[SelectedIndex].MaxCraftable : 0;
	return FWLUILogic::CraftButtonLabel(Quantity, Max);
}

void UWLCraftingViewModel::Craft()
{
	AWLPlayerCharacter* Body = Pawn.Get();
	UWLSkillingComponent* Sk = Body ? Body->GetSkilling() : nullptr;
	if (!Sk || !Recipes.IsValidIndex(SelectedIndex))
	{
		return;
	}
	// "All" = INT32_MAX: the server crafts until materials run out (it clamps and validates everything).
	const int32 Count = Quantity < 0 ? INT32_MAX : FMath::Max(1, Quantity);
	Sk->ServerCraft(Recipes[SelectedIndex].RecipeRowName, Count);
}

void UWLCraftingViewModel::Close()
{
	if (AWLPlayerCharacter* Body = Pawn.Get())
	{
		if (UWLSkillingComponent* Sk = Body->GetSkilling())
		{
			Sk->CloseStation();
		}
	}
	UE_MVVM_SET_PROPERTY_VALUE(bOpen, false);
	NotifyChanged();
}

// =================================================================================================
// Skills
// =================================================================================================

FText UWLSkillsViewModel::SkillDisplayName(const UObject* WorldContext, EWLSkill Skill)
{
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(WorldContext);
	const FWLSkillRow* Row = Data ? Data->FindSkill(Skill) : nullptr;
	return (Row && !Row->DisplayName.IsEmpty()) ? Row->DisplayName : FText::FromString(WLTypes::SkillToString(Skill));
}

void UWLSkillsViewModel::OnBind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetSkills()->OnSkillsChanged.AddUObject(this, &UWLSkillsViewModel::HandleSkillsChanged);
	}
	Refresh();
}

void UWLSkillsViewModel::OnUnbind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetSkills()->OnSkillsChanged.RemoveAll(this);
	}
}

void UWLSkillsViewModel::Refresh()
{
	Skills.Reset();
	const AWLPlayerState* P = PS.Get();
	const UWLSkillsComponent* Sk = P ? P->GetSkills() : nullptr;
	const UWLGameUserSettings* Settings = UWLGameUserSettings::GetWLSettings();
	if (Sk)
	{
		for (EWLSkill S : TEnumRange<EWLSkill>())
		{
			FWLSkillView& View = Skills.AddDefaulted_GetRef();
			View.Skill = S;
			View.DisplayName = Sk->GetSkillDisplayName(S);
			View.Level = Sk->GetLevel(S);
			View.XP = Sk->GetXP(S);
			View.XPToNext = Sk->GetXPToNextLevel(S);
			View.Progress = Sk->GetLevelProgress(S);
			View.bTracked = Settings->bHasTrackedSkill && Settings->TrackedSkill == S;
		}
		UE_MVVM_SET_PROPERTY_VALUE(TotalLevel, Sk->GetTotalLevel());
		UE_MVVM_SET_PROPERTY_VALUE(CombatLevel, Sk->GetCombatLevel());
	}
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Skills);
	NotifyChanged();
}

void UWLSkillsViewModel::ToggleTracked(EWLSkill Skill)
{
	UWLGameUserSettings* Settings = UWLGameUserSettings::GetWLSettings();
	if (Settings->bHasTrackedSkill && Settings->TrackedSkill == Skill)
	{
		Settings->bHasTrackedSkill = false;
	}
	else
	{
		Settings->TrackedSkill = Skill;
		Settings->bHasTrackedSkill = true;
	}
	Settings->SaveSettings();

	if (UWLUISubsystem* Root = UI.Get())
	{
		Root->GetChat()->AddLocalLine(Settings->bHasTrackedSkill
			? FText::Format(LOCTEXT("Tracking", "[TRACK]: Now tracking {0} on your HUD."), SkillDisplayName(PS.Get(), Skill))
			: LOCTEXT("TrackingOff", "[TRACK]: Skill tracking off."));
	}
	Refresh();
}

FText UWLSkillsViewModel::GetTooltip(EWLSkill Skill) const
{
	const AWLPlayerState* P = PS.Get();
	const UWLSkillsComponent* Sk = P ? P->GetSkills() : nullptr;
	if (!Sk)
	{
		return FText::GetEmpty();
	}
	const int32 Level = Sk->GetLevel(Skill);
	if (Level >= FWLXPTable::MaxLevelFor(Skill))
	{
		return FText::Format(LOCTEXT("TooltipMax", "{0}\nXP: {1}\nMaximum level"), Sk->GetSkillDisplayName(Skill), FText::AsNumber(Sk->GetXP(Skill)));
	}
	return FText::Format(LOCTEXT("Tooltip", "{0}\nXP: {1}\nNext level at: {2}\nRemaining: {3}"),
		Sk->GetSkillDisplayName(Skill), FText::AsNumber(Sk->GetXP(Skill)),
		FText::AsNumber(FWLXPTable::XPForLevel(Skill, Level + 1)), FText::AsNumber(Sk->GetXPToNextLevel(Skill)));
}

// =================================================================================================
// XP drops
// =================================================================================================

void UWLXPDropViewModel::OnBind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		// These fire on every client from OnRep; we only ever bind the LOCAL player's PlayerState.
		P->GetSkills()->OnXPGained.AddUObject(this, &UWLXPDropViewModel::HandleXPGained);
		P->GetSkills()->OnLevelUp.AddUObject(this, &UWLXPDropViewModel::HandleLevelUp);
	}
	Drops.Reset();
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Drops);
}

void UWLXPDropViewModel::OnUnbind()
{
	if (AWLPlayerState* P = PS.Get())
	{
		P->GetSkills()->OnXPGained.RemoveAll(this);
		P->GetSkills()->OnLevelUp.RemoveAll(this);
	}
}

void UWLXPDropViewModel::HandleXPGained(EWLSkill Skill, int32 Amount)
{
	const AWLPlayerState* P = PS.Get();
	if (!P || (!P->IsCharacterLoaded() && P->HasAuthority()))
	{
		// Listen server applying the save: not a gain.
		return;
	}
	// Unity colour-split "+N <pale>Skill</pale>"; the skill name is the enum name like Unity.
	FWLUILogic::PushXPDrop(Drops, Skill, Amount, FText::FromString(WLTypes::SkillToString(Skill)));
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Drops);
	NotifyChanged();
}

void UWLXPDropViewModel::HandleLevelUp(EWLSkill Skill, int32 NewLevel)
{
	const AWLPlayerState* P = PS.Get();
	if (!P)
	{
		return;
	}
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(P);
	const FWLSkillRow* Row = Data ? Data->FindSkill(Skill) : nullptr;
	const FText Name = P->GetSkills()->GetSkillDisplayName(Skill);

	// OSRS-style level-up: a chat shout, the jingle, and the on-screen congratulations.
	if (UWLUISubsystem* Root = UI.Get())
	{
		const FText Flavour = (Row && !Row->LevelUpFlavour.IsEmpty()) ? Row->LevelUpFlavour : FText::GetEmpty();
		Root->GetChat()->AddLocalLine(FText::Format(LOCTEXT("LevelUpChat", "Congratulations! You've reached {0} level {1}.  {2}"),
			Name, FText::AsNumber(NewLevel), Flavour));
	}
	UWLAudioEvents::PlayEvent(P, TEXT("LevelUp"));

	WL_SET_TEXT(CelebrationText, FText::Format(LOCTEXT("LevelUpBanner", "Congratulations!\n{0} level {1}"), Name, FText::AsNumber(NewLevel)));
	UE_MVVM_SET_PROPERTY_VALUE(CelebrationTimeLeft, FWLUILogic::CelebrationSeconds);
	NotifyChanged();
}

void UWLXPDropViewModel::TickViewModel(float DeltaSeconds)
{
	if (Drops.Num() == 0 && CelebrationTimeLeft <= 0.f)
	{
		return;
	}
	FWLUILogic::TickXPDrops(Drops, DeltaSeconds);
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Drops);
	UE_MVVM_SET_PROPERTY_VALUE(CelebrationTimeLeft, FMath::Max(0.f, CelebrationTimeLeft - DeltaSeconds));
	NotifyChanged();
}

#undef WL_SET_TEXT
#undef LOCTEXT_NAMESPACE
