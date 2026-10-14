#include "Dialogue/WLDialogueComponent.h"
#include "Dialogue/WLDialogueAsset.h"
#include "Quests/WLQuestComponent.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Companions/WLCompanionComponent.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Items/WLInventoryComponent.h"
#include "Items/WLBankComponent.h"
#include "Items/WLEquipmentComponent.h"
#include "Skills/WLSkillsComponent.h"
#include "Data/WLDataSubsystem.h"
#include "Core/WLXPTable.h"
#include "EngineUtils.h"
#include "TimerManager.h"
#include "Engine/World.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/Controller.h"

#define LOCTEXT_NAMESPACE "WLDialogue"

// Unity sources: UI/DialogueUI.cs (conversation flow, pagination, walk-away), UI/DialogueClickAdvance.cs
// (click / talk-again advances), and the branching formerly hard-coded in NPC/RoxyNPC.cs, TutorialNPC.cs
// and WandererNPC.cs (now data in UWLDialogueAsset, built by FWLBuiltinDialogues).

namespace
{
	constexpr int32 MaxNodeDepth = 16;     // OnEnter jumps / empty pass-through nodes per step
	constexpr float WalkAwayCheckSeconds = 0.5f;
}

UWLDialogueComponent::UWLDialogueComponent()
{
	PrimaryComponentTick.bCanEverTick = false;
	SetIsReplicatedByDefault(true);
}

UWLDialogueComponent* UWLDialogueComponent::Get(const AActor* PlayerActor)
{
	if (!PlayerActor)
	{
		return nullptr;
	}
	if (UWLDialogueComponent* Direct = PlayerActor->FindComponentByClass<UWLDialogueComponent>())
	{
		return Direct;
	}
	const APlayerState* PS = nullptr;
	if (const APawn* Pawn = Cast<APawn>(PlayerActor))
	{
		PS = Pawn->GetPlayerState();
	}
	else if (const AController* Controller = Cast<AController>(PlayerActor))
	{
		PS = Controller->PlayerState;
	}
	return PS ? PS->FindComponentByClass<UWLDialogueComponent>() : nullptr;
}

void UWLDialogueComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWorld* World = GetWorld())
	{
		World->GetTimerManager().ClearTimer(WalkAwayTimer);
	}
	Super::EndPlay(EndPlayReason);
}

// ================================================================================== server

AWLPlayerState* UWLDialogueComponent::GetPS() const
{
	return Cast<AWLPlayerState>(GetOwner());
}

AWLPlayerController* UWLDialogueComponent::GetPC() const
{
	const AWLPlayerState* PS = GetPS();
	return PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr;
}

FVector UWLDialogueComponent::GetPlayerLocation() const
{
	const AWLPlayerState* PS = GetPS();
	const APawn* Pawn = PS ? PS->GetPawn() : nullptr;
	return Pawn ? Pawn->GetActorLocation() : FVector::ZeroVector;
}

void UWLDialogueComponent::Message(const FText& Text) const
{
	if (Text.IsEmpty())
	{
		return;
	}
	if (AWLPlayerController* PC = GetPC())
	{
		PC->ClientGameMessage(Text);
	}
}

void UWLDialogueComponent::RequestUI(FName UIName)
{
	if (GetOwner() && GetOwner()->HasAuthority() && !UIName.IsNone())
	{
		ClientUIRequest(UIName);
	}
}

void UWLDialogueComponent::Talk(AActor* Npc, const UWLDialogueAsset* Asset, const FText& NpcName)
{
	if (!GetOwner() || !GetOwner()->HasAuthority())
	{
		return;
	}
	// DialogueClickAdvance / DialogueUI.IsShowingFor: talking to the same NPC again turns the page.
	if (bServerActive && IsConversationWith(Npc))
	{
		ClientAdvancePage();
		return;
	}
	StartConversation(Npc, Asset, NpcName);
}

