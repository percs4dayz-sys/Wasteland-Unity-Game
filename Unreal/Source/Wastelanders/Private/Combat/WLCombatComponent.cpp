#include "Combat/WLCombatComponent.h"
#include "Combat/WLEnemyCharacter.h"
#include "Combat/WLCombatFeedbackSubsystem.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "AbilitySystem/WLGA_BasicAttack.h"
#include "AbilitySystem/WLGA_SpecialAttack.h"
#include "AbilitySystem/WLGA_Reload.h"
#include "Skills/WLSkillsComponent.h"
#include "Items/WLEquipmentComponent.h"
#include "Companions/WLCompanionComponent.h"
#include "Skilling/WLSkillingComponent.h"
#include "Data/WLDataTypes.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Player/WLCharacterMovementComponent.h"
#include "Core/WLGameMode.h"
#include "GameFramework/GameStateBase.h"
#include "EngineUtils.h"
#include "Net/UnrealNetwork.h"

#define LOCTEXT_NAMESPACE "WLCombat"

namespace
{
	/** Refined fission cores (gauntlet ammo): FissionItems.RefinedCore(1..5) = 410..414. */
	constexpr int32 RefinedCoreFirst = 410;
	constexpr int32 RefinedCoreLast = 414;
}

UWLCombatComponent::UWLCombatComponent()
{
	SetIsReplicatedByDefault(true);
	BasicAttackAbility = UWLGA_BasicAttack::StaticClass();
	SpecialAttackAbility = UWLGA_SpecialAttack::StaticClass();
	ReloadAbility = UWLGA_Reload::StaticClass();
}

void UWLCombatComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	// Style/stance/target are visible to others (aiming pose, who is fighting what).
	DOREPLIFETIME(UWLCombatComponent, Style);
	DOREPLIFETIME(UWLCombatComponent, Stance);
	DOREPLIFETIME(UWLCombatComponent, Target);
	// The magazine HUD is the owner's business only.
	DOREPLIFETIME_CONDITION(UWLCombatComponent, bRepUsesMagazine, COND_OwnerOnly);
	DOREPLIFETIME_CONDITION(UWLCombatComponent, RepMagLoaded, COND_OwnerOnly);
	DOREPLIFETIME_CONDITION(UWLCombatComponent, RepMagSize, COND_OwnerOnly);
	DOREPLIFETIME_CONDITION(UWLCombatComponent, bRepReloading, COND_OwnerOnly);
	DOREPLIFETIME_CONDITION(UWLCombatComponent, ReloadStartServerTime, COND_OwnerOnly);
	DOREPLIFETIME_CONDITION(UWLCombatComponent, ReloadEndServerTime, COND_OwnerOnly);
}

void UWLCombatComponent::BeginPlay()
{
	Super::BeginPlay();

	if (AWLPlayerCharacter* Character = GetPlayerCharacter())
	{
		Character->OnAbilitySystemReady.AddUObject(this, &UWLCombatComponent::HandleAbilitySystemReady);
		if (Character->IsAbilitySystemReady())
		{
			HandleAbilitySystemReady(Character);
		}
	}

	if (GetOwner() && GetOwner()->HasAuthority())
	{
		Rng.Initialize(FMath::Rand());
		SpawnedAt = Now();
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			Tick->Register(TScriptInterface<IWLTickable>(this));
			bRegisteredTick = true;
		}
		RefreshStyleFromEquipment();
	}
}

void UWLCombatComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (bRegisteredTick)
	{
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			Tick->Unregister(TScriptInterface<IWLTickable>(this));
		}
		bRegisteredTick = false;
	}
	if (UWLAbilitySystemComponent* ASC = BoundASC.Get())
	{
		ASC->OnDeathStateChanged.RemoveAll(this);
	}
	if (AWLPlayerCharacter* Character = GetPlayerCharacter())
	{
		Character->OnAbilitySystemReady.RemoveAll(this);
	}
	Super::EndPlay(EndPlayReason);
}

// --- owner helpers -----------------------------------------------------------------------------------

AWLPlayerCharacter* UWLCombatComponent::GetPlayerCharacter() const { return Cast<AWLPlayerCharacter>(GetOwner()); }

AWLPlayerState* UWLCombatComponent::GetWLPlayerState() const
{
	const AWLPlayerCharacter* Character = GetPlayerCharacter();
	return Character ? Character->GetWLPlayerState() : nullptr;
}

UWLAbilitySystemComponent* UWLCombatComponent::GetASC() const
{
	const AWLPlayerState* PS = GetWLPlayerState();
	return PS ? PS->GetWLAbilitySystem() : nullptr;
}

UWLEquipmentComponent* UWLCombatComponent::GetEquipment() const
{
	const AWLPlayerState* PS = GetWLPlayerState();
	return PS ? PS->GetEquipment() : nullptr;
}

UWLSkillsComponent* UWLCombatComponent::GetSkills() const
{
	const AWLPlayerState* PS = GetWLPlayerState();
	return PS ? PS->GetSkills() : nullptr;
}

const FWLItemRow* UWLCombatComponent::GetWeapon() const
{
	const UWLEquipmentComponent* Eq = GetEquipment();
	return Eq ? Eq->GetItem(EWLEquipSlot::Weapon) : nullptr;
}

EWLWeaponStyle UWLCombatComponent::EquippedWeaponStyle() const
{
	// Bare hands count as melee.
	const FWLItemRow* W = GetWeapon();
	return (W && W->WeaponStyle != EWLWeaponStyle::None) ? W->WeaponStyle : EWLWeaponStyle::Melee;
}

bool UWLCombatComponent::IsRangedEquipped() const
{
	// Guns AND gauntlets: both fire from range and feed from the Ammo slot.
	const EWLWeaponStyle S = EquippedWeaponStyle();
	return S == EWLWeaponStyle::Ranged || S == EWLWeaponStyle::Fission;
}

bool UWLCombatComponent::IsFissionEquipped() const { return EquippedWeaponStyle() == EWLWeaponStyle::Fission; }
bool UWLCombatComponent::IsGunEquipped() const { return EquippedWeaponStyle() == EWLWeaponStyle::Ranged; }

bool UWLCombatComponent::IsDead() const
{
	const UWLAbilitySystemComponent* ASC = GetASC();
	return !ASC || ASC->IsDead();
}

int32 UWLCombatComponent::Level(EWLSkill Skill) const
{
	const UWLSkillsComponent* Skills = GetSkills();
	return Skills ? Skills->GetLevel(Skill) : FWLXPTable::MinLevelFor(Skill);
}

int64 UWLCombatComponent::CurrentTick() const
{
	const UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this);
	return Tick ? Tick->GetTickCount() : 0;
}

double UWLCombatComponent::Now() const
{
	return GetWorld() ? GetWorld()->GetTimeSeconds() : 0.0;
}

uint64 UWLCombatComponent::KeyFor(const AActor* Actor) const
{
	return Actor ? static_cast<uint64>(Actor->GetUniqueID()) : 0;
}

