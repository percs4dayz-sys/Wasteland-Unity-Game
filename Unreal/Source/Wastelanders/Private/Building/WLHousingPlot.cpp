#include "Building/WLHousingPlot.h"
#include "Player/WLPlayerState.h"
#include "Quests/WLPlayerFlagsComponent.h"

#define LOCTEXT_NAMESPACE "WLHousingPlot"

const FString AWLHousingPlot::PlotFlag = TEXT("housing_plot");

AWLHousingPlot::AWLHousingPlot()
{
	DisplayName = LOCTEXT("Name", "Housing Plot");
	ExamineText = LOCTEXT("Examine", "The foundation of your wasteland home.");
	PlaceholderColor = FLinearColor(0.45f, 0.4f, 0.32f);
	PlaceholderSize = FVector(240.f, 240.f, 40.f);
}

void AWLHousingPlot::MarkBuilder(AWLPlayerState* Builder)
{
	if (Builder && Builder->GetFlags())
	{
		Builder->GetFlags()->SetFlag(PlotFlag);
	}
}

TArray<FWLInteractOption> AWLHousingPlot::GetInteractOptions_Implementation(const AWLPlayerCharacter*) const
{
	return {}; // Examine only (appended automatically).
}

#undef LOCTEXT_NAMESPACE
