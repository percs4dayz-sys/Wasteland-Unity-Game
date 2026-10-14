#pragma once

#include "CoreMinimal.h"
#include "WLPlaceholderMesh.generated.h"

class UStaticMesh;
class UStaticMeshComponent;

/** Engine basic shapes used while no new-art-direction mesh is assigned (Unity's CreatePrimitive). */
UENUM(BlueprintType)
enum class EWLPlaceholderShape : uint8
{
	Cube,
	Cylinder,
	Sphere,
	Cone,
	Plane,
	None
};

/**
 * Asset-free stand-in visuals. Unity built tinted primitives at runtime (BuildManager.Paint,
 * EnemySpawner.Paint); this does the same with /Engine/BasicShapes and the engine's
 * BasicShapeMaterial "Color" parameter, so a level is readable before any art exists.
 */
struct WASTELANDERS_API FWLPlaceholderMesh
{
	/** The engine basic-shape mesh (1 m cube/cylinder/sphere/cone, or a 1 m plane). Null for None. Game thread. */
	static UStaticMesh* GetShape(EWLPlaceholderShape Shape);

	/**
	 * Give Component a mesh: the art mesh if Art is set (sync-loads it), else the basic shape scaled to
	 * SizeCm and tinted Color. Returns true if a placeholder was used.
	 */
	static bool Apply(UStaticMeshComponent* Component, const TSoftObjectPtr<UStaticMesh>& Art, EWLPlaceholderShape Shape,
		const FVector& SizeCm, const FLinearColor& Color);

	/** Tint a component that uses the engine BasicShapeMaterial (creates a dynamic instance once). */
	static void SetColor(UStaticMeshComponent* Component, const FLinearColor& Color);

	/**
	 * Uniform scale that makes a basic shape SizeCm big. Basic shapes are 100 uu across and centred on
	 * the origin, so callers offset by half the height to stand them on the ground.
	 */
	static FVector ScaleForSize(const FVector& SizeCm) { return SizeCm / 100.f; }
};
