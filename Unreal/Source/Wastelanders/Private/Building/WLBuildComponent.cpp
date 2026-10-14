#include "Building/WLBuildComponent.h"
#include "Building/WLPlacedPieceComponent.h"
#include "Building/WLHousingPlot.h"
#include "Building/WLTurret.h"
#include "World/WLCraftingStation.h"
#include "World/WLAFKStation.h"
#include "World/WLPlaceholderMesh.h"
#include "World/WLWorldSettings.h"
#include "Items/WLBankingCrate.h"
#include "Items/WLInventoryComponent.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Skills/WLSkillsComponent.h"
#include "Player/WLPlayerCharacter.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerController.h"
#include "Data/WLDataSubsystem.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "CollisionQueryParams.h"

#define LOCTEXT_NAMESPACE "WLBuildComponent"

namespace
{
	// Unity: Hearthcraft is PlayerStats' Smithing skill.
	constexpr EWLSkill HearthcraftSkill = EWLSkill::Smithing;
}

UWLBuildComponent::UWLBuildComponent()
{
	PrimaryComponentTick.bCanEverTick = false;
	SetIsReplicatedByDefault(true);
}

UWLBuildComponent* UWLBuildComponent::EnsureOn(AActor* Owner)
{
	if (!Owner || !Owner->HasAuthority())
	{
		return nullptr;
	}
	if (UWLBuildComponent* Existing = Owner->FindComponentByClass<UWLBuildComponent>())
	{
		return Existing;
	}
	UWLBuildComponent* Comp = NewObject<UWLBuildComponent>(Owner, TEXT("BuildComponent"));
	Comp->SetIsReplicated(true);
	Comp->RegisterComponent();
	return Comp;
}

AWLPlayerCharacter* UWLBuildComponent::GetCharacter() const
{
	return Cast<AWLPlayerCharacter>(GetOwner());
}

AWLPlayerState* UWLBuildComponent::GetPlayerState() const
{
	const AWLPlayerCharacter* Char = GetCharacter();
	return Char ? Char->GetWLPlayerState() : nullptr;
}

void UWLBuildComponent::Msg(const FText& Text) const
{
	const AWLPlayerState* PS = GetPlayerState();
	if (AWLPlayerController* PC = PS ? Cast<AWLPlayerController>(PS->GetOwningController()) : nullptr)
	{
		PC->ClientGameMessage(FText::Format(LOCTEXT("Prefix", "[BUILD]: {0}"), Text));
	}
}

void UWLBuildComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	DestroyGhost();
	Super::EndPlay(EndPlayReason);
}

void UWLBuildComponent::ClientEndPlacement_Implementation()
{
	CancelPlacement();
}

// --- local checks -------------------------------------------------------------------------------------

bool UWLBuildComponent::HasLevel(const FWLBuildable& B) const
{
	const AWLPlayerState* PS = GetPlayerState();
	return PS && PS->GetSkills() && FWLBuildRules::MeetsLevel(PS->GetSkills()->GetLevel(HearthcraftSkill), B);
}

bool UWLBuildComponent::CanAfford(const FWLBuildable& B) const
{
	const AWLPlayerState* PS = GetPlayerState();
	const UWLInventoryComponent* Inv = PS ? PS->GetInventory() : nullptr;
	if (!Inv)
	{
		return false;
	}
	return FWLBuildRules::CanAfford([Inv](int32 Id) { return Inv->CountOf(Id); }, B);
}

FText UWLBuildComponent::WhyCantBuild(const FWLBuildable& B) const
{
	if (!HasLevel(B))
	{
		return FText::Format(LOCTEXT("NeedLevel", "You need Hearthcraft level {0} to build that."), FText::AsNumber(B.HearthLevel));
	}
	return LOCTEXT("NeedMats", "You don't have the materials for that.");
}

FText UWLBuildComponent::CostText(const UObject* WorldContext, const FWLBuildable& B)
{
	if (B.Cost.IsEmpty())
	{
		return LOCTEXT("Free", "free");
	}
	const UWLDataSubsystem* Data = UWLDataSubsystem::Get(WorldContext);
	TArray<FText> Parts;
	for (const FWLBuildCost& C : B.Cost)
	{
		const FText Name = Data ? Data->GetItemName(C.ItemId) : FText::GetEmpty();
		Parts.Add(FText::Format(LOCTEXT("CostPart", "{0} {1}"), FText::AsNumber(C.Quantity),
			Name.IsEmpty() ? FText::Format(LOCTEXT("ItemN", "item {0}"), FText::AsNumber(C.ItemId)) : Name));
	}
	return FText::Join(FText::FromString(TEXT(", ")), Parts);
}

