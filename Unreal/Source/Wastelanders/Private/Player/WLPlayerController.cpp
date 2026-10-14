#include "Player/WLPlayerController.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "Player/WLCharacterMovementComponent.h"
#include "Player/WLChatCommands.h"
#include "Interaction/WLInteractable.h"
#include "Interaction/WLInteractorComponent.h"
#include "Companions/WLCompanionComponent.h"
#include "EnhancedInputComponent.h"
#include "EnhancedInputSubsystems.h"
#include "InputAction.h"
#include "InputMappingContext.h"
#include "InputModifiers.h"
#include "NavigationSystem.h"
#include "NavigationPath.h"
#include "GameFramework/GameStateBase.h"
#include "Wastelanders.h"

AWLPlayerController::AWLPlayerController()
{
	bShowMouseCursor = true;
	DefaultMouseCursor = EMouseCursor::Default;
	DefaultClickTraceChannel = ECC_Visibility;
}

AWLPlayerCharacter* AWLPlayerController::GetWLCharacter() const
{
	return Cast<AWLPlayerCharacter>(GetPawn());
}

AWLPlayerState* AWLPlayerController::GetWLPlayerState() const
{
	return GetPlayerState<AWLPlayerState>();
}

void AWLPlayerController::BeginPlay()
{
	Super::BeginPlay();

	if (IsLocalController())
	{
		FInputModeGameAndUI Mode;
		Mode.SetHideCursorDuringCapture(false);
		Mode.SetLockMouseToViewportBehavior(EMouseLockMode::DoNotLock);
		SetInputMode(Mode);
	}
}

static UInputAction* MakeAction(UObject* Outer, const TCHAR* Name, EInputActionValueType Type)
{
	UInputAction* Action = NewObject<UInputAction>(Outer, Name);
	Action->ValueType = Type;
	return Action;
}

void AWLPlayerController::BuildDefaultInput()
{
	if (DefaultMappingContext)
	{
		return;
	}

	UInputMappingContext* Ctx = NewObject<UInputMappingContext>(this, TEXT("IMC_WLDefault"));
	MoveAction = MakeAction(this, TEXT("IA_Move"), EInputActionValueType::Axis2D);
	PrimaryClickAction = MakeAction(this, TEXT("IA_PrimaryClick"), EInputActionValueType::Boolean);
	SecondaryClickAction = MakeAction(this, TEXT("IA_SecondaryClick"), EInputActionValueType::Boolean);
	OrbitKeysAction = MakeAction(this, TEXT("IA_OrbitKeys"), EInputActionValueType::Axis1D);
	OrbitDragAction = MakeAction(this, TEXT("IA_OrbitDrag"), EInputActionValueType::Axis2D);
	OrbitDragEnableAction = MakeAction(this, TEXT("IA_OrbitDragEnable"), EInputActionValueType::Boolean);
	ZoomAction = MakeAction(this, TEXT("IA_Zoom"), EInputActionValueType::Axis1D);
	RunAction = MakeAction(this, TEXT("IA_Run"), EInputActionValueType::Boolean);
	DodgeAction = MakeAction(this, TEXT("IA_Dodge"), EInputActionValueType::Boolean);
	CompanionToggleAction = MakeAction(this, TEXT("IA_CompanionToggle"), EInputActionValueType::Boolean);
	CompanionSpecialAction = MakeAction(this, TEXT("IA_CompanionSpecial"), EInputActionValueType::Boolean);

	auto Negate = [this](FEnhancedActionKeyMapping& M) { M.Modifiers.Add(NewObject<UInputModifierNegate>(this)); };
	auto Swizzle = [this](FEnhancedActionKeyMapping& M) { M.Modifiers.Add(NewObject<UInputModifierSwizzleAxis>(this)); };

	// WASD -> X = right, Y = forward
	Swizzle(Ctx->MapKey(MoveAction, EKeys::W));
	{ FEnhancedActionKeyMapping& M = Ctx->MapKey(MoveAction, EKeys::S); Swizzle(M); Negate(M); }
	Ctx->MapKey(MoveAction, EKeys::D);
	Negate(Ctx->MapKey(MoveAction, EKeys::A));
	Ctx->MapKey(MoveAction, EKeys::Gamepad_Left2D);

	Ctx->MapKey(PrimaryClickAction, EKeys::LeftMouseButton);
	Ctx->MapKey(SecondaryClickAction, EKeys::RightMouseButton);

	Ctx->MapKey(OrbitKeysAction, EKeys::E);
	Negate(Ctx->MapKey(OrbitKeysAction, EKeys::Q));
	Ctx->MapKey(OrbitKeysAction, EKeys::Gamepad_RightX);

	Ctx->MapKey(OrbitDragAction, EKeys::Mouse2D);
	Ctx->MapKey(OrbitDragEnableAction, EKeys::MiddleMouseButton);

	Ctx->MapKey(ZoomAction, EKeys::MouseWheelAxis);

	Ctx->MapKey(RunAction, EKeys::LeftControl);
	Ctx->MapKey(RunAction, EKeys::Gamepad_LeftThumbstick);
	Ctx->MapKey(DodgeAction, EKeys::SpaceBar);
	Ctx->MapKey(DodgeAction, EKeys::Gamepad_FaceButton_Right);
	Ctx->MapKey(CompanionToggleAction, EKeys::B);
	Ctx->MapKey(CompanionSpecialAction, EKeys::H);

	DefaultMappingContext = Ctx;
}

