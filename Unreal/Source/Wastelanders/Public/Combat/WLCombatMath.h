#pragma once

#include "CoreMinimal.h"
#include "Math/RandomStream.h"
#include "Core/WLTypes.h"

/**
 * Old School RuneScape-style combat math, shared by every combat path (player attacks, enemy
 * attacks, turrets, the special) so nothing can drift. Ported from Assets/Scripts/Combat/CombatMath.cs.
 *
 *   Effective level = base level + stance bonus + 8           (no prayer in this game)
 *   Attack roll     = effectiveAtk * (equipment attack bonus  + 64)
 *   Defence roll    = effectiveDef * (equipment defence bonus + 64)
 *   Hit chance      = atk>def ?  1 - (def+2)/(2*(atk+1))  :  atk/(2*(def+1))
 *   Max hit         = floor( 0.5 + effectiveStr * (strength bonus + 64) / 640 )
 *   Damage roll     = random 0..maxHit on a landed hit
 *
 * Every random function takes an FRandomStream so the automation tests can drive it
 * deterministically; on the server the combat component seeds its stream from FMath::Rand().
 */
struct WASTELANDERS_API FWLCombatMath
{
	/** Global damage tuning knob. 1.0 = pure OSRS (Unity CombatMath.DamageScale). */
	static constexpr float DamageScale = 1.f;

	static int32 EffectiveLevel(int32 Level, int32 StanceBonus) { return Level + StanceBonus + 8; }

	static int32 AttackRoll(int32 AtkLevel, int32 EquipAttackBonus, int32 StanceBonus)
	{
		return EffectiveLevel(AtkLevel, StanceBonus) * (EquipAttackBonus + 64);
	}

	static int32 DefenceRoll(int32 DefLevel, int32 EquipDefenceBonus, int32 StanceBonus)
	{
		return EffectiveLevel(DefLevel, StanceBonus) * (EquipDefenceBonus + 64);
	}

	static float HitChance(int32 AttackRoll, int32 DefenceRoll);

	/** Never below 1 (Unity: Mathf.Max(1, ...)). */
	static int32 MaxHit(int32 StrLevel, int32 EquipStrengthBonus, int32 StanceBonus);

	/** Damage of a landed hit: uniform 0..MaxHit inclusive (OSRS allows a 0). */
	static int32 RollDamage(int32 MaxHit, FRandomStream& Rng);

	/** Unity's Random.value <= chance test, as used by every accuracy roll. */
	static bool RollHit(float Chance, FRandomStream& Rng) { return Rng.GetFraction() <= Chance; }

	// --- stances (CombatManager.cs + the stance helpers in ActionCombat3D / Enemy3D) -------------

	/** Melee: Accurate/Aggressive/Defensive. Ranged: Accurate/Rapid/Distance. Fission: Accurate only. */
	static TArray<EWLCombatStance> StancesFor(EWLCombatStyle Style);
	static bool IsStanceAllowed(EWLCombatStyle Style, EWLCombatStance Stance);

	/**
	 * CombatManager.SetStyle: a stance the new style doesn't offer is clamped to its first option,
	 * because "a stance you cannot see is a stance you cannot get out of".
	 */
	static EWLCombatStance ClampStance(EWLCombatStyle Style, EWLCombatStance Stance);

	/** Bare hands count as melee; gauntlets are their own style. */
	static EWLCombatStyle StyleForWeapon(EWLWeaponStyle WeaponStyle);

	/** The skill a stance trains (CombatManager.StanceSkill). */
	static EWLSkill StanceSkill(EWLCombatStance Stance, EWLCombatStyle Style);

	/** Accurate -> +3 accuracy. */
	static int32 AccuracyStanceBonus(EWLCombatStance Stance) { return Stance == EWLCombatStance::Accurate ? 3 : 0; }

	/** Aggressive -> +3 strength. */
	static int32 StrengthStanceBonus(EWLCombatStance Stance) { return Stance == EWLCombatStance::Aggressive ? 3 : 0; }

	/** Hidden defensive modifier (Enemy3D.DefenceStanceBonus): Defensive/Distance +3, Controlled +1. */
	static int32 DefenceStanceBonus(EWLCombatStance Stance);

	/** OSRS Longrange: the Distance option adds 2 tiles of reach (ranged only). */
	static int32 LongrangeBonusTiles(EWLCombatStance Stance) { return Stance == EWLCombatStance::Longrange ? 2 : 0; }

	/** Weapon cadence in ticks: default 3; Rapid is one faster (min 2); Aggressive one slower. */
	static int32 AttackSpeedTicks(int32 WeaponSpeedTicks, EWLCombatStance Stance);

	/**
	 * Reach in Unreal units (ActionCombat3D.ReachMeters): MeleeReach at 1 tile, each extra tile of
	 * weapon range adds PerTile; ranged weapons also get the Distance stance's +2 tiles.
	 */
	static float ReachUnits(int32 WeaponRangeTiles, bool bRanged, EWLCombatStance Stance, float MeleeReach, float PerTile);

	// --- attack loop timing -----------------------------------------------------------------------

	/**
	 * Next game tick (fractional) the weapon is ready again. Unity timed swings in real seconds, so a
	 * module's 3% cooldown cut was real. On a whole-tick clock it would round away, so the lateness of
	 * this swing (how far past ReadyAt it fired, capped under one tick) is carried into the next one.
	 * Sustained fire then averages exactly CooldownTicks per swing.
	 */
	static double NextReadyTick(int64 CurrentTick, double ReadyAt, double CooldownTicks);

	// --- Fission special: Overload Cascade (ActionCombat3D.CascadeDamage) --------------------------

