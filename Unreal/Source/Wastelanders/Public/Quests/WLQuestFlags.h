#pragma once

#include "CoreMinimal.h"

/**
 * Every story / tutorial flag string the Unity game used. These are kept byte-for-byte: they are
 * persisted in FWLCharacterSave::Flags, Unity saves are imported straight into that list, and
 * recipes gate on them (FWLRecipeRow::RequiredFlag = "roxy_fission_given").
 *
 * Sources: NPC/RoxyNPC.cs, NPC/TutorialNPC.cs, Combat/CompanionManager.cs, UI/EggChoiceUI.cs,
 * Skills/BeastTasks.cs, World/StarterGuide.cs, Managers/JournalManager.cs, UI/BankGuideUI.cs,
 * World/BlackwaterArrival.cs.
 */
namespace WLQuestFlags
{
	// --- Roxy (RoxyNPC.cs) ---
	inline const TCHAR* RoxyMet            = TEXT("roxy_met");             // StarterGuide.MetFlag: stop pointing new players at her
	inline const TCHAR* RoxyGreeted        = TEXT("roxy_greeted");         // first chat (greeting + lesson one) done
	inline const TCHAR* RoxyGiftPrefix     = TEXT("roxy_gift:");           // + lesson index: that lesson's tool was handed over
	inline const TCHAR* RoxyKitPrefix      = TEXT("roxy_kit:");            // + item id: one piece of the fission kit was handed over
	inline const TCHAR* RoxyBeastGiven     = TEXT("roxy_beast_given");     // Beastmastery lesson given (re-offer the picker until chosen)
	inline const TCHAR* RoxyCombatTraining = TEXT("roxy_combat_training"); // waiting for 10 Attack/Strength/Defence/Marksmanship
	inline const TCHAR* RoxyFissionGiven   = TEXT("roxy_fission_given");   // Geiger counter + gauntlets handed over; unlocks refining
	inline const TCHAR* RoxyBankLesson     = TEXT("roxy_bank_lesson");     // BankGuideUI.LessonFlag

	/** Legacy Unity progress flags. UWLQuestComponent migrates them into quest states on load. */
	inline const TCHAR* LegacyRoxyNextPrefix    = TEXT("roxy_next:");      // + next lesson index
	inline const TCHAR* LegacyGuideLessonPrefix = TEXT("guide_lesson:");   // + (int)skill + ":" + item id
	inline const TCHAR* LegacyGuideReturn       = TEXT("guide_return");
	inline const TCHAR* LegacyBeastTaskPrefix   = TEXT("bmtask|");         // bmtask|creature|tier|done|required|xpPerKill

	// --- Companion (CompanionManager.cs, EggChoiceUI.cs) ---
	inline const TCHAR* EggChosen     = TEXT("egg_chosen");   // the one-time beast choice is made
	inline const TCHAR* PupHatched    = TEXT("pup_hatched");  // a companion exists (BeastTasks.OwnsBeast)
	inline const TCHAR* SpeciesPrefix = TEXT("species_");     // + "wolf" / "turtle" / "bird"

	// --- Old Mara (TutorialNPC.cs) ---
	inline const TCHAR* MaraGifted     = TEXT("mara_gifted");
	inline const TCHAR* MaraGuideShown = TEXT("mara_guide_shown");

	// --- Journal (JournalManager.cs) ---
	inline const TCHAR* JournalPrefix = TEXT("journal_");     // + page item id: page granted

	// --- Blackwater (BlackwaterArrival.cs) ---
	inline const TCHAR* BlackwaterArrived = TEXT("blackwater_arrived");

	inline FString RoxyGift(int32 LessonIndex) { return FString(RoxyGiftPrefix) + FString::FromInt(LessonIndex); }
	inline FString RoxyKit(int32 ItemId) { return FString(RoxyKitPrefix) + FString::FromInt(ItemId); }
	inline FString Species(const FString& Key) { return FString(SpeciesPrefix) + Key; }
	inline FString Journal(int32 ItemId) { return FString(JournalPrefix) + FString::FromInt(ItemId); }
}

/** Item ids the quest code hands out (stable ids from DT_Items; names/stats come from the table). */
namespace WLQuestItems
{
	constexpr int32 RustyMachete = 1;
	constexpr int32 ScrapShield = 2;
	constexpr int32 Pickaxe = 3;
	constexpr int32 Hatchet = 4;
	constexpr int32 FishingRod = 5;
	constexpr int32 ScrapMetal = 10;
	constexpr int32 ScrapBar = 11;
	constexpr int32 RawShrimp = 22;
	constexpr int32 GeigerCounter = 400;   // FissionItems.GeigerCounter
	constexpr int32 PowerGauntlets = 402;  // FissionItems.Gauntlets
	constexpr int32 RawCoreBase = 405;     // 405..409 = tier 1..5 raw fission core

	inline int32 RawCore(int32 Tier) { return RawCoreBase + FMath::Clamp(Tier, 1, 5) - 1; }
}