void AWLPlayerController::SetupInputComponent()
{
	Super::SetupInputComponent();

	BuildDefaultInput();

	if (ULocalPlayer* LP = GetLocalPlayer())
	{
		if (UEnhancedInputLocalPlayerSubsystem* Subsystem = LP->GetSubsystem<UEnhancedInputLocalPlayerSubsystem>())
		{
			Subsystem->AddMappingContext(DefaultMappingContext, 0);
		}
	}

	UEnhancedInputComponent* EIC = Cast<UEnhancedInputComponent>(InputComponent);
	if (!EIC)
	{
		UE_LOG(LogWastelanders, Error, TEXT("Enhanced Input component missing; check DefaultInput.ini"));
		return;
	}

	EIC->BindAction(MoveAction, ETriggerEvent::Triggered, this, &AWLPlayerController::OnMove);
	EIC->BindAction(PrimaryClickAction, ETriggerEvent::Started, this, &AWLPlayerController::OnPrimaryClick);
	EIC->BindAction(SecondaryClickAction, ETriggerEvent::Started, this, &AWLPlayerController::OnSecondaryClick);
	EIC->BindAction(OrbitKeysAction, ETriggerEvent::Triggered, this, &AWLPlayerController::OnOrbitKeys);
	EIC->BindAction(OrbitDragAction, ETriggerEvent::Triggered, this, &AWLPlayerController::OnOrbitDrag);
	EIC->BindAction(OrbitDragEnableAction, ETriggerEvent::Started, this, &AWLPlayerController::OnOrbitDragEnable);
	EIC->BindAction(OrbitDragEnableAction, ETriggerEvent::Completed, this, &AWLPlayerController::OnOrbitDragDisable);
	EIC->BindAction(ZoomAction, ETriggerEvent::Triggered, this, &AWLPlayerController::OnZoom);
	EIC->BindAction(RunAction, ETriggerEvent::Started, this, &AWLPlayerController::OnRunStart);
	EIC->BindAction(RunAction, ETriggerEvent::Completed, this, &AWLPlayerController::OnRunStop);
	EIC->BindAction(DodgeAction, ETriggerEvent::Started, this, &AWLPlayerController::OnDodge);
	EIC->BindAction(CompanionToggleAction, ETriggerEvent::Started, this, &AWLPlayerController::OnCompanionToggle);
	EIC->BindAction(CompanionSpecialAction, ETriggerEvent::Started, this, &AWLPlayerController::OnCompanionSpecial);
}

