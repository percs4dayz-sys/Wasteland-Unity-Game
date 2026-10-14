#include "Combat/WLModuleEffects.h"


void FWLModuleEffects::NotifyGear(uint32 InSignature)
{
	if (bHasGearSignature && InSignature == GearSignature)
	{
		return;
	}
	bHasGearSignature = true;
	GearSignature = InSignature;
	Shield = FMath::Min(Shield, ShieldCapacity());
	ResetCombat();
}

void FWLModuleEffects::ResetCombat()
{
	Chain = 0;
	Misses = 0;
	Charge = 0;
	CurrentTarget = 0;
	ReactiveUntil = 0.0;
	ServoUntil = 0.0;
	KillUntil = 0.0;
}

void FWLModuleEffects::Track(uint64 Target, double Now)
{
	const double Window = Powers(EWLModuleEffect::Combo) >= 2 ? 10.0 : 6.0;
	if (Target != CurrentTarget || Now - LastHit > Window)
	{
		Chain = 0;
		Charge = 0;
		Misses = 0;
		CurrentTarget = Target;
	}
}

int32 FWLModuleEffects::Accuracy(uint64 Target, bool bRanged, double Now)
{
	Track(Target, Now);
	return Misses * 5 * Powers(EWLModuleEffect::MissAdapt) + (bRanged ? 4 * Powers(EWLModuleEffect::Targeting) : 0);
}

int32 FWLModuleEffects::Defence(double Now) const
{
	return Now < ReactiveUntil ? 8 * Powers(EWLModuleEffect::Reactive) : 0;
}

int32 FWLModuleEffects::TargetDefence(uint64 Target, int32 TargetDefenceLevel, double Now)
{
	Track(Target, Now);
	const float Shred = FMath::Min(0.4f, Chain * 0.02f * Powers(EWLModuleEffect::Resonance));
	return FMath::Max(0, FMath::RoundToInt(TargetDefenceLevel * (1.f - Shred)));
}

void FWLModuleEffects::Miss(uint64 Target, double Now)
{
	Track(Target, Now);
	Misses = FMath::Min(3, Misses + 1);
	Chain = 0;
	LastHit = Now;
}

float FWLModuleEffects::CooldownMultiplier(double Now, FRandomStream& Rng) const
{
	float Reduction = 0.03f * Powers(EWLModuleEffect::Processor) + 0.03f * Powers(EWLModuleEffect::Recovery) * (Now < KillUntil ? 2 : 1);
	const int32 Adaptive = Powers(EWLModuleEffect::Adaptive);
	Reduction += FMath::Min(Chain, 5) * (0.02f * Powers(EWLModuleEffect::Overclock) + (Adaptive >= 2 ? 0.03f : Adaptive * 0.02f));
	if (Now < ServoUntil && Powers(EWLModuleEffect::Servo) > 0)
	{
		Reduction += 0.08f;
	}
	const int32 Loose = Powers(EWLModuleEffect::HasteProc);
	if (Loose > 0 && Rng.GetFraction() < 0.05f + 0.05f * Loose)
	{
		Reduction += 0.20f;
	}
	return FMath::Clamp(1.f - Reduction, 0.5f, 1.f);
}

FWLModuleOutgoing FWLModuleEffects::Outgoing(uint64 Target, int32 Damage, bool bRanged, int32 TargetHP,
	int32 PlayerHP, int32 PlayerMaxHP, double Now, FRandomStream& Rng)
{
	FWLModuleOutgoing Out;
	Out.Damage = Damage;
	if (Damage <= 0)
	{
		return Out;
	}
	Track(Target, Now);
	const int32 PriorMisses = Misses;
	Misses = 0;
	Chain = FMath::Min(8, Chain + 1);
	LastHit = Now;

	const int32 Capped = FMath::Min(5, Chain);
	const int32 Combo = Powers(EWLModuleEffect::Combo);
	float Bonus = 0.04f * Powers(EWLModuleEffect::Power) + 0.01f * Powers(EWLModuleEffect::Scanner) * Capped;
	if (Combo > 0) Bonus += (Combo >= 2 ? 0.03f : 0.02f) * FMath::Min(Chain, Combo >= 2 ? 8 : 5);
	if (Powers(EWLModuleEffect::Adaptive) >= 2) Bonus += 0.02f * Capped;
	if (Powers(EWLModuleEffect::Tactical) >= 2) Bonus += 0.01f * Capped;
	if (Powers(EWLModuleEffect::MissAdapt) >= 2) Bonus += 0.03f * PriorMisses;
	if (Powers(EWLModuleEffect::Reactive) >= 2 && Now < ReactiveUntil) Bonus += 0.05f;
	if (Powers(EWLModuleEffect::Emergency) >= 2 && PlayerHP < PlayerMaxHP * 0.3f) Bonus += 0.1f;
	if (Rng.GetFraction() < 0.05f * Powers(EWLModuleEffect::Critical)) Bonus += 0.5f;
	if (bRanged && Powers(EWLModuleEffect::Targeting) >= 2 && Rng.GetFraction() < 0.05f) Bonus += 0.5f;
	if (Rng.GetFraction() < 0.05f * Powers(EWLModuleEffect::DoubleStrike)) Bonus += 0.4f;
	if (Rng.GetFraction() < 0.08f * Powers(EWLModuleEffect::Burst)) Bonus += 0.5f;
	const int32 Cap = Powers(EWLModuleEffect::Capacitor);
	if (Cap > 0 && ++Charge >= (Cap >= 2 ? 3 : 5))
	{
		Charge = 0;
		Bonus += 0.3f;
	}

	const int32 Result = FMath::Max(1, FMath::RoundToInt(Damage * (1.f + Bonus)));
	const int32 Actual = FMath::Min(Result, TargetHP);
	if (Powers(EWLModuleEffect::Leech) > 0 && Actual > 0)
	{
		Out.LeechHeal = FMath::Max(1, FMath::FloorToInt(Actual * 0.03f * Powers(EWLModuleEffect::Leech)));
	}
	const int32 Serrated = Powers(EWLModuleEffect::Serrated);
	if (Rng.GetFraction() < 0.05f * Powers(EWLModuleEffect::Bleed) + 0.10f * Serrated)
	{
		Out.BleedDamage = FMath::Max(1, FMath::RoundToInt(Damage * 0.10f));
		Out.BleedMaxStacks = Serrated >= 2 ? 2 : 1;
	}
	if (Powers(EWLModuleEffect::Servo) > 0)
	{
		ServoUntil = Now + (Powers(EWLModuleEffect::Servo) >= 2 ? 6.0 : 3.0);
	}
	if (Result >= TargetHP)
	{
		KillUntil = Now + 5.0;
	}
	Out.Damage = Result;
	return Out;
}

