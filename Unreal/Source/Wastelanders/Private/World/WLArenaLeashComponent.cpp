#include "World/WLArenaLeashComponent.h"
#include "Combat/WLEnemyCharacter.h"
#include "GameFramework/Actor.h"

UWLArenaLeashComponent::UWLArenaLeashComponent()
{
	PrimaryComponentTick.bCanEverTick = true;
	// After the character's movement this frame (Unity DefaultExecutionOrder(1000) + LateUpdate).
	PrimaryComponentTick.TickGroup = TG_PostPhysics;
}

void UWLArenaLeashComponent::BeginPlay()
{
	Super::BeginPlay();
	Home = GetOwner()->GetActorLocation();
	SetComponentTickEnabled(GetOwner()->HasAuthority());
}

FVector UWLArenaLeashComponent::ClampToArena(const FVector& Location, const FVector& InHome, float Radius)
{
	FVector Offset = Location - InHome;
	Offset.Z = 0.f;
	Offset = Offset.GetClampedToMaxSize(FMath::Max(0.f, Radius));
	return FVector(InHome.X + Offset.X, InHome.Y + Offset.Y, Location.Z);
}

void UWLArenaLeashComponent::TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction)
{
	Super::TickComponent(DeltaTime, TickType, ThisTickFunction);

	AActor* Owner = GetOwner();
	if (const AWLEnemyCharacter* Enemy = Cast<AWLEnemyCharacter>(Owner); Enemy && Enemy->IsDead())
	{
		return;
	}
	const FVector Loc = Owner->GetActorLocation();
	const FVector Wanted = ClampToArena(Loc, Home, ArenaRadius);
	if (!Wanted.Equals(Loc, 1.f))
	{
		Owner->SetActorLocation(Wanted, /*bSweep*/ true);
	}
}
