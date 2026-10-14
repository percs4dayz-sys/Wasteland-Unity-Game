#pragma once

#include "CoreMinimal.h"
#include "Core/WLTypes.h"

/**
 * XP -> level tables, ported exactly from Assets/Scripts/Skills/XPTable.cs.
 *
 *  - Standard skills: levels 1-99, 1,918,756 XP at 99 (OSRS shape with 2^(n/9), ~6.8x gentler).
 *  - Endurance: levels 10-220 (it IS your hit points), ~4M XP at 220, about double a normal 99.
 *
 * Content/Data/XPTable_Reference.json holds the Unity-generated values; the automation test
 * Wastelanders.Core.XPTable compares against them.
 */
struct WASTELANDERS_API FWLXPTable
{
	static constexpr int32 MaxLevel = 99;
	static constexpr int32 EnduranceStart = 10;
	static constexpr int32 EnduranceMax = 220;

	static int32 MaxLevelFor(EWLSkill Skill) { return Skill == EWLSkill::Endurance ? EnduranceMax : MaxLevel; }
	static int32 MinLevelFor(EWLSkill Skill) { return Skill == EWLSkill::Endurance ? EnduranceStart : 1; }

	/** Cumulative XP needed for a level (clamped to the skill's range). */
	static int32 XPForLevel(EWLSkill Skill, int32 Level);

	/** Level for an XP total. Starts the scan AT the max level (Unity once capped every skill at 98). */
	static int32 LevelForXP(EWLSkill Skill, int32 XP);

	/** XP remaining to the next level, 0 at max. */
	static int32 XPToNextLevel(EWLSkill Skill, int32 CurrentXP);

	/** 0-1 progress through the current level, 1 at max. */
	static float LevelProgress(EWLSkill Skill, int32 CurrentXP);

private:
	static const TArray<int32>& Table(EWLSkill Skill);
};
