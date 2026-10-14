#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameStateBase.h"
#include "WLGameState.generated.h"

/** Replicates the server game-tick count so clients can line up tick-based visuals (XP drops, gather swings). */
UCLASS()
class WASTELANDERS_API AWLGameState : public AGameStateBase
{
	GENERATED_BODY()

public:
	AWLGameState();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void BeginPlay() override;

	/** How many ticks between tick-count syncs (the client extrapolates in between). */
	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders")
	int32 SyncEveryNTicks = 50;

protected:
	UFUNCTION()
	void OnRep_ServerTick();

	void HandleGameTick(int64 TickCount);

	UPROPERTY(ReplicatedUsing = OnRep_ServerTick)
	int64 ServerTick = 0;
};
