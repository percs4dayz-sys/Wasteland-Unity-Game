#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Core/WLGameTickSubsystem.h"
#include "Core/WLTypes.h"
#include "WLSkillingComponent.generated.h"

class AWLResourceNode;
class AWLCraftingStation;
class AWLPlayerCharacter;
class AWLPlayerState;
class UWLInventoryComponent;
struct FWLRecipeRow;

/** What the body is doing on the game tick. Replicated to the owner for HUD / animation. */
UENUM(BlueprintType)
enum class EWLSkillingAction : uint8
{
	None,
	Gathering,
	Crafting
};

DECLARE_MULTICAST_DELEGATE_TwoParams(FWLOnStationChanged, bool /*bOpen*/, EWLCraftingStation);
DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnSkillingActionChanged, EWLSkillingAction);
DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnCraftedOne, FName /*RecipeRowName*/);

/**
 * Per-body skilling actions on the 0.6 s game tick: gathering from resource nodes and batch
 * crafting at stations. Ports Managers/SkillingManager.cs and Managers/CraftingManager.cs (Unity
 * singletons) into one server-authoritative pawn component.
 *
 * Gathering: tool-speed progress accumulates per tick (fractional progress is kept, so upgraded
 * tools are genuinely faster), every node cycle resolves AWLResourceNode::TryGatherCycle, and the
 * action stops on depletion, full pack, missing tool/level.
 *
 * Crafting: the player opens a station (server remembers it, client gets ClientOpenCrafting), then
 * ServerCraft(Recipe, Count) validates station range, recipe/station match, RequiredFlag, level and
 * materials, and crafts one output every 4 ticks. Inputs are consumed before the output is added
 * (frees slots, so a full pack doesn't falsely refuse), refunded if the output still doesn't fit.
 * Cooking burns with Unity's chance: 45% at the recipe level falling linearly to 0 at NoBurnLevel.
 *
 * Both stop when the body moves away from where the action began, dies, or interacts with
 * something else.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLSkillingComponent : public UActorComponent, public IWLTickable
{
	GENERATED_BODY()

public:
	UWLSkillingComponent();

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;
	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

	// IWLTickable (server)
	virtual void OnGameTick(int64 TickCount) override;

	// --- gathering ---

	/** SERVER. Begin working Node (called from AWLResourceNode::Interact). */
	bool StartGathering(AWLResourceNode* Node);

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skilling")
	AWLResourceNode* GetGatherNode() const { return GatherNode.Get(); }

	// --- crafting ---

	/** SERVER. Remember Station and open the crafting UI on the owning client. */
	bool OpenStation(AWLCraftingStation* Station);

	/** OWNING CLIENT -> server. Craft Count of a recipe (<= 0 means 1; clamped to what the materials allow). */
	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Skilling")
	void ServerCraft(FName RecipeRowName, int32 Count);

	/** OWNING CLIENT. The crafting window was dismissed: forget the station locally and on the server. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Skilling")
	void CloseStation();

	UFUNCTION(Server, Reliable)
	void ServerCloseStation();

	/** Server -> owner: show the crafting UI for Station. */
	UFUNCTION(Client, Reliable)
	void ClientOpenCrafting(AWLCraftingStation* Station);

	/** Server -> owner: the station closed (walked away, interacted elsewhere...). */
	UFUNCTION(Client, Reliable)
	void ClientCloseCrafting();

	/** Server -> owner: one output was produced (UI refreshes counts, Unity RefreshAfterCraft). */
	UFUNCTION(Client, Unreliable)
	void ClientCraftedOne(FName RecipeRowName);

	/** Owning client (or server): the station currently open for this player. */
	bool GetOpenStation(EWLCraftingStation& Out) const;
	AWLCraftingStation* GetOpenStationActor() const;

	// --- general ---

	/** Stop gathering / crafting. On a client this asks the server. Does not close the station UI. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Skilling")
	void StopAction();

	UFUNCTION(Server, Reliable)
	void ServerStopAction();

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skilling")
	bool IsBusy() const { return CurrentAction != EWLSkillingAction::None; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Skilling")
	EWLSkillingAction GetCurrentAction() const { return CurrentAction; }

	/** SERVER (interactor). Doing something to Target interrupts other actions and stations. */
	void NotifyInteractingWith(AActor* Target);

	FWLOnStationChanged OnStationChanged;
	FWLOnSkillingActionChanged OnActionChanged;
	FWLOnCraftedOne OnCraftedOne;

	// --- pure-ish helpers (UI uses these for the recipe list) ---

	/** How many of Recipe the materials allow (Unity CraftingManager.MaxCraftable). */
	static int32 MaxCraftable(const FWLRecipeRow& Recipe, const UWLInventoryComponent* Inventory);

	/** Non-consuming check: flag, level, materials. OutReason names exactly what's short. */
	static bool CanCraft(const FWLRecipeRow& Recipe, const AWLPlayerState* PS, FText& OutReason);

	/** Moving further than this from where an action began cancels it (Unity: any walking). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Skilling")
	float CancelMoveDistance = 60.f;

	/** Upper bound for a batch (anti-abuse); "Make All" is clamped to the materials anyway. */
	static constexpr int32 MaxBatch = 1000;

protected:
	UFUNCTION()
	void OnRep_CurrentAction();

	AWLPlayerCharacter* GetCharacter() const;
	AWLPlayerState* GetPlayerState() const;
	void Message(const FText& Text) const;
	void SetAction(EWLSkillingAction NewAction);

	void TickGathering();
	void TickCrafting();

	void StopGathering();
	void StopCrafting();
	void FinishCrafting();

	/** SERVER. Forget the station; tell the client if asked. */
	void CloseStationServer(bool bNotifyClient);

	/** SERVER. One craft: consume, roll burn, output, XP. */
	bool CraftOne(const FWLRecipeRow& Recipe, FText& OutMessage);

	bool IsStationInRange(const AWLCraftingStation* Station) const;
	bool IsDead() const;

	UPROPERTY(ReplicatedUsing = OnRep_CurrentAction)
	EWLSkillingAction CurrentAction = EWLSkillingAction::None;

	// Server state.
	TWeakObjectPtr<AWLResourceNode> GatherNode;
	float TicksSinceGather = 0.f; // retains fractional progress from upgraded tools
	FVector ActionStartLocation = FVector::ZeroVector;

	FName CraftRecipe;
	int32 CraftRemaining = 0;
	int32 CraftTicks = 0;
	int32 CraftMade = 0;

	TWeakObjectPtr<AWLCraftingStation> ServerStation;

	// Owning-client state.
	TWeakObjectPtr<AWLCraftingStation> ClientStation;
};
