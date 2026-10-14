#include "Core/WLGameMode.h"
#include "Core/WLGameState.h"
#include "Core/WLGameInstance.h"
#include "Frontend/WLFrontendSubsystem.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerCharacter.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Persistence/WLPersistenceSubsystem.h"
#include "Persistence/WLCharacterSave.h"
#include "Kismet/GameplayStatics.h"
#include "GameFramework/PlayerStart.h"
#include "TimerManager.h"
#include "Engine/World.h"
#include "Wastelanders.h"

AWLGameMode::AWLGameMode()
{
	DefaultPawnClass = AWLPlayerCharacter::StaticClass();
	PlayerControllerClass = AWLPlayerController::StaticClass();
	PlayerStateClass = AWLPlayerState::StaticClass();
	GameStateClass = AWLGameState::StaticClass();
	bUseSeamlessTravel = true;
}

void AWLGameMode::InitGame(const FString& MapName, const FString& Options, FString& ErrorMessage)
{
	Super::InitGame(MapName, Options, ErrorMessage);
	GetWorldTimerManager().SetTimer(AutosaveTimer, this, &AWLGameMode::SaveAllPlayers, AutosaveInterval, true);
}

FString AWLGameMode::InitNewPlayer(APlayerController* NewPlayerController, const FUniqueNetIdRepl& UniqueId, const FString& Options, const FString& Portal)
{
	const FString Error = Super::InitNewPlayer(NewPlayerController, UniqueId, Options, Portal);

	FString Name = UGameplayStatics::ParseOption(Options, TEXT("Name"));
	if (Name.IsEmpty())
	{
		Name = TEXT("Survivor");
	}
	Name = Name.Left(20);

	const FString AccountId = UniqueId.IsValid() ? UniqueId->ToString() : FString::Printf(TEXT("dev:%s"), *Name.ToLower());
	PendingAccounts.Add(NewPlayerController, AccountId);
	const FString Look = UGameplayStatics::ParseOption(Options, TEXT("Look"));
	if (!Look.IsEmpty())
	{
		PendingLooks.Add(NewPlayerController, UWLGameInstance::LookFromUrlSafe(Look));
	}
	if (NewPlayerController->PlayerState)
	{
		NewPlayerController->PlayerState->SetPlayerName(Name);
	}
	return Error;
}

bool AWLGameMode::PlayerCanRestart_Implementation(APlayerController* Player)
{
	// Never spawn a body before the character record has been applied.
	const AWLPlayerState* PS = Player ? Player->GetPlayerState<AWLPlayerState>() : nullptr;
	return PS && PS->IsCharacterLoaded() && Super::PlayerCanRestart_Implementation(Player);
}

void AWLGameMode::HandleStartingNewPlayer_Implementation(APlayerController* NewPlayer)
{
	// Deliberately not calling Super: it would spawn immediately. Spawn happens after the load.
	AWLPlayerState* PS = NewPlayer ? NewPlayer->GetPlayerState<AWLPlayerState>() : nullptr;
	if (!PS)
	{
		return;
	}
	if (PS->IsCharacterLoaded())
	{
		// Seamless travel keeps the PlayerState (CopyProperties); just respawn.
		RestartPlayer(NewPlayer);
		return;
	}

	UWLPersistenceSubsystem* Persistence = UWLPersistenceSubsystem::Get(this);
	if (!Persistence)
	{
		UE_LOG(LogWastelanders, Error, TEXT("No persistence subsystem; starting %s as a fresh character"), *PS->GetPlayerName());
		OnCharacterLoaded(true, UWLPersistenceSubsystem::MakeNewCharacter(TEXT("offline"), PS->GetPlayerName()), NewPlayer);
		return;
	}

	const FString* AccountId = PendingAccounts.Find(NewPlayer);
	Persistence->LoadCharacter(AccountId ? *AccountId : FString(TEXT("dev:unknown")), PS->GetPlayerName(),
		FWLOnCharacterLoaded::CreateUObject(this, &AWLGameMode::OnCharacterLoaded, TWeakObjectPtr<APlayerController>(NewPlayer)));
}

