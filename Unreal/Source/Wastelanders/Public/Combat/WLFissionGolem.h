#pragma once

#include "CoreMinimal.h"
#include "Combat/WLEnemyCharacter.h"
#include "WLFissionGolem.generated.h"

class AWLResourceNode;

/**
 * Elemental Golem (Assets/Scripts/World/FissionGolem.cs). While alive it is a normal enemy that the
 * geiger counter points toward. When killed, its corpse becomes a harvestable node: raw fission cores
 * are stripped from the body with the ordinary gathering loop (an AWLResourceNode spawned on the
 * corpse), and once stripped clean the corpse is removed so the geiger moves on.
 *
 *   geiger -> living golem -> fight & kill -> harvest corpse -> corpse depletes -> golem gone
 *
 * Respawning a fresh golem is the job of AWLFissionGolemSpawner.
 */
UCLASS()
class WASTELANDERS_API AWLFissionGolem : public AWLEnemyCharacter
{
	GENERATED_BODY()

public:
	AWLFissionGolem(const FObjectInitializer& ObjectInitializer);

	virtual bool IsFissionGolem() const override { return true; }

	/** Living golem or a still-harvestable corpse of this tier exists in the world. */
	static bool AnyActiveOfTier(const UWorld* World, int32 InTier);

	bool IsCorpse() const { return bIsCorpse; }

	/** World tier 1-5 (also the loot tier). */
	int32 GetGolemTier() const { return FMath::Clamp(Tier, 1, 5); }

	/** Corpse level gate: override when > 0, else by tier. */
	int32 EffectiveHarvestLevel() const;

	/** XP per core: override when > 0, else by tier. */
	int32 EffectiveXPPerCore() const;

	/** Stripping a corpse is mining: it pays Scrapping like an ordinary node of the same tier. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	EWLSkill HarvestSkill = EWLSkill::Scrapping;

	/** 0 = derive from the tier (HarvestLevelByTier). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem", meta = (ClampMin = "0"))
	int32 HarvestLevelRequired = 0;

	/** Raw cores per corpse, rolled per kill; averages 9 so walking between golems is not the bottleneck. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem", meta = (ClampMin = "1"))
	int32 MinYield = 3;

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem", meta = (ClampMin = "1"))
	int32 MaxYield = 15;

	/** 0 = derive from the tier (HarvestXPByTier). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem", meta = (ClampMin = "0"))
	int32 XPPerCore = 0;

	/** Matches the scrap nodes of the same tier (Junk Pile 1 ... Abandoned Robotics 80). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	TArray<int32> HarvestLevelByTier = { 1, 20, 40, 60, 80 };

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	TArray<int32> HarvestXPByTier = { 25, 50, 80, 120, 170 };

	/** FissionItems.RawCore(1): raw core ids run 405..409 for tiers 1-5. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	int32 RawCoreBaseItemId = 405;

	/** Pickaxe (upgrades also qualify, per the gathering tools rules). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	int32 HarvestToolItemId = 3;

	/** The node class spawned on the corpse. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	TSubclassOf<AWLResourceNode> CorpseNodeClass;

	/** Seconds the stripped corpse lingers before it is removed. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Golem")
	float StrippedCorpseSeconds = 1.5f;

protected:
	virtual void BeginCorpse() override;

	void HandleCorpseDepleted(AWLResourceNode* Node);

	UPROPERTY(Transient)
	TObjectPtr<AWLResourceNode> CorpseNode;

	bool bIsCorpse = false;
	/** Corpse stripped clean and about to vanish: no longer counts as this tier's golem. */
	bool bStripped = false;
};
