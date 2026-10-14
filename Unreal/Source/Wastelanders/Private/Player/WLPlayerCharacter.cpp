#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "Player/WLCharacterMovementComponent.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Combat/WLCombatComponent.h"
#include "Interaction/WLInteractorComponent.h"
#include "Skilling/WLSkillingComponent.h"
#include "Persistence/WLCharacterSave.h"
#include "Camera/CameraComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "Components/CapsuleComponent.h"

AWLPlayerCharacter::AWLPlayerCharacter(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer.SetDefaultSubobjectClass<UWLCharacterMovementComponent>(ACharacter::CharacterMovementComponentName))
{
	PrimaryActorTick.bCanEverTick = true;

	GetCapsuleComponent()->InitCapsuleSize(35.f, 90.f);

	// Character faces its movement, camera orbits independently (top-down/third-person OSRS feel).
	bUseControllerRotationPitch = false;
	bUseControllerRotationYaw = false;
	bUseControllerRotationRoll = false;

	CameraBoom = CreateDefaultSubobject<USpringArmComponent>(TEXT("CameraBoom"));
	CameraBoom->SetupAttachment(RootComponent);
	CameraBoom->SetUsingAbsoluteRotation(true);
	CameraBoom->TargetArmLength = TargetArmLength;
	CameraBoom->bDoCollisionTest = true;
	CameraBoom->ProbeSize = 30.f;
	CameraBoom->bEnableCameraLag = true;
	CameraBoom->CameraLagSpeed = 10.f;
	CameraBoom->SetRelativeRotation(FRotator(-CameraPitch, CameraYaw, 0.f));

	Camera = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	Camera->SetupAttachment(CameraBoom, USpringArmComponent::SocketName);
	Camera->bUsePawnControlRotation = false;

	Combat = CreateDefaultSubobject<UWLCombatComponent>(TEXT("Combat"));
	Interactor = CreateDefaultSubobject<UWLInteractorComponent>(TEXT("Interactor"));
	Skilling = CreateDefaultSubobject<UWLSkillingComponent>(TEXT("Skilling"));
}

UWLCharacterMovementComponent* AWLPlayerCharacter::GetWLMovement() const
{
	return Cast<UWLCharacterMovementComponent>(GetCharacterMovement());
}

AWLPlayerState* AWLPlayerCharacter::GetWLPlayerState() const
{
	return GetPlayerState<AWLPlayerState>();
}

UAbilitySystemComponent* AWLPlayerCharacter::GetAbilitySystemComponent() const
{
	const AWLPlayerState* PS = GetWLPlayerState();
	return PS ? PS->GetAbilitySystemComponent() : nullptr;
}

void AWLPlayerCharacter::BeginPlay()
{
	Super::BeginPlay();
	TargetArmLength = CameraBoom->TargetArmLength;
	RefreshAppearance();
}

void AWLPlayerCharacter::PossessedBy(AController* NewController)
{
	Super::PossessedBy(NewController);
	InitAbilitySystem();
}

void AWLPlayerCharacter::OnRep_PlayerState()
{
	Super::OnRep_PlayerState();
	InitAbilitySystem();
	RefreshAppearance();
}

void AWLPlayerCharacter::InitAbilitySystem()
{
	AWLPlayerState* PS = GetWLPlayerState();
	if (!PS)
	{
		return;
	}
	UAbilitySystemComponent* ASC = PS->GetAbilitySystemComponent();
	if (!ASC)
	{
		return;
	}
	ASC->InitAbilityActorInfo(PS, this);
	bAbilitySystemReady = true;
	OnAbilitySystemReady.Broadcast(this);
}

void AWLPlayerCharacter::CaptureSave(FWLCharacterSave& Out) const
{
	Out.MapName = GetWorld() ? GetWorld()->GetMapName() : FString();
	Out.Location = GetActorLocation();
	Out.Yaw = GetActorRotation().Yaw;
	Out.bHasLocation = true;
}

void AWLPlayerCharacter::RefreshAppearance_Implementation()
{
	// Intentionally empty: new modular character art will hook in here (Blueprint override).
}

void AWLPlayerCharacter::AddCameraYaw(float Degrees)
{
	CameraYaw = FRotator::NormalizeAxis(CameraYaw + Degrees);
}

void AWLPlayerCharacter::AddCameraPitch(float Degrees)
{
	CameraPitch = FMath::Clamp(CameraPitch + Degrees, MinCameraPitch, MaxCameraPitch);
}

void AWLPlayerCharacter::AddCameraZoom(float Delta)
{
	TargetArmLength = FMath::Clamp(TargetArmLength - Delta * ZoomStep, MinCameraDistance, MaxCameraDistance);
}

void AWLPlayerCharacter::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (IsLocallyControlled())
	{
		CameraBoom->TargetArmLength = FMath::FInterpTo(CameraBoom->TargetArmLength, TargetArmLength, DeltaSeconds, 12.f);
		CameraBoom->SetRelativeRotation(FRotator(-CameraPitch, CameraYaw, 0.f));
	}
}
