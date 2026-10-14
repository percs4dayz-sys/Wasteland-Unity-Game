#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameUserSettings.h"
#include "Core/WLTypes.h"
#include "WLGameUserSettings.generated.h"

/** OSRS mobile function-button modes (Unity FunctionButton.Mode). */
UENUM(BlueprintType)
enum class EWLFunctionButtonMode : uint8
{
	TapToDrop,
	SingleTap,
	Keyboard
};

/**
 * Per-player client settings, saved in GameUserSettings.ini. Replaces Unity's PlayerPrefs keys used by
 * SettingsUI.cs (vol_master, vol_music, ui_scale), TouchSettings.cs (touch.*), FunctionButton.cs,
 * MusicManager.cs (music_disabled), SkillsPanelUI's tracked skill and CharacterSelect's "PlayerName".
 *
 * Requires DefaultEngine.ini: [/Script/Engine.Engine] GameUserSettingsClassName=/Script/Wastelanders.WLGameUserSettings
 * Get() falls back to a transient default object when that line is missing, so callers never null-check.
 */
UCLASS(Config = GameUserSettings, ConfigDoNotCheckDefaults)
class WASTELANDERS_API UWLGameUserSettings : public UGameUserSettings
{
	GENERATED_BODY()

public:
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Settings")
	static UWLGameUserSettings* GetWLSettings();

	virtual void SetToDefaults() override;
	virtual void ApplyNonResolutionSettings() override;

	// --- audio (SettingsUI) -------------------------------------------------------------------

	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Settings") void SetMasterVolume(float V) { MasterVolume = FMath::Clamp(V, 0.f, 1.f); }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Settings") float GetMasterVolume() const { return MasterVolume; }
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Settings") void SetMusicVolume(float V) { MusicVolume = FMath::Clamp(V, 0.f, 1.f); }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Settings") float GetMusicVolume() const { return MusicVolume; }
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Settings") void SetEffectsVolume(float V) { EffectsVolume = FMath::Clamp(V, 0.f, 1.f); }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Settings") float GetEffectsVolume() const { return EffectsVolume; }

	/** Push volumes to the audio engine and the music subsystem now (sliders call this live). */
	void ApplyAudio(const UObject* WorldContext) const;

	// --- interface (UITheme.InterfaceScale) --------------------------------------------------

	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Settings") void SetInterfaceScale(float V);
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Settings") float GetInterfaceScale() const;

	// --- camera ------------------------------------------------------------------------------

	/** Multiplier on AWLPlayerController's Q/E and middle-drag orbit speeds. */
	UPROPERTY(Config, BlueprintReadWrite, Category = "Camera") float CameraOrbitSpeed = 1.f;

	// --- touch (TouchSettings.cs; mobile is future work, values are stored and shown) --------

	static constexpr int32 MinLongPressMs = 200;
	static constexpr int32 MaxLongPressMs = 1000;

	UPROPERTY(Config, BlueprintReadWrite, Category = "Touch") int32 LongPressMs = 500;
	UPROPERTY(Config, BlueprintReadWrite, Category = "Touch") bool bVibrateOnMenuOpen = true;
	UPROPERTY(Config, BlueprintReadWrite, Category = "Touch") bool bVibrateOnMenuHover = true;
	UPROPERTY(Config, BlueprintReadWrite, Category = "Touch") bool bVibrateOnDrag = true;
	UPROPERTY(Config, BlueprintReadWrite, Category = "Touch") bool bVibrateOnInteraction = false;
	UPROPERTY(Config, BlueprintReadWrite, Category = "Touch") bool bShowFunctionButton = true;
	UPROPERTY(Config, BlueprintReadWrite, Category = "Touch") EWLFunctionButtonMode FunctionButtonMode = EWLFunctionButtonMode::TapToDrop;

	// --- music (MusicManager disabled list) ---------------------------------------------------

	UPROPERTY(Config) TArray<FName> DisabledMusicTracks;

	// --- HUD ----------------------------------------------------------------------------------

	/** Skill pinned to the HUD's tracked-XP bar (right-click in the skills panel). */
	UPROPERTY(Config) bool bHasTrackedSkill = false;
	UPROPERTY(Config) EWLSkill TrackedSkill = EWLSkill::Attack;

	// --- front end ----------------------------------------------------------------------------

	/** Last character this client entered the world with ("Continue" on the main menu). */
	UPROPERTY(Config) FString LastCharacterName;

	/** Last world server address typed or used. */
	UPROPERTY(Config) FString LastServerAddress;

protected:
	UPROPERTY(Config) float MasterVolume = 1.f;
	UPROPERTY(Config) float MusicVolume = 0.5f;
	UPROPERTY(Config) float EffectsVolume = 1.f;
	UPROPERTY(Config) float InterfaceScale = 1.f;
};
