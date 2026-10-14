#include "World/WLWaterVolume.h"
#include "World/WLPlaceholderMesh.h"
#include "Components/BoxComponent.h"
#include "Components/StaticMeshComponent.h"
#include "NavModifierComponent.h"
#include "NavAreas/NavArea_Null.h"
#include "Engine/StaticMesh.h"

AWLWaterVolume::AWLWaterVolume()
{
	PrimaryActorTick.bCanEverTick = false;
	bReplicates = false; // static level geometry: both sides load it from the map

	Blocker = CreateDefaultSubobject<UBoxComponent>(TEXT("Blocker"));
	SetRootComponent(Blocker);
	Blocker->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
	Blocker->SetCollisionObjectType(ECC_WorldStatic);
	Blocker->SetCollisionResponseToAllChannels(ECR_Ignore);
	Blocker->SetCollisionResponseToChannel(ECC_Pawn, ECR_Block);
	Blocker->SetGenerateOverlapEvents(false);
	// Nav relevant as a DYNAMIC OBSTACLE: never rasterized as walkable geometry (no navmesh on the
	// water's top face), only used as a modifier area. The NavModifier below supplies its bounds with
	// NavArea_Null, which makes the hole.
	Blocker->SetCanEverAffectNavigation(true);
	Blocker->bDynamicObstacle = true;
	Blocker->ShapeColor = FColor(40, 120, 220);

	NavModifier = CreateDefaultSubobject<UNavModifierComponent>(TEXT("NavModifier"));
	NavModifier->SetAreaClass(UNavArea_Null::StaticClass());

	Surface = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Surface"));
	Surface->SetupAttachment(Blocker);
	Surface->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Surface->SetCanEverAffectNavigation(false);
	Surface->SetCastShadow(false);
}

void AWLWaterVolume::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);

	// The blocker reaches BlockAboveSurface over the water so a capsule can't step up onto it. The
	// surface sits BlockAboveSurface below the box top.
	const float Above = FMath::Max(0.f, BlockAboveSurface);
	Blocker->SetBoxExtent(FVector(Extent.X, Extent.Y, Extent.Z + Above * 0.5f));

	const FVector SurfaceCentre(0.f, 0.f, Extent.Z - Above * 0.5f);
	if (bShowPlaceholderSurface || !SurfaceArtMesh.IsNull())
	{
		Surface->SetVisibility(true);
		if (FWLPlaceholderMesh::Apply(Surface, SurfaceArtMesh, EWLPlaceholderShape::Plane, FVector(Extent.X * 2.f, Extent.Y * 2.f, 100.f),
				FLinearColor(0.12f, 0.3f, 0.45f)))
		{
			Surface->SetRelativeScale3D(FVector(Extent.X / 50.f, Extent.Y / 50.f, 1.f));
		}
		Surface->SetRelativeLocation(SurfaceCentre);
	}
	else
	{
		Surface->SetVisibility(false);
	}
}

float AWLWaterVolume::GetSurfaceZ() const
{
	return Surface->GetComponentLocation().Z;
}

bool AWLWaterVolume::IsOverWater(const FVector& WorldPoint) const
{
	const FVector Local = GetActorTransform().InverseTransformPosition(WorldPoint);
	return FMath::Abs(Local.X) <= Extent.X && FMath::Abs(Local.Y) <= Extent.Y;
}
