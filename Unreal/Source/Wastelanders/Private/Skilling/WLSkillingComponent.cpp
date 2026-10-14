#include "Skilling/WLSkillingComponent.h"
#include "Skilling/WLGatheringTools.h"
#include "Skilling/WLSkillingMath.h"
#include "World/WLResourceNode.h"
#include "World/WLCraftingStation.h"
#include "Interaction/WLInteractionLibrary.h"
#include "Interaction/WLInteractorComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Items/WLInventoryComponent.h"
#include "Items/WLItemTypes.h"
#include "Skills/WLSkillsComponent.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Combat/WLCombatComponent.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Core/WLGameplayTags.h"
#include "Data/WLDataSubsystem.h"
#include "Net/UnrealNetwork.h"
#include "Engine/World.h"

#define LOCTEXT_NAMESPACE "WLSkilling"

namespace
{
	FText SkillDisplayName(const UObject* Ctx, EWLSkill Skill)
	{
		if (const UWLDataSubsystem* Data = UWLDataSubsystem::Get(Ctx))
		{
			if (const FWLSkillRow* Row = Data->FindSkill(Skill))
			{
				return Row->DisplayName;
			}
		}
		return FText::FromString(WLTypes::SkillToString(Skill));
	}

	FText ItemDisplayName(const UObject* Ctx, int32 ItemId)
	{
		if (const UWLDataSubsystem* Data = UWLDataSubsystem::Get(Ctx))
		{
			if (const FWLItemRow* Row = Data->FindItem(ItemId))
			{
				return Row->DisplayName;
			}
		}
		return FText::Format(LOCTEXT("ItemN", "item {0}"), ItemId);
	}

	bool GiveTo(UWLInventoryComponent* Inv, int32 ItemId, int32 Qty, FName Source)
	{
		FWLItemStack Stack;
		Stack.ItemId = ItemId;
		Stack.Quantity = Qty;
		return Inv->GiveItem(Stack, Source);
	}
}

UWLSkillingComponent::UWLSkillingComponent()
{
	PrimaryComponentTick.bCanEverTick = false; // everything runs on the game tick
	SetIsReplicatedByDefault(true);
}

void UWLSkillingComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME_CONDITION(UWLSkillingComponent, CurrentAction, COND_OwnerOnly);
}

void UWLSkillingComponent::BeginPlay()
{
	Super::BeginPlay();
	if (GetOwner() && GetOwner()->HasAuthority())
	{
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			Tick->Register(TScriptInterface<IWLTickable>(this));
		}
	}
}

void UWLSkillingComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
	{
		Tick->Unregister(TScriptInterface<IWLTickable>(this));
	}
	Super::EndPlay(EndPlayReason);
}

AWLPlayerCharacter* UWLSkillingComponent::GetCharacter() const
{
	return Cast<AWLPlayerCharacter>(GetOwner());
}

AWLPlayerState* UWLSkillingComponent::GetPlayerState() const
{
	const AWLPlayerCharacter* Char = GetCharacter();
	return Char ? Char->GetWLPlayerState() : nullptr;
}

void UWLSkillingComponent::Message(const FText& Text) const
{
	if (Text.IsEmpty())
	{
		return;
	}
	const AWLPlayerCharacter* Char = GetCharacter();
	if (AWLPlayerController* PC = Char ? Cast<AWLPlayerController>(Char->GetController()) : nullptr)
	{
		PC->ClientGameMessage(Text);
	}
}

bool UWLSkillingComponent::IsDead() const
{
	const AWLPlayerState* PS = GetPlayerState();
	const UWLAbilitySystemComponent* ASC = PS ? PS->GetWLAbilitySystem() : nullptr;
	return !ASC || ASC->IsDead();
}

void UWLSkillingComponent::SetAction(EWLSkillingAction NewAction)
{
	if (CurrentAction == NewAction)
	{
		return;
	}
	CurrentAction = NewAction;
	if (AWLPlayerState* PS = GetPlayerState())
	{
		if (UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem())
		{
			// Server-side state tag so abilities (eat, attack) can see the body is busy skilling.
			ASC->SetLooseGameplayTagCount(WLTags::State_Skilling, NewAction != EWLSkillingAction::None ? 1 : 0);
		}
	}
	OnActionChanged.Broadcast(CurrentAction);
}

