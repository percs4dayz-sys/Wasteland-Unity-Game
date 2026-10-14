#pragma once

#include "CoreMinimal.h"
#include "WLCharacterSave.generated.h"

/**
 * Persistent character record. Mirrors Unity's SaveData.cs field-for-field (so old JSON saves can be
 * imported) plus the extra state an MMO character needs. Serialized to JSON by the persistence
 * backend (FJsonObjectConverter), so property names are the on-disk keys: never rename them.
 *
 * Ownership: every persistent component on AWLPlayerState implements
 *     void CaptureSave(FWLCharacterSave& Out) const;
 *     void ApplySave(const FWLCharacterSave& In);
 * and touches only its own fields.
 */

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSaveSkill
{
	GENERATED_BODY()

	/** Skill name; legacy Unity names are accepted on load (WLTypes::SkillFromString). */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FString Skill;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 XP = 0;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSaveSlot
{
	GENERATED_BODY()

	/** 0 = empty slot (slot-exact restore, like Unity). */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 ItemId = 0;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 Quantity = 0;

	/** Socketed module item ids (0 = empty socket). */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TArray<int32> Modules;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSaveEquip
{
	GENERATED_BODY()

	/** "Weapon", "Shield", ... (WLTypes::SlotToString). */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FString Slot;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 ItemId = 0;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TArray<int32> Modules;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSaveQuestState
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FName QuestId;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 Stage = 0;

	/** Free-form counters (kills toward a beast task, items handed in...). */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TMap<FName, int32> Counters;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLSaveCompanion
{
	GENERATED_BODY()

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FName CompanionType;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FString Nickname;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 Level = 1;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 XP = 0;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	bool bSummoned = false;
};

USTRUCT(BlueprintType)
struct WASTELANDERS_API FWLCharacterSave
{
	GENERATED_BODY()

	/** Bump when the layout changes; the persistence subsystem migrates older records on load. */
	static constexpr int32 CurrentVersion = 1;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 Version = CurrentVersion;

	/** Server-assigned unique id; the persistence key. */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FGuid CharacterId;

	/** Owning account (platform/EOS id string). */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FString AccountId;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FString PlayerName;

	/** Character-creator choices (body, colours...) as name -> value pairs; meaning owned by the appearance system. */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TMap<FName, FString> Appearance;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TArray<FWLSaveSkill> SkillXP;

	/** Inventory slots, slot-exact, including saddlebag overflow slots. */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TArray<FWLSaveSlot> Inventory;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TArray<FWLSaveSlot> Bank;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TArray<FWLSaveEquip> Equipment;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 AmmoQuantity = 0;

	/** Story / tutorial flags ("roxy_fission_given", ...). */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TArray<FString> Flags;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TArray<FWLSaveQuestState> Quests;

	/** Journal page item ids the player has read. */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TArray<int32> JournalPagesRead;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	TArray<FWLSaveCompanion> Companions;

	/** World placement. Map is the persistent level / shard map name. */
	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FString MapName;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FVector Location = FVector::ZeroVector;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	float Yaw = 0.f;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	bool bHasLocation = false;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 CurrentHP = 0;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	int32 TotalKills = 0;

	UPROPERTY(BlueprintReadWrite, Category = "Save")
	FDateTime LastSaved;
};
