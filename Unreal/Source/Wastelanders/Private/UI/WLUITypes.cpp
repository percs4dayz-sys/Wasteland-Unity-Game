#include "UI/WLUITypes.h"
#include "Data/WLDataTypes.h"

#define LOCTEXT_NAMESPACE "WLUI"

namespace WLItemVerbs
{
	const FName Equip(TEXT("Equip"));
	const FName Use(TEXT("Use"));
	const FName Eat(TEXT("Eat"));
	const FName Bury(TEXT("Bury"));
	const FName Read(TEXT("Read"));
	const FName Drop(TEXT("Drop"));
	const FName Deposit(TEXT("Deposit"));
	const FName Examine(TEXT("Examine"));
	const FName Cancel(TEXT("Cancel"));
}

TArray<FName> FWLUILogic::ItemVerbs(const FWLItemRow& Item, bool bBankOpen)
{
	// Unity InventorySlotUI.OpenMenu order.
	TArray<FName> Verbs;
	if (bBankOpen)
	{
		Verbs.Add(WLItemVerbs::Deposit);
	}
	if (Item.IsEquippable())
	{
		Verbs.Add(WLItemVerbs::Equip);
	}
	else if (Item.IsConsumable())
	{
		// Food reads "Eat" like OSRS; other consumables (eggs, kits) stay "Use". Both run the default action.
		Verbs.Add(Item.HealAmount > 0 ? WLItemVerbs::Eat : WLItemVerbs::Use);
	}
	if (Item.bBuryable)
	{
		Verbs.Add(WLItemVerbs::Bury);
	}
	if (Item.IsReadable())
	{
		Verbs.Add(WLItemVerbs::Read);
	}
	Verbs.Add(WLItemVerbs::Drop);
	return Verbs;
}

FName FWLUILogic::DefaultItemVerb(const FWLItemRow& Item, bool bBankOpen, bool bIsModule)
{
	if (bBankOpen)
	{
		return WLItemVerbs::Deposit;
	}
	if (Item.IsEquippable())
	{
		return WLItemVerbs::Equip;
	}
	if (Item.IsConsumable())
	{
		return Item.HealAmount > 0 ? WLItemVerbs::Eat : WLItemVerbs::Use;
	}
	if (Item.bBuryable)
	{
		return WLItemVerbs::Bury;
	}
	if (Item.IsReadable())
	{
		return WLItemVerbs::Read;
	}
	// Modules open the equipment panel (Unity opened ModulesPanelUI); the caller handles NAME_None.
	return bIsModule ? FName(TEXT("Modules")) : NAME_None;
}

FText FWLUILogic::VerbLabel(FName Verb)
{
	if (Verb == WLItemVerbs::Equip) return LOCTEXT("Equip", "Equip");
	if (Verb == WLItemVerbs::Use) return LOCTEXT("Use", "Use");
	if (Verb == WLItemVerbs::Eat) return LOCTEXT("Eat", "Eat");
	if (Verb == WLItemVerbs::Bury) return LOCTEXT("Bury", "Bury");
	if (Verb == WLItemVerbs::Read) return LOCTEXT("Read", "Read");
	if (Verb == WLItemVerbs::Drop) return LOCTEXT("Drop", "Drop");
	if (Verb == WLItemVerbs::Deposit) return LOCTEXT("Deposit", "Deposit");
	if (Verb == WLItemVerbs::Examine) return LOCTEXT("Examine", "Examine");
	if (Verb == WLItemVerbs::Cancel) return LOCTEXT("Cancel", "Cancel");
	return FText::FromName(Verb);
}

void FWLUILogic::TickXPDrops(TArray<FWLXPDropView>& Drops, float DeltaSeconds)
{
	for (int32 i = Drops.Num() - 1; i >= 0; --i)
	{
		FWLXPDropView& D = Drops[i];
		D.Age += DeltaSeconds;
		D.OffsetY += XPDropRise / XPDropLifetime * DeltaSeconds;
		if (D.Age >= XPDropLifetime)
		{
			Drops.RemoveAt(i);
		}
	}
}

