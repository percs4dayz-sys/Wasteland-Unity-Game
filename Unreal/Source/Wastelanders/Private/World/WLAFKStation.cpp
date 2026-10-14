#include "World/WLAFKStation.h"
#include "Building/WLPlacedPieceComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Items/WLInventoryComponent.h"
#include "Items/WLItemTypes.h"
#include "Skills/WLSkillsComponent.h"

#define LOCTEXT_NAMESPACE "WLAFKStation"

AWLAFKStation::AWLAFKStation()
{
	DisplayName = LOCTEXT("DefaultName", "AFK Station");
	PlaceholderColor = FLinearColor(0.5f, 0.5f, 0.52f);
}

void AWLAFKStation::Configure(const FText& InName, EWLSkill InSkill, int32 InProduceItemId, int32 InConsumeItemId)
{
	DisplayName = InName;
	Skill = InSkill;
	ProduceItemId = InProduceItemId;
	ConsumeItemId = InConsumeItemId;
}

void AWLAFKStation::BeginPlay()
{
	Super::BeginPlay();
	if (HasAuthority())
	{
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			Tick->Register(TScriptInterface<IWLTickable>(this));
		}
	}
}

void AWLAFKStation::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
	{
		Tick->Unregister(TScriptInterface<IWLTickable>(this));
	}
	Super::EndPlay(EndPlayReason);
}

EWLAFKMode AWLAFKStation::NextMode(EWLAFKMode Current, bool bCanProduce, bool bCanProcess)
{
	switch (Current)
	{
	case EWLAFKMode::Off:      return bCanProduce ? EWLAFKMode::Resource : (bCanProcess ? EWLAFKMode::Process : EWLAFKMode::Off);
	case EWLAFKMode::Resource: return bCanProcess ? EWLAFKMode::Process : EWLAFKMode::Off;
	default:                   return EWLAFKMode::Off;
	}
}

FText AWLAFKStation::ModeText(EWLAFKMode Mode)
{
	switch (Mode)
	{
	case EWLAFKMode::Resource: return LOCTEXT("Resource", "Resource");
	case EWLAFKMode::Process:  return LOCTEXT("Process", "Process");
	default:                   return LOCTEXT("Off", "Off");
	}
}

FText AWLAFKStation::GetDefaultVerbLabel() const
{
	return FText::Format(LOCTEXT("SetLabel", "Set {0}"), GetInteractName_Implementation());
}

FText AWLAFKStation::GetExamineText_Implementation() const
{
	// Modes are per player and live on the server, so the examine line can't name the viewer's mode.
	return FText::Format(LOCTEXT("Examine", "{0} - walk up and click to switch mode."), GetInteractName_Implementation());
}

void AWLAFKStation::Msg(AWLPlayerState* PS, const FText& Text) const
{
	if (AWLPlayerController* PC = PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr)
	{
		PC->ClientGameMessage(FText::Format(LOCTEXT("BuildPrefix", "[BUILD]: {0}"), Text));
	}
}

bool AWLAFKStation::Interact_Implementation(AWLPlayerCharacter* Player, FName Verb)
{
	AWLPlayerState* PS = Player ? Player->GetWLPlayerState() : nullptr;
	if (!PS || Verb != GetDefaultVerb())
	{
		return false;
	}
	if (const UWLPlacedPieceComponent* Piece = FindComponentByClass<UWLPlacedPieceComponent>(); Piece && !Piece->IsOwnedBy(PS))
	{
		Msg(PS, LOCTEXT("NotYours", "That station belongs to someone else."));
		return false;
	}

	FUser& User = Users.FindOrAdd(TWeakObjectPtr<AWLPlayerState>(PS));
	User.Mode = NextMode(User.Mode, ProduceItemId > 0, ConsumeItemId > 0);
	User.Ticks = 0;
	Msg(PS, FText::Format(LOCTEXT("SetTo", "{0} set to {1}."), GetInteractName_Implementation(), ModeText(User.Mode)));
	if (User.Mode == EWLAFKMode::Off)
	{
		Users.Remove(TWeakObjectPtr<AWLPlayerState>(PS));
	}
	return true;
}

void AWLAFKStation::OnGameTick(int64 TickCount)
{
	for (auto It = Users.CreateIterator(); It; ++It)
	{
		AWLPlayerState* PS = It.Key().Get();
		if (!PS)
		{
			It.RemoveCurrent(); // logged out
			continue;
		}
		FUser& User = It.Value();
		if (MaxUserDistance > 0.f)
		{
			const APawn* Pawn = PS->GetPawn();
			if (!Pawn || FVector::Dist(Pawn->GetActorLocation(), GetActorLocation()) > MaxUserDistance)
			{
				continue;
			}
		}
		if (++User.Ticks < TicksPerAction)
		{
			continue;
		}
		User.Ticks = 0;

		UWLInventoryComponent* Inv = PS->GetInventory();
		UWLSkillsComponent* Skills = PS->GetSkills();
		if (!Inv || !Skills)
		{
			continue;
		}

		if (User.Mode == EWLAFKMode::Resource)
		{
			if (ProduceItemId <= 0)
			{
				continue;
			}
			FWLItemStack Stack;
			Stack.ItemId = ProduceItemId;
			Stack.Quantity = 1;
			if (Inv->FreeSlots() <= 0 || !Inv->GiveItem(Stack, TEXT("AFK")))
			{
				Msg(PS, FText::Format(LOCTEXT("PackFull", "{0}: your pack is full."), GetInteractName_Implementation()));
				continue;
			}
			Skills->AddXP(Skill, ResourceXP);
		}
		else if (User.Mode == EWLAFKMode::Process)
		{
			if (ConsumeItemId <= 0)
			{
				continue;
			}
			if (!Inv->Contains(ConsumeItemId, 1) || !Inv->RemoveItem(ConsumeItemId, 1))
			{
				Msg(PS, FText::Format(LOCTEXT("OutOfMats", "{0}: out of materials."), GetInteractName_Implementation()));
				continue;
			}
			Skills->AddXP(Skill, ProcessXP);
		}
	}
}

#undef LOCTEXT_NAMESPACE
