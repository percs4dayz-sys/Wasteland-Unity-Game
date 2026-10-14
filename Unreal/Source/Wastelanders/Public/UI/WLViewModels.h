#pragma once

#include "CoreMinimal.h"
#include "MVVMViewModelBase.h"
#include "UI/WLUITypes.h"
#include "WLViewModels.generated.h"

class AActor;
class AWLPlayerController;
class AWLPlayerState;
class AWLPlayerCharacter;
class UWLUISubsystem;
class UWLSkillsComponent;
struct FWLDialogueView;

/**
 * MVVM view models for the in-world UI (ModelViewViewModel plugin). Each one binds to the component
 * delegates of the LOCAL player's PlayerState / pawn / controller (see the contract table in
 * Docs/PortingConventions.md) and exposes FieldNotify properties that UMG widgets bind to, plus
 * BlueprintCallable commands that send intent to the server through the owning components' RPCs.
 *
 * They replace the "find PlayerEntity.Instance, then poll" pattern of the Unity UI scripts. The asset-free
 * AWLDebugHUD draws from the same view models, so the debug UI and the future UMG UI never disagree.
 *
 * Every property is also broadcast through the native OnChanged delegate after a refresh, for C++
 * consumers that don't use field notifications (the debug HUD, CommonUI base widgets).
 */
UCLASS(Abstract)
class WASTELANDERS_API UWLViewModelBase : public UMVVMViewModelBase
{
	GENERATED_BODY()

public:
	/** (Re)bind to the local player's objects. Any of them may be null (no pawn while dead, etc.). */
	void BindContext(UWLUISubsystem* InUI, AWLPlayerController* InPC, AWLPlayerState* InPS, AWLPlayerCharacter* InPawn);

	/** Drop every delegate binding. */
	void Unbind();

	/** Per-frame update for values that change continuously (energy regen, cooldowns, fades). */
	virtual void TickViewModel(float DeltaSeconds) {}

	/** Fires after the view model changed anything. */
	FSimpleMulticastDelegate OnChanged;

	AWLPlayerController* GetPC() const { return PC.Get(); }
	AWLPlayerState* GetPS() const { return PS.Get(); }
	AWLPlayerCharacter* GetPawn() const { return Pawn.Get(); }

protected:
	virtual void OnBind() {}
	virtual void OnUnbind() {}

	void NotifyChanged() { OnChanged.Broadcast(); }

	TWeakObjectPtr<UWLUISubsystem> UI;
	TWeakObjectPtr<AWLPlayerController> PC;
	TWeakObjectPtr<AWLPlayerState> PS;
	TWeakObjectPtr<AWLPlayerCharacter> Pawn;
};

// -------------------------------------------------------------------------------------------------
// HUD (Unity PlayerHudPanel + HUDController HP read-out)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLHudViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	virtual void TickViewModel(float DeltaSeconds) override;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") FText PlayerName;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") int32 CombatLevel = 3;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") int32 Health = 0;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") int32 MaxHealth = 0;
	/** Value turns red at <= 30% (Unity PlayerHudPanel). */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") bool bHealthLow = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") bool bDead = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") float SpecialEnergy = 0.f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") float MaxSpecialEnergy = 100.f;
	/** Number only; Unity dropped the fill because it reads as a glitch for most of the game. */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") int32 TotalLevel = 0;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") bool bTrackingSkill = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") FText TrackedSkillName;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") int32 TrackedSkillLevel = 1;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") float TrackedSkillProgress = 0.f;

	/** Beast task row (only while one is active; red caption = beast not summoned). */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") bool bShowBeastTask = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") FText BeastTaskLabel;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") FText BeastTaskValue;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") float BeastTaskFill = 0.f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") bool bBeastTaskAlert = false;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") FText ObjectiveText;

	/** Run / dodge (client-predicted movement). */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") bool bRunning = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") float DodgeCooldownRemaining = 0.f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") float DodgeCooldown = 5.f;

	/** Quest banner (title, subtitle) and seconds left to show it. */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") FText BannerTitle;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") FText BannerSubtitle;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "HUD") float BannerTimeLeft = 0.f;

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;

