#include "Quests/WLQuestComponent.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Quests/WLQuestFlags.h"
#include "Quests/WLStarterGuide.h"
#include "Quests/WLJournalLore.h"
#include "Quests/WLBlackwaterAtlas.h"
#include "Quests/WLQuestTargets.h"
#include "Quests/WLQuestCam.h"
#include "Dialogue/WLDialogueComponent.h"
#include "Companions/WLCompanionComponent.h"
#include "NPC/WLNPC.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerCharacter.h"
#include "Items/WLInventoryComponent.h"
#include "Items/WLBankComponent.h"
#include "Items/WLEquipmentComponent.h"
#include "Skills/WLSkillsComponent.h"
#include "Combat/WLEnemyCharacter.h"
#include "Core/WLGameEventsSubsystem.h"
#include "Core/WLGameTickSubsystem.h"
#include "Data/WLDataSubsystem.h"
#include "EngineUtils.h"
#include "Engine/World.h"
#include "TimerManager.h"
#include "Net/UnrealNetwork.h"

#define LOCTEXT_NAMESPACE "WLQuests"

namespace WLQuestPrivate
{
	const FName CounterSkill(TEXT("Skill"));
	const FName CounterItem(TEXT("Item"));
	const FName CounterDone(TEXT("Done"));
	const FName CounterRequired(TEXT("Required"));
	const FName CounterXP(TEXT("XPPerKill"));
	const TCHAR* BeastTaskQuestPrefix = TEXT("bmtask|");
}

bool FWLQuestObjective::GetPoint(FVector& Out) const
{
	if (const AActor* T = Target.Get())
	{
		Out = T->GetActorLocation();
		return true;
	}
	if (bHasLocation)
	{
		Out = Location;
		return true;
	}
	return false;
}

UWLQuestComponent::UWLQuestComponent()
{
	PrimaryComponentTick.bCanEverTick = false;
	SetIsReplicatedByDefault(true);
}

void UWLQuestComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME_CONDITION(UWLQuestComponent, BeastTask, COND_OwnerOnly);
	DOREPLIFETIME_CONDITION(UWLQuestComponent, GuideState, COND_OwnerOnly);
	DOREPLIFETIME_CONDITION(UWLQuestComponent, Objective, COND_OwnerOnly);
	DOREPLIFETIME_CONDITION(UWLQuestComponent, JournalPagesRead, COND_OwnerOnly);
	DOREPLIFETIME_CONDITION(UWLQuestComponent, bHuntableNearby, COND_OwnerOnly);
}

void UWLQuestComponent::BeginPlay()
{
	Super::BeginPlay();
	AActor* Owner = GetOwner();
	if (!Owner || !Owner->HasAuthority())
	{
		return;
	}

	// The conversation component rides on the PlayerState too. Create it early (long before anyone
	// talks) so it has replicated to the client by the time its first Client RPC is sent.
	if (!Owner->FindComponentByClass<UWLDialogueComponent>())
	{
		UWLDialogueComponent* Dialogue = NewObject<UWLDialogueComponent>(Owner, TEXT("Dialogue"));
		Dialogue->SetIsReplicated(true);
		Owner->AddInstanceComponent(Dialogue);
		Dialogue->RegisterComponent();
	}

	if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
	{
		TickHandle = Tick->OnGameTick.AddUObject(this, &UWLQuestComponent::HandleGameTick);
	}
	if (UWLGameEventsSubsystem* Events = UWLGameEventsSubsystem::Get(this))
	{
		KilledHandle = Events->OnEnemyKilled.AddUObject(this, &UWLQuestComponent::HandleEnemyKilled);
		XPHandle = Events->OnXPGained.AddUObject(this, &UWLQuestComponent::HandleXPGained);
	}
	GuideStartedAt = GetWorld()->GetTimeSeconds();
}

void UWLQuestComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
	{
		Tick->OnGameTick.Remove(TickHandle);
	}
	if (UWLGameEventsSubsystem* Events = UWLGameEventsSubsystem::Get(this))
	{
		Events->OnEnemyKilled.Remove(KilledHandle);
		Events->OnXPGained.Remove(XPHandle);
	}
	Super::EndPlay(EndPlayReason);
}

// ------------------------------------------------------------------ accessors

AWLPlayerState* UWLQuestComponent::GetPS() const { return Cast<AWLPlayerState>(GetOwner()); }

AWLPlayerController* UWLQuestComponent::GetPC() const
{
	const AWLPlayerState* PS = GetPS();
	AWLPlayerController* PC = PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr;
	if (!PC && GetWorld() && GetWorld()->GetNetMode() != NM_DedicatedServer)
	{
		PC = Cast<AWLPlayerController>(GetWorld()->GetFirstPlayerController());
	}
	return PC;
}

AWLPlayerCharacter* UWLQuestComponent::GetPawnChar() const
{
	const AWLPlayerState* PS = GetPS();
	return PS ? PS->GetPawn<AWLPlayerCharacter>() : nullptr;
}

UWLPlayerFlagsComponent* UWLQuestComponent::GetFlags() const
{
	const AWLPlayerState* PS = GetPS();
	return PS ? PS->GetFlags() : nullptr;
}

int32 UWLQuestComponent::GetLevel(EWLSkill Skill) const
{
	const AWLPlayerState* PS = GetPS();
	return PS && PS->GetSkills() ? PS->GetSkills()->GetLevel(Skill) : 1;
}

