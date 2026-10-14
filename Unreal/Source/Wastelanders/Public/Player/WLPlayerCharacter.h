#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "AbilitySystemInterface.h"
#include "WLPlayerCharacter.generated.h"

class USpringArmComponent;
class UCameraComponent;
class UWLCharacterMovementComponent;
class UWLCombatComponent;
class UWLInteractorComponent;
class UWLSkillingComponent;
class AWLPlayerState;
struct FWLCharacterSave;

DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnAbilitySystemReady, class AWLPlayerCharacter*);

/**
 * The player's body in the world (Unity Player3DController + OrbitCamera3D + PlayerAnimator3D host).
 * Persistent data is on AWLPlayerState; this class owns movement, camera and the per-body action
 * components (combat, interaction, skilling).
 *
 * Art: no Unity models are ported. Until new character art exists the mesh slot is left for a
 * Blueprint child (BP_PlayerCharacter) to fill, and RefreshAppearance applies Appearance choices.
 */
UCLASS()
class WASTELANDERS_API AWLPlayerCharacter : public ACharacter, public IAbilitySystemInterface
{
	GENERATED_BODY()

public:
	AWLPlayerCharacter(const FObjectInitializer& ObjectInitializer);

	virtual UAbilitySystemComponent* GetAbilitySystemComponent() const override;
	virtual void PossessedBy(AController* NewController) override;
	virtual void OnRep_PlayerState() override;
	virtual void BeginPlay() override;

	AWLPlayerState* GetWLPlayerState() const;

	UWLCharacterMovementComponent* GetWLMovement() const;
	UWLCombatComponent* GetCombat() const { return Combat; }
	UWLInteractorComponent* GetInteractor() const { return Interactor; }
	UWLSkillingComponent* GetSkilling() const { return Skilling; }
	USpringArmComponent* GetCameraBoom() const { return CameraBoom; }
	UCameraComponent* GetCamera() const { return Camera; }

	/** Fires on server and owning client once the ASC knows this pawn as its avatar. Combat binds here. */
	FWLOnAbilitySystemReady OnAbilitySystemReady;
	bool IsAbilitySystemReady() const { return bAbilitySystemReady; }

	/** SERVER. Write position into the save. */
	void CaptureSave(FWLCharacterSave& Out) const;

	/** Re-apply character-creator choices to the body. Override in Blueprint once modular art exists. */
	UFUNCTION(BlueprintNativeEvent, Category = "Wastelanders|Appearance")
	void RefreshAppearance();

	// --- camera (Unity OrbitCamera3D defaults: distance 16 m, pitch 52 deg, 8-85 deg tilt, zoom 0.35-26 m)
	void AddCameraYaw(float Degrees);
	void AddCameraPitch(float Degrees);
	void AddCameraZoom(float Delta);

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Camera")
	float MinCameraDistance = 150.f;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Camera")
	float MaxCameraDistance = 2600.f;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Camera")
	float MinCameraPitch = 8.f;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Camera")
	float MaxCameraPitch = 85.f;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Camera")
	float ZoomStep = 120.f;

protected:
	void InitAbilitySystem();

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<USpringArmComponent> CameraBoom;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UCameraComponent> Camera;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UWLCombatComponent> Combat;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UWLInteractorComponent> Interactor;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UWLSkillingComponent> Skilling;

private:
	float TargetArmLength = 1600.f;
	float CameraPitch = 52.f;
	float CameraYaw = 0.f;
	bool bAbilitySystemReady = false;

	virtual void Tick(float DeltaSeconds) override;
};
