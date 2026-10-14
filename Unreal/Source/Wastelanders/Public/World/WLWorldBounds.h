#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Subsystems/WorldSubsystem.h"
#include "WLWorldBounds.generated.h"

class UBoxComponent;

/**
 * "Is this position a real place?" Ported from World/WorldBounds.cs (+ BoundaryVisualizer.cs).
 *
 * Unity derived the playable box from the terrain footprint. Landscape bounds in an open World
 * Partition map are not all loaded on a server at once, so here designers place one or more
 * AWLWorldBoundsVolume boxes (the BoundaryVisualizer's wire box, now the actual data) and the union
 * is the world. No volume = never "outside" (bound-less test maps are never second-guessed).
 *
 * The vertical test matters most: a bad saved Z parks a character kilometres up with valid X/Y.
 */
struct WASTELANDERS_API FWLWorldBounds
{
	static constexpr float DefaultEdgeMargin = 500.f;     // Unity 5 m
	static constexpr float DefaultCeilingMargin = 15000.f; // Unity 150 m
	static constexpr float DefaultFloorMargin = 10000.f;   // Unity 100 m

	/** Pure: P outside Box (+margins)? Unit tested. */
	static bool IsOutsideBox(const FVector& P, const FBox& Box, float Edge = DefaultEdgeMargin, float Ceiling = DefaultCeilingMargin, float Floor = DefaultFloorMargin);

	/** Pure: which test failed and by how much, empty when inside (for logs). */
	static FString Explain(const FVector& P, const FBox& Box, float Edge = DefaultEdgeMargin, float Ceiling = DefaultCeilingMargin, float Floor = DefaultFloorMargin);

	/** World query: outside every bounds volume? False when the world has none. */
	static bool IsOutside(const UObject* WorldContext, const FVector& P);
};

/** Marks the playable region. Editor-only visuals; no collision, no gameplay effect by itself. */
UCLASS()
class WASTELANDERS_API AWLWorldBoundsVolume : public AActor
{
	GENERATED_BODY()

public:
	AWLWorldBoundsVolume();

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	FBox GetBounds() const;

protected:
	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UBoxComponent> Box;
};

/**
 * Keeps the bounds list and runs the server watchdog (Unity PlayerRespawn's out-of-world rescue):
 * every few seconds, any player pawn outside the bounds is returned to a spawn point through
 * AWLGameMode::TeleportToSpawn, with the reason logged.
 */
UCLASS()
class WASTELANDERS_API UWLWorldBoundsSubsystem : public UWorldSubsystem
{
	GENERATED_BODY()

public:
	virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
	virtual void OnWorldBeginPlay(UWorld& InWorld) override;

	void RegisterVolume(AWLWorldBoundsVolume* Volume) { Volumes.AddUnique(Volume); }
	void UnregisterVolume(AWLWorldBoundsVolume* Volume) { Volumes.Remove(Volume); }

	/** Union of all registered volumes; false when there are none. */
	bool TryGet(FBox& OutBox) const;

	/** Seconds between watchdog sweeps. */
	float WatchdogInterval = 2.f;

private:
	void Watchdog();

	TArray<TWeakObjectPtr<AWLWorldBoundsVolume>> Volumes;
	FTimerHandle WatchdogTimer;
};
