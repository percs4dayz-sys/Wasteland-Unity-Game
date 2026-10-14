#include "UI/WLGameUserSettings.h"
#include "UI/WLUIThemeSettings.h"
#include "Audio/WLAudioSettings.h"
#include "Audio/WLMusicSubsystem.h"
#include "AudioDevice.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "Kismet/GameplayStatics.h"
#include "Sound/SoundClass.h"
#include "Sound/SoundMix.h"

UWLGameUserSettings* UWLGameUserSettings::GetWLSettings()
{
	if (UWLGameUserSettings* Settings = GEngine ? Cast<UWLGameUserSettings>(GEngine->GetGameUserSettings()) : nullptr)
	{
		return Settings;
	}
	// GameUserSettingsClassName not configured yet: keep working on defaults (nothing persists).
	return GetMutableDefault<UWLGameUserSettings>();
}

void UWLGameUserSettings::SetToDefaults()
{
	Super::SetToDefaults();
	MasterVolume = 1.f;   // Unity vol_master default
	MusicVolume = 0.5f;   // Unity vol_music default
	EffectsVolume = 1.f;
	InterfaceScale = 1.f;
	CameraOrbitSpeed = 1.f;
	LongPressMs = 500;
	bVibrateOnMenuOpen = true;
	bVibrateOnMenuHover = true;
	bVibrateOnDrag = true;
	bVibrateOnInteraction = false;
	bShowFunctionButton = true;
	FunctionButtonMode = EWLFunctionButtonMode::TapToDrop;
}

void UWLGameUserSettings::ApplyNonResolutionSettings()
{
	Super::ApplyNonResolutionSettings();
	LongPressMs = FMath::Clamp(LongPressMs, MinLongPressMs, MaxLongPressMs);
	if (GEngine)
	{
		for (const FWorldContext& Ctx : GEngine->GetWorldContexts())
		{
			if (Ctx.World() && (Ctx.WorldType == EWorldType::Game || Ctx.WorldType == EWorldType::PIE))
			{
				ApplyAudio(Ctx.World());
			}
		}
	}
}

void UWLGameUserSettings::SetInterfaceScale(float V)
{
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	InterfaceScale = FMath::Clamp(V, Theme->MinInterfaceScale, Theme->MaxInterfaceScale);
}

float UWLGameUserSettings::GetInterfaceScale() const
{
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	return FMath::Clamp(InterfaceScale, Theme->MinInterfaceScale, Theme->MaxInterfaceScale);
}

void UWLGameUserSettings::ApplyAudio(const UObject* WorldContext) const
{
	UWorld* World = WorldContext ? WorldContext->GetWorld() : nullptr;
	if (!World || World->GetNetMode() == NM_DedicatedServer)
	{
		return;
	}

	// Master: Unity set AudioListener.volume. The engine equivalent is the device's transient primary volume.
	if (FAudioDeviceHandle Device = World->GetAudioDevice())
	{
		Device->SetTransientPrimaryVolume(MasterVolume);
	}

	// Optional per-class volumes when a mix has been authored.
	if (const UWLAudioSettings* Audio = UWLAudioSettings::Get())
	{
		USoundMix* Mix = Audio->VolumeMix.LoadSynchronous();
		if (Mix)
		{
			// Music volume is applied on the jukebox's own component (below), so only effects go through the mix.
			if (USoundClass* Effects = Audio->EffectsClass.LoadSynchronous())
			{
				UGameplayStatics::SetSoundMixClassOverride(World, Mix, Effects, EffectsVolume, 1.f, 0.f, true);
			}
			UGameplayStatics::SetBaseSoundMix(World, Mix); // idempotent, unlike pushing a modifier
		}
	}

	if (UWLMusicSubsystem* MusicSys = UWLMusicSubsystem::Get(World))
	{
		MusicSys->SetVolume(MusicVolume);
	}
}
