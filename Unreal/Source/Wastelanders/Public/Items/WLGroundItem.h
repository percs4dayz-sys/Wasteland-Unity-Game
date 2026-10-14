#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Subsystems/WorldSubsystem.h"
#include "Interaction/WLInteractable.h"
#include "Items/WLItemTypes.h"
#include "WLGroundItem.generated.h"

class USphereComponent;
class UStaticMeshComponent;
class AWLPlayerState;
class AWLPlayerCharacter;

/**
 * A stack of loot lying in the world. Ported from Assets/Scripts/World/GroundItem.cs, made
 * MMO-safe with OSRS ownership rules:
 *
 *  - Spawned by the server, owned by a player (the killer or the dropper). For PrivateSeconds only
 *    the owner can see it (network relevancy) or take it; then it turns public for everyone.
 *  - Despawns LifeSeconds after landing.
 *  - Lands one game tick after spawning (Unity: drops fall in for a tick and only become grabbable
 *    on landing).
 *  - Pickup is always deliberate (Interact "Take"), except the Magnet module sweep, which collects
 *    resource-type drops silently (UWLEquipmentComponent).
 *
 * Art: uses the item's HeldMesh when one exists, otherwise a small engine cube. No Unity art.
 */
UCLASS()
class WASTELANDERS_API AWLGroundItem : public AActor, public IWLInteractable
{
	GENERATED_BODY()

public:
	AWLGroundItem();

	/** SERVER. Drop a stack at a location (scattered a little). Null if the item id is unknown or no authority. */
	static AWLGroundItem* Spawn(UWorld* World, const FWLItemStack& Stack, const FVector& Location, AWLPlayerState* Owner);

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;
	virtual void Tick(float DeltaSeconds) override;
	virtual bool IsNetRelevantFor(const AActor* RealViewer, const AActor* ViewTarget, const FVector& SrcLocation) const override;

	// IWLInteractable
	virtual FText GetInteractName_Implementation() const override;
	virtual FText GetExamineText_Implementation() const override;
	virtual TArray<FWLInteractOption> GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const override;
	virtual float GetInteractRange_Implementation() const override;
	virtual bool Interact_Implementation(AWLPlayerCharacter* Player, FName Verb) override;

	/** SERVER. GroundItem.TryPickUp. bSilentFull suppresses the "pack is full" line (Magnet sweeps). */
	bool TryPickUp(AWLPlayerState* Picker, bool bSilentFull = false);

	/** SERVER. Whether this player may see / take it right now (owner, or the item has gone public). */
	bool IsAvailableTo(const AWLPlayerState* Player) const;

	const FWLItemStack& GetStack() const { return Stack; }
	bool IsGrabbable() const { return bGrabbable; }

	/** Owner-only window (OSRS: 60 s). */
	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Loot")
	float PrivateSeconds = 60.f;

	/** Despawn after landing (180 s; Unity was 120 s single-player). */
	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Loot")
	float LifeSeconds = 180.f;

	/** Random horizontal scatter so several drops don't stack on one point (Unity +-0.4 m). */
	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Loot")
	float ScatterRadius = 40.f;

	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Loot")
	float PickupRange = 200.f;

	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Visual")
	float PlaceholderScale = 0.2f;

protected:
	UFUNCTION()
	void OnRep_Stack();

	void HandleGameTick(int64 TickCount);
	void Land();
	void GoPublic();
	void Despawn();
	void RefreshVisual();

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<USphereComponent> Collision;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UStaticMeshComponent> Mesh;

	UPROPERTY(ReplicatedUsing = OnRep_Stack)
	FWLItemStack Stack;

	UPROPERTY(Replicated)
	bool bGrabbable = false;

	/** Server only: who may take it during the private window. Null = public. */
	TWeakObjectPtr<AWLPlayerState> OwnerPlayer;
	bool bPublic = false;
	int64 LandTick = TNumericLimits<int64>::Max();
	FDelegateHandle TickHandle;
	FTimerHandle PublicTimer;
	FTimerHandle DespawnTimer;
	FTimerHandle LandFallbackTimer;

	// Cosmetic (clients): fall-in, bob and spin.
	float VisualTime = 0.f;
	float VisualDrop = 55.f;
};

/** Registry of live ground items (Unity GroundItem.All) for radius queries (Magnet sweep, minimap). */
UCLASS()
class WASTELANDERS_API UWLGroundItemSubsystem : public UWorldSubsystem
{
	GENERATED_BODY()

public:
	static UWLGroundItemSubsystem* Get(const UObject* WorldContextObject);

	void Register(AWLGroundItem* Item) { Items.AddUnique(Item); }
	void Unregister(AWLGroundItem* Item) { Items.Remove(Item); }

	/** Items within Radius (uu) of Center. Copy: callers may destroy items while iterating. */
	TArray<AWLGroundItem*> FindInRadius(const FVector& Center, float Radius) const;

	const TArray<TWeakObjectPtr<AWLGroundItem>>& GetAll() const { return Items; }

private:
	TArray<TWeakObjectPtr<AWLGroundItem>> Items;
};
