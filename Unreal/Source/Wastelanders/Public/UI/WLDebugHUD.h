#pragma once

#include "CoreMinimal.h"
#include "UI/WLCanvasHUDBase.h"
#include "Types/SlateEnums.h"
#include "WLDebugHUD.generated.h"

class UWLUISubsystem;
class SWidget;
class SEditableTextBox;

/**
 * Fully working, asset-free in-game UI drawn on the Canvas with the engine's small font. It reads the
 * same view models the future UMG / CommonUI widgets bind to (UWLUISubsystem) and sends every action
 * through them, so the server-authoritative RPC path is exercised exactly as the real UI will.
 *
 * Ports the presentation of the Unity UI scripts in Assets/Scripts/UI: PlayerHudPanel / HUDController
 * (HP, special, totals, tracked skill, beast rows, objective), HudTabBar, InventoryPanelUI /
 * InventorySlotUI, EquipmentPanelUI, SkillsPanelUI, CombatStyleUI / SpecialAttackHUD, BankUI, CraftingUI,
 * XPDropUI, MinimapHUD, ContextMenuUI, TooltipUI, ChatInput / Hud3DChat, DialogueUI (+ the egg picker),
 * SettingsUI, MusicPanelUI, WastelandMenuUI, BuildInfoPanelUI, DevPanel (non-shipping), OverheadNameplate,
 * ClickCross and SplashScreen.
 *
 * Keys: I inventory, K skills, F7 combat style, F9 equipment, O settings, F10 system menu, F8 dev panel,
 * Escape closes the top-most thing, Enter opens the chat line, Space continues dialogue, 1-9 pick a
 * dialogue choice. Shift + click drops an item. Mouse wheel scrolls the crafting, bank and dev lists.
 */
UCLASS()
class WASTELANDERS_API AWLDebugHUD : public AWLCanvasHUDBase
{
	GENERATED_BODY()

public:
	virtual void DrawHUD() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

protected:
	virtual void DrawCanvas() override;
	virtual bool OnUnhandledClick(bool bRightButton) override;

private:
	void HandleHotkeys(UWLUISubsystem& UI);

	void DrawNameplates();
	void DrawClickCrosses(float DeltaSeconds);
	void DrawHudFrame(UWLUISubsystem& UI);
	void DrawMinimap(UWLUISubsystem& UI, float X, float Y, float Size);
	float DrawTabBar(UWLUISubsystem& UI, float Right, float Y);
	void DrawSidePanels(UWLUISubsystem& UI, float Right, float Top);
	void DrawChat(UWLUISubsystem& UI);
	void DrawXPDrops(UWLUISubsystem& UI);
	void DrawBanner(UWLUISubsystem& UI);
	void DrawCrafting(UWLUISubsystem& UI);
	void DrawBank(UWLUISubsystem& UI);
	void DrawDialogue(UWLUISubsystem& UI);
	void DrawEggChoice(UWLUISubsystem& UI);
	void DrawContextMenu(UWLUISubsystem& UI);
	void DrawSplash(float DeltaSeconds);

	// Side panels: each returns its height for a given width when bMeasureOnly.
	float PanelInventory(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly);
	float PanelCombatStyle(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly);
	float PanelSkills(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly);
	float PanelEquipment(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly);
	float PanelMusic(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly);
	float PanelBuild(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly);
	float PanelSettings(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly);
	float PanelSystemMenu(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly);
	float PanelDev(UWLUISubsystem& UI, float X, float Y, float W, bool bMeasureOnly);

	/** "-  label value  +" row used by the settings panel. */
	void StepperRow(float X, float Y, float W, const FString& Caption, const FString& Value, float Fraction,
		TFunction<void()> OnMinus, TFunction<void()> OnPlus);

	int32 WheelDelta() const;

	// Chat line (Slate text box over the viewport; the only non-canvas piece, needed for text entry).
	void OpenChatLine(UWLUISubsystem& UI);
	void CloseChatLine();
	void HandleChatCommitted(const FText& Text, ETextCommit::Type CommitType);
	TSharedPtr<SWidget> ChatContainer;
	TSharedPtr<SEditableTextBox> ChatBox;
	bool bCloseChatPending = false;

	struct FClickCross
	{
		FVector2D Position;
		float Age = 0.f;
		bool bAction = false;
	};
	TArray<FClickCross> ClickCrosses;

	float SplashAge = -1.f;
	bool bSplashChecked = false;
	bool bSplashFading = false;

	int32 CraftScroll = 0;
	int32 BankScroll = 0;
	int32 DevScroll = 0;
	TArray<int32> DevItemIds;
};
