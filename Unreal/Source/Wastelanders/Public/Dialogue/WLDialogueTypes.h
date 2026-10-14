#pragma once

#include "CoreMinimal.h"
#include "Core/WLTypes.h"
#include "WLDialogueTypes.generated.h"

class AActor;

/**
 * Data model for NPC conversations. Unity hard-coded every NPC's branching in its MonoBehaviour
 * (RoxyNPC.Interact, TutorialNPC.Interact...); here a conversation is data:
 *
 *   UWLDialogueAsset
 *     PreActions     run on every "Talk" before anything else (Old Mara tops up your kit first)
 *     Entries        ordered rules; the first whose Conditions all pass runs its Actions and opens Node
 *     Nodes          {Speaker, Lines, OnEnter actions, Choices, Next}
 *
 * Every action can carry its own Conditions, store its success in a per-conversation "scratch"
 * variable (ResultVar) that later conditions read, and either abort the whole conversation or jump
 * to FailNode when it fails (e.g. Roxy keeps the lesson for later when your bag is full).
 *
 * Text may contain named tokens, resolved on the server before sending:
 *   {player} {npc} {task_done} {task_required} {task_creature} {combat_progress}
 */

UENUM(BlueprintType)
enum class EWLDialogueConditionType : uint8
{
	FlagSet,            // Name = flag
	FlagNotSet,         // Name = flag
	SkillAtLeast,       // Skill, Value
	SkillBelow,         // Skill, Value
	HasItem,            // ItemId, Value = quantity (inventory)
	OwnsItem,           // ItemId: inventory, bank or equipped
	QuestStageEquals,   // QuestId, Value
	QuestStageAtLeast,  // QuestId, Value
	QuestStageBelow,    // QuestId, Value
	LessonActive,       // Roxy lesson under way: Skill, ItemId (-1 = any item). Value 1 = any skill
	BeastTaskActive,
	BeastTaskInactive,
	HasCompanion,
	CompanionSummoned,
	ScratchSet,         // Name = scratch variable set true earlier in this conversation
	ScratchNotSet,
	TownBanksExist      // Unity BankGuideUI.HasTownBanks: any actor tagged "TownBank" in the world
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLDialogueCondition
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	EWLDialogueConditionType Type = EWLDialogueConditionType::FlagSet;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FString Name;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FName QuestId;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	EWLSkill Skill = EWLSkill::Attack;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	int32 Value = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	int32 ItemId = 0;
};

UENUM(BlueprintType)
enum class EWLDialogueActionType : uint8
{
	SetFlag,             // Name
	RemoveFlag,          // Name
	GiveItem,            // ItemId, Quantity; Name = once-flag (set on success, skip if set); bSkipIfOwned; Text* messages
	GiveItemSet,         // ItemIds: hand over every one the player has neither carried nor equipped (Old Mara's kit)
	GiveXP,              // Skill, Value
	TeachSkill,          // Skill, Value = level: raise to at least that level
	SetQuestStage,       // QuestId, Value
	StartLesson,         // Skill, ItemId: the starter guide points at where this lesson is done
	StartCombatTraining, // Roxy: "train all four combat skills to 10"
	GiveFissionKit,      // Roxy: Geiger counter + gauntlets, each at most once (fails on a full bag)
	StartBeastTask,      // assign a beast task now if the player owns a beast and has none
	SummonCompanion,
	OpenUI,              // Name: "EggChoice", "BankGuide", "Bank", "Shop" (client UI request)
	Message,             // Text: a chat/game-log line
	SetScratch           // Name, Value != 0
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLDialogueAction
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	EWLDialogueActionType Type = EWLDialogueActionType::SetFlag;

