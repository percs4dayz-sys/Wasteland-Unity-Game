#include "Core/WLGameplayTags.h"

namespace WLTags
{
	UE_DEFINE_GAMEPLAY_TAG_COMMENT(Ability_Attack, "Ability.Attack", "Basic attack with the equipped weapon");
	UE_DEFINE_GAMEPLAY_TAG(Ability_Attack_Melee, "Ability.Attack.Melee");
	UE_DEFINE_GAMEPLAY_TAG(Ability_Attack_Ranged, "Ability.Attack.Ranged");
	UE_DEFINE_GAMEPLAY_TAG(Ability_Attack_Fission, "Ability.Attack.Fission");
	UE_DEFINE_GAMEPLAY_TAG_COMMENT(Ability_Special, "Ability.Special", "Weapon special attack, spends special energy");
	UE_DEFINE_GAMEPLAY_TAG(Ability_Eat, "Ability.Eat");
	UE_DEFINE_GAMEPLAY_TAG(Ability_Reload, "Ability.Reload");

	UE_DEFINE_GAMEPLAY_TAG(State_Dead, "State.Dead");
	UE_DEFINE_GAMEPLAY_TAG(State_InCombat, "State.InCombat");
	UE_DEFINE_GAMEPLAY_TAG(State_Skilling, "State.Skilling");
	UE_DEFINE_GAMEPLAY_TAG(State_Reloading, "State.Reloading");
	UE_DEFINE_GAMEPLAY_TAG_COMMENT(State_EatDelay, "State.EatDelay", "Attack delay after eating");

	UE_DEFINE_GAMEPLAY_TAG(Status_Bleed, "Status.Bleed");
	UE_DEFINE_GAMEPLAY_TAG(Status_Barrier, "Status.Barrier");

	UE_DEFINE_GAMEPLAY_TAG(SetByCaller_Damage, "SetByCaller.Damage");
	UE_DEFINE_GAMEPLAY_TAG(SetByCaller_Heal, "SetByCaller.Heal");

	UE_DEFINE_GAMEPLAY_TAG(Event_Hit, "Event.Hit");
	UE_DEFINE_GAMEPLAY_TAG(Event_Kill, "Event.Kill");
	UE_DEFINE_GAMEPLAY_TAG(Event_Death, "Event.Death");
}
