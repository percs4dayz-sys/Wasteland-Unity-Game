#pragma once

#include "CoreMinimal.h"
#include "World/WLInteractableActor.h"
#include "Core/WLGameTickSubsystem.h"
#include "Core/WLTypes.h"
#include "WLResourceNode.generated.h"

class AWLPlayerState;
class AWLResourceNode;

/** Stored as an index on placed nodes: APPEND ONLY (Unity ResourceNodeType). */
UENUM(BlueprintType)
enum class EWLResourceNodeType : uint8
{
	RubblePile,
	WoodenDebris,
	WaterBarrel,
	RatNest,
	SulphurVent
};

/** Outcome of one gather cycle (Unity GatherCycleResult). */
enum class EWLGatherResult : uint8
{
	Stop,
	NoCatch,
	Caught
};

DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnNodeStateChanged, AWLResourceNode*);

/**
 * A gatherable node: scrap pile, deadwood, fishing spot, sulphur vent. Ported from
 * Assets/Scripts/World/ResourceNode.cs.
 *
 * Server: rolls a random yield (MinYield..MaxYield, inclusive) on spawn and on every respawn,
 * resolves gather cycles for UWLSkillingComponent (tool, level, inventory, OSRS-style 0-255 rolls,
 * better-tool overrides, Gather-module bonus), and counts respawn ticks on the game tick.
 * Clients: see bDepleted (replicated) and hide the node / show the depleted visual (a stump).
 *
 * Gathering vocabulary per skill: "Chop down", "Mine", "Fish at", else "Gather" (verb id "Gather").
 * The item and XP numbers are per-placement data, as in Unity; DT_Items supplies names.
 */
UCLASS()
class WASTELANDERS_API AWLResourceNode : public AWLInteractableActor, public IWLTickable
{
	GENERATED_BODY()

public:
	AWLResourceNode();

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;
	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

	// IWLTickable (server)
	virtual void OnGameTick(int64 TickCount) override;

	// IWLInteractable
	virtual FText GetInteractName_Implementation() const override;
	virtual FText GetExamineText_Implementation() const override;
	virtual TArray<FWLInteractOption> GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const override;
	virtual bool Interact_Implementation(AWLPlayerCharacter* Player, FName Verb) override;

	static const FName GatherVerb;

	// --- queries (also used by QuestCam / starter guide) ---
	EWLSkill GetSkill() const { return Skill; }
	int32 GetLevelRequired() const { return LevelRequired; }
	int32 GetRequiredToolId() const { return RequiredToolId; }
	int32 GetDropItemId() const { return DropItemId; }
	EWLResourceNodeType GetNodeType() const { return NodeType; }
	bool IsSulphurVent() const { return NodeType == EWLResourceNodeType::SulphurVent; }
	bool IsDepleted() const { return bDepleted; }
	int32 GetHitsRemaining() const { return HitsRemaining; }

	/** True when an explicit stand point is set (fishing spot bank); gathering then uses it as the reach anchor. */
	bool HasInteractionPoint() const { return bUseInteractionPoint; }
	FVector GetInteractionLocation() const;

	/** Point to face while working: the middle of the visible mesh (fishing spots face the spot itself). */
	FVector GetFacingLocation() const;

	/** SERVER. Ticks between roll cycles for this player (better tool aware; <= 0 falls back to 4). */
	int32 CurrentTicksPerCycle(const AWLPlayerState* PS) const;

	/** SERVER. One roll cycle. OutMessage is set for Stop and Caught (NoCatch stays silent: no chat spam). */
	EWLGatherResult TryGatherCycle(AWLPlayerState* PS, FText& OutMessage);

	/** SERVER. Roll a fresh yield (Unity ResetYield). */
	void ResetYield();

	/** Fire on server (state change) and clients (OnRep). */
	FWLOnNodeStateChanged OnDepleted;
	FWLOnNodeStateChanged OnRespawned;

