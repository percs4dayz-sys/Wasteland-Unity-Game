#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS

#include "Items/WLItemRules.h"
#include "Items/WLItemTypes.h"
#include "Items/WLDropTableAsset.h"
#include "Items/WLLootLibrary.h"
#include "Items/WLLootSettings.h"
#include "Items/WLWeaponSkins.h"
#include "Data/WLDataTypes.h"

namespace WLItemsTest
{
	constexpr int32 Sword = 1;    // non-stackable weapon
	constexpr int32 Scrap = 10;   // non-stackable resource
	constexpr int32 Ammo = 13;    // stackable
	constexpr int32 Module = 600; // stackable module

	/** Tiny fake item table so the production rules run without DataTables. */
	struct FFakeItems
	{
		TMap<int32, FWLItemRow> Rows;

		FFakeItems()
		{
			Add(Sword, EWLItemType::Weapon, false);
			Add(Scrap, EWLItemType::Resource, false);
			Add(Ammo, EWLItemType::Ammo, true);
			Add(Module, EWLItemType::Resource, true);
		}

		void Add(int32 Id, EWLItemType Type, bool bStack)
		{
			FWLItemRow& R = Rows.Add(Id);
			R.ItemId = Id;
			R.ItemType = Type;
			R.bStackable = bStack;
		}

		const FWLItemRow* operator()(int32 Id) const { return Rows.Find(Id); }
	};

	FWLDropRoll MakeRoll(float Chance, bool bSalvage, int32 ItemId, int32 Min, int32 Max, float Weight)
	{
		FWLDropRoll Roll;
		Roll.Chance = Chance;
		Roll.bSalvageBonus = bSalvage;
		FWLDropOutcome Outcome;
		Outcome.ItemId = ItemId;
		Outcome.MinQuantity = Min;
		Outcome.MaxQuantity = Max;
		Outcome.Weight = Weight;
		Roll.Outcomes.Add(Outcome);
		return Roll;
	}

