#include "Skills/WLSkillsComponent.h"
#include "Skills/WLSkillMath.h"
#include "Core/WLXPTable.h"
#include "Core/WLGameEventsSubsystem.h"
#include "Data/WLDataSubsystem.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Persistence/WLCharacterSave.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Net/UnrealNetwork.h"

#define LOCTEXT_NAMESPACE "WLSkills"

namespace
{
	constexpr int32 NumSkills = static_cast<int32>(EWLSkill::Count);

	int32 MinXPFor(EWLSkill Skill)
	{
		// Endurance starts at level 10: a fresh character must never read 1 HP.
		return FWLXPTable::XPForLevel(Skill, FWLXPTable::MinLevelFor(Skill));
	}
}

UWLSkillsComponent::UWLSkillsComponent()
{
	SetIsReplicatedByDefault(true);
	SkillXP.SetNumZeroed(NumSkills);
	for (EWLSkill Skill : TEnumRange<EWLSkill>())
	{
		SkillXP[static_cast<int32>(Skill)] = MinXPFor(Skill);
	}
}

void UWLSkillsComponent::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(UWLSkillsComponent, SkillXP);
}

void UWLSkillsComponent::BeginPlay()
{
	Super::BeginPlay();
	if (GetOwner() && GetOwner()->HasAuthority())
	{
		SyncMaxHealth(false);
	}
}

int32 UWLSkillsComponent::GetXP(EWLSkill Skill) const
{
	const int32 Index = static_cast<int32>(Skill);
	return SkillXP.IsValidIndex(Index) ? SkillXP[Index] : 0;
}

int32 UWLSkillsComponent::GetLevel(EWLSkill Skill) const
{
	// Unknown skill -> its minimum level, never a bare 1 (so Endurance can't report 1 HP).
	const int32 Index = static_cast<int32>(Skill);
	return SkillXP.IsValidIndex(Index) ? FWLXPTable::LevelForXP(Skill, SkillXP[Index]) : FWLXPTable::MinLevelFor(Skill);
}

int32 UWLSkillsComponent::GetTotalLevel() const
{
	int32 Total = 0;
	for (EWLSkill Skill : TEnumRange<EWLSkill>())
	{
		Total += GetLevel(Skill);
	}
	return Total;
}

int32 UWLSkillsComponent::GetCombatLevel() const
{
	return FWLSkillMath::CombatLevel(GetLevel(EWLSkill::Endurance), GetLevel(EWLSkill::Attack), GetLevel(EWLSkill::Strength),
		GetLevel(EWLSkill::Defence), GetLevel(EWLSkill::Marksmanship), GetLevel(EWLSkill::Fission));
}

float UWLSkillsComponent::GetLevelProgress(EWLSkill Skill) const
{
	return FWLXPTable::LevelProgress(Skill, GetXP(Skill));
}

int32 UWLSkillsComponent::GetXPToNextLevel(EWLSkill Skill) const
{
	return FWLXPTable::XPToNextLevel(Skill, GetXP(Skill));
}

int32 UWLSkillsComponent::GetMaxHP() const
{
	return FWLSkillMath::HPForLevel(GetLevel(EWLSkill::Endurance));
}

bool UWLSkillsComponent::IsReadyToLeaveTutorial() const
{
	return GetIncompleteTutorialSkills().Num() == 0;
}

TArray<EWLSkill> UWLSkillsComponent::GetIncompleteTutorialSkills() const
{
	TArray<EWLSkill> Out;
	for (EWLSkill Skill : TEnumRange<EWLSkill>())
	{
		if (FWLSkillMath::IsTutorialSkill(Skill) && GetLevel(Skill) < FWLSkillMath::TutorialLevelRequired)
		{
			Out.Add(Skill);
		}
	}
	return Out;
}

FText UWLSkillsComponent::GetSkillDisplayName(EWLSkill Skill) const
{
	if (const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this))
	{
		if (const FWLSkillRow* Row = Data->FindSkill(Skill))
		{
			if (!Row->DisplayName.IsEmpty())
			{
				return Row->DisplayName;
			}
		}
	}
	return FText::FromString(WLTypes::SkillToString(Skill));
}

void UWLSkillsComponent::AddXP(EWLSkill Skill, int32 Amount)
{
	const int32 Index = static_cast<int32>(Skill);
	if (Amount <= 0 || !SkillXP.IsValidIndex(Index) || !GetOwner() || !GetOwner()->HasAuthority())
	{
		return;   // retired/unknown skill or client call: no-op
	}

	const int32 OldLevel = GetLevel(Skill);
	// Saturate instead of overflowing int32 at absurd totals.
	SkillXP[Index] = static_cast<int32>(FMath::Min<int64>(static_cast<int64>(SkillXP[Index]) + Amount, MAX_int32));
	const int32 NewLevel = GetLevel(Skill);

	AWLPlayerState* PS = GetWLPlayerState();
	UWLGameEventsSubsystem* Events = UWLGameEventsSubsystem::Get(this);

	OnXPGained.Broadcast(Skill, Amount);
	if (Events)
	{
		Events->OnXPGained.Broadcast(PS, Skill, Amount);
	}

	if (NewLevel > OldLevel)
	{
		if (Skill == EWLSkill::Endurance)
		{
			SyncMaxHealth(true);
		}
		OnLevelUp.Broadcast(Skill, NewLevel);
		if (Events)
		{
			Events->OnLevelUp.Broadcast(PS, Skill, NewLevel);
		}
		SendLevelUpMessage(Skill, NewLevel);
	}

	OnSkillsChanged.Broadcast(this);
	if (AActor* Owner = GetOwner())
	{
		Owner->ForceNetUpdate();
	}
}

