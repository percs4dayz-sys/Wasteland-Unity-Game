#include "World/WLOccluderSubsystem.h"
#include "World/WLWorldSettings.h"
#include "Components/StaticMeshComponent.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/PlayerController.h"
#include "Engine/World.h"
#include "EngineUtils.h"

// --- click-through ------------------------------------------------------------------------------

namespace WLClickThrough
{
	struct FState
	{
		int32 Count = 0;
		TEnumAsByte<ECollisionResponse> Original = ECR_Block;
	};
	static TMap<TWeakObjectPtr<UPrimitiveComponent>, FState>& States()
	{
		static TMap<TWeakObjectPtr<UPrimitiveComponent>, FState> Map;
		return Map;
	}
}

void FWLClickThrough::Push(UPrimitiveComponent* Component)
{
	if (!IsValid(Component))
	{
		return;
	}
	WLClickThrough::FState& State = WLClickThrough::States().FindOrAdd(TWeakObjectPtr<UPrimitiveComponent>(Component));
	if (State.Count++ == 0)
	{
		State.Original = Component->GetCollisionResponseToChannel(ECC_Visibility);
		Component->SetCollisionResponseToChannel(ECC_Visibility, ECR_Ignore);
	}
}

void FWLClickThrough::Pop(UPrimitiveComponent* Component)
{
	TMap<TWeakObjectPtr<UPrimitiveComponent>, WLClickThrough::FState>& Map = WLClickThrough::States();
	const TWeakObjectPtr<UPrimitiveComponent> Key(Component);
	WLClickThrough::FState* State = Map.Find(Key);
	if (!State)
	{
		return;
	}
	if (--State->Count <= 0)
	{
		if (IsValid(Component))
		{
			Component->SetCollisionResponseToChannel(ECC_Visibility, State->Original);
		}
		Map.Remove(Key);
	}
	// Opportunistic cleanup of components destroyed while hidden.
	for (auto It = Map.CreateIterator(); It; ++It)
	{
		if (!It.Key().IsValid())
		{
			It.RemoveCurrent();
		}
	}
}

// --- subsystem ----------------------------------------------------------------------------------

const FName UWLOccluderSubsystem::KeepVisibleTag(TEXT("KeepVisibleOverhead"));

bool UWLOccluderSubsystem::ShouldCreateSubsystem(UObject* Outer) const
{
	const UWorld* World = Cast<UWorld>(Outer);
	return World && World->IsGameWorld() && !IsRunningDedicatedServer() && UWLWorldSettings::Get()->bHideOverheadOccluders;
}

void UWLOccluderSubsystem::Deinitialize()
{
	RestoreAll(); // never leave holes in the world or click channels switched off
	Super::Deinitialize();
}

TStatId UWLOccluderSubsystem::GetStatId() const
{
	RETURN_QUICK_DECLARE_CYCLE_STAT(UWLOccluderSubsystem, STATGROUP_Tickables);
}

bool UWLOccluderSubsystem::IsOverhead(const FBox& Bounds, const FVector& Feet, float Headroom, float Radius)
{
	if (!Bounds.IsValid || Bounds.Min.Z <= Feet.Z + Headroom)
	{
		return false; // starts at or below head height: a wall, pillar or doorframe
	}
	const float DX = FMath::Max(0.f, FMath::Max(Bounds.Min.X - Feet.X, Feet.X - Bounds.Max.X));
	const float DY = FMath::Max(0.f, FMath::Max(Bounds.Min.Y - Feet.Y, Feet.Y - Bounds.Max.Y));
	return DX * DX + DY * DY <= Radius * Radius;
}

void UWLOccluderSubsystem::RebuildCache(const APawn* Player)
{
	Cache.Reset();
	const float Radius = UWLWorldSettings::Get()->OccluderCacheRadius;
	const FVector From = Player->GetActorLocation();
	const FBox Near = FBox(From - FVector(Radius), From + FVector(Radius));

	for (TActorIterator<AActor> It(GetWorld()); It; ++It)
	{
		const AActor* Actor = *It;
		if (Actor == Player || Actor->IsAttachedTo(Player) || Actor->GetOwner() == Player || Actor->ActorHasTag(KeepVisibleTag))
		{
			continue;
		}
		TInlineComponentArray<UStaticMeshComponent*> Meshes(Actor);
		for (UStaticMeshComponent* Mesh : Meshes)
		{
			if (!Mesh->IsRegistered() || Mesh->ComponentHasTag(KeepVisibleTag) || !Mesh->Bounds.GetBox().Intersect(Near))
			{
				continue;
			}
			// Something hidden by design (not by us) must stay hidden after we restore.
			if (Mesh->bHiddenInGame && !Hidden.Contains(TWeakObjectPtr<UStaticMeshComponent>(Mesh)))
			{
				continue;
			}
			Cache.Add(Mesh);
		}
	}
}

void UWLOccluderSubsystem::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);

	const UWLWorldSettings* Settings = UWLWorldSettings::Get();
	const APlayerController* PC = GetWorld()->GetFirstPlayerController();
	const APawn* Player = PC ? PC->GetPawn() : nullptr;
	if (!Player)
	{
		RestoreAll();
		return;
	}

	CacheTimer -= DeltaTime;
	if (CacheTimer <= 0.f)
	{
		CacheTimer = FMath::Max(0.5f, Settings->OccluderCacheInterval);
		RebuildCache(Player);
	}

	ScanTimer -= DeltaTime;
	if (ScanTimer > 0.f)
	{
		return;
	}
	ScanTimer = FMath::Max(0.f, Settings->OccluderScanInterval);

	const FVector Feet = Player->GetActorLocation() - FVector(0.f, 0.f, Player->GetSimpleCollisionHalfHeight());

	TSet<TWeakObjectPtr<UStaticMeshComponent>> Current;
	for (const TWeakObjectPtr<UStaticMeshComponent>& Weak : Cache)
	{
		const UStaticMeshComponent* Mesh = Weak.Get();
		if (Mesh && IsOverhead(Mesh->Bounds.GetBox(), Feet, Settings->OverheadHeadroom, Settings->OverheadRadius))
		{
			Current.Add(Weak);
		}
	}

	// Reveal what stopped qualifying.
	for (auto It = Hidden.CreateIterator(); It; ++It)
	{
		if (!Current.Contains(*It))
		{
			if (UStaticMeshComponent* Mesh = It->Get())
			{
				Mesh->SetHiddenInGame(false);
				FWLClickThrough::Pop(Mesh);
			}
			It.RemoveCurrent();
		}
	}
	// Hide what newly qualifies; invisible must also be click-through.
	for (const TWeakObjectPtr<UStaticMeshComponent>& Weak : Current)
	{
		if (!Hidden.Contains(Weak))
		{
			UStaticMeshComponent* Mesh = Weak.Get();
			Mesh->SetHiddenInGame(true);
			FWLClickThrough::Push(Mesh);
			Hidden.Add(Weak);
		}
	}
}

void UWLOccluderSubsystem::RestoreAll()
{
	for (const TWeakObjectPtr<UStaticMeshComponent>& Weak : Hidden)
	{
		if (UStaticMeshComponent* Mesh = Weak.Get())
		{
			Mesh->SetHiddenInGame(false);
			FWLClickThrough::Pop(Mesh);
		}
	}
	Hidden.Reset();
}
