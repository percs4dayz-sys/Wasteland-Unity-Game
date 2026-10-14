#include "Quests/WLBlackwaterAtlas.h"
#include "Quests/WLQuestFlags.h"
#include "Engine/World.h"

#define LOCTEXT_NAMESPACE "WLBlackwater"

const TCHAR* FWLBlackwaterAtlas::MapName = TEXT("BlackwaterReach");

const TArray<FWLBlackwaterPlace>& FWLBlackwaterAtlas::Places()
{
	static const TArray<FWLBlackwaterPlace> P = []
	{
		TArray<FWLBlackwaterPlace> Out;
		auto Add = [&Out](const FText& N, const FText& A, float X, float Z, float H, float R)
		{
			FWLBlackwaterPlace& Place = Out.AddDefaulted_GetRef();
			Place.Name = N;
			Place.Activity = A;
			Place.Position = FVector2D(X, Z);
			Place.Height = H;
			Place.Radius = R;
		};
		Add(LOCTEXT("Hearthwick", "Hearthwick"), LOCTEXT("HearthwickA", "SAFE HAVEN / BANK / CRAFTING"), 360, 300, 64, 68);
		Add(LOCTEXT("Orchard", "Orchard Farm"), LOCTEXT("OrchardA", "BEGINNER WOODCUTTING"), 230, 165, 66, 40);
		Add(LOCTEXT("Mosswood", "Mosswood"), LOCTEXT("MosswoodA", "WOODCUTTING / FOREST TRAIL"), 170, 440, 78, 38);
		Add(LOCTEXT("Hollow", "Hollow Parish"), LOCTEXT("HollowA", "ABANDONED TOWN / LEVEL 10"), 330, 575, 87, 52);
		Add(LOCTEXT("Cinder", "Cinder Freight"), LOCTEXT("CinderA", "SALVAGE / LEVEL 10"), 580, 510, 68, 53);
		Add(LOCTEXT("Harbor", "Blackwater Harbor"), LOCTEXT("HarborA", "FISHING / BANK / SALVAGE"), 760, 325, 55, 52);
		Add(LOCTEXT("Relay", "Relay Nine"), LOCTEXT("RelayA", "MILITARY RIDGE / LEVEL 25"), 505, 780, 112, 53);
		Add(LOCTEXT("Cut", "The Cut"), LOCTEXT("CutA", "QUARRY / LEVEL 25"), 725, 700, 87, 44);
		Add(LOCTEXT("Southwatch", "Southwatch"), LOCTEXT("SouthwatchA", "ROAD CHECKPOINT / BEGINNER COMBAT"), 490, 135, 63, 35);
		return Out;
	}();
	return P;
}

const TArray<TArray<FVector2D>>& FWLBlackwaterAtlas::Roads()
{
	static const TArray<TArray<FVector2D>> R = []
	{
		auto Path = [](std::initializer_list<float> XY)
		{
			TArray<FVector2D> Pts;
			const float* It = XY.begin();
			for (size_t i = 0; i + 1 < XY.size(); i += 2)
			{
				Pts.Emplace(It[i], It[i + 1]);
			}
			return Pts;
		};
		TArray<TArray<FVector2D>> Out;
		Out.Add(Path({ 360,300, 318,268, 277,232, 230,165 }));
		Out.Add(Path({ 360,300, 291,332, 241,375, 170,440 }));
		Out.Add(Path({ 170,440, 222,483, 270,537, 330,575 }));
		Out.Add(Path({ 360,300, 367,388, 346,471, 330,575 }));
		Out.Add(Path({ 360,300, 453,321, 546,300, 644,309, 760,325 }));
		Out.Add(Path({ 546,300, 552,367, 580,438, 580,510 }));
		Out.Add(Path({ 330,575, 417,567, 502,539, 580,510 }));
		Out.Add(Path({ 330,575, 368,654, 442,713, 505,780 }));
		Out.Add(Path({ 505,780, 590,763, 650,727, 725,700 }));
		Out.Add(Path({ 580,510, 638,566, 696,615, 725,700 }));
		Out.Add(Path({ 760,325, 754,412, 704,463, 580,510 }));
		Out.Add(Path({ 360,300, 412,228, 454,188, 490,135 }));
		Out.Add(Path({ 230,165, 319,130, 398,116, 490,135 }));
		return Out;
	}();
	return R;
}

int32 FWLBlackwaterAtlas::NearestPlace(const FVector2D& PositionMetres)
{
	int32 Nearest = INDEX_NONE;
	float Best = RegionRadius;
	const TArray<FWLBlackwaterPlace>& P = Places();
	for (int32 i = 0; i < P.Num(); ++i)
	{
		const float D = FVector2D::Distance(PositionMetres, P[i].Position);
		if (D < Best)
		{
			Best = D;
			Nearest = i;
		}
	}
	return Nearest;
}

FText FWLBlackwaterAtlas::RegionTitle(int32 PlaceIndex)
{
	return Places().IsValidIndex(PlaceIndex) ? Places()[PlaceIndex].Name.ToUpper() : LOCTEXT("ReachTitle", "BLACKWATER REACH");
}

FText FWLBlackwaterAtlas::RegionSubtitle(int32 PlaceIndex)
{
	return Places().IsValidIndex(PlaceIndex)
		? FText::Format(LOCTEXT("PlaceSub", "{0}  /  F8: ATLAS"), Places()[PlaceIndex].Activity)
		: LOCTEXT("RoadSub", "THE OLD COAST ROAD  /  F8: ATLAS");
}

bool FWLBlackwaterAtlas::IsBlackwaterWorld(const UWorld* World)
{
	if (!World)
	{
		return false;
	}
	const FString Map = UWorld::RemovePIEPrefix(World->GetMapName());
	return Map.Equals(MapName, ESearchCase::IgnoreCase);
}

const TArray<int32>& FWLBlackwaterAtlas::ArrivalItems()
{
	static const TArray<int32> Items = { WLQuestItems::Pickaxe, WLQuestItems::Hatchet, WLQuestItems::FishingRod };
	return Items;
}

#undef LOCTEXT_NAMESPACE
