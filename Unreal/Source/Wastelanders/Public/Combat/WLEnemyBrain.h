#pragma once

#include "CoreMinimal.h"
#include "Combat/WLCombatTypes.h"

/** Snapshot of what the enemy knows this think, for the pure decision below. */
struct FWLEnemyBrainInput
{
	bool bDead = false;
	bool bHasTarget = false;
	float DistToTarget = 0.f;
	float AttackRange = 180.f;
	bool bWindingUp = false;
	float DistFromHome = 0.f;
	/** Radius around home that counts as "home" (max(WanderRadius, 50uu), Unity used max(r, 0.5 m)). */
	float HomePatch = 50.f;
	/** Walking home after a leash: ignores targets until back inside the home patch. */
	bool bReturningFromLeash = false;
	bool bHasWanderPoint = false;
};

/**
 * Enemy3D.Update as a pure decision, so the state machine is testable without a world.
 * AWLEnemyAIController feeds it each think and carries out the chosen state.
 */
struct FWLEnemyBrain
{
	/** Unity: chase/attack only while the target is within aggro range AND (aggressive OR provoked by it). */
	static bool IsValidAggroTarget(bool bAlive, float Dist, float AggroRange, bool bAggressive, bool bProvokedByIt)
	{
		return bAlive && Dist <= AggroRange && (bAggressive || bProvokedByIt);
	}

	/** Leash past LeashRange from home; 0 disables (pure Unity behaviour). */
	static bool ShouldLeash(float DistFromHome, float LeashRange)
	{
		return LeashRange > 0.f && DistFromHome > LeashRange;
	}

	static EWLEnemyAIState Decide(const FWLEnemyBrainInput& In)
	{
		if (In.bDead)
		{
			return EWLEnemyAIState::Dead;
		}
		if (In.bHasTarget && !In.bReturningFromLeash)
		{
			// Mid-windup the enemy stands its ground; the swing lands or whiffs when the timer hits.
			return (In.bWindingUp || In.DistToTarget <= In.AttackRange) ? EWLEnemyAIState::Attack : EWLEnemyAIState::Chase;
		}
		if (In.DistFromHome > In.HomePatch)
		{
			return EWLEnemyAIState::Return;
		}
		return In.bHasWanderPoint ? EWLEnemyAIState::Wander : EWLEnemyAIState::Idle;
	}
};
