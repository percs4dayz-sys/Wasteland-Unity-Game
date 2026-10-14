#include "Items/WLEquipmentComponent.h"
#include "Items/WLItemRules.h"
#include "Items/WLItemActions.h"
#include "Items/WLWeaponSkins.h"
#include "Items/WLGroundItem.h"
#include "Items/WLLootSettings.h"
#include "Data/WLDataSubsystem.h"
#include "Persistence/WLCharacterSave.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "GameFramework/Pawn.h"
#include "Engine/World.h"
#include "TimerManager.h"
#include "Net/UnrealNetwork.h"

static int32 Equip_SlotIdx(EWLEquipSlot Slot) { return static_cast<int32>(Slot); }

UWLEquipmentComponent::UWLEquipmentComponent()
{
	PrimaryComponentTick.bCanEverTick = false;
	SetIsReplicatedByDefault(true);
	Equipped.SetNum(static_cast<int32>(EWLEquipSlot::Count));
}

void UWLEquipmentComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(UWLEquipmentComponent, Equipped);                 // everyone renders your gear
	DOREPLIFETIME(UWLEquipmentComponent, AppliedWeaponSkin);        // and your weapon finish
	DOREPLIFETIME_CONDITION(UWLEquipmentComponent, AmmoQuantity, COND_OwnerOnly);
}

void UWLEquipmentComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (const UWorld* World = GetWorld())
	{
		World->GetTimerManager().ClearTimer(MagnetTimer);
	}
	Super::EndPlay(EndPlayReason);
}

bool UWLEquipmentComponent::IsServer() const
{
	const AActor* Owner = GetOwner();
	return Owner && Owner->HasAuthority();
}

AWLPlayerState* UWLEquipmentComponent::GetOwningPlayerState() const
{
	return Cast<AWLPlayerState>(GetOwner());
}

void UWLEquipmentComponent::Report(const FText& Message) const
{
	const AWLPlayerState* PS = GetOwningPlayerState();
	if (AWLPlayerController* PC = PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr)
	{
		if (!Message.IsEmpty())
		{
			PC->ClientGameMessage(Message);
		}
	}
}

// --- queries ---------------------------------------------------------------------------------

int32 UWLEquipmentComponent::GetItemId(EWLEquipSlot Slot) const
{
	const int32 I = Equip_SlotIdx(Slot);
	return Equipped.IsValidIndex(I) && !Equipped[I].IsEmpty() ? Equipped[I].ItemId : 0;
}

const FWLItemRow* UWLEquipmentComponent::GetItem(EWLEquipSlot Slot) const
{
	const int32 Id = GetItemId(Slot);
	const UWLDataSubsystem* Data = Id > 0 ? UWLDataSubsystem::Get(this) : nullptr;
	return Data ? Data->FindItem(Id) : nullptr;
}

TArray<int32> UWLEquipmentComponent::GetModules(EWLEquipSlot Slot) const
{
	const int32 I = Equip_SlotIdx(Slot);
	return Equipped.IsValidIndex(I) && !Equipped[I].IsEmpty() ? Equipped[I].Modules : TArray<int32>();
}

FWLItemStack UWLEquipmentComponent::GetStack(EWLEquipSlot Slot) const
{
	const int32 Id = GetItemId(Slot);
	if (Id <= 0)
	{
		return FWLItemStack();
	}
	return FWLItemStack(Id, Slot == EWLEquipSlot::Ammo ? AmmoQuantity : 1, GetModules(Slot));
}

void UWLEquipmentComponent::GetWornRows(TArray<const FWLItemRow*, TInlineAllocator<8>>& Out) const
{
	for (EWLEquipSlot Slot : TEnumRange<EWLEquipSlot>())
	{
		if (const FWLItemRow* Row = GetItem(Slot))
		{
			Out.Add(Row);
		}
	}
}

int32 UWLEquipmentComponent::SumWorn(TFunctionRef<int32(const FWLItemRow&)> Select) const
{
	int32 Total = 0;
	for (EWLEquipSlot Slot : TEnumRange<EWLEquipSlot>())
	{
		if (Slot == EWLEquipSlot::Ammo || Slot == EWLEquipSlot::Tool)
		{
			continue; // worn combat gear only
		}
		if (const FWLItemRow* Row = GetItem(Slot))
		{
			Total += Select(*Row);
		}
	}
	return Total;
}

