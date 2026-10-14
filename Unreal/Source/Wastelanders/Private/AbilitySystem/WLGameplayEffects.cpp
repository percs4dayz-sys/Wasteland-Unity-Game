#include "AbilitySystem/WLGameplayEffects.h"
#include "AbilitySystem/WLCombatAttributeSet.h"
#include "Core/WLGameplayTags.h"

namespace
{
	FGameplayModifierInfo MakeSetByCallerAdd(const FGameplayAttribute& Attribute, const FGameplayTag& Tag)
	{
		FSetByCallerFloat SetByCaller;
		SetByCaller.DataTag = Tag;

		FGameplayModifierInfo Mod;
		Mod.Attribute = Attribute;
		Mod.ModifierOp = EGameplayModOp::Additive;
		Mod.ModifierMagnitude = FGameplayEffectModifierMagnitude(SetByCaller);
		return Mod;
	}
}

UWLGE_Damage::UWLGE_Damage()
{
	DurationPolicy = EGameplayEffectDurationType::Instant;
	Modifiers.Add(MakeSetByCallerAdd(UWLCombatAttributeSet::GetDamageAttribute(), WLTags::SetByCaller_Damage));
}

UWLGE_Heal::UWLGE_Heal()
{
	DurationPolicy = EGameplayEffectDurationType::Instant;
	Modifiers.Add(MakeSetByCallerAdd(UWLCombatAttributeSet::GetHealingAttribute(), WLTags::SetByCaller_Heal));
}
