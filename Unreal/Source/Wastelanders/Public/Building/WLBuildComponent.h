#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Building/WLBuildCatalog.h"
#include "WLBuildComponent.generated.h"

class UStaticMeshComponent;
class AWLPlayerState;
class AWLPlayerCharacter;

DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnPlacementChanged, int32 /*BuildableIndex, -1 = not placing*/);

/**
 * Hearthcraft construction, ported from World/BuildManager.cs (menu + ghost placement + spawn).
 * Lives on the player pawn (added by UWLInteractorComponent::BeginPlay via EnsureOn).
 *
 * Split of Unity's BuildManager:
 *  - CLIENT: BeginPlacement(Index) shows a tinted ghost; the HUD/input layer drives UpdateGhost
 *    (cursor ground point), RotateGhost (R = +45 deg) and ConfirmPlacement (left click) and
 *    CancelPlacement (Esc / H). The H build menu itself is UI: it lists UWLBuildCatalog::Get().
 *    Ghost colour is green/red from a local affordability check, which is cosmetic only.
 *  - SERVER: ServerPlace re-validates everything (index, Hearthcraft level, materials, reach,
 *    ground, per-player cap, rate limit), then consumes materials, grants XP and spawns the piece.
 *
 * MMO changes: pieces remember their builder (UWLPlacedPieceComponent), are capped per player, and are
 * removed when the builder's pawn leaves, because placed pieces are not persisted yet (Unity said the
 * same: session-only; persistence is a future pass).
 */
UCLASS(ClassGroup = "Wastelanders", meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLBuildComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLBuildComponent();

	/** SERVER. Finds or adds (replicated) the component on a pawn. */
	static UWLBuildComponent* EnsureOn(AActor* Owner);

	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	// --- client placement flow ---------------------------------------------------------------------

	/** CLIENT. Enter placement for catalog entry Index. False if the index or level is wrong (a chat line says why). */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Build")
	bool BeginPlacement(int32 BuildableIndex);

	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Build")
	void CancelPlacement();

	/** CLIENT. Move the ghost to a ground point (call each frame from the cursor/aim trace). */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Build")
	void UpdateGhost(const FVector& GroundPoint);

	/** CLIENT. Unity R key: +45 degrees. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Build")
	void RotateGhost(float DeltaYaw = 45.f);

	/** CLIENT. Left click: ask the server to build at the ghost. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Build")
	void ConfirmPlacement();

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Build")
	bool IsPlacing() const { return PlacingIndex != INDEX_NONE; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Build")
	int32 GetPlacingIndex() const { return PlacingIndex; }

	/** Local checks used for ghost colour and menu row colours (Unity HasLevel / CanAfford). */
	bool HasLevel(const FWLBuildable& B) const;
	bool CanAfford(const FWLBuildable& B) const;
	FText WhyCantBuild(const FWLBuildable& B) const;
	/** "3 Wood, 5 Scrap Metal" or "free" (Unity CostText). */
	static FText CostText(const UObject* WorldContext, const FWLBuildable& B);

	/** Fires on the owning client when placement starts/stops. */
	FWLOnPlacementChanged OnPlacementChanged;

	// --- server -------------------------------------------------------------------------------------

	UFUNCTION(Server, Reliable, WithValidation)
	void ServerPlace(int32 BuildableIndex, FVector_NetQuantize Location, float Yaw);

	/** Server -> owning client: leave placement (out of materials). */
	UFUNCTION(Client, Reliable)
	void ClientEndPlacement();

	/** Max simultaneously placed pieces per player (MMO addition, stops world spam). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Build", meta = (ClampMin = "1"))
	int32 MaxPiecesPerPlayer = 40;

	/** Minimum seconds between placements. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Build")
	float PlaceCooldown = 0.25f;

protected:
	AWLPlayerCharacter* GetCharacter() const;
	AWLPlayerState* GetPlayerState() const;
	void Msg(const FText& Text) const;

	/** SERVER. Spawn and configure the deployable (Unity SpawnDeployable). Null if the class couldn't spawn. */
	AActor* SpawnDeployable(int32 BuildableIndex, const FWLBuildable& B, const FVector& Ground, float Yaw, AWLPlayerState* Builder);

	void SetGhostTint(bool bOk);
	void DestroyGhost();

	int32 PlacingIndex = INDEX_NONE;
	float GhostYaw = 0.f;
	FVector GhostGround = FVector::ZeroVector;
	bool bHaveGhostGround = false;

	UPROPERTY(Transient)
	TObjectPtr<UStaticMeshComponent> Ghost;

	// server
	double LastPlaceTime = -1000.0;
};