int32 UWLEquipmentComponent::ModuleBonus(TFunctionRef<int32(const FWLItemRow&)> Select) const
{
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	if (!Data)
	{
		return 0;
	}
	int32 Total = 0;
	for (EWLEquipSlot Slot : TEnumRange<EWLEquipSlot>())
	{
		if (!IsEquipped(Slot))
		{
			continue;
		}
		for (const int32 Id : GetModules(Slot))
		{
			if (const FWLItemRow* Module = Id > 0 ? Data->FindItem(Id) : nullptr)
			{
				Total += Select(*Module);
			}
		}
	}
	return Total;
}

int32 UWLEquipmentComponent::TotalAttackBonus() const
{
	return SumWorn([](const FWLItemRow& I) { return I.AttackBonus; }) + ModuleBonus([](const FWLItemRow& I) { return I.AttackBonus; });
}

int32 UWLEquipmentComponent::TotalAttackBonusFor(EWLWeaponStyle Style) const
{
	// A Marksman rig sharpens your shooting without also sharpening a sword you happen to hold.
	return SumWorn([Style](const FWLItemRow& I)
	{
		if (I.AttackBonus == 0) return 0;
		if (!I.IsArmour()) return I.AttackBonus;                         // weapons / tools: always
		if (I.ArmourStyle == EWLWeaponStyle::None) return I.AttackBonus;  // style-neutral gear
		return I.ArmourStyle == Style ? I.AttackBonus : 0;
	}) + ModuleBonus([](const FWLItemRow& I) { return I.AttackBonus; });
}

int32 UWLEquipmentComponent::TotalStrengthBonus() const
{
	return SumWorn([](const FWLItemRow& I) { return I.StrengthBonus; }) + ModuleBonus([](const FWLItemRow& I) { return I.StrengthBonus; });
}

int32 UWLEquipmentComponent::TotalDefenceBonus() const
{
	return SumWorn([](const FWLItemRow& I) { return I.DefenceBonus; }) + ModuleBonus([](const FWLItemRow& I) { return I.DefenceBonus; });
}

int32 UWLEquipmentComponent::AmmoStrengthBonus() const
{
	const FWLItemRow* Ammo = GetItem(EWLEquipSlot::Ammo);
	return Ammo ? Ammo->StrengthBonus : 0;
}

float UWLEquipmentComponent::GetSetDamageMultiplier(EWLWeaponStyle Style) const
{
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	if (!Data)
	{
		return 1.f;
	}
	TArray<const FWLItemRow*, TInlineAllocator<8>> Worn;
	GetWornRows(Worn);
	return FWLGearSetRules::DamageMultiplier(Worn, Style, [Data](FName Id) { return Data->FindGearSet(Id); });
}

int32 UWLEquipmentComponent::GetSetBonusDefence(EWLWeaponStyle /*Style*/) const
{
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	if (!Data)
	{
		return 0;
	}
	TArray<const FWLItemRow*, TInlineAllocator<8>> Worn;
	GetWornRows(Worn);
	return FWLGearSetRules::BonusDefence(Worn, [Data](FName Id) { return Data->FindGearSet(Id); });
}

int32 UWLEquipmentComponent::ModulePower(FName Effect) const
{
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	if (!Data || Effect.IsNone())
	{
		return 0;
	}
	int32 Result = 0;
	for (EWLEquipSlot Slot : TEnumRange<EWLEquipSlot>())
	{
		if (!IsEquipped(Slot))
		{
			continue;
		}
		for (const int32 Id : GetModules(Slot))
		{
			// Only named catalogue modules carry an effect; legacy 500-series are pure stats.
			const FWLModuleRow* Row = Id > 0 ? Data->FindModule(Id) : nullptr;
			if (Row && Row->Effect == Effect)
			{
				Result += FWLModuleRules::Grade(Id, Row);
			}
		}
	}
	return FMath::Min(Result, FWLModuleRules::MaxPower);
}

// --- mutation --------------------------------------------------------------------------------

void UWLEquipmentComponent::NotifyChanged()
{
	if (IsServer())
	{
		RefreshAppliedWeaponSkin();
		UpdateMagnetTimer();
	}
	OnEquipmentChanged.Broadcast();
}

void UWLEquipmentComponent::Equip(EWLEquipSlot Slot, int32 ItemId, const TArray<int32>& Modules)
{
	const int32 I = Equip_SlotIdx(Slot);
	if (!IsServer() || !Equipped.IsValidIndex(I) || ItemId <= 0)
	{
		return;
	}
	Equipped[I] = FWLItemStack(ItemId, 1, Modules);
	NotifyChanged();
}

