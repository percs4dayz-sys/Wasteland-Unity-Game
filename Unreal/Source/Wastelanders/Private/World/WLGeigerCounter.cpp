#include "World/WLGeigerCounter.h"
#include "World/WLWorldSettings.h"
#include "Player/WLPlayerState.h"
#include "Items/WLInventoryComponent.h"
#include "Items/WLEquipmentComponent.h"
#include "Combat/WLFissionGolem.h"
#include "World/WLResourceNode.h"
#include "EngineUtils.h"
#include "GameFramework/PlayerController.h"
#include "Camera/PlayerCameraManager.h"
#include "Net/UnrealNetwork.h"
#include "Engine/World.h"

// --- source -------------------------------------------------------------------------------------

UWLFissionSourceComponent::UWLFissionSourceComponent()
{
	PrimaryComponentTick.bCanEverTick = false;
	SetIsReplicatedByDefault(true);
}

void UWLFissionSourceComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(UWLFissionSourceComponent, Tier);
	DOREPLIFETIME(UWLFissionSourceComponent, bIsCorpse);
	DOREPLIFETIME(UWLFissionSourceComponent, bActive);
}

void UWLFissionSourceComponent::BeginPlay()
{
	Super::BeginPlay();
	if (UWLGeigerCounterSubsystem* Geiger = UWLGeigerCounterSubsystem::Get(this))
	{
		Geiger->RegisterSource(this);
	}
}

void UWLFissionSourceComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWLGeigerCounterSubsystem* Geiger = UWLGeigerCounterSubsystem::Get(this))
	{
		Geiger->UnregisterSource(this);
	}
	Super::EndPlay(EndPlayReason);
}

// --- subsystem ----------------------------------------------------------------------------------

bool UWLGeigerCounterSubsystem::ShouldCreateSubsystem(UObject* Outer) const
{
	const UWorld* World = Cast<UWorld>(Outer);
	return World && World->IsGameWorld() && !IsRunningDedicatedServer();
}

UWLGeigerCounterSubsystem* UWLGeigerCounterSubsystem::Get(const UObject* WorldContext)
{
	const UWorld* World = WorldContext ? WorldContext->GetWorld() : nullptr;
	return World ? World->GetSubsystem<UWLGeigerCounterSubsystem>() : nullptr;
}

TStatId UWLGeigerCounterSubsystem::GetStatId() const
{
	RETURN_QUICK_DECLARE_CYCLE_STAT(UWLGeigerCounterSubsystem, STATGROUP_Tickables);
}

float UWLGeigerCounterSubsystem::ClickHzFor(float Distance, float DetectRange, float MinHz, float MaxHz)
{
	const float Prox = 1.f - FMath::Clamp(Distance / FMath::Max(1.f, DetectRange), 0.f, 1.f);
	return FMath::Lerp(MinHz, MaxHz, Prox * Prox);
}

int32 UWLGeigerCounterSubsystem::BarsFor(float Distance, float DetectRange)
{
	return FMath::RoundToInt(FMath::Clamp(1.f - Distance / FMath::Max(1.f, DetectRange), 0.f, 1.f) * 10.f);
}

bool UWLGeigerCounterSubsystem::LocalPlayerHasCounter() const
{
	const APlayerController* PC = GetWorld()->GetFirstPlayerController();
	const AWLPlayerState* PS = PC ? PC->GetPlayerState<AWLPlayerState>() : nullptr;
	if (!PS)
	{
		return false;
	}
	// Inventory and equipment replicate to the owner, so the client can answer this itself.
	const int32 GeigerId = UWLWorldSettings::Get()->GeigerItemId;
	const UWLInventoryComponent* Inv = PS->GetInventory();
	const UWLEquipmentComponent* Equip = PS->GetEquipment();
	return (Inv && Inv->Contains(GeigerId, 1)) || (Equip && Equip->GetItemId(EWLEquipSlot::Tool) == GeigerId);
}

