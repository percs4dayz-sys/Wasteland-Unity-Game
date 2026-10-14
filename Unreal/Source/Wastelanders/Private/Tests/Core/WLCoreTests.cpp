#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS

#include "Core/WLXPTable.h"
#include "Core/WLTypes.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLXPTableTest, "Wastelanders.Core.XPTable",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLXPTableTest::RunTest(const FString& Parameters)
{
	// Spot values from the Unity curve.
	TestEqual(TEXT("Standard L1 costs 0"), FWLXPTable::XPForLevel(EWLSkill::Attack, 1), 0);
	TestEqual(TEXT("Standard L2"), FWLXPTable::XPForLevel(EWLSkill::Attack, 2), 81);
	TestEqual(TEXT("Standard L99"), FWLXPTable::XPForLevel(EWLSkill::Attack, 99), 1918756);
	TestEqual(TEXT("Endurance starts at 10"), FWLXPTable::LevelForXP(EWLSkill::Endurance, 0), 10);
	TestEqual(TEXT("Endurance L220"), FWLXPTable::XPForLevel(EWLSkill::Endurance, 220), 4017680);

	// Level 99 must be reachable (Unity once capped every skill at 98).
	TestEqual(TEXT("99 reachable"), FWLXPTable::LevelForXP(EWLSkill::Fishing, 1918756), 99);
	TestEqual(TEXT("Just under 99"), FWLXPTable::LevelForXP(EWLSkill::Fishing, 1918755), 98);
	TestEqual(TEXT("No XP to next at max"), FWLXPTable::XPToNextLevel(EWLSkill::Fishing, 5000000), 0);
	TestEqual(TEXT("Progress at max is 1"), FWLXPTable::LevelProgress(EWLSkill::Fishing, 5000000), 1.f);

	// Full comparison against the Unity-generated reference, when the data folder is present.
	FString Json;
	const FString Path = FPaths::Combine(FPaths::ProjectContentDir(), TEXT("Data/XPTable_Reference.json"));
	if (FFileHelper::LoadFileToString(Json, *Path))
	{
		TSharedPtr<FJsonObject> Root;
		if (FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json), Root) && Root.IsValid())
		{
			auto Check = [this, &Root](const TCHAR* Key, EWLSkill Skill)
			{
				const TSharedPtr<FJsonObject>* Table = nullptr;
				if (!Root->TryGetObjectField(Key, Table))
				{
					AddError(FString::Printf(TEXT("Reference missing %s"), Key));
					return;
				}
				for (const auto& Pair : (*Table)->Values)
				{
					const int32 Level = FCString::Atoi(*Pair.Key);
					const int32 Expected = static_cast<int32>(Pair.Value->AsNumber());
					if (FWLXPTable::XPForLevel(Skill, Level) != Expected)
					{
						AddError(FString::Printf(TEXT("%s level %d: expected %d got %d"), Key, Level, Expected, FWLXPTable::XPForLevel(Skill, Level)));
					}
				}
			};
			Check(TEXT("Standard"), EWLSkill::Attack);
			Check(TEXT("Endurance"), EWLSkill::Endurance);
		}
	}
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLTypesTest, "Wastelanders.Core.Types",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

bool FWLTypesTest::RunTest(const FString& Parameters)
{
	EWLSkill Skill;
	TestTrue(TEXT("Legacy Hearthcraft parses"), WLTypes::SkillFromString(TEXT("Hearthcraft"), Skill));
	TestEqual(TEXT("Hearthcraft is Smithing"), Skill, EWLSkill::Smithing);
	TestTrue(TEXT("Legacy Bladework parses"), WLTypes::SkillFromString(TEXT("Bladework"), Skill));
	TestEqual(TEXT("Bladework is Attack"), Skill, EWLSkill::Attack);
	TestFalse(TEXT("Nonsense rejected"), WLTypes::SkillFromString(TEXT("Juggling"), Skill));

	// Enum order is serialized data: these indices must never move.
	TestEqual(TEXT("Smithing index"), static_cast<int32>(EWLSkill::Smithing), 8);
	TestEqual(TEXT("Refinement index"), static_cast<int32>(EWLSkill::Refinement), 12);

	EWLEquipSlot Slot;
	TestTrue(TEXT("Boots maps to Legs"), WLTypes::SlotFromString(TEXT("Boots"), Slot));
	TestEqual(TEXT("Legs"), Slot, EWLEquipSlot::Legs);
	TestEqual(TEXT("Slot round trip"), WLTypes::SlotToString(EWLEquipSlot::Ammo), FString(TEXT("Ammo")));
	return true;
}

#endif