	static constexpr int32 SpecialHits = 4;
	static constexpr float SpecialAccuracyMult = 1.5f;
	static constexpr float SpecialEnergyCost = 50.f;

	/**
	 * Dragon-claws pattern: accuracy is rolled hit by hit; the first success rolls a damage band
	 * (earlier = higher) and later hits halve, the last mirroring the third. A total whiff still
	 * chips 1. Meltdown makes the first landing hit a flat 2x max, scaling the whole burst.
	 */
	static TArray<int32> CascadeDamage(int32 MaxHit, float HitChance, bool bMeltdown, FRandomStream& Rng);

	// --- loot / magazine helpers -------------------------------------------------------------------

	/** CombatTarget.LootTier: explicit tier when set, else inferred from max HP (40/90/160/280/450 mobs). */
	static int32 LootTier(int32 ExplicitTier, int32 MaxHP);

	/** WeaponMagazine defaults for weapons without explicit data: 8 rounds, 1.5 s. */
	static int32 MagazineSize(int32 ItemMagazineSize) { return ItemMagazineSize > 0 ? ItemMagazineSize : 8; }
	static float ReloadSeconds(float ItemReloadSeconds) { return ItemReloadSeconds > 0.f ? ItemReloadSeconds : 1.5f; }
};

/**
 * Combat-skill milestone passives (Assets/Scripts/Combat/CombatEffects.cs). Levels are the player's
 * Attack ("melee"), Strength ("brutality"), Defence ("hardening"), Marksmanship and Fission.
 */
struct WASTELANDERS_API FWLCombatPerks
{
	// Attack L60: armour bypass, ignore the target's defence (melee only).
	static bool ArmorBypass(int32 Attack, FRandomStream& Rng) { return Attack >= 60 && Rng.GetFraction() < 0.10f; }

	// Strength L20: power attack.
	static constexpr float PowerAttackMult = 1.4f;
	static bool PowerAttack(int32 Strength, FRandomStream& Rng) { return Strength >= 20 && Rng.GetFraction() < 0.12f; }

	// Strength L40: stagger, delays the target's next swing.
	static constexpr float StaggerSeconds = 1.2f;
	static bool Stagger(int32 Strength, FRandomStream& Rng) { return Strength >= 40 && Rng.GetFraction() < 0.15f; }

	// Strength L80: cleave, splash onto enemies near the target (radius in metres in Unity -> uu here).
	static constexpr float CleaveRadius = 250.f;
	static constexpr float CleaveFraction = 0.5f;
	static bool Cleave(int32 Strength, FRandomStream& Rng) { return Strength >= 80 && Rng.GetFraction() < 0.20f; }

	// Strength L99: first strike on a freshly engaged target, and a boss bonus.
	static constexpr int32 FirstStrikeLevel = 99;
	static constexpr float FirstStrikeMult = 1.5f;
	static float FirstStrikeMultiplier(int32 Strength, bool bFirstStrike) { return (bFirstStrike && Strength >= FirstStrikeLevel) ? FirstStrikeMult : 1.f; }
	static float BossMultiplier(int32 Strength, bool bBoss) { return (bBoss && Strength >= 99) ? 1.25f : 1.f; }

	/** Defence L20 5% / L60 15% / L99 25% passive reduction (Unity's "at low HP" note was never coded; neither is it here). */
	static float DamageReduction(int32 Defence);
	// Defence L40: Iron Will. Unity defines it but players are never staggered, so nothing calls it yet.
	static bool IgnoreStagger(int32 Defence, FRandomStream& Rng) { return Defence >= 40 && Rng.GetFraction() < 0.10f; }
	// Defence L60: 5% to negate an incoming hit outright.
	static bool NegateHit(int32 Defence, FRandomStream& Rng) { return Defence >= 60 && Rng.GetFraction() < 0.05f; }
	// Defence L99: 8% to reflect a landed hit back at the attacker.
	static bool ReflectDamage(int32 Defence, FRandomStream& Rng) { return Defence >= 99 && Rng.GetFraction() < 0.08f; }

	// Marksmanship L40: headshot.
	static constexpr float HeadshotMult = 1.6f;
	static bool Headshot(int32 Marks, FRandomStream& Rng) { return Marks >= 40 && Rng.GetFraction() < 0.12f; }
	// Marksmanship L99: consecutive hits on the same target ramp damage, +6% per hit up to 5.
	static float ConsecutiveMultiplier(int32 Marks, int32 Streak) { return Marks >= 99 ? 1.f + FMath::Min(Streak, 5) * 0.06f : 1.f; }

	// Fission: Meltdown, 1% at level 1, +1% per 10 levels, 10% cap; deals exactly 2x the core's max.
	static constexpr int32 MeltdownMaxPercent = 10;
	static constexpr int32 MeltdownMultiplier = 2;
	static int32 MeltdownPercent(int32 Fission) { return FMath::Clamp(Fission / 10 + 1, 1, MeltdownMaxPercent); }
	static bool Meltdown(int32 Fission, FRandomStream& Rng) { return Rng.GetFraction() < MeltdownPercent(Fission) / 100.f; }
};

/**
 * Fractional combat XP (ActionCombat3D.GiveXP): 4 XP per damage to the style skill and 4/3 to
 * Endurance, with the remainder carried per skill so 1.333-per-hit awards add up to the exact total.
 */
struct WASTELANDERS_API FWLCombatXP
{
	static constexpr float XPPerDamage = 4.f;
	static constexpr float HPXPPerDamage = 4.f / 3.f;

	/** Returns the whole XP to award now and keeps the fraction. */
	int32 Add(EWLSkill Skill, float Amount);

	void Reset() { Remainder.Reset(); }

private:
	TMap<EWLSkill, float> Remainder;
};
