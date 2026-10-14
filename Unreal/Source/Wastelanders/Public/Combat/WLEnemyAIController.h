#pragma once

#include "CoreMinimal.h"
#include "AIController.h"
#include "Combat/WLCombatTypes.h"
#include "WLEnemyAIController.generated.h"

class AWLEnemyCharacter;

/**
 * Server-side brain for AWLEnemyCharacter: Unity Enemy3D.Update as a compact explicit state machine
 * (Idle / Wander / Chase / Attack / Return), decided by FWLEnemyBrain. No Behavior Tree or
 * Blackboard assets are needed.
 *
 * MMO changes from Unity, where the only target was PlayerEntity.Instance:
 *  - Targets are any living player pawn. Aggressive mobs take the nearest one in aggro range;
 *    passive mobs only fight players who have hit them (per-player provocation).
 *  - A target that escapes aggro range is forgotten (its provocation too), like Unity.
 *  - Optional leash from home (AWLEnemyCharacter::LeashRange).
 *
 * Movement uses the navmesh when one exists and falls back to steering straight at the goal
 * (Unity moved the transform directly), so enemies work in an unbuilt test map.
 * Thinks run at ThinkInterval (Unity ran every frame; 10 Hz is plenty for melee timing).
 */
UCLASS()
class WASTELANDERS_API AWLEnemyAIController : public AAIController
{
	GENERATED_BODY()

public:
	AWLEnemyAIController();

	virtual void OnPossess(APawn* InPawn) override;
	virtual void Tick(float DeltaSeconds) override;

	/** A player hit us: passive creatures fight back against that player. */
	void NotifyProvoked(APawn* By);
	bool IsProvokedBy(const AActor* Pawn) const;

	/** Strength L40 stagger. */
	void Stagger(float Seconds);

	/** Owner died / reset: drop everything. */
	void ResetBrain();

	APawn* GetCurrentTarget() const { return Target.Get(); }
	EWLEnemyAIState GetState() const { return State; }

	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI")
	float ThinkInterval = 0.1f;

protected:
	AWLEnemyCharacter* GetEnemy() const;

	void Think(float DeltaSeconds);
	void UpdateTarget(AWLEnemyCharacter* Enemy);
	APawn* FindBestTarget(AWLEnemyCharacter* Enemy) const;
	bool IsTargetValid(const AWLEnemyCharacter* Enemy, const APawn* Candidate) const;

	void DoChase(AWLEnemyCharacter* Enemy, APawn* InTarget);
	void DoAttack(AWLEnemyCharacter* Enemy, APawn* InTarget, double Now);
	void DoReturn(AWLEnemyCharacter* Enemy);
	void DoWander(AWLEnemyCharacter* Enemy, double Now);

	void StartWindup(AWLEnemyCharacter* Enemy, double Now);
	void CancelWindup();
	void MoveTowards(AWLEnemyCharacter* Enemy, const FVector& Goal, float Acceptance, float SpeedMul);
	void StopMoving();
	void FaceToward(AWLEnemyCharacter* Enemy, const FVector& Point);

	TWeakObjectPtr<APawn> Target;
	TArray<TWeakObjectPtr<APawn>> ProvokedBy;

	EWLEnemyAIState State = EWLEnemyAIState::Idle;
	bool bWindingUp = false;
	double HitLandsAt = 0.0;
	double NextAttackAt = 0.0;
	bool bReturningFromLeash = false;

	bool bHasWanderPoint = false;
	FVector WanderPoint = FVector::ZeroVector;
	double NextWanderAt = 0.0;

	FVector LastMoveGoal = FVector(FLT_MAX);
	bool bUsingPathfinding = false;
	float ThinkAccumulator = 0.f;
};
