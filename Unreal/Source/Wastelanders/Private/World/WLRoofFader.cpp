#include "World/WLRoofFader.h"
#include "World/WLOccluderSubsystem.h"
#include "Components/BoxComponent.h"
#include "Components/PrimitiveComponent.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "GameFramework/Pawn.h"

AWLRoofFader::AWLRoofFader()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.bStartWithTickEnabled = false; // ticks only while fading
	bReplicates = false;

	Interior = CreateDefaultSubobject<UBoxComponent>(TEXT("Interior"));
	SetRootComponent(Interior);
	Interior->SetBoxExtent(FVector(400.f, 400.f, 200.f));
	Interior->SetCollisionProfileName(UCollisionProfile::NoCollision_ProfileName);
	Interior->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
	Interior->SetCollisionObjectType(ECC_WorldDynamic);
	Interior->SetCollisionResponseToAllChannels(ECR_Ignore);
	Interior->SetCollisionResponseToChannel(ECC_Pawn, ECR_Overlap);
	Interior->SetGenerateOverlapEvents(true);
	Interior->SetCanEverAffectNavigation(false);
	Interior->ShapeColor = FColor(77, 204, 255);
}

void AWLRoofFader::BeginPlay()
{
	Super::BeginPlay();
	if (GetNetMode() == NM_DedicatedServer)
	{
		return; // pure presentation
	}
	Collect();
	Interior->OnComponentBeginOverlap.AddDynamic(this, &AWLRoofFader::HandleBeginOverlap);
	Interior->OnComponentEndOverlap.AddDynamic(this, &AWLRoofFader::HandleEndOverlap);
}

void AWLRoofFader::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	// Never leave a roof hidden because the building streamed out mid-visit.
	Inside = 0;
	bHidden = false;
	Fade = 1.f;
	SetRoofVisible(true);
	Super::EndPlay(EndPlayReason);
}

void AWLRoofFader::Collect()
{
	Primitives.Reset();
	TArray<AActor*> Actors;
	for (AActor* A : Roof) { if (A) { Actors.AddUnique(A); } }
	for (AActor* A : AlsoHide) { if (A) { Actors.AddUnique(A); } }

	TArray<AActor*> WithChildren = Actors;
	for (AActor* A : Actors)
	{
		TArray<AActor*> Attached;
		A->GetAttachedActors(Attached, false, true);
		for (AActor* Child : Attached) { WithChildren.AddUnique(Child); }
	}

	for (AActor* A : WithChildren)
	{
		if (A == this)
		{
			continue; // our own interior volume must keep detecting the exit
		}
		TInlineComponentArray<UPrimitiveComponent*> Prims(A);
		for (UPrimitiveComponent* P : Prims)
		{
			Primitives.Add(P);
		}
	}
	if (Primitives.Num() == 0)
	{
		UE_LOG(LogTemp, Warning, TEXT("[RoofFader] %s has no roof primitives assigned; nothing to hide."), *GetName());
	}
}

bool AWLRoofFader::IsLocalPlayer(const AActor* Actor)
{
	const APawn* Pawn = Cast<APawn>(Actor);
	return Pawn && Pawn->IsLocallyControlled() && Pawn->IsPlayerControlled();
}

void AWLRoofFader::HandleBeginOverlap(UPrimitiveComponent*, AActor* OtherActor, UPrimitiveComponent*, int32, bool, const FHitResult&)
{
	if (IsLocalPlayer(OtherActor))
	{
		++Inside;
		Apply();
	}
}

void AWLRoofFader::HandleEndOverlap(UPrimitiveComponent*, AActor* OtherActor, UPrimitiveComponent*, int32)
{
	if (IsLocalPlayer(OtherActor))
	{
		Inside = FMath::Max(0, Inside - 1);
		Apply();
	}
}

void AWLRoofFader::Apply()
{
	const bool bWantHidden = Inside > 0;
	if (bWantHidden == bHidden)
	{
		return;
	}
	bHidden = bWantHidden;
	if (FadeDuration <= 0.f)
	{
		SetRoofVisible(!bHidden);
	}
	else
	{
		SetRoofVisible(true); // fade from visible
		SetActorTickEnabled(true);
	}
}

void AWLRoofFader::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	const float Target = bHidden ? 0.f : 1.f;
	Fade = FMath::FInterpConstantTo(Fade, Target, DeltaSeconds, 1.f / FMath::Max(0.01f, FadeDuration));
	SetFade(Fade);
	SetRoofVisible(Fade > 0.01f);
	if (FMath::IsNearlyEqual(Fade, Target))
	{
		SetActorTickEnabled(false);
	}
}

void AWLRoofFader::SetRoofVisible(bool bVisible)
{
	if (bVisible == bVisibleNow)
	{
		return;
	}
	bVisibleNow = bVisible;
	for (const TWeakObjectPtr<UPrimitiveComponent>& Weak : Primitives)
	{
		UPrimitiveComponent* P = Weak.Get();
		if (!P)
		{
			continue;
		}
		P->SetVisibility(bVisible);
		// Hidden primitives cast no shadow unless bCastHiddenShadow; keep the shadow if asked to.
		P->SetCastHiddenShadow(!bSuppressShadows);
		if (bVisible)
		{
			FWLClickThrough::Pop(P);
		}
		else
		{
			FWLClickThrough::Push(P);
		}
	}
}

void AWLRoofFader::SetFade(float Alpha)
{
	for (const TWeakObjectPtr<UPrimitiveComponent>& Weak : Primitives)
	{
		UPrimitiveComponent* P = Weak.Get();
		if (!P)
		{
			continue;
		}
		for (int32 i = 0; i < P->GetNumMaterials(); ++i)
		{
			if (UMaterialInstanceDynamic* MID = P->CreateDynamicMaterialInstance(i))
			{
				MID->SetScalarParameterValue(FadeParameterName, Alpha);
			}
		}
	}
}
