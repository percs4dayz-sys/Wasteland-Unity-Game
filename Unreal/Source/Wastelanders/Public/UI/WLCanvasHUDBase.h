#pragma once

#include "CoreMinimal.h"
#include "GameFramework/HUD.h"
#include "InputCoreTypes.h"
#include "WLCanvasHUDBase.generated.h"

class UFont;

/**
 * Shared immediate-mode toolkit for the asset-free canvas HUDs (AWLDebugHUD in game, AWLFrontendHUD in
 * the front end). It draws themed boxes, bars, text and buttons with GEngine->GetSmallFont() and the
 * UWLUIThemeSettings palette (Unity UITheme), and handles clicks by hit-testing: every Button / AddHitBox
 * call during DrawCanvas registers a rectangle, and after drawing the top-most rectangle under the cursor
 * receives a left or right click. Areas drawn with Panel / BlockRect count as "pointer over UI".
 *
 * No textures, fonts or materials are referenced, so this works on a project with no art at all.
 */
UCLASS(Abstract, NotBlueprintable)
class WASTELANDERS_API AWLCanvasHUDBase : public AHUD
{
	GENERATED_BODY()

public:
	virtual void DrawHUD() override;

	/** True when the cursor was over a panel or button in the last drawn frame. */
	bool IsPointerOverCanvasUI() const { return bPointerOverUI; }

protected:
	/** Subclasses draw here. Hit boxes registered during this call are clicked after it returns. */
	virtual void DrawCanvas() {}

	/** A click landed on no hit box and no panel. Return true when it was used. */
	virtual bool OnUnhandledClick(bool bRightButton) { return false; }

	/** Overall scale applied to every size and to the font (interface scale x resolution). */
	virtual float ComputeUIScale() const;

	// --- drawing ----------------------------------------------------------------------------------

	float S(float V) const { return V * UIScale; }
	float LineH() const;
	FVector2f Measure(const FString& Text, float Scale = 1.f) const;
	/** Truncate with "..." to fit MaxWidth pixels. */
	FString Fit(const FString& Text, float MaxWidth, float Scale = 1.f) const;
	/** Word-wrap to MaxWidth pixels (explicit newlines respected). */
	TArray<FString> Wrap(const FString& Text, float MaxWidth, float Scale = 1.f) const;

	void Fill(float X, float Y, float W, float H, const FLinearColor& Color);
	void Frame(float X, float Y, float W, float H, const FLinearColor& Color, float Thickness = 1.f);
	/** Unity UITheme panel: dark outer, rim line, gradient-ish body, header strip with amber title. Blocks clicks. Returns the Y below the header. */
	float Panel(float X, float Y, float W, float H, const FString& Title);
	/** Returns the drawn width. */
	float Label(const FString& Text, float X, float Y, const FLinearColor& Color, float Scale = 1.f);
	void LabelCentered(const FString& Text, float CenterX, float Y, const FLinearColor& Color, float Scale = 1.f);
	/** Track + fill bar (Unity UITheme.TrackTint and a fill colour). */
	void Bar(float X, float Y, float W, float H, float Fraction, const FLinearColor& FillColor);

	/** A clickable box with a centred label. Returns true while hovered. */
	bool Button(const FString& Text, float X, float Y, float W, float H, TFunction<void()> OnClick,
		bool bHighlighted = false, TFunction<void()> OnRightClick = nullptr, const FText& Tooltip = FText::GetEmpty());

	void AddHitBox(float X, float Y, float W, float H, TFunction<void()> OnLeft, TFunction<void()> OnRight = nullptr,
		const FText& Tooltip = FText::GetEmpty());
	/** Marks an area as UI without a click action (clicks there are swallowed, not passed to the world). */
	void BlockRect(float X, float Y, float W, float H);

	bool IsMouseIn(float X, float Y, float W, float H) const;
	bool WasJustPressed(const FKey& Key) const;
	bool IsKeyDown(const FKey& Key) const;

	FVector2D Mouse = FVector2D(-1.f, -1.f);
	bool bHasMouse = false;
	float UIScale = 1.f;

	UPROPERTY(Transient)
	TObjectPtr<UFont> Font;

private:
	struct FHitBox
	{
		FBox2D Rect;
		TFunction<void()> OnLeft;
		TFunction<void()> OnRight;
		FText Tooltip;
	};

	void DrawTooltip(const FText& Tooltip);

	TArray<FHitBox> HitBoxes;
	TArray<FBox2D> UIRects;
	bool bPointerOverUI = false;
};
