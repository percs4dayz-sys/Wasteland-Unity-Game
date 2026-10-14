#include "Interaction/WLInteractorComponent.h"
#include "Interaction/WLInteractionLibrary.h"
#include "Skilling/WLSkillingComponent.h"
#include "Building/WLBuildComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerState.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Engine/World.h"
#include "Wastelanders.h"

#define LOCTEXT_NAMESPACE "WLInteractor"

UWLInteractorComponent::UWLInteractorComponent()
{
	PrimaryComponentTick.bCanEverTick = true;
	PrimaryComponentTick.bStartWithTickEnabled = false; // only while an errand is pending
	SetIsReplicatedByDefault(true);
}

void UWLInteractorComponent::BeginPlay()
{
	Super::BeginPlay();

	// Building RPCs need a component on an actor the client owns. If the framework hasn't added
	// UWLBuildComponent to the pawn as a default subobject, add it here on the authority; it then
	// replicates to the owning client as a dynamic subobject.
	if (GetOwner() && GetOwner()->HasAuthority())
	{
		UWLBuildComponent::EnsureOn(GetOwner());
	}
}

AWLPlayerCharacter* UWLInteractorComponent::GetCharacter() const
{
	return Cast<AWLPlayerCharacter>(GetOwner());
}

AWLPlayerController* UWLInteractorComponent::GetLocalController() const
{
	const AWLPlayerCharacter* Char = GetCharacter();
	return Char ? Cast<AWLPlayerController>(Char->GetController()) : nullptr;
}

void UWLInteractorComponent::ShowLocalMessage(const FText& Message) const
{
	if (AWLPlayerController* PC = GetLocalController(); PC && PC->IsLocalController())
	{
		// Local only (no RPC): Examine and "can't reach" are the client's own business.
		PC->OnGameMessage.Broadcast(Message);
	}
}

TArray<FWLInteractOption> UWLInteractorComponent::GetOptionsFor(AActor* Target) const
{
	return FWLInteraction::GetMenuOptions(Target, GetCharacter());
}

FText UWLInteractorComponent::GetNameFor(AActor* Target) const
{
	return FWLInteraction::GetDisplayName(Target);
}

void UWLInteractorComponent::RequestInteract(AActor* Target, FName Verb)
{
	AWLPlayerCharacter* Char = GetCharacter();
	if (!Char || !Target || !Char->IsLocallyControlled())
	{
		return;
	}

	if (Verb == FWLInteraction::ExamineVerb)
	{
		ShowLocalMessage(FWLInteraction::GetExamineText(Target));
		return;
	}

	CancelPendingInteraction();

	const float Range = FWLInteraction::GetRange(Target);
	const float Distance = FWLInteraction::DistanceToTarget(Target, Char->GetActorLocation());
	if (Distance <= Range)
	{
		FireInteraction(Target, Verb);
		return;
	}

	AWLPlayerController* PC = GetLocalController();
	if (!PC)
	{
		return;
	}
	PendingTarget = Target;
	PendingVerb = Verb;
	PendingBestDistance = Distance;
	PendingStallSince = GetWorld()->GetTimeSeconds();
	// Stop a little inside the reach so arrival is unambiguous.
	PC->MoveToLocation(FWLInteraction::GetUseLocation(Target, Char->GetActorLocation()), FMath::Max(10.f, Range - 25.f));
	SetComponentTickEnabled(true);
	OnPendingInteractionChanged.Broadcast(Target, Verb);
}

void UWLInteractorComponent::CancelPendingInteraction()
{
	const bool bHad = PendingTarget.IsValid() || !PendingVerb.IsNone();
	PendingTarget.Reset();
	PendingVerb = NAME_None;
	PendingBestDistance = TNumericLimits<float>::Max();
	SetComponentTickEnabled(false);
	if (bHad)
	{
		OnPendingInteractionChanged.Broadcast(nullptr, NAME_None);
	}
}

void UWLInteractorComponent::TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction)
{
	Super::TickComponent(DeltaTime, TickType, ThisTickFunction);

	AActor* Target = PendingTarget.Get();
	AWLPlayerCharacter* Char = GetCharacter();
	AWLPlayerController* PC = GetLocalController();
	if (!Target || !Char || !PC)
	{
		CancelPendingInteraction();
		return;
	}

	const double Now = GetWorld()->GetTimeSeconds();
	const float Range = FWLInteraction::GetRange(Target);
	const float Distance = FWLInteraction::DistanceToTarget(Target, Char->GetActorLocation());
	if (Distance < PendingBestDistance - 2.f)
	{
		PendingBestDistance = Distance;
		PendingStallSince = Now;
	}

	// Close enough, or as close as the ground allows: the walk ended or the pawn has been pressed
	// against the target's collider without getting nearer.
	const bool bStalled = !PC->HasDestination() || (Now - PendingStallSince) > StallSeconds;
	if (Distance <= Range || (bStalled && Distance <= Range * StalledRangeFactor))
	{
		const FName Verb = PendingVerb;
		PC->StopPathFollowing();
		CancelPendingInteraction();
		FireInteraction(Target, Verb);
		return;
	}

	if (!PC->HasDestination())
	{
		CancelPendingInteraction();
		ShowLocalMessage(LOCTEXT("CantReach", "I can't reach that from here. Try approaching from another side."));
	}
}

