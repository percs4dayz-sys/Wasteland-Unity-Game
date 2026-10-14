#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "GameFramework/PlayerController.h"
#include "UI/WLCanvasHUDBase.h"
#include "WLFrontendGameMode.generated.h"

/**
 * Front-end rules for the menu map (Config: GameDefaultMap = L_Frontend; set this as that map's
 * GameMode override). Spawns no pawn: the menu is a pure canvas HUD plus a mouse-driven controller.
 * Replaces Unity's CharacterSelect scene (CharacterSelectUI + CharacterCreatorUI).
 */
UCLASS()
class WASTELANDERS_API AWLFrontendGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	AWLFrontendGameMode();
};

/** Shows the cursor and routes input to the UI only; no pawn, no world input. */
UCLASS()
class WASTELANDERS_API AWLFrontendPlayerController : public APlayerController
{
	GENERATED_BODY()

public:
	AWLFrontendPlayerController();
	virtual void BeginPlay() override;
};

/**
 * Asset-free character screen: name, gender (CharacterSelectUI), look rows with -/+ steppers and
 * Randomise (CharacterCreatorUI), server address, and Start (UWLFrontendSubsystem::Join ->
 * UWLGameInstance::JoinWorld). Text entry uses two Slate boxes; everything else is canvas buttons.
 */
UCLASS()
class WASTELANDERS_API AWLFrontendHUD : public AWLCanvasHUDBase
{
	GENERATED_BODY()

public:
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

protected:
	virtual void DrawCanvas() override;

private:
	void EnsureTextBoxes();
	void RemoveTextBoxes();
	void DrawRow(class UWLFrontendSubsystem& FE, FName Row, float X, float& Y, float W);

	TSharedPtr<class SWidget> Container;
	TSharedPtr<class SEditableTextBox> NameBox;
	TSharedPtr<class SEditableTextBox> AddressBox;
	FRandomStream Random;
	bool bInitialised = false;
	FString Status;
};