// --- client placement ---------------------------------------------------------------------------------

bool UWLBuildComponent::BeginPlacement(int32 BuildableIndex)
{
	const UWLBuildCatalog* Catalog = UWLBuildCatalog::Get();
	if (!Catalog || !Catalog->IsValidIndex(BuildableIndex))
	{
		return false;
	}
	const FWLBuildable& B = Catalog->Buildables[BuildableIndex];
	if (!HasLevel(B))
	{
		Msg(WhyCantBuild(B));
		return false;
	}

	DestroyGhost();
	PlacingIndex = BuildableIndex;
	GhostYaw = 0.f;
	bHaveGhostGround = false;

	if (AActor* Owner = GetOwner())
	{
		Ghost = NewObject<UStaticMeshComponent>(Owner, NAME_None, RF_Transient);
		Ghost->SetCollisionEnabled(ECollisionEnabled::NoCollision); // must not block its own ground trace
		Ghost->SetCastShadow(false);
		Ghost->SetUsingAbsoluteLocation(true);
		Ghost->SetUsingAbsoluteRotation(true);
		Ghost->SetUsingAbsoluteScale(true);
		Ghost->SetupAttachment(Owner->GetRootComponent());
		Ghost->RegisterComponent();
		const EWLPlaceholderShape Shape = B.Kind == EWLBuildKind::Turret ? EWLPlaceholderShape::Cylinder : EWLPlaceholderShape::Cube;
		FWLPlaceholderMesh::Apply(Ghost, TSoftObjectPtr<UStaticMesh>(), Shape, B.Size, B.Color);
		Ghost->SetVisibility(false);
	}
	SetGhostTint(HasLevel(B) && CanAfford(B));

	Msg(FText::Format(LOCTEXT("Placing", "Placing {0} - click to build, R to rotate, Esc to cancel."), B.Name));
	OnPlacementChanged.Broadcast(PlacingIndex);
	return true;
}

void UWLBuildComponent::CancelPlacement()
{
	if (!IsPlacing())
	{
		return;
	}
	PlacingIndex = INDEX_NONE;
	bHaveGhostGround = false;
	DestroyGhost();
	OnPlacementChanged.Broadcast(INDEX_NONE);
}

void UWLBuildComponent::DestroyGhost()
{
	if (Ghost)
	{
		Ghost->DestroyComponent();
		Ghost = nullptr;
	}
}

void UWLBuildComponent::SetGhostTint(bool bOk)
{
	if (Ghost)
	{
		FWLPlaceholderMesh::SetColor(Ghost, bOk ? FLinearColor(0.35f, 1.f, 0.45f) : FLinearColor(1.f, 0.35f, 0.35f));
	}
}

void UWLBuildComponent::UpdateGhost(const FVector& GroundPoint)
{
	const UWLBuildCatalog* Catalog = UWLBuildCatalog::Get();
	if (!IsPlacing() || !Ghost || !Catalog || !Catalog->IsValidIndex(PlacingIndex))
	{
		return;
	}
	const FWLBuildable& B = Catalog->Buildables[PlacingIndex];
	GhostGround = GroundPoint;
	bHaveGhostGround = true;
	// Basic shapes are centred on their origin: lift by half the height so the piece sits on the ground.
	Ghost->SetWorldLocationAndRotation(GroundPoint + FVector(0.f, 0.f, B.Size.Z * 0.5f), FRotator(0.f, GhostYaw, 0.f));
	Ghost->SetVisibility(true);
	SetGhostTint(HasLevel(B) && CanAfford(B));
}

void UWLBuildComponent::RotateGhost(float DeltaYaw)
{
	GhostYaw = FWLBuildRules::NormalizeYaw(GhostYaw + DeltaYaw);
	if (bHaveGhostGround)
	{
		UpdateGhost(GhostGround);
	}
}