bool UWLGeigerCounterSubsystem::DescribeSource(const AActor* Actor, int32& OutTier, bool& bOutCorpse) const
{
	if (!IsValid(Actor) || Actor->IsActorBeingDestroyed())
	{
		return false;
	}
	if (const UWLFissionSourceComponent* Comp = Actor->FindComponentByClass<UWLFissionSourceComponent>())
	{
		OutTier = Comp->GetTier();
		bOutCorpse = Comp->IsCorpse();
		return Comp->bActive;
	}
	// Living golem: something to kill.
	if (const AWLFissionGolem* Golem = Cast<AWLFissionGolem>(Actor))
	{
		OutTier = Golem->GetGolemTier();
		bOutCorpse = false;
		return !Golem->IsDead();
	}
	// The corpse node a dead golem spawns (owner replicates): something to strip until depleted.
	if (const AWLResourceNode* Node = Cast<AWLResourceNode>(Actor))
	{
		const AWLFissionGolem* Golem = Cast<AWLFissionGolem>(Node->GetOwner());
		if (!Golem)
		{
			return false;
		}
		OutTier = Golem->GetGolemTier();
		bOutCorpse = true;
		return !Node->IsDepleted();
	}
	return false;
}

AActor* UWLGeigerCounterSubsystem::FindNearest(const FVector& From) const
{
	const float Range = UWLWorldSettings::Get()->GeigerDetectRange;
	AActor* Best = nullptr;
	float BestSq = Range * Range;
	int32 Tier = 0;
	bool bCorpse = false;

	auto Consider = [&](AActor* Actor)
	{
		if (!DescribeSource(Actor, Tier, bCorpse))
		{
			return;
		}
		const float Sq = FVector::DistSquared(Actor->GetActorLocation(), From);
		if (Sq < BestSq)
		{
			BestSq = Sq;
			Best = Actor;
		}
	};

	for (const TWeakObjectPtr<UWLFissionSourceComponent>& Weak : Sources)
	{
		if (UWLFissionSourceComponent* S = Weak.Get())
		{
			Consider(S->GetOwner());
		}
	}
	// Every 0.4 s, over a handful of golems: cheap enough to iterate rather than keep a registry.
	for (TActorIterator<AWLFissionGolem> It(GetWorld()); It; ++It)
	{
		Consider(*It);
	}
	for (TActorIterator<AWLResourceNode> It(GetWorld()); It; ++It)
	{
		if (Cast<AWLFissionGolem>(It->GetOwner()))
		{
			Consider(*It);
		}
	}
	return Best;
}

void UWLGeigerCounterSubsystem::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);

	const UWLWorldSettings* Settings = UWLWorldSettings::Get();
	APlayerController* PC = GetWorld()->GetFirstPlayerController();
	const APawn* Pawn = PC ? PC->GetPawn() : nullptr;

	Reading = FWLGeigerReading();
	Reading.bHasCounter = Pawn && LocalPlayerHasCounter();
	if (!Reading.bHasCounter)
	{
		Target.Reset(); // only works while the geiger is on you
		return;
	}

	ScanTimer -= DeltaTime;
	if (ScanTimer <= 0.f)
	{
		ScanTimer = Settings->GeigerScanInterval;
		Target = FindNearest(Pawn->GetActorLocation());
	}
	const AActor* Source = Target.Get();
	int32 Tier = 0;
	bool bCorpse = false;
	if (!DescribeSource(Source, Tier, bCorpse))
	{
		Target.Reset();
		return;
	}

	const FVector To = Source->GetActorLocation() - Pawn->GetActorLocation();
	Reading.bHasTarget = true;
	Reading.Distance = To.Size();
	Reading.bIsCorpse = bCorpse;
	Reading.Tier = Tier;
	Reading.ClickHz = ClickHzFor(Reading.Distance, Settings->GeigerDetectRange, Settings->GeigerMinClickHz, Settings->GeigerMaxClickHz);
	Reading.Bars = BarsFor(Reading.Distance, Settings->GeigerDetectRange);

	// Bearing relative to where the camera faces, so left/right/ahead reads on screen.
	const float CamYaw = PC->PlayerCameraManager ? PC->PlayerCameraManager->GetCameraRotation().Yaw : PC->GetControlRotation().Yaw;
	Reading.BearingDegrees = FRotator::NormalizeAxis(To.Rotation().Yaw - CamYaw);

	// Faster clicks the closer you are; audio binds OnClick.
	ClickTimer -= DeltaTime;
	if (ClickTimer <= 0.f)
	{
		ClickTimer = 1.f / FMath::Max(Reading.ClickHz, 0.01f);
		OnClick.Broadcast();
	}
}
