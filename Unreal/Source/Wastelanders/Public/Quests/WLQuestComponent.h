#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Persistence/WLCharacterSave.h"
#include "Quests/WLQuestTypes.h"
#include "Quests/WLBeastTaskRules.h"
#include "WLQuestComponent.generated.h"

class AWLPlayerState;
class AWLPlayerController;
class AWLPlayerCharacter;
class AWLNPC;
class UWLPlayerFlagsComponent;

DECLARE_MULTICAST_DELEGATE(FWLOnQuestStateChanged);
DECLARE_MULTICAST_DELEGATE_ThreeParams(FWLOnQuestBanner, const FText& /*Title*/, const FText& /*Subtitle*/, float /*Seconds*/);
DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnJournalPageOpened, int32 /*ItemId*/);
DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnBeastTaskCompleted, AWLPlayerState*);

/**
 * A character's quest progress: journal pages, beast tasks, Roxy's lesson chain, the new-player
 * starter guide and the "where next" objective. On AWLPlayerState, server-authoritative,
 * replicated to the owner only.
 *
 * Ports: Managers/JournalManager.cs, Items/JournalLore.cs (reading), Skills/BeastTasks.cs,
 * World/StarterGuide.cs, UI/QuestGuide.cs (objective state, banner), World/BlackwaterArrival.cs,
 * and the lesson bookkeeping half of NPC/RoxyNPC.cs.
 *
 * Save layout (FWLCharacterSave):
 *   JournalPagesRead            item ids read
 *   Quests["roxy_lessons"]      Stage = index of Roxy's next lesson
 *   Quests["starter_guide"]     Stage = EWLStarterGuideStage, Counters {Skill, Item}
 *   Quests["bmtask|<creature>"] Stage = tier, Counters {Done, Required, XPPerKill}
 *   any other Quests entries    kept as-is (designer quests via Get/SetQuestStage)
 * Unity kept all of this in flags; ApplySave migrates those legacy flags (roxy_next:N,
 * guide_lesson:S:I, guide_return, bmtask|...) into the quest states and removes them.
 * Grant flags (journal_300, roxy_gift:N, ...) stay flags, exactly as Unity.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLQuestComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLQuestComponent();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	void CaptureSave(FWLCharacterSave& Out) const;
	void ApplySave(const FWLCharacterSave& In);

	// ---------------------------------------------------------------- generic quest stages

	/** SERVER (state is server-only for generic quests). 0 if the quest was never started. */
	int32 GetQuestStage(FName QuestId) const;
	void SetQuestStage(FName QuestId, int32 Stage);
	int32 GetQuestCounter(FName QuestId, FName Counter) const;
	void SetQuestCounter(FName QuestId, FName Counter, int32 Value);

	// ---------------------------------------------------------------- journal

	/**
	 * SERVER. The player reads a journal page from their inventory (the items system calls this from the
	 * "Read" verb). Validates the item is carried and is a journal page, records it, opens it on the client.
	 */
	bool ReadJournalPage(int32 ItemId);

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Journal")
	bool HasReadJournalPage(int32 ItemId) const { return JournalPagesRead.Contains(ItemId); }

	const TArray<int32>& GetJournalPagesRead() const { return JournalPagesRead; }

	/** Client: open the reader on this page (text: FWLJournalLore::Find). */
	FWLOnJournalPageOpened OnJournalPageOpened;

	// ---------------------------------------------------------------- beast tasks

	UFUNCTION(BlueprintPure, Category = "Wastelanders|BeastTask")
	bool HasBeastTask() const { return BeastTask.IsValid(); }

	const FWLBeastTask& GetBeastTask() const { return BeastTask; }

	/** HUD row: label = what to hunt or a status line, value "3/12". False = hide (no beast yet). Alert = beast not out. */
	bool GetBeastTaskHud(FText& OutLabel, FText& OutValue, float& OutFill, bool& bOutAlert) const;

	/** SERVER. Assign a task now if the player owns a beast and has none. */
	bool TryAssignBeastTask();

	/** SERVER. A whole task finished (not each kill). The Roxy hunting lesson ends here. */
	FWLOnBeastTaskCompleted OnBeastTaskCompleted;

	/** Huntable for beast tasks: ordinary creatures, no dummies, bosses, minibosses, the God-Hunter or golems. */
	static bool IsHuntable(const AActor* Enemy);
	static FString CreatureNameOf(const AActor* Enemy);
	static int32 CreatureMaxHPOf(const AActor* Enemy);
	static int32 CreatureTierOf(const AActor* Enemy);

	/** Does this enemy count for the current task (by name, or by tier where the named creature doesn't live)? */
	bool MatchesCurrentTask(const AActor* Enemy) const;

	// ---------------------------------------------------------------- Roxy / starter guide

	const FWLStarterGuideState& GetGuideState() const { return GuideState; }

	/** StarterGuide.TryActiveLesson: a lesson is under way (not "return to Roxy"). */
	bool TryActiveLesson(EWLSkill& OutSkill, int32& OutItem) const;

	/** SERVER. Roxy handed out a lesson: objective + camera peek on where it's done. */
	void LessonGiven(EWLSkill Skill, int32 GiveItemId);

	/** SERVER. Roxy asked for 10 in all four combat skills before Fission. */
	void AwaitCombatTraining();

	/** SERVER. RoxyNPC.TryGiveFissionKit: Geiger counter + gauntlets, each at most once. False on a full bag. */
	bool TryGiveFissionKit();

	/** Is the character's combat at 10 across the board (RoxyNPC.ReadyForFission)? */
	bool IsReadyForFission() const;
	FText GetCombatProgress() const;

	// ---------------------------------------------------------------- objective (QuestGuide)

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Quest")
	const FWLQuestObjective& GetObjective() const { return Objective; }

	/** SERVER. Show one objective (Target may be null: no place, line only). */
	void ShowObjective(const FText& Text, AActor* Target);
	void ClearObjective();

	/** SERVER. Big centred title + subtitle (QuestGuide.Banner). */
	void ShowBanner(const FText& Title, const FText& Subtitle, float Seconds = 4.5f);

	/** SERVER. Camera peek on the owning client (QuestCam.Peek). */
	void PeekAt(const FVector& Point, const FText& Caption);

	/** Fires on the server and owning client when objective, guide state, task or journal change. */
	FWLOnQuestStateChanged OnQuestStateChanged;
	FWLOnQuestBanner OnBanner;

	// ---------------------------------------------------------------- tuning

	/** JournalManager "onMainland": map names starting with any of these count as the mainland. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Journal")
	TArray<FString> MainlandMapPrefixes = { TEXT("MainWorld") };

	/**
	 * Beast tasks only consider creatures within this distance of the player (uu). 0 = the whole
	 * loaded world. Unity scanned the loaded scene (one area); an MMO world is one big map, so "what
	 * lives where you are" needs a radius.
	 */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|BeastTask")
	float BeastTaskScanRadius = 30000.f;

