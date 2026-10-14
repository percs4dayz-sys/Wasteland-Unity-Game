#include "World/WLInteractableActor.h"
#include "Interaction/WLInteractionLibrary.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerController.h"
#include "Components/StaticMeshComponent.h"
#include "Net/UnrealNetwork.h"

AWLInteractableActor::AWLInteractableActor()
{
	PrimaryActorTick.bCanEverTick = false;
	bReplicates = true;
	SetNetUpdateFrequency(10.f);

	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	SetRootComponent(Root);

	Mesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Mesh"));
	Mesh->SetupAttachment(Root);
	Mesh->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
	Mesh->SetCanEverAffectNavigation(true);
}

void AWLInteractableActor::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	// Placed pieces get their names at runtime, so the name replicates; examine text is static data.
	DOREPLIFETIME(AWLInteractableActor, DisplayName);
}

void AWLInteractableActor::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	RefreshVisual();
}

void AWLInteractableActor::RefreshVisual()
{
	if (!Mesh)
	{
		return;
	}
	if (FWLPlaceholderMesh::Apply(Mesh, ArtMesh, PlaceholderShape, PlaceholderSize, PlaceholderColor))
	{
		// Basic shapes are centred on their origin; lift so the shape stands on the actor's feet
		// (Unity lifted primitives by half their height for the same reason).
		Mesh->SetRelativeLocation(FVector(0.f, 0.f, PlaceholderShape == EWLPlaceholderShape::Plane ? 1.f : PlaceholderSize.Z * 0.5f));
	}
	else
	{
		Mesh->SetRelativeLocation(FVector::ZeroVector);
		Mesh->SetRelativeScale3D(FVector::OneVector);
	}
}

FText AWLInteractableActor::GetInteractName_Implementation() const
{
	return DisplayName.IsEmpty() ? FText::FromString(FWLInteraction::PrettyName(GetName())) : DisplayName;
}

FText AWLInteractableActor::GetExamineText_Implementation() const
{
	return ExamineText;
}

FText AWLInteractableActor::GetDefaultVerbLabel() const
{
	return FText::Format(NSLOCTEXT("WLWorld", "UseLabel", "Use {0}"), GetInteractName_Implementation());
}

TArray<FWLInteractOption> AWLInteractableActor::GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const
{
	FWLInteractOption Option;
	Option.Verb = GetDefaultVerb();
	Option.Label = GetDefaultVerbLabel();
	Option.bDefault = true;
	return { Option };
}

AWLPlayerController* AWLInteractableActor::GetPlayerController(const AWLPlayerCharacter* Player)
{
	return Player ? Cast<AWLPlayerController>(Player->GetController()) : nullptr;
}

void AWLInteractableActor::SendMessage(const AWLPlayerCharacter* Player, const FText& Message)
{
	if (AWLPlayerController* PC = GetPlayerController(Player))
	{
		PC->ClientGameMessage(Message);
	}
}
