#include "Items/WLGroundItem.h"
#include "Items/WLInventoryComponent.h"
#include "Data/WLDataSubsystem.h"
#include "Core/WLGameTickSubsystem.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerCharacter.h"
#include "Components/SphereComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "TimerManager.h"
#include "UObject/ConstructorHelpers.h"
#include "Engine/GameInstance.h"
#include "Net/UnrealNetwork.h"

#define LOCTEXT_NAMESPACE "WLGroundItem"

static const FName GroundItem_VerbTake(TEXT("Take"));

AWLGroundItem::AWLGroundItem()
{
	bReplicates = true;
	SetReplicatingMovement(false); // static once spawned
	PrimaryActorTick.bCanEverTick = true;

	Collision = CreateDefaultSubobject<USphereComponent>(TEXT("Collision"));
	Collision->InitSphereRadius(50.f);
	// Clickable (Visibility traces) and overlap-detectable, never blocks movement.
	Collision->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
	Collision->SetCollisionResponseToAllChannels(ECR_Overlap);
	Collision->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);
	Collision->SetCollisionResponseToChannel(ECC_Camera, ECR_Ignore);
	Collision->SetGenerateOverlapEvents(false); // radius queries go through UWLGroundItemSubsystem
	SetRootComponent(Collision);

	Mesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Mesh"));
	Mesh->SetupAttachment(Collision);
	Mesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Mesh->SetCastShadow(false);

	// Placeholder art: a small engine cube. No Unity models are used.
	static ConstructorHelpers::FObjectFinder<UStaticMesh> Cube(TEXT("/Engine/BasicShapes/Cube.Cube"));
	if (Cube.Succeeded())
	{
		Mesh->SetStaticMesh(Cube.Object);
	}
	Mesh->SetRelativeScale3D(FVector(PlaceholderScale));
}

AWLGroundItem* AWLGroundItem::Spawn(UWorld* World, const FWLItemStack& InStack, const FVector& Location, AWLPlayerState* Owner)
{
	if (!World || World->GetNetMode() == NM_Client || InStack.IsEmpty())
	{
		return nullptr;
	}
	const UWLDataSubsystem* Data = World->GetGameInstance() ? World->GetGameInstance()->GetSubsystem<UWLDataSubsystem>() : nullptr;
	if (!Data || !Data->FindItem(InStack.ItemId))
	{
		return nullptr;
	}

	const AWLGroundItem* Defaults = GetDefault<AWLGroundItem>();
	const FVector2D Scatter = FVector2D(FMath::FRandRange(-1.f, 1.f), FMath::FRandRange(-1.f, 1.f)) * Defaults->ScatterRadius;
	const FTransform Transform(FRotator::ZeroRotator, Location + FVector(Scatter.X, Scatter.Y, 0.f));

	AWLGroundItem* Item = World->SpawnActorDeferred<AWLGroundItem>(AWLGroundItem::StaticClass(), Transform, nullptr, nullptr, ESpawnActorCollisionHandlingMethod::AlwaysSpawn);
	if (!Item)
	{
		return nullptr;
	}
	Item->Stack = InStack;
	Item->Stack.Quantity = FMath::Max(1, InStack.Quantity);
	Item->OwnerPlayer = Owner;
	Item->bPublic = (Owner == nullptr);
	Item->FinishSpawning(Transform);
	return Item;
}

void AWLGroundItem::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(AWLGroundItem, Stack);
	DOREPLIFETIME(AWLGroundItem, bGrabbable);
}

