#pragma once

#include "CoreMinimal.h"
#include "Core/WLTypes.h"

class AActor;
class UWorld;

/**
 * "Where is this lesson done?" lookups (World/QuestCam.cs FindTargetForSkill / NearestNode /
 * NearestStation). Runs on the server, which has every actor loaded, and the result goes into the
 * replicated objective. Every resource node and station counts, wherever it is.
 */
struct WASTELANDERS_API FWLQuestTargets
{
	/**
	 * Gathering skills: the nearest node of that skill. It prefers a node that is usable at your level
	 * and needs the lesson's tool, then any usable node (never a Sulphur Vent, which sits beside every
	 * mining cluster but isn't "the rubble pile"), then any node of the skill.
	 * Cooking: the nearest cooking fire. Smithing: the nearest furnace, else the nearest workbench.
	 * Other skills have no fixed world spot (null).
	 */
	static AActor* TargetForSkill(const UWorld* World, EWLSkill Skill, const FVector& From, int32 PlayerLevel, int32 ToolId = 0);

	static AActor* NearestNode(const UWorld* World, EWLSkill Skill, const FVector& From, int32 PlayerLevel, int32 ToolId = 0);

	static AActor* NearestStation(const UWorld* World, EWLCraftingStation Station, const FVector& From);

	/** Starter guide target for a lesson (StarterGuide.StartLesson's switch). */
	static AActor* TargetForLesson(const UWorld* World, EWLSkill Skill, int32 GiveItemId, const FVector& From, int32 PlayerLevel);
};
