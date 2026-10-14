#include "Quests/WLSkillGuide.h"
#include "Core/WLXPTable.h"
#include "Data/WLDataSubsystem.h"

#define LOCTEXT_NAMESPACE "WLSkillGuide"

UWLSkillGuideAsset::UWLSkillGuideAsset()
{
	// Content ported verbatim from Skills/SkillGuide.cs (_subtitles, _trainedBy, _guides).
	AddEntry(EWLSkill::Attack,
		LOCTEXT("AttackSub", "Melee accuracy \u2014 how reliably your swings land. Gates the melee weapon tiers."),
		LOCTEXT("AttackHow", "Attack enemies with a melee weapon in Accurate stance."),
		{
			{ 1,  LOCTEXT("Atk1",  "Equip Scrap Blade & Pipe Melee.") },
			{ 20, LOCTEXT("Atk20", "Equip Steel Blade; unlock power attacks (chance for a heavier swing).") },
			{ 40, LOCTEXT("Atk40", "Equip Energy-Infused Blade; stagger chance delays the enemy's next hit.") },
			{ 60, LOCTEXT("Atk60", "Equip Vibro-Blade; chance to bypass enemy defence.") },
			{ 80, LOCTEXT("Atk80", "Equip Myomer Blade; devastating cleave to nearby enemies.") },
			{ 99, LOCTEXT("Atk99", "Max melee - bonus first-strike damage and extra damage vs bosses.") },
		});

	AddEntry(EWLSkill::Strength,
		LOCTEXT("StrSub", "Raw melee power. Trained via Powerful (Aggressive) stance. Determines max hit."),
		LOCTEXT("StrHow", "Attack enemies in Powerful (Aggressive) stance with a melee weapon."),
		{
			{ 1,  LOCTEXT("Str1",  "Base max-hit bonus from strength.") },
			{ 20, LOCTEXT("Str20", "Unlock heavy swings \u2014 chance for bonus damage on each hit.") },
			{ 40, LOCTEXT("Str40", "Stagger strikes \u2014 delay enemy attacks with crushing blows.") },
			{ 60, LOCTEXT("Str60", "Sunder \u2014 attacks have a chance to reduce enemy defence.") },
			{ 80, LOCTEXT("Str80", "Cleave \u2014 melee hits splash to nearby enemies.") },
			{ 99, LOCTEXT("Str99", "Maximum carnage \u2014 bonus damage vs bosses and guaranteed stagger.") },
		});

	AddEntry(EWLSkill::Defence,
		LOCTEXT("DefSub", "Melee defence and durability. Trained via Defensive stance. Reduces damage taken."),
		LOCTEXT("DefHow", "Attack enemies in Defensive stance with a melee weapon."),
		{
			{ 1,  LOCTEXT("Def1",  "Base damage reduction from melee attacks.") },
			{ 20, LOCTEXT("Def20", "Thick Skin \u2014 5% passive melee damage reduction.") },
			{ 40, LOCTEXT("Def40", "Iron Will \u2014 10% chance to ignore stagger.") },
			{ 60, LOCTEXT("Def60", "Battle Scarred \u2014 15% damage reduction at low HP.") },
			{ 80, LOCTEXT("Def80", "Juggernaut \u2014 cannot be staggered while attacking.") },
			{ 99, LOCTEXT("Def99", "Unbreakable \u2014 25% passive melee damage reduction.") },
		});

	// Endurance IS your hit points: the milestones are notable points on the 10 -> 220 climb.
	AddEntry(EWLSkill::Endurance,
		LOCTEXT("EndSub", "Your hit-point pool \u2014 this level IS your HP, up to 220. Trains off any combat."),
		LOCTEXT("EndHow", "Deal damage in any fight \u2014 it levels passively."),
		{
			{ 10,  LOCTEXT("End10",  "Starting vitality \u2014 10 HP.") },
			{ 25,  LOCTEXT("End25",  "25 HP \u2014 you survive a second mistake.") },
			{ 50,  LOCTEXT("End50",  "50 HP.") },
			{ 75,  LOCTEXT("End75",  "75 HP.") },
			{ 99,  LOCTEXT("End99",  "99 HP \u2014 where every other skill in the wasteland ends. You keep going.") },
			{ 125, LOCTEXT("End125", "125 HP.") },
			{ 150, LOCTEXT("End150", "150 HP \u2014 most things can no longer burst you down.") },
			{ 175, LOCTEXT("End175", "175 HP.") },
			{ 200, LOCTEXT("End200", "200 HP.") },
			{ 220, LOCTEXT("End220", "220 HP \u2014 the ceiling. Nothing in the wasteland out-lasts you.") },
		});

	AddEntry(EWLSkill::Marksmanship,
		LOCTEXT("MrkSub", "Ranged accuracy and damage. Gates all firearm tiers."),
		LOCTEXT("MrkHow", "Shoot enemies with a gun (forge one at the Workbench)."),
		{
			{ 1,  LOCTEXT("Mrk1",  "Equip Pipe Pistol - base ranged accuracy.") },
			{ 20, LOCTEXT("Mrk20", "Equip Mid-tier Rifle - accuracy boost tier 1.") },
			{ 40, LOCTEXT("Mrk40", "Equip Alloy Rifle - accuracy boost tier 2, headshot chance.") },
			{ 60, LOCTEXT("Mrk60", "Equip Precision Rifle - long-range damage bonus.") },
			{ 80, LOCTEXT("Mrk80", "Equip Experimental Energy Rifle - accuracy boost tier 3.") },
			{ 99, LOCTEXT("Mrk99", "Max ranged accuracy - bonus damage on consecutive hits.") },
		});

	AddEntry(EWLSkill::Fishing,
		LOCTEXT("FshSub", "Food fished from hazardous waterways. Feeds Cooking."),
		LOCTEXT("FshHow", "Fish at a Fishing Spot."),
		{
			{ 1,  LOCTEXT("Fsh1",  "Stagnant Puddles: Sludge Minnow (basic low-heal food).") },
			{ 20, LOCTEXT("Fsh20", "Toxic Runoff Streams: Mutated Carp (mid-tier meals).") },
			{ 40, LOCTEXT("Fsh40", "Irradiated River: Glowfin Bass (strong healing meals).") },
			{ 60, LOCTEXT("Fsh60", "Contaminated Lake: Deepwater Lurker (high-tier meals).") },
			{ 80, LOCTEXT("Fsh80", "Flooded Ruins: Abyssal Specimen (endgame food).") },
		});

	AddEntry(EWLSkill::Cooking,
		LOCTEXT("CokSub", "Cooking raw catches into healing meals and buff foods."),
		LOCTEXT("CokHow", "Cook raw catches at a Cooking Fire."),
		{
			{ 1,  LOCTEXT("Cok1",  "Grilled Minnow - low heal, removes minor toxin.") },
			{ 20, LOCTEXT("Cok20", "Carp Stew - mid heal, small endurance buff.") },
			{ 40, LOCTEXT("Cok40", "Seared Glowfin - strong heal, minor combat buff.") },
			{ 60, LOCTEXT("Cok60", "Lurker Broth - high heal, sustained stat buffs.") },
			{ 80, LOCTEXT("Cok80", "Abyssal Feast - max heal, powerful multi-stat buff.") },
		});

	AddEntry(EWLSkill::Woodcutting,
		LOCTEXT("WcSub", "Harvesting lumber and structural timber. Feeds ammo pressing."),
		LOCTEXT("WcHow", "Strip wood from Wooden Debris."),
		{
			{ 1,  LOCTEXT("Wc1",  "Dead Scrub & Brush: Dry Kindling -> Rough Lumber.") },
			{ 20, LOCTEXT("Wc20", "Irradiated Thicket: Gnarled Timber -> Treated Lumber.") },
			{ 40, LOCTEXT("Wc40", "Fossilized Deadwood: Dense Wood Core -> Hardened Lumber.") },
			{ 60, LOCTEXT("Wc60", "Petrified Forest: Wood Shard -> Petrified Lumber.") },
			{ 80, LOCTEXT("Wc80", "Ancient Hardened Flora: Heartwood -> Prime Lumber.") },
		});

	AddEntry(EWLSkill::Smithing,
		LOCTEXT("SmSub", "Smelt scrap into bars at a Furnace, then forge those bars into all armor, weapons and ammo at a Workbench."),
		LOCTEXT("SmHow", "Smelt scrap into bars at a Furnace, then forge gear at a Workbench."),
		{
			{ 1,  LOCTEXT("Sm1",  "Smelt Iron-Grade Bars; forge Scrap Armor.") },
			{ 5,  LOCTEXT("Sm5",  "Iron-Grade Bar: Pipe Pistol, Scrap Blade.") },
			{ 10, LOCTEXT("Sm10", "Iron-Grade Bar: Low-Velocity Lead Ammo.") },
			{ 20, LOCTEXT("Sm20", "Smelt Steel-Grade Bars; forge Riveted Vest & Helmet.") },
			{ 25, LOCTEXT("Sm25", "Steel-Grade Bar: Mid-tier Rifle, Steel Blade.") },
			{ 30, LOCTEXT("Sm30", "Steel-Grade Bar: Standard Ballistic Ammo.") },
			{ 40, LOCTEXT("Sm40", "Smelt Wiring/Alloy Bars; forge Hardened Alloy Plate Armor.") },
			{ 45, LOCTEXT("Sm45", "Wiring/Alloy Bar: Alloy Rifle, Energy-Infused Blade.") },
			{ 50, LOCTEXT("Sm50", "Wiring/Alloy Bar: High-Grain Ammo.") },
			{ 60, LOCTEXT("Sm60", "Smelt Titanium-Alloy Bars; forge Tactical Ballistic Armor.") },
			{ 65, LOCTEXT("Sm65", "Titanium-Alloy Bar: Precision Rifle, Vibro-Blade.") },
			{ 70, LOCTEXT("Sm70", "Titanium-Alloy Bar: Armor-Piercing Ammo.") },
			{ 80, LOCTEXT("Sm80", "Smelt CyberSteel Bars; forge Power-Assisted Armor.") },
			{ 85, LOCTEXT("Sm85", "CyberSteel Bar: Energy Rifle, Myomer Blade.") },
			{ 90, LOCTEXT("Sm90", "CyberSteel Bar: Heavy Ordnance & Experimental Ammo.") },
		});

	AddEntry(EWLSkill::Scrapping,
		LOCTEXT("ScSub", "Digging scrap metal and components. Smelted into bars for Smithing."),
		LOCTEXT("ScHow", "Break Rubble Piles for scrap."),
		{
			{ 1,  LOCTEXT("Sc1",  "Junk Piles: Raw Scrap Metal -> Iron-Grade Bar.") },
			{ 20, LOCTEXT("Sc20", "Abandoned Car: Salvaged Chassis -> Steel-Grade Bar.") },
			{ 40, LOCTEXT("Sc40", "Tech Dumpster: Circuitry & Copper -> Wiring/Alloy Bar.") },
			{ 60, LOCTEXT("Sc60", "Downed Military Drone: Heavy Plating -> Titanium-Alloy Bar.") },
			{ 80, LOCTEXT("Sc80", "Abandoned Robotics: Synaptic Core -> Cyber-Steel/Myomer Bar.") },
		});

	AddEntry(EWLSkill::Beastmastery,
		LOCTEXT("BmSub", "Slayer-style hunting: your beast always has a task \u2014 kill a set number of one creature, matched to your tier. Higher levels = meaner prey and a stronger beast."),
		LOCTEXT("BmHow", "Kill your beast task's creature with your beast out (B). Each kill gives XP equal to its max HP; finishing a task adds a 10% bonus."),
		{
			{ 1,  LOCTEXT("Bm1",  "Pick your beast at Roxy. Tier 1 beast tasks: 10-15 kills of one tier-1 creature.") },
			{ 20, LOCTEXT("Bm20", "Tier 2 beast tasks: 12-20 kills of tougher tier-2 prey.") },
			{ 30, LOCTEXT("Bm30", "Your beast grows into its second stage - bigger, tougher, harder-hitting.") },
			{ 40, LOCTEXT("Bm40", "Tier 3 beast tasks: 15-25 kills of tier-3 prey.") },
			{ 60, LOCTEXT("Bm60", "Tier 4 beast tasks: 18-30 kills. Your beast reaches its final stage.") },
			{ 80, LOCTEXT("Bm80", "Tier 5 beast tasks: 20-35 kills of the wasteland's worst.") },
			{ 99, LOCTEXT("Bm99", "Master Beastmaster - you and your beast have hunted it all.") },
		});

	AddEntry(EWLSkill::Fission,
		LOCTEXT("FisSub", "The third combat style. Power gauntlets fed by refined reactor cores \u2014 few shots, heavy hits."),
		LOCTEXT("FisHow", "Fight with Power Gauntlets loaded with refined fission cores."),
		{
			{ 1,  LOCTEXT("Fis1",  "Equip Power Gauntlets. Meltdown: 1% chance a blast deals DOUBLE the core's max hit.") },
			{ 10, LOCTEXT("Fis10", "Meltdown 2%. (Levels buy accuracy \u2014 damage is whatever core you load.)") },
			{ 20, LOCTEXT("Fis20", "Meltdown 3%. Volatile Cores hit for 20.") },
			{ 40, LOCTEXT("Fis40", "Meltdown 5%. Enriched Cores hit for 32.") },
			{ 60, LOCTEXT("Fis60", "Meltdown 7%. Weaponized Cores hit for 46.") },
			{ 80, LOCTEXT("Fis80", "Meltdown 9%. Singularity Cores hit for 62 - the endgame charge.") },
			{ 90, LOCTEXT("Fis90", "Meltdown 10% - the cap. One blast in ten lands double.") },
			{ 99, LOCTEXT("Fis99", "Max Fission - 95% to hit even a tier-5 target.") },
		});

	AddEntry(EWLSkill::Refinement,
		LOCTEXT("RefSub", "Refines raw fission cores into the charged cores that load into power gauntlets."),
		LOCTEXT("RefHow", "Refine raw fission cores at a Furnace. Get them by stripping an Elemental Golem's corpse (that part trains Scrapping)."),
		{
			{ 1,  LOCTEXT("Ref1",  "Refine Unstable Cores (Tier 1, Earth Golem).") },
			{ 20, LOCTEXT("Ref20", "Refine Volatile Cores (Tier 2, Light Golem).") },
			{ 40, LOCTEXT("Ref40", "Refine Enriched Cores (Tier 3, Fire Golem).") },
			{ 60, LOCTEXT("Ref60", "Refine Weaponized Cores (Tier 4, Ice Golem).") },
			{ 80, LOCTEXT("Ref80", "Refine Singularity Cores (Tier 5, Shadow Golem).") },
			{ 99, LOCTEXT("Ref99", "Max Refinement - nothing is wasted, nothing goes critical.") },
		});
}

