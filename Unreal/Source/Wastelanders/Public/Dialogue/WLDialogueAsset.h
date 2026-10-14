#pragma once

#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "Dialogue/WLDialogueTypes.h"
#include "WLDialogueAsset.generated.h"

/**
 * One NPC's conversation graph (see WLDialogueTypes.h for the model). Designers author these as data
 * assets; until they exist, FWLBuiltinDialogues builds Roxy's, Old Mara's and the wanderers' graphs
 * in code from the Unity content, so every NPC works with zero assets.
 */
UCLASS(BlueprintType)
class WASTELANDERS_API UWLDialogueAsset : public UPrimaryDataAsset
{
	GENERATED_BODY()

public:
	/** Run on every "Talk", before the entry rules. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<FWLDialogueAction> PreActions;

	/** Ordered: the first entry whose conditions pass starts the conversation. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<FWLDialogueEntry> Entries;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<FWLDialogueNode> Nodes;

	const FWLDialogueNode* FindNode(FName Id) const
	{
		return Id.IsNone() ? nullptr : Nodes.FindByPredicate([Id](const FWLDialogueNode& N) { return N.Id == Id; });
	}

	// --- builder helpers (used by FWLBuiltinDialogues; handy for tests and tools) ---
	FWLDialogueNode& AddNode(FName Id, std::initializer_list<FText> Lines, FName Next = NAME_None);
	FWLDialogueEntry& AddEntry(FName Node);

#if WITH_EDITOR
	/** Flags entries / choices / actions pointing at nodes that don't exist. */
	virtual EDataValidationResult IsDataValid(class FDataValidationContext& Context) const override;
#endif
};