bool FWLModuleEffects::AddBleed(uint64 Target, int32 Damage, int32 MaxStacks)
{
	if (FWLBleedState* Current = Bleeds.Find(Target))
	{
		Current->Damage = FMath::Max(Current->Damage, Damage);
		Current->Stacks = FMath::Min(MaxStacks, Current->Stacks + 1);
		Current->TicksLeft = 3;
		return false;
	}
	FWLBleedState& State = Bleeds.Add(Target);
	State.Damage = Damage;
	return true;
}

void FWLModuleEffects::TickBleeds(TFunctionRef<bool(uint64, int32)> Apply)
{
	for (auto It = Bleeds.CreateIterator(); It; ++It)
	{
		FWLBleedState& State = It.Value();
		if (State.TicksLeft <= 0 || !Apply(It.Key(), State.Damage * State.Stacks))
		{
			It.RemoveCurrent();
			continue;
		}
		if (--State.TicksLeft <= 0)
		{
			It.RemoveCurrent();
		}
	}
}

float FWLModuleEffects::ShieldCapacity() const
{
	return 6.f * Powers(EWLModuleEffect::Barrier) + 10.f * Powers(EWLModuleEffect::Nano);
}

void FWLModuleEffects::Regenerate(double Now)
{
	const double Elapsed = FMath::Max(0.0, Now - LastRegen);
	LastRegen = Now;
	Shield = FMath::Min(ShieldCapacity(), Shield + static_cast<float>(Elapsed) * Powers(EWLModuleEffect::Nano));
	const int32 Barrier = Powers(EWLModuleEffect::Barrier);
	if (Barrier > 0 && Now - LastIncoming >= (Barrier >= 2 ? 8.0 : 12.0))
	{
		Shield = FMath::Max(Shield, 6.f * Barrier);
	}
}

FWLModuleIncoming FWLModuleEffects::Incoming(int32 Damage, bool bAttackerAlive, bool bRanged,
	int32 PlayerHP, int32 PlayerMaxHP, double Now, FRandomStream& Rng)
{
	FWLModuleIncoming Out;
	if (Damage <= 0)
	{
		return Out;
	}
	Regenerate(Now);
	LastIncoming = Now;
	const int32 Original = Damage;

	if (Rng.GetFraction() < 0.05f * Powers(EWLModuleEffect::Block))
	{
		Out.bBlocked = true;
		if (Powers(EWLModuleEffect::Block) >= 2 && bAttackerAlive)
		{
			Out.ReturnDamage = FMath::Max(1, Original / 4);
		}
		return Out;
	}
	if (bRanged && Powers(EWLModuleEffect::Ballistic) >= 2 && Rng.GetFraction() < 0.05f)
	{
		Out.bBlocked = true;
		return Out;
	}

	float Reduction = 0.03f * Powers(EWLModuleEffect::Plates);
	if (Damage >= 10) Reduction += 0.10f * Powers(EWLModuleEffect::HeavyGuard);
	if (bRanged) Reduction += 0.10f * Powers(EWLModuleEffect::Ballistic);
	if (PlayerHP < PlayerMaxHP * 0.3f) Reduction += 0.10f * Powers(EWLModuleEffect::Emergency);
	Damage = FMath::Max(1, FMath::RoundToInt(Damage * (1.f - FMath::Min(0.6f, Reduction))));

	const int32 Absorbed = FMath::Min(Damage, FMath::FloorToInt(Shield));
	Shield -= Absorbed;
	Damage -= Absorbed;
	ReactiveUntil = Now + 4.0;

	if (bAttackerAlive)
	{
		int32 Reflect = Powers(EWLModuleEffect::Reflect) > 0 ? FMath::Max(1, FMath::FloorToInt(Original * 0.05f * Powers(EWLModuleEffect::Reflect))) : 0;
		if (Rng.GetFraction() < 0.08f * Powers(EWLModuleEffect::Deflect))
		{
			Reflect += FMath::Max(1, Original / 2);
		}
		Out.ReturnDamage = Reflect;
	}
	Out.Damage = Damage;
	return Out;
}
