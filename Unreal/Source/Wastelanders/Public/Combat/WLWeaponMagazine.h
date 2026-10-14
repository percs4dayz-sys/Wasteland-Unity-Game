#pragma once

#include "CoreMinimal.h"

/**
 * Per-weapon magazine counts (Assets/Scripts/Player3D/WeaponMagazine.cs). Rounds still live in the
 * Ammo slot and are spent per shot; the magazine is a TIMING gate on top. State is remembered per
 * weapon id, so swapping guns is never a free reload; a never-fired weapon starts full.
 * Pure value type: UWLCombatComponent owns one and handles the reload timing.
 */
struct FWLWeaponMagazine
{
	int32 GetLoaded(int32 WeaponId, int32 MagSize) const
	{
		const int32* N = Loaded.Find(WeaponId);
		return N ? *N : MagSize;
	}

	bool IsFull(int32 WeaponId, int32 MagSize) const { return GetLoaded(WeaponId, MagSize) >= MagSize; }

	/** Every trigger pull spends a round. Returns true when the magazine is now empty (auto-reload). */
	bool NoteShotFired(int32 WeaponId, int32 MagSize)
	{
		const int32 Left = FMath::Max(0, GetLoaded(WeaponId, MagSize) - 1);
		Loaded.Add(WeaponId, Left);
		return Left == 0;
	}

	void Refill(int32 WeaponId, int32 MagSize) { Loaded.Add(WeaponId, MagSize); }

private:
	TMap<int32, int32> Loaded;
};
