#include "Combat/WLFissionGolem.h"
#include "World/WLResourceNode.h"
#include "Components/CapsuleComponent.h"
#include "EngineUtils.h"
#include "Engine/World.h"

#define LOCTEXT_NAMESPACE "WLFissionGolem"

AWLFissionGolem::AWLFissionGolem(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
	// Golems are big (~4.5 m body).
	GetCapsuleComponent()->InitCapsuleSize(120.f, 225.f);
	PlaceholderBody->SetRelativeScale3D(FVector(2.4f, 2.4f, 4.5f));

	DeathMode = EWLEnemyDeathMode::Custom;   // the corpse becomes a harvest node
	bIsAggressive = true;
	Tier = 1;
	CorpseNodeClass = AWLResourceNode::StaticClass();
}

bool AWLFissionGolem::AnyActiveOfTier(const UWorld* World, int32 InTier)
{
	if (!World)
	{
		return false;
	}
	for (TActorIterator<AWLFissionGolem> It(const_cast<UWorld*>(World)); It; ++It)
	{
		if (IsValid(*It) && !It->IsActorBeingDestroyed() && !It->bStripped && It->GetGolemTier() == InTier)
		{
			return true;
		}
	}
	return false;
}

int32 AWLFissionGolem::EffectiveHarvestLevel() const
{
	if (HarvestLevelRequired > 0)
	{
		return HarvestLevelRequired;
	}
	const int32 Index = GetGolemTier() - 1;
	return HarvestLevelByTier.IsValidIndex(Index) ? HarvestLevelByTier[Index] : 1;
}

int32 AWLFissionGolem::EffectiveXPPerCore() const
{
	if (XPPerCore > 0)
	{
		return XPPerCore;
	}
	const int32 Index = GetGolemTier() - 1;
	return HarvestXPByTier.IsValidIndex(Index) ? HarvestXPByTier[Index] : 25;
}

void AWLFissionGolem::BeginCorpse()
{
	if (bIsCorpse || !HasAuthority())
	{
		return;
	}
	bIsCorpse = true;

	// Silence the combatant: the body stays where it fell; the node on it takes the clicks.
	SetCombatCollision(false);

	UWorld* World = GetWorld();
	UClass* NodeClass = CorpseNodeClass ? CorpseNodeClass.Get() : AWLResourceNode::StaticClass();
	const FTransform Where(GetActorRotation(), GetActorLocation() - FVector(0.f, 0.f, GetCapsuleComponent()->GetScaledCapsuleHalfHeight()));
	AWLResourceNode* Node = World ? World->SpawnActorDeferred<AWLResourceNode>(NodeClass, Where, this, nullptr, ESpawnActorCollisionHandlingMethod::AlwaysSpawn) : nullptr;
	if (!Node)
	{
		SetLifeSpan(StrippedCorpseSeconds);
		return;
	}

	Node->Skill = HarvestSkill;
	Node->RequiredToolId = HarvestToolItemId;
	Node->LevelRequired = EffectiveHarvestLevel();
	Node->DropItemId = RawCoreBaseItemId + GetGolemTier() - 1;
	Node->DropQuantity = 1;                  // one core per successful strip
	Node->XPPerAction = EffectiveXPPerCore();
	Node->MinYield = FMath::Max(1, MinYield);
	Node->MaxYield = FMath::Max(Node->MinYield, MaxYield);
	Node->RespawnTicks = MAX_int32;          // this corpse never respawns; a spawner makes a new golem
	Node->TicksPerCycle = 4;
	Node->AttemptsPerCycle = 2;
	Node->SuccessChance = 256;
	Node->DisplayNameOverride = FText::Format(LOCTEXT("CorpseName", "{0} Corpse"), GetEnemyDisplayName());
	Node->ExamineOverride = LOCTEXT("CorpseExamine", "The reactor at its core is exposed. Strip it for fission slag.");
	Node->FinishSpawning(Where);
	Node->ResetYield();
	Node->OnDepleted.AddUObject(this, &AWLFissionGolem::HandleCorpseDepleted);
	CorpseNode = Node;
}

void AWLFissionGolem::HandleCorpseDepleted(AWLResourceNode* Node)
{
	if (!HasAuthority())
	{
		return;
	}
	bStripped = true;
	// Stripped clean: remove the corpse so the geiger stops pointing here (and the spawner can replace it).
	if (Node)
	{
		Node->SetLifeSpan(StrippedCorpseSeconds);
	}
	SetLifeSpan(StrippedCorpseSeconds);
}

#undef LOCTEXT_NAMESPACE
