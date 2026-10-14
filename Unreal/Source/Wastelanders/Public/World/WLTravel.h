#pragma once

#include "CoreMinimal.h"

class UWorld;
class AActor;
class AWLPlayerCharacter;
class APlayerController;

/**
 * Moving players around the world and between shards (Unity SceneWarp.cs + TierWaypointPad.Teleport).
 *
 *  - TeleportPlayer: SERVER. Moves the pawn (TeleportTo, so the CMC corrects the client), stops any
 *    skilling action, and tells the owning client to drop its click-to-move path so it doesn't walk
 *    straight back (Unity: ClearDestination after the warp).
 *  - SeatOnGround: drop a point onto the first walkable surface below it (Unity SeatPlayerOnGround,
 *    which kept warps from landing in the void). Pure query.
 *  - TravelToDestination: SERVER. A named destination from UWLWorldSettings::TravelDestinations.
 *    Unity loaded another scene locally; in an MMO a different map is a different server, so the
 *    player is saved and their client travels to that shard's address with ?Name=<character>.
 *
 * Unity's F10/F11/F12 debug keys (ranged kit, level-5 skills, world toggle) are not ported here:
 * they are cheats and belong behind the server's /dev chat commands (FWLChatCommands).
 */
struct WASTELANDERS_API FWLTravel
{
	static bool TeleportPlayer(AWLPlayerCharacter* Character, const FVector& Location, const FRotator* Rotation = nullptr);

	/** True and InOutLocation moved onto ground (+HalfHeight) when a walkable surface (normal Z >= 0.2) exists below. */
	static bool SeatOnGround(const UWorld* World, FVector& InOutLocation, float HalfHeight = 90.f, const AActor* Ignore = nullptr, float StartAbove = 200.f);

	static bool TravelToDestination(APlayerController* Controller, FName Destination);
};
