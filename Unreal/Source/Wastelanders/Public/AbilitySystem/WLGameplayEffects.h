#pragma once

#include "CoreMinimal.h"
#include "GameplayEffect.h"
#include "WLGameplayEffects.generated.h"

/**
 * Asset-free gameplay effects. Each is configured entirely in its constructor, so specs are made
 * straight from the class (MakeOutgoingSpec(UWLGE_Damage::StaticClass(), ...)) with no Blueprint or
 * data asset to author, and every damage/heal still flows through GAS (prediction keys, cues,
 * logging, attribute-set clamping) rather than poking attributes directly.
 */

/** Instant: adds SetByCaller.Damage to the meta Damage attribute. */
UCLASS()
class WASTELANDERS_API UWLGE_Damage : public UGameplayEffect
{
	GENERATED_BODY()

public:
	UWLGE_Damage();
};

/** Instant: adds SetByCaller.Heal to the meta Healing attribute. */
UCLASS()
class WASTELANDERS_API UWLGE_Heal : public UGameplayEffect
{
	GENERATED_BODY()

public:
	UWLGE_Heal();
};
