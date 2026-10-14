#include "Persistence/WLJsonFileBackend.h"
#include "Persistence/WLPersistenceSettings.h"
#include "Async/Async.h"
#include "HAL/FileManager.h"
#include "JsonObjectConverter.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Wastelanders.h"

FWLJsonFileBackend::FWLJsonFileBackend(const FString& InRootDir)
	: IOPipe(TEXT("WLPersistenceIO"))
{
	const UWLPersistenceSettings* Settings = UWLPersistenceSettings::Get();
	RootDir = InRootDir.IsEmpty()
		? FPaths::Combine(FPaths::ProjectSavedDir(), Settings ? Settings->CharactersFolder : FString(TEXT("Characters")))
		: InRootDir;
	bKeepBackup = Settings ? Settings->bKeepBackup : true;
}

FWLJsonFileBackend::~FWLJsonFileBackend()
{
	// The pipe must be idle before it is destroyed; task bodies only use static helpers and copies.
	Flush();
}

// ---------------------------------------------------------------------------------------------
// Paths
// ---------------------------------------------------------------------------------------------

static FString SanitizePathPart(const FString& In)
{
	FString Out;
	Out.Reserve(In.Len());
	for (const TCHAR C : In)
	{
		Out.AppendChar((FChar::IsAlnum(C) || C == TEXT('_') || C == TEXT('-')) ? C : TEXT('_'));
	}
	// Never let a path part be "." / ".." / empty, and keep it a sane length for every filesystem.
	Out = Out.Left(96);
	return Out.IsEmpty() ? FString(TEXT("_")) : Out;
}

FString FWLJsonFileBackend::SanitizeAccountId(const FString& AccountId)
{
	return SanitizePathPart(AccountId.TrimStartAndEnd());
}

FString FWLJsonFileBackend::SanitizeCharacterName(const FString& CharacterName)
{
	// Spaces become underscores already in UWLGameInstance::JoinWorld; fold case so a case-insensitive
	// filesystem (Windows/macOS dev boxes) and a case-sensitive one (Linux servers) agree on identity.
	return SanitizePathPart(CharacterName.TrimStartAndEnd().ToLower());
}

FString FWLJsonFileBackend::GetRecordPath(const FString& AccountId, const FString& CharacterName) const
{
	return FPaths::Combine(RootDir, SanitizeAccountId(AccountId), SanitizeCharacterName(CharacterName) + TEXT(".json"));
}

// ---------------------------------------------------------------------------------------------
// Serialization
// ---------------------------------------------------------------------------------------------

bool FWLJsonFileBackend::RecordToJson(const FWLCharacterSave& Record, FString& OutJson)
{
	return FJsonObjectConverter::UStructToJsonObjectString(Record, OutJson, /*CheckFlags*/ 0, /*SkipFlags*/ 0, /*Indent*/ 0, nullptr, /*bPrettyPrint*/ true);
}

bool FWLJsonFileBackend::JsonToRecord(const FString& Json, FWLCharacterSave& OutRecord)
{
	OutRecord = FWLCharacterSave();
	OutRecord.Version = 0; // a document without a Version key predates versioning
	if (Json.TrimStartAndEnd().IsEmpty())
	{
		return false;
	}
	return FJsonObjectConverter::JsonObjectStringToUStruct(Json, &OutRecord, /*CheckFlags*/ 0, /*SkipFlags*/ 0);
}

// ---------------------------------------------------------------------------------------------
// Disk (runs on the I/O pipe)
// ---------------------------------------------------------------------------------------------