float UWLCombatComponent::ReachUnits() const
{
	const FWLItemRow* W = GetWeapon();
	return FWLCombatMath::ReachUnits(W ? W->AttackRange : 1, IsRangedEquipped(), Stance, MeleeReach, ReachPerTile);
}

int32 UWLCombatComponent::CurrentSpeedTicks() const
{
	const FWLItemRow* W = GetWeapon();
	return FWLCombatMath::AttackSpeedTicks(W ? W->AttackSpeedTicks : 0, Stance);
}

bool UWLCombatComponent::IsValidEnemy(const AActor* Actor) const
{
	const AWLEnemyCharacter* Enemy = Cast<AWLEnemyCharacter>(Actor);
	return IsValid(Enemy) && !Enemy->IsDead();
}

bool UWLCombatComponent::TooFar(const AActor* Actor) const
{
	const AActor* Owner = GetOwner();
	return !Actor || !Owner || FVector::Dist2D(Actor->GetActorLocation(), Owner->GetActorLocation()) > LeashRange;
}

void UWLCombatComponent::Message(const FText& Text) const
{
	const AWLPlayerCharacter* Character = GetPlayerCharacter();
	if (AWLPlayerController* PC = Character ? Cast<AWLPlayerController>(Character->GetController()) : nullptr)
	{
		PC->ClientGameMessage(Text);
	}
}

// --- ability system wiring -----------------------------------------------------------------------------

void UWLCombatComponent::HandleAbilitySystemReady(AWLPlayerCharacter* Character)
{
	UWLAbilitySystemComponent* ASC = GetASC();
	if (!ASC)
	{
		return;
	}
	if (BoundASC.Get() != ASC)
	{
		if (UWLAbilitySystemComponent* Old = BoundASC.Get())
		{
			Old->OnDeathStateChanged.RemoveAll(this);
		}
		ASC->OnDeathStateChanged.AddUObject(this, &UWLCombatComponent::HandleDeathStateChanged);
		BoundASC = ASC;
	}
	if (ASC->IsOwnerActorAuthoritative())
	{
		// The ASC lives on the PlayerState and outlives this pawn, so grants are idempotent.
		BasicAttackHandle = ASC->GiveAbilityOnce(BasicAttackAbility);
		SpecialAttackHandle = ASC->GiveAbilityOnce(SpecialAttackAbility);
		ReloadHandle = ASC->GiveAbilityOnce(ReloadAbility);
		RefreshStyleFromEquipment();
	}
	HandleDeathStateChanged(ASC->IsDead());
}

void UWLCombatComponent::HandleDeathStateChanged(bool bDead)
{
	AWLPlayerCharacter* Character = GetPlayerCharacter();
	UCharacterMovementComponent* Move = Character ? Character->GetCharacterMovement() : nullptr;
	if (bDead)
	{
		// Freeze the corpse while the death plays out (PlayerRespawn disables the controller).
		if (Move)
		{
			Move->StopMovementImmediately();
			Move->DisableMovement();
		}
		if (GetOwner() && GetOwner()->HasAuthority())
		{
			Disengage();
			Modules.ResetCombat();
			AbortReload();
		}
	}
	else if (Move && Move->MovementMode == MOVE_None)
	{
		Move->SetMovementMode(MOVE_Walking);
	}
}

bool UWLCombatComponent::ActivateOrRun(const FGameplayAbilitySpecHandle& Handle, TFunctionRef<bool()> Fallback)
{
	UWLAbilitySystemComponent* ASC = GetASC();
	if (ASC && Handle.IsValid() && ASC->FindAbilitySpecFromHandle(Handle))
	{
		return ASC->TryActivateAbility(Handle);
	}
	return Fallback();
}

// --- style / stance ------------------------------------------------------------------------------------

void UWLCombatComponent::RefreshStyleFromEquipment()
{
	if (!GetOwner() || !GetOwner()->HasAuthority())
	{
		return;
	}
	const EWLCombatStyle NewStyle = FWLCombatMath::StyleForWeapon(EquippedWeaponStyle());
	// Clamp: a stance the new style doesn't show is a stance you can't get out of.
	const EWLCombatStance NewStance = FWLCombatMath::ClampStance(NewStyle, Stance);
	const bool bChanged = NewStyle != Style || NewStance != Stance;
	Style = NewStyle;
	Stance = NewStance;

	// Swapped the gun away mid-reload: the reload is lost (no free background reloads).
	const FWLItemRow* W = GetWeapon();
	if (bRepReloading && (!W || W->ItemId != ReloadWeaponId))
	{
		if (UWLAbilitySystemComponent* ASC = GetASC())
		{
			if (ReloadHandle.IsValid())
			{
				ASC->CancelAbilityHandle(ReloadHandle);
			}
		}
		AbortReload();
	}

	SyncModules();
	UpdateMagazineReplication();
	if (bChanged)
	{
		OnStyleChanged.Broadcast();
	}
}

void UWLCombatComponent::ServerSetStance_Implementation(EWLCombatStance NewStance)
{
	if (!FWLCombatMath::IsStanceAllowed(Style, NewStance))
	{
		return;
	}
	Stance = NewStance;
	OnStyleChanged.Broadcast();
}

void UWLCombatComponent::OnRep_Style()
{
	OnStyleChanged.Broadcast();
}

FText UWLCombatComponent::StanceLabel(EWLCombatStance InStance)
{
	switch (InStance)
	{
	case EWLCombatStance::Accurate:   return LOCTEXT("Accurate", "Accurate");
	case EWLCombatStance::Aggressive: return LOCTEXT("Aggressive", "Aggressive");
	case EWLCombatStance::Defensive:  return LOCTEXT("Defensive", "Defensive");
	case EWLCombatStance::Rapid:      return LOCTEXT("Rapid", "Rapid");
	case EWLCombatStance::Longrange:  return LOCTEXT("Distance", "Distance");
	default:                          return LOCTEXT("Controlled", "Controlled");
	}
}

FText UWLCombatComponent::StanceDescription(EWLCombatStance InStance, EWLCombatStyle InStyle)
{
	// CombatManager.StanceTrains, with the OSRS skill names the game uses now.
	if (InStyle == EWLCombatStyle::Fission)
	{
		return LOCTEXT("FissionTrains", "better accuracy · trains Fission");
	}
	if (InStyle == EWLCombatStyle::Ranged)
	{
		switch (InStance)
		{
		case EWLCombatStance::Rapid:     return LOCTEXT("RapidTrains", "faster fire rate · trains Marksmanship");
		case EWLCombatStance::Longrange: return LOCTEXT("LongTrains", "longer range, better defence · trains Marksmanship");
		default:                         return LOCTEXT("AccRTrains", "better accuracy · trains Marksmanship");
		}
	}
	switch (InStance)
	{
	case EWLCombatStance::Aggressive: return LOCTEXT("AggTrains", "bigger hits, slower · trains Strength");
	case EWLCombatStance::Defensive:  return LOCTEXT("DefTrains", "tougher defence, reduced damage · trains Defence");
	default:                          return LOCTEXT("AccMTrains", "better accuracy · trains Attack");
	}
}

