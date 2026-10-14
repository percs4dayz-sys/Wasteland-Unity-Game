#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Core/WLTypes.h"
#include "Items/WLItemTypes.h"
#include "Items/WLItemContainerComponent.h"
#include "WLEquipmentComponent.generated.h"

struct FWLItemRow;
struct FWLCharacterSave;
class AWLPlayerState;

/**
 * Worn gear. Ported from Assets/Scripts/Items/Equipment.cs (+ GearSets.cs for set bonuses and
 * ModuleCatalog.Power for module effects). Replaces Unity's string-keyed dictionaries with one
 * entry per EWLEquipSlot.
 *
 * Replication: the worn items and their modules replicate to EVERYONE (other players render your
 * gear); the ammo count is COND_OwnerOnly (nobody else needs it, and it changes every shot).
 *
 * Also hosts the Magnet module auto-pickup (Unity ModuleEffects.Update): while a Magnet module is
 * worn the server sweeps nearby resource drops every 0.4 s.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLEquipmentComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLEquipmentComponent();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	// --- queries (everyone) ---------------------------------------------------------------------

	/** Equipped item id, 0 = empty. */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Equipment")
	int32 GetItemId(EWLEquipSlot Slot) const;

	/** Equipped item row, or null. */
	const FWLItemRow* GetItem(EWLEquipSlot Slot) const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Equipment")
	bool IsEquipped(EWLEquipSlot Slot) const { return GetItemId(Slot) > 0; }

	/** Socketed module ids for the worn copy (0 = empty socket). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Equipment")
	TArray<int32> GetModules(EWLEquipSlot Slot) const;

	/** Equipment.GetStack: the worn copy as a stack (ammo carries the ammo count). Empty if nothing worn. */
	FWLItemStack GetStack(EWLEquipSlot Slot) const;

	/** Owner/server only (COND_OwnerOnly). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Equipment")
	int32 GetAmmoQuantity() const { return AmmoQuantity; }

	/** Cosmetic weapon finish currently rendered on the held weapon (NAME_None = default look). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Equipment")
	FName GetAppliedWeaponSkin() const { return AppliedWeaponSkin; }

	// --- combat totals (Equipment.cs; WORN gear only, excludes Ammo and Tool) --------------------

	/** Equipment.TotalAttackBonus: every worn piece + socketed modules. */
	int32 TotalAttackBonus() const;

	/** Weapons always count; armour only when its line matches the style or it is style-neutral. */
	int32 TotalAttackBonusFor(EWLWeaponStyle Style) const;
	int32 TotalStrengthBonus() const;
	int32 TotalDefenceBonus() const;

	/** Ranged strength from the worn ammo. Ranged max hit only, never melee. */
	int32 AmmoStrengthBonus() const;

	/** GearSets.DamageMultiplier for a completed set of this style (>= 1). */
	float GetSetDamageMultiplier(EWLWeaponStyle Style) const;

	/**
	 * GearSets.BonusDefence. Unity applies the best COMPLETED set's defence whatever style you are
	 * using ("you're wearing it regardless"), so Style is accepted for the API but not used.
	 */
	int32 GetSetBonusDefence(EWLWeaponStyle Style = EWLWeaponStyle::None) const;

	/** ModuleCatalog.Power: summed grades of worn modules with this effect (Bleed, Salvage, Magnet...), capped at 4. */
	int32 ModulePower(FName Effect) const;

	// --- SERVER mutation ----------------------------------------------------------------------

	/** Equipment.Equip: put an item (with its modules) in a slot, replacing whatever was there. */
	void Equip(EWLEquipSlot Slot, int32 ItemId, const TArray<int32>& Modules);

	/** Equipment.EquipAmmo: fresh ammo stack (replaces the slot). */
	void EquipAmmo(int32 ItemId, int32 Quantity);

	/** Equipment.AddAmmo. */
	void AddAmmo(int32 Quantity);

	/** Spend ammo when firing. Clears the slot at zero. False if not enough. */
	bool ConsumeAmmo(int32 Quantity = 1);

	/** Equipment.Unequip: returns the removed item id (0 if nothing). */
	int32 Unequip(EWLEquipSlot Slot);

	/** Equipment.SetModules for the worn copy. */
	void SetModules(EWLEquipSlot Slot, const TArray<int32>& Modules);

	void Clear();

	/** Re-evaluate the rendered weapon skin (after equip changes and skin selection). */
	void RefreshAppliedWeaponSkin();

	void CaptureSave(FWLCharacterSave& Out) const;
	void ApplySave(const FWLCharacterSave& In);

	// --- client intent ----------------------------------------------------------------------------

	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Equipment")
	void ServerUnequip(EWLEquipSlot Slot);

	/** Install (ModuleItemId > 0) or remove (0) a module in a socket of the worn item. */
	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Equipment")
	void ServerSocketModule(EWLEquipSlot Slot, int32 SocketIndex, int32 ModuleItemId);

	/** WeaponSkins K key: cycle the finish for the weapon in hand. */
	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Equipment")
	void ServerCycleWeaponSkin();

	/** Fires on the server after every change and on every client after replication. */
	FWLOnItemContainerChanged OnEquipmentChanged;

	/** Seconds between Magnet sweeps (Unity 0.4). */
	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Modules")
	float MagnetSweepInterval = 0.4f;

	/** Pickup radius per Magnet power in uu (Unity 2 m per point). */
	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Modules")
	float MagnetRadiusPerPower = 200.f;

protected:
	UFUNCTION()
	void OnRep_Equipped();

	UFUNCTION()
	void OnRep_AmmoQuantity();

	UFUNCTION()
	void OnRep_AppliedWeaponSkin();

	void NotifyChanged();
	void UpdateMagnetTimer();
	void MagnetSweep();
	int32 SumWorn(TFunctionRef<int32(const FWLItemRow&)> Select) const;
	int32 ModuleBonus(TFunctionRef<int32(const FWLItemRow&)> Select) const;
	void GetWornRows(TArray<const FWLItemRow*, TInlineAllocator<8>>& Out) const;
	bool IsServer() const;
	AWLPlayerState* GetOwningPlayerState() const;
	void Report(const FText& Message) const;

	/** One entry per EWLEquipSlot (Quantity 1 when worn; ammo count lives in AmmoQuantity). */
	UPROPERTY(ReplicatedUsing = OnRep_Equipped)
	TArray<FWLItemStack> Equipped;

	UPROPERTY(ReplicatedUsing = OnRep_AmmoQuantity)
	int32 AmmoQuantity = 0;

	UPROPERTY(ReplicatedUsing = OnRep_AppliedWeaponSkin)
	FName AppliedWeaponSkin;

	FTimerHandle MagnetTimer;
};
