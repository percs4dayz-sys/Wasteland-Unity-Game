#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "WLPlacedPieceComponent.generated.h"

class AWLPlayerState;

/**
 * Marks an actor as a player-built deployable and remembers who built it (server only; not
 * replicated). Unity had a single player so placed pieces needed no owner; in the MMO, personal
 * pieces (AFK stations) only accept their builder (see AWLAFKStation::Interact), and a builder's
 * pieces are cleaned up when they leave (UWLBuildComponent), since placed pieces are not persisted
 * (Unity: "placed deployables are session-only").
 */
UCLASS(ClassGroup = "Wastelanders", meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLPlacedPieceComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLPlacedPieceComponent();

	/** SERVER. */
	void SetBuilder(const AWLPlayerState* PS);

	/** True if PS built this piece. A piece with no recorded builder (designer-placed) accepts everyone. */
	bool IsOwnedBy(const AWLPlayerState* PS) const;

	/** SERVER. Number of live pieces PS has built in World. */
	static int32 CountOwnedBy(UWorld* World, const AWLPlayerState* PS);

	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	/** Seconds between orphan checks. */
	static constexpr float OrphanCheckInterval = 30.f;

	/** Index into UWLBuildCatalog::Buildables this piece came from (-1 = unknown). */
	UPROPERTY(BlueprintReadOnly, Category = "Building")
	int32 BuildableIndex = -1;

private:
	void CheckOrphaned();
	FTimerHandle OrphanTimer;
	FGuid BuilderId;
	TWeakObjectPtr<const AWLPlayerState> Builder;
	bool bHasBuilder = false;
};
