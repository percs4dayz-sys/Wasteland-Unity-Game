#include "World/WLCritter.h"
#include "World/WLPlaceholderMesh.h"
#include "Components/StaticMeshComponent.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/PlayerController.h"
#include "Camera/PlayerCameraManager.h"
#include "Engine/World.h"
#include "CollisionQueryParams.h"

// --- critter ------------------------------------------------------------------------------------

AWLCritter::AWLCritter()
{
	PrimaryActorTick.bCanEverTick = true;
	bReplicates = false;
	SetCanBeDamaged(false);

	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	SetRootComponent(Root);

	Body = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Body"));
	Body->SetupAttachment(Root);
	Body->SetCollisionEnabled(ECollisionEnabled::NoCollision); // scenery: never blocks, never clicked
	Body->SetCanEverAffectNavigation(false);
	Body->SetCastShadow(true);
}

void AWLCritter::Init(APawn* InPlayer, const FWLCritterSpecies& InSpecies)
{
	Player = InPlayer;
	Species = InSpecies;

	if (FWLPlaceholderMesh::Apply(Body, Species.Mesh, Species.PlaceholderShape, Species.BodySize, Species.PlaceholderColor))
	{
		Body->SetRelativeLocation(FVector(0.f, 0.f, Species.BodySize.Z * 0.5f)); // stand on the ground
	}
	SetActorScale3D(FVector(FMath::FRandRange(Species.ScaleMin, FMath::Max(Species.ScaleMin, Species.ScaleMax))));

	const float Now = GetWorld()->GetTimeSeconds();
	NextThink = Now + FMath::FRand() * DecideEvery; // stagger the herd's brains
	StateUntil = Now + FMath::FRandRange(1.f, 4.f);
}

bool AWLCritter::TraceGround(const FVector& From, float Down, FVector& OutHit) const
{
	FCollisionObjectQueryParams Objects(ECC_WorldStatic);
	Objects.AddObjectTypesToQuery(ECC_WorldDynamic);
	FCollisionQueryParams Params(SCENE_QUERY_STAT(WLCritterGround), false, this);
	FHitResult Hit;
	if (GetWorld()->LineTraceSingleByObjectType(Hit, From, From - FVector(0.f, 0.f, Down), Objects, Params))
	{
		OutHit = Hit.ImpactPoint;
		return true;
	}
	return false;
}

void AWLCritter::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (!Player.IsValid())
	{
		return;
	}

	const float Now = GetWorld()->GetTimeSeconds();
	if (Now >= NextThink)
	{
		NextThink = Now + DecideEvery;
		Think();
	}

	// A move-in-place species only covers ground while fleeing.
	const float Desired = State == EWLCritterState::Flee ? Species.FleeSpeed
		: (State == EWLCritterState::Wander && !Species.bMoveInPlace) ? Species.WalkSpeed
		: 0.f;
	Speed = FMath::FInterpConstantTo(Speed, Desired, DeltaSeconds, 800.f); // Unity 8 m/s^2

	if (Speed <= 5.f)
	{
		return;
	}
	FVector To = Target - GetActorLocation();
	To.Z = 0.f;
	if (To.SizeSquared() <= 400.f) // 0.2 m
	{
		return;
	}
	const bool bFlyingOff = Species.bFlier && State == EWLCritterState::Flee;
	const FRotator Look(bFlyingOff ? GetActorRotation().Pitch : 0.f, To.Rotation().Yaw, 0.f);
	SetActorRotation(FMath::RInterpTo(GetActorRotation(), Look, DeltaSeconds, 6.f));
	const FVector Flat = FRotator(0.f, GetActorRotation().Yaw, 0.f).Vector();
	SetActorLocation(GetActorLocation() + Flat * (Speed * DeltaSeconds));

	if (bFlyingOff)
	{
		ClimbAway(DeltaSeconds);
	}
	else
	{
		SnapToGround();
	}
}