// --- modules -------------------------------------------------------------------------------------------

void UWLCombatComponent::SyncModules()
{
	const UWLEquipmentComponent* Eq = GetEquipment();
	if (!Eq)
	{
		return;
	}
	FWLModulePowers Powers;
	for (EWLModuleEffect Effect : TEnumRange<EWLModuleEffect>())
	{
		Powers.Set(Effect, Eq->ModulePower(WLModules::EffectName(Effect)));
	}
	Modules.SetPowers(Powers);

	// Gear signature: worn items + their modules, ignoring Ammo/Tool (spending ammo is also a change).
	uint32 Signature = 0;
	for (EWLEquipSlot Slot : TEnumRange<EWLEquipSlot>())
	{
		if (Slot == EWLEquipSlot::Ammo || Slot == EWLEquipSlot::Tool)
		{
			continue;
		}
		Signature = HashCombine(Signature, GetTypeHash(Eq->GetItemId(Slot)));
		for (const int32 ModuleId : Eq->GetModules(Slot))
		{
			Signature = HashCombine(Signature, GetTypeHash(ModuleId));
		}
	}
	Modules.NotifyGear(Signature);
}

// --- targeting -----------------------------------------------------------------------------------------

void UWLCombatComponent::SetTarget(AActor* NewTarget)
{
	if (GetOwner() && GetOwner()->HasAuthority())
	{
		if (NewTarget)
		{
			Engage(NewTarget);
		}
		else
		{
			Disengage();
		}
		return;
	}
	ServerSetTarget(NewTarget);
}

void UWLCombatComponent::ServerSetTarget_Implementation(AActor* NewTarget)
{
	// Engage re-validates (alive, enemy, in range); a bad target is simply ignored.
	if (NewTarget)
	{
		Engage(NewTarget);
	}
	else
	{
		Disengage(LOCTEXT("BreakOff", "You break off the attack."));
	}
}

void UWLCombatComponent::SetTargetInternal(AActor* NewTarget)
{
	if (Target == NewTarget)
	{
		return;
	}
	Target = NewTarget;
	OnTargetChanged.Broadcast(Target);
}

void UWLCombatComponent::OnRep_Target()
{
	OnTargetChanged.Broadcast(Target);
}

bool UWLCombatComponent::Engage(AActor* InTarget)
{
	if (!GetOwner() || !GetOwner()->HasAuthority() || IsDead())
	{
		return false;
	}
	AWLEnemyCharacter* Enemy = Cast<AWLEnemyCharacter>(InTarget);
	if (!IsValidEnemy(Enemy) || TooFar(Enemy))
	{
		return false;
	}

	// Swinging at something ends the gather.
	if (AWLPlayerCharacter* Character = GetPlayerCharacter())
	{
		if (UWLSkillingComponent* Skilling = Character->GetSkilling())
		{
			if (Skilling->IsBusy())
			{
				Skilling->StopAction();
			}
		}
	}

	const bool bIsNew = Enemy != Target || !bSwingQueued;
	const bool bNewEnemy = Enemy != Target;
	SetTargetInternal(Enemy);
	bSwingQueued = true;
	LastApproachGoal = FVector(FLT_MAX);
	if (bIsNew && bNewEnemy)
	{
		bPendingFirstStrike = true;   // arm the Strength L99 opening hit
		StreakTarget.Reset();
		Streak = 0;
		Message(FText::Format(LOCTEXT("Attack", "You attack the {0}."), Enemy->GetEnemyDisplayName()));
	}
	return true;
}

void UWLCombatComponent::Disengage(const FText& InMessage)
{
	const bool bHadFight = Target != nullptr || bSwingQueued;
	SetTargetInternal(nullptr);
	bSwingQueued = false;
	StreakTarget.Reset();
	Streak = 0;
	bPendingFirstStrike = false;
	if (LastApproachGoal.X != FLT_MAX)
	{
		LastApproachGoal = FVector(FLT_MAX);
		ClientStopApproach();
	}
	if (bHadFight && !InMessage.IsEmpty())
	{
		Message(InMessage);
	}
}

void UWLCombatComponent::AttackNearest()
{
	if (GetOwner() && GetOwner()->HasAuthority())
	{
		ServerAttackNearest_Implementation();
	}
	else
	{
		ServerAttackNearest();
	}
}

void UWLCombatComponent::ServerAttackNearest_Implementation()
{
	if (IsDead())
	{
		return;
	}
	AWLEnemyCharacter* Best = NearestEnemy(AcquireRange);
	if (!Best)
	{
		Message(LOCTEXT("NothingNearby", "Nothing to attack nearby."));
		return;
	}
	Engage(Best);
}

AWLEnemyCharacter* UWLCombatComponent::NearestEnemy(float Range) const
{
	const AActor* Owner = GetOwner();
	AWLEnemyCharacter* Best = nullptr;
	float BestDistSq = Range * Range;
	for (TActorIterator<AWLEnemyCharacter> It(GetWorld()); It; ++It)
	{
		if (It->IsDead())
		{
			continue;
		}
		const float D = FVector::DistSquared(It->GetActorLocation(), Owner->GetActorLocation());
		if (D < BestDistSq)
		{
			BestDistSq = D;
			Best = *It;
		}
	}
	return Best;
}

AWLEnemyCharacter* UWLCombatComponent::BestTargetInArc(const FVector& AimPoint, float Range) const
{
	const AActor* Owner = GetOwner();
	FVector Dir = AimPoint - Owner->GetActorLocation();
	Dir.Z = 0.f;
	if (Dir.SizeSquared() < 1.f)
	{
		Dir = Owner->GetActorForwardVector();
	}
	Dir = Dir.GetSafeNormal2D();

	AWLEnemyCharacter* Best = nullptr;
	float BestDistSq = Range * Range;
	for (TActorIterator<AWLEnemyCharacter> It(GetWorld()); It; ++It)
	{
		if (It->IsDead())
		{
			continue;   // dummies stay valid: shift-swinging at the training dummy works
		}
		FVector To = It->GetActorLocation() - Owner->GetActorLocation();
		To.Z = 0.f;
		const float D = To.SizeSquared();
		if (D > BestDistSq)
		{
			continue;
		}
		if (D > 1.f && FVector::DotProduct(Dir, To.GetSafeNormal()) < 0.64f)
		{
			continue;   // about +-50 degrees: a swing, not a snipe
		}
		BestDistSq = D;
		Best = *It;
	}
	return Best;
}

void UWLCombatComponent::AttackInPlace(FVector AimPoint)
{
	if (GetOwner() && GetOwner()->HasAuthority())
	{
		ServerAttackInPlace_Implementation(AimPoint);
	}
	else
	{
		ServerAttackInPlace(AimPoint);
	}
}

