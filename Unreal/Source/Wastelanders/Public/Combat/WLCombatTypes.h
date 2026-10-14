#pragma once

#include "CoreMinimal.h"
#include "WLCombatTypes.generated.h"

/**
 * Module proc effects, 1:1 with Unity's ModuleCatalog.Effect (Assets/Scripts/Items/ModuleCatalog.cs).
 * The enumerator NAMES are data: FWLModuleRow::Effect stores them as FNames and
 * UWLEquipmentComponent::ModulePower(FName) looks them up by name. Append only.
 */
UENUM(BlueprintType)
enum class EWLModuleEffect : uint8
{
	Bleed,
	HasteProc,
	Salvage,
	Visor,
	Plates,
	Gather,
	Reflect,
	Critical,
	Power,
	Serrated,
	Targeting,
	HeavyGuard,
	EatRecovery,
	Block,
	Capacitor,
	Overclock,
	DoubleStrike,
	Scanner,
	Reactive,
	Magnet,
	Barrier,
	Resonance,
	MissAdapt,
	Leech,
	Processor,
	Ballistic,
	Recovery,
	Deflect,
	Adaptive,
	Combo,
	Burst,
	Tactical,
	Emergency,
	Servo,
	Nano,
	Count UMETA(Hidden)
};
ENUM_RANGE_BY_COUNT(EWLModuleEffect, EWLModuleEffect::Count);

/** What the enemy brain is doing (Enemy3D's implicit states made explicit). */
UENUM(BlueprintType)
enum class EWLEnemyAIState : uint8
{
	Idle,    // standing at home between strolls
	Wander,  // ambling to a point near home
	Chase,   // closing on its target
	Attack,  // in reach: winding up / swinging on cooldown
	Return,  // lost interest, walking home
	Dead
};

/**
 * One hitsplat (Unity CombatFeedbackUI.ShowWorldSplat). Combat on the server multicasts these
 * unreliably; UWLCombatFeedbackSubsystem re-broadcasts them locally for the HUD.
 */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLHitsplat
{
	GENERATED_BODY()

	/** Who got hit (an enemy or a player pawn). */
	UPROPERTY(BlueprintReadOnly, Category = "Combat")
	TWeakObjectPtr<AActor> Target;

	UPROPERTY(BlueprintReadOnly, Category = "Combat")
	int32 Amount = 0;

	/** false = blue "0" miss splat; true = red damage splat (Amount may still be 0 on a landed 0 roll). */
	UPROPERTY(BlueprintReadOnly, Category = "Combat")
	bool bHit = false;

	/** Optional callout: HEADSHOT, MELTDOWN, POWER, FIRST STRIKE, BLEED, REFLECT... NAME_None = plain. */
	UPROPERTY(BlueprintReadOnly, Category = "Combat")
	FName Tag;
};

namespace WLModules
{
	/** Enum -> the FName used by FWLModuleRow::Effect / UWLEquipmentComponent::ModulePower. */
	WASTELANDERS_API FName EffectName(EWLModuleEffect Effect);
}
