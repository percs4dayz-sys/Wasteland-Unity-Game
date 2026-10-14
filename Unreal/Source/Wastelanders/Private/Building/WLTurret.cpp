#include "Building/WLTurret.h"
#include "Combat/WLEnemyCharacter.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Combat/WLCombatFeedbackSubsystem.h"
#include "EngineUtils.h"
#include "Net/UnrealNetwork.h"

#define LOCTEXT_NAMESPACE "WLTurret"

AWLTurret::AWLTurret()
{
	DisplayName = LOCTEXT("Name", "Turret");
	PlaceholderShape = EWLPlaceholderShape::Cylinder;
	PlaceholderSize = FVector(80.f, 80.f, 160.f);
	PlaceholderColor = FLinearColor(0.3f, 0.34f, 0.4f);
}

void AWLTurret::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(AWLTurret, MaxHit);
	DOREPLIFETIME(AWLTurret, Range);
}

void AWLTurret::Configure(const FText& InName, int32 InMaxHit, float InRange)
{
	DisplayName = InName;
	MaxHit = FMath::Max(1, InMaxHit);
	Range = FMath::Max(0.f, InRange);
}

void AWLTurret::BeginPlay()
{
	Super::BeginPlay();
	if (HasAuthority())
	{
		if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
		{
			Tick->Register(TScriptInterface<IWLTickable>(this));
		}
	}
}

void AWLTurret::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UWLGameTickSubsystem* Tick = UWLGameTickSubsystem::Get(this))
	{
		Tick->Unregister(TScriptInterface<IWLTickable>(this));
	}
	Super::EndPlay(EndPlayReason);
}

FText AWLTurret::GetExamineText_Implementation() const
{
	return FText::Format(LOCTEXT("Examine", "{0} - auto-fires on hostiles within {1} m."),
		DisplayName, FText::AsNumber(FMath::RoundToInt(Range / 100.f)));
}

TArray<FWLInteractOption> AWLTurret::GetInteractOptions_Implementation(const AWLPlayerCharacter*) const
{
	return {}; // Examine only.
}

bool AWLTurret::AdvanceShotTimer(int32& Counter, int32 TicksPerShot)
{
	++Counter;
	if (Counter < FMath::Max(1, TicksPerShot))
	{
		return false;
	}
	Counter = 0;
	return true;
}

int32 AWLTurret::RollDamage(int32 MaxHit)
{
	return FMath::Max(1, FMath::RandRange(1, FMath::Max(1, MaxHit)));
}

AWLEnemyCharacter* AWLTurret::FindNearestTarget() const
{
	AWLEnemyCharacter* Best = nullptr;
	float BestSq = FMath::Square(Range);
	for (TActorIterator<AWLEnemyCharacter> It(GetWorld()); It; ++It)
	{
		AWLEnemyCharacter* E = *It;
		if (!E || E->IsDead() || E->IsDummy() || E->IsHidden())
		{
			continue;
		}
		const float D = FVector::DistSquared(E->GetActorLocation(), GetActorLocation());
		if (D < BestSq)
		{
			BestSq = D;
			Best = E;
		}
	}
	return Best;
}

void AWLTurret::OnGameTick(int64)
{
	if (!AdvanceShotTimer(TickCounter, TicksPerShot))
	{
		return;
	}
	AWLEnemyCharacter* Target = FindNearestTarget();
	UWLAbilitySystemComponent* ASC = Target ? Target->GetWLAbilitySystem() : nullptr;
	if (!ASC)
	{
		return;
	}

	// Face the target (cosmetic) and fire.
	FVector To = Target->GetActorLocation() - GetActorLocation();
	To.Z = 0.f;
	if (!To.IsNearlyZero(10.f))
	{
		SetActorRotation(To.Rotation());
	}

	const int32 Dealt = ASC->ApplyDamage(RollDamage(MaxHit), this, this);
	if (Dealt > 0)
	{
		FWLCombatFeedback::Hitsplat(Target, Dealt, true);
	}
}

#undef LOCTEXT_NAMESPACE
