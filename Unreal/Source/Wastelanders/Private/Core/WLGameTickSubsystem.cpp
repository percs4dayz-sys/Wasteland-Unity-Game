#include "Core/WLGameTickSubsystem.h"
#include "Core/WLGameSettings.h"
#include "Engine/World.h"

void UWLGameTickSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);
	TickDuration = UWLGameSettings::Get()->TickDurationSeconds;
}

bool UWLGameTickSubsystem::ShouldCreateSubsystem(UObject* Outer) const
{
	const UWorld* World = Cast<UWorld>(Outer);
	return World && (World->IsGameWorld());
}

UWLGameTickSubsystem* UWLGameTickSubsystem::Get(const UObject* WorldContextObject)
{
	const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
	return World ? World->GetSubsystem<UWLGameTickSubsystem>() : nullptr;
}

bool UWLGameTickSubsystem::HasAuthority() const
{
	const UWorld* World = GetWorld();
	return World && World->GetNetMode() != NM_Client;
}

bool UWLGameTickSubsystem::IsTickable() const
{
	return !IsTemplate() && GetWorld() && GetWorld()->IsGameWorld();
}

TStatId UWLGameTickSubsystem::GetStatId() const
{
	RETURN_QUICK_DECLARE_CYCLE_STAT(UWLGameTickSubsystem, STATGROUP_Tickables);
}

void UWLGameTickSubsystem::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);

	Accumulator += DeltaTime;
	while (Accumulator >= TickDuration)
	{
		Accumulator -= TickDuration;
		++TickCount;

		// Clients keep a local estimate between server syncs but never run gameplay ticks.
		if (!HasAuthority())
		{
			continue;
		}

		// Reverse order so a tickable may unregister itself while being ticked.
		for (int32 i = Tickables.Num() - 1; i >= 0; --i)
		{
			if (i >= Tickables.Num())
			{
				continue;
			}
			if (IWLTickable* Tickable = Tickables[i].GetInterface(); Tickable && IsValid(Tickables[i].GetObject()))
			{
				Tickable->OnGameTick(TickCount);
			}
			else
			{
				Tickables.RemoveAt(i);
			}
		}
		OnGameTick.Broadcast(TickCount);
	}
}

void UWLGameTickSubsystem::Register(TScriptInterface<IWLTickable> Tickable)
{
	if (Tickable.GetObject())
	{
		Tickables.AddUnique(Tickable);
	}
}

void UWLGameTickSubsystem::Unregister(TScriptInterface<IWLTickable> Tickable)
{
	Tickables.Remove(Tickable);
}

void UWLGameTickSubsystem::SyncFromServer(int64 ServerTickCount)
{
	if (!HasAuthority())
	{
		TickCount = ServerTickCount;
		Accumulator = 0.f;
	}
}