protected:
	UFUNCTION()
	void OnRep_QuestState();

	UFUNCTION(Client, Reliable)
	void ClientOpenJournalPage(int32 ItemId);

	UFUNCTION(Client, Reliable)
	void ClientBanner(const FText& Title, const FText& Subtitle, float Seconds);

	UFUNCTION(Client, Reliable)
	void ClientPeek(FVector_NetQuantize Point, const FText& Caption);

	/** First visit, not yet met Roxy: welcome banner, then the camera glides to her (StarterGuide.Welcome). */
	UFUNCTION(Client, Reliable)
	void ClientWelcome(AActor* Roxy);

private:
	void HandleGameTick(int64 TickCount);
	void HandleEnemyKilled(AActor* Enemy, AWLPlayerState* Killer);
	void HandleXPGained(AWLPlayerState* PS, EWLSkill Skill, int32 Amount);

	void UpdateJournal();
	void UpdateBlackwaterArrival();
	void UpdateBeastTasks(double Now);
	void UpdateStarterGuide(double Now);
	void UpdateBeastObjective();

	void ScanHuntable();
	bool AssignBeastTask();
	void RemindBeastTask();
	void StartLesson(EWLSkill Skill, int32 GiveItemId, bool bPeek);
	void FinishLesson();
	void SetGuideStage(EWLStarterGuideStage Stage);
	void ReplicatedStateChanged();

	AWLNPC* FindRoxy() const;
	bool IsBeastOut() const;
	bool OwnsBeast() const;
	bool HoldsRawCore() const;
	bool IsOnMainland() const;
	int32 GetLevel(EWLSkill Skill) const;
	void Say(const FText& Text) const;
	void SayBeastTask(const FText& Text) const;

	AWLPlayerState* GetPS() const;
	AWLPlayerController* GetPC() const;
	AWLPlayerCharacter* GetPawnChar() const;
	UWLPlayerFlagsComponent* GetFlags() const;

	FWLSaveQuestState* FindQuest(FName QuestId);
	const FWLSaveQuestState* FindQuest(FName QuestId) const;
	void MigrateLegacyFlags();

	// --- replicated to owner ---
	UPROPERTY(ReplicatedUsing = OnRep_QuestState)
	FWLBeastTask BeastTask;

	UPROPERTY(ReplicatedUsing = OnRep_QuestState)
	FWLStarterGuideState GuideState;

	UPROPERTY(ReplicatedUsing = OnRep_QuestState)
	FWLQuestObjective Objective;

	UPROPERTY(ReplicatedUsing = OnRep_QuestState)
	TArray<int32> JournalPagesRead;

	/** False when nothing huntable lives near the player (HUD: "Nothing to hunt here" vs "Finding a hunt..."). */
	UPROPERTY(ReplicatedUsing = OnRep_QuestState)
	bool bHuntableNearby = true;

	// --- server only ---
	/** Generic quest states (and the Roxy lesson index). Beast task / guide are separate fields. */
	TArray<FWLSaveQuestState> Quests;

	TArray<FWLBeastTaskRules::FKind> HuntableKinds;
	FString LastCreature;
	double NextScanAt = 0.0;
	double NextAssignAt = 0.0;
	double NextHintAt = 0.0;
	double NextJournalCheck = 0.0;
	double NextBeastGuideAt = 0.0;
	double NextRoxySearchAt = 0.0;
	double GuideStartedAt = -1.0;
	bool bRemindAfterScan = true;
	bool bWelcomed = false;
	bool bCombatObjectiveShown = false;
	bool bReturnObjectiveShown = false;
	bool bLessonRestored = false;
	bool bArrivalChecked = false;
	TWeakObjectPtr<AWLNPC> CachedRoxy;
	FDelegateHandle TickHandle;
	FDelegateHandle KilledHandle;
	FDelegateHandle XPHandle;
};
