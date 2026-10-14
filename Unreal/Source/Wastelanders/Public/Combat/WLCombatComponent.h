#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "GameplayAbilitySpecHandle.h"
#include "Math/RandomStream.h"
#include "Core/WLTypes.h"
#include "Core/WLGameTickSubsystem.h"
#include "Combat/WLCombatMath.h"
#include "Combat/WLModuleEffects.h"
#include "Combat/WLWeaponMagazine.h"
#include "WLCombatComponent.generated.h"

class AWLPlayerCharacter;
class AWLPlayerState;
class AWLEnemyCharacter;
class UWLAbilitySystemComponent;
class UWLEquipmentComponent;
class UWLSkillsComponent;
class UGameplayAbility;
struct FWLItemRow;

DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnCombatTargetChanged, AActor*);
DECLARE_MULTICAST_DELEGATE(FWLOnCombatStateChanged);
DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnAttackPerformed, bool /*bRanged*/);

/**
 * The player's fighting body (Unity ActionCombat3D + WeaponMagazine + CombatManager style/stance +
 * ModuleEffects state). Lives on AWLPlayerCharacter; all resolution is server-side.
 *
 * Attack loop: the client picks a target (SetTarget / AttackNearest / AttackInPlace / the enemy's
 * "Attack" verb). The server validates it, walks the client into reach (ClientApproachTarget ->
 * AWLPlayerController::MoveToLocation), and on each 0.6 s game tick fires UWLGA_BasicAttack when
 * the weapon is off cooldown. Cooldowns are counted in game ticks from the weapon's
 * AttackSpeedTicks (stance and module adjusted, fractional remainder carried, see
 * FWLCombatMath::NextReadyTick). Accuracy, max hit, milestone perks, set bonuses, module procs, ammo,
 * the magazine, XP and hitsplats all follow the Unity code.
 *
 * Deviations from Unity (by design):
 *  - Auto-repeat: OSRS keeps swinging until the target dies or you disengage. Unity's real-time
 *    build swung once per click. bAutoRepeat (default true) picks the OSRS rule.
 *  - The first-person crosshair hitscan (FireCrosshair) is not ported: the UE client is the
 *    click-to-move camera, and a client-authored "my ray hit him" is a cheat vector in an MMO.
 *  - Gauntlet/gun "ranged" reach uses the same reach formula as Unity's click combat.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLCombatComponent : public UActorComponent, public IWLTickable
{
	GENERATED_BODY()

public:
	UWLCombatComponent();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	// IWLTickable (server)
	virtual void OnGameTick(int64 TickCount) override;

	// --- contract API ------------------------------------------------------------------------------

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat")
	EWLCombatStyle GetStyle() const { return Style; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat")
	EWLCombatStance GetStance() const { return Stance; }

	/** Pick an attack option. Rejected unless the current style offers it. */
	UFUNCTION(Server, Reliable, BlueprintCallable, Category = "Wastelanders|Combat")
	void ServerSetStance(EWLCombatStance NewStance);

	/** Attack this actor (an AWLEnemyCharacter), or nullptr to disengage. Server applies directly; a client RPCs. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Combat")
	void SetTarget(AActor* NewTarget);

	/** SERVER. Eating mid-fight delays the next attack (no effect when idle or already off cooldown). */
	void DelayAttackAfterEating(int32 Ticks);

	/** SERVER. Weapon changed: style, stance clamp, magazine, modules. Equipment calls this after every change. */
	void RefreshStyleFromEquipment();

	// --- engage / disengage (server) ---------------------------------------------------------------

	/** SERVER. Validate and start attacking Target (walks into reach first). */
	bool Engage(AActor* InTarget);

	/** SERVER. Drop the target and any queued swing. */
	void Disengage(const FText& Message = FText::GetEmpty());

	/** F key / X button: nearest living enemy within AcquireRange. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Combat")
	void AttackNearest();

	/** Shift-click: swing toward AimPoint without moving, at the nearest enemy in a ~100 degree arc. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Combat")
	void AttackInPlace(FVector AimPoint);

	/** Fission special (Overload Cascade). Instant; there is no armed state. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Combat")
	void RequestSpecialAttack();

	/** R key: reload the magazine early. */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Combat")
	void RequestReload();

	// --- entry points for the gameplay abilities (server) -------------------------------------------

	/** UWLGA_BasicAttack: one swing/shot at the current target. */
	bool PerformBasicAttack();

	/** UWLGA_SpecialAttack: the Overload Cascade. */
	bool PerformSpecialAttack();

	/** UWLGA_Reload: start a reload; returns its duration in seconds, or 0 if it can't start. */
	float BeginReload();
	void CompleteReload();
	void AbortReload();

	// --- incoming ---------------------------------------------------------------------------------------

	/** SERVER. An enemy's swing landed in reach: dodge, defence roll, perks, modules, companion. Returns damage taken. */
	int32 ResolveIncomingAttack(AWLEnemyCharacter* Attacker);

	// --- queries (work on the owning client too) ----------------------------------------------------

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") AActor* GetTarget() const { return Target; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") bool HasWeaponSpecial() const;
	/** Empty when the special can fire now; otherwise why not (Unity SpecialBlockReason). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") FText GetSpecialBlockReason() const;
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") float GetSpecialEnergyCost() const { return FWLCombatMath::SpecialEnergyCost; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") FText GetSpecialAttackName() const;
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") bool UsesMagazine() const { return bRepUsesMagazine; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") int32 GetMagazineLoaded() const { return RepMagLoaded; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") int32 GetMagazineSize() const { return RepMagSize; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") bool IsReloading() const { return bRepReloading; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") float GetReloadProgress() const;
	/** Holding a ranged weapon on a target (firing-stance animation). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Combat") bool IsAiming() const;

	static TArray<EWLCombatStance> StancesFor(EWLCombatStyle InStyle) { return FWLCombatMath::StancesFor(InStyle); }
	static FText StanceLabel(EWLCombatStance InStance);
	static FText StanceDescription(EWLCombatStance InStance, EWLCombatStyle InStyle);

	// --- events ---------------------------------------------------------------------------------------

	FWLOnCombatTargetChanged OnTargetChanged;
	FWLOnCombatStateChanged OnMagazineChanged;
	FWLOnCombatStateChanged OnStyleChanged;
	/** Every machine: a swing/shot went off (drive the attack animation). */
	FWLOnAttackPerformed OnAttackPerformed;

	UFUNCTION(NetMulticast, Unreliable)
	void MulticastHitsplat(int32 Amount, bool bHit, FName Tag);

	// --- tuning (Unity metres x 100) ---------------------------------------------------------------

	/** Reach at 1 tile (touching). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Combat") float MeleeReach = 190.f;
	/** Standoff each extra tile of weapon range buys. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Combat") float ReachPerTile = 200.f;
	/** How far AttackNearest picks a target. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Combat") float AcquireRange = 1400.f;
	/** Further than this and the fight drops (also the max distance a target may be selected from). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Combat") float LeashRange = 1600.f;
	/** OSRS auto-attack (true) or Unity one-swing-per-click (false). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Combat") bool bAutoRepeat = true;

	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Abilities") TSubclassOf<UGameplayAbility> BasicAttackAbility;
	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Abilities") TSubclassOf<UGameplayAbility> SpecialAttackAbility;
	UPROPERTY(EditDefaultsOnly, Category = "Wastelanders|Abilities") TSubclassOf<UGameplayAbility> ReloadAbility;

protected:
	UFUNCTION(Server, Reliable) void ServerSetTarget(AActor* NewTarget);
	UFUNCTION(Server, Reliable) void ServerAttackNearest();
	UFUNCTION(Server, Reliable) void ServerAttackInPlace(FVector_NetQuantize AimPoint);
	UFUNCTION(Server, Reliable) void ServerRequestSpecialAttack();
	UFUNCTION(Server, Reliable) void ServerRequestReload();

	/** Server -> owner: walk into reach of the target (client-side path following). */
	UFUNCTION(Client, Unreliable) void ClientApproachTarget(FVector_NetQuantize Goal, float Acceptance);
	/** Server -> owner: face the swing (rotation is client-owned with predicted movement). */
	UFUNCTION(Client, Unreliable) void ClientFaceToward(FVector_NetQuantize Point);
	/** Server -> owner: stop the auto-walk when the fight drops. */
	UFUNCTION(Client, Reliable) void ClientStopApproach();

	UFUNCTION(NetMulticast, Unreliable) void MulticastAttackPerformed(bool bRanged);

	UFUNCTION() void OnRep_Target();
	UFUNCTION() void OnRep_Style();
	UFUNCTION() void OnRep_Magazine();

	void HandleAbilitySystemReady(AWLPlayerCharacter* Character);
	void HandleDeathStateChanged(bool bDead);

	/** Activate an ability by its granted handle; falls back to calling Fallback when not granted yet. */
	bool ActivateOrRun(const FGameplayAbilitySpecHandle& Handle, TFunctionRef<bool()> Fallback);

	void ResolveHit(AWLEnemyCharacter* InTarget, bool bRanged);
	void AwardXP(int32 Damage, EWLCombatStyle HitStyle);
	void GiveXP(EWLSkill Skill, float Amount);
	void ConsumeShot(bool bFission);
	void Cleave(AWLEnemyCharacter* Primary, int32 Damage);
	void DealDamage(AWLEnemyCharacter* Victim, int32 Damage, FName Tag);
	void TickBleeds();
	void TickQueuedSwing(int64 TickCount);
	void StartCooldown(int64 TickCount);
	bool CheckRangedGates(bool bAutoReload, bool bClearQueueWhenEmpty);
	void FaceToward(const FVector& Point);
	void SyncModules();
	void UpdateMagazineReplication();
	void SetTargetInternal(AActor* NewTarget);
	void Message(const FText& Text) const;

	AWLEnemyCharacter* BestTargetInArc(const FVector& AimPoint, float Range) const;
	AWLEnemyCharacter* NearestEnemy(float Range) const;
	bool IsValidEnemy(const AActor* Actor) const;
	bool TooFar(const AActor* Actor) const;

	AWLPlayerCharacter* GetPlayerCharacter() const;
	AWLPlayerState* GetWLPlayerState() const;
	UWLAbilitySystemComponent* GetASC() const;
	UWLEquipmentComponent* GetEquipment() const;
	UWLSkillsComponent* GetSkills() const;
	const FWLItemRow* GetWeapon() const;
	EWLWeaponStyle EquippedWeaponStyle() const;
	bool IsRangedEquipped() const;
	bool IsFissionEquipped() const;
	bool IsGunEquipped() const;
	bool IsDead() const;
	float ReachUnits() const;
	int32 CurrentSpeedTicks() const;
	int64 CurrentTick() const;
	double Now() const;
	int32 Level(EWLSkill Skill) const;
	uint64 KeyFor(const AActor* Actor) const;

	// --- replicated state ----------------------------------------------------------------------------

	/** Shooter pivot: Unity started you ranged; RefreshStyleFromEquipment sets the real value. */
	UPROPERTY(ReplicatedUsing = OnRep_Style) EWLCombatStyle Style = EWLCombatStyle::Ranged;
	UPROPERTY(ReplicatedUsing = OnRep_Style) EWLCombatStance Stance = EWLCombatStance::Accurate;
	UPROPERTY(ReplicatedUsing = OnRep_Target) TObjectPtr<AActor> Target;

	UPROPERTY(ReplicatedUsing = OnRep_Magazine) bool bRepUsesMagazine = false;
	UPROPERTY(ReplicatedUsing = OnRep_Magazine) int32 RepMagLoaded = 0;
	UPROPERTY(ReplicatedUsing = OnRep_Magazine) int32 RepMagSize = 0;
	UPROPERTY(ReplicatedUsing = OnRep_Magazine) bool bRepReloading = false;
	UPROPERTY(Replicated) float ReloadStartServerTime = 0.f;
	UPROPERTY(Replicated) float ReloadEndServerTime = 0.f;

	// --- server state ----------------------------------------------------------------------------------

	bool bSwingQueued = false;
	double ReadyAtTick = 0.0;
	bool bPendingFirstStrike = false;
	TWeakObjectPtr<AActor> StreakTarget;
	int32 Streak = 0;
	FVector LastApproachGoal = FVector(FLT_MAX);

	FWLCombatXP XPCarry;
	FWLModuleEffects Modules;
	FWLWeaponMagazine Magazine;
	int32 ReloadWeaponId = 0;
	TMap<uint64, TWeakObjectPtr<AWLEnemyCharacter>> BleedTargets;
	FRandomStream Rng;

	FGameplayAbilitySpecHandle BasicAttackHandle;
	FGameplayAbilitySpecHandle SpecialAttackHandle;
	FGameplayAbilitySpecHandle ReloadHandle;
	TWeakObjectPtr<UWLAbilitySystemComponent> BoundASC;
	bool bRegisteredTick = false;
};
