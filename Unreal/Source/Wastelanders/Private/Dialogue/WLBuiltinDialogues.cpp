#include "Dialogue/WLBuiltinDialogues.h"
#include "Dialogue/WLDialogueAsset.h"
#include "Quests/WLQuestFlags.h"
#include "Quests/WLStarterGuide.h"

#define LOCTEXT_NAMESPACE "WLBuiltinDialogues"

namespace
{
	using ECond = EWLDialogueConditionType;
	using EAct = EWLDialogueActionType;

	FWLDialogueCondition Cond(ECond Type, const FString& Name = FString(), int32 Value = 0)
	{
		FWLDialogueCondition C;
		C.Type = Type;
		C.Name = Name;
		C.Value = Value;
		return C;
	}
	FWLDialogueCondition FlagSet(const FString& F) { return Cond(ECond::FlagSet, F); }
	FWLDialogueCondition FlagNotSet(const FString& F) { return Cond(ECond::FlagNotSet, F); }
	FWLDialogueCondition StageEq(FName Quest, int32 V) { FWLDialogueCondition C = Cond(ECond::QuestStageEquals, FString(), V); C.QuestId = Quest; return C; }
	FWLDialogueCondition StageAtLeast(FName Quest, int32 V) { FWLDialogueCondition C = Cond(ECond::QuestStageAtLeast, FString(), V); C.QuestId = Quest; return C; }
	FWLDialogueCondition SkillAtLeast(EWLSkill S, int32 V) { FWLDialogueCondition C = Cond(ECond::SkillAtLeast, FString(), V); C.Skill = S; return C; }
	FWLDialogueCondition Lesson(EWLSkill S, int32 Item) { FWLDialogueCondition C = Cond(ECond::LessonActive); C.Skill = S; C.ItemId = Item; return C; }
	FWLDialogueCondition AnyLesson() { FWLDialogueCondition C = Cond(ECond::LessonActive, FString(), 1); C.ItemId = -1; return C; }

	FWLDialogueAction Act(EAct Type, const FString& Name = FString())
	{
		FWLDialogueAction A;
		A.Type = Type;
		A.Name = Name;
		return A;
	}
	FWLDialogueAction SetFlag(const FString& F) { return Act(EAct::SetFlag, F); }
	FWLDialogueAction OpenUI(const TCHAR* UI) { return Act(EAct::OpenUI, UI); }
	FWLDialogueAction SetStage(FName Quest, int32 V) { FWLDialogueAction A = Act(EAct::SetQuestStage); A.QuestId = Quest; A.Value = V; return A; }
	FWLDialogueAction StartLesson(EWLSkill S, int32 Item) { FWLDialogueAction A = Act(EAct::StartLesson); A.Skill = S; A.ItemId = Item; return A; }

	/** RoxyNPC.GiveQuestItem: once per lesson; already owned (carried, worn or banked) counts; a full bag keeps the lesson. */
	FWLDialogueAction RoxyGift(int32 LessonIndex, const FWLRoxyLesson& L)
	{
		FWLDialogueAction A = Act(EAct::GiveItem, WLQuestFlags::RoxyGift(LessonIndex));
		A.ItemId = L.GiveItemId;
		A.Quantity = FMath::Max(1, L.GiveQty);
		A.bSkipIfOwned = true;
		A.bAbortOnFailure = true;   // no dialogue at all: she tries again next time you talk
		A.TextAlreadyOwned = LOCTEXT("RoxyOwned", "[Roxy]: You've already got a {item}, so I'll hang on to mine.");
		A.TextInBank = LOCTEXT("RoxyBanked", "[Roxy]: You've already got a {item} in your bank, sweetcheeks. Take it out for this one.");
		A.Text = LOCTEXT("RoxyGive", "[Roxy]: Here, take {count}{item} — you'll want it for this one. *winks*");
		A.TextNoRoom = LOCTEXT("RoxyNoRoom", "[Roxy]: Your bag's stuffed. Make some room and I'll hand you the {item}.");
		return A;
	}

	FText TitleLine(const FText& Title) { return FText::Format(LOCTEXT("TitleFmt", "— {0} —"), Title); }
}

UWLDialogueAsset* FWLBuiltinDialogues::Build(EWLBuiltinDialogue Which, UObject* Outer, const TArray<FText>& WandererLines)
{
	switch (Which)
	{
	case EWLBuiltinDialogue::Roxy:     return BuildRoxy(Outer);
	case EWLBuiltinDialogue::OldMara:  return BuildOldMara(Outer);
	case EWLBuiltinDialogue::Wanderer: return BuildWanderer(Outer, WandererLines.Num() ? WandererLines : DefaultWandererLines());
	default:                           return nullptr;
	}
}