void UWLSkillsComponent::SetXP(EWLSkill Skill, int32 XP)
{
	const int32 Index = static_cast<int32>(Skill);
	if (!SkillXP.IsValidIndex(Index) || !GetOwner() || !GetOwner()->HasAuthority())
	{
		return;
	}
	SkillXP[Index] = FMath::Max(XP, MinXPFor(Skill));
	if (Skill == EWLSkill::Endurance)
	{
		SyncMaxHealth(false);
	}
	OnSkillsChanged.Broadcast(this);
}

void UWLSkillsComponent::SendLevelUpMessage(EWLSkill Skill, int32 NewLevel) const
{
	const AWLPlayerState* PS = GetWLPlayerState();
	AWLPlayerController* PC = PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr;
	if (!PC)
	{
		return;
	}
	FText Flavour;
	if (const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this))
	{
		if (const FWLSkillRow* Row = Data->FindSkill(Skill))
		{
			Flavour = Row->LevelUpFlavour;
		}
	}
	// XPDropUI.OnLevelUp: "Congratulations! You've reached <Skill> level N.  <flavour>"
	const FText Line = Flavour.IsEmpty()
		? FText::Format(LOCTEXT("LevelUp", "Congratulations! You've reached {0} level {1}."), GetSkillDisplayName(Skill), NewLevel)
		: FText::Format(LOCTEXT("LevelUpFlavour", "Congratulations! You've reached {0} level {1}.  {2}"), GetSkillDisplayName(Skill), NewLevel, Flavour);
	PC->ClientGameMessage(Line);
}

void UWLSkillsComponent::SyncMaxHealth(bool bHealDelta)
{
	if (AWLPlayerState* PS = GetWLPlayerState())
	{
		if (UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem())
		{
			ASC->SetMaxHealth(GetMaxHP(), bHealDelta);
		}
	}
}

void UWLSkillsComponent::OnRep_SkillXP(const TArray<int32>& OldXP)
{
	// Re-derive the server's events from the diff so client UI (XP drops, level-up fanfare) can bind
	// to the same delegates. A first replication (empty/default OldXP) is a load, not a gain.
	// The constructor already filled the array on clients, so the size can't tell; a flag does.
	const bool bInitial = !bReceivedInitialXP || OldXP.Num() != SkillXP.Num();
	bReceivedInitialXP = true;
	if (!bInitial)
	{
		for (EWLSkill Skill : TEnumRange<EWLSkill>())
		{
			const int32 Index = static_cast<int32>(Skill);
			if (!SkillXP.IsValidIndex(Index) || SkillXP[Index] <= OldXP[Index])
			{
				continue;
			}
			OnXPGained.Broadcast(Skill, SkillXP[Index] - OldXP[Index]);
			const int32 OldLevel = FWLXPTable::LevelForXP(Skill, OldXP[Index]);
			const int32 NewLevel = FWLXPTable::LevelForXP(Skill, SkillXP[Index]);
			if (NewLevel > OldLevel)
			{
				OnLevelUp.Broadcast(Skill, NewLevel);
			}
		}
	}
	OnSkillsChanged.Broadcast(this);
}

void UWLSkillsComponent::CaptureSave(FWLCharacterSave& Out) const
{
	Out.SkillXP.Reset();
	for (EWLSkill Skill : TEnumRange<EWLSkill>())
	{
		FWLSaveSkill& Entry = Out.SkillXP.AddDefaulted_GetRef();
		Entry.Skill = WLTypes::SkillToString(Skill);
		Entry.XP = GetXP(Skill);
	}
}

void UWLSkillsComponent::ApplySave(const FWLCharacterSave& In)
{
	for (EWLSkill Skill : TEnumRange<EWLSkill>())
	{
		SkillXP[static_cast<int32>(Skill)] = MinXPFor(Skill);
	}

	TSet<EWLSkill> Loaded;
	for (const FWLSaveSkill& Entry : In.SkillXP)
	{
		EWLSkill Skill;
		if (!WLTypes::SkillFromString(Entry.Skill, Skill))
		{
			continue;   // removed skill: drop its XP rather than fail the load
		}
		int32& XP = SkillXP[static_cast<int32>(Skill)];
		const int32 Value = FMath::Max(Entry.XP, MinXPFor(Skill));
		// Smithing absorbed both Hearthcraft and Tinkering. Unity let whichever loaded last win;
		// keeping the higher one never loses a player's progress.
		XP = Loaded.Contains(Skill) ? FMath::Max(XP, Value) : Value;
		Loaded.Add(Skill);
	}

	SyncMaxHealth(false);
	OnSkillsChanged.Broadcast(this);
}

AWLPlayerState* UWLSkillsComponent::GetWLPlayerState() const
{
	return Cast<AWLPlayerState>(GetOwner());
}

#undef LOCTEXT_NAMESPACE
