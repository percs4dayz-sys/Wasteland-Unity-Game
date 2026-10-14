#include "Companions/WLCompanionTypes.h"

#define LOCTEXT_NAMESPACE "WLCompanion"

UWLCompanionDefinitions::UWLCompanionDefinitions()
{
	// CompanionManager.cs Defs + EggChoiceUI.cs Options, verbatim.
	{
		FWLCompanionSpecies& S = Species.AddDefaulted_GetRef();
		S.Key = TEXT("wolf");
		S.EggItemId = 50;
		S.AttackEveryTicks = 3;
		S.DamageMult = 1.f;
		S.HPMult = 1.f;
		S.SpecialName = LOCTEXT("WolfSpecial", "Frenzy");
		S.SpecialMult = 3.f;
		S.Verb = LOCTEXT("WolfVerb", "mauls");
		S.StageNames = { LOCTEXT("Wolf0", "Mutant Pup"), LOCTEXT("Wolf1", "Mutant Wolf"), LOCTEXT("Wolf2", "Alpha Wolf") };
		S.StageScale = { 1.f, 1.2f, 1.45f };
		S.CarrySlots = { 0, 0, 0 };
		S.SharedDamageFraction = 0.10f;
		S.HatchMessage = LOCTEXT("WolfHatch", "The egg cracks open \u2014 a scrawny mutant pup blinks up at you!");
		S.ChoiceTitle = LOCTEXT("WolfTitle", "Mutant Pup");
		S.ChoiceBlurb = LOCTEXT("WolfBlurb", "A snarling scrap of teeth and fur. Grows into a savage hunter at your side \u2014 and one day, an ALPHA.");
		S.StageMeshes.SetNum(3);
	}
	{
		FWLCompanionSpecies& S = Species.AddDefaulted_GetRef();
		S.Key = TEXT("turtle");
		S.EggItemId = 51;
		S.AttackEveryTicks = 5;
		S.DamageMult = 0.6f;
		S.HPMult = 2.f;
		S.SpecialName = LOCTEXT("TurtleSpecial", "Shell Slam");
		S.SpecialMult = 2.5f;
		S.Verb = LOCTEXT("TurtleVerb", "slams");
		S.StageNames = { LOCTEXT("Turtle0", "Shellback Hatchling"), LOCTEXT("Turtle1", "Shellback"), LOCTEXT("Turtle2", "Colossal Shellback") };
		S.StageScale = { 0.85f, 1.4f, 2.f };
		S.CarrySlots = { 2, 4, 6 };
		S.SharedDamageFraction = 0.35f;
		S.HatchMessage = LOCTEXT("TurtleHatch", "The heavy egg splits \u2014 a tiny shellback pokes its head out and yawns.");
		S.ChoiceTitle = LOCTEXT("TurtleTitle", "Shellback Hatchling");
		S.ChoiceBlurb = LOCTEXT("TurtleBlurb", "A plodding little tank. Grows into a great shellback that hauls extra gear for you (+2/+4/+6 pack slots).");
		S.StageMeshes.SetNum(3);
	}
	{
		FWLCompanionSpecies& S = Species.AddDefaulted_GetRef();
		S.Key = TEXT("bird");
		S.EggItemId = 52;
		S.AttackEveryTicks = 4;
		S.DamageMult = 1.6f;
		S.HPMult = 0.75f;
		S.SpecialName = LOCTEXT("BirdSpecial", "Dive Bomb");
		S.SpecialMult = 3.f;
		S.Verb = LOCTEXT("BirdVerb", "rakes");
		S.StageNames = { LOCTEXT("Bird0", "Rad-Chick"), LOCTEXT("Bird1", "Razorbeak"), LOCTEXT("Bird2", "Storm Razorbeak") };
		S.StageScale = { 0.7f, 1.f, 1.3f };
		S.CarrySlots = { 0, 0, 0 };
		S.SharedDamageFraction = 0.10f;
		S.MarksmanshipBonus = 0.10f;      // 1.10 + 0.05 x stage
		S.MarksmanshipPerStage = 0.05f;
		S.HoverHeight = 130.f;            // Unity hoverHeight 1.3 m
		S.HatchMessage = LOCTEXT("BirdHatch", "The speckled egg bursts \u2014 a scrappy rad-chick shrieks its first war cry!");
		S.ChoiceTitle = LOCTEXT("BirdTitle", "Rad-Chick");
		S.ChoiceBlurb = LOCTEXT("BirdBlurb", "A scrappy ball of feathers and attitude. Grows into a skyhunter whose strikes hit far harder than they ought to.");
		S.StageMeshes.SetNum(3);
	}
}

const FWLCompanionSpecies* UWLCompanionDefinitions::FindSpecies(FName Key) const
{
	return Species.FindByPredicate([Key](const FWLCompanionSpecies& S) { return S.Key == Key; });
}

const FWLCompanionSpecies* UWLCompanionDefinitions::FindByEgg(int32 EggItemId) const
{
	return Species.FindByPredicate([EggItemId](const FWLCompanionSpecies& S) { return S.EggItemId == EggItemId; });
}

#undef LOCTEXT_NAMESPACE
