#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "WLAudioEvents.generated.h"

/**
 * One-shot sound hooks by name, ported from Unity Audio/AudioEventPlayer.cs (Play("clipName") from
 * animation events, interaction callbacks and UI). Sounds come from UWLAudioSettings::Events; a name
 * with no sound assigned is a silent no-op (logged once per name, where Unity warned on every call).
 *
 * Cosmetic and local: never plays on a dedicated server. Gameplay never depends on it.
 */
UCLASS()
class WASTELANDERS_API UWLAudioEvents : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/** 2D UI / feedback sound (level-up jingle, clicks). */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Audio", meta = (WorldContext = "WorldContextObject"))
	static void PlayEvent(const UObject* WorldContextObject, FName EventName, float VolumeMultiplier = 1.f);

	/** Positional sound (animation events, world interactions). */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Audio", meta = (WorldContext = "WorldContextObject"))
	static void PlayEventAtLocation(const UObject* WorldContextObject, FName EventName, FVector Location, float VolumeMultiplier = 1.f);

	/** True when a sound is assigned for the event. */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Audio")
	static bool HasEvent(FName EventName);
};
