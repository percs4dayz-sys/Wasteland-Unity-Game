#include "World/WLResourceNode.h"
#include "Skilling/WLGatheringTools.h"
#include "Skilling/WLSkillingMath.h"
#include "Skilling/WLSkillingComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "Items/WLInventoryComponent.h"
#include "Items/WLEquipmentComponent.h"
#include "Items/WLItemTypes.h"
#include "Skills/WLSkillsComponent.h"
#include "Data/WLDataSubsystem.h"
#include "Components/StaticMeshComponent.h"
#include "Net/UnrealNetwork.h"

#define LOCTEXT_NAMESPACE "WLResourceNode"

const FName AWLResourceNode::GatherVerb(TEXT("Gather"));

namespace WLNodeText
{
	FText SkillName(const UObject* Ctx, EWLSkill Skill)
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

	FText ItemName(const UObject* Ctx, int32 ItemId, const FText& Fallback)
	{
		if (const UWLDataSubsystem* Data = UWLDataSubsystem::Get(Ctx))
		{
			if (const FWLItemRow* Row = Data->FindItem(ItemId))
			{
				return Row->DisplayName;
			}
		}
		return Fallback;
	}
}

AWLResourceNode::AWLResourceNode()
{
	DisplayName = FText::GetEmpty();
	InteractRange = 110.f; // Unity GatherReach: work solid nodes standing right against them

	InteractionPoint = CreateDefaultSubobject<USceneComponent>(TEXT("InteractionPoint"));
	InteractionPoint->SetupAttachment(Root);
	InteractionPoint->SetRelativeLocation(FVector(-150.f, 0.f, 0.f));

	DepletedMesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("DepletedMesh"));
	DepletedMesh->SetupAttachment(Root);
	DepletedMesh->SetHiddenInGame(true);
	DepletedMesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);

	PlaceholderColor = FLinearColor(0.45f, 0.42f, 0.38f);
	PlaceholderSize = FVector(140.f, 140.f, 90.f);
}

void AWLResourceNode::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(AWLResourceNode, bDepleted);
}

void AWLResourceNode::BeginPlay()
{
	Super::BeginPlay();

	if (!DepletedArtMesh.IsNull())
	{
		DepletedMesh->SetStaticMesh(DepletedArtMesh.LoadSynchronous());
	}

	ActiveCollision = Mesh ? Mesh->GetCollisionEnabled() : ECollisionEnabled::QueryAndPhysics;

	if (HasAuthority())
	{
		ResetYield();
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			Tick->Register(TScriptInterface<IWLTickable>(this));
		}
	}
	ApplyDepletedState();
}

void AWLResourceNode::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
	{
		Tick->Unregister(TScriptInterface<IWLTickable>(this));
	}
	Super::EndPlay(EndPlayReason);
}

void AWLResourceNode::ResetYield()
{
	int32 Lo = 1, Hi = 1;
	FWLSkillingMath::ClampYield(MinYield, MaxYield, Lo, Hi);
	HitsRemaining = FMath::RandRange(Lo, Hi); // inclusive, like Unity Random.Range(lo, hi + 1)
}

void AWLResourceNode::OnGameTick(int64 TickCount)
{
	if (!bDepleted)
	{
		return;
	}
	if (--RespawnCountdown <= 0)
	{
		Respawn();
	}
}

void AWLResourceNode::Deplete()
{
	RespawnCountdown = RespawnTicks;
	bDepleted = true;
	ApplyDepletedState();
	OnDepleted.Broadcast(this);
}

void AWLResourceNode::Respawn()
{
	ResetYield();
	bDepleted = false;
	ApplyDepletedState();
	OnRespawned.Broadcast(this);
}

void AWLResourceNode::OnRep_Depleted()
{
	ApplyDepletedState();
	if (bDepleted)
	{
		OnDepleted.Broadcast(this);
	}
	else
	{
		OnRespawned.Broadcast(this);
	}
}

void AWLResourceNode::ApplyDepletedState()
{
	// The node vanishes (render + collision) while depleted, showing its stump if it has one.
	if (Mesh)
	{
		Mesh->SetHiddenInGame(bDepleted);
		Mesh->SetCollisionEnabled(bDepleted ? ECollisionEnabled::NoCollision : ActiveCollision.GetValue());
	}
	if (DepletedMesh)
	{
		const bool bShowStump = bDepleted && DepletedMesh->GetStaticMesh() != nullptr;
		DepletedMesh->SetHiddenInGame(!bShowStump);
		DepletedMesh->SetCollisionEnabled(bShowStump ? ECollisionEnabled::QueryAndPhysics : ECollisionEnabled::NoCollision);
	}
}

FVector AWLResourceNode::GetInteractionLocation() const
{
	return bUseInteractionPoint && InteractionPoint ? InteractionPoint->GetComponentLocation() : GetActorLocation();
}

