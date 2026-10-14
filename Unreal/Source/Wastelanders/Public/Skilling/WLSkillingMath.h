#pragma once

#include "CoreMinimal.h"
#include "Templates/Function.h"

/**
 * Pure skilling formulas, kept free of actors so they can be unit tested
 * (Private/Tests/World/WLSkillingTests.cpp). Ported from ResourceNode.cs and CraftingManager.cs.
 */
struct WASTELANDERS_API FWLSkillingMath
{
	/** Out-of-256 roll scale used by every gather chance (OSRS convention). */
	static constexpr int32 ChanceScale = 256;

	/** Unity CraftingManager.BASE_BURN: burn chance at the recipe's own required level. */
	static constexpr float BaseBurnChance = 0.45f;

	/** Unity CraftingManager.CRAFT_INTERVAL: one output every 4 ticks (2.4 s), even for "Make All". */
	static constexpr int32 CraftIntervalTicks = 4;

	/**
	 * Per-attempt success chance out of 256 (ResourceNode.CurrentChance without the better-tool
	 * override): when ChanceAtLevel99 > 0, linear from level 1 to 99, rounded and clamped 1-256;
	 * otherwise the flat chance (<= 0 means 256, guaranteed).
	 */
	static int32 GatherChance(int32 Level, int32 FlatChance, int32 ChanceAtLevel1, int32 ChanceAtLevel99);

	/**
	 * One gather cycle: up to Attempts rolls of RandRange(0,255) < Chance, stopping at the first hit.
	 * Rolls is a source of 0-255 integers (FMath::RandRange on the server, fixed values in tests).
	 */
	static bool RollCycle(int32 Attempts, int32 Chance, TFunctionRef<int32()> Roll);

	/**
	 * Cooking burn chance (CraftingManager.RollBurn): 0 when NoBurnLevel <= LevelRequired or Level >=
	 * NoBurnLevel; otherwise lerp(0.45, 0, inverseLerp(LevelRequired, NoBurnLevel, Level)).
	 */
	static float BurnChance(int32 LevelRequired, int32 NoBurnLevel, int32 Level);

	/** Number of gathered items: max(1, DropQuantity), +1 on the Gather-module proc. */
	static int32 GatherQuantity(int32 DropQuantity, bool bModuleProc) { return FMath::Max(1, DropQuantity) + (bModuleProc ? 1 : 0); }

	/** Gather-module proc chance: 10% per point of ModulePower("Gather") (Unity ModuleCatalog.Effect.Gather). */
	static float GatherModuleChance(int32 ModulePower) { return 0.10f * FMath::Max(0, ModulePower); }

	/** Inclusive random yield range used by resource nodes (min clamped to 1, max to >= min). */
	static void ClampYield(int32 MinYield, int32 MaxYield, int32& OutLo, int32& OutHi)
	{
		OutLo = FMath::Max(1, MinYield);
		OutHi = FMath::Max(OutLo, MaxYield);
	}
};
