#include "Audio/WLMusicSubsystem.h"
#include "Audio/WLAudioSettings.h"
#include "UI/WLGameUserSettings.h"
#include "Components/AudioComponent.h"
#include "Engine/AssetManager.h"
#include "Engine/StreamableManager.h"
#include "Engine/World.h"
#include "Kismet/GameplayStatics.h"
#include "Sound/SoundBase.h"
#include "Sound/SoundClass.h"
#include "TimerManager.h"

#define LOCTEXT_NAMESPACE "WLMusic"

UWLMusicSubsystem* UWLMusicSubsystem::Get(const UObject* WorldContextObject)
{
	const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
	return World ? World->GetSubsystem<UWLMusicSubsystem>() : nullptr;
}

bool UWLMusicSubsystem::ShouldCreateSubsystem(UObject* Outer) const
{
	return !IsRunningDedicatedServer() && Super::ShouldCreateSubsystem(Outer);
}

bool UWLMusicSubsystem::DoesSupportWorldType(const EWorldType::Type WorldType) const
{
	return WorldType == EWorldType::Game || WorldType == EWorldType::PIE;
}

void UWLMusicSubsystem::OnWorldBeginPlay(UWorld& InWorld)
{
	Super::OnWorldBeginPlay(InWorld);
	if (InWorld.GetNetMode() == NM_DedicatedServer)
	{
		return; // PIE dedicated-server worlds live in the same process
	}

	Rng.GenerateNewSeed();

	Tracks.Reset();
	if (const UWLAudioSettings* Settings = UWLAudioSettings::Get())
	{
		for (const FWLMusicTrack& T : Settings->MusicTracks)
		{
			if (!T.Sound.IsNull() && !T.Name.IsNone())
			{
				Tracks.Add({ T.Name, T.Sound });
			}
		}
	}
	// Stable alphabetical order so the panel list and saved disables line up run to run (Unity sorted too).
	Tracks.Sort([](const FTrack& A, const FTrack& B) { return A.Name.ToString().Compare(B.Name.ToString(), ESearchCase::IgnoreCase) < 0; });

	Disabled.Reset();
	Disabled.Append(UWLGameUserSettings::GetWLSettings()->DisabledMusicTracks);

	PlayNext();
}

void UWLMusicSubsystem::Deinitialize()
{
	if (Audio)
	{
		Audio->Stop();
		Audio->DestroyComponent();
		Audio = nullptr;
	}
	Super::Deinitialize();
}

FName UWLMusicSubsystem::GetTrackName(int32 Index) const
{
	return Tracks.IsValidIndex(Index) ? Tracks[Index].Name : NAME_None;
}

bool UWLMusicSubsystem::IsTrackEnabled(int32 Index) const
{
	return Tracks.IsValidIndex(Index) && !Disabled.Contains(Tracks[Index].Name);
}

FText UWLMusicSubsystem::GetCurrentTrackDisplayName() const
{
	return Tracks.IsValidIndex(CurrentIndex) ? FText::FromName(Tracks[CurrentIndex].Name) : LOCTEXT("Nothing", "(nothing)");
}

int32 UWLMusicSubsystem::PickNext(const TArray<int32>& Pool, int32 Current, FRandomStream& InRng)
{
	if (Pool.Num() == 0)
	{
		return INDEX_NONE;
	}
	int32 Next = Pool[InRng.RandRange(0, Pool.Num() - 1)];
	// Avoid repeating the same track when there's a choice (Unity: up to 8 re-rolls).
	for (int32 Guard = 0; Pool.Num() > 1 && Next == Current && Guard < 8; ++Guard)
	{
		Next = Pool[InRng.RandRange(0, Pool.Num() - 1)];
	}
	return Next;
}

void UWLMusicSubsystem::PlayNext()
{
	if (Tracks.Num() == 0)
	{
		return;
	}
	TArray<int32> Pool;
	for (int32 i = 0; i < Tracks.Num(); ++i)
	{
		if (IsTrackEnabled(i))
		{
			Pool.Add(i);
		}
	}
	if (Pool.Num() == 0)
	{
		if (Audio)
		{
			Audio->Stop();
		}
		CurrentIndex = INDEX_NONE;
		OnTrackChanged.Broadcast();
		return;
	}
	PlayTrack(PickNext(Pool, CurrentIndex, Rng));
}

