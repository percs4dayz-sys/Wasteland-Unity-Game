#include "World/WLFishingSpot.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/World.h"

AWLFishingSpot::AWLFishingSpot()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.TickInterval = 0.f;

	// Spots move: replicate the actor transform so all clients agree where the fish are.
	SetReplicatingMovement(true);

	NodeType = EWLResourceNodeType::WaterBarrel;
	Skill = EWLSkill::Fishing;
	RequiredToolId = 5; // Fishing Rod
	DropItemId = 22;    // Raw Shrimp
	TicksPerCycle = 5;
	bUseInteractionPoint = true;
	InteractRange = 200.f;

	PlaceholderShape = EWLPlaceholderShape::Cylinder;
	PlaceholderSize = FVector(120.f, 120.f, 4.f);
	PlaceholderColor = FLinearColor(0.7f, 0.92f, 1.f);

	// A disc on the water: clickable (Visibility), but never blocks pawns or the camera.
	Mesh->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
	Mesh->SetCollisionResponseToAllChannels(ECR_Ignore);
	Mesh->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);
	Mesh->SetCanEverAffectNavigation(false);
	Mesh->SetCastShadow(false);

	BubblesMesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Bubbles"));
	BubblesMesh->SetupAttachment(Mesh);
	BubblesMesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	BubblesMesh->SetCastShadow(false);
}

void AWLFishingSpot::BeginPlay()
{
	Super::BeginPlay();

	HomeTransform = GetActorTransform();
	BaseMeshScale = Mesh->GetRelativeScale3D();
	if (!BubblesArtMesh.IsNull())
	{
		BubblesMesh->SetStaticMesh(BubblesArtMesh.LoadSynchronous());
	}

	// Cosmetics only where something is drawn.
	SetActorTickEnabled(GetNetMode() != NM_DedicatedServer);

	if (HasAuthority())
	{
		ScheduleNextMove();
	}
}

void AWLFishingSpot::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);

	// FishingShimmer: pulse X/Y scale and slowly spin. Hidden with the node while it's fished out.
	ShimmerTime += DeltaSeconds;
	const float S = 1.f + FMath::Sin(ShimmerTime * PulseSpeed) * PulseAmount;
	Mesh->SetRelativeScale3D(FVector(BaseMeshScale.X * S, BaseMeshScale.Y * S, BaseMeshScale.Z));
	Mesh->AddLocalRotation(FRotator(0.f, SpinSpeed * DeltaSeconds, 0.f));
}

void AWLFishingSpot::ScheduleNextMove()
{
	TicksUntilMove = (MaxMoveTicks > 0 && AlternateLocations.Num() > 0)
		? FMath::RandRange(FMath::Max(1, MinMoveTicks), FMath::Max(MinMoveTicks, MaxMoveTicks))
		: 0;
}

void AWLFishingSpot::OnGameTick(int64 TickCount)
{
	Super::OnGameTick(TickCount);

	if (TicksUntilMove > 0 && --TicksUntilMove == 0)
	{
		MoveToAnotherLocation();
		ScheduleNextMove();
	}
}

void AWLFishingSpot::Deplete()
{
	if (bMoveInsteadOfDeplete && AlternateLocations.Num() > 0)
	{
		MoveToAnotherLocation();
		ResetYield(); // fresh fish at the new spot, no downtime
		OnDepleted.Broadcast(this);
		return;
	}
	Super::Deplete();
}

void AWLFishingSpot::MoveToAnotherLocation()
{
	const int32 Count = AlternateLocations.Num() + 1; // + home
	if (Count <= 1)
	{
		return;
	}
	int32 Next = CurrentLocationIndex;
	while (Next == CurrentLocationIndex)
	{
		Next = FMath::RandRange(-1, AlternateLocations.Num() - 1); // -1 = home (INDEX_NONE)
	}
	CurrentLocationIndex = Next;
	const FVector Target = Next == INDEX_NONE ? HomeTransform.GetLocation() : HomeTransform.TransformPosition(AlternateLocations[Next]);
	SetActorLocation(Target, false, nullptr, ETeleportType::TeleportPhysics);
}