void UWLBuildComponent::ConfirmPlacement()
{
	const UWLBuildCatalog* Catalog = UWLBuildCatalog::Get();
	if (!IsPlacing() || !bHaveGhostGround || !Catalog || !Catalog->IsValidIndex(PlacingIndex))
	{
		return;
	}
	const FWLBuildable& B = Catalog->Buildables[PlacingIndex];
	if (!HasLevel(B) || !CanAfford(B))
	{
		Msg(WhyCantBuild(B));
		return;
	}
	ServerPlace(PlacingIndex, GhostGround, GhostYaw);
	// Unity: keep placing more of the same while you can still afford it, otherwise drop out. The
	// inventory change is not replicated yet, so the follow-up check runs after the server answers
	// (ghost tint updates every UpdateGhost); the server tells the client when it is out of materials.
}

// --- server -------------------------------------------------------------------------------------------

bool UWLBuildComponent::ServerPlace_Validate(int32 BuildableIndex, FVector_NetQuantize Location, float Yaw)
{
	return BuildableIndex >= 0 && !FVector(Location).ContainsNaN() && FMath::IsFinite(Yaw);
}

void UWLBuildComponent::ServerPlace_Implementation(int32 BuildableIndex, FVector_NetQuantize Location, float Yaw)
{
	AWLPlayerCharacter* Char = GetCharacter();
	AWLPlayerState* PS = GetPlayerState();
	UWorld* World = GetWorld();
	const UWLBuildCatalog* Catalog = UWLBuildCatalog::Get();
	if (!Char || !PS || !World || !Catalog || !Catalog->IsValidIndex(BuildableIndex))
	{
		return;
	}
	if (const UWLAbilitySystemComponent* ASC = PS->GetWLAbilitySystem(); !ASC || ASC->IsDead())
	{
		return;
	}
	const double Now = World->GetTimeSeconds();
	if (Now - LastPlaceTime < PlaceCooldown)
	{
		return;
	}
	LastPlaceTime = Now;

	const FWLBuildable& B = Catalog->Buildables[BuildableIndex];
	UWLInventoryComponent* Inv = PS->GetInventory();
	UWLSkillsComponent* Skills = PS->GetSkills();
	if (!Inv || !Skills)
	{
		return;
	}

	if (!FWLBuildRules::MeetsLevel(Skills->GetLevel(HearthcraftSkill), B))
	{
		Msg(FText::Format(LOCTEXT("NeedLevel2", "You need Hearthcraft level {0} to build that."), FText::AsNumber(B.HearthLevel)));
		return;
	}
	if (!FWLBuildRules::CanAfford([Inv](int32 Id) { return Inv->CountOf(Id); }, B))
	{
		Msg(LOCTEXT("NeedMats2", "You don't have the materials for that."));
		return;
	}

	if (UWLPlacedPieceComponent::CountOwnedBy(World, PS) >= MaxPiecesPerPlayer)
	{
		Msg(LOCTEXT("Cap", "You have built as much as you can keep up."));
		return;
	}

	// Snap to the real ground under the requested point: the client's Z is never trusted.
	const FVector Requested = Location;
	FHitResult Hit;
	FCollisionQueryParams Params(SCENE_QUERY_STAT(WLBuildGround), false, Char);
	if (!World->LineTraceSingleByChannel(Hit, Requested + FVector(0.f, 0.f, 200.f), Requested - FVector(0.f, 0.f, 1000.f), ECC_Visibility, Params))
	{
		Msg(LOCTEXT("NoGround", "You can't build there."));
		return;
	}
	const UWLWorldSettings* Settings = UWLWorldSettings::Get();
	const float MaxDist = Settings ? Settings->MaxPlaceDistance : 1500.f;
	if (!FWLBuildRules::IsPlacementReachable(Char->GetActorLocation(), Hit.ImpactPoint, MaxDist))
	{
		Msg(LOCTEXT("TooFar", "That's too far away to build."));
		return;
	}

	// Spawn first so a failed spawn costs nothing, then consume (Unity consumed first; same result).
	AActor* Piece = SpawnDeployable(BuildableIndex, B, Hit.ImpactPoint, FWLBuildRules::NormalizeYaw(Yaw), PS);
	if (!Piece)
	{
		Msg(LOCTEXT("SpawnFail", "That can't be built here."));
		return;
	}
	for (const FWLBuildCost& C : B.Cost)
	{
		Inv->RemoveItem(C.ItemId, C.Quantity);
	}
	Skills->AddXP(HearthcraftSkill, B.XP);
	Msg(FText::Format(LOCTEXT("Built", "You build a {0}. ({1} Hearthcraft XP)"), B.Name, FText::AsNumber(B.XP)));

	if (!FWLBuildRules::CanAfford([Inv](int32 Id) { return Inv->CountOf(Id); }, B))
	{
		Msg(LOCTEXT("OutOfMats", "Out of materials for another one."));
		ClientEndPlacement();
	}
}

