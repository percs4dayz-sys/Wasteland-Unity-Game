#pragma once

#include "CoreMinimal.h"
#include "World/WLFlagPassage.h"
#include "WLBossShortcut.generated.h"

/**
 * A shortcut (drawbridge, slab, shutters, lift) that opens permanently, per player, when a boss
 * dies. Ports both World/BossShortcut.cs (one bridge, placed in its OPEN pose, starting raised by an
 * offset) and World/BCGateBossLink.cs (Broken Crescent shutters, placed CLOSED, sliding up by an
 * offset, also unlocking a tier for the waypoint network). Choose with bPlacedPoseIsOpen.
 *
 * Boss death -> UnlockFlag set on every credited player (killer + nearby), optional
 * "tier_reached_<UnlockedTier>", OpenMessage. Each client animates from its own flags; an already
 * open shortcut snaps open after load. Players without the flag stay blocked by the Blocker volume
 * (disable its collision for a bridge that is simply unwalkable when raised).
 */
UCLASS()
class WASTELANDERS_API AWLBossShortcut : public AWLFlagPassage
{
	GENERATED_BODY()

public:
	AWLBossShortcut();

	virtual void BeginPlay() override;
	virtual bool IsOpenFor(const AWLPlayerState* PS) const override;

	/** Save flag that remembers it's open. Blank = "shortcut_<actor label/name>" (Unity default). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Shortcut")
	FString UnlockFlag;

	/** BCGateBossLink unlockedTier: >= 2 also sets "tier_reached_<n>". */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Shortcut", meta = (ClampMin = "0", ClampMax = "5"))
	int32 UnlockedTier = 0;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Shortcut")
	FText OpenMessage;

	const FString& GetUnlockFlag() const { return ResolvedFlag; }

protected:
	virtual void OnBossDefeatedBy(AWLPlayerState* PS) override;

	FString ResolvedFlag;
};