void UWLEquipmentComponent::EquipAmmo(int32 ItemId, int32 Quantity)
{
	if (!IsServer() || ItemId <= 0 || Quantity <= 0)
	{
		return;
	}
	Equipped[Equip_SlotIdx(EWLEquipSlot::Ammo)] = FWLItemStack(ItemId, 1);
	AmmoQuantity = Quantity;
	NotifyChanged();
}

void UWLEquipmentComponent::AddAmmo(int32 Quantity)
{
	if (!IsServer() || Quantity <= 0 || !IsEquipped(EWLEquipSlot::Ammo))
	{
		return;
	}
	AmmoQuantity += Quantity;
	NotifyChanged();
}

bool UWLEquipmentComponent::ConsumeAmmo(int32 Quantity)
{
	if (!IsServer() || !IsEquipped(EWLEquipSlot::Ammo) || AmmoQuantity < Quantity)
	{
		return false;
	}
	AmmoQuantity -= Quantity;
	if (AmmoQuantity <= 0)
	{
		Equipped[Equip_SlotIdx(EWLEquipSlot::Ammo)].Reset();
		AmmoQuantity = 0;
		NotifyChanged();
	}
	else
	{
		// Only the count changed: skip the skin / magnet refresh, just tell listeners.
		OnEquipmentChanged.Broadcast();
	}
	return true;
}

int32 UWLEquipmentComponent::Unequip(EWLEquipSlot Slot)
{
	const int32 Id = GetItemId(Slot);
	if (!IsServer() || Id <= 0)
	{
		return 0;
	}
	Equipped[Equip_SlotIdx(Slot)].Reset();
	if (Slot == EWLEquipSlot::Ammo)
	{
		AmmoQuantity = 0;
	}
	NotifyChanged();
	return Id;
}

void UWLEquipmentComponent::SetModules(EWLEquipSlot Slot, const TArray<int32>& Modules)
{
	const int32 I = Equip_SlotIdx(Slot);
	if (!IsServer() || !Equipped.IsValidIndex(I) || Equipped[I].IsEmpty())
	{
		return;
	}
	Equipped[I].Modules = Modules;
	NotifyChanged();
}

void UWLEquipmentComponent::Clear()
{
	if (!IsServer())
	{
		return;
	}
	for (FWLItemStack& S : Equipped)
	{
		S.Reset();
	}
	AmmoQuantity = 0;
	NotifyChanged();
}

void UWLEquipmentComponent::RefreshAppliedWeaponSkin()
{
	if (!IsServer())
	{
		return;
	}
	const FWLWeaponSkinDef* Skin = FWLWeaponSkins::AppliedFor(GetOwningPlayerState(), GetItem(EWLEquipSlot::Weapon));
	const FName NewSkin = Skin ? Skin->Id : NAME_None;
	if (NewSkin != AppliedWeaponSkin)
	{
		AppliedWeaponSkin = NewSkin;
		OnEquipmentChanged.Broadcast(); // Unity Equipment.RaiseChanged: rebuild the held model
	}
}

void UWLEquipmentComponent::OnRep_Equipped()
{
	OnEquipmentChanged.Broadcast();
}

void UWLEquipmentComponent::OnRep_AmmoQuantity()
{
	OnEquipmentChanged.Broadcast();
}

void UWLEquipmentComponent::OnRep_AppliedWeaponSkin()
{
	OnEquipmentChanged.Broadcast();
}

// --- Magnet module (ModuleEffects.Update) ------------------------------------------------------

void UWLEquipmentComponent::UpdateMagnetTimer()
{
	UWorld* World = GetWorld();
	if (!World)
	{
		return;
	}
	FTimerManager& Timers = World->GetTimerManager();
	const bool bWant = ModulePower(TEXT("Magnet")) > 0;
	if (bWant && !Timers.IsTimerActive(MagnetTimer))
	{
		Timers.SetTimer(MagnetTimer, this, &UWLEquipmentComponent::MagnetSweep, FMath::Max(0.1f, MagnetSweepInterval), true);
	}
	else if (!bWant && Timers.IsTimerActive(MagnetTimer))
	{
		Timers.ClearTimer(MagnetTimer);
	}
}