AActor* UWLBuildComponent::SpawnDeployable(int32 BuildableIndex, const FWLBuildable& B, const FVector& Ground, float Yaw, AWLPlayerState* Builder)
{
	UWorld* World = GetWorld();
	if (!World)
	{
		return nullptr;
	}

	UClass* Class = B.OverrideClass.Get();
	if (!Class)
	{
		switch (B.Kind)
		{
		case EWLBuildKind::CookingFire:
		case EWLBuildKind::Furnace:
		case EWLBuildKind::Workbench: Class = AWLCraftingStation::StaticClass(); break;
		case EWLBuildKind::BankChest: Class = AWLBankingCrate::StaticClass(); break;
		case EWLBuildKind::AFKStation: Class = AWLAFKStation::StaticClass(); break;
		case EWLBuildKind::Turret: Class = AWLTurret::StaticClass(); break;
		case EWLBuildKind::HousingPlot: Class = AWLHousingPlot::StaticClass(); break;
		}
	}
	if (!Class)
	{
		return nullptr;
	}

	const FTransform Xf(FRotator(0.f, Yaw, 0.f), Ground);
	AActor* Actor = World->SpawnActorDeferred<AActor>(Class, Xf, nullptr, nullptr, ESpawnActorCollisionHandlingMethod::AlwaysSpawn);
	if (!Actor)
	{
		return nullptr;
	}

	// Placeholder look for the interactable-actor based kinds (before FinishSpawning runs OnConstruction).
	if (AWLInteractableActor* IA = Cast<AWLInteractableActor>(Actor))
	{
		if (B.Kind != EWLBuildKind::CookingFire && B.Kind != EWLBuildKind::Furnace && B.Kind != EWLBuildKind::Workbench)
		{
			IA->PlaceholderSize = B.Size;
			IA->PlaceholderColor = B.Color;
		}
		else
		{
			IA->PlaceholderColor = B.Color;
		}
		IA->DisplayName = B.Name;
	}

	switch (B.Kind)
	{
	case EWLBuildKind::CookingFire:
	case EWLBuildKind::Furnace:
	case EWLBuildKind::Workbench:
		if (AWLCraftingStation* St = Cast<AWLCraftingStation>(Actor))
		{
			St->SetStation(B.Kind == EWLBuildKind::Furnace ? EWLCraftingStation::Furnace
				: B.Kind == EWLBuildKind::Workbench ? EWLCraftingStation::Workbench : EWLCraftingStation::CookingFire);
		}
		break;
	case EWLBuildKind::AFKStation:
		if (AWLAFKStation* Afk = Cast<AWLAFKStation>(Actor))
		{
			Afk->Configure(B.Name, B.AFKSkill, B.AFKProduceItemId, B.AFKConsumeItemId);
		}
		break;
	case EWLBuildKind::Turret:
		if (AWLTurret* Tu = Cast<AWLTurret>(Actor))
		{
			Tu->Configure(B.Name, B.TurretMaxHit, B.TurretRange);
		}
		break;
	default:
		break;
	}

	Actor->FinishSpawning(Xf);

	UWLPlacedPieceComponent* Marker = NewObject<UWLPlacedPieceComponent>(Actor, TEXT("PlacedPiece"));
	Marker->SetBuilder(Builder);
	Marker->BuildableIndex = BuildableIndex;
	Marker->RegisterComponent();

	if (B.Kind == EWLBuildKind::HousingPlot)
	{
		AWLHousingPlot::MarkBuilder(Builder);
	}
	return Actor;
}

#undef LOCTEXT_NAMESPACE
