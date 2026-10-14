#include "Combat/WLEnemyAIController.h"
#include "Combat/WLEnemyCharacter.h"
#include "Combat/WLEnemyBrain.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "AbilitySystemInterface.h"
#include "GameFramework/GameStateBase.h"
#include "GameFramework/PlayerState.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "NavigationSystem.h"
#include "Navigation/PathFollowingComponent.h"
#include "Engine/World.h"

namespace
{
	float Dist2D(const FVector& A, const FVector& B)
	{
		return FVector::Dist2D(A, B);
	}

	bool IsPawnAlive(const APawn* Pawn)
	{
		if (!IsValid(Pawn))
		{
			return false;
		}
		const IAbilitySystemInterface* ASI = Cast<IAbilitySystemInterface>(Pawn);
		const UWLAbilitySystemComponent* ASC = ASI ? Cast<UWLAbilitySystemComponent>(ASI->GetAbilitySystemComponent()) : nullptr;
		return ASC ? !ASC->IsDead() : true;
	}
}

AWLEnemyAIController::AWLEnemyAIController()
{
	PrimaryActorTick.bCanEverTick = true;
	bWantsPlayerState = false;
	bSetControlRotationFromPawnOrientation = true;
}

AWLEnemyCharacter* AWLEnemyAIController::GetEnemy() const
{
	return Cast<AWLEnemyCharacter>(GetPawn());
}

void AWLEnemyAIController::OnPossess(APawn* InPawn)
{
	Super::OnPossess(InPawn);
	ResetBrain();
	// Spread thinks so a spawned pack doesn't all evaluate on the same frame.
	ThinkAccumulator = FMath::FRand() * ThinkInterval;
}

void AWLEnemyAIController::ResetBrain()
{
	CancelWindup();
	StopMoving();
	Target.Reset();
	ProvokedBy.Reset();
	State = EWLEnemyAIState::Idle;
	bReturningFromLeash = false;
	bHasWanderPoint = false;
	NextAttackAt = 0.0;
}

void AWLEnemyAIController::NotifyProvoked(APawn* By)
{
	if (IsValid(By) && !IsProvokedBy(By))
	{
		ProvokedBy.Add(By);
	}
}

bool AWLEnemyAIController::IsProvokedBy(const AActor* Pawn) const
{
	for (const TWeakObjectPtr<APawn>& P : ProvokedBy)
	{
		if (P.Get() == Pawn)
		{
			return true;
		}
	}
	return false;
}

void AWLEnemyAIController::Stagger(float Seconds)
{
	CancelWindup();
	const double Now = GetWorld() ? GetWorld()->GetTimeSeconds() : 0.0;
	NextAttackAt = FMath::Max(NextAttackAt, Now + Seconds);
}

void AWLEnemyAIController::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (!HasAuthority())
	{
		return;
	}
	ThinkAccumulator += DeltaSeconds;
	if (ThinkAccumulator >= ThinkInterval)
	{
		Think(ThinkAccumulator);
		ThinkAccumulator = 0.f;
	}

	// Direct steering (no navmesh) needs input every frame, not every think.
	AWLEnemyCharacter* Enemy = GetEnemy();
	if (Enemy && !bUsingPathfinding && LastMoveGoal.X != FLT_MAX)
	{
		const FVector To = (LastMoveGoal - Enemy->GetActorLocation()).GetSafeNormal2D();
		if (!To.IsNearlyZero())
		{
			Enemy->AddMovementInput(To, 1.f);
		}
	}
}

