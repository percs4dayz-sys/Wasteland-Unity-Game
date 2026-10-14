#pragma once

#include "CoreMinimal.h"

class UWorld;

/**
 * Blackwater Reach geography (World/BlackwaterAtlas.cs), shared by the in-game atlas (F8) and the
 * "you are entering <place>" header (World/BlackwaterGuide.cs). Coordinates are Unity metres on the
 * 1024 m map (x, z); multiply by UWLGameSettings::UnitsPerTile for world units.
 *
 * Not ported: BlackwaterNavigator.cs (click-to-move over the navmesh is AWLPlayerController::
 * MoveToLocation) and BlackwaterPresentation.cs (a per-scene URP pipeline swap; rendering settings
 * belong to the new art direction's level setup, not code).
 */
struct WASTELANDERS_API FWLBlackwaterPlace
{
	FText Name;
	FText Activity;
	FVector2D Position = FVector2D::ZeroVector;
	float Height = 0.f;
	float Radius = 0.f;
};

struct WASTELANDERS_API FWLBlackwaterAtlas
{
	/** The Blackwater persistent level's name (Unity SceneName). */
	static const TCHAR* MapName;
	static constexpr float Size = 1024.f;
	static constexpr float SeaLevel = 50.3f;
	/** Beyond this distance (m) from every place you're "on the old coast road". */
	static constexpr float RegionRadius = 110.f;

	static const TArray<FWLBlackwaterPlace>& Places();
	static const TArray<TArray<FVector2D>>& Roads();

	/** Index of the nearest place within RegionRadius of a map position (metres), or INDEX_NONE. */
	static int32 NearestPlace(const FVector2D& PositionMetres);

	/** Header lines for the HUD (BlackwaterGuide.OnGUI). */
	static FText RegionTitle(int32 PlaceIndex);
	static FText RegionSubtitle(int32 PlaceIndex);

	/** Is this world the Blackwater map? (PIE prefixes stripped.) */
	static bool IsBlackwaterWorld(const UWorld* World);

	/** BlackwaterArrival: the gathering tools handed over on first arrival. */
	static const TArray<int32>& ArrivalItems();
};
