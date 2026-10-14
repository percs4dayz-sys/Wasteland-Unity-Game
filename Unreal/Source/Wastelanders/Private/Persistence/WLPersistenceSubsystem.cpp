#include "Persistence/WLPersistenceSubsystem.h"
#include "Persistence/WLJsonFileBackend.h"
#include "Persistence/WLPersistenceSettings.h"
#include "Core/WLGameSettings.h"
#include "Core/WLTypes.h"
#include "Core/WLXPTable.h"
#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "Engine/GameInstance.h"
#include "Engine/World.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Wastelanders.h"

// Ported from Assets/Scripts/Save/SaveManager.cs and SaveData.cs.

void UWLPersistenceSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);
	// The file backend is cheap to construct and does no I/O until asked, so every GameInstance gets one;
	// only the server (AWLGameMode) ever calls it. A server plugin may swap in a database via SetBackend.
	Backend = MakeShared<FWLJsonFileBackend>();
}

void UWLPersistenceSubsystem::Deinitialize()
{
	// Unity saved in OnApplicationQuit; here the game mode saves everyone in EndPlay and we make sure
	// those writes reach the disk before the process goes away.
	Flush();
	Backend.Reset();
	Super::Deinitialize();
}

UWLPersistenceSubsystem* UWLPersistenceSubsystem::Get(const UObject* WorldContextObject)
{
	const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
	const UGameInstance* GI = World ? World->GetGameInstance() : nullptr;
	return GI ? GI->GetSubsystem<UWLPersistenceSubsystem>() : nullptr;
}

void UWLPersistenceSubsystem::LoadCharacter(const FString& AccountId, const FString& CharacterName, FWLOnCharacterLoaded Callback)
{
	if (!Backend.IsValid())
	{
		Callback.ExecuteIfBound(false, FWLCharacterSave());
		return;
	}
	Backend->LoadOrCreate(AccountId, CharacterName, MoveTemp(Callback));
}

void UWLPersistenceSubsystem::SaveCharacter(const FWLCharacterSave& Record)
{
	if (Backend.IsValid())
	{
		Backend->Save(Record);
	}
}

void UWLPersistenceSubsystem::Flush()
{
	if (Backend.IsValid())
	{
		Backend->Flush();
	}
}

void UWLPersistenceSubsystem::SetBackend(TSharedPtr<IWLPersistenceBackend> NewBackend)
{
	if (!NewBackend.IsValid())
	{
		return;
	}
	// Writes queued on the old backend must land before anything reads through the new one.
	Flush();
	Backend = MoveTemp(NewBackend);
}

FWLCharacterSave UWLPersistenceSubsystem::MakeNewCharacter(const FString& AccountId, const FString& CharacterName)
{
	FWLCharacterSave Save;
	Save.Version = FWLCharacterSave::CurrentVersion;
	Save.CharacterId = FGuid::NewGuid();
	Save.AccountId = AccountId;
	Save.PlayerName = CharacterName.IsEmpty() ? FString(TEXT("Survivor")) : CharacterName; // Unity PlayerPrefs default
	Save.LastSaved = FDateTime::UtcNow();

	// Every skill listed explicitly; Endurance starts at level 10 because it IS the hit point pool.
	for (EWLSkill Skill : TEnumRange<EWLSkill>())
	{
		FWLSaveSkill& Entry = Save.SkillXP.AddDefaulted_GetRef();
		Entry.Skill = WLTypes::SkillToString(Skill);
		Entry.XP = FWLXPTable::XPForLevel(Skill, FWLXPTable::MinLevelFor(Skill));
	}

	if (const UWLPersistenceSettings* Settings = UWLPersistenceSettings::Get())
	{
		for (const int32 ItemId : Settings->StarterKitItemIds)
		{
			if (ItemId > 0)
			{
				FWLSaveSlot& Slot = Save.Inventory.AddDefaulted_GetRef();
				Slot.ItemId = ItemId;
				Slot.Quantity = 1;
			}
		}
	}

	// Tutorial flags clear, no location (spawn at a PlayerStart), HP 0 = full.
	return Save;
}

// ---------------------------------------------------------------------------------------------
// Unity import
// ---------------------------------------------------------------------------------------------

namespace WLUnityImport
{
	static int32 IntField(const TSharedPtr<FJsonObject>& Obj, const TCHAR* Key, int32 Default = 0)
	{
		double Value = 0.0;
		return Obj.IsValid() && Obj->TryGetNumberField(Key, Value) ? static_cast<int32>(Value) : Default;
	}

