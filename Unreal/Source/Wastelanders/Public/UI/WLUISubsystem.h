#pragma once

#include "CoreMinimal.h"
#include "Subsystems/LocalPlayerSubsystem.h"
#include "Tickable.h"
#include "WLUISubsystem.generated.h"

class AWLPlayerController;
class AWLPlayerState;
class AWLPlayerCharacter;
class UWLViewModelBase;
class UWLHudViewModel;
class UWLInventoryViewModel;
class UWLEquipmentViewModel;
class UWLBankViewModel;
class UWLCraftingViewModel;
class UWLSkillsViewModel;
class UWLXPDropViewModel;
class UWLCombatStyleViewModel;
class UWLChatViewModel;
class UWLDialogueViewModel;
class UWLContextMenuViewModel;
class UWLMinimapViewModel;
class UWLSettingsViewModel;

/**
 * Toggleable windows (Unity HudTabBar tabs + hotkey panels). Several may be open at once: Unity's tab
 * bar deliberately stopped closing other panels so e.g. Inventory and Crafting can sit side by side.
 */
UENUM(BlueprintType)
enum class EWLUIPanel : uint8
{
	Inventory,    // I
	CombatStyle,  // F7
	Skills,       // K
	Equipment,    // F9
	Music,
	Build,        // BuildInfoPanelUI signpost
	Settings,     // O
	SystemMenu,   // F10 (WastelandMenuUI)
	DevPanel,     // non-shipping only
	Count UMETA(Hidden)
};

DECLARE_MULTICAST_DELEGATE(FWLOnUIPanelsChanged);

/**
 * Per-local-player UI root. Owns every view model, follows the local player's controller / PlayerState /
 * pawn as they appear and change (respawn, seamless travel) and re-binds the view models, and holds the
 * open/closed state of the windows. Replaces the Unity UI's per-script "bind to PlayerEntity.Instance,
 * retry every frame until it exists" loops.
 *
 * Also installs the asset-free AWLDebugHUD on the local controller while UWLUIThemeSettings says so.
 */
UCLASS()
class WASTELANDERS_API UWLUISubsystem : public ULocalPlayerSubsystem, public FTickableGameObject
{
	GENERATED_BODY()

public:
	static UWLUISubsystem* Get(const APlayerController* PC);
	static UWLUISubsystem* Get(const UObject* WorldContextObject);

	virtual void Initialize(FSubsystemCollectionBase& Collection) override;
	virtual void Deinitialize() override;

	// FTickableGameObject
	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override;
	virtual ETickableTickType GetTickableTickType() const override { return ETickableTickType::Conditional; }
	virtual bool IsTickable() const override;
	virtual bool IsTickableWhenPaused() const override { return true; }
	virtual UWorld* GetTickableGameObjectWorld() const override;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLHudViewModel* GetHud() const { return Hud; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLInventoryViewModel* GetInventory() const { return Inventory; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLEquipmentViewModel* GetEquipment() const { return Equipment; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLBankViewModel* GetBank() const { return Bank; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLCraftingViewModel* GetCrafting() const { return Crafting; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLSkillsViewModel* GetSkills() const { return Skills; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLXPDropViewModel* GetXPDrops() const { return XPDrops; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLCombatStyleViewModel* GetCombatStyle() const { return CombatStyle; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLChatViewModel* GetChat() const { return Chat; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLDialogueViewModel* GetDialogue() const { return Dialogue; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLContextMenuViewModel* GetContextMenu() const { return ContextMenu; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLMinimapViewModel* GetMinimap() const { return Minimap; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") UWLSettingsViewModel* GetSettings() const { return Settings; }

	// --- windows --------------------------------------------------------------------------------

	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") bool IsPanelOpen(EWLUIPanel Panel) const;
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|UI") void SetPanelOpen(EWLUIPanel Panel, bool bOpen);
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|UI") void TogglePanel(EWLUIPanel Panel);
	/**
	 * Escape: close the top-most thing (context menu, then dialogue choice picker, then crafting, bank,
	 * windows). Returns false when nothing was open.
	 */
	UFUNCTION(BlueprintCallable, Category = "Wastelanders|UI") bool CloseTopmost();

	FWLOnUIPanelsChanged OnPanelsChanged;

	// --- pointer arbitration ----------------------------------------------------------------------

	/**
	 * Set every frame by whichever UI draws under the cursor (debug HUD or widgets). The player controller
	 * should skip world clicks while true (Unity ContextMenuUI.ConsumedClick) - see the shared-file note.
	 */
	void SetPointerOverUI(bool bOver) { bPointerOverUI = bOver; }
	UFUNCTION(BlueprintPure, Category = "Wastelanders|UI") bool IsPointerOverUI() const { return bPointerOverUI || bPointerOverUILastFrame; }

	/** Local player objects currently bound. */
	AWLPlayerController* GetBoundController() const { return BoundPC.Get(); }
	AWLPlayerState* GetBoundPlayerState() const { return BoundPS.Get(); }
	AWLPlayerCharacter* GetBoundPawn() const { return BoundPawn.Get(); }

private:
	void RebindAll();
	void MaybeInstallDebugHUD(AWLPlayerController* PC);
	TArray<UWLViewModelBase*> AllViewModels() const;

	template <typename T>
	T* MakeVM() { return NewObject<T>(this); }

	UPROPERTY(Transient) TObjectPtr<UWLHudViewModel> Hud;
	UPROPERTY(Transient) TObjectPtr<UWLInventoryViewModel> Inventory;
	UPROPERTY(Transient) TObjectPtr<UWLEquipmentViewModel> Equipment;
	UPROPERTY(Transient) TObjectPtr<UWLBankViewModel> Bank;
	UPROPERTY(Transient) TObjectPtr<UWLCraftingViewModel> Crafting;
	UPROPERTY(Transient) TObjectPtr<UWLSkillsViewModel> Skills;
	UPROPERTY(Transient) TObjectPtr<UWLXPDropViewModel> XPDrops;
	UPROPERTY(Transient) TObjectPtr<UWLCombatStyleViewModel> CombatStyle;
	UPROPERTY(Transient) TObjectPtr<UWLChatViewModel> Chat;
	UPROPERTY(Transient) TObjectPtr<UWLDialogueViewModel> Dialogue;
	UPROPERTY(Transient) TObjectPtr<UWLContextMenuViewModel> ContextMenu;
	UPROPERTY(Transient) TObjectPtr<UWLMinimapViewModel> Minimap;
	UPROPERTY(Transient) TObjectPtr<UWLSettingsViewModel> Settings;

	TWeakObjectPtr<AWLPlayerController> BoundPC;
	TWeakObjectPtr<AWLPlayerState> BoundPS;
	TWeakObjectPtr<AWLPlayerCharacter> BoundPawn;

	uint32 OpenPanels = 0;
	bool bPointerOverUI = false;
	bool bPointerOverUILastFrame = false;
	bool bInitialized = false;
};
