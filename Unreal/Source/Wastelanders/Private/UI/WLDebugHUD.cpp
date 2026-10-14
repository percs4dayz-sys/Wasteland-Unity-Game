#include "UI/WLDebugHUD.h"
#include "UI/WLUISubsystem.h"
#include "UI/WLViewModels.h"
#include "UI/WLUIThemeSettings.h"
#include "UI/WLUITypes.h"
#include "Frontend/WLFrontendSubsystem.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerCharacter.h"
#include "Skills/WLSkillsComponent.h"
#include "Combat/WLEnemyCharacter.h"
#include "Interaction/WLInteractable.h"
#include "Data/WLDataSubsystem.h"
#include "Data/WLDataTypes.h"
#include "Core/WLXPTable.h"
#include "Core/WLTypes.h"
#include "Engine/Canvas.h"
#include "Engine/Engine.h"
#include "Engine/GameViewportClient.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "Framework/Application/SlateApplication.h"
#include "Widgets/Input/SEditableTextBox.h"
#include "Widgets/Layout/SBox.h"

#define LOCTEXT_NAMESPACE "WLDebugHUD"

namespace WLDebugHUD
{
	const FLinearColor LevelCyan = FLinearColor::FromSRGBColor(FColor::FromHex(TEXT("8FE0FF")));   // OverheadNameplate "LvN"
	const FLinearColor Shade(0.f, 0.f, 0.f, 0.55f);

	FString Pct(float V) { return FString::Printf(TEXT("%d%%"), FMath::RoundToInt(V * 100.f)); }
	FString Fmt(const FText& T) { return T.ToString(); }

	FLinearColor MarkerColor(EWLMinimapMarkerKind Kind)
	{
		switch (Kind)
		{
		case EWLMinimapMarkerKind::GroundItem: return FLinearColor(0.9f, 0.1f, 0.1f);
		case EWLMinimapMarkerKind::Creature:
		case EWLMinimapMarkerKind::Npc: return FLinearColor(1.f, 0.9f, 0.1f);
		case EWLMinimapMarkerKind::Player: return FLinearColor::White;
		case EWLMinimapMarkerKind::GatherNode: return FLinearColor(0.35f, 0.85f, 0.45f);
		case EWLMinimapMarkerKind::Objective: return UWLUIThemeSettings::Get()->Amber;
		case EWLMinimapMarkerKind::Destination: return FLinearColor(1.f, 0.15f, 0.1f);
		}
		return FLinearColor::White;
	}
}

using namespace WLDebugHUD;

void AWLDebugHUD::DrawHUD()
{
	Super::DrawHUD();
	if (UWLUISubsystem* UI = UWLUISubsystem::Get(PlayerOwner.Get()))
	{
		UI->SetPointerOverUI(IsPointerOverCanvasUI());
	}
}

void AWLDebugHUD::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	CloseChatLine();
	Super::EndPlay(EndPlayReason);
}

void AWLDebugHUD::DrawCanvas()
{
	UWLUISubsystem* UI = UWLUISubsystem::Get(PlayerOwner.Get());
	if (!UI)
	{
		return;
	}
	const float Dt = GetWorld() ? GetWorld()->GetDeltaSeconds() : 0.016f;

	if (bCloseChatPending)
	{
		CloseChatLine();
	}

	HandleHotkeys(*UI);

	DrawNameplates();
	DrawClickCrosses(Dt);
	DrawHudFrame(*UI);

	const float Margin = S(10);
	const float MapSize = S(150);
	const float Right = Canvas->ClipX - Margin;
	DrawMinimap(*UI, Right - MapSize, Margin, MapSize);
	const float TabBottom = DrawTabBar(*UI, Right, Margin + MapSize + S(6));
	DrawSidePanels(*UI, Right, TabBottom + S(6));

	DrawChat(*UI);
	DrawXPDrops(*UI);
	DrawBanner(*UI);
	DrawCrafting(*UI);
	DrawBank(*UI);
	DrawDialogue(*UI);
	DrawEggChoice(*UI);
	DrawContextMenu(*UI);
	DrawSplash(Dt);
}

// -------------------------------------------------------------------------------------------------
// Input
// -------------------------------------------------------------------------------------------------

int32 AWLDebugHUD::WheelDelta() const
{
	return (WasJustPressed(EKeys::MouseScrollDown) ? 1 : 0) - (WasJustPressed(EKeys::MouseScrollUp) ? 1 : 0);
}

void AWLDebugHUD::HandleHotkeys(UWLUISubsystem& UI)
{
	UWLChatViewModel* Chat = UI.GetChat();
	if (ChatBox.IsValid() || (Chat && Chat->bTyping))
	{
		return; // ChatInput.IsTyping: every hotkey is ignored while typing
	}

	if (WasJustPressed(EKeys::I)) UI.TogglePanel(EWLUIPanel::Inventory);
	if (WasJustPressed(EKeys::K)) UI.TogglePanel(EWLUIPanel::Skills);
	if (WasJustPressed(EKeys::F7)) UI.TogglePanel(EWLUIPanel::CombatStyle);
	if (WasJustPressed(EKeys::F9)) UI.TogglePanel(EWLUIPanel::Equipment);
	if (WasJustPressed(EKeys::O)) UI.TogglePanel(EWLUIPanel::Settings);
	if (WasJustPressed(EKeys::F10)) UI.TogglePanel(EWLUIPanel::SystemMenu);
#if !UE_BUILD_SHIPPING
	if (WasJustPressed(EKeys::F8)) UI.TogglePanel(EWLUIPanel::DevPanel);
#endif
	if (WasJustPressed(EKeys::Escape))
	{
		if (!UI.CloseTopmost())
		{
			UI.SetPanelOpen(EWLUIPanel::SystemMenu, true);
		}
	}
	if (WasJustPressed(EKeys::Enter))
	{
		OpenChatLine(UI);
		return;
	}

	UWLDialogueViewModel* Dialogue = UI.GetDialogue();
	if (Dialogue && Dialogue->bEggChoiceOpen)
	{
		static const FName Species[] = { FName(TEXT("wolf")), FName(TEXT("turtle")), FName(TEXT("bird")) };
		static const FKey Keys[] = { EKeys::One, EKeys::Two, EKeys::Three };
		for (int32 i = 0; i < 3; ++i)
		{
			if (WasJustPressed(Keys[i]))
			{
				Dialogue->ChooseStarterBeast(Species[i]);
			}
		}
	}
	else if (Dialogue && Dialogue->bActive)
	{
		if (Dialogue->Choices.Num() > 0)
		{
			static const FKey Keys[] = { EKeys::One, EKeys::Two, EKeys::Three, EKeys::Four, EKeys::Five, EKeys::Six, EKeys::Seven, EKeys::Eight, EKeys::Nine };
			for (int32 i = 0; i < FMath::Min(9, Dialogue->Choices.Num()); ++i)
			{
				if (WasJustPressed(Keys[i]))
				{
					Dialogue->Choose(i);
					break;
				}
			}
		}
		else if (WasJustPressed(EKeys::SpaceBar))
		{
			Dialogue->Continue();
		}
	}
}

bool AWLDebugHUD::OnUnhandledClick(bool bRightButton)
{
	if (bRightButton || !PlayerOwner)
	{
		return false; // the controller raises OnContextMenuRequested for world right-clicks
	}
	// Unity ClickCross: yellow for a walk click, red when the click lands on something to act on.
	FHitResult Hit;
	bool bAction = false;
	if (PlayerOwner->GetHitResultUnderCursor(ECC_Visibility, false, Hit))
	{
		const AActor* A = Hit.GetActor();
		bAction = A && (A->Implements<UWLInteractable>() || A->IsA<AWLEnemyCharacter>());
	}
	FClickCross& C = ClickCrosses.AddDefaulted_GetRef();
	C.Position = Mouse;
	C.bAction = bAction;
	return false; // let the controller act on it too
}