void AWLEnemyAIController::Think(float /*DeltaSeconds*/)
{
	AWLEnemyCharacter* Enemy = GetEnemy();
	if (!Enemy)
	{
		return;
	}
	const double Now = GetWorld()->GetTimeSeconds();

	if (Enemy->IsDead())
	{
		if (State != EWLEnemyAIState::Dead)
		{
			ResetBrain();
			State = EWLEnemyAIState::Dead;
		}
		return;
	}
	if (State == EWLEnemyAIState::Dead)
	{
		State = EWLEnemyAIState::Idle;   // respawned
	}

	const float DistFromHome = Dist2D(Enemy->GetActorLocation(), Enemy->GetHome());
	const float HomePatch = FMath::Max(Enemy->WanderRadius, 50.f);

	if (FWLEnemyBrain::ShouldLeash(DistFromHome, Enemy->LeashRange))
	{
		bReturningFromLeash = true;
		Target.Reset();
		ProvokedBy.Reset();
		CancelWindup();
	}
	if (bReturningFromLeash && DistFromHome <= HomePatch)
	{
		bReturningFromLeash = false;
	}

	if (!bReturningFromLeash)
	{
		UpdateTarget(Enemy);
	}
	APawn* CurrentTarget = Target.Get();

	FWLEnemyBrainInput In;
	In.bHasTarget = CurrentTarget != nullptr;
	In.DistToTarget = CurrentTarget ? Dist2D(CurrentTarget->GetActorLocation(), Enemy->GetActorLocation()) : 0.f;
	In.AttackRange = Enemy->AttackRange;
	In.bWindingUp = bWindingUp;
	In.DistFromHome = DistFromHome;
	In.HomePatch = HomePatch;
	In.bReturningFromLeash = bReturningFromLeash;
	In.bHasWanderPoint = bHasWanderPoint;
	State = FWLEnemyBrain::Decide(In);

	if (State != EWLEnemyAIState::Attack)
	{
		CancelWindup();
	}

	switch (State)
	{
	case EWLEnemyAIState::Chase:  DoChase(Enemy, CurrentTarget); break;
	case EWLEnemyAIState::Attack: DoAttack(Enemy, CurrentTarget, Now); break;
	case EWLEnemyAIState::Return: DoReturn(Enemy); break;
	case EWLEnemyAIState::Idle:
	case EWLEnemyAIState::Wander: DoWander(Enemy, Now); break;
	default: break;
	}
}

bool AWLEnemyAIController::IsTargetValid(const AWLEnemyCharacter* Enemy, const APawn* Candidate) const
{
	if (!IsValid(Candidate) || Candidate == Enemy)
	{
		return false;
	}
	const float Dist = Dist2D(Candidate->GetActorLocation(), Enemy->GetActorLocation());
	return FWLEnemyBrain::IsValidAggroTarget(IsPawnAlive(Candidate), Dist, Enemy->AggroRange, Enemy->IsAggressive(), IsProvokedBy(Candidate));
}

void AWLEnemyAIController::UpdateTarget(AWLEnemyCharacter* Enemy)
{
	// Forget anyone who got away or died (Unity: "you got away, it loses interest").
	for (int32 i = ProvokedBy.Num() - 1; i >= 0; --i)
	{
		const APawn* P = ProvokedBy[i].Get();
		if (!IsValid(P) || !IsPawnAlive(P) || Dist2D(P->GetActorLocation(), Enemy->GetActorLocation()) > Enemy->AggroRange)
		{
			ProvokedBy.RemoveAt(i);
		}
	}
	if (!IsTargetValid(Enemy, Target.Get()))
	{
		Target = FindBestTarget(Enemy);
	}
}

APawn* AWLEnemyAIController::FindBestTarget(AWLEnemyCharacter* Enemy) const
{
	const AGameStateBase* GS = GetWorld()->GetGameState();
	if (!GS)
	{
		return nullptr;
	}
	APawn* Best = nullptr;
	float BestDist = TNumericLimits<float>::Max();
	for (const APlayerState* PS : GS->PlayerArray)
	{
		APawn* Candidate = PS ? PS->GetPawn() : nullptr;
		if (!IsTargetValid(Enemy, Candidate))
		{
			continue;
		}
		// A player who hit us outranks a closer bystander for passive creatures; for aggressive
		// ones the nearest wins (matches the single-player feel where both were the same person).
		const float Dist = Dist2D(Candidate->GetActorLocation(), Enemy->GetActorLocation()) - (IsProvokedBy(Candidate) ? 10000.f : 0.f);
		if (Dist < BestDist)
		{
			BestDist = Dist;
			Best = Candidate;
		}
	}
	return Best;
}

void AWLEnemyAIController::DoChase(AWLEnemyCharacter* Enemy, APawn* InTarget)
{
	bHasWanderPoint = false;
	MoveTowards(Enemy, InTarget->GetActorLocation(), Enemy->AttackRange * 0.9f, 1.f);
}

