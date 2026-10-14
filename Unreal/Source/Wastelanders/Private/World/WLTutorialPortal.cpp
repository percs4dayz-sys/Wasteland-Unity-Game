#include "World/WLTutorialPortal.h"
#include "World/WLTravel.h"
#include "World/WLWorldSettings.h"
#include "World/WLPlaceholderMesh.h"
#include "Interaction/WLInteractorComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Skills/WLSkillsComponent.h"
#include "Quests/WLPlayerFlagsComponent.h"
#include "Data/WLDataSubsystem.h"
#include "Components/BoxComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/World.h"

#define LOCTEXT_NAMESPACE "WLTutorialPortal"

AWLTutorialPortal::AWLTutorialPortal()
{
	PrimaryActorTick.bCanEverTick = false;
	bReplicates = false;

	Trigger = CreateDefaultSubobject<UBoxComponent>(TEXT("Trigger"));
	SetRootComponent(Trigger);
	Trigger->SetBoxExtent(FVector(80.f, 120.f, 150.f));
	Trigger->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
	Trigger->SetCollisionResponseToAllChannels(ECR_Ignore);
	Trigger->SetCollisionResponseToChannel(ECC_Pawn, ECR_Overlap);
	Trigger->SetGenerateOverlapEvents(true);

	Visual = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Visual"));
	Visual->SetupAttachment(Trigger);
	Visual->SetCollisionEnabled(ECollisionEnabled::NoCollision);

	// Unity IsTutorialSkill: not retired melee (Attack/Strength/Defence), Beastmastery, Fission or
	// Refinement. Endurance starts at 10 so it always passes; it is listed for faithfulness.
	TutorialSkills = { EWLSkill::Endurance, EWLSkill::Marksmanship, EWLSkill::Fishing, EWLSkill::Cooking,
		EWLSkill::Woodcutting, EWLSkill::Smithing, EWLSkill::Scrapping };
}

void AWLTutorialPortal::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	FWLPlaceholderMesh::Apply(Visual, ArtMesh, EWLPlaceholderShape::Cylinder, FVector(40.f, 220.f, 280.f), FLinearColor(0.4f, 0.9f, 0.7f));
}

void AWLTutorialPortal::BeginPlay()
{
	Super::BeginPlay();
	if (HasAuthority())
	{
		Trigger->OnComponentBeginOverlap.AddDynamic(this, &AWLTutorialPortal::OnTriggerBegin);
	}
}

TArray<EWLSkill> AWLTutorialPortal::IncompleteSkills(TConstArrayView<EWLSkill> Skills, int32 Required, TFunctionRef<int32(EWLSkill)> LevelOf)
{
	TArray<EWLSkill> Out;
	for (const EWLSkill S : Skills)
	{
		if (LevelOf(S) < Required)
		{
			Out.Add(S);
		}
	}
	return Out;
}

void AWLTutorialPortal::OnTriggerBegin(UPrimitiveComponent* OverlappedComp, AActor* OtherActor, UPrimitiveComponent* OtherComp, int32 OtherBodyIndex, bool bFromSweep, const FHitResult& SweepResult)
{
	AWLPlayerCharacter* Char = Cast<AWLPlayerCharacter>(OtherActor);
	AWLPlayerState* PS = Char ? Char->GetWLPlayerState() : nullptr;
	if (!PS)
	{
		return;
	}
	const double Now = GetWorld()->GetTimeSeconds();
	const TWeakObjectPtr<AWLPlayerState> Key(PS);
	if (const double* Next = NextUseTime.Find(Key); Next && Now < *Next)
	{
		return; // standing in it doesn't spam
	}
	NextUseTime.Add(Key, Now + RetriggerCooldown);
	TryEnter(Char);
}

void AWLTutorialPortal::TryEnter(AWLPlayerCharacter* Char)
{
	AWLPlayerState* PS = Char->GetWLPlayerState();
	AWLPlayerController* PC = Cast<AWLPlayerController>(Char->GetController());
	const UWLSkillsComponent* Skills = PS->GetSkills();
	const UWLPlayerFlagsComponent* Flags = PS->GetFlags();
	if (!Skills || !Flags || !PC)
	{
		return;
	}

	const TArray<EWLSkill> Missing = IncompleteSkills(TutorialSkills, RequiredLevel, [Skills](EWLSkill S) { return Skills->GetLevel(S); });
	if (Missing.Num() > 0)
	{
		const UWLDataSubsystem* Data = UWLDataSubsystem::Get(this);
		TArray<FText> Names;
		for (const EWLSkill S : Missing)
		{
			const FWLSkillRow* Row = Data ? Data->FindSkill(S) : nullptr;
			Names.Add(Row ? Row->DisplayName : FText::FromString(WLTypes::SkillToString(S)));
		}
		PC->ClientGameMessage(FText::Format(LOCTEXT("NotReady", "[PORTAL]: The portal crackles and pushes you back. Reach level {0} in: {1}"),
			RequiredLevel, FText::Join(LOCTEXT("Comma", ", "), Names)));
		return;
	}

	// Mara's last-second gift: everyone needs a companion out there.
	if (!Flags->HasFlag(TEXT("egg_chosen")) && !Flags->HasFlag(TEXT("pup_hatched")))
	{
		if (UWLInteractorComponent* Interactor = Char->GetInteractor())
		{
			Interactor->ClientWorldEvent(TEXT("EggChoice"), LOCTEXT("Mara", "Old Mara"),
				LOCTEXT("MaraLine", "Hold it right there, Survivor! One more thing before you step through. Pick one of these young beasts - your choice, and you only get the one. Everyone needs a companion out there."));
		}
		return;
	}

	if (bTravelToOtherMap)
	{
		FWLTravel::TravelToDestination(PC, UWLWorldSettings::Get()->MainWorldDestination);
	}
	else
	{
		FVector Dest = GetActorTransform().TransformPosition(DestinationOffset);
		FWLTravel::SeatOnGround(GetWorld(), Dest, 90.f, Char);
		if (FWLTravel::TeleportPlayer(Char, Dest))
		{
			PC->ClientGameMessage(LOCTEXT("Stepping", "[WARP]: Stepping into the wasteland..."));
		}
	}
}

#undef LOCTEXT_NAMESPACE
