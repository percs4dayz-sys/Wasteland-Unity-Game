#include "Companions/WLCompanionComponent.h"
#include "Companions/WLCompanionPawn.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Quests/WLQuestFlags.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerCharacter.h"
#include "Items/WLInventoryComponent.h"
#include "Skills/WLSkillsComponent.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Combat/WLEnemyCharacter.h"
#include "Combat/WLCombatComponent.h"
#include "Combat/WLCombatFeedbackSubsystem.h"
#include "Core/WLGameTickSubsystem.h"
#include "Core/WLXPTable.h"
#include "Persistence/WLCharacterSave.h"
#include "EngineUtils.h"
#include "Engine/World.h"
#include "Net/UnrealNetwork.h"

#define LOCTEXT_NAMESPACE "WLCompanion"

UWLCompanionComponent::UWLCompanionComponent()
{
	PrimaryComponentTick.bCanEverTick = false;
	SetIsReplicatedByDefault(true);
	PawnClass = AWLCompanionPawn::StaticClass();
}

void UWLCompanionComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME_CONDITION(UWLCompanionComponent, Status, COND_OwnerOnly);
}

void UWLCompanionComponent::BeginPlay()
{
	Super::BeginPlay();
	if (GetOwner() && GetOwner()->HasAuthority())
	{
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			TickHandle = Tick->OnGameTick.AddUObject(this, &UWLCompanionComponent::HandleGameTick);
		}
		Stamina = GetDefs()->MaxStamina;
	}
}

void UWLCompanionComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
	{
		Tick->OnGameTick.Remove(TickHandle);
	}
	DestroyPawn();
	Super::EndPlay(EndPlayReason);
}

// ------------------------------------------------------------------ accessors

const UWLCompanionDefinitions* UWLCompanionComponent::GetDefs() const
{
	return Definitions ? Definitions.Get() : UWLCompanionDefinitions::GetDefaultDefinitions();
}

AWLPlayerState* UWLCompanionComponent::GetPS() const { return Cast<AWLPlayerState>(GetOwner()); }

AWLPlayerController* UWLCompanionComponent::GetPC() const
{
	const AWLPlayerState* PS = GetPS();
	return PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr;
}

APawn* UWLCompanionComponent::GetOwnerPawn() const
{
	const AWLPlayerState* PS = GetPS();
	return PS ? PS->GetPawn() : nullptr;
}

double UWLCompanionComponent::Now() const
{
	const UWorld* World = GetWorld();
	return World ? World->GetTimeSeconds() : 0.0;
}

int32 UWLCompanionComponent::BeastLevel() const
{
	const AWLPlayerState* PS = GetPS();
	return PS && PS->GetSkills() ? PS->GetSkills()->GetLevel(EWLSkill::Beastmastery) : 1;
}

bool UWLCompanionComponent::HasCompanion() const
{
	const AWLPlayerState* PS = GetPS();
	if (PS && PS->GetFlags())
	{
		return PS->GetFlags()->HasFlag(WLQuestFlags::PupHatched);
	}
	return Status.bHasCompanion;   // client
}

const FWLCompanionSpecies* UWLCompanionComponent::GetSpecies() const
{
	const UWLCompanionDefinitions* Defs = GetDefs();
	FName Key = Species;
	if (Key.IsNone())
	{
		Key = Status.Species;
	}
	if (Key.IsNone())
	{
		// The line chosen at the egg, persisted via flags. Pre-rework saves (pup, no species flag) are wolves.
		if (const AWLPlayerState* PS = GetPS(); PS && PS->GetFlags())
		{
			for (const FWLCompanionSpecies& S : Defs->Species)
			{
				if (PS->GetFlags()->HasFlag(WLQuestFlags::Species(S.Key.ToString())))
				{
					return &S;
				}
			}
		}
		Key = Defs->DefaultSpecies;
	}
	if (const FWLCompanionSpecies* Found = Defs->FindSpecies(Key))
	{
		return Found;
	}
	return Defs->Species.Num() ? &Defs->Species[0] : nullptr;
}