bool UWLDialogueComponent::Begin(AActor* Npc, const UWLDialogueAsset* Asset, const FText& NpcName)
{
	const AWLPlayerState* PS = GetPS();
	if (!GetOwner() || !GetOwner()->HasAuthority() || !Npc || !Asset || !PS || !PS->IsCharacterLoaded())
	{
		return false;
	}
	// A new conversation replaces any other one (the client simply shows the new node).
	bServerActive = true;
	ActiveNpc = Npc;
	ActiveAsset = const_cast<UWLDialogueAsset*>(Asset);
	ActiveNpcName = NpcName;
	CurrentNodeId = NAME_None;
	VisibleChoices.Reset();
	Scratch.Reset();
	StartLocation = GetPlayerLocation();
	if (UWorld* World = GetWorld())
	{
		World->GetTimerManager().SetTimer(WalkAwayTimer, this, &UWLDialogueComponent::CheckWalkAway, WalkAwayCheckSeconds, true);
	}
	return true;
}

bool UWLDialogueComponent::StartConversation(AActor* Npc, const UWLDialogueAsset* Asset, const FText& NpcName)
{
	if (!Begin(Npc, Asset, NpcName))
	{
		return false;
	}

	FName Jump;
	const EActionsResult Pre = RunActions(Asset->PreActions, Jump);
	if (Pre == EActionsResult::Abort)
	{
		EndConversation();
		return false;
	}
	if (Pre == EActionsResult::Jump)
	{
		EnterNode(Jump);
		return bServerActive;
	}

	for (const FWLDialogueEntry& Entry : Asset->Entries)
	{
		if (!CheckAll(Entry.Conditions))
		{
			continue;
		}
		const EActionsResult R = RunActions(Entry.Actions, Jump);
		if (R == EActionsResult::Abort)
		{
			EndConversation();
			return false;
		}
		if (R == EActionsResult::Jump)
		{
			EnterNode(Jump);
			return bServerActive;
		}
		if (Entry.Node.IsNone())
		{
			EndConversation();   // the entry only had actions to run
			return true;
		}
		EnterNode(Entry.Node);
		return bServerActive;
	}

	EndConversation();   // nothing to say
	return false;
}

bool UWLDialogueComponent::StartAtNode(AActor* Npc, const UWLDialogueAsset* Asset, const FText& NpcName, FName NodeId)
{
	if (!Begin(Npc, Asset, NpcName))
	{
		return false;
	}
	EnterNode(NodeId);
	return bServerActive;
}

void UWLDialogueComponent::SayLines(AActor* Npc, const FText& Speaker, const TArray<FText>& Lines)
{
	if (!GetOwner() || !GetOwner()->HasAuthority() || !Npc || Lines.Num() == 0)
	{
		return;
	}
	const AWLPlayerState* PS = GetPS();
	if (!PS || !PS->IsCharacterLoaded())
	{
		return;
	}
	bServerActive = true;
	ActiveNpc = Npc;
	ActiveAsset = nullptr;
	ActiveNpcName = Speaker;
	CurrentNodeId = NAME_None;   // no graph: advancing past the last page ends the conversation
	VisibleChoices.Reset();
	Scratch.Reset();
	StartLocation = GetPlayerLocation();
	if (UWorld* World = GetWorld())
	{
		World->GetTimerManager().SetTimer(WalkAwayTimer, this, &UWLDialogueComponent::CheckWalkAway, WalkAwayCheckSeconds, true);
	}

	FWLDialogueNetNode Net;
	Net.Speaker = Speaker;
	for (const FText& Line : Lines)
	{
		Net.Lines.Add(Resolve(Line));
	}
	Net.Serial = ++ServerSerial;
	Net.Npc = Npc;
	ClientShowNode(Net);
}

void UWLDialogueComponent::EndConversation()
{
	if (!GetOwner() || !GetOwner()->HasAuthority())
	{
		return;
	}
	const bool bWasActive = bServerActive;
	bServerActive = false;
	ActiveNpc.Reset();
	ActiveAsset = nullptr;
	CurrentNodeId = NAME_None;
	VisibleChoices.Reset();
	Scratch.Reset();
	++ServerSerial;   // any in-flight reply is now stale
	if (UWorld* World = GetWorld())
	{
		World->GetTimerManager().ClearTimer(WalkAwayTimer);
	}
	if (bWasActive)
	{
		ClientEndDialogue();
	}
}

