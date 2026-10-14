#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS

#include "Building/WLBuildCatalog.h"
#include "Building/WLTurret.h"
#include "World/WLAFKStation.h"

#define WL_WORLD_TEST_FLAGS (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLBuildCatalogTest, "Wastelanders.World.BuildCatalog", WL_WORLD_TEST_FLAGS)
bool FWLBuildCatalogTest::RunTest(const FString& Parameters)
{
	const UWLBuildCatalog* Catalog = GetDefault<UWLBuildCatalog>();
	TestEqual(TEXT("Unity catalog has 13 buildables"), Catalog->Buildables.Num(), 13);

	// Spot-check the Unity numbers.
	const FWLBuildable& Fire = Catalog->Buildables[0];
	TestEqual(TEXT("Campfire level"), Fire.HearthLevel, 1);
	TestEqual(TEXT("Campfire xp"), Fire.XP, 20);
	TestEqual(TEXT("Campfire cost entries"), Fire.Cost.Num(), 1);
	TestEqual(TEXT("Campfire wood id"), Fire.Cost[0].ItemId, 40);
	TestEqual(TEXT("Foundation has two costs"), Catalog->Buildables[1].Cost.Num(), 2);
	TestTrue(TEXT("Foundation is a housing plot"), Catalog->Buildables[1].Kind == EWLBuildKind::HousingPlot);
	TestEqual(TEXT("Basic turret range is 8 m"), Catalog->Buildables[10].TurretRange, 800.f);
	TestEqual(TEXT("Advanced turret max hit"), Catalog->Buildables[11].TurretMaxHit, 8);

	// Cost / level rules.
	TMap<int32, int32> Held;
	Held.Add(40, 2);
	auto CountOf = [&Held](int32 Id) { const int32* N = Held.Find(Id); return N ? *N : 0; };
	TestFalse(TEXT("2 wood can't buy a campfire"), FWLBuildRules::CanAfford(CountOf, Fire));
	Held[40] = 3;
	TestTrue(TEXT("3 wood can"), FWLBuildRules::CanAfford(CountOf, Fire));
	TestFalse(TEXT("Level 19 can't build L20"), FWLBuildRules::MeetsLevel(19, Catalog->Buildables[2]));
	TestTrue(TEXT("Level 20 can"), FWLBuildRules::MeetsLevel(20, Catalog->Buildables[2]));

	// Placement.
	TestTrue(TEXT("Near is reachable"), FWLBuildRules::IsPlacementReachable(FVector::ZeroVector, FVector(1000, 0, 0), 1500.f));
	TestFalse(TEXT("Far is not"), FWLBuildRules::IsPlacementReachable(FVector::ZeroVector, FVector(2000, 0, 0), 1500.f));
	TestFalse(TEXT("NaN is not"), FWLBuildRules::IsPlacementReachable(FVector::ZeroVector, FVector(NAN, 0, 0), 1500.f));
	TestEqual(TEXT("Yaw wraps"), FWLBuildRules::NormalizeYaw(405.f), 45.f);
	TestEqual(TEXT("Negative yaw wraps"), FWLBuildRules::NormalizeYaw(-45.f), 315.f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLTurretLogicTest, "Wastelanders.World.TurretLogic", WL_WORLD_TEST_FLAGS)
bool FWLTurretLogicTest::RunTest(const FString& Parameters)
{
	int32 Counter = 0;
	TestFalse(TEXT("Tick 1 of 2"), AWLTurret::AdvanceShotTimer(Counter, 2));
	TestTrue(TEXT("Tick 2 fires"), AWLTurret::AdvanceShotTimer(Counter, 2));
	TestEqual(TEXT("Counter resets"), Counter, 0);
	for (int32 i = 0; i < 200; ++i)
	{
		const int32 D = AWLTurret::RollDamage(3);
		TestTrue(TEXT("Damage in 1..3"), D >= 1 && D <= 3);
	}
	TestEqual(TEXT("MaxHit 0 still hits 1"), AWLTurret::RollDamage(0), 1);
	TestTrue(TEXT("Inside range"), AWLTurret::IsInRange(99.f * 99.f, 100.f));
	TestFalse(TEXT("On the edge is out (strict <)"), AWLTurret::IsInRange(100.f * 100.f, 100.f));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLAFKModeTest, "Wastelanders.World.AFKModeCycle", WL_WORLD_TEST_FLAGS)
bool FWLAFKModeTest::RunTest(const FString& Parameters)
{
	using M = EWLAFKMode;
	TestTrue(TEXT("Off -> Resource"), AWLAFKStation::NextMode(M::Off, true, true) == M::Resource);
	TestTrue(TEXT("Resource -> Process"), AWLAFKStation::NextMode(M::Resource, true, true) == M::Process);
	TestTrue(TEXT("Process -> Off"), AWLAFKStation::NextMode(M::Process, true, true) == M::Off);
	TestTrue(TEXT("Off skips Resource when it cannot produce"), AWLAFKStation::NextMode(M::Off, false, true) == M::Process);
	TestTrue(TEXT("Resource -> Off when it cannot process"), AWLAFKStation::NextMode(M::Resource, true, false) == M::Off);
	TestTrue(TEXT("Nothing possible stays Off"), AWLAFKStation::NextMode(M::Off, false, false) == M::Off);
	return true;
}

#endif
