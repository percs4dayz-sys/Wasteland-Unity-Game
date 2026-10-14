#pragma once

#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "WLCompanionTypes.generated.h"

class USkeletalMesh;
class UStaticMesh;

/**
 * One beast line (Combat/CompanionManager.cs SpeciesDef + EggChoiceUI.cs option). Grows with the
 * owner's Beastmastery level: stage 0 below 30, stage 1 at 30, stage 2 at 60.
 */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLCompanionSpecies
{
	GENERATED_BODY()

	/** Save / flag key: "wolf", "turtle", "bird" (flag "species_<key>"). Never rename. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	FName Key;

	/** Carrying this egg item incubates it into this species. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	int32 EggItemId = 0;

	/** Attack interval in game ticks. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	int32 AttackEveryTicks = 3;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	float DamageMult = 1.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	float HPMult = 1.f;

	/** Special max hit = normal max hit x this. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	float SpecialMult = 3.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	FText SpecialName;

	/** "Your mutant pup <verb> the Frog Marauder for 4." */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	FText Verb;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	FText HatchMessage;

	/** Name per growth stage (3). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	TArray<FText> StageNames;

	/** Body scale per growth stage (3). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	TArray<float> StageScale;

	/** Saddlebag inventory slots while it's out, per stage (3). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	TArray<int32> CarrySlots;

	/** Share of the owner's incoming damage it soaks while within ShareRange (shellback 35%, others 10%). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	float SharedDamageFraction = 0.10f;

	/** Spotter perk: +X% owner Marksmanship damage at stage 0, +MarksmanshipPerStage per stage (bird 10% / +5%). 0 = none. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	float MarksmanshipBonus = 0.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	float MarksmanshipPerStage = 0.f;

	/** Flying lines hover this high over the ground (uu). 0 = walks. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	float HoverHeight = 0.f;

	/** The one-time picker (EggChoiceUI). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	FText ChoiceTitle;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion", meta = (MultiLine = true))
	FText ChoiceBlurb;

	/** New-art-direction bodies per stage. Unity models are NOT ported; empty = placeholder shape. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Visual")
	TArray<TSoftObjectPtr<USkeletalMesh>> StageMeshes;

	int32 SafeCarrySlots(int32 Stage) const { return CarrySlots.IsValidIndex(Stage) ? CarrySlots[Stage] : 0; }
	float SafeScale(int32 Stage) const { return StageScale.IsValidIndex(Stage) ? StageScale[Stage] : 1.f; }
	FText SafeStageName(int32 Stage) const { return StageNames.IsValidIndex(Stage) ? StageNames[Stage] : FText::FromName(Key); }
};

/**
 * Beast definitions and tuning (CompanionManager.cs Defs + [Header("Tuning")] fields + constants).
 * Unity's values are the C++ defaults, so the class default object works with no asset.
 */
UCLASS(BlueprintType)
class WASTELANDERS_API UWLCompanionDefinitions : public UPrimaryDataAsset
{
	GENERATED_BODY()

public:
	UWLCompanionDefinitions();

	static const UWLCompanionDefinitions* GetDefaultDefinitions() { return GetDefault<UWLCompanionDefinitions>(); }

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	TArray<FWLCompanionSpecies> Species;

	/** Pre-rework saves (pup hatched, no species flag) behave as this line. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Companion")
	FName DefaultSpecies = TEXT("wolf");

	UPROPERTY(EditAnywhere, Category = "Tuning") float SpecialCooldownSeconds = 20.f;
	/** Downtime after being scattered. */
	UPROPERTY(EditAnywhere, Category = "Tuning") float RegroupSeconds = 12.f;
	/** Carry an egg this long and it hatches. */
	UPROPERTY(EditAnywhere, Category = "Tuning") float IncubateSeconds = 120.f;
	UPROPERTY(EditAnywhere, Category = "Tuning") float MaxStamina = 100.f;
	/** Stamina drain per second while out (fighting / idle). */
	UPROPERTY(EditAnywhere, Category = "Tuning") float StaminaDrainFighting = 6.f;
	UPROPERTY(EditAnywhere, Category = "Tuning") float StaminaDrainIdle = 1.5f;
	/** Regen per second while resting; doubled when the owner is at or below half HP. */
	UPROPERTY(EditAnywhere, Category = "Tuning") float StaminaRegen = 4.f;
	/** Can't be summoned below this stamina. */
	UPROPERTY(EditAnywhere, Category = "Tuning") float MinSummonStamina = 20.f;
	/** An exhausted beast rejoins on its own at this fraction of max stamina. */
	UPROPERTY(EditAnywhere, Category = "Tuning") float RejoinStaminaFraction = 0.8f;
	/** How close it must be to bite (uu). Unity PackReach 1.8 m. */
	UPROPERTY(EditAnywhere, Category = "Tuning") float PackReach = 180.f;
	/** Extra slack when checking bite range (uu). Unity +0.7 m. */
	UPROPERTY(EditAnywhere, Category = "Tuning") float BiteSlack = 70.f;
	/** How far from the owner it will join a fight already underway with no locked target (uu). Unity 14 m. */
	UPROPERTY(EditAnywhere, Category = "Tuning") float AssistRange = 1400.f;
	/** Damage sharing only while this close to the owner (uu). Unity 8 m. */
	UPROPERTY(EditAnywhere, Category = "Tuning") float ShareRange = 800.f;
	/** Growth stages. */
	UPROPERTY(EditAnywhere, Category = "Tuning") int32 Stage1Level = 30;
	UPROPERTY(EditAnywhere, Category = "Tuning") int32 Stage2Level = 60;
	/** Heel offset from the owner (uu, owner-relative world axes). Unity (-0.7, -0.7) m. */
	UPROPERTY(EditAnywhere, Category = "Tuning") FVector2D FollowOffset = FVector2D(-70.f, -70.f);
	/** Teleport to the owner when further than this (uu): the MMO world streams; never lose your pet. */
	UPROPERTY(EditAnywhere, Category = "Tuning") float LeashDistance = 3000.f;

	/** Placeholder body until new art exists (engine basic shape). */
	UPROPERTY(EditAnywhere, Category = "Visual") TSoftObjectPtr<UStaticMesh> PlaceholderMesh;

	const FWLCompanionSpecies* FindSpecies(FName Key) const;
	const FWLCompanionSpecies* FindByEgg(int32 EggItemId) const;

	// --- pure rules (unit tested) ---
	int32 StageForLevel(int32 BeastmasteryLevel) const { return BeastmasteryLevel >= Stage2Level ? 2 : BeastmasteryLevel >= Stage1Level ? 1 : 0; }
	/** round((15 + level) x hpMult). */
	static int32 MaxHPFor(int32 Level, float HPMult) { return FMath::RoundToInt((15 + Level) * HPMult); }
	/** max(1, round((1 + level x 0.2) x dmgMult)). */
	static int32 MaxHitFor(int32 Level, float DamageMult) { return FMath::Max(1, FMath::RoundToInt((1.f + Level * 0.2f) * DamageMult)); }
	/** max(2, round(maxHit x specialMult)). */
	static int32 SpecialMaxHitFor(int32 MaxHit, float SpecialMult) { return FMath::Max(2, FMath::RoundToInt(MaxHit * SpecialMult)); }
	static float MarksmanshipMultFor(const FWLCompanionSpecies& S, int32 Stage) { return S.MarksmanshipBonus > 0.f ? 1.f + S.MarksmanshipBonus + S.MarksmanshipPerStage * Stage : 1.f; }
	/** Shared portion of an incoming hit: min(currentHP, floor(damage x fraction)). */
	static int32 SharedDamage(int32 Damage, float Fraction, int32 CompanionHP) { return FMath::Min(CompanionHP, FMath::FloorToInt(Damage * Fraction)); }
};

/** Owner-only snapshot for the pet bar (CompanionManager.UpdateStatusUI). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLCompanionStatus
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "Companion") FName Species;
	UPROPERTY(BlueprintReadOnly, Category = "Companion") bool bHasCompanion = false;
	UPROPERTY(BlueprintReadOnly, Category = "Companion") bool bActive = false;
	UPROPERTY(BlueprintReadOnly, Category = "Companion") bool bIncubating = false;
	UPROPERTY(BlueprintReadOnly, Category = "Companion") bool bScattered = false;
	UPROPERTY(BlueprintReadOnly, Category = "Companion") uint8 IncubationPercent = 0;
	UPROPERTY(BlueprintReadOnly, Category = "Companion") uint8 StaminaPercent = 100;
	UPROPERTY(BlueprintReadOnly, Category = "Companion") int32 HP = 0;
	UPROPERTY(BlueprintReadOnly, Category = "Companion") int32 MaxHP = 0;
	UPROPERTY(BlueprintReadOnly, Category = "Companion") int32 Stage = 0;
	/** Whole seconds until the special is ready (0 = ready). */
	UPROPERTY(BlueprintReadOnly, Category = "Companion") int32 SpecialCooldown = 0;
	/** Whole seconds until a scattered beast regroups. */
	UPROPERTY(BlueprintReadOnly, Category = "Companion") int32 RegroupIn = 0;

	bool operator==(const FWLCompanionStatus& O) const
	{
		return Species == O.Species && bHasCompanion == O.bHasCompanion && bActive == O.bActive && bIncubating == O.bIncubating
			&& bScattered == O.bScattered && IncubationPercent == O.IncubationPercent && StaminaPercent == O.StaminaPercent
			&& HP == O.HP && MaxHP == O.MaxHP && Stage == O.Stage && SpecialCooldown == O.SpecialCooldown && RegroupIn == O.RegroupIn;
	}
	bool operator!=(const FWLCompanionStatus& O) const { return !(*this == O); }
};