void AWLGroundItem::BeginPlay()
{
	Super::BeginPlay();

	if (UWLGroundItemSubsystem* Registry = UWLGroundItemSubsystem::Get(this))
	{
		Registry->Register(this);
	}

	if (HasAuthority())
	{
		// Land exactly one game tick after spawning (OSRS-style); time fallback without a ticker.
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			LandTick = Tick->GetTickCount() + 1;
			TickHandle = Tick->OnGameTick.AddUObject(this, &AWLGroundItem::HandleGameTick);
		}
		else
		{
			GetWorldTimerManager().SetTimer(LandFallbackTimer, this, &AWLGroundItem::Land, 0.6f, false);
		}
		if (!bPublic)
		{
			GetWorldTimerManager().SetTimer(PublicTimer, this, &AWLGroundItem::GoPublic, FMath::Max(0.1f, PrivateSeconds), false);
		}
	}

	if (GetNetMode() == NM_DedicatedServer)
	{
		SetActorTickEnabled(false); // visuals only
	}
	RefreshVisual();
}

void AWLGroundItem::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWLGroundItemSubsystem* Registry = UWLGroundItemSubsystem::Get(this))
	{
		Registry->Unregister(this);
	}
	if (TickHandle.IsValid())
	{
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			Tick->OnGameTick.Remove(TickHandle);
		}
		TickHandle.Reset();
	}
	Super::EndPlay(EndPlayReason);
}

void AWLGroundItem::HandleGameTick(int64 TickCount)
{
	if (!bGrabbable && TickCount >= LandTick)
	{
		Land();
	}
}

void AWLGroundItem::Land()
{
	if (bGrabbable)
	{
		return;
	}
	bGrabbable = true;
	if (TickHandle.IsValid())
	{
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			Tick->OnGameTick.Remove(TickHandle);
		}
		TickHandle.Reset();
	}
	// Lifetime starts on landing.
	GetWorldTimerManager().SetTimer(DespawnTimer, this, &AWLGroundItem::Despawn, FMath::Max(1.f, LifeSeconds), false);
	ForceNetUpdate();
}

void AWLGroundItem::GoPublic()
{
	bPublic = true;
	ForceNetUpdate(); // now relevant to everyone nearby
}

void AWLGroundItem::Despawn()
{
	Destroy();
}

bool AWLGroundItem::IsAvailableTo(const AWLPlayerState* Player) const
{
	if (!Player)
	{
		return false;
	}
	// Owner gone (logged out) or window elapsed: public.
	const AWLPlayerState* OwnerPS = OwnerPlayer.Get();
	return bPublic || !OwnerPS || OwnerPS == Player;
}

bool AWLGroundItem::IsNetRelevantFor(const AActor* RealViewer, const AActor* ViewTarget, const FVector& SrcLocation) const
{
	if (!bPublic && OwnerPlayer.IsValid())
	{
		// Private loot exists only for its owner's client.
		const APlayerController* PC = Cast<APlayerController>(RealViewer);
		const AWLPlayerState* ViewerPS = PC ? PC->GetPlayerState<AWLPlayerState>() : nullptr;
		if (ViewerPS != OwnerPlayer.Get())
		{
			return false;
		}
	}
	return Super::IsNetRelevantFor(RealViewer, ViewTarget, SrcLocation);
}

bool AWLGroundItem::TryPickUp(AWLPlayerState* Picker, bool bSilentFull)
{
	if (!HasAuthority() || !bGrabbable || IsActorBeingDestroyed() || !IsAvailableTo(Picker))
	{
		return false;
	}
	UWLInventoryComponent* Inv = Picker->GetInventory();
	AWLPlayerController* PC = Cast<AWLPlayerController>(Picker->GetOwningController());
	if (Inv && Inv->GiveItem(Stack, TEXT("Loot")))
	{
		if (PC)
		{
			const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
			const FText Name = Data ? Data->GetItemName(Stack.ItemId) : FText::GetEmpty();
			PC->ClientGameMessage(Stack.Quantity > 1
				? FText::Format(LOCTEXT("PickUpN", "[LOOT]: You pick up {0}x {1}."), Stack.Quantity, Name)
				: FText::Format(LOCTEXT("PickUp", "[LOOT]: You pick up {0}."), Name));
		}
		bGrabbable = false; // no double pickup in the same frame
		Destroy();
		return true;
	}
	if (!bSilentFull && PC)
	{
		PC->ClientGameMessage(LOCTEXT("PackFull", "[LOOT]: Your pack is full - make room to grab that."));
	}
	return false;
}

