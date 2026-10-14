#include "Frontend/WLFrontendGameMode.h"
#include "Frontend/WLFrontendSubsystem.h"
#include "UI/WLUIThemeSettings.h"
#include "Engine/Canvas.h"
#include "Engine/Engine.h"
#include "Engine/GameViewportClient.h"
#include "Framework/Application/SlateApplication.h"
#include "Widgets/Input/SEditableTextBox.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/SBoxPanel.h"
#include "Widgets/Text/STextBlock.h"

#define LOCTEXT_NAMESPACE "WLFrontend"

AWLFrontendGameMode::AWLFrontendGameMode()
{
	DefaultPawnClass = nullptr;
	PlayerControllerClass = AWLFrontendPlayerController::StaticClass();
	HUDClass = AWLFrontendHUD::StaticClass();
}

AWLFrontendPlayerController::AWLFrontendPlayerController()
{
	bShowMouseCursor = true;
	bEnableClickEvents = true;
	bEnableMouseOverEvents = true;
}

void AWLFrontendPlayerController::BeginPlay()
{
	Super::BeginPlay();
	if (IsLocalController())
	{
		// Game-and-UI so the canvas can read the mouse while the Slate text boxes still take keys.
		FInputModeGameAndUI Mode;
		Mode.SetLockMouseToViewportBehavior(EMouseLockMode::DoNotLock);
		SetInputMode(Mode);
	}
}

void AWLFrontendHUD::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	RemoveTextBoxes();
	Super::EndPlay(EndPlayReason);
}

void AWLFrontendHUD::EnsureTextBoxes()
{
	if (Container.IsValid() || !GEngine || !GEngine->GameViewport || !FSlateApplication::IsInitialized())
	{
		return;
	}
	UWLFrontendSubsystem* FE = UWLFrontendSubsystem::Get(this);
	if (!FE)
	{
		return;
	}
	TWeakObjectPtr<UWLFrontendSubsystem> WeakFE(FE);
	SAssignNew(NameBox, SEditableTextBox)
		.Text(FText::FromString(FE->CharacterName))
		.HintText(LOCTEXT("NameHint", "Character name"))
		.OnTextChanged_Lambda([WeakFE](const FText& T)
		{
			if (UWLFrontendSubsystem* S = WeakFE.Get()) { S->CharacterName = T.ToString().Left(20); }
		});
	SAssignNew(AddressBox, SEditableTextBox)
		.Text(FText::FromString(FE->ServerAddress))
		.HintText(LOCTEXT("AddrHint", "host:port"))
		.OnTextChanged_Lambda([WeakFE](const FText& T)
		{
			if (UWLFrontendSubsystem* S = WeakFE.Get()) { S->ServerAddress = T.ToString().TrimStartAndEnd(); }
		});
	const float W = S(320);
	Container = SNew(SBox)
		.Visibility(EVisibility::SelfHitTestInvisible)
		.HAlign(HAlign_Center)
		.VAlign(VAlign_Top)
		.Padding(FMargin(0.f, S(70), 0.f, 0.f))
		[
			SNew(SBox).WidthOverride(W)
			[
				SNew(SVerticalBox)
				+ SVerticalBox::Slot().AutoHeight().Padding(0.f, 0.f, 0.f, S(6))[ NameBox.ToSharedRef() ]
				+ SVerticalBox::Slot().AutoHeight()[ AddressBox.ToSharedRef() ]
			]
		];
	GEngine->GameViewport->AddViewportWidgetContent(Container.ToSharedRef(), 100);
	FSlateApplication::Get().SetKeyboardFocus(NameBox, EFocusCause::SetDirectly);
}

void AWLFrontendHUD::RemoveTextBoxes()
{
	if (Container.IsValid() && GEngine && GEngine->GameViewport)
	{
		GEngine->GameViewport->RemoveViewportWidgetContent(Container.ToSharedRef());
	}
	Container.Reset();
	NameBox.Reset();
	AddressBox.Reset();
}