void UWLDialogueComponent::EnterNode(FName Id, int32 Depth)
{
	if (!bServerActive || !ActiveAsset || Id.IsNone() || Depth > MaxNodeDepth)
	{
		EndConversation();
		return;
	}
	const FWLDialogueNode* Node = ActiveAsset->FindNode(Id);
	if (!Node)
	{
		EndConversation();
		return;
	}
	CurrentNodeId = Id;

	FName Jump;
	const EActionsResult R = RunActions(Node->OnEnter, Jump);
	if (R == EActionsResult::Abort)
	{
		EndConversation();
		return;
	}
	if (R == EActionsResult::Jump)
	{
		EnterNode(Jump, Depth + 1);
		return;
	}
	if (!bServerActive)
	{
		return;   // an action ended it
	}

	bool bAnyChoice = false;
	for (const FWLDialogueChoice& Choice : Node->Choices)
	{
		if (CheckAll(Choice.Conditions))
		{
			bAnyChoice = true;
			break;
		}
	}
	if (Node->Lines.Num() == 0 && !bAnyChoice)
	{
		EnterNode(Node->Next, Depth + 1);   // pure action node: pass straight through
		return;
	}
	SendCurrentNode(*Node);
}

void UWLDialogueComponent::SendCurrentNode(const FWLDialogueNode& Node)
{
	FWLDialogueNetNode Net;
	Net.Speaker = Resolve(Node.Speaker.IsEmpty() ? ActiveNpcName : Node.Speaker);
	Net.Npc = ActiveNpc.Get();

	if (Node.bCycleOneLine && Node.Lines.Num() > 0)
	{
		// WandererNPC / TutorialNPC postDialogue: one line per visit, cycling.
		const FString Key = ActiveNpcName.ToString() + TEXT("|") + Node.Id.ToString();
		int32& Counter = CycleCounters.FindOrAdd(Key);
		Net.Lines.Add(Resolve(Node.Lines[Counter % Node.Lines.Num()]));
		Counter = (Counter + 1) % Node.Lines.Num();
	}
	else
	{
		for (const FText& Line : Node.Lines)
		{
			Net.Lines.Add(Resolve(Line));
		}
	}

	VisibleChoices.Reset();
	for (int32 i = 0; i < Node.Choices.Num(); ++i)
	{
		if (CheckAll(Node.Choices[i].Conditions))
		{
			VisibleChoices.Add(i);
			Net.Choices.Add(Resolve(Node.Choices[i].Label));
		}
	}

	Net.Serial = ++ServerSerial;
	ClientShowNode(Net);
}

void UWLDialogueComponent::CheckWalkAway()
{
	if (!bServerActive)
	{
		return;
	}
	const AActor* Npc = ActiveNpc.Get();
	const AWLPlayerState* PS = GetPS();
	const APawn* Pawn = PS ? PS->GetPawn() : nullptr;
	if (!Npc || !Pawn || FVector::Dist(Pawn->GetActorLocation(), StartLocation) > WalkAwayDistance)
	{
		EndConversation();
	}
}

// ------------------------------------------------------------------ RPCs (server side)

void UWLDialogueComponent::ServerAdvanceDialogue_Implementation(int32 Serial)
{
	if (!bServerActive || Serial != ServerSerial)
	{
		return;   // stale or forged
	}
	if (CurrentNodeId.IsNone() || !ActiveAsset)
	{
		EndConversation();   // SayLines, or a node-less conversation
		return;
	}
	const FWLDialogueNode* Node = ActiveAsset->FindNode(CurrentNodeId);
	if (!Node)
	{
		EndConversation();
		return;
	}
	if (VisibleChoices.Num() > 0)
	{
		return;   // a choice must be picked
	}
	EnterNode(Node->Next);
}

void UWLDialogueComponent::ServerChooseDialogueOption_Implementation(int32 Serial, int32 ChoiceIndex)
{
	if (!bServerActive || Serial != ServerSerial || !ActiveAsset || !VisibleChoices.IsValidIndex(ChoiceIndex))
	{
		return;
	}
	const FWLDialogueNode* Node = ActiveAsset->FindNode(CurrentNodeId);
	if (!Node || !Node->Choices.IsValidIndex(VisibleChoices[ChoiceIndex]))
	{
		EndConversation();
		return;
	}
	const FWLDialogueChoice& Choice = Node->Choices[VisibleChoices[ChoiceIndex]];
	if (!CheckAll(Choice.Conditions))
	{
		SendCurrentNode(*Node);   // state changed under the player: re-offer what is valid now
		return;
	}
	++ServerSerial;   // consume: a double click can't fire the choice twice

	FName Jump;
	const EActionsResult R = RunActions(Choice.Actions, Jump);
	if (R == EActionsResult::Abort)
	{
		EndConversation();
		return;
	}
	if (!bServerActive)
	{
		return;
	}
	EnterNode(R == EActionsResult::Jump ? Jump : Choice.Next);
}