void UWLSkillingComponent::OnRep_CurrentAction()
{
	OnActionChanged.Broadcast(CurrentAction);
}

// --- interruption ---------------------------------------------------------------------------------

void UWLSkillingComponent::NotifyInteractingWith(AActor* Target)
{
	StopAction();
	if (ServerStation.IsValid() && ServerStation.Get() != Target)
	{
		CloseStationServer(true);
	}
}

void UWLSkillingComponent::StopAction()
{
	if (!GetOwner())
	{
		return;
	}
	if (!GetOwner()->HasAuthority())
	{
		ServerStopAction();
		return;
	}
	StopGathering();
	StopCrafting();
}

void UWLSkillingComponent::ServerStopAction_Implementation()
{
	StopAction();
}

void UWLSkillingComponent::OnGameTick(int64 TickCount)
{
	if (CurrentAction == EWLSkillingAction::None && !ServerStation.IsValid())
	{
		return;
	}
	const AWLPlayerCharacter* Char = GetCharacter();
	if (!Char || IsDead())
	{
		StopAction();
		CloseStationServer(true);
		return;
	}

	// Walking away interrupts work (Unity Interactor3D: any movement). Distance from where the action
	// began, not velocity, so the deceleration at the end of the walk-up doesn't cancel it instantly.
	if (CurrentAction != EWLSkillingAction::None && FVector::Dist2D(Char->GetActorLocation(), ActionStartLocation) > CancelMoveDistance)
	{
		const bool bWasCrafting = CurrentAction == EWLSkillingAction::Crafting;
		StopAction();
		if (bWasCrafting)
		{
			Message(LOCTEXT("StopCrafting", "You stop crafting."));
		}
	}

	if (ServerStation.IsValid() && !IsStationInRange(ServerStation.Get()))
	{
		StopCrafting();
		CloseStationServer(true);
	}

	if (CurrentAction == EWLSkillingAction::Gathering)
	{
		TickGathering();
	}
	else if (CurrentAction == EWLSkillingAction::Crafting)
	{
		TickCrafting();
	}
}

// --- gathering ------------------------------------------------------------------------------------

bool UWLSkillingComponent::StartGathering(AWLResourceNode* Node)
{
	AWLPlayerCharacter* Char = GetCharacter();
	if (!Node || !Char || !Char->HasAuthority())
	{
		return false;
	}
	if (Node->IsDepleted())
	{
		Message(LOCTEXT("NothingLeft", "Nothing left to gather here."));
		return false;
	}

	StopCrafting();
	// Gathering and fighting don't mix: a live combat target keeps turning the body toward the enemy.
	if (UWLCombatComponent* Combat = Char->GetCombat())
	{
		Combat->SetTarget(nullptr);
	}

	GatherNode = Node;
	TicksSinceGather = 0.f;
	ActionStartLocation = Char->GetActorLocation();
	SetAction(EWLSkillingAction::Gathering);
	Message(LOCTEXT("BeginGathering", "You begin gathering..."));
	return true;
}

void UWLSkillingComponent::StopGathering()
{
	GatherNode.Reset();
	TicksSinceGather = 0.f;
	if (CurrentAction == EWLSkillingAction::Gathering)
	{
		SetAction(EWLSkillingAction::None);
	}
}

