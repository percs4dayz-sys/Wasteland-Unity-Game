#pragma once

#include "CoreMinimal.h"
#include "UObject/Interface.h"
#include "WLInteractable.generated.h"

class AWLPlayerCharacter;

/** One entry in the right-click context menu (Unity ContextMenuUI options). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLInteractOption
{
	GENERATED_BODY()

	/** Stable verb id sent back to the server, e.g. "Chop", "Talk", "Examine", "Bank". */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Interaction")
	FName Verb;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Interaction")
	FText Label;

	/** The first option is the default left-click action. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Interaction")
	bool bDefault = false;
};

UINTERFACE(MinimalAPI, BlueprintType)
class UWLInteractable : public UInterface
{
	GENERATED_BODY()
};

/**
 * Anything the player can click in the world: resource nodes, stations, NPCs, ground items, doors.
 * Merges Unity's Interactor3D targets and IExaminable.
 *
 * Flow: the client asks for options (local, cosmetic) -> player picks one -> the client's
 * UWLInteractorComponent sends a Server RPC with (Target, Verb) -> the server walks the pawn into
 * range and then calls Interact on the server copy. Interact must validate everything itself.
 */
class WASTELANDERS_API IWLInteractable
{
	GENERATED_BODY()

public:
	/** Name shown on hover and at the top of the context menu. */
	UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category = "Interaction")
	FText GetInteractName() const;

	/** Examine text (OSRS "Examine"). */
	UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category = "Interaction")
	FText GetExamineText() const;

	/** Context menu options for this player, default action first. "Examine" is appended automatically. */
	UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category = "Interaction")
	TArray<FWLInteractOption> GetInteractOptions(const AWLPlayerCharacter* Player) const;

	/** How close (uu) the pawn must stand before Interact runs. */
	UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category = "Interaction")
	float GetInteractRange() const;

	/** SERVER ONLY. Perform the verb for this player. Return false if it could not start. */
	UFUNCTION(BlueprintNativeEvent, BlueprintCallable, BlueprintAuthorityOnly, Category = "Interaction")
	bool Interact(AWLPlayerCharacter* Player, FName Verb);
};