int32 UWLCompanionComponent::GetStage() const
{
	if (GetOwner() && GetOwner()->HasAuthority())
	{
		return GetDefs()->StageForLevel(BeastLevel());
	}
	return Status.Stage;
}

FText UWLCompanionComponent::GetBeastName() const
{
	const FWLCompanionSpecies* S = GetSpecies();
	return S ? S->SafeStageName(GetStage()) : LOCTEXT("Beast", "beast");
}

void UWLCompanionComponent::SetSpecies(FName Key)
{
	Species = Key;
}

void UWLCompanionComponent::Msg(const FText& Text) const
{
	if (AWLPlayerController* PC = GetPC())
	{
		PC->ClientGameMessage(FText::Format(LOCTEXT("BeastPrefix", "[BEAST]: {0}"), Text));
	}
}

// ------------------------------------------------------------------ tick

void UWLCompanionComponent::HandleGameTick(int64 /*TickCount*/)
{
	AWLPlayerState* PS = GetPS();
	if (!PS || !PS->IsCharacterLoaded())
	{
		return;
	}
	const UWLGameTickSubsystem* TickSys = UWLGameTickSubsystem::Get(this);
	const float Dt = TickSys ? TickSys->GetTickDuration() : 0.6f;

	TickIncubation(Dt);

	const UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem();
	APawn* OwnerPawn = GetOwnerPawn();
	if (!OwnerPawn || (ASC && ASC->IsDead()))
	{
		// Owner down (or between bodies): the beast leaves; nothing is pending when they return.
		bExhausted = false;
		RegroupAt = 0.0;
		if (bAlive || Pawn)
		{
			Dismiss(FText::GetEmpty());
		}
		SyncCarryPerk();
		RefreshStatus();
		return;
	}

	if (bResummonOnSpawn)
	{
		bResummonOnSpawn = false;
		Summon();
	}

	AActor* Target = IsSummoned() ? FindAssistTarget() : nullptr;
	if (IsSummoned())
	{
		// Grew into the next stage (or species changed)? Re-dress the body.
		const FWLCompanionSpecies* S = GetSpecies();
		const int32 Stage = GetStage();
		if (S && (Pawn->GetStage() != Stage || Pawn->GetSpecies() != S->Key))
		{
			Pawn->SetAppearance(S->Key, Stage, S->SafeStageName(Stage), S->SafeScale(Stage), S->HoverHeight);
		}
		UpdatePawnGoal(Target);
		TickCombat();
	}

	TickRegroup(Now());
	TickStamina(Dt, Target != nullptr);
	SyncCarryPerk();
	RefreshStatus();
}

void UWLCompanionComponent::TickIncubation(float Dt)
{
	AWLPlayerState* PS = GetPS();
	UWLInventoryComponent* Inv = PS ? PS->GetInventory() : nullptr;
	if (!Inv || HasCompanion())
	{
		bIncubating = false;
		return;
	}

	const FWLCompanionSpecies* Egg = nullptr;
	for (const FWLCompanionSpecies& S : GetDefs()->Species)
	{
		if (S.EggItemId > 0 && Inv->Contains(S.EggItemId))
		{
			Egg = &S;
			break;
		}
	}
	if (!Egg)
	{
		Incubation = 0.f;
		bIncubating = false;
		return;
	}

	const float Total = GetDefs()->IncubateSeconds;
	const float Before = Incubation;
	Incubation += Dt;
	if (Before < Total * 0.5f && Incubation >= Total * 0.5f)
	{
		Msg(LOCTEXT("EggTwitch", "The egg twitches in your pack..."));
	}
	bIncubating = true;

	if (Incubation >= Total)
	{
		if (!Inv->RemoveItem(Egg->EggItemId, 1))
		{
			return;   // moved out of the bag this very tick; try again next tick
		}
		UWLPlayerFlagsComponent* Flags = PS->GetFlags();
		Flags->SetFlag(WLQuestFlags::PupHatched);
		Flags->SetFlag(WLQuestFlags::Species(Egg->Key.ToString()));
		SetSpecies(Egg->Key);
		bIncubating = false;
		Incubation = 0.f;
		Msg(Egg->HatchMessage);
		Stamina = GetDefs()->MaxStamina;
		DestroyPawn();
		bAlive = false;
		Summon();
	}
}

