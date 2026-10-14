#include "Items/WLLootSettings.h"

#define LOCTEXT_NAMESPACE "WLLootSettings"

namespace
{
	FWLWeaponSkinDef LootSettings_MakeSkin(const TCHAR* Id, const FText& Name, EWLWeaponStyle Category, const FLinearColor& Tint, const FText& Rarity)
	{
		FWLWeaponSkinDef Skin;
		Skin.Id = FName(Id);
		Skin.DisplayName = Name;
		Skin.Category = Category;
		Skin.Tint = Tint;
		Skin.Rarity = Rarity;
		return Skin;
	}
}

UWLLootSettings::UWLLootSettings()
{
	// Defaults are the Unity EnemyLoot.cs tables (tier 1-5 at index 0-4). Rates get RARER as tiers
	// climb because the rewards get proportionally stronger: deliberate, not a typo.
	ModuleCommonChance = { 4, 5, 6, 8, 10 };
	ModuleRefinedChance = { 20, 25, 30, 40, 50 };

	// Ore follows the gathering tier, Scrap Metal at tier 1.
	OreChance = { 2, 2, 3, 3, 4 };
	OreItemId = { 10, 60, 61, 62, 63 };

	// Ammo drops one tier BELOW the enemy, so the top grade of every tier is only ever pressed at a
	// Workbench (keeps ammo crafting worth doing). Tier 1 drops improvised Scrap Rounds.
	AmmoItemId = { 13, 115, 116, 117, 118 };
	AmmoMin = { 10, 15, 20, 25, 30 };
	AmmoMax = { 20, 30, 40, 50, 60 };

	GearChanceFromMob = { 128, 256, 350, 512, 750 };
	GearChanceFromMiniBoss = { 8, 12, 16, 20, 24 };
	GearMelee = {
		FWLLootIdList{ 100, 101, 136, 137, 138, 139 }, // T1: starter weapons + Salvaged set
		FWLLootIdList{ 102, 120, 121, 122, 123 },      // T2: Steel Blade + Riveted
		FWLLootIdList{ 103, 124, 125, 126, 127 },      // T3: Energy Blade + Hardened Alloy
		FWLLootIdList{ 104, 128, 129, 130, 131 },      // T4: Vibro-Blade + Tactical Ballistic
		FWLLootIdList{ 105, 132, 133, 134, 135 },      // T5: Myomer Blade + Power-Assisted
	};
	GearRanged = {
		FWLLootIdList{ 12 },                 // T1: Pipe Pistol
		FWLLootIdList{ 110, 144, 145, 146 }, // T2: Mid-tier Rifle + Scout
		FWLLootIdList{ 111, 147, 148, 149 }, // T3: Alloy Rifle + Ranger
		FWLLootIdList{ 112, 150, 151, 152 }, // T4: Precision Rifle + Marksman
		FWLLootIdList{ 113, 153, 154, 155 }, // T5: Energy Rifle + Deadeye
	};

	// God-Hunter: one tier above T5, so its ammo is Experimental (119).
	GodGearPools = {
		FWLLootIdList{ 159 },                // melee weapon
		FWLLootIdList{ 140, 141, 142, 143 }, // melee armour
		FWLLootIdList{ 160, 161 },           // railgun or slugs
		FWLLootIdList{ 156, 157, 158 },      // ranged armour
	};

	// WeaponSkins.cs starter set: tint-only, ids are stable save-flag strings.
	WeaponSkins = {
		LootSettings_MakeSkin(TEXT("rng_rust"),   LOCTEXT("rng_rust", "Rustpunk Finish"), EWLWeaponStyle::Ranged, FLinearColor(0.72f, 0.45f, 0.30f), LOCTEXT("Common", "Common")),
		LootSettings_MakeSkin(TEXT("rng_ash"),    LOCTEXT("rng_ash", "Ashen Ops"),        EWLWeaponStyle::Ranged, FLinearColor(0.28f, 0.29f, 0.32f), LOCTEXT("Uncommon", "Uncommon")),
		LootSettings_MakeSkin(TEXT("rng_neon"),   LOCTEXT("rng_neon", "Neon Circuit"),    EWLWeaponStyle::Ranged, FLinearColor(0.20f, 0.85f, 0.95f), LOCTEXT("Rare", "Rare")),
		LootSettings_MakeSkin(TEXT("rng_gold"),   LOCTEXT("rng_gold", "Warlord's Gilt"),  EWLWeaponStyle::Ranged, FLinearColor(0.95f, 0.78f, 0.25f), LOCTEXT("Epic", "Epic")),
		LootSettings_MakeSkin(TEXT("mel_blood"),  LOCTEXT("mel_blood", "Blood-Forged"),   EWLWeaponStyle::Melee,  FLinearColor(0.75f, 0.15f, 0.15f), LOCTEXT("Common", "Common")),
		LootSettings_MakeSkin(TEXT("mel_chrome"), LOCTEXT("mel_chrome", "Chrome Edge"),   EWLWeaponStyle::Melee,  FLinearColor(0.90f, 0.92f, 0.95f), LOCTEXT("Uncommon", "Uncommon")),
		LootSettings_MakeSkin(TEXT("mel_toxic"),  LOCTEXT("mel_toxic", "Toxic Bite"),     EWLWeaponStyle::Melee,  FLinearColor(0.45f, 0.90f, 0.25f), LOCTEXT("Rare", "Rare")),
		LootSettings_MakeSkin(TEXT("mel_void"),   LOCTEXT("mel_void", "Voidsteel"),       EWLWeaponStyle::Melee,  FLinearColor(0.45f, 0.30f, 0.70f), LOCTEXT("Epic", "Epic")),
	};
}

#undef LOCTEXT_NAMESPACE
