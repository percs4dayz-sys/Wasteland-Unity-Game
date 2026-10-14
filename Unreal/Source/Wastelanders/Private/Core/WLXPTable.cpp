#include "Core/WLXPTable.h"

namespace
{
	constexpr double StandardDivisor = 9.0;
	constexpr double EnduranceDivisor = 20.25;

	/** Cumulative XP per level: floor each term, sum, then floor(total / 4). StartLevel costs 0 XP. */
	TArray<int32> BuildTable(int32 StartLevel, int32 MaxLevel, double Divisor)
	{
		TArray<int32> Table;
		Table.SetNumZeroed(MaxLevel + 1);
		for (int32 Level = StartLevel; Level <= MaxLevel; ++Level)
		{
			double Total = 0.0;
			for (int32 N = StartLevel; N < Level; ++N)
			{
				Total += FMath::FloorToDouble(N + 300.0 * FMath::Pow(2.0, N / Divisor));
			}
			Table[Level] = static_cast<int32>(FMath::FloorToDouble(Total / 4.0));
		}
		return Table;
	}
}

const TArray<int32>& FWLXPTable::Table(EWLSkill Skill)
{
	static const TArray<int32> Standard = BuildTable(1, MaxLevel, StandardDivisor);
	static const TArray<int32> Endurance = BuildTable(EnduranceStart, EnduranceMax, EnduranceDivisor);
	return Skill == EWLSkill::Endurance ? Endurance : Standard;
}

int32 FWLXPTable::XPForLevel(EWLSkill Skill, int32 Level)
{
	return Table(Skill)[FMath::Clamp(Level, MinLevelFor(Skill), MaxLevelFor(Skill))];
}

int32 FWLXPTable::LevelForXP(EWLSkill Skill, int32 XP)
{
	const TArray<int32>& T = Table(Skill);
	const int32 Min = MinLevelFor(Skill);
	for (int32 Level = MaxLevelFor(Skill); Level >= Min; --Level)
	{
		if (XP >= T[Level])
		{
			return Level;
		}
	}
	return Min;
}

int32 FWLXPTable::XPToNextLevel(EWLSkill Skill, int32 CurrentXP)
{
	const int32 Level = LevelForXP(Skill, CurrentXP);
	if (Level >= MaxLevelFor(Skill))
	{
		return 0;
	}
	return Table(Skill)[Level + 1] - CurrentXP;
}

float FWLXPTable::LevelProgress(EWLSkill Skill, int32 CurrentXP)
{
	const int32 Level = LevelForXP(Skill, CurrentXP);
	if (Level >= MaxLevelFor(Skill))
	{
		return 1.f;
	}
	const int32 Cur = CurrentXP - XPForLevel(Skill, Level);
	const int32 Total = XPForLevel(Skill, Level + 1) - XPForLevel(Skill, Level);
	return Total > 0 ? static_cast<float>(Cur) / static_cast<float>(Total) : 0.f;
}
