#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS

#include "Quests/WLBeastTaskRules.h"
#include "Quests/WLStarterGuide.h"
#include "Quests/WLJournalLore.h"
#include "Quests/WLBlackwaterAtlas.h"
#include "Quests/WLSkillGuide.h"
#include "Quests/WLSulphurVents.h"
#include "Quests/WLQuestFlags.h"
#include "Dialogue/WLDialogueTypes.h"
#include "Companions/WLCompanionTypes.h"

#define WL_QUEST_TEST_FLAGS (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::EngineFilter)

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLBeastTaskRulesTest, "Wastelanders.Quests.BeastTaskRules", WL_QUEST_TEST_FLAGS)
bool FWLBeastTaskRulesTest::RunTest(const FString& Parameters)
{
	TestEqual(TEXT("Tier L1"), FWLBeastTaskRules::TierForLevel(1), 1);
	TestEqual(TEXT("Tier L20"), FWLBeastTaskRules::TierForLevel(20), 2);
	TestEqual(TEXT("Tier L99"), FWLBeastTaskRules::TierForLevel(99), 5);
	TestEqual(TEXT("Min kills T1"), FWLBeastTaskRules::MinKills(1), 10);
	TestEqual(TEXT("Max kills T5"), FWLBeastTaskRules::MaxKills(5), 35);
	TestEqual(TEXT("XP per kill is max HP"), FWLBeastTaskRules::XPForKill(12), 12);
	TestEqual(TEXT("XP never below 1"), FWLBeastTaskRules::XPForKill(0), 1);
	TestEqual(TEXT("Bonus is 10% of total"), FWLBeastTaskRules::CompletionBonus(10, 20), 20);

	TestEqual(TEXT("Clone noise"), FWLBeastTaskRules::NormalizeCreatureName(TEXT("Frog Marauder(Clone)")), FString(TEXT("Frog Marauder")));
	TestEqual(TEXT("Instance number"), FWLBeastTaskRules::NormalizeCreatureName(TEXT("Frog Marauder (2)")), FString(TEXT("Frog Marauder")));
	TestEqual(TEXT("Trailing number"), FWLBeastTaskRules::NormalizeCreatureName(TEXT("Frog Marauder 3")), FString(TEXT("Frog Marauder")));
	TestEqual(TEXT("Empty"), FWLBeastTaskRules::NormalizeCreatureName(TEXT("")), FString(TEXT("Creature")));
	TestFalse(TEXT("Pipe removed"), FWLBeastTaskRules::NormalizeCreatureName(TEXT("A|B")).Contains(TEXT("|")));

	TArray<FWLBeastTaskRules::FKind> Kinds;
	Kinds.Add({ TEXT("Rat"), 1, 5, 3 });
	Kinds.Add({ TEXT("Golem"), 3, 40, 1 });
	TestEqual(TEXT("Exact tier"), FWLBeastTaskRules::PickTier(3, Kinds), 3);
	TestEqual(TEXT("Falls to lower"), FWLBeastTaskRules::PickTier(4, Kinds), 3);
	TestEqual(TEXT("Falls to lower 2"), FWLBeastTaskRules::PickTier(2, Kinds), 1);
	TArray<FWLBeastTaskRules::FKind> High;
	High.Add({ TEXT("Boss"), 5, 100, 1 });
	TestEqual(TEXT("Falls to higher"), FWLBeastTaskRules::PickTier(2, High), 5);

	FWLBeastTask T;
	TestFalse(TEXT("Empty task invalid"), T.IsValid());
	T.Creature = TEXT("Rat"); T.Required = 10; T.Done = 5;
	TestTrue(TEXT("Task valid"), T.IsValid());
	TestEqual(TEXT("Progress"), FWLBeastTaskRules::Progress(T), 0.5f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLPaginateTest, "Wastelanders.Quests.DialoguePaginate", WL_QUEST_TEST_FLAGS)
bool FWLPaginateTest::RunTest(const FString& Parameters)
{
	TArray<FString> Short = { TEXT("Hello there.") };
	TestEqual(TEXT("Short line untouched"), FWLDialogueText::Paginate(Short).Num(), 1);

	FString Long;
	for (int32 i = 0; i < 12; ++i)
	{
		Long += TEXT("This is a sentence of moderate length. ");
	}
	TArray<FString> Pages = FWLDialogueText::Paginate({ Long });
	TestTrue(TEXT("Long line splits"), Pages.Num() > 1);
	for (const FString& P : Pages)
	{
		TestTrue(TEXT("Page within limit"), P.Len() <= FWLDialogueText::MaxPageChars);
	}
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLStarterGuideTest, "Wastelanders.Quests.StarterGuide", WL_QUEST_TEST_FLAGS)
bool FWLStarterGuideTest::RunTest(const FString& Parameters)
{
	TestEqual(TEXT("Starter count"), FWLStarterGuideRules::StarterCount(), 7);
	TestTrue(TEXT("Eight lessons"), FWLStarterGuideRules::RoxyLessons().Num() == 8);

	TestTrue(TEXT("Ready at 10"), FWLStarterGuideRules::ReadyForFission([](EWLSkill) { return 10; }));
	TestFalse(TEXT("Not ready at 9"), FWLStarterGuideRules::ReadyForFission([](EWLSkill S) { return S == EWLSkill::Attack ? 9 : 10; }));

	FWLStarterGuideState State;
	State.Stage = EWLStarterGuideStage::Lesson;
	State.LessonSkill = EWLSkill::Fishing;
	State.LessonItem = 0;
	TestTrue(TEXT("Own skill finishes"), FWLStarterGuideRules::EvaluateXP(State, EWLSkill::Fishing, false) == EWLLessonXPResult::Finish);
	TestTrue(TEXT("Other skill ignored"), FWLStarterGuideRules::EvaluateXP(State, EWLSkill::Cooking, false) == EWLLessonXPResult::Ignore);

	EWLSkill Skill = EWLSkill::Attack;
	int32 Item = 0;
	const FString LegacyFlag = FString(WLQuestFlags::LegacyGuideLessonPrefix) + FString::FromInt(static_cast<int32>(EWLSkill::Fishing)) + TEXT(":0");
	TestTrue(TEXT("Legacy lesson flag"), FWLStarterGuideRules::ParseLegacyLessonFlag(LegacyFlag, Skill, Item) && Skill == EWLSkill::Fishing && Item == 0);
	int32 Index = -1;
	TestFalse(TEXT("Garbage next flag"), FWLStarterGuideRules::ParseLegacyNextFlag(TEXT("nonsense"), Index));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLJournalAtlasTest, "Wastelanders.Quests.JournalAndAtlas", WL_QUEST_TEST_FLAGS)
bool FWLJournalAtlasTest::RunTest(const FString& Parameters)
{
	const FWLJournalEntry* Mainland = FWLJournalLore::Find(300);
	if (TestNotNull(TEXT("Page 300"), Mainland))
	{
		TestTrue(TEXT("Due on mainland"), FWLJournalLore::IsDue(*Mainland, true, 1));
		TestFalse(TEXT("Not due elsewhere"), FWLJournalLore::IsDue(*Mainland, false, 1));
	}
	const FWLJournalEntry* L150 = FWLJournalLore::Find(301);
	if (TestNotNull(TEXT("Page 301"), L150))
	{
		TestFalse(TEXT("Below total level"), FWLJournalLore::IsDue(*L150, false, 149));
		TestTrue(TEXT("At total level"), FWLJournalLore::IsDue(*L150, false, 150));
	}
	TestNull(TEXT("Unknown page"), FWLJournalLore::Find(99999));

	const TArray<FWLBlackwaterPlace>& Places = FWLBlackwaterAtlas::Places();
	TestTrue(TEXT("Nine places"), Places.Num() == 9);
	if (Places.Num() > 0)
	{
		TestEqual(TEXT("On a place"), FWLBlackwaterAtlas::NearestPlace(Places[0].Position), 0);
	}
	TestEqual(TEXT("Far from everything"), FWLBlackwaterAtlas::NearestPlace(FVector2D(-100000.0, -100000.0)), (int32)INDEX_NONE);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLCompanionRulesTest, "Wastelanders.Quests.CompanionRules", WL_QUEST_TEST_FLAGS)
bool FWLCompanionRulesTest::RunTest(const FString& Parameters)
{
	TestEqual(TEXT("Max HP"), UWLCompanionDefinitions::MaxHPFor(10, 1.f), 25);
	TestEqual(TEXT("Max hit"), UWLCompanionDefinitions::MaxHitFor(10, 1.f), 3);
	TestEqual(TEXT("Max hit floor"), UWLCompanionDefinitions::MaxHitFor(0, 0.1f), 1);
	TestEqual(TEXT("Special at least 2"), UWLCompanionDefinitions::SpecialMaxHitFor(1, 1.f), 2);
	TestEqual(TEXT("Shared damage capped by HP"), UWLCompanionDefinitions::SharedDamage(100, 0.5f, 20), 20);
	TestEqual(TEXT("Shared damage fraction"), UWLCompanionDefinitions::SharedDamage(10, 0.5f, 99), 5);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLSulphurVentRulesTest, "Wastelanders.Quests.SulphurVents", WL_QUEST_TEST_FLAGS)
bool FWLSulphurVentRulesTest::RunTest(const FString& Parameters)
{
	// Two groups 100 m apart, one bridged group.
	TArray<FVector> Pts = { FVector(0, 0, 0), FVector(2000, 0, 0), FVector(10000, 0, 0) };
	TestEqual(TEXT("Two clusters"), FWLSulphurVentRules::BuildClusters(Pts).Num(), 2);
	Pts.Add(FVector(4500, 0, 0));   // joins the first cluster only (25 m from 2000, 55 m from 10000)
	TestEqual(TEXT("Still two"), FWLSulphurVentRules::BuildClusters(Pts).Num(), 2);
	Pts.Add(FVector(7400, 0, 0));   // 29 m from 4500 and 26 m from 10000: bridges both
	TestEqual(TEXT("Single linkage merges"), FWLSulphurVentRules::BuildClusters(Pts).Num(), 1);
	TestEqual(TEXT("Small cluster wants 2"), FWLSulphurVentRules::VentsWanted(3), 2);
	TestEqual(TEXT("Big cluster wants 3"), FWLSulphurVentRules::VentsWanted(4), 3);
	TestEqual(TEXT("Seed is stable"), FWLSulphurVentRules::Seed(FVector(1234, 5678, 9)), FWLSulphurVentRules::Seed(FVector(1234, 5678, 100)));
	return true;
}

#endif
