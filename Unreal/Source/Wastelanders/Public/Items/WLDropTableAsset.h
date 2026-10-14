#pragma once

#include "CoreMinimal.h"
#include "Templates/Function.h"
#include "Math/RandomStream.h"
#include "Engine/DataAsset.h"
#include "Data/WLDataTypes.h"
#include "WLDropTableAsset.generated.h"

class UWLLootSettings;

/** One weighted result of a roll (MonsterDropTable.Outcome). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLDropOutcome
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Drop")
	int32 ItemId = 10;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Drop", meta = (ClampMin = "1"))
	int32 MinQuantity = 1;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Drop", meta = (ClampMin = "1"))
	int32 MaxQuantity = 1;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Drop", meta = (ClampMin = "0"))
	float Weight = 1.f;
};

/** One independent roll (MonsterDropTable.Roll). A won roll picks exactly one weighted outcome. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLDropRoll
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Drop")
	FString Label = TEXT("New drop");

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Drop", meta = (ClampMin = "0", ClampMax = "1"))
	float Chance = 1.f;

	/** Allow the killer's Salvage module a second chance (10% per power) when this roll fails. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Drop")
	bool bSalvageBonus = false;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Drop")
	TArray<FWLDropOutcome> Outcomes;
};

/**
 * Custom per-monster drop table. Ported from Assets/Scripts/Combat/MonsterDropTable.cs
 * (a ScriptableObject there). Assign one to an enemy to replace the standard tier table.
 */
UCLASS(BlueprintType)
class WASTELANDERS_API UWLDropTableAsset : public UPrimaryDataAsset
{
	GENERATED_BODY()

public:
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Drops")
	TArray<FWLDropRoll> Rolls;

	/** -1 keeps the standard cosmetic skin chance (normal 2%, miniboss 10%, boss 25%). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Drops", meta = (ClampMin = "-1", ClampMax = "1"))
	float CosmeticChance = -1.f;

	/**
	 * MonsterDropTable.RollDrops. Kept separate from world spawning so drop logic is testable.
	 * IsValidItem filters outcomes whose item no longer exists.
	 */
	void RollDrops(float SalvagePower, FRandomStream& Rng, TFunctionRef<bool(int32)> IsValidItem, TArray<FWLItemQuantity>& Out) const;

	/** Shared implementation (also used for tables built in code). */
	static void RollTable(const TArray<FWLDropRoll>& InRolls, float SalvagePower, float SalvageChancePerPower, FRandomStream& Rng, TFunctionRef<bool(int32)> IsValidItem, TArray<FWLItemQuantity>& Out);

	// --- editor convenience: EnemyLoot.CreateDefaultTable -----------------------------------

	UPROPERTY(EditAnywhere, Category = "Defaults", meta = (ClampMin = "1", ClampMax = "5"))
	int32 DefaultsTier = 1;

	UPROPERTY(EditAnywhere, Category = "Defaults")
	bool bDefaultsMiniBoss = false;

	UPROPERTY(EditAnywhere, Category = "Defaults")
	bool bDefaultsGodHunter = false;

	/** Replace Rolls with an editable copy of the standard table for the settings above. */
	UFUNCTION(CallInEditor, Category = "Defaults")
	void FillFromStandardTable();
};