void AWLCritter::Think()
{
	const APawn* P = Player.Get();
	const float PlayerDist = FVector::Dist(GetActorLocation(), P->GetActorLocation());
	const float Now = GetWorld()->GetTimeSeconds();

	// Fear overrides everything.
	if (PlayerDist < PanicRadius && State != EWLCritterState::Flee)
	{
		State = EWLCritterState::Flee;
		PickFleeTarget();
		return;
	}

	switch (State)
	{
	case EWLCritterState::Flee:
		// A flier never settles; a ground animal calms once it has put space between them.
		if (Species.bFlier)
		{
			PickFleeTarget();
		}
		else if (PlayerDist > CalmRadius)
		{
			State = EWLCritterState::Graze;
			StateUntil = Now + FMath::FRandRange(2.f, 5.f);
		}
		else if (FVector::DistSquared(Target, GetActorLocation()) < 100.f * 100.f)
		{
			PickFleeTarget();
		}
		break;

	case EWLCritterState::Graze:
		if (Now >= StateUntil)
		{
			State = EWLCritterState::Wander;
			const FVector2D R = FMath::RandPointInCircle(WanderRadius);
			Target = GetActorLocation() + FVector(R.X, R.Y, 0.f);
		}
		break;

	case EWLCritterState::Wander:
	{
		FVector To = Target - GetActorLocation();
		To.Z = 0.f;
		// Unity: sqrMagnitude < 1.2 m^2 (about 1.1 m), or 12 s past the graze timer.
		if (To.SizeSquared() < 12000.f || Now >= StateUntil + 12.f)
		{
			State = EWLCritterState::Graze;
			StateUntil = Now + FMath::FRandRange(2.f, 6.f);
		}
		break;
	}
	}
}

void AWLCritter::PickFleeTarget()
{
	// Directly away from the player, with jitter so a group scatters instead of forming a line.
	FVector Away = GetActorLocation() - Player->GetActorLocation();
	Away.Z = 0.f;
	if (Away.SizeSquared() < 1.f)
	{
		Away = FVector(FMath::VRand().X, FMath::VRand().Y, 0.f);
	}
	const FVector Jitter = FRotator(0.f, FMath::FRandRange(-35.f, 35.f), 0.f).RotateVector(Away.GetSafeNormal());
	Target = GetActorLocation() + Jitter * FleeDistance;
}

void AWLCritter::SnapToGround()
{
	FVector Ground;
	if (TraceGround(GetActorLocation() + FVector(0.f, 0.f, 300.f), 3000.f, Ground))
	{
		SetActorLocation(FVector(GetActorLocation().X, GetActorLocation().Y, Ground.Z));
	}
}

void AWLCritter::ClimbAway(float DeltaSeconds)
{
	float GroundZ = GetActorLocation().Z;
	FVector Ground;
	if (TraceGround(GetActorLocation() + FVector(0.f, 0.f, 300.f), 25000.f, Ground))
	{
		GroundZ = Ground.Z;
	}
	const float Ceiling = GroundZ + FlyAltitude;
	if (GetActorLocation().Z < Ceiling)
	{
		const float Z = FMath::FInterpConstantTo(GetActorLocation().Z, Ceiling, DeltaSeconds, ClimbSpeed);
		SetActorLocation(FVector(GetActorLocation().X, GetActorLocation().Y, Z));
		// Ease a gentle nose-up pitch (Unity forward.y 0.35, about 19 degrees) so it reads as flight.
		const FRotator Pitched(19.f, GetActorRotation().Yaw, 0.f);
		SetActorRotation(FMath::RInterpTo(GetActorRotation(), Pitched, DeltaSeconds, 3.f));
	}
}

// --- subsystem ----------------------------------------------------------------------------------

bool UWLCritterSubsystem::ShouldCreateSubsystem(UObject* Outer) const
{
	const UWorld* World = Cast<UWorld>(Outer);
	return World && World->IsGameWorld() && !IsRunningDedicatedServer() && UWLWorldSettings::Get()->bEnableCritters;
}

void UWLCritterSubsystem::Deinitialize()
{
	for (const TWeakObjectPtr<AWLCritter>& Weak : Critters)
	{
		if (AWLCritter* C = Weak.Get())
		{
			C->Destroy();
		}
	}
	Critters.Reset();
	Super::Deinitialize();
}

