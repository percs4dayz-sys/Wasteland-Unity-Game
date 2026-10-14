#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Core/WLTypes.h"
#include "Templates/Function.h"
#include "WLTutorialPortal.generated.h"

class UBoxComponent;
class UStaticMeshComponent;
class UStaticMesh;
class AWLPlayerState;

/**
 * The tutorial island's exit portal. Ported from World/Portal3D.cs. Walk into it (server):
 *  - tutorial skills not all at level 2 -> pushed back with the list of what's missing;
 *  - ready but no companion chosen ("egg_chosen" / "pup_hatched") -> Old Mara stops you and the
 *    client's UI opens the egg choice (UWLInteractorComponent::ClientWorldEvent "EggChoice");
 *  - otherwise -> on to the main world: a teleport within this map, or travel to the main-world
 *    shard (UWLWorldSettings::MainWorldDestination) when bTravelToOtherMap.
 */
UCLASS()
class WASTELANDERS_API AWLTutorialPortal : public AActor
{
	GENERATED_BODY()

public:
	AWLTutorialPortal();

	virtual void BeginPlay() override;
	virtual void OnConstruction(const FTransform& Transform) override;

	/** Skills that must reach level 2. Unity: every skill except retired melee, Beastmastery, Fission, Refinement. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Portal")
	TArray<EWLSkill> TutorialSkills;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Portal")
	int32 RequiredLevel = 2;

	/** True: travel to the main-world shard. False: teleport to DestinationOffset in this map. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Portal")
	bool bTravelToOtherMap = true;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Portal", meta = (MakeEditWidget = true, EditCondition = "!bTravelToOtherMap"))
	FVector DestinationOffset = FVector(0.f, 0.f, 100.f);

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Portal")
	float RetriggerCooldown = 2.f;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Visual")
	TSoftObjectPtr<UStaticMesh> ArtMesh;

	/** Pure: names of skills below RequiredLevel (unit-testable via the level lookup). */
	static TArray<EWLSkill> IncompleteSkills(TConstArrayView<EWLSkill> Skills, int32 Required, TFunctionRef<int32(EWLSkill)> LevelOf);

protected:
	UFUNCTION()
	void OnTriggerBegin(UPrimitiveComponent* OverlappedComp, AActor* OtherActor, UPrimitiveComponent* OtherComp, int32 OtherBodyIndex, bool bFromSweep, const FHitResult& SweepResult);

	void TryEnter(class AWLPlayerCharacter* Char);

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UBoxComponent> Trigger;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UStaticMeshComponent> Visual;

	TMap<TWeakObjectPtr<AWLPlayerState>, double> NextUseTime;
};
