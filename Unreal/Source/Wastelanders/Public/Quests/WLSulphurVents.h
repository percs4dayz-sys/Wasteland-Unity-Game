#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"
#include "Subsystems/WorldSubsystem.h"
#include "Engine/TimerHandle.h"
#include "WLSulphurVents.generated.h"

class AWLResourceNode;
class UWorld;
struct FHitResult;

/**
 * Pure placement rules from World/SulphurCompanions.cs (unit tested). Distances in uu (cm);
 * "flat" means horizontal (UE X/Y; Unity's X/Z).
 */
struct WASTELANDERS_API FWLSulphurVentRules
{
	static constexpr int32 PickaxeId = 3;
	static constexpr float ClusterLink = 3000.f;     // nodes closer than this share a cluster (30 m)
	static constexpr float CoveredRadius = 2500.f;   // a vent this far past a cluster's edge already serves it
	static constexpr float MinSpacing = 300.f;       // between vents
	static constexpr float MaxHeightDiff = 600.f;    // pit or ledge
	static constexpr float MinNormalZ = 0.85f;       // steeper than ~32 degrees is rejected
	static constexpr int32 SpotAttempts = 24;

	struct FCluster
	{
		FVector Center = FVector::ZeroVector;
		float Radius = 0.f;
		int32 Count = 0;
		TArray<FVector> Members;
	};

	static float FlatDistance(const FVector& A, const FVector& B);
	static bool AnyWithin(const TArray<FVector>& Points, const FVector& P, float Range);

	/** Single-linkage grouping: a node joins every cluster it's within ClusterLink of (merging them). */
	static TArray<FCluster> BuildClusters(const TArray<FVector>& Points);

	/** 3 vents for a cluster of 4+ nodes, else 2. */
	static int32 VentsWanted(int32 ClusterCount) { return ClusterCount >= 4 ? 3 : 2; }

	/** Same spots every session: seeded from the cluster centre (in metres, as Unity did). */
	static int32 Seed(const FVector& Center);

	/** A candidate point just outside the cluster edge (radius + 3..9 m). */
	static FVector ProbePoint(const FCluster& Cluster, FRandomStream& Rng);
};

/** Project Settings > Wastelanders Sulphur Vents. */
UCLASS(Config = Game, DefaultConfig, meta = (DisplayName = "Wastelanders Sulphur Vents"))
class WASTELANDERS_API UWLSulphurVentSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	/** Blueprint of a sulphur-vent AWLResourceNode (NodeType = SulphurVent). Unset = no auto vents. */
	UPROPERTY(Config, EditAnywhere, Category = "Sulphur Vents")
	TSoftClassPtr<AWLResourceNode> VentClass;

	UPROPERTY(Config, EditAnywhere, Category = "Sulphur Vents")
	bool bEnabled = true;

	/** Unity waited a second for spawners to place their nodes first. */
	UPROPERTY(Config, EditAnywhere, Category = "Sulphur Vents", meta = (ClampMin = "0"))
	float Delay = 1.f;
};

/**
 * World/SulphurCompanions.cs. Wherever there's mining (pickaxe nodes), a few sulphur vents sit
 * alongside it. Once per level load (server only), mining nodes are grouped into clusters, and each
 * cluster with no vent nearby gets 2 (3 for a big cluster) vents on open, gently sloped natural ground
 * around its edge. Placement is seeded from the cluster position, so vents land in the same spots
 * every session, and existing vents count, so hand-placed ones are never doubled up.
 *
 * "Natural ground": Unity accepted only TerrainCollider hits. Here a hit counts as ground when the
 * component belongs to a Landscape (class name check, so no Landscape module dependency) or its actor
 * or component is tagged "Ground".
 */
UCLASS()
class WASTELANDERS_API UWLSulphurVentPlacer : public UWorldSubsystem
{
	GENERATED_BODY()

public:
	virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
	virtual void OnWorldBeginPlay(UWorld& InWorld) override;

	/** SERVER. Run the placement now (also called by the delayed timer). Returns the number placed. */
	int32 Populate();

private:
	bool FindSpot(const FWLSulphurVentRules::FCluster& Cluster, FRandomStream& Rng, const TArray<FVector>& Vents, FVector& OutPos) const;
	bool IsGroundHit(const FHitResult& Hit) const;
	bool IsBlocked(const FVector& Ground) const;

	FTimerHandle PopulateTimer;
	bool bPopulated = false;
};