void UWLCombatComponent::ServerAttackInPlace_Implementation(FVector_NetQuantize AimPoint)
{
	if (IsDead() || !GetOwner())
	{
		return;
	}
	// Plant your feet: cancel any walk-to-attack and any gathering.
	bSwingQueued = false;
	if (AWLPlayerCharacter* Character = GetPlayerCharacter())
	{
		if (UWLSkillingComponent* Skilling = Character->GetSkilling())
		{
			if (Skilling->IsBusy())
			{
				Skilling->StopAction();
			}
		}
	}
	if (LastApproachGoal.X != FLT_MAX)
	{
		LastApproachGoal = FVector(FLT_MAX);
		ClientStopApproach();
	}
	FaceToward(AimPoint);

	const int64 Tick = CurrentTick();
	if (Tick < ReadyAtTick)
	{
		return;
	}
	const bool bRanged = IsRangedEquipped();
	if (bRanged && !CheckRangedGates(true, false))
	{
		return;
	}

	if (AWLEnemyCharacter* Enemy = BestTargetInArc(AimPoint, ReachUnits()))
	{
		SetTargetInternal(Enemy);
		ResolveHit(Enemy, bRanged);
	}
	else
	{
		MulticastAttackPerformed(bRanged);   // clean whiff: swing the weapon anyway
	}
	if (IsGunEquipped())
	{
		const FWLItemRow* W = GetWeapon();
		if (W && Magazine.NoteShotFired(W->ItemId, FWLCombatMath::MagazineSize(W->MagazineSize)))
		{
			RequestReload();
		}
		UpdateMagazineReplication();
	}
	StartCooldown(Tick);
}

// --- the tick loop -------------------------------------------------------------------------------------

void UWLCombatComponent::OnGameTick(int64 TickCount)
{
	TickBleeds();
	if (IsDead())
	{
		OutOfBoundsTimer = 0.f;
		return;
	}
	TickVoidRescue();
	TickQueuedSwing(TickCount);
}

bool UWLCombatComponent::HasGroundBelow() const
{
	const AActor* Owner = GetOwner();
	if (!Owner || !GetWorld())
	{
		return true;
	}
	// Any walkable surface (not a wall or an underside) within VoidFallDistance below the feet.
	const FVector Start = Owner->GetActorLocation() + FVector(0.f, 0.f, 200.f);
	const FVector End = Start - FVector(0.f, 0.f, VoidFallDistance + 200.f);
	FCollisionQueryParams Params(SCENE_QUERY_STAT(WLVoidRescue), false, Owner);
	TArray<FHitResult> Hits;
	GetWorld()->LineTraceMultiByChannel(Hits, Start, End, ECC_Visibility, Params);
	for (const FHitResult& Hit : Hits)
	{
		if (Hit.bBlockingHit && Hit.ImpactNormal.Z >= 0.2f)
		{
			return true;
		}
	}
	// LineTraceMulti only reports overlaps before the first block; check the block itself too.
	FHitResult Single;
	return GetWorld()->LineTraceSingleByChannel(Single, Start, End, ECC_Visibility, Params) && Single.ImpactNormal.Z >= 0.2f;
}

void UWLCombatComponent::TickVoidRescue()
{
	if (OutOfBoundsDelay <= 0.f)
	{
		return;
	}
	if (HasGroundBelow())
	{
		OutOfBoundsTimer = 0.f;
		return;
	}
	const UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this);
	OutOfBoundsTimer += Tick ? Tick->GetTickDuration() : 0.6f;

	// Just logged in: a bad saved position is visible at once; don't let them plummet for 5 s first.
	const bool bJustLoaded = Now() - SpawnedAt < 4.0;
	if (OutOfBoundsTimer < (bJustLoaded ? 0.f : OutOfBoundsDelay))
	{
		return;
	}
	OutOfBoundsTimer = 0.f;

	AWLPlayerCharacter* Character = GetPlayerCharacter();
	AWLGameMode* GM = GetWorld()->GetAuthGameMode<AWLGameMode>();
	if (!Character || !GM || !GM->TeleportToSpawn(Character->GetController()))
	{
		return;
	}
	UE_LOG(LogTemp, Warning, TEXT("%s: no ground within %.0f uu below; returned to spawn."), *GetNameSafe(Character), VoidFallDistance);
	Disengage();
	Message(bJustLoaded
		? LOCTEXT("VoidLoad", "Your saved position was outside the world — returned to spawn.")
		: LOCTEXT("VoidFall", "You drifted out of bounds — returned to spawn."));
}

void UWLCombatComponent::TickQueuedSwing(int64 TickCount)
{
	AActor* Current = Target;
	if (!Current)
	{
		return;
	}
	// Combat focus fades when the fight is over or you've left it behind.
	if (!IsValidEnemy(Current))
	{
		Disengage();
		return;
	}
	if (TooFar(Current))
	{
		Disengage(bSwingQueued ? LOCTEXT("BreakOff", "You break off the attack.") : FText::GetEmpty());
		return;
	}
	if (!bSwingQueued)
	{
		return;
	}

	const float Reach = ReachUnits();
	const float Dist = FVector::Dist2D(Current->GetActorLocation(), GetOwner()->GetActorLocation());
	if (Dist > Reach)
	{
		// Out of reach: walk in. Re-send only when the target has moved, so WASD isn't fought every tick.
		const FVector Goal = Current->GetActorLocation();
		if (LastApproachGoal.X == FLT_MAX || FVector::Dist2D(Goal, LastApproachGoal) > 100.f)
		{
			LastApproachGoal = Goal;
			ClientApproachTarget(Goal, Reach * 0.9f);
		}
		return;
	}
	if (LastApproachGoal.X != FLT_MAX)
	{
		LastApproachGoal = FVector(FLT_MAX);
		ClientStopApproach();
	}
	if (TickCount < ReadyAtTick)
	{
		return;   // clicked mid-cooldown: the swing fires the moment it's up
	}
	ActivateOrRun(BasicAttackHandle, [this]() { return PerformBasicAttack(); });
}

void UWLCombatComponent::StartCooldown(int64 TickCount)
{
	const double Cooldown = CurrentSpeedTicks() * static_cast<double>(Modules.CooldownMultiplier(Now(), Rng));
	ReadyAtTick = FWLCombatMath::NextReadyTick(TickCount, ReadyAtTick, Cooldown);
}

void UWLCombatComponent::DelayAttackAfterEating(int32 Ticks)
{
	if (Target == nullptr && !bSwingQueued)
	{
		return;
	}
	const double Tick = static_cast<double>(CurrentTick());
	if (ReadyAtTick <= Tick)
	{
		return;   // weapon ready: eating adds no delay
	}
	ReadyAtTick = FMath::Max(ReadyAtTick, Tick + Modules.EatDelay(Ticks));
}

