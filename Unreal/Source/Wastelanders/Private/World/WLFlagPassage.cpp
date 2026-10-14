#include "World/WLFlagPassage.h"
#include "World/WLEnemySpawner.h"
#include "World/WLPlaceholderMesh.h"
#include "Combat/WLEnemyCharacter.h"
#include "Core/WLGameEventsSubsystem.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Components/BoxComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Components/CapsuleComponent.h"
#include "GameFramework/Character.h"
#include "GameFramework/GameStateBase.h"
#include "Engine/World.h"
#include "TimerManager.h"

AWLFlagPassage::AWLFlagPassage()
{
	PrimaryActorTick.bCanEverTick = false;
	// Nothing about the passage itself replicates: state is each player's flags.
	bReplicates = false;

	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	SetRootComponent(Root);

	Blocker = CreateDefaultSubobject<UBoxComponent>(TEXT("Blocker"));
	Blocker->SetupAttachment(Root);
	Blocker->SetBoxExtent(FVector(50.f, 400.f, 300.f));
	Blocker->SetRelativeLocation(FVector(0.f, 0.f, 300.f));
	Blocker->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
	Blocker->SetCollisionResponseToAllChannels(ECR_Ignore);
	Blocker->SetCollisionResponseToChannel(ECC_Pawn, ECR_Block);
	Blocker->SetCanEverAffectNavigation(false); // the path exists; only some players may walk it
	Blocker->ShapeColor = FColor(255, 80, 60);

	Trigger = CreateDefaultSubobject<UBoxComponent>(TEXT("Trigger"));
	Trigger->SetupAttachment(Root);
	Trigger->SetBoxExtent(FVector(250.f, 450.f, 300.f));
	Trigger->SetRelativeLocation(FVector(0.f, 0.f, 300.f));
	Trigger->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
	Trigger->SetCollisionResponseToAllChannels(ECR_Ignore);
	Trigger->SetCollisionResponseToChannel(ECC_Pawn, ECR_Overlap);
	Trigger->SetGenerateOverlapEvents(true);
	Trigger->SetCanEverAffectNavigation(false);

	Barrier = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Barrier"));
	Barrier->SetupAttachment(Blocker);
	Barrier->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Barrier->SetCanEverAffectNavigation(false);
	Barrier->SetCastShadow(false);
}

void AWLFlagPassage::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	const FVector Size = Blocker->GetUnscaledBoxExtent() * 2.f;
	if (FWLPlaceholderMesh::Apply(Barrier, BarrierArtMesh, EWLPlaceholderShape::Cube, Size, FLinearColor(0.25f, 0.25f, 0.3f)))
	{
		Barrier->SetHiddenInGame(!bShowPlaceholderBarrier);
	}
	else
	{
		Barrier->SetRelativeScale3D(FVector::OneVector);
	}
}

void AWLFlagPassage::BeginPlay()
{
	Super::BeginPlay();

	// Capture each mover's poses. The authored pose is CLOSED unless bPlacedPoseIsOpen.
	TArray<USceneComponent*> Scene;
	GetComponents<USceneComponent>(Scene);
	for (const FWLPassageMover& M : Movers)
	{
		USceneComponent** Found = Scene.FindByPredicate([&M](const USceneComponent* C) { return C && C->GetFName() == M.ComponentName; });
		if (!Found)
		{
			continue;
		}
		FMoverState& S = MoverStates.AddDefaulted_GetRef();
		S.Component = *Found;
		const FVector Placed = (*Found)->GetRelativeLocation();
		const FRotator PlacedRot = (*Found)->GetRelativeRotation();
		if (bPlacedPoseIsOpen)
		{
			S.OpenLocation = Placed;
			S.OpenRotation = PlacedRot;
			S.ClosedLocation = Placed - M.OpenLocationOffset;
			S.ClosedRotation = (FQuat(PlacedRot) * FQuat(M.OpenRotationOffset).Inverse()).Rotator();
		}
		else
		{
			S.ClosedLocation = Placed;
			S.ClosedRotation = PlacedRot;
			S.OpenLocation = Placed + M.OpenLocationOffset;
			S.OpenRotation = (FQuat(PlacedRot) * FQuat(M.OpenRotationOffset)).Rotator();
		}
	}
	ApplyVisual(0.f);

	if (HasAuthority())
	{
		Trigger->OnComponentBeginOverlap.AddDynamic(this, &AWLFlagPassage::OnTriggerBegin);
		if (UWLGameEventsSubsystem* Events = UWLGameEventsSubsystem::Get(this))
		{
			EnemyKilledHandle = Events->OnEnemyKilled.AddUObject(this, &AWLFlagPassage::HandleEnemyKilled);
		}
		GetWorldTimerManager().SetTimer(PollTimer, this, &AWLFlagPassage::ServerPoll, 0.5f, true, FMath::FRand() * 0.5f);
	}
	if (GetNetMode() != NM_DedicatedServer)
	{
		FTimerHandle ClientTimer;
		GetWorldTimerManager().SetTimer(ClientTimer, this, &AWLFlagPassage::ClientPoll, 0.25f, true, 0.1f);
	}
}

void AWLFlagPassage::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWLGameEventsSubsystem* Events = UWLGameEventsSubsystem::Get(this))
	{
		Events->OnEnemyKilled.Remove(EnemyKilledHandle);
	}
	GetWorldTimerManager().ClearAllTimersForObject(this);
	Super::EndPlay(EndPlayReason);
}

void AWLFlagPassage::Message(AWLPlayerState* PS, const FText& Text)
{
	if (AWLPlayerController* PC = PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr)
	{
		PC->ClientGameMessage(Text);
	}
}

