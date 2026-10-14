#include "Player/WLPlayerState.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "AbilitySystem/WLCombatAttributeSet.h"
#include "Skills/WLSkillsComponent.h"
#include "Items/WLInventoryComponent.h"
#include "Items/WLEquipmentComponent.h"
#include "Items/WLBankComponent.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Quests/WLQuestComponent.h"
#include "Companions/WLCompanionComponent.h"
#include "Persistence/WLCharacterSave.h"
#include "Player/WLPlayerCharacter.h"
#include "Net/UnrealNetwork.h"

AWLPlayerState::AWLPlayerState()
{
	// GAS on a PlayerState needs a high update rate or attribute changes feel laggy.
	SetNetUpdateFrequency(100.f);

	AbilitySystem = CreateDefaultSubobject<UWLAbilitySystemComponent>(TEXT("AbilitySystem"));
	AbilitySystem->SetIsReplicated(true);
	// Mixed: full GE replication to the owner, minimal to everyone else (MMO bandwidth).
	AbilitySystem->SetReplicationMode(EGameplayEffectReplicationMode::Mixed);

	CombatAttributes = CreateDefaultSubobject<UWLCombatAttributeSet>(TEXT("CombatAttributes"));

	Skills = CreateDefaultSubobject<UWLSkillsComponent>(TEXT("Skills"));
	Inventory = CreateDefaultSubobject<UWLInventoryComponent>(TEXT("Inventory"));
	Equipment = CreateDefaultSubobject<UWLEquipmentComponent>(TEXT("Equipment"));
	Bank = CreateDefaultSubobject<UWLBankComponent>(TEXT("Bank"));
	Flags = CreateDefaultSubobject<UWLPlayerFlagsComponent>(TEXT("Flags"));
	Quests = CreateDefaultSubobject<UWLQuestComponent>(TEXT("Quests"));
	Companions = CreateDefaultSubobject<UWLCompanionComponent>(TEXT("Companions"));
}

void AWLPlayerState::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(AWLPlayerState, Appearance);
	DOREPLIFETIME(AWLPlayerState, CharacterId);
	DOREPLIFETIME(AWLPlayerState, TotalKills);
}

UAbilitySystemComponent* AWLPlayerState::GetAbilitySystemComponent() const
{
	return AbilitySystem;
}

int32 AWLPlayerState::GetCombatLevel() const
{
	return Skills ? Skills->GetCombatLevel() : 3;
}

void AWLPlayerState::CaptureSave(FWLCharacterSave& Out) const
{
	Out.Version = FWLCharacterSave::CurrentVersion;
	Out.CharacterId = CharacterId;
	Out.AccountId = AccountId;
	Out.PlayerName = GetPlayerName();
	Out.Appearance = Appearance;
	Out.TotalKills = TotalKills;
	Out.LastSaved = FDateTime::UtcNow();

	Skills->CaptureSave(Out);
	Inventory->CaptureSave(Out);
	Equipment->CaptureSave(Out);
	Bank->CaptureSave(Out);
	Flags->CaptureSave(Out);
	Quests->CaptureSave(Out);
	Companions->CaptureSave(Out);

	if (const AWLPlayerCharacter* Pawn = GetPawn<AWLPlayerCharacter>())
	{
		Pawn->CaptureSave(Out);
	}
}

void AWLPlayerState::ApplySave(const FWLCharacterSave& In)
{
	check(HasAuthority());

	CharacterId = In.CharacterId;
	AccountId = In.AccountId;
	if (!In.PlayerName.IsEmpty())
	{
		SetPlayerName(In.PlayerName);
	}
	Appearance = In.Appearance;
	TotalKills = In.TotalKills;

	// Order matters: skills before equipment (equip gates read levels), inventory before bank.
	Skills->ApplySave(In);
	Inventory->ApplySave(In);
	Equipment->ApplySave(In);
	Bank->ApplySave(In);
	Flags->ApplySave(In);
	Quests->ApplySave(In);
	Companions->ApplySave(In);

	bCharacterLoaded = true;
	ForceNetUpdate();
}

void AWLPlayerState::CopyProperties(APlayerState* PlayerState)
{
	Super::CopyProperties(PlayerState);
	if (AWLPlayerState* Other = Cast<AWLPlayerState>(PlayerState))
	{
		Other->CharacterId = CharacterId;
		Other->AccountId = AccountId;
		Other->Appearance = Appearance;
		Other->TotalKills = TotalKills;
	}
}

void AWLPlayerState::OnRep_Appearance()
{
	if (AWLPlayerCharacter* Pawn = GetPawn<AWLPlayerCharacter>())
	{
		Pawn->RefreshAppearance();
	}
}
