#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "WLFlagPassage.generated.h"

class UBoxComponent;
class UStaticMeshComponent;
class UStaticMesh;
class AWLEnemyCharacter;
class AWLEnemySpawner;
class AWLPlayerState;
class APawn;

/** One moving part of a passage (door leaf, shutter, drawbridge), found by component name. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLPassageMover
{
	GENERATED_BODY()

	/** Name of a scene component on this actor (add meshes in a Blueprint child). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Passage")
	FName ComponentName;

	/** Offset from the CLOSED pose to the open pose, in the component's parent space. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Passage")
	FVector OpenLocationOffset = FVector::ZeroVector;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Passage")
	FRotator OpenRotationOffset = FRotator::ZeroRotator;
};

/**
 * Shared machinery for things that are open or shut PER PLAYER, driven by that player's flags:
 * tier gates, boss shortcuts, Broken Crescent shutters, fog walls.
 *
 * Unity was single player, so these simply moved a door. In a shared world the door must be open
 * for the player who earned it and shut for everyone else, at the same time:
 *  - Collision: an invisible Blocker box blocks pawns. A player who may pass has the blocker added to
 *    their capsule's ignore-when-moving list, on the server and on their own client, so prediction
 *    and the server agree. Other players are still blocked.
 *  - Visuals: each client animates the movers / hides the barrier from its OWN player's flags (flags
 *    replicate owner-only). Already open at load = snap, not animate (Unity OpenInstant / SnapOpen).
 *  - Boss credit: on the server, an UWLGameEventsSubsystem::OnEnemyKilled for the configured boss (a
 *    placed enemy, or anything a referenced spawner produced, so respawned bosses count) credits the
 *    killer and every player within BossCreditRadius. Unity credited "the player"; this is its MMO form.
 */
UCLASS(Abstract)
class WASTELANDERS_API AWLFlagPassage : public AActor
{
	GENERATED_BODY()

public:
	AWLFlagPassage();

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;
	virtual void OnConstruction(const FTransform& Transform) override;

	/** Whether this passage is open for that player. Runs on server and on the player's own client. */
	virtual bool IsOpenFor(const AWLPlayerState* PS) const PURE_VIRTUAL(AWLFlagPassage::IsOpenFor, return false;);

	// --- boss link ---

	/** A placed boss whose death counts. */
	UPROPERTY(EditInstanceOnly, BlueprintReadOnly, Category = "Wastelanders|Passage|Boss")
	TSoftObjectPtr<AWLEnemyCharacter> RequiredBoss;

	/** Or: any enemy this spawner produced counts (respawning bosses). */
	UPROPERTY(EditInstanceOnly, BlueprintReadOnly, Category = "Wastelanders|Passage|Boss")
	TSoftObjectPtr<AWLEnemySpawner> RequiredBossSpawner;

	/** Players this close to the boss when it dies share the credit (plus the killer). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Passage|Boss")
	float BossCreditRadius = 3000.f;

	bool HasBoss() const { return !RequiredBoss.IsNull() || !RequiredBossSpawner.IsNull(); }

	// --- visuals ---

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Passage")
	TArray<FWLPassageMover> Movers;

	/** Seconds for movers to reach the open pose (TierGate slideDuration 3.5, BossShortcut dropDuration 2). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Passage")
	float OpenDuration = 3.5f;

	/** If true, the movers' placed pose is the OPEN pose (Unity BossShortcut) and they start offset closed. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Passage")
	bool bPlacedPoseIsOpen = false;

	/** Optional art for the closed barrier (fog / glow wall). Unset = engine cube placeholder. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Passage")
	TSoftObjectPtr<UStaticMesh> BarrierArtMesh;

	/** Show the placeholder barrier in game when no art is assigned (greybox readability). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Passage")
	bool bShowPlaceholderBarrier = true;

protected:
	/** SERVER. A player entered the trigger. */
	virtual void OnPlayerEntered(AWLPlayerState* PS, APawn* Pawn) {}

	/** SERVER. The configured boss died with this player credited. */
	virtual void OnBossDefeatedBy(AWLPlayerState* PS) {}

	/** SERVER. Re-evaluate one player's pass-through (also used right after opening). */
	void RefreshPawnCollision(APawn* Pawn);

	/** Chat line to one player. */
	static void Message(AWLPlayerState* PS, const FText& Text);

	UFUNCTION()
	void OnTriggerBegin(UPrimitiveComponent* OverlappedComp, AActor* OtherActor, UPrimitiveComponent* OtherComp, int32 OtherBodyIndex, bool bFromSweep, const FHitResult& SweepResult);

	void HandleEnemyKilled(AActor* Enemy, AWLPlayerState* Killer);
	bool IsRequiredBoss(const AActor* Enemy) const;

	void ServerPoll();
	void ClientPoll();
	void ApplyVisual(float Alpha);

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<USceneComponent> Root;

	/** Invisible pawn blocker. Disable collision on it for passages that never block (fog walls). */
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UBoxComponent> Blocker;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UBoxComponent> Trigger;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UStaticMeshComponent> Barrier;

	struct FMoverState
	{
		TWeakObjectPtr<USceneComponent> Component;
		FVector ClosedLocation;
		FRotator ClosedRotation;
		FVector OpenLocation;
		FRotator OpenRotation;
	};
	TArray<FMoverState> MoverStates;

	FTimerHandle PollTimer;
	FDelegateHandle EnemyKilledHandle;

	// Client visual state.
	bool bClientEvaluated = false;
	bool bClientOpen = false;
	float ClientAlpha = 0.f;
	FTimerHandle AnimTimer;
	void TickAnimation();
};
