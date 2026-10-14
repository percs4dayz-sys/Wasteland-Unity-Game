#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "WLGameMode.generated.h"

class AWLPlayerState;
struct FWLCharacterSave;

/**
 * Server rules for one world shard: login -> load character -> spawn; logout/autosave -> persist;
 * death -> respawn. Replaces Unity's SaveManager + PlayerRespawn + scene bootstrap logic.
 *
 * Players may connect with ?Name=<character> in the URL. The account id is the online subsystem's
 * unique net id when present, else derived from the name (development only).
 */
UCLASS()
class WASTELANDERS_API AWLGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	AWLGameMode();

	virtual void InitGame(const FString& MapName, const FString& Options, FString& ErrorMessage) override;
	virtual FString InitNewPlayer(APlayerController* NewPlayerController, const FUniqueNetIdRepl& UniqueId, const FString& Options, const FString& Portal) override;
	virtual void HandleStartingNewPlayer_Implementation(APlayerController* NewPlayer) override;
	virtual void Logout(AController* Exiting) override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;
	virtual bool PlayerCanRestart_Implementation(APlayerController* Player) override;

	/** Persist one player now (also called on important events: trades, rare drops, level-ups). */
	void SavePlayer(AWLPlayerState* PS);

	/** Persist everyone (autosave tick, shutdown). */
	void SaveAllPlayers();

	/** Move the player's pawn to a spawn point (/stuck, out-of-bounds rescue). */
	bool TeleportToSpawn(AController* Controller);

	/** After death: wait for the death animation, then respawn at full health at a spawn point. */
	void RequestRespawn(AController* Controller, float Delay = 3.f);

	/** Seconds between autosaves. Unity saved every 3 s locally; an MMO server batches more. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Persistence")
	float AutosaveInterval = 60.f;

protected:
	void OnCharacterLoaded(bool bSuccess, const FWLCharacterSave& Save, TWeakObjectPtr<APlayerController> WeakPC);
	void SpawnLoadedPlayer(APlayerController* PC, const FWLCharacterSave& Save);
	void DoRespawn(TWeakObjectPtr<AController> WeakController);

	FTimerHandle AutosaveTimer;

	/** Account ids by controller, captured at login before the PlayerState is fully set up. */
	TMap<TWeakObjectPtr<APlayerController>, FString> PendingAccounts;

	/** Character-creator look from the join URL; only used when the character is brand new. */
	TMap<TWeakObjectPtr<APlayerController>, FString> PendingLooks;
};
