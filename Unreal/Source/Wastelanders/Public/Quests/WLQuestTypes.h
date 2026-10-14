#pragma once

#include "CoreMinimal.h"
#include "Core/WLTypes.h"
#include "WLQuestTypes.generated.h"

class AActor;

/**
 * The live beast task (Skills/BeastTasks.cs "Task"). Beastmastery trains like RuneScape Slayer:
 * kill Required of one creature kind while your beast is out.
 */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLBeastTask
{
	GENERATED_BODY()

	/** Normalised creature name, the match key (FWLBeastTaskRules::NormalizeCreatureName). */
	UPROPERTY(BlueprintReadOnly, Category = "BeastTask")
	FString Creature;

	UPROPERTY(BlueprintReadOnly, Category = "BeastTask")
	int32 Tier = 1;

	UPROPERTY(BlueprintReadOnly, Category = "BeastTask")
	int32 Done = 0;

	UPROPERTY(BlueprintReadOnly, Category = "BeastTask")
	int32 Required = 0;

	/** The creature's max HP at assignment time: XP per kill, and the base of the completion bonus. */
	UPROPERTY(BlueprintReadOnly, Category = "BeastTask")
	int32 XPPerKill = 1;

	/** Unity Read(): a task with no creature, no count, or an already-met count is "no task". */
	bool IsValid() const { return !Creature.IsEmpty() && Required > 0 && Done < Required; }

	bool operator==(const FWLBeastTask& O) const
	{
		return Creature == O.Creature && Tier == O.Tier && Done == O.Done && Required == O.Required && XPPerKill == O.XPPerKill;
	}
};

/**
 * Where the starter guide is (World/StarterGuide.cs). Unity encoded this in flags
 * ("guide_lesson:<skill>:<item>", "guide_return"); here it's the "starter_guide" quest stage.
 */
UENUM(BlueprintType)
enum class EWLStarterGuideStage : uint8
{
	None,           // no lesson in progress (also: before meeting Roxy)
	Lesson,         // a lesson from Roxy is under way (LessonSkill / LessonItem)
	ReturnToRoxy    // lesson done: go back to Roxy for the next one
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLStarterGuideState
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "Guide")
	EWLStarterGuideStage Stage = EWLStarterGuideStage::None;

	/** The lesson's skill (meaningful while Stage == Lesson). */
	UPROPERTY(BlueprintReadOnly, Category = "Guide")
	EWLSkill LessonSkill = EWLSkill::Attack;

	/** The item Roxy handed over for it (0 = none). Distinguishes the two Smithing lessons and the Geiger lesson. */
	UPROPERTY(BlueprintReadOnly, Category = "Guide")
	int32 LessonItem = 0;

	bool operator==(const FWLStarterGuideState& O) const
	{
		return Stage == O.Stage && LessonSkill == O.LessonSkill && LessonItem == O.LessonItem;
	}
};

/**
 * The single "where do I go next" objective (UI/QuestGuide.cs): a line at the top of the screen, an
 * arrow over the target / on the screen edge, and a minimap marker. Target may be unset (no place).
 */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLQuestObjective
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "Objective")
	bool bActive = false;

	UPROPERTY(BlueprintReadOnly, Category = "Objective")
	FText Text;

	/** The thing to point at (replicates if it's a replicated actor, e.g. an NPC or resource node). */
	UPROPERTY(BlueprintReadOnly, Category = "Objective")
	TWeakObjectPtr<AActor> Target;

	/** Fallback point when Target isn't relevant on the client (out of net cull range). */
	UPROPERTY(BlueprintReadOnly, Category = "Objective")
	FVector Location = FVector::ZeroVector;

	UPROPERTY(BlueprintReadOnly, Category = "Objective")
	bool bHasLocation = false;

	/** World point to aim the arrow at: the live target if the client has it, else the server's snapshot. */
	bool GetPoint(FVector& Out) const;
};
