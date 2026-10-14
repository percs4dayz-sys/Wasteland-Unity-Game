#include "UI/WLCanvasHUDBase.h"
#include "UI/WLUIThemeSettings.h"
#include "UI/WLGameUserSettings.h"
#include "Engine/Canvas.h"
#include "Engine/Engine.h"
#include "Engine/Font.h"
#include "GameFramework/PlayerController.h"

// Unity source: Assets/Scripts/UI/UITheme.cs (palette, panel / header / bar look). The layout helpers
// replace the per-panel RectTransform builders of the self-building Unity UI scripts.

float AWLCanvasHUDBase::ComputeUIScale() const
{
	const float Res = Canvas ? FMath::Max(1.f, Canvas->ClipY / 1080.f) : 1.f;
	return Res * UWLGameUserSettings::GetWLSettings()->GetInterfaceScale();
}

void AWLCanvasHUDBase::DrawHUD()
{
	Super::DrawHUD();
	if (!Canvas || !PlayerOwner)
	{
		return;
	}

	Font = GEngine ? GEngine->GetSmallFont() : nullptr;
	HitBoxes.Reset();
	UIRects.Reset();
	UIScale = ComputeUIScale();

	float MX = 0.f, MY = 0.f;
	bHasMouse = PlayerOwner->GetMousePosition(MX, MY);
	Mouse = bHasMouse ? FVector2D(MX, MY) : FVector2D(-1.f, -1.f);

	DrawCanvas();

	int32 HoverIndex = INDEX_NONE;
	if (bHasMouse)
	{
		for (int32 i = HitBoxes.Num() - 1; i >= 0; --i)
		{
			if (HitBoxes[i].Rect.IsInsideOrOn(Mouse))
			{
				HoverIndex = i;
				break;
			}
		}
	}
	bool bOverRect = false;
	if (bHasMouse)
	{
		for (const FBox2D& R : UIRects)
		{
			if (R.IsInsideOrOn(Mouse))
			{
				bOverRect = true;
				break;
			}
		}
	}
	bPointerOverUI = HoverIndex != INDEX_NONE || bOverRect;

	if (HoverIndex != INDEX_NONE && !HitBoxes[HoverIndex].Tooltip.IsEmpty())
	{
		DrawTooltip(HitBoxes[HoverIndex].Tooltip);
	}

	const bool bLeft = WasJustPressed(EKeys::LeftMouseButton);
	const bool bRight = WasJustPressed(EKeys::RightMouseButton);
	if (bLeft || bRight)
	{
		if (HoverIndex != INDEX_NONE)
		{
			// Copy first: the handler may open / close panels, which is fine, but never touch the array mid-call.
			const TFunction<void()> Handler = bLeft ? HitBoxes[HoverIndex].OnLeft : HitBoxes[HoverIndex].OnRight;
			if (Handler)
			{
				Handler();
			}
		}
		else if (!bOverRect)
		{
			OnUnhandledClick(bRight);
		}
	}
}

float AWLCanvasHUDBase::LineH() const
{
	const float Base = Font ? Font->GetMaxCharHeight() : 12.f;
	return (Base + 3.f) * UIScale;
}

FVector2f AWLCanvasHUDBase::Measure(const FString& Text, float Scale) const
{
	float W = 0.f, H = 0.f;
	if (Canvas && Font)
	{
		GetTextSize(Text, W, H, Font.Get(), Scale * UIScale);
	}
	return FVector2f(W, H);
}

FString AWLCanvasHUDBase::Fit(const FString& Text, float MaxWidth, float Scale) const
{
	if (Measure(Text, Scale).X <= MaxWidth)
	{
		return Text;
	}
	FString Out = Text;
	while (Out.Len() > 1 && Measure(Out + TEXT("..."), Scale).X > MaxWidth)
	{
		Out.LeftChopInline(1);
	}
	return Out + TEXT("...");
}

TArray<FString> AWLCanvasHUDBase::Wrap(const FString& Text, float MaxWidth, float Scale) const
{
	TArray<FString> Out;
	TArray<FString> Paragraphs;
	Text.ParseIntoArray(Paragraphs, TEXT("\n"), false);
	for (const FString& Para : Paragraphs)
	{
		TArray<FString> Words;
		Para.ParseIntoArrayWS(Words);
		FString Line;
		for (const FString& Word : Words)
		{
			const FString Candidate = Line.IsEmpty() ? Word : Line + TEXT(" ") + Word;
			if (!Line.IsEmpty() && Measure(Candidate, Scale).X > MaxWidth)
			{
				Out.Add(Line);
				Line = Word;
			}
			else
			{
				Line = Candidate;
			}
		}
		Out.Add(Line);
	}
	return Out;
}

void AWLCanvasHUDBase::Fill(float X, float Y, float W, float H, const FLinearColor& Color)
{
	DrawRect(Color, X, Y, W, H);
}

void AWLCanvasHUDBase::Frame(float X, float Y, float W, float H, const FLinearColor& Color, float Thickness)
{
	const float T = FMath::Max(1.f, Thickness);
	DrawRect(Color, X, Y, W, T);
	DrawRect(Color, X, Y + H - T, W, T);
	DrawRect(Color, X, Y, T, H);
	DrawRect(Color, X + W - T, Y, T, H);
}