void UWLQuestComponent::Say(const FText& Text) const
{
	if (AWLPlayerController* PC = GetPC(); PC && GetOwner()->HasAuthority())
	{
		PC->ClientGameMessage(Text);
	}
}

void UWLQuestComponent::SayBeastTask(const FText& Text) const
{
	Say(FText::Format(LOCTEXT("BeastTaskPrefix", "[BEAST TASK]: {0}"), Text));
}

bool UWLQuestComponent::OwnsBeast() const
{
	const UWLPlayerFlagsComponent* Flags = GetFlags();
	return Flags && Flags->HasFlag(WLQuestFlags::PupHatched);
}

bool UWLQuestComponent::IsBeastOut() const
{
	const AWLPlayerState* PS = GetPS();
	const UWLCompanionComponent* C = PS ? PS->GetCompanions() : nullptr;
	if (!C)
	{
		return false;
	}
	return GetOwner()->HasAuthority() ? C->IsSummoned() : C->GetStatus().bActive;
}

bool UWLQuestComponent::HoldsRawCore() const
{
	const AWLPlayerState* PS = GetPS();
	const UWLInventoryComponent* Inv = PS ? PS->GetInventory() : nullptr;
	if (!Inv)
	{
		return false;
	}
	for (int32 Tier = 1; Tier <= 5; ++Tier)
	{
		if (Inv->Contains(WLQuestItems::RawCore(Tier)))
		{
			return true;
		}
	}
	return false;
}

bool UWLQuestComponent::IsOnMainland() const
{
	const FString Map = UWorld::RemovePIEPrefix(GetWorld()->GetMapName());
	for (const FString& Prefix : MainlandMapPrefixes)
	{
		if (!Prefix.IsEmpty() && Map.StartsWith(Prefix))
		{
			return true;
		}
	}
	return false;
}

void UWLQuestComponent::ReplicatedStateChanged()
{
	GetOwner()->ForceNetUpdate();
	OnQuestStateChanged.Broadcast();
}

void UWLQuestComponent::OnRep_QuestState()
{
	OnQuestStateChanged.Broadcast();
}

// ------------------------------------------------------------------ generic quest stages

FWLSaveQuestState* UWLQuestComponent::FindQuest(FName QuestId)
{
	return Quests.FindByPredicate([QuestId](const FWLSaveQuestState& Q) { return Q.QuestId == QuestId; });
}

const FWLSaveQuestState* UWLQuestComponent::FindQuest(FName QuestId) const
{
	return Quests.FindByPredicate([QuestId](const FWLSaveQuestState& Q) { return Q.QuestId == QuestId; });
}

int32 UWLQuestComponent::GetQuestStage(FName QuestId) const
{
	const FWLSaveQuestState* Q = FindQuest(QuestId);
	return Q ? Q->Stage : 0;
}

void UWLQuestComponent::SetQuestStage(FName QuestId, int32 Stage)
{
	if (!GetOwner()->HasAuthority() || QuestId.IsNone())
	{
		return;
	}
	FWLSaveQuestState* Q = FindQuest(QuestId);
	if (!Q)
	{
		Q = &Quests.AddDefaulted_GetRef();
		Q->QuestId = QuestId;
	}
	Q->Stage = Stage;
}

int32 UWLQuestComponent::GetQuestCounter(FName QuestId, FName Counter) const
{
	const FWLSaveQuestState* Q = FindQuest(QuestId);
	const int32* V = Q ? Q->Counters.Find(Counter) : nullptr;
	return V ? *V : 0;
}

void UWLQuestComponent::SetQuestCounter(FName QuestId, FName Counter, int32 Value)
{
	if (!GetOwner()->HasAuthority() || QuestId.IsNone())
	{
		return;
	}
	FWLSaveQuestState* Q = FindQuest(QuestId);
	if (!Q)
	{
		Q = &Quests.AddDefaulted_GetRef();
		Q->QuestId = QuestId;
	}
	Q->Counters.Add(Counter, Value);
}

// ------------------------------------------------------------------ tick

void UWLQuestComponent::HandleGameTick(int64 /*TickCount*/)
{
	const AWLPlayerState* PS = GetPS();
	if (!PS || !PS->IsCharacterLoaded() || !GetPawnChar())
	{
		return;
	}
	const double Now = GetWorld()->GetTimeSeconds();

	if (!bArrivalChecked)
	{
		bArrivalChecked = true;
		UpdateBlackwaterArrival();
	}
	if (Now >= NextJournalCheck)
	{
		NextJournalCheck = Now + 1.0;   // a light cadence is plenty for level / map gates
		UpdateJournal();
	}
	UpdateBeastTasks(Now);
	UpdateStarterGuide(Now);
}

// ------------------------------------------------------------------ journal (JournalManager.cs)

void UWLQuestComponent::UpdateJournal()
{
	AWLPlayerState* PS = GetPS();
	UWLPlayerFlagsComponent* Flags = GetFlags();
	UWLInventoryComponent* Inv = PS->GetInventory();
	if (!Flags || !Inv || !PS->GetSkills())
	{
		return;
	}
	const bool bOnMainland = IsOnMainland();
	const int32 Total = PS->GetSkills()->GetTotalLevel();
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);

	for (const FWLJournalEntry& E : FWLJournalLore::All())
	{
		const FString Flag = WLQuestFlags::Journal(E.ItemId);
		if (Flags->HasFlag(Flag) || !FWLJournalLore::IsDue(E, bOnMainland, Total))
		{
			continue;
		}
		if (Inv->IsFull() || !Inv->AddItem(E.ItemId, 1))
		{
			continue;   // no room: try again next check
		}
		Flags->SetFlag(Flag);
		const FText Name = Data ? Data->GetItemName(E.ItemId) : E.Title;
		Say(FText::Format(LOCTEXT("JournalGranted", "[JOURNAL]: A weathered journal page is in your pack \u2014 {0}. Click it in your inventory to read."), Name));
	}
}