void UWLInteractorComponent::FireInteraction(AActor* Target, FName Verb)
{
	AWLPlayerCharacter* Char = GetCharacter();
	if (!Char || !Target)
	{
		return;
	}
	// Face the thing (Unity FaceToward). The autonomous proxy's rotation travels with its moves.
	FVector To = FWLInteraction::GetUseLocation(Target, Char->GetActorLocation()) - Char->GetActorLocation();
	if (To.SizeSquared2D() < 1.f)
	{
		To = Target->GetActorLocation() - Char->GetActorLocation();
	}
	To.Z = 0.f;
	if (To.SizeSquared() > 1.f)
	{
		Char->SetActorRotation(To.Rotation());
	}
	ServerInteract(Target, Verb);
}

bool UWLInteractorComponent::ValidateServerInteract(AActor* Target, FName Verb, FText& OutReason) const
{
	const AWLPlayerCharacter* Char = GetCharacter();
	if (!Char || !IsValid(Target) || !Target->Implements<UWLInteractable>() || Verb.IsNone() || Verb == FWLInteraction::ExamineVerb)
	{
		return false;
	}

	const AWLPlayerState* PS = Char->GetWLPlayerState();
	const UWLAbilitySystemComponent* ASC = PS ? PS->GetWLAbilitySystem() : nullptr;
	if (!ASC || ASC->IsDead())
	{
		return false;
	}

	// The verb must be one this target actually offers this player (no hidden or spoofed verbs).
	const TArray<FWLInteractOption> Options = IWLInteractable::Execute_GetInteractOptions(Target, Char);
	if (!Options.ContainsByPredicate([Verb](const FWLInteractOption& O) { return O.Verb == Verb; }))
	{
		return false;
	}

	const float Range = FWLInteraction::GetRange(Target);
	if (FWLInteraction::DistanceToTarget(Target, Char->GetActorLocation()) > Range * ServerRangeTolerance)
	{
		OutReason = LOCTEXT("TooFar", "You're too far away to do that.");
		return false;
	}
	return true;
}

void UWLInteractorComponent::ServerInteract_Implementation(AActor* Target, FName Verb)
{
	AWLPlayerCharacter* Char = GetCharacter();
	if (!Char)
	{
		return;
	}

	const double Now = GetWorld()->GetTimeSeconds();
	if (LastServerInteractTime >= 0.0 && Now - LastServerInteractTime < MinServerInteractInterval)
	{
		return; // rate limit: silently drop spam
	}

	FText Reason;
	if (!ValidateServerInteract(Target, Verb, Reason))
	{
		if (!Reason.IsEmpty())
		{
			if (AWLPlayerController* PC = Cast<AWLPlayerController>(Char->GetController()))
			{
				PC->ClientGameMessage(Reason);
			}
		}
		return;
	}
	LastServerInteractTime = Now;

	// Doing something else interrupts gathering / batch crafting and closes a station opened elsewhere.
	if (UWLSkillingComponent* Skilling = Char->GetSkilling())
	{
		Skilling->NotifyInteractingWith(Target);
	}

	IWLInteractable::Execute_Interact(Target, Char, Verb);
}

void UWLInteractorComponent::ClientAbortMovement_Implementation()
{
	CancelPendingInteraction();
	if (AWLPlayerController* PC = GetLocalController())
	{
		PC->StopPathFollowing();
	}
}

void UWLInteractorComponent::ClientWorldEvent_Implementation(FName EventId, const FText& Speaker, const FText& Line)
{
	OnWorldEvent.Broadcast(EventId, Speaker, Line);
	if (!Line.IsEmpty())
	{
		// Unity fell back to a chat line when no dialogue UI was present; keep the line in the log too.
		ShowLocalMessage(Speaker.IsEmpty() ? Line : FText::Format(LOCTEXT("SpeakerLine", "[{0}]: {1}"), Speaker, Line));
	}
}

#undef LOCTEXT_NAMESPACE
