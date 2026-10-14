#include "Player/WLChatCommands.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerCharacter.h"
#include "Core/WLGameMode.h"
#include "Core/WLTypes.h"
#include "Items/WLInventoryComponent.h"
#include "Skills/WLSkillsComponent.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Data/WLDataSubsystem.h"
#include "Engine/World.h"

#define LOCTEXT_NAMESPACE "WLChatCommands"

namespace
{
	TMap<TWeakObjectPtr<AWLPlayerController>, double> LastStuck;

	void Say(AWLPlayerController* PC, const FText& Text) { PC->ClientGameMessage(Text); }

	bool DevCommandsAllowed(const AWLPlayerController* PC)
	{
#if UE_BUILD_SHIPPING
		const AWLPlayerState* PS = PC->GetWLPlayerState();
		return PS && PS->HasGMRights();
#else
		return true;
#endif
	}

	void DoStuck(AWLPlayerController* PC)
	{
		const double Now = PC->GetWorld()->GetTimeSeconds();
		if (const double* Last = LastStuck.Find(PC); Last && Now - *Last < FWLChatCommands::StuckCooldownSeconds)
		{
			const int32 Wait = FMath::CeilToInt(FWLChatCommands::StuckCooldownSeconds - (Now - *Last));
			Say(PC, FText::Format(LOCTEXT("StuckCooldown", "/stuck is on cooldown. Try again in {0}m {1}s."), Wait / 60, Wait % 60));
			return;
		}
		AWLGameMode* GM = PC->GetWorld()->GetAuthGameMode<AWLGameMode>();
		if (!GM || !GM->TeleportToSpawn(PC))
		{
			Say(PC, LOCTEXT("StuckUnavailable", "/stuck isn't available here."));
			return;
		}
		LastStuck.Add(PC, Now);
		Say(PC, LOCTEXT("StuckDone", "Returned to your spawn point."));
	}

	void DoDev(AWLPlayerController* PC, const TArray<FString>& Args)
	{
		if (!DevCommandsAllowed(PC))
		{
			Say(PC, LOCTEXT("NoDev", "You don't have permission to use developer commands."));
			return;
		}
		AWLPlayerState* PS = PC->GetWLPlayerState();
		if (!PS || Args.Num() < 2)
		{
			Say(PC, LOCTEXT("DevUsage", "/dev item <id> [qty] | /dev xp <skill> <amount> | /dev heal"));
			return;
		}

		const FString Sub = Args[1].ToLower();
		if (Sub == TEXT("item") && Args.Num() >= 3)
		{
			const int32 Id = FCString::Atoi(*Args[2]);
			const int32 Qty = Args.Num() >= 4 ? FMath::Max(1, FCString::Atoi(*Args[3])) : 1;
			const bool bOk = PS->GetInventory()->AddItem(Id, Qty);
			Say(PC, bOk ? FText::Format(LOCTEXT("DevItem", "Spawned {0} x {1}."), Qty, UWLDataSubsystem::Get(PC)->GetItemName(Id))
			            : LOCTEXT("DevItemFail", "Couldn't add that item (unknown id or full inventory)."));
		}
		else if (Sub == TEXT("xp") && Args.Num() >= 4)
		{
			EWLSkill Skill;
			if (!WLTypes::SkillFromString(Args[2], Skill))
			{
				Say(PC, LOCTEXT("DevSkill", "Unknown skill."));
				return;
			}
			PS->GetSkills()->AddXP(Skill, FMath::Max(0, FCString::Atoi(*Args[3])));
		}
		else if (Sub == TEXT("heal"))
		{
			PS->GetWLAbilitySystem()->HealToFull();
		}
		else
		{
			Say(PC, LOCTEXT("DevUnknown", "Unknown /dev command."));
		}
	}
}

void FWLChatCommands::Execute(AWLPlayerController* PC, const FString& CommandLine)
{
	if (!PC || !PC->HasAuthority())
	{
		return;
	}

	TArray<FString> Args;
	CommandLine.ParseIntoArrayWS(Args);
	if (Args.Num() == 0)
	{
		return;
	}

	const FString Cmd = Args[0].ToLower();
	if (Cmd == TEXT("stuck") || Cmd == TEXT("unstuck"))
	{
		DoStuck(PC);
	}
	else if (Cmd == TEXT("help"))
	{
		Say(PC, LOCTEXT("Help", "Commands: /stuck returns you to a spawn point if you get stuck or fall out of the world. /played shows your session stats."));
	}
	else if (Cmd == TEXT("played"))
	{
		const AWLPlayerState* PS = PC->GetWLPlayerState();
		const int32 Minutes = FMath::FloorToInt(PC->GetWorld()->GetTimeSeconds() / 60.0);
		Say(PC, FText::Format(LOCTEXT("Played", "Server up {0} min. Total kills: {1}."), Minutes, PS ? PS->GetTotalKills() : 0));
	}
	else if (Cmd == TEXT("dev"))
	{
		DoDev(PC, Args);
	}
	else
	{
		Say(PC, FText::Format(LOCTEXT("Unknown", "Unknown command: /{0}. Type /help."), FText::FromString(Cmd)));
	}
}

#undef LOCTEXT_NAMESPACE