private:
	void HandleBanner(const FText& Title, const FText& Subtitle, float Seconds);
};

// -------------------------------------------------------------------------------------------------
// Inventory (Unity InventoryPanelUI + InventorySlotUI)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLInventoryViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	/** Base slots plus any non-empty saddlebag overflow slots. */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Inventory") TArray<FWLItemSlotView> Slots;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Inventory") int32 Capacity = 28;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Inventory") int32 FreeSlots = 28;
	/** OSRS mobile Tap-to-drop (and desktop shift-click): left click drops. The panel turns red. */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Inventory") bool bDropMode = false;

	/** Left click: default action (Deposit 1 while banking, else Equip / Eat / Use / Bury / Read), or Drop in drop mode. */
	UFUNCTION(BlueprintCallable, Category = "Inventory") void ActivateSlot(int32 SlotIndex);
	/** Context-menu verb (Equip, Use, Eat, Bury, Read, Drop, Deposit, Examine). */
	UFUNCTION(BlueprintCallable, Category = "Inventory") void PerformVerb(int32 SlotIndex, FName Verb);
	/** Drag and drop swap. */
	UFUNCTION(BlueprintCallable, Category = "Inventory") void MoveSlot(int32 From, int32 To);
	UFUNCTION(BlueprintCallable, Category = "Inventory") void SetDropMode(bool bOn);
	/** Menu rows for a slot (verbs + Examine + Cancel). Empty for an empty slot. */
	UFUNCTION(BlueprintCallable, Category = "Inventory") TArray<FWLMenuEntry> GetMenuForSlot(int32 SlotIndex) const;
	UFUNCTION(BlueprintPure, Category = "Inventory") FText GetSlotTitle(int32 SlotIndex) const;

	void Refresh();

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;

private:
	void Examine(int32 ItemId) const;
};

// -------------------------------------------------------------------------------------------------
// Equipment (Unity EquipmentPanelUI paper doll)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLEquipmentViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Equipment") TArray<FWLEquipSlotView> Slots;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Equipment") int32 AttackBonus = 0;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Equipment") int32 StrengthBonus = 0;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Equipment") int32 DefenceBonus = 0;

	/** Click a slot to unequip. */
	UFUNCTION(BlueprintCallable, Category = "Equipment") void Unequip(EWLEquipSlot Slot);
	/** Socket (ModuleItemId) or clear (0) a module socket. */
	UFUNCTION(BlueprintCallable, Category = "Equipment") void SocketModule(EWLEquipSlot Slot, int32 SocketIndex, int32 ModuleItemId);

	void Refresh();

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;
};

// -------------------------------------------------------------------------------------------------
// Bank (Unity BankUI)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLBankViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	virtual void TickViewModel(float DeltaSeconds) override;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Bank") bool bOpen = false;
	/** Non-empty bank slots only (Unity BankUI skips empties); SlotIndex is the real bank slot. */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Bank") TArray<FWLItemSlotView> Items;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Bank") int32 UsedSlots = 0;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Bank") int32 Capacity = 300;

	/** Click a bank item: withdraw 1 (Unity). Qty <= 0 = all. */
	UFUNCTION(BlueprintCallable, Category = "Bank") void Withdraw(int32 BankSlot, int32 Quantity = 1);
	/** Click an inventory item in the bank window: deposit the whole stack (Unity). */
	UFUNCTION(BlueprintCallable, Category = "Bank") void Deposit(int32 InventorySlot, int32 Quantity = 0);
	UFUNCTION(BlueprintCallable, Category = "Bank") void DepositAll();
	UFUNCTION(BlueprintCallable, Category = "Bank") void Close();

	void Refresh();

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;

private:
	void HandleOpened();
	void HandleClosed();

	FVector OpenedAt = FVector::ZeroVector;
};

