#pragma once

#include "CoreMinimal.h"
#include "Core/WLTypes.h"

class AWLPlayerState;
class UWLLootSettings;
struct FWLItemRow;
struct FWLWeaponSkinDef;

/**
 * Cosmetic weapon finishes: unlock and selection state only. Ported from
 * Assets/Scripts/Items/WeaponSkins.cs. Skin definitions live in UWLLootSettings::WeaponSkins.
 *
 * Persistence is the same as Unity: unlocks are player flags "skin_<id>", and the one selected look
 * per category is a flag "skinsel_melee_<id>" / "skinsel_ranged_<id>". The flags component owns
 * them; the equipment component replicates the resulting applied skin to everyone.
 *
 * Rendering (tint / skin mesh) is left to the new art pipeline: nothing here touches meshes.
 */
struct WASTELANDERS_API FWLWeaponSkins
{
	static const FWLWeaponSkinDef* Find(FName SkinId);

	static FString UnlockFlag(FName SkinId) { return FString::Printf(TEXT("skin_%s"), *SkinId.ToString()); }
	static FString SelectionPrefix(EWLWeaponStyle Category) { return Category == EWLWeaponStyle::Ranged ? TEXT("skinsel_ranged_") : TEXT("skinsel_melee_"); }
	static FString SelectionFlag(EWLWeaponStyle Category, FName SkinId) { return SelectionPrefix(Category) + SkinId.ToString(); }

	static bool IsUnlocked(const AWLPlayerState* PS, FName SkinId);

	/** The skin chosen for a category, or null (= weapon's default look). */
	static const FWLWeaponSkinDef* Selected(const AWLPlayerState* PS, EWLWeaponStyle Category);

	/** SERVER. One look per category: clears any previous selection. NAME_None = default look. */
	static void Select(AWLPlayerState* PS, EWLWeaponStyle Category, FName SkinId);

	/** The skin to render on this weapon: the category's selection if unlocked, else null. */
	static const FWLWeaponSkinDef* AppliedFor(const AWLPlayerState* PS, const FWLItemRow* Weapon);

	/** SERVER. Unlock + announcement. No-op if unknown or already owned. */
	static void Unlock(AWLPlayerState* PS, FName SkinId);

	/** SERVER. Rare cosmetic drop on a kill: picks a random not-yet-owned skin. */
	static void RollDrop(AWLPlayerState* PS, float Chance, FRandomStream& Rng);

	/** SERVER. The K key: cycle default -> each unlocked skin of the held weapon's category. Returns the chat line. */
	static FText CycleHeldWeaponSkin(AWLPlayerState* PS);

	/** Pure: the option after Current in the cycle (NAME_None = default look is always option 0). */
	static FName NextInCycle(const TArray<FName>& UnlockedInCategory, FName Current);
};
