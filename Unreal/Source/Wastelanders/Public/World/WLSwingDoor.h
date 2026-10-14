#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "WLSwingDoor.generated.h"

class UStaticMeshComponent;
class UStaticMesh;

/**
 * A door or gate leaf that swings open as a player walks up and shuts once everyone has gone.
 * Ported from World/SwingDoor.cs.
 *
 * Server decides (any player pawn within OpenDistance opens it; it closes after every pawn has been
 * beyond CloseDistance for CloseDelay) and replicates bOpen plus the swing direction, chosen so the
 * free end moves away from the player who opened it (a door never opens into your face). Clients
 * animate locally. The leaf's collision is off whenever it isn't fully shut, so a moving door can
 * never shove anyone, and the leaf never affects the navmesh, so routes go through doorways.
 *
 * The hinge comes from the mesh: whichever end of the leaf sits near its pivot, else one end.
 */
UCLASS()
class WASTELANDERS_API AWLSwingDoor : public AActor
{
	GENERATED_BODY()

public:
	AWLSwingDoor();

	virtual void BeginPlay() override;
	virtual void Tick(float DeltaSeconds) override;
	virtual void OnConstruction(const FTransform& Transform) override;
	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Door")
	float OpenDistance = 340.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Door")
	float CloseDistance = 700.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Door")
	float CloseDelay = 2.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Door")
	float SwingSeconds = 0.35f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Door")
	float OpenAngle = 95.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Visual")
	TSoftObjectPtr<UStaticMesh> ArtMesh;

	/**
	 * Pure hinge solver (unit tested): given the leaf's bounds in the door's local frame (pivot at the
	 * origin), the hinge point, the free-end point and the leaf normal, all local.
	 */
	static void SolveHinge(const FBox& LocalBounds, FVector& OutHinge, FVector& OutFree, FVector& OutNormal);

	bool IsOpen() const { return bOpen; }

protected:
	UFUNCTION()
	void OnRep_DoorState();

	void ServerCheck();
	void SetLeafSolid(bool bSolid);
	void ApplyAlpha(float Alpha);

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<USceneComponent> Root;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UStaticMeshComponent> Leaf;

	UPROPERTY(ReplicatedUsing = OnRep_DoorState)
	bool bOpen = false;

	/** +1 / -1: which way the leaf swings this time. */
	UPROPERTY(ReplicatedUsing = OnRep_DoorState)
	int8 SwingSign = 1;

	FTransform LeafClosed;
	FVector Hinge = FVector::ZeroVector;
	FVector Free = FVector::ZeroVector;
	FVector Normal = FVector::ForwardVector;
	float Alpha = 0.f;
	double FarSince = -1.0;
	FTimerHandle CheckTimer;
	bool bReady = false;
};
