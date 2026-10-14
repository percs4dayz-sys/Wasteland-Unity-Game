#pragma once

#include "CoreMinimal.h"
#include "Core/WLTypes.h"
#include "WLUITypes.generated.h"

struct FWLItemRow;

/** One inventory / bank cell as the UI shows it. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLItemSlotView
{
	GENERATED_BODY()

	/** Index in the source container (inventory slot or bank slot). */
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 SlotIndex = INDEX_NONE;
	/** 0 = empty. */
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 ItemId = 0;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 Quantity = 0;
	UPROPERTY(BlueprintReadOnly, Category = "UI") FText DisplayName;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 ModuleCount = 0;
	/** Saddlebag overflow slot (past base capacity). */
	UPROPERTY(BlueprintReadOnly, Category = "UI") bool bOverflow = false;

	bool IsEmpty() const { return ItemId <= 0; }
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLEquipSlotView
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "UI") EWLEquipSlot Slot = EWLEquipSlot::Weapon;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 ItemId = 0;
	/** Item name, " xN" appended for ammo (Unity EquipmentPanelUI). Empty when nothing equipped. */
	UPROPERTY(BlueprintReadOnly, Category = "UI") FText DisplayName;
	UPROPERTY(BlueprintReadOnly, Category = "UI") FText SlotLabel;
	UPROPERTY(BlueprintReadOnly, Category = "UI") TArray<int32> Modules;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSkillView
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "UI") EWLSkill Skill = EWLSkill::Attack;
	UPROPERTY(BlueprintReadOnly, Category = "UI") FText DisplayName;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 Level = 1;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 XP = 0;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 XPToNext = 0;
	UPROPERTY(BlueprintReadOnly, Category = "UI") float Progress = 0.f;
	UPROPERTY(BlueprintReadOnly, Category = "UI") bool bTracked = false;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLRecipeView
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "UI") FName RecipeRowName;
	/** Output item name with " xN" when the recipe makes more than one. */
	UPROPERTY(BlueprintReadOnly, Category = "UI") FText DisplayName;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 OutputItemId = 0;
	UPROPERTY(BlueprintReadOnly, Category = "UI") EWLSkill Skill = EWLSkill::Smithing;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 LevelRequired = 1;
	UPROPERTY(BlueprintReadOnly, Category = "UI") bool bLevelMet = false;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 XPEach = 0;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 MaxCraftable = 0;
	UPROPERTY(BlueprintReadOnly, Category = "UI") bool bCanMake = false;
	/** Why not (missing flag, level, materials); empty when makeable. */
	UPROPERTY(BlueprintReadOnly, Category = "UI") FText Reason;
	/** "Scrap Metal: 3/5" per input, with bInputMet in the parallel array for green/red. */
	UPROPERTY(BlueprintReadOnly, Category = "UI") TArray<FText> InputLines;
	UPROPERTY(BlueprintReadOnly, Category = "UI") TArray<bool> InputMet;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLStanceView
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "UI") EWLCombatStance Stance = EWLCombatStance::Accurate;
	UPROPERTY(BlueprintReadOnly, Category = "UI") FText Label;
	/** "better accuracy · trains Attack". */
	UPROPERTY(BlueprintReadOnly, Category = "UI") FText Description;
	UPROPERTY(BlueprintReadOnly, Category = "UI") bool bActive = false;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLXPDropView
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "UI") EWLSkill Skill = EWLSkill::Attack;
	UPROPERTY(BlueprintReadOnly, Category = "UI") int32 Amount = 0;
	UPROPERTY(BlueprintReadOnly, Category = "UI") FText Text;
	/** Seconds alive; the drop fades over FWLUILogic::XPDropLifetime. */
	UPROPERTY(BlueprintReadOnly, Category = "UI") float Age = 0.f;
	/** Pixels above the anchor (rises, and older drops are pushed up when a new one arrives). */
	UPROPERTY(BlueprintReadOnly, Category = "UI") float OffsetY = 0.f;
};

/** One right-click menu row. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLMenuEntry
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "UI") FText Label;
	/** Verb sent to the server (interactable verb or inventory verb). "Examine" / "Cancel" are local. */
	UPROPERTY(BlueprintReadOnly, Category = "UI") FName Verb;
};