void AWLPlayerController::OnMove(const FInputActionValue& Value)
{
	AWLPlayerCharacter* Char = GetWLCharacter();
	if (!Char)
	{
		return;
	}
	const FVector2D Input = Value.Get<FVector2D>();
	if (Input.IsNearlyZero())
	{
		return;
	}

	// Manual movement cancels click-to-move and any queued interaction (Unity behaviour).
	StopPathFollowing();
	if (UWLInteractorComponent* Interactor = Char->GetInteractor())
	{
		Interactor->CancelPendingInteraction();
	}

	// Camera-relative, flattened to the ground plane.
	const FRotator CamYaw(0.f, Char->GetCameraBoom()->GetComponentRotation().Yaw, 0.f);
	const FVector Forward = FRotationMatrix(CamYaw).GetUnitAxis(EAxis::X);
	const FVector Right = FRotationMatrix(CamYaw).GetUnitAxis(EAxis::Y);
	Char->AddMovementInput(Forward, Input.Y);
	Char->AddMovementInput(Right, Input.X);
}

void AWLPlayerController::OnPrimaryClick(const FInputActionValue&)
{
	FHitResult Hit;
	if (!GetHitResultUnderCursor(ECC_Visibility, false, Hit))
	{
		return;
	}

	AWLPlayerCharacter* Char = GetWLCharacter();
	AActor* HitActor = Hit.GetActor();
	if (Char && HitActor && HitActor->Implements<UWLInteractable>())
	{
		// Default option = first option flagged default, else the first one.
		const TArray<FWLInteractOption> Options = IWLInteractable::Execute_GetInteractOptions(HitActor, Char);
		const FWLInteractOption* Chosen = Options.FindByPredicate([](const FWLInteractOption& O) { return O.bDefault; });
		if (!Chosen && Options.Num() > 0)
		{
			Chosen = &Options[0];
		}
		if (Chosen)
		{
			StopPathFollowing();
			Char->GetInteractor()->RequestInteract(HitActor, Chosen->Verb);
			return;
		}
	}

	if (Char)
	{
		Char->GetInteractor()->CancelPendingInteraction();
	}
	MoveToLocation(Hit.ImpactPoint);
}

void AWLPlayerController::OnSecondaryClick(const FInputActionValue&)
{
	FHitResult Hit;
	if (GetHitResultUnderCursor(ECC_Visibility, false, Hit) && Hit.GetActor() && Hit.GetActor()->Implements<UWLInteractable>())
	{
		float X = 0.f, Y = 0.f;
		GetMousePosition(X, Y);
		OnContextMenuRequested.Broadcast(Hit.GetActor(), FVector2D(X, Y));
	}
}

void AWLPlayerController::OnOrbitKeys(const FInputActionValue& Value)
{
	if (AWLPlayerCharacter* Char = GetWLCharacter())
	{
		Char->AddCameraYaw(Value.Get<float>() * KeyOrbitSpeed * GetWorld()->GetDeltaSeconds());
	}
}

void AWLPlayerController::OnOrbitDrag(const FInputActionValue& Value)
{
	if (!bOrbitDragging)
	{
		return;
	}
	if (AWLPlayerCharacter* Char = GetWLCharacter())
	{
		const FVector2D Delta = Value.Get<FVector2D>();
		Char->AddCameraYaw(Delta.X * MouseOrbitSpeed);
		Char->AddCameraPitch(-Delta.Y * MouseOrbitSpeed);
	}
}

void AWLPlayerController::OnOrbitDragEnable(const FInputActionValue&) { bOrbitDragging = true; }
void AWLPlayerController::OnOrbitDragDisable(const FInputActionValue&) { bOrbitDragging = false; }

void AWLPlayerController::OnZoom(const FInputActionValue& Value)
{
	if (AWLPlayerCharacter* Char = GetWLCharacter())
	{
		Char->AddCameraZoom(Value.Get<float>());
	}
}

void AWLPlayerController::OnRunStart(const FInputActionValue&)
{
	if (AWLPlayerCharacter* Char = GetWLCharacter())
	{
		Char->GetWLMovement()->SetWantsToRun(true);
	}
}

void AWLPlayerController::OnRunStop(const FInputActionValue&)
{
	if (AWLPlayerCharacter* Char = GetWLCharacter())
	{
		Char->GetWLMovement()->SetWantsToRun(false);
	}
}