bool UWLQuestComponent::ReadJournalPage(int32 ItemId)
{
	AWLPlayerState* PS = GetPS();
	if (!PS || !GetOwner()->HasAuthority() || !FWLJournalLore::Find(ItemId) || !PS->GetInventory() || !PS->GetInventory()->Contains(ItemId))
	{
		return false;
	}
	if (!JournalPagesRead.Contains(ItemId))
	{
		JournalPagesRead.Add(ItemId);
		ReplicatedStateChanged();
	}
	ClientOpenJournalPage(ItemId);
	return true;
}

void UWLQuestComponent::ClientOpenJournalPage_Implementation(int32 ItemId)
{
	OnJournalPageOpened.Broadcast(ItemId);
}

// ------------------------------------------------------------------ Blackwater (BlackwaterArrival.cs)

void UWLQuestComponent::UpdateBlackwaterArrival()
{
	UWLPlayerFlagsComponent* Flags = GetFlags();
	AWLPlayerState* PS = GetPS();
	if (!Flags || !PS->GetInventory() || !FWLBlackwaterAtlas::IsBlackwaterWorld(GetWorld()) || Flags->HasFlag(WLQuestFlags::BlackwaterArrived))
	{
		return;
	}
	Flags->SetFlag(WLQuestFlags::BlackwaterArrived);
	for (const int32 Id : FWLBlackwaterAtlas::ArrivalItems())
	{
		PS->GetInventory()->AddItem(Id, 1);
	}
	Say(LOCTEXT("BlackwaterWelcome", "Welcome to Hearthwick. Your gathering tools are in your bag. Bank and workshops are on the square; press F8 for the Blackwater atlas."));
}

// ------------------------------------------------------------------ beast tasks (BeastTasks.cs)

bool UWLQuestComponent::IsHuntable(const AActor* Enemy)
{
	const AWLEnemyCharacter* E = Cast<AWLEnemyCharacter>(Enemy);
	return E && E->IsHuntable();
}

FString UWLQuestComponent::CreatureNameOf(const AActor* Enemy)
{
	const AWLEnemyCharacter* E = Cast<AWLEnemyCharacter>(Enemy);
	return FWLBeastTaskRules::NormalizeCreatureName(E ? E->GetEnemyDisplayName().ToString() : FString());
}

int32 UWLQuestComponent::CreatureMaxHPOf(const AActor* Enemy)
{
	const AWLEnemyCharacter* E = Cast<AWLEnemyCharacter>(Enemy);
	return FMath::Max(1, E ? E->GetMaxHP() : 1);
}

int32 UWLQuestComponent::CreatureTierOf(const AActor* Enemy)
{
	const AWLEnemyCharacter* E = Cast<AWLEnemyCharacter>(Enemy);
	return FMath::Clamp(E ? E->GetTier() : 1, 1, 5);
}

bool UWLQuestComponent::MatchesCurrentTask(const AActor* Enemy) const
{
	if (!BeastTask.IsValid() || !Enemy)
	{
		return false;
	}
	if (CreatureNameOf(Enemy) == BeastTask.Creature)
	{
		return true;
	}
	// Travelled somewhere your target doesn't live? Any creature of the task's tier counts there.
	const bool bTargetLivesHere = HuntableKinds.ContainsByPredicate([this](const FWLBeastTaskRules::FKind& K) { return K.Name == BeastTask.Creature; });
	return !bTargetLivesHere && CreatureTierOf(Enemy) == BeastTask.Tier;
}

void UWLQuestComponent::ScanHuntable()
{
	HuntableKinds.Reset();
	const AWLPlayerCharacter* Char = GetPawnChar();
	if (!Char)
	{
		return;
	}
	const FVector From = Char->GetActorLocation();
	const double RadiusSq = BeastTaskScanRadius > 0.f ? FMath::Square(double(BeastTaskScanRadius)) : TNumericLimits<double>::Max();

	for (TActorIterator<AWLEnemyCharacter> It(GetWorld()); It; ++It)
	{
		const AWLEnemyCharacter* E = *It;
		// Dead ones count too: they respawn, and Unity included inactive objects.
		if (!E || !E->IsHuntable() || FVector::DistSquared(E->GetActorLocation(), From) > RadiusSq)
		{
			continue;
		}
		const FString Name = CreatureNameOf(E);
		FWLBeastTaskRules::FKind* Kind = HuntableKinds.FindByPredicate([&Name](const FWLBeastTaskRules::FKind& K) { return K.Name == Name; });
		if (!Kind)
		{
			Kind = &HuntableKinds.AddDefaulted_GetRef();
			Kind->Name = Name;
			Kind->Tier = CreatureTierOf(E);
			Kind->MaxHP = CreatureMaxHPOf(E);
		}
		++Kind->Count;
	}

	const bool bAny = HuntableKinds.Num() > 0;
	if (bAny != bHuntableNearby)
	{
		bHuntableNearby = bAny;
		ReplicatedStateChanged();
	}
}

