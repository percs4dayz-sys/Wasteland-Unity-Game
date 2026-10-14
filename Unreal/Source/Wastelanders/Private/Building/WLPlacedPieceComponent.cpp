#include "Building/WLPlacedPieceComponent.h"
#include "Player/WLPlayerState.h"
#include "EngineUtils.h"
#include "Engine/World.h"
#include "TimerManager.h"

UWLPlacedPieceComponent::UWLPlacedPieceComponent()
{
	PrimaryComponentTick.bCanEverTick = false;
}

void UWLPlacedPieceComponent::SetBuilder(const AWLPlayerState* PS)
{
	Builder = PS;
	bHasBuilder = PS != nullptr;
	BuilderId = PS ? PS->GetCharacterId() : FGuid();

	if (bHasBuilder && GetWorld() && GetOwner() && GetOwner()->HasAuthority())
	{
		GetWorld()->GetTimerManager().SetTimer(OrphanTimer, this, &UWLPlacedPieceComponent::CheckOrphaned, OrphanCheckInterval, true);
	}
}

void UWLPlacedPieceComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWorld* World = GetWorld())
	{
		World->GetTimerManager().ClearTimer(OrphanTimer);
	}
	Super::EndPlay(EndPlayReason);
}

void UWLPlacedPieceComponent::CheckOrphaned()
{
	// Builder gone (logout destroys their PlayerState): remove the piece, as it can't be saved yet.
	if (!Builder.IsValid() && GetOwner())
	{
		GetOwner()->Destroy();
	}
}

int32 UWLPlacedPieceComponent::CountOwnedBy(UWorld* World, const AWLPlayerState* PS)
{
	int32 Count = 0;
	if (!World || !PS)
	{
		return 0;
	}
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		const UWLPlacedPieceComponent* Piece = It->FindComponentByClass<UWLPlacedPieceComponent>();
		if (Piece && Piece->bHasBuilder && Piece->IsOwnedBy(PS))
		{
			++Count;
		}
	}
	return Count;
}

bool UWLPlacedPieceComponent::IsOwnedBy(const AWLPlayerState* PS) const
{
	if (!bHasBuilder)
	{
		return true;
	}
	if (!PS)
	{
		return false;
	}
	// Match by character id so a reconnecting player (new PlayerState) still owns their piece.
	return Builder.Get() == PS || (BuilderId.IsValid() && BuilderId == PS->GetCharacterId());
}
