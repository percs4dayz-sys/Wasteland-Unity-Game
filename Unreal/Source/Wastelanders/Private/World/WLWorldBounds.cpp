#include "World/WLWorldBounds.h"
#include "Core/WLGameMode.h"
#include "Player/WLPlayerController.h"
#include "Components/BoxComponent.h"
#include "GameFramework/GameStateBase.h"
#include "GameFramework/PlayerState.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "TimerManager.h"
#include "Wastelanders.h"

// --- pure ---------------------------------------------------------------------------------------

bool FWLWorldBounds::IsOutsideBox(const FVector& P, const FBox& Box, float Edge, float Ceiling, float Floor)
{
	if (!Box.IsValid)
	{
		return false;
	}
	if (P.X < Box.Min.X - Edge || P.X > Box.Max.X + Edge) return true;
	if (P.Y < Box.Min.Y - Edge || P.Y > Box.Max.Y + Edge) return true;
	if (P.Z > Box.Max.Z + Ceiling || P.Z < Box.Min.Z - Floor) return true;
	return false;
}

FString FWLWorldBounds::Explain(const FVector& P, const FBox& Box, float Edge, float Ceiling, float Floor)
{
	if (!Box.IsValid)
	{
		return FString();
	}
	if (P.X < Box.Min.X - Edge) return FString::Printf(TEXT("X %.1f is %.1f uu past the west edge (%.0f)"), P.X, Box.Min.X - Edge - P.X, Box.Min.X);
	if (P.X > Box.Max.X + Edge) return FString::Printf(TEXT("X %.1f is %.1f uu past the east edge (%.0f)"), P.X, P.X - Box.Max.X - Edge, Box.Max.X);
	if (P.Y < Box.Min.Y - Edge) return FString::Printf(TEXT("Y %.1f is %.1f uu past the south edge (%.0f)"), P.Y, Box.Min.Y - Edge - P.Y, Box.Min.Y);
	if (P.Y > Box.Max.Y + Edge) return FString::Printf(TEXT("Y %.1f is %.1f uu past the north edge (%.0f)"), P.Y, P.Y - Box.Max.Y - Edge, Box.Max.Y);
	if (P.Z > Box.Max.Z + Ceiling) return FString::Printf(TEXT("Z %.1f is %.1f uu above the ceiling (%.1f)"), P.Z, P.Z - Box.Max.Z - Ceiling, Box.Max.Z + Ceiling);
	if (P.Z < Box.Min.Z - Floor) return FString::Printf(TEXT("Z %.1f is %.1f uu below the floor (%.1f)"), P.Z, Box.Min.Z - Floor - P.Z, Box.Min.Z - Floor);
	return FString();
}

bool FWLWorldBounds::IsOutside(const UObject* WorldContext, const FVector& P)
{
	const UWorld* World = WorldContext ? WorldContext->GetWorld() : nullptr;
	const UWLWorldBoundsSubsystem* Sub = World ? World->GetSubsystem<UWLWorldBoundsSubsystem>() : nullptr;
	FBox Box(ForceInit);
	return Sub && Sub->TryGet(Box) && IsOutsideBox(P, Box);
}

// --- volume -------------------------------------------------------------------------------------

AWLWorldBoundsVolume::AWLWorldBoundsVolume()
{
	PrimaryActorTick.bCanEverTick = false;
	bReplicates = false;
	Box = CreateDefaultSubobject<UBoxComponent>(TEXT("Box"));
	SetRootComponent(Box);
	Box->SetBoxExtent(FVector(3000.f, 3000.f, 200.f)); // Unity BoundaryVisualizer 60 x 4 x 60 m
	Box->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Box->SetCanEverAffectNavigation(false);
	Box->ShapeColor = FColor(255, 115, 50);
	Box->SetHiddenInGame(true);
	Box->SetLineThickness(8.f);
}

void AWLWorldBoundsVolume::BeginPlay()
{
	Super::BeginPlay();
	// Streamed-in volumes register themselves; the subsystem also sweeps at world begin play.
	if (UWLWorldBoundsSubsystem* Sub = GetWorld()->GetSubsystem<UWLWorldBoundsSubsystem>())
	{
		Sub->RegisterVolume(this);
	}
}

void AWLWorldBoundsVolume::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWLWorldBoundsSubsystem* Sub = GetWorld() ? GetWorld()->GetSubsystem<UWLWorldBoundsSubsystem>() : nullptr)
	{
		Sub->UnregisterVolume(this);
	}
	Super::EndPlay(EndPlayReason);
}

FBox AWLWorldBoundsVolume::GetBounds() const
{
	return Box->Bounds.GetBox();
}

// --- subsystem ----------------------------------------------------------------------------------

bool UWLWorldBoundsSubsystem::ShouldCreateSubsystem(UObject* Outer) const
{
	const UWorld* World = Cast<UWorld>(Outer);
	return World && World->IsGameWorld();
}

void UWLWorldBoundsSubsystem::OnWorldBeginPlay(UWorld& InWorld)
{
	Super::OnWorldBeginPlay(InWorld);
	// Collect volumes once at start (placed in the persistent level / always-loaded data layer).
	for (TActorIterator<AWLWorldBoundsVolume> It(&InWorld); It; ++It)
	{
		RegisterVolume(*It);
	}
	if (InWorld.GetNetMode() != NM_Client)
	{
		InWorld.GetTimerManager().SetTimer(WatchdogTimer, FTimerDelegate::CreateUObject(this, &UWLWorldBoundsSubsystem::Watchdog), WatchdogInterval, true);
	}
}

bool UWLWorldBoundsSubsystem::TryGet(FBox& OutBox) const
{
	OutBox = FBox(ForceInit);
	for (const TWeakObjectPtr<AWLWorldBoundsVolume>& V : Volumes)
	{
		if (const AWLWorldBoundsVolume* Vol = V.Get())
		{
			OutBox += Vol->GetBounds();
		}
	}
	return OutBox.IsValid != 0;
}

void UWLWorldBoundsSubsystem::Watchdog()
{
	UWorld* World = GetWorld();
	FBox Box(ForceInit);
	if (!World || !TryGet(Box))
	{
		return;
	}
	AWLGameMode* GM = World->GetAuthGameMode<AWLGameMode>();
	const AGameStateBase* GS = World->GetGameState();
	if (!GM || !GS)
	{
		return;
	}
	for (APlayerState* PS : GS->PlayerArray)
	{
		APawn* Pawn = PS ? PS->GetPawn() : nullptr;
		if (!Pawn || !FWLWorldBounds::IsOutsideBox(Pawn->GetActorLocation(), Box))
		{
			continue;
		}
		UE_LOG(LogWastelanders, Warning, TEXT("%s is outside the world: %s. Returning to spawn."),
			*PS->GetPlayerName(), *FWLWorldBounds::Explain(Pawn->GetActorLocation(), Box));
		if (GM->TeleportToSpawn(Pawn->GetController()))
		{
			if (AWLPlayerController* PC = Cast<AWLPlayerController>(Pawn->GetController()))
			{
				PC->ClientGameMessage(NSLOCTEXT("WLWorldBounds", "Rescued", "You were somewhere impossible. You've been returned to safety."));
			}
		}
	}
}