FVector AWLResourceNode::GetFacingLocation() const
{
	// A model's pivot is often at a corner; aim at the visible middle. Fishing spots face the spot.
	if (Skill == EWLSkill::Fishing || !Mesh || !Mesh->GetStaticMesh())
	{
		return GetActorLocation();
	}
	return Mesh->Bounds.Origin;
}

// --- presentation -------------------------------------------------------------------------------

FText AWLResourceNode::GetDefaultDisplayName() const
{
	switch (NodeType)
	{
	case EWLResourceNodeType::RubblePile:   return LOCTEXT("RubblePile", "Rubble Pile");
	case EWLResourceNodeType::WoodenDebris: return LOCTEXT("WoodenDebris", "Wooden Debris");
	case EWLResourceNodeType::WaterBarrel:  return LOCTEXT("FishingSpot", "Fishing Spot");
	case EWLResourceNodeType::RatNest:      return LOCTEXT("RatNest", "Rat Nest");
	case EWLResourceNodeType::SulphurVent:  return LOCTEXT("SulphurVent", "Sulphur Vent");
	default:                                return LOCTEXT("ResourceNode", "Resource Node");
	}
}

FText AWLResourceNode::GetInteractName_Implementation() const
{
	return DisplayNameOverride.IsEmpty() ? GetDefaultDisplayName() : DisplayNameOverride;
}

FText AWLResourceNode::GetExamineText_Implementation() const
{
	if (bDepleted)
	{
		return FText::Format(LOCTEXT("ExamineDepleted", "A {0} - picked clean for now."),
			FText::FromString(GetInteractName_Implementation().ToString().ToLower()));
	}
	if (!ExamineOverride.IsEmpty())
	{
		return ExamineOverride;
	}
	switch (NodeType)
	{
	case EWLResourceNodeType::RubblePile:   return LOCTEXT("ExRubble", "A pile of broken concrete. Worth breaking apart for scrap.");
	case EWLResourceNodeType::WoodenDebris: return LOCTEXT("ExWood", "Splintered planks and pallets. Good for salvaging wood.");
	case EWLResourceNodeType::WaterBarrel:  return LOCTEXT("ExWater", "A murky pool. Cast a line to catch some shrimp.");
	case EWLResourceNodeType::RatNest:      return LOCTEXT("ExRat", "A filthy nest. Something stirs inside...");
	case EWLResourceNodeType::SulphurVent:  return LOCTEXT("ExSulphur", "A crust of yellow crystal around a hissing vent. Ammo starts here.");
	default:                                return LOCTEXT("ExDefault", "A scavengeable resource.");
	}
}

TArray<FWLInteractOption> AWLResourceNode::GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const
{
	FText VerbText;
	switch (Skill)
	{
	case EWLSkill::Woodcutting: VerbText = LOCTEXT("ChopDown", "Chop down"); break;
	case EWLSkill::Scrapping:   VerbText = LOCTEXT("Mine", "Mine"); break;
	case EWLSkill::Fishing:     VerbText = LOCTEXT("FishAt", "Fish at"); break;
	default:                    VerbText = LOCTEXT("Gather", "Gather"); break;
	}
	FWLInteractOption Option;
	Option.Verb = GatherVerb;
	Option.Label = FText::Format(LOCTEXT("VerbTarget", "{0} {1}"), VerbText, GetInteractName_Implementation());
	Option.bDefault = true;
	return { Option };
}

bool AWLResourceNode::Interact_Implementation(AWLPlayerCharacter* Player, FName Verb)
{
	if (!Player || Verb != GatherVerb)
	{
		return false;
	}
	if (bDepleted)
	{
		SendMessage(Player, LOCTEXT("NothingLeft", "Nothing left there right now."));
		return false;
	}
	UWLSkillingComponent* Skilling = Player->GetSkilling();
	return Skilling && Skilling->StartGathering(this);
}

// --- gathering ----------------------------------------------------------------------------------

bool AWLResourceNode::HasBetterTool(const AWLPlayerState* PS) const
{
	if (BetterToolId <= 0 || !PS)
	{
		return false;
	}
	const UWLInventoryComponent* Inv = PS->GetInventory();
	const UWLEquipmentComponent* Equip = PS->GetEquipment();
	return (Inv && Inv->Contains(BetterToolId, 1)) || (Equip && Equip->GetItemId(EWLEquipSlot::Tool) == BetterToolId);
}

int32 AWLResourceNode::CurrentTicksPerCycle(const AWLPlayerState* PS) const
{
	if (HasBetterTool(PS) && BetterToolTicksPerCycle > 0)
	{
		return BetterToolTicksPerCycle;
	}
	return TicksPerCycle > 0 ? TicksPerCycle : 4;
}