UENUM(BlueprintType)
enum class EWLMinimapMarkerKind : uint8
{
	GroundItem,  // red dot (OSRS)
	Creature,    // yellow dot
	Npc,         // yellow dot
	Player,      // white dot
	GatherNode,  // small square, coloured by skill
	Objective,   // pulsing amber dot
	Destination  // red flag
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLMinimapMarker
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "UI") EWLMinimapMarkerKind Kind = EWLMinimapMarkerKind::Npc;
	UPROPERTY(BlueprintReadOnly, Category = "UI") FVector WorldLocation = FVector::ZeroVector;
	/**
	 * Position on the map in -1..1 (X right, Y up), already rotated so "up" is the camera's facing, like
	 * OSRS. Markers past the edge are clamped to the rim when bClampToRim (ground items), else hidden.
	 */
	UPROPERTY(BlueprintReadOnly, Category = "UI") FVector2D MapPosition = FVector2D::ZeroVector;
	UPROPERTY(BlueprintReadOnly, Category = "UI") bool bOnRim = false;
	/** Gather nodes: which skill (colour). */
	UPROPERTY(BlueprintReadOnly, Category = "UI") EWLSkill Skill = EWLSkill::Woodcutting;
};

/** Item verbs the inventory UI offers (Unity InventorySlotUI context menu). Server re-validates all. */
namespace WLItemVerbs
{
	WASTELANDERS_API extern const FName Equip;
	WASTELANDERS_API extern const FName Use;
	WASTELANDERS_API extern const FName Eat;
	WASTELANDERS_API extern const FName Bury;
	WASTELANDERS_API extern const FName Read;
	WASTELANDERS_API extern const FName Drop;
	WASTELANDERS_API extern const FName Deposit;
	WASTELANDERS_API extern const FName Examine;
	WASTELANDERS_API extern const FName Cancel;
}

/**
 * Pure presentation rules shared by the view models and the debug HUD. World-free so they are unit
 * tested (Private/Tests/UI). Each cites the Unity behaviour it ports.
 */
struct WASTELANDERS_API FWLUILogic
{
	/** Unity XPDropUI: a drop lives 1.6 s and rises 60 px over its life; a new drop pushes older ones up 22 px. */
	static constexpr float XPDropLifetime = 1.6f;
	static constexpr float XPDropRise = 60.f;
	static constexpr float XPDropStackPush = 22.f;
	/** Unity XPDropUI level-up announcement duration. */
	static constexpr float CelebrationSeconds = 4.f;
	/** Unity HUDController.MAX_CHAT_LINES. */
	static constexpr int32 MaxChatLines = 100;
	/** Unity BankUI.AutoCloseDistance (2 m). */
	static constexpr float BankAutoCloseDistance = 200.f;
	/** Unity PlayerHudPanel: HP value turns red at or below 30%. */
	static constexpr float LowHealthFraction = 0.3f;

	/**
	 * Context-menu verbs for an item, in Unity's order: Deposit (bank open), Equip | Use (consumable) |
	 * Bury | Read, then Drop. "Examine" and "Cancel" are appended by the menu itself.
	 */
	static TArray<FName> ItemVerbs(const FWLItemRow& Item, bool bBankOpen);

	/** Left-click default (Unity InventorySlotUI.DoDefault): Deposit when banking, else Equip/Use/Bury/Read. None = "You can't use this item." */
	static FName DefaultItemVerb(const FWLItemRow& Item, bool bBankOpen, bool bIsModule);

	static FText VerbLabel(FName Verb);

	/** Ages drops by DeltaSeconds (rising as they go) and removes expired ones. */
	static void TickXPDrops(TArray<FWLXPDropView>& Drops, float DeltaSeconds);

	/** Adds a drop at the anchor, pushing existing drops up. Ignores Amount <= 0. */
	static void PushXPDrop(TArray<FWLXPDropView>& Drops, EWLSkill Skill, int32 Amount, const FText& SkillName);

	/** Opacity of a drop of the given age. */
	static float XPDropAlpha(float Age) { return FMath::Clamp(1.f - Age / XPDropLifetime, 0.f, 1.f); }

	/** Append and trim to MaxChatLines (oldest dropped). */
	static void AppendChatLine(TArray<FText>& Lines, const FText& Line, int32 MaxLines = MaxChatLines);

	/**
	 * Unity ContextMenuUI.Open: the menu grows down-right from the cursor, flips left / up when it would
	 * spill off the right / bottom edge, and never hangs off the left / top. Screen space, Y down.
	 */
	static FVector2D PlaceMenu(const FVector2D& Cursor, const FVector2D& MenuSize, const FVector2D& ViewportSize);

	/** Rotate a world offset into map space (-1..1, up = camera facing) for a map showing Radius units. */
	static FVector2D WorldToMap(const FVector& Offset, float CameraYawDegrees, float Radius, bool bClampToRim, bool& bOutOnRim, bool& bOutVisible);

	/** Inverse of WorldToMap (tap on the map -> world offset, Z = 0). */
	static FVector MapToWorld(const FVector2D& MapPos, float CameraYawDegrees, float Radius);

	/** Unity CraftingUI quantity button label: -1 = "Make All (N)", else "Make xN". */
	static FText CraftButtonLabel(int32 Quantity, int32 MaxCraftable);
};
