#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerStart.h"
#include "WLPlayerStart.generated.h"

/**
 * The authored world spawn, with a tier and area tag. Ported from World/WorldSpawnPoint.cs.
 *
 * Where a brand-new character starts and where the lost (out of bounds, /stuck) and the dead are
 * returned to. Deliberately separate from the SAVED position: AWLGameMode still restores the saved
 * spot first, and a spawn point is only the fallback, so a corrupt save can never become the spawn
 * (the self-sustaining "respawn into the void" loop Unity fixed).
 *
 * Unity's hard-coded fallback position (1.6, 157, -166.7) is not ported: a map without any player
 * start falls back to the engine default spawn. Unity's ground snapping is the engine's collision
 * adjustment when spawning at a PlayerStart.
 */
UCLASS()
class WASTELANDERS_API AWLPlayerStart : public APlayerStart
{
	GENERATED_BODY()

public:
	AWLPlayerStart(const FObjectInitializer& ObjectInitializer);

	/** World tier (1-5) this start belongs to; respawns can prefer the tier a player is in. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Spawn", meta = (ClampMin = "1", ClampMax = "5"))
	int32 Tier = 1;

	/** Free-form area id ("Greenbelt", "BrokenCrescent"). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Spawn")
	FName AreaTag;

	/** The start new characters and generic rescues use. One per map. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Spawn")
	bool bDefaultSpawn = false;

	/** Face the marker's direction on spawn (Unity useMarkerRotation). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Spawn")
	bool bUseMarkerRotation = true;

	/**
	 * Pick a start: the closest start of PreferredTier to NearLocation if any, else the default
	 * spawn, else any AWLPlayerStart. Returns null when the map has none (caller falls back to the
	 * engine's choice). For AWLGameMode::ChoosePlayerStart / TeleportToSpawn.
	 */
	static AWLPlayerStart* Choose(const UWorld* World, int32 PreferredTier = 0, const FVector* NearLocation = nullptr);
};
