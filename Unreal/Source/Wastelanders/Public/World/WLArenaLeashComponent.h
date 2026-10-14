#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "WLArenaLeashComponent.generated.h"

/**
 * Keeps a boss inside its authored, grounded arena. Ported from World/BCBossArenaMotion.cs.
 *
 * Add to an enemy (or a Blueprint of one). SERVER: after movement each frame, if the owner has
 * strayed more than ArenaRadius (planar) from its home, it is slid back to the edge with a sweep, so
 * collision still applies (Unity replayed the clamped move through the CharacterController). Dead
 * owners are left alone. Home is the owner's location at BeginPlay unless set.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLArenaLeashComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLArenaLeashComponent();

	virtual void BeginPlay() override;
	virtual void TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction) override;

	/** Unity arenaRadius 14 m. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Arena")
	float ArenaRadius = 1400.f;

	void SetHome(const FVector& InHome) { Home = InHome; }

	/** Pure: Location clamped to the planar circle (Home, Radius); Z untouched. Unit tested. */
	static FVector ClampToArena(const FVector& Location, const FVector& InHome, float Radius);

private:
	FVector Home = FVector::ZeroVector;
};
