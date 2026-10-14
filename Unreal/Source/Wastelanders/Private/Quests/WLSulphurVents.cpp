#include "Quests/WLSulphurVents.h"
#include "World/WLResourceNode.h"
#include "Combat/WLEnemyCharacter.h"
#include "EngineUtils.h"
#include "Engine/World.h"
#include "Engine/HitResult.h"
#include "TimerManager.h"
#include "CollisionQueryParams.h"

// Unity source: World/SulphurCompanions.cs

float FWLSulphurVentRules::FlatDistance(const FVector& A, const FVector& B)
{
	return FVector2D::Distance(FVector2D(A.X, A.Y), FVector2D(B.X, B.Y));
}

bool FWLSulphurVentRules::AnyWithin(const TArray<FVector>& Points, const FVector& P, float Range)
{
	for (const FVector& Q : Points)
	{
		if (FlatDistance(P, Q) < Range)
		{
			return true;
		}
	}
	return false;
}

TArray<FWLSulphurVentRules::FCluster> FWLSulphurVentRules::BuildClusters(const TArray<FVector>& Points)
{
	TArray<FCluster> Clusters;
	for (const FVector& P : Points)
	{
		int32 Home = INDEX_NONE;
		for (int32 i = Clusters.Num() - 1; i >= 0; --i)
		{
			if (!AnyWithin(Clusters[i].Members, P, ClusterLink))
			{
				continue;
			}
			if (Home == INDEX_NONE)
			{
				Home = i;
				continue;
			}
			// P bridges two clusters: merge the lower-index (Home is the higher index) into it.
			Clusters[Home].Members.Append(Clusters[i].Members);
			Clusters.RemoveAt(i);
			if (i < Home)
			{
				--Home;
			}
		}
		if (Home == INDEX_NONE)
		{
			Home = Clusters.Add(FCluster());
		}
		Clusters[Home].Members.Add(P);
	}
	for (FCluster& C : Clusters)
	{
		FVector Sum = FVector::ZeroVector;
		for (const FVector& M : C.Members)
		{
			Sum += M;
		}
		C.Center = Sum / FMath::Max(1, C.Members.Num());
		C.Count = C.Members.Num();
		for (const FVector& M : C.Members)
		{
			C.Radius = FMath::Max(C.Radius, FlatDistance(M, C.Center));
		}
	}
	return Clusters;
}

int32 FWLSulphurVentRules::Seed(const FVector& Center)
{
	const int32 X = FMath::RoundToInt(Center.X / 100.f);
	const int32 Y = FMath::RoundToInt(Center.Y / 100.f);
	return static_cast<int32>(static_cast<uint32>(X) * 73856093u ^ static_cast<uint32>(Y) * 19349663u);
}

FVector FWLSulphurVentRules::ProbePoint(const FCluster& Cluster, FRandomStream& Rng)
{
	const float Ang = Rng.FRand() * UE_TWO_PI;
	const float Dist = Cluster.Radius + 300.f + Rng.FRand() * 600.f;
	return Cluster.Center + FVector(FMath::Cos(Ang), FMath::Sin(Ang), 0.f) * Dist;
}

// ------------------------------------------------------------------ subsystem

bool UWLSulphurVentPlacer::ShouldCreateSubsystem(UObject* Outer) const
{
	const UWorld* World = Cast<UWorld>(Outer);
	return World && (World->IsGameWorld()) && World->GetNetMode() != NM_Client && Super::ShouldCreateSubsystem(Outer);
}

void UWLSulphurVentPlacer::OnWorldBeginPlay(UWorld& InWorld)
{
	Super::OnWorldBeginPlay(InWorld);
	const UWLSulphurVentSettings* Settings = GetDefault<UWLSulphurVentSettings>();
	if (!Settings || !Settings->bEnabled || Settings->VentClass.IsNull())
	{
		return;
	}
	InWorld.GetTimerManager().SetTimer(PopulateTimer, FTimerDelegate::CreateWeakLambda(this, [this]() { Populate(); }), FMath::Max(0.1f, Settings->Delay), false);
}

bool UWLSulphurVentPlacer::IsGroundHit(const FHitResult& Hit) const
{
	const UPrimitiveComponent* Comp = Hit.GetComponent();
	if (!Comp)
	{
		return false;
	}
	static const FName GroundTag(TEXT("Ground"));
	if (Comp->ComponentHasTag(GroundTag) || (Comp->GetOwner() && Comp->GetOwner()->ActorHasTag(GroundTag)))
	{
		return true;
	}
	for (const UClass* C = Comp->GetClass(); C; C = C->GetSuperClass())
	{
		if (C->GetName().StartsWith(TEXT("Landscape")))
		{
			return true;
		}
	}
	return false;
}