void AWLFrontendHUD::DrawRow(UWLFrontendSubsystem& FE, FName Row, float X, float& Y, float W)
{
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	const float H = LineH() + S(8);
	const float Btn = H;
	Label(UWLFrontendSubsystem::RowLabel(Row), X, Y + S(4), Theme->Text);
	const float ValueX = X + W * 0.55f;
	TWeakObjectPtr<UWLFrontendSubsystem> WeakFE(&FE);
	Button(TEXT("-"), ValueX, Y, Btn, H, [WeakFE, Row]() { if (UWLFrontendSubsystem* S = WeakFE.Get()) { S->StepRow(Row, -1); } });
	LabelCentered(FE.GetRowValue(Row), ValueX + Btn + S(30), Y + S(4), Theme->Amber);
	Button(TEXT("+"), ValueX + Btn + S(60), Y, Btn, H, [WeakFE, Row]() { if (UWLFrontendSubsystem* S = WeakFE.Get()) { S->StepRow(Row, 1); } });
	Y += H + S(4);
}

void AWLFrontendHUD::DrawCanvas()
{
	UWLFrontendSubsystem* FE = UWLFrontendSubsystem::Get(this);
	if (!Canvas || !FE)
	{
		return;
	}
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	if (!bInitialised)
	{
		bInitialised = true;
		Random.GenerateNewSeed();
		FE->Randomise(Random);   // Unity starts the creator on a complete character, not an empty rig
	}
	EnsureTextBoxes();

	Fill(0.f, 0.f, Canvas->ClipX, Canvas->ClipY, FLinearColor(0.02f, 0.02f, 0.025f, 1.f));
	const float CX = Canvas->ClipX * 0.5f;
	LabelCentered(TEXT("WASTELANDERS"), CX, S(20), Theme->Amber, 3.f);
	LabelCentered(TEXT("Name and server (type above), then shape your survivor"), CX, S(60) + LineH() * 0.f, Theme->TextDim);

	// Panel sits below the Slate boxes (two boxes ~ S(70)+S(60)).
	const float PW = S(520);
	const float PX = CX - PW * 0.5f;
	const float PY = S(70) + S(80);
	const float PH = Canvas->ClipY - PY - S(20);
	float Y = Panel(PX, PY, PW, PH, TEXT("Character")) + S(8);
	const float X = PX + S(14);
	const float W = PW - S(28);

	// Gender (CharacterSelectUI): required before Start.
	Label(TEXT("Gender"), X, Y + S(4), Theme->Text);
	TWeakObjectPtr<UWLFrontendSubsystem> WeakFE(FE);
	const float GH = LineH() + S(8);
	Button(TEXT("Male"), X + W * 0.40f, Y, S(80), GH, [WeakFE]() { if (UWLFrontendSubsystem* S = WeakFE.Get()) { S->SetGender(TEXT("Male")); } }, FE->GetGender() == TEXT("Male"));
	Button(TEXT("Female"), X + W * 0.40f + S(88), Y, S(80), GH, [WeakFE]() { if (UWLFrontendSubsystem* S = WeakFE.Get()) { S->SetGender(TEXT("Female")); } }, FE->GetGender() == TEXT("Female"));
	Y += GH + S(10);

	for (const FName& R : UWLFrontendSubsystem::GetPartRows()) { DrawRow(*FE, R, X, Y, W); }
	Y += S(4);
	for (const FName& R : UWLFrontendSubsystem::GetSliderRows()) { DrawRow(*FE, R, X, Y, W); }
	Y += S(4);
	for (const FName& R : UWLFrontendSubsystem::GetColorRows()) { DrawRow(*FE, R, X, Y, W); }
	Y += S(10);

	Button(TEXT("Randomise"), X, Y, S(120), GH, [this, WeakFE]()
	{
		if (UWLFrontendSubsystem* S = WeakFE.Get()) { S->Randomise(Random); }
	});

	const bool bCan = FE->CanJoin();
	Button(bCan ? TEXT("Start") : TEXT("Start (name + gender needed)"), X + W - S(260), Y, S(260), GH, [this, WeakFE]()
	{
		UWLFrontendSubsystem* S = WeakFE.Get();
		if (!S) { return; }
		if (!S->Join())
		{
			Status = TEXT("Pick a name (2+ letters) and a gender first.");
		}
	}, bCan);
	Y += GH + S(8);
	if (!Status.IsEmpty())
	{
		Label(Status, X, Y, Theme->Alert);
	}
}

#undef LOCTEXT_NAMESPACE