void UWLDialogueComponent::ServerEndDialogue_Implementation()
{
	if (bServerActive)
	{
		EndConversation();
	}
}

// ------------------------------------------------------------------ actions

UWLDialogueComponent::EActionsResult UWLDialogueComponent::RunActions(const TArray<FWLDialogueAction>& Actions, FName& OutJump)
{
	for (const FWLDialogueAction& Action : Actions)
	{
		if (!CheckAll(Action.Conditions))
		{
			continue;   // skipped: neither success nor failure
		}
		const bool bOk = RunAction(Action);
		if (!Action.ResultVar.IsNone())
		{
			Scratch.Add(Action.ResultVar, bOk);
		}
		if (!bOk)
		{
			if (Action.bAbortOnFailure)
			{
				return EActionsResult::Abort;
			}
			if (!Action.FailNode.IsNone())
			{
				OutJump = Action.FailNode;
				return EActionsResult::Jump;
			}
		}
	}
	return EActionsResult::Continue;
}

bool UWLDialogueComponent::RunAction(const FWLDialogueAction& Action)
{
	AWLPlayerState* PS = GetPS();
	if (!PS)
	{
		return false;
	}
	UWLPlayerFlagsComponent* Flags = PS->GetFlags();
	UWLQuestComponent* Quests = PS->GetQuests();
	UWLSkillsComponent* Skills = PS->GetSkills();
	UWLCompanionComponent* Companions = PS->GetCompanions();

	switch (Action.Type)
	{
	case EWLDialogueActionType::SetFlag:
		if (Flags && !Action.Name.IsEmpty())
		{
			Flags->SetFlag(Action.Name);
			return true;
		}
		return false;

	case EWLDialogueActionType::RemoveFlag:
		if (Flags && !Action.Name.IsEmpty())
		{
			Flags->RemoveFlag(Action.Name);
			return true;
		}
		return false;

	case EWLDialogueActionType::GiveItem:
		return GiveItem(Action);

	case EWLDialogueActionType::GiveItemSet:
		return GiveItemSet(Action);

	case EWLDialogueActionType::GiveXP:
		if (Skills && Action.Value > 0)
		{
			Skills->AddXP(Action.Skill, Action.Value);
			return true;
		}
		return false;

	case EWLDialogueActionType::TeachSkill:
		if (Skills && Action.Value > 1)
		{
			if (Skills->GetLevel(Action.Skill) < Action.Value)
			{
				Skills->SetXP(Action.Skill, FWLXPTable::XPForLevel(Action.Skill, Action.Value));
			}
			return true;
		}
		return false;

	case EWLDialogueActionType::SetQuestStage:
		if (Quests && !Action.QuestId.IsNone())
		{
			Quests->SetQuestStage(Action.QuestId, Action.Value);
			return true;
		}
		return false;

	case EWLDialogueActionType::StartLesson:
		if (Quests)
		{
			Quests->LessonGiven(Action.Skill, Action.ItemId);
			return true;
		}
		return false;

	case EWLDialogueActionType::StartCombatTraining:
		if (Quests)
		{
			Quests->AwaitCombatTraining();
			return true;
		}
		return false;

	case EWLDialogueActionType::GiveFissionKit:
		return Quests && Quests->TryGiveFissionKit();

	case EWLDialogueActionType::StartBeastTask:
		return Quests && Quests->TryAssignBeastTask();

	case EWLDialogueActionType::SummonCompanion:
		if (Companions && Companions->HasCompanion())
		{
			Companions->Summon();
			return Companions->IsSummoned();
		}
		return false;

	case EWLDialogueActionType::OpenUI:
		if (!Action.Name.IsEmpty())
		{
			RequestUI(FName(*Action.Name));
			return true;
		}
		return false;

	case EWLDialogueActionType::Message:
		Message(Resolve(Action.Text));
		return true;

	case EWLDialogueActionType::SetScratch:
		if (!Action.Name.IsEmpty())
		{
			Scratch.Add(FName(*Action.Name), Action.Value != 0);
			return true;
		}
		return false;
	}
	return false;
}

