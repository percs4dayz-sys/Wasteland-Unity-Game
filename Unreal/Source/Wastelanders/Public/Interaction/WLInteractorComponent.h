#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Interaction/WLInteractable.h"
#include "WLInteractorComponent.generated.h"

class AWLPlayerCharacter;
class AWLPlayerController;

DECLARE_MULTICAST_DELEGATE_ThreeParams(FWLOnWorldEvent, FName /*EventId*/, const FText& /*Speaker*/, const FText& /*Line*/);
DECLARE_MULTICAST_DELEGATE_TwoParams(FWLOnPendingInteractionChanged, AActor* /*Target, null when cleared*/, FName /*Verb*/);

/**
 * The player's "use things" half (Unity Interactor3D + the interaction parts of ClickToMove3D).
 * Lives on the pawn; the owning client drives it, the server validates and executes.
 *
 * Client flow (RequestInteract): Examine is answered locally. Anything else: if the pawn already
 * stands within the target's reach the request goes straight to the server; otherwise the pawn
 * paths to the target's near side with AWLPlayerController::MoveToLocation and TickComponent polls
 * until it is close enough, or as close as the ground allows (stalled against a collider), then
 * sends it. A failed path never silently uses something from afar (Unity fix: it once used things
 * 8.5 m away); the player is told it can't be reached.
 *
 * Server flow (ServerInteract): target must implement IWLInteractable, offer the verb to this
 * player, be within GetInteractRange()*1.5 of the pawn, and the pawn must be alive. Then any other
 * skilling action is cancelled and IWLInteractable::Interact runs on the server copy.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLInteractorComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLInteractorComponent();

	virtual void BeginPlay() override;
	virtual void TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction) override;

	/** OWNING CLIENT. Walk to Target if needed, then perform Verb on it. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Interaction")
	void RequestInteract(AActor* Target, FName Verb);

	/** OWNING CLIENT. Forget a walk-to-use errand (WASD, ground click, dodge). */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Interaction")
	void CancelPendingInteraction();

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Interaction")
	bool HasPendingInteraction() const { return PendingTarget.IsValid(); }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Interaction")
	AActor* GetPendingTarget() const { return PendingTarget.Get(); }

	/** Context menu options for Target (its options, default first, then Examine). For the UI. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Interaction")
	TArray<FWLInteractOption> GetOptionsFor(AActor* Target) const;

	/** Hover label text (Unity WorldInteractables.Name). */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Interaction")
	FText GetNameFor(AActor* Target) const;

	/** Server -> owner: drop any click-to-move path and errand (after teleports, travel, gates). */
	UFUNCTION(Client, Reliable)
	void ClientAbortMovement();

	/**
	 * Server -> owner: a world object wants the UI to do something that isn't a chat line, e.g.
	 * EventId "EggChoice" (the tutorial portal's companion pick) or "Dialogue" with a speaker.
	 */
	UFUNCTION(Client, Reliable)
	void ClientWorldEvent(FName EventId, const FText& Speaker, const FText& Line);

	FWLOnWorldEvent OnWorldEvent;
	FWLOnPendingInteractionChanged OnPendingInteractionChanged;

	/** Server: last time an interaction was accepted for this pawn (rate limiting). */
	double GetLastServerInteractTime() const { return LastServerInteractTime; }

	/** Minimum seconds between accepted ServerInteract calls (anti-spam). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Interaction")
	float MinServerInteractInterval = 0.1f;

	/** Seconds without getting nearer before an errand counts as stalled (Unity 0.3 s). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Interaction")
	float StallSeconds = 0.3f;

	/** Server accepts up to range * this, covering latency and the stalled-at-collider case. */
	static constexpr float ServerRangeTolerance = 1.5f;

	/** A stalled errand still fires within range * this (kept below ServerRangeTolerance). */
	static constexpr float StalledRangeFactor = 1.4f;

protected:
	UFUNCTION(Server, Reliable)
	void ServerInteract(AActor* Target, FName Verb);

	AWLPlayerCharacter* GetCharacter() const;
	AWLPlayerController* GetLocalController() const;

	/** Client: send now (in range), face the target, clear the errand. */
	void FireInteraction(AActor* Target, FName Verb);

	void ShowLocalMessage(const FText& Message) const;

	/** Server-side validation shared by ServerInteract; returns false with a player-facing reason. */
	bool ValidateServerInteract(AActor* Target, FName Verb, FText& OutReason) const;

	TWeakObjectPtr<AActor> PendingTarget;
	FName PendingVerb;
	float PendingBestDistance = TNumericLimits<float>::Max();
	double PendingStallSince = 0.0;

	double LastServerInteractTime = -1.0;
};
