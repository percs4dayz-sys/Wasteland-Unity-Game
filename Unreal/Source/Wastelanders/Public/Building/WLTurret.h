#pragma once

#include "CoreMinimal.h"
#include "World/WLInteractableActor.h"
#include "Core/WLGameTickSubsystem.h"
#include "WLTurret.generated.h"

class AWLEnemyCharacter;

/**
 * A Hearthcraft defensive turret. Ported from World/Turret.cs: every TicksPerShot game ticks it
 * picks the nearest living hostile within Range and fires for a random 1..MaxHit hit. Static
 * defence: it grants no combat XP and ignores training dummies. Damage goes through the enemy's ASC
 * with the turret as instigator, which the enemy treats as "no player credit, no provocation".
 * Server only; clients see the hitsplat through the enemy's own feedback and the turret yaw replicates
 * with the actor.
 */
UCLASS()
class WASTELANDERS_API AWLTurret : public AWLInteractableActor, public IWLTickable
{
	GENERATED_BODY()

public:
	AWLTurret();

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;
	virtual void OnGameTick(int64 TickCount) override;

	virtual FText GetExamineText_Implementation() const override;
	virtual TArray<FWLInteractOption> GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const override;

	/** SERVER (build system): configure before FinishSpawning. */
	void Configure(const FText& InName, int32 InMaxHit, float InRange);

	/** Pure: counts a tick, returns true when a shot is due (and resets Counter). */
	static bool AdvanceShotTimer(int32& Counter, int32 TicksPerShot);

	/** Pure: damage roll, 1..max(1, MaxHit) (Unity Max(1, Range(1, maxHit+1))). */
	static int32 RollDamage(int32 MaxHit);

	/** Pure: true if a target at DistSq is strictly inside Range. */
	static bool IsInRange(float DistSq, float Range) { return DistSq < FMath::Square(Range); }

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Replicated, Category = "Wastelanders|Turret", meta = (ClampMin = "1"))
	int32 MaxHit = 3;

	/** uu (Unity 8 m). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Replicated, Category = "Wastelanders|Turret")
	float Range = 800.f;

	/** 2 ticks ~= 1.2 s. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Turret", meta = (ClampMin = "1"))
	int32 TicksPerShot = 2;

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

protected:
	AWLEnemyCharacter* FindNearestTarget() const;

	int32 TickCounter = 0;
};
