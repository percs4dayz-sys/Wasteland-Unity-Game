#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Companions/WLCompanionTypes.h"
#include "WLCompanionComponent.generated.h"

class AWLPlayerState;
class AWLPlayerController;
class AWLCompanionPawn;
class UWLCompanionDefinitions;
struct FWLCharacterSave;

DECLARE_MULTICAST_DELEGATE(FWLOnCompanionChanged);

/**
 * Beastmastery companion (Combat/CompanionManager.cs, UI/EggChoiceUI.cs logic).
 *
 * You pick ONE beast from Roxy (or hatch a carried egg). It grows with your Beastmastery level
 * (stages at 30 and 60), follows you, joins fights already underway (it never starts one), runs
 * on stamina, can be scattered by damage, and has a special attack on a cooldown:
 *   wolf   Mutant Pup -> Mutant Wolf -> Alpha Wolf               savage melee, Frenzy
 *   turtle Shellback Hatchling -> Shellback -> Colossal Shellback slow tank, saddlebag +2/+4/+6 slots, soaks 35%
 *   bird   Rad-Chick -> Razorbeak -> Storm Razorbeak             spotter: +10/15/20% owner ranged damage
 *
 * On AWLPlayerState; all state is server-side. The pet bar gets an owner-only FWLCompanionStatus,
 * and the body (AWLCompanionPawn) replicates to everyone. Simulation runs on the 0.6 s game tick.
 * The companion's level is the owner's Beastmastery level, exactly as in Unity: there is no separate
 * pet XP. FWLSaveCompanion.Level keeps a snapshot for tools.
 *
 * The flags "pup_hatched", "egg_chosen" and "species_<key>" are still written, because Roxy's
 * dialogue and beast tasks read them and imported Unity saves carry them.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLCompanionComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLCompanionComponent();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

	void CaptureSave(FWLCharacterSave& Out) const;
	void ApplySave(const FWLCharacterSave& In);

	// --- queries (server; the owning client reads GetStatus) ---

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Companion")
	bool HasCompanion() const;

	/** Out and alive (CompanionManager.IsActive). */
	UFUNCTION(BlueprintPure, Category = "Wastelanders|Companion")
	bool IsSummoned() const { return bAlive && Pawn != nullptr; }

	FName GetSpeciesKey() const { return Species; }
	const FWLCompanionSpecies* GetSpecies() const;
	int32 GetStage() const;
	FText GetBeastName() const;
	AWLCompanionPawn* GetPawn() const { return Pawn; }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Companion")
	const FWLCompanionStatus& GetStatus() const { return Status; }

	/** Pet-bar line and fill, from the replicated status (CompanionManager.UpdateStatusUI). False = hide. */
	bool GetStatusLine(FText& OutText, float& OutFill) const;

	/** The special's blocker, or empty when it can fire (SpecialBlockReason). */
	FText GetSpecialBlockReason() const;

	// --- hooks for other systems (server) ---

	/** Combat calls this on the player's incoming hit; apply the returned damage. Shellback soaks 35%, others 10%. */
	int32 AbsorbForPlayer(int32 IncomingDamage);

	/** Razorbeak spotter perk on the owner's Marksmanship hits; 1 otherwise. */
	float GetMarksmanshipDamageMultiplier() const;

	// --- commands ---

	/** SERVER. */
	void Summon();
	void Dismiss(const FText& Message);
	void Toggle();
	void TriggerSpecial();
	/** The one-time pick: live baby right away, no egg (EggChoiceUI.Pick). */
	bool ChooseStarterBeast(FName SpeciesKey);
	/** Dev: hatch the wolf and step its growth stage (Unity F9). Not in shipping unless GM. */
	void DevCycleBeast();

	/** Client wrappers for input / UI (B, H, the picker). */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Companion") void RequestToggle() { ServerToggleSummon(); }
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Companion") void RequestSpecial() { ServerTriggerSpecial(); }
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|Companion") void RequestChooseStarterBeast(FName SpeciesKey) { ServerChooseStarterBeast(SpeciesKey); }

	UFUNCTION(Server, Reliable) void ServerToggleSummon();
	UFUNCTION(Server, Reliable) void ServerTriggerSpecial();
	UFUNCTION(Server, Reliable) void ServerChooseStarterBeast(FName SpeciesKey);

	/** Fires on the server and owning client when the status changes. */
	FWLOnCompanionChanged OnCompanionChanged;

	/** Optional designer definitions; null = UWLCompanionDefinitions class defaults (Unity values). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Companion")
	TObjectPtr<UWLCompanionDefinitions> Definitions;

	/** Body class (a Blueprint child can add the new art). */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Companion")
	TSubclassOf<AWLCompanionPawn> PawnClass;

	const UWLCompanionDefinitions* GetDefs() const;

	/** The enemy the beast should fight: your locked target, else the nearest mob already engaged with you. */
	AActor* FindAssistTarget() const;

protected:
	UFUNCTION()
	void OnRep_Status();

private:
	void HandleGameTick(int64 TickCount);
	void TickIncubation(float Dt);
	void TickCombat();
	void TickRegroup(double Now);
	void TickStamina(float Dt, bool bFighting);
	void SyncCarryPerk();
	void UpdatePawnGoal(AActor* Target);
	void Strike(AActor* Target, int32 MaxHit, const FText& Prefix);
	void TakePackDamage(int32 Amount);
	void SpawnPawn();
	void DestroyPawn();
	void RefreshStatus();
	void Msg(const FText& Text) const;
	void SetSpecies(FName Key);
	int32 BeastLevel() const;
	double Now() const;

	AWLPlayerState* GetPS() const;
	AWLPlayerController* GetPC() const;
	APawn* GetOwnerPawn() const;

	UPROPERTY(ReplicatedUsing = OnRep_Status)
	FWLCompanionStatus Status;

	UPROPERTY(Transient)
	TObjectPtr<AWLCompanionPawn> Pawn;

	FName Species;
	bool bAlive = false;
	int32 HP = 0;
	int32 MaxHP = 0;
	int32 TicksSinceAttack = 0;
	double SpecialReadyAt = 0.0;
	/** > 0 while scattered: when it auto-regroups. 0 = not scattered (dismissed on purpose or out). */
	double RegroupAt = 0.0;
	float Stamina = 100.f;
	bool bExhausted = false;
	float Incubation = 0.f;
	bool bIncubating = false;
	int32 AppliedCarrySlots = 0;
	/** Save said it was out: summon once the owner's pawn exists. */
	bool bResummonOnSpawn = false;
	FDelegateHandle TickHandle;
};
