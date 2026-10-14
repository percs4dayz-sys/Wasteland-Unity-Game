#include "Core/WLGameState.h"
#include "Core/WLGameTickSubsystem.h"
#include "Net/UnrealNetwork.h"

AWLGameState::AWLGameState()
{
}

void AWLGameState::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(AWLGameState, ServerTick);
}

void AWLGameState::BeginPlay()
{
	Super::BeginPlay();
	if (HasAuthority())
	{
		if (UWLGameTickSubsystem* Ticks = UWLGameTickSubsystem::Get(this))
		{
			Ticks->OnGameTick.AddUObject(this, &AWLGameState::HandleGameTick);
		}
	}
}

void AWLGameState::HandleGameTick(int64 TickCount)
{
	if (SyncEveryNTicks <= 1 || TickCount % SyncEveryNTicks == 0)
	{
		ServerTick = TickCount;
	}
}

void AWLGameState::OnRep_ServerTick()
{
	if (UWLGameTickSubsystem* Ticks = UWLGameTickSubsystem::Get(this))
	{
		Ticks->SyncFromServer(ServerTick);
	}
}