void UWLQuestComponent::UpdateBeastTasks(double Now)
{
	if (Now >= NextScanAt)
	{
		ScanHuntable();
		NextScanAt = Now + FWLBeastTaskRules::RescanSeconds;
		if (bRemindAfterScan)
		{
			bRemindAfterScan = false;
			RemindBeastTask();   // on arrival: tell the player where their task stands
		}
	}

	if (!OwnsBeast() || BeastTask.IsValid())
	{
		return;
	}
	if (!BeastTask.Creature.IsEmpty())
	{
		BeastTask = FWLBeastTask();   // anything left by a bad / finished record is cleared
		ReplicatedStateChanged();
	}
	if (Now >= NextAssignAt && !AssignBeastTask())
	{
		NextAssignAt = Now + FWLBeastTaskRules::RetryAssignSeconds;   // nothing huntable here yet
	}
}

bool UWLQuestComponent::TryAssignBeastTask()
{
	if (!GetOwner()->HasAuthority() || !OwnsBeast() || BeastTask.IsValid())
	{
		return false;
	}
	if (HuntableKinds.Num() == 0)
	{
		ScanHuntable();
	}
	return AssignBeastTask();
}

bool UWLQuestComponent::AssignBeastTask()
{
	if (HuntableKinds.Num() == 0)
	{
		return false;
	}
	const int32 Want = FWLBeastTaskRules::TierForLevel(GetLevel(EWLSkill::Beastmastery));
	const int32 Tier = FWLBeastTaskRules::PickTier(Want, HuntableKinds);
	const int32 Index = FWLBeastTaskRules::PickKind(HuntableKinds, Tier, LastCreature, [](int32 Total) { return FMath::RandRange(0, FMath::Max(0, Total - 1)); });
	if (Index == INDEX_NONE)
	{
		return false;
	}
	const FWLBeastTaskRules::FKind& Pick = HuntableKinds[Index];

	BeastTask.Creature = Pick.Name;
	BeastTask.Tier = Tier;
	BeastTask.Done = 0;
	BeastTask.Required = FMath::RandRange(FWLBeastTaskRules::MinKills(Tier), FWLBeastTaskRules::MaxKills(Tier));
	BeastTask.XPPerKill = FMath::Max(1, Pick.MaxHP);
	ReplicatedStateChanged();

	const FText Note = Tier < Want
		? FText::Format(LOCTEXT("TierNote", " (nothing of tier {0} lives around here, so it's a tier {1} hunt)"), Want, Tier)
		: FText::GetEmpty();
	SayBeastTask(FText::Format(LOCTEXT("NewTask", "New beast task: kill {0} \u00D7 {1} with your beast at your side \u2014 +{2} Beastmastery XP per kill{3}."),
		BeastTask.Required, FText::FromString(BeastTask.Creature), BeastTask.XPPerKill, Note));
	if (!IsBeastOut())
	{
		SayBeastTask(LOCTEXT("SummonHint", "Summon your beast with B \u2014 kills only count while it's out with you."));
	}
	return true;
}

void UWLQuestComponent::RemindBeastTask()
{
	if (!OwnsBeast() || !BeastTask.IsValid())
	{
		return;
	}
	const bool bLivesHere = HuntableKinds.ContainsByPredicate([this](const FWLBeastTaskRules::FKind& K) { return K.Name == BeastTask.Creature; });
	const FText Where = bLivesHere ? FText::GetEmpty()
		: FText::Format(LOCTEXT("NotHere", " They don't live around here \u2014 any tier {0} creature counts while you're in this area."), BeastTask.Tier);
	SayBeastTask(FText::Format(LOCTEXT("Remind", "Current beast task: {0}/{1} {2}.{3}"), BeastTask.Done, BeastTask.Required, FText::FromString(BeastTask.Creature), Where));
}

void UWLQuestComponent::HandleEnemyKilled(AActor* Enemy, AWLPlayerState* Killer)
{
	AWLPlayerState* PS = GetPS();
	if (!PS || Killer != PS || !OwnsBeast() || !IsHuntable(Enemy) || !BeastTask.IsValid())
	{
		return;
	}
	const AWLPlayerCharacter* Char = GetPawnChar();
	if (!Char || FVector::DistSquared(Enemy->GetActorLocation(), Char->GetActorLocation()) > FMath::Square(FWLBeastTaskRules::KillRange))
	{
		return;
	}
	if (!MatchesCurrentTask(Enemy))
	{
		return;
	}

	const double Now = GetWorld()->GetTimeSeconds();
	if (!IsBeastOut())
	{
		if (Now >= NextHintAt)
		{
			NextHintAt = Now + FWLBeastTaskRules::BeastOutHintSeconds;
			SayBeastTask(LOCTEXT("NoBeastKill", "That kill didn't count \u2014 your beast has to be out with you. Press B to summon it."));
		}
		return;
	}

	UWLSkillsComponent* Skills = PS->GetSkills();
	const int32 XP = FWLBeastTaskRules::XPForKill(CreatureMaxHPOf(Enemy));
	++BeastTask.Done;
	if (Skills)
	{
		Skills->AddXP(EWLSkill::Beastmastery, XP);
	}

	if (BeastTask.Done >= BeastTask.Required)
	{
		const FWLBeastTask Finished = BeastTask;
		const int32 Bonus = FWLBeastTaskRules::CompletionBonus(Finished.Required, Finished.XPPerKill);
		if (Skills)
		{
			Skills->AddXP(EWLSkill::Beastmastery, Bonus);
		}
		LastCreature = Finished.Creature;
		BeastTask = FWLBeastTask();
		NextAssignAt = Now + FWLBeastTaskRules::NextTaskDelay;
		ReplicatedStateChanged();
		SayBeastTask(FText::Format(LOCTEXT("TaskDone", "Task complete! {0} \u00D7 {1} hunted down with your beast. Bonus +{2} Beastmastery XP \u2014 your next task is on its way."),
			Finished.Required, FText::FromString(Finished.Creature), Bonus));
		OnBeastTaskCompleted.Broadcast(PS);

		// The hunting lesson ends when the whole task is done (never on a single kill).
		EWLSkill LessonSkill;
		int32 LessonItem;
		if (TryActiveLesson(LessonSkill, LessonItem) && LessonSkill == EWLSkill::Beastmastery)
		{
			FinishLesson();
		}
	}
	else
	{
		ReplicatedStateChanged();
		SayBeastTask(FText::Format(LOCTEXT("TaskKill", "{0}/{1} {2}  (+{3} Beastmastery XP)"), BeastTask.Done, BeastTask.Required, FText::FromString(BeastTask.Creature), XP));
	}
}

