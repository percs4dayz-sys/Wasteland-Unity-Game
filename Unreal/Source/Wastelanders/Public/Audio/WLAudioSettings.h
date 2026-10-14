#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"
#include "WLAudioSettings.generated.h"

class USoundBase;
class USoundMix;
class USoundClass;

/** One jukebox track. Unity loaded every clip in Resources/Music; here the list is explicit data. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLMusicTrack
{
	GENERATED_BODY()

	/** Stable name: shown in the music panel and stored in the player's disabled-track list. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Music")
	FName Name;

	/** New-art-direction music. Empty until composed; empty tracks are skipped. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Music")
	TSoftObjectPtr<USoundBase> Sound;
};

/**
 * Audio data (Project Settings > Game > Wastelanders Audio). No Unity audio is ported: every slot starts
 * empty and all audio code is a silent no-op until sounds are assigned.
 */
UCLASS(Config = Game, DefaultConfig, meta = (DisplayName = "Wastelanders Audio"))
class WASTELANDERS_API UWLAudioSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	virtual FName GetCategoryName() const override { return TEXT("Game"); }

	static const UWLAudioSettings* Get() { return GetDefault<UWLAudioSettings>(); }

	/** The jukebox (Unity MusicManager). Listed alphabetically in the music panel, played shuffled. */
	UPROPERTY(Config, EditAnywhere, Category = "Music")
	TArray<FWLMusicTrack> MusicTracks;

	/** Gap between one track ending and the next starting (Unity waited 0.5 s). */
	UPROPERTY(Config, EditAnywhere, Category = "Music", meta = (ClampMin = "0"))
	float GapBetweenTracks = 0.5f;

	/**
	 * One-shot sounds by event name (Unity AudioEventPlayer: Resources/SFX clips played by name).
	 * Names used by code: "LevelUp", "UIClick", "UIOpen", "UIClose", "Equip", "Drop", "Error".
	 */
	UPROPERTY(Config, EditAnywhere, Category = "Events")
	TMap<FName, TSoftObjectPtr<USoundBase>> Events;

	/** Optional mix whose class overrides carry the volume sliders. Unset = engine primary volume only. */
	UPROPERTY(Config, EditAnywhere, Category = "Mixing")
	TSoftObjectPtr<USoundMix> VolumeMix;

	UPROPERTY(Config, EditAnywhere, Category = "Mixing")
	TSoftObjectPtr<USoundClass> MusicClass;

	UPROPERTY(Config, EditAnywhere, Category = "Mixing")
	TSoftObjectPtr<USoundClass> EffectsClass;
};
