#pragma once

#include "CoreMinimal.h"
#include "AttributeSet.h"
#include "AbilitySystemComponent.h"
#include "WLCombatAttributeSet.generated.h"

#define WL_ATTRIBUTE_ACCESSORS(ClassName, PropertyName) \
	GAMEPLAYATTRIBUTE_PROPERTY_GETTER(ClassName, PropertyName) \
	GAMEPLAYATTRIBUTE_VALUE_GETTER(PropertyName) \
	GAMEPLAYATTRIBUTE_VALUE_SETTER(PropertyName) \
	GAMEPLAYATTRIBUTE_VALUE_INITTER(PropertyName)

/**
 * Hit points and special-attack energy for players and enemies.
 *
 *  - Health / MaxHealth: Unity PlayerStats.CurrentHP / MaxHP (Endurance level) and CombatTarget HP.
 *    MaxHealth for players is driven by UWLSkillsComponent (Endurance level, identity 10-220).
 *  - SpecialEnergy / MaxSpecialEnergy: CombatManager's 0-100 special pool (regen in the ASC).
 *  - Damage / Healing: meta attributes. Gameplay effects write here and PostGameplayEffectExecute
 *    turns them into Health changes, so clamping, "no damage once dead" and death detection all
 *    live in exactly one place.
 *
 * Health replicates to everyone (HP bars over other players and enemies); special energy too, it
 * is tiny and the minimal ASC replication mode already sends attributes to all.
 */
UCLASS()
class WASTELANDERS_API UWLCombatAttributeSet : public UAttributeSet
{
	GENERATED_BODY()

public:
	UWLCombatAttributeSet();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void PreAttributeChange(const FGameplayAttribute& Attribute, float& NewValue) override;
	virtual void PreAttributeBaseChange(const FGameplayAttribute& Attribute, float& NewValue) const override;
	virtual void PostAttributeChange(const FGameplayAttribute& Attribute, float OldValue, float NewValue) override;
	virtual void PostGameplayEffectExecute(const struct FGameplayEffectModCallbackData& Data) override;

	UPROPERTY(BlueprintReadOnly, ReplicatedUsing = OnRep_Health, Category = "Combat")
	FGameplayAttributeData Health;
	WL_ATTRIBUTE_ACCESSORS(UWLCombatAttributeSet, Health)

	UPROPERTY(BlueprintReadOnly, ReplicatedUsing = OnRep_MaxHealth, Category = "Combat")
	FGameplayAttributeData MaxHealth;
	WL_ATTRIBUTE_ACCESSORS(UWLCombatAttributeSet, MaxHealth)

	UPROPERTY(BlueprintReadOnly, ReplicatedUsing = OnRep_SpecialEnergy, Category = "Combat")
	FGameplayAttributeData SpecialEnergy;
	WL_ATTRIBUTE_ACCESSORS(UWLCombatAttributeSet, SpecialEnergy)

	UPROPERTY(BlueprintReadOnly, ReplicatedUsing = OnRep_MaxSpecialEnergy, Category = "Combat")
	FGameplayAttributeData MaxSpecialEnergy;
	WL_ATTRIBUTE_ACCESSORS(UWLCombatAttributeSet, MaxSpecialEnergy)

	/** Meta: incoming damage, server only, never replicated. */
	UPROPERTY(BlueprintReadOnly, Category = "Combat|Meta")
	FGameplayAttributeData Damage;
	WL_ATTRIBUTE_ACCESSORS(UWLCombatAttributeSet, Damage)

	/** Meta: incoming healing, server only, never replicated. */
	UPROPERTY(BlueprintReadOnly, Category = "Combat|Meta")
	FGameplayAttributeData Healing;
	WL_ATTRIBUTE_ACCESSORS(UWLCombatAttributeSet, Healing)

protected:
	UFUNCTION() void OnRep_Health(const FGameplayAttributeData& OldValue);
	UFUNCTION() void OnRep_MaxHealth(const FGameplayAttributeData& OldValue);
	UFUNCTION() void OnRep_SpecialEnergy(const FGameplayAttributeData& OldValue);
	UFUNCTION() void OnRep_MaxSpecialEnergy(const FGameplayAttributeData& OldValue);

private:
	void ClampAttribute(const FGameplayAttribute& Attribute, float& NewValue) const;
};
