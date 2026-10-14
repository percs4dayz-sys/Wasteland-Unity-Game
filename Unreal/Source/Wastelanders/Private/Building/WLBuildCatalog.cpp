#include "Building/WLBuildCatalog.h"
#include "World/WLWorldSettings.h"

#define LOCTEXT_NAMESPACE "WLBuildCatalog"

bool FWLBuildRules::CanAfford(TFunctionRef<int32(int32)> CountOf, const FWLBuildable& B)
{
	for (const FWLBuildCost& C : B.Cost)
	{
		if (CountOf(C.ItemId) < C.Quantity)
		{
			return false;
		}
	}
	return true;
}

bool FWLBuildRules::IsPlacementReachable(const FVector& Builder, const FVector& Target, float MaxDistance)
{
	if (Target.ContainsNaN() || Builder.ContainsNaN())
	{
		return false;
	}
	return FVector::DistSquared(Builder, Target) <= FMath::Square(MaxDistance);
}

float FWLBuildRules::NormalizeYaw(float Yaw)
{
	if (!FMath::IsFinite(Yaw))
	{
		return 0.f;
	}
	const float Wrapped = FMath::Fmod(Yaw, 360.f);
	return Wrapped < 0.f ? Wrapped + 360.f : Wrapped;
}

const UWLBuildCatalog* UWLBuildCatalog::Get()
{
	const UWLWorldSettings* Settings = UWLWorldSettings::Get();
	if (Settings && !Settings->BuildCatalog.IsNull())
	{
		if (const UWLBuildCatalog* Loaded = Settings->BuildCatalog.LoadSynchronous())
		{
			return Loaded;
		}
	}
	return GetDefault<UWLBuildCatalog>();
}

namespace
{
	// Unity metres -> uu.
	FVector M(float X, float Y, float Z) { return FVector(X, Y, Z) * 100.f; }

	void AddCost(FWLBuildable& B, int32 ItemId, int32 Qty)
	{
		if (Qty > 0)
		{
			FWLBuildCost C;
			C.ItemId = ItemId;
			C.Quantity = Qty;
			B.Cost.Add(C);
		}
	}

	FWLBuildable Make(const FText& Name, const FText& Desc, int32 Lvl, int32 XP, EWLBuildKind Kind, FLinearColor Color,
		int32 Id1, int32 Q1, int32 Id2 = 0, int32 Q2 = 0)
	{
		FWLBuildable B;
		B.Name = Name; B.Description = Desc; B.HearthLevel = Lvl; B.XP = XP; B.Kind = Kind; B.Color = Color;
		AddCost(B, Id1, Q1);
		AddCost(B, Id2, Q2);
		return B;
	}

	FWLBuildable MakeAFK(const FText& Name, const FText& Desc, int32 Lvl, int32 XP, EWLSkill Skill, int32 Produce, int32 Consume,
		FLinearColor Color, int32 Id1, int32 Q1, int32 Id2 = 0, int32 Q2 = 0)
	{
		FWLBuildable B = Make(Name, Desc, Lvl, XP, EWLBuildKind::AFKStation, Color, Id1, Q1, Id2, Q2);
		B.AFKSkill = Skill; B.AFKProduceItemId = Produce; B.AFKConsumeItemId = Consume;
		return B;
	}

	FWLBuildable MakeTurret(const FText& Name, const FText& Desc, int32 Lvl, int32 XP, int32 MaxHit, float RangeM,
		FLinearColor Color, int32 Id1, int32 Q1)
	{
		FWLBuildable B = Make(Name, Desc, Lvl, XP, EWLBuildKind::Turret, Color, Id1, Q1);
		B.Size = M(0.8f, 0.8f, 1.6f);
		B.TurretMaxHit = MaxHit; B.TurretRange = RangeM * 100.f;
		return B;
	}
}

