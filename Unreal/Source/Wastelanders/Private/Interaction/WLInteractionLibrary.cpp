#include "Interaction/WLInteractionLibrary.h"
#include "World/WLResourceNode.h"
#include "Player/WLPlayerCharacter.h"
#include "GameFramework/Actor.h"

const FName FWLInteraction::ExamineVerb(TEXT("Examine"));

float FWLInteraction::DistanceToBox2D(const FVector& Point, const FBox& Box)
{
	if (!Box.IsValid)
	{
		return TNumericLimits<float>::Max();
	}
	const float DX = FMath::Max3(0.f, static_cast<float>(Box.Min.X - Point.X), static_cast<float>(Point.X - Box.Max.X));
	const float DY = FMath::Max3(0.f, static_cast<float>(Box.Min.Y - Point.Y), static_cast<float>(Point.Y - Box.Max.Y));
	return FMath::Sqrt(DX * DX + DY * DY);
}

FVector FWLInteraction::ClosestPointOnBox2D(const FVector& Point, const FBox& Box, float Z)
{
	if (!Box.IsValid)
	{
		return FVector(Point.X, Point.Y, Z);
	}
	return FVector(FMath::Clamp(Point.X, Box.Min.X, Box.Max.X), FMath::Clamp(Point.Y, Box.Min.Y, Box.Max.Y), Z);
}

static FBox UseBounds(const AActor* Target)
{
	// Colliding components only: an invisible decorative child or a huge trigger must not grant
	// remote use (Unity clamped collider bounds to the visible mesh for the same reason).
	FBox Box = Target->GetComponentsBoundingBox(/*bNonColliding*/ false);
	if (!Box.IsValid)
	{
		const FVector L = Target->GetActorLocation();
		Box = FBox(L, L);
	}
	return Box;
}

FVector FWLInteraction::GetUseLocation(const AActor* Target, const FVector& FromLocation)
{
	if (!Target)
	{
		return FromLocation;
	}
	if (const AWLResourceNode* Node = Cast<AWLResourceNode>(Target); Node && Node->HasInteractionPoint())
	{
		return Node->GetInteractionLocation();
	}
	return ClosestPointOnBox2D(FromLocation, UseBounds(Target), Target->GetActorLocation().Z);
}

float FWLInteraction::DistanceToTarget(const AActor* Target, const FVector& FromLocation)
{
	if (!Target)
	{
		return TNumericLimits<float>::Max();
	}
	if (const AWLResourceNode* Node = Cast<AWLResourceNode>(Target); Node && Node->HasInteractionPoint())
	{
		return FVector::Dist2D(FromLocation, Node->GetInteractionLocation());
	}
	return DistanceToBox2D(FromLocation, UseBounds(Target));
}

FString FWLInteraction::PrettyName(const FString& Raw)
{
	FString S = Raw;
	// Unity cut at "(Clone)"; Unreal names carry "_C_12" (Blueprint) or "_12" instance suffixes.
	int32 Cut = INDEX_NONE;
	if (S.FindChar(TEXT('('), Cut) && Cut > 0)
	{
		S.LeftInline(Cut);
	}
	if (S.StartsWith(TEXT("BP_")))
	{
		S.RightChopInline(3);
	}
	{
		const int32 BPSuffix = S.Find(TEXT("_C_"), ESearchCase::CaseSensitive, ESearchDir::FromEnd);
		if (BPSuffix > 0)
		{
			S.LeftInline(BPSuffix);
		}
		int32 Us = INDEX_NONE;
		if (S.FindLastChar(TEXT('_'), Us) && Us > 0 && Us < S.Len() - 1 && S.Mid(Us + 1).IsNumeric())
		{
			S.LeftInline(Us);
		}
	}
	S.ReplaceCharInline(TEXT('_'), TEXT(' '));
	S.TrimStartAndEndInline();

	FString Out;
	Out.Reserve(S.Len() + 8);
	for (int32 i = 0; i < S.Len(); ++i)
	{
		const TCHAR Ch = S[i];
		if (i > 0 && FChar::IsUpper(Ch) && FChar::IsLower(S[i - 1]))
		{
			Out.AppendChar(TEXT(' '));
		}
		Out.AppendChar(Ch);
	}
	return Out;
}

FText FWLInteraction::GetDisplayName(const AActor* Target)
{
	if (!Target)
	{
		return FText::GetEmpty();
	}
	if (Target->Implements<UWLInteractable>())
	{
		const FText Name = IWLInteractable::Execute_GetInteractName(Target);
		if (!Name.IsEmpty())
		{
			return Name;
		}
	}
	return FText::FromString(PrettyName(Target->GetName()));
}

FText FWLInteraction::GetExamineText(const AActor* Target)
{
	if (Target && Target->Implements<UWLInteractable>())
	{
		const FText Text = IWLInteractable::Execute_GetExamineText(Target);
		if (!Text.IsEmpty())
		{
			return Text;
		}
	}
	return NSLOCTEXT("WLInteraction", "NothingInteresting", "Nothing interesting about it.");
}

TArray<FWLInteractOption> FWLInteraction::GetMenuOptions(const AActor* Target, const AWLPlayerCharacter* Player)
{
	TArray<FWLInteractOption> Options;
	if (!Target || !Target->Implements<UWLInteractable>())
	{
		return Options;
	}
	Options = IWLInteractable::Execute_GetInteractOptions(Target, Player);
	Options.RemoveAll([](const FWLInteractOption& O) { return O.Verb == ExamineVerb || O.Verb.IsNone(); });

	// Default action first, so the menu reads like OSRS and left-click agrees with it.
	const int32 DefaultIdx = Options.IndexOfByPredicate([](const FWLInteractOption& O) { return O.bDefault; });
	if (DefaultIdx > 0)
	{
		const FWLInteractOption Default = Options[DefaultIdx];
		Options.RemoveAt(DefaultIdx);
		Options.Insert(Default, 0);
	}

	FWLInteractOption Examine;
	Examine.Verb = ExamineVerb;
	Examine.Label = FText::Format(NSLOCTEXT("WLInteraction", "ExamineLabel", "Examine {0}"), GetDisplayName(Target));
	Examine.bDefault = Options.Num() == 0;
	Options.Add(Examine);
	return Options;
}

float FWLInteraction::GetRange(const AActor* Target)
{
	if (!Target || !Target->Implements<UWLInteractable>())
	{
		return 200.f;
	}
	// Unity's use reach was capped at 2 m and gather reach was 1.1 m; never accept a zero or a map-wide range.
	return FMath::Clamp(IWLInteractable::Execute_GetInteractRange(Target), 50.f, 2000.f);
}