bool UWLQuestComponent::GetBeastTaskHud(FText& OutLabel, FText& OutValue, float& OutFill, bool& bOutAlert) const
{
	OutLabel = FText::GetEmpty();
	OutValue = FText::GetEmpty();
	OutFill = 0.f;
	bOutAlert = false;
	if (!OwnsBeast())
	{
		return false;
	}
	if (!BeastTask.IsValid())
	{
		OutLabel = bHuntableNearby ? LOCTEXT("FindingHunt", "Finding a hunt\u2026") : LOCTEXT("NothingHere", "Nothing to hunt here");
		return true;
	}
	OutFill = FWLBeastTaskRules::Progress(BeastTask);
	bOutAlert = !IsBeastOut();
	OutLabel = FText::FromString(BeastTask.Creature);
	OutValue = FText::Format(LOCTEXT("HudValue", "{0}/{1}"), BeastTask.Done, BeastTask.Required);
	return true;
}

// ------------------------------------------------------------------ starter guide (StarterGuide.cs)

AWLNPC* UWLQuestComponent::FindRoxy() const
{
	if (AWLNPC* Cached = CachedRoxy.Get())
	{
		return Cached;
	}
	const AWLPlayerCharacter* Char = GetPawnChar();
	const FVector From = Char ? Char->GetActorLocation() : FVector::ZeroVector;
	AWLNPC* Best = nullptr;
	double BestSq = TNumericLimits<double>::Max();
	for (TActorIterator<AWLNPC> It(GetWorld()); It; ++It)
	{
		if (*It && It->IsRoxy())
		{
			const double D = FVector::DistSquared(It->GetActorLocation(), From);
			if (D < BestSq)
			{
				BestSq = D;
				Best = *It;
			}
		}
	}
	const_cast<UWLQuestComponent*>(this)->CachedRoxy = Best;
	return Best;
}

bool UWLQuestComponent::TryActiveLesson(EWLSkill& OutSkill, int32& OutItem) const
{
	if (GuideState.Stage != EWLStarterGuideStage::Lesson)
	{
		return false;
	}
	OutSkill = GuideState.LessonSkill;
	OutItem = GuideState.LessonItem;
	return true;
}

bool UWLQuestComponent::IsReadyForFission() const
{
	return FWLStarterGuideRules::ReadyForFission([this](EWLSkill S) { return GetLevel(S); });
}

FText UWLQuestComponent::GetCombatProgress() const
{
	return FWLStarterGuideRules::CombatProgress([this](EWLSkill S) { return GetLevel(S); });
}

void UWLQuestComponent::UpdateStarterGuide(double Now)
{
	AWLNPC* Roxy = nullptr;
	if (CachedRoxy.IsValid() || Now >= NextRoxySearchAt)
	{
		NextRoxySearchAt = Now + 2.0;
		Roxy = FindRoxy();
	}
	if (!Roxy)
	{
		return;   // no Roxy in this world: the guide does nothing
	}
	const UWLPlayerFlagsComponent* Flags = GetFlags();
	if (!Flags)
	{
		return;
	}

	if (!Flags->HasFlag(WLQuestFlags::RoxyMet))
	{
		if (!bWelcomed && Now - GuideStartedAt > 2.0)
		{
			bWelcomed = true;
			ClientWelcome(Roxy);
		}
		if (Objective.Target.Get() != Roxy)
		{
			ShowObjective(LOCTEXT("TalkToRoxy", "Talk to Roxy"), Roxy);
		}
		return;
	}

	// Back in a saved game: pick up the lesson (or "return to Roxy") where it was left.
	if (!bLessonRestored && Now - GuideStartedAt > 2.0)
	{
		bLessonRestored = true;
		if (!Flags->HasFlag(WLQuestFlags::RoxyCombatTraining))
		{
			if (GuideState.Stage == EWLStarterGuideStage::Lesson)
			{
				StartLesson(GuideState.LessonSkill, GuideState.LessonItem, /*bPeek*/ false);
			}
			else if (GuideState.Stage == EWLStarterGuideStage::ReturnToRoxy)
			{
				ShowObjective(LOCTEXT("ReturnToRoxy", "Return to Roxy for your next lesson"), Roxy);
			}
		}
	}

	if (Flags->HasFlag(WLQuestFlags::RoxyCombatTraining))
	{
		const bool bReady = IsReadyForFission();
		const FText Text = bReady
			? LOCTEXT("CombatReady", "Return to Roxy for your Geiger Counter and Fission lesson")
			: FText::Format(LOCTEXT("CombatTrain", "Train all four combat skills: {0}"), GetCombatProgress());
		if (!Objective.bActive || !Objective.Text.ToString().Equals(Text.ToString()))
		{
			ShowObjective(Text, bReady ? Roxy : nullptr);
		}
		return;
	}

	if (GuideState.Stage == EWLStarterGuideStage::Lesson && GuideState.LessonSkill == EWLSkill::Beastmastery && Now >= NextBeastGuideAt)
	{
		NextBeastGuideAt = Now + 1.0;
		UpdateBeastObjective();
	}
	if (GuideState.Stage == EWLStarterGuideStage::ReturnToRoxy && Objective.Target.Get() != Roxy)
	{
		ShowObjective(LOCTEXT("ReturnToRoxy", "Return to Roxy for your next lesson"), Roxy);
	}
}

