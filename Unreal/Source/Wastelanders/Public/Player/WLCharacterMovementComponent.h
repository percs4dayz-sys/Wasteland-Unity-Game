#pragma once

#include "CoreMinimal.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "WLCharacterMovementComponent.generated.h"

/**
 * Client-predicted run and dodge (Unity Player3DController: hold Ctrl to run at 1.8x, Space to slide
 * 4.5 m over 0.35 s with a 5 s cooldown). Both travel in the saved-move compressed flags so the server
 * replays them exactly and the client never gets corrected for sprinting.
 */
UCLASS()
class WASTELANDERS_API UWLCharacterMovementComponent : public UCharacterMovementComponent
{
	GENERATED_BODY()

	friend class FSavedMove_WL;

public:
	UWLCharacterMovementComponent();

	/** Unity moveSpeed 5.5 m/s. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Movement")
	float WalkSpeed = 550.f;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Movement")
	float RunMultiplier = 1.8f;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Dodge")
	float DodgeDistance = 450.f;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Dodge")
	float DodgeDuration = 0.35f;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Dodge")
	float DodgeCooldown = 5.f;

	/** Multiplier applied by combat while aiming (Unity MoveMultiplier). Not predicted; keep it cosmetic-small. */
	UPROPERTY(Transient, BlueprintReadWrite, Category = "Wastelanders|Movement")
	float ExternalSpeedMultiplier = 1.f;

	void SetWantsToRun(bool bRun) { bWantsToRun = bRun; }
	void RequestDodge() { bWantsToDodge = true; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Movement")
	bool IsRunning() const { return bWantsToRun && !IsDodging() && Velocity.SizeSquared2D() > 1.f; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Movement")
	bool IsDodging() const { return DodgeTimeRemaining > 0.f; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Movement")
	float GetDodgeCooldownRemaining() const { return DodgeCooldownRemaining; }

	virtual float GetMaxSpeed() const override;
	virtual void UpdateFromCompressedFlags(uint8 Flags) override;
	virtual class FNetworkPredictionData_Client* GetPredictionData_Client() const override;

protected:
	virtual void OnMovementUpdated(float DeltaSeconds, const FVector& OldLocation, const FVector& OldVelocity) override;
	virtual void CalcVelocity(float DeltaTime, float Friction, bool bFluid, float BrakingDeceleration) override;

private:
	void TryStartDodge();

	uint8 bWantsToRun : 1 = false;
	uint8 bWantsToDodge : 1 = false;

	float DodgeTimeRemaining = 0.f;
	float DodgeCooldownRemaining = 0.f;
	FVector DodgeDirection = FVector::ForwardVector;
};

class FSavedMove_WL : public FSavedMove_Character
{
public:
	typedef FSavedMove_Character Super;

	virtual void Clear() override;
	virtual uint8 GetCompressedFlags() const override;
	virtual bool CanCombineWith(const FSavedMovePtr& NewMove, ACharacter* InCharacter, float MaxDelta) const override;
	virtual void SetMoveFor(ACharacter* C, float InDeltaTime, FVector const& NewAccel, FNetworkPredictionData_Client_Character& ClientData) override;
	virtual void PrepMoveFor(ACharacter* C) override;

	uint8 bSavedWantsToRun : 1 = false;
	uint8 bSavedWantsToDodge : 1 = false;
	float SavedDodgeTimeRemaining = 0.f;
	float SavedDodgeCooldownRemaining = 0.f;
	FVector SavedDodgeDirection = FVector::ForwardVector;
};

class FNetworkPredictionData_Client_WL : public FNetworkPredictionData_Client_Character
{
public:
	typedef FNetworkPredictionData_Client_Character Super;

	explicit FNetworkPredictionData_Client_WL(const UCharacterMovementComponent& ClientMovement) : Super(ClientMovement) {}

	virtual FSavedMovePtr AllocateNewMove() override { return FSavedMovePtr(new FSavedMove_WL()); }
};
