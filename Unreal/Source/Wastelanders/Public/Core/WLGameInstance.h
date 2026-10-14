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
	/**
	 * Connect to a world server as the given character. Address like "127.0.0.1:7777".
	 * EncodedLook is the character-creator choices (UWLFrontendSubsystem::EncodeLook); the server only
	 * applies it when the character is brand new.
	 */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Session")
	void JoinWorld(const FString& Address, const FString& CharacterName, const FString& EncodedLook = TEXT(""));

	/** Look strings ride on the travel URL, where '=' and '|' would break option parsing. */
	static FString LookToUrlSafe(const FString& EncodedLook);
	static FString LookFromUrlSafe(const FString& UrlSafe);

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Session")
	const FString& GetSelectedCharacterName() const { return SelectedCharacterName; }

private:
	FString SelectedCharacterName;
};