void FWLUILogic::PushXPDrop(TArray<FWLXPDropView>& Drops, EWLSkill Skill, int32 Amount, const FText& SkillName)
{
	if (Amount <= 0)
	{
		return;
	}
	// Push existing drops up a little so a fresh one doesn't overlap (Unity XPDropUI).
	for (FWLXPDropView& D : Drops)
	{
		D.OffsetY += XPDropStackPush;
	}
	FWLXPDropView& New = Drops.AddDefaulted_GetRef();
	New.Skill = Skill;
	New.Amount = Amount;
	New.Text = FText::Format(LOCTEXT("XPDrop", "+{0} {1}"), FText::AsNumber(Amount), SkillName);
}

void FWLUILogic::AppendChatLine(TArray<FText>& Lines, const FText& Line, int32 MaxLines)
{
	Lines.Add(Line);
	const int32 Excess = Lines.Num() - FMath::Max(1, MaxLines);
	if (Excess > 0)
	{
		Lines.RemoveAt(0, Excess);
	}
}

FVector2D FWLUILogic::PlaceMenu(const FVector2D& Cursor, const FVector2D& MenuSize, const FVector2D& ViewportSize)
{
	FVector2D Pos = Cursor;
	if (Pos.X + MenuSize.X > ViewportSize.X)
	{
		Pos.X = FMath::Max(0.0, Pos.X - MenuSize.X);
	}
	if (Pos.Y + MenuSize.Y > ViewportSize.Y)
	{
		Pos.Y = FMath::Max(0.0, Pos.Y - MenuSize.Y);
	}
	Pos.X = FMath::Max(Pos.X, 0.0);
	Pos.Y = FMath::Max(Pos.Y, 0.0);
	return Pos;
}

FVector2D FWLUILogic::WorldToMap(const FVector& Offset, float CameraYawDegrees, float Radius, bool bClampToRim, bool& bOutOnRim, bool& bOutVisible)
{
	const float Yaw = FMath::DegreesToRadians(CameraYawDegrees);
	const FVector2D Fwd(FMath::Cos(Yaw), FMath::Sin(Yaw));
	const FVector2D Right(-FMath::Sin(Yaw), FMath::Cos(Yaw));
	const FVector2D Flat(Offset.X, Offset.Y);
	const float R = FMath::Max(Radius, 1.f);
	FVector2D Map(FVector2D::DotProduct(Flat, Right) / R, FVector2D::DotProduct(Flat, Fwd) / R);

	bOutOnRim = false;
	bOutVisible = true;
	if (Map.SizeSquared() > 1.f)
	{
		if (bClampToRim)
		{
			Map = Map.GetSafeNormal();
			bOutOnRim = true;
		}
		else
		{
			bOutVisible = false;
		}
	}
	return Map;
}

FVector FWLUILogic::MapToWorld(const FVector2D& MapPos, float CameraYawDegrees, float Radius)
{
	const float Yaw = FMath::DegreesToRadians(CameraYawDegrees);
	const FVector Fwd(FMath::Cos(Yaw), FMath::Sin(Yaw), 0.f);
	const FVector Right(-FMath::Sin(Yaw), FMath::Cos(Yaw), 0.f);
	return (Fwd * MapPos.Y + Right * MapPos.X) * Radius;
}

FText FWLUILogic::CraftButtonLabel(int32 Quantity, int32 MaxCraftable)
{
	return Quantity < 0
		? FText::Format(LOCTEXT("MakeAll", "Make All ({0})"), FText::AsNumber(MaxCraftable))
		: FText::Format(LOCTEXT("MakeN", "Make x{0}"), FText::AsNumber(Quantity));
}

#undef LOCTEXT_NAMESPACE