void AWLGameMode::OnCharacterLoaded(bool bSuccess, const FWLCharacterSave& Save, TWeakObjectPtr<APlayerController> WeakPC)
{
	APlayerController* PC = WeakPC.Get();
	PendingAccounts.Remove(WeakPC);
	const FString PendingLook = PendingLooks.FindRef(WeakPC);
	PendingLooks.Remove(WeakPC);
	if (!PC)
	{
		return; // disconnected while loading
	}
	if (!bSuccess)
	{
		UE_LOG(LogWastelanders, Warning, TEXT("Character load failed for %s; kicking"), *GetNameSafe(PC));
		if (AWLPlayerController* WLPC = Cast<AWLPlayerController>(PC))
		{
			WLPC->ClientGameMessage(NSLOCTEXT("WL", "LoadFailed", "Your character could not be loaded. Please try again shortly."));
		}
		PC->ClientReturnToMainMenuWithTextReason(NSLOCTEXT("WL", "LoadFailedKick", "Character load failed"));
		return;
	}
	FWLCharacterSave Loaded = Save;
	if (Loaded.Appearance.Num() == 0 && !PendingLook.IsEmpty())
	{
		Loaded.Appearance = UWLFrontendSubsystem::DecodeLook(PendingLook);
	}
	SpawnLoadedPlayer(PC, Loaded);
}

void AWLGameMode::SpawnLoadedPlayer(APlayerController* PC, const FWLCharacterSave& Save)
{
	AWLPlayerState* PS = PC->GetPlayerState<AWLPlayerState>();
	PS->ApplySave(Save);

	const bool bSameMap = Save.bHasLocation && Save.MapName == GetWorld()->GetMapName();
	if (bSameMap)
	{
		RestartPlayerAtTransform(PC, FTransform(FRotator(0.f, Save.Yaw, 0.f), Save.Location + FVector(0.f, 0.f, 10.f)));
	}
	if (!PC->GetPawn())
	{
		// No saved spot on this map, or the saved spot was blocked: use a PlayerStart.
		RestartPlayer(PC);
	}

	if (UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem())
	{
		ASC->RestoreHealth(Save.CurrentHP);
	}
	UE_LOG(LogWastelanders, Log, TEXT("%s entered the world (%s)"), *PS->GetPlayerName(), *PS->GetCharacterId().ToString());
}

void AWLGameMode::SavePlayer(AWLPlayerState* PS)
{
	if (!PS || !PS->IsCharacterLoaded())
	{
		return;
	}
	if (UWLPersistenceSubsystem* Persistence = UWLPersistenceSubsystem::Get(this))
	{
		FWLCharacterSave Save;
		PS->CaptureSave(Save);
		if (const UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem())
		{
			Save.CurrentHP = ASC->GetCurrentHealthInt();
		}
		Persistence->SaveCharacter(Save);
	}
}

void AWLGameMode::SaveAllPlayers()
{
	if (const AGameStateBase* GS = GameState)
	{
		for (APlayerState* PS : GS->PlayerArray)
		{
			SavePlayer(Cast<AWLPlayerState>(PS));
		}
	}
}

void AWLGameMode::Logout(AController* Exiting)
{
	SavePlayer(Exiting ? Exiting->GetPlayerState<AWLPlayerState>() : nullptr);
	if (APlayerController* PC = Cast<APlayerController>(Exiting))
	{
		PendingAccounts.Remove(PC);
		PendingLooks.Remove(PC);
	}
	Super::Logout(Exiting);
}

void AWLGameMode::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	SaveAllPlayers();
	if (UWLPersistenceSubsystem* Persistence = UWLPersistenceSubsystem::Get(this))
	{
		Persistence->Flush();
	}
	Super::EndPlay(EndPlayReason);
}

bool AWLGameMode::TeleportToSpawn(AController* Controller)
{
	APawn* Pawn = Controller ? Controller->GetPawn() : nullptr;
	AActor* Start = Controller ? FindPlayerStart(Controller) : nullptr;
	if (!Pawn || !Start)
	{
		return false;
	}
	return Pawn->TeleportTo(Start->GetActorLocation(), Start->GetActorRotation());
}

void AWLGameMode::RequestRespawn(AController* Controller, float Delay)
{
	FTimerHandle Handle;
	GetWorldTimerManager().SetTimer(Handle,
		FTimerDelegate::CreateUObject(this, &AWLGameMode::DoRespawn, TWeakObjectPtr<AController>(Controller)),
		FMath::Max(0.1f, Delay), false);
}

void AWLGameMode::DoRespawn(TWeakObjectPtr<AController> WeakController)
{
	AController* Controller = WeakController.Get();
	if (!Controller)
	{
		return;
	}
	if (AWLPlayerState* PS = Controller->GetPlayerState<AWLPlayerState>())
	{
		if (UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem())
		{
			ASC->Revive();
		}
	}
	if (!TeleportToSpawn(Controller))
	{
		RestartPlayer(Controller);
	}
}
