#include "WastelandersEditor.h"
#include "WLDataImporter.h"
#include "Modules/ModuleManager.h"
#include "ToolMenus.h"
#include "HAL/IConsoleManager.h"

DEFINE_LOG_CATEGORY(LogWastelandersEditor);

IMPLEMENT_MODULE(FWastelandersEditorModule, WastelandersEditor);

static FAutoConsoleCommand GImportGameDataCmd(
	TEXT("WL.ImportGameData"),
	TEXT("Create/refresh /Game/Data DataTables from Unreal/Content/Data/*.json"),
	FConsoleCommandDelegate::CreateLambda([]() { FWLDataImporter::ImportAll(); }));

void FWastelandersEditorModule::StartupModule()
{
	UToolMenus::RegisterStartupCallback(FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &FWastelandersEditorModule::RegisterMenus));
}

void FWastelandersEditorModule::ShutdownModule()
{
	if (UObjectInitialized())
	{
		UToolMenus::UnRegisterStartupCallback(this);
		UToolMenus::UnregisterOwner(this);
	}
}

void FWastelandersEditorModule::RegisterMenus()
{
	FToolMenuOwnerScoped OwnerScoped(this);
	UToolMenu* Menu = UToolMenus::Get()->ExtendMenu("LevelEditor.MainMenu.Tools");
	FToolMenuSection& Section = Menu->FindOrAddSection("Wastelanders", NSLOCTEXT("WLEditor", "Section", "Wastelanders"));
	Section.AddMenuEntry(
		"WLImportGameData",
		NSLOCTEXT("WLEditor", "ImportData", "Import Game Data"),
		NSLOCTEXT("WLEditor", "ImportDataTip", "Create or refresh the item, recipe, skill, module and gear set DataTables from Content/Data/*.json"),
		FSlateIcon(),
		FUIAction(FExecuteAction::CreateLambda([]() { FWLDataImporter::ImportAll(); })));
}
