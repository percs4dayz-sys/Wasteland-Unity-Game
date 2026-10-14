#include "Quests/WLBeastTaskRules.h"

namespace
{
	// Index 0 unused, exactly as BeastTasks.cs.
	constexpr int32 GMinKills[] = { 0, 10, 12, 15, 18, 20 };
	constexpr int32 GMaxKills[] = { 0, 15, 20, 25, 30, 35 };

	bool IsDigits(const FString& S)
	{
		if (S.IsEmpty())
		{
			return false;
		}
		for (const TCHAR C : S)
		{
			if (!FChar::IsDigit(C))
			{
				return false;
			}
		}
		return true;
	}
}

int32 FWLBeastTaskRules::MinKills(int32 Tier)
{
	return GMinKills[FMath::Clamp(Tier, 1, 5)];
}

int32 FWLBeastTaskRules::MaxKills(int32 Tier)
{
	return GMaxKills[FMath::Clamp(Tier, 1, 5)];
}

int32 FWLBeastTaskRules::CompletionBonus(int32 Required, int32 XPPerKill)
{
	return FMath::Max(1, FMath::RoundToInt(Required * XPPerKill * BonusFraction));
}

FString FWLBeastTaskRules::NormalizeCreatureName(const FString& RawName)
{
	FString N = RawName.Replace(TEXT("(Clone)"), TEXT("")).TrimStartAndEnd();
	while (true)
	{
		const FString Before = N;

		// "Name (2)"
		if (N.EndsWith(TEXT(")")))
		{
			int32 Open = INDEX_NONE;
			if (N.FindLastChar(TEXT('('), Open) && Open >= 0)
			{
				const FString Inner = N.Mid(Open + 1, N.Len() - Open - 2);
				if (IsDigits(Inner))
				{
					N = N.Left(Open).TrimStartAndEnd();
				}
			}
		}

		// "Name 3"
		int32 Space = INDEX_NONE;
		if (N.FindLastChar(TEXT(' '), Space) && Space > 0 && IsDigits(N.Mid(Space + 1)))
		{
			N = N.Left(Space).TrimStartAndEnd();
		}

		if (N == Before)
		{
			break;
		}
	}
	N.ReplaceCharInline(TEXT('|'), TEXT('/'));
	return N.IsEmpty() ? FString(TEXT("Creature")) : N;
}

int32 FWLBeastTaskRules::PickTier(int32 Want, const TArray<FKind>& Kinds)
{
	auto Has = [&Kinds](int32 Tier) { return Kinds.ContainsByPredicate([Tier](const FKind& K) { return K.Tier == Tier; }); };
	if (Has(Want))
	{
		return Want;
	}
	for (int32 T = Want - 1; T >= 1; --T)
	{
		if (Has(T))
		{
			return T;
		}
	}
	for (int32 T = Want + 1; T <= 5; ++T)
	{
		if (Has(T))
		{
			return T;
		}
	}
	return Want;
}

int32 FWLBeastTaskRules::PickKind(const TArray<FKind>& Kinds, int32 Tier, const FString& LastCreature, TFunctionRef<int32(int32)> RollFn)
{
	TArray<int32> Pool;
	for (int32 i = 0; i < Kinds.Num(); ++i)
	{
		if (Kinds[i].Tier == Tier)
		{
			Pool.Add(i);
		}
	}
	if (Pool.Num() == 0)
	{
		return INDEX_NONE;
	}
	if (Pool.Num() > 1)
	{
		Pool.RemoveAll([&](int32 i) { return Kinds[i].Name == LastCreature; });   // variety when there's a choice
	}
	if (Pool.Num() == 0)
	{
		return INDEX_NONE;   // only possible if every candidate shared LastCreature's name (duplicates)
	}

	int32 Total = 0;
	for (const int32 i : Pool)
	{
		Total += FMath::Max(1, Kinds[i].Count);
	}
	int32 Roll = FMath::Clamp(RollFn(Total), 0, FMath::Max(0, Total - 1));
	for (const int32 i : Pool)
	{
		const int32 W = FMath::Max(1, Kinds[i].Count);
		if (Roll < W)
		{
			return i;
		}
		Roll -= W;
	}
	return Pool[0];
}

bool FWLBeastTaskRules::ParseLegacyFlag(const FString& Flag, FWLBeastTask& Out)
{
	if (!Flag.StartsWith(TEXT("bmtask|"), ESearchCase::CaseSensitive))
	{
		return false;
	}
	TArray<FString> Parts;
	Flag.ParseIntoArray(Parts, TEXT("|"), /*CullEmpty*/ false);
	if (Parts.Num() < 6)
	{
		return false;
	}
	auto ParseInt = [](const FString& S, int32& V) { return S.IsNumeric() && LexTryParseString(V, *S); };
	int32 Tier = 0, Done = 0, Req = 0, XP = 0;
	if (!ParseInt(Parts[2], Tier) || !ParseInt(Parts[3], Done) || !ParseInt(Parts[4], Req) || !ParseInt(Parts[5], XP))
	{
		return false;
	}
	if (Parts[1].IsEmpty() || Req <= 0 || Done >= Req)
	{
		return false;
	}
	Out.Creature = Parts[1];
	Out.Tier = FMath::Clamp(Tier, 1, 5);
	Out.Done = FMath::Max(0, Done);
	Out.Required = Req;
	Out.XPPerKill = FMath::Max(1, XP);
	return true;
}