bool FWLJsonFileBackend::WriteFileAtomic(const FString& Path, const FString& Contents, bool bInKeepBackup)
{
	IFileManager& FM = IFileManager::Get();
	FM.MakeDirectory(*FPaths::GetPath(Path), /*Tree*/ true);

	const FString Tmp = Path + TEXT(".tmp");
	if (!FFileHelper::SaveStringToFile(Contents, *Tmp, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
	{
		UE_LOG(LogWastelanders, Warning, TEXT("[Persistence] Could not write %s"), *Tmp);
		return false;
	}

	if (bInKeepBackup && FM.FileExists(*Path))
	{
		// Previous good version survives as .bak; failure here is not fatal.
		FM.Copy(*(Path + TEXT(".bak")), *Path, /*Replace*/ true);
	}

	// Move with replace: the real file is either the old version or the new one, never half of each.
	if (!FM.Move(*Path, *Tmp, /*Replace*/ true, /*EvenIfReadOnly*/ true))
	{
		UE_LOG(LogWastelanders, Warning, TEXT("[Persistence] Could not move %s over %s"), *Tmp, *Path);
		return false;
	}
	return true;
}

FWLJsonFileBackend::ELoadResult FWLJsonFileBackend::ReadRecord(const FString& Path, FWLCharacterSave& OutRecord)
{
	IFileManager& FM = IFileManager::Get();

	// A crash between writing .tmp and the move leaves only the .tmp; it is complete (the write finished
	// before the move was attempted), so adopt it.
	const FString Tmp = Path + TEXT(".tmp");
	if (!FM.FileExists(*Path) && FM.FileExists(*Tmp))
	{
		FM.Move(*Path, *Tmp, true, true);
	}

	auto TryLoad = [&OutRecord](const FString& File) -> ELoadResult
	{
		FString Json;
		if (!IFileManager::Get().FileExists(*File))
		{
			return ELoadResult::NotFound;
		}
		if (!FFileHelper::LoadFileToString(Json, *File) || !JsonToRecord(Json, OutRecord))
		{
			return ELoadResult::Corrupt;
		}
		return ELoadResult::Loaded;
	};

	const ELoadResult Main = TryLoad(Path);
	if (Main != ELoadResult::Corrupt)
	{
		return Main;
	}

	// Quarantine instead of deleting (Unity deleted the file and silently started the player over).
	const FString Quarantine = FString::Printf(TEXT("%s.corrupt-%s"), *Path, *FDateTime::UtcNow().ToString(TEXT("%Y%m%d-%H%M%S")));
	FM.Move(*Quarantine, *Path, true, true);
	UE_LOG(LogWastelanders, Error, TEXT("[Persistence] %s is corrupt; moved to %s, trying backup"), *Path, *Quarantine);

	const ELoadResult Backup = TryLoad(Path + TEXT(".bak"));
	if (Backup == ELoadResult::Loaded)
	{
		UE_LOG(LogWastelanders, Warning, TEXT("[Persistence] Restored %s from its backup"), *Path);
		return ELoadResult::Loaded;
	}
	return ELoadResult::Corrupt;
}

// ---------------------------------------------------------------------------------------------
// IWLPersistenceBackend
// ---------------------------------------------------------------------------------------------

void FWLJsonFileBackend::Track(UE::Tasks::FTask Task)
{
	FScopeLock Lock(&TasksLock);
	Outstanding.RemoveAll([](const UE::Tasks::FTask& T) { return T.IsCompleted(); });
	Outstanding.Add(MoveTemp(Task));
}

void FWLJsonFileBackend::LoadOrCreate(const FString& AccountId, const FString& CharacterName, FWLOnCharacterLoaded Callback)
{
	const FString Path = GetRecordPath(AccountId, CharacterName);
	const bool bBackup = bKeepBackup;

	Track(IOPipe.Launch(TEXT("WLLoadCharacter"), [Path, AccountId, CharacterName, Callback, bBackup]()
	{
		FWLCharacterSave Record;
		bool bOk = true;
		switch (ReadRecord(Path, Record))
		{
		case ELoadResult::Loaded:
			UWLPersistenceSubsystem::Migrate(Record);
			if (!Record.CharacterId.IsValid())
			{
				Record.CharacterId = FGuid::NewGuid();
			}
			// The folder is the account; a record copied between folders by hand adopts its new owner.
			Record.AccountId = AccountId;
			if (Record.PlayerName.IsEmpty())
			{
				Record.PlayerName = CharacterName;
			}
			break;

		case ELoadResult::NotFound:
		{
			// Unity SaveManager: no save = new game. Persist immediately so the CharacterId is stable even if
			// the server stops before the first autosave.
			Record = UWLPersistenceSubsystem::MakeNewCharacter(AccountId, CharacterName);
			FString Json;
			if (RecordToJson(Record, Json))
			{
				WriteFileAtomic(Path, Json, bBackup);
			}
			UE_LOG(LogWastelanders, Log, TEXT("[Persistence] Created new character %s for %s"), *CharacterName, *AccountId);
			break;
		}

		case ELoadResult::Corrupt:
		default:
			bOk = false;
			break;
		}

		AsyncTask(ENamedThreads::GameThread, [Callback, bOk, Record = MoveTemp(Record)]()
		{
			Callback.ExecuteIfBound(bOk, Record);
		});
	}));
}

void FWLJsonFileBackend::Save(const FWLCharacterSave& Record)
{
	if (Record.AccountId.IsEmpty() || Record.PlayerName.IsEmpty())
	{
		UE_LOG(LogWastelanders, Warning, TEXT("[Persistence] Refusing to save a record with no account or name (%s)"), *Record.CharacterId.ToString());
		return;
	}

	// Serialize on the calling (game) thread so the worker never touches reflection data concurrently
	// with a hot reload / GC; only the disk write goes to the pipe.
	FString Json;
	if (!RecordToJson(Record, Json))
	{
		UE_LOG(LogWastelanders, Warning, TEXT("[Persistence] Could not serialize %s"), *Record.PlayerName);
		return;
	}

	const FString Path = GetRecordPath(Record.AccountId, Record.PlayerName);
	const bool bBackup = bKeepBackup;
	Track(IOPipe.Launch(TEXT("WLSaveCharacter"), [Path, Json = MoveTemp(Json), bBackup]()
	{
		WriteFileAtomic(Path, Json, bBackup);
	}));
}

void FWLJsonFileBackend::Flush()
{
	TArray<UE::Tasks::FTask> ToWait;
	{
		FScopeLock Lock(&TasksLock);
		ToWait = Outstanding;
		Outstanding.Reset();
	}
	UE::Tasks::Wait(ToWait);
}