	static float FloatField(const TSharedPtr<FJsonObject>& Obj, const TCHAR* Key)
	{
		double Value = 0.0;
		return Obj.IsValid() && Obj->TryGetNumberField(Key, Value) ? static_cast<float>(Value) : 0.f;
	}

	static TArray<int32> IntArray(const TSharedPtr<FJsonObject>& Obj, const TCHAR* Key)
	{
		TArray<int32> Out;
		const TArray<TSharedPtr<FJsonValue>>* Arr = nullptr;
		if (Obj.IsValid() && Obj->TryGetArrayField(Key, Arr))
		{
			for (const TSharedPtr<FJsonValue>& V : *Arr)
			{
				double N = 0.0;
				Out.Add(V.IsValid() && V->TryGetNumber(N) ? static_cast<int32>(N) : 0);
			}
		}
		return Out;
	}

	/** Unity SlotEntry {itemId, qty, modules}. Empty / removed-looking entries keep their position as ItemId 0. */
	static void ReadSlots(const TSharedPtr<FJsonObject>& Root, const TCHAR* Key, TArray<FWLSaveSlot>& Out)
	{
		const TArray<TSharedPtr<FJsonValue>>* Arr = nullptr;
		if (!Root->TryGetArrayField(Key, Arr))
		{
			return;
		}
		for (const TSharedPtr<FJsonValue>& V : *Arr)
		{
			const TSharedPtr<FJsonObject> Entry = V.IsValid() ? V->AsObject() : nullptr;
			FWLSaveSlot& Slot = Out.AddDefaulted_GetRef();
			const int32 Id = IntField(Entry, TEXT("itemId"));
			const int32 Qty = IntField(Entry, TEXT("qty"));
			if (Id > 0 && Qty > 0)
			{
				Slot.ItemId = Id;
				Slot.Quantity = Qty;
				Slot.Modules = IntArray(Entry, TEXT("modules")); // missing array in old saves = empty sockets
			}
		}
	}
}

bool UWLPersistenceSubsystem::ImportUnitySave(const FString& UnityJson, const FString& AccountId, FWLCharacterSave& Out)
{
	using namespace WLUnityImport;

	TSharedPtr<FJsonObject> Root;
	if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(UnityJson), Root) || !Root.IsValid())
	{
		return false;
	}
	// Signature check: every Unity SaveData has these two arrays (JsonUtility writes empty lists too).
	if (!Root->HasTypedField<EJson::Array>(TEXT("skillXP")) || !Root->HasTypedField<EJson::Array>(TEXT("inventory")))
	{
		return false;
	}

	Out = FWLCharacterSave();
	Out.Version = FWLCharacterSave::CurrentVersion;
	Out.CharacterId = FGuid::NewGuid();
	Out.AccountId = AccountId;
	Out.LastSaved = FDateTime::UtcNow();

	FString Name;
	Out.PlayerName = Root->TryGetStringField(TEXT("playerName"), Name) && !Name.IsEmpty() ? Name : FString(TEXT("Survivor"));

	// skillXP [{skill, xp}] with Unity's legacy-name migration (WLTypes::SkillFromString).
	// Unity let whichever of Hearthcraft/Tinkering loaded LAST win the merged Smithing XP; keeping the
	// higher value is the same "they were the same job" intent without the order dependence.
	TMap<EWLSkill, int32> SkillXP;
	for (const TSharedPtr<FJsonValue>& V : Root->GetArrayField(TEXT("skillXP")))
	{
		const TSharedPtr<FJsonObject> Entry = V.IsValid() ? V->AsObject() : nullptr;
		FString SkillName;
		EWLSkill Skill;
		if (Entry.IsValid() && Entry->TryGetStringField(TEXT("skill"), SkillName) && WLTypes::SkillFromString(SkillName, Skill))
		{
			const int32 XP = FMath::Max(0, IntField(Entry, TEXT("xp")));
			int32& Slot = SkillXP.FindOrAdd(Skill, 0);
			Slot = FMath::Max(Slot, XP);
		}
	}
	for (EWLSkill Skill : TEnumRange<EWLSkill>())
	{
		FWLSaveSkill& Entry = Out.SkillXP.AddDefaulted_GetRef();
		Entry.Skill = WLTypes::SkillToString(Skill);
		const int32* XP = SkillXP.Find(Skill);
		Entry.XP = XP ? *XP : FWLXPTable::XPForLevel(Skill, FWLXPTable::MinLevelFor(Skill));
	}

	// Slot-exact inventory (includes saddlebag overflow) and bank. Items since removed from the game are
	// dropped by the item components' ApplySave (they own the item table lookup), like Unity's ApplyTo.
	ReadSlots(Root, TEXT("inventory"), Out.Inventory);
	ReadSlots(Root, TEXT("bank"), Out.Bank);

	const TArray<TSharedPtr<FJsonValue>>* Equip = nullptr;
	if (Root->TryGetArrayField(TEXT("equipment"), Equip))
	{
		for (const TSharedPtr<FJsonValue>& V : *Equip)
		{
			const TSharedPtr<FJsonObject> Entry = V.IsValid() ? V->AsObject() : nullptr;
			FString SlotName;
			EWLEquipSlot Slot;
			const int32 Id = IntField(Entry, TEXT("itemId"));
			if (Id <= 0 || !Entry.IsValid() || !Entry->TryGetStringField(TEXT("slot"), SlotName) || !WLTypes::SlotFromString(SlotName, Slot))
			{
				continue;
			}
			FWLSaveEquip& E = Out.Equipment.AddDefaulted_GetRef();
			E.Slot = WLTypes::SlotToString(Slot); // normalises legacy "Boots" -> "Legs"
			E.ItemId = Id;
			E.Modules = IntArray(Entry, TEXT("modules"));
		}
	}
	Out.AmmoQuantity = FMath::Max(0, IntField(Root, TEXT("ammoQty")));

	const TArray<TSharedPtr<FJsonValue>>* Flags = nullptr;
	if (Root->TryGetArrayField(TEXT("flags"), Flags))
	{
		for (const TSharedPtr<FJsonValue>& V : *Flags)
		{
			FString Flag;
			if (V.IsValid() && V->TryGetString(Flag) && !Flag.IsEmpty())
			{
				Out.Flags.AddUnique(Flag);
			}
		}
	}

	// 3D position: Unity is left-handed Y-up in metres; Unreal is left-handed Z-up in units.
	// Unity +Z (forward) -> UE +X, Unity +X (right) -> UE +Y, Unity +Y (up) -> UE +Z.
	// tileX/tileY were 2D-only and are ignored. The map name is the Unity scene; AWLGameMode only uses the
	// location when it matches the current map, otherwise the player starts at a PlayerStart.
	bool bHasPos = false;
	Root->TryGetBoolField(TEXT("hasPos3D"), bHasPos);
	if (bHasPos)
	{
		const UWLGameSettings* GS = UWLGameSettings::Get();
		const float Scale = GS ? GS->UnitsPerTile : 100.f;
		Out.Location = FVector(FloatField(Root, TEXT("posZ")), FloatField(Root, TEXT("posX")), FloatField(Root, TEXT("posY"))) * Scale;
		Out.bHasLocation = true;
	}
	Root->TryGetStringField(TEXT("sceneName"), Out.MapName);

	// HP was never saved by Unity: 0 = start at full health.
	Out.CurrentHP = 0;
	return true;
}

