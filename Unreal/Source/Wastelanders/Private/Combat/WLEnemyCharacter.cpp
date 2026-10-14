#include "Combat/WLEnemyCharacter.h"
#include "Combat/WLEnemyAIController.h"
#include "Combat/WLCombatComponent.h"
#include "Combat/WLCombatMath.h"
#include "Combat/WLCombatFeedbackSubsystem.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "AbilitySystem/WLCombatAttributeSet.h"
#include "Core/WLGameEventsSubsystem.h"
#include "Items/WLLootLibrary.h"
#include "Items/WLEquipmentComponent.h"
#include "Items/WLDropTableAsset.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "Components/CapsuleComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "UObject/ConstructorHelpers.h"
#include "Net/UnrealNetwork.h"
#include "TimerManager.h"

#define LOCTEXT_NAMESPACE "WLEnemy"

AWLEnemyCharacter::AWLEnemyCharacter(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
	PrimaryActorTick.bCanEverTick = false;
	bReplicates = true;
	SetNetUpdateFrequency(20.f);

	AIControllerClass = AWLEnemyAIController::StaticClass();
	AutoPossessAI = EAutoPossessAI::PlacedInWorldOrSpawned;

	GetCapsuleComponent()->InitCapsuleSize(40.f, 90.f);
	bUseControllerRotationYaw = false;
	if (UCharacterMovementComponent* Move = GetCharacterMovement())
	{
		Move->bOrientRotationToMovement = true;
		Move->RotationRate = FRotator(0.f, 480.f, 0.f);
		Move->MaxWalkSpeed = MoveSpeed;
	}

	AbilitySystem = CreateDefaultSubobject<UWLAbilitySystemComponent>(TEXT("AbilitySystem"));
	AbilitySystem->SetIsReplicated(true);
	// Minimal: attributes (HP bar) to everyone, no GE replication. Enemies have no special energy.
	AbilitySystem->SetReplicationMode(EGameplayEffectReplicationMode::Minimal);
	AbilitySystem->bRegenSpecialEnergy = false;

	CombatAttributes = CreateDefaultSubobject<UWLCombatAttributeSet>(TEXT("CombatAttributes"));

	// Placeholder body: an engine cylinder the size of the capsule. Not Unity art; hidden once a
	// skeletal mesh for the new art direction is assigned.
	PlaceholderBody = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("PlaceholderBody"));
	PlaceholderBody->SetupAttachment(GetCapsuleComponent());
	PlaceholderBody->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	PlaceholderBody->SetRelativeScale3D(FVector(0.8f, 0.8f, 1.8f));
	static ConstructorHelpers::FObjectFinder<UStaticMesh> Cylinder(TEXT("/Engine/BasicShapes/Cylinder.Cylinder"));
	if (Cylinder.Succeeded())
	{
		PlaceholderBody->SetStaticMesh(Cylinder.Object);
	}
}

void AWLEnemyCharacter::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(AWLEnemyCharacter, DisplayName);
	DOREPLIFETIME(AWLEnemyCharacter, Tier);
	DOREPLIFETIME(AWLEnemyCharacter, bIsDummy);
	DOREPLIFETIME(AWLEnemyCharacter, bIsMiniBoss);
	DOREPLIFETIME(AWLEnemyCharacter, bIsBoss);
}

void AWLEnemyCharacter::PostInitializeComponents()
{
	Super::PostInitializeComponents();
	AbilitySystem->InitAbilityActorInfo(this, this);
}

void AWLEnemyCharacter::BeginPlay()
{
	Super::BeginPlay();

	const bool bHasArt = GetMesh() && GetMesh()->GetSkeletalMeshAsset() != nullptr;
	PlaceholderBody->SetVisibility(!bHasArt);

	if (HasAuthority())
	{
		HomeLocation = GetActorLocation();
		GetCharacterMovement()->MaxWalkSpeed = MoveSpeed;
		AbilitySystem->SetMaxHealth(MaxHP, false);
		AbilitySystem->HealToFull();
		AbilitySystem->OnDamageTaken.AddUObject(this, &AWLEnemyCharacter::HandleDamageTaken);
		AbilitySystem->OnOutOfHealth.AddUObject(this, &AWLEnemyCharacter::HandleOutOfHealth);
	}
}

