#include "Items/WLBankingCrate.h"
#include "Items/WLBankComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "UObject/ConstructorHelpers.h"

#define LOCTEXT_NAMESPACE "WLBankingCrate"

const FName AWLBankingCrate::VerbBank(TEXT("Bank"));

AWLBankingCrate::AWLBankingCrate()
{
	bReplicates = true;
	PrimaryActorTick.bCanEverTick = false;
	Label = LOCTEXT("DefaultLabel", "Abandoned Crate");

	Mesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Mesh"));
	SetRootComponent(Mesh);
	Mesh->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);

	// Placeholder until new-art-direction booth meshes exist: a crate-sized engine cube.
	static ConstructorHelpers::FObjectFinder<UStaticMesh> Cube(TEXT("/Engine/BasicShapes/Cube.Cube"));
	if (Cube.Succeeded())
	{
		Mesh->SetStaticMesh(Cube.Object);
	}
	Mesh->SetRelativeScale3D(FVector(0.9f, 0.6f, 0.6f));
}

FText AWLBankingCrate::GetInteractName_Implementation() const
{
	return Label;
}

FText AWLBankingCrate::GetExamineText_Implementation() const
{
	return LOCTEXT("Examine", "A battered storage crate. Stash your gear here for safekeeping.");
}

TArray<FWLInteractOption> AWLBankingCrate::GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const
{
	FWLInteractOption Bank;
	Bank.Verb = VerbBank;
	Bank.Label = LOCTEXT("BankVerb", "Bank");
	Bank.bDefault = true;
	return { Bank };
}

float AWLBankingCrate::GetInteractRange_Implementation() const
{
	return InteractRange;
}

bool AWLBankingCrate::Interact_Implementation(AWLPlayerCharacter* Player, FName Verb)
{
	if (!HasAuthority() || !Player || Verb != VerbBank)
	{
		return false;
	}
	AWLPlayerState* PS = Player->GetWLPlayerState();
	UWLBankComponent* Bank = PS ? PS->GetBank() : nullptr;
	if (!Bank)
	{
		return false;
	}
	if (const UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem())
	{
		if (ASC->IsDead())
		{
			return false;
		}
	}
	if (FVector::DistSquared(Player->GetActorLocation(), GetActorLocation()) > FMath::Square(Bank->UseRange))
	{
		return false;
	}
	Bank->OpenBank(this);
	return true;
}

#undef LOCTEXT_NAMESPACE