bool UWLDialogueComponent::IsEquipped(int32 ItemId) const
{
	const AWLPlayerState* PS = GetPS();
	const UWLEquipmentComponent* Equipment = PS ? PS->GetEquipment() : nullptr;
	if (!Equipment || ItemId <= 0)
	{
		return false;
	}
	for (EWLEquipSlot Slot : TEnumRange<EWLEquipSlot>())
	{
		if (Equipment->GetItemId(Slot) == ItemId)
		{
			return true;
		}
	}
	return false;
}

bool UWLDialogueComponent::OwnsItem(int32 ItemId, int32 Qty, bool bIncludeBank) const
{
	const AWLPlayerState* PS = GetPS();
	if (!PS || ItemId <= 0)
	{
		return false;
	}
	Qty = FMath::Max(1, Qty);
	if (PS->GetInventory() && PS->GetInventory()->Contains(ItemId, Qty))
	{
		return true;
	}
	if (IsEquipped(ItemId))
	{
		return true;
	}
	return bIncludeBank && PS->GetBank() && PS->GetBank()->Contains(ItemId, Qty);
}

bool UWLDialogueComponent::GiveItem(const FWLDialogueAction& Action)
{
	// RoxyNPC.GiveQuestItem: true when handled (bagged, already owned, or nothing to give); false on a full bag.
	AWLPlayerState* PS = GetPS();
	UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	const bool bHasOnceFlag = !Action.Name.IsEmpty();
	if (bHasOnceFlag && Flags && Flags->HasFlag(Action.Name))
	{
		return true;   // already handed over once
	}
	auto MarkDone = [&]()
	{
		if (bHasOnceFlag && Flags)
		{
			Flags->SetFlag(Action.Name);
		}
	};
	if (Action.ItemId <= 0)
	{
		MarkDone();
		return true;
	}
	UWLInventoryComponent* Inventory = PS ? PS->GetInventory() : nullptr;
	if (!Inventory)
	{
		return false;
	}

	const int32 Qty = FMath::Max(1, Action.Quantity);
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	FFormatNamedArguments Args;
	Args.Add(TEXT("item"), Data ? Data->GetItemName(Action.ItemId) : FText::AsNumber(Action.ItemId));
	Args.Add(TEXT("count"), Qty > 1 ? FText::Format(LOCTEXT("CountPrefix", "{0}x "), FText::AsNumber(Qty)) : FText::GetEmpty());
	Args.Add(TEXT("qty"), FText::AsNumber(Qty));
	auto Say = [&](const FText& Pattern)
	{
		if (!Pattern.IsEmpty())
		{
			Message(FText::Format(Pattern, Args));
		}
	};

	if (Action.bSkipIfOwned)
	{
		if (Inventory->Contains(Action.ItemId, Qty) || IsEquipped(Action.ItemId))
		{
			Say(Action.TextAlreadyOwned);
			MarkDone();
			return true;
		}
		if (PS->GetBank() && PS->GetBank()->Contains(Action.ItemId, Qty))
		{
			Say(Action.TextInBank);
			MarkDone();
			return true;
		}
	}

	if (Inventory->AddItem(Action.ItemId, Qty))
	{
		Say(Action.Text);
		MarkDone();
		return true;
	}
	Say(Action.TextNoRoom);
	return false;
}

bool UWLDialogueComponent::GiveItemSet(const FWLDialogueAction& Action)
{
	// TutorialNPC.TryGiveGifts: hand over every kit item the player neither carries nor wears.
	AWLPlayerState* PS = GetPS();
	UWLInventoryComponent* Inventory = PS ? PS->GetInventory() : nullptr;
	if (!Inventory)
	{
		return false;
	}
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	bool bAnyGiven = false;
	bool bFull = false;
	for (const int32 Id : Action.ItemIds)
	{
		if (Id <= 0 || Inventory->Contains(Id) || IsEquipped(Id))
		{
			continue;
		}
		if (Inventory->AddItem(Id, 1))
		{
			bAnyGiven = true;
			if (!Action.Text.IsEmpty())
			{
				FFormatNamedArguments Args;
				Args.Add(TEXT("item"), Data ? Data->GetItemName(Id) : FText::AsNumber(Id));
				Args.Add(TEXT("count"), FText::GetEmpty());
				Args.Add(TEXT("qty"), FText::AsNumber(1));
				Message(FText::Format(Action.Text, Args));
			}
		}
		else
		{
			bFull = true;
		}
	}
	if (bFull)
	{
		Message(Action.TextNoRoom);
	}
	return bAnyGiven;
}

