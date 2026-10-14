#include "Audio/WLAudioEvents.h"
#include "Audio/WLAudioSettings.h"
#include "UI/WLGameUserSettings.h"
#include "Engine/World.h"
#include "Kismet/GameplayStatics.h"
#include "Sound/SoundBase.h"
#include "Wastelanders.h"

namespace
{
	/** Resolve an event's sound; small one-shots load synchronously on first use and stay resident. */
	USoundBase* ResolveEvent(const UObject* WorldContextObject, FName EventName)
	{
		if (EventName.IsNone() || IsRunningDedicatedServer())
		{
			return nullptr;
		}
		const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
		if (!World || World->GetNetMode() == NM_DedicatedServer)
		{
			return nullptr;
		}
		const UWLAudioSettings* Settings = UWLAudioSettings::Get();
		const TSoftObjectPtr<USoundBase>* Soft = Settings ? Settings->Events.Find(EventName) : nullptr;
		USoundBase* Sound = Soft ? Soft->LoadSynchronous() : nullptr;
		if (!Sound)
		{
			static TSet<FName> Warned;
			if (!Warned.Contains(EventName))
			{
				Warned.Add(EventName);
				UE_LOG(LogWastelanders, Verbose, TEXT("[Audio] No sound assigned for event '%s' (Project Settings > Wastelanders Audio)"), *EventName.ToString());
			}
		}
		return Sound;
	}

	/** The effects slider, unless an authored mix already applies it through the effects sound class. */
	float EffectsScale()
	{
		const UWLAudioSettings* Settings = UWLAudioSettings::Get();
		const bool bMixHandlesIt = Settings && !Settings->VolumeMix.IsNull() && !Settings->EffectsClass.IsNull();
		return bMixHandlesIt ? 1.f : UWLGameUserSettings::GetWLSettings()->GetEffectsVolume();
	}
}

void UWLAudioEvents::PlayEvent(const UObject* WorldContextObject, FName EventName, float VolumeMultiplier)
{
	if (USoundBase* Sound = ResolveEvent(WorldContextObject, EventName))
	{
		// PlaySound2D overlaps like Unity's PlayOneShot.
		UGameplayStatics::PlaySound2D(WorldContextObject, Sound, VolumeMultiplier * EffectsScale());
	}
}

void UWLAudioEvents::PlayEventAtLocation(const UObject* WorldContextObject, FName EventName, FVector Location, float VolumeMultiplier)
{
	if (USoundBase* Sound = ResolveEvent(WorldContextObject, EventName))
	{
		UGameplayStatics::PlaySoundAtLocation(WorldContextObject, Sound, Location, VolumeMultiplier * EffectsScale());
	}
}

bool UWLAudioEvents::HasEvent(FName EventName)
{
	const UWLAudioSettings* Settings = UWLAudioSettings::Get();
	const TSoftObjectPtr<USoundBase>* Soft = Settings ? Settings->Events.Find(EventName) : nullptr;
	return Soft && !Soft->IsNull();
}
