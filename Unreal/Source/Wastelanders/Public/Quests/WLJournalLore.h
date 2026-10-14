#pragma once

#include "CoreMinimal.h"

/**
 * The survivor's journal: readable lore pages that steer the player toward the endgame
 * (Items/JournalLore.cs). The page ITEMS (ids 300+, type Readable, name + examine hook) are rows in
 * DT_Items; the full page text and the grant rule are not item data, so they live here.
 *
 *   Entry 1 (Day 3)   - granted on first reaching the mainland.
 *   Entry 2 (Day 27)  - at total level 150 (points at the First Guardian).
 *   Entry 5 (Day 184) - at total level 400 (points at the Major Boss).
 *
 * Granting runs in UWLQuestComponent (JournalManager.cs); reading is UWLQuestComponent::ReadJournalPage.
 */
struct WASTELANDERS_API FWLJournalEntry
{
	int32 ItemId = 0;
	/** Header shown in the reading UI. */
	FText Title;
	FText Body;
	/** Granted the moment the player is on a mainland map. */
	bool bOnMainlandArrival = false;
	/** Or granted at this total level (0 = ignore). */
	int32 TotalLevelRequired = 0;
};

struct WASTELANDERS_API FWLJournalLore
{
	static const TArray<FWLJournalEntry>& All();
	static const FWLJournalEntry* Find(int32 ItemId);

	/** JournalManager's grant rule. */
	static bool IsDue(const FWLJournalEntry& Entry, bool bOnMainland, int32 TotalLevel)
	{
		return (Entry.bOnMainlandArrival && bOnMainland) || (Entry.TotalLevelRequired > 0 && TotalLevel >= Entry.TotalLevelRequired);
	}
};