TStatId UWLCritterSubsystem::GetStatId() const
{
	RETURN_QUICK_DECLARE_CYCLE_STAT(UWLCritterSubsystem, STATGROUP_Tickables);
}

void UWLCritterSubsystem::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);

	// The player can arrive late (character select first); keep looking.
	APlayerController* PC = GetWorld()->GetFirstPlayerController();
	APawn* Player = PC ? PC->GetPawn() : nullptr;
	if (!Player)
	{
		return;
	}
	const UWLWorldSettings* Settings = UWLWorldSettings::Get();
	const float DespawnSq = FMath::Square(Settings->CritterDespawnDistance);

	for (int32 i = Critters.Num() - 1; i >= 0; --i)
	{
		AWLCritter* C = Critters[i].Get();
		if (!C)
		{
			Critters.RemoveAtSwap(i);
			continue;
		}
		if (FVector::DistSquared(C->GetActorLocation(), Player->GetActorLocation()) > DespawnSq)
		{
			C->Destroy();
			Critters.RemoveAtSwap(i);
		}
	}

	// At most one per interval, so a fresh area fills gradually.
	SpawnTimer -= DeltaTime;
	if (Critters.Num() < Settings->MaxCritters && SpawnTimer <= 0.f)
	{
		SpawnTimer = Settings->CritterSpawnInterval;
		TrySpawnOne(Player);
	}
}

void UWLCritterSubsystem::TrySpawnOne(APawn* Player)
{
	const UWLWorldSettings* Settings = UWLWorldSettings::Get();
	if (Settings->CritterSpecies.Num() == 0)
	{
		return;
	}
	const FWLCritterSpecies& Species = Settings->CritterSpecies[FMath::RandRange(0, Settings->CritterSpecies.Num() - 1)];
	const APlayerController* PC = Cast<APlayerController>(Player->GetController());
	const FVector CamForward = PC && PC->PlayerCameraManager ? PC->PlayerCameraManager->GetCameraRotation().Vector() : Player->GetActorForwardVector();

	FCollisionObjectQueryParams Objects(ECC_WorldStatic);
	Objects.AddObjectTypesToQuery(ECC_WorldDynamic);
	FCollisionQueryParams Params(SCENE_QUERY_STAT(WLCritterSpawn), false, Player);

	// A few attempts; give up quietly if the area is all cliffs or water.
	for (int32 Attempt = 0; Attempt < 6; ++Attempt)
	{
		const FVector2D D2 = FMath::RandPointInCircle(1.f).GetSafeNormal();
		const FVector Dir(D2.X, D2.Y, 0.f);
		if (Dir.IsNearlyZero())
		{
			continue;
		}
		// Prefer behind the camera so nothing pops in on screen; the last attempts may be in view.
		if (Attempt < 4 && FVector::DotProduct(CamForward, Dir) > 0.15f)
		{
			continue;
		}
		const float Dist = FMath::FRandRange(Settings->CritterSpawnMin, Settings->CritterSpawnMax);
		const FVector Probe = Player->GetActorLocation() + Dir * Dist + FVector(0.f, 0.f, 4000.f);
		FHitResult Hit;
		if (!GetWorld()->LineTraceSingleByObjectType(Hit, Probe, Probe - FVector(0.f, 0.f, 12000.f), Objects, Params))
		{
			continue;
		}
		if (Hit.ImpactNormal.Z < 0.7f || Hit.ImpactPoint.Z <= Settings->SeaLevelZ)
		{
			continue; // too steep, or on/under the water
		}

		UClass* Class = Species.CritterClass.IsNull() ? AWLCritter::StaticClass() : Species.CritterClass.LoadSynchronous();
		if (!Class)
		{
			Class = AWLCritter::StaticClass();
		}
		FActorSpawnParameters SP;
		SP.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
		SP.ObjectFlags |= RF_Transient;
		const FRotator Yaw(0.f, FMath::FRandRange(0.f, 360.f), 0.f);
		if (AWLCritter* C = GetWorld()->SpawnActor<AWLCritter>(Class, Hit.ImpactPoint, Yaw, SP))
		{
			C->Init(Player, Species);
			Critters.Add(C);
		}
		return;
	}
}
