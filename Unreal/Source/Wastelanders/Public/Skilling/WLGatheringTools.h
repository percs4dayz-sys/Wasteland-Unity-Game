#pragma once

#include "CoreMinimal.h"
#include "Core/WLTypes.h"

class AWLPlayerState;

/**
 * Gathering tool families and upgrades, ported from Assets/Scripts/Items/GatheringTools.cs.
 *
 * Starter tools: 3 Pickaxe (Scrapping), 4 Hatchet (Woodcutting), 5 Fishing Rod (Fishing).
 * Upgrades 700-708: three per family, Steel / Alloy / Titanium, ranks 1-3, needing level 20/40/60
 * in the family's skill and gathering 15/30/50% faster. The item rows (names, requirements) live
 * in DT_Items; only the id arithmetic is code, exactly as in Unity.
 *
 * The best usable tool carried OR equipped in the Tool slot is picked automatically.
 */
struct WASTELANDERS_API FWLGatheringTools
{
	static constexpr int32 FirstUpgradeId = 700;
	static constexpr int32 LastUpgradeId = 708;

	static bool IsUpgrade(int32 ItemId) { return ItemId >= FirstUpgradeId && ItemId <= LastUpgradeId; }

	/** Starter tool id (3/4/5) of the family ItemId belongs to, or 0 if it isn't a gathering tool. */
	static int32 BaseTool(int32 ItemId);

	/** 0 for a starter tool, 1-3 for Steel / Alloy / Titanium. */
	static int32 Rank(int32 ItemId) { return IsUpgrade(ItemId) ? (ItemId - FirstUpgradeId) % 3 + 1 : 0; }

	/** Level needed in the family skill: 1 for starters, 20/40/60 for upgrades. */
	static int32 RequiredLevel(int32 ItemId) { return IsUpgrade(ItemId) ? Rank(ItemId) * 20 : 1; }

	/** The skill a family trains (Scrapping, Woodcutting, Fishing). Only meaningful when BaseTool != 0. */
	static EWLSkill GatheringSkill(int32 ItemId);

	/** Cycle speed multiplier per rank: 1, 1.15, 1.30, 1.50. */
	static float SpeedForRank(int32 InRank);

	/**
	 * Pure core of BestFor: from the candidate ids (equipped tool first, then inventory) pick the
	 * best one usable at SkillLevel for RequiredToolId. Non-family required tools need an exact
	 * match. Returns 0 if none qualifies. Unit tested.
	 */
	static int32 BestOf(int32 RequiredToolId, TConstArrayView<int32> CandidateIds, int32 SkillLevel);

	/** Best usable tool the player carries or has equipped for RequiredToolId, 0 if none. */
	static int32 BestFor(const AWLPlayerState* PlayerState, int32 RequiredToolId);

	/** True if no tool is required or the player has a usable one. */
	static bool CanUse(const AWLPlayerState* PlayerState, int32 RequiredToolId);

	/** Tick progress per game tick for this player's best tool (1 for non-family tools). */
	static float CycleSpeed(const AWLPlayerState* PlayerState, int32 RequiredToolId);
};
