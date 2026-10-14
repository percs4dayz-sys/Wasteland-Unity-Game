#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "WLMusicSubsystem.generated.h"

class UAudioComponent;
class USoundBase;

DECLARE_MULTICAST_DELEGATE(FWLOnMusicTrackChanged);

/**
 * Client-side jukebox, ported from Unity Managers/MusicManager.cs (+ MusicPanelUI's controls):
 *  - plays the configured tracks shuffled on a rolling loop, never the same track twice in a row
 *    when there is a choice;
 *  - the player can play a track now, skip, and disable ("X") tracks, remembered between runs
 *    (UWLGameUserSettings::DisabledMusicTracks);
 *  - disabling the playing track skips off it; when every track is disabled, music stops.
 *
 * Note: the Unity MusicManager is a jukebox only. There is no area/combat music state machine in the
 * Unity project, so none is invented here; area themes can later be a filter over MusicTracks.
 *
 * Never created on dedicated servers. Track list order is alphabetical (Unity sorted by clip name) so
 * indices are stable between runs.
 */
UCLASS()
class WASTELANDERS_API UWLMusicSubsystem : public UWorldSubsystem
{
	GENERATED_BODY()

public:
	static UWLMusicSubsystem* Get(const UObject* WorldContextObject);

	virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
	virtual bool DoesSupportWorldType(const EWorldType::Type WorldType) const override;
	virtual void OnWorldBeginPlay(UWorld& InWorld) override;
	virtual void Deinitialize() override;

	int32 NumTracks() const { return Tracks.Num(); }
	FName GetTrackName(int32 Index) const;
	bool IsTrackEnabled(int32 Index) const;
	int32 GetCurrentIndex() const { return CurrentIndex; }
	/** "(nothing)" when idle, like Unity. */
	FText GetCurrentTrackDisplayName() const;

	/** Play a specific track now (music panel click). */
	void PlayTrack(int32 Index);
	/** Skip to the next random enabled track. */
	void Next();
	/** The "X" next to a song. Persisted. */
	void SetTrackEnabled(int32 Index, bool bEnabled);

	void SetVolume(float Volume);

	/** Fires when the current track or the enabled set changes. */
	FWLOnMusicTrackChanged OnTrackChanged;

	/**
	 * Pure shuffle rule (exposed for tests): pick a random entry of Pool, re-rolling up to 8 times to avoid
	 * Current when there is a choice. Returns INDEX_NONE for an empty pool.
	 */
	static int32 PickNext(const TArray<int32>& Pool, int32 Current, FRandomStream& Rng);

private:
	struct FTrack
	{
		FName Name;
		TSoftObjectPtr<USoundBase> Sound;
	};

	void PlayNext();
	void HandleAudioFinished(UAudioComponent* Component);
	void StartTrack(int32 Index, USoundBase* Sound);
	void SaveDisabled() const;

	TArray<FTrack> Tracks;
	TSet<FName> Disabled;
	int32 CurrentIndex = INDEX_NONE;
	FRandomStream Rng;
	FTimerHandle GapTimer;

	UPROPERTY(Transient)
	TObjectPtr<UAudioComponent> Audio;
};
