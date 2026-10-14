#include "AbilitySystem/WLCombatAttributeSet.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "GameplayEffect.h"
#include "GameplayEffectExtension.h"
#include "Net/UnrealNetwork.h"

UWLCombatAttributeSet::UWLCombatAttributeSet()
{
	// Fresh characters: Endurance 10 = 10 HP (UWLSkillsComponent raises MaxHealth from the save).
	InitHealth(10.f);
	InitMaxHealth(10.f);
	InitSpecialEnergy(100.f);
	InitMaxSpecialEnergy(100.f);
	InitDamage(0.f);
	InitHealing(0.f);
}

void UWLCombatAttributeSet::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME_CONDITION_NOTIFY(UWLCombatAttributeSet, Health, COND_None, REPNOTIFY_Always);
	DOREPLIFETIME_CONDITION_NOTIFY(UWLCombatAttributeSet, MaxHealth, COND_None, REPNOTIFY_Always);
	DOREPLIFETIME_CONDITION_NOTIFY(UWLCombatAttributeSet, SpecialEnergy, COND_None, REPNOTIFY_Always);
	DOREPLIFETIME_CONDITION_NOTIFY(UWLCombatAttributeSet, MaxSpecialEnergy, COND_None, REPNOTIFY_Always);
}

void UWLCombatAttributeSet::ClampAttribute(const FGameplayAttribute& Attribute, float& NewValue) const
{
	if (Attribute == GetHealthAttribute())
	{
		NewValue = FMath::Clamp(NewValue, 0.f, GetMaxHealth());
	}
	else if (Attribute == GetMaxHealthAttribute())
	{
		NewValue = FMath::Max(NewValue, 1.f);
	}
	else if (Attribute == GetSpecialEnergyAttribute())
	{
		NewValue = FMath::Clamp(NewValue, 0.f, GetMaxSpecialEnergy());
	}
	else if (Attribute == GetMaxSpecialEnergyAttribute())
	{
		NewValue = FMath::Max(NewValue, 0.f);
	}
}

void UWLCombatAttributeSet::PreAttributeChange(const FGameplayAttribute& Attribute, float& NewValue)
{
	Super::PreAttributeChange(Attribute, NewValue);
	ClampAttribute(Attribute, NewValue);
}

void UWLCombatAttributeSet::PreAttributeBaseChange(const FGameplayAttribute& Attribute, float& NewValue) const
{
	Super::PreAttributeBaseChange(Attribute, NewValue);
	ClampAttribute(Attribute, NewValue);
}

void UWLCombatAttributeSet::PostAttributeChange(const FGameplayAttribute& Attribute, float OldValue, float NewValue)
{
	Super::PostAttributeChange(Attribute, OldValue, NewValue);

	// A lowered max must pull the current value down with it (e.g. dev XP resets).
	UAbilitySystemComponent* ASC = GetOwningAbilitySystemComponent();
	if (!ASC || !ASC->IsOwnerActorAuthoritative())
	{
		return;
	}
	if (Attribute == GetMaxHealthAttribute() && GetHealth() > NewValue)
	{
		ASC->ApplyModToAttribute(GetHealthAttribute(), EGameplayModOp::Override, NewValue);
	}
	else if (Attribute == GetMaxSpecialEnergyAttribute() && GetSpecialEnergy() > NewValue)
	{
		ASC->ApplyModToAttribute(GetSpecialEnergyAttribute(), EGameplayModOp::Override, NewValue);
	}
}

void UWLCombatAttributeSet::PostGameplayEffectExecute(const FGameplayEffectModCallbackData& Data)
{
	Super::PostGameplayEffectExecute(Data);

	UWLAbilitySystemComponent* WLASC = Cast<UWLAbilitySystemComponent>(GetOwningAbilitySystemComponent());
	const FGameplayEffectContextHandle& Context = Data.EffectSpec.GetContext();

	if (Data.EvaluatedData.Attribute == GetDamageAttribute())
	{
		const float Incoming = GetDamage();
		SetDamage(0.f);

		// Already down: block further damage and re-death spam until revived (PlayerStats.TakeDamage).
		if (Incoming <= 0.f || (WLASC && WLASC->IsDead()))
		{
			return;
		}
		const float OldHealth = GetHealth();
		const float NewHealth = FMath::Clamp(OldHealth - Incoming, 0.f, GetMaxHealth());
		SetHealth(NewHealth);

		if (WLASC)
		{
			const int32 Dealt = FMath::RoundToInt(OldHealth - NewHealth);
			WLASC->NotifyDamageTaken(Dealt, Context.GetOriginalInstigator(), Context.GetEffectCauser());
			if (NewHealth <= 0.f)
			{
				WLASC->HandleOutOfHealth(Context.GetOriginalInstigator(), Context.GetEffectCauser());
			}
		}
	}
	else if (Data.EvaluatedData.Attribute == GetHealingAttribute())
	{
		const float Incoming = GetHealing();
		SetHealing(0.f);

		// No eating your way out of death (PlayerStats.Heal).
		if (Incoming <= 0.f || (WLASC && WLASC->IsDead()))
		{
			return;
		}
		SetHealth(FMath::Clamp(GetHealth() + Incoming, 0.f, GetMaxHealth()));
	}
}

void UWLCombatAttributeSet::OnRep_Health(const FGameplayAttributeData& OldValue)
{
	GAMEPLAYATTRIBUTE_REPNOTIFY(UWLCombatAttributeSet, Health, OldValue);
}

void UWLCombatAttributeSet::OnRep_MaxHealth(const FGameplayAttributeData& OldValue)
{
	GAMEPLAYATTRIBUTE_REPNOTIFY(UWLCombatAttributeSet, MaxHealth, OldValue);
}

void UWLCombatAttributeSet::OnRep_SpecialEnergy(const FGameplayAttributeData& OldValue)
{
	GAMEPLAYATTRIBUTE_REPNOTIFY(UWLCombatAttributeSet, SpecialEnergy, OldValue);
}

void UWLCombatAttributeSet::OnRep_MaxSpecialEnergy(const FGameplayAttributeData& OldValue)
{
	GAMEPLAYATTRIBUTE_REPNOTIFY(UWLCombatAttributeSet, MaxSpecialEnergy, OldValue);
}
