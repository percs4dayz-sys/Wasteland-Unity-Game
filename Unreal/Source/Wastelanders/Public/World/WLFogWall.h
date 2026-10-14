#pragma once

#include "CoreMinimal.h"
#include "World/WLFlagPassage.h"
#include "WLFogWall.generated.h"

/**
 * A thin vertical curtain of fog on a zone boundary that hides the next area until you push through.
 * Ported from World/FogWall.cs. Never blocks movement; the first crossing sets "fogwall_<Id>" and
 * the curtain dissolves for that player for good (an already-crossed wall starts clear after load).
 *
 * Asset-free: the Unity particle fog isn't ported. Assign BarrierArtMesh (a fog card / volumetric
 * mesh) for the look; without art the curtain is invisible in game and shows as a box in the editor.
 * The Blocker box defines the curtain's shape (Unity: the collider IS the fog shape).
 */
UCLASS()
class WASTELANDERS_API AWLFogWall : public AWLFlagPassage
{
	GENERATED_BODY()

public:
	AWLFogWall();

	virtual bool IsOpenFor(const AWLPlayerState* PS) const override;

	/** Must be unique per wall. Persists as "fogwall_<Id>". */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Fog")
	FString Id = TEXT("boundary");

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Fog")
	FText RevealMessage;

	FString GetFlag() const { return TEXT("fogwall_") + Id; }

protected:
	virtual void OnPlayerEntered(AWLPlayerState* PS, APawn* Pawn) override;
};
