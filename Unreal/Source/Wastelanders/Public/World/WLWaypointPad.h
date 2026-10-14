#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "WLWaypointPad.generated.h"

class UBoxComponent;
class UStaticMeshComponent;
class UStaticMesh;
class AWLPlayerState;

/**
 * One teleport pad of the tier shortcut network. Ported from World/TierWaypointPad.cs.
 *
 * Unlock chain (all persisted in player flags): a TierGate opening sets "tier_reached_<N>", which
 * unlocks the hub pad for tier N; "tier_reached_2" unlocks the pads that travel TO the hub. Walk onto
 * a pad: locked -> message; unlocked -> teleport (server) to the destination, keeping facing.
 *
 * The destination is stored as a transform relative to the pad (3D widget in the editor) rather than
 * an actor reference, so it works when the destination's World Partition cell isn't loaded.
 * Locked / unlocked visuals follow the LOCAL player's flags on each client.
 */
UCLASS()
class WASTELANDERS_API AWLWaypointPad : public AActor
{
	GENERATED_BODY()

public:
	AWLWaypointPad();

	virtual void BeginPlay() override;
	virtual void OnConstruction(const FTransform& Transform) override;

	bool IsUnlockedFor(const AWLPlayerState* PS) const;

	/** Where the player lands, relative to this pad. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Waypoint", meta = (MakeEditWidget = true))
	FVector DestinationOffset = FVector(0.f, 0.f, 100.f);

	/** Shown in chat on arrival, e.g. "Frozen Wastes". */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Waypoint")
	FText DestinationName;

	/** Flag required; blank = always open. Tier pads use "tier_reached_<N>"; hub pads "tier_reached_2". */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Waypoint")
	FString RequiredFlag;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Waypoint")
	FText LockedMessage;

	/** Per-player seconds before the same pad fires again. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Waypoint")
	float RetriggerCooldown = 2.5f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Visual")
	TSoftObjectPtr<UStaticMesh> UnlockedArtMesh;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Visual")
	TSoftObjectPtr<UStaticMesh> LockedArtMesh;

	FVector GetDestinationLocation() const { return GetActorTransform().TransformPosition(DestinationOffset); }

protected:
	UFUNCTION()
	void OnTriggerBegin(UPrimitiveComponent* OverlappedComp, AActor* OtherActor, UPrimitiveComponent* OtherComp, int32 OtherBodyIndex, bool bFromSweep, const FHitResult& SweepResult);

	void RefreshVisuals();

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UBoxComponent> Trigger;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UStaticMeshComponent> UnlockedVisual;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UStaticMeshComponent> LockedVisual;

	TMap<TWeakObjectPtr<AWLPlayerState>, double> NextUseTime;
	FTimerHandle VisualTimer;
};