AActor* UWLCompanionComponent::FindAssistTarget() const
{
	const AWLPlayerCharacter* Char = Cast<AWLPlayerCharacter>(GetOwnerPawn());
	if (!Char)
	{
		return nullptr;
	}

	// The enemy you're locked onto...
	if (const UWLCombatComponent* Combat = Char->GetCombat())
	{
		if (AWLEnemyCharacter* Locked = Cast<AWLEnemyCharacter>(Combat->GetTarget()); Locked && !Locked->IsDead())
		{
			return Locked;
		}
	}

	// ...otherwise the nearest mob ALREADY engaged with you, so the pack keeps fighting while you
	// kite, but never starts a fight on its own.
	const FVector From = Char->GetActorLocation();
	double BestSq = FMath::Square(GetDefs()->AssistRange);
	AWLEnemyCharacter* Best = nullptr;
	for (TActorIterator<AWLEnemyCharacter> It(GetWorld()); It; ++It)
	{
		AWLEnemyCharacter* E = *It;
		if (!E || E->IsDead() || E->IsDummy() || !E->IsAggroOn(Char))
		{
			continue;
		}
		const double D = FVector::DistSquared(E->GetActorLocation(), From);
		if (D < BestSq)
		{
			BestSq = D;
			Best = E;
		}
	}
	return Best;
}

void UWLCompanionComponent::UpdatePawnGoal(AActor* Target)
{
	APawn* OwnerPawn = GetOwnerPawn();
	if (!Pawn || !OwnerPawn)
	{
		return;
	}
	const UWLCompanionDefinitions* Defs = GetDefs();
	const FVector OwnerLoc = OwnerPawn->GetActorLocation();
	const FVector Heel = OwnerLoc + FVector(Defs->FollowOffset.X, Defs->FollowOffset.Y, 0.f);

	if (FVector::Dist2D(Pawn->GetActorLocation(), OwnerLoc) > Defs->LeashDistance)
	{
		Pawn->TeleportNear(Heel);
	}

	if (Target)
	{
		// Stand just within striking distance, on the side nearest the owner, so it doesn't pile onto your shot.
		const FVector TP = Target->GetActorLocation();
		FVector Dir = OwnerLoc - TP;
		Dir.Z = 0.f;
		Dir = Dir.SizeSquared() > 1.f ? Dir.GetSafeNormal() : FVector::BackwardVector;
		Pawn->SetGoal(TP + Dir * Defs->PackReach, Target);
	}
	else
	{
		Pawn->SetGoal(Heel, nullptr);
	}
}

void UWLCompanionComponent::TickCombat()
{
	const FWLCompanionSpecies* S = GetSpecies();
	if (!S || !Pawn)
	{
		return;
	}
	AActor* Target = FindAssistTarget();
	if (!Target)
	{
		TicksSinceAttack = S->AttackEveryTicks;   // first bite lands as soon as it reaches the next fight
		return;
	}

	// It has to reach the enemy first: no biting from across the map.
	const float Bite = GetDefs()->PackReach + GetDefs()->BiteSlack;
	if (FVector::DistSquared2D(Target->GetActorLocation(), Pawn->GetActorLocation()) > FMath::Square(Bite))
	{
		return;
	}

	if (++TicksSinceAttack < S->AttackEveryTicks)
	{
		return;
	}
	TicksSinceAttack = 0;
	Strike(Target, UWLCompanionDefinitions::MaxHitFor(BeastLevel(), S->DamageMult), FText::GetEmpty());
}

