#include "Combat/WLCombatMath.h"
#include "Combat/WLCombatTypes.h"

float FWLCombatMath::HitChance(int32 AttackRoll, int32 DefenceRoll)
{
	if (AttackRoll > DefenceRoll)
	{
		return 1.f - (DefenceRoll + 2.f) / (2.f * (AttackRoll + 1.f));
	}
	return AttackRoll / (2.f * (DefenceRoll + 1.f));
}

int32 FWLCombatMath::MaxHit(int32 StrLevel, int32 EquipStrengthBonus, int32 StanceBonus)
{
	const int32 Eff = EffectiveLevel(StrLevel, StanceBonus);
	const int32 Max = FMath::FloorToInt(0.5f + static_cast<float>(Eff * (EquipStrengthBonus + 64)) / 640.f);
	return FMath::Max(1, FMath::RoundToInt(Max * DamageScale));
}

int32 FWLCombatMath::RollDamage(int32 InMaxHit, FRandomStream& Rng)
{
	// Unity Random.Range(0, max+1) is max-exclusive; RandRange is inclusive on both ends.
	return Rng.RandRange(0, FMath::Max(1, InMaxHit));
}

TArray<EWLCombatStance> FWLCombatMath::StancesFor(EWLCombatStyle Style)
{
	switch (Style)
	{
	case EWLCombatStyle::Fission: return { EWLCombatStance::Accurate };
	case EWLCombatStyle::Melee:   return { EWLCombatStance::Accurate, EWLCombatStance::Aggressive, EWLCombatStance::Defensive };
	default:                      return { EWLCombatStance::Accurate, EWLCombatStance::Rapid, EWLCombatStance::Longrange };
	}
}

bool FWLCombatMath::IsStanceAllowed(EWLCombatStyle Style, EWLCombatStance Stance)
{
	return StancesFor(Style).Contains(Stance);
}

EWLCombatStance FWLCombatMath::ClampStance(EWLCombatStyle Style, EWLCombatStance Stance)
{
	const TArray<EWLCombatStance> Allowed = StancesFor(Style);
	if (Allowed.Contains(Stance))
	{
		return Stance;
	}
	return Allowed.Num() > 0 ? Allowed[0] : EWLCombatStance::Accurate;
}

EWLCombatStyle FWLCombatMath::StyleForWeapon(EWLWeaponStyle WeaponStyle)
{
	switch (WeaponStyle)
	{
	case EWLWeaponStyle::Fission: return EWLCombatStyle::Fission;
	case EWLWeaponStyle::Ranged:  return EWLCombatStyle::Ranged;
	default:                      return EWLCombatStyle::Melee;
	}
}

EWLSkill FWLCombatMath::StanceSkill(EWLCombatStance Stance, EWLCombatStyle Style)
{
	if (Style == EWLCombatStyle::Fission)
	{
		return EWLSkill::Fission;   // gauntlets train Fission, never Marksmanship
	}
	if (Style == EWLCombatStyle::Ranged)
	{
		return EWLSkill::Marksmanship;
	}
	switch (Stance)
	{
	case EWLCombatStance::Aggressive: return EWLSkill::Strength;
	case EWLCombatStance::Defensive:  return EWLSkill::Defence;
	default:                          return EWLSkill::Attack;
	}
}

int32 FWLCombatMath::DefenceStanceBonus(EWLCombatStance Stance)
{
	switch (Stance)
	{
	case EWLCombatStance::Defensive:  return 3;
	case EWLCombatStance::Longrange:  return 3;
	case EWLCombatStance::Controlled: return 1;
	default:                          return 0;
	}
}

int32 FWLCombatMath::AttackSpeedTicks(int32 WeaponSpeedTicks, EWLCombatStance Stance)
{
	int32 Ticks = WeaponSpeedTicks > 0 ? WeaponSpeedTicks : 3;
	if (Stance == EWLCombatStance::Rapid)
	{
		Ticks = FMath::Max(2, Ticks - 1);
	}
	if (Stance == EWLCombatStance::Aggressive)
	{
		Ticks += 1;
	}
	return Ticks;
}

