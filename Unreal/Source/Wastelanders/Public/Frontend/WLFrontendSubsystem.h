#pragma once

#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "WLFrontendSubsystem.generated.h"

/**
 * Front-end state that survives the map change into the world: the chosen character name, the
 * character-creator "look" and the server address. Ports the data side of Unity's CharacterSelectUI /
 * CharacterCreatorUI / CharacterAppearance (Assets/Scripts/UI, Assets/Scripts/Player3D).
 *
 * Unity's creator picked Sidekick meshes by name; with no art in the project the look is stored as
 * name -> value pairs (FWLCharacterSave::Appearance keys) where part rows hold an option index and
 * colour rows hold a palette index. The appearance system (BP_PlayerCharacter::RefreshAppearance) maps
 * those to meshes/materials later. Creation is face and body only; gear is loot (CharacterCreatorConfig).
 *
 * The pure helpers are static so they can be unit tested without a world.
 */
UCLASS()
class WASTELANDERS_API UWLFrontendSubsystem : public UGameInstanceSubsystem
{
	GENERATED_BODY()

public:
	static UWLFrontendSubsystem* Get(const UObject* WorldContextObject);

	// --- look definition (CharacterCreatorUI.Rows + extra rows) -----------------------------

	/** Part rows in display order (Head is hidden, as in Unity). Keys double as Appearance keys. */
	static const TArray<FName>& GetPartRows();
	/** Slider rows, -100..+100 (CharacterAppearance.Look BodyType / BodySize / Musculature). */
	static const TArray<FName>& GetSliderRows();
	/** Colour rows (hair, facial hair, brow, skin). */
	static const TArray<FName>& GetColorRows();
	static FString RowLabel(FName Row);

	/** Option counts. Without art there is no mesh library; these bound the indices until one exists. */
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Frontend")
	int32 OptionsPerPartRow = 12;
	UPROPERTY(EditAnywhere, Category = "Wastelanders|Frontend")
	int32 ColorPaletteSize = 8;

	/** Step a row by Delta with wrap-around (part/colour) or clamp (slider, step 10). */
	void StepRow(FName Row, int32 Delta);
	/** Randomise every row (Unity CharacterCreatorUI.Randomise). */
	void Randomise(FRandomStream& Random);
	/** Current display value of a row. */
	FString GetRowValue(FName Row) const;

	const TMap<FName, FString>& GetLook() const { return Look; }
	void SetLook(const TMap<FName, FString>& NewLook) { Look = NewLook; }

	// --- identity -----------------------------------------------------------------------------

	/** Same rules as UWLGameInstance::JoinWorld: alnum/underscore, spaces become '_', max 20. */
	static FString SanitizeName(const FString& Raw);
	static bool IsValidName(const FString& Raw);

	/** "Male" / "Female" (CharacterSelectUI gender pick); drives the BodyType default. */
	void SetGender(const FString& InGender);
	const FString& GetGender() const { return Gender; }

	FString CharacterName;
	FString ServerAddress = TEXT("127.0.0.1:7777");

	bool CanJoin() const { return IsValidName(CharacterName) && !Gender.IsEmpty() && !ServerAddress.IsEmpty(); }
	/** Hands off to UWLGameInstance::JoinWorld. False (and nothing happens) when !CanJoin(). */
	bool Join();

	// --- look serialisation (CharacterAppearance.SaveLook "k=v|k=v") --------------------------

	static FString EncodeLook(const TMap<FName, FString>& InLook);
	static TMap<FName, FString> DecodeLook(const FString& Encoded);

	// --- splash -------------------------------------------------------------------------------

	/** True once per process: the in-world HUD shows the NEW GAME splash after a fresh join. */
	bool ConsumePendingSplash();
	void QueueSplash() { bPendingSplash = true; }

private:
	TMap<FName, FString> Look;
	FString Gender;
	bool bPendingSplash = false;
};