void AWLEnemyAIController::DoAttack(AWLEnemyCharacter* Enemy, APawn* InTarget, double Now)
{
	bHasWanderPoint = false;
	StopMoving();
	FaceToward(Enemy, InTarget->GetActorLocation());

	if (bWindingUp)
	{
		if (Now >= HitLandsAt)
		{
			bWindingUp = false;
			Enemy->LandSwing(InTarget);
		}
		return;
	}
	if (Now >= NextAttackAt)
	{
		StartWindup(Enemy, Now);
	}
}

void AWLEnemyAIController::DoReturn(AWLEnemyCharacter* Enemy)
{
	bHasWanderPoint = false;
	MoveTowards(Enemy, Enemy->GetHome(), 25.f, Enemy->ReturnSpeedMul);
}

void AWLEnemyAIController::DoWander(AWLEnemyCharacter* Enemy, double Now)
{
	if (Enemy->WanderRadius <= 1.f)
	{
		StopMoving();   // pinned in place
		return;
	}
	if (!bHasWanderPoint)
	{
		StopMoving();
		if (Now >= NextWanderAt)
		{
			const FVector2D R = FMath::RandPointInCircle(Enemy->WanderRadius);
			WanderPoint = Enemy->GetHome() + FVector(R.X, R.Y, 0.f);
			bHasWanderPoint = true;
		}
		return;
	}
	if (Dist2D(WanderPoint, Enemy->GetActorLocation()) < 15.f)
	{
		bHasWanderPoint = false;
		NextWanderAt = Now + FMath::FRandRange(Enemy->WanderPauseRange.X, Enemy->WanderPauseRange.Y);
		StopMoving();
		return;
	}
	MoveTowards(Enemy, WanderPoint, 10.f, Enemy->WanderSpeedMul);
}

void AWLEnemyAIController::StartWindup(AWLEnemyCharacter* Enemy, double Now)
{
	bWindingUp = true;
	HitLandsAt = Now + Enemy->WindupSeconds;
	// The cooldown runs from the swing, so the rhythm matches the old instant hits.
	NextAttackAt = HitLandsAt + Enemy->AttackCooldown;
	Enemy->OnSwingStarted.Broadcast(Enemy->WindupSeconds);
}

void AWLEnemyAIController::CancelWindup()
{
	if (!bWindingUp)
	{
		return;
	}
	bWindingUp = false;
	if (AWLEnemyCharacter* Enemy = GetEnemy())
	{
		Enemy->OnSwingCancelled.Broadcast();
	}
}

void AWLEnemyAIController::MoveTowards(AWLEnemyCharacter* Enemy, const FVector& Goal, float Acceptance, float SpeedMul)
{
	if (UCharacterMovementComponent* Move = Enemy->GetCharacterMovement())
	{
		Move->MaxWalkSpeed = Enemy->MoveSpeed * SpeedMul;
	}
	if (Dist2D(Goal, Enemy->GetActorLocation()) <= Acceptance)
	{
		StopMoving();
		return;
	}
	// Re-path only when the goal moved meaningfully (a chased player moves every think).
	const bool bGoalMoved = LastMoveGoal.X == FLT_MAX || Dist2D(Goal, LastMoveGoal) > 50.f;
	if (!bGoalMoved && (!bUsingPathfinding || GetMoveStatus() != EPathFollowingStatus::Idle))
	{
		return;
	}
	LastMoveGoal = Goal;

	UNavigationSystemV1* NavSys = FNavigationSystem::GetCurrent<UNavigationSystemV1>(GetWorld());
	if (NavSys && NavSys->GetDefaultNavDataInstance(FNavigationSystem::DontCreate))
	{
		const EPathFollowingRequestResult::Type Result = MoveToLocation(Goal, Acceptance, false, true, true, false, nullptr, true);
		bUsingPathfinding = Result != EPathFollowingRequestResult::Failed;
	}
	else
	{
		bUsingPathfinding = false;
	}
}

void AWLEnemyAIController::StopMoving()
{
	if (bUsingPathfinding)
	{
		StopMovement();
	}
	bUsingPathfinding = false;
	LastMoveGoal = FVector(FLT_MAX);
}

void AWLEnemyAIController::FaceToward(AWLEnemyCharacter* Enemy, const FVector& Point)
{
	const FVector To = (Point - Enemy->GetActorLocation()).GetSafeNormal2D();
	if (!To.IsNearlyZero())
	{
		Enemy->SetActorRotation(To.Rotation());
	}
}
