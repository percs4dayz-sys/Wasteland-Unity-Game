#pragma once

#include "CoreMinimal.h"
#include "Math/RandomStream.h"
#include "Combat/WLCombatTypes.h"

/**
 * Socketed-module power per effect: the sum of module grades worn for that effect, capped at 4
 * (ModuleCatalog.Power). Snapshotted from UWLEquipmentComponent::ModulePower so the proc logic
 * below stays a pure, testable value type.
 */
struct WASTELANDERS_API FWLModulePowers
{
	int32 Values[static_cast<int32>(EWLModuleEffect::Count)] = {};

	int32 operator()(EWLModuleEffect Effect) const { return Values[static_cast<int32>(Effect)]; }
	void Set(EWLModuleEffect Effect, int32 Power) { Values[static_cast<int32>(Effect)] = FMath::Clamp(Power, 0, 4); }
};

/** Outgoing-hit result (ModuleEffects.Outgoing). */
struct FWLModuleOutgoing
{
	int32 Damage = 0;
	/** Blood Reactor heal for the attacker. */
	int32 LeechHeal = 0;
	/** > 0 = start/refresh a bleed on the target for this much per tick. */
	int32 BleedDamage = 0;
	int32 BleedMaxStacks = 1;
};

/** Incoming-hit result (ModuleEffects.Incoming). */
struct FWLModuleIncoming
{
	/** Damage the player actually takes after reductions and shield. */
	int32 Damage = 0;
	/** Damage sent back to the attacker (Riot Plating counter + Welding Shield reflect + Deflection Field). */
	int32 ReturnDamage = 0;
	bool bBlocked = false;
};

/** One target's bleed (3 ticks, stacking per Serrated Refined). */
struct FWLBleedState
{
	int32 Damage = 0;
	int32 Stacks = 1;
	int32 TicksLeft = 3;
};

/**
 * Per-player module combat state, ported from Assets/Scripts/Combat/ModuleEffects.cs. Covers every
 * combat effect of the 35 in ModuleCatalog; the three non-combat ones are handled elsewhere:
 *   Salvage -> FWLEnemyLootParams.SalvagePower (Items), Gather -> the gathering loop (Skilling),
 *   Magnet  -> ground-item pickup (Items). Visor/Tactical/Processor stat bonuses are plain item
 *   bonuses on the module rows, so they arrive through the equipment totals.
 *
 * Time is passed in (world seconds) and targets are opaque keys, so the automation tests can drive
 * it without a world. Proc damage never recursively procs more modules (same as Unity).
 */
class WASTELANDERS_API FWLModuleEffects
{
public:
	void SetPowers(const FWLModulePowers& InPowers) { Powers = InPowers; }
	const FWLModulePowers& GetPowers() const { return Powers; }

	/**
	 * Gear changed. Spending ammo is also an equipment change in Unity, so only a real change of the
	 * non-ammo/tool loadout (the signature) resets combat streaks and clamps the shield.
	 */
	void NotifyGear(uint32 GearSignature);

	/** Bonus accuracy: Combat AI Chip miss stacks + Targeting HUD for ranged. */
	int32 Accuracy(uint64 Target, bool bRanged, double Now);

	/** Reactive Armor defence bonus for 4 s after being hit. */
	int32 Defence(double Now) const;

	/** Target's defence level after Resonance Core shred (up to -40%). */
	int32 TargetDefence(uint64 Target, int32 TargetDefenceLevel, double Now);

	void Miss(uint64 Target, double Now);

	/** Attack cooldown multiplier in [0.5, 1]: Processor, Recovery, Overclock, Adaptive, Servo, HasteProc. */
	float CooldownMultiplier(double Now, FRandomStream& Rng) const;

	/** Reinforced Servos: shave ticks off the post-eat attack delay (min 1). */
	int32 EatDelay(int32 Ticks) const { return FMath::Max(1, Ticks - Powers(EWLModuleEffect::EatRecovery)); }

	/** Outgoing landed hit: damage bonuses, crits, leech, bleed, servo/kill windows. */
	FWLModuleOutgoing Outgoing(uint64 Target, int32 Damage, bool bRanged, int32 TargetHP,
		int32 PlayerHP, int32 PlayerMaxHP, double Now, FRandomStream& Rng);

	/** Incoming landed hit: block, ballistic negate, % reductions, shield absorb, reflect. */
	FWLModuleIncoming Incoming(int32 Damage, bool bAttackerAlive, bool bRanged,
		int32 PlayerHP, int32 PlayerMaxHP, double Now, FRandomStream& Rng);

	/** Start or refresh a bleed. Returns true when it is a new bleed. */
	bool AddBleed(uint64 Target, int32 Damage, int32 MaxStacks);

	/**
	 * Advance every bleed one game tick. Calls Apply(Target, Damage) per bleeding target; Apply
	 * returns false if the target is gone/dead, which ends that bleed.
	 */
	void TickBleeds(TFunctionRef<bool(uint64 /*Target*/, int32 /*Damage*/)> Apply);

	/** Energy/Nanofiber barrier capacity. */
	float ShieldCapacity() const;
	float GetShield() const { return Shield; }

	/** Shield regen; call before reading the shield. */
	void Regenerate(double Now);

	/** Player died or gear changed: drop streaks and timed windows. */
	void ResetCombat();

	int32 GetChain() const { return Chain; }
	int32 GetMisses() const { return Misses; }

private:
	void Track(uint64 Target, double Now);

	FWLModulePowers Powers;
	uint64 CurrentTarget = 0;
	int32 Chain = 0;
	int32 Misses = 0;
	int32 Charge = 0;
	double LastHit = -1000.0;
	double ReactiveUntil = 0.0;
	double KillUntil = 0.0;
	double ServoUntil = 0.0;
	double LastIncoming = -100.0;
	double LastRegen = 0.0;
	float Shield = 0.f;
	uint32 GearSignature = 0;
	bool bHasGearSignature = false;
	TMap<uint64, FWLBleedState> Bleeds;
};