void UWLCompanionComponent::Strike(AActor* Target, int32 MaxHit, const FText& Prefix)
{
	AWLEnemyCharacter* Enemy = Cast<AWLEnemyCharacter>(Target);
	const FWLCompanionSpecies* S = GetSpecies();
	if (!Enemy || !S || Enemy->IsDead() || !Enemy->GetWLAbilitySystem())
	{
		return;
	}
	const int32 Damage = FMath::Max(1, FMath::RandRange(1, FMath::Max(1, MaxHit)));
	// Instigator = the owner's pawn so kill credit (and beast tasks) go to the owner; causer = the beast.
	// The beast's own hits give no XP: Beastmastery trains from beast-task kills.
	Enemy->GetWLAbilitySystem()->ApplyDamage(Damage, GetOwnerPawn(), Pawn);
	FWLCombatFeedback::Hitsplat(Enemy, Damage, true);

	const FText Name = GetBeastName().ToLower();
	const FText EnemyName = Enemy->GetEnemyDisplayName();
	Msg(FText::Format(LOCTEXT("Strike", "{0}Your {1} {2} the {3} for {4}."), Prefix, Name, S->Verb, EnemyName, Damage));
	if (Enemy->IsDead())
	{
		Msg(FText::Format(LOCTEXT("StrikeKill", "Your {0} helps bring down the {1}!"), Name, EnemyName));
	}
}

FText UWLCompanionComponent::GetSpecialBlockReason() const
{
	const AWLPlayerState* PS = GetPS();
	const UWLAbilitySystemComponent* ASC = PS ? PS->GetWLAbilitySystem() : nullptr;
	if (!PS || !GetOwnerPawn() || (ASC && ASC->IsDead()))
	{
		return LOCTEXT("BlockDown", "Unavailable while down");
	}
	if (!IsSummoned())
	{
		return LOCTEXT("BlockSummon", "Summon your beast [B]");
	}
	const double Remaining = SpecialReadyAt - Now();
	if (Remaining > 0.0)
	{
		return FText::Format(LOCTEXT("BlockCooldown", "Ready in {0}s"), FMath::CeilToInt(Remaining));
	}
	const AActor* Target = FindAssistTarget();
	if (!Target)
	{
		return LOCTEXT("BlockTarget", "Select an enemy");
	}
	const float Reach = GetDefs()->PackReach + 100.f;
	if (FVector::DistSquared(Pawn->GetActorLocation(), Target->GetActorLocation()) > FMath::Square(Reach))
	{
		return LOCTEXT("BlockApproach", "Beast approaching target");
	}
	return FText::GetEmpty();
}

void UWLCompanionComponent::TriggerSpecial()
{
	const FText Blocked = GetSpecialBlockReason();
	if (!Blocked.IsEmpty())
	{
		Msg(FText::Format(LOCTEXT("BlockedMsg", "{0}."), Blocked));
		return;
	}
	const FWLCompanionSpecies* S = GetSpecies();
	AActor* Target = FindAssistTarget();
	if (!S || !Target)
	{
		return;
	}
	SpecialReadyAt = Now() + GetDefs()->SpecialCooldownSeconds;
	const int32 MaxHit = UWLCompanionDefinitions::MaxHitFor(BeastLevel(), S->DamageMult);
	Strike(Target, UWLCompanionDefinitions::SpecialMaxHitFor(MaxHit, S->SpecialMult),
		FText::Format(LOCTEXT("SpecialPrefix", "{0}! "), S->SpecialName.ToUpper()));
	RefreshStatus();
}

// ------------------------------------------------------------------ damage / recovery

int32 UWLCompanionComponent::AbsorbForPlayer(int32 IncomingDamage)
{
	const APawn* OwnerPawn = GetOwnerPawn();
	const FWLCompanionSpecies* S = GetSpecies();
	if (IncomingDamage <= 0 || !IsSummoned() || !OwnerPawn || !S
		|| FVector::DistSquared(Pawn->GetActorLocation(), OwnerPawn->GetActorLocation()) > FMath::Square(GetDefs()->ShareRange))
	{
		return IncomingDamage;
	}
	const int32 Shared = UWLCompanionDefinitions::SharedDamage(IncomingDamage, S->SharedDamageFraction, HP);
	if (Shared > 0)
	{
		TakePackDamage(Shared);
	}
	return IncomingDamage - Shared;
}