bool UWLCombatComponent::CheckRangedGates(bool bAutoReload, bool bClearQueueWhenEmpty)
{
	const UWLEquipmentComponent* Eq = GetEquipment();
	if (!Eq || Eq->GetItemId(EWLEquipSlot::Ammo) <= 0 || Eq->GetAmmoQuantity() <= 0)
	{
		Message(LOCTEXT("OutOfAmmo", "Out of ammo — load rounds into your Ammo slot."));
		if (bClearQueueWhenEmpty)
		{
			bSwingQueued = false;
		}
		return false;
	}
	if (IsGunEquipped())
	{
		if (bRepReloading)
		{
			return false;   // the queued shot fires when it's done
		}
		const FWLItemRow* W = GetWeapon();
		if (W && Magazine.GetLoaded(W->ItemId, FWLCombatMath::MagazineSize(W->MagazineSize)) <= 0)
		{
			if (bAutoReload)
			{
				RequestReload();
			}
			return false;
		}
	}
	return true;
}

bool UWLCombatComponent::PerformBasicAttack()
{
	AWLEnemyCharacter* Enemy = Cast<AWLEnemyCharacter>(Target);
	if (!GetOwner() || !GetOwner()->HasAuthority() || IsDead() || !IsValidEnemy(Enemy))
	{
		return false;
	}
	const bool bRanged = IsRangedEquipped();
	if (bRanged && !CheckRangedGates(true, true))
	{
		return false;
	}

	FaceToward(Enemy->GetActorLocation());
	ResolveHit(Enemy, bRanged);

	if (IsGunEquipped())
	{
		// Every trigger pull spends a magazine round; an empty mag auto-reloads.
		const FWLItemRow* W = GetWeapon();
		if (W && Magazine.NoteShotFired(W->ItemId, FWLCombatMath::MagazineSize(W->MagazineSize)))
		{
			RequestReload();
		}
		UpdateMagazineReplication();
	}
	StartCooldown(CurrentTick());
	if (!bAutoRepeat)
	{
		bSwingQueued = false;
	}
	return true;
}

// --- hit resolution (ActionCombat3D.ResolveHit) --------------------------------------------------------

void UWLCombatComponent::ResolveHit(AWLEnemyCharacter* Enemy, bool bRanged)
{
	MulticastAttackPerformed(bRanged);

	UWLEquipmentComponent* Eq = GetEquipment();
	UWLAbilitySystemComponent* ASC = GetASC();
	if (!Eq || !ASC)
	{
		return;
	}
	const double T = Now();
	const uint64 Key = KeyFor(Enemy);
	const bool bFission = IsFissionEquipped();
	const int32 Attack = Level(EWLSkill::Attack);
	const int32 Strength = Level(EWLSkill::Strength);
	const int32 Marks = Level(EWLSkill::Marksmanship);
	const int32 FissionLv = Level(EWLSkill::Fission);
	const EWLWeaponStyle HitStyle = bFission ? EWLWeaponStyle::Fission : bRanged ? EWLWeaponStyle::Ranged : EWLWeaponStyle::Melee;
	const EWLCombatStyle CombatStyle = FWLCombatMath::StyleForWeapon(HitStyle);

	// Accuracy: the style's own level + style-matched gear + module bonuses + stance.
	const int32 AtkLevel = bFission ? FissionLv : bRanged ? Marks : Attack;
	const int32 EquipAcc = Eq->TotalAttackBonusFor(HitStyle) + Modules.Accuracy(Key, bRanged, T);
	const int32 AtkRoll = FWLCombatMath::AttackRoll(AtkLevel, EquipAcc, FWLCombatMath::AccuracyStanceBonus(Stance));

	// Attack L60: armour bypass (melee only). Enemy defence is a level, no gear bonus.
	const bool bBypass = !bRanged && FWLCombatPerks::ArmorBypass(Attack, Rng);
	const int32 DefRoll = bBypass ? 0 : FWLCombatMath::DefenceRoll(Modules.TargetDefence(Key, Enemy->GetDefenceLevel(), T), 0, 0);

	const bool bHit = bBypass || FWLCombatMath::RollHit(FWLCombatMath::HitChance(AtkRoll, DefRoll), Rng);
	if (!bHit)
	{
		Modules.Miss(Key, T);
		if (bRanged)
		{
			ConsumeShot(bFission);
		}
		FWLCombatFeedback::Hitsplat(Enemy, 0, false);
		StreakTarget.Reset();   // a miss breaks the ranged consecutive-hit ramp
		Streak = 0;
		return;
	}

	int32 MaxHit;
	if (bFission)
	{
		// The loaded core's rating IS the max hit, flat: no level term, no gear term.
		MaxHit = FMath::Max(1, Eq->AmmoStrengthBonus());
	}
	else
	{
		const int32 StrLevel = bRanged ? Marks : Strength;
		int32 StrBonus = Eq->TotalStrengthBonus();
		if (bRanged)
		{
			StrBonus += Eq->AmmoStrengthBonus();   // OSRS: ammo supplies ranged strength
		}
		MaxHit = FWLCombatMath::MaxHit(StrLevel, StrBonus, FWLCombatMath::StrengthStanceBonus(Stance));
	}

	// Completed gear set: style-matched only (no Fission armour line yet).
	const float SetMul = Eq->GetSetDamageMultiplier(HitStyle);
	if (SetMul > 1.f)
	{
		MaxHit = FMath::RoundToInt(MaxHit * SetMul);
	}

	float DmgF = static_cast<float>(FMath::Max(1, FWLCombatMath::RollDamage(MaxHit, Rng)));
	FName Tag = NAME_None;

	if (bRanged && !bFission)
	{
		// Marksmanship milestones key off marks level, so they never fire for gauntlets.
		if (const AWLPlayerState* PS = GetWLPlayerState())
		{
			if (const UWLCompanionComponent* Companions = PS->GetCompanions())
			{
				DmgF *= Companions->GetMarksmanshipDamageMultiplier();   // Razorbeak spotting
			}
		}
		if (FWLCombatPerks::Headshot(Marks, Rng))
		{
			DmgF *= FWLCombatPerks::HeadshotMult;
			Tag = TEXT("HEADSHOT");
		}
		if (StreakTarget.Get() == Enemy)
		{
			++Streak;
		}
		else
		{
			StreakTarget = Enemy;
			Streak = 0;
		}
		DmgF *= FWLCombatPerks::ConsecutiveMultiplier(Marks, Streak);
	}
	else if (!bFission)
	{
		const float FirstStrike = FWLCombatPerks::FirstStrikeMultiplier(Strength, bPendingFirstStrike);
		if (FirstStrike > 1.f)
		{
			Tag = TEXT("FIRST STRIKE");
		}
		DmgF *= FirstStrike;
		if (FWLCombatPerks::PowerAttack(Strength, Rng))
		{
			DmgF *= FWLCombatPerks::PowerAttackMult;
			if (Tag.IsNone())
			{
				Tag = TEXT("POWER");
			}
		}
		DmgF *= FWLCombatPerks::BossMultiplier(Strength, Enemy->IsBoss());
	}
	else if (FWLCombatPerks::Meltdown(FissionLv, Rng))
	{
		// Not a multiplier on the roll: a straight 2x the core's max.
		DmgF = static_cast<float>(MaxHit * FWLCombatPerks::MeltdownMultiplier);
		Tag = TEXT("MELTDOWN");
	}
	bPendingFirstStrike = false;

	const FWLModuleOutgoing Out = Modules.Outgoing(Key, FMath::Max(1, FMath::RoundToInt(DmgF)), bRanged, Enemy->GetCurrentHP(),
		ASC->GetCurrentHealthInt(), ASC->GetMaxHealthInt(), T, Rng);
	const int32 Dmg = FMath::Min(Out.Damage, Enemy->GetCurrentHP());

	if (bRanged)
	{
		ConsumeShot(bFission);
	}
	DealDamage(Enemy, Dmg, Tag);
	AwardXP(Dmg, CombatStyle);

	if (Out.LeechHeal > 0)
	{
		ASC->ApplyHeal(Out.LeechHeal);
	}
	if (Out.BleedDamage > 0)
	{
		Modules.AddBleed(Key, Out.BleedDamage, Out.BleedMaxStacks);
		BleedTargets.Add(Key, Enemy);
	}

	// Strength L40 stagger / L80 cleave (melee only).
	if (!bRanged && FWLCombatPerks::Stagger(Strength, Rng))
	{
		Enemy->Stagger(FWLCombatPerks::StaggerSeconds);
	}
	if (!bRanged && FWLCombatPerks::Cleave(Strength, Rng))
	{
		Cleave(Enemy, FMath::Max(1, FMath::RoundToInt(Dmg * FWLCombatPerks::CleaveFraction)));
	}

	if (!Tag.IsNone())
	{
		Message(FText::Format(LOCTEXT("PerkTag", "{0}!"), FText::FromName(Tag)));
	}
	if (Enemy->IsDead())
	{
		Message(FText::Format(LOCTEXT("Defeated", "{0} is defeated."), Enemy->GetEnemyDisplayName()));
	}
}