void UWLSkillingComponent::TickGathering()
{
	AWLResourceNode* Node = GatherNode.Get();
	AWLPlayerState* PS = GetPlayerState();
	const AWLPlayerCharacter* Char = GetCharacter();
	if (!Node || !PS || !Char)
	{
		StopGathering();
		return;
	}
	if (Node->IsDepleted())
	{
		Message(LOCTEXT("Depleted", "The node is depleted."));
		StopGathering();
		return;
	}
	// A moving fishing spot (or anything else) that is no longer in reach ends the action.
	if (FWLInteraction::DistanceToTarget(Node, Char->GetActorLocation()) > FWLInteraction::GetRange(Node) * UWLInteractorComponent::ServerRangeTolerance)
	{
		Message(LOCTEXT("OutOfReach", "It's out of reach now."));
		StopGathering();
		return;
	}

	TicksSinceGather += FWLGatheringTools::CycleSpeed(PS, Node->GetRequiredToolId());
	const int32 Cycle = Node->CurrentTicksPerCycle(PS);
	if (TicksSinceGather < Cycle)
	{
		return;
	}
	TicksSinceGather -= Cycle;

	FText Msg;
	const EWLGatherResult Result = Node->TryGatherCycle(PS, Msg);
	Message(Msg); // Caught or Stop; NoCatch stays silent (no chat spam)
	if (Result == EWLGatherResult::Stop || Node->IsDepleted())
	{
		StopGathering();
	}
}

// --- stations -------------------------------------------------------------------------------------

bool UWLSkillingComponent::IsStationInRange(const AWLCraftingStation* Station) const
{
	const AWLPlayerCharacter* Char = GetCharacter();
	return Station && Char
		&& FWLInteraction::DistanceToTarget(Station, Char->GetActorLocation()) <= FWLInteraction::GetRange(Station) * UWLInteractorComponent::ServerRangeTolerance;
}

bool UWLSkillingComponent::OpenStation(AWLCraftingStation* Station)
{
	if (!Station || !GetOwner() || !GetOwner()->HasAuthority())
	{
		return false;
	}
	StopGathering();
	ServerStation = Station;
	ClientOpenCrafting(Station);
	return true;
}

void UWLSkillingComponent::CloseStationServer(bool bNotifyClient)
{
	if (!ServerStation.IsValid())
	{
		return;
	}
	ServerStation.Reset();
	if (bNotifyClient)
	{
		ClientCloseCrafting();
	}
}

void UWLSkillingComponent::CloseStation()
{
	const bool bHad = ClientStation.IsValid();
	EWLCraftingStation Type = EWLCraftingStation::Workbench;
	if (bHad)
	{
		Type = ClientStation->GetStation();
	}
	ClientStation.Reset();
	if (bHad)
	{
		OnStationChanged.Broadcast(false, Type);
	}
	ServerCloseStation();
}

void UWLSkillingComponent::ServerCloseStation_Implementation()
{
	// Closing the window cancels a batch in progress (Unity CraftingManager.StopCrafting on close).
	StopCrafting();
	CloseStationServer(false);
}

void UWLSkillingComponent::ClientOpenCrafting_Implementation(AWLCraftingStation* Station)
{
	ClientStation = Station;
	if (Station)
	{
		OnStationChanged.Broadcast(true, Station->GetStation());
	}
}

void UWLSkillingComponent::ClientCloseCrafting_Implementation()
{
	if (AWLCraftingStation* Station = ClientStation.Get())
	{
		const EWLCraftingStation Type = Station->GetStation();
		ClientStation.Reset();
		OnStationChanged.Broadcast(false, Type);
	}
	ClientStation.Reset();
}

void UWLSkillingComponent::ClientCraftedOne_Implementation(FName RecipeRowName)
{
	OnCraftedOne.Broadcast(RecipeRowName);
}

bool UWLSkillingComponent::GetOpenStation(EWLCraftingStation& Out) const
{
	if (const AWLCraftingStation* Station = GetOpenStationActor())
	{
		Out = Station->GetStation();
		return true;
	}
	return false;
}

AWLCraftingStation* UWLSkillingComponent::GetOpenStationActor() const
{
	if (AWLCraftingStation* Client = ClientStation.Get())
	{
		return Client;
	}
	return ServerStation.Get();
}

// --- crafting -------------------------------------------------------------------------------------

int32 UWLSkillingComponent::MaxCraftable(const FWLRecipeRow& Recipe, const UWLInventoryComponent* Inventory)
{
	if (!Inventory || Recipe.Inputs.Num() == 0)
	{
		return 0;
	}
	int32 Max = TNumericLimits<int32>::Max();
	for (const FWLItemQuantity& In : Recipe.Inputs)
	{
		if (In.Quantity <= 0)
		{
			continue;
		}
		Max = FMath::Min(Max, Inventory->CountOf(In.ItemId) / In.Quantity);
	}
	return Max == TNumericLimits<int32>::Max() ? 0 : Max;
}