void UWLCompanionComponent::TakePackDamage(int32 Amount)
{
	if (!IsSummoned())
	{
		return;
	}
	HP = FMath::Max(0, HP - Amount);
	if (HP <= 0)
	{
		const FText Name = GetBeastName().ToLower();
		bAlive = false;
		DestroyPawn();
		RegroupAt = Now() + GetDefs()->RegroupSeconds;
		Msg(FText::Format(LOCTEXT("Scattered", "Your {0} is overwhelmed and flees!"), Name));
	}
	RefreshStatus();
}

void UWLCompanionComponent::TickRegroup(double InNow)
{
	// Auto-regroup after being scattered (RegroupAt is 0 when dismissed on purpose).
	if (bAlive || RegroupAt <= 0.0 || InNow < RegroupAt)
	{
		return;
	}
	RegroupAt = 0.0;
	Summon();
	if (IsSummoned())
	{
		Msg(FText::Format(LOCTEXT("Regrouped", "Your {0} returns, ready to fight again."), GetBeastName().ToLower()));
	}
	else
	{
		bExhausted = true;   // too tired to regroup now: returns once rested
	}
}

void UWLCompanionComponent::TickStamina(float Dt, bool bFighting)
{
	// Stamina is the Beastmastery downside: drains while out (faster in combat), regenerates while
	// resting, twice as fast when the owner is at or below half HP. Empty = it goes off to rest.
	const UWLCompanionDefinitions* Defs = GetDefs();
	const AWLPlayerState* PS = GetPS();
	const UWLAbilitySystemComponent* ASC = PS ? PS->GetWLAbilitySystem() : nullptr;
	const bool bLowHP = ASC && ASC->GetMaxHealthInt() > 0 && ASC->GetCurrentHealthInt() <= ASC->GetMaxHealthInt() * 0.5f;

	if (IsSummoned())
	{
		Stamina -= (bFighting ? Defs->StaminaDrainFighting : Defs->StaminaDrainIdle) * Dt;
		if (Stamina <= 0.f)
		{
			Stamina = 0.f;
			bExhausted = true;
			RegroupAt = 0.0;   // a rest, not an HP scatter
			Dismiss(FText::Format(LOCTEXT("Exhausted", "Your {0} is exhausted and slinks off to rest."), GetBeastName().ToLower()));
		}
	}
	else
	{
		Stamina = FMath::Min(Defs->MaxStamina, Stamina + Defs->StaminaRegen * (bLowHP ? 2.f : 1.f) * Dt);
		// An exhausted beast returns on its own once rested (a manual dismiss stays down).
		if (bExhausted && Stamina >= Defs->MaxStamina * Defs->RejoinStaminaFraction)
		{
			bExhausted = false;
			Summon();
			if (IsSummoned())
			{
				Msg(FText::Format(LOCTEXT("Rested", "Your {0} is rested and rejoins you."), GetBeastName().ToLower()));
			}
		}
	}
}

void UWLCompanionComponent::SyncCarryPerk()
{
	AWLPlayerState* PS = GetPS();
	UWLInventoryComponent* Inv = PS ? PS->GetInventory() : nullptr;
	if (!Inv)
	{
		return;
	}
	const FWLCompanionSpecies* S = GetSpecies();
	const int32 Want = IsSummoned() && S ? S->SafeCarrySlots(GetStage()) : 0;
	const int32 Current = Inv->GetBonusSlots();
	if (Current == Want)
	{
		AppliedCarrySlots = Want;
		return;
	}
	const bool bGained = Want > Current;
	Inv->SetBonusSlots(Want);
	AppliedCarrySlots = Want;
	if (Want > 0 && bGained)
	{
		Msg(FText::Format(LOCTEXT("CarryOn", "Your {0} shoulders your extra gear (+{1} pack slots)."), GetBeastName().ToLower(), Want));
	}
	else if (Want == 0)
	{
		Msg(LOCTEXT("CarryOff", "Your saddlebag slots are carried off with your beast."));
	}
}