int32 AWLResourceNode::CurrentAttempts(const AWLPlayerState* PS) const
{
	if (HasBetterTool(PS) && BetterToolAttemptsPerCycle > 0)
	{
		return BetterToolAttemptsPerCycle;
	}
	return AttemptsPerCycle > 0 ? AttemptsPerCycle : 1;
}

int32 AWLResourceNode::CurrentChance(const AWLPlayerState* PS) const
{
	// A held better tool (e.g. sacred-clay harpoon) overrides everything when set.
	if (HasBetterTool(PS) && BetterToolSuccessChance > 0)
	{
		return BetterToolSuccessChance;
	}
	const UWLSkillsComponent* Skills = PS ? PS->GetSkills() : nullptr;
	const int32 Level = Skills ? Skills->GetLevel(Skill) : 1;
	return FWLSkillingMath::GatherChance(Level, SuccessChance, ChanceAtLevel1, ChanceAtLevel99);
}

EWLGatherResult AWLResourceNode::TryGatherCycle(AWLPlayerState* PS, FText& OutMessage)
{
	OutMessage = FText::GetEmpty();
	if (!HasAuthority() || !PS)
	{
		return EWLGatherResult::Stop;
	}
	if (bDepleted)
	{
		OutMessage = LOCTEXT("NothingToGather", "Nothing left to gather here.");
		return EWLGatherResult::Stop;
	}

	UWLInventoryComponent* Inv = PS->GetInventory();
	UWLSkillsComponent* Skills = PS->GetSkills();
	const UWLEquipmentComponent* Equip = PS->GetEquipment();
	if (!Inv || !Skills)
	{
		return EWLGatherResult::Stop;
	}

	if (!FWLGatheringTools::CanUse(PS, RequiredToolId))
	{
		const FText ToolName = WLNodeText::ItemName(this, RequiredToolId, LOCTEXT("RightTool", "the right tool"));
		OutMessage = FText::Format(LOCTEXT("NeedTool", "You need a {0} or a usable upgrade to do that."), ToolName);
		return EWLGatherResult::Stop;
	}

	if (Skills->GetLevel(Skill) < LevelRequired)
	{
		OutMessage = FText::Format(LOCTEXT("NeedLevel", "Requires {0} level {1}."), WLNodeText::SkillName(this, Skill), LevelRequired);
		return EWLGatherResult::Stop;
	}

	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
	const FWLItemRow* DropRow = Data ? Data->FindItem(DropItemId) : nullptr;
	const bool bStackable = DropRow && DropRow->bStackable;
	// Unity stopped on any full pack; a stackable that already has a stack still fits, so allow it.
	if (Inv->FreeSlots() <= 0 && !(bStackable && Inv->Contains(DropItemId, 1)))
	{
		OutMessage = LOCTEXT("InvFull", "Your inventory is full.");
		return EWLGatherResult::Stop;
	}

	const int32 Chance = CurrentChance(PS);
	const bool bCaught = FWLSkillingMath::RollCycle(CurrentAttempts(PS), Chance, [] { return FMath::RandRange(0, FWLSkillingMath::ChanceScale - 1); });
	if (!bCaught)
	{
		return EWLGatherResult::NoCatch; // missed this cycle: keep trying, no chat spam
	}

	// Clamped so a node configured with 0 still yields something instead of silently nothing.
	int32 Qty = FMath::Max(1, DropQuantity);
	const int32 GatherPower = Equip ? Equip->ModulePower(TEXT("Gather")) : 0;
	const bool bRoomForBonus = bStackable || Inv->FreeSlots() > Qty;
	const bool bProc = bRoomForBonus && FMath::FRand() < FWLSkillingMath::GatherModuleChance(GatherPower);
	Qty = FWLSkillingMath::GatherQuantity(DropQuantity, bProc);

	FWLItemStack Stack;
	Stack.ItemId = DropItemId;
	Stack.Quantity = Qty;
	if (!Inv->GiveItem(Stack, TEXT("Gather")))
	{
		OutMessage = LOCTEXT("NoRoom", "There is no room for this resource.");
		return EWLGatherResult::Stop;
	}
	Skills->AddXP(Skill, XPPerAction);

	if (--HitsRemaining <= 0)
	{
		Deplete(); // node vanishes until it respawns
	}

	const FText DropName = DropRow ? DropRow->DisplayName : LOCTEXT("Item", "item");
	const FText Amount = Qty > 1
		? FText::Format(LOCTEXT("AmountMany", "{0} x {1}"), Qty, DropName)
		: FText::Format(LOCTEXT("AmountOne", "some {0}"), DropName);
	OutMessage = FText::Format(LOCTEXT("Gathered", "You gather {0}. ({1} {2} XP)"), Amount, XPPerAction, WLNodeText::SkillName(this, Skill));
	return EWLGatherResult::Caught;
}

#undef LOCTEXT_NAMESPACE
