#include "World/WLWorldSettings.h"

namespace
{
	FWLCritterSpecies MakeSpecies(const TCHAR* Name, EWLPlaceholderShape Shape, const FLinearColor& Color, const FVector& Size,
		float Walk, float Flee, float ScaleMin, float ScaleMax, bool bInPlace, bool bFlier)
	{
		FWLCritterSpecies S;
		S.Name = Name;
		S.PlaceholderShape = Shape;
		S.PlaceholderColor = Color;
		S.BodySize = Size;
		S.WalkSpeed = Walk;
		S.FleeSpeed = Flee;
		S.ScaleMin = ScaleMin;
		S.ScaleMax = ScaleMax;
		S.bMoveInPlace = bInPlace;
		S.bFlier = bFlier;
		return S;
	}
}

UWLWorldSettings::UWLWorldSettings()
{
	SectionName = TEXT("WastelandersWorld");

	// Unity CritterLife.Table: speeds in m/s -> cm/s and move-in-place / flier flags exactly as Unity.
	// Unity's per-model scale factors corrected vendor model sizes; here BodySize is the real-world
	// size and ScaleMin/Max keep Unity's relative variation. The wolf stays benched as in Unity.
	CritterSpecies = {
		MakeSpecies(TEXT("Cottontail"), EWLPlaceholderShape::Sphere, FLinearColor(0.55f, 0.48f, 0.4f), FVector(40.f, 22.f, 25.f), 110.f, 220.f, 1.0f, 1.4f, false, false),
		MakeSpecies(TEXT("Rhinoceros"), EWLPlaceholderShape::Cube, FLinearColor(0.4f, 0.4f, 0.42f), FVector(320.f, 140.f, 160.f), 100.f, 250.f, 0.9f, 1.15f, true, false),
		MakeSpecies(TEXT("Feral Stray"), EWLPlaceholderShape::Cube, FLinearColor(0.45f, 0.35f, 0.25f), FVector(100.f, 30.f, 70.f), 140.f, 600.f, 0.9f, 1.05f, true, false),
		MakeSpecies(TEXT("Wild Boar"), EWLPlaceholderShape::Cube, FLinearColor(0.3f, 0.22f, 0.18f), FVector(140.f, 60.f, 80.f), 220.f, 320.f, 1.0f, 1.3f, false, false),
		MakeSpecies(TEXT("Bald Eagle"), EWLPlaceholderShape::Cone, FLinearColor(0.35f, 0.25f, 0.15f), FVector(80.f, 80.f, 40.f), 80.f, 700.f, 0.9f, 1.15f, false, true),
		MakeSpecies(TEXT("Raccoon"), EWLPlaceholderShape::Sphere, FLinearColor(0.35f, 0.35f, 0.35f), FVector(60.f, 28.f, 30.f), 100.f, 300.f, 0.9f, 1.15f, true, false),
		MakeSpecies(TEXT("Tree Frog"), EWLPlaceholderShape::Sphere, FLinearColor(0.3f, 0.6f, 0.25f), FVector(20.f, 15.f, 12.f), 60.f, 200.f, 1.0f, 1.5f, true, false),
	};
}