void UWLQuestComponent::UpdateBeastObjective()
{
	const UWLPlayerFlagsComponent* Flags = GetFlags();
	AWLNPC* Roxy = FindRoxy();
	if (!Flags->HasFlag(WLQuestFlags::EggChosen))
	{
		ShowObjective(LOCTEXT("ChooseBeast", "Talk to Roxy to choose your beast companion"), Roxy);
		return;
	}
	if (!IsBeastOut())
	{
		ShowObjective(LOCTEXT("SummonFirst", "Summon your beast, then hunt a creature from your beast task"), nullptr);
		return;
	}
	if (!BeastTask.IsValid())
	{
		ShowObjective(LOCTEXT("FindingHuntObj", "Your beast is finding a hunt"), nullptr);
		return;
	}
	const AWLPlayerCharacter* Char = GetPawnChar();
	AActor* Nearest = nullptr;
	double BestSq = TNumericLimits<double>::Max();
	for (TActorIterator<AWLEnemyCharacter> It(GetWorld()); It && Char; ++It)
	{
		AWLEnemyCharacter* E = *It;
		if (!E || !E->IsHuntable() || E->IsDead() || !MatchesCurrentTask(E))
		{
			continue;
		}
		const double D = FVector::DistSquared(E->GetActorLocation(), Char->GetActorLocation());
		if (D < BestSq)
		{
			BestSq = D;
			Nearest = E;
		}
	}
	ShowObjective(FText::Format(LOCTEXT("HuntObj", "Hunt {0} with your beast to earn Beastmastery XP"), FText::FromString(BeastTask.Creature)), Nearest);
}

void UWLQuestComponent::LessonGiven(EWLSkill Skill, int32 GiveItemId)
{
	if (GetOwner()->HasAuthority())
	{
		StartLesson(Skill, GiveItemId, /*bPeek*/ true);
	}
}

void UWLQuestComponent::StartLesson(EWLSkill Skill, int32 GiveItemId, bool bPeek)
{
	bLessonRestored = true;
	FWLStarterGuideState NewState;
	NewState.Stage = EWLStarterGuideStage::Lesson;
	NewState.LessonSkill = Skill;
	NewState.LessonItem = GiveItemId;
	GuideState = NewState;

	const AWLPlayerCharacter* Char = GetPawnChar();
	const FVector From = Char ? Char->GetActorLocation() : FVector::ZeroVector;
	AActor* Target = FWLQuestTargets::TargetForLesson(GetWorld(), Skill, GiveItemId, From, GetLevel(Skill));
	ShowObjective(FWLStarterGuideRules::LessonText(Skill, GiveItemId), Target);
	if (bPeek && Target)
	{
		PeekAt(Target->GetActorLocation(), FWLStarterGuideRules::PeekCaption(Skill, GiveItemId));
	}
	ReplicatedStateChanged();
}

void UWLQuestComponent::FinishLesson()
{
	GuideState.Stage = EWLStarterGuideStage::ReturnToRoxy;
	ShowBanner(LOCTEXT("LessonComplete", "Lesson complete"), LOCTEXT("LessonCompleteSub", "Head back to Roxy for the next one."), 3.f);
	if (AWLNPC* Roxy = FindRoxy())
	{
		ShowObjective(LOCTEXT("ReturnToRoxy", "Return to Roxy for your next lesson"), Roxy);
	}
	ReplicatedStateChanged();
}

void UWLQuestComponent::SetGuideStage(EWLStarterGuideStage Stage)
{
	GuideState.Stage = Stage;
	ReplicatedStateChanged();
}

void UWLQuestComponent::AwaitCombatTraining()
{
	if (!GetOwner()->HasAuthority())
	{
		return;
	}
	if (UWLPlayerFlagsComponent* Flags = GetFlags())
	{
		Flags->SetFlag(WLQuestFlags::RoxyCombatTraining);
	}
	// Unity Remember(): the combat flag replaces any lesson / return marker.
	SetGuideStage(EWLStarterGuideStage::None);
}

void UWLQuestComponent::HandleXPGained(AWLPlayerState* PS, EWLSkill Skill, int32 /*Amount*/)
{
	if (PS != GetPS())
	{
		return;
	}
	switch (FWLStarterGuideRules::EvaluateXP(GuideState, Skill, HoldsRawCore()))
	{
	case EWLLessonXPResult::Finish:              FinishLesson(); break;
	case EWLLessonXPResult::AdvanceToRefinement: StartLesson(EWLSkill::Refinement, GuideState.LessonItem, false); break;
	case EWLLessonXPResult::AdvanceToFission:    StartLesson(EWLSkill::Fission, GuideState.LessonItem, false); break;
	default: break;
	}
}

