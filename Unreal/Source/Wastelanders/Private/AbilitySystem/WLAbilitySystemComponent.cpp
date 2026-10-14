#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "AbilitySystem/WLCombatAttributeSet.h"
#include "AbilitySystem/WLGameplayEffects.h"
#include "Abilities/GameplayAbility.h"
#include "Core/WLGameplayTags.h"
#include "Core/WLGameEventsSubsystem.h"
#include "Core/WLGameTickSubsystem.h"
#include "Core/WLGameMode.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Net/UnrealNetwork.h"
#include "Engine/World.h"

#define LOCTEXT_NAMESPACE "WLAbilitySystem"

UWLAbilitySystemComponent::UWLAbilitySystemComponent()
{
	SetIsReplicatedByDefault(true);
}

void UWLAbilitySystemComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(UWLAbilitySystemComponent, bDead);
}

void UWLAbilitySystemComponent::BeginPlay()
{
	Super::BeginPlay();

	GetGameplayAttributeValueChangeDelegate(UWLCombatAttributeSet::GetHealthAttribute()).AddUObject(this, &UWLAbilitySystemComponent::HandleHealthAttributeChanged);
	GetGameplayAttributeValueChangeDelegate(UWLCombatAttributeSet::GetMaxHealthAttribute()).AddUObject(this, &UWLAbilitySystemComponent::HandleHealthAttributeChanged);
	GetGameplayAttributeValueChangeDelegate(UWLCombatAttributeSet::GetSpecialEnergyAttribute()).AddUObject(this, &UWLAbilitySystemComponent::HandleSpecialAttributeChanged);

	if (IsOwnerActorAuthoritative() && bRegenSpecialEnergy)
	{
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			GameTickHandle = Tick->OnGameTick.AddUObject(this, &UWLAbilitySystemComponent::HandleGameTick);
		}
	}
}

void UWLAbilitySystemComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (GameTickHandle.IsValid())
	{
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			Tick->OnGameTick.Remove(GameTickHandle);
		}
		GameTickHandle.Reset();
	}
	Super::EndPlay(EndPlayReason);
}

// --- health ------------------------------------------------------------------------------------------

int32 UWLAbilitySystemComponent::GetCurrentHealthInt() const
{
	return FMath::RoundToInt(GetNumericAttribute(UWLCombatAttributeSet::GetHealthAttribute()));
}

int32 UWLAbilitySystemComponent::GetMaxHealthInt() const
{
	return FMath::RoundToInt(GetNumericAttribute(UWLCombatAttributeSet::GetMaxHealthAttribute()));
}

void UWLAbilitySystemComponent::ApplyHeal(int32 Amount)
{
	if (!IsOwnerActorAuthoritative() || Amount <= 0 || bDead)
	{
		return;
	}
	FGameplayEffectContextHandle Context = MakeEffectContext();
	const FGameplayEffectSpecHandle Spec = MakeOutgoingSpec(UWLGE_Heal::StaticClass(), 1.f, Context);
	if (Spec.IsValid())
	{
		Spec.Data->SetSetByCallerMagnitude(WLTags::SetByCaller_Heal, static_cast<float>(Amount));
		ApplyGameplayEffectSpecToSelf(*Spec.Data.Get());
	}
}

void UWLAbilitySystemComponent::HealToFull()
{
	if (!IsOwnerActorAuthoritative() || bDead)
	{
		return;
	}
	SetNumericAttributeBase(UWLCombatAttributeSet::GetHealthAttribute(), GetMaxHealthInt());
}

void UWLAbilitySystemComponent::RestoreHealth(int32 SavedHP)
{
	if (!IsOwnerActorAuthoritative())
	{
		return;
	}
	const int32 Max = GetMaxHealthInt();
	// A saved 0 would load a corpse that never got its respawn: treat it as full, like a fresh save.
	const int32 HP = (SavedHP <= 0 || SavedHP > Max) ? Max : SavedHP;
	SetDead(false);
	SetNumericAttributeBase(UWLCombatAttributeSet::GetHealthAttribute(), HP);
}

