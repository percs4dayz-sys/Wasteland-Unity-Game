#pragma once

#include "CoreMinimal.h"
#include "Modules/ModuleInterface.h"

WASTELANDERSEDITOR_API DECLARE_LOG_CATEGORY_EXTERN(LogWastelandersEditor, Log, All);

class FWastelandersEditorModule : public IModuleInterface
{
public:
	virtual void StartupModule() override;
	virtual void ShutdownModule() override;

private:
	void RegisterMenus();
};
