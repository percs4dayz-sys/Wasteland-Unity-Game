#pragma once

#include "CoreMinimal.h"
#include "Engine/GameInstance.h"
#include "WLGameInstance.generated.h"

/** Client/server process-level state: the chosen character name and connection flow (front end -> world). */
UCLASS()
class WASTELANDERS_API UWLGameInstance : public UGameInstance
{
	GENERATED_BODY()

public:
	/** Connect to a world server as the given character. Address like "127.0.0.1:7777". */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Session")
	void JoinWorld(const FString& Address, const FString& CharacterName);

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Session")
	const FString& GetSelectedCharacterName() const { return SelectedCharacterName; }

private:
	FString SelectedCharacterName;
};
