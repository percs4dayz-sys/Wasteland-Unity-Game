#pragma once

#include "CoreMinimal.h"
#include "World/WLInteractableActor.h"
#include "Core/WLGameTickSubsystem.h"
#include "Core/WLTypes.h"
#include "WLAFKStation.generated.h"

class AWLPlayerState;

UENUM(BlueprintType)
enum class EWLAFKMode : uint8
{
	Off,
	Resource, // passively produce a raw material (+ a little XP)
	Process   // consume a material for a bigger chunk of XP
};

/**
 * A Hearthcraft AFK station (usually placed by the build system). Ported from World/AFKStation.cs.
 * Clicking cycles Off -> Resource -> Process -> Off, skipping modes the station can't do; a fresh
 * station is Off so it never surprises anyone by filling their pack.
 *
 * MMO change: Unity fed the single local player. Here each player who sets the station has their
 * own mode and tick counter (server map keyed by PlayerState), so a shared world station serves
 * everyone independently; a placed station only accepts its owner (see UWLPlacedPieceComponent).
 */
UCLASS()
class WASTELANDERS_API AWLAFKStation : public AWLInteractableActor, public IWLTickable
{
	GENERATED_BODY()

public:
	AWLAFKStation();

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	virtual void OnGameTick(int64 TickCount) override;

	virtual FText GetExamineText_Implementation() const override;
	virtual bool Interact_Implementation(AWLPlayerCharacter* Player, FName Verb) override;

	/** Next mode after Current for a station with these capabilities. Pure; unit tested. */
	static EWLAFKMode NextMode(EWLAFKMode Current, bool bCanProduce, bool bCanProcess);

	/** SERVER (build system): configure before FinishSpawning. */
	void Configure(const FText& InName, EWLSkill InSkill, int32 InProduceItemId, int32 InConsumeItemId);

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|AFK")
	EWLSkill Skill = EWLSkill::Scrapping;

	/** Resource-mode output (0 = none). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|AFK")
	int32 ProduceItemId = 0;

	/** Process-mode input (0 = none). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|AFK")
	int32 ConsumeItemId = 0;

	/** 5 ticks = 3 s. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|AFK", meta = (ClampMin = "1"))
	int32 TicksPerAction = 5;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|AFK")
	int32 ResourceXP = 4;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|AFK")
	int32 ProcessXP = 20;

	/** 0 = works from anywhere (Unity). Otherwise users further than this (uu) are paused. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|AFK")
	float MaxUserDistance = 0.f;

protected:
	virtual FName GetDefaultVerb() const override { return TEXT("Set"); }
	virtual FText GetDefaultVerbLabel() const override;

	static FText ModeText(EWLAFKMode Mode);
	void Msg(AWLPlayerState* PS, const FText& Text) const;

	struct FUser
	{
		EWLAFKMode Mode = EWLAFKMode::Off;
		int32 Ticks = 0;
	};
	TMap<TWeakObjectPtr<AWLPlayerState>, FUser> Users;
};
