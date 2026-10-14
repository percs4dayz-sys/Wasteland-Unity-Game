#include "Skilling/WLSkillingMath.h"

int32 FWLSkillingMath::GatherChance(int32 Level, int32 FlatChance, int32 ChanceAtLevel1, int32 ChanceAtLevel99)
{
	if (ChanceAtLevel99 > 0)
	{
		const float T = FMath::Clamp((Level - 1) / 98.f, 0.f, 1.f);
		// Unity Mathf.RoundToInt rounds half to even; FMath::RoundHalfToEven matches it exactly.
		const int32 C = static_cast<int32>(FMath::RoundHalfToEven(FMath::Lerp(static_cast<float>(ChanceAtLevel1), static_cast<float>(ChanceAtLevel99), T)));
		return FMath::Clamp(C, 1, ChanceScale);
	}
	return FlatChance > 0 ? FlatChance : ChanceScale;
}

bool FWLSkillingMath::RollCycle(int32 Attempts, int32 Chance, TFunctionRef<int32()> Roll)
{
	for (int32 i = 0; i < FMath::Max(1, Attempts); ++i)
	{
		if (Roll() < Chance)
		{
			return true;
		}
	}
	return false;
}

float FWLSkillingMath::BurnChance(int32 LevelRequired, int32 NoBurnLevel, int32 Level)
{
	if (NoBurnLevel <= LevelRequired || Level >= NoBurnLevel)
	{
		return 0.f;
	}
	// Mathf.InverseLerp clamps to 0-1, so a level below the requirement still burns at the base rate.
	const float T = FMath::Clamp(static_cast<float>(Level - LevelRequired) / static_cast<float>(NoBurnLevel - LevelRequired), 0.f, 1.f);
	return FMath::Lerp(BaseBurnChance, 0.f, T);
}