// --- IWLInteractable ---------------------------------------------------------------------------

FText AWLGroundItem::GetInteractName_Implementation() const
{
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	return Data ? Data->GetItemName(Stack.ItemId) : LOCTEXT("Item", "item");
}

FText AWLGroundItem::GetExamineText_Implementation() const
{
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	const FWLItemRow* Row = Data ? Data->FindItem(Stack.ItemId) : nullptr;
	return (Row && !Row->Description.IsEmpty()) ? Row->Description : LOCTEXT("Examine", "Something lying on the ground.");
}

TArray<FWLInteractOption> AWLGroundItem::GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const
{
	FWLInteractOption Take;
	Take.Verb = GroundItem_VerbTake;
	Take.Label = FText::Format(LOCTEXT("TakeLabel", "Take {0}"), GetInteractName_Implementation());
	Take.bDefault = true;
	return { Take };
}

float AWLGroundItem::GetInteractRange_Implementation() const
{
	return PickupRange;
}

bool AWLGroundItem::Interact_Implementation(AWLPlayerCharacter* Player, FName Verb)
{
	if (!HasAuthority() || !Player || Verb != GroundItem_VerbTake)
	{
		return false;
	}
	// Re-check range here too: the interactor walks into range, but never trust it blindly.
	if (FVector::DistSquared(Player->GetActorLocation(), GetActorLocation()) > FMath::Square(PickupRange * 1.5f))
	{
		return false;
	}
	return TryPickUp(Player->GetWLPlayerState());
}

// --- visuals (clients) ------------------------------------------------------------------------

void AWLGroundItem::OnRep_Stack()
{
	RefreshVisual();
}

void AWLGroundItem::RefreshVisual()
{
	if (GetNetMode() == NM_DedicatedServer)
	{
		return;
	}
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	const FWLItemRow* Row = Data ? Data->FindItem(Stack.ItemId) : nullptr;
	if (Row && !Row->HeldMesh.IsNull())
	{
		// New-art item mesh when one exists (small, synchronous load; ground items are rare).
		if (UStaticMesh* Art = Row->HeldMesh.LoadSynchronous())
		{
			Mesh->SetStaticMesh(Art);
			Mesh->SetRelativeScale3D(FVector::OneVector);
		}
	}
}

void AWLGroundItem::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);

	// Fall in toward the resting height, then bob and spin so loot reads clearly on the ground.
	VisualTime += DeltaSeconds;
	VisualDrop = FMath::FInterpConstantTo(VisualDrop, 0.f, DeltaSeconds, 400.f);
	const float Bob = bGrabbable ? FMath::Sin(VisualTime * 2.f) * 8.f : 0.f;
	Mesh->SetRelativeLocation(FVector(0.f, 0.f, 35.f + VisualDrop + Bob));
	if (bGrabbable)
	{
		Mesh->AddRelativeRotation(FRotator(0.f, 90.f * DeltaSeconds, 0.f));
	}
}

// --- registry ---------------------------------------------------------------------------------

UWLGroundItemSubsystem* UWLGroundItemSubsystem::Get(const UObject* WorldContextObject)
{
	const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
	return World ? World->GetSubsystem<UWLGroundItemSubsystem>() : nullptr;
}

TArray<AWLGroundItem*> UWLGroundItemSubsystem::FindInRadius(const FVector& Center, float Radius) const
{
	TArray<AWLGroundItem*> Result;
	const float RadiusSq = FMath::Square(Radius);
	for (const TWeakObjectPtr<AWLGroundItem>& Weak : Items)
	{
		AWLGroundItem* Item = Weak.Get();
		if (Item && FVector::DistSquared(Item->GetActorLocation(), Center) <= RadiusSq)
		{
			Result.Add(Item);
		}
	}
	return Result;
}

#undef LOCTEXT_NAMESPACE