void UWLAbilitySystemComponent::Revive()
{
	if (!IsOwnerActorAuthoritative())
	{
		return;
	}
	SetDead(false);
	SetNumericAttributeBase(UWLCombatAttributeSet::GetHealthAttribute(), GetMaxHealthInt());

	if (AWLPlayerState* PS = GetOwningWLPlayerState())
	{
		if (AWLPlayerController* PC = Cast<AWLPlayerController>(PS->GetOwningController()))
		{
			PC->ClientGameMessage(LOCTEXT("Respawned", "You wake at your spawn point."));
		}
	}
}

void UWLAbilitySystemComponent::SetMaxHealth(int32 NewMax, bool bHealDelta)
{
	if (!IsOwnerActorAuthoritative())
	{
		return;
	}
	NewMax = FMath::Max(1, NewMax);
	const int32 OldMax = GetMaxHealthInt();
	SetNumericAttributeBase(UWLCombatAttributeSet::GetMaxHealthAttribute(), NewMax);
	if (bHealDelta && NewMax > OldMax && !bDead)
	{
		const int32 NewHP = FMath::Min(GetCurrentHealthInt() + (NewMax - OldMax), NewMax);
		SetNumericAttributeBase(UWLCombatAttributeSet::GetHealthAttribute(), NewHP);
	}
}

int32 UWLAbilitySystemComponent::ApplyDamage(int32 Amount, AActor* Instigator, AActor* Causer)
{
	if (!IsOwnerActorAuthoritative() || Amount <= 0 || bDead)
	{
		return 0;
	}
	const int32 Before = GetCurrentHealthInt();

	FGameplayEffectContextHandle Context = MakeEffectContext();
	Context.AddInstigator(Instigator, Causer ? Causer : Instigator);
	const FGameplayEffectSpecHandle Spec = MakeOutgoingSpec(UWLGE_Damage::StaticClass(), 1.f, Context);
	if (!Spec.IsValid())
	{
		return 0;
	}
	Spec.Data->SetSetByCallerMagnitude(WLTags::SetByCaller_Damage, static_cast<float>(Amount));
	ApplyGameplayEffectSpecToSelf(*Spec.Data.Get());

	return FMath::Max(0, Before - GetCurrentHealthInt());
}

void UWLAbilitySystemComponent::NotifyDamageTaken(int32 Amount, AActor* Instigator, AActor* Causer)
{
	if (Amount > 0)
	{
		OnDamageTaken.Broadcast(Amount, Instigator, Causer);
	}
}

void UWLAbilitySystemComponent::HandleOutOfHealth(AActor* Instigator, AActor* Causer)
{
	if (bDead || !IsOwnerActorAuthoritative())
	{
		return;
	}
	SetDead(true);
	OnOutOfHealth.Broadcast(Instigator, Causer);

	if (GetOwningWLPlayerState())
	{
		HandlePlayerDeath();
	}
}

void UWLAbilitySystemComponent::HandlePlayerDeath()
{
	AWLPlayerState* PS = GetOwningWLPlayerState();
	if (!PS)
	{
		return;
	}
	// Stop whatever the body was doing; State_Dead blocks re-activation.
	CancelAllAbilities();

	if (UWLGameEventsSubsystem* Events = UWLGameEventsSubsystem::Get(this))
	{
		Events->OnPlayerDied.Broadcast(PS);
	}

	AController* Controller = PS->GetOwningController();
	if (AWLPlayerController* PC = Cast<AWLPlayerController>(Controller))
	{
		PC->ClientGameMessage(LOCTEXT("Died", "You have died. Respawning..."));
	}
	if (AWLGameMode* GM = GetWorld() ? GetWorld()->GetAuthGameMode<AWLGameMode>() : nullptr)
	{
		// Unity PlayerRespawn.respawnDelay: long enough for the death to read.
		GM->RequestRespawn(Controller, 3.f);
	}
}