void UWLSkillGuideAsset::AddEntry(EWLSkill Skill, const FText& Subtitle, const FText& TrainedBy, std::initializer_list<TPair<int32, FText>> Milestones)
{
	FWLSkillGuideEntry& E = Entries.AddDefaulted_GetRef();
	E.Skill = Skill;
	E.Subtitle = Subtitle;
	E.TrainedBy = TrainedBy;
	for (const TPair<int32, FText>& M : Milestones)
	{
		FWLSkillMilestone& Row = E.Milestones.AddDefaulted_GetRef();
		Row.Level = M.Key;
		Row.Unlock = M.Value;
	}
}

const FWLSkillGuideEntry* UWLSkillGuideAsset::FindEntry(EWLSkill Skill) const
{
	return Entries.FindByPredicate([Skill](const FWLSkillGuideEntry& E) { return E.Skill == Skill; });
}

FText UWLSkillGuideAsset::GetSubtitle(EWLSkill Skill) const
{
	const FWLSkillGuideEntry* E = FindEntry(Skill);
	return E ? E->Subtitle : FText::GetEmpty();
}

FText UWLSkillGuideAsset::GetTrainedBy(EWLSkill Skill) const
{
	const FWLSkillGuideEntry* E = FindEntry(Skill);
	return E ? E->TrainedBy : FText::GetEmpty();
}

