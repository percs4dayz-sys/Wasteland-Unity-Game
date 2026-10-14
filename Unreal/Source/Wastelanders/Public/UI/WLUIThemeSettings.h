#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"
#include "Templates/SubclassOf.h"
#include "WLUIThemeSettings.generated.h"

class AHUD;

/**
 * The shared UI look (Project Settings > Game > Wastelanders UI). Ported from Unity UITheme.cs: the
 * "wasteland field kit" palette (dark gunmetal panels, bronze rim, amber accents). Unity drew every
 * sprite in code; here the palette is data so UMG/CommonUI styles and the asset-free debug HUD read the
 * same colours. No Unity sprites or fonts are ported.
 */
UCLASS(Config = Game, DefaultConfig, meta = (DisplayName = "Wastelanders UI"))
class WASTELANDERS_API UWLUIThemeSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	virtual FName GetCategoryName() const override { return TEXT("Game"); }

	static const UWLUIThemeSettings* Get() { return GetDefault<UWLUIThemeSettings>(); }

	// --- palette (UITheme.cs) ---------------------------------------------------------------

	/** Titles / highlights. */
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor Amber = FLinearColor(0.97f, 0.72f, 0.32f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor Text = FLinearColor(0.93f, 0.91f, 0.86f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor TextDim = FLinearColor(0.64f, 0.62f, 0.58f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor Alert = FLinearColor(1.f, 0.40f, 0.33f, 1.f);
	/** Buttons / rows. */
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor TileTint = FLinearColor(0.17f, 0.16f, 0.14f, 1.f);
	/** Empty part of a bar. */
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor TrackTint = FLinearColor(0.045f, 0.043f, 0.04f, 0.92f);
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor HpFill = FLinearColor(0.80f, 0.23f, 0.19f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor GoldFill = FLinearColor(0.88f, 0.62f, 0.22f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor XpFill = FLinearColor(0.33f, 0.60f, 0.93f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor BeastFill = FLinearColor(0.30f, 0.72f, 0.52f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Palette") FLinearColor TaskFill = FLinearColor(0.92f, 0.49f, 0.19f, 1.f);

	// Panel art colours (Unity drew these into its 9-slice sprites).
	UPROPERTY(Config, EditAnywhere, Category = "Panels") FLinearColor Outer = FLinearColor(0.015f, 0.015f, 0.015f, 0.92f);
	UPROPERTY(Config, EditAnywhere, Category = "Panels") FLinearColor Rim = FLinearColor(0.31f, 0.25f, 0.17f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Panels") FLinearColor PanelTop = FLinearColor(0.120f, 0.111f, 0.098f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Panels") FLinearColor PanelBottom = FLinearColor(0.074f, 0.070f, 0.064f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Panels") FLinearColor HeaderTop = FLinearColor(0.180f, 0.154f, 0.114f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Panels") FLinearColor HeaderBottom = FLinearColor(0.122f, 0.105f, 0.080f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Panels") FLinearColor AccentLine = FLinearColor(0.86f, 0.60f, 0.24f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Panels") FLinearColor AccentDark = FLinearColor(0.30f, 0.19f, 0.07f, 1.f);

	// Feedback colours used across panels (CraftingUI good/bad, ClickCross, chat tags).
	UPROPERTY(Config, EditAnywhere, Category = "Feedback") FLinearColor Good = FLinearColor(0.55f, 0.95f, 0.55f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Feedback") FLinearColor Bad = FLinearColor(0.95f, 0.45f, 0.45f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Feedback") FLinearColor ClickWalk = FLinearColor(1.f, 0.92f, 0.2f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Feedback") FLinearColor ClickAction = FLinearColor(1.f, 0.15f, 0.1f, 1.f);
	UPROPERTY(Config, EditAnywhere, Category = "Feedback") FLinearColor SelectedRow = FLinearColor(0.42f, 0.33f, 0.14f, 1.f);

	// --- interface size (UITheme.InterfaceScale) ----------------------------------------------

	/** Layout design resolution; widgets scale from it (CommonUI/DPI curve) times the player's Interface Size. */
	UPROPERTY(Config, EditAnywhere, Category = "Scale") FVector2D DesignResolution = FVector2D(1920.f, 1080.f);
	UPROPERTY(Config, EditAnywhere, Category = "Scale") float MinInterfaceScale = 0.8f;
	UPROPERTY(Config, EditAnywhere, Category = "Scale") float MaxInterfaceScale = 1.5f;

	// --- debug HUD --------------------------------------------------------------------------

	/**
	 * Until UMG widgets exist, the local player gets AWLDebugHUD (asset-free canvas UI) automatically.
	 * Turn off once the real HUD class is assigned in the game mode.
	 */
	UPROPERTY(Config, EditAnywhere, Category = "Debug HUD") bool bAutoInstallDebugHUD = true;

	/** HUD class the UI subsystem installs when bAutoInstallDebugHUD is on (defaults to AWLDebugHUD). */
	UPROPERTY(Config, EditAnywhere, Category = "Debug HUD") TSoftClassPtr<AHUD> DebugHUDClass;
};