void UWLMusicSubsystem::Next()
{
	PlayNext();
}

void UWLMusicSubsystem::PlayTrack(int32 Index)
{
	if (!Tracks.IsValidIndex(Index))
	{
		return;
	}
	CurrentIndex = Index;
	if (UWorld* World = GetWorld())
	{
		World->GetTimerManager().ClearTimer(GapTimer);
	}

	const FSoftObjectPath Path = Tracks[Index].Sound.ToSoftObjectPath();
	if (USoundBase* Loaded = Tracks[Index].Sound.Get())
	{
		StartTrack(Index, Loaded);
	}
	else if (UAssetManager::IsInitialized())
	{
		// Stream the track in; music can be large and must not hitch the frame.
		TWeakObjectPtr<UWLMusicSubsystem> WeakThis(this);
		UAssetManager::GetStreamableManager().RequestAsyncLoad(Path, FStreamableDelegate::CreateLambda([WeakThis, Index, Path]()
		{
			UWLMusicSubsystem* Self = WeakThis.Get();
			if (Self && Self->CurrentIndex == Index)
			{
				Self->StartTrack(Index, Cast<USoundBase>(Path.ResolveObject()));
			}
		}));
	}
	else
	{
		StartTrack(Index, Tracks[Index].Sound.LoadSynchronous());
	}
	OnTrackChanged.Broadcast();
}

void UWLMusicSubsystem::StartTrack(int32 Index, USoundBase* Sound)
{
	if (Audio)
	{
		Audio->OnAudioFinishedNative.RemoveAll(this);
		Audio->Stop();
		Audio->DestroyComponent();
		Audio = nullptr;
	}
	if (!Sound)
	{
		return;
	}
	const float Volume = UWLGameUserSettings::GetWLSettings()->GetMusicVolume();
	Audio = UGameplayStatics::CreateSound2D(this, Sound, Volume, 1.f, 0.f, nullptr, /*bPersistAcrossLevelTransition*/ false, /*bAutoDestroy*/ false);
	if (Audio)
	{
		if (const UWLAudioSettings* Settings = UWLAudioSettings::Get())
		{
			if (USoundClass* MusicClass = Settings->MusicClass.LoadSynchronous())
			{
				Audio->SoundClassOverride = MusicClass;
			}
		}
		Audio->OnAudioFinishedNative.AddUObject(this, &UWLMusicSubsystem::HandleAudioFinished);
		Audio->Play();
	}
}

void UWLMusicSubsystem::HandleAudioFinished(UAudioComponent* Component)
{
	if (Component != Audio)
	{
		return;
	}
	UWorld* World = GetWorld();
	const float Gap = UWLAudioSettings::Get() ? UWLAudioSettings::Get()->GapBetweenTracks : 0.5f;
	if (World && Gap > 0.f)
	{
		World->GetTimerManager().SetTimer(GapTimer, FTimerDelegate::CreateUObject(this, &UWLMusicSubsystem::PlayNext), Gap, false);
	}
	else
	{
		PlayNext();
	}
}

void UWLMusicSubsystem::SetTrackEnabled(int32 Index, bool bEnabled)
{
	if (!Tracks.IsValidIndex(Index))
	{
		return;
	}
	const FName Name = Tracks[Index].Name;
	if (bEnabled)
	{
		Disabled.Remove(Name);
	}
	else
	{
		Disabled.Add(Name);
	}
	SaveDisabled();

	// Disabling the track that's playing skips off it.
	if (!bEnabled && Index == CurrentIndex)
	{
		PlayNext();
	}
	else
	{
		OnTrackChanged.Broadcast();
	}
}

void UWLMusicSubsystem::SetVolume(float Volume)
{
	if (Audio)
	{
		Audio->SetVolumeMultiplier(FMath::Clamp(Volume, 0.f, 1.f));
	}
}

void UWLMusicSubsystem::SaveDisabled() const
{
	UWLGameUserSettings* Settings = UWLGameUserSettings::GetWLSettings();
	Settings->DisabledMusicTracks = Disabled.Array();
	Settings->SaveSettings();
}

#undef LOCTEXT_NAMESPACE