EWLSkill UWLSkillGuideAsset::ToolSkill(int32 ItemId)
{
	switch ((ItemId - FirstToolId) / 3)
	{
	case 0: return EWLSkill::Scrapping;    // pickaxes
	case 1: return EWLSkill::Woodcutting;  // hatchets
	default: return EWLSkill::Fishing;     // fishing rods
	}
}

TArray<FWLSkillMilestone> UWLSkillGuideAsset::GetMilestones(const UObject* WorldContext, EWLSkill Skill) const
{
	TArray<FWLSkillMilestone> Result;
	if (const FWLSkillGuideEntry* E = FindEntry(Skill))
	{
		Result = E->Milestones;
	}

	if (bIncludeGatheringTools)
	{
		const UWLDataSubsystem* Data = WorldContext ? UWLDataSubsystem::Get(WorldContext) : nullptr;
		for (int32 Id = FirstToolId; Id <= LastToolId; ++Id)
		{
			// Smithing lists every tool (they're forged at the workbench); a gathering skill lists its own.
			if (Skill != EWLSkill::Smithing && ToolSkill(Id) != Skill)
			{
				continue;
			}
			const FText Name = Data && Data->ItemExists(Id) ? Data->GetItemName(Id) : FText::AsNumber(Id);
			FWLSkillMilestone& M = Result.AddDefaulted_GetRef();
			M.Level = ToolRequiredLevel(Id);
			M.Unlock = Skill == EWLSkill::Smithing
				? FText::Format(LOCTEXT("ToolCraft", "Craft {0} at a Workbench."), Name)
				: FText::Format(LOCTEXT("ToolUse", "Use {0} \u2014 {1}% faster gathering."), Name, ToolSpeedBonusPercent(Id));
		}
	}

	Result.StableSort([](const FWLSkillMilestone& A, const FWLSkillMilestone& B) { return A.Level < B.Level; });
	return Result;
}

