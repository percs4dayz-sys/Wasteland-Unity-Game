#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "WLEnemySpawner.generated.h"

class AWLEnemyCharacter;
class UBillboardComponent;

DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnSpawnerEnemy, AWLEnemyCharacter*);

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSpawnEntry
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Spawn")
	TSubclassOf<AWLEnemyCharacter> EnemyClass;

	/** Relative pick weight among entries. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Spawn", meta = (ClampMin = "0"))
	float Weight = 1.f;
};

/**
 * Keeps a hand-placed group of enemies alive: spawns Count enemies around itself and respawns each
 * one RespawnDelay seconds after it dies. SERVER ONLY (spawned enemies replicate themselves).
 *
 * Ported from World/EnemySpawner.cs. Unity's spawner was a roaming "horde around the player" with
 * a hard-coded distance-tier stat table, and it was switched OFF (AutoPopulate = false) in favour of
 * hand-placed mobs that "persist and respawn in place". That surviving behaviour is what this actor
 * is. The disabled roaming mode is not ported: a per-player population ring makes no sense with many
 * players sharing a world, and enemy stats now live on the enemy classes, not in the spawner.
 * Difficulty by region is expressed by which classes each placed spawner uses.
 */
UCLASS()
class WASTELANDERS_API AWLEnemySpawner : public AActor
{
	GENERATED_BODY()

public:
	AWLEnemySpawner();

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	/** True if Enemy is (or was) spawned by this spawner. Boss gates use this to accept respawned bosses. */
	bool DidSpawn(const AWLEnemyCharacter* Enemy) const;

	int32 GetLiveCount() const { return Live.Num(); }

	/** SERVER. Spawn one enemy now (normally automatic). */
	AWLEnemyCharacter* SpawnOne();

	FWLOnSpawnerEnemy OnEnemySpawned;
	FWLOnSpawnerEnemy OnSpawnedEnemyDied;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Spawner")
	TArray<FWLSpawnEntry> Entries;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Spawner", meta = (ClampMin = "0"))
	int32 Count = 1;

	/** Enemies appear within this radius (uu) of the spawner. 0 = exactly here. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Spawner")
	float SpawnRadius = 600.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Spawner")
	float RespawnDelay = 30.f;

	/** Seconds after level start before the first spawn (lets the navmesh / level stream in). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Spawner")
	float InitialDelay = 0.5f;

protected:
	void HandleEnemyDied(AWLEnemyCharacter* Enemy);
	void RespawnTimerFired();
	TSubclassOf<AWLEnemyCharacter> PickClass() const;
	bool FindSpawnLocation(FVector& OutLocation) const;
	void FillToCount();

#if WITH_EDITORONLY_DATA
	UPROPERTY()
	TObjectPtr<UBillboardComponent> Sprite;
#endif

	TSet<TWeakObjectPtr<AWLEnemyCharacter>> Live;
	TSet<TWeakObjectPtr<AWLEnemyCharacter>> EverSpawned;
	TArray<FTimerHandle> PendingRespawns;
	FTimerHandle InitialTimer;
};
