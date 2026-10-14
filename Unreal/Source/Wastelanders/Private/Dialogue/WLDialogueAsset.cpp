#include "Dialogue/WLDialogueAsset.h"
#if WITH_EDITOR
#include "Misc/DataValidation.h"
#endif

#define LOCTEXT_NAMESPACE "WLDialogue"

FWLDialogueNode& UWLDialogueAsset::AddNode(FName Id, std::initializer_list<FText> Lines, FName Next)
{
	FWLDialogueNode& N = Nodes.AddDefaulted_GetRef();
	N.Id = Id;
	N.Lines = TArray<FText>(Lines);
	N.Next = Next;
	return N;
}

FWLDialogueEntry& UWLDialogueAsset::AddEntry(FName Node)
{
	FWLDialogueEntry& E = Entries.AddDefaulted_GetRef();
	E.Node = Node;
	return E;
}

#if WITH_EDITOR
EDataValidationResult UWLDialogueAsset::IsDataValid(FDataValidationContext& Context) const
{
	EDataValidationResult Result = Super::IsDataValid(Context);
	auto Check = [&](FName Id, const TCHAR* Where)
	{
		if (!Id.IsNone() && !FindNode(Id))
		{
			Context.AddError(FText::Format(LOCTEXT("MissingNode", "{0} points at missing node '{1}'."), FText::FromString(Where), FText::FromName(Id)));
			Result = EDataValidationResult::Invalid;
		}
	};
	auto CheckActions = [&](const TArray<FWLDialogueAction>& Actions, const TCHAR* Where)
	{
		for (const FWLDialogueAction& A : Actions)
		{
			Check(A.FailNode, Where);
		}
	};
	CheckActions(PreActions, TEXT("PreActions"));
	for (const FWLDialogueEntry& E : Entries)
	{
		Check(E.Node, TEXT("Entry"));
		CheckActions(E.Actions, TEXT("Entry action"));
	}
	for (const FWLDialogueNode& N : Nodes)
	{
		Check(N.Next, TEXT("Node.Next"));
		CheckActions(N.OnEnter, TEXT("Node.OnEnter"));
		for (const FWLDialogueChoice& C : N.Choices)
		{
			Check(C.Next, TEXT("Choice.Next"));
			CheckActions(C.Actions, TEXT("Choice action"));
		}
	}
	return Result;
}
#endif

TArray<FString> FWLDialogueText::Paginate(const TArray<FString>& Lines)
{
	TArray<FString> Pages;
	FString Page;
	auto Add = [&](const FString& Sentence)
	{
		if (Page.Len() > 0 && Page.Len() + 1 + Sentence.Len() > MaxPageChars)
		{
			Pages.Add(Page);
			Page.Reset();
		}
		if (Page.Len() > 0)
		{
			Page.AppendChar(TEXT(' '));
		}
		Page += Sentence;
	};

	for (const FString& Line : Lines)
	{
		if (Line.IsEmpty() || Line.Len() <= MaxPageChars)
		{
			Pages.Add(Line);
			continue;
		}
		int32 Start = 0;
		for (int32 i = 0; i < Line.Len(); ++i)
		{
			const TCHAR C = Line[i];
			if ((C != TEXT('.') && C != TEXT('!') && C != TEXT('?')) || (i + 1 < Line.Len() && Line[i + 1] != TEXT(' ')))
			{
				continue;
			}
			const FString Sentence = Line.Mid(Start, i + 1 - Start).TrimStartAndEnd();
			Start = i + 1;
			if (!Sentence.IsEmpty())
			{
				Add(Sentence);
			}
		}
		const FString Rest = Line.Mid(Start).TrimStartAndEnd();
		if (!Rest.IsEmpty())
		{
			Add(Rest);
		}
		if (!Page.IsEmpty())
		{
			Pages.Add(Page);
			Page.Reset();
		}
	}
	return Pages;
}

#undef LOCTEXT_NAMESPACE
