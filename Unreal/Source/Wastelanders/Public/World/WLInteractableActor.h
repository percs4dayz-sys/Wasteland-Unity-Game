#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Interaction/WLInteractable.h"
#include "World/WLPlaceholderMesh.h"
#include "WLInteractableActor.generated.h"

class UStaticMeshComponent;
class UStaticMesh;
class AWLPlayerController;

/**
 * Base for clickable world objects (stations, nodes, AFK stations, placed pieces): a replicated
 * actor with a display name, examine text, reach, and a visual that is either new art or an engine
 * basic shape. Merges Unity's IExaminable with the per-object boilerplate each Unity component
 * repeated (DisplayName / ExamineText switch tables).
 */
UCLASS(Abstract)
class WASTELANDERS_API AWLInteractableActor : public AActor, public IWLInteractable
{
	GENERATED_BODY()

public:
	AWLInteractableActor();

	virtual void OnConstruction(const FTransform& Transform) override;

	// IWLInteractable
	virtual FText GetInteractName_Implementation() const override;
	virtual FText GetExamineText_Implementation() const override;
	virtual TArray<FWLInteractOption> GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const override;
	virtual float GetInteractRange_Implementation() const override { return InteractRange; }
	virtual bool Interact_Implementation(AWLPlayerCharacter* Player, FName Verb) override { return false; }

	UStaticMeshComponent* GetMesh() const { return Mesh; }

	/** Re-apply art or placeholder (after a runtime config change such as a build piece's size). */
	void RefreshVisual();

	/** SERVER helper: chat line to one player. */
	static void SendMessage(const AWLPlayerCharacter* Player, const FText& Message);

	static AWLPlayerController* GetPlayerController(const AWLPlayerCharacter* Player);

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Replicated, Category = "Wastelanders|Interaction")
	FText DisplayName;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Wastelanders|Interaction", meta = (MultiLine = true))
	FText ExamineText;

	/** Reach in uu measured to the near side of the collision (Unity use reach 2 m). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Wastelanders|Interaction")
	float InteractRange = 200.f;

	/** New-art mesh. Unset = PlaceholderShape. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Wastelanders|Visual")
	TSoftObjectPtr<UStaticMesh> ArtMesh;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Wastelanders|Visual")
	EWLPlaceholderShape PlaceholderShape = EWLPlaceholderShape::Cube;

	/** Placeholder size in cm; the shape stands on the actor origin. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Wastelanders|Visual")
	FVector PlaceholderSize = FVector(120.f, 120.f, 120.f);

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Wastelanders|Visual")
	FLinearColor PlaceholderColor = FLinearColor(0.5f, 0.5f, 0.55f);

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

protected:
	/** The default verb when a subclass doesn't override GetInteractOptions. */
	virtual FName GetDefaultVerb() const { return TEXT("Use"); }
	virtual FText GetDefaultVerbLabel() const;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<USceneComponent> Root;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Components")
	TObjectPtr<UStaticMeshComponent> Mesh;
};