void UWLAbilitySystemComponent::SetDead(bool bNewDead)
{
	if (bDead == bNewDead)
	{
		return;
	}
	bDead = bNewDead;
	ApplyDeadTag();
	OnDeathStateChanged.Broadcast(bDead);
	if (AActor* Owner = GetOwner())
	{
		Owner->ForceNetUpdate();
	}
}

void UWLAbilitySystemComponent::OnRep_Dead()
{
	ApplyDeadTag();
	OnDeathStateChanged.Broadcast(bDead);
}

void UWLAbilitySystemComponent::ApplyDeadTag()
{
	// The flag itself replicates; the loose tag is mirrored locally on every machine so tag queries
	// (ability blocking, UI) agree everywhere without relying on loose-tag replication.
	if (bDead && !bDeadTagApplied)
	{
		AddLooseGameplayTag(WLTags::State_Dead);
		bDeadTagApplied = true;
	}
	else if (!bDead && bDeadTagApplied)
	{
		RemoveLooseGameplayTag(WLTags::State_Dead);
		bDeadTagApplied = false;
	}
}

// --- special energy ----------------------------------------------------------------------------------

float UWLAbilitySystemComponent::GetSpecialEnergyValue() const
{
	return GetNumericAttribute(UWLCombatAttributeSet::GetSpecialEnergyAttribute());
}

float UWLAbilitySystemComponent::GetMaxSpecialEnergyValue() const
{
	return GetNumericAttribute(UWLCombatAttributeSet::GetMaxSpecialEnergyAttribute());
}

bool UWLAbilitySystemComponent::TrySpendSpecial(float Cost)
{
	if (!IsOwnerActorAuthoritative())
	{
		return false;
	}
	const float Energy = GetSpecialEnergyValue();
	if (Energy + 0.001f < Cost)
	{
		return false;
	}
	SetNumericAttributeBase(UWLCombatAttributeSet::GetSpecialEnergyAttribute(), FMath::Max(0.f, Energy - Cost));
	return true;
}

void UWLAbilitySystemComponent::RestoreSpecial()
{
	if (IsOwnerActorAuthoritative())
	{
		SetNumericAttributeBase(UWLCombatAttributeSet::GetSpecialEnergyAttribute(), GetMaxSpecialEnergyValue());
	}
}

void UWLAbilitySystemComponent::HandleGameTick(int64 /*TickCount*/)
{
	const float Max = GetMaxSpecialEnergyValue();
	const float Energy = GetSpecialEnergyValue();
	if (Energy >= Max)
	{
		return;
	}
	const UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this);
	const float Dt = Tick ? Tick->GetTickDuration() : 0.6f;
	SetNumericAttributeBase(UWLCombatAttributeSet::GetSpecialEnergyAttribute(), FMath::Min(Max, Energy + SpecialRegenPerSecond * Dt));
}

// --- abilities ---------------------------------------------------------------------------------------

FGameplayAbilitySpecHandle UWLAbilitySystemComponent::GiveAbilityOnce(TSubclassOf<UGameplayAbility> AbilityClass, int32 Level)
{
	if (!IsOwnerActorAuthoritative() || !AbilityClass)
	{
		return FGameplayAbilitySpecHandle();
	}
	if (const FGameplayAbilitySpec* Existing = FindAbilitySpecFromClass(AbilityClass))
	{
		return Existing->Handle;
	}
	return GiveAbility(FGameplayAbilitySpec(AbilityClass, Level, INDEX_NONE, GetOwner()));
}

// --- attribute -> delegate plumbing ------------------------------------------------------------------

void UWLAbilitySystemComponent::HandleHealthAttributeChanged(const FOnAttributeChangeData& /*Data*/)
{
	OnHealthChanged.Broadcast(GetCurrentHealthInt(), GetMaxHealthInt());
}

void UWLAbilitySystemComponent::HandleSpecialAttributeChanged(const FOnAttributeChangeData& Data)
{
	OnSpecialEnergyChanged.Broadcast(Data.NewValue);
}

AWLPlayerState* UWLAbilitySystemComponent::GetOwningWLPlayerState() const
{
	return Cast<AWLPlayerState>(GetOwner());
}

#undef LOCTEXT_NAMESPACE
