#include "Companions/WLCompanionPawn.h"
#include "Player/WLPlayerState.h"
#include "World/WLPlaceholderMesh.h"
#include "AIController.h"
#include "Components/CapsuleComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Components/TextRenderComponent.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Net/UnrealNetwork.h"

AWLCompanionPawn::AWLCompanionPawn(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
	bReplicates = true;
	SetReplicatingMovement(true);
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.TickInterval = 0.1f;

	AIControllerClass = AAIController::StaticClass();
	AutoPossessAI = EAutoPossessAI::Spawned;

	GetCapsuleComponent()->InitCapsuleSize(30.f, 45.f);
	// Pets never block players or each other (MMO crowding); they still stand on the ground.
	GetCapsuleComponent()->SetCollisionResponseToChannel(ECC_Pawn, ECR_Ignore);
	GetCapsuleComponent()->SetCollisionResponseToChannel(ECC_Camera, ECR_Ignore);

	UCharacterMovementComponent* Move = GetCharacterMovement();
	Move->MaxWalkSpeed = 1100.f;   // keeps up with a running owner (550 x 1.8)
	Move->bOrientRotationToMovement = true;
	Move->RotationRate = FRotator(0.f, 720.f, 0.f);
	bUseControllerRotationYaw = false;

	PlaceholderBody = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("PlaceholderBody"));
	PlaceholderBody->SetupAttachment(GetCapsuleComponent());
	PlaceholderBody->SetCollisionEnabled(ECollisionEnabled::NoCollision);

	NameTag = CreateDefaultSubobject<UTextRenderComponent>(TEXT("NameTag"));
	NameTag->SetupAttachment(GetCapsuleComponent());
	NameTag->SetHorizontalAlignment(EHTA_Center);
	NameTag->SetWorldSize(18.f);
	NameTag->SetTextRenderColor(FColor(201, 162, 39));
	NameTag->SetRelativeLocation(FVector(0.f, 0.f, 90.f));
}

void AWLCompanionPawn::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(AWLCompanionPawn, OwnerPlayerState);
	DOREPLIFETIME(AWLCompanionPawn, Species);
	DOREPLIFETIME(AWLCompanionPawn, Stage);
	DOREPLIFETIME(AWLCompanionPawn, DisplayName);
	DOREPLIFETIME(AWLCompanionPawn, BodyScale);
	DOREPLIFETIME(AWLCompanionPawn, HoverHeight);
}

void AWLCompanionPawn::SetAppearance(FName InSpecies, int32 InStage, const FText& InName, float InScale, float InHoverHeight)
{
	Species = InSpecies;
	Stage = InStage;
	DisplayName = InName;
	BodyScale = InScale;
	HoverHeight = InHoverHeight;
	ApplyAppearance();
	ForceNetUpdate();
}

void AWLCompanionPawn::OnRep_Appearance()
{
	ApplyAppearance();
}

void AWLCompanionPawn::ApplyAppearance()
{
	const float Height = BaseHeight * BodyScale;
	// No new-art mesh is assigned at this level; Blueprint children replace the placeholder.
	FWLPlaceholderMesh::Apply(PlaceholderBody, TSoftObjectPtr<UStaticMesh>(), EWLPlaceholderShape::Sphere,
		FVector(Height * 1.3f, Height, Height), FLinearColor(0.5f, 0.42f, 0.34f));   // Unity's fallback brown
	const float HalfHeight = GetCapsuleComponent()->GetScaledCapsuleHalfHeight();
	PlaceholderBody->SetRelativeLocation(FVector(0.f, 0.f, -HalfHeight + Height * 0.5f + HoverHeight));
	NameTag->SetText(DisplayName);
	NameTag->SetRelativeLocation(FVector(0.f, 0.f, -HalfHeight + Height + HoverHeight + 30.f));
}

void AWLCompanionPawn::SetGoal(const FVector& InGoal, AActor* InFaceTarget)
{
	Goal = InGoal;
	FaceTarget = InFaceTarget;
	bHasGoal = true;
}

void AWLCompanionPawn::TeleportNear(const FVector& Location)
{
	SetActorLocation(Location + FVector(0.f, 0.f, GetCapsuleComponent()->GetScaledCapsuleHalfHeight()), false, nullptr, ETeleportType::TeleportPhysics);
	LastMoveGoal = FVector(TNumericLimits<float>::Max());
}

void AWLCompanionPawn::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (!HasAuthority() || !bHasGoal)
	{
		return;
	}

	if (AAIController* AI = Cast<AAIController>(GetController()))
	{
		// Re-path only when the goal really moved: MoveTo every frame would thrash the path follower.
		if (FVector::DistSquared2D(Goal, LastMoveGoal) > FMath::Square(40.f))
		{
			LastMoveGoal = Goal;
			AI->MoveToLocation(Goal, /*AcceptanceRadius*/ 20.f, /*bStopOnOverlap*/ false, /*bUsePathfinding*/ true,
				/*bProjectDestinationToNavigation*/ true, /*bCanStrafe*/ true);
		}
	}

	// Face the enemy while fighting (Unity turned the model toward the target).
	if (const AActor* Face = FaceTarget.Get())
	{
		FVector To = Face->GetActorLocation() - GetActorLocation();
		To.Z = 0.f;
		if (To.SizeSquared() > 4.f)
		{
			const FRotator Want = To.Rotation();
			SetActorRotation(FMath::RInterpTo(GetActorRotation(), Want, DeltaSeconds, 8.f));
		}
	}
}
