#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"
#include "World/WLPlaceholderMesh.h"
#include "WLWorldSettings.generated.h"

class UStaticMesh;
class UWLBuildCatalog;
class AWLCritter;

/**
 * One ambient critter species (Unity CritterLife.Species). The Unity table referenced vendor models
 * and animator controllers that are deliberately NOT ported; here each species is a placeholder
 * shape at roughly real size until new art fills Mesh / CritterClass.
 */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLCritterSpecies
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	FName Name;

	/** Optional actor class (new art). Unset = AWLCritter with a placeholder shape. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	TSoftClassPtr<AWLCritter> CritterClass;

	/** Optional mesh for the default critter class. Unset = PlaceholderShape. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	TSoftObjectPtr<UStaticMesh> Mesh;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	EWLPlaceholderShape PlaceholderShape = EWLPlaceholderShape::Sphere;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	FLinearColor PlaceholderColor = FLinearColor(0.5f, 0.4f, 0.3f);

	/** Placeholder body size in cm (X length, Y width, Z height). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	FVector BodySize = FVector(40.f, 25.f, 25.f);

	/** cm/s while wandering. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	float WalkSpeed = 110.f;

	/** cm/s while fleeing. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	float FleeSpeed = 220.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	float ScaleMin = 1.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	float ScaleMax = 1.f;

	/** Unity moveInPlace: "wander" animates in place and only covers ground when fleeing. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	bool bMoveInPlace = false;

	/** Unity flier: on flee it takes off and climbs away instead of settling. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Critter")
	bool bFlier = false;
};

/** Where a "travel to map" request goes (Unity SceneWarp scene names -> server shards). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLTravelDestination
{
	GENERATED_BODY()

	/** Map package to open when the shard runs in the same process (PIE / standalone), e.g. /Game/Maps/MainWorld. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Travel")
	FString MapPath;

	/** Server address for that map's shard ("host:port"). Empty = same server, use MapPath. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Travel")
	FString ServerAddress;
};

/**
 * World-system tuning (Project Settings > Game > Wastelanders World). Holds the numbers Unity
 * hard-coded in its self-bootstrapping world managers (CritterLife, OccluderHider, GeigerCounter,
 * SceneWarp, BuildManager) so designers can tune them; the Unity values are the defaults.
 */
UCLASS(Config = Game, DefaultConfig, meta = (DisplayName = "Wastelanders World"))
class WASTELANDERS_API UWLWorldSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	UWLWorldSettings();

	virtual FName GetCategoryName() const override { return TEXT("Game"); }

	static const UWLWorldSettings* Get() { return GetDefault<UWLWorldSettings>(); }

	// --- Building -------------------------------------------------------------------------------

	/** Build catalog asset. Unset = the UWLBuildCatalog class defaults (the Unity BuildCatalog). */
	UPROPERTY(Config, EditAnywhere, Category = "Building")
	TSoftObjectPtr<UWLBuildCatalog> BuildCatalog;

	/** Furthest a placement may be from the builder's pawn (uu). Unity placed under the cursor anywhere on screen. */
	UPROPERTY(Config, EditAnywhere, Category = "Building")
	float MaxPlaceDistance = 1500.f;

	// --- Water ----------------------------------------------------------------------------------

	/** Unity LivingWorld/CritterLife sea level (50.3 m + margin). Critters never spawn at or below it. */
	UPROPERTY(Config, EditAnywhere, Category = "Water")
	float SeaLevelZ = 5070.f;

	// --- Critters (Unity CritterLife) -----------------------------------------------------------

	UPROPERTY(Config, EditAnywhere, Category = "Critters")
	bool bEnableCritters = true;

	UPROPERTY(Config, EditAnywhere, Category = "Critters", meta = (ClampMin = "0"))
	int32 MaxCritters = 6;

	UPROPERTY(Config, EditAnywhere, Category = "Critters")
	float CritterSpawnMin = 2800.f;

	UPROPERTY(Config, EditAnywhere, Category = "Critters")
	float CritterSpawnMax = 4600.f;

	UPROPERTY(Config, EditAnywhere, Category = "Critters")
	float CritterDespawnDistance = 6000.f;

	UPROPERTY(Config, EditAnywhere, Category = "Critters")
	float CritterSpawnInterval = 4.f;

	UPROPERTY(Config, EditAnywhere, Category = "Critters")
	TArray<FWLCritterSpecies> CritterSpecies;

	// --- Overhead occluders (Unity OccluderHider) ------------------------------------------------

	UPROPERTY(Config, EditAnywhere, Category = "Occlusion")
	bool bHideOverheadOccluders = true;

	/** Horizontal reach of overhead hiding (Unity overheadRadius 6 m). */
	UPROPERTY(Config, EditAnywhere, Category = "Occlusion")
	float OverheadRadius = 600.f;

	/** Clearance above the feet that still counts as head height (Unity headroom 2.2 m). */
	UPROPERTY(Config, EditAnywhere, Category = "Occlusion")
	float OverheadHeadroom = 220.f;

	UPROPERTY(Config, EditAnywhere, Category = "Occlusion")
	float OccluderScanInterval = 0.05f;

	UPROPERTY(Config, EditAnywhere, Category = "Occlusion")
	float OccluderCacheInterval = 5.f;

	/** Candidates are gathered within this distance of the player on each cache rebuild. */
	UPROPERTY(Config, EditAnywhere, Category = "Occlusion")
	float OccluderCacheRadius = 5000.f;

	// --- Geiger counter (Unity GeigerCounter) ---------------------------------------------------

	/** Item id of the Geiger Counter (Unity FissionItems.GeigerCounter = 400). */
	UPROPERTY(Config, EditAnywhere, Category = "Geiger")
	int32 GeigerItemId = 400;

	UPROPERTY(Config, EditAnywhere, Category = "Geiger")
	float GeigerDetectRange = 40000.f;

	UPROPERTY(Config, EditAnywhere, Category = "Geiger")
	float GeigerScanInterval = 0.4f;

	UPROPERTY(Config, EditAnywhere, Category = "Geiger")
	float GeigerMinClickHz = 0.6f;

	UPROPERTY(Config, EditAnywhere, Category = "Geiger")
	float GeigerMaxClickHz = 8.f;

	// --- Travel (Unity SceneWarp) ---------------------------------------------------------------

	/** Named destinations ("MainWorld", "TutorialIsland") for portals and travel requests. */
	UPROPERTY(Config, EditAnywhere, Category = "Travel")
	TMap<FName, FWLTravelDestination> TravelDestinations;

	/** Where the tutorial portal sends players. */
	UPROPERTY(Config, EditAnywhere, Category = "Travel")
	FName MainWorldDestination = TEXT("MainWorld");
};
