#include "Quests/WLStarterGuide.h"
#include "Quests/WLQuestFlags.h"

#define LOCTEXT_NAMESPACE "WLStarterGuide"

const FName FWLStarterGuideRules::RoxyQuestId(TEXT("roxy_lessons"));
const FName FWLStarterGuideRules::StarterGuideQuestId(TEXT("starter_guide"));

FText FWLStarterGuideRules::RoxyGreeting()
{
	return LOCTEXT("RoxyGreeting", "Hey there, survivor. Aren't you a sight for sore eyes out here. Stick with me and I'll teach you how to stay alive \u2014 lesson by lesson. Talk to me whenever you're ready for the next one.");
}

FText FWLStarterGuideRules::BeastLessonLine()
{
	// The beast-task version. Unity's MigrateBeastLesson swapped out an older "bury these bones"
	// lesson saved into scenes; there are no serialized scenes here, so only the current text exists.
	return LOCTEXT("BeastLesson", "Alright, gorgeous, time to get a little blood on those boots. Out here you're only as good as the beast at your side \u2014 so I'm letting you pick one of my little monsters. Here's the deal: your beast always has a hunt lined up. A head count of one nasty kind of critter, matched to how far you've come \u2014 the better your Beastmastery, the meaner the prey. Call your beast with B, put them down together, and every kill on the list makes you a better Beastmaster. Kills without your beast don't count, sweetcheeks \u2014 no cheating on your pet. Finish the count and the next hunt finds you on its own. Now go on, choose your new best friend.");
}

const TArray<FWLRoxyLesson>& FWLStarterGuideRules::RoxyLessons()
{
	static const TArray<FWLRoxyLesson> Lessons = []
	{
		TArray<FWLRoxyLesson> L;
		auto Add = [&L](EWLSkill Skill, int32 Item, const FText& Title, const FText& Line)
		{
			FWLRoxyLesson& R = L.AddDefaulted_GetRef();
			R.Skill = Skill;
			R.GiveItemId = Item;
			R.Title = Title;
			R.Line = Line;
		};

		Add(EWLSkill::Fishing, WLQuestItems::FishingRod, LOCTEXT("T0", "Hooked on You"),
			LOCTEXT("L0", "Well, look at those forearm muscles. You look like you could wrestle a mutant brute, but let's start with something a bit more... relaxing. See that puddle of glowing sludge over there? That's a 'Basic Fishing Spot.' Grab this rod and go pull something out of it. Don't worry, the extra glowing limbs on the fish just mean more protein. The very first wet, slimy thing you yank out will magically trigger a 'Level Up' notification in your brain. Don't fight it, babe, just let the dopamine wash over you. Now go on, show me your technique."));

		Add(EWLSkill::Woodcutting, WLQuestItems::Hatchet, LOCTEXT("T1", "Stripping Down"),
			LOCTEXT("L1", "Mmm, I love a man who isn't afraid of hard labor. Here, take this hatchet \u2014 you'll need something to swing. See those dead trees out past the square? Go chop one down for me. Just keep whacking until a piece of 'Wood Scrap' pops into existence. The universe has this hilarious rule where your first successful piece of garbage instantly makes you a certified expert Woodcutter. It makes absolutely no sense, but hey, I don't make the code, I just live in it. Go break a sweat, handsome. I'll be right here watching."));

		Add(EWLSkill::Scrapping, WLQuestItems::Pickaxe, LOCTEXT("T2", "Heavy Metal Romance"),
			LOCTEXT("L2", "You know what's hotter than a guy with a sword? A guy with a pickaxe \u2014 here, take mine. There's a 'Rubble Pile' over there that's just begging for a pounding. Go smash it up until you find some 'Scrap Metal.' The second that rusty piece of junk hits your inventory, the game is going to grant you your first level in Scrapping. It's a dark, miserable world, but watching you smash old trash into neat little resource nodes really brightens up my apocalypse. Get to it!"));

		Add(EWLSkill::Cooking, WLQuestItems::RawShrimp, LOCTEXT("T3", "Something's Fishy"),
			LOCTEXT("L3", "Here \u2014 I saved you one of the radioactive flopping things I pulled out of the water earlier. Now, as much as I love a raw diet, if you eat that thing right now, your teeth will probably fall out. Take it over to that Cooking Fire and roast it. The moment you turn that biohazard into a 'Cooked Meal,' your Cooking skill will level up. It's a literal life-saver. Plus, if you don't burn it to a crisp, maybe I'll let you feed me a bite. Come on, cook for me, chef."));

		Add(EWLSkill::Smithing, WLQuestItems::ScrapMetal, LOCTEXT("T4", "Sparks Will Fly"),
			LOCTEXT("L4", "Is it hot out here, or is it just you? Let's turn up the heat. Here's a chunk of scrap metal \u2014 shove it into the Furnace to melt it into a 'Scrap Bar.' That first bar unlocks your Smithing skill \u2014 the same one you'll level forging that bar into weapons and armor later. I highly recommend hanging around the fire while you're at it \u2014 the lighting does wonders for your jawline, and the wasteland gets so terribly cold at night when you're sleeping alone."));

		Add(EWLSkill::Smithing, WLQuestItems::ScrapBar, LOCTEXT("T5", "Hardening Your Assets"),
			LOCTEXT("L5", "Here's a shiny scrap bar to get you started \u2014 because walking around this wasteland half-naked is my job, not yours, even if I highly enjoy the view. Take that bar over to the Workbench and forge your very first item. 'Scrap Armor' is the tier-1 unlock, which is perfect because I prefer my men heavily protected... at least until we get back to camp. Forging it levels the same Smithing skill you started at the furnace. Go build yourself protection, sweetcheeks. You're going to need it."));

		// Opens the beast picker instead of handing over an item.
		Add(EWLSkill::Beastmastery, 0, LOCTEXT("T6", "Deader is Better"), BeastLessonLine());

		// Advanced: introduces the fission gathering loop and hands over the Geiger Counter.
		Add(EWLSkill::Scrapping, WLQuestItems::GeigerCounter, LOCTEXT("T7", "Hot Stuff"),
			LOCTEXT("L7", "Ooh, now we're getting to the dangerous part \u2014 my favorite. Take this Geiger Counter, handsome. Hold onto it and it'll start clicking whenever one of the big Elemental Golems is stomping around out there. The faster it ticks, the closer you are, so let it lead you right to the brute. Then? You put it DOWN. Beat that walking reactor into a corpse. Once it's cold, you crack open its body and strip the fission cores out of it \u2014 same as smashing rubble, just with more radiation and better loot. Farm it 'til there's nothing left, then follow the clicks to wherever the next one crawled up. Refine what you pull, load it into a pair of power gauntlets, and you'll be punching holes in the world. Bring me back something that glows, sweetcheeks \u2014 I'll make it worth your while."));

		return L;
	}();
	return Lessons;
}