// ---------------------------------------------------------------------------------------------
// Roxy (RoxyNPC.Interact, in order)
// ---------------------------------------------------------------------------------------------
UWLDialogueAsset* FWLBuiltinDialogues::BuildRoxy(UObject* Outer)
{
	UWLDialogueAsset* D = NewObject<UWLDialogueAsset>(Outer, TEXT("Dialogue_Roxy"), RF_Transient);
	const FName Roxy = FWLStarterGuideRules::RoxyQuestId;
	const TArray<FWLRoxyLesson>& Lessons = FWLStarterGuideRules::RoxyLessons();
	const int32 StarterCount = FWLStarterGuideRules::StarterCount();
	const FWLDialogueAction Met = SetFlag(WLQuestFlags::RoxyMet);   // the new-player guide stops pointing at her

	// 1. The bank lesson comes first (its own flag: inserting it must not shift lesson or gift indices).
	{
		D->AddNode(TEXT("bank"), {
			LOCTEXT("BankIntro", "Before you fill your pockets with half the wasteland, handsome: use the town bank. Every town opens the same stash. Leave supplies here and collect them from any other town."),
			LOCTEXT("BankHow", "Interact with the crate. Use the inventory side to deposit and the bank side to withdraw. Deposit All stores everything in your bag. Reopen this guide with Roxy's Town banks button."),
		});
		FWLDialogueEntry& E = D->AddEntry(TEXT("bank"));
		E.Conditions = { FlagNotSet(WLQuestFlags::RoxyBankLesson), Cond(ECond::TownBanksExist) };
		E.Actions = { OpenUI(TEXT("BankGuide")), SetFlag(WLQuestFlags::RoxyBankLesson) };
	}

	// 2. She's set the Beastmastery lesson but the picker was closed without choosing: re-offer it.
	{
		D->AddNode(TEXT("beast_reoffer"), {
			LOCTEXT("BeastReoffer", "Still haven't picked? Go on — choose your companion. The moment you do, your first hunt is waiting for you. And be nice, they bite."),
		});
		FWLDialogueEntry& E = D->AddEntry(TEXT("beast_reoffer"));
		E.Conditions = { FlagSet(WLQuestFlags::RoxyBeastGiven), FlagNotSet(WLQuestFlags::EggChosen) };
		E.Actions = { OpenUI(TEXT("EggChoice")) };
	}

	// 3. A lesson is under way: replay it (talking again must not advance the lesson index).
	{
		D->AddNode(TEXT("beast_lesson"), { FWLStarterGuideRules::BeastLessonLine() });
		FWLDialogueEntry& Pick = D->AddEntry(TEXT("beast_lesson"));
		Pick.Conditions = { Lesson(EWLSkill::Beastmastery, -1), FlagNotSet(WLQuestFlags::EggChosen) };
		Pick.Actions = { Met, OpenUI(TEXT("EggChoice")) };

		// Mid-hunt: say where the count stands, so it's clear she's still waiting on this hunt.
		D->AddNode(TEXT("hunt_progress"), {
			LOCTEXT("HuntProgress", "You're not done yet, sweetcheeks. {task_done}/{task_required} {task_creature} down. Keep your beast out and finish the count, then come see me."),
		});
		FWLDialogueEntry& Progress = D->AddEntry(TEXT("hunt_progress"));
		Progress.Conditions = { Lesson(EWLSkill::Beastmastery, -1), FlagSet(WLQuestFlags::EggChosen), Cond(ECond::BeastTaskActive) };
		Progress.Actions = { Met, Act(EAct::SummonCompanion) };

		D->AddNode(TEXT("hunt_wait"), {
			LOCTEXT("HuntWait", "Your beast is sniffing out your first hunt. Keep it out with you, finish the whole count, then come see me."),
		});
		FWLDialogueEntry& Wait = D->AddEntry(TEXT("hunt_wait"));
		Wait.Conditions = { Lesson(EWLSkill::Beastmastery, -1), FlagSet(WLQuestFlags::EggChosen) };
		Wait.Actions = { Met, Act(EAct::SummonCompanion) };

		for (int32 i = 0; i < Lessons.Num(); ++i)
		{
			const FWLRoxyLesson& L = Lessons[i];
			if (L.Skill == EWLSkill::Beastmastery)
			{
				continue;
			}
			const FName Node(*FString::Printf(TEXT("replay_%d"), i));
			D->AddNode(Node, { TitleLine(L.Title), L.Line });
			FWLDialogueEntry& E = D->AddEntry(Node);
			E.Conditions = { Lesson(L.Skill, L.GiveItemId) };
			E.Actions = { Met };
		}

		D->AddNode(TEXT("replay_fallback"), { LOCTEXT("ReplayFallback", "Finish the lesson shown in your objective, then come back to me.") });
		FWLDialogueEntry& Fallback = D->AddEntry(TEXT("replay_fallback"));
		Fallback.Conditions = { AnyLesson() };
		Fallback.Actions = { Met };
	}

	// 4. The starter lessons, one per visit. The first-ever chat greets, then rolls into the lesson.
	for (int32 i = 0; i < StarterCount; ++i)
	{
		const FWLRoxyLesson& L = Lessons[i];
		const bool bBeast = L.Skill == EWLSkill::Beastmastery;

		TArray<FWLDialogueAction> Common;
		Common.Add(Met);
		if (bBeast)
		{
			// The flag makes her re-offer the picker if it's closed without choosing.
			Common.Add(SetFlag(WLQuestFlags::RoxyBeastGiven));
		}
		if (L.GiveItemId > 0)
		{
			Common.Add(RoxyGift(i, L));
		}

		FWLDialogueAction OpenPicker = OpenUI(TEXT("EggChoice"));
		OpenPicker.Conditions = { FlagNotSet(WLQuestFlags::EggChosen) };

		// Greeting variant (Unity: !_greeted -> greeted = true, index = 1).
		{
			const FName Node(*FString::Printf(TEXT("greet_%d"), i));
			D->AddNode(Node, { FWLStarterGuideRules::RoxyGreeting(), TitleLine(L.Title), L.Line });
			FWLDialogueEntry& E = D->AddEntry(Node);
			E.Conditions = { FlagNotSet(WLQuestFlags::RoxyGreeted), StageEq(Roxy, i) };
			E.Actions = Common;
			E.Actions.Add(SetFlag(WLQuestFlags::RoxyGreeted));
			E.Actions.Add(SetStage(Roxy, 1));
			E.Actions.Add(StartLesson(L.Skill, L.GiveItemId));
			if (bBeast)
			{
				E.Actions.Add(OpenPicker);
			}
		}
		{
			const FName Node(*FString::Printf(TEXT("lesson_%d"), i));
			D->AddNode(Node, { TitleLine(L.Title), L.Line });
			FWLDialogueEntry& E = D->AddEntry(Node);
			E.Conditions = { StageEq(Roxy, i) };
			E.Actions = Common;
			E.Actions.Add(SetStage(Roxy, i + 1));
			E.Actions.Add(StartLesson(L.Skill, L.GiveItemId));
			if (bBeast)
			{
				E.Actions.Add(OpenPicker);
			}
		}
	}

	// 5. The advanced lesson: Fission, after 10 in all four combat skills.
	{
		D->AddNode(TEXT("adv_given"), {
			LOCTEXT("AdvGiven", "Follow your Geiger Counter toward an Earth Golem in the starter region. Defeat it, scrap its remains with your pickaxe, then refine the raw cores at a furnace. Equip your power gauntlets and load the refined cores as ammunition. Scrapping supplies the cores, Refinement prepares them, and firing the gauntlets trains Fission. Higher-tier golems supply stronger cores. Try not to glow brighter than me, sweetcheeks."),
		});
		FWLDialogueEntry& Given = D->AddEntry(TEXT("adv_given"));
		Given.Conditions = { StageAtLeast(Roxy, StarterCount), FlagSet(WLQuestFlags::RoxyFissionGiven) };
		Given.Actions = { Met };

		D->AddNode(TEXT("adv_room"), {
			LOCTEXT("AdvRoom", "Make room in your bag for your Geiger Counter and power gauntlets, then talk to me again. I'm keeping your lesson ready."),
		});

		const FWLRoxyLesson& Hot = Lessons.IsValidIndex(StarterCount) ? Lessons[StarterCount] : Lessons.Last();
		D->AddNode(TEXT("hot_stuff"), {
			TitleLine(Hot.Title),
			LOCTEXT("HotStuff1", "All four combat skills at level 10. Now we're getting dangerous. Your Geiger Counter and power gauntlets are ready. Keep the counter in your bag or equip it as a tool; its readout points you toward a nearby Elemental Golem. If you already stored your counter in the bank, take it out first."),
			LOCTEXT("HotStuff2", "Start with an Earth Golem here in the starter region. Defeat it with your usual weapons, then use your pickaxe to scrap its body for raw fission cores. That's Scrapping. Take the cores to a furnace and refine them — that trains Refinement. Finally, equip the power gauntlets and the refined cores in your ammo slot. Dealing damage with them trains Fission. Better golems yield stronger cores as those skills grow. Go make something glow, handsome."),
		});
		FWLDialogueEntry& Ready = D->AddEntry(TEXT("hot_stuff"));
		Ready.Conditions = { StageAtLeast(Roxy, StarterCount),
			SkillAtLeast(EWLSkill::Attack, FWLStarterGuideRules::FissionCombatLevel), SkillAtLeast(EWLSkill::Strength, FWLStarterGuideRules::FissionCombatLevel),
			SkillAtLeast(EWLSkill::Defence, FWLStarterGuideRules::FissionCombatLevel), SkillAtLeast(EWLSkill::Marksmanship, FWLStarterGuideRules::FissionCombatLevel) };
		FWLDialogueAction Kit = Act(EAct::GiveFissionKit);
		Kit.FailNode = TEXT("adv_room");
		Ready.Actions = { Met, Kit, StartLesson(Hot.Skill, Hot.GiveItemId) };

		D->AddNode(TEXT("adv_not_ready"), {
			LOCTEXT("AdvNotReady", "You've learned the basics, gorgeous. Before I introduce you to Fission, get Attack, Strength, Defence AND Marksmanship to level 10. All four. Come back when you've done that and I'll give you a Geiger Counter to find your first Elemental Golem."),
			LOCTEXT("AdvProgress", "{combat_progress}"),
		});
		FWLDialogueEntry& NotReady = D->AddEntry(TEXT("adv_not_ready"));
		NotReady.Conditions = { StageAtLeast(Roxy, StarterCount) };
		NotReady.Actions = { Met, Act(EAct::StartCombatTraining) };
	}

	return D;
}