UWLBuildCatalog::UWLBuildCatalog()
{
	// L1: the basics
	FWLBuildable Campfire = Make(LOCTEXT("Campfire", "Campfire"), LOCTEXT("CampfireD", "A cook surface. Roast raw catches here."),
		1, 20, EWLBuildKind::CookingFire, FLinearColor(0.5f, 0.28f, 0.12f), 40, 3);
	Buildables.Add(Campfire);

	FWLBuildable Foundation = Make(LOCTEXT("Foundation", "Foundation Shelter"), LOCTEXT("FoundationD", "Designates this spot as your housing plot."),
		1, 40, EWLBuildKind::HousingPlot, FLinearColor(0.45f, 0.4f, 0.32f), 40, 5, 11, 3);
	Foundation.Size = M(2.4f, 2.4f, 0.4f);
	Buildables.Add(Foundation);

	// AFK gathering stations (passive resource OR XP)
	Buildables.Add(MakeAFK(LOCTEXT("Sorting", "Scrap Sorting Bench"), LOCTEXT("SortingD", "Sorts scrap on its own, or burns it for Scrapping XP."),
		20, 60, EWLSkill::Scrapping, 10, 10, FLinearColor(0.5f, 0.5f, 0.52f), 11, 5));
	Buildables.Add(MakeAFK(LOCTEXT("Chopping", "Chopping Frame"), LOCTEXT("ChoppingD", "Strips timber on its own, or processes it for Woodcutting XP."),
		30, 80, EWLSkill::Woodcutting, 40, 40, FLinearColor(0.42f, 0.3f, 0.16f), 11, 5, 40, 3));
	Buildables.Add(MakeAFK(LOCTEXT("Fishery", "Indoor Fishery Tank"), LOCTEXT("FisheryD", "Farms fish on its own, or tends them for Fishing XP."),
		35, 90, EWLSkill::Fishing, 22, 22, FLinearColor(0.2f, 0.42f, 0.5f), 11, 5, 40, 3));
	Buildables.Add(MakeAFK(LOCTEXT("Forge", "Forge Station"), LOCTEXT("ForgeD", "Smelts scrap into bars, or refines them for Tinkering XP."),
		50, 140, EWLSkill::Smithing, 11, 10, FLinearColor(0.4f, 0.28f, 0.22f), 70, 8));
	Buildables.Add(MakeAFK(LOCTEXT("Barrel", "Burn Barrel"), LOCTEXT("BarrelD", "Burns wood for steady Hearthcraft XP."),
		60, 160, EWLSkill::Smithing, 0, 40, FLinearColor(0.35f, 0.2f, 0.14f), 70, 6));

	// deployable crafting surfaces
	Buildables.Add(Make(LOCTEXT("Kitchen", "Field Kitchen"), LOCTEXT("KitchenD", "A sturdy deployable cook surface."),
		40, 120, EWLBuildKind::CookingFire, FLinearColor(0.5f, 0.3f, 0.16f), 70, 6));
	FWLBuildable Bench = Make(LOCTEXT("Bench", "Portable Workbench"), LOCTEXT("BenchD", "Build gear, weapons and ammo in the field."),
		60, 180, EWLBuildKind::Workbench, FLinearColor(0.3f, 0.3f, 0.34f), 70, 8);
	Bench.Size = M(1.6f, 0.9f, 1.1f);
	Buildables.Add(Bench);
	FWLBuildable Furnace = Make(LOCTEXT("Furnace", "Portable Furnace"), LOCTEXT("FurnaceD", "A deployable smelter."),
		60, 180, EWLBuildKind::Furnace, FLinearColor(0.35f, 0.22f, 0.18f), 70, 8);
	Furnace.Size = M(1.4f, 1.4f, 1.6f);
	Buildables.Add(Furnace);

	// defenses
	Buildables.Add(MakeTurret(LOCTEXT("Turret1", "Basic Turret"), LOCTEXT("Turret1D", "Auto-fires on hostiles that wander close."),
		60, 200, 3, 8.f, FLinearColor(0.3f, 0.34f, 0.4f), 70, 10));
	Buildables.Add(MakeTurret(LOCTEXT("Turret2", "Advanced Turret"), LOCTEXT("Turret2D", "A heavier auto-turret with longer reach."),
		80, 320, 8, 11.f, FLinearColor(0.28f, 0.3f, 0.45f), 72, 12));

	// endgame utility
	Buildables.Add(Make(LOCTEXT("Bank", "Deployable Bank Chest"), LOCTEXT("BankD", "Stash and withdraw your gear anywhere."),
		80, 300, EWLBuildKind::BankChest, FLinearColor(0.3f, 0.34f, 0.2f), 72, 10));
}

#undef LOCTEXT_NAMESPACE