UAbilitySystemComponent* AWLEnemyCharacter::GetAbilitySystemComponent() const
{
	return AbilitySystem;
}

int32 AWLEnemyCharacter::GetTier() const
{
	return FWLCombatMath::LootTier(Tier, MaxHP);
}

bool AWLEnemyCharacter::IsDead() const
{
	return AbilitySystem && AbilitySystem->IsDead();
}

bool AWLEnemyCharacter::IsHuntable() const
{
	return !bIsDummy && !bIsBoss && !bIsMiniBoss && !bIsGodHunter && !IsFissionGolem();
}

FText AWLEnemyCharacter::GetEnemyDisplayName() const
{
	if (bIsDummy && DisplayName.IsEmpty())
	{
		return LOCTEXT("Dummy", "Training Dummy");
	}
	if (!DisplayName.IsEmpty())
	{
		return DisplayName;
	}
	// Unity prettified the GameObject name ("HornedRavager(Clone)" -> "Horned Ravager").
	FString Name = GetClass()->GetName();
	Name.RemoveFromStart(TEXT("BP_"));
	Name.RemoveFromEnd(TEXT("_C"));
	return FText::FromString(FName::NameToDisplayString(Name, false));
}

int32 AWLEnemyCharacter::GetCurrentHP() const
{
	return AbilitySystem ? AbilitySystem->GetCurrentHealthInt() : 0;
}

int32 AWLEnemyCharacter::GetMaxHP() const
{
	return AbilitySystem ? AbilitySystem->GetMaxHealthInt() : MaxHP;
}

AWLEnemyAIController* AWLEnemyCharacter::GetEnemyAI() const
{
	return Cast<AWLEnemyAIController>(GetController());
}

AActor* AWLEnemyCharacter::GetAggroTarget() const
{
	const AWLEnemyAIController* AI = GetEnemyAI();
	return AI ? AI->GetCurrentTarget() : nullptr;
}

bool AWLEnemyCharacter::IsAggroOn(const AActor* Pawn) const
{
	if (!Pawn || IsDead())
	{
		return false;
	}
	if (FVector::Dist2D(Pawn->GetActorLocation(), GetActorLocation()) > AggroRange)
	{
		return false;
	}
	const AWLEnemyAIController* AI = GetEnemyAI();
	return bIsAggressive || (AI && AI->IsProvokedBy(Pawn));
}

void AWLEnemyCharacter::Stagger(float Seconds)
{
	if (AWLEnemyAIController* AI = GetEnemyAI())
	{
		AI->Stagger(Seconds);
	}
}

void AWLEnemyCharacter::ApplyStats(const FWLEnemyStats& Stats)
{
	MaxHP = FMath::Max(1, Stats.MaxHP);
	AttackLevel = FMath::Max(1, Stats.AttackLevel);
	DefenceLevel = FMath::Max(1, Stats.DefenceLevel);
	MaxHit = FMath::Max(0, Stats.MaxHit);
	bIsAggressive = Stats.bAggressive;
	AggroRange = Stats.AggroRange;
	AttackRange = Stats.AttackRange;
	MoveSpeed = Stats.MoveSpeed;
	AttackCooldown = FMath::Max(0.1f, Stats.AttackCooldown);
	WindupSeconds = Stats.WindupSeconds;
	WanderRadius = Stats.WanderRadius;

	if (HasAuthority() && HasActorBegunPlay())
	{
		// Applied after spawn: re-seed HP (Unity "AddComponent initialised HP before the stats").
		GetCharacterMovement()->MaxWalkSpeed = MoveSpeed;
		AbilitySystem->SetMaxHealth(MaxHP, false);
		AbilitySystem->HealToFull();
	}
}

void AWLEnemyCharacter::ResetEnemy()
{
	if (!HasAuthority())
	{
		return;
	}
	GetWorldTimerManager().ClearTimer(CorpseTimer);
	GetWorldTimerManager().ClearTimer(RespawnTimer);
	DamageByPlayer.Reset();
	AbilitySystem->SetMaxHealth(MaxHP, false);
	AbilitySystem->Revive();
	SetActorHiddenInGame(false);
	SetCombatCollision(true);
	if (AWLEnemyAIController* AI = GetEnemyAI())
	{
		AI->ResetBrain();
	}
}