// ------------------------------------------------------------------ summon / dismiss

void UWLCompanionComponent::Summon()
{
	if (!GetOwner() || !GetOwner()->HasAuthority() || IsSummoned())
	{
		return;
	}
	if (Now() < RegroupAt)
	{
		Msg(LOCTEXT("Regrouping", "Your beast is regrouping."));
		return;
	}
	AWLPlayerState* PS = GetPS();
	if (!PS || !GetOwnerPawn())
	{
		return;
	}
	if (!HasCompanion())
	{
		Msg(bIncubating
			? LOCTEXT("EggNotHatched", "Your egg hasn't hatched yet — keep it warm in your pack.")
			: LOCTEXT("NoBeast", "You have no beast companion. Ask Roxy about her Beastmastery lesson."));
		return;
	}
	if (Stamina < GetDefs()->MinSummonStamina)
	{
		Msg(LOCTEXT("TooTired", "Your beast is too worn out — let it rest a moment."));
		return;
	}
	const FWLCompanionSpecies* S = GetSpecies();
	if (!S)
	{
		return;
	}

	SpawnPawn();
	if (!Pawn)
	{
		return;
	}
	MaxHP = UWLCompanionDefinitions::MaxHPFor(BeastLevel(), S->HPMult);
	HP = MaxHP;
	bAlive = true;
	TicksSinceAttack = S->AttackEveryTicks;
	Msg(FText::Format(LOCTEXT("Summoned", "Your {0} falls in at your side!"), GetBeastName().ToLower()));
	const float Mult = GetMarksmanshipDamageMultiplier();
	if (Mult > 1.f)
	{
		Msg(FText::Format(LOCTEXT("Spotter", "Its keen eyes mark your targets (+{0}% ranged damage)."), FMath::RoundToInt((Mult - 1.f) * 100.f)));
	}
	SyncCarryPerk();
	RefreshStatus();
}

void UWLCompanionComponent::Dismiss(const FText& Message)
{
	bAlive = false;
	DestroyPawn();
	if (!Message.IsEmpty())
	{
		Msg(Message);
	}
	SyncCarryPerk();
	RefreshStatus();
}

void UWLCompanionComponent::Toggle()
{
	if (bAlive)
	{
		bExhausted = false;
		Dismiss(LOCTEXT("WhistleDown", "You whistle your beast down."));
	}
	else
	{
		Summon();
	}
}

void UWLCompanionComponent::SpawnPawn()
{
	APawn* OwnerPawn = GetOwnerPawn();
	UWorld* World = GetWorld();
	const FWLCompanionSpecies* S = GetSpecies();
	if (!OwnerPawn || !World || !S)
	{
		return;
	}
	DestroyPawn();
	const UWLCompanionDefinitions* Defs = GetDefs();
	const FVector Loc = OwnerPawn->GetActorLocation() + FVector(Defs->FollowOffset.X, Defs->FollowOffset.Y, 0.f);

	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AdjustIfPossibleButAlwaysSpawn;
	UClass* Class = PawnClass ? PawnClass.Get() : AWLCompanionPawn::StaticClass();
	Pawn = World->SpawnActor<AWLCompanionPawn>(Class, Loc, OwnerPawn->GetActorRotation(), Params);
	if (Pawn)
	{
		Pawn->OwnerPlayerState = GetPS();
		const int32 Stage = GetStage();
		Pawn->SetAppearance(S->Key, Stage, S->SafeStageName(Stage), S->SafeScale(Stage), S->HoverHeight);
	}
}

void UWLCompanionComponent::DestroyPawn()
{
	if (Pawn)
	{
		Pawn->Destroy();
		Pawn = nullptr;
	}
}

float UWLCompanionComponent::GetMarksmanshipDamageMultiplier() const
{
	const FWLCompanionSpecies* S = GetSpecies();
	return IsSummoned() && S ? UWLCompanionDefinitions::MarksmanshipMultFor(*S, GetStage()) : 1.f;
}