// -------------------------------------------------------------------------------------------------
// Crafting (Unity CraftingUI)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLCraftingViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Crafting") bool bOpen = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Crafting") EWLCraftingStation Station = EWLCraftingStation::Workbench;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Crafting") FText StationName;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Crafting") TArray<FWLRecipeView> Recipes;
	/** Index into Recipes, INDEX_NONE = "Select an item to make". */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Crafting") int32 SelectedIndex = INDEX_NONE;
	/** 1, 5, 10 or -1 for "All". */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Crafting") int32 Quantity = 1;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Crafting") bool bBusy = false;

	UFUNCTION(BlueprintCallable, Category = "Crafting") void Select(int32 RecipeIndex);
	UFUNCTION(BlueprintCallable, Category = "Crafting") void SetQuantity(int32 NewQuantity);
	UFUNCTION(BlueprintCallable, Category = "Crafting") void Craft();
	/** Closing the window does NOT stop a running batch (Unity); it closes the station session. */
	UFUNCTION(BlueprintCallable, Category = "Crafting") void Close();
	UFUNCTION(BlueprintPure, Category = "Crafting") FText GetCraftButtonLabel() const;

	static const TArray<int32>& QuantityOptions();

	void Refresh();

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;

private:
	void HandleStationChanged(bool bInOpen, EWLCraftingStation InStation);
	void HandleCraftedOne(FName RecipeRowName);
	void BindInventorySide();
};

// -------------------------------------------------------------------------------------------------
// Skills (Unity SkillsPanelUI + SkillEntryUI)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLSkillsViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Skills") TArray<FWLSkillView> Skills;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Skills") int32 TotalLevel = 0;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Skills") int32 CombatLevel = 3;

	/** Right click: pin / unpin a skill on the HUD tracked-XP bar (persisted). */
	UFUNCTION(BlueprintCallable, Category = "Skills") void ToggleTracked(EWLSkill Skill);
	/** Hover tooltip: "XP: 1,234 / Next level in: 56". */
	UFUNCTION(BlueprintPure, Category = "Skills") FText GetTooltip(EWLSkill Skill) const;

	static FText SkillDisplayName(const UObject* WorldContext, EWLSkill Skill);

	void Refresh();

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;

private:
	void HandleSkillsChanged(UWLSkillsComponent* Component) { Refresh(); }
};

// -------------------------------------------------------------------------------------------------
// XP drops + level-up celebration (Unity XPDropUI)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLXPDropViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	virtual void TickViewModel(float DeltaSeconds) override;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "XP") TArray<FWLXPDropView> Drops;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "XP") FText CelebrationText;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "XP") float CelebrationTimeLeft = 0.f;

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;

private:
	void HandleXPGained(EWLSkill Skill, int32 Amount);
	void HandleLevelUp(EWLSkill Skill, int32 NewLevel);
};

// -------------------------------------------------------------------------------------------------
// Combat style + specials (Unity CombatStyleUI + SpecialAttackHUD)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLCombatStyleViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	virtual void TickViewModel(float DeltaSeconds) override;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") EWLCombatStyle Style = EWLCombatStyle::Melee;
	/** "MELEE STANCES" / "MARKSMANSHIP STANCES" / "FISSION STANCES". */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") FText StyleHeading;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") TArray<FWLStanceView> Stances;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") bool bHasWeaponSpecial = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") FText SpecialName;
	/** Empty = ready. */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") FText SpecialBlockReason;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") float SpecialEnergy = 0.f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") float MaxSpecialEnergy = 100.f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") float SpecialCost = 50.f;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") bool bUsesMagazine = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") int32 MagazineLoaded = 0;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") int32 MagazineSize = 0;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") bool bReloading = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Combat") float ReloadProgress = 0.f;

	UFUNCTION(BlueprintCallable, Category = "Combat") void SetStance(EWLCombatStance Stance);
	UFUNCTION(BlueprintCallable, Category = "Combat") void FireSpecial();
	UFUNCTION(BlueprintCallable, Category = "Combat") void Reload();

	static FText HeadingFor(EWLCombatStyle Style);

	void Refresh();

private:
	EWLCombatStyle BuiltStyle = EWLCombatStyle::Melee;
	EWLCombatStance LastStance = EWLCombatStance::Accurate;
	bool bBuilt = false;
};