float FWLCombatMath::ReachUnits(int32 WeaponRangeTiles, bool bRanged, EWLCombatStance Stance, float MeleeReach, float PerTile)
{
	int32 Tiles = WeaponRangeTiles > 0 ? WeaponRangeTiles : 1;
	if (bRanged)
	{
		Tiles += LongrangeBonusTiles(Stance);
	}
	return MeleeReach + FMath::Max(0, Tiles - 1) * PerTile;
}

double FWLCombatMath::NextReadyTick(int64 CurrentTick, double ReadyAt, double CooldownTicks)
{
	// Sustained fire is always under a tick late; anything later means the weapon sat idle, and an
	// idle weapon earns no carry (otherwise every fresh engage would start almost a tick early).
	const double Since = static_cast<double>(CurrentTick) - ReadyAt;
	const double Late = (Since > 0.0 && Since < 1.0) ? Since : 0.0;
	return static_cast<double>(CurrentTick) - Late + CooldownTicks;
}

TArray<int32> FWLCombatMath::CascadeDamage(int32 InMaxHit, float InHitChance, bool bMeltdown, FRandomStream& Rng)
{
	TArray<int32> D;
	D.SetNumZeroed(SpecialHits);

	for (int32 i = 0; i < SpecialHits; ++i)
	{
		if (Rng.GetFraction() > InHitChance)
		{
			continue;   // this hit missed: try the next one down
		}

		// Bands sit ABOVE a normal 0..max roll so the cascade averages a little more than the four
		// ordinary shots its four cores would buy (a special must never be a damage loss).
		int32 Lo, Hi;
		switch (i)
		{
		case 0:  Lo = InMaxHit * 5 / 8; Hi = InMaxHit * 10 / 8; break;
		case 1:  Lo = InMaxHit * 4 / 8; Hi = InMaxHit * 9 / 8;  break;
		case 2:  Lo = InMaxHit * 3 / 8; Hi = InMaxHit * 8 / 8;  break;
		default: Lo = InMaxHit * 3 / 8; Hi = InMaxHit * 12 / 8; break;   // last gasp, swingy
		}
		D[i] = bMeltdown
			? InMaxHit * FWLCombatPerks::MeltdownMultiplier
			: Rng.RandRange(FMath::Max(1, Lo), FMath::Max(2, Hi));

		if (i + 1 < SpecialHits) D[i + 1] = FMath::Max(1, D[i] / 2);
		if (i + 2 < SpecialHits) D[i + 2] = FMath::Max(1, D[i + 1] / 2);
		if (i + 3 < SpecialHits) D[i + 3] = D[i + 2];
		return D;
	}

	D[SpecialHits - 1] = 1;   // total whiff still chips for 1
	return D;
}

int32 FWLCombatMath::LootTier(int32 ExplicitTier, int32 MaxHP)
{
	if (ExplicitTier > 0) return ExplicitTier;
	if (MaxHP >= 450) return 5;
	if (MaxHP >= 280) return 4;
	if (MaxHP >= 160) return 3;
	if (MaxHP >= 90) return 2;
	return 1;
}

float FWLCombatPerks::DamageReduction(int32 Defence)
{
	if (Defence < 20) return 0.f;
	float Dr = 0.05f;
	if (Defence >= 60) Dr = 0.15f;
	if (Defence >= 99) Dr = 0.25f;
	return Dr;
}

int32 FWLCombatXP::Add(EWLSkill Skill, float Amount)
{
	if (Amount <= 0.f)
	{
		return 0;
	}
	float& R = Remainder.FindOrAdd(Skill);
	const float Carried = Amount + R;
	const int32 Whole = FMath::FloorToInt(Carried);
	R = Carried - Whole;
	return Whole;
}

FName WLModules::EffectName(EWLModuleEffect Effect)
{
	static TArray<FName> Names = []()
	{
		TArray<FName> Out;
		const UEnum* Enum = StaticEnum<EWLModuleEffect>();
		for (int32 i = 0; i < static_cast<int32>(EWLModuleEffect::Count); ++i)
		{
			Out.Add(FName(*Enum->GetNameStringByValue(i)));
		}
		return Out;
	}();
	const int32 Index = static_cast<int32>(Effect);
	return Names.IsValidIndex(Index) ? Names[Index] : NAME_None;
}
