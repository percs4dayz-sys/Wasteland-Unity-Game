#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS

#include "Combat/WLCombatMath.h"
#include "Combat/WLModuleEffects.h"
#include "Combat/WLEnemyBrain.h"
#include "Combat/WLWeaponMagazine.h"
#include "Skills/WLSkillMath.h"
#include "Core/WLXPTable.h"

#define WL_COMBAT_TEST_FLAGS (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLCombatMathTest, "Wastelanders.Combat.Math", WL_COMBAT_TEST_FLAGS)

bool FWLCombatMathTest::RunTest(const FString& Parameters)
{
	// OSRS formulas (CombatMath.cs).
	TestEqual(TEXT("Effective level"), FWLCombatMath::EffectiveLevel(1, 3), 12);
	TestEqual(TEXT("Attack roll L1 no gear"), FWLCombatMath::AttackRoll(1, 0, 0), 9 * 64);
	TestEqual(TEXT("Defence roll"), FWLCombatMath::DefenceRoll(10, 6, 3), 21 * 70);
	TestTrue(TEXT("Equal rolls are below 50%"), FWLCombatMath::HitChance(576, 576) < 0.5f);
	TestEqual(TEXT("atk<=def branch"), FWLCombatMath::HitChance(100, 199), 100.f / 400.f);
	TestEqual(TEXT("atk>def branch"), FWLCombatMath::HitChance(199, 100), 1.f - 102.f / 400.f);
	TestEqual(TEXT("Max hit never below 1"), FWLCombatMath::MaxHit(1, 0, 0), 1);
	// eff = 99+3+8 = 110; 110 * (64+64) / 640 = 22 -> floor(22.5) = 22
	TestEqual(TEXT("Max hit L99 +64 str aggressive"), FWLCombatMath::MaxHit(99, 64, 3), 22);

	FRandomStream Rng(1234);
	for (int32 i = 0; i < 200; ++i)
	{
		const int32 D = FWLCombatMath::RollDamage(5, Rng);
		if (D < 0 || D > 5) { AddError(TEXT("RollDamage out of 0..max")); break; }
	}

	// Stances (CombatManager.StancesFor / SetStyle clamp / StanceSkill).
	TestEqual(TEXT("Fission has one stance"), FWLCombatMath::StancesFor(EWLCombatStyle::Fission).Num(), 1);
	TestTrue(TEXT("Rapid is ranged"), FWLCombatMath::IsStanceAllowed(EWLCombatStyle::Ranged, EWLCombatStance::Rapid));
	TestFalse(TEXT("Rapid is not melee"), FWLCombatMath::IsStanceAllowed(EWLCombatStyle::Melee, EWLCombatStance::Rapid));
	TestTrue(TEXT("Distance clamps to Accurate on melee"), FWLCombatMath::ClampStance(EWLCombatStyle::Melee, EWLCombatStance::Longrange) == EWLCombatStance::Accurate);
	TestTrue(TEXT("Aggressive trains Strength"), FWLCombatMath::StanceSkill(EWLCombatStance::Aggressive, EWLCombatStyle::Melee) == EWLSkill::Strength);
	TestTrue(TEXT("Gauntlets train Fission"), FWLCombatMath::StanceSkill(EWLCombatStance::Accurate, EWLCombatStyle::Fission) == EWLSkill::Fission);
	TestTrue(TEXT("Guns train Marksmanship"), FWLCombatMath::StanceSkill(EWLCombatStance::Longrange, EWLCombatStyle::Ranged) == EWLSkill::Marksmanship);
	TestTrue(TEXT("Bare hands are melee"), FWLCombatMath::StyleForWeapon(EWLWeaponStyle::None) == EWLCombatStyle::Melee);
	TestEqual(TEXT("Distance defence +3"), FWLCombatMath::DefenceStanceBonus(EWLCombatStance::Longrange), 3);

	// Speed and reach (ActionCombat3D.CurrentSpeedTicks / ReachMeters).
	TestEqual(TEXT("Default speed 3"), FWLCombatMath::AttackSpeedTicks(0, EWLCombatStance::Accurate), 3);
	TestEqual(TEXT("Rapid min 2"), FWLCombatMath::AttackSpeedTicks(2, EWLCombatStance::Rapid), 2);
	TestEqual(TEXT("Rapid -1"), FWLCombatMath::AttackSpeedTicks(5, EWLCombatStance::Rapid), 4);
	TestEqual(TEXT("Aggressive +1"), FWLCombatMath::AttackSpeedTicks(4, EWLCombatStance::Aggressive), 5);
	TestEqual(TEXT("Melee reach 1 tile"), FWLCombatMath::ReachUnits(1, false, EWLCombatStance::Accurate, 190.f, 200.f), 190.f);
	TestEqual(TEXT("Gun 4 tiles + Distance"), FWLCombatMath::ReachUnits(4, true, EWLCombatStance::Longrange, 190.f, 200.f), 190.f + 5 * 200.f);
	TestEqual(TEXT("Distance ignored for melee"), FWLCombatMath::ReachUnits(1, false, EWLCombatStance::Longrange, 190.f, 200.f), 190.f);

	// Cooldown carry: a 3.88-tick weapon fired every time it is ready averages 3.88 ticks.
	{
		double ReadyAt = 0.0;
		int64 Tick = 0;
		int32 Swings = 0;
		const double Cooldown = 4.0 * 0.97;
		for (Tick = 0; Tick < 3880; ++Tick)
		{
			if (Tick >= ReadyAt)
			{
				ReadyAt = FWLCombatMath::NextReadyTick(Tick, ReadyAt, Cooldown);
				++Swings;
			}
		}
		TestTrue(TEXT("Fractional cooldowns are not rounded away"), Swings >= 999 && Swings <= 1001);
	}
	TestEqual(TEXT("An idle weapon earns no carry"), FWLCombatMath::NextReadyTick(100, 3.0, 4.0), 104.0);

	// Loot tier inference (CombatTarget.LootTier).
	TestEqual(TEXT("Explicit tier"), FWLCombatMath::LootTier(3, 10), 3);
	TestEqual(TEXT("40 HP -> 1"), FWLCombatMath::LootTier(0, 40), 1);
	TestEqual(TEXT("160 HP -> 3"), FWLCombatMath::LootTier(0, 160), 3);
	TestEqual(TEXT("450 HP -> 5"), FWLCombatMath::LootTier(0, 450), 5);

	// Magazine defaults.
	TestEqual(TEXT("Default mag 8"), FWLCombatMath::MagazineSize(0), 8);
	TestEqual(TEXT("Default reload 1.5"), FWLCombatMath::ReloadSeconds(0.f), 1.5f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLCombatCascadeTest, "Wastelanders.Combat.Cascade", WL_COMBAT_TEST_FLAGS)

bool FWLCombatCascadeTest::RunTest(const FString& Parameters)
{
	FRandomStream Rng(42);

	// Certain hit: the first blast lands in its band, then halves, the last mirrors the third.
	const TArray<int32> Hit = FWLCombatMath::CascadeDamage(40, 1.f, false, Rng);
	TestEqual(TEXT("Four hits"), Hit.Num(), 4);
	TestTrue(TEXT("First hit in band 25..50"), Hit[0] >= 25 && Hit[0] <= 50);
	TestEqual(TEXT("Second halves"), Hit[1], FMath::Max(1, Hit[0] / 2));
	TestEqual(TEXT("Third halves"), Hit[2], FMath::Max(1, Hit[1] / 2));
	TestEqual(TEXT("Fourth mirrors third"), Hit[3], Hit[2]);

	// Meltdown: flat 2x max on the first landing hit.
	const TArray<int32> Melt = FWLCombatMath::CascadeDamage(40, 1.f, true, Rng);
	TestEqual(TEXT("Meltdown 2x max"), Melt[0], 80);

	// Total whiff still chips 1.
	const TArray<int32> Whiff = FWLCombatMath::CascadeDamage(40, -1.f, false, Rng);
	TestEqual(TEXT("Whiff first three zero"), Whiff[0] + Whiff[1] + Whiff[2], 0);
	TestEqual(TEXT("Whiff chips 1"), Whiff[3], 1);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLCombatPerksTest, "Wastelanders.Combat.PerksAndXP", WL_COMBAT_TEST_FLAGS)

bool FWLCombatPerksTest::RunTest(const FString& Parameters)
{
	TestEqual(TEXT("Meltdown 1% at L1"), FWLCombatPerks::MeltdownPercent(1), 1);
	TestEqual(TEXT("Meltdown 5% at L45"), FWLCombatPerks::MeltdownPercent(45), 5);
	TestEqual(TEXT("Meltdown caps at 10%"), FWLCombatPerks::MeltdownPercent(99), 10);
	TestEqual(TEXT("No DR under 20"), FWLCombatPerks::DamageReduction(19), 0.f);
	TestEqual(TEXT("DR 5% at 20"), FWLCombatPerks::DamageReduction(20), 0.05f);
	TestEqual(TEXT("DR 25% at 99"), FWLCombatPerks::DamageReduction(99), 0.25f);
	TestEqual(TEXT("First strike only at 99"), FWLCombatPerks::FirstStrikeMultiplier(98, true), 1.f);
	TestEqual(TEXT("Consecutive caps at 5"), FWLCombatPerks::ConsecutiveMultiplier(99, 9), 1.3f);

	FRandomStream Rng(7);
	bool bAnyBelow = false;
	for (int32 i = 0; i < 100; ++i)
	{
		bAnyBelow |= FWLCombatPerks::Headshot(39, Rng);
	}
	TestFalse(TEXT("No headshots below L40"), bAnyBelow);

	// XP carry: three 1-damage hits give 4 XP to HP in total (1.333 each), never floored away.
	FWLCombatXP XP;
	int32 HP = 0;
	for (int32 i = 0; i < 3; ++i)
	{
		HP += XP.Add(EWLSkill::Endurance, 1 * FWLCombatXP::HPXPPerDamage);
	}
	TestEqual(TEXT("Fractional HP XP accumulates"), HP, 4);
	TestEqual(TEXT("Style XP 4 per damage"), XP.Add(EWLSkill::Attack, 5 * FWLCombatXP::XPPerDamage), 20);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLSkillMathTest, "Wastelanders.Combat.SkillMath", WL_COMBAT_TEST_FLAGS)

bool FWLSkillMathTest::RunTest(const FString& Parameters)
{
	// Fresh character: Endurance 10, everything else 1 -> floor(2.75 + 0.65) = 3.
	TestEqual(TEXT("Fresh combat level"), FWLSkillMath::CombatLevel(10, 1, 1, 1, 1, 1), 3);
	// Maxed: base 0.25*(220+99) = 79.75; melee 0.325*198 = 64.35 -> 144.
	TestEqual(TEXT("Maxed combat level"), FWLSkillMath::CombatLevel(220, 99, 99, 99, 99, 99), 144);
	// Pure ranged 60: 0.325*(30+60) = 29.25 beats melee 0.65; base 0.25*(50+1) = 12.75 -> 42.
	TestEqual(TEXT("Best style only"), FWLSkillMath::CombatLevel(50, 1, 1, 1, 60, 1), 42);
	TestEqual(TEXT("HP identity"), FWLSkillMath::HPForLevel(57), 57);
	TestEqual(TEXT("HP clamps low"), FWLSkillMath::HPForLevel(1), 10);
	TestEqual(TEXT("HP clamps high"), FWLSkillMath::HPForLevel(999), 220);
	TestFalse(TEXT("Fission not a tutorial skill"), FWLSkillMath::IsTutorialSkill(EWLSkill::Fission));
	TestTrue(TEXT("Attack is a tutorial skill (melee is live)"), FWLSkillMath::IsTutorialSkill(EWLSkill::Attack));
	TestEqual(TEXT("Endurance 0 XP = level 10"), FWLXPTable::LevelForXP(EWLSkill::Endurance, 0), 10);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLModuleEffectsTest, "Wastelanders.Combat.Modules", WL_COMBAT_TEST_FLAGS)

bool FWLModuleEffectsTest::RunTest(const FString& Parameters)
{
	FRandomStream Rng(99);

	// No modules: nothing changes.
	{
		FWLModuleEffects Fx;
		TestEqual(TEXT("Neutral cooldown"), Fx.CooldownMultiplier(0.0, Rng), 1.f);
		TestEqual(TEXT("Neutral outgoing"), Fx.Outgoing(1, 10, false, 100, 50, 50, 0.0, Rng).Damage, 10);
		TestEqual(TEXT("Neutral incoming"), Fx.Incoming(10, true, false, 50, 50, 0.0, Rng).Damage, 10);
	}
	// Processor 2: 6% shorter cooldown.
	{
		FWLModuleEffects Fx;
		FWLModulePowers P;
		P.Set(EWLModuleEffect::Processor, 2);
		Fx.SetPowers(P);
		TestTrue(TEXT("Processor cooldown"), FMath::IsNearlyEqual(Fx.CooldownMultiplier(0.0, Rng), 0.94f));
	}
	// Power 2: +8% damage.
	{
		FWLModuleEffects Fx;
		FWLModulePowers P;
		P.Set(EWLModuleEffect::Power, 2);
		Fx.SetPowers(P);
		TestEqual(TEXT("Hardened Edge +8%"), Fx.Outgoing(1, 100, false, 1000, 50, 50, 0.0, Rng).Damage, 108);
	}
	// Power clamps at 4.
	{
		FWLModulePowers P;
		P.Set(EWLModuleEffect::Power, 9);
		TestEqual(TEXT("Power cap 4"), P(EWLModuleEffect::Power), 4);
	}
	// Combat AI Chip: misses stack accuracy, reset on a different target.
	{
		FWLModuleEffects Fx;
		FWLModulePowers P;
		P.Set(EWLModuleEffect::MissAdapt, 1);
		Fx.SetPowers(P);
		Fx.Miss(7, 0.0);
		Fx.Miss(7, 0.1);
		TestEqual(TEXT("Two misses = +10"), Fx.Accuracy(7, false, 0.2), 10);
		TestEqual(TEXT("New target resets"), Fx.Accuracy(8, false, 0.3), 0);
	}
	// Plates 2 (6%) + Energy Barrier 1 (6 shield).
	{
		FWLModuleEffects Fx;
		FWLModulePowers P;
		P.Set(EWLModuleEffect::Plates, 2);
		P.Set(EWLModuleEffect::Barrier, 1);
		Fx.SetPowers(P);
		Fx.Regenerate(100.0);   // long since last hit: barrier charged
		const FWLModuleIncoming In = Fx.Incoming(20, false, false, 50, 50, 100.0, Rng);
		// 20 * 0.94 = 18.8 -> 19; minus 6 shield = 13.
		TestEqual(TEXT("Plates + barrier"), In.Damage, 13);
		TestEqual(TEXT("Shield spent"), Fx.GetShield(), 0.f);
	}
	// Welding Shield reflect 2: 10% of the original hit back.
	{
		FWLModuleEffects Fx;
		FWLModulePowers P;
		P.Set(EWLModuleEffect::Reflect, 2);
		Fx.SetPowers(P);
		TestEqual(TEXT("Reflect 10%"), Fx.Incoming(30, true, false, 50, 50, 0.0, Rng).ReturnDamage, 3);
	}
	// Bleed: 3 ticks then gone.
	{
		FWLModuleEffects Fx;
		TestTrue(TEXT("New bleed"), Fx.AddBleed(5, 2, 1));
		int32 Total = 0;
		for (int32 i = 0; i < 5; ++i)
		{
			Fx.TickBleeds([&Total](uint64, int32 D) { Total += D; return true; });
		}
		TestEqual(TEXT("Bleed 3 ticks x 2"), Total, 6);
		TestFalse(TEXT("Bleed ended"), Fx.HasBleed(5));
	}
	// Gear change resets streaks; an identical signature (ammo spent) does not.
	{
		FWLModuleEffects Fx;
		Fx.NotifyGear(1);
		Fx.Outgoing(3, 10, false, 100, 50, 50, 0.0, Rng);
		Fx.NotifyGear(1);
		TestEqual(TEXT("Same gear keeps chain"), Fx.GetChain(), 1);
		Fx.NotifyGear(2);
		TestEqual(TEXT("New gear resets chain"), Fx.GetChain(), 0);
	}
	// Reinforced Servos shave eat delay, min 1.
	{
		FWLModuleEffects Fx;
		FWLModulePowers P;
		P.Set(EWLModuleEffect::EatRecovery, 4);
		Fx.SetPowers(P);
		TestEqual(TEXT("Eat delay min 1"), Fx.EatDelay(3), 1);
	}
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLEnemyBrainTest, "Wastelanders.Combat.EnemyBrain", WL_COMBAT_TEST_FLAGS)

bool FWLEnemyBrainTest::RunTest(const FString& Parameters)
{
	TestTrue(TEXT("Aggressive targets in range"), FWLEnemyBrain::IsValidAggroTarget(true, 500.f, 900.f, true, false));
	TestFalse(TEXT("Passive ignores until provoked"), FWLEnemyBrain::IsValidAggroTarget(true, 500.f, 900.f, false, false));
	TestTrue(TEXT("Passive fights back"), FWLEnemyBrain::IsValidAggroTarget(true, 500.f, 900.f, false, true));
	TestFalse(TEXT("Out of aggro range"), FWLEnemyBrain::IsValidAggroTarget(true, 950.f, 900.f, true, true));
	TestFalse(TEXT("Dead targets ignored"), FWLEnemyBrain::IsValidAggroTarget(false, 10.f, 900.f, true, true));
	TestFalse(TEXT("Leash disabled at 0"), FWLEnemyBrain::ShouldLeash(99999.f, 0.f));

	FWLEnemyBrainInput In;
	In.bHasTarget = true;
	In.DistToTarget = 500.f;
	TestTrue(TEXT("Chase"), FWLEnemyBrain::Decide(In) == EWLEnemyAIState::Chase);
	In.DistToTarget = 100.f;
	TestTrue(TEXT("Attack in reach"), FWLEnemyBrain::Decide(In) == EWLEnemyAIState::Attack);
	In.DistToTarget = 500.f;
	In.bWindingUp = true;
	TestTrue(TEXT("Hold during windup"), FWLEnemyBrain::Decide(In) == EWLEnemyAIState::Attack);
	In = FWLEnemyBrainInput();
	In.DistFromHome = 400.f;
	TestTrue(TEXT("Return home"), FWLEnemyBrain::Decide(In) == EWLEnemyAIState::Return);
	In.DistFromHome = 10.f;
	TestTrue(TEXT("Idle at home"), FWLEnemyBrain::Decide(In) == EWLEnemyAIState::Idle);
	In.bHasWanderPoint = true;
	TestTrue(TEXT("Wander"), FWLEnemyBrain::Decide(In) == EWLEnemyAIState::Wander);
	In.bDead = true;
	TestTrue(TEXT("Dead"), FWLEnemyBrain::Decide(In) == EWLEnemyAIState::Dead);
	In = FWLEnemyBrainInput();
	In.bHasTarget = true;
	In.bReturningFromLeash = true;
	In.DistFromHome = 3000.f;
	TestTrue(TEXT("Leashed ignores target"), FWLEnemyBrain::Decide(In) == EWLEnemyAIState::Return);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLMagazineTest, "Wastelanders.Combat.Magazine", WL_COMBAT_TEST_FLAGS)

bool FWLMagazineTest::RunTest(const FString& Parameters)
{
	FWLWeaponMagazine Mag;
	TestEqual(TEXT("Never-fired weapon starts full"), Mag.GetLoaded(100, 6), 6);
	for (int32 i = 0; i < 5; ++i)
	{
		TestFalse(TEXT("Not empty yet"), Mag.NoteShotFired(100, 6));
	}
	TestTrue(TEXT("Sixth shot empties"), Mag.NoteShotFired(100, 6));
	TestEqual(TEXT("Other weapon unaffected (no free swaps)"), Mag.GetLoaded(101, 10), 10);
	Mag.Refill(100, 6);
	TestTrue(TEXT("Refilled"), Mag.IsFull(100, 6));
	return true;
}

#undef WL_COMBAT_TEST_FLAGS

#endif // WITH_DEV_AUTOMATION_TESTS