int32 FWLStarterGuideRules::StarterCount()
{
	const int32 Index = RoxyLessons().IndexOfByPredicate([](const FWLRoxyLesson& L) { return L.GiveItemId == WLQuestItems::GeigerCounter; });
	return Index != INDEX_NONE ? Index : RoxyLessons().Num();
}

bool FWLStarterGuideRules::ReadyForFission(TFunctionRef<int32(EWLSkill)> GetLevel)
{
	for (const EWLSkill S : { EWLSkill::Attack, EWLSkill::Strength, EWLSkill::Defence, EWLSkill::Marksmanship })
	{
		if (GetLevel(S) < FissionCombatLevel)
		{
			return false;
		}
	}
	return true;
}

FText FWLStarterGuideRules::CombatProgress(TFunctionRef<int32(EWLSkill)> GetLevel)
{
	return FText::Format(LOCTEXT("CombatProgress", "Attack {0}/10, Strength {1}/10, Defence {2}/10, Marksmanship {3}/10"),
		GetLevel(EWLSkill::Attack), GetLevel(EWLSkill::Strength), GetLevel(EWLSkill::Defence), GetLevel(EWLSkill::Marksmanship));
}

FText FWLStarterGuideRules::LessonText(EWLSkill Skill, int32 GiveItemId)
{
	switch (Skill)
	{
	case EWLSkill::Fishing:     return LOCTEXT("LtFish", "Catch a fish at the fishing spot");
	case EWLSkill::Woodcutting: return LOCTEXT("LtWood", "Chop down a dead tree");
	case EWLSkill::Scrapping:
		return GiveItemId == WLQuestItems::GeigerCounter
			? LOCTEXT("LtGeiger", "Use the Geiger Counter, defeat an Earth Golem, then scrap its body for raw cores")
			: LOCTEXT("LtScrap", "Smash the rubble pile for scrap");
	case EWLSkill::Refinement:  return LOCTEXT("LtRefine", "Refine your raw fission cores at a furnace");
	case EWLSkill::Fission:     return LOCTEXT("LtFission", "Equip power gauntlets and refined cores as ammo; deal Fission damage");
	case EWLSkill::Cooking:     return LOCTEXT("LtCook", "Cook your catch on the cooking fire");
	case EWLSkill::Smithing:
		return GiveItemId == WLQuestItems::ScrapBar
			? LOCTEXT("LtForge", "Forge scrap armour at the workbench")
			: LOCTEXT("LtSmelt", "Smelt your scrap into a bar at the furnace");
	case EWLSkill::Beastmastery: return LOCTEXT("LtBeast", "Choose your beast companion");
	default:                     return LOCTEXT("LtDefault", "Do Roxy's lesson");
	}
}

