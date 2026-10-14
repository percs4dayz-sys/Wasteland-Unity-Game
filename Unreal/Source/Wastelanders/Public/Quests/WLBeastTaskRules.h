#pragma once

#include "CoreMinimal.h"
#include "Templates/Function.h"
#include "Quests/WLQuestTypes.h"

/**
 * Pure beast-task rules, ported from Skills/BeastTasks.cs. No world access, so they are unit
 * tested (Wastelanders.Quests.BeastTasks). The stateful half lives in UWLQuestComponent.
 *
 *  - Task tier follows Beastmastery in the 20-level brackets every skill uses: 1-19 -> 1, 20-39 -> 2,
 *    40-59 -> 3, 60-79 -> 4, 80+ -> 5.
 *  - Each counted kill gives Beastmastery XP equal to the creature's max HP (Slayer's rule).
 *  - Finishing adds a 10% bonus of the task's total kill XP.
 */
struct WASTELANDERS_API FWLBeastTaskRules
{
	/** A kill further than this (uu) from the player isn't theirs. Unity: 80 m. */
	static constexpr float KillRange = 8000.f;
	/** Pause after finishing before the next assignment (s). */
	static constexpr float NextTaskDelay = 4.f;
	/** Completion bonus, as a share of the task's kill XP. */
	static constexpr float BonusFraction = 0.10f;
	/** Spawners can add creatures after load, so the huntable list is re-read this often (s). */
	static constexpr float RescanSeconds = 30.f;
	/** Retry delay when nothing huntable is around (s). */
	static constexpr float RetryAssignSeconds = 10.f;
	/** Minimum gap between "your beast has to be out" reminders (s). */
	static constexpr float BeastOutHintSeconds = 30.f;

	/** One huntable creature kind near the player. */
	struct FKind
	{
		FString Name;
		int32 Tier = 1;
		int32 MaxHP = 1;
		int32 Count = 0;
	};

	/** Beastmastery level -> task tier (1-5). */
	static int32 TierForLevel(int32 Level) { return FMath::Clamp(1 + Level / 20, 1, 5); }

	/** Kills per task by tier, inclusive (Unity MinKills / MaxKills). */
	static int32 MinKills(int32 Tier);
	static int32 MaxKills(int32 Tier);

	/** XP for one counted kill: the creature's max HP, at least 1. */
	static int32 XPForKill(int32 MaxHP) { return FMath::Max(1, MaxHP); }

	/** Completion bonus: max(1, round(required * xpPerKill * 10%)). */
	static int32 CompletionBonus(int32 Required, int32 XPPerKill);

	/**
	 * A creature's task name: its display name without clone / instance-number noise, so
	 * "Frog Marauder", "Frog Marauder (2)", "Frog Marauder 3" and "Frog Marauder(Clone)" are one kind.
	 * '|' becomes '/' (it was the Unity save-flag separator). Empty -> "Creature".
	 */
	static FString NormalizeCreatureName(const FString& RawName);

	/**
	 * The wanted tier if anything of it lives here, else the nearest lower one, else the nearest
	 * higher one, else Want itself.
	 */
	static int32 PickTier(int32 Want, const TArray<FKind>& Kinds);

	/**
	 * Picks a creature of Tier weighted by how many of each live nearby, avoiding LastCreature when
	 * there's a choice (variety). Roll must be in [0, total count of the pool). Returns the index into
	 * Kinds, or INDEX_NONE if no kind has that tier.
	 */
	static int32 PickKind(const TArray<FKind>& Kinds, int32 Tier, const FString& LastCreature, TFunctionRef<int32(int32 /*TotalWeight*/)> RollFn);

	/** Parses a Unity "bmtask|creature|tier|done|required|xp" flag. False if malformed or already complete. */
	static bool ParseLegacyFlag(const FString& Flag, FWLBeastTask& Out);

	/** HUD fill 0-1. */
	static float Progress(const FWLBeastTask& Task) { return Task.Required > 0 ? FMath::Clamp(float(Task.Done) / Task.Required, 0.f, 1.f) : 0.f; }
};
