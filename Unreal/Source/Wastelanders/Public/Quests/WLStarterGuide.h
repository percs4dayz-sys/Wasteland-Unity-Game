#pragma once

#include "CoreMinimal.h"
#include "Templates/Function.h"
#include "Core/WLTypes.h"
#include "Quests/WLQuestTypes.h"

/** One of Roxy's lessons (RoxyNPC.cs Quest). */
struct WASTELANDERS_API FWLRoxyLesson
{
	EWLSkill Skill = EWLSkill::Fishing;
	FText Title;
	FText Line;
	/** Item she hands over with the lesson (0 = nothing). Given once per lesson. */
	int32 GiveItemId = 0;
	int32 GiveQty = 1;
};

/** What an XP gain does to the lesson in progress (StarterGuide.OnXp). */
enum class EWLLessonXPResult : uint8
{
	Ignore,              // not this lesson's skill, or a Beastmastery tick (the lesson is the whole hunt)
	Finish,              // lesson done: "Return to Roxy"
	AdvanceToRefinement, // Geiger lesson: you scrapped a golem and hold raw cores, now refine them
	AdvanceToFission     // Geiger lesson: you refined cores, now fight with gauntlets
};

/**
 * Data and pure rules for Roxy's lesson chain and the new-player guide.
 * Ports the static parts of NPC/RoxyNPC.cs, World/StarterGuide.cs and World/QuestCam.cs (captions).
 * The stateful parts live in UWLQuestComponent; the dialogue graph in FWLBuiltinDialogues.
 */
struct WASTELANDERS_API FWLStarterGuideRules
{
	/** Quest ids in FWLCharacterSave::Quests. */
	static const FName RoxyQuestId;         // Stage = index of Roxy's next lesson (Unity "roxy_next:N")
	static const FName StarterGuideQuestId; // Stage = EWLStarterGuideStage, Counters Skill / Item

	/** Roxy's lessons, in order: seven starter lessons, then the advanced "Hot Stuff" handoff. */
	static const TArray<FWLRoxyLesson>& RoxyLessons();

	/** Starter lessons before the advanced handoff (the index of the Geiger Counter lesson). */
	static int32 StarterCount();

	static FText RoxyGreeting();
	static FText BeastLessonLine();

	/** Attack, Strength, Defence AND Marksmanship at level 10 (RoxyNPC.ReadyForFission). */
	static constexpr int32 FissionCombatLevel = 10;
	static bool ReadyForFission(TFunctionRef<int32(EWLSkill)> GetLevel);
	static FText CombatProgress(TFunctionRef<int32(EWLSkill)> GetLevel);

	/** The objective line for a lesson (StarterGuide.LessonText). */
	static FText LessonText(EWLSkill Skill, int32 GiveItemId);

	/** QuestCam.DefaultCaption: what the camera peek says over a lesson's spot. */
	static FText PeekCaption(EWLSkill Skill, int32 GiveItemId);

	/** StarterGuide.OnXp. bHoldsRawCore = the player carries any tier of raw fission core. */
	static EWLLessonXPResult EvaluateXP(const FWLStarterGuideState& State, EWLSkill XPSkill, bool bHoldsRawCore);

	/** Parses Unity's "guide_lesson:<skill>:<item>" flag. */
	static bool ParseLegacyLessonFlag(const FString& Flag, EWLSkill& OutSkill, int32& OutItem);

	/** Parses Unity's "roxy_next:<n>" flag. */
	static bool ParseLegacyNextFlag(const FString& Flag, int32& OutIndex);
};
