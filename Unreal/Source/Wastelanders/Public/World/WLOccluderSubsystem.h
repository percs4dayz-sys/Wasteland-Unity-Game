#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "WLOccluderSubsystem.generated.h"

class UPrimitiveComponent;
class UStaticMeshComponent;

/**
 * Reference-counted "clicks pass through this" for client-side hiding. RoofFader and the overhead
 * occluder can both hide the same mesh; each Push makes the component ignore ECC_Visibility (the
 * click/hover channel), and the last Pop restores the original response. Unity disabled the
 * colliders outright; here only the click channel changes, because pawn collision must stay
 * identical to the server's or client movement prediction would diverge.
 */
struct WASTELANDERS_API FWLClickThrough
{
	static void Push(UPrimitiveComponent* Component);
	static void Pop(UPrimitiveComponent* Component);
};

/**
 * Hides anything ENTIRELY above the local player's head, near them. Ported from
 * Player3D/OccluderHider.cs. Walk into a crypt, a cave or under a bridge and the ceiling over you is
 * gone; walk out and it comes back. Nothing to place or tag.
 *
 * The rule (unchanged from Unity): a static mesh hides when its bounds' bottom is above feet +
 * Headroom AND its footprint is within OverheadRadius horizontally. "Bottom above head" is what keeps
 * walls, pillars and doorframes visible, since they start at the floor.
 *
 * Detection uses render bounds, not physics, because kit props often have no collision. Hiding uses
 * SetHiddenInGame (a separate flag from SetVisibility, which RoofFader uses) so the two never
 * fight. Skipped: landscape (not a static mesh), the local pawn and anything attached to it, and any
 * actor or component tagged "KeepVisibleOverhead" (Unity's KeepVisibleOverhead component).
 *
 * The cache is rebuilt every OccluderCacheInterval seconds, within OccluderCacheRadius of the player,
 * instead of Unity's whole-scene scan, which would be too costly in a World Partition map.
 *
 * Client only: not created on dedicated servers. Tunables are in UWLWorldSettings.
 */
UCLASS()
class WASTELANDERS_API UWLOccluderSubsystem : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
	virtual void Deinitialize() override;
	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override;

	static const FName KeepVisibleTag;

	/** Pure (unit tested): does Bounds count as overhead for feet at Feet? */
	static bool IsOverhead(const FBox& Bounds, const FVector& Feet, float Headroom, float Radius);

private:
	void RebuildCache(const APawn* Player);
	void RestoreAll();

	TArray<TWeakObjectPtr<UStaticMeshComponent>> Cache;
	TSet<TWeakObjectPtr<UStaticMeshComponent>> Hidden;
	float ScanTimer = 0.f;
	float CacheTimer = 0.f;
};
