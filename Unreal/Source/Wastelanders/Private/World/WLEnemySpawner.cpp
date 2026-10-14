#include "World/WLEnemySpawner.h"
#include "Combat/WLEnemyCharacter.h"
#include "Components/BillboardComponent.h"
#include "Engine/World.h"
#include "TimerManager.h"
#include "Wastelanders.h"

AWLEnemySpawner::AWLEnemySpawner()
{
	PrimaryActorTick.bCanEverTick = false;
	bReplicates = false; // server bookkeeping only; the enemies replicate themselves

	SetRootComponent(CreateDefaultSubobject<USceneComponent>(TEXT("Root")));
#if WITH_EDITORONLY_DATA
	Sprite = CreateEditorOnlyDefaultSubobject<UBillboardComponent>(TEXT("Sprite"));
	if (Sprite)
	{
		Sprite->SetupAttachment(GetRootComponent());
	}
#endif
}

void AWLEnemySpawner::BeginPlay()
{
	Super::BeginPlay();
	if (!HasAuthority())
	{
		return;
	}
	GetWorldTimerManager().SetTimer(InitialTimer, this, &AWLEnemySpawner::FillToCount, FMath::Max(0.01f, InitialDelay), false);
}

void AWLEnemySpawner::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	for (const TWeakObjectPtr<AWLEnemyCharacter>& Weak : Live)
	{
		if (AWLEnemyCharacter* Enemy = Weak.Get())
		{
			Enemy->OnEnemyDied.RemoveAll(this);
		}
	}
	GetWorldTimerManager().ClearAllTimersForObject(this);
	Super::EndPlay(EndPlayReason);
}

bool AWLEnemySpawner::DidSpawn(const AWLEnemyCharacter* Enemy) const
{
	return Enemy && EverSpawned.Contains(TWeakObjectPtr<AWLEnemyCharacter>(const_cast<AWLEnemyCharacter*>(Enemy)));
}

TSubclassOf<AWLEnemyCharacter> AWLEnemySpawner::PickClass() const
{
	float Total = 0.f;
	for (const FWLSpawnEntry& E : Entries)
	{
		if (E.EnemyClass && E.Weight > 0.f)
		{
			Total += E.Weight;
		}
	}
	if (Total <= 0.f)
	{
		return nullptr;
	}
	float Pick = FMath::FRandRange(0.f, Total);
	for (const FWLSpawnEntry& E : Entries)
	{
		if (!E.EnemyClass || E.Weight <= 0.f)
		{
			continue;
		}
		Pick -= E.Weight;
		if (Pick <= 0.f)
		{
			return E.EnemyClass;
		}
	}
	return Entries.Last().EnemyClass;
}

bool AWLEnemySpawner::FindSpawnLocation(FVector& OutLocation) const
{
	const FVector Origin = GetActorLocation();
	for (int32 Attempt = 0; Attempt < 6; ++Attempt)
	{
		const FVector2D Offset = FMath::RandPointInCircle(SpawnRadius);
		const FVector XY = Origin + FVector(Offset.X, Offset.Y, 0.f);
		FHitResult Hit;
		FCollisionQueryParams Params(SCENE_QUERY_STAT(WLSpawnerGround), false, this);
		// Unity GroundAt: cast down from well above; skip points with no ground.
		if (GetWorld()->LineTraceSingleByChannel(Hit, XY + FVector(0.f, 0.f, 1000.f), XY - FVector(0.f, 0.f, 5000.f), ECC_Visibility, Params))
		{
			OutLocation = Hit.ImpactPoint;
			return true;
		}
	}
	return false;
}

AWLEnemyCharacter* AWLEnemySpawner::SpawnOne()
{
	if (!HasAuthority())
	{
		return nullptr;
	}
	const TSubclassOf<AWLEnemyCharacter> Class = PickClass();
	if (!Class)
	{
		UE_LOG(LogWastelanders, Warning, TEXT("%s has no enemy class assigned"), *GetName());
		return nullptr;
	}
	FVector Ground;
	if (!FindSpawnLocation(Ground))
	{
		return nullptr;
	}

	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AdjustIfPossibleButAlwaysSpawn;
	const FRotator Facing(0.f, FMath::FRandRange(0.f, 360.f), 0.f);
	// Lift by a capsule's worth; collision handling settles it onto the ground.
	AWLEnemyCharacter* Enemy = GetWorld()->SpawnActor<AWLEnemyCharacter>(Class, Ground + FVector(0.f, 0.f, 100.f), Facing, Params);
	if (!Enemy)
	{
		return nullptr;
	}
	Enemy->OnEnemyDied.AddUObject(this, &AWLEnemySpawner::HandleEnemyDied);
	Live.Add(Enemy);
	EverSpawned.Add(Enemy);
	OnEnemySpawned.Broadcast(Enemy);
	return Enemy;
}

void AWLEnemySpawner::FillToCount()
{
	// Prune anything destroyed without a death event (streamed out, GM-removed).
	for (auto It = Live.CreateIterator(); It; ++It)
	{
		if (!It->IsValid())
		{
			It.RemoveCurrent();
		}
	}
	const int32 Missing = Count - Live.Num() - PendingRespawns.Num();
	for (int32 i = 0; i < Missing; ++i)
	{
		SpawnOne();
	}
}

void AWLEnemySpawner::HandleEnemyDied(AWLEnemyCharacter* Enemy)
{
	if (!Enemy)
	{
		return;
	}
	Enemy->OnEnemyDied.RemoveAll(this);
	Live.Remove(Enemy);
	OnSpawnedEnemyDied.Broadcast(Enemy);

	FTimerHandle& Handle = PendingRespawns.AddDefaulted_GetRef();
	GetWorldTimerManager().SetTimer(Handle, this, &AWLEnemySpawner::RespawnTimerFired, FMath::Max(0.1f, RespawnDelay), false);
}

void AWLEnemySpawner::RespawnTimerFired()
{
	// Drop the handle that just fired (timers fire in order, but match by inactivity to be safe).
	PendingRespawns.RemoveAll([this](const FTimerHandle& H) { return !GetWorldTimerManager().IsTimerActive(H); });
	FillToCount();
}
