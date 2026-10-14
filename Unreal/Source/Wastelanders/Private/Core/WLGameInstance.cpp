#include "Core/WLGameInstance.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"

FString UWLGameInstance::LookToUrlSafe(const FString& EncodedLook)
{
	FString Out = EncodedLook.Left(400);
	Out.ReplaceInline(TEXT("="), TEXT("~"));
	Out.ReplaceInline(TEXT("|"), TEXT("."));
	Out.ReplaceInline(TEXT("?"), TEXT(""));
	Out.ReplaceInline(TEXT("&"), TEXT(""));
	Out.ReplaceInline(TEXT(" "), TEXT("_"));
	return Out;
}

FString UWLGameInstance::LookFromUrlSafe(const FString& UrlSafe)
{
	FString Out = UrlSafe;
	Out.ReplaceInline(TEXT("~"), TEXT("="));
	Out.ReplaceInline(TEXT("."), TEXT("|"));
	return Out;
}

void UWLGameInstance::JoinWorld(const FString& Address, const FString& CharacterName, const FString& EncodedLook)
{
	// Names are letters, digits, spaces and underscores only, so they ride safely on the travel URL.
	SelectedCharacterName.Reset();
	for (const TCHAR C : CharacterName.Left(20))
	{
		if (FChar::IsAlnum(C) || C == TEXT('_'))
		{
			SelectedCharacterName.AppendChar(C);
		}
		else if (C == TEXT(' '))
		{
			SelectedCharacterName.AppendChar(TEXT('_'));
		}
	}
	if (APlayerController* PC = GetFirstLocalPlayerController())
	{
		// The character name rides on the URL; the server resolves account + character on login.
		FString Url = FString::Printf(TEXT("%s?Name=%s"), *Address, *SelectedCharacterName);
		if (!EncodedLook.IsEmpty())
		{
			Url += TEXT("?Look=") + LookToUrlSafe(EncodedLook);
		}
		PC->ClientTravel(Url, TRAVEL_Absolute);
	}
}
