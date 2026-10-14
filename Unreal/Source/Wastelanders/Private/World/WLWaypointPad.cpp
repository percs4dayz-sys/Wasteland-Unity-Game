#include "World/WLWaypointPad.h"
#include "World/WLTravel.h"
#include "World/WLPlaceholderMesh.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Components/BoxComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/World.h"
#include "TimerManager.h"

#define LOCTEXT_NAMESPACE "WLWaypointPad"

AWLWaypointPad::AWLWaypointPad()
{
	PrimaryActorTick.bCanEverTick = false;
	bReplicates = false; // per-player lock state comes from flags

	Trigger = CreateDefaultSubobject<UBoxComponent>(TEXT("Trigger"));
	SetRootComponent(Trigger);
	Trigger->SetBoxExtent(FVector(100.f, 100.f, 80.f));
	Trigger->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
	Trigger->SetCollisionResponseToAllChannels(ECR_Ignore);
	Trigger->SetCollisionResponseToChannel(ECC_Pawn, ECR_Overlap);
	Trigger->SetGenerateOverlapEvents(true);

	UnlockedVisual = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("UnlockedVisual"));
	UnlockedVisual->SetupAttachment(Trigger);
	UnlockedVisual->SetCollisionEnabled(ECollisionEnabled::NoCollision);

	LockedVisual = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("LockedVisual"));
	LockedVisual->SetupAttachment(Trigger);
	LockedVisual->SetCollisionEnabled(ECollisionEnabled::NoCollision);

	DestinationName = LOCTEXT("DefaultDest", "Greenbelt");
	LockedMessage = LOCTEXT("Locked", "The pad is dark. You have not yet set foot in that region.");
}

void AWLWaypointPad::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	const FVector PadSize(180.f, 180.f, 10.f);
	FWLPlaceholderMesh::Apply(UnlockedVisual, UnlockedArtMesh, EWLPlaceholderShape::Cylinder, PadSize, FLinearColor(0.3f, 0.85f, 1.f));
	FWLPlaceholderMesh::Apply(LockedVisual, LockedArtMesh, EWLPlaceholderShape::Cylinder, PadSize, FLinearColor(0.15f, 0.15f, 0.17f));
	const FVector Down(0.f, 0.f, -Trigger->GetUnscaledBoxExtent().Z + 5.f);
	UnlockedVisual->SetRelativeLocation(Down);
	LockedVisual->SetRelativeLocation(Down);
}

void AWLWaypointPad::BeginPlay()
{
	Super::BeginPlay();
	if (HasAuthority())
	{
		Trigger->OnComponentBeginOverlap.AddDynamic(this, &AWLWaypointPad::OnTriggerBegin);
	}
	RefreshVisuals();
	if (GetNetMode() != NM_DedicatedServer)
	{
		// The character loads a moment after the level, so keep visuals in step with the flags.
		GetWorldTimerManager().SetTimer(VisualTimer, this, &AWLWaypointPad::RefreshVisuals, 0.5f, true);
	}
}

bool AWLWaypointPad::IsUnlockedFor(const AWLPlayerState* PS) const
{
	if (RequiredFlag.IsEmpty())
	{
		return true;
	}
	const UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	return Flags && Flags->HasFlag(RequiredFlag);
}

void AWLWaypointPad::RefreshVisuals()
{
	const APlayerController* PC = GetWorld() ? GetWorld()->GetFirstPlayerController() : nullptr;
	const bool bUnlocked = IsUnlockedFor(PC ? PC->GetPlayerState<AWLPlayerState>() : nullptr);
	UnlockedVisual->SetHiddenInGame(!bUnlocked);
	LockedVisual->SetHiddenInGame(bUnlocked);
}

void AWLWaypointPad::OnTriggerBegin(UPrimitiveComponent* OverlappedComp, AActor* OtherActor, UPrimitiveComponent* OtherComp, int32 OtherBodyIndex, bool bFromSweep, const FHitResult& SweepResult)
{
	AWLPlayerCharacter* Char = Cast<AWLPlayerCharacter>(OtherActor);
	AWLPlayerState* PS = Char ? Char->GetWLPlayerState() : nullptr;
	if (!PS)
	{
		return;
	}
	const double Now = GetWorld()->GetTimeSeconds();
	const TWeakObjectPtr<AWLPlayerState> Key(PS);
	if (const double* Next = NextUseTime.Find(Key); Next && Now < *Next)
	{
		return;
	}
	NextUseTime.Add(Key, Now + RetriggerCooldown);

	AWLPlayerController* PC = Cast<AWLPlayerController>(Char->GetController());
	if (!IsUnlockedFor(PS))
	{
		if (PC)
		{
			PC->ClientGameMessage(FText::Format(LOCTEXT("LockedLine", "[WAYPOINT] {0}"), LockedMessage));
		}
		return;
	}

	FVector Dest = GetDestinationLocation();
	FWLTravel::SeatOnGround(GetWorld(), Dest, 90.f, Char);
	if (FWLTravel::TeleportPlayer(Char, Dest) && PC)
	{
		PC->ClientGameMessage(FText::Format(LOCTEXT("Arrive", "[WAYPOINT] {0}."), DestinationName));
	}
}

#undef LOCTEXT_NAMESPACE
