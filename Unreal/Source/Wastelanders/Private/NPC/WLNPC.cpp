#include "NPC/WLNPC.h"
#include "Dialogue/WLDialogueAsset.h"
#include "Dialogue/WLDialogueComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerState.h"
#include "World/WLPlaceholderMesh.h"
#include "Components/CapsuleComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Components/TextRenderComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "GameFramework/CharacterMovementComponent.h"

#define LOCTEXT_NAMESPACE "WLNPC"

AWLNPC::AWLNPC(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
	bReplicates = true;
	SetReplicatingMovement(true);   // the facing turn
	PrimaryActorTick.bCanEverTick = false;

	// Unity RoxyAutoSetup's human-sized capsule (1.8 m x 0.4 m).
	GetCapsuleComponent()->InitCapsuleSize(40.f, 90.f);
	GetCapsuleComponent()->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);   // clickable

	// NPCs stand still: no movement simulation cost.
	GetCharacterMovement()->SetMovementMode(MOVE_None);
	GetCharacterMovement()->SetComponentTickEnabled(false);
	GetCharacterMovement()->GravityScale = 0.f;

	PlaceholderBody = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("PlaceholderBody"));
	PlaceholderBody->SetupAttachment(GetCapsuleComponent());
	PlaceholderBody->SetCollisionEnabled(ECollisionEnabled::NoCollision);

	NameTag = CreateDefaultSubobject<UTextRenderComponent>(TEXT("NameTag"));
	NameTag->SetupAttachment(GetCapsuleComponent());
	NameTag->SetHorizontalAlignment(EHTA_Center);
	NameTag->SetWorldSize(22.f);
	NameTag->SetTextRenderColor(FColor(255, 200, 64));
	NameTag->SetRelativeLocation(FVector(0.f, 0.f, 115.f));

	DisplayName = LOCTEXT("DefaultName", "Wastelander");
	ExamineText = LOCTEXT("DefaultExamine", "A scavenger in a gas mask, pack stuffed with other people's junk.");
}

void AWLNPC::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	const bool bPlaceholder = FWLPlaceholderMesh::Apply(PlaceholderBody, BodyArt, EWLPlaceholderShape::Cylinder, FVector(60.f, 60.f, 180.f), PlaceholderColor);
	if (bPlaceholder)
	{
		PlaceholderBody->SetRelativeLocation(FVector::ZeroVector);   // capsule centre = cylinder centre
	}
	// Hide the placeholder once skeletal art is assigned on the inherited mesh.
	PlaceholderBody->SetVisibility(!(GetMesh() && GetMesh()->GetSkeletalMeshAsset()) || !bPlaceholder);
	GetMesh()->SetRelativeRotation(FRotator(0.f, -90.f + MeshYawOffset, 0.f));
	NameTag->SetText(DisplayName);
}

void AWLNPC::BeginPlay()
{
	Super::BeginPlay();
	NameTag->SetText(DisplayName);
	if (HasAuthority() && !DialogueAsset)
	{
		RuntimeDialogue = FWLBuiltinDialogues::Build(BuiltinDialogue, this, WandererLines);
	}
}

const UWLDialogueAsset* AWLNPC::GetDialogue() const
{
	return DialogueAsset ? DialogueAsset.Get() : RuntimeDialogue.Get();
}

TArray<FWLInteractOption> AWLNPC::GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const
{
	TArray<FWLInteractOption> Options;
	FWLInteractOption& Talk = Options.AddDefaulted_GetRef();
	Talk.Verb = TEXT("Talk");
	Talk.Label = FText::Format(LOCTEXT("TalkTo", "Talk-to {0}"), DisplayName);
	Talk.bDefault = true;
	return Options;
}

bool AWLNPC::Interact_Implementation(AWLPlayerCharacter* Player, FName Verb)
{
	if (!HasAuthority() || !Player)
	{
		return false;
	}
	AWLPlayerState* PS = Player->GetWLPlayerState();
	if (!PS)
	{
		return false;
	}

	if (Verb == TEXT("Examine"))
	{
		if (AWLPlayerController* PC = Cast<AWLPlayerController>(Player->GetController()))
		{
			PC->ClientGameMessage(ExamineText);
		}
		return true;
	}
	if (Verb != TEXT("Talk"))
	{
		return false;
	}
	// Range is enforced by the interactor before Interact runs; re-check loosely against teleport tricks.
	if (FVector::Dist2D(Player->GetActorLocation(), GetActorLocation()) > InteractRange + 300.f)
	{
		return false;
	}

	UWLDialogueComponent* Dialogue = UWLDialogueComponent::Get(PS);
	const UWLDialogueAsset* Asset = GetDialogue();
	if (!Dialogue || !Asset)
	{
		return false;
	}
	FacePlayer(Player);
	Dialogue->Talk(this, Asset, DisplayName);
	return true;
}

void AWLNPC::FacePlayer(const AActor* Player)
{
	// Yaw only, so any upright tilt the model needs is preserved (TutorialNPC's 3D path).
	FVector D = Player->GetActorLocation() - GetActorLocation();
	D.Z = 0.f;
	if (D.SizeSquared() > 1.f)
	{
		FRotator R = GetActorRotation();
		R.Yaw = D.Rotation().Yaw;
		SetActorRotation(R);
	}
}

// ------------------------------------------------------------------ configured NPCs

AWLRoxyNPC::AWLRoxyNPC(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
	BuiltinDialogue = EWLBuiltinDialogue::Roxy;
	DisplayName = LOCTEXT("RoxyName", "Roxy");
	ExamineText = LOCTEXT("RoxyExamine", "Roxy. Trouble in all the best ways. She's got work for you.");
	PlaceholderColor = FLinearColor(1.f, 0.48f, 0.69f);
}

AWLTutorialNPC::AWLTutorialNPC(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
	BuiltinDialogue = EWLBuiltinDialogue::OldMara;
	DisplayName = LOCTEXT("MaraName", "Old Mara");
	ExamineText = LOCTEXT("MaraExamine", "Old Mara. A weathered survivor who's lasted longer than most.");
	PlaceholderColor = FLinearColor(0.6f, 0.55f, 0.4f);
}

AWLWandererNPC::AWLWandererNPC(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
	BuiltinDialogue = EWLBuiltinDialogue::Wanderer;
}

#undef LOCTEXT_NAMESPACE