bool UWLCompanionComponent::ChooseStarterBeast(FName SpeciesKey)
{
	AWLPlayerState* PS = GetPS();
	UWLPlayerFlagsComponent* Flags = PS ? PS->GetFlags() : nullptr;
	const FWLCompanionSpecies* S = GetDefs()->FindSpecies(SpeciesKey);
	// Only offered by Roxy's Beastmastery lesson, and only once.
	if (!Flags || !S || Flags->HasFlag(WLQuestFlags::EggChosen) || !Flags->HasFlag(WLQuestFlags::RoxyBeastGiven))
	{
		return false;
	}

	// Live baby, right now: no egg, no incubation.
	Flags->SetFlag(WLQuestFlags::EggChosen);
	Flags->SetFlag(WLQuestFlags::PupHatched);
	Flags->SetFlag(WLQuestFlags::Species(S->Key.ToString()));
	SetSpecies(S->Key);
	Stamina = GetDefs()->MaxStamina;
	bIncubating = false;
	Incubation = 0.f;

	Summon();
	Msg(FText::Format(LOCTEXT("Chosen", "A {0} bounds to your side!"), S->ChoiceTitle.ToLower()));
	RefreshStatus();
	return true;
}

void UWLCompanionComponent::DevCycleBeast()
{
	AWLPlayerState* PS = GetPS();
	if (!PS || !PS->GetFlags() || !PS->GetSkills())
	{
		return;
	}
#if UE_BUILD_SHIPPING
	if (!PS->HasGMRights())
	{
		return;
	}
#endif
	PS->GetFlags()->SetFlag(WLQuestFlags::PupHatched);
	PS->GetFlags()->SetFlag(WLQuestFlags::Species(TEXT("wolf")));
	SetSpecies(TEXT("wolf"));

	const int32 Level = BeastLevel();
	const int32 Target = !IsSummoned() ? FMath::Max(1, Level)   // first press: summon at the current stage
		: Level < 30 ? 30                                          // -> adolescent
		: Level < 60 ? 60                                          // -> adult
		: 1;                                                       // -> back to pup
	PS->GetSkills()->SetXP(EWLSkill::Beastmastery, FWLXPTable::XPForLevel(EWLSkill::Beastmastery, Target));

	Stamina = GetDefs()->MaxStamina;
	RegroupAt = 0.0;
	bAlive = false;
	DestroyPawn();
	Summon();
	Msg(FText::Format(LOCTEXT("DevCycle", "[DEBUG] Dog at Beastmastery {0} — {1}."), Target, GetBeastName()));
}

void UWLCompanionComponent::ServerToggleSummon_Implementation()
{
	Toggle();
}

void UWLCompanionComponent::ServerTriggerSpecial_Implementation()
{
	TriggerSpecial();
}

void UWLCompanionComponent::ServerChooseStarterBeast_Implementation(FName SpeciesKey)
{
	ChooseStarterBeast(SpeciesKey);
}

// ------------------------------------------------------------------ status

void UWLCompanionComponent::RefreshStatus()
{
	if (!GetOwner() || !GetOwner()->HasAuthority())
	{
		return;
	}
	const UWLCompanionDefinitions* Defs = GetDefs();
	const FWLCompanionSpecies* S = GetSpecies();
	const double T = Now();

	FWLCompanionStatus New;
	New.Species = S ? S->Key : NAME_None;
	New.bHasCompanion = HasCompanion();
	New.bActive = IsSummoned();
	New.bIncubating = bIncubating;
	New.bScattered = !bAlive && RegroupAt > 0.0;
	New.IncubationPercent = static_cast<uint8>(FMath::Clamp(FMath::FloorToInt(Incubation / FMath::Max(1.f, Defs->IncubateSeconds) * 100.f), 0, 100));
	New.StaminaPercent = static_cast<uint8>(FMath::Clamp(FMath::FloorToInt(Stamina / FMath::Max(1.f, Defs->MaxStamina) * 100.f), 0, 100));
	New.HP = HP;
	New.MaxHP = MaxHP;
	New.Stage = GetStage();
	New.SpecialCooldown = FMath::Max(0, FMath::CeilToInt(SpecialReadyAt - T));
	New.RegroupIn = New.bScattered ? FMath::Max(0, FMath::CeilToInt(RegroupAt - T)) : 0;

	if (New != Status)
	{
		Status = New;
		OnCompanionChanged.Broadcast();
	}
}

