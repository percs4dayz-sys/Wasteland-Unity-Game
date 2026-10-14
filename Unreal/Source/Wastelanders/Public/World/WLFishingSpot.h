#pragma once

#include "CoreMinimal.h"
#include "World/WLResourceNode.h"
#include "WLFishingSpot.generated.h"

/**
 * A fishing spot: a resource node on the water, worked from an interaction point on the bank.
 * Ports World/FishingBubbles.cs (bubbles well up where the fish are, gone while fished out) and
 * World/FishingShimmer.cs (the marker pulses and slowly spins), plus OSRS-style MOVING spots.
 *
 * Movement is server-driven and replicated through the actor's own movement replication, so every
 * client sees the spot in the same place. A player fishing a spot that moves away is stopped by the
 * skilling component's range check.
 *
 * Visuals are asset-free: the placeholder is a flat disc; BubblesMesh is an optional art slot.
 * Shimmer runs only where something renders (never on a dedicated server).
 */
UCLASS()
class WASTELANDERS_API AWLFishingSpot : public AWLResourceNode
{
	GENERATED_BODY()

public:
	AWLFishingSpot();

	virtual void BeginPlay() override;
	virtual void Tick(float DeltaSeconds) override;
	virtual void OnGameTick(int64 TickCount) override;

	/**
	 * Other places this spot can be (relative to its placed transform; edit with the 3D widget).
	 * Empty = the spot never moves.
	 */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Fishing", meta = (MakeEditWidget = true))
	TArray<FVector> AlternateLocations;

	/** Move to another location every MinMoveTicks..MaxMoveTicks game ticks. 0 = only on depletion. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Fishing")
	int32 MinMoveTicks = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Fishing")
	int32 MaxMoveTicks = 0;

	/** When fished out, jump to another location and respawn there at once (OSRS) instead of waiting in place. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Fishing")
	bool bMoveInsteadOfDeplete = false;

	// FishingShimmer defaults.
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Fishing|Shimmer")
	float PulseAmount = 0.15f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Fishing|Shimmer")
	float PulseSpeed = 2.2f;

	/** Degrees per second. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Fishing|Shimmer")
	float SpinSpeed = 25.f;

	/** Optional bubbles art (a mesh with its own animated material). Unset = the shimmering disc only. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Visual")
	TSoftObjectPtr<UStaticMesh> BubblesArtMesh;

protected:
	virtual void Deplete() override;

	/** SERVER. Jump to a different candidate location. */
	void MoveToAnotherLocation();

	void ScheduleNextMove();

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UStaticMeshComponent> BubblesMesh;

	/** Placed transform: AlternateLocations are relative to it. */
	FTransform HomeTransform;
	int32 CurrentLocationIndex = INDEX_NONE; // INDEX_NONE = home
	int32 TicksUntilMove = 0;
	FVector BaseMeshScale = FVector::OneVector;
	float ShimmerTime = 0.f;
};
