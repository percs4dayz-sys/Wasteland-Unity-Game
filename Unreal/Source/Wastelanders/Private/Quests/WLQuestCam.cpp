#include "Quests/WLQuestCam.h"
#include "Player/WLPlayerController.h"
#include "Camera/CameraActor.h"
#include "Camera/PlayerCameraManager.h"
#include "GameFramework/Pawn.h"
#include "Engine/World.h"
#include "TimerManager.h"

#define LOCTEXT_NAMESPACE "WLQuestCam"

UWLQuestCam* UWLQuestCam::Get(const UObject* WorldContextObject)
{
	const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
	return World ? World->GetSubsystem<UWLQuestCam>() : nullptr;
}

void UWLQuestCam::Peek(APlayerController* PC, const FVector& Point, const FText& Caption)
{
	UWorld* World = GetWorld();
	if (!World || !PC || !PC->IsLocalController() || World->GetNetMode() == NM_DedicatedServer)
	{
		return;
	}
	Cancel();

	// Keep the player's current viewing angle and distance, just centred on the point.
	FRotator ViewRot = FRotator(-52.f, 0.f, 0.f);
	float Distance = FallbackDistance;
	if (const APlayerCameraManager* PCM = PC->PlayerCameraManager)
	{
		ViewRot = PCM->GetCameraRotation();
		if (const APawn* Pawn = PC->GetPawn())
		{
			Distance = FMath::Clamp(static_cast<float>(FVector::Dist(PCM->GetCameraLocation(), Pawn->GetActorLocation())), 300.f, 3000.f);
		}
	}

	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	Params.ObjectFlags |= RF_Transient;
	PeekCamera = World->SpawnActor<ACameraActor>(Point - ViewRot.Vector() * Distance, ViewRot, Params);
	if (!PeekCamera)
	{
		return;
	}
	PeekCamera->SetReplicates(false);
	PeekPC = PC;
	PC->SetViewTargetWithBlend(PeekCamera, TravelIn, VTBlend_EaseInOut, 2.f);

	if (!Caption.IsEmpty())
	{
		if (AWLPlayerController* WLPC = Cast<AWLPlayerController>(PC))
		{
			WLPC->OnGameMessage.Broadcast(FText::Format(LOCTEXT("GuideCaption", "[GUIDE]: {0}"), Caption));
		}
	}

	World->GetTimerManager().SetTimer(ReturnTimer, this, &UWLQuestCam::BeginReturn, FMath::Max(0.01f, TravelIn + Hold), false);
}

void UWLQuestCam::BeginReturn()
{
	APlayerController* PC = PeekPC.Get();
	if (PC && PC->GetPawn())
	{
		PC->SetViewTargetWithBlend(PC->GetPawn(), TravelOut, VTBlend_EaseInOut, 2.f);
	}
	if (UWorld* World = GetWorld())
	{
		World->GetTimerManager().SetTimer(FinishTimer, this, &UWLQuestCam::Finish, FMath::Max(0.01f, TravelOut), false);
	}
}

void UWLQuestCam::Finish()
{
	if (PeekCamera)
	{
		APlayerController* PC = PeekPC.Get();
		if (PC && PC->GetViewTarget() == PeekCamera && PC->GetPawn())
		{
			PC->SetViewTarget(PC->GetPawn());
		}
		PeekCamera->Destroy();
		PeekCamera = nullptr;
	}
	PeekPC.Reset();
}

void UWLQuestCam::Cancel()
{
	if (UWorld* World = GetWorld())
	{
		World->GetTimerManager().ClearTimer(ReturnTimer);
		World->GetTimerManager().ClearTimer(FinishTimer);
	}
	Finish();
}

void UWLQuestCam::Deinitialize()
{
	Cancel();
	Super::Deinitialize();
}

#undef LOCTEXT_NAMESPACE
