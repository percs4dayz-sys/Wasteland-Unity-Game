#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Combat/WLEnemyCharacter.h"
#include "WLFissionGolemSpawner.generated.h"

class AWLFissionGolem;
class UWLDropTableAsset;
class UBillboardComponent;

/**
 * Keeps one living Elemental Golem of its tier in the world so the geiger counter always has a
 * target. Merges Unity's FissionGolemSpawner (all tiers, placed by LivingWorld's biome map) and
 * Broken Crescent's BCEarthGolemSpawner (one tier, authored child spawn points): place one of
 * these per tier in that tier's biome with a few SpawnPoints. Kill a golem and strip its corpse and
 * a fresh one appears at a different point after RespawnDelay. SERVER ONLY.
 *
 * The biome lookup (LivingWorld.FindTierSpot) is replaced by designer-placed points, which is
 * what an authored MMO world wants anyway. Models are not ported: golems use the enemy
 * placeholder body until new art is assigned on GolemClass.
 */
UCLASS()
class WASTELANDERS_API AWLFissionGolemSpawner : public AActor
{
	GENERATED_BODY()

public:
	AWLFissionGolemSpawner();

	virtual void BeginPlay() override;

	/** Unity default stats for a tier (FissionGolemSpawner.Stat / BCEarthGolemSpawner.DefaultStats). */
	static FWLEnemyStats DefaultStats(int32 InTier);

	/** "Earth Golem (T1)" ... "Shadow Golem (T5)". */
	static FText GolemName(int32 InTier);

	/** SERVER. Spawn this tier's golem now at a spawn point. */
	AWLFissionGolem* SpawnGolem();

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem", meta = (ClampMin = "1", ClampMax = "5"))
	int32 Tier = 1;

	/** Relative points (edit widgets); the spawner never reuses the last point twice in a row. Empty = the spawner's own spot. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem", meta = (MakeEditWidget = true))
	TArray<FVector> SpawnPoints;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	float RespawnDelay = 20.f;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	TSubclassOf<AWLFissionGolem> GolemClass;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	bool bOverrideStats = false;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem", meta = (EditCondition = "bOverrideStats"))
	FWLEnemyStats OverrideStats;

	/** Golem corpse yield (per kill) and harvest overrides (0 = derive from tier). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem") int32 MinYield = 3;
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem") int32 MaxYield = 15;
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem") int32 HarvestLevelRequired = 0;
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem") int32 XPPerCore = 0;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	TObjectPtr<UWLDropTableAsset> DropTable;

	/** Seconds between population checks (Unity scanned every 2 s). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	float ScanInterval = 2.f;

protected:
	void Scan();
	bool FindGround(const FVector& Point, FVector& OutGround) const;

#if WITH_EDITORONLY_DATA
	UPROPERTY()
	TObjectPtr<UBillboardComponent> Sprite;
#endif

	FTimerHandle ScanTimer;
	double RespawnAt = 0.0;
	int32 LastPoint = INDEX_NONE;
};
