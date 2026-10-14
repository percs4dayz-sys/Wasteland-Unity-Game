#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "UObject/Interface.h"
#include "WLGameTickSubsystem.generated.h"

UINTERFACE(MinimalAPI, meta = (CannotImplementInterfaceInBlueprint))
class UWLTickable : public UInterface
{
	GENERATED_BODY()
};

/** Anything that advances on the 0.6 s game tick (skilling actions, regen, timers). Ported from ITickable. */
class WASTELANDERS_API IWLTickable
{
	GENERATED_BODY()

public:
	virtual void OnGameTick(int64 TickCount) = 0;
};

DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnGameTick, int64 /*TickCount*/);

/**
 * The fixed-cadence game tick (Unity GameTick.cs). Runs on the server (and in standalone);
 * clients only mirror the tick count replicated through AWLGameState for UI interpolation.
 *
 * Gameplay that must be deterministic (gather rolls, crafting cadence, attack cooldowns measured
 * in ticks) hangs off this rather than frame Tick, exactly like the Unity version.
 */
UCLASS()
class WASTELANDERS_API UWLGameTickSubsystem : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	virtual void Initialize(FSubsystemCollectionBase& Collection) override;
	virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override;
	virtual bool IsTickable() const override;

	static UWLGameTickSubsystem* Get(const UObject* WorldContextObject);

	void Register(TScriptInterface<IWLTickable> Tickable);
	void Unregister(TScriptInterface<IWLTickable> Tickable);

	/** Fires after every registered IWLTickable has run for the tick. */
	FWLOnGameTick OnGameTick;

	/** Ticks elapsed since the world started (server authoritative). */
	int64 GetTickCount() const { return TickCount; }

	/** 0-1 fraction of the way to the next tick, for smoothing visuals. */
	float GetInterpolation() const { return TickDuration > 0.f ? Accumulator / TickDuration : 0.f; }

	float GetTickDuration() const { return TickDuration; }

	/** Clients: adopt the server's tick count (called from AWLGameState's OnRep). */
	void SyncFromServer(int64 ServerTickCount);

private:
	bool HasAuthority() const;

	float TickDuration = 0.6f;
	float Accumulator = 0.f;
	int64 TickCount = 0;

	UPROPERTY(Transient)
	TArray<TScriptInterface<IWLTickable>> Tickables;
};
