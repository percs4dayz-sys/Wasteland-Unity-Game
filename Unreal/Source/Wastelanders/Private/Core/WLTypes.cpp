#include "Core/WLTypes.h"

namespace WLTypes
{
	static const TCHAR* const SlotNames[] = { TEXT("Weapon"), TEXT("Shield"), TEXT("Helmet"), TEXT("Chest"), TEXT("Legs"), TEXT("Ammo"), TEXT("Tool") };
	static_assert(UE_ARRAY_COUNT(SlotNames) == static_cast<int32>(EWLEquipSlot::Count), "Slot name table out of sync with EWLEquipSlot");

	static const TCHAR* const SkillNames[] = {
		TEXT("Attack"), TEXT("Strength"), TEXT("Defence"), TEXT("Endurance"), TEXT("Marksmanship"), TEXT("Fishing"),
		TEXT("Cooking"), TEXT("Woodcutting"), TEXT("Smithing"), TEXT("Scrapping"), TEXT("Beastmastery"), TEXT("Fission"),
		TEXT("Refinement") };
	static_assert(UE_ARRAY_COUNT(SkillNames) == static_cast<int32>(EWLSkill::Count), "Skill name table out of sync with EWLSkill");

	FString SlotToString(EWLEquipSlot Slot)
	{
		const int32 Index = static_cast<int32>(Slot);
		return (Index >= 0 && Index < UE_ARRAY_COUNT(SlotNames)) ? FString(SlotNames[Index]) : FString();
	}

	bool SlotFromString(const FString& Str, EWLEquipSlot& OutSlot)
	{
		for (int32 i = 0; i < UE_ARRAY_COUNT(SlotNames); ++i)
		{
			if (Str.Equals(SlotNames[i], ESearchCase::IgnoreCase))
			{
				OutSlot = static_cast<EWLEquipSlot>(i);
				return true;
			}
		}
		// "Boots" was the leg slot's name until 2026-07-31.
		if (Str.Equals(TEXT("Boots"), ESearchCase::IgnoreCase))
		{
			OutSlot = EWLEquipSlot::Legs;
			return true;
		}
		return false;
	}

	bool SlotForItemType(EWLItemType Type, EWLEquipSlot& OutSlot)
	{
		switch (Type)
		{
		case EWLItemType::Weapon: OutSlot = EWLEquipSlot::Weapon; return true;
		case EWLItemType::Shield: OutSlot = EWLEquipSlot::Shield; return true;
		case EWLItemType::Helmet: OutSlot = EWLEquipSlot::Helmet; return true;
		case EWLItemType::Chest:  OutSlot = EWLEquipSlot::Chest;  return true;
		case EWLItemType::Legs:   OutSlot = EWLEquipSlot::Legs;   return true;
		case EWLItemType::Ammo:   OutSlot = EWLEquipSlot::Ammo;   return true;
		case EWLItemType::Tool:   OutSlot = EWLEquipSlot::Tool;   return true;
		default: return false;
		}
	}

	FString SkillToString(EWLSkill Skill)
	{
		const int32 Index = static_cast<int32>(Skill);
		return (Index >= 0 && Index < UE_ARRAY_COUNT(SkillNames)) ? FString(SkillNames[Index]) : FString();
	}

	bool SkillFromString(const FString& InStr, EWLSkill& OutSkill)
	{
		// Legacy names from SaveData.ApplyTo, so old Unity saves keep their XP.
		static const TMap<FString, FString> Legacy = {
			{ TEXT("Salvaging"), TEXT("Woodcutting") },
			{ TEXT("Scavenging"), TEXT("Fishing") },
			{ TEXT("Excavation"), TEXT("Scrapping") },
			{ TEXT("Melee"), TEXT("Attack") },
			{ TEXT("Bladework"), TEXT("Attack") },
			{ TEXT("Brutality"), TEXT("Strength") },
			{ TEXT("Hardening"), TEXT("Defence") },
			{ TEXT("Sustenance"), TEXT("Cooking") },
			{ TEXT("Hearthcraft"), TEXT("Smithing") },
			{ TEXT("Tinkering"), TEXT("Smithing") },
		};
		const FString* Mapped = Legacy.Find(InStr);
		const FString& Str = Mapped ? *Mapped : InStr;
		for (int32 i = 0; i < UE_ARRAY_COUNT(SkillNames); ++i)
		{
			if (Str.Equals(SkillNames[i], ESearchCase::IgnoreCase))
			{
				OutSkill = static_cast<EWLSkill>(i);
				return true;
			}
		}
		return false;
	}
}
