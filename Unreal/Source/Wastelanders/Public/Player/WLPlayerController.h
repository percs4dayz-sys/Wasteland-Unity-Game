#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"
#include "WLPlayerController.generated.h"

class UInputAction;
class UInputMappingContext;
class AWLPlayerCharacter;
class AWLPlayerState;
struct FInputActionValue;

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FWLOnGameMessage, const FText&, Message);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FWLOnContextMenuRequested, AActor*, Target, FVector2D, ScreenPosition);

/**
 * Input and the client-side half of moving around (Unity Player3DController, ClickToMove3D,
 * OrbitCamera3D input, ChatCommands routing).
 *
 * Controls (same as Unity): WASD/left stick move, left click ground to walk / object to use its
 * default action, right click for the context menu, Q/E or middle-drag to orbit, wheel to zoom,
 * hold Ctrl to run, Space to dodge.
 *
 * Input assets: if no mapping context is assigned in a Blueprint child, a default one is built in
 * code so the project is playable before any input assets are authored.
 */
UCLASS()
class WASTELANDERS_API AWLPlayerController : public APlayerController
{
	GENERATED_BODY()

public:
	AWLPlayerController();

	/** Server -> owning client game/chat message (Unity's chat log lines). */
	UFUNCTION(Client, Reliable)
	void ClientGameMessage(const FText& Message);

	/** UI listens to this to append chat/game messages. */
	UPROPERTY(BlueprintAssignable, Category = "Wastelanders|UI")
	FWLOnGameMessage OnGameMessage;

	/** UI listens to this to open the right-click context menu. */
	UPROPERTY(BlueprintAssignable, Category = "Wastelanders|UI")
	FWLOnContextMenuRequested OnContextMenuRequested;

	/** Walk to a world point along the navmesh (client-side path, predicted movement). */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Movement")
	void MoveToLocation(const FVector& Destination, float AcceptanceRadius = 25.f);

	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Movement")
	void StopPathFollowing();

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Movement")
	bool HasDestination() const { return PathPoints.Num() > 0; }

	/** Where click-to-move is headed (the minimap draws a flag here). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Movement")
	FVector GetDestination() const { return PathPoints.Num() ? PathPoints.Last() : FVector::ZeroVector; }

	/** Chat box submit: "/" commands are handled on the server, everything else is chat. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Chat")
	void SubmitChat(const FString& Text);

	AWLPlayerCharacter* GetWLCharacter() const;
	AWLPlayerState* GetWLPlayerState() const;

	// Optional authored input assets (else built at runtime).
	UPROPERTY(EditDefaultsOnly, Category = "Input")
	TObjectPtr<UInputMappingContext> DefaultMappingContext;

	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> MoveAction;
	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> PrimaryClickAction;
	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> SecondaryClickAction;
	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> OrbitKeysAction;
	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> OrbitDragAction;
	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> OrbitDragEnableAction;
	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> ZoomAction;
	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> RunAction;
	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> DodgeAction;
	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> CompanionToggleAction;
	UPROPERTY(EditDefaultsOnly, Category = "Input") TObjectPtr<UInputAction> CompanionSpecialAction;

	UPROPERTY(EditAnywhere, Category = "Input")
	float KeyOrbitSpeed = 90.f;

	UPROPERTY(EditAnywhere, Category = "Input")
	float MouseOrbitSpeed = 4.f;

protected:
	virtual void BeginPlay() override;
	virtual void SetupInputComponent() override;
	virtual void PlayerTick(float DeltaTime) override;

	UFUNCTION(Server, Reliable)
	void ServerChat(const FString& Text);

	void BuildDefaultInput();

	void OnMove(const FInputActionValue& Value);
	void OnPrimaryClick(const FInputActionValue& Value);
	void OnSecondaryClick(const FInputActionValue& Value);
	void OnOrbitKeys(const FInputActionValue& Value);
	void OnOrbitDrag(const FInputActionValue& Value);
	void OnOrbitDragEnable(const FInputActionValue& Value);
	void OnOrbitDragDisable(const FInputActionValue& Value);
	void OnZoom(const FInputActionValue& Value);
	void OnRunStart(const FInputActionValue& Value);
	void OnRunStop(const FInputActionValue& Value);
	void OnDodge(const FInputActionValue& Value);
	void OnCompanionToggle(const FInputActionValue& Value);
	void OnCompanionSpecial(const FInputActionValue& Value);

	void FollowPath();

private:
	TArray<FVector> PathPoints;
	int32 PathIndex = 0;
	float PathAcceptance = 25.f;
	bool bOrbitDragging = false;
};