bool UWLSkillingComponent::CanCraft(const FWLRecipeRow& Recipe, const AWLPlayerState* PS, FText& OutReason)
{
	const UWLInventoryComponent* Inv = PS ? PS->GetInventory() : nullptr;
	const UWLSkillsComponent* Skills = PS ? PS->GetSkills() : nullptr;
	if (!Inv || !Skills)
	{
		OutReason = LOCTEXT("NoPlayer", "No player.");
		return false;
	}

	if (!Recipe.RequiredFlag.IsEmpty())
	{
		const UWLPlayerFlagsComponent* Flags = PS->GetFlags();
		if (!Flags || !Flags->HasFlag(Recipe.RequiredFlag))
		{
			OutReason = LOCTEXT("NeedFlag", "Complete Roxy's Fission lesson to unlock this replacement recipe.");
			return false;
		}
	}

	if (Skills->GetLevel(Recipe.Skill) < Recipe.LevelRequired)
	{
		OutReason = FText::Format(LOCTEXT("NeedLevel", "You need {0} level {1} to make that."), SkillDisplayName(PS, Recipe.Skill), Recipe.LevelRequired);
		return false;
	}

	// Name exactly what's short so the player knows what to go gather.
	TArray<FText> Missing;
	for (const FWLItemQuantity& In : Recipe.Inputs)
	{
		const int32 Have = Inv->CountOf(In.ItemId);
		if (Have < In.Quantity)
		{
			Missing.Add(FText::Format(LOCTEXT("MissingOne", "{0} more {1}"), In.Quantity - Have, ItemDisplayName(PS, In.ItemId)));
		}
	}
	if (Missing.Num() > 0)
	{
		OutReason = FText::Format(LOCTEXT("NeedItems", "You need {0} to make that."), FText::Join(LOCTEXT("And", " and "), Missing));
		return false;
	}

	OutReason = FText::GetEmpty();
	return true;
}

void UWLSkillingComponent::ServerCraft_Implementation(FName RecipeRowName, int32 Count)
{
	AWLPlayerState* PS = GetPlayerState();
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	const FWLRecipeRow* Recipe = Data ? Data->FindRecipe(RecipeRowName) : nullptr;
	AWLCraftingStation* Station = ServerStation.Get();
	if (!PS || !Recipe || IsDead())
	{
		Message(LOCTEXT("NothingSelected", "Nothing selected."));
		return;
	}
	// The player must be at an open station of the recipe's kind (no crafting from across the map).
	if (!Station || Station->GetStation() != Recipe->Station || !IsStationInRange(Station))
	{
		Message(LOCTEXT("NotAtStation", "You need to be at the right station to make that."));
		return;
	}

	// Validate up front so impossible jobs give instant feedback; the first output still takes a
	// full interval, so every output costs the same 2.4 s.
	FText Why;
	if (!CanCraft(*Recipe, PS, Why))
	{
		StopCrafting();
		Message(Why);
		return;
	}

	StopGathering();
	const int32 Possible = FMath::Max(1, MaxCraftable(*Recipe, PS->GetInventory()));
	CraftRecipe = RecipeRowName;
	CraftMade = 0;
	CraftRemaining = FMath::Clamp(Count <= 0 ? 1 : Count, 1, FMath::Min(Possible, MaxBatch));
	CraftTicks = 0;
	if (const AWLPlayerCharacter* Char = GetCharacter())
	{
		ActionStartLocation = Char->GetActorLocation();
	}
	SetAction(EWLSkillingAction::Crafting);
	Message(LOCTEXT("BeginCrafting", "You begin crafting..."));
}

void UWLSkillingComponent::StopCrafting()
{
	CraftRecipe = NAME_None;
	CraftRemaining = 0;
	CraftMade = 0;
	CraftTicks = 0;
	if (CurrentAction == EWLSkillingAction::Crafting)
	{
		SetAction(EWLSkillingAction::None);
	}
}