	TArray<FWLItemStack> EmptyInventory()
	{
		TArray<FWLItemStack> Slots;
		Slots.SetNum(FWLItemRules::InventoryMaxSize);
		return Slots;
	}
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLInventoryRulesTest, "Wastelanders.Items.InventoryRules",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLInventoryRulesTest::RunTest(const FString& Parameters)
{
	using namespace WLItemsTest;
	const FFakeItems Items;
	auto Lookup = [&Items](int32 Id) { return Items(Id); };
	const int32 Cap = FWLItemRules::InventoryBaseSize;

	TArray<FWLItemStack> Inv = EmptyInventory();
	TestEqual(TEXT("28 free"), FWLItemRules::FreeSlots(Inv, Cap), 28);

	// Non-stackables: one per slot.
	TestTrue(TEXT("Add 3 scrap"), FWLItemRules::InventoryAdd(Inv, Cap, Scrap, 3, Lookup));
	TestEqual(TEXT("3 slots used"), FWLItemRules::FreeSlots(Inv, Cap), 25);
	TestEqual(TEXT("Count 3"), FWLItemRules::CountOf(Inv, Scrap), 3);

	// Stackables merge.
	TestTrue(TEXT("Add ammo"), FWLItemRules::InventoryAdd(Inv, Cap, Ammo, 50, Lookup));
	TestTrue(TEXT("Add more ammo"), FWLItemRules::InventoryAdd(Inv, Cap, Ammo, 25, Lookup));
	TestEqual(TEXT("One ammo slot"), FWLItemRules::FreeSlots(Inv, Cap), 24);
	TestEqual(TEXT("75 ammo"), FWLItemRules::CountOf(Inv, Ammo), 75);

	// Unknown items are refused.
	TestFalse(TEXT("Unknown id"), FWLItemRules::InventoryAdd(Inv, Cap, 99999, 1, Lookup));
	TestFalse(TEXT("Zero qty"), FWLItemRules::InventoryAdd(Inv, Cap, Scrap, 0, Lookup));

	// All-or-nothing for non-stackables.
	TestFalse(TEXT("Needs 25 free slots"), FWLItemRules::InventoryAdd(Inv, Cap, Scrap, 25, Lookup));
	TestEqual(TEXT("Nothing added on failure"), FWLItemRules::CountOf(Inv, Scrap), 3);

	// Remove across slots; never half-removes (the furnace dupe).
	TestFalse(TEXT("Can't remove 4"), FWLItemRules::InventoryRemove(Inv, Scrap, 4));
	TestEqual(TEXT("Still 3"), FWLItemRules::CountOf(Inv, Scrap), 3);
	TestTrue(TEXT("Remove 3 across slots"), FWLItemRules::InventoryRemove(Inv, Scrap, 3));
	TestEqual(TEXT("None left"), FWLItemRules::CountOf(Inv, Scrap), 0);

	// RemoveAt.
	const int32 AmmoSlot = FWLItemRules::FindFirst(Inv, Ammo);
	TestFalse(TEXT("RemoveAt too many"), FWLItemRules::RemoveAt(Inv, AmmoSlot, 100));
	TestTrue(TEXT("RemoveAt all"), FWLItemRules::RemoveAt(Inv, AmmoSlot, 75));
	TestTrue(TEXT("Slot emptied"), Inv[AmmoSlot].IsEmpty());

	// Moduled stack: its own slot, qty 1 only, never merges.
	FWLItemStack Moduled(Sword, 1, { 600, 0 });
	TestTrue(TEXT("Moduled add"), FWLItemRules::InventoryAddStack(Inv, Cap, Moduled, Lookup));
	TestFalse(TEXT("Moduled qty 2 refused"), FWLItemRules::InventoryAddStack(Inv, Cap, Moduled.CopyWithQuantity(2), Lookup));
	const int32 SwordSlot = FWLItemRules::FindFirst(Inv, Sword);
	TestTrue(TEXT("Modules kept"), Inv[SwordSlot].HasModules());
	TestFalse(TEXT("Plain empty module array is not 'has modules'"), FWLItemStack(Sword, 1, { 0, 0 }).HasModules());
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLSaddlebagTest, "Wastelanders.Items.Saddlebag",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLSaddlebagTest::RunTest(const FString& Parameters)
{
	using namespace WLItemsTest;
	const FFakeItems Items;
	auto Lookup = [&Items](int32 Id) { return Items(Id); };

	TArray<FWLItemStack> Inv = EmptyInventory();
	const int32 WithBag = FWLItemRules::InventoryBaseSize + 6;
	TestTrue(TEXT("Fill 34 with the saddlebag"), FWLItemRules::InventoryAdd(Inv, WithBag, Scrap, 34, Lookup));
	TestEqual(TEXT("Full"), FWLItemRules::FreeSlots(Inv, WithBag), 0);

	// Beast dismissed: capacity back to 28. Items in 28-33 stay and still count.
	const int32 NoBag = FWLItemRules::InventoryBaseSize;
	TestEqual(TEXT("Overflow still counted"), FWLItemRules::CountOf(Inv, Scrap), 34);
	TestTrue(TEXT("Overflow removable"), FWLItemRules::RemoveAt(Inv, 30, 1));
	TestFalse(TEXT("Locked empty slot can't receive"), FWLItemRules::InventorySwap(Inv, NoBag, 0, 30));
	TestTrue(TEXT("Occupied locked slot can give up its item"), FWLItemRules::InventorySwap(Inv, NoBag, 0, 31));

	// Freed base slot accepts items, the locked one doesn't.
	TestTrue(TEXT("Remove one base"), FWLItemRules::RemoveAt(Inv, 5, 1));
	TestTrue(TEXT("Re-add fits in base slot"), FWLItemRules::InventoryAdd(Inv, NoBag, Scrap, 1, Lookup));
	TestTrue(TEXT("Slot 30 still empty"), Inv[30].IsEmpty());

	// Stackables merge into overflow slots too (Unity merges across MAX).
	TArray<FWLItemStack> Inv2 = EmptyInventory();
	Inv2[32] = FWLItemStack(Ammo, 10);
	TestTrue(TEXT("Merge into overflow ammo"), FWLItemRules::InventoryAdd(Inv2, NoBag, Ammo, 5, Lookup));
	TestEqual(TEXT("Merged"), Inv2[32].Quantity, 15);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLBankRulesTest, "Wastelanders.Items.BankRules",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLBankRulesTest::RunTest(const FString& Parameters)
{
	using namespace WLItemsTest;
	const FFakeItems Items;
	auto Lookup = [&Items](int32 Id) { return Items(Id); };

	TArray<FWLItemStack> Bank;
	Bank.SetNum(FWLItemRules::BankSize);
	TestEqual(TEXT("Bank is 300 (Unity Bank.SIZE)"), Bank.Num(), 300);

	// The bank always stacks, even non-stackables.
	TestTrue(TEXT("Deposit scrap"), FWLItemRules::BankDeposit(Bank, FWLItemStack(Scrap, 1), Lookup));
	TestTrue(TEXT("Deposit scrap again"), FWLItemRules::BankDeposit(Bank, FWLItemStack(Scrap, 4), Lookup));
	TestEqual(TEXT("One stack of 5"), Bank[0].Quantity, 5);
	TestTrue(TEXT("Slot 1 empty"), Bank[1].IsEmpty());

	// Moduled copies keep their own slot.
	TestTrue(TEXT("Deposit moduled sword"), FWLItemRules::BankDeposit(Bank, FWLItemStack(Sword, 1, { 600, 601 }), Lookup));
	TestTrue(TEXT("Deposit plain sword"), FWLItemRules::BankDeposit(Bank, FWLItemStack(Sword, 1), Lookup));
	TestEqual(TEXT("Moduled in its own slot"), Bank[1].Modules.Num(), 2);
	TestFalse(TEXT("Plain did not merge into moduled"), Bank[2].HasModules());

	TestFalse(TEXT("Unknown refused"), FWLItemRules::BankDeposit(Bank, FWLItemStack(12345, 1), Lookup));

	// Withdraw by id uses the first slot holding it.
	TestFalse(TEXT("Too many"), FWLItemRules::BankWithdraw(Bank, Scrap, 6));
	TestTrue(TEXT("Withdraw 5"), FWLItemRules::BankWithdraw(Bank, Scrap, 5));
	TestTrue(TEXT("Emptied"), Bank[0].IsEmpty());

	TestTrue(TEXT("Swap"), FWLItemRules::SwapSlots(Bank, 1, 299));
	TestTrue(TEXT("Swapped"), Bank[299].HasModules());
	TestFalse(TEXT("Bad index"), FWLItemRules::SwapSlots(Bank, 1, 300));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLModuleRulesTest, "Wastelanders.Items.ModuleRules",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLModuleRulesTest::RunTest(const FString& Parameters)
{
	TestEqual(TEXT("T1 v0 common"), FWLModuleRules::CatalogIdFor(1, 0, false), 600);
	TestEqual(TEXT("T1 v2 refined"), FWLModuleRules::CatalogIdFor(1, 2, true), 605);
	TestEqual(TEXT("T5 v6 refined"), FWLModuleRules::CatalogIdFor(5, 6, true), 669);
	TestEqual(TEXT("Tier clamps"), FWLModuleRules::CatalogIdFor(9, 9, false), 668);

	TestEqual(TEXT("Legacy T1 common"), FWLModuleRules::LegacyIdFor(1, FWLModuleRules::ELegacyQuality::Common), 500);
	TestEqual(TEXT("God refined (God-Hunter drop)"), FWLModuleRules::LegacyIdFor(6, FWLModuleRules::ELegacyQuality::Refined), 516);
	TestTrue(TEXT("Legacy 516 is a module"), FWLModuleRules::IsLegacyModule(516));
	TestFalse(TEXT("Perfect 502 retired"), FWLModuleRules::IsLegacyModule(502));
	TestFalse(TEXT("517 is not"), FWLModuleRules::IsLegacyModule(517));
	TestEqual(TEXT("Legacy refined grade"), FWLModuleRules::Grade(501, nullptr), 2);
	TestEqual(TEXT("Catalog odd = refined"), FWLModuleRules::Grade(601, nullptr), 2);

	FWLModuleRow Edge;
	Edge.ItemId = 600;
	Edge.Slot = TEXT("Weapon");
	Edge.Grade = 1;
	TestTrue(TEXT("Edge fits weapon"), FWLModuleRules::Fits(600, &Edge, EWLEquipSlot::Weapon));
	TestFalse(TEXT("Edge doesn't fit chest"), FWLModuleRules::Fits(600, &Edge, EWLEquipSlot::Chest));
	TestTrue(TEXT("Legacy fits anything"), FWLModuleRules::Fits(500, nullptr, EWLEquipSlot::Legs));
	TestFalse(TEXT("Non-module never fits"), FWLModuleRules::Fits(10, nullptr, EWLEquipSlot::Weapon));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLGearSetRulesTest, "Wastelanders.Items.GearSets",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLGearSetRulesTest::RunTest(const FString& Parameters)
{
	FWLGearSetRow Melee;
	Melee.PieceCount = 4;
	Melee.Style = EWLWeaponStyle::Melee;
	Melee.DamageMultiplier = 1.6f;
	Melee.BonusDefence = 520;
	auto Lookup = [&Melee](FName Id) -> const FWLGearSetRow* { return Id == FName(TEXT("ascendant_melee")) ? &Melee : nullptr; };

	FWLItemRow Piece;
	Piece.SetId = TEXT("ascendant_melee");
	FWLItemRow Other;

	TArray<const FWLItemRow*> Worn = { &Piece, &Piece, &Piece, &Other };
	TestEqual(TEXT("3 worn"), FWLGearSetRules::WornCount(Worn, TEXT("ascendant_melee")), 3);
	TestEqual(TEXT("Incomplete: x1"), FWLGearSetRules::DamageMultiplier(Worn, EWLWeaponStyle::Melee, Lookup), 1.f);
	TestEqual(TEXT("Incomplete: no defence"), FWLGearSetRules::BonusDefence(Worn, Lookup), 0);

	Worn[3] = &Piece;
	TestEqual(TEXT("Complete: x1.6 melee"), FWLGearSetRules::DamageMultiplier(Worn, EWLWeaponStyle::Melee, Lookup), 1.6f);
	TestEqual(TEXT("Wrong style: x1"), FWLGearSetRules::DamageMultiplier(Worn, EWLWeaponStyle::Ranged, Lookup), 1.f);
	TestEqual(TEXT("Defence applies whatever the style"), FWLGearSetRules::BonusDefence(Worn, Lookup), 520);

	Melee.DamageMultiplier = 0.5f;
	TestEqual(TEXT("Never below 1"), FWLGearSetRules::DamageMultiplier(Worn, EWLWeaponStyle::Melee, Lookup), 1.f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLDropTableTest, "Wastelanders.Items.DropTable",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLDropTableTest::RunTest(const FString& Parameters)
{
	FRandomStream Rng(1234);
	auto AllValid = [](int32 Id) { return Id != 666; };

	TArray<FWLDropRoll> Rolls;
	Rolls.Add(WLItemsTest::MakeRoll(1.f, false, 10, 2, 5, 1.f));
	Rolls.Add(WLItemsTest::MakeRoll(0.f, false, 11, 1, 1, 1.f));
	Rolls.Add(WLItemsTest::MakeRoll(1.f, false, 666, 1, 1, 1.f)); // item no longer exists
	Rolls.Add(WLItemsTest::MakeRoll(1.f, false, 12, 1, 1, 0.f));  // zero weight

	for (int32 i = 0; i < 200; ++i)
	{
		TArray<FWLItemQuantity> Out;
		UWLDropTableAsset::RollTable(Rolls, 0.f, 0.1f, Rng, AllValid, Out);
		if (!TestEqual(TEXT("Only the guaranteed roll drops"), Out.Num(), 1))
		{
			break;
		}
		TestTrue(TEXT("Quantity in range"), Out[0].ItemId == 10 && Out[0].Quantity >= 2 && Out[0].Quantity <= 5);
	}

	// Salvage: power 10 -> 100% second chance on a salvage-flagged failed roll.
	TArray<FWLDropRoll> SalvageRolls;
	SalvageRolls.Add(WLItemsTest::MakeRoll(0.f, true, 10, 1, 1, 1.f));
	TArray<FWLItemQuantity> Out;
	UWLDropTableAsset::RollTable(SalvageRolls, 10.f, 0.1f, Rng, AllValid, Out);
	TestEqual(TEXT("Salvage second chance"), Out.Num(), 1);
	Out.Reset();
	UWLDropTableAsset::RollTable(SalvageRolls, 0.f, 0.1f, Rng, AllValid, Out);
	TestEqual(TEXT("No salvage, no drop"), Out.Num(), 0);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLTierLootTest, "Wastelanders.Items.TierLoot",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLTierLootTest::RunTest(const FString& Parameters)
{
	const UWLLootSettings* S = NewObject<UWLLootSettings>();
	FRandomStream Rng(42);

	// CombatTarget.LootTier.
	FWLEnemyLootParams P;
	P.MaxHP = 10;  TestEqual(TEXT("HP 10 -> T1"), P.GetLootTier(), 1);
	P.MaxHP = 160; TestEqual(TEXT("HP 160 -> T3"), P.GetLootTier(), 3);
	P.MaxHP = 999; TestEqual(TEXT("HP 999 -> T5"), P.GetLootTier(), 5);
	P.Tier = 2;    TestEqual(TEXT("Explicit tier wins"), P.GetLootTier(), 2);

	// Statistical check on tier 1: module ~ 1/20 + (19/20)/4 = 28.75%, ammo 1/3, ore 1/2.
	const int32 N = 20000;
	int32 Modules = 0, AmmoDrops = 0, OreDrops = 0;
	for (int32 i = 0; i < N; ++i)
	{
		TArray<FWLItemQuantity> Out;
		FWLLootRules::RollTierLoot(*S, 1, false, 0, Rng, Out);
		for (const FWLItemQuantity& D : Out)
		{
			if (D.ItemId >= 600 && D.ItemId < 614) { ++Modules; }
			else if (D.ItemId == 13) { ++AmmoDrops; TestTrue(TEXT("Ammo qty 10-20"), D.Quantity >= 10 && D.Quantity <= 20); }
			else if (D.ItemId == 10) { ++OreDrops; TestTrue(TEXT("Ore qty 1-3"), D.Quantity >= 1 && D.Quantity <= 3); }
		}
	}
	TestTrue(TEXT("Module rate ~28.75%"), FMath::IsNearlyEqual(Modules / float(N), 0.2875f, 0.02f));
	TestTrue(TEXT("Ammo rate ~33%"), FMath::IsNearlyEqual(AmmoDrops / float(N), 1.f / 3.f, 0.02f));
	TestTrue(TEXT("Ore rate ~50%"), FMath::IsNearlyEqual(OreDrops / float(N), 0.5f, 0.02f));

	// God-Hunter: never an empty drop.
	for (int32 i = 0; i < 500; ++i)
	{
		TArray<FWLItemQuantity> Out;
		const int32 God = FWLLootRules::RollGodHunter(*S, Rng, Out);
		const bool bHasOre = Out.ContainsByPredicate([](const FWLItemQuantity& D) { return D.ItemId == 63 && D.Quantity >= 10 && D.Quantity <= 25; });
		const bool bHasAmmo = Out.ContainsByPredicate([](const FWLItemQuantity& D) { return D.ItemId == 119 && D.Quantity >= 50 && D.Quantity <= 100; });
		if (!TestTrue(TEXT("God-Hunter guaranteed piles"), bHasOre && bHasAmmo))
		{
			break;
		}
		if (God > 0)
		{
			TestTrue(TEXT("God gear id from the God table"), (God >= 140 && God <= 143) || (God >= 156 && God <= 161));
		}
	}

	// Default table mirrors the code path's odds.
	const TArray<FWLDropRoll> Table = FWLLootRules::BuildDefaultTable(*S, 1, false, false);
	TestEqual(TEXT("4 rolls"), Table.Num(), 4);
	if (Table.Num() == 4)
	{
		TestTrue(TEXT("Module chance 28.75%"), FMath::IsNearlyEqual(Table[0].Chance, 0.2875f, 0.0001f));
		TestEqual(TEXT("14 module outcomes"), Table[0].Outcomes.Num(), 14);
		TestTrue(TEXT("Ore roll gets salvage"), Table[1].bSalvageBonus);
		TestTrue(TEXT("Gear 1 in 128"), FMath::IsNearlyEqual(Table[3].Chance, 1.f / 128.f, 0.0001f));
	}

	TestEqual(TEXT("Cosmetic normal"), FWLLootRules::CosmeticChance(*S, false, false, -1.f), 0.02f);
	TestEqual(TEXT("Cosmetic boss"), FWLLootRules::CosmeticChance(*S, true, true, -1.f), 0.25f);
	TestEqual(TEXT("Cosmetic override"), FWLLootRules::CosmeticChance(*S, true, false, 0.5f), 0.5f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLWeaponSkinsTest, "Wastelanders.Items.WeaponSkins",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLWeaponSkinsTest::RunTest(const FString& Parameters)
{
	TestEqual(TEXT("Unlock flag"), FWLWeaponSkins::UnlockFlag(TEXT("rng_rust")), FString(TEXT("skin_rng_rust")));
	TestEqual(TEXT("Selection flag"), FWLWeaponSkins::SelectionFlag(EWLWeaponStyle::Melee, TEXT("mel_void")), FString(TEXT("skinsel_melee_mel_void")));

	const TArray<FName> Owned = { TEXT("a"), TEXT("b") };
	TestEqual(TEXT("Default -> a"), FWLWeaponSkins::NextInCycle(Owned, NAME_None), FName(TEXT("a")));
	TestEqual(TEXT("a -> b"), FWLWeaponSkins::NextInCycle(Owned, TEXT("a")), FName(TEXT("b")));
	TestEqual(TEXT("b -> default"), FWLWeaponSkins::NextInCycle(Owned, TEXT("b")), FName(NAME_None));
	TestEqual(TEXT("Unknown current -> a"), FWLWeaponSkins::NextInCycle(Owned, TEXT("zzz")), FName(TEXT("a")));
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