// ---------------------------------------------------------------------------------------------
// Old Mara (TutorialNPC.Interact)
// ---------------------------------------------------------------------------------------------
UWLDialogueAsset* FWLBuiltinDialogues::BuildOldMara(UObject* Outer)
{
	UWLDialogueAsset* D = NewObject<UWLDialogueAsset>(Outer, TEXT("Dialogue_OldMara"), RF_Transient);
	const FName KitGiven(TEXT("kit_given"));

	// Always top up any missing starter items before saying anything.
	{
		FWLDialogueAction Kit = Act(EAct::GiveItemSet);
		Kit.ItemIds = { WLQuestItems::RustyMachete, WLQuestItems::ScrapShield, WLQuestItems::FishingRod, WLQuestItems::Hatchet, WLQuestItems::Pickaxe };
		Kit.Text = LOCTEXT("MaraReceived", "[INFO]: Received {item}");
		Kit.TextNoRoom = LOCTEXT("MaraFull", "[WARNING]: Your inventory was too full to receive some items!");
		Kit.ResultVar = KitGiven;
		D->PreActions.Add(Kit);

		FWLDialogueAction Blessing = Act(EAct::Message);
		Blessing.Text = LOCTEXT("MaraBlessing", "[Old Mara]: Use these well. Survival isn't a gift, it's a habit.");
		Blessing.Conditions = { Cond(ECond::ScratchSet, KitGiven.ToString()), FlagNotSet(WLQuestFlags::MaraGifted) };
		D->PreActions.Add(Blessing);
	}

	D->AddNode(TEXT("intro"), {
		LOCTEXT("Mara0", "Survivor. Good — I was starting to think I was the only one left out here."),
		LOCTEXT("Mara1", "This place is a proving ground. The portal to the north won't open until you've shown some basic competence."),
		LOCTEXT("Mara2", "Dig through rubble, scavenge water, salvage wood. Learn to fight, to craft, to sustain yourself."),
		LOCTEXT("Mara3", "I've got a few things that might help you get started. Don't waste them."),
		LOCTEXT("Mara4", "When all your skills hit level 2, the portal opens. That's all you need to know."),
	});
	FWLDialogueEntry& Intro = D->AddEntry(TEXT("intro"));
	Intro.Conditions = { FlagNotSet(WLQuestFlags::MaraGifted) };
	Intro.Actions = { SetFlag(WLQuestFlags::MaraGifted) };

	D->AddNode(TEXT("lost_kit"), { LOCTEXT("MaraLost", "Lost your kit already? Try not to make a habit of it.") });
	FWLDialogueEntry& Lost = D->AddEntry(TEXT("lost_kit"));
	Lost.Conditions = { Cond(ECond::ScratchSet, KitGiven.ToString()) };

	// Second chat: the full skill rundown + the warning about the map's corners. Old skill names
	// (Bladework, Hearthcraft...) are Mara's own words from the tutorial island and kept as written.
	D->AddNode(TEXT("guide"), {
		LOCTEXT("Guide0", "Want the rundown? Fine. Here's what you're building, and where to do it."),
		LOCTEXT("Guide1", "COMBAT. Bladework is your melee aim — it unlocks sharper blades. Brutality is raw power. Hardening toughens you and lets you wear heavier plate. Endurance is your lifeblood; more of it, the more hits you walk away from."),
		LOCTEXT("Guide2", "The trick nobody tells you: hit F7 to change your STANCE. Same swing, different skill — Quick trains Bladework, Heavy trains Brutality, Defensive trains Hardening. Beat on that dummy in each one to round yourself out."),
		LOCTEXT("Guide3", "Marksmanship is for guns. Forge a Pipe Pistol at the bench, load some rounds, and put them downrange into the dummy."),
		LOCTEXT("Guide4", "GATHERING. Scrapping cracks rubble into scrap. Woodcutting strips wood from debris. Fishing hauls food out of the water."),
		LOCTEXT("Guide5", "CRAFTING. The furnace smelts scrap into bars — that's Hearthcraft. The bench builds guns and ammo — Tinkering. The cooking fire turns raw catches into food you can stomach — Sustenance. Eat it to heal."),
		LOCTEXT("Guide6", "Beastmastery is your companion. It grows stronger the more it fights at your side. You'll pick yours at the portal before you leave."),
		LOCTEXT("Guide7", "Get every one of those to level two and the gate opens. Not a moment before."),
		LOCTEXT("Guide8", "Last thing, and listen close: there are things bedded down in the far corners of the wasteland. Big, patient, hungry things. Stay clear of the map's corners until you're hard enough to survive what's waiting in them."),
	});
	FWLDialogueEntry& Guide = D->AddEntry(TEXT("guide"));
	Guide.Conditions = { FlagNotSet(WLQuestFlags::MaraGuideShown) };
	Guide.Actions = { SetFlag(WLQuestFlags::MaraGuideShown) };

	FWLDialogueNode& Post = D->AddNode(TEXT("post"), {
		LOCTEXT("Post0", "Don't get greedy, Survivor. Keep pawing at my pack and I'll make you into lunch instead of teaching you how to cook one."),
		LOCTEXT("Post1", "I already gave you a fair start. The rest you earn out there."),
		LOCTEXT("Post2", "Go on. Rubble won't dig itself — and that portal won't open while you're chatting up an old woman."),
	});
	Post.bCycleOneLine = true;
	D->AddEntry(TEXT("post"));

	return D;
}

