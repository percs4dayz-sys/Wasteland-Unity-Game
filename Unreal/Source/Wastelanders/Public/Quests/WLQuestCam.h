#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "WLQuestCam.generated.h"

class ACameraActor;
class APlayerController;

/**
 * Client-side "quest intro" camera peek (World/QuestCam.cs). When Roxy hands out a lesson, the
 * camera glides from the player to the place to go (fishing spot, rubble, workbench...), holds a
 * beat with a caption, then glides back.
 *
 * It's purely cosmetic and never takes control of the player: movement and clicks keep working. Only
 * the view target is borrowed, through a temporary local camera and SetViewTargetWithBlend
 * (ease in/out, like Unity's SmoothStep ramp). Driven by UWLQuestComponent::ClientPeek.
 * Choosing WHAT to peek at is server-side (FWLQuestTargets).
 */
UCLASS()
class WASTELANDERS_API UWLQuestCam : public UWorldSubsystem
{
	GENERATED_BODY()

public:
	static UWLQuestCam* Get(const UObject* WorldContextObject);

	/** Peek at a world point, then return to the pawn. A new peek replaces a running one. */
	void Peek(APlayerController* PC, const FVector& Point, const FText& Caption);

	/** Cancel a running peek and blend straight back. */
	void Cancel();

	bool IsPeeking() const { return PeekCamera != nullptr; }

	/** Glide out to the target (s). */
	float TravelIn = 1.0f;
	/** Linger on it (s). */
	float Hold = 1.4f;
	/** Glide back to the player (s). */
	float TravelOut = 0.8f;
	/** Camera distance from the point when the player's own camera distance can't be read (uu). */
	float FallbackDistance = 1400.f;

	virtual void Deinitialize() override;

private:
	void BeginReturn();
	void Finish();

	UPROPERTY(Transient)
	TObjectPtr<ACameraActor> PeekCamera;

	TWeakObjectPtr<APlayerController> PeekPC;
	FTimerHandle ReturnTimer;
	FTimerHandle FinishTimer;
};