float AWLCanvasHUDBase::Panel(float X, float Y, float W, float H, const FString& Title)
{
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	Fill(X - S(2), Y - S(2), W + S(4), H + S(4), Theme->Outer);
	Fill(X, Y, W, H * 0.5f, Theme->PanelTop);
	Fill(X, Y + H * 0.5f, W, H * 0.5f, Theme->PanelBottom);
	Frame(X, Y, W, H, Theme->Rim);
	BlockRect(X - S(2), Y - S(2), W + S(4), H + S(4));
	if (Title.IsEmpty())
	{
		return Y + S(4);
	}
	const float HeaderH = LineH() + S(6);
	Fill(X + 1, Y + 1, W - 2, HeaderH * 0.5f, Theme->HeaderTop);
	Fill(X + 1, Y + 1 + HeaderH * 0.5f, W - 2, HeaderH * 0.5f, Theme->HeaderBottom);
	Fill(X + 1, Y + HeaderH, W - 2, FMath::Max(1.f, S(1)), Theme->AccentLine);
	Label(Title.ToUpper(), X + S(8), Y + S(3), Theme->Amber);
	return Y + HeaderH + S(4);
}

float AWLCanvasHUDBase::Label(const FString& Text, float X, float Y, const FLinearColor& Color, float Scale)
{
	if (!Canvas || !Font || Text.IsEmpty())
	{
		return 0.f;
	}
	// One-pixel drop shadow so labels read over any world background (Unity used TMP outlines).
	DrawText(Text, FLinearColor(0.f, 0.f, 0.f, Color.A * 0.85f), X + 1.f, Y + 1.f, Font, Scale * UIScale);
	DrawText(Text, Color, X, Y, Font, Scale * UIScale);
	return Measure(Text, Scale).X;
}

void AWLCanvasHUDBase::LabelCentered(const FString& Text, float CenterX, float Y, const FLinearColor& Color, float Scale)
{
	Label(Text, CenterX - Measure(Text, Scale).X * 0.5f, Y, Color, Scale);
}

void AWLCanvasHUDBase::Bar(float X, float Y, float W, float H, float Fraction, const FLinearColor& FillColor)
{
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	Fill(X, Y, W, H, Theme->TrackTint);
	const float F = FMath::Clamp(Fraction, 0.f, 1.f);
	if (F > 0.f)
	{
		Fill(X + 1, Y + 1, (W - 2) * F, H - 2, FillColor);
	}
}

bool AWLCanvasHUDBase::Button(const FString& Text, float X, float Y, float W, float H, TFunction<void()> OnClick,
	bool bHighlighted, TFunction<void()> OnRightClick, const FText& Tooltip)
{
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const bool bHover = IsMouseIn(X, Y, W, H);
	FLinearColor Bg = bHighlighted ? Theme->SelectedRow : Theme->TileTint;
	if (bHover)
	{
		Bg = FLinearColor::LerpUsingHSV(Bg, Theme->HeaderTop, 0.6f);
	}
	Fill(X, Y, W, H, Bg);
	Frame(X, Y, W, H, bHover ? Theme->AccentLine : Theme->Rim);
	const FString Shown = Fit(Text, W - S(6));
	const FVector2f Size = Measure(Shown);
	Label(Shown, X + (W - Size.X) * 0.5f, Y + (H - Size.Y) * 0.5f, bHighlighted ? Theme->Amber : Theme->Text);
	AddHitBox(X, Y, W, H, MoveTemp(OnClick), MoveTemp(OnRightClick), Tooltip);
	return bHover;
}

void AWLCanvasHUDBase::AddHitBox(float X, float Y, float W, float H, TFunction<void()> OnLeft, TFunction<void()> OnRight, const FText& Tooltip)
{
	FHitBox& Box = HitBoxes.AddDefaulted_GetRef();
	Box.Rect = FBox2D(FVector2D(X, Y), FVector2D(X + W, Y + H));
	Box.OnLeft = MoveTemp(OnLeft);
	Box.OnRight = MoveTemp(OnRight);
	Box.Tooltip = Tooltip;
}

void AWLCanvasHUDBase::BlockRect(float X, float Y, float W, float H)
{
	UIRects.Add(FBox2D(FVector2D(X, Y), FVector2D(X + W, Y + H)));
}

bool AWLCanvasHUDBase::IsMouseIn(float X, float Y, float W, float H) const
{
	return bHasMouse && Mouse.X >= X && Mouse.X <= X + W && Mouse.Y >= Y && Mouse.Y <= Y + H;
}

bool AWLCanvasHUDBase::WasJustPressed(const FKey& Key) const
{
	return PlayerOwner && PlayerOwner->WasInputKeyJustPressed(Key);
}

bool AWLCanvasHUDBase::IsKeyDown(const FKey& Key) const
{
	return PlayerOwner && PlayerOwner->IsInputKeyDown(Key);
}

void AWLCanvasHUDBase::DrawTooltip(const FText& Tooltip)
{
	// Unity TooltipUI: small dark box offset down-right of the cursor, flipped to stay on screen.
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	TArray<FString> Lines;
	Tooltip.ToString().ParseIntoArrayLines(Lines);
	float W = 0.f;
	for (const FString& L : Lines)
	{
		W = FMath::Max(W, Measure(L).X);
	}
	W += S(12);
	const float H = Lines.Num() * LineH() + S(8);
	const float MX = static_cast<float>(Mouse.X);
	const float MY = static_cast<float>(Mouse.Y);
	float PX = MX + S(16);
	float PY = MY + S(16);
	if (PX + W > Canvas->ClipX) PX = FMath::Max(0.f, MX - W - S(4));
	if (PY + H > Canvas->ClipY) PY = FMath::Max(0.f, MY - H - S(4));
	Fill(PX, PY, W, H, Theme->Outer);
	Frame(PX, PY, W, H, Theme->Rim);
	float Y = PY + S(4);
	for (const FString& L : Lines)
	{
		Label(L, PX + S(6), Y, Theme->Text);
		Y += LineH();
	}
}
