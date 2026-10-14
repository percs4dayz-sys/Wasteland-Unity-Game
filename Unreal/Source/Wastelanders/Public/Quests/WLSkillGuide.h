#pragma once

#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "Core/WLTypes.h"
#include "WLSkillGuide.generated.h"

/** One unlock line in a skill's guide. */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSkillMilestone
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "SkillGuide")
	int32 Level = 1;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "SkillGuide", meta = (MultiLine = true))
	FText Unlock;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSkillGuideEntry
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "SkillGuide")
	EWLSkill Skill = EWLSkill::Attack;

	/** One-line description under the guide title. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "SkillGuide", meta = (MultiLine = true))
	FText Subtitle;

	/** "How / where to train this" (skills-panel hover tooltip). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "SkillGuide", meta = (MultiLine = true))
	FText TrainedBy;

	/** Ascending by level. Gathering-tool unlocks are appended at runtime from the item table. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "SkillGuide")
	TArray<FWLSkillMilestone> Milestones;
};

/** A row as the guide popup shows it (UI/SkillGuideUI.cs AddRow). */
USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSkillGuideRow
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadOnly, Category = "SkillGuide")
	int32 Level = 1;

	UPROPERTY(BlueprintReadOnly, Category = "SkillGuide")
	FText Unlock;

	/** Highlighted (unlocked) vs dimmed with "(locked)". */
	UPROPERTY(BlueprintReadOnly, Category = "SkillGuide")
	bool bUnlocked = false;
};

/**
 * The OSRS-style skill guide content (Skills/SkillGuide.cs): what each level of each skill
 * unlocks. Unity's text is the C++ default of this asset, so the guide works with no asset at all
 * (GetDefault<UWLSkillGuideAsset>()); designers can make a data asset of this class and edit it.
 *
 * SkillGuideUI.cs logic is BuildRows: milestones + gathering-tool unlocks, sorted, marked unlocked
 * when the player's level reaches them. The widget itself belongs to the UI layer.
 */
UCLASS(BlueprintType)
class WASTELANDERS_API UWLSkillGuideAsset : public UPrimaryDataAsset
{
	GENERATED_BODY()

public:
	UWLSkillGuideAsset();

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "SkillGuide")
	TArray<FWLSkillGuideEntry> Entries;

	/** Append the upgraded gathering tools (ids 700-708) to the matching skills, as Unity's For() did. */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "SkillGuide")
	bool bIncludeGatheringTools = true;

	const FWLSkillGuideEntry* FindEntry(EWLSkill Skill) const;

	/** The guide to use when none is assigned: the class default (Unity's content). */
	static const UWLSkillGuideAsset* GetDefaultGuide() { return GetDefault<UWLSkillGuideAsset>(); }

	UFUNCTION(BlueprintPure, Category = "Wastelanders|SkillGuide")
	FText GetSubtitle(EWLSkill Skill) const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|SkillGuide")
	FText GetTrainedBy(EWLSkill Skill) const;

	/** Milestones for a skill including tool unlocks, ascending by level. WorldContext resolves item names (may be null). */
	TArray<FWLSkillMilestone> GetMilestones(const UObject* WorldContext, EWLSkill Skill) const;

	/** Rows for the guide popup at the player's current level. Guide null = default guide. */
	static TArray<FWLSkillGuideRow> BuildRows(const UObject* WorldContext, EWLSkill Skill, int32 CurrentLevel, const UWLSkillGuideAsset* Guide = nullptr);

	/** Popup title: "<Skill>  -  Level N/99" (Endurance shows /220). */
	static FText MakeTitle(const UObject* WorldContext, EWLSkill Skill, int32 CurrentLevel);

	/** Upgraded gathering tools, Items/GatheringTools.cs: ids 700-708 = {Steel, Alloy, Titanium} x {Pickaxe, Hatchet, Fishing Rod}. */
	static constexpr int32 FirstToolId = 700;
	static constexpr int32 LastToolId = 708;
	static int32 ToolRank(int32 ItemId) { return (ItemId - FirstToolId) % 3 + 1; }
	static int32 ToolRequiredLevel(int32 ItemId) { return ToolRank(ItemId) * 20; }
	static EWLSkill ToolSkill(int32 ItemId);
	static int32 ToolSpeedBonusPercent(int32 ItemId) { const int32 R = ToolRank(ItemId); return R == 1 ? 15 : R == 2 ? 30 : 50; }

private:
	void AddEntry(EWLSkill Skill, const FText& Subtitle, const FText& TrainedBy, std::initializer_list<TPair<int32, FText>> Milestones);
};
