#pragma once

#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "Persistence/WLCharacterSave.h"
#include "WLPersistenceSubsystem.generated.h"

DECLARE_DELEGATE_TwoParams(FWLOnCharacterLoaded, bool /*bSuccess*/, const FWLCharacterSave& /*Save*/);

/**
 * Storage backend for character records. The default is a JSON file per character under
 * Saved/Characters (fine for development and small private servers). A production MMO swaps in a
 * database-backed implementation (see Docs/ConversionPlan.md, "Persistence") without touching
 * gameplay code, because gameplay only ever sees FWLCharacterSave.
 */
class WASTELANDERS_API IWLPersistenceBackend
{
public:
	virtual ~IWLPersistenceBackend() = default;

	/** Load the account's character by name, or create a fresh one. Callback on the game thread. */
	virtual void LoadOrCreate(const FString& AccountId, const FString& CharacterName, FWLOnCharacterLoaded Callback) = 0;

	/** Persist a record (upsert by CharacterId). Fire-and-forget; failures are logged. */
	virtual void Save(const FWLCharacterSave& Record) = 0;

	/** Block until queued writes are done (server shutdown). */
	virtual void Flush() = 0;
};

/**
 * Server-side persistence entry point. AWLGameMode calls this on login, logout and autosave.
 * Also imports Unity save files (wasteland_save.json) so existing progress carries over.
 */
UCLASS()
class WASTELANDERS_API UWLPersistenceSubsystem : public UGameInstanceSubsystem
{
	GENERATED_BODY()

public:
	virtual void Initialize(FSubsystemCollectionBase& Collection) override;
	virtual void Deinitialize() override;

	static UWLPersistenceSubsystem* Get(const UObject* WorldContextObject);

	void LoadCharacter(const FString& AccountId, const FString& CharacterName, FWLOnCharacterLoaded Callback);
	void SaveCharacter(const FWLCharacterSave& Record);
	void Flush();

	/** Replace the backend (e.g. a database implementation registered by a server plugin). */
	void SetBackend(TSharedPtr<IWLPersistenceBackend> NewBackend);

	/** Builds a starting character: Endurance 10, starter kit, tutorial flags clear. */
	static FWLCharacterSave MakeNewCharacter(const FString& AccountId, const FString& CharacterName);

	/** Converts a Unity SaveData JSON document into a character record. False if it isn't one. */
	static bool ImportUnitySave(const FString& UnityJson, const FString& AccountId, FWLCharacterSave& Out);

	/** Upgrades older record layouts to FWLCharacterSave::CurrentVersion in place. */
	static void Migrate(FWLCharacterSave& Record);

private:
	TSharedPtr<IWLPersistenceBackend> Backend;
};