// ------------------------------------------------------------------ conditions

bool UWLDialogueComponent::CheckAll(const TArray<FWLDialogueCondition>& Conditions) const
{
	for (const FWLDialogueCondition& C : Conditions)
	{
		if (!CheckCondition(C))
		{
			return false;
		}
	}
	return true;
}

bool UWLDialogueComponent::CheckCondition(const FWLDialogueCondition& C) const
{
	const AWLPlayerState* PS = GetPS();
	if (!PS)
	{
		return false;
	}
	const UWLPlayerFlagsComponent* Flags = PS->GetFlags();
	const UWLQuestComponent* Quests = PS->GetQuests();
	const UWLSkillsComponent* Skills = PS->GetSkills();
	const UWLCompanionComponent* Companions = PS->GetCompanions();

	switch (C.Type)
	{
	case EWLDialogueConditionType::FlagSet:
		return Flags && Flags->HasFlag(C.Name);
	case EWLDialogueConditionType::FlagNotSet:
		return !Flags || !Flags->HasFlag(C.Name);
	case EWLDialogueConditionType::SkillAtLeast:
		return Skills && Skills->GetLevel(C.Skill) >= C.Value;
	case EWLDialogueConditionType::SkillBelow:
		return Skills && Skills->GetLevel(C.Skill) < C.Value;
	case EWLDialogueConditionType::HasItem:
		return PS->GetInventory() && PS->GetInventory()->Contains(C.ItemId, FMath::Max(1, C.Value));
	case EWLDialogueConditionType::OwnsItem:
		return OwnsItem(C.ItemId, FMath::Max(1, C.Value), /*bIncludeBank*/ true);
	case EWLDialogueConditionType::QuestStageEquals:
		return Quests && Quests->GetQuestStage(C.QuestId) == C.Value;
	case EWLDialogueConditionType::QuestStageAtLeast:
		return Quests && Quests->GetQuestStage(C.QuestId) >= C.Value;
	case EWLDialogueConditionType::QuestStageBelow:
		return Quests && Quests->GetQuestStage(C.QuestId) < C.Value;
	case EWLDialogueConditionType::LessonActive:
	{
		EWLSkill Skill = EWLSkill::Attack;
		int32 Item = 0;
		if (!Quests || !Quests->TryActiveLesson(Skill, Item))
		{
			return false;
		}
		return (C.Value == 1 || Skill == C.Skill) && (C.ItemId == -1 || Item == C.ItemId);
	}
	case EWLDialogueConditionType::BeastTaskActive:
		return Quests && Quests->HasBeastTask();
	case EWLDialogueConditionType::BeastTaskInactive:
		return !Quests || !Quests->HasBeastTask();
	case EWLDialogueConditionType::HasCompanion:
		return Companions && Companions->HasCompanion();
	case EWLDialogueConditionType::CompanionSummoned:
		return Companions && Companions->IsSummoned();
	case EWLDialogueConditionType::ScratchSet:
		return Scratch.FindRef(FName(*C.Name));
	case EWLDialogueConditionType::ScratchNotSet:
		return !Scratch.FindRef(FName(*C.Name));
	case EWLDialogueConditionType::TownBanksExist:
	{
		// BankGuideUI.HasTownBanks: designers tag the town's bank crates "TownBank".
		UWorld* World = GetWorld();
		if (!World)
		{
			return false;
		}
		static const FName TownBankTag(TEXT("TownBank"));
		for (TActorIterator<AActor> It(World); It; ++It)
		{
			if (It->ActorHasTag(TownBankTag))
			{
				return true;
			}
		}
		return false;
	}
	}
	return false;
}

