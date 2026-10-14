#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "AbilitySystemInterface.h"
#include "Interaction/WLInteractable.h"
#include "WLEnemyCharacter.generated.h"

class UWLAbilitySystemComponent;
class UWLCombatAttributeSet;
class UWLDropTableAsset;
class UStaticMeshComponent;
class AWLPlayerState;
class AWLEnemyAIController;

DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnEnemyDied, class AWLEnemyCharacter*);
DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnEnemySwingStarted, float /*WindupSeconds*/);
DECLARE_MULTICAST_DELEGATE(FWLOnEnemySwingEvent);

/** What happens to the body after death. */
UENUM(BlueprintType)
enum class EWLEnemyDeathMode : uint8
{
	/** Enemy3D default: corpse lingers, hides, then the enemy respawns at home after RespawnSeconds. */
	RespawnAtHome,
	/** RespawnOnDeath (training dummies): quietly reset HP in place after RespawnSeconds. */
	ResetInPlace,
	/** SpawnedEnemy: destroy after CorpseSeconds; the owning spawner replaces it (OnEnemyDied). */
	DestroyCorpse,
	/** FissionGolem: the subclass owns the corpse (it becomes a harvest node). */
	Custom
};

/** Spawner-applied stats (FissionGolemSpawner.Stat / BCEarthGolemSpawner.GolemStats). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLEnemyStats
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") int32 MaxHP = 10;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") int32 AttackLevel = 1;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") int32 DefenceLevel = 1;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") int32 MaxHit = 1;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") bool bAggressive = false;
	/** Unreal units (Unity metres x 100). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") float AggroRange = 900.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") float AttackRange = 180.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") float MoveSpeed = 320.f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") float AttackCooldown = 1.8f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") float WindupSeconds = 0.4f;
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Enemy") float WanderRadius = 150.f;
};

/**
 * A hostile creature (Unity CombatTarget + Enemy3D + SpawnedEnemy + RespawnOnDeath).
 *
 * Owns its own ASC + UWLCombatAttributeSet (HP). Brains run server-side in AWLEnemyAIController;
 * this class holds the designer data, the swing (windup -> land) the AI triggers, and death:
 * kill credit to the player who dealt the most damage, IncrementKills, OnEnemyKilled, loot through
 * UWLLootLibrary, OnEnemyDied (spawners respawn on it), and the corpse lifecycle.
 *
 * Art: no Unity models. The skeletal Mesh slot stays empty for new art; until then an engine
 * cylinder placeholder shows the capsule (hidden automatically once a mesh is assigned).
 */
UCLASS()
class WASTELANDERS_API AWLEnemyCharacter : public ACharacter, public IAbilitySystemInterface, public IWLInteractable
{
	GENERATED_BODY()

public:
	AWLEnemyCharacter(const FObjectInitializer& ObjectInitializer);

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void PostInitializeComponents() override;
	virtual void BeginPlay() override;

	virtual UAbilitySystemComponent* GetAbilitySystemComponent() const override;
	UWLAbilitySystemComponent* GetWLAbilitySystem() const { return AbilitySystem; }

	// --- contract API -------------------------------------------------------------------------------

	/** Loot tier 1-5: explicit Tier, else inferred from MaxHP (CombatTarget.LootTier). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy")
	int32 GetTier() const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy") bool IsBoss() const { return bIsBoss; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy") bool IsMiniBoss() const { return bIsMiniBoss; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy") bool IsDummy() const { return bIsDummy; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy") bool IsGodHunter() const { return bIsGodHunter; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy") bool IsAggressive() const { return bIsAggressive; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy") bool IsDead() const;
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy") virtual bool IsFissionGolem() const { return false; }

	/** Beast-task target (BeastTasks.Huntable): not a dummy, boss, mini-boss, God-Hunter or golem. */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy")
	bool IsHuntable() const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy")
	FText GetEnemyDisplayName() const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy") int32 GetCurrentHP() const;
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Enemy") int32 GetMaxHP() const;

	int32 GetAttackLevel() const { return AttackLevel; }
	int32 GetDefenceLevel() const { return DefenceLevel; }
	int32 GetMaxHit() const { return MaxHit; }
	UWLDropTableAsset* GetDropTable() const { return DropTable; }

	/** Fires on the server once per death, after loot. Spawners listen to replace the enemy. */
	FWLOnEnemyDied OnEnemyDied;

	// --- AI-facing ---------------------------------------------------------------------------------

	/** The pawn this enemy is chasing or swinging at (server), or null. */
	AActor* GetAggroTarget() const;

	/** Unity IsAggroOnPlayer: alive, Pawn within aggro range, and aggressive or provoked by it. */
	bool IsAggroOn(const AActor* Pawn) const;

	/** Re-anchor home (spawner placement). */
	void SetHome(const FVector& WorldPos) { HomeLocation = WorldPos; }
	const FVector& GetHome() const { return HomeLocation; }

	/** SERVER. Strength L40 stagger: interrupt a windup and push the next swing back. */
	void Stagger(float Seconds);

	/** SERVER. Spawner configuration (call before or right after spawning). */
	void ApplyStats(const FWLEnemyStats& Stats);

	/** SERVER. Full HP, alive, visible, collision back on (respawn / dummy reset). */
	virtual void ResetEnemy();

	/** SERVER. Called by the AI when a windup completes: hit or whiff the target. */
	void LandSwing(APawn* Target);

	/** SERVER. Damage this enemy as a reflect/counter from Victim's defences. */
	void TakeReturnDamage(int32 Amount, AActor* Source);