void UWLCombatComponent::DealDamage(AWLEnemyCharacter* Victim, int32 Damage, FName Tag)
{
	UWLAbilitySystemComponent* EnemyASC = Victim ? Victim->GetWLAbilitySystem() : nullptr;
	if (!EnemyASC)
	{
		return;
	}
	// Instigator = our pawn: the enemy resolves it to our PlayerState for kill credit.
	const int32 Dealt = EnemyASC->ApplyDamage(Damage, GetOwner(), GetOwner());
	FWLCombatFeedback::Hitsplat(Victim, Dealt, true, Tag);
}

void UWLCombatComponent::Cleave(AWLEnemyCharacter* Primary, int32 Damage)
{
	const float R2 = FWLCombatPerks::CleaveRadius * FWLCombatPerks::CleaveRadius;
	TArray<AWLEnemyCharacter*> Hits;
	for (TActorIterator<AWLEnemyCharacter> It(GetWorld()); It; ++It)
	{
		if (*It == Primary || It->IsDead())
		{
			continue;
		}
		if (FVector::DistSquared(It->GetActorLocation(), Primary->GetActorLocation()) <= R2)
		{
			Hits.Add(*It);
		}
	}
	for (AWLEnemyCharacter* Enemy : Hits)
	{
		DealDamage(Enemy, Damage, NAME_None);
	}
}

void UWLCombatComponent::TickBleeds()
{
	if (BleedTargets.Num() == 0)
	{
		return;
	}
	Modules.TickBleeds([this](uint64 Key, int32 Damage)
	{
		const TWeakObjectPtr<AWLEnemyCharacter>* Weak = BleedTargets.Find(Key);
		AWLEnemyCharacter* Enemy = Weak ? Weak->Get() : nullptr;
		if (!IsValidEnemy(Enemy))
		{
			return false;
		}
		// Proc damage never procs more modules.
		DealDamage(Enemy, Damage, NAME_None);
		return true;
	});
	for (auto It = BleedTargets.CreateIterator(); It; ++It)
	{
		if (!Modules.HasBleed(It.Key()))
		{
			It.RemoveCurrent();
		}
	}
}

void UWLCombatComponent::ConsumeShot(bool bFission)
{
	UWLEquipmentComponent* Eq = GetEquipment();
	if (!Eq)
	{
		return;
	}
	if (bFission)
	{
		// Fission cores are the scarcest ammo; the gauntlets' save chance keeps the style playable.
		const FWLItemRow* W = GetWeapon();
		const float Save = W ? W->AmmoSaveChance : 0.f;
		if (Save > 0.f && Rng.GetFraction() < Save)
		{
			Message(LOCTEXT("CoreSaved", "The gauntlets recover the core."));
			return;
		}
	}
	Eq->ConsumeAmmo(1);
}

void UWLCombatComponent::AwardXP(int32 Damage, EWLCombatStyle HitStyle)
{
	if (Damage <= 0)
	{
		return;
	}
	// 4 XP/damage to the stance's skill (gauntlets always Fission, guns Marksmanship), 4/3 to HP.
	GiveXP(FWLCombatMath::StanceSkill(Stance, HitStyle), Damage * FWLCombatXP::XPPerDamage);
	GiveXP(EWLSkill::Endurance, Damage * FWLCombatXP::HPXPPerDamage);
}

void UWLCombatComponent::GiveXP(EWLSkill Skill, float Amount)
{
	const int32 Whole = XPCarry.Add(Skill, Amount);
	if (Whole > 0)
	{
		if (UWLSkillsComponent* Skills = GetSkills())
		{
			Skills->AddXP(Skill, Whole);
		}
	}
}

// --- special attack (Overload Cascade) -----------------------------------------------------------------

bool UWLCombatComponent::HasWeaponSpecial() const
{
	return IsFissionEquipped();
}

FText UWLCombatComponent::GetSpecialAttackName() const
{
	return LOCTEXT("OverloadCascade", "Overload Cascade");
}

FText UWLCombatComponent::GetSpecialBlockReason() const
{
	const UWLAbilitySystemComponent* ASC = GetASC();
	if (!ASC || ASC->IsDead())
	{
		return LOCTEXT("SpecDown", "Unavailable while down");
	}
	if (!HasWeaponSpecial())
	{
		return LOCTEXT("SpecGauntlets", "Equip Power Gauntlets");
	}
	if (ASC->GetSpecialEnergyValue() + 0.001f < FWLCombatMath::SpecialEnergyCost)
	{
		return LOCTEXT("SpecRecharging", "Recharging");
	}
	const UWLEquipmentComponent* Eq = GetEquipment();
	const int32 AmmoId = Eq ? Eq->GetItemId(EWLEquipSlot::Ammo) : 0;
	if (AmmoId < RefinedCoreFirst || AmmoId > RefinedCoreLast || Eq->GetAmmoQuantity() < FWLCombatMath::SpecialHits)
	{
		return LOCTEXT("SpecCores", "Load 4 fission cores");
	}
	if (!IsValidEnemy(Target))
	{
		return LOCTEXT("SpecTarget", "Select an enemy");
	}
	if (TooFar(Target))
	{
		return LOCTEXT("SpecFar", "Move closer to target");
	}
	return FText::GetEmpty();
}

