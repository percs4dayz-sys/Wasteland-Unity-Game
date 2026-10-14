#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Subsystems/WorldSubsystem.h"
#include "WLGeigerCounter.generated.h"

class UWLFissionSourceComponent;

/**
 * Marks an actor as something the Geiger counter senses: a living fission golem to kill, or a golem
 * corpse still worth stripping. Replicated so every client knows the sources around it (Unity read
 * FissionGolem.Active directly). Optional: the subsystem already senses AWLFissionGolem actors and the
 * corpse nodes they own without it. Use this for any other source, or on a golem whose tier is set
 * at runtime (the golem's own Tier is not replicated).
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLFissionSourceComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLFissionSourceComponent();

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;
	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

	/** SERVER. */
	void SetTier(int32 InTier) { Tier = InTier; }
	void SetIsCorpse(bool bInCorpse) { bIsCorpse = bInCorpse; }

	int32 GetTier() const { return Tier; }
	bool IsCorpse() const { return bIsCorpse; }

	/** Off = not sensed (a fully stripped corpse). */
	UPROPERTY(EditAnywhere, Replicated, BlueprintReadWrite, Category = "Wastelanders|Fission")
	bool bActive = true;

protected:
	UPROPERTY(EditAnywhere, Replicated, BlueprintReadOnly, Category = "Wastelanders|Fission", meta = (ClampMin = "1", ClampMax = "5"))
	int32 Tier = 1;

	UPROPERTY(EditAnywhere, Replicated, BlueprintReadOnly, Category = "Wastelanders|Fission")
	bool bIsCorpse = false;
};

/** What the HUD shows (Unity GeigerCounter's IMGUI box, now data for UMG). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLGeigerReading
{
	GENERATED_BODY()

	/** The local player carries or wears a Geiger Counter. */
	UPROPERTY(BlueprintReadOnly, Category = "Geiger") bool bHasCounter = false;
	/** A source is within detection range. False = "...silent. No cores in range." */
	UPROPERTY(BlueprintReadOnly, Category = "Geiger") bool bHasTarget = false;
	UPROPERTY(BlueprintReadOnly, Category = "Geiger") float Distance = 0.f;
	/** Signed yaw from the camera's facing to the source: negative left, positive right. */
	UPROPERTY(BlueprintReadOnly, Category = "Geiger") float BearingDegrees = 0.f;
	UPROPERTY(BlueprintReadOnly, Category = "Geiger") bool bIsCorpse = false;
	UPROPERTY(BlueprintReadOnly, Category = "Geiger") int32 Tier = 0;
	UPROPERTY(BlueprintReadOnly, Category = "Geiger") float ClickHz = 0.f;
	/** 0-10 signal bars. */
	UPROPERTY(BlueprintReadOnly, Category = "Geiger") int32 Bars = 0;
};

DECLARE_MULTICAST_DELEGATE(FWLOnGeigerClick);

/**
 * Client-side Geiger counter (World/GeigerCounter.cs). Every few tenths of a second it locks onto
 * the nearest fission source (a registered UWLFissionSourceComponent, a living AWLFissionGolem, or an
 * undepleted corpse node owned by one) and computes a bearing, distance and click rate that speeds
 * up as you close in. Pure presentation: no RPCs, nothing authoritative. Not created on dedicated
 * servers. UI reads GetReading(); audio binds OnClick.
 */
UCLASS()
class WASTELANDERS_API UWLGeigerCounterSubsystem : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override;

	static UWLGeigerCounterSubsystem* Get(const UObject* WorldContext);

	void RegisterSource(UWLFissionSourceComponent* Source) { Sources.AddUnique(Source); }
	void UnregisterSource(UWLFissionSourceComponent* Source) { Sources.Remove(Source); }

	const FWLGeigerReading& GetReading() const { return Reading; }

	FWLOnGeigerClick OnClick;

	/** Pure (unit tested): click rate for a distance, quadratic in proximity. */
	static float ClickHzFor(float Distance, float DetectRange, float MinHz, float MaxHz);

	/** Pure (unit tested): 0-10 bars. */
	static int32 BarsFor(float Distance, float DetectRange);

private:
	bool LocalPlayerHasCounter() const;
	AActor* FindNearest(const FVector& From) const;

	/** True if Actor is still a live source; fills its tier and corpse state. */
	bool DescribeSource(const AActor* Actor, int32& OutTier, bool& bOutCorpse) const;

	TArray<TWeakObjectPtr<UWLFissionSourceComponent>> Sources;
	TWeakObjectPtr<AActor> Target;
	FWLGeigerReading Reading;
	float ScanTimer = 0.f;
	float ClickTimer = 0.f;
};
