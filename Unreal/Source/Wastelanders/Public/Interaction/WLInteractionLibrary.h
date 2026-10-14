#pragma once

#include "CoreMinimal.h"
#include "Interaction/WLInteractable.h"

class AActor;
class AWLPlayerCharacter;

/**
 * Shared answers to "where do I stand to use this, and am I close enough?" (Unity
 * ClickToMove3D.UsePosition / WorldInteractables). Used by the client to path and by the server to
 * validate, so both measure distance the same way.
 *
 * Distance is measured on the ground plane to the NEAR SIDE of the target's colliding bounds, not
 * its pivot: Unity learned that walking to a node's centre left the player swinging at air (or
 * stopped by the collider metres short). Resource nodes with an explicit interaction point (a
 * fishing spot's bank) use that point instead.
 */
struct WASTELANDERS_API FWLInteraction
{
	/** Verb id for the always-appended Examine option. Handled locally, never sent to the server. */
	static const FName ExamineVerb;

	/** Planar distance from Point to the nearest point of Box (0 inside). Pure; unit tested. */
	static float DistanceToBox2D(const FVector& Point, const FBox& Box);

	/** Closest point of Box to Point on the ground plane, at height Z. Pure; unit tested. */
	static FVector ClosestPointOnBox2D(const FVector& Point, const FBox& Box, float Z);

	/** Where to walk to use Target from FromLocation (near side of its bounds, or its interaction point). */
	static FVector GetUseLocation(const AActor* Target, const FVector& FromLocation);

	/** Planar distance from FromLocation to the place Target is used from. */
	static float DistanceToTarget(const AActor* Target, const FVector& FromLocation);

	/** "HornedRavager_C_3" -> "Horned Ravager" (Unity WorldInteractables.Pretty). Pure; unit tested. */
	static FString PrettyName(const FString& Raw);

	/** Display name: the interactable's name, else the pretty actor name. */
	static FText GetDisplayName(const AActor* Target);

	/** Examine text, or Unity's "Nothing interesting about it." fallback. */
	static FText GetExamineText(const AActor* Target);

	/**
	 * Context-menu options for Player in OSRS order: the target's own options (default first), then
	 * "Examine". Empty when Target is not interactable.
	 */
	static TArray<FWLInteractOption> GetMenuOptions(const AActor* Target, const AWLPlayerCharacter* Player);

	/** The target's interact range, clamped to something sane (an unset range must not mean "anywhere"). */
	static float GetRange(const AActor* Target);
};
