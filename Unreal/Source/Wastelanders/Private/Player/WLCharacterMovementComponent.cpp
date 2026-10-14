#include "Player/WLCharacterMovementComponent.h"
#include "GameFramework/Character.h"

UWLCharacterMovementComponent::UWLCharacterMovementComponent()
{
	MaxWalkSpeed = WalkSpeed;
	// No jump in this game (Unity: "the player should always be on the ground").
	NavAgentProps.bCanJump = false;
	bOrientRotationToMovement = true;
	RotationRate = FRotator(0.f, 720.f, 0.f);
	BrakingDecelerationWalking = 4096.f;
	GroundFriction = 12.f;
}

float UWLCharacterMovementComponent::GetMaxSpeed() const
{
	if (IsDodging())
	{
		return DodgeDistance / FMath::Max(DodgeDuration, 0.05f);
	}
	float Speed = WalkSpeed * ExternalSpeedMultiplier;
	if (bWantsToRun && MovementMode == MOVE_Walking)
	{
		Speed *= RunMultiplier;
	}
	return Speed;
}

void UWLCharacterMovementComponent::UpdateFromCompressedFlags(uint8 Flags)
{
	Super::UpdateFromCompressedFlags(Flags);
	bWantsToRun = (Flags & FSavedMove_Character::FLAG_Custom_0) != 0;
	bWantsToDodge = (Flags & FSavedMove_Character::FLAG_Custom_1) != 0;
}

void UWLCharacterMovementComponent::TryStartDodge()
{
	bWantsToDodge = false;
	if (IsDodging() || DodgeCooldownRemaining > 0.f || !IsMovingOnGround())
	{
		return;
	}
	// Slide toward the stick / keys if held, otherwise straight ahead.
	FVector Dir = GetCurrentAcceleration().GetSafeNormal2D();
	if (Dir.IsNearlyZero())
	{
		Dir = CharacterOwner ? CharacterOwner->GetActorForwardVector().GetSafeNormal2D() : FVector::ForwardVector;
	}
	DodgeDirection = Dir;
	DodgeTimeRemaining = DodgeDuration;
	DodgeCooldownRemaining = DodgeDuration + DodgeCooldown;
}

void UWLCharacterMovementComponent::CalcVelocity(float DeltaTime, float Friction, bool bFluid, float BrakingDeceleration)
{
	if (IsDodging() && MovementMode == MOVE_Walking)
	{
		// The slide ignores input steering: constant burst along the committed direction.
		const float Z = Velocity.Z;
		Velocity = DodgeDirection * GetMaxSpeed();
		Velocity.Z = Z;
		return;
	}
	Super::CalcVelocity(DeltaTime, Friction, bFluid, BrakingDeceleration);
}

void UWLCharacterMovementComponent::OnMovementUpdated(float DeltaSeconds, const FVector& OldLocation, const FVector& OldVelocity)
{
	Super::OnMovementUpdated(DeltaSeconds, OldLocation, OldVelocity);

	DodgeTimeRemaining = FMath::Max(0.f, DodgeTimeRemaining - DeltaSeconds);
	DodgeCooldownRemaining = FMath::Max(0.f, DodgeCooldownRemaining - DeltaSeconds);
	if (bWantsToDodge)
	{
		TryStartDodge();
	}
}

FNetworkPredictionData_Client* UWLCharacterMovementComponent::GetPredictionData_Client() const
{
	if (!ClientPredictionData)
	{
		UWLCharacterMovementComponent* MutableThis = const_cast<UWLCharacterMovementComponent*>(this);
		MutableThis->ClientPredictionData = new FNetworkPredictionData_Client_WL(*this);
	}
	return ClientPredictionData;
}

// ---------------------------------------------------------------------------------------------

void FSavedMove_WL::Clear()
{
	Super::Clear();
	bSavedWantsToRun = false;
	bSavedWantsToDodge = false;
	SavedDodgeTimeRemaining = 0.f;
	SavedDodgeCooldownRemaining = 0.f;
	SavedDodgeDirection = FVector::ForwardVector;
}

uint8 FSavedMove_WL::GetCompressedFlags() const
{
	uint8 Result = Super::GetCompressedFlags();
	if (bSavedWantsToRun)
	{
		Result |= FLAG_Custom_0;
	}
	if (bSavedWantsToDodge)
	{
		Result |= FLAG_Custom_1;
	}
	return Result;
}

bool FSavedMove_WL::CanCombineWith(const FSavedMovePtr& NewMove, ACharacter* InCharacter, float MaxDelta) const
{
	const FSavedMove_WL* Other = static_cast<const FSavedMove_WL*>(NewMove.Get());
	if (bSavedWantsToRun != Other->bSavedWantsToRun || bSavedWantsToDodge || Other->bSavedWantsToDodge)
	{
		return false;
	}
	return Super::CanCombineWith(NewMove, InCharacter, MaxDelta);
}

void FSavedMove_WL::SetMoveFor(ACharacter* C, float InDeltaTime, FVector const& NewAccel, FNetworkPredictionData_Client_Character& ClientData)
{
	Super::SetMoveFor(C, InDeltaTime, NewAccel, ClientData);
	if (const UWLCharacterMovementComponent* Move = Cast<UWLCharacterMovementComponent>(C->GetCharacterMovement()))
	{
		bSavedWantsToRun = Move->bWantsToRun;
		bSavedWantsToDodge = Move->bWantsToDodge;
		SavedDodgeTimeRemaining = Move->DodgeTimeRemaining;
		SavedDodgeCooldownRemaining = Move->DodgeCooldownRemaining;
		SavedDodgeDirection = Move->DodgeDirection;
	}
}

void FSavedMove_WL::PrepMoveFor(ACharacter* C)
{
	Super::PrepMoveFor(C);
	if (UWLCharacterMovementComponent* Move = Cast<UWLCharacterMovementComponent>(C->GetCharacterMovement()))
	{
		Move->DodgeTimeRemaining = SavedDodgeTimeRemaining;
		Move->DodgeCooldownRemaining = SavedDodgeCooldownRemaining;
		Move->DodgeDirection = SavedDodgeDirection;
	}
}