void AWLPlayerController::OnDodge(const FInputActionValue&)
{
	if (AWLPlayerCharacter* Char = GetWLCharacter())
	{
		Char->GetWLMovement()->RequestDodge();
	}
}

void AWLPlayerController::OnCompanionToggle(const FInputActionValue&)
{
	if (AWLPlayerState* PS = GetWLPlayerState())
	{
		PS->GetCompanions()->RequestToggle();
	}
}

void AWLPlayerController::OnCompanionSpecial(const FInputActionValue&)
{
	if (AWLPlayerState* PS = GetWLPlayerState())
	{
		PS->GetCompanions()->RequestSpecial();
	}
}

void AWLPlayerController::MoveToLocation(const FVector& Destination, float AcceptanceRadius)
{
	PathPoints.Reset();
	PathIndex = 0;
	PathAcceptance = AcceptanceRadius;

	APawn* MyPawn = GetPawn();
	if (!MyPawn)
	{
		return;
	}

	// Client-side path (needs bAllowClientSideNavigation). Movement itself stays CMC-predicted.
	if (UNavigationSystemV1* Nav = FNavigationSystem::GetCurrent<UNavigationSystemV1>(GetWorld()))
	{
		if (UNavigationPath* Path = Nav->FindPathToLocationSynchronously(GetWorld(), MyPawn->GetActorLocation(), Destination, MyPawn))
		{
			if (Path->IsValid() && Path->PathPoints.Num() > 1)
			{
				PathPoints = Path->PathPoints;
				PathIndex = 1;
				return;
			}
		}
	}

	// No navmesh: walk straight there.
	PathPoints = { MyPawn->GetActorLocation(), Destination };
	PathIndex = 1;
}

void AWLPlayerController::StopPathFollowing()
{
	PathPoints.Reset();
	PathIndex = 0;
}

void AWLPlayerController::PlayerTick(float DeltaTime)
{
	Super::PlayerTick(DeltaTime);
	FollowPath();
}

void AWLPlayerController::FollowPath()
{
	APawn* MyPawn = GetPawn();
	if (!MyPawn || PathPoints.Num() == 0)
	{
		return;
	}

	const FVector Loc = MyPawn->GetActorLocation();
	while (PathPoints.IsValidIndex(PathIndex))
	{
		const bool bLast = PathIndex == PathPoints.Num() - 1;
		const float Accept = bLast ? PathAcceptance : 60.f;
		if (FVector::Dist2D(Loc, PathPoints[PathIndex]) > Accept)
		{
			break;
		}
		++PathIndex;
	}

	if (!PathPoints.IsValidIndex(PathIndex))
	{
		StopPathFollowing();
		return;
	}

	const FVector Dir = (PathPoints[PathIndex] - Loc).GetSafeNormal2D();
	MyPawn->AddMovementInput(Dir, 1.f);
}

void AWLPlayerController::ClientGameMessage_Implementation(const FText& Message)
{
	OnGameMessage.Broadcast(Message);
}

void AWLPlayerController::SubmitChat(const FString& Text)
{
	const FString Trimmed = Text.TrimStartAndEnd();
	if (!Trimmed.IsEmpty())
	{
		ServerChat(Trimmed.Left(240));
	}
}

void AWLPlayerController::ServerChat_Implementation(const FString& Text)
{
	if (Text.StartsWith(TEXT("/")))
	{
		FWLChatCommands::Execute(this, Text.Mid(1));
		return;
	}

	// Local chat: broadcast to every player. A proximity/channel system replaces this later.
	const FText Line = FText::Format(NSLOCTEXT("WL", "ChatLine", "{0}: {1}"),
		FText::FromString(PlayerState ? PlayerState->GetPlayerName() : TEXT("?")), FText::FromString(Text));
	if (AGameStateBase* GS = GetWorld()->GetGameState())
	{
		for (APlayerState* PS : GS->PlayerArray)
		{
			if (AWLPlayerController* PC = PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr)
			{
				PC->ClientGameMessage(Line);
			}
		}
	}
}