FText UWLDialogueComponent::Resolve(const FText& In) const
{
	const FString& Source = In.ToString();
	int32 BraceIndex = INDEX_NONE;
	if (In.IsEmpty() || !Source.FindChar(TEXT('{'), BraceIndex))
	{
		return In;
	}
	const AWLPlayerState* PS = GetPS();
	const UWLQuestComponent* Quests = PS ? PS->GetQuests() : nullptr;

	FFormatNamedArguments Args;
	Args.Add(TEXT("player"), FText::FromString(PS ? PS->GetPlayerName() : FString()));
	Args.Add(TEXT("npc"), ActiveNpcName);
	if (Quests && Quests->HasBeastTask())
	{
		const FWLBeastTask& Task = Quests->GetBeastTask();
		Args.Add(TEXT("task_done"), FText::AsNumber(Task.Done));
		Args.Add(TEXT("task_required"), FText::AsNumber(Task.Required));
		Args.Add(TEXT("task_creature"), FText::FromString(Task.Creature));
	}
	else
	{
		Args.Add(TEXT("task_done"), FText::AsNumber(0));
		Args.Add(TEXT("task_required"), FText::AsNumber(0));
		Args.Add(TEXT("task_creature"), FText::GetEmpty());
	}
	Args.Add(TEXT("combat_progress"), Quests ? Quests->GetCombatProgress() : FText::GetEmpty());
	return FText::Format(In, Args);
}

// ================================================================================== client

void UWLDialogueComponent::ClientShowNode_Implementation(const FWLDialogueNetNode& Node)
{
	ClientNode = Node;
	TArray<FString> Raw;
	Raw.Reserve(Node.Lines.Num());
	for (const FText& Line : Node.Lines)
	{
		Raw.Add(Line.ToString());
	}
	ClientPages = FWLDialogueText::Paginate(Raw);
	if (ClientPages.Num() == 0)
	{
		ClientPages.Add(FString());   // a choice-only node still needs a box
	}
	ClientPage = 0;
	bAwaitingServer = false;
	ShowPage();
}

void UWLDialogueComponent::ClientEndDialogue_Implementation()
{
	ClearClient(/*bBroadcast*/ true);
}

void UWLDialogueComponent::ClientAdvancePage_Implementation()
{
	Continue();
}

void UWLDialogueComponent::ClientUIRequest_Implementation(FName UIName)
{
	OnUIRequest.Broadcast(UIName);
}

void UWLDialogueComponent::ShowPage()
{
	const bool bLast = ClientPage >= ClientPages.Num() - 1;
	View.Speaker = ClientNode.Speaker;
	View.Line = ClientPages.IsValidIndex(ClientPage) ? FText::FromString(ClientPages[ClientPage]) : FText::GetEmpty();
	View.Choices = bLast ? ClientNode.Choices : TArray<FText>();
	View.bActive = true;
	View.bHasMorePages = !bLast;
	View.Serial = ClientNode.Serial;
	View.Npc = ClientNode.Npc;
	OnDialogueUpdated.Broadcast(View);
}

void UWLDialogueComponent::ClearClient(bool bBroadcast)
{
	const bool bWasActive = View.bActive;
	ClientNode = FWLDialogueNetNode();
	ClientPages.Reset();
	ClientPage = 0;
	bAwaitingServer = false;
	View = FWLDialogueView();
	if (bBroadcast && bWasActive)
	{
		OnDialogueEnded.Broadcast();
	}
}

void UWLDialogueComponent::Continue()
{
	if (!View.bActive || bAwaitingServer)
	{
		return;
	}
	if (ClientPage < ClientPages.Num() - 1)
	{
		++ClientPage;
		ShowPage();
		return;
	}
	if (ClientNode.Choices.Num() > 0)
	{
		return;   // pick one
	}
	bAwaitingServer = true;
	ServerAdvanceDialogue(ClientNode.Serial);
}

void UWLDialogueComponent::Choose(int32 ChoiceIndex)
{
	if (!View.bActive || bAwaitingServer || View.bHasMorePages || !ClientNode.Choices.IsValidIndex(ChoiceIndex))
	{
		return;
	}
	bAwaitingServer = true;
	ServerChooseDialogueOption(ClientNode.Serial, ChoiceIndex);
}

void UWLDialogueComponent::RequestEnd()
{
	const bool bWasActive = View.bActive;
	ClearClient(/*bBroadcast*/ true);
	if (bWasActive)
	{
		ServerEndDialogue();
	}
}

#undef LOCTEXT_NAMESPACE
