#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerState.h"
#include "AbilitySystemInterface.h"
#include "WLPlayerState.generated.h"

class UAbilitySystemComponent;
class UWLAbilitySystemComponent;
class UWLCombatAttributeSet;
class UWLSkillsComponent;
class UWLInventoryComponent;
class UWLEquipmentComponent;
class UWLBankComponent;
class UWLPlayerFlagsComponent;
class UWLQuestComponent;
class UWLCompanionComponent;
class UWLDialogueComponent;
struct FWLCharacterSave;

/**
 * The persistent character. Everything Unity kept on PlayerEntity (stats, inventory, equipment,
 * bank, flags) lives here as replicated components, so it survives pawn death/respawn and maps 1:1
 * onto FWLCharacterSave. The pawn (AWLPlayerCharacter) is just the body in the world.
 *
 * The Gameplay Ability System component also lives here (Lyra pattern), with the pawn as avatar.
 */
UCLASS()
class WASTELANDERS_API AWLPlayerState : public APlayerState, public IAbilitySystemInterface
{
	GENERATED_BODY()

public:
	AWLPlayerState();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

	// IAbilitySystemInterface
	virtual UAbilitySystemComponent* GetAbilitySystemComponent() const override;

	UWLAbilitySystemComponent* GetWLAbilitySystem() const { return AbilitySystem; }
	const UWLCombatAttributeSet* GetCombatAttributes() const { return CombatAttributes; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders") UWLSkillsComponent* GetSkills() const { return Skills; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders") UWLInventoryComponent* GetInventory() const { return Inventory; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders") UWLEquipmentComponent* GetEquipment() const { return Equipment; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders") UWLBankComponent* GetBank() const { return Bank; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders") UWLPlayerFlagsComponent* GetFlags() const { return Flags; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders") UWLQuestComponent* GetQuests() const { return Quests; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders") UWLCompanionComponent* GetCompanions() const { return Companions; }

	/** Persistence key, assigned by the server when the character is loaded or created. */
	UFUNCTION(BlueprintPure, Category = "Wastelanders")
	const FGuid& GetCharacterId() const { return CharacterId; }

	const FString& GetAccountId() const { return AccountId; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders")
	int32 GetTotalKills() const { return TotalKills; }

	/** SERVER. Unity PlayerEntity.IncrementKills. */
	void IncrementKills() { ++TotalKills; }

	/** OSRS-style combat level, from the skills component. */
	UFUNCTION(BlueprintPure, Category = "Wastelanders")
	int32 GetCombatLevel() const;

	/** SERVER. Gather the whole character into a save record. */
	void CaptureSave(FWLCharacterSave& Out) const;

	/** SERVER. Restore the whole character from a save record (before the pawn spawns). */
	void ApplySave(const FWLCharacterSave& In);

	/** Game-master rights (dev commands in shipping builds). Set by the server from the account record. */
	bool HasGMRights() const { return bGMRights; }
	void SetGMRights(bool bInGM) { bGMRights = bInGM; }

	/** True once ApplySave has run; the game mode waits for it before spawning the pawn. */
	bool IsCharacterLoaded() const { return bCharacterLoaded; }

	/** Character-creator choices, replicated so other clients can build the body. */
	UPROPERTY(ReplicatedUsing = OnRep_Appearance, BlueprintReadOnly, Category = "Wastelanders")
	TMap<FName, FString> Appearance;

protected:
	virtual void CopyProperties(APlayerState* PlayerState) override;

	UFUNCTION()
	void OnRep_Appearance();

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UWLAbilitySystemComponent> AbilitySystem;

	UPROPERTY()
	TObjectPtr<UWLCombatAttributeSet> CombatAttributes;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UWLSkillsComponent> Skills;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UWLInventoryComponent> Inventory;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UWLEquipmentComponent> Equipment;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UWLBankComponent> Bank;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UWLPlayerFlagsComponent> Flags;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UWLQuestComponent> Quests;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UWLCompanionComponent> Companions;

	/** Server-owned conversation state; the quest component finds it instead of creating one at runtime. */
	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UWLDialogueComponent> Dialogue;

	UPROPERTY(Replicated)
	FGuid CharacterId;

	/** Not replicated: other clients have no business knowing account ids. */
	FString AccountId;

	UPROPERTY(Replicated)
	int32 TotalKills = 0;

	bool bCharacterLoaded = false;
	bool bGMRights = false;
};
