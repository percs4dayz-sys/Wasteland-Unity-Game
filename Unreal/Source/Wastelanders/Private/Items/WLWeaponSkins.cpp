#include "Items/WLWeaponSkins.h"
#include "Items/WLLootSettings.h"
#include "Items/WLEquipmentComponent.h"
#include "Data/WLDataTypes.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Quests/WLPlayerFlagsComponent.h"

#define LOCTEXT_NAMESPACE "WLWeaponSkins"

namespace
{
	UWLPlayerFlagsComponent* Skin_FlagsOf(const AWLPlayerState* PS)
	{
		return PS ? PS->GetFlags() : nullptr;
	}

	void Skin_Say(AWLPlayerState* PS, const FText& Message)
	{
		if (AWLPlayerController* PC = PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr)
		{
			PC->ClientGameMessage(Message);
		}
	}

	FText Skin_CategoryName(EWLWeaponStyle Category)
	{
		return Category == EWLWeaponStyle::Ranged ? LOCTEXT("Ranged", "Ranged") : LOCTEXT("Melee", "Melee");
	}

	bool Skin_HasCategory(const FWLItemRow* Weapon)
	{
		// Only Melee / Ranged have finishes; Fission gauntlets and None have none (Unity: two categories).
		return Weapon && Weapon->IsWeapon() && (Weapon->WeaponStyle == EWLWeaponStyle::Melee || Weapon->WeaponStyle == EWLWeaponStyle::Ranged);
	}
}

const FWLWeaponSkinDef* FWLWeaponSkins::Find(FName SkinId)
{
	if (SkinId.IsNone())
	{
		return nullptr;
	}
	for (const FWLWeaponSkinDef& Skin : UWLLootSettings::Get()->WeaponSkins)
	{
		if (Skin.Id == SkinId)
		{
			return &Skin;
		}
	}
	return nullptr;
}

bool FWLWeaponSkins::IsUnlocked(const AWLPlayerState* PS, FName SkinId)
{
	const UWLPlayerFlagsComponent* Flags = Skin_FlagsOf(PS);
	return Flags && !SkinId.IsNone() && Flags->HasFlag(UnlockFlag(SkinId));
}

const FWLWeaponSkinDef* FWLWeaponSkins::Selected(const AWLPlayerState* PS, EWLWeaponStyle Category)
{
	const UWLPlayerFlagsComponent* Flags = Skin_FlagsOf(PS);
	if (!Flags)
	{
		return nullptr;
	}
	const FString Prefix = SelectionPrefix(Category);
	const FString* Flag = Flags->FindFlagWithPrefix(Prefix);
	return Flag ? Find(FName(*Flag->RightChop(Prefix.Len()))) : nullptr;
}

void FWLWeaponSkins::Select(AWLPlayerState* PS, EWLWeaponStyle Category, FName SkinId)
{
	UWLPlayerFlagsComponent* Flags = Skin_FlagsOf(PS);
	if (!Flags || !PS->HasAuthority())
	{
		return;
	}
	// One look per category: clear any prior selection first.
	Flags->RemoveFlagsWithPrefix(SelectionPrefix(Category));
	if (!SkinId.IsNone())
	{
		Flags->SetFlag(SelectionFlag(Category, SkinId));
	}
	if (UWLEquipmentComponent* Equipment = PS->GetEquipment())
	{
		Equipment->RefreshAppliedWeaponSkin(); // rebuild the held model with the new look
	}
}

const FWLWeaponSkinDef* FWLWeaponSkins::AppliedFor(const AWLPlayerState* PS, const FWLItemRow* Weapon)
{
	if (!Skin_HasCategory(Weapon))
	{
		return nullptr;
	}
	const FWLWeaponSkinDef* Sel = Selected(PS, Weapon->WeaponStyle);
	return (Sel && IsUnlocked(PS, Sel->Id)) ? Sel : nullptr;
}

void FWLWeaponSkins::Unlock(AWLPlayerState* PS, FName SkinId)
{
	UWLPlayerFlagsComponent* Flags = Skin_FlagsOf(PS);
	const FWLWeaponSkinDef* Skin = Find(SkinId);
	if (!Flags || !Skin || !PS->HasAuthority() || IsUnlocked(PS, SkinId))
	{
		return;
	}
	Flags->SetFlag(UnlockFlag(SkinId));
	Skin_Say(PS, FText::Format(LOCTEXT("Unlocked", "[SKIN]: You found a weapon finish - {0} ({1} {2}). Hold the matching weapon and press K to equip the look."),
		Skin->DisplayName, Skin->Rarity, Skin_CategoryName(Skin->Category)));
	if (UWLEquipmentComponent* Equipment = PS->GetEquipment())
	{
		Equipment->RefreshAppliedWeaponSkin();
	}
}

void FWLWeaponSkins::RollDrop(AWLPlayerState* PS, float Chance, FRandomStream& Rng)
{
	if (!PS || Chance <= 0.f || Rng.FRand() > Chance)
	{
		return;
	}
	// Pick a not-yet-owned skin at random.
	TArray<FName, TInlineAllocator<16>> Locked;
	for (const FWLWeaponSkinDef& Skin : UWLLootSettings::Get()->WeaponSkins)
	{
		if (!IsUnlocked(PS, Skin.Id))
		{
			Locked.Add(Skin.Id);
		}
	}
	if (Locked.Num() > 0)
	{
		Unlock(PS, Locked[Rng.RandRange(0, Locked.Num() - 1)]);
	}
}

FName FWLWeaponSkins::NextInCycle(const TArray<FName>& UnlockedInCategory, FName Current)
{
	// Options: [default look] + every unlocked skin in the category.
	TArray<FName> Options;
	Options.Add(NAME_None);
	Options.Append(UnlockedInCategory);
	const int32 Index = FMath::Max(0, Options.IndexOfByKey(Current));
	return Options[(Index + 1) % Options.Num()];
}

FText FWLWeaponSkins::CycleHeldWeaponSkin(AWLPlayerState* PS)
{
	const UWLEquipmentComponent* Equipment = PS ? PS->GetEquipment() : nullptr;
	const FWLItemRow* Weapon = Equipment ? Equipment->GetItem(EWLEquipSlot::Weapon) : nullptr;
	if (!Skin_HasCategory(Weapon))
	{
		return LOCTEXT("HoldWeapon", "[SKIN]: Hold a weapon to change its finish.");
	}
	const EWLWeaponStyle Category = Weapon->WeaponStyle;

	TArray<FName> Unlocked;
	for (const FWLWeaponSkinDef& Skin : UWLLootSettings::Get()->WeaponSkins)
	{
		if (Skin.Category == Category && IsUnlocked(PS, Skin.Id))
		{
			Unlocked.Add(Skin.Id);
		}
	}
	if (Unlocked.Num() == 0)
	{
		return FText::Format(LOCTEXT("NoneYet", "[SKIN]: No {0} finishes collected yet - beat tough enemies to find them."), Skin_CategoryName(Category));
	}

	const FWLWeaponSkinDef* Current = Selected(PS, Category);
	const FName Next = NextInCycle(Unlocked, Current ? Current->Id : NAME_None);
	Select(PS, Category, Next);
	const FWLWeaponSkinDef* NextSkin = Find(Next);
	return FText::Format(LOCTEXT("Cycled", "[SKIN]: {0} finish -> {1}."), Skin_CategoryName(Category),
		NextSkin ? NextSkin->DisplayName : LOCTEXT("Default", "Default"));
}

#undef LOCTEXT_NAMESPACE