void UWLPersistenceSubsystem::Migrate(FWLCharacterSave& Record)
{
	if (Record.Version > FWLCharacterSave::CurrentVersion)
	{
		// Written by a newer server build. Leave it alone; unknown fields were already ignored on load.
		UE_LOG(LogWastelanders, Warning, TEXT("[Persistence] %s has record version %d, newer than this build (%d)"),
			*Record.PlayerName, Record.Version, FWLCharacterSave::CurrentVersion);
		return;
	}

	if (Record.Version < 1)
	{
		// v0 -> v1: canonicalise skill names (legacy Unity names) and merge duplicates, keeping the higher XP.
		TMap<EWLSkill, int32> Merged;
		for (const FWLSaveSkill& Entry : Record.SkillXP)
		{
			EWLSkill Skill;
			if (WLTypes::SkillFromString(Entry.Skill, Skill))
			{
				int32& XP = Merged.FindOrAdd(Skill, 0);
				XP = FMath::Max(XP, Entry.XP);
			}
		}
		Record.SkillXP.Reset();
		for (EWLSkill Skill : TEnumRange<EWLSkill>())
		{
			FWLSaveSkill& Entry = Record.SkillXP.AddDefaulted_GetRef();
			Entry.Skill = WLTypes::SkillToString(Skill);
			const int32* XP = Merged.Find(Skill);
			Entry.XP = XP ? *XP : FWLXPTable::XPForLevel(Skill, FWLXPTable::MinLevelFor(Skill));
		}

		// Legacy slot names ("Boots").
		for (FWLSaveEquip& Equip : Record.Equipment)
		{
			EWLEquipSlot Slot;
			if (WLTypes::SlotFromString(Equip.Slot, Slot))
			{
				Equip.Slot = WLTypes::SlotToString(Slot);
			}
		}
		Record.Version = 1;
	}

	// Future: if (Record.Version < 2) { ...; Record.Version = 2; }
	Record.Version = FWLCharacterSave::CurrentVersion;
}
