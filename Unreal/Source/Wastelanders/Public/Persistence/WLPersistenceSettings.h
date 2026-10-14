#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"
#include "WLPersistenceSettings.generated.h"

/**
 * Tunables for character persistence (Project Settings > Game > Wastelanders Persistence).
 *
 * The starter kit used to be handed out by Mara (Unity TutorialNPC.GiftIds: Machete, Shield, Rod,
 * Hatchet, Pickaxe) on first talk. A fresh MMO character gets it in its record instead, so a player who
 * logs out before reaching her still owns a kit; Mara's own "top up missing items" logic stays harmless
 * because it only gives what the player lacks.
 */
UCLASS(Config = Game, DefaultConfig, meta = (DisplayName = "Wastelanders Persistence"))
class WASTELANDERS_API UWLPersistenceSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	virtual FName GetCategoryName() const override { return TEXT("Game"); }

	static const UWLPersistenceSettings* Get() { return GetDefault<UWLPersistenceSettings>(); }

	/** Item ids placed in the first inventory slots of a brand new character. Unity TutorialNPC.GiftIds order. */
	UPROPERTY(Config, EditAnywhere, Category = "New Character")
	TArray<int32> StarterKitItemIds = { 1, 2, 5, 4, 3 };

	/** Folder under Saved/ for the JSON file backend. */
	UPROPERTY(Config, EditAnywhere, Category = "File Backend")
	FString CharactersFolder = TEXT("Characters");

	/** Keep the previous version of each record as <name>.json.bak (recovery when the main file is corrupt). */
	UPROPERTY(Config, EditAnywhere, Category = "File Backend")
	bool bKeepBackup = true;
};
