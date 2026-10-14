#pragma once

#include "CoreMinimal.h"
#include "World/WLFlagPassage.h"
#include "WLTierGate.generated.h"

/**
 * Tier progression gate at a bridge chokepoint between world rings. Ported from World/TierGate.cs.
 *
 * The boss is the ONLY lock: no level or kill requirements anywhere. Killing the tier's boss once
 * banks "bossdown_<GateId>" (it survives the boss respawning, and the save); walking into the gate
 * after that opens it for good: "tiergate_<GateId>" plus "tier_reached_<TargetTier>", which the
 * waypoint pads key off. A gate with no boss configured opens for everyone and logs a warning, so a
 * scene-setup mistake is a bug report, never a dead end (Unity behaviour).
 *
 * Doors: add two door meshes in a Blueprint child and list them as Movers sliding apart along local
 * X (Unity leftDoor / rightDoor, 6 m over 3.5 s). The placeholder barrier stands in until then.
 */
UCLASS()
class WASTELANDERS_API AWLTierGate : public AWLFlagPassage
{
	GENERATED_BODY()

public:
	AWLTierGate();

	virtual void BeginPlay() override;
	virtual bool IsOpenFor(const AWLPlayerState* PS) const override;

	/** Which tier this gate leads INTO (2-5). Tier 1 is the starting hub. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Gate", meta = (ClampMin = "2", ClampMax = "5"))
	int32 TargetTier = 2;

	/** Unique id; persists as "tiergate_<id>" and "bossdown_<id>". */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Gate")
	FString GateId = TEXT("t2_north");

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Gate")
	FText BossAliveMessage;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Gate")
	FText BossDefeatedMessage;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Gate")
	FText UnlockMessage;

	/** Name used in "Defeat {boss} to open this gate" (Unity used the boss object's name). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Gate")
	FText BossName;

	FString GetGateFlag() const { return TEXT("tiergate_") + GateId; }
	FString GetBossFlag() const { return TEXT("bossdown_") + GateId; }
	FString GetTierFlag() const { return FString::Printf(TEXT("tier_reached_%d"), TargetTier); }

	/** Pure: the flag names for an id/tier (unit tested). */
	static FString TierFlagFor(int32 Tier) { return FString::Printf(TEXT("tier_reached_%d"), Tier); }

protected:
	virtual void OnPlayerEntered(AWLPlayerState* PS, APawn* Pawn) override;
	virtual void OnBossDefeatedBy(AWLPlayerState* PS) override;

	bool IsBossDefeatedFor(const AWLPlayerState* PS) const;
	void Open(AWLPlayerState* PS);
	FText Prefix(bool bGood) const;
};