bool UWLQuestComponent::TryGiveFissionKit()
{
	AWLPlayerState* PS = GetPS();
	UWLPlayerFlagsComponent* Flags = GetFlags();
	if (!PS || !Flags || !GetOwner()->HasAuthority() || !IsReadyForFission())
	{
		return false;
	}
	UWLInventoryComponent* Inv = PS->GetInventory();
	const UWLBankComponent* Bank = PS->GetBank();
	const UWLEquipmentComponent* Equip = PS->GetEquipment();
	if (!Inv)
	{
		return false;
	}
	for (const int32 Item : { WLQuestItems::GeigerCounter, WLQuestItems::PowerGauntlets })
	{
		const FString KitFlag = WLQuestFlags::RoxyKit(Item);
		if (Flags->HasFlag(KitFlag))
		{
			continue;
		}
		const EWLEquipSlot Slot = Item == WLQuestItems::GeigerCounter ? EWLEquipSlot::Tool : EWLEquipSlot::Weapon;
		const bool bOwns = Inv->Contains(Item) || (Bank && Bank->Contains(Item)) || (Equip && Equip->GetItemId(Slot) == Item);
		if (!bOwns && !Inv->AddItem(Item, 1))
		{
			return false;
		}
		// Record each award immediately: a full bag between the two must not duplicate the first.
		Flags->SetFlag(KitFlag);
	}
	Flags->SetFlag(WLQuestFlags::RoxyFissionGiven);
	Flags->RemoveFlag(WLQuestFlags::RoxyCombatTraining);
	return true;
}

// ------------------------------------------------------------------ objective / banner / peek

void UWLQuestComponent::ShowObjective(const FText& Text, AActor* Target)
{
	if (!GetOwner()->HasAuthority())
	{
		return;
	}
	if (Objective.bActive && Objective.Target.Get() == Target && Objective.Text.ToString().Equals(Text.ToString()))
	{
		return;   // unchanged: don't churn replication
	}
	Objective.bActive = true;
	Objective.Text = Text;
	Objective.Target = Target;
	Objective.bHasLocation = Target != nullptr;
	Objective.Location = Target ? Target->GetActorLocation() : FVector::ZeroVector;
	ReplicatedStateChanged();
}

void UWLQuestComponent::ClearObjective()
{
	if (GetOwner()->HasAuthority() && Objective.bActive)
	{
		Objective = FWLQuestObjective();
		ReplicatedStateChanged();
	}
}

void UWLQuestComponent::ShowBanner(const FText& Title, const FText& Subtitle, float Seconds)
{
	if (GetOwner()->HasAuthority())
	{
		ClientBanner(Title, Subtitle, Seconds);
	}
}

void UWLQuestComponent::ClientBanner_Implementation(const FText& Title, const FText& Subtitle, float Seconds)
{
	OnBanner.Broadcast(Title, Subtitle, Seconds);
}

void UWLQuestComponent::PeekAt(const FVector& Point, const FText& Caption)
{
	if (GetOwner()->HasAuthority())
	{
		ClientPeek(Point, Caption);
	}
}

void UWLQuestComponent::ClientPeek_Implementation(FVector_NetQuantize Point, const FText& Caption)
{
	if (UWLQuestCam* Cam = UWLQuestCam::Get(this))
	{
		Cam->Peek(GetPC(), Point, Caption);
	}
}

void UWLQuestComponent::ClientWelcome_Implementation(AActor* Roxy)
{
#if PLATFORM_ANDROID || PLATFORM_IOS
	const bool bTouch = true;
#else
	const bool bTouch = false;
#endif
	OnBanner.Broadcast(LOCTEXT("WelcomeTitle", "Welcome to the Broken Crescent"),
		bTouch ? LOCTEXT("WelcomeSubTouch", "Roxy has work for you \u2014 she's just up the road. Tap her to talk.")
		       : LOCTEXT("WelcomeSub", "Roxy has work for you \u2014 she's just up the road. Click her to talk."), 4.5f);

	TWeakObjectPtr<AActor> WeakRoxy = Roxy;
	FTimerHandle Handle;
	GetWorld()->GetTimerManager().SetTimer(Handle, FTimerDelegate::CreateWeakLambda(this, [this, WeakRoxy, bTouch]()
	{
		AActor* R = WeakRoxy.Get();
		UWLQuestCam* Cam = UWLQuestCam::Get(this);
		if (R && Cam)
		{
			Cam->Peek(GetPC(), R->GetActorLocation() + FVector(0.f, 0.f, 120.f), bTouch
				? LOCTEXT("PeekRoxyTouch", "That's Roxy. Tap her to talk \u2014 and hold your finger on anything to see everything you can do with it.")
				: LOCTEXT("PeekRoxy", "That's Roxy. Click her to talk \u2014 and right-click anything to see everything you can do with it."));
		}
	}), 3.f, false);
}

// ------------------------------------------------------------------ save

