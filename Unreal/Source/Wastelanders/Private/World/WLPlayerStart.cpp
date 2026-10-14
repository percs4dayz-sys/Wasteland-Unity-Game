#include "World/WLPlayerStart.h"
#include "EngineUtils.h"
#include "Engine/World.h"

AWLPlayerStart::AWLPlayerStart(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
}

AWLPlayerStart* AWLPlayerStart::Choose(const UWorld* World, int32 PreferredTier, const FVector* NearLocation)
{
	if (!World)
	{
		return nullptr;
	}
	AWLPlayerStart* Default = nullptr;
	AWLPlayerStart* Any = nullptr;
	AWLPlayerStart* BestTier = nullptr;
	double BestDistSq = TNumericLimits<double>::Max();

	for (TActorIterator<AWLPlayerStart> It(World); It; ++It)
	{
		AWLPlayerStart* Start = *It;
		Any = Any ? Any : Start;
		if (Start->bDefaultSpawn && !Default)
		{
			Default = Start;
		}
		if (PreferredTier > 0 && Start->Tier == PreferredTier)
		{
			const double D = NearLocation ? FVector::DistSquared(*NearLocation, Start->GetActorLocation()) : 0.0;
			if (!BestTier || D < BestDistSq)
			{
				BestTier = Start;
				BestDistSq = D;
			}
		}
	}
	return BestTier ? BestTier : (Default ? Default : Any);
}