// -------------------------------------------------------------------------------------------------
// Chat log (Unity HUDController chat + Hud3DChat + ChatInput)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLChatViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Chat") TArray<FText> Lines;
	/** Single source of truth for "the player is typing" (Unity ChatInput.IsTyping). */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Chat") bool bTyping = false;

	/** "/" commands go to the server's command handler, everything else is local chat. */
	UFUNCTION(BlueprintCallable, Category = "Chat") void Submit(const FString& Text);
	/** Client-only line (examine text, level-up shout, UI feedback). */
	UFUNCTION(BlueprintCallable, Category = "Chat") void AddLocalLine(const FText& Line);
	UFUNCTION(BlueprintCallable, Category = "Chat") void SetTyping(bool bInTyping);

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;

	UFUNCTION()
	void HandleGameMessage(const FText& Message);
};

// -------------------------------------------------------------------------------------------------
// Dialogue (Unity DialogueUI presentation; state lives in the quests system's UWLDialogueComponent)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLDialogueViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	virtual void TickViewModel(float DeltaSeconds) override;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Dialogue") bool bActive = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Dialogue") FText Speaker;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Dialogue") FText Line;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Dialogue") TArray<FText> Choices;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Dialogue") bool bHasMorePages = false;
	/** "Click here to continue" (Unity said "Tap" on phones). */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Dialogue") FText ContinuePrompt;
	/** The starter-beast picker requested by dialogue ("EggChoice"). */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Dialogue") bool bEggChoiceOpen = false;

	/** Space / click the box. Ignored while typing in chat. */
	UFUNCTION(BlueprintCallable, Category = "Dialogue") void Continue();
	UFUNCTION(BlueprintCallable, Category = "Dialogue") void Choose(int32 ChoiceIndex);
	UFUNCTION(BlueprintCallable, Category = "Dialogue") void End();
	/** "wolf", "turtle" or "bird". */
	UFUNCTION(BlueprintCallable, Category = "Dialogue") void ChooseStarterBeast(FName Species);
	UFUNCTION(BlueprintCallable, Category = "Dialogue") void CloseEggChoice();

	/** Other dialogue UI requests ("BankGuide", "Shop") for widgets that implement them. */
	DECLARE_MULTICAST_DELEGATE_OneParam(FWLOnDialogueUIRequestForward, FName);
	FWLOnDialogueUIRequestForward OnUIRequest;

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;

private:
	bool TryBindDialogue();
	void HandleUpdated(const FWLDialogueView& View);
	void HandleEnded();
	void HandleUIRequest(FName Request);

	bool bDialogueBound = false;
};

// -------------------------------------------------------------------------------------------------
// Context menu (Unity ContextMenuUI) - world objects and inventory items
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLContextMenuViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Menu") bool bOpen = false;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Menu") FText Title;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Menu") TArray<FWLMenuEntry> Entries;
	/** Viewport pixels where the menu was requested (the widget places it with FWLUILogic::PlaceMenu). */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Menu") FVector2D ScreenPosition = FVector2D::ZeroVector;

	UFUNCTION(BlueprintCallable, Category = "Menu") void OpenForActor(AActor* Target, FVector2D InScreenPosition);
	UFUNCTION(BlueprintCallable, Category = "Menu") void OpenForInventorySlot(int32 SlotIndex, FVector2D InScreenPosition);
	UFUNCTION(BlueprintCallable, Category = "Menu") void Choose(int32 EntryIndex);
	UFUNCTION(BlueprintCallable, Category = "Menu") void Close();

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;

	UFUNCTION()
	void HandleContextMenuRequested(AActor* Target, FVector2D InScreenPosition);

private:
	TWeakObjectPtr<AActor> TargetActor;
	int32 TargetSlot = INDEX_NONE;
};

// -------------------------------------------------------------------------------------------------
// Minimap (Unity MinimapHUD markers; rendering is the widget's job)
// -------------------------------------------------------------------------------------------------

