#pragma once

#include "CoreMinimal.h"
#include "WLBuiltinDialogues.generated.h"

class UWLDialogueAsset;

/** Which code-built conversation an NPC uses when no UWLDialogueAsset is assigned. */
UENUM(BlueprintType)
enum class EWLBuiltinDialogue : uint8
{
	None,
	Roxy,      // NPC/RoxyNPC.cs: bank lesson, starter lessons, beast picker, fission handoff
	OldMara,   // NPC/TutorialNPC.cs: starter kit, intro, skill rundown, cycling barks
	Wanderer   // NPC/WandererNPC.cs: one line per visit, cycling
};

/**
 * Builds Unity's hard-coded NPC conversations as UWLDialogueAsset graphs at runtime, so every NPC
 * works with zero assets. The graphs reproduce Unity's branch order, conditions, gifts and flags
 * exactly; RoxyNPC.Interact's if-chain becomes the ordered entry list. Designers replace any of them
 * by assigning a dialogue asset on the NPC.
 */
struct WASTELANDERS_API FWLBuiltinDialogues
{
	/** Null for None. WandererLines is only used by Wanderer (each instance has its own lines). */
	static UWLDialogueAsset* Build(EWLBuiltinDialogue Which, UObject* Outer, const TArray<FText>& WandererLines);

	static UWLDialogueAsset* BuildRoxy(UObject* Outer);
	static UWLDialogueAsset* BuildOldMara(UObject* Outer);
	static UWLDialogueAsset* BuildWanderer(UObject* Outer, const TArray<FText>& Lines);

	/** WandererNPC.cs default lines. */
	static TArray<FText> DefaultWandererLines();
};