// -------------------------------------------------------------------------------------------------
// World-space bits
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawNameplates()
{
	UWorld* World = GetWorld();
	if (!World)
	{
		return;
	}
	// OverheadNameplate: "Name  LvN" floating ~2.4 m above the feet (the capsule centre sits ~0.9 m up).
	for (TActorIterator<AWLPlayerCharacter> It(World); It; ++It)
	{
		const AWLPlayerCharacter* P = *It;
		const AWLPlayerState* PS = P ? P->GetPlayerState<AWLPlayerState>() : nullptr;
		if (!PS)
		{
			continue;
		}
		const FVector Screen = Project(P->GetActorLocation() + FVector(0.f, 0.f, 150.f));
		if (Screen.Z <= 0.f)
		{
			continue;
		}
		const FString Name = PS->GetPlayerName().IsEmpty() ? FString(TEXT("Survivor")) : PS->GetPlayerName();
		const int32 Level = PS->GetSkills() ? PS->GetSkills()->GetCombatLevel() : 3;
		const FString Lv = FString::Printf(TEXT("  Lv%d"), Level);
		const float W = Measure(Name).X + Measure(Lv).X;
		const float X = static_cast<float>(Screen.X) - W * 0.5f;
		const float Y = static_cast<float>(Screen.Y);
		const float NW = Label(Name, X, Y, FLinearColor::White);
		Label(Lv, X + NW, Y, LevelCyan);
	}
	// Enemy health bars while damaged (combat read-out; hit splats belong to the combat feedback system).
	for (TActorIterator<AWLEnemyCharacter> It(World); It; ++It)
	{
		const AWLEnemyCharacter* E = *It;
		if (!E || E->IsDead() || E->GetMaxHP() <= 0 || E->GetCurrentHP() >= E->GetMaxHP())
		{
			continue;
		}
		const FVector Screen = Project(E->GetActorLocation() + FVector(0.f, 0.f, 140.f));
		if (Screen.Z <= 0.f)
		{
			continue;
		}
		const float W = S(50);
		Bar(static_cast<float>(Screen.X) - W * 0.5f, static_cast<float>(Screen.Y), W, S(6),
			static_cast<float>(E->GetCurrentHP()) / E->GetMaxHP(), UWLUIThemeSettings::Get()->HpFill);
	}
}

void AWLDebugHUD::DrawClickCrosses(float DeltaSeconds)
{
	// Unity ClickCross: a small X that shrinks and fades over ~0.4 s.
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	constexpr float Life = 0.4f;
	for (int32 i = ClickCrosses.Num() - 1; i >= 0; --i)
	{
		FClickCross& C = ClickCrosses[i];
		C.Age += DeltaSeconds;
		if (C.Age >= Life)
		{
			ClickCrosses.RemoveAt(i);
			continue;
		}
		const float T = C.Age / Life;
		const float R = S(9) * (1.f - 0.5f * T);
		FLinearColor Col = C.bAction ? Theme->ClickAction : Theme->ClickWalk;
		Col.A = 1.f - T;
		const float X = static_cast<float>(C.Position.X);
		const float Y = static_cast<float>(C.Position.Y);
		DrawLine(X - R, Y - R, X + R, Y + R, Col, S(2));
		DrawLine(X - R, Y + R, X + R, Y - R, Col, S(2));
	}
}

