#pragma once

#include "CoreMinimal.h"

/**
 * Creates or refreshes the game's DataTable assets from the JSON written by Tools/UnityDataExport
 * (Unreal/Content/Data/*.json -> /Game/Data/DT_*.uasset). Run from Tools > Wastelanders > Import Game
 * Data, or the console command "WL.ImportGameData". Rerun whenever the exporter output changes.
 */
struct WASTELANDERSEDITOR_API FWLDataImporter
{
	/** Imports every known table. Returns the number of tables written. */
	static int32 ImportAll();

	/** Imports one JSON file into a DataTable with the given row struct. */
	static bool ImportTable(const FString& JsonFile, const FString& AssetPath, UScriptStruct* RowStruct);
};
