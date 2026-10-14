#pragma once

#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "Core/WLTypes.h"
#include "World/WLPlaceholderMesh.h"
#include "WLBuildCatalog.generated.h"

class AActor;

/** What a placed deployable becomes. Ported from BuildCatalog.cs BuildKind. */
UENUM(BlueprintType)
enum class EWLBuildKind : uint8
{
	CookingFire,  // AWLCraftingStation (CookingFire)
	Furnace,      // AWLCraftingStation (Furnace)
	Workbench,    // AWLCraftingStation (Workbench)
	BankChest,    // AWLBankingCrate
	AFKStation,   // AWLAFKStation
	Turret,       // AWLTurret
	HousingPlot   // AWLHousingPlot (sets the housing_plot flag; marker only)
};

/** One material of a build cost (Unity CraftingIngredient). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLBuildCost
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Build") int32 ItemId = 0;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Build") int32 Quantity = 0;
};

/** One buildable in the Hearthcraft construction menu (Unity Buildable). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLBuildable
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Build") FText Name;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Build", meta = (MultiLine = true)) FText Description;

	/** Hearthcraft level required. Hearthcraft is the Unity name for EWLSkill::Smithing. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Build", meta = (ClampMin = "1")) int32 HearthLevel = 1;
	/** Hearthcraft XP granted on placement. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Build") int32 XP = 0;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Build") TArray<FWLBuildCost> Cost;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Build") EWLBuildKind Kind = EWLBuildKind::CookingFire;

	// AFKStation params (ignored by other kinds).
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AFK") EWLSkill AFKSkill = EWLSkill::Scrapping;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AFK") int32 AFKProduceItemId = 0;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "AFK") int32 AFKConsumeItemId = 0;

	// Turret params (ignored by other kinds).
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Turret") int32 TurretMaxHit = 3;
	/** uu (Unity metres x 100). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Turret") float TurretRange = 800.f;

	/** Placeholder footprint in cm (Unity metres x 100). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Visual") FVector Size = FVector(120.f, 120.f, 120.f);
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Visual") FLinearColor Color = FLinearColor(0.5f, 0.5f, 0.55f);

	/** New-art actor class. Unset = the default class for Kind with an engine-shape placeholder. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Visual") TSubclassOf<AActor> OverrideClass;
};

/** Pure build rules (unit tested without a world). */
struct WASTELANDERS_API FWLBuildRules
{
	static bool MeetsLevel(int32 HearthLevel, const FWLBuildable& B) { return HearthLevel >= B.HearthLevel; }

	/** CountOf maps an item id to how many the player holds. */
	static bool CanAfford(TFunctionRef<int32(int32)> CountOf, const FWLBuildable& B);

	/** Server placement sanity: finite, and within MaxDistance of the builder (2D+Z, uu). */
	static bool IsPlacementReachable(const FVector& Builder, const FVector& Target, float MaxDistance);

	/** Yaw snapped into [0, 360). The Unity ghost rotates in 45 degree steps; the server only normalises. */
	static float NormalizeYaw(float Yaw);
};

/**
 * The Hearthcraft construction catalog (Unity BuildCatalog.cs static list), as a data asset so
 * designers can add buildables and assign new-art classes. The constructor fills the Unity list, so
 * an unset UWLWorldSettings::BuildCatalog falls back to the class defaults (see Get).
 * Item ids: wood 40/220-223, bars 11/70-73, raw scrap 10, raw fish 22.
 */
UCLASS(BlueprintType)
class WASTELANDERS_API UWLBuildCatalog : public UDataAsset
{
	GENERATED_BODY()

public:
	UWLBuildCatalog();

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Build")
	TArray<FWLBuildable> Buildables;

	/** The project catalog: UWLWorldSettings::BuildCatalog if set, else the class default object. */
	static const UWLBuildCatalog* Get();

	UFUNCTION(BlueprintPure, Category = "Build")
	bool IsValidIndex(int32 Index) const { return Buildables.IsValidIndex(Index); }
};
