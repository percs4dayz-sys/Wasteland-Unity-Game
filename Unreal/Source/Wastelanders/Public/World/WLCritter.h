#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Subsystems/WorldSubsystem.h"
#include "World/WLWorldSettings.h"
#include "WLCritter.generated.h"

class UStaticMeshComponent;

/**
 * One ambient critter (World/CritterLife.cs, class Critter). SCENERY: no health, no collision, not
 * targetable, not replicated. Each client runs its own wildlife around its own player; nobody else
 * needs to agree on where a rabbit is.
 *
 * Brain: Graze (stand) -> Wander (amble to a point within 12 m) -> Flee (player within 9 m: run
 * directly away with +/-35 degrees jitter until 20 m clear, then graze). Fliers take off on flee,
 * climb to 18 m over the ground at 5 m/s and never settle; the subsystem recycles them by distance.
 * Decisions run every 0.4 s, staggered per critter; only movement runs per frame.
 *
 * Visual: Species.Mesh, or the species' placeholder shape at BodySize. The Unity animator drive
 * (Speed parameter / CrossFade states) is not ported with the vendor rigs; a Blueprint subclass set
 * as Species.CritterClass can read GetSpeed() and GetState() to drive its own animation.
 */
UENUM(BlueprintType)
enum class EWLCritterState : uint8
{
	Graze,
	Wander,
	Flee
};

UCLASS()
class WASTELANDERS_API AWLCritter : public AActor
{
	GENERATED_BODY()

public:
	AWLCritter();

	virtual void Tick(float DeltaSeconds) override;

	/** Called once by the subsystem right after spawning. */
	virtual void Init(APawn* InPlayer, const FWLCritterSpecies& InSpecies);

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Critter") float GetSpeed() const { return Speed; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Critter") EWLCritterState GetState() const { return State; }

	static constexpr float PanicRadius = 900.f;
	static constexpr float CalmRadius = 2000.f;
	static constexpr float DecideEvery = 0.4f;
	static constexpr float FlyAltitude = 1800.f;
	static constexpr float ClimbSpeed = 500.f;
	static constexpr float WanderRadius = 1200.f;
	static constexpr float FleeDistance = 1800.f;

protected:
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<USceneComponent> Root;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UStaticMeshComponent> Body;

private:
	void Think();
	void PickFleeTarget();
	void SnapToGround();
	void ClimbAway(float DeltaSeconds);
	bool TraceGround(const FVector& From, float Down, FVector& OutHit) const;

	TWeakObjectPtr<APawn> Player;
	FWLCritterSpecies Species;
	EWLCritterState State = EWLCritterState::Graze;
	FVector Target = FVector::ZeroVector;
	float NextThink = 0.f;
	float StateUntil = 0.f;
	float Speed = 0.f;
};

/**
 * Keeps a small population of harmless wildlife alive around the local player (World/CritterLife.cs).
 * Up to MaxCritters live at once; one top-up attempt every CritterSpawnInterval seconds in a ring
 * CritterSpawnMin..Max away, preferring points behind the camera (the first 4 of 6 attempts must be),
 * on ground with normal Z >= 0.7 and above SeaLevelZ; anything past CritterDespawnDistance is
 * recycled. Species come from UWLWorldSettings::CritterSpecies (the 7 Unity species as placeholders).
 *
 * Client only (not created on dedicated servers). Unity's "species model missing -> disable" path
 * is replaced by the placeholder fallback, so it never disables itself.
 */
UCLASS()
class WASTELANDERS_API UWLCritterSubsystem : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
	virtual void Deinitialize() override;
	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override;

	int32 NumCritters() const { return Critters.Num(); }

private:
	void TrySpawnOne(APawn* Player);

	TArray<TWeakObjectPtr<AWLCritter>> Critters;
	float SpawnTimer = 0.f;
};
