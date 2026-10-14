#pragma once

#include "CoreMinimal.h"
#include "Math/RandomStream.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "Data/WLDataTypes.h"
#include "WLLootLibrary.generated.h"

class AActor;
class AWLPlayerState;
class UWLDropTableAsset;
class UWLLootSettings;
struct FWLDropRoll;

/** What the loot code needs to know about the enemy that died (Unity CombatTarget fields). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLEnemyLootParams
{
	GENERATED_BODY()

	/** The dead enemy (world context; may be null if Killer is set). */
	UPROPERTY(BlueprintReadWrite, Category = "Loot")
	TObjectPtr<AActor> Enemy = nullptr;

	/** World tier 1-5. 0 = infer from MaxHP (CombatTarget.LootTier: 40/90/160/280/450 bands). */
	UPROPERTY(BlueprintReadWrite, Category = "Loot")
	int32 Tier = 0;

	UPROPERTY(BlueprintReadWrite, Category = "Loot")
	int32 MaxHP = 10;

	UPROPERTY(BlueprintReadWrite, Category = "Loot")
	bool bDummy = false;

	UPROPERTY(BlueprintReadWrite, Category = "Loot")
	bool bMiniBoss = false;

	UPROPERTY(BlueprintReadWrite, Category = "Loot")
	bool bBoss = false;

	/** Uses the God-Hunter table. */
	UPROPERTY(BlueprintReadWrite, Category = "Loot")
	bool bGodHunter = false;

	/** Optional custom table; replaces the tier / God-Hunter tables. */
	UPROPERTY(BlueprintReadWrite, Category = "Loot")
	TObjectPtr<UWLDropTableAsset> DropTable = nullptr;

	/** Killer's Salvage module power. -1 = read it from the killer's equipment. */
	UPROPERTY(BlueprintReadWrite, Category = "Loot")
	int32 SalvagePower = -1;

	/** CombatTarget.LootTier. */
	int32 GetLootTier() const;
};

/** Pure, testable loot rolls (EnemyLoot.cs). All randomness comes from the passed stream. */
struct WASTELANDERS_API FWLLootRules
{
	/** EnemyLoot.OneIn: true with probability 1/N (never for N <= 0). */
	static bool OneIn(int32 N, FRandomStream& Rng) { return N > 0 && Rng.RandRange(0, N - 1) == 0; }

	/** One module roll: rarest quality first so a lucky roll isn't masked. 0 = none. */
	static int32 RollModule(const UWLLootSettings& S, int32 Tier, FRandomStream& Rng);

	/** A gear piece for the tier, melee or ranged evenly. 0 = none. */
	static int32 RollGear(const UWLLootSettings& S, int32 Tier, FRandomStream& Rng);

	/** Standard table: module roll, then ore / ammo / gear, each independent. */
	static void RollTierLoot(const UWLLootSettings& S, int32 Tier, bool bMiniOrBoss, int32 SalvagePower, FRandomStream& Rng, TArray<FWLItemQuantity>& Out);

	/** God-Hunter: guaranteed ore + ammo, 1-in-3 Refined module, 1-in-50 God gear. Returns the God gear id or 0. */
	static int32 RollGodHunter(const UWLLootSettings& S, FRandomStream& Rng, TArray<FWLItemQuantity>& Out);

	/** EnemyLoot.CreateDefaultTable: the standard table as editable rolls (same odds). */
	static TArray<FWLDropRoll> BuildDefaultTable(const UWLLootSettings& S, int32 Tier, bool bMiniOrBoss, bool bGodHunter);

	/** WeaponSkins.RollDrop chance: table override if >= 0, else boss / miniboss / normal. */
	static float CosmeticChance(const UWLLootSettings& S, bool bBoss, bool bMiniBoss, float TableOverride);
};

/**
 * Server entry point for enemy death drops. Combat calls DropEnemyLoot when an enemy dies.
 */
UCLASS()
class WASTELANDERS_API UWLLootLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/**
	 * SERVER. EnemyLoot.Drop + WeaponSkins.RollDrop. Dummies drop nothing. Every drop spawns an
	 * AWLGroundItem owned by the killer (private for 60 s, then public).
	 */
	static void DropEnemyLoot(const FWLEnemyLootParams& Params, const FVector& Location, AWLPlayerState* Killer);

	UFUNCTION(BlueprintCallable, BlueprintAuthorityOnly, Category = "Wastelanders|Loot", meta = (DisplayName = "Drop Enemy Loot"))
	static void K2_DropEnemyLoot(const FWLEnemyLootParams& Params, FVector Location, AWLPlayerState* Killer) { DropEnemyLoot(Params, Location, Killer); }
};