// --- swings ----------------------------------------------------------------------------------------

void AWLEnemyCharacter::LandSwing(APawn* Target)
{
	if (!HasAuthority() || IsDead() || !Target)
	{
		return;
	}
	OnSwingLanded.Broadcast();   // the blow goes in either way; whether it connects is decided below

	const float Dist = FVector::Dist2D(Target->GetActorLocation(), GetActorLocation());
	if (Dist > AttackRange * SwingReachBonus)
	{
		FWLCombatFeedback::Hitsplat(Target, 0, false);   // clean whiff: they stepped out
		return;
	}
	if (const AWLPlayerCharacter* Player = Cast<AWLPlayerCharacter>(Target))
	{
		if (UWLCombatComponent* Combat = Player->GetCombat())
		{
			Combat->ResolveIncomingAttack(this);
		}
	}
}

void AWLEnemyCharacter::TakeReturnDamage(int32 Amount, AActor* Source)
{
	if (Amount <= 0 || IsDead())
	{
		return;
	}
	const int32 Dealt = AbilitySystem->ApplyDamage(Amount, Source, Source);
	FWLCombatFeedback::Hitsplat(this, Dealt, true, TEXT("REFLECT"));
}

void AWLEnemyCharacter::MulticastHitsplat_Implementation(int32 Amount, bool bHit, FName Tag)
{
	if (UWLCombatFeedbackSubsystem* Feedback = UWLCombatFeedbackSubsystem::Get(this))
	{
		FWLHitsplat Splat;
		Splat.Target = this;
		Splat.Amount = Amount;
		Splat.bHit = bHit;
		Splat.Tag = Tag;
		Feedback->Push(Splat);
	}
}

// --- damage & death --------------------------------------------------------------------------------

AWLPlayerState* AWLEnemyCharacter::ResolvePlayerState(AActor* Instigator)
{
	if (AWLPlayerState* PS = Cast<AWLPlayerState>(Instigator))
	{
		return PS;
	}
	if (const APawn* Pawn = Cast<APawn>(Instigator))
	{
		return Pawn->GetPlayerState<AWLPlayerState>();
	}
	if (const AController* Controller = Cast<AController>(Instigator))
	{
		return Controller->GetPlayerState<AWLPlayerState>();
	}
	return nullptr;
}

void AWLEnemyCharacter::HandleDamageTaken(int32 Amount, AActor* Instigator, AActor* /*Causer*/)
{
	AWLPlayerState* PS = ResolvePlayerState(Instigator);
	if (!PS)
	{
		return;   // turrets, reflect from nobody: no credit, no provocation
	}
	DamageByPlayer.FindOrAdd(PS) += Amount;

	// Passive creatures fight back once hit (Enemy3D._provoked), against that player's body.
	if (AWLEnemyAIController* AI = GetEnemyAI())
	{
		AI->NotifyProvoked(PS->GetPawn());
	}
}

AWLPlayerState* AWLEnemyCharacter::FindTopDamager() const
{
	AWLPlayerState* Best = nullptr;
	int32 BestDamage = 0;
	for (const TPair<TWeakObjectPtr<AWLPlayerState>, int32>& Pair : DamageByPlayer)
	{
		if (Pair.Key.IsValid() && Pair.Value > BestDamage)
		{
			Best = Pair.Key.Get();
			BestDamage = Pair.Value;
		}
	}
	return Best;
}

void AWLEnemyCharacter::HandleOutOfHealth(AActor* Instigator, AActor* /*Causer*/)
{
	HandleDeath(Instigator);
}

