#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "WLCompanionPawn.generated.h"

class UStaticMeshComponent;
class UTextRenderComponent;
class AWLPlayerState;

/**
 * The beast's body in the world. It replicates to everyone (movement, species, growth stage, name),
 * while all decisions live in the owner's UWLCompanionComponent on the server. That component sets a
 * goal every game tick (heel at the owner's side, or stand within bite range of the target on the
 * side nearest the owner), and this pawn walks there with its AI controller over the navmesh.
 *
 * Art: Unity's wolf / tortoise / bird models are not ported. A placeholder engine sphere is scaled
 * by growth stage (Unity stageScale x 0.7 m base height); a Blueprint child or the species'
 * StageMeshes slots supply the new art. Flying lines hover their body above the capsule.
 */
UCLASS()
class WASTELANDERS_API AWLCompanionPawn : public ACharacter
{
	GENERATED_BODY()

public:
	AWLCompanionPawn(const FObjectInitializer& ObjectInitializer);

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void Tick(float DeltaSeconds) override;

	/** SERVER. Configure for a species / stage (also re-applied on clients via OnRep). */
	void SetAppearance(FName InSpecies, int32 InStage, const FText& InName, float InScale, float InHoverHeight);

	/** SERVER. Where to stand; FaceTarget non-null = turn toward it (fighting). */
	void SetGoal(const FVector& Goal, AActor* FaceTarget);

	/** SERVER. Snap next to the owner (summon, leash). */
	void TeleportNear(const FVector& Location);

	FName GetSpecies() const { return Species; }
	int32 GetStage() const { return Stage; }

	/** The owning player (for UI / nameplates). */
	UPROPERTY(Replicated, BlueprintReadOnly, Category = "Companion")
	TObjectPtr<AWLPlayerState> OwnerPlayerState;

	/** Unity ModelBaseHeight: a stage-1 beast is about 0.7 m tall; stage scale grows from here (uu). */
	UPROPERTY(EditAnywhere, Category = "Companion")
	float BaseHeight = 70.f;

protected:
	UFUNCTION()
	void OnRep_Appearance();

	void ApplyAppearance();

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UStaticMeshComponent> PlaceholderBody;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UTextRenderComponent> NameTag;

	UPROPERTY(ReplicatedUsing = OnRep_Appearance)
	FName Species;

	UPROPERTY(ReplicatedUsing = OnRep_Appearance)
	int32 Stage = 0;

	UPROPERTY(ReplicatedUsing = OnRep_Appearance)
	FText DisplayName;

	UPROPERTY(ReplicatedUsing = OnRep_Appearance)
	float BodyScale = 1.f;

	UPROPERTY(ReplicatedUsing = OnRep_Appearance)
	float HoverHeight = 0.f;

private:
	FVector Goal = FVector::ZeroVector;
	FVector LastMoveGoal = FVector(TNumericLimits<float>::Max());
	TWeakObjectPtr<AActor> FaceTarget;
	bool bHasGoal = false;
};