FText FWLStarterGuideRules::PeekCaption(EWLSkill Skill, int32 GiveItemId)
{
	if (Skill == EWLSkill::Smithing && GiveItemId == WLQuestItems::ScrapBar)
	{
		return LOCTEXT("CapForge", "Take the bar to the workbench and forge your armour.");
	}
	switch (Skill)
	{
	case EWLSkill::Fishing:     return LOCTEXT("CapFish", "Head to the water \u2014 cast a line there.");
	case EWLSkill::Woodcutting: return LOCTEXT("CapWood", "See those dead trees? Chop one down for wood.");
	case EWLSkill::Scrapping:   return LOCTEXT("CapScrap", "That rubble pile is where you'll dig up scrap.");
	case EWLSkill::Cooking:     return LOCTEXT("CapCook", "Take your catch to the cooking fire over there.");
	case EWLSkill::Smithing:    return LOCTEXT("CapSmith", "Smelt scrap into bars at the furnace, then forge gear at the workbench.");
	default:                    return LOCTEXT("CapDefault", "There's your next stop.");
	}
}

EWLLessonXPResult FWLStarterGuideRules::EvaluateXP(const FWLStarterGuideState& State, EWLSkill XPSkill, bool bHoldsRawCore)
{
	if (State.Stage != EWLStarterGuideStage::Lesson || State.LessonSkill != XPSkill)
	{
		return EWLLessonXPResult::Ignore;
	}
	// A single task kill is Beastmastery XP too, but the lesson is the full hunt: finishing it early
	// made Roxy hand out her next lesson mid-count. Beast-task completion ends that lesson instead.
	if (XPSkill == EWLSkill::Beastmastery)
	{
		return EWLLessonXPResult::Ignore;
	}
	if (State.LessonItem == WLQuestItems::GeigerCounter)
	{
		if (XPSkill == EWLSkill::Scrapping)
		{
			// Ordinary junk doesn't complete the golem lesson: only scrapping that yielded raw cores.
			return bHoldsRawCore ? EWLLessonXPResult::AdvanceToRefinement : EWLLessonXPResult::Ignore;
		}
		if (XPSkill == EWLSkill::Refinement)
		{
			return EWLLessonXPResult::AdvanceToFission;
		}
	}
	return EWLLessonXPResult::Finish;
}

bool FWLStarterGuideRules::ParseLegacyLessonFlag(const FString& Flag, EWLSkill& OutSkill, int32& OutItem)
{
	const FString Prefix(WLQuestFlags::LegacyGuideLessonPrefix);
	if (!Flag.StartsWith(Prefix, ESearchCase::CaseSensitive))
	{
		return false;
	}
	TArray<FString> Parts;
	Flag.Mid(Prefix.Len()).ParseIntoArray(Parts, TEXT(":"), false);
	int32 SkillIndex = 0;
	if (Parts.Num() != 2 || !Parts[0].IsNumeric() || !Parts[1].IsNumeric()
		|| !LexTryParseString(SkillIndex, *Parts[0]) || !LexTryParseString(OutItem, *Parts[1]))
	{
		return false;
	}
	// Unity's Skill enum order is the EWLSkill order (append-only serialized data).
	if (SkillIndex < 0 || SkillIndex >= static_cast<int32>(EWLSkill::Count))
	{
		return false;
	}
	OutSkill = static_cast<EWLSkill>(SkillIndex);
	return true;
}

bool FWLStarterGuideRules::ParseLegacyNextFlag(const FString& Flag, int32& OutIndex)
{
	const FString Prefix(WLQuestFlags::LegacyRoxyNextPrefix);
	if (!Flag.StartsWith(Prefix, ESearchCase::CaseSensitive))
	{
		return false;
	}
	const FString Num = Flag.Mid(Prefix.Len());
	return Num.IsNumeric() && LexTryParseString(OutIndex, *Num);
}

#undef LOCTEXT_NAMESPACE
