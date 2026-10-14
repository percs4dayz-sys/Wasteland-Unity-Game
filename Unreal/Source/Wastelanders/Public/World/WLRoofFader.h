#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "WLRoofFader.generated.h"

class UBoxComponent;
class UPrimitiveComponent;

/**
 * RuneScape-style roof hiding (World/RoofFader.cs). Walk into a building and its roof vanishes so
 * the top-down camera can see inside; walk out and it comes back.
 *
 * Setup: place one per building, size Interior to cover the interior floor (tall enough that the
 * capsule always overlaps), and pick the roof actor(s) in Roof / AlsoHide. Every primitive on those
 * actors (and actors attached to them) is hidden.
 *
 * CLIENT-ONLY presentation. It reacts only to the locally controlled pawn, so each player sees their
 * own building's interior. Nothing replicates; on a dedicated server it does nothing. Hidden
 * primitives also stop catching clicks (FWLClickThrough), matching Unity's "invisible and click-
 * through are the same state". Shadows go with the visibility unless bSuppressShadows is off.
 *
 * Fade: FadeDuration > 0 drives the scalar material parameter FadeParameterName (0..1) on dynamic
 * instances; it only shows if the roof material uses it (an opaque material pops, as in Unity).
 * Dropped: keepInFirstPerson. The port has no first-person camera mode.
 */
UCLASS()
class WASTELANDERS_API AWLRoofFader : public AActor
{
	GENERATED_BODY()

public:
	AWLRoofFader();

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;
	virtual void Tick(float DeltaSeconds) override;

	/** The roof. Every primitive on it, and on actors attached under it, is hidden. */
	UPROPERTY(EditInstanceOnly, BlueprintReadOnly, Category = "Wastelanders|Roof")
	TArray<TObjectPtr<AActor>> Roof;

	/** Upper floors, ceiling beams, anything else blocking the view. */
	UPROPERTY(EditInstanceOnly, BlueprintReadOnly, Category = "Wastelanders|Roof")
	TArray<TObjectPtr<AActor>> AlsoHide;

	/** 0 = instant pop (RuneScape-style). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Roof", meta = (ClampMin = "0", ClampMax = "1"))
	float FadeDuration = 0.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Roof")
	FName FadeParameterName = TEXT("Fade");

	/** Stop the roof casting shadows while hidden, otherwise the interior stays dark. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Roof")
	bool bSuppressShadows = true;

	bool IsRoofHidden() const { return bHidden; }

protected:
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UBoxComponent> Interior;

private:
	UFUNCTION()
	void HandleBeginOverlap(UPrimitiveComponent* OverlappedComponent, AActor* OtherActor, UPrimitiveComponent* OtherComp,
		int32 OtherBodyIndex, bool bFromSweep, const FHitResult& SweepResult);

	UFUNCTION()
	void HandleEndOverlap(UPrimitiveComponent* OverlappedComponent, AActor* OtherActor, UPrimitiveComponent* OtherComp, int32 OtherBodyIndex);

	static bool IsLocalPlayer(const AActor* Actor);
	void Collect();
	void Apply();
	void SetRoofVisible(bool bVisible);
	void SetFade(float Alpha);

	TArray<TWeakObjectPtr<UPrimitiveComponent>> Primitives;
	int32 Inside = 0; // overlaps can pair oddly; count instead of a bool
	bool bHidden = false;
	bool bVisibleNow = true;
	float Fade = 1.f;
};