void UWLEquipmentComponent::MagnetSweep()
{
	AWLPlayerState* PS = GetOwningPlayerState();
	const APawn* Pawn = PS ? PS->GetPawn() : nullptr;
	const UWLGroundItemSubsystem* Ground = UWLGroundItemSubsystem::Get(this);
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	if (!Pawn || !Ground || !Data)
	{
		return;
	}
	if (const UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem())
	{
		if (ASC->IsDead())
		{
			return; // Unity: no pickups while down
		}
	}
	const int32 Power = ModulePower(TEXT("Magnet"));
	if (Power <= 0)
	{
		return;
	}
	for (AWLGroundItem* Loot : Ground->FindInRadius(Pawn->GetActorLocation(), MagnetRadiusPerPower * Power))
	{
		// Material drops only (resource type): never vacuums gear.
		const FWLItemRow* Row = Loot ? Data->FindItem(Loot->GetStack().ItemId) : nullptr;
		if (Row && Row->ItemType == EWLItemType::Resource)
		{
			Loot->TryPickUp(PS, /*bSilentFull*/ true);
		}
	}
}

// --- persistence -----------------------------------------------------------------------------

void UWLEquipmentComponent::CaptureSave(FWLCharacterSave& Out) const
{
	Out.Equipment.Reset();
	for (EWLEquipSlot Slot : TEnumRange<EWLEquipSlot>())
	{
		const int32 Id = GetItemId(Slot);
		if (Id > 0)
		{
			FWLSaveEquip& Entry = Out.Equipment.AddDefaulted_GetRef();
			Entry.Slot = WLTypes::SlotToString(Slot);
			Entry.ItemId = Id;
			Entry.Modules = GetModules(Slot);
		}
	}
	Out.AmmoQuantity = AmmoQuantity;
}

void UWLEquipmentComponent::ApplySave(const FWLCharacterSave& In)
{
	if (!IsServer())
	{
		return;
	}
	for (FWLItemStack& S : Equipped)
	{
		S.Reset();
	}
	AmmoQuantity = 0;

	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	for (const FWLSaveEquip& Entry : In.Equipment)
	{
		// Unknown (removed) items are dropped; missing module arrays mean empty sockets.
		const FWLItemRow* Row = (Entry.ItemId > 0 && Data) ? Data->FindItem(Entry.ItemId) : nullptr;
		EWLEquipSlot Slot;
		if (!Row || !WLTypes::SlotForItemType(Row->ItemType, Slot))
		{
			continue;
		}
		if (Slot == EWLEquipSlot::Ammo)
		{
			if (In.AmmoQuantity > 0)
			{
				Equipped[Equip_SlotIdx(Slot)] = FWLItemStack(Entry.ItemId, 1);
				AmmoQuantity = In.AmmoQuantity;
			}
		}
		else
		{
			Equipped[Equip_SlotIdx(Slot)] = FWLItemStack(Entry.ItemId, 1, Entry.Modules);
		}
	}
	NotifyChanged();

	// Flags (skin unlocks / selection) are applied after equipment; re-evaluate the skin next tick.
	if (UWorld* World = GetWorld())
	{
		World->GetTimerManager().SetTimerForNextTick(FTimerDelegate::CreateWeakLambda(this, [this]() { RefreshAppliedWeaponSkin(); }));
	}
}

// --- RPCs ------------------------------------------------------------------------------------

void UWLEquipmentComponent::ServerUnequip_Implementation(EWLEquipSlot Slot)
{
	if (Slot >= EWLEquipSlot::Count)
	{
		return;
	}
	if (AWLPlayerState* PS = GetOwningPlayerState())
	{
		Report(FWLItemActions::Unequip(PS, Slot));
	}
}

void UWLEquipmentComponent::ServerSocketModule_Implementation(EWLEquipSlot Slot, int32 SocketIndex, int32 ModuleItemId)
{
	if (Slot >= EWLEquipSlot::Count || ModuleItemId < 0)
	{
		return;
	}
	if (AWLPlayerState* PS = GetOwningPlayerState())
	{
		Report(FWLItemActions::Socket(PS, Slot, SocketIndex, ModuleItemId));
	}
}

void UWLEquipmentComponent::ServerCycleWeaponSkin_Implementation()
{
	if (AWLPlayerState* PS = GetOwningPlayerState())
	{
		Report(FWLWeaponSkins::CycleHeldWeaponSkin(PS));
	}
}