void UWLCombatComponent::RequestSpecialAttack()
{
	if (GetOwner() && GetOwner()->HasAuthority())
	{
		ServerRequestSpecialAttack_Implementation();
	}
	else
	{
		ServerRequestSpecialAttack();
	}
}

void UWLCombatComponent::ServerRequestSpecialAttack_Implementation()
{
	ActivateOrRun(SpecialAttackHandle, [this]() { return PerformSpecialAttack(); });
}

bool UWLCombatComponent::PerformSpecialAttack()
{
	if (!GetOwner() || !GetOwner()->HasAuthority())
	{
		return false;
	}
	const FText Blocked = GetSpecialBlockReason();
	if (!Blocked.IsEmpty())
	{
		Message(FText::Format(LOCTEXT("SpecBlocked", "{0}."), Blocked));
		return false;
	}
	UWLAbilitySystemComponent* ASC = GetASC();
	UWLEquipmentComponent* Eq = GetEquipment();
	AWLEnemyCharacter* Enemy = Cast<AWLEnemyCharacter>(Target);
	if (!ASC || !Eq || !Enemy)
	{
		return false;
	}
	const int32 MaxHit = FMath::Max(1, Eq->AmmoStrengthBonus());

	// Energy goes FIRST, after every other check: a failure can never cost cores for nothing.
	if (!ASC->TrySpendSpecial(FWLCombatMath::SpecialEnergyCost))
	{
		return false;
	}

	// Cores are spent up front (a committed action); the save chance applies per core.
	const FWLItemRow* W = GetWeapon();
	const float Save = W ? W->AmmoSaveChance : 0.f;
	int32 Recovered = 0;
	for (int32 i = 0; i < FWLCombatMath::SpecialHits; ++i)
	{
		if (Save > 0.f && Rng.GetFraction() < Save)
		{
			++Recovered;
			continue;
		}
		Eq->ConsumeAmmo(1);
	}

	FaceToward(Enemy->GetActorLocation());
	MulticastAttackPerformed(true);

	const double T = Now();
	const int32 FissionLv = Level(EWLSkill::Fission);
	const int32 EquipAcc = Eq->TotalAttackBonusFor(EWLWeaponStyle::Fission);
	const int32 AtkRoll = FMath::RoundToInt(FWLCombatMath::AttackRoll(FissionLv, EquipAcc, FWLCombatMath::AccuracyStanceBonus(Stance)) * FWLCombatMath::SpecialAccuracyMult);
	const int32 DefRoll = FWLCombatMath::DefenceRoll(Modules.TargetDefence(KeyFor(Enemy), Enemy->GetDefenceLevel(), T), 0, 0);
	const float Chance = FWLCombatMath::HitChance(AtkRoll, DefRoll);

	// One Meltdown roll for the whole cascade: every hit derives from the first that lands.
	const bool bMeltdown = FWLCombatPerks::Meltdown(FissionLv, Rng);
	FText Line = bMeltdown ? LOCTEXT("CascadeMelt", "MELTDOWN OVERLOAD CASCADE!!") : LOCTEXT("Cascade", "OVERLOAD CASCADE!");
	if (Recovered > 0)
	{
		Line = FText::Format(LOCTEXT("CascadeRecovered", "{0} ({1} {1}|plural(one=core,other=cores) recovered)"), Line, Recovered);
	}
	Message(Line);

	const TArray<int32> Hits = FWLCombatMath::CascadeDamage(MaxHit, Chance, bMeltdown, Rng);
	int32 Total = 0;
	for (const int32 D : Hits)
	{
		if (D <= 0)
		{
			FWLCombatFeedback::Hitsplat(Enemy, 0, false);
			continue;
		}
		const int32 Dealt = Enemy->GetWLAbilitySystem()->ApplyDamage(D, GetOwner(), GetOwner());
		FWLCombatFeedback::Hitsplat(Enemy, Dealt, true, bMeltdown ? FName(TEXT("MELTDOWN")) : NAME_None);
		Total += Dealt;
		if (Enemy->IsDead())
		{
			break;
		}
	}
	if (Total > 0)
	{
		AwardXP(Total, EWLCombatStyle::Fission);
	}
	if (Enemy->IsDead())
	{
		Message(FText::Format(LOCTEXT("Defeated", "{0} is defeated."), Enemy->GetEnemyDisplayName()));
	}
	return true;
}

// --- magazine & reload (WeaponMagazine.cs) -------------------------------------------------------------

void UWLCombatComponent::RequestReload()
{
	if (GetOwner() && GetOwner()->HasAuthority())
	{
		ServerRequestReload_Implementation();
	}
	else
	{
		ServerRequestReload();
	}
}

void UWLCombatComponent::ServerRequestReload_Implementation()
{
	if (IsDead() || bRepReloading)
	{
		return;
	}
	ActivateOrRun(ReloadHandle, [this]()
	{
		const float Duration = BeginReload();
		if (Duration <= 0.f)
		{
			return false;
		}
		// No ability granted yet: time it with a plain timer.
		FTimerHandle Handle;
		GetWorld()->GetTimerManager().SetTimer(Handle, FTimerDelegate::CreateWeakLambda(this, [this]() { CompleteReload(); }), Duration, false);
		return true;
	});
}

float UWLCombatComponent::BeginReload()
{
	const FWLItemRow* W = GetWeapon();
	if (!W || W->WeaponStyle != EWLWeaponStyle::Ranged || bRepReloading || IsDead())
	{
		return 0.f;
	}
	const int32 MagSize = FWLCombatMath::MagazineSize(W->MagazineSize);
	if (Magazine.IsFull(W->ItemId, MagSize))
	{
		Message(LOCTEXT("MagFull", "Magazine's already full."));
		return 0.f;
	}
	const UWLEquipmentComponent* Eq = GetEquipment();
	if (!Eq || Eq->GetItemId(EWLEquipSlot::Ammo) <= 0 || Eq->GetAmmoQuantity() <= 0)
	{
		Message(LOCTEXT("NoRounds", "No rounds to load — put ammo in your Ammo slot."));
		return 0.f;
	}
	const float Duration = FWLCombatMath::ReloadSeconds(W->ReloadSeconds);
	ReloadWeaponId = W->ItemId;
	bRepReloading = true;
	const AGameStateBase* GS = GetWorld()->GetGameState();
	ReloadStartServerTime = static_cast<float>(GS ? GS->GetServerWorldTimeSeconds() : GetWorld()->GetTimeSeconds());
	ReloadEndServerTime = ReloadStartServerTime + Duration;
	UpdateMagazineReplication();
	return Duration;
}

void UWLCombatComponent::CompleteReload()
{
	if (!bRepReloading)
	{
		return;
	}
	bRepReloading = false;
	const FWLItemRow* W = GetWeapon();
	if (W && W->ItemId == ReloadWeaponId)
	{
		Magazine.Refill(W->ItemId, FWLCombatMath::MagazineSize(W->MagazineSize));
	}
	ReloadWeaponId = 0;
	UpdateMagazineReplication();
}