TArray<FWLSkillGuideRow> UWLSkillGuideAsset::BuildRows(const UObject* WorldContext, EWLSkill Skill, int32 CurrentLevel, const UWLSkillGuideAsset* Guide)
{
	if (!Guide)
	{
		Guide = GetDefaultGuide();
	}
	TArray<FWLSkillGuideRow> Rows;
	for (const FWLSkillMilestone& M : Guide->GetMilestones(WorldContext, Skill))
	{
		FWLSkillGuideRow& Row = Rows.AddDefaulted_GetRef();
		Row.Level = M.Level;
		Row.Unlock = M.Unlock;
		Row.bUnlocked = CurrentLevel >= M.Level;
	}
	return Rows;
}

FText UWLSkillGuideAsset::MakeTitle(const UObject* WorldContext, EWLSkill Skill, int32 CurrentLevel)
{
	FText Name = FText::FromString(WLTypes::SkillToString(Skill));
	if (const UWLDataSubsystem* Data = WorldContext ? UWLDataSubsystem::Get(WorldContext) : nullptr)
	{
		if (const FWLSkillRow* Row = Data->FindSkill(Skill); Row && !Row->DisplayName.IsEmpty())
		{
			Name = Row->DisplayName;
		}
	}
	// Unity printed "/99" for every skill; Endurance runs to 220, so show its real cap.
	return FText::Format(LOCTEXT("GuideTitle", "{0}  \u2014  Level {1}/{2}"), Name, CurrentLevel, FWLXPTable::MaxLevelFor(Skill));
}

#undef LOCTEXT_NAMESPACE
