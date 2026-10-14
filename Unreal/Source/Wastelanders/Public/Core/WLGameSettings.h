#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"
#include "WLGameSettings.generated.h"

class UDataTable;

/** Project-wide gameplay settings (Project Settings > Game > Wastelanders). Stored in DefaultGame.ini. */
UCLASS(Config = Game, DefaultConfig, meta = (DisplayName = "Wastelanders"))
class WASTELANDERS_API UWLGameSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	UWLGameSettings();

	virtual FName GetCategoryName() const override { return TEXT("Game"); }

	static const UWLGameSettings* Get() { return GetDefault<UWLGameSettings>(); }

	UPROPERTY(Config, EditAnywhere, Category = "Data", meta = (RequiredAssetDataTags = "RowStructure=/Script/Wastelanders.WLItemRow"))
	TSoftObjectPtr<UDataTable> ItemTable;

	UPROPERTY(Config, EditAnywhere, Category = "Data", meta = (RequiredAssetDataTags = "RowStructure=/Script/Wastelanders.WLRecipeRow"))
	TSoftObjectPtr<UDataTable> RecipeTable;

	UPROPERTY(Config, EditAnywhere, Category = "Data", meta = (RequiredAssetDataTags = "RowStructure=/Script/Wastelanders.WLSkillRow"))
	TSoftObjectPtr<UDataTable> SkillTable;

	UPROPERTY(Config, EditAnywhere, Category = "Data", meta = (RequiredAssetDataTags = "RowStructure=/Script/Wastelanders.WLModuleRow"))
	TSoftObjectPtr<UDataTable> ModuleTable;

	UPROPERTY(Config, EditAnywhere, Category = "Data", meta = (RequiredAssetDataTags = "RowStructure=/Script/Wastelanders.WLGearSetRow"))
	TSoftObjectPtr<UDataTable> GearSetTable;

	/** Seconds per server game tick. 0.6 = the Unity/OSRS cadence. */
	UPROPERTY(Config, EditAnywhere, Category = "Simulation", meta = (ClampMin = "0.05", ClampMax = "5.0"))
	float TickDurationSeconds = 0.6f;

	/** Unreal units per Unity metre / game tile. */
	UPROPERTY(Config, EditAnywhere, Category = "Simulation")
	float UnitsPerTile = 100.f;
};
