#include "Quests/WLJournalLore.h"

#define LOCTEXT_NAMESPACE "WLJournalLore"

const TArray<FWLJournalEntry>& FWLJournalLore::All()
{
	static const TArray<FWLJournalEntry> Entries = []
	{
		TArray<FWLJournalEntry> E;

		FWLJournalEntry Day3;
		Day3.ItemId = 300;
		Day3.Title = LOCTEXT("Title300", "Journal Entry #1  \u00B7  Day 3");
		Day3.bOnMainlandArrival = true;
		Day3.Body = LOCTEXT("Body300",
			"Found in a ruined campsite near the starting area.\n\n"
			"If you're reading this, then the land claimed another fool.\n\n"
			"I woke up here three days ago with nothing but the clothes on my back. No roads. No people. "
			"Just wilderness stretching farther than I could see.\n\n"
			"I spent my first night hiding from the things that roam after dark. That was a mistake.\n"
			"The second night I built a shelter. The third night I survived.\n\n"
			"If you want to live, stop looking for answers and start gathering supplies. Wood. Stone. Food. Water.\n"
			"The land rewards preparation and punishes arrogance.\n\n"
			"I've seen strange structures in the distance. Ancient places. Dangerous places. Something powerful "
			"lives beyond them. I can feel it.\n\n"
			"But anyone chasing monsters before learning to survive is digging their own grave.\n\n"
			"Build first. Fight later.");
		E.Add(Day3);

		FWLJournalEntry Day27;
		Day27.ItemId = 301;
		Day27.Title = LOCTEXT("Title301", "Journal Entry #2  \u00B7  Day 27");
		Day27.TotalLevelRequired = 150;
		Day27.Body = LOCTEXT("Body301",
			"Found near the first mini-boss arena.\n\n"
			"I've finally learned what this place really is. It's not random.\n\n"
			"The creatures grow stronger the farther you travel from where we arrived. The land is divided into "
			"layers, almost as if something is testing us.\n\n"
			"Today I found evidence of another survivor. Or what remained of one. Broken armor. Torn pack. Huge "
			"claw marks. Nearby, tracks led toward an ancient ruin. Whatever killed them wasn't a normal beast.\n\n"
			"If you're standing where I stood, you've probably become stronger than most creatures roaming the "
			"wilds. Strong enough to hunt instead of hide. Strong enough to challenge the First Guardian.\n\n"
			"But listen carefully. The Guardian is not the end. It's the gate.\n\n"
			"Defeat it and you'll discover something much worse waiting beyond. I almost turned back when I "
			"realized that. Almost.");
		E.Add(Day27);

		FWLJournalEntry Day184;
		Day184.ItemId = 302;
		Day184.Title = LOCTEXT("Title302", "Journal Entry #5  \u00B7  Day 184");
		Day184.TotalLevelRequired = 400;
		Day184.Body = LOCTEXT("Body302",
			"Found shortly before the Major Boss.\n\n"
			"I understand now. The Guardians were never rulers. They were locks.\n\n"
			"Every one I've defeated has opened another piece of this world. Another path. Another secret. "
			"Another nightmare.\n\n"
			"The creature ahead is different. The others defended territory. This one commands it. I've watched "
			"it from a distance for weeks. The sky darkens when it moves. Predators flee its presence. Even the "
			"Guardians feared it.\n\n"
			"If you're reading this, then either I'm dead or I've become part of this place. Maybe that's what "
			"happens to everyone eventually.\n\n"
			"Still, if you've made it this far, you've already accomplished what I once thought impossible. You "
			"survived. You adapted. You conquered every challenge the land placed before you. The person who "
			"arrived here no longer exists. Only the survivor remains.\n\n"
			"If you intend to face the creature beyond these walls, remember one final lesson:\n\n"
			"Strength wasn't found in the weapons you crafted. Or the armor you forged. Or the monsters you "
			"defeated. Strength was earned every time you chose not to give up.\n\n"
			"Now go finish what I started.");
		E.Add(Day184);

		return E;
	}();
	return Entries;
}

const FWLJournalEntry* FWLJournalLore::Find(int32 ItemId)
{
	return All().FindByPredicate([ItemId](const FWLJournalEntry& E) { return E.ItemId == ItemId; });
}

#undef LOCTEXT_NAMESPACE