// -------------------------------------------------------------------------------------------------
// HUD frame (PlayerHudPanel)
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawHudFrame(UWLUISubsystem& UI)
{
	const UWLHudViewModel* H = UI.GetHud();
	if (!H)
	{
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float X = S(10), W = S(230);
	const float L = LineH();

	int32 Rows = 4; // name, HP, special, total
	Rows += H->bTrackingSkill ? 1 : 0;
	Rows += H->bOwnsBeast ? 1 : 0;
	Rows += H->bShowBeastTask ? 1 : 0;
	Rows += H->ObjectiveText.IsEmpty() ? 0 : 2;
	Rows += 1; // run / dodge
	const float PanelH = Rows * (L + S(4)) + S(10);
	float Y = Panel(X, S(10), W, PanelH, FString());

	const float IX = X + S(8), IW = W - S(16);
	Label(FString::Printf(TEXT("%s"), *Fmt(H->PlayerName)), IX, Y, Theme->Amber);
	const FString Cb = FString::Printf(TEXT("Combat %d"), H->CombatLevel);
	Label(Cb, IX + IW - Measure(Cb).X, Y, Theme->Text);
	Y += L + S(4);

	// HP: the value turns red at <= 30% (and the whole row while dead).
	const FString Hp = H->bDead ? FString(TEXT("DEAD")) : FString::Printf(TEXT("%d / %d"), H->Health, H->MaxHealth);
	Bar(IX + S(40), Y, IW - S(40), L, H->MaxHealth > 0 ? static_cast<float>(H->Health) / H->MaxHealth : 0.f, Theme->HpFill);
	Label(TEXT("HP"), IX, Y, Theme->TextDim);
	Label(Hp, IX + S(46), Y, (H->bHealthLow || H->bDead) ? Theme->Alert : Theme->Text);
	Y += L + S(4);

	Bar(IX + S(40), Y, IW - S(40), L, H->MaxSpecialEnergy > 0.f ? H->SpecialEnergy / H->MaxSpecialEnergy : 0.f, Theme->GoldFill);
	Label(TEXT("SPEC"), IX, Y, Theme->TextDim);
	Label(FString::Printf(TEXT("%d%%"), FMath::RoundToInt(H->MaxSpecialEnergy > 0.f ? 100.f * H->SpecialEnergy / H->MaxSpecialEnergy : 0.f)), IX + S(46), Y, Theme->Text);
	Y += L + S(4);

	Label(FString::Printf(TEXT("Total level %d"), H->TotalLevel), IX, Y, Theme->Text);
	Y += L + S(4);

	if (H->bTrackingSkill)
	{
		Bar(IX + S(110), Y, IW - S(110), L, H->TrackedSkillProgress, Theme->XpFill);
		Label(FString::Printf(TEXT("%s %d"), *Fmt(H->TrackedSkillName), H->TrackedSkillLevel), IX, Y, Theme->Text);
		Y += L + S(4);
	}
	if (H->bOwnsBeast)
	{
		Bar(IX + S(110), Y, IW - S(110), L, H->BeastStatusFill, Theme->BeastFill);
		Label(Fit(Fmt(H->BeastStatusText), S(105)), IX, Y, Theme->Text);
		Y += L + S(4);
	}
	if (H->bShowBeastTask)
	{
		Bar(IX + S(110), Y, IW - S(110), L, H->BeastTaskFill, Theme->TaskFill);
		Label(Fit(Fmt(H->BeastTaskLabel), S(105)), IX, Y, H->bBeastTaskAlert ? Theme->Alert : Theme->Text);
		Label(Fmt(H->BeastTaskValue), IX + S(114), Y, Theme->Text);
		Y += L + S(4);
	}
	if (!H->ObjectiveText.IsEmpty())
	{
		Label(TEXT("Objective"), IX, Y, Theme->Amber);
		Y += L + S(4);
		Label(Fit(Fmt(H->ObjectiveText), IW), IX, Y, Theme->Text);
		Y += L + S(4);
	}
	const FString Run = H->bRunning ? FString(TEXT("Run: on")) : FString(TEXT("Run: off"));
	Label(Run, IX, Y, H->bRunning ? Theme->Good : Theme->TextDim);
	if (H->DodgeCooldownRemaining > 0.f)
	{
		const float BX = IX + S(80);
		Bar(BX, Y, IW - S(80), L, 1.f - H->DodgeCooldownRemaining / FMath::Max(0.01f, H->DodgeCooldown), Theme->XpFill);
		Label(FString::Printf(TEXT("Dodge %.1fs"), H->DodgeCooldownRemaining), BX + S(4), Y, Theme->Text);
	}
	else
	{
		Label(TEXT("Dodge ready"), IX + S(80), Y, Theme->Good);
	}
}

// -------------------------------------------------------------------------------------------------
// Minimap + tab bar
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawMinimap(UWLUISubsystem& UI, float X, float Y, float Size)
{
	UWLMinimapViewModel* M = UI.GetMinimap();
	if (!M)
	{
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	Fill(X - S(2), Y - S(2), Size + S(4), Size + S(4), Theme->Outer);
	Fill(X, Y, Size, Size, Theme->TileTint);
	Frame(X, Y, Size, Size, Theme->Rim);
	const float R = Size * 0.5f;
	const float CX = X + R, CY = Y + R;

	// Click the map: walk there (MinimapHUD tap-to-walk).
	AddHitBox(X, Y, Size, Size, [this, M, CX, CY, R]()
	{
		const FVector2D MapPos((Mouse.X - CX) / R, -(Mouse.Y - CY) / R);
		M->WalkToMapPoint(MapPos);
	}, nullptr, LOCTEXT("MapTip", "Click to walk here"));

	for (const FWLMinimapMarker& Mk : M->Markers)
	{
		const float PX = CX + static_cast<float>(Mk.MapPosition.X) * (R - S(3));
		const float PY = CY - static_cast<float>(Mk.MapPosition.Y) * (R - S(3));
		const float D = Mk.Kind == EWLMinimapMarkerKind::GatherNode ? S(4) : S(3);
		if (Mk.Kind == EWLMinimapMarkerKind::Destination)
		{
			DrawLine(PX, PY, PX, PY - S(8), MarkerColor(Mk.Kind), S(1.5f));
			Fill(PX, PY - S(8), S(5), S(3), MarkerColor(Mk.Kind));
			continue;
		}
		FLinearColor Col = MarkerColor(Mk.Kind);
		if (Mk.Kind == EWLMinimapMarkerKind::Objective)
		{
			Col.A = 0.6f + 0.4f * FMath::Sin(GetWorld()->GetTimeSeconds() * 4.f); // pulsing
		}
		Fill(PX - D * 0.5f, PY - D * 0.5f, D, D, Col);
	}

	// Centre arrow: the character's facing relative to map-up.
	const float A = FMath::DegreesToRadians(M->PlayerHeadingOnMap);
	const FVector2D Dir(FMath::Sin(A), -FMath::Cos(A));
	DrawLine(CX, CY, CX + static_cast<float>(Dir.X) * S(8), CY + static_cast<float>(Dir.Y) * S(8), FLinearColor::White, S(2));
	Fill(CX - S(2), CY - S(2), S(4), S(4), FLinearColor::White);

	// Compass: click = face north, right-click cycles N / E / S / W (MinimapHUD compass hold menu).
	const float CS = S(22);
	Button(TEXT("N"), X + S(3), Y + S(3), CS, CS, [M]() { M->FaceNorth(); }, false,
		[M]()
		{
			const float Next = FMath::Fmod(FMath::GridSnap(M->CameraYaw, 90.f) + 90.f + 360.f, 360.f);
			M->LookToward(Next);
		}, LOCTEXT("CompassTip", "Face north (right-click: rotate 90)"));
}

float AWLDebugHUD::DrawTabBar(UWLUISubsystem& UI, float Right, float Y)
{
	struct FTab { const TCHAR* Label; EWLUIPanel Panel; const TCHAR* Tip; };
	TArray<FTab> Tabs = {
		{ TEXT("Inv"), EWLUIPanel::Inventory, TEXT("Inventory (I)") },
		{ TEXT("Style"), EWLUIPanel::CombatStyle, TEXT("Combat style (F7)") },
		{ TEXT("Skills"), EWLUIPanel::Skills, TEXT("Skills (K)") },
		{ TEXT("Equip"), EWLUIPanel::Equipment, TEXT("Equipment (F9)") },
		{ TEXT("Music"), EWLUIPanel::Music, TEXT("Music") },
		{ TEXT("Build"), EWLUIPanel::Build, TEXT("Building") },
		{ TEXT("Opts"), EWLUIPanel::Settings, TEXT("Settings (O)") },
		{ TEXT("Menu"), EWLUIPanel::SystemMenu, TEXT("System menu (F10)") },
	};
#if !UE_BUILD_SHIPPING
	Tabs.Add({ TEXT("Dev"), EWLUIPanel::DevPanel, TEXT("Dev panel (F8)") });
#endif
	const float BW = S(46), BH = S(22), Gap = S(3);
	const int32 PerRow = 5;
	const float RowW = PerRow * BW + (PerRow - 1) * Gap;
	const float Left = Right - RowW;
	for (int32 i = 0; i < Tabs.Num(); ++i)
	{
		const float BX = Left + (i % PerRow) * (BW + Gap);
		const float BY = Y + (i / PerRow) * (BH + Gap);
		const EWLUIPanel P = Tabs[i].Panel;
		Button(Tabs[i].Label, BX, BY, BW, BH, [&UI, P]() { UI.TogglePanel(P); }, UI.IsPanelOpen(P), nullptr, FText::FromString(Tabs[i].Tip));
	}
	const int32 RowsUsed = (Tabs.Num() + PerRow - 1) / PerRow;
	return Y + RowsUsed * (BH + Gap);
}

// -------------------------------------------------------------------------------------------------
// Side panels (flow down the right edge, wrapping into new columns to the left)
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawSidePanels(UWLUISubsystem& UI, float Right, float Top)
{
	using FPanelFn = float (AWLDebugHUD::*)(UWLUISubsystem&, float, float, float, bool);
	struct FEntry { EWLUIPanel Panel; FPanelFn Fn; float Width; };
	const FEntry Entries[] = {
		{ EWLUIPanel::Inventory, &AWLDebugHUD::PanelInventory, 262.f },
		{ EWLUIPanel::Equipment, &AWLDebugHUD::PanelEquipment, 262.f },
		{ EWLUIPanel::CombatStyle, &AWLDebugHUD::PanelCombatStyle, 262.f },
		{ EWLUIPanel::Skills, &AWLDebugHUD::PanelSkills, 262.f },
		{ EWLUIPanel::Music, &AWLDebugHUD::PanelMusic, 262.f },
		{ EWLUIPanel::Build, &AWLDebugHUD::PanelBuild, 262.f },
		{ EWLUIPanel::Settings, &AWLDebugHUD::PanelSettings, 262.f },
		{ EWLUIPanel::SystemMenu, &AWLDebugHUD::PanelSystemMenu, 262.f },
		{ EWLUIPanel::DevPanel, &AWLDebugHUD::PanelDev, 262.f },
	};
	const float Gap = S(8);
	const float Bottom = Canvas->ClipY - S(10);
	float ColRight = Right;
	float ColWidth = 0.f;
	float Y = Top;
	for (const FEntry& E : Entries)
	{
		if (!UI.IsPanelOpen(E.Panel))
		{
			continue;
		}
		const float W = S(E.Width);
		const float H = (this->*E.Fn)(UI, 0.f, 0.f, W, true);
		if (Y + H > Bottom && Y > Top)
		{
			ColRight -= ColWidth + Gap;
			ColWidth = 0.f;
			Y = S(10) + LineH() * 10.f; // below the HUD frame's height range is not guaranteed; start high
			Y = FMath::Min(Y, Top);
		}
		(this->*E.Fn)(UI, ColRight - W, Y, W, false);
		ColWidth = FMath::Max(ColWidth, W);
		Y += H + Gap;
	}
}

float AWLDebugHUD::PanelInventory(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly)
{
	UWLInventoryViewModel* Inv = UI.GetInventory();
	if (!Inv)
	{
		return 0.f;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	constexpr int32 Cols = 4;
	const float Pad = S(6), CellGap = S(3);
	const float CellW = (W - Pad * 2 - CellGap * (Cols - 1)) / Cols;
	const float CellH = LineH() * 2.f + S(4);
	const int32 Rows = (Inv->Slots.Num() + Cols - 1) / Cols;
	const float HeaderH = LineH() + S(10);
	const float H = HeaderH + Rows * (CellH + CellGap) + LineH() + S(16);
	if (bMeasureOnly)
	{
		return H;
	}

	float CY = Panel(X, Y, W, H, FString::Printf(TEXT("Inventory  %d/%d free"), Inv->FreeSlots, Inv->Capacity));
	if (Inv->bDropMode)
	{
		Frame(X, Y, W, H, Theme->Alert, S(2)); // drop mode: the panel turns red
	}
	UWLContextMenuViewModel* Menu = UI.GetContextMenu();
	for (int32 i = 0; i < Inv->Slots.Num(); ++i)
	{
		const FWLItemSlotView& Slot = Inv->Slots[i];
		const float SX = X + Pad + (i % Cols) * (CellW + CellGap);
		const float SY = CY + (i / Cols) * (CellH + CellGap);
		const bool bHover = IsMouseIn(SX, SY, CellW, CellH);
		FLinearColor Bg = Slot.bOverflow ? FLinearColor::LerpUsingHSV(Theme->TileTint, Theme->BeastFill, 0.15f) : Theme->TileTint;
		Fill(SX, SY, CellW, CellH, bHover ? Theme->HeaderTop : Bg);
		Frame(SX, SY, CellW, CellH, Theme->Rim);
		if (Slot.IsEmpty())
		{
			continue;
		}
		const TArray<FString> NameLines = Wrap(Fmt(Slot.DisplayName), CellW - S(4), 0.85f);
		for (int32 l = 0; l < FMath::Min(2, NameLines.Num()); ++l)
		{
			Label(Fit(NameLines[l], CellW - S(4), 0.85f), SX + S(2), SY + S(1) + l * LineH() * 0.85f, Theme->Text, 0.85f);
		}
		if (Slot.Quantity > 1)
		{
			Label(FString::Printf(TEXT("x%d"), Slot.Quantity), SX + S(2), SY + CellH - LineH() * 0.8f, Theme->Amber, 0.8f);
		}
		if (Slot.ModuleCount > 0)
		{
			const FString M = FString::Printf(TEXT("+%d"), Slot.ModuleCount);
			Label(M, SX + CellW - Measure(M, 0.8f).X - S(2), SY + CellH - LineH() * 0.8f, Theme->XpFill, 0.8f);
		}
		const int32 Index = Slot.SlotIndex;
		AddHitBox(SX, SY, CellW, CellH,
			[this, Inv, Index]()
			{
				// Desktop shift-click drops (OSRS); otherwise the default action (or drop in drop mode).
				if (IsKeyDown(EKeys::LeftShift) || IsKeyDown(EKeys::RightShift))
				{
					Inv->PerformVerb(Index, WLItemVerbs::Drop);
				}
				else
				{
					Inv->ActivateSlot(Index);
				}
			},
			[this, Menu, Index]()
			{
				if (Menu)
				{
					Menu->OpenForInventorySlot(Index, Mouse);
				}
			},
			Inv->GetSlotTitle(Index));
	}
	const float BY = CY + Rows * (CellH + CellGap) + S(4);
	Button(Inv->bDropMode ? TEXT("Tap-to-drop: ON") : TEXT("Tap-to-drop: off"), X + Pad, BY, W - Pad * 2, LineH() + S(4),
		[Inv]() { Inv->SetDropMode(!Inv->bDropMode); }, Inv->bDropMode);
	return H;
}

float AWLDebugHUD::PanelCombatStyle(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly)
{
	UWLCombatStyleViewModel* C = UI.GetCombatStyle();
	if (!C)
	{
		return 0.f;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float L = LineH(), RowH = L + S(8);
	int32 Rows = C->Stances.Num() + 2; // stances + special label + special button
	Rows += C->bHasBeastSpecial ? 1 : 0;
	Rows += C->bUsesMagazine ? 1 : 0;
	const float H = L + S(10) + Rows * (RowH + S(3)) + S(10);
	if (bMeasureOnly)
	{
		return H;
	}
	float CY = Panel(X, Y, W, H, Fmt(C->StyleHeading));
	const float IX = X + S(6), IW = W - S(12);
	for (const FWLStanceView& St : C->Stances)
	{
		const EWLCombatStance Stance = St.Stance;
		Button(Fmt(St.Label), IX, CY, IW, RowH, [C, Stance]() { C->SetStance(Stance); }, St.bActive, nullptr, St.Description);
		CY += RowH + S(3);
	}
	// Special attack (SpecialAttackHUD): energy bar + button, greyed with the reason when blocked.
	const FString SpecName = C->bHasWeaponSpecial ? Fmt(C->SpecialName) : FString(TEXT("No special attack"));
	Bar(IX, CY, IW, RowH, C->MaxSpecialEnergy > 0.f ? C->SpecialEnergy / C->MaxSpecialEnergy : 0.f, Theme->GoldFill);
	Label(FString::Printf(TEXT("%s  (%d / %d)"), *SpecName, FMath::RoundToInt(C->SpecialEnergy), FMath::RoundToInt(C->SpecialCost)), IX + S(4), CY + S(4), Theme->Text);
	CY += RowH + S(3);
	const bool bReady = C->bHasWeaponSpecial && C->SpecialBlockReason.IsEmpty();
	Button(bReady ? FString(TEXT("Use special")) : Fmt(C->SpecialBlockReason.IsEmpty() ? FText::FromString(TEXT("Unavailable")) : C->SpecialBlockReason),
		IX, CY, IW, RowH, [C]() { C->FireSpecial(); }, bReady);
	CY += RowH + S(3);
	if (C->bHasBeastSpecial)
	{
		const bool bBeastReady = C->BeastSpecialBlockReason.IsEmpty();
		const FString Text = bBeastReady ? FString(TEXT("Beast special"))
			: (C->BeastSpecialCooldown > 0 ? FString::Printf(TEXT("Beast special (%d)"), C->BeastSpecialCooldown) : Fmt(C->BeastSpecialBlockReason));
		Button(Text, IX, CY, IW, RowH, [C]() { C->FireBeastSpecial(); }, bBeastReady);
		CY += RowH + S(3);
	}
	if (C->bUsesMagazine)
	{
		const float BW = S(70);
		if (C->bReloading)
		{
			Bar(IX, CY, IW - BW - S(4), RowH, C->ReloadProgress, Theme->XpFill);
			Label(TEXT("Reloading..."), IX + S(4), CY + S(4), Theme->Text);
		}
		else
		{
			Label(FString::Printf(TEXT("Magazine %d / %d"), C->MagazineLoaded, C->MagazineSize), IX, CY + S(4),
				C->MagazineLoaded == 0 ? Theme->Alert : Theme->Text);
		}
		Button(TEXT("Reload"), IX + IW - BW, CY, BW, RowH, [C]() { C->Reload(); });
	}
	return H;
}

float AWLDebugHUD::PanelSkills(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly)
{
	UWLSkillsViewModel* Sk = UI.GetSkills();
	if (!Sk)
	{
		return 0.f;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	constexpr int32 Cols = 3;
	const float L = LineH();
	const float Pad = S(6), CellGap = S(3);
	const float CellW = (W - Pad * 2 - CellGap * (Cols - 1)) / Cols;
	const float CellH = L * 2.f + S(2);
	const int32 Rows = (Sk->Skills.Num() + Cols - 1) / Cols;
	const float H = L + S(10) + Rows * (CellH + CellGap) + L + S(12);
	if (bMeasureOnly)
	{
		return H;
	}
	const float CY = Panel(X, Y, W, H, TEXT("Skills"));
	for (int32 i = 0; i < Sk->Skills.Num(); ++i)
	{
		const FWLSkillView& V = Sk->Skills[i];
		const float SX = X + Pad + (i % Cols) * (CellW + CellGap);
		const float SY = CY + (i / Cols) * (CellH + CellGap);
		Fill(SX, SY, CellW, CellH, IsMouseIn(SX, SY, CellW, CellH) ? Theme->HeaderTop : Theme->TileTint);
		Frame(SX, SY, CellW, CellH, V.bTracked ? Theme->Amber : Theme->Rim);
		Label(Fit(Fmt(V.DisplayName), CellW - S(26), 0.85f), SX + S(3), SY + S(1), V.bTracked ? Theme->Amber : Theme->Text, 0.85f);
		const FString Lv = FString::FromInt(V.Level);
		Label(Lv, SX + CellW - Measure(Lv).X - S(3), SY + S(1), Theme->Amber);
		Bar(SX + S(3), SY + L + S(2), CellW - S(6), L * 0.6f, V.Progress, Theme->XpFill);
		const EWLSkill Skill = V.Skill;
		AddHitBox(SX, SY, CellW, CellH, nullptr, [Sk, Skill]() { Sk->ToggleTracked(Skill); },
			FText::Format(LOCTEXT("SkillTip", "{0}\n{1}\nRight-click to track on the HUD"), V.DisplayName, Sk->GetTooltip(Skill)));
	}
	const float FY = CY + Rows * (CellH + CellGap) + S(2);
	Label(FString::Printf(TEXT("Total level: %d"), Sk->TotalLevel), X + Pad, FY, Theme->Text);
	const FString Cb = FString::Printf(TEXT("Combat: %d"), Sk->CombatLevel);
	Label(Cb, X + W - Pad - Measure(Cb).X, FY, Theme->Text);
	return H;
}

float AWLDebugHUD::PanelEquipment(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly)
{
	UWLEquipmentViewModel* Eq = UI.GetEquipment();
	if (!Eq)
	{
		return 0.f;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float L = LineH(), RowH = L + S(6);
	const float H = L + S(10) + Eq->Slots.Num() * (RowH + S(2)) + L * 2.f + S(14);
	if (bMeasureOnly)
	{
		return H;
	}
	float CY = Panel(X, Y, W, H, TEXT("Equipment"));
	const float IX = X + S(6), IW = W - S(12);
	for (const FWLEquipSlotView& V : Eq->Slots)
	{
		const EWLEquipSlot Slot = V.Slot;
		const bool bEmpty = V.ItemId <= 0;
		FString Mods;
		for (const int32 M : V.Modules)
		{
			Mods += M > 0 ? TEXT("*") : TEXT("o"); // filled / empty socket
		}
		const FString Text = FString::Printf(TEXT("%s: %s%s%s"), *Fmt(V.SlotLabel), bEmpty ? TEXT("-") : *Fmt(V.DisplayName),
			Mods.IsEmpty() ? TEXT("") : TEXT("  "), *Mods);
		const bool bHover = IsMouseIn(IX, CY, IW, RowH);
		Fill(IX, CY, IW, RowH, bHover && !bEmpty ? Theme->HeaderTop : Theme->TileTint);
		Label(Fit(Text, IW - S(8)), IX + S(4), CY + S(3), bEmpty ? Theme->TextDim : Theme->Text);
		if (!bEmpty)
		{
			AddHitBox(IX, CY, IW, RowH, [Eq, Slot]() { Eq->Unequip(Slot); }, nullptr, LOCTEXT("UnequipTip", "Click to unequip"));
		}
		CY += RowH + S(2);
	}
	CY += S(4);
	Label(FString::Printf(TEXT("Attack +%d   Strength +%d"), Eq->AttackBonus, Eq->StrengthBonus), IX, CY, Theme->Text);
	Label(FString::Printf(TEXT("Defence +%d"), Eq->DefenceBonus), IX, CY + L, Theme->Text);
	return H;
}

float AWLDebugHUD::PanelMusic(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly)
{
	UWLSettingsViewModel* St = UI.GetSettings();
	if (!St)
	{
		return 0.f;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float L = LineH(), RowH = L + S(6);
	const int32 Rows = FMath::Max(1, St->Tracks.Num());
	const float H = L + S(10) + L + S(4) + Rows * (RowH + S(2)) + RowH + S(12);
	if (bMeasureOnly)
	{
		return H;
	}
	float CY = Panel(X, Y, W, H, TEXT("Music"));
	const float IX = X + S(6), IW = W - S(12);
	Label(Fit(FString::Printf(TEXT("Now playing: %s"), *Fmt(St->NowPlaying)), IW), IX, CY, Theme->Amber);
	CY += L + S(4);
	if (St->Tracks.Num() == 0)
	{
		Label(TEXT("No tracks configured."), IX, CY, Theme->TextDim);
		CY += RowH + S(2);
	}
	for (const FWLMusicTrackView& T : St->Tracks)
	{
		const int32 Index = T.Index;
		const FString Text = FString::Printf(TEXT("%s%s"), T.bPlaying ? TEXT("> ") : TEXT(""), *Fmt(T.Name));
		Button(Text, IX, CY, IW, RowH, [St, Index]() { St->PlayTrack(Index); }, T.bPlaying,
			[St, Index]() { St->ToggleTrack(Index); },
			T.bEnabled ? LOCTEXT("TrackOn", "Click to play, right-click to remove from the shuffle")
			           : LOCTEXT("TrackOff", "Removed from the shuffle. Right-click to add back"));
		if (!T.bEnabled)
		{
			Fill(IX + S(4), CY + RowH * 0.5f, IW - S(8), FMath::Max(1.f, S(1)), Theme->Alert); // struck through
		}
		CY += RowH + S(2);
	}
	Button(TEXT("Skip track"), IX, CY + S(2), IW, RowH, [St]() { St->SkipTrack(); });
	return H;
}

float AWLDebugHUD::PanelBuild(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly)
{
	// BuildInfoPanelUI: a "coming soon" signpost for the building system.
	const FString Body = TEXT("Coming soon.\n\nBuilding lets you place deployables in the world: cooking fires, furnaces, "
		"workbenches, banks, auto-turrets and AFK resource stations.\n\nIt isn't its own skill yet. Placing costs "
		"materials and is being reworked into a proper progression. This tab will become the build browser when it lands.");
	const TArray<FString> Lines = Wrap(Body, W - S(16));
	const float H = LineH() + S(10) + Lines.Num() * LineH() + S(12);
	if (bMeasureOnly)
	{
		return H;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	float CY = Panel(X, Y, W, H, TEXT("Building"));
	for (int32 i = 0; i < Lines.Num(); ++i)
	{
		Label(Lines[i], X + S(8), CY, i == 0 ? Theme->Amber : Theme->Text);
		CY += LineH();
	}
	return H;
}

void AWLDebugHUD::StepperRow(float X, float Y, float W, const FString& Caption, const FString& Value, float Fraction,
	TFunction<void()> OnMinus, TFunction<void()> OnPlus)
{
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float L = LineH(), BW = S(22), RowH = L + S(6);
	Label(Caption, X, Y + S(3), Theme->Text);
	const float BarX = X + S(100);
	const float BarW = W - S(100) - BW * 2 - S(8);
	Button(TEXT("-"), BarX, Y, BW, RowH, MoveTemp(OnMinus));
	Bar(BarX + BW + S(4), Y + S(2), BarW, RowH - S(4), Fraction, Theme->Amber);
	LabelCentered(Value, BarX + BW + S(4) + BarW * 0.5f, Y + S(3), Theme->Text);
	Button(TEXT("+"), BarX + BW + S(8) + BarW, Y, BW, RowH, MoveTemp(OnPlus));
}

float AWLDebugHUD::PanelSettings(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly)
{
	UWLSettingsViewModel* St = UI.GetSettings();
	if (!St)
	{
		return 0.f;
	}
	const float L = LineH(), RowH = L + S(6) + S(4);
	constexpr int32 Rows = 6;
	const float H = L + S(10) + Rows * RowH + S(10);
	if (bMeasureOnly)
	{
		return H;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	float CY = Panel(X, Y, W, H, TEXT("Settings"));
	const float IX = X + S(6), IW = W - S(12);
	StepperRow(IX, CY, IW, TEXT("Master"), Pct(St->MasterVolume), St->MasterVolume,
		[St]() { St->SetMasterVolume(St->MasterVolume - 0.1f); }, [St]() { St->SetMasterVolume(St->MasterVolume + 0.1f); });
	CY += RowH;
	StepperRow(IX, CY, IW, TEXT("Music"), Pct(St->MusicVolume), St->MusicVolume,
		[St]() { St->SetMusicVolume(St->MusicVolume - 0.1f); }, [St]() { St->SetMusicVolume(St->MusicVolume + 0.1f); });
	CY += RowH;
	StepperRow(IX, CY, IW, TEXT("Effects"), Pct(St->EffectsVolume), St->EffectsVolume,
		[St]() { St->SetEffectsVolume(St->EffectsVolume - 0.1f); }, [St]() { St->SetEffectsVolume(St->EffectsVolume + 0.1f); });
	CY += RowH;
	const float ScaleFrac = (St->InterfaceScale - Theme->MinInterfaceScale) / FMath::Max(0.01f, Theme->MaxInterfaceScale - Theme->MinInterfaceScale);
	StepperRow(IX, CY, IW, TEXT("Interface"), FString::Printf(TEXT("%.2fx"), St->InterfaceScale), ScaleFrac,
		[St]() { St->SetInterfaceScale(St->InterfaceScale - 0.05f); }, [St]() { St->SetInterfaceScale(St->InterfaceScale + 0.05f); });
	CY += RowH;
	StepperRow(IX, CY, IW, TEXT("Camera speed"), FString::Printf(TEXT("%.1fx"), St->CameraOrbitSpeed), (St->CameraOrbitSpeed - 0.25f) / 2.75f,
		[St]() { St->SetCameraOrbitSpeed(St->CameraOrbitSpeed - 0.25f); }, [St]() { St->SetCameraOrbitSpeed(St->CameraOrbitSpeed + 0.25f); });
	CY += RowH;
	StepperRow(IX, CY, IW, TEXT("Long press"), FString::Printf(TEXT("%d ms"), St->LongPressMs), (St->LongPressMs - 200) / 800.f,
		[St]() { St->SetLongPressMs(St->LongPressMs - 50); }, [St]() { St->SetLongPressMs(St->LongPressMs + 50); });
	return H;
}

float AWLDebugHUD::PanelSystemMenu(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly)
{
	const float L = LineH(), RowH = L + S(8);
	const float H = L + S(10) + L + S(6) + 2 * (RowH + S(4)) + S(8);
	if (bMeasureOnly)
	{
		return H;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	float CY = Panel(X, Y, W, H, TEXT("Wasteland Menu"));
	const float IX = X + S(6), IW = W - S(12);
	const UWLHudViewModel* Hud = UI.GetHud();
	Label(FString::Printf(TEXT("Playing as %s"), Hud ? *Fmt(Hud->PlayerName) : TEXT("?")), IX, CY, Theme->Text);
	CY += L + S(6);
	// WastelandMenuUI's rename / wipe-save actions are server-side decisions now; logout returns to the front end.
	APlayerController* PC = PlayerOwner.Get();
	Button(TEXT("Log out"), IX, CY, IW, RowH, [PC]() { if (PC) { PC->ConsoleCommand(TEXT("disconnect")); } });
	CY += RowH + S(4);
	Button(TEXT("Quit game"), IX, CY, IW, RowH, [PC]() { if (PC) { PC->ConsoleCommand(TEXT("quit")); } });
	return H;
}

float AWLDebugHUD::PanelDev(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly)
{
#if UE_BUILD_SHIPPING
	return 0.f;
#else
	// DevPanel: quick actions + a click-to-add list of weapons / armour / ammo. Everything goes through the
	// server's /dev chat commands (which check permission), never local state.
	if (DevItemIds.Num() == 0)
	{
		if (const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this))
		{
			for (const TPair<int32, const FWLItemRow*>& Pair : Data->GetAllItems())
			{
				if (Pair.Value && (Pair.Value->IsWeapon() || Pair.Value->IsArmour() || Pair.Value->IsAmmo()))
				{
					DevItemIds.Add(Pair.Key);
				}
			}
			DevItemIds.Sort();
		}
	}
	constexpr int32 Visible = 12;
	const float L = LineH(), RowH = L + S(6);
	const float H = L + S(10) + (RowH + S(4)) + L + S(4) + Visible * (RowH + S(2)) + RowH + S(12);
	if (bMeasureOnly)
	{
		return H;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	UWLChatViewModel* Chat = UI.GetChat();
	float CY = Panel(X, Y, W, H, TEXT("Dev menu"));
	const float IX = X + S(6), IW = W - S(12);
	const float HalfW = (IW - S(4)) * 0.5f;
	Button(TEXT("Heal"), IX, CY, HalfW, RowH, [Chat]() { if (Chat) { Chat->Submit(TEXT("/dev heal")); } });
	Button(TEXT("Max combat Lv"), IX + HalfW + S(4), CY, HalfW, RowH, [Chat]()
	{
		if (!Chat)
		{
			return;
		}
		for (const EWLSkill Skill : { EWLSkill::Attack, EWLSkill::Strength, EWLSkill::Defence, EWLSkill::Endurance, EWLSkill::Marksmanship, EWLSkill::Fission })
		{
			const int32 XP = FWLXPTable::XPForLevel(Skill, FWLXPTable::MaxLevelFor(Skill));
			Chat->Submit(FString::Printf(TEXT("/dev xp %s %d"), *WLTypes::SkillToString(Skill), XP));
		}
	});
	CY += RowH + S(4);
	Label(TEXT("Tier weapons / armour / ammo: click to add"), IX, CY, Theme->Amber);
	CY += L + S(4);

	const int32 MaxScroll = FMath::Max(0, DevItemIds.Num() - Visible);
	if (IsMouseIn(X, Y, W, H))
	{
		DevScroll = FMath::Clamp(DevScroll + WheelDelta(), 0, MaxScroll);
	}
	DevScroll = FMath::Clamp(DevScroll, 0, MaxScroll);
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	for (int32 r = 0; r < Visible; ++r)
	{
		const int32 i = DevScroll + r;
		if (!DevItemIds.IsValidIndex(i))
		{
			break;
		}
		const int32 Id = DevItemIds[i];
		const FString Name = Data ? Data->GetItemName(Id).ToString() : FString::FromInt(Id);
		Button(FString::Printf(TEXT("%s  (#%d)"), *Name, Id), IX, CY, IW, RowH,
			[Chat, Id]() { if (Chat) { Chat->Submit(FString::Printf(TEXT("/dev item %d"), Id)); } });
		CY += RowH + S(2);
	}
	CY = Y + H - RowH - S(8);
	Button(TEXT("Up"), IX, CY, HalfW, RowH, [this]() { DevScroll = FMath::Max(0, DevScroll - 12); });
	Button(TEXT("Down"), IX + HalfW + S(4), CY, HalfW, RowH, [this, MaxScroll]() { DevScroll = FMath::Min(MaxScroll, DevScroll + 12); });
	return H;
#endif
}

// -------------------------------------------------------------------------------------------------
// Chat (HUDController chat log + ChatInput)
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawChat(UWLUISubsystem& UI)
{
	const UWLChatViewModel* Chat = UI.GetChat();
	if (!Chat)
	{
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	constexpr int32 Shown = 8;
	const float L = LineH();
	const float W = S(520);
	const float X = S(10);
	const float H = Shown * L + L + S(14);
	const float Y = Canvas->ClipY - H - S(10) - (ChatBox.IsValid() ? S(30) : 0.f);
	Fill(X, Y, W, H, Shade);
	BlockRect(X, Y, W, H);
	const int32 First = FMath::Max(0, Chat->Lines.Num() - Shown);
	float LY = Y + S(4);
	for (int32 i = First; i < Chat->Lines.Num(); ++i)
	{
		Label(Fit(Chat->Lines[i].ToString(), W - S(10)), X + S(5), LY, Theme->Text);
		LY += L;
	}
	if (!ChatBox.IsValid())
	{
		Label(TEXT("Press Enter to chat"), X + S(5), Y + H - L - S(4), Theme->TextDim);
	}
}

void AWLDebugHUD::OpenChatLine(UWLUISubsystem& UI)
{
	if (ChatBox.IsValid() || !GEngine || !GEngine->GameViewport || !FSlateApplication::IsInitialized())
	{
		return;
	}
	TWeakObjectPtr<AWLDebugHUD> WeakThis(this);
	TSharedRef<SEditableTextBox> Box = SNew(SEditableTextBox)
		.HintText(LOCTEXT("ChatHint", "Say something, or type a /command"))
		.ClearKeyboardFocusOnCommit(false)
		.RevertTextOnEscape(true)
		.OnTextCommitted_Lambda([WeakThis](const FText& Text, ETextCommit::Type Type)
		{
			if (AWLDebugHUD* Self = WeakThis.Get())
			{
				Self->HandleChatCommitted(Text, Type);
			}
		});
	ChatBox = Box;
	ChatContainer = SNew(SBox)
		.Visibility(EVisibility::SelfHitTestInvisible)
		.HAlign(HAlign_Left)
		.VAlign(VAlign_Bottom)
		.Padding(FMargin(S(10), 0.f, 0.f, S(10)))
		[
			SNew(SBox)
			.WidthOverride(S(520))
			[
				Box
			]
		];
	GEngine->GameViewport->AddViewportWidgetContent(ChatContainer.ToSharedRef(), 100);
	FSlateApplication::Get().SetKeyboardFocus(Box, EFocusCause::SetDirectly);
	if (UWLChatViewModel* Chat = UI.GetChat())
	{
		Chat->SetTyping(true);
	}
}

void AWLDebugHUD::HandleChatCommitted(const FText& Text, ETextCommit::Type CommitType)
{
	if (CommitType == ETextCommit::OnEnter && !Text.IsEmptyOrWhitespace())
	{
		if (UWLUISubsystem* UI = UWLUISubsystem::Get(PlayerOwner.Get()))
		{
			if (UWLChatViewModel* Chat = UI->GetChat())
			{
				Chat->Submit(Text.ToString());
			}
		}
	}
	// Removing the widget from inside its own commit callback is unsafe; close on the next draw.
	bCloseChatPending = true;
}

void AWLDebugHUD::CloseChatLine()
{
	bCloseChatPending = false;
	if (ChatContainer.IsValid() && GEngine && GEngine->GameViewport)
	{
		GEngine->GameViewport->RemoveViewportWidgetContent(ChatContainer.ToSharedRef());
	}
	const bool bWasOpen = ChatBox.IsValid();
	ChatContainer.Reset();
	ChatBox.Reset();
	if (bWasOpen)
	{
		if (FSlateApplication::IsInitialized())
		{
			FSlateApplication::Get().SetAllUserFocusToGameViewport();
		}
		if (UWLUISubsystem* UI = UWLUISubsystem::Get(PlayerOwner.Get()))
		{
			if (UWLChatViewModel* Chat = UI->GetChat())
			{
				Chat->SetTyping(false);
			}
		}
	}
}

// -------------------------------------------------------------------------------------------------
// XP drops, banner
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawXPDrops(UWLUISubsystem& UI)
{
	const UWLXPDropViewModel* XP = UI.GetXPDrops();
	if (!XP)
	{
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	// Anchored left of the minimap, rising (XPDropUI).
	const float AnchorX = Canvas->ClipX - S(10) - S(150) - S(16);
	const float AnchorY = S(10) + S(150);
	for (const FWLXPDropView& D : XP->Drops)
	{
		FLinearColor C = Theme->Text;
		C.A = FWLUILogic::XPDropAlpha(D.Age);
		const FString T = D.Text.ToString();
		Label(T, AnchorX - Measure(T).X, AnchorY - S(D.OffsetY), C);
	}
	if (XP->CelebrationTimeLeft > 0.f && !XP->CelebrationText.IsEmpty())
	{
		FLinearColor C = Theme->Amber;
		C.A = FMath::Clamp(XP->CelebrationTimeLeft / 0.5f, 0.f, 1.f);
		LabelCentered(XP->CelebrationText.ToString(), Canvas->ClipX * 0.5f, Canvas->ClipY * 0.14f, C, 1.6f);
	}
}

void AWLDebugHUD::DrawBanner(UWLUISubsystem& UI)
{
	const UWLHudViewModel* H = UI.GetHud();
	if (!H || H->BannerTimeLeft <= 0.f || H->BannerTitle.IsEmpty())
	{
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float A = FMath::Clamp(H->BannerTimeLeft / 0.5f, 0.f, 1.f);
	FLinearColor TitleC = Theme->Amber;
	TitleC.A = A;
	FLinearColor SubC = Theme->Text;
	SubC.A = A;
	const float Y = Canvas->ClipY * 0.22f;
	LabelCentered(H->BannerTitle.ToString(), Canvas->ClipX * 0.5f, Y, TitleC, 2.2f);
	LabelCentered(H->BannerSubtitle.ToString(), Canvas->ClipX * 0.5f, Y + LineH() * 2.4f, SubC, 1.2f);
}

// -------------------------------------------------------------------------------------------------
// Crafting (CraftingUI)
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawCrafting(UWLUISubsystem& UI)
{
	UWLCraftingViewModel* Cr = UI.GetCrafting();
	if (!Cr || !Cr->bOpen)
	{
		CraftScroll = 0;
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float L = LineH(), RowH = L + S(6);
	const float W = S(460), H = S(430);
	const float X = Canvas->ClipX * 0.5f - W - S(10);
	const float Y = Canvas->ClipY * 0.5f - H * 0.5f;
	const float TopY = Panel(X, Y, W, H, Fmt(Cr->StationName));
	Button(TEXT("X"), X + W - S(24), Y + S(3), S(20), L + S(2), [Cr]() { Cr->Close(); });

	// Recipe list (left column).
	const float ListW = S(220);
	const float ListH = H - (TopY - Y) - S(8);
	const int32 Visible = FMath::Max(1, FMath::FloorToInt(ListH / (RowH + S(2))));
	const int32 MaxScroll = FMath::Max(0, Cr->Recipes.Num() - Visible);
	if (IsMouseIn(X, TopY, ListW, ListH))
	{
		CraftScroll += WheelDelta();
	}
	CraftScroll = FMath::Clamp(CraftScroll, 0, MaxScroll);
	float CY = TopY;
	for (int32 r = 0; r < Visible; ++r)
	{
		const int32 i = CraftScroll + r;
		if (!Cr->Recipes.IsValidIndex(i))
		{
			break;
		}
		const FWLRecipeView& Rc = Cr->Recipes[i];
		const bool bSel = Cr->SelectedIndex == i;
		const bool bHover = IsMouseIn(X + S(6), CY, ListW, RowH);
		Fill(X + S(6), CY, ListW, RowH, bSel ? Theme->SelectedRow : (bHover ? Theme->HeaderTop : Theme->TileTint));
		const FLinearColor C = !Rc.bLevelMet ? Theme->Bad : (Rc.bCanMake ? Theme->Good : Theme->Text);
		Label(Fit(Fmt(Rc.DisplayName), ListW - S(44)), X + S(10), CY + S(3), C);
		const FString Lv = FString::Printf(TEXT("Lv%d"), Rc.LevelRequired);
		Label(Lv, X + S(6) + ListW - Measure(Lv).X - S(4), CY + S(3), Rc.bLevelMet ? Theme->TextDim : Theme->Bad);
		AddHitBox(X + S(6), CY, ListW, RowH, [Cr, i]() { Cr->Select(i); }, nullptr, Rc.Reason);
		CY += RowH + S(2);
	}
	if (Cr->Recipes.Num() == 0)
	{
		Label(TEXT("Nothing to make here."), X + S(10), TopY, Theme->TextDim);
	}

	// Details (right column).
	const float DX = X + S(6) + ListW + S(10);
	const float DW = W - (DX - X) - S(8);
	CY = TopY;
	if (!Cr->Recipes.IsValidIndex(Cr->SelectedIndex))
	{
		Label(TEXT("Select an item to make"), DX, CY, Theme->TextDim);
		return;
	}
	const FWLRecipeView& Sel = Cr->Recipes[Cr->SelectedIndex];
	Label(Fit(Fmt(Sel.DisplayName), DW), DX, CY, Theme->Amber);
	CY += L + S(2);
	Label(FString::Printf(TEXT("Level %d   (%d XP each)"), Sel.LevelRequired, Sel.XPEach), DX, CY, Sel.bLevelMet ? Theme->Text : Theme->Bad);
	CY += L + S(6);
	Label(TEXT("Requires:"), DX, CY, Theme->TextDim);
	CY += L;
	for (int32 k = 0; k < Sel.InputLines.Num(); ++k)
	{
		const bool bMet = Sel.InputMet.IsValidIndex(k) && Sel.InputMet[k];
		Label(Fit(Sel.InputLines[k].ToString(), DW), DX + S(6), CY, bMet ? Theme->Good : Theme->Bad);
		CY += L;
	}
	if (!Sel.Reason.IsEmpty())
	{
		CY += S(4);
		for (const FString& Line : Wrap(Sel.Reason.ToString(), DW))
		{
			Label(Line, DX, CY, Theme->Bad);
			CY += L;
		}
	}
	// Quantity: 1 / 5 / 10 / All, then the Make button.
	const float QY = Y + H - RowH * 2 - S(16);
	const TArray<int32>& Options = UWLCraftingViewModel::QuantityOptions();
	const float QW = (DW - S(3) * (Options.Num() - 1)) / FMath::Max(1, Options.Num());
	for (int32 q = 0; q < Options.Num(); ++q)
	{
		const int32 Opt = Options[q];
		Button(Opt < 0 ? FString(TEXT("All")) : FString::FromInt(Opt), DX + q * (QW + S(3)), QY, QW, RowH,
			[Cr, Opt]() { Cr->SetQuantity(Opt); }, Cr->Quantity == Opt);
	}
	const bool bCan = Sel.bCanMake && !Cr->bBusy;
	Button(Cr->bBusy ? FString(TEXT("Working...")) : Cr->GetCraftButtonLabel().ToString(), DX, QY + RowH + S(6), DW, RowH,
		[Cr]() { Cr->Craft(); }, bCan);
}

// -------------------------------------------------------------------------------------------------
// Bank (BankUI)
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawBank(UWLUISubsystem& UI)
{
	UWLBankViewModel* B = UI.GetBank();
	if (!B || !B->bOpen)
	{
		BankScroll = 0;
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float L = LineH(), RowH = L + S(6);
	const float W = S(460), H = S(430);
	const float X = Canvas->ClipX * 0.5f - W - S(10);
	const float Y = Canvas->ClipY * 0.5f - H * 0.5f;
	const float TopY = Panel(X, Y, W, H, FString::Printf(TEXT("Bank  %d / %d"), B->UsedSlots, B->Capacity));
	Button(TEXT("X"), X + W - S(24), Y + S(3), S(20), L + S(2), [B]() { B->Close(); });

	constexpr int32 Cols = 5;
	const float Pad = S(6), Gap = S(3);
	const float CellW = (W - Pad * 2 - Gap * (Cols - 1)) / Cols;
	const float CellH = L * 2.f + S(4);
	const float GridH = H - (TopY - Y) - RowH - S(16);
	const int32 VisibleRows = FMath::Max(1, FMath::FloorToInt(GridH / (CellH + Gap)));
	const int32 TotalRows = (B->Items.Num() + Cols - 1) / Cols;
	if (IsMouseIn(X, TopY, W, GridH))
	{
		BankScroll += WheelDelta();
	}
	BankScroll = FMath::Clamp(BankScroll, 0, FMath::Max(0, TotalRows - VisibleRows));
	for (int32 r = 0; r < VisibleRows; ++r)
	{
		for (int32 c = 0; c < Cols; ++c)
		{
			const int32 i = (BankScroll + r) * Cols + c;
			if (!B->Items.IsValidIndex(i))
			{
				break;
			}
			const FWLItemSlotView& It = B->Items[i];
			const float SX = X + Pad + c * (CellW + Gap);
			const float SY = TopY + r * (CellH + Gap);
			Fill(SX, SY, CellW, CellH, IsMouseIn(SX, SY, CellW, CellH) ? Theme->HeaderTop : Theme->TileTint);
			Frame(SX, SY, CellW, CellH, Theme->Rim);
			Label(Fit(Fmt(It.DisplayName), CellW - S(4), 0.85f), SX + S(2), SY + S(1), Theme->Text, 0.85f);
			Label(FString::Printf(TEXT("x%d"), It.Quantity), SX + S(2), SY + L, Theme->Amber, 0.85f);
			const int32 BankSlot = It.SlotIndex;
			AddHitBox(SX, SY, CellW, CellH, [B, BankSlot]() { B->Withdraw(BankSlot, 1); }, [B, BankSlot]() { B->Withdraw(BankSlot, 0); },
				FText::Format(LOCTEXT("BankTip", "{0} x{1}\nClick: withdraw 1, right-click: withdraw all"), It.DisplayName, FText::AsNumber(It.Quantity)));
		}
	}
	if (B->Items.Num() == 0)
	{
		Label(TEXT("Your bank is empty. Click inventory items to deposit them."), X + Pad, TopY, Theme->TextDim);
	}
	Button(TEXT("Deposit inventory"), X + Pad, Y + H - RowH - S(8), W - Pad * 2, RowH, [B]() { B->DepositAll(); });
}

// -------------------------------------------------------------------------------------------------
// Dialogue (DialogueUI) + starter beast picker
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawDialogue(UWLUISubsystem& UI)
{
	UWLDialogueViewModel* D = UI.GetDialogue();
	if (!D || !D->bActive)
	{
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float L = LineH(), RowH = L + S(6);
	const float W = FMath::Min(S(620), Canvas->ClipX - S(40));
	const TArray<FString> Lines = Wrap(D->Line.ToString(), W - S(20));
	const int32 NumChoices = D->Choices.Num();
	const float H = L + S(10) + Lines.Num() * L + S(8) + FMath::Max(1, NumChoices) * (RowH + S(2)) + S(10);
	const float X = Canvas->ClipX * 0.5f - W * 0.5f;
	const float Y = Canvas->ClipY - H - S(150);
	float CY = Panel(X, Y, W, H, Fmt(D->Speaker));
	Button(TEXT("X"), X + W - S(24), Y + S(3), S(20), L + S(2), [D]() { D->End(); });
	for (const FString& Line : Lines)
	{
		Label(Line, X + S(10), CY, Theme->Text);
		CY += L;
	}
	CY += S(8);
	if (NumChoices > 0)
	{
		for (int32 i = 0; i < NumChoices; ++i)
		{
			const FString Text = FString::Printf(TEXT("%d. %s"), i + 1, *D->Choices[i].ToString());
			Button(Text, X + S(10), CY, W - S(20), RowH, [D, i]() { D->Choose(i); });
			CY += RowH + S(2);
		}
	}
	else
	{
		Button(D->ContinuePrompt.IsEmpty() ? FString(TEXT("Click here to continue")) : D->ContinuePrompt.ToString(),
			X + S(10), CY, W - S(20), RowH, [D]() { D->Continue(); }, true);
	}
}

void AWLDebugHUD::DrawEggChoice(UWLUISubsystem& UI)
{
	UWLDialogueViewModel* D = UI.GetDialogue();
	if (!D || !D->bEggChoiceOpen)
	{
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float L = LineH(), RowH = L + S(10);
	const float W = S(360), H = L + S(10) + L + S(8) + 3 * (RowH + S(4)) + S(10);
	const float X = Canvas->ClipX * 0.5f - W * 0.5f;
	const float Y = Canvas->ClipY * 0.5f - H * 0.5f;
	float CY = Panel(X, Y, W, H, TEXT("Choose your companion"));
	Button(TEXT("X"), X + W - S(24), Y + S(3), S(20), L + S(2), [D]() { D->CloseEggChoice(); });
	Label(TEXT("Pick the egg you'll raise:"), X + S(10), CY, Theme->Text);
	CY += L + S(8);
	struct FEgg { const TCHAR* Label; const TCHAR* Species; };
	const FEgg Eggs[] = { { TEXT("1. Wolf"), TEXT("wolf") }, { TEXT("2. Turtle"), TEXT("turtle") }, { TEXT("3. Bird"), TEXT("bird") } };
	for (const FEgg& E : Eggs)
	{
		const FName Species(E.Species);
		Button(E.Label, X + S(10), CY, W - S(20), RowH, [D, Species]() { D->ChooseStarterBeast(Species); });
		CY += RowH + S(4);
	}
}

// -------------------------------------------------------------------------------------------------
// Context menu (ContextMenuUI) - drawn last so it is top-most
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawContextMenu(UWLUISubsystem& UI)
{
	UWLContextMenuViewModel* M = UI.GetContextMenu();
	if (!M || !M->bOpen)
	{
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float L = LineH(), RowH = L + S(4);
	float W = Measure(M->Title.ToString()).X;
	for (const FWLMenuEntry& E : M->Entries)
	{
		W = FMath::Max(W, Measure(E.Label.ToString()).X);
	}
	W += S(20);
	const float H = (M->Entries.Num() + 1) * RowH + S(6);
	const FVector2D Pos = FWLUILogic::PlaceMenu(M->ScreenPosition, FVector2D(W, H), FVector2D(Canvas->ClipX, Canvas->ClipY));
	const float X = static_cast<float>(Pos.X), Y = static_cast<float>(Pos.Y);

	// Any click outside closes the menu and is consumed (ContextMenuUI.ConsumedClick).
	AddHitBox(0.f, 0.f, Canvas->ClipX, Canvas->ClipY, [M]() { M->Close(); }, [M]() { M->Close(); });

	Fill(X - 1, Y - 1, W + 2, H + 2, Theme->Rim);
	Fill(X, Y, W, H, Theme->Outer);
	Fill(X, Y, W, RowH, Theme->HeaderBottom);
	Label(M->Title.ToString(), X + S(6), Y + S(2), Theme->Amber);
	float CY = Y + RowH + S(3);
	for (int32 i = 0; i < M->Entries.Num(); ++i)
	{
		const bool bHover = IsMouseIn(X, CY, W, RowH);
		if (bHover)
		{
			Fill(X, CY, W, RowH, Theme->SelectedRow);
		}
		Label(M->Entries[i].Label.ToString(), X + S(6), CY + S(2), bHover ? Theme->Amber : Theme->Text);
		AddHitBox(X, CY, W, RowH, [M, i]() { M->Choose(i); }, [M, i]() { M->Choose(i); });
		CY += RowH;
	}
}

// -------------------------------------------------------------------------------------------------
// New-game splash (SplashScreen)
// -------------------------------------------------------------------------------------------------

void AWLDebugHUD::DrawSplash(float DeltaSeconds)
{
	constexpr float HoldSeconds = 2.0f;
	constexpr float FadeSeconds = 0.6f;
	if (!bSplashChecked)
	{
		// Wait for the pawn so the splash covers the drop into the world, not the loading frames.
		const AWLPlayerController* PC = Cast<AWLPlayerController>(PlayerOwner.Get());
		if (!PC || !PC->GetWLCharacter())
		{
			return;
		}
		bSplashChecked = true;
		UWLFrontendSubsystem* FE = UWLFrontendSubsystem::Get(this);
		if (FE && FE->ConsumePendingSplash())
		{
			SplashAge = 0.f;
		}
	}
	if (SplashAge < 0.f)
	{
		return;
	}
	SplashAge += DeltaSeconds;
	// Any click / key after a beat skips straight to the fade.
	if (!bSplashFading && SplashAge >= 0.25f &&
		(WasJustPressed(EKeys::LeftMouseButton) || WasJustPressed(EKeys::SpaceBar) || WasJustPressed(EKeys::Enter) || WasJustPressed(EKeys::Escape)))
	{
		bSplashFading = true;
		SplashAge = HoldSeconds;
	}
	if (SplashAge >= HoldSeconds)
	{
		bSplashFading = true;
	}
	const float Alpha = SplashAge < HoldSeconds ? 1.f : FMath::Clamp(1.f - (SplashAge - HoldSeconds) / FadeSeconds, 0.f, 1.f);
	if (Alpha <= 0.f)
	{
		SplashAge = -1.f;
		return;
	}
	Fill(0.f, 0.f, Canvas->ClipX, Canvas->ClipY, FLinearColor(0.f, 0.f, 0.f, Alpha));
	AddHitBox(0.f, 0.f, Canvas->ClipX, Canvas->ClipY, [this]() { bSplashFading = true; SplashAge = FMath::Max(SplashAge, HoldSeconds); }, nullptr);
	const float CX = Canvas->ClipX * 0.5f, CY = Canvas->ClipY * 0.5f;
	LabelCentered(TEXT("WASTELANDERS"), CX, CY - LineH() * 3.f, FLinearColor(1.f, 0.85f, 0.4f, Alpha), 4.f);
	LabelCentered(TEXT("NEW GAME"), CX, CY + LineH() * 3.f, FLinearColor(0.9f, 0.95f, 1.f, Alpha), 2.f);
	LabelCentered(TEXT("click to continue"), CX, CY + LineH() * 5.5f, FLinearColor(0.75f, 0.75f, 0.78f, Alpha), 1.f);
}

#undef LOCTEXT_NAMESPACE
