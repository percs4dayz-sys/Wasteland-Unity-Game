#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "Interaction/WLInteractable.h"
#include "Dialogue/WLBuiltinDialogues.h"
#include "WLNPC.generated.h"

class UStaticMesh;
class UStaticMeshComponent;
class UTextRenderComponent;
class UWLDialogueAsset;

/**
 * A talkable townsperson (Unity ITalkableNPC: RoxyNPC, TutorialNPC, WandererNPC).
 *
 * "Talk" (the default click) opens its conversation through the player's UWLDialogueComponent;
 * talking again mid-conversation turns the page. The conversation comes from DialogueAsset if one is
 * set, otherwise from the code-built graph picked by BuiltinDialogue. The NPC turns (yaw only) to
 * face whoever talks to it, and shows its name overhead.
 *
 * Art: no Unity models. Placeholder engine cylinder + capsule; a Blueprint child adds the new mesh to
 * the inherited skeletal mesh slot (MeshYawOffset fixes a model authored facing the wrong way).
 * Unity's RoxyAutoSetup (finding a scene object named "roxy" and bolting on a collider and an idle
 * animator) is not needed: place an AWLRoxyNPC in the level.
 */
UCLASS()
class WASTELANDERS_API AWLNPC : public ACharacter, public IWLInteractable
{
	GENERATED_BODY()

public:
	AWLNPC(const FObjectInitializer& ObjectInitializer);

	virtual void BeginPlay() override;
	virtual void OnConstruction(const FTransform& Transform) override;

	// IWLInteractable
	virtual FText GetInteractName_Implementation() const override { return DisplayName; }
	virtual FText GetExamineText_Implementation() const override { return ExamineText; }
	virtual TArray<FWLInteractOption> GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const override;
	virtual float GetInteractRange_Implementation() const override { return InteractRange; }
	virtual bool Interact_Implementation(AWLPlayerCharacter* Player, FName Verb) override;

	/** The conversation in use (asset, or the built-in graph). Server. */
	const UWLDialogueAsset* GetDialogue() const;

	bool IsRoxy() const { return BuiltinDialogue == EWLBuiltinDialogue::Roxy; }

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "NPC")
	FText DisplayName;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "NPC", meta = (MultiLine = true))
	FText ExamineText;

	/** Authored conversation. Overrides BuiltinDialogue when set. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "NPC")
	TObjectPtr<UWLDialogueAsset> DialogueAsset;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "NPC")
	EWLBuiltinDialogue BuiltinDialogue = EWLBuiltinDialogue::Wanderer;

	/** Wanderer lines (one per visit, cycling). Empty = WandererNPC.cs defaults. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "NPC", meta = (MultiLine = true, EditCondition = "BuiltinDialogue == EWLBuiltinDialogue::Wanderer"))
	TArray<FText> WandererLines;

	UPROPERTY(EditAnywhere, Category = "NPC")
	float InteractRange = 250.f;

	/** Unity modelYawOffset: nudge if the new model faces the wrong way (often 180). */
	UPROPERTY(EditAnywhere, Category = "NPC")
	float MeshYawOffset = 0.f;

	/** Placeholder body colour until new art exists. */
	UPROPERTY(EditAnywhere, Category = "NPC|Placeholder")
	FLinearColor PlaceholderColor = FLinearColor(0.55f, 0.5f, 0.45f);

	/** New-art static body (optional; skeletal art goes on the inherited Mesh). */
	UPROPERTY(EditAnywhere, Category = "NPC|Placeholder")
	TSoftObjectPtr<UStaticMesh> BodyArt;

protected:
	void FacePlayer(const AActor* Player);

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UStaticMeshComponent> PlaceholderBody;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UTextRenderComponent> NameTag;

	UPROPERTY(Transient)
	TObjectPtr<UWLDialogueAsset> RuntimeDialogue;
};

/** Roxy, the mainland guide (NPC/RoxyNPC.cs). */
UCLASS()
class WASTELANDERS_API AWLRoxyNPC : public AWLNPC
{
	GENERATED_BODY()
public:
	AWLRoxyNPC(const FObjectInitializer& ObjectInitializer);
};

/** Old Mara, the tutorial survivor (NPC/TutorialNPC.cs). */
UCLASS()
class WASTELANDERS_API AWLTutorialNPC : public AWLNPC
{
	GENERATED_BODY()
public:
	AWLTutorialNPC(const FObjectInitializer& ObjectInitializer);
};

/** A townsperson with a few cycling lines (NPC/WandererNPC.cs). */
UCLASS()
class WASTELANDERS_API AWLWandererNPC : public AWLNPC
{
	GENERATED_BODY()
public:
	AWLWandererNPC(const FObjectInitializer& ObjectInitializer);
};