void UWLSkillingComponent::FinishCrafting()
{
	if (CraftMade > 0)
	{
		if (const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this))
		{
			if (const FWLRecipeRow* Recipe = Data->FindRecipe(CraftRecipe))
			{
				const FText OutName = Data->ItemExists(Recipe->OutputItemId) ? ItemDisplayName(this, Recipe->OutputItemId) : Recipe->DisplayName;
				Message(FText::Format(LOCTEXT("Finished", "You finish crafting {0} x {1}."), CraftMade, OutName));
			}
		}
	}
	StopCrafting();
}

void UWLSkillingComponent::TickCrafting()
{
	if (++CraftTicks < FWLSkillingMath::CraftIntervalTicks)
	{
		return;
	}
	CraftTicks = 0;

	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	const FWLRecipeRow* Recipe = Data ? Data->FindRecipe(CraftRecipe) : nullptr;
	if (!Recipe)
	{
		StopCrafting();
		return;
	}

	FText Msg;
	if (!CraftOne(*Recipe, Msg))
	{
		Message(Msg);
		StopCrafting();
		return;
	}
	Message(Msg);
	++CraftMade;
	--CraftRemaining;
	ClientCraftedOne(CraftRecipe);

	if (CraftRemaining <= 0)
	{
		FinishCrafting();
	}
}

bool UWLSkillingComponent::CraftOne(const FWLRecipeRow& Recipe, FText& OutMessage)
{
	AWLPlayerState* PS = GetPlayerState();
	UWLInventoryComponent* Inv = PS ? PS->GetInventory() : nullptr;
	UWLSkillsComponent* Skills = PS ? PS->GetSkills() : nullptr;
	if (!Inv || !Skills || !CanCraft(Recipe, PS, OutMessage))
	{
		return false;
	}

	// Consume first so freed slots are available for the output (prevents a false "full").
	TArray<FWLItemQuantity, TInlineAllocator<4>> Consumed;
	for (const FWLItemQuantity& In : Recipe.Inputs)
	{
		if (In.Quantity <= 0)
		{
			continue;
		}
		if (!Inv->RemoveItem(In.ItemId, In.Quantity))
		{
			for (const FWLItemQuantity& Back : Consumed)
			{
				Inv->AddItem(Back.ItemId, Back.Quantity);
			}
			OutMessage = LOCTEXT("MissingNow", "You don't have the materials for that.");
			return false;
		}
		Consumed.Add(In);
	}

	// Cooking can burn: Burnt Food (no XP) instead of the meal.
	const bool bBurned = Recipe.BurnItemId != 0
		&& FMath::FRand() < FWLSkillingMath::BurnChance(Recipe.LevelRequired, Recipe.NoBurnLevel, Skills->GetLevel(Recipe.Skill));
	const int32 OutId = bBurned ? Recipe.BurnItemId : Recipe.OutputItemId;
	const int32 OutQty = bBurned ? 1 : FMath::Max(1, Recipe.OutputQuantity);

	if (!GiveTo(Inv, OutId, OutQty, TEXT("Craft")))
	{
		// Genuinely full: give the materials back so nothing is lost.
		for (const FWLItemQuantity& Back : Consumed)
		{
			Inv->AddItem(Back.ItemId, Back.Quantity);
		}
		OutMessage = LOCTEXT("InvFull", "Your inventory is full.");
		return false;
	}

	if (bBurned)
	{
		OutMessage = LOCTEXT("Burned", "You accidentally burn the food.");
		return true;
	}

	Skills->AddXP(Recipe.Skill, Recipe.XPGranted);
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	const FText OutName = (Data && Data->ItemExists(Recipe.OutputItemId)) ? ItemDisplayName(this, Recipe.OutputItemId) : Recipe.DisplayName;
	OutMessage = FText::Format(LOCTEXT("Made", "You make {0}. ({1} {2} XP)"), OutName, Recipe.XPGranted, SkillDisplayName(this, Recipe.Skill));
	return true;
}

#undef LOCTEXT_NAMESPACE
