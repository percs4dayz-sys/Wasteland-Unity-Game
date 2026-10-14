#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS

#include "Persistence/WLPersistenceSubsystem.h"
#include "Persistence/WLJsonFileBackend.h"
#include "Persistence/WLPersistenceSettings.h"
#include "Core/WLXPTable.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"

namespace WLPersistenceTestUtil
{
	static const FWLSaveSkill* FindSkill(const FWLCharacterSave& S, const TCHAR* Name)
	{
		return S.SkillXP.FindByPredicate([Name](const FWLSaveSkill& E) { return E.Skill == Name; });
	}

	// A trimmed real Unity SaveData document (JsonUtility output), including legacy skill names.
	static const TCHAR* UnitySave = TEXT(R"JSON({
	"playerName": "Rusty",
	"tileX": 3, "tileY": 4,
	"skillXP": [
		{ "skill": "Bladework", "xp": 1200 },
		{ "skill": "Endurance", "xp": 5000 },
		{ "skill": "Hearthcraft", "xp": 900 },
		{ "skill": "Tinkering", "xp": 300 },
		{ "skill": "Salvaging", "xp": 77 },
		{ "skill": "NotASkill", "xp": 5 }
	],
	"inventory": [
		{ "itemId": 1, "qty": 1, "modules": [600, 0] },
		{ "itemId": 0, "qty": 0, "modules": [] },
		{ "itemId": 21, "qty": 3 }
	],
	"bank": [ { "itemId": 10, "qty": 25, "modules": [] } ],
	"equipment": [
		{ "slot": "Weapon", "itemId": 12, "modules": [601] },
		{ "slot": "Boots", "itemId": 30, "modules": [] },
		{ "slot": "Ammo", "itemId": 40, "modules": [] }
	],
	"ammoQty": 57,
	"flags": [ "mara_gifted", "roxy_fission_given", "mara_gifted" ],
	"posX": 1.5, "posY": 2.0, "posZ": -3.0,
	"hasPos3D": true,
	"sceneName": "Overworld_BrokenCrescent"
})JSON");
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLUnityImportTest, "Wastelanders.Persistence.UnityImport",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLUnityImportTest::RunTest(const FString& Parameters)
{
	using namespace WLPersistenceTestUtil;

	FWLCharacterSave S;
	TestTrue(TEXT("Imports a Unity save"), UWLPersistenceSubsystem::ImportUnitySave(UnitySave, TEXT("dev:rusty"), S));
	TestEqual(TEXT("Name"), S.PlayerName, FString(TEXT("Rusty")));
	TestEqual(TEXT("Account"), S.AccountId, FString(TEXT("dev:rusty")));
	TestTrue(TEXT("New id"), S.CharacterId.IsValid());
	TestEqual(TEXT("Version"), S.Version, FWLCharacterSave::CurrentVersion);

	const FWLSaveSkill* Attack = FindSkill(S, TEXT("Attack"));
	TestTrue(TEXT("Bladework -> Attack"), Attack && Attack->XP == 1200);
	const FWLSaveSkill* Smithing = FindSkill(S, TEXT("Smithing"));
	TestTrue(TEXT("Hearthcraft/Tinkering merge keeps the higher XP"), Smithing && Smithing->XP == 900);
	const FWLSaveSkill* WC = FindSkill(S, TEXT("Woodcutting"));
	TestTrue(TEXT("Salvaging -> Woodcutting"), WC && WC->XP == 77);
	const FWLSaveSkill* Fishing = FindSkill(S, TEXT("Fishing"));
	TestTrue(TEXT("Missing skill defaults to 0"), Fishing && Fishing->XP == 0);
	TestEqual(TEXT("Every skill present once"), S.SkillXP.Num(), static_cast<int32>(EWLSkill::Count));

	TestEqual(TEXT("Inventory slot-exact"), S.Inventory.Num(), 3);
	TestEqual(TEXT("Slot 0 item"), S.Inventory[0].ItemId, 1);
	TestEqual(TEXT("Slot 0 modules"), S.Inventory[0].Modules.Num(), 2);
	TestEqual(TEXT("Slot 1 empty"), S.Inventory[1].ItemId, 0);
	TestEqual(TEXT("Slot 2 qty"), S.Inventory[2].Quantity, 3);
	TestEqual(TEXT("Missing modules = none"), S.Inventory[2].Modules.Num(), 0);
	TestEqual(TEXT("Bank"), S.Bank.Num(), 1);

	TestEqual(TEXT("Equipment count"), S.Equipment.Num(), 3);
	TestEqual(TEXT("Boots -> Legs"), S.Equipment[1].Slot, FString(TEXT("Legs")));
	TestEqual(TEXT("Weapon modules"), S.Equipment[0].Modules.Num(), 1);
	TestEqual(TEXT("Ammo qty"), S.AmmoQuantity, 57);
	TestEqual(TEXT("Flags deduped"), S.Flags.Num(), 2);

	// Unity (x, y, z) = (1.5, 2, -3) m -> UE (z, x, y) * 100.
	TestTrue(TEXT("Has location"), S.bHasLocation);
	TestTrue(TEXT("Axis conversion"), S.Location.Equals(FVector(-300.f, 150.f, 200.f), 0.01f));
	TestEqual(TEXT("Scene"), S.MapName, FString(TEXT("Overworld_BrokenCrescent")));
	TestEqual(TEXT("HP full"), S.CurrentHP, 0);

	FWLCharacterSave Junk;
	TestFalse(TEXT("Rejects non-JSON"), UWLPersistenceSubsystem::ImportUnitySave(TEXT("not json"), TEXT("a"), Junk));
	TestFalse(TEXT("Rejects other JSON"), UWLPersistenceSubsystem::ImportUnitySave(TEXT("{\"foo\":1}"), TEXT("a"), Junk));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLNewCharacterTest, "Wastelanders.Persistence.NewCharacter",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLNewCharacterTest::RunTest(const FString& Parameters)
{
	using namespace WLPersistenceTestUtil;
	const FWLCharacterSave S = UWLPersistenceSubsystem::MakeNewCharacter(TEXT("dev:bob"), TEXT("Bob"));
	TestTrue(TEXT("Id"), S.CharacterId.IsValid());
	TestEqual(TEXT("Name"), S.PlayerName, FString(TEXT("Bob")));
	const FWLSaveSkill* End = FindSkill(S, TEXT("Endurance"));
	TestTrue(TEXT("Endurance 10"), End && FWLXPTable::LevelForXP(EWLSkill::Endurance, End->XP) == 10);
	const FWLSaveSkill* Att = FindSkill(S, TEXT("Attack"));
	TestTrue(TEXT("Attack 1"), Att && Att->XP == 0);
	TestEqual(TEXT("Starter kit"), S.Inventory.Num(), UWLPersistenceSettings::Get()->StarterKitItemIds.Num());
	TestEqual(TEXT("No flags"), S.Flags.Num(), 0);
	TestFalse(TEXT("No location"), S.bHasLocation);
	TestEqual(TEXT("Default name"), UWLPersistenceSubsystem::MakeNewCharacter(TEXT("a"), TEXT("")).PlayerName, FString(TEXT("Survivor")));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLMigrateTest, "Wastelanders.Persistence.Migrate",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLMigrateTest::RunTest(const FString& Parameters)
{
	using namespace WLPersistenceTestUtil;
	FWLCharacterSave S;
	S.Version = 0;
	auto AddSkill = [&S](const TCHAR* Name, int32 XP)
	{
		FWLSaveSkill& E = S.SkillXP.AddDefaulted_GetRef();
		E.Skill = Name;
		E.XP = XP;
	};
	AddSkill(TEXT("Brutality"), 500);
	AddSkill(TEXT("Strength"), 200);
	AddSkill(TEXT("Excavation"), 40);
	FWLSaveEquip Boots;
	Boots.Slot = TEXT("Boots");
	Boots.ItemId = 30;
	S.Equipment.Add(Boots);

	UWLPersistenceSubsystem::Migrate(S);
	TestEqual(TEXT("Version bumped"), S.Version, FWLCharacterSave::CurrentVersion);
	const FWLSaveSkill* Str = FindSkill(S, TEXT("Strength"));
	TestTrue(TEXT("Duplicates merged, max kept"), Str && Str->XP == 500);
	const FWLSaveSkill* Scr = FindSkill(S, TEXT("Scrapping"));
	TestTrue(TEXT("Excavation -> Scrapping"), Scr && Scr->XP == 40);
	TestEqual(TEXT("Boots -> Legs"), S.Equipment[0].Slot, FString(TEXT("Legs")));

	FWLCharacterSave Future;
	Future.Version = FWLCharacterSave::CurrentVersion + 5;
	UWLPersistenceSubsystem::Migrate(Future);
	TestEqual(TEXT("Newer versions untouched"), Future.Version, FWLCharacterSave::CurrentVersion + 5);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLJsonBackendTest, "Wastelanders.Persistence.JsonBackend",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLJsonBackendTest::RunTest(const FString& Parameters)
{
	TestEqual(TEXT("Account sanitised"), FWLJsonFileBackend::SanitizeAccountId(TEXT("dev:bob/../x")), FString(TEXT("dev_bob____x")));
	TestEqual(TEXT("Name case-folded"), FWLJsonFileBackend::SanitizeCharacterName(TEXT("Big_Bob")), FString(TEXT("big_bob")));
	TestEqual(TEXT("Never empty"), FWLJsonFileBackend::SanitizeAccountId(TEXT("")), FString(TEXT("_")));

	// Round trip through the on-disk format.
	FWLCharacterSave In = UWLPersistenceSubsystem::MakeNewCharacter(TEXT("dev:bob"), TEXT("Bob"));
	In.Appearance.Add(TEXT("Hair"), TEXT("3"));
	In.Location = FVector(1.f, 2.f, 3.f);
	In.bHasLocation = true;
	In.Flags.Add(TEXT("mara_gifted"));
	FWLSaveQuestState Q;
	Q.QuestId = TEXT("Roxy");
	Q.Stage = 2;
	Q.Counters.Add(TEXT("kills"), 4);
	In.Quests.Add(Q);

	FString Json;
	TestTrue(TEXT("Serialises"), FWLJsonFileBackend::RecordToJson(In, Json));
	FWLCharacterSave Out;
	TestTrue(TEXT("Deserialises"), FWLJsonFileBackend::JsonToRecord(Json, Out));
	TestTrue(TEXT("Id"), Out.CharacterId == In.CharacterId);
	TestEqual(TEXT("Version"), Out.Version, In.Version);
	TestEqual(TEXT("Skills"), Out.SkillXP.Num(), In.SkillXP.Num());
	TestEqual(TEXT("Appearance"), Out.Appearance.FindRef(TEXT("Hair")), FString(TEXT("3")));
	TestTrue(TEXT("Location"), Out.Location.Equals(In.Location));
	TestTrue(TEXT("bHasLocation"), Out.bHasLocation);
	TestEqual(TEXT("Quest counter"), Out.Quests.Num() ? Out.Quests[0].Counters.FindRef(TEXT("kills")) : -1, 4);
	TestFalse(TEXT("Empty document rejected"), FWLJsonFileBackend::JsonToRecord(TEXT(""), Out));

	// Atomic write, backup, and corrupt-file quarantine with backup recovery.
	const FString Dir = FPaths::Combine(FPaths::AutomationTransientDir(), TEXT("WLPersistence"));
	IFileManager::Get().DeleteDirectory(*Dir, false, true);
	const FString Path = FPaths::Combine(Dir, TEXT("bob.json"));

	TestTrue(TEXT("Missing file"), FWLJsonFileBackend::ReadRecord(Path, Out) == FWLJsonFileBackend::ELoadResult::NotFound);
	TestTrue(TEXT("First write"), FWLJsonFileBackend::WriteFileAtomic(Path, Json, true));
	TestFalse(TEXT("No temp left behind"), IFileManager::Get().FileExists(*(Path + TEXT(".tmp"))));
	TestTrue(TEXT("Second write"), FWLJsonFileBackend::WriteFileAtomic(Path, Json, true));
	TestTrue(TEXT("Backup kept"), IFileManager::Get().FileExists(*(Path + TEXT(".bak"))));

	FFileHelper::SaveStringToFile(TEXT("{ this is not json"), *Path);
	TestTrue(TEXT("Corrupt main recovers from backup"), FWLJsonFileBackend::ReadRecord(Path, Out) == FWLJsonFileBackend::ELoadResult::Loaded);
	TestTrue(TEXT("Recovered id"), Out.CharacterId == In.CharacterId);

	IFileManager::Get().DeleteDirectory(*Dir, false, true);
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
