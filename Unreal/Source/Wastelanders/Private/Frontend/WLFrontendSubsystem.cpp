#include "Frontend/WLFrontendSubsystem.h"
#include "Core/WLGameInstance.h"
#include "Engine/GameInstance.h"
#include "Engine/World.h"


UWLFrontendSubsystem* UWLFrontendSubsystem::Get(const UObject* WorldContextObject)
{
	const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
	UGameInstance* GI = World ? World->GetGameInstance() : nullptr;
	return GI ? GI->GetSubsystem<UWLFrontendSubsystem>() : nullptr;
}

const TArray<FName>& UWLFrontendSubsystem::GetPartRows()
{
	static const TArray<FName> Rows = { TEXT("Hair"), TEXT("Eyebrows"), TEXT("Eyes"), TEXT("Ears"), TEXT("FacialHair"), TEXT("Nose"), TEXT("Build") };
	return Rows;
}

const TArray<FName>& UWLFrontendSubsystem::GetSliderRows()
{
	static const TArray<FName> Rows = { TEXT("BodyType"), TEXT("BodySize"), TEXT("Musculature") };
	return Rows;
}

const TArray<FName>& UWLFrontendSubsystem::GetColorRows()
{
	static const TArray<FName> Rows = { TEXT("HairColor"), TEXT("FacialHairColor"), TEXT("BrowColor"), TEXT("SkinColor") };
	return Rows;
}

FString UWLFrontendSubsystem::RowLabel(FName Row)
{
	static const TMap<FName, FString> Labels = {
		{ TEXT("Hair"), TEXT("Hair") }, { TEXT("Eyebrows"), TEXT("Eyebrows") }, { TEXT("Eyes"), TEXT("Eyes") },
		{ TEXT("Ears"), TEXT("Ears") }, { TEXT("FacialHair"), TEXT("Facial hair") }, { TEXT("Nose"), TEXT("Nose") },
		{ TEXT("Build"), TEXT("Build") }, { TEXT("BodyType"), TEXT("Body type") }, { TEXT("BodySize"), TEXT("Body size") },
		{ TEXT("Musculature"), TEXT("Musculature") }, { TEXT("HairColor"), TEXT("Hair colour") },
		{ TEXT("FacialHairColor"), TEXT("Facial hair colour") }, { TEXT("BrowColor"), TEXT("Brow colour") },
		{ TEXT("SkinColor"), TEXT("Skin colour") } };
	const FString* L = Labels.Find(Row);
	return L ? *L : Row.ToString();
}

FString UWLFrontendSubsystem::GetRowValue(FName Row) const
{
	const FString* V = Look.Find(Row);
	return V ? *V : TEXT("0");
}

void UWLFrontendSubsystem::StepRow(FName Row, int32 Delta)
{
	const int32 Cur = FCString::Atoi(*GetRowValue(Row));
	int32 Next = Cur;
	if (GetSliderRows().Contains(Row))
	{
		Next = FMath::Clamp(Cur + Delta * 10, -100, 100);
	}
	else if (GetColorRows().Contains(Row))
	{
		const int32 N = FMath::Max(1, ColorPaletteSize);
		Next = ((Cur + Delta) % N + N) % N;
	}
	else if (GetPartRows().Contains(Row))
	{
		const int32 N = FMath::Max(1, OptionsPerPartRow);
		Next = ((Cur + Delta) % N + N) % N;
	}
	else
	{
		return;
	}
	Look.Add(Row, FString::FromInt(Next));
}

void UWLFrontendSubsystem::Randomise(FRandomStream& Random)
{
	for (const FName& R : GetPartRows())
	{
		Look.Add(R, FString::FromInt(Random.RandRange(0, FMath::Max(1, OptionsPerPartRow) - 1)));
	}
	for (const FName& R : GetColorRows())
	{
		Look.Add(R, FString::FromInt(Random.RandRange(0, FMath::Max(1, ColorPaletteSize) - 1)));
	}
	// Sliders land on multiples of 10 so the stepper can reach them again.
	for (const FName& R : GetSliderRows())
	{
		Look.Add(R, FString::FromInt(Random.RandRange(-5, 5) * 10));
	}
	if (Gender == TEXT("Male"))
	{
		Look.Add(TEXT("BodyType"), TEXT("-100"));
	}
	else if (Gender == TEXT("Female"))
	{
		Look.Add(TEXT("BodyType"), TEXT("100"));
	}
}

FString UWLFrontendSubsystem::SanitizeName(const FString& Raw)
{
	FString Out;
	for (const TCHAR C : Raw.TrimStartAndEnd().Left(20))
	{
		if (FChar::IsAlnum(C) || C == TEXT('_'))
		{
			Out.AppendChar(C);
		}
		else if (C == TEXT(' '))
		{
			Out.AppendChar(TEXT('_'));
		}
	}
	return Out;
}

bool UWLFrontendSubsystem::IsValidName(const FString& Raw)
{
	return SanitizeName(Raw).Len() >= 2;
}

void UWLFrontendSubsystem::SetGender(const FString& InGender)
{
	if (InGender != TEXT("Male") && InGender != TEXT("Female"))
	{
		return;
	}
	Gender = InGender;
	Look.Add(TEXT("BodyType"), InGender == TEXT("Male") ? TEXT("-100") : TEXT("100"));
}

bool UWLFrontendSubsystem::Join()
{
	if (!CanJoin())
	{
		return false;
	}
	UWLGameInstance* GI = Cast<UWLGameInstance>(GetGameInstance());
	if (!GI)
	{
		return false;
	}
	Look.Add(TEXT("Gender"), Gender);
	bPendingSplash = true;
	GI->JoinWorld(ServerAddress, SanitizeName(CharacterName), EncodeLook(Look));
	return true;
}

FString UWLFrontendSubsystem::EncodeLook(const TMap<FName, FString>& InLook)
{
	TArray<FName> Keys;
	InLook.GetKeys(Keys);
	Keys.Sort(FNameLexicalLess());
	TArray<FString> Parts;
	for (const FName& K : Keys)
	{
		// Separators are stripped so a hostile value cannot forge another key.
		FString V = InLook[K];
		V.ReplaceInline(TEXT("|"), TEXT(""));
		V.ReplaceInline(TEXT("="), TEXT(""));
		Parts.Add(FString::Printf(TEXT("%s=%s"), *K.ToString(), *V));
	}
	return FString::Join(Parts, TEXT("|"));
}

TMap<FName, FString> UWLFrontendSubsystem::DecodeLook(const FString& Encoded)
{
	TMap<FName, FString> Out;
	TArray<FString> Parts;
	Encoded.ParseIntoArray(Parts, TEXT("|"), true);
	for (const FString& P : Parts)
	{
		FString K, V;
		if (P.Split(TEXT("="), &K, &V) && !K.IsEmpty())
		{
			Out.Add(FName(*K), V);
		}
	}
	return Out;
}

bool UWLFrontendSubsystem::ConsumePendingSplash()
{
	const bool b = bPendingSplash;
	bPendingSplash = false;
	return b;
}