void UWLQuestComponent::CaptureSave(FWLCharacterSave& Out) const
{
	Out.JournalPagesRead = JournalPagesRead;
	Out.Quests = Quests;

	FWLSaveQuestState& Guide = Out.Quests.AddDefaulted_GetRef();
	Guide.QuestId = FWLStarterGuideRules::StarterGuideQuestId;
	Guide.Stage = static_cast<int32>(GuideState.Stage);
	Guide.Counters.Add(WLQuestPrivate::CounterSkill, static_cast<int32>(GuideState.LessonSkill));
	Guide.Counters.Add(WLQuestPrivate::CounterItem, GuideState.LessonItem);

	if (BeastTask.IsValid())
	{
		FWLSaveQuestState& Task = Out.Quests.AddDefaulted_GetRef();
		Task.QuestId = FName(*(FString(WLQuestPrivate::BeastTaskQuestPrefix) + BeastTask.Creature));
		Task.Stage = BeastTask.Tier;
		Task.Counters.Add(WLQuestPrivate::CounterDone, BeastTask.Done);
		Task.Counters.Add(WLQuestPrivate::CounterRequired, BeastTask.Required);
		Task.Counters.Add(WLQuestPrivate::CounterXP, BeastTask.XPPerKill);
	}
}

void UWLQuestComponent::ApplySave(const FWLCharacterSave& In)
{
	JournalPagesRead = In.JournalPagesRead;
	Quests.Reset();
	GuideState = FWLStarterGuideState();
	BeastTask = FWLBeastTask();

	for (const FWLSaveQuestState& Q : In.Quests)
	{
		const FString Id = Q.QuestId.ToString();
		if (Q.QuestId == FWLStarterGuideRules::StarterGuideQuestId)
		{
			const int32 Skill = Q.Counters.FindRef(WLQuestPrivate::CounterSkill);
			GuideState.Stage = static_cast<EWLStarterGuideStage>(FMath::Clamp(Q.Stage, 0, static_cast<int32>(EWLStarterGuideStage::ReturnToRoxy)));
			GuideState.LessonSkill = static_cast<EWLSkill>(FMath::Clamp(Skill, 0, static_cast<int32>(EWLSkill::Count) - 1));
			GuideState.LessonItem = Q.Counters.FindRef(WLQuestPrivate::CounterItem);
		}
		else if (Id.StartsWith(WLQuestPrivate::BeastTaskQuestPrefix))
		{
			FWLBeastTask T;
			T.Creature = Id.Mid(FCString::Strlen(WLQuestPrivate::BeastTaskQuestPrefix));
			T.Tier = FMath::Clamp(Q.Stage, 1, 5);
			T.Done = FMath::Max(0, Q.Counters.FindRef(WLQuestPrivate::CounterDone));
			T.Required = Q.Counters.FindRef(WLQuestPrivate::CounterRequired);
			T.XPPerKill = FMath::Max(1, Q.Counters.FindRef(WLQuestPrivate::CounterXP));
			if (T.IsValid())
			{
				BeastTask = T;
			}
		}
		else
		{
			Quests.Add(Q);
		}
	}

	MigrateLegacyFlags();

	bWelcomed = false;
	bLessonRestored = false;
	bArrivalChecked = false;
	bRemindAfterScan = true;
	NextScanAt = 0.0;
	NextAssignAt = 0.0;
	ReplicatedStateChanged();
}

void UWLQuestComponent::MigrateLegacyFlags()
{
	// Runs after Flags->ApplySave (AWLPlayerState applies flags first).
	UWLPlayerFlagsComponent* Flags = GetFlags();
	if (!Flags)
	{
		return;
	}
	const TArray<FString> Snapshot = Flags->GetFlags();
	int32 NextLesson = GetQuestStage(FWLStarterGuideRules::RoxyQuestId);
	bool bHadNext = false, bReturn = false, bLesson = false;
	EWLSkill LessonSkill = EWLSkill::Attack;
	int32 LessonItem = 0;

	for (const FString& F : Snapshot)
	{
		int32 Index = 0;
		FWLBeastTask Task;
		if (FWLStarterGuideRules::ParseLegacyNextFlag(F, Index))
		{
			NextLesson = FMath::Max(NextLesson, Index);
			bHadNext = true;
		}
		else if (F == WLQuestFlags::LegacyGuideReturn)
		{
			bReturn = true;
		}
		else if (!bLesson && FWLStarterGuideRules::ParseLegacyLessonFlag(F, LessonSkill, LessonItem))
		{
			bLesson = true;
		}
		else if (!BeastTask.IsValid() && FWLBeastTaskRules::ParseLegacyFlag(F, Task))
		{
			BeastTask = Task;
		}
	}

	if (bHadNext)
	{
		SetQuestStage(FWLStarterGuideRules::RoxyQuestId, NextLesson);
	}
	if (GuideState.Stage == EWLStarterGuideStage::None)
	{
		// StarterGuide.TryActiveLesson: "guide_return" wins over a stale lesson flag.
		if (bReturn)
		{
			GuideState.Stage = EWLStarterGuideStage::ReturnToRoxy;
		}
		else if (bLesson)
		{
			GuideState.Stage = EWLStarterGuideStage::Lesson;
			GuideState.LessonSkill = LessonSkill;
			GuideState.LessonItem = LessonItem;
		}
	}
	Flags->RemoveFlagsWithPrefix(WLQuestFlags::LegacyRoxyNextPrefix);
	Flags->RemoveFlagsWithPrefix(WLQuestFlags::LegacyGuideLessonPrefix);
	Flags->RemoveFlagsWithPrefix(WLQuestFlags::LegacyBeastTaskPrefix);
	Flags->RemoveFlag(WLQuestFlags::LegacyGuideReturn);
}

#undef LOCTEXT_NAMESPACE
