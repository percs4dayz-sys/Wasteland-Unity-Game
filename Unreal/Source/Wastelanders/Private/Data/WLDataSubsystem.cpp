#include "Data/WLDataSubsystem.h"
#include "Core/WLGameSettings.h"
#include "Engine/DataTable.h"
#include "Engine/GameInstance.h"
#include "Engine/World.h"
#include "Wastelanders.h"

void UWLDataSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);
	LoadTables();
}

void UWLDataSubsystem::Deinitialize()
{
	Items.Reset();
	Modules.Reset();
	GearSets.Reset();
	Skills.Reset();
	RecipesByName.Reset();
	RecipeNames.Reset();
	for (TArray<const FWLRecipeRow*>& List : RecipesByStation)
	{
		List.Reset();
	}
	Super::Deinitialize();
}

UWLDataSubsystem* UWLDataSubsystem::Get(const UObject* WorldContextObject)
{
	const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
	const UGameInstance* GI = World ? World->GetGameInstance() : nullptr;
	return GI ? GI->GetSubsystem<UWLDataSubsystem>() : nullptr;
}

template <typename RowType>
static void ForEachRow(UDataTable* Table, TFunctionRef<void(FName, const RowType&)> Fn)
{
	if (!Table)
	{
		return;
	}
	if (Table->GetRowStruct() != RowType::StaticStruct())
	{
		UE_LOG(LogWastelanders, Error, TEXT("DataTable %s has row struct %s, expected %s"),
			*Table->GetName(), *GetNameSafe(Table->GetRowStruct()), *RowType::StaticStruct()->GetName());
		return;
	}
	for (const TPair<FName, uint8*>& Pair : Table->GetRowMap())
	{
		Fn(Pair.Key, *reinterpret_cast<const RowType*>(Pair.Value));
	}
}

void UWLDataSubsystem::LoadTables()
{
	const UWLGameSettings* Settings = UWLGameSettings::Get();
	ItemTable = Settings->ItemTable.LoadSynchronous();
	RecipeTable = Settings->RecipeTable.LoadSynchronous();
	SkillTable = Settings->SkillTable.LoadSynchronous();
	ModuleTable = Settings->ModuleTable.LoadSynchronous();
	GearSetTable = Settings->GearSetTable.LoadSynchronous();

	if (!ItemTable)
	{
		UE_LOG(LogWastelanders, Error, TEXT("No item DataTable configured (Project Settings > Game > Wastelanders). Import Content/Data/DT_Items.json."));
	}

	ForEachRow<FWLItemRow>(ItemTable, [this](FName, const FWLItemRow& Row)
	{
		// Ids must be unique: a clash used to overwrite silently in Unity (see ItemRegistry.Add).
		if (const FWLItemRow* const* Existing = Items.Find(Row.ItemId))
		{
			UE_LOG(LogWastelanders, Error, TEXT("DUPLICATE item id %d: '%s' overwrites '%s'"),
				Row.ItemId, *Row.DisplayName.ToString(), *(*Existing)->DisplayName.ToString());
		}
		Items.Add(Row.ItemId, &Row);
	});

	ForEachRow<FWLModuleRow>(ModuleTable, [this](FName, const FWLModuleRow& Row) { Modules.Add(Row.ItemId, &Row); });
	ForEachRow<FWLGearSetRow>(GearSetTable, [this](FName Name, const FWLGearSetRow& Row) { GearSets.Add(Name, &Row); });
	ForEachRow<FWLSkillRow>(SkillTable, [this](FName, const FWLSkillRow& Row) { Skills.Add(Row.Skill, &Row); });
	ForEachRow<FWLRecipeRow>(RecipeTable, [this](FName Name, const FWLRecipeRow& Row)
	{
		RecipesByName.Add(Name, &Row);
		RecipeNames.Add(&Row, Name);
		const int32 Station = static_cast<int32>(Row.Station);
		if (Station >= 0 && Station < UE_ARRAY_COUNT(RecipesByStation))
		{
			RecipesByStation[Station].Add(&Row);
		}
	});

	for (TArray<const FWLRecipeRow*>& List : RecipesByStation)
	{
		List.StableSort([](const FWLRecipeRow& A, const FWLRecipeRow& B)
		{
			if (A.LevelRequired != B.LevelRequired)
			{
				return A.LevelRequired < B.LevelRequired;
			}
			return A.DisplayName.ToString() < B.DisplayName.ToString();
		});
	}

	UE_LOG(LogWastelanders, Log, TEXT("Game data loaded: %d items, %d recipes, %d modules, %d gear sets, %d skills"),
		Items.Num(), RecipesByName.Num(), Modules.Num(), GearSets.Num(), Skills.Num());
}

const FWLItemRow* UWLDataSubsystem::FindItem(int32 ItemId) const
{
	const FWLItemRow* const* Found = Items.Find(ItemId);
	return Found ? *Found : nullptr;
}

const FWLModuleRow* UWLDataSubsystem::FindModule(int32 ItemId) const
{
	const FWLModuleRow* const* Found = Modules.Find(ItemId);
	return Found ? *Found : nullptr;
}

const FWLGearSetRow* UWLDataSubsystem::FindGearSet(FName SetId) const
{
	const FWLGearSetRow* const* Found = SetId.IsNone() ? nullptr : GearSets.Find(SetId);
	return Found ? *Found : nullptr;
}

const FWLSkillRow* UWLDataSubsystem::FindSkill(EWLSkill Skill) const
{
	const FWLSkillRow* const* Found = Skills.Find(Skill);
	return Found ? *Found : nullptr;
}

const TArray<const FWLRecipeRow*>& UWLDataSubsystem::GetRecipes(EWLCraftingStation Station) const
{
	static const TArray<const FWLRecipeRow*> Empty;
	const int32 Index = static_cast<int32>(Station);
	return (Index >= 0 && Index < UE_ARRAY_COUNT(RecipesByStation)) ? RecipesByStation[Index] : Empty;
}

const FWLRecipeRow* UWLDataSubsystem::FindRecipe(FName RecipeRowName) const
{
	const FWLRecipeRow* const* Found = RecipesByName.Find(RecipeRowName);
	return Found ? *Found : nullptr;
}

FName UWLDataSubsystem::GetRecipeRowName(const FWLRecipeRow* Recipe) const
{
	const FName* Found = RecipeNames.Find(Recipe);
	return Found ? *Found : NAME_None;
}

bool UWLDataSubsystem::GetItem(int32 ItemId, FWLItemRow& OutItem) const
{
	if (const FWLItemRow* Row = FindItem(ItemId))
	{
		OutItem = *Row;
		return true;
	}
	return false;
}

FText UWLDataSubsystem::GetItemName(int32 ItemId) const
{
	const FWLItemRow* Row = FindItem(ItemId);
	return Row ? Row->DisplayName : FText::FromString(FString::Printf(TEXT("Unknown item %d"), ItemId));
}