void UWLCompanionComponent::OnRep_Status()
{
	OnCompanionChanged.Broadcast();
}

bool UWLCompanionComponent::GetStatusLine(FText& OutText, float& OutFill) const
{
	const FWLCompanionStatus& St = Status;
	// A standing reminder whenever you OWN a beast that isn't out: it's not in your inventory, so
	// otherwise there's no cue you have a companion at all.
	if (!(St.bActive || St.bScattered || St.bIncubating || St.bHasCompanion))
	{
		return false;
	}
	const FWLCompanionSpecies* S = GetDefs()->FindSpecies(St.Species);
	const FText Name = S ? S->SafeStageName(St.Stage).ToUpper() : LOCTEXT("BeastUpper", "BEAST");

	if (St.bIncubating)
	{
		OutFill = St.IncubationPercent / 100.f;
		OutText = FText::Format(LOCTEXT("StatusEgg", "EGG INCUBATING... {0}%"), St.IncubationPercent);
	}
	else if (St.bActive)
	{
		OutFill = St.MaxHP > 0 ? float(St.HP) / St.MaxHP : 0.f;
		const FText SpecialName = S ? S->SpecialName : FText::GetEmpty();
		const FText Special = St.SpecialCooldown <= 0
			? FText::Format(LOCTEXT("SpecialReady", "{0} READY (H)"), SpecialName.ToUpper())
			: FText::Format(LOCTEXT("SpecialCd", "{0} {1}s"), SpecialName, St.SpecialCooldown);
		OutText = FText::Format(LOCTEXT("StatusActive", "{0}  {1}/{2}   {3}"), Name, St.HP, St.MaxHP, Special);
	}
	else if (St.bScattered)
	{
		OutFill = 0.f;
		OutText = FText::Format(LOCTEXT("StatusFled", "Beast fled — returns {0}s"), St.RegroupIn);
	}
	else
	{
		OutFill = St.StaminaPercent / 100.f;
		const bool bRested = St.StaminaPercent >= 20;
		OutText = bRested
			? FText::Format(LOCTEXT("StatusResting", "{0} resting — tap to summon (B)"), Name)
			: FText::Format(LOCTEXT("StatusWorn", "{0} worn out — {1}% rested"), Name, St.StaminaPercent);
	}
	return true;
}

// ------------------------------------------------------------------ save

void UWLCompanionComponent::CaptureSave(FWLCharacterSave& Out) const
{
	Out.Companions.Reset();
	if (!HasCompanion())
	{
		return;
	}
	const FWLCompanionSpecies* S = GetSpecies();
	FWLSaveCompanion& C = Out.Companions.AddDefaulted_GetRef();
	C.CompanionType = S ? S->Key : GetDefs()->DefaultSpecies;
	C.Level = BeastLevel();   // snapshot: the beast's level IS Beastmastery
	C.XP = 0;
	C.bSummoned = IsSummoned();
}

void UWLCompanionComponent::ApplySave(const FWLCharacterSave& In)
{
	// Runs after Flags->ApplySave, so legacy saves can still be read from species_* flags.
	Species = NAME_None;
	bResummonOnSpawn = false;
	if (In.Companions.Num() > 0 && !In.Companions[0].CompanionType.IsNone())
	{
		Species = In.Companions[0].CompanionType;
		bResummonOnSpawn = In.Companions[0].bSummoned;
	}
	if (Species.IsNone())
	{
		if (const FWLCompanionSpecies* S = GetSpecies())
		{
			Species = S->Key;
		}
	}
	Stamina = GetDefs()->MaxStamina;
	RefreshStatus();
}

#undef LOCTEXT_NAMESPACE