void AWLEnemyCharacter::HandleDeath(AActor* Instigator)
{
	if (AWLEnemyAIController* AI = GetEnemyAI())
	{
		AI->ResetBrain();
	}
	GetCharacterMovement()->StopMovementImmediately();

	AWLPlayerState* Killer = FindTopDamager();
	if (!Killer)
	{
		Killer = ResolvePlayerState(Instigator);
	}

	// Dummies never count as kills or drop loot (CombatTarget.TakeDamage).
	if (!bIsDummy)
	{
		if (Killer)
		{
			Killer->IncrementKills();
		}
		if (UWLGameEventsSubsystem* Events = UWLGameEventsSubsystem::Get(this))
		{
			Events->OnEnemyKilled.Broadcast(this, Killer);
		}

		FWLEnemyLootParams Params;
		Params.Tier = GetTier();
		Params.bMiniBoss = bIsMiniBoss;
		Params.bBoss = bIsBoss;
		Params.bGodHunter = bIsGodHunter;
		Params.DropTable = DropTable;
		const UWLEquipmentComponent* Equipment = Killer ? Killer->GetEquipment() : nullptr;
		Params.SalvagePower = Equipment ? Equipment->ModulePower(WLModules::EffectName(EWLModuleEffect::Salvage)) : 0;
		UWLLootLibrary::DropEnemyLoot(Params, GetActorLocation(), Killer);
	}

	OnEnemyDied.Broadcast(this);
	BeginCorpse();
}

void AWLEnemyCharacter::SetCombatCollision(bool bEnabled)
{
	// Dead bodies can't be clicked or bumped (Enemy3D turns colliders off at death).
	GetCapsuleComponent()->SetCollisionEnabled(bEnabled ? ECollisionEnabled::QueryAndPhysics : ECollisionEnabled::NoCollision);
}

void AWLEnemyCharacter::BeginCorpse()
{
	switch (DeathMode)
	{
	case EWLEnemyDeathMode::RespawnAtHome:
		SetCombatCollision(false);
		GetWorldTimerManager().SetTimer(CorpseTimer, this, &AWLEnemyCharacter::HideCorpse, FMath::Max(0.01f, CorpseSeconds), false);
		GetWorldTimerManager().SetTimer(RespawnTimer, FTimerDelegate::CreateWeakLambda(this, [this]()
		{
			TeleportTo(HomeLocation, GetActorRotation());
			ResetEnemy();
		}), FMath::Max(CorpseSeconds, RespawnSeconds), false);
		break;

	case EWLEnemyDeathMode::ResetInPlace:
		// Training dummies stay put and clickable; they just refill a few seconds later.
		GetWorldTimerManager().SetTimer(RespawnTimer, this, &AWLEnemyCharacter::ResetEnemy, FMath::Max(0.1f, RespawnSeconds), false);
		break;

	case EWLEnemyDeathMode::DestroyCorpse:
		SetCombatCollision(false);
		SetLifeSpan(FMath::Max(0.1f, CorpseSeconds));
		break;

	case EWLEnemyDeathMode::Custom:
	default:
		break;
	}
}

void AWLEnemyCharacter::HideCorpse()
{
	SetActorHiddenInGame(true);
}

// --- IWLInteractable ---------------------------------------------------------------------------------

FText AWLEnemyCharacter::GetInteractName_Implementation() const
{
	return GetEnemyDisplayName();
}

FText AWLEnemyCharacter::GetExamineText_Implementation() const
{
	return bIsDummy
		? LOCTEXT("ExamineDummy", "A stuffed practice dummy. Hit it to train your combat.")
		: LOCTEXT("ExamineHostile", "A hostile creature. Looks like it wants a fight.");
}

TArray<FWLInteractOption> AWLEnemyCharacter::GetInteractOptions_Implementation(const AWLPlayerCharacter* /*Player*/) const
{
	TArray<FWLInteractOption> Options;
	if (!IsDead())
	{
		FWLInteractOption& Attack = Options.AddDefaulted_GetRef();
		Attack.Verb = TEXT("Attack");
		Attack.Label = LOCTEXT("Attack", "Attack");
		Attack.bDefault = true;
	}
	return Options;
}

float AWLEnemyCharacter::GetInteractRange_Implementation() const
{
	// Combat walks into weapon reach itself (and knows ranged reach); the interactor must not
	// march a gunner into melee range first. Anything within leash distance may start the fight.
	return 1600.f;
}

bool AWLEnemyCharacter::Interact_Implementation(AWLPlayerCharacter* Player, FName Verb)
{
	if (!HasAuthority() || !Player || IsDead() || Verb != TEXT("Attack"))
	{
		return false;
	}
	UWLCombatComponent* Combat = Player->GetCombat();
	return Combat && Combat->Engage(this);
}

#undef LOCTEXT_NAMESPACE
