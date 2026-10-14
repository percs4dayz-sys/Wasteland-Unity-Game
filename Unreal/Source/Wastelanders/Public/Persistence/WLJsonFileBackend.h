#pragma once

#include "CoreMinimal.h"
#include "Persistence/WLPersistenceSubsystem.h"
#include "Tasks/Task.h"
#include "Tasks/Pipe.h"

/**
 * Default persistence backend: one JSON document per character at
 *     Saved/Characters/<AccountIdSanitized>/<charactername>.json
 * Ported from Unity SaveManager.cs (wasteland_save.json in persistentDataPath).
 *
 * Behaviour kept from Unity:
 *  - Atomic write: serialize to <file>.tmp, then move over the real file, so a crash mid-write never
 *    leaves a half-written save (Unity: WriteAllText(tmp) + Delete + Move).
 * Changed for the MMO server:
 *  - All disk I/O runs off the game thread on one ordered task pipe; a load queued after a save for the
 *    same character always sees that save. Load results are marshalled back to the game thread.
 *  - A corrupt file is NOT deleted (Unity deleted it and started fresh, silently wiping progress).
 *    It is renamed to <file>.corrupt-<timestamp>, the .bak copy is tried, and if that fails too the
 *    load reports failure so the player is kicked instead of being handed an empty character that
 *    would overwrite the real one on the next autosave.
 */
class WASTELANDERS_API FWLJsonFileBackend : public IWLPersistenceBackend, public TSharedFromThis<FWLJsonFileBackend>
{
public:
	/** RootDir defaults to Saved/<UWLPersistenceSettings::CharactersFolder>. */
	explicit FWLJsonFileBackend(const FString& InRootDir = FString());
	virtual ~FWLJsonFileBackend() override;

	virtual void LoadOrCreate(const FString& AccountId, const FString& CharacterName, FWLOnCharacterLoaded Callback) override;
	virtual void Save(const FWLCharacterSave& Record) override;
	virtual void Flush() override;

	const FString& GetRootDir() const { return RootDir; }

	/** Full path for a character record. */
	FString GetRecordPath(const FString& AccountId, const FString& CharacterName) const;

	/** Filesystem-safe folder name for an account id ("steam:7656..." -> "steam_7656..."). Never empty. */
	static FString SanitizeAccountId(const FString& AccountId);

	/** Filesystem-safe, case-folded file stem for a character name, so "Bob" and "bob" are one character. */
	static FString SanitizeCharacterName(const FString& CharacterName);

	/** Pure (de)serialization, exposed for tests and the import command. */
	static bool RecordToJson(const FWLCharacterSave& Record, FString& OutJson);
	static bool JsonToRecord(const FString& Json, FWLCharacterSave& OutRecord);

	/** Synchronous helpers used on the I/O pipe. Public for tests. */
	static bool WriteFileAtomic(const FString& Path, const FString& Contents, bool bKeepBackup);
	enum class ELoadResult : uint8 { Loaded, NotFound, Corrupt };
	static ELoadResult ReadRecord(const FString& Path, FWLCharacterSave& OutRecord);

private:
	void Track(UE::Tasks::FTask Task);

	FString RootDir;
	bool bKeepBackup = true;

	/** Serializes every read and write in submission order. */
	UE::Tasks::FPipe IOPipe;

	FCriticalSection TasksLock;
	TArray<UE::Tasks::FTask> Outstanding;
};