// ---------------------------------------------------------------------------------------------
// Wanderer (WandererNPC.Interact)
// ---------------------------------------------------------------------------------------------
TArray<FText> FWLBuiltinDialogues::DefaultWandererLines()
{
	return {
		LOCTEXT("Wanderer0", "Roxy's the one worth listening to. I just carry things and complain about it."),
		LOCTEXT("Wanderer1", "Water out past the creek is clean enough if you don't mind it looking back at you."),
		LOCTEXT("Wanderer2", "Heard something clicking out in the fields. Big. Glowing. I walked the other way."),
		LOCTEXT("Wanderer3", "You bank your stuff, right? Every crate in every town opens the same stash. Don't ask me how."),
	};
}

UWLDialogueAsset* FWLBuiltinDialogues::BuildWanderer(UObject* Outer, const TArray<FText>& Lines)
{
	UWLDialogueAsset* D = NewObject<UWLDialogueAsset>(Outer, TEXT("Dialogue_Wanderer"), RF_Transient);
	if (Lines.Num() == 0)
	{
		return D;   // nothing to say: no entries, Talk does nothing (Unity returned early)
	}
	FWLDialogueNode& Node = D->Nodes.AddDefaulted_GetRef();
	Node.Id = TEXT("lines");
	Node.Lines = Lines;
	Node.bCycleOneLine = true;
	D->AddEntry(TEXT("lines"));
	return D;
}

#undef LOCTEXT_NAMESPACE
