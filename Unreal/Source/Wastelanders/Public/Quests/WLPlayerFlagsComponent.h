#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "WLPlayerFlagsComponent.generated.h"

struct FWLCharacterSave;

DECLARE_MULTICAST_DELEGATE(FWLOnFlagsChanged);

/**
 * Story / tutorial flags (Unity PlayerEntity.HasFlag / SetFlag / RemoveFlag / GetAllFlags).
 * A plain set of strings; the exact Unity spellings are in Quests/WLQuestFlags.h.
 *
 * Lives on AWLPlayerState. Server-authoritative; replicated to the owning client only (nobody else
 * needs to know another player's tutorial progress). OnFlagsChanged fires on the server after every
 * change and on the owning client from OnRep, so UI and client-side guides can refresh.
 */
UCLASS(ClassGroup = (Wastelanders), meta = (BlueprintSpawnableComponent))
class WASTELANDERS_API UWLPlayerFlagsComponent : public UActorComponent
{
	GENERATED_BODY()

public:
	UWLPlayerFlagsComponent();

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

	UFUNCTION(BlueprintPure, Category = "Wastelanders|Flags")
	bool HasFlag(const FString& Flag) const;

	/** SERVER. Adds the flag (no-op if present) and broadcasts UWLGameEventsSubsystem::OnFlagSet. True if it was new. */
	bool SetFlag(const FString& Flag);

	/** SERVER. True if it was present. */
	bool RemoveFlag(const FString& Flag);

	/** SERVER. Removes every flag starting with Prefix; returns how many were removed. */
	int32 RemoveFlagsWithPrefix(const FString& Prefix);

	/** First flag beginning with Prefix (Unity's "foreach flag ... StartsWith" idiom), or null. */
	const FString* FindFlagWithPrefix(const FString& Prefix) const;

	const TArray<FString>& GetFlags() const { return Flags; }

	/** Fires on the server and owning client whenever the set changes. */
	FWLOnFlagsChanged OnFlagsChanged;

	void CaptureSave(FWLCharacterSave& Out) const;
	void ApplySave(const FWLCharacterSave& In);

private:
	UFUNCTION()
	void OnRep_Flags();

	void BroadcastChanged();

	/** Owner-only. Order is irrelevant; duplicates are never stored. */
	UPROPERTY(ReplicatedUsing = OnRep_Flags)
	TArray<FString> Flags;
};
