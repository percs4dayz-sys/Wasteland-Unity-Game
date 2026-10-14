#pragma once

#include "CoreMinimal.h"
#include "AbilitySystemComponent.h"
#include "WLAbilitySystemComponent.generated.h"

class UGameplayAbility;
class AWLPlayerState;

DECLARE_MULTICAST_DELEGATE_TwoParams(FWLOnHealthChanged, int32 /*Current*/, int32 /*Max*/);
DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnSpecialEnergyChanged, float /*Current*/);
DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnDeathStateChanged, bool /*bDead*/);
DECLARE_MULTICAST_DELEGATE_ThreeParams(FWLOnDamageTaken, int32 /*Amount*/, AActor* /*Instigator*/, AActor* /*Causer*/);
DECLARE_MULTICAST_DELEGATE_TwoParams(FWLOnOutOfHealth, AActor* /*Instigator*/, AActor* /*Causer*/);

/**
 * Gameplay Ability System component for players (on AWLPlayerState, avatar = pawn) and enemies
 * (on AWLEnemyCharacter itself). Wraps the HP/special pool that Unity kept in PlayerStats,
 * CombatTarget and CombatManager behind a small integer API, and owns the dead state.
 *
 * Death: UWLCombatAttributeSet detects Health reaching 0 and calls HandleOutOfHealth. The dead flag
 * replicates (so State_Dead is mirrored as a loose tag on every machine) and blocks damage, healing
 * and abilities until Revive. For players this component also runs the respawn flow (Unity
 * PlayerRespawn.HandleDied): game event, chat line, AWLGameMode::RequestRespawn.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLAbilitySystemComponent : public UAbilitySystemComponent
{
	GENERATED_BODY()

public:
	UWLAbilitySystemComponent();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	// --- contract API ------------------------------------------------------------------------------

	/** SERVER. Heal (food, leech, dev). Ignored while dead. */
	void ApplyHeal(int32 Amount);

	/** SERVER. Top up to max (dev /heal, respawn). Ignored while dead; use Revive for that. */
	void HealToFull();

	/** SERVER. Restore saved HP on login; 0 (or anything out of range) means full. */
	void RestoreHealth(int32 SavedHP);

	int32 GetCurrentHealthInt() const;
	int32 GetMaxHealthInt() const;

	/** SERVER. Back to life at full HP (Unity PlayerStats.Revive), clearing State_Dead. */
	void Revive();

	bool IsDead() const { return bDead; }

	// --- damage ------------------------------------------------------------------------------------

	/**
	 * SERVER. Deal damage through UWLGE_Damage. Instigator is the attacking pawn or PlayerState (kill
	 * credit reads it), Causer the thing that hit (weapon actor, turret, companion). Returns the HP
	 * actually removed (0 when dead or Amount <= 0).
	 */
	int32 ApplyDamage(int32 Amount, AActor* Instigator, AActor* Causer = nullptr);

	// --- max health (driven by Endurance for players, by the enemy's MaxHP for enemies) -----------

	/**
	 * SERVER. Set MaxHealth. With bHealDelta the current HP rises by the increase (an Endurance
	 * level-up adds its HP to the pool, PlayerStats.AddXP), otherwise it is only clamped.
	 */
	void SetMaxHealth(int32 NewMax, bool bHealDelta);

	// --- special energy (CombatManager) ------------------------------------------------------------

	float GetSpecialEnergyValue() const;
	float GetMaxSpecialEnergyValue() const;

	/** SERVER. Spend energy if there is enough (0.001 tolerance like Unity); false spends nothing. */
	bool TrySpendSpecial(float Cost);

	/** SERVER. Refill instantly (dev tools). */
	void RestoreSpecial();

	/** 3.33/s refills the bar in 30 s, so a 50-cost special is up about every 15 s. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Combat")
	float SpecialRegenPerSecond = 3.33f;

	/** Enemies turn this off (they have no special). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Combat")
	bool bRegenSpecialEnergy = true;

	// --- abilities ---------------------------------------------------------------------------------

	/** SERVER. Grant an ability once (the ASC outlives pawns, so re-possession must not stack grants). */
	FGameplayAbilitySpecHandle GiveAbilityOnce(TSubclassOf<UGameplayAbility> AbilityClass, int32 Level = 1);

	// --- events --------------------------------------------------------------------------------------

	/** Server and clients (attribute replication). */
	FWLOnHealthChanged OnHealthChanged;
	FWLOnSpecialEnergyChanged OnSpecialEnergyChanged;
	FWLOnDeathStateChanged OnDeathStateChanged;

	/** Server only: HP removed by a damage effect. Enemies use it for provocation and kill credit. */
	FWLOnDamageTaken OnDamageTaken;

	/** Server only: Health just hit 0. */
	FWLOnOutOfHealth OnOutOfHealth;

	// --- called by UWLCombatAttributeSet -------------------------------------------------------------
	void NotifyDamageTaken(int32 Amount, AActor* Instigator, AActor* Causer);
	void HandleOutOfHealth(AActor* Instigator, AActor* Causer);

protected:
	UFUNCTION()
	void OnRep_Dead();

	void SetDead(bool bNewDead);
	void ApplyDeadTag();
	void HandlePlayerDeath();
	void HandleGameTick(int64 TickCount);
	void HandleHealthAttributeChanged(const struct FOnAttributeChangeData& Data);
	void HandleSpecialAttributeChanged(const struct FOnAttributeChangeData& Data);

	AWLPlayerState* GetOwningWLPlayerState() const;

	UPROPERTY(ReplicatedUsing = OnRep_Dead)
	bool bDead = false;

	bool bDeadTagApplied = false;
	FDelegateHandle GameTickHandle;
};
