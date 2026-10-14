#pragma once

#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "Data/WLDataTypes.h"
#include "WLDataSubsystem.generated.h"

class UDataTable;

/**
 * Read-only game data: items, recipes, skills, modules and gear sets, indexed for fast lookup.
 * Replaces Unity's static ItemRegistry / CraftingRecipes / ModuleCatalog / GearSets classes.
 *
 * Loaded once per GameInstance (client and server alike). The tables never change at runtime, so
 * returned row pointers stay valid for the GameInstance's lifetime.
 */
UCLASS()
class WASTELANDERS_API UWLDataSubsystem : public UGameInstanceSubsystem
{
	GENERATED_BODY()

public:
	virtual void Initialize(FSubsystemCollectionBase& Collection) override;
	virtual void Deinitialize() override;

	/** Convenience accessor from any world-context object. May return null very early in boot. */
	static UWLDataSubsystem* Get(const UObject* WorldContextObject);

	/** Item by stable id, or null if no such item exists (removed items in old saves). */
	const FWLItemRow* FindItem(int32 ItemId) const;
	bool ItemExists(int32 ItemId) const { return Items.Contains(ItemId); }
	const TMap<int32, const FWLItemRow*>& GetAllItems() const { return Items; }

	const FWLModuleRow* FindModule(int32 ItemId) const;
	const FWLGearSetRow* FindGearSet(FName SetId) const;
	const FWLSkillRow* FindSkill(EWLSkill Skill) const;

	/** Recipes for one station, sorted by level then name (the order the crafting UI lists them). */
	const TArray<const FWLRecipeRow*>& GetRecipes(EWLCraftingStation Station) const;

	/** Recipe by row name; recipes are replicated / RPC'd by row name. */
	const FWLRecipeRow* FindRecipe(FName RecipeRowName) const;
	FName GetRecipeRowName(const FWLRecipeRow* Recipe) const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Data")
	bool GetItem(int32 ItemId, FWLItemRow& OutItem) const;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Data")
	FText GetItemName(int32 ItemId) const;

private:
	void LoadTables();

	UPROPERTY(Transient)
	TObjectPtr<UDataTable> ItemTable;

	UPROPERTY(Transient)
	TObjectPtr<UDataTable> RecipeTable;

	UPROPERTY(Transient)
	TObjectPtr<UDataTable> SkillTable;

	UPROPERTY(Transient)
	TObjectPtr<UDataTable> ModuleTable;

	UPROPERTY(Transient)
	TObjectPtr<UDataTable> GearSetTable;

	TMap<int32, const FWLItemRow*> Items;
	TMap<int32, const FWLModuleRow*> Modules;
	TMap<FName, const FWLGearSetRow*> GearSets;
	TMap<EWLSkill, const FWLSkillRow*> Skills;
	TMap<FName, const FWLRecipeRow*> RecipesByName;
	TMap<const FWLRecipeRow*, FName> RecipeNames;
	TArray<const FWLRecipeRow*> RecipesByStation[3];
};