	/** Cosmetic hooks for future animation (server side; mirror with a multicast once art exists). */
	FWLOnEnemySwingStarted OnSwingStarted;
	FWLOnEnemySwingEvent OnSwingLanded;
	FWLOnEnemySwingEvent OnSwingCancelled;

	UFUNCTION(NetMulticast, Unreliable)
	void MulticastHitsplat(int32 Amount, bool bHit, FName Tag);

	// --- designer data (CombatTarget) ---------------------------------------------------------------

	UPROPERTY(EditAnywhere, Replicated, BlueprintReadOnly, Category = "Wastelanders|Enemy")
	FText DisplayName;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Enemy", meta = (ClampMin = "1"))
	int32 MaxHP = 10;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Enemy", meta = (ClampMin = "1"))
	int32 AttackLevel = 1;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Enemy", meta = (ClampMin = "1"))
	int32 DefenceLevel = 1;

	/** Unity maxDamage. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Enemy", meta = (ClampMin = "0"))
	int32 MaxHit = 1;

	/** World tier 1-5 for the drop table; 0 = infer from MaxHP. */
	UPROPERTY(EditAnywhere, Replicated, BlueprintReadOnly, Category = "Wastelanders|Enemy", meta = (ClampMin = "0", ClampMax = "5"))
	int32 Tier = 0;

	UPROPERTY(EditAnywhere, Replicated, BlueprintReadOnly, Category = "Wastelanders|Enemy") bool bIsDummy = false;
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Enemy") bool bIsAggressive = false;
	/** Better gear odds; never a beast-task target. */
	UPROPERTY(EditAnywhere, Replicated, BlueprintReadOnly, Category = "Wastelanders|Enemy") bool bIsMiniBoss = false;
	/** Strength L99 bonus damage applies; never a beast-task target. */
	UPROPERTY(EditAnywhere, Replicated, BlueprintReadOnly, Category = "Wastelanders|Enemy") bool bIsBoss = false;
	/** Own drop table: guaranteed resources plus a 1-in-50 God Tier roll (Items decides). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Enemy") bool bIsGodHunter = false;

	/** Optional custom drops; empty keeps the standard tier / boss table. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Wastelanders|Enemy")
	TObjectPtr<UWLDropTableAsset> DropTable;

	// --- behaviour (Enemy3D) ------------------------------------------------------------------------

	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI") float AggroRange = 900.f;
	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI") float AttackRange = 180.f;
	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI") float MoveSpeed = 320.f;
	/** Seconds from one swing's hit to the next windup. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI") float AttackCooldown = 1.8f;
	/** The dodge window between the telegraph and the hit. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI") float WindupSeconds = 0.4f;
	/** How far past AttackRange a swing still connects: one backstep doesn't cheese it, a dodge does. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI") float SwingReachBonus = 1.35f;
	/** Idle pacing radius around home (0 = stand still). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI") float WanderRadius = 150.f;
	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI", meta = (ClampMin = "0.1", ClampMax = "1")) float WanderSpeedMul = 0.45f;
	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI") FVector2D WanderPauseRange = FVector2D(1.5f, 4.f);
	/** Walking home after losing interest (Unity: 0.6x move speed). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI") float ReturnSpeedMul = 0.6f;
	/**
	 * MMO addition: past this distance from home the enemy drops the chase and walks back, so a mob
	 * can't be dragged across a shard. 0 = Unity behaviour (chase while the target is within aggro range).
	 */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|AI") float LeashRange = 2500.f;

	// --- death ----------------------------------------------------------------------------------------

	UPROPERTY(EditAnywhere, Category = "Wastelanders|Death") EWLEnemyDeathMode DeathMode = EWLEnemyDeathMode::RespawnAtHome;
	/** RespawnAtHome / ResetInPlace delay (Enemy3D.respawnSeconds 10, RespawnOnDeath.delay 3). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Death") float RespawnSeconds = 10.f;
	/** How long the corpse stays visible (Enemy3D.deathLingerSeconds 2.2, SpawnedEnemy.corpseSeconds 3). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Death") float CorpseSeconds = 2.2f;

protected:
	// IWLInteractable
	virtual FText GetInteractName_Implementation() const override;
	virtual FText GetExamineText_Implementation() const override;
	virtual TArray<FWLInteractOption> GetInteractOptions_Implementation(const AWLPlayerCharacter* Player) const override;
	virtual float GetInteractRange_Implementation() const override;
	virtual bool Interact_Implementation(AWLPlayerCharacter* Player, FName Verb) override;

	/** SERVER. Death: credit, loot, events, corpse. Subclasses extend (golem corpse). */
	virtual void HandleDeath(AActor* Instigator);

	/** Corpse handling per DeathMode; Custom does nothing here. */
	virtual void BeginCorpse();

	void HandleDamageTaken(int32 Amount, AActor* Instigator, AActor* Causer);
	void HandleOutOfHealth(AActor* Instigator, AActor* Causer);
	void HideCorpse();
	void SetCombatCollision(bool bEnabled);

	/** The player who dealt the most damage this life (ties: first to reach it). */
	AWLPlayerState* FindTopDamager() const;
	static AWLPlayerState* ResolvePlayerState(AActor* Instigator);
	AWLEnemyAIController* GetEnemyAI() const;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UWLAbilitySystemComponent> AbilitySystem;

	UPROPERTY()
	TObjectPtr<UWLCombatAttributeSet> CombatAttributes;

	UPROPERTY(VisibleAnywhere, Category = "Components")
	TObjectPtr<UStaticMeshComponent> PlaceholderBody;

	FVector HomeLocation = FVector::ZeroVector;
	TMap<TWeakObjectPtr<AWLPlayerState>, int32> DamageByPlayer;
	FTimerHandle CorpseTimer;
	FTimerHandle RespawnTimer;
};
