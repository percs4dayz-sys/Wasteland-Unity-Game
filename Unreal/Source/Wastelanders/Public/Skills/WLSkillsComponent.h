#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Core/WLTypes.h"
#include "WLSkillsComponent.generated.h"

class AWLPlayerState;
struct FWLCharacterSave;

DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnSkillsChanged, class UWLSkillsComponent*);
DECLARE_MULTICAST_DELEGATE_TwoParams(FWLOnSkillXPGained, EWLSkill, int32 /*Amount*/);
DECLARE_MULTICAST_DELEGATE_TwoParams(FWLOnSkillLevelUp, EWLSkill, int32 /*NewLevel*/);

/**
 * The character's 13 skills (Unity PlayerStats.cs + SkillData.cs). Lives on AWLPlayerState.
 *
 * XP is the only stored value; levels come from FWLXPTable. XP replicates to everyone because the
 * combat level and total level are public (nameplates, inspect). Every delegate fires on the server
 * and on every client: clients diff the old and new XP in OnRep to re-derive XP drops and level-ups.
 *
 * Endurance IS hit points: its level is pushed into the ASC's MaxHealth (identity 10-220), and a
 * level-up heals the HP it added, exactly like PlayerStats.AddXP.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLSkillsComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLSkillsComponent();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void BeginPlay() override;

	/** SERVER. Add XP; fires OnXPGained, and OnLevelUp + the chat line on a level-up. */
	void AddXP(EWLSkill Skill, int32 Amount);

	/** SERVER. Overwrite a skill's XP (dev tools, migrations). No events beyond OnSkillsChanged. */
	void SetXP(EWLSkill Skill, int32 XP);

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skills")
	int32 GetLevel(EWLSkill Skill) const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skills")
	int32 GetXP(EWLSkill Skill) const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skills")
	int32 GetTotalLevel() const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skills")
	int32 GetCombatLevel() const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skills")
	float GetLevelProgress(EWLSkill Skill) const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skills")
	int32 GetXPToNextLevel(EWLSkill Skill) const;

	/** Max HP = Endurance level. */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skills")
	int32 GetMaxHP() const;

	/** Every tutorial skill at level 2 (the portal off the tutorial island). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skills")
	bool IsReadyToLeaveTutorial() const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skills")
	TArray<EWLSkill> GetIncompleteTutorialSkills() const;

	/** Display name from DT_Skills ("Hit Points" for Endurance), falling back to the enum name. */
	FText GetSkillDisplayName(EWLSkill Skill) const;

	/** SERVER. */
	void CaptureSave(FWLCharacterSave& Out) const;
	void ApplySave(const FWLCharacterSave& In);

	FWLOnSkillsChanged OnSkillsChanged;
	FWLOnSkillXPGained OnXPGained;
	FWLOnSkillLevelUp OnLevelUp;

protected:
	UFUNCTION()
	void OnRep_SkillXP(const TArray<int32>& OldXP);

	/** Push Endurance into the ASC's MaxHealth. */
	void SyncMaxHealth(bool bHealDelta);

	void SendLevelUpMessage(EWLSkill Skill, int32 NewLevel) const;
	AWLPlayerState* GetWLPlayerState() const;

	/** Indexed by EWLSkill. Always exactly EWLSkill::Count entries. */
	UPROPERTY(ReplicatedUsing = OnRep_SkillXP)
	TArray<int32> SkillXP;

	/** Client: false until the first XP replication arrives (that one is a load, not a gain). */
	bool bReceivedInitialXP = false;
};
