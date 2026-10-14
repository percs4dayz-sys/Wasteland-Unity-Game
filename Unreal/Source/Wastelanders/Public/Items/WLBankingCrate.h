#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Interaction/WLInteractable.h"
#include "WLBankingCrate.generated.h"

class UStaticMeshComponent;
class UTexture2D;
class AWLPlayerCharacter;

/**
 * A bank booth. Ported from Assets/Scripts/World/BankingCrate.cs: "Bank" opens the player's
 * bank session (UWLBankComponent::OpenBank) on the server. Also carries the data for Roxy's town
 * bank guide (town name, directions, order).
 *
 * Art: a scaled engine cube until a new-art mesh is assigned in a Blueprint child.
 */
UCLASS()
class WASTELANDERS_API AWLBankingCrate : public AActor, public IWLInteractable
{
	GENERATED_BODY()

public:
	AWLBankingCrate();

	virtual FText GetInteractName_Implementation() const override;
	virtual FText GetExamineText_Implementation() const override;
	virtual TArray<FWLInteractOption> GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const override;
	virtual float GetInteractRange_Implementation() const override;
	virtual bool Interact_Implementation(AWLPlayerCharacter* Player, FName Verb) override;

	static const FName VerbBank;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Bank")
	FText Label;

	/** Roxy's town bank guide. Empty town name = not listed in the guide. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Bank Guide")
	FText TownName;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Bank Guide", meta = (MultiLine = true))
	FText Directions;

	/** New-art guide picture (Unity guideImage is not ported). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Bank Guide")
	TSoftObjectPtr<UTexture2D> GuideImage;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Bank Guide")
	int32 GuideOrder = 0;

	UPROPERTY(EditAnywhere, Category = "Bank")
	float InteractRange = 250.f;

protected:
	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UStaticMeshComponent> Mesh;
};
