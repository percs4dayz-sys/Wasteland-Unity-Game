#pragma once

#include "CoreMinimal.h"
#include "Core/WLTypes.h"
#include "Core/WLXPTable.h"

/**
 * Pure skill rules from Assets/Scripts/Skills/PlayerStats.cs, split out of the component so the
 * automation tests can check them without a world.
 */
struct WASTELANDERS_API FWLSkillMath
{
	/** Endurance IS hit points: identity clamp to 10-220 (PlayerStats.HPForLevel). */
	static int32 HPForLevel(int32 EnduranceLevel)
	{
		return FMath::Clamp(EnduranceLevel, FWLXPTable::EnduranceStart, FWLXPTable::EnduranceMax);
	}

	/**
	 * OSRS structure with the Endurance-as-HP model (PlayerStats.GetCombatLevel):
	 *   base  = 0.25  * (Endurance + Defence)
	 *   melee = 0.325 * (Attack + Strength)
	 *   range = 0.325 * (floor(Marks/2) + Marks)
	 *   magic = 0.325 * (floor(Fission/2) + Fission)
	 *   level = max(1, floor(base + max(melee, range, magic)))
	 * Double precision on purpose: the terms land on exact .25/.325 boundaries.
	 */
	static int32 CombatLevel(int32 Endurance, int32 Attack, int32 Strength, int32 Defence, int32 Marksmanship, int32 Fission)
	{
		const double Base = 0.25 * (Endurance + Defence);
		const double Melee = 0.325 * (Attack + Strength);
		const double Range = 0.325 * (Marksmanship / 2 + Marksmanship);   // int division is the floor
		const double Magic = 0.325 * (Fission / 2 + Fission);
		const double Best = FMath::Max(Melee, FMath::Max(Range, Magic));
		return FMath::Max(1, FMath::FloorToInt(Base + Best));
	}

	/**
	 * Skills the tutorial portal waits on (level 2 in each). Beastmastery, Fission and Refinement have
	 * no tutorial-island content, so requiring them would make the portal impossible to open. The
	 * melee line is live again (Skills.IsRetiredMelee returns false), so it counts.
	 */
	static bool IsTutorialSkill(EWLSkill Skill)
	{
		return Skill != EWLSkill::Beastmastery && Skill != EWLSkill::Fission && Skill != EWLSkill::Refinement && Skill != EWLSkill::Count;
	}

	static constexpr int32 TutorialLevelRequired = 2;
};