void AWLFlagPassage::RefreshPawnCollision(APawn* Pawn)
{
	const ACharacter* Char = Cast<ACharacter>(Pawn);
	UCapsuleComponent* Capsule = Char ? Char->GetCapsuleComponent() : nullptr;
	if (!Capsule)
	{
		return;
	}
	const bool bOpen = IsOpenFor(Pawn->GetPlayerState<AWLPlayerState>());
	Capsule->IgnoreComponentWhenMoving(Blocker, bOpen);
}

void AWLFlagPassage::OnTriggerBegin(UPrimitiveComponent* OverlappedComp, AActor* OtherActor, UPrimitiveComponent* OtherComp, int32 OtherBodyIndex, bool bFromSweep, const FHitResult& SweepResult)
{
	APawn* Pawn = Cast<APawn>(OtherActor);
	AWLPlayerState* PS = Pawn ? Pawn->GetPlayerState<AWLPlayerState>() : nullptr;
	if (!PS)
	{
		return;
	}
	OnPlayerEntered(PS, Pawn);
	RefreshPawnCollision(Pawn);
}

void AWLFlagPassage::ServerPoll()
{
	// Keep pass-through in step with flags for everyone nearby (flags can change anywhere: a boss
	// kill, a quest, an admin command), so a player is never blocked by a gate they've opened.
	const AGameStateBase* GS = GetWorld()->GetGameState();
	if (!GS)
	{
		return;
	}
	const FVector Here = GetActorLocation();
	for (APlayerState* PS : GS->PlayerArray)
	{
		APawn* Pawn = PS ? PS->GetPawn() : nullptr;
		if (Pawn && FVector::DistSquared(Pawn->GetActorLocation(), Here) < FMath::Square(5000.f))
		{
			RefreshPawnCollision(Pawn);
		}
	}
}

void AWLFlagPassage::ClientPoll()
{
	const UWorld* World = GetWorld();
	APlayerController* PC = World ? World->GetFirstPlayerController() : nullptr;
	if (!PC || !PC->IsLocalController())
	{
		return;
	}
	APawn* Pawn = PC->GetPawn();
	const AWLPlayerState* PS = PC->GetPlayerState<AWLPlayerState>();
	if (!PS)
	{
		return;
	}
	if (Pawn)
	{
		RefreshPawnCollision(Pawn);
	}

	const bool bOpen = IsOpenFor(PS);
	if (!bClientEvaluated)
	{
		// First look after load: an already-open passage snaps open, never animates.
		bClientEvaluated = true;
		bClientOpen = bOpen;
		ClientAlpha = bOpen ? 1.f : 0.f;
		ApplyVisual(ClientAlpha);
		return;
	}
	if (bOpen != bClientOpen)
	{
		bClientOpen = bOpen;
		GetWorldTimerManager().SetTimer(AnimTimer, this, &AWLFlagPassage::TickAnimation, 1.f / 30.f, true);
	}
}

void AWLFlagPassage::TickAnimation()
{
	const float Step = (1.f / 30.f) / FMath::Max(0.01f, OpenDuration);
	ClientAlpha = FMath::Clamp(ClientAlpha + (bClientOpen ? Step : -Step), 0.f, 1.f);
	ApplyVisual(ClientAlpha);
	if (ClientAlpha <= 0.f || ClientAlpha >= 1.f)
	{
		GetWorldTimerManager().ClearTimer(AnimTimer);
	}
}

void AWLFlagPassage::ApplyVisual(float Alpha)
{
	const float K = FMath::SmoothStep(0.f, 1.f, Alpha);
	for (const FMoverState& S : MoverStates)
	{
		if (USceneComponent* C = S.Component.Get())
		{
			C->SetRelativeLocationAndRotation(FMath::Lerp(S.ClosedLocation, S.OpenLocation, K),
				FQuat::Slerp(FQuat(S.ClosedRotation), FQuat(S.OpenRotation), K));
		}
	}
	if (Barrier && Barrier->GetStaticMesh())
	{
		const bool bPlaceholder = BarrierArtMesh.IsNull();
		Barrier->SetHiddenInGame(Alpha > 0.f || (bPlaceholder && !bShowPlaceholderBarrier));
	}
}

bool AWLFlagPassage::IsRequiredBoss(const AActor* Enemy) const
{
	if (!Enemy)
	{
		return false;
	}
	if (RequiredBoss.Get() == Enemy)
	{
		return true;
	}
	const AWLEnemySpawner* Spawner = RequiredBossSpawner.Get();
	return Spawner && Spawner->DidSpawn(Cast<AWLEnemyCharacter>(Enemy));
}

void AWLFlagPassage::HandleEnemyKilled(AActor* Enemy, AWLPlayerState* Killer)
{
	if (!IsRequiredBoss(Enemy))
	{
		return;
	}
	TSet<AWLPlayerState*> Credited;
	if (Killer)
	{
		Credited.Add(Killer);
	}
	if (const AGameStateBase* GS = GetWorld()->GetGameState())
	{
		for (APlayerState* PS : GS->PlayerArray)
		{
			const APawn* Pawn = PS ? PS->GetPawn() : nullptr;
			if (Pawn && FVector::Dist(Pawn->GetActorLocation(), Enemy->GetActorLocation()) <= BossCreditRadius)
			{
				Credited.Add(Cast<AWLPlayerState>(PS));
			}
		}
	}
	for (AWLPlayerState* PS : Credited)
	{
		if (PS)
		{
			OnBossDefeatedBy(PS);
		}
	}
}
