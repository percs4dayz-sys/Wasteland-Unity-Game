#include "Combat/WLFissionGolemSpawner.h"
#include "Combat/WLFissionGolem.h"
#include "Items/WLDropTableAsset.h"
#include "Components/BillboardComponent.h"
#include "Components/CapsuleComponent.h"
#include "Engine/World.h"
#include "TimerManager.h"
#include "Wastelanders.h"

#define LOCTEXT_NAMESPACE "WLFissionGolemSpawner"

AWLFissionGolemSpawner::AWLFissionGolemSpawner()
{
	PrimaryActorTick.bCanEverTick = false;
	bReplicates = false;   // server-side bookkeeping; golems replicate themselves

	RootComponent = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
#if WITH_EDITORONLY_DATA
	Sprite = CreateEditorOnlyDefaultSubobject<UBillboardComponent>(TEXT("Sprite"));
	if (Sprite)
	{
		Sprite->SetupAttachment(RootComponent);
	}
#endif
	GolemClass = AWLFissionGolem::StaticClass();
}

FWLEnemyStats AWLFissionGolemSpawner::DefaultStats(int32 InTier)
{
	static const int32 HP[] = { 150, 260, 420, 650, 950 };
	static const int32 Atk[] = { 12, 20, 32, 46, 64 };
	static const int32 Def[] = { 10, 16, 24, 34, 48 };
	static const int32 Dmg[] = { 8, 12, 18, 26, 38 };
	// FissionGolemSpawner aggro (14/15/16/18/20 m). BCEarthGolemSpawner used 14 + tier-1; the
	// main-world table wins since it covered every tier.
	static const float Aggro[] = { 1400.f, 1500.f, 1600.f, 1800.f, 2000.f };
	const int32 I = FMath::Clamp(InTier, 1, 5) - 1;

	FWLEnemyStats S;
	S.MaxHP = HP[I];
	S.AttackLevel = Atk[I];
	S.DefenceLevel = Def[I];
	S.MaxHit = Dmg[I];
	S.bAggressive = true;
	S.AggroRange = Aggro[I];
	S.AttackRange = 180.f;
	S.MoveSpeed = 260.f + I * 20.f;        // 2.6 m/s + 0.2 per tier
	S.AttackCooldown = 2.6f;
	S.WindupSeconds = 0.4f;
	S.WanderRadius = 150.f;
	return S;
}

FText AWLFissionGolemSpawner::GolemName(int32 InTier)
{
	static const TCHAR* Elements[] = { TEXT("Earth"), TEXT("Light"), TEXT("Fire"), TEXT("Ice"), TEXT("Shadow") };
	const int32 T = FMath::Clamp(InTier, 1, 5);
	return FText::Format(LOCTEXT("GolemName", "{0} Golem (T{1})"), FText::FromString(Elements[T - 1]), T);
}

void AWLFissionGolemSpawner::BeginPlay()
{
	Super::BeginPlay();
	if (!HasAuthority())
	{
		return;
	}
	Tier = FMath::Clamp(Tier, 1, 5);
	RespawnAt = GetWorld()->GetTimeSeconds() + 1.0;   // BC spawner: first golem a second after start
	GetWorldTimerManager().SetTimer(ScanTimer, this, &AWLFissionGolemSpawner::Scan, FMath::Max(0.25f, ScanInterval), true);
}

void AWLFissionGolemSpawner::Scan()
{
	const double Now = GetWorld()->GetTimeSeconds();
	if (AWLFissionGolem::AnyActiveOfTier(GetWorld(), Tier))
	{
		RespawnAt = 0.0;
		return;
	}
	if (RespawnAt <= 0.0)
	{
		RespawnAt = Now + RespawnDelay;
		return;
	}
	if (Now >= RespawnAt)
	{
		// On failure (no ground) retry on the next scan rather than waiting out another delay.
		if (SpawnGolem())
		{
			RespawnAt = 0.0;
		}
	}
}

bool AWLFissionGolemSpawner::FindGround(const FVector& Point, FVector& OutGround) const
{
	FHitResult Hit;
	FCollisionQueryParams Params(SCENE_QUERY_STAT(GolemSpawnGround), false, this);
	if (GetWorld()->LineTraceSingleByChannel(Hit, Point + FVector(0.f, 0.f, 1000.f), Point - FVector(0.f, 0.f, 5000.f), ECC_Visibility, Params))
	{
		OutGround = Hit.ImpactPoint;
		return true;
	}
	OutGround = Point;
	return false;
}

AWLFissionGolem* AWLFissionGolemSpawner::SpawnGolem()
{
	if (!HasAuthority())
	{
		return nullptr;
	}

	// Pick a point, never the same one twice in a row when there's a choice.
	FVector Point = GetActorLocation();
	const int32 N = SpawnPoints.Num();
	if (N > 0)
	{
		int32 I = FMath::RandRange(0, N - 1);
		if (N > 1 && I == LastPoint)
		{
			I = (I + 1 + FMath::RandRange(0, N - 2)) % N;
		}
		LastPoint = I;
		Point = GetActorTransform().TransformPosition(SpawnPoints[I]);
	}
	FVector Ground;
	if (!FindGround(Point, Ground) && N > 0)
	{
		UE_LOG(LogWastelanders, Warning, TEXT("%s: no ground under spawn point %s; spawning at the raw point"), *GetName(), *Point.ToString());
	}

	UClass* Class = GolemClass ? GolemClass.Get() : AWLFissionGolem::StaticClass();
	const AWLFissionGolem* CDO = Class->GetDefaultObject<AWLFissionGolem>();
	const float HalfHeight = CDO ? CDO->GetCapsuleComponent()->GetScaledCapsuleHalfHeight() : 225.f;
	const FTransform Where(FRotator(0.f, FMath::FRandRange(0.f, 360.f), 0.f), Ground + FVector(0.f, 0.f, HalfHeight + 5.f));

	AWLFissionGolem* Golem = GetWorld()->SpawnActorDeferred<AWLFissionGolem>(Class, Where, this, nullptr,
		ESpawnActorCollisionHandlingMethod::AdjustIfPossibleButAlwaysSpawn);
	if (!Golem)
	{
		return nullptr;
	}

	// Configure before BeginPlay so HP seeds from the tier stats (Unity re-Reset() after AddComponent).
	Golem->ApplyStats(bOverrideStats ? OverrideStats : DefaultStats(Tier));
	Golem->Tier = Tier;
	Golem->bIsBoss = Tier == 5;
	Golem->bIsMiniBoss = Tier < 5;   // a farmable mini-boss, never a progression boss
	Golem->DisplayName = GolemName(Tier);
	Golem->DropTable = DropTable;
	Golem->MinYield = FMath::Max(1, MinYield);
	Golem->MaxYield = FMath::Max(Golem->MinYield, MaxYield);
	Golem->HarvestLevelRequired = HarvestLevelRequired;
	Golem->XPPerCore = XPPerCore;
	Golem->DeathMode = EWLEnemyDeathMode::Custom;
	Golem->FinishSpawning(Where);
	Golem->SetHome(Golem->GetActorLocation());

	UE_LOG(LogWastelanders, Log, TEXT("%s spawned %s at %s"), *GetName(), *GolemName(Tier).ToString(), *Golem->GetActorLocation().ToString());
	return Golem;
}

#undef LOCTEXT_NAMESPACE