	/** All must pass or the action is skipped (a skipped action is neither a success nor a failure). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<FWLDialogueCondition> Conditions;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FString Name;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FName QuestId;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	EWLSkill Skill = EWLSkill::Attack;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	int32 Value = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	int32 ItemId = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	int32 Quantity = 1;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<int32> ItemIds;

	/** GiveItem: count it as done (no new item) when the player already carries / wears / banks one. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	bool bSkipIfOwned = false;

	/** Message text, or GiveItem / GiveItemSet "handed over" line. Args: {item} {qty}. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FText Text;

	/** GiveItem: already carried or equipped. GiveItemSet: unused. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FText TextAlreadyOwned;

	/** GiveItem: already in the bank. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FText TextInBank;

	/** GiveItem / GiveItemSet / GiveFissionKit: bag full. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FText TextNoRoom;

	/** If set, the scratch variable receives whether the action succeeded. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FName ResultVar;

	/** On failure: stop and close the conversation without showing anything. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	bool bAbortOnFailure = false;

	/** On failure: stop the remaining actions and open this node instead. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FName FailNode;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLDialogueChoice
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FText Label;

	/** Hidden unless all pass. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<FWLDialogueCondition> Conditions;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<FWLDialogueAction> Actions;

	/** None = end the conversation. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FName Next;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLDialogueNode
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FName Id;

	/** Empty = the NPC's name. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FText Speaker;

	/** Each line is one box (long lines page at 230 characters on the client, as in Unity). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue", meta = (MultiLine = true))
	TArray<FText> Lines;

	/** Say ONE line per visit, cycling (Unity WandererNPC / TutorialNPC postDialogue). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	bool bCycleOneLine = false;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<FWLDialogueAction> OnEnter;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<FWLDialogueChoice> Choices;

	/** After the last line when there are no choices. None = end. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FName Next;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLDialogueEntry
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<FWLDialogueCondition> Conditions;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	TArray<FWLDialogueAction> Actions;

	/** None = run the actions and say nothing. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Dialogue")
	FName Node;
};

/** What the server sends the owning client for one node. */
USTRUCT()
struct WASTELANDERS_API FWLDialogueNetNode
{
	GENERATED_BODY()

	UPROPERTY()
	FText Speaker;

	/** Token-resolved lines (not yet paginated). */
	UPROPERTY()
	TArray<FText> Lines;

	/** Labels of the choices whose conditions passed, in order. */
	UPROPERTY()
	TArray<FText> Choices;

	/** Echoed back in replies so a stale click can't act on a newer node. */
	UPROPERTY()
	int32 Serial = 0;

	UPROPERTY()
	TObjectPtr<AActor> Npc = nullptr;
};

/** What the dialogue box shows right now (one page). Fired through UWLDialogueComponent::OnDialogueUpdated. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLDialogueView
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "Dialogue")
	FText Speaker;

	/** The current page. */
	UPROPERTY(BlueprintReadOnly, Category = "Dialogue")
	FText Line;

	/** Only on the last page of a node that offers choices. */
	UPROPERTY(BlueprintReadOnly, Category = "Dialogue")
	TArray<FText> Choices;

	UPROPERTY(BlueprintReadOnly, Category = "Dialogue")
	bool bActive = false;

	/** "Click here to continue" leads to another page of this node. */
	UPROPERTY(BlueprintReadOnly, Category = "Dialogue")
	bool bHasMorePages = false;

	UPROPERTY(BlueprintReadOnly, Category = "Dialogue")
	int32 Serial = 0;

	UPROPERTY(BlueprintReadOnly, Category = "Dialogue")
	TWeakObjectPtr<AActor> Npc;
};

/** Pure text helpers (unit tested). */
struct WASTELANDERS_API FWLDialogueText
{
	/** About four lines in the box: longer speeches page on "continue", like OSRS. */
	static constexpr int32 MaxPageChars = 230;

	/**
	 * DialogueUI.Paginate: break long lines into box-sized pages at sentence ends (". ", "! ", "? ").
	 * A single over-long sentence keeps its own page; short lines pass through untouched.
	 */
	static TArray<FString> Paginate(const TArray<FString>& Lines);
};
