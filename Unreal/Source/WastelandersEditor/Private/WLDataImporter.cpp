#include "WLDataImporter.h"
#include "WastelandersEditor.h"
#include "Data/WLDataTypes.h"
#include "Engine/DataTable.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "UObject/Package.h"
#include "UObject/SavePackage.h"
#include "AssetRegistry/AssetRegistryModule.h"

int32 FWLDataImporter::ImportAll()
{
	struct FEntry { const TCHAR* File; const TCHAR* Asset; UScriptStruct* Struct; };
	const FEntry Entries[] = {
		{ TEXT("DT_Items.json"),    TEXT("/Game/Data/DT_Items"),    FWLItemRow::StaticStruct() },
		{ TEXT("DT_Recipes.json"),  TEXT("/Game/Data/DT_Recipes"),  FWLRecipeRow::StaticStruct() },
		{ TEXT("DT_Skills.json"),   TEXT("/Game/Data/DT_Skills"),   FWLSkillRow::StaticStruct() },
		{ TEXT("DT_Modules.json"),  TEXT("/Game/Data/DT_Modules"),  FWLModuleRow::StaticStruct() },
		{ TEXT("DT_GearSets.json"), TEXT("/Game/Data/DT_GearSets"), FWLGearSetRow::StaticStruct() },
	};

	int32 Written = 0;
	const FString Dir = FPaths::Combine(FPaths::ProjectContentDir(), TEXT("Data"));
	for (const FEntry& E : Entries)
	{
		if (ImportTable(FPaths::Combine(Dir, E.File), E.Asset, E.Struct))
		{
			++Written;
		}
	}
	UE_LOG(LogWastelandersEditor, Display, TEXT("Imported %d/%d game data tables"), Written, UE_ARRAY_COUNT(Entries));
	return Written;
}

bool FWLDataImporter::ImportTable(const FString& JsonFile, const FString& AssetPath, UScriptStruct* RowStruct)
{
	FString Json;
	if (!FFileHelper::LoadFileToString(Json, *JsonFile))
	{
		UE_LOG(LogWastelandersEditor, Error, TEXT("Missing %s (run Tools/UnityDataExport)"), *JsonFile);
		return false;
	}

	const FString AssetName = FPackageName::GetLongPackageAssetName(AssetPath);
	UPackage* Package = CreatePackage(*AssetPath);
	Package->FullyLoad();

	UDataTable* Table = FindObject<UDataTable>(Package, *AssetName);
	const bool bCreated = Table == nullptr;
	if (bCreated)
	{
		Table = NewObject<UDataTable>(Package, *AssetName, RF_Public | RF_Standalone | RF_Transactional);
	}
	Table->Modify();
	Table->RowStruct = RowStruct;

	const TArray<FString> Problems = Table->CreateTableFromJSONString(Json);
	for (const FString& Problem : Problems)
	{
		UE_LOG(LogWastelandersEditor, Warning, TEXT("%s: %s"), *AssetName, *Problem);
	}

	if (bCreated)
	{
		FAssetRegistryModule::AssetCreated(Table);
	}
	Package->MarkPackageDirty();

	const FString Filename = FPackageName::LongPackageNameToFilename(AssetPath, FPackageName::GetAssetPackageExtension());
	FSavePackageArgs Args;
	Args.TopLevelFlags = RF_Public | RF_Standalone;
	Args.SaveFlags = SAVE_NoError;
	const bool bSaved = UPackage::SavePackage(Package, Table, *Filename, Args);
	UE_LOG(LogWastelandersEditor, Display, TEXT("%s: %d rows%s"), *AssetName, Table->GetRowMap().Num(), bSaved ? TEXT("") : TEXT(" (SAVE FAILED)"));
	return bSaved;
}
