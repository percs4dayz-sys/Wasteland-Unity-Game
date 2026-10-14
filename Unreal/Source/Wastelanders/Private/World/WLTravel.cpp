#include "World/WLTravel.h"
#include "World/WLWorldSettings.h"
#include "Interaction/WLInteractorComponent.h"
#include "Skilling/WLSkillingComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerState.h"
#include "Core/WLGameMode.h"
#include "Engine/World.h"
#include "Wastelanders.h"

#define LOCTEXT_NAMESPACE "WLTravel"

bool FWLTravel::TeleportPlayer(AWLPlayerCharacter* Character, const FVector& Location, const FRotator* Rotation)
{
	if (!Character || !Character->HasAuthority())
	{
		return false;
	}
	const FRotator Rot = Rotation ? *Rotation : Character->GetActorRotation();
	if (!Character->TeleportTo(Location, Rot))
	{
		return false;
	}
	if (UWLSkillingComponent* Skilling = Character->GetSkilling())
	{
		Skilling->StopAction();
	}
	if (UWLInteractorComponent* Interactor = Character->GetInteractor())
	{
		Interactor->ClientAbortMovement();
	}
	return true;
}

bool FWLTravel::SeatOnGround(const UWorld* World, FVector& InOutLocation, float HalfHeight, const AActor* Ignore, float StartAbove)
{
	if (!World)
	{
		return false;
	}
	TArray<FHitResult> Hits;
	FCollisionQueryParams Params(SCENE_QUERY_STAT(WLSeatOnGround), false, Ignore);
	const FVector Start(InOutLocation.X, InOutLocation.Y, InOutLocation.Z + StartAbove);
	const FVector End(InOutLocation.X, InOutLocation.Y, InOutLocation.Z - 2000000.f);
	World->LineTraceMultiByChannel(Hits, Start, End, ECC_Visibility, Params);
	// Skip steep hits (walls, undersides): only a walkable top seats the player.
	for (const FHitResult& Hit : Hits)
	{
		if (Hit.bBlockingHit && Hit.ImpactNormal.Z >= 0.2f)
		{
			InOutLocation = Hit.ImpactPoint + FVector(0.f, 0.f, HalfHeight + 5.f);
			return true;
		}
	}
	return false;
}

bool FWLTravel::TravelToDestination(APlayerController* Controller, FName Destination)
{
	if (!Controller || !Controller->HasAuthority())
	{
		return false;
	}
	AWLPlayerController* WLPC = Cast<AWLPlayerController>(Controller);
	const FWLTravelDestination* Dest = UWLWorldSettings::Get()->TravelDestinations.Find(Destination);
	if (!Dest || (Dest->ServerAddress.IsEmpty() && Dest->MapPath.IsEmpty()))
	{
		UE_LOG(LogWastelanders, Warning, TEXT("Travel destination '%s' is not configured (Project Settings > Wastelanders World)"), *Destination.ToString());
		if (WLPC)
		{
			WLPC->ClientGameMessage(LOCTEXT("NoDest", "[WARP]: The way is shut. (That region isn't reachable from this server.)"));
		}
		return false;
	}

	AWLPlayerState* PS = Controller->GetPlayerState<AWLPlayerState>();
	if (AWLGameMode* GM = Controller->GetWorld()->GetAuthGameMode<AWLGameMode>())
	{
		GM->SavePlayer(PS); // the next shard loads the character from persistence
	}

	if (WLPC)
	{
		WLPC->ClientGameMessage(LOCTEXT("Stepping", "[WARP]: Stepping into the wasteland..."));
	}

	FString Name = PS ? PS->GetPlayerName() : FString(TEXT("Survivor"));
	Name.ReplaceCharInline(TEXT(' '), TEXT('_'));
	const FString Target = Dest->ServerAddress.IsEmpty() ? Dest->MapPath : Dest->ServerAddress;
	Controller->ClientTravel(FString::Printf(TEXT("%s?Name=%s"), *Target, *Name), TRAVEL_Absolute);
	return true;
}

#undef LOCTEXT_NAMESPACE
