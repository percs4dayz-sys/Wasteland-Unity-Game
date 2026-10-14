#include "World/WLPlaceholderMesh.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "Materials/MaterialInterface.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "UObject/UObjectGlobals.h"

UStaticMesh* FWLPlaceholderMesh::GetShape(EWLPlaceholderShape Shape)
{
	const TCHAR* Path = nullptr;
	switch (Shape)
	{
	case EWLPlaceholderShape::Cube:     Path = TEXT("/Engine/BasicShapes/Cube.Cube"); break;
	case EWLPlaceholderShape::Cylinder: Path = TEXT("/Engine/BasicShapes/Cylinder.Cylinder"); break;
	case EWLPlaceholderShape::Sphere:   Path = TEXT("/Engine/BasicShapes/Sphere.Sphere"); break;
	case EWLPlaceholderShape::Cone:     Path = TEXT("/Engine/BasicShapes/Cone.Cone"); break;
	case EWLPlaceholderShape::Plane:    Path = TEXT("/Engine/BasicShapes/Plane.Plane"); break;
	default: return nullptr;
	}
	// Engine content is always cooked and resident; LoadObject is cheap after the first call.
	return LoadObject<UStaticMesh>(nullptr, Path);
}

bool FWLPlaceholderMesh::Apply(UStaticMeshComponent* Component, const TSoftObjectPtr<UStaticMesh>& Art, EWLPlaceholderShape Shape,
	const FVector& SizeCm, const FLinearColor& Color)
{
	if (!Component)
	{
		return false;
	}
	if (!Art.IsNull())
	{
		if (UStaticMesh* Mesh = Art.LoadSynchronous())
		{
			Component->SetStaticMesh(Mesh);
			return false;
		}
	}
	UStaticMesh* ShapeMesh = GetShape(Shape);
	Component->SetStaticMesh(ShapeMesh);
	if (ShapeMesh)
	{
		Component->SetRelativeScale3D(ScaleForSize(SizeCm));
		SetColor(Component, Color);
	}
	return true;
}

void FWLPlaceholderMesh::SetColor(UStaticMeshComponent* Component, const FLinearColor& Color)
{
	if (!Component || Component->GetNumMaterials() == 0)
	{
		return;
	}
	UMaterialInstanceDynamic* MID = Cast<UMaterialInstanceDynamic>(Component->GetMaterial(0));
	if (!MID)
	{
		MID = Component->CreateDynamicMaterialInstance(0);
	}
	if (MID)
	{
		// BasicShapeMaterial exposes "Color"; harmless no-op on materials without it.
		MID->SetVectorParameterValue(TEXT("Color"), Color);
	}
}
