#include "CoreMinimal.h"

#if !UE_BUILD_SHIPPING

#include "HAL/IConsoleManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Engine/World.h"
#include "Persistence/WLPersistenceSubsystem.h"
#include "Wastelanders.h"

/**
 * Developer console commands for persistence (server / standalone / PIE host).
 *
 *   wl.ImportUnitySave <path-to-wasteland_save.json> <AccountId> [CharacterName]
 *       Converts a Unity SaveData file and writes it as that account's character. The character must
 *       be logged out (a logged-in character would overwrite the import on its next autosave).
 *       Unity kept one save per world: wasteland_save.json, wasteland_brokencrescent_save.json, ...
 *       Dev accounts made by AWLGameMode are "dev:<lowercase name>".
 *
 *   wl.FlushSaves
 *       Block until queued character writes are on disk.
 */
namespace
{
	void ImportUnitySaveCommand(const TArray<FString>& Args, UWorld* World)
	{
		if (Args.Num() < 2)
		{
			UE_LOG(LogWastelanders, Display, TEXT("Usage: wl.ImportUnitySave <path> <AccountId> [CharacterName]"));
			return;
		}
		UWLPersistenceSubsystem* Persistence = UWLPersistenceSubsystem::Get(World);
		if (!Persistence)
		{
			UE_LOG(LogWastelanders, Error, TEXT("wl.ImportUnitySave: no persistence subsystem in this world"));
			return;
		}

		FString Path = Args[0];
		FPaths::NormalizeFilename(Path);
		FString Json;
		if (!FFileHelper::LoadFileToString(Json, *Path))
		{
			UE_LOG(LogWastelanders, Error, TEXT("wl.ImportUnitySave: could not read %s"), *Path);
			return;
		}

		FWLCharacterSave Record;
		if (!UWLPersistenceSubsystem::ImportUnitySave(Json, Args[1], Record))
		{
			UE_LOG(LogWastelanders, Error, TEXT("wl.ImportUnitySave: %s is not a Unity SaveData document"), *Path);
			return;
		}
		if (Args.Num() >= 3 && !Args[2].IsEmpty())
		{
			Record.PlayerName = Args[2].Left(20);
		}

		Persistence->SaveCharacter(Record);
		Persistence->Flush();
		UE_LOG(LogWastelanders, Display, TEXT("wl.ImportUnitySave: imported '%s' for account %s (%d skills, %d inventory slots, %d bank slots, %d flags)"),
			*Record.PlayerName, *Record.AccountId, Record.SkillXP.Num(), Record.Inventory.Num(), Record.Bank.Num(), Record.Flags.Num());
	}

	FAutoConsoleCommandWithWorldAndArgs GImportUnitySave(
		TEXT("wl.ImportUnitySave"),
		TEXT("Import a Unity wasteland_save.json: wl.ImportUnitySave <path> <AccountId> [CharacterName]"),
		FConsoleCommandWithWorldAndArgsDelegate::CreateStatic(&ImportUnitySaveCommand));

	FAutoConsoleCommandWithWorldAndArgs GFlushSaves(
		TEXT("wl.FlushSaves"),
		TEXT("Block until queued character saves are written"),
		FConsoleCommandWithWorldAndArgsDelegate::CreateLambda([](const TArray<FString>&, UWorld* World)
		{
			if (UWLPersistenceSubsystem* Persistence = UWLPersistenceSubsystem::Get(World))
			{
				Persistence->Flush();
			}
		}));
}

#endif // !UE_BUILD_SHIPPING