void UWLCombatComponent::AbortReload()
{
	if (!bRepReloading)
	{
		return;
	}
	bRepReloading = false;
	ReloadWeaponId = 0;
	UpdateMagazineReplication();
}

void UWLCombatComponent::UpdateMagazineReplication()
{
	const FWLItemRow* W = GetWeapon();
	const bool bGun = W && W->WeaponStyle == EWLWeaponStyle::Ranged;
	bRepUsesMagazine = bGun;
	RepMagSize = bGun ? FWLCombatMath::MagazineSize(W->MagazineSize) : 0;
	RepMagLoaded = bGun ? Magazine.GetLoaded(W->ItemId, RepMagSize) : 0;
	OnMagazineChanged.Broadcast();
}

void UWLCombatComponent::OnRep_Magazine()
{
	OnMagazineChanged.Broadcast();
}

float UWLCombatComponent::GetReloadProgress() const
{
	if (!bRepReloading || ReloadEndServerTime <= ReloadStartServerTime)
	{
		return 0.f;
	}
	const AGameStateBase* GS = GetWorld() ? GetWorld()->GetGameState() : nullptr;
	const float NowServer = GS ? static_cast<float>(GS->GetServerWorldTimeSeconds()) : 0.f;
	return FMath::Clamp((NowServer - ReloadStartServerTime) / (ReloadEndServerTime - ReloadStartServerTime), 0.f, 1.f);
}

bool UWLCombatComponent::IsAiming() const
{
	return Target != nullptr && IsRangedEquipped();
}

// --- incoming (Enemy3D.Attack) -------------------------------------------------------------------------

int32 UWLCombatComponent::ResolveIncomingAttack(AWLEnemyCharacter* Attacker)
{
	AWLPlayerCharacter* Character = GetPlayerCharacter();
	UWLAbilitySystemComponent* ASC = GetASC();
	UWLEquipmentComponent* Eq = GetEquipment();
	if (!Attacker || !Character || !ASC || !Eq || ASC->IsDead() || !GetOwner()->HasAuthority())
	{
		return 0;
	}

	// A swing that lands mid-dodge whiffs outright: the dodge is a real defensive tool.
	if (const UWLCharacterMovementComponent* Move = Character->GetWLMovement())
	{
		if (Move->IsDodging())
		{
			FWLCombatFeedback::Hitsplat(Character, 0, false);
			return 0;
		}
	}

	const int32 Defence = Level(EWLSkill::Defence);
	if (FWLCombatPerks::NegateHit(Defence, Rng))
	{
		FWLCombatFeedback::Hitsplat(Character, 0, false);
		Message(LOCTEXT("Negated", "[NEGATED] Your Defence absorbed the blow."));
		return 0;
	}

	// Defence IS the defence level; gear bonuses are balanced against a real level.
	const double T = Now();
	const int32 AtkRoll = FWLCombatMath::AttackRoll(Attacker->GetAttackLevel(), 0, 0);
	const int32 EquipDef = Eq->TotalDefenceBonus() + Eq->GetSetBonusDefence(EquippedWeaponStyle()) + Modules.Defence(T);
	const int32 DefRoll = FWLCombatMath::DefenceRoll(Defence, EquipDef, FWLCombatMath::DefenceStanceBonus(Stance));
	const bool bHit = FWLCombatMath::RollHit(FWLCombatMath::HitChance(AtkRoll, DefRoll), Rng);

	int32 Dmg = 0;
	if (bHit)
	{
		Dmg = FMath::Max(1, Rng.RandRange(0, Attacker->GetMaxHit()));
		const float Dr = FWLCombatPerks::DamageReduction(Defence);
		if (Dr > 0.f)
		{
			Dmg = FMath::Max(1, FMath::RoundToInt(Dmg * (1.f - Dr)));
		}

		// Defence L99: reflect the hit back (the player still takes it).
		if (FWLCombatPerks::ReflectDamage(Defence, Rng))
		{
			Attacker->TakeReturnDamage(Dmg, Character);
			Message(FText::Format(LOCTEXT("Reflect", "[REFLECT] Your Defence reflects {0} damage!"), Dmg));
		}

		const bool bRangedAttacker = Attacker->AttackRange > 400.f;
		const FWLModuleIncoming In = Modules.Incoming(Dmg, !Attacker->IsDead(), bRangedAttacker,
			ASC->GetCurrentHealthInt(), ASC->GetMaxHealthInt(), T, Rng);
		Dmg = In.Damage;
		if (In.ReturnDamage > 0)
		{
			Attacker->TakeReturnDamage(In.ReturnDamage, Character);
		}

		if (const AWLPlayerState* PS = GetWLPlayerState())
		{
			if (UWLCompanionComponent* Companions = PS->GetCompanions())
			{
				Dmg = Companions->AbsorbForPlayer(Dmg);   // the pack shares the hit
			}
		}
		if (Dmg > 0)
		{
			Dmg = ASC->ApplyDamage(Dmg, Attacker, Attacker);
		}
	}
	FWLCombatFeedback::Hitsplat(Character, Dmg, bHit);
	return Dmg;
}

// --- client RPCs / cosmetics -----------------------------------------------------------------------------

void UWLCombatComponent::FaceToward(const FVector& Point)
{
	ClientFaceToward(Point);
}

void UWLCombatComponent::ClientFaceToward_Implementation(FVector_NetQuantize Point)
{
	if (AActor* Owner = GetOwner())
	{
		const FVector To = (FVector(Point) - Owner->GetActorLocation()).GetSafeNormal2D();
		if (!To.IsNearlyZero())
		{
			Owner->SetActorRotation(To.Rotation());
		}
	}
}

void UWLCombatComponent::ClientApproachTarget_Implementation(FVector_NetQuantize Goal, float Acceptance)
{
	const AWLPlayerCharacter* Character = GetPlayerCharacter();
	if (AWLPlayerController* PC = Character ? Cast<AWLPlayerController>(Character->GetController()) : nullptr)
	{
		PC->MoveToLocation(Goal, Acceptance);
	}
}

void UWLCombatComponent::ClientStopApproach_Implementation()
{
	const AWLPlayerCharacter* Character = GetPlayerCharacter();
	if (AWLPlayerController* PC = Character ? Cast<AWLPlayerController>(Character->GetController()) : nullptr)
	{
		PC->StopPathFollowing();
	}
}

void UWLCombatComponent::MulticastAttackPerformed_Implementation(bool bRanged)
{
	OnAttackPerformed.Broadcast(bRanged);
}

void UWLCombatComponent::MulticastHitsplat_Implementation(int32 Amount, bool bHit, FName Tag)
{
	if (UWLCombatFeedbackSubsystem* Feedback = UWLCombatFeedbackSubsystem::Get(this))
	{
		FWLHitsplat Splat;
		Splat.Target = GetOwner();
		Splat.Amount = Amount;
		Splat.bHit = bHit;
		Splat.Tag = Tag;
		Feedback->Push(Splat);
	}
}

#undef LOCTEXT_NAMESPACE