	// --- node config (Unity inspector fields, same defaults) ---
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node")
	EWLResourceNodeType NodeType = EWLResourceNodeType::RubblePile;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node")
	EWLSkill Skill = EWLSkill::Scrapping;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node", meta = (ClampMin = "1"))
	int32 LevelRequired = 1;

	/** Item id of the tool needed (3 pickaxe, 4 hatchet, 5 rod; upgrades also qualify). 0 = none. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node")
	int32 RequiredToolId = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node")
	int32 DropItemId = 0;

	/** Items per successful gather: 1 for non-stackables (one item = one slot), more for bulk sulphur. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node", meta = (ClampMin = "1"))
	int32 DropQuantity = 1;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node")
	int32 XPPerAction = 25;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node")
	int32 RespawnTicks = 10;

	/** Each node (and each respawn) yields a fresh random amount in this range. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|Yield")
	int32 MinYield = 1;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|Yield")
	int32 MaxYield = 8;

	/** Optional per-tier flavour; overrides the node-type defaults when set. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node")
	FText DisplayNameOverride;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node", meta = (MultiLine = true))
	FText ExamineOverride;

	/** Use InteractionPoint as the place to stand (e.g. the bank beside a fishing ripple). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node")
	bool bUseInteractionPoint = false;

	/** Ticks between roll cycles (0.6 s each). Woodcutting 4, Fishing 5, basic-tool mining 8. 0 -> 4. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|Rolls")
	int32 TicksPerCycle = 4;

	/** Rolls per cycle; the first success yields. 0 -> 1. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|Rolls")
	int32 AttemptsPerCycle = 1;

	/** Flat per-attempt chance out of 256, used when ChanceAtLevel99 is 0. 0 -> 256 (guaranteed). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|Rolls", meta = (ClampMin = "0", ClampMax = "256"))
	int32 SuccessChance = 256;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|Rolls", meta = (ClampMin = "0", ClampMax = "256"))
	int32 ChanceAtLevel1 = 0;

	/** > 0 enables level scaling (linear 1 -> 99). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|Rolls", meta = (ClampMin = "0", ClampMax = "256"))
	int32 ChanceAtLevel99 = 0;

	/** Upgraded tool (carried or equipped) that overrides the values below; fields <= 0 keep the base. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|BetterTool")
	int32 BetterToolId = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|BetterTool")
	int32 BetterToolTicksPerCycle = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|BetterTool")
	int32 BetterToolAttemptsPerCycle = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Node|BetterTool", meta = (ClampMin = "0", ClampMax = "256"))
	int32 BetterToolSuccessChance = 0;

	/** Shown while depleted (a stump). Unset = the node simply vanishes until it respawns. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Visual")
	TSoftObjectPtr<UStaticMesh> DepletedArtMesh;

protected:
	UFUNCTION()
	void OnRep_Depleted();

	/** Apply bDepleted to visibility/collision (server and clients). */
	void ApplyDepletedState();

	/** SERVER. Called when the last hit lands; subclasses (fishing spots) may relocate instead. */
	virtual void Deplete();

	/** SERVER. Respawn: new yield, visible again. */
	virtual void Respawn();

	bool HasBetterTool(const AWLPlayerState* PS) const;
	int32 CurrentAttempts(const AWLPlayerState* PS) const;
	int32 CurrentChance(const AWLPlayerState* PS) const;

	FText GetDefaultDisplayName() const;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<USceneComponent> InteractionPoint;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UStaticMeshComponent> DepletedMesh;

	UPROPERTY(ReplicatedUsing = OnRep_Depleted, BlueprintReadOnly, Category = "Wastelanders|Node")
	bool bDepleted = false;

	int32 HitsRemaining = 0;
	int32 RespawnCountdown = 0;

	/** The mesh's authored collision, restored on respawn (fishing spots are query-only). */
	TEnumAsByte<ECollisionEnabled::Type> ActiveCollision = ECollisionEnabled::QueryAndPhysics;
};