bool UWLSulphurVentPlacer::IsBlocked(const FVector& Ground) const
{
	TArray<FOverlapResult> Overlaps;
	FCollisionQueryParams Params(SCENE_QUERY_STAT(WLVentBlocked), false);
	GetWorld()->OverlapMultiByChannel(Overlaps, Ground + FVector(0.f, 0.f, 90.f), FQuat::Identity, ECC_Visibility,
		FCollisionShape::MakeSphere(80.f), Params);
	for (const FOverlapResult& O : Overlaps)
	{
		if (O.GetComponent() && !IsGroundHit(FHitResult(O.GetActor(), const_cast<UPrimitiveComponent*>(O.GetComponent()), Ground, FVector::UpVector)))
		{
			return true;
		}
	}
	return false;
}

bool UWLSulphurVentPlacer::FindSpot(const FWLSulphurVentRules::FCluster& Cluster, FRandomStream& Rng, const TArray<FVector>& Vents, FVector& OutPos) const
{
	UWorld* World = GetWorld();
	for (int32 Attempt = 0; Attempt < FWLSulphurVentRules::SpotAttempts; ++Attempt)
	{
		const FVector Probe = FWLSulphurVentRules::ProbePoint(Cluster, Rng);
		FHitResult Hit;
		FCollisionQueryParams Params(SCENE_QUERY_STAT(WLVentSpot), false);
		if (!World->LineTraceSingleByChannel(Hit, Probe + FVector(0.f, 0.f, 6000.f), Probe - FVector(0.f, 0.f, 10000.f), ECC_Visibility, Params))
		{
			continue;
		}
		if (!IsGroundHit(Hit) || Hit.ImpactNormal.Z < FWLSulphurVentRules::MinNormalZ)
		{
			continue;
		}
		if (FMath::Abs(Hit.ImpactPoint.Z - Cluster.Center.Z) > FWLSulphurVentRules::MaxHeightDiff)
		{
			continue;
		}
		if (IsBlocked(Hit.ImpactPoint) || FWLSulphurVentRules::AnyWithin(Vents, Hit.ImpactPoint, FWLSulphurVentRules::MinSpacing))
		{
			continue;
		}
		OutPos = Hit.ImpactPoint;
		return true;
	}
	return false;
}

int32 UWLSulphurVentPlacer::Populate()
{
	UWorld* World = GetWorld();
	if (!World || bPopulated || World->GetNetMode() == NM_Client)
	{
		return 0;
	}
	bPopulated = true;
	const UWLSulphurVentSettings* Settings = GetDefault<UWLSulphurVentSettings>();
	UClass* VentClass = Settings ? Settings->VentClass.LoadSynchronous() : nullptr;
	if (!VentClass)
	{
		return 0;
	}

	TArray<FVector> Mining, Vents;
	for (TActorIterator<AWLResourceNode> It(World); It; ++It)
	{
		const AWLResourceNode* Node = *It;
		if (Node->IsSulphurVent())
		{
			Vents.Add(Node->GetActorLocation());
			continue;
		}
		if (Node->GetRequiredToolId() != FWLSulphurVentRules::PickaxeId)
		{
			continue;
		}
		if (const AWLEnemyCharacter* Owner = Cast<AWLEnemyCharacter>(Node->GetOwner()))
		{
			if (Owner->IsFissionGolem())
			{
				continue;   // a golem corpse isn't mining
			}
		}
		Mining.Add(Node->GetActorLocation());
	}
	if (Mining.Num() == 0)
	{
		return 0;
	}

	int32 Placed = 0;
	for (const FWLSulphurVentRules::FCluster& C : FWLSulphurVentRules::BuildClusters(Mining))
	{
		if (FWLSulphurVentRules::AnyWithin(Vents, C.Center, C.Radius + FWLSulphurVentRules::CoveredRadius))
		{
			continue;
		}
		FRandomStream Rng(FWLSulphurVentRules::Seed(C.Center));
		const int32 Want = FWLSulphurVentRules::VentsWanted(C.Count);
		for (int32 i = 0; i < Want; ++i)
		{
			FVector Pos;
			if (!FindSpot(C, Rng, Vents, Pos))
			{
				continue;
			}
			const FRotator Yaw(0.f, Rng.FRand() * 360.f, 0.f);
			FActorSpawnParameters P;
			P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
			if (World->SpawnActor<AWLResourceNode>(VentClass, Pos, Yaw, P))
			{
				Vents.Add(Pos);
				++Placed;
			}
		}
	}
	UE_LOG(LogTemp, Log, TEXT("[SulphurVents] %d mining node(s): placed %d vent(s)."), Mining.Num(), Placed);
	return Placed;
}
