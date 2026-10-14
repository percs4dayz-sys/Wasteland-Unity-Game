#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Dialogue/WLDialogueTypes.h"
#include "WLDialogueComponent.generated.h"

class AWLPlayerState;
class AWLPlayerController;
class UWLDialogueAsset;

DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnDialogueUpdated, const FWLDialogueView& /*View*/);
DECLARE_MULTICAST_DELEGATE(FWLOnDialogueEnded);
DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnDialogueUIRequest, FName /*EggChoice, BankGuide, Bank, Shop*/);

/**
 * One player's conversation with an NPC. Replaces Unity's DialogueUI singleton (logic half),
 * DialogueClickAdvance and the hard-coded branching in each NPC script.
 *
 * Server: owns the conversation. It evaluates the NPC's UWLDialogueAsset (entry rules, conditions,
 * actions), resolves text tokens and sends the current node to the owning client. It validates
 * every reply (serial number, choice index, conditions re-checked), and ends the conversation when
 * the player walks more than 8 m from where it started (Unity WalkAwayDistance).
 *
 * Client: paginates the node (230 characters per box), fires OnDialogueUpdated for each page, and
 * after the last page sends ServerAdvanceDialogue, or ServerChooseDialogueOption when there are choices.
 * Talking to the same NPC again mid-conversation turns the page, as in Unity.
 *
 * Lives on AWLPlayerState. If the framework hasn't added it as a default subobject, UWLQuestComponent
 * creates it at BeginPlay on the server (it replicates as a dynamic component).
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLDialogueComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLDialogueComponent();

	/** From a PlayerState, pawn or controller. */
	static UWLDialogueComponent* Get(const AActor* PlayerActor);

	// --- server API (called by AWLNPC::Interact) ---

	/** SERVER. "Talk": page on if already talking to this NPC, else start the conversation. */
	void Talk(AActor* Npc, const UWLDialogueAsset* Asset, const FText& NpcName);

	/** SERVER. Start a conversation from the asset's entry rules. False if nothing was said. */
	bool StartConversation(AActor* Npc, const UWLDialogueAsset* Asset, const FText& NpcName);

	/** SERVER. Same as StartConversation but always opens a specific node (scripted events). */
	bool StartAtNode(AActor* Npc, const UWLDialogueAsset* Asset, const FText& NpcName, FName NodeId);

	/** SERVER. Say plain lines with no graph (one-off barks). */
	void SayLines(AActor* Npc, const FText& Speaker, const TArray<FText>& Lines);

	/** SERVER. */
	void EndConversation();

	bool IsConversationWith(const AActor* Npc) const { return Npc && ActiveNpc.Get() == Npc; }
	bool IsInConversation() const { return bServerActive; }

	/** SERVER. Ask the owning client to open a UI (EggChoice, BankGuide, Bank, Shop). */
	void RequestUI(FName UIName);

	// --- client API (UI binds these) ---

	FWLOnDialogueUpdated OnDialogueUpdated;
	FWLOnDialogueEnded OnDialogueEnded;
	FWLOnDialogueUIRequest OnUIRequest;

	/** Click / Space / tap: next page, or tell the server the node is finished. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Dialogue")
	void Continue();

	/** Pick one of the choices offered on the last page. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Dialogue")
	void Choose(int32 ChoiceIndex);

	/** Close the box (Esc / walking away is detected server-side too). */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Dialogue")
	void RequestEnd();

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Dialogue")
	const FWLDialogueView& GetView() const { return View; }

	// --- RPCs ---

	UFUNCTION(Server, Reliable)
	void ServerAdvanceDialogue(int32 Serial);

	UFUNCTION(Server, Reliable)
	void ServerChooseDialogueOption(int32 Serial, int32 ChoiceIndex);

	UFUNCTION(Server, Reliable)
	void ServerEndDialogue();

	/** Walk-away distance from where the conversation started (uu). Unity: 8 m. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Dialogue")
	float WalkAwayDistance = 800.f;

protected:
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	UFUNCTION(Client, Reliable)
	void ClientShowNode(const FWLDialogueNetNode& Node);

	UFUNCTION(Client, Reliable)
	void ClientEndDialogue();

	UFUNCTION(Client, Reliable)
	void ClientAdvancePage();

	UFUNCTION(Client, Reliable)
	void ClientUIRequest(FName UIName);

private:
	// --- server ---
	enum class EActionsResult : uint8 { Continue, Abort, Jump };

	bool Begin(AActor* Npc, const UWLDialogueAsset* Asset, const FText& NpcName);
	/** Enter a node (runs OnEnter); ends the conversation if Id is None or missing. Guards against cycles. */
	void EnterNode(FName Id, int32 Depth = 0);
	EActionsResult RunActions(const TArray<FWLDialogueAction>& Actions, FName& OutJump);
	bool RunAction(const FWLDialogueAction& Action);
	bool CheckAll(const TArray<FWLDialogueCondition>& Conditions) const;
	bool CheckCondition(const FWLDialogueCondition& Condition) const;
	FText Resolve(const FText& In) const;
	void SendCurrentNode(const FWLDialogueNode& Node);
	void CheckWalkAway();
	void Message(const FText& Text) const;

	bool GiveItem(const FWLDialogueAction& Action);
	bool GiveItemSet(const FWLDialogueAction& Action);
	bool OwnsItem(int32 ItemId, int32 Qty, bool bIncludeBank) const;
	bool IsEquipped(int32 ItemId) const;

	AWLPlayerState* GetPS() const;
	AWLPlayerController* GetPC() const;
	FVector GetPlayerLocation() const;

	bool bServerActive = false;
	TWeakObjectPtr<AActor> ActiveNpc;

	UPROPERTY(Transient)
	TObjectPtr<UWLDialogueAsset> ActiveAsset;

	FText ActiveNpcName;
	FName CurrentNodeId;
	int32 ServerSerial = 0;
	/** Visible choice index -> index into the node's Choices. */
	TArray<int32> VisibleChoices;
	/** Per-conversation scratch variables (ResultVar / SetScratch). */
	TMap<FName, bool> Scratch;
	/** bCycleOneLine counters, per NPC + node, for this player's session (Unity kept them per NPC, unsaved). */
	TMap<FString, int32> CycleCounters;
	FVector StartLocation = FVector::ZeroVector;
	FTimerHandle WalkAwayTimer;

	// --- client ---
	void ShowPage();
	void ClearClient(bool bBroadcast);

	FWLDialogueNetNode ClientNode;
	TArray<FString> ClientPages;
	int32 ClientPage = 0;
	bool bAwaitingServer = false;
	FWLDialogueView View;
};