UCLASS(BlueprintType)
class WASTELANDERS_API UWLMinimapViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	virtual void TickViewModel(float DeltaSeconds) override;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Minimap") TArray<FWLMinimapMarker> Markers;
	/** World units from centre to rim. */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Minimap") float Radius = 4000.f;
	/** Map "up" = camera facing (turns only with the view, never with the character, like OSRS). */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Minimap") float CameraYaw = 0.f;
	/** Centre arrow: which way the character faces, relative to map up. */
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Minimap") float PlayerHeadingOnMap = 0.f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Minimap") FVector PlayerLocation = FVector::ZeroVector;

	/** Tap / click the map (MapPos in -1..1, Y up): walk there; the destination flag shows it. */
	UFUNCTION(BlueprintCallable, Category = "Minimap") void WalkToMapPoint(FVector2D MapPos);
	/** Compass click: face north. */
	UFUNCTION(BlueprintCallable, Category = "Minimap") void FaceNorth() { LookToward(0.f); }
	/** Compass hold / right-click: Look North / East / South / West (0 / 90 / 180 / 270). */
	UFUNCTION(BlueprintCallable, Category = "Minimap") void LookToward(float WorldYaw);

	/** Seconds between scans for creatures / NPCs / nodes (Unity: every couple of seconds). */
	float ScanInterval = 2.f;

private:
	void ScanWorld();
	void Rebuild();

	struct FTracked
	{
		TWeakObjectPtr<AActor> Actor;
		EWLMinimapMarkerKind Kind;
		EWLSkill Skill;
	};
	TArray<FTracked> Tracked;
	float ScanTimer = 0.f;
	float GroundItemTimer = 0.f;
};

// -------------------------------------------------------------------------------------------------
// Settings + music (Unity SettingsUI + MusicPanelUI)
// -------------------------------------------------------------------------------------------------

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLMusicTrackView
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "Music") int32 Index = 0;
	UPROPERTY(BlueprintReadOnly, Category = "Music") FText Name;
	UPROPERTY(BlueprintReadOnly, Category = "Music") bool bEnabled = true;
	UPROPERTY(BlueprintReadOnly, Category = "Music") bool bPlaying = false;
};

UCLASS(BlueprintType)
class WASTELANDERS_API UWLSettingsViewModel : public UWLViewModelBase
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Settings") float MasterVolume = 1.f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Settings") float MusicVolume = 0.5f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Settings") float EffectsVolume = 1.f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Settings") float InterfaceScale = 1.f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Settings") float CameraOrbitSpeed = 1.f;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Settings") int32 LongPressMs = 500;

	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Music") TArray<FWLMusicTrackView> Tracks;
	UPROPERTY(BlueprintReadOnly, FieldNotify, Category = "Music") FText NowPlaying;

	UFUNCTION(BlueprintCallable, Category = "Settings") void SetMasterVolume(float V);
	UFUNCTION(BlueprintCallable, Category = "Settings") void SetMusicVolume(float V);
	UFUNCTION(BlueprintCallable, Category = "Settings") void SetEffectsVolume(float V);
	/** Applied on release (Unity ApplyOnRelease) so the layout doesn't jump while dragging. */
	UFUNCTION(BlueprintCallable, Category = "Settings") void SetInterfaceScale(float V);
	UFUNCTION(BlueprintCallable, Category = "Settings") void SetCameraOrbitSpeed(float V);
	UFUNCTION(BlueprintCallable, Category = "Settings") void SetLongPressMs(int32 Ms);

	UFUNCTION(BlueprintCallable, Category = "Music") void PlayTrack(int32 Index);
	UFUNCTION(BlueprintCallable, Category = "Music") void ToggleTrack(int32 Index);
	UFUNCTION(BlueprintCallable, Category = "Music") void SkipTrack();

	void Refresh();
	void RefreshMusic();

	/** Push camera speed to the local player controller. */
	void ApplyCameraSettings() const;

protected:
	virtual void OnBind() override;
	virtual void OnUnbind() override;

private:
	void Save() const;
	FDelegateHandle MusicHandle;
	TWeakObjectPtr<class UWLMusicSubsystem> BoundMusic;
	float BaseKeyOrbitSpeed = -1.f;
	float BaseMouseOrbitSpeed = -1.f;
};
