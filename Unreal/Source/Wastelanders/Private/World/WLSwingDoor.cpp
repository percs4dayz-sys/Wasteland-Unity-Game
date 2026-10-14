#include "World/WLSwingDoor.h"
#include "World/WLPlaceholderMesh.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "GameFramework/GameStateBase.h"
#include "GameFramework/PlayerState.h"
#include "Net/UnrealNetwork.h"
#include "Engine/World.h"
#include "TimerManager.h"

AWLSwingDoor::AWLSwingDoor()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.bStartWithTickEnabled = false; // ticks only while swinging
	bReplicates = true;
	SetNetUpdateFrequency(10.f);

	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	SetRootComponent(Root);

	Leaf = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Leaf"));
	Leaf->SetupAttachment(Root);
	Leaf->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
	Leaf->SetCanEverAffectNavigation(false); // routes go through doorways
}

void AWLSwingDoor::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(AWLSwingDoor, bOpen);
	DOREPLIFETIME(AWLSwingDoor, SwingSign);
}

void AWLSwingDoor::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	// Placeholder leaf: 1 m wide, 10 cm thick, 2.1 m tall, pivot on its left edge like a real door.
	if (FWLPlaceholderMesh::Apply(Leaf, ArtMesh, EWLPlaceholderShape::Cube, FVector(100.f, 10.f, 210.f), FLinearColor(0.35f, 0.25f, 0.18f)))
	{
		Leaf->SetRelativeLocation(FVector(50.f, 0.f, 105.f));
	}
}

void AWLSwingDoor::SolveHinge(const FBox& LocalBounds, FVector& OutHinge, FVector& OutFree, FVector& OutNormal)
{
	const FVector Centre = LocalBounds.GetCenter();
	const FVector Ext = LocalBounds.GetExtent();
	// The longer horizontal axis is the leaf's width.
	const bool bAlongX = Ext.X >= Ext.Y;
	const FVector Axis = bAlongX ? FVector::ForwardVector : FVector::RightVector;
	const float Half = bAlongX ? Ext.X : Ext.Y;
	const float PivotOnAxis = bAlongX ? -Centre.X : -Centre.Y; // where the pivot sits along the width
	const float HingeSide = FMath::Abs(PivotOnAxis) > Half * 0.25f ? FMath::Sign(PivotOnAxis) : -1.f;

	OutHinge = Centre + Axis * (HingeSide * Half);
	OutFree = Centre - Axis * (HingeSide * Half);
	OutHinge.Z = OutFree.Z = 0.f;
	OutNormal = bAlongX ? FVector::RightVector : FVector::ForwardVector;
}

void AWLSwingDoor::BeginPlay()
{
	Super::BeginPlay();

	LeafClosed = Leaf->GetRelativeTransform();
	if (const UStaticMesh* M = Leaf->GetStaticMesh())
	{
		const FBox LocalBounds = M->GetBoundingBox().TransformBy(LeafClosed);
		SolveHinge(LocalBounds, Hinge, Free, Normal);
		bReady = true;
	}

	if (HasAuthority() && bReady)
	{
		GetWorldTimerManager().SetTimer(CheckTimer, this, &AWLSwingDoor::ServerCheck, 0.1f, true, FMath::FRand() * 0.1f);
	}
}

void AWLSwingDoor::ServerCheck()
{
	const AGameStateBase* GS = GetWorld()->GetGameState();
	if (!GS)
	{
		return;
	}
	const FTransform& TM = GetActorTransform();
	const FVector Mid = TM.TransformPosition((Hinge + Free) * 0.5f);
	const FVector WorldNormal = TM.TransformVectorNoScale(Normal);

	float NearestDist = TNumericLimits<float>::Max();
	FVector NearestOffset = FVector::ZeroVector;
	for (const APlayerState* PS : GS->PlayerArray)
	{
		const APawn* Pawn = PS ? PS->GetPawn() : nullptr;
		if (!Pawn)
		{
			continue;
		}
		FVector D = Pawn->GetActorLocation() - Mid;
		D.Z = 0.f;
		const float Dist = D.Size();
		if (Dist < NearestDist)
		{
			NearestDist = Dist;
			NearestOffset = D;
		}
	}

	const double Now = GetWorld()->GetTimeSeconds();
	if (NearestDist <= OpenDistance)
	{
		if (!bOpen && Alpha <= 0.001f)
		{
			// Swing so the free end moves away from the side the player is on.
			const float Side = FMath::Sign(FVector::DotProduct(NearestOffset, WorldNormal));
			const FVector Arm = TM.TransformVectorNoScale(Free - Hinge);
			const FVector Swung = FRotator(0.f, 90.f, 0.f).RotateVector(FVector(Arm.X, Arm.Y, 0.f));
			SwingSign = FVector::DotProduct(Swung, WorldNormal) * Side > 0.f ? -1 : 1;
		}
		if (!bOpen)
		{
			bOpen = true;
			OnRep_DoorState();
		}
		FarSince = -1.0;
	}
	else if (NearestDist >= CloseDistance)
	{
		if (FarSince < 0.0)
		{
			FarSince = Now;
		}
		else if (bOpen && Now - FarSince >= CloseDelay)
		{
			bOpen = false;
			OnRep_DoorState();
		}
	}
	else
	{
		FarSince = -1.0;
	}
}

void AWLSwingDoor::OnRep_DoorState()
{
	if (!bReady)
	{
		return;
	}
	if (bOpen)
	{
		SetLeafSolid(false); // never shove anyone while it moves
	}
	SetActorTickEnabled(true);
}

void AWLSwingDoor::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	const float Target = bOpen ? 1.f : 0.f;
	Alpha = FMath::FInterpConstantTo(Alpha, Target, DeltaSeconds, 1.f / FMath::Max(0.05f, SwingSeconds));
	ApplyAlpha(Alpha);
	if (FMath::IsNearlyEqual(Alpha, Target))
	{
		Alpha = Target;
		SetLeafSolid(Alpha <= 0.001f);
		SetActorTickEnabled(false);
	}
}

void AWLSwingDoor::ApplyAlpha(float InAlpha)
{
	// Dedicated servers skip the pose: only collision state matters there.
	if (GetNetMode() == NM_DedicatedServer)
	{
		return;
	}
	const float Angle = SwingSign * OpenAngle * FMath::SmoothStep(0.f, 1.f, InAlpha);
	const FQuat Rot(FVector::UpVector, FMath::DegreesToRadians(Angle));
	FTransform T = LeafClosed;
	T.SetLocation(Hinge + Rot.RotateVector(LeafClosed.GetLocation() - Hinge));
	T.SetRotation(Rot * LeafClosed.GetRotation());
	Leaf->SetRelativeTransform(T);
}

void AWLSwingDoor::SetLeafSolid(bool bSolid)
{
	Leaf->SetCollisionEnabled(bSolid ? ECollisionEnabled::QueryAndPhysics : ECollisionEnabled::NoCollision);
}
