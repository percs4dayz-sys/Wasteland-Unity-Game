#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "WLWaterVolume.generated.h"

class UBoxComponent;
class UNavModifierComponent;
class UStaticMeshComponent;
class UStaticMesh;

/**
 * Unwalkable water (replaces World/WaterSurface.cs and the water half of LivingWorld/PlayerMover).
 *
 * Unity's water was a visual mesh with no collider; the player mover asked WaterSurface.Blocks()
 * every step, triangle-sampling the mesh height against the terrain and walkable bridge tops. In UE
 * the same rule is expressed with engine systems, which also makes it server-authoritative for free:
 *
 *  - Blocker: a box that blocks the Pawn channel only. The server's character movement stops
 *    players (and enemies) at the shoreline exactly as the predicting client does, so no custom
 *    per-step test is needed. It ignores Visibility, so clicks pass through to the river bed or the
 *    fishing spot.
 *  - NavModifier with NavArea_Null: the navmesh has a hole where the water is, so click-to-move
 *    paths go around rivers and over bridges, never through. Bridges and platforms above the water
 *    keep their own navmesh, which is what Unity's "safe only if you can step onto its top" test
 *    approximated.
 *
 * Sloping rivers: rotate or chain several volumes along the river (Unity sampled the actual
 * triangles; boxes are coarser but cost nothing at runtime). The surface mesh is optional art; the
 * placeholder is a flat tinted plane at the top of the box, hidden from collision.
 *
 * Unity's HeightAt() is not ported: its callers (critter spawn, player mover) use SeaLevelZ and the
 * blocker instead.
 */
UCLASS()
class WASTELANDERS_API AWLWaterVolume : public AActor
{
	GENERATED_BODY()

public:
	AWLWaterVolume();

	virtual void OnConstruction(const FTransform& Transform) override;

	/** Water footprint and depth (half extents, cm), centred on the actor. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Water")
	FVector Extent = FVector(1000.f, 1000.f, 100.f);

	/** Extra height above the surface that still blocks, so a capsule can't step up onto the water. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Water")
	float BlockAboveSurface = 60.f;

	/** New-art surface mesh. Unset = tinted engine plane. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Water")
	TSoftObjectPtr<UStaticMesh> SurfaceArtMesh;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Water")
	bool bShowPlaceholderSurface = true;

	/** World Z of the water surface. */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Water")
	float GetSurfaceZ() const;

	/** True if the planar point lies over this water (ignores Z). */
	bool IsOverWater(const FVector& WorldPoint) const;

protected:
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UBoxComponent> Blocker;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UNavModifierComponent> NavModifier;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UStaticMeshComponent> Surface;
};
