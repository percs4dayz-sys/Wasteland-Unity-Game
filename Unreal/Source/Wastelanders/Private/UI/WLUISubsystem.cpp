#include "UI/WLUISubsystem.h"
#include "UI/WLViewModels.h"
#include "UI/WLUIThemeSettings.h"
#include "UI/WLDebugHUD.h"
#include "UI/WLGameUserSettings.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerCharacter.h"
#include "Engine/Engine.h"
#include "Engine/LocalPlayer.h"
#include "Engine/World.h"
#include "GameFramework/HUD.h"

UWLUISubsystem* UWLUISubsystem::Get(const APlayerController* PC)
{
	const ULocalPlayer* LP = PC ? PC->GetLocalPlayer() : nullptr;
	return LP ? LP->GetSubsystem<UWLUISubsystem>() : nullptr;
}

UWLUISubsystem* UWLUISubsystem::Get(const UObject* WorldContextObject)
{
	if (const APlayerController* PC = Cast<APlayerController>(WorldContextObject))
	{
		return Get(PC);
	}
	const UWorld* World = WorldContextObject ? WorldContextObject->GetWorld() : nullptr;
	const ULocalPlayer* LP = (World && GEngine) ? GEngine->GetFirstGamePlayer(const_cast<UWorld*>(World)) : nullptr;
	return LP ? LP->GetSubsystem<UWLUISubsystem>() : nullptr;
}

void UWLUISubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);
	Hud = MakeVM<UWLHudViewModel>();
	Inventory = MakeVM<UWLInventoryViewModel>();
	Equipment = MakeVM<UWLEquipmentViewModel>();
	Bank = MakeVM<UWLBankViewModel>();
	Crafting = MakeVM<UWLCraftingViewModel>();
	Skills = MakeVM<UWLSkillsViewModel>();
	XPDrops = MakeVM<UWLXPDropViewModel>();
	CombatStyle = MakeVM<UWLCombatStyleViewModel>();
	Chat = MakeVM<UWLChatViewModel>();
	Dialogue = MakeVM<UWLDialogueViewModel>();
	ContextMenu = MakeVM<UWLContextMenuViewModel>();
	Minimap = MakeVM<UWLMinimapViewModel>();
	Settings = MakeVM<UWLSettingsViewModel>();
	bInitialized = true;
}

void UWLUISubsystem::Deinitialize()
{
	bInitialized = false;
	for (UWLViewModelBase* VM : AllViewModels())
	{
		if (VM)
		{
			VM->Unbind();
		}
	}
	Super::Deinitialize();
}

TArray<UWLViewModelBase*> UWLUISubsystem::AllViewModels() const
{
	return { Hud, Inventory, Equipment, Bank, Crafting, Skills, XPDrops, CombatStyle, Chat, Dialogue, ContextMenu, Minimap, Settings };
}

TStatId UWLUISubsystem::GetStatId() const
{
	RETURN_QUICK_DECLARE_CYCLE_STAT(UWLUISubsystem, STATGROUP_Tickables);
}

bool UWLUISubsystem::IsTickable() const
{
	return bInitialized && !HasAnyFlags(RF_ClassDefaultObject) && GetLocalPlayer() != nullptr;
}

UWorld* UWLUISubsystem::GetTickableGameObjectWorld() const
{
	const ULocalPlayer* LP = GetLocalPlayer();
	return LP ? LP->GetWorld() : nullptr;
}

void UWLUISubsystem::Tick(float DeltaTime)
{
	bPointerOverUILastFrame = bPointerOverUI;
	bPointerOverUI = false;

	ULocalPlayer* LP = GetLocalPlayer();
	UWorld* World = LP ? LP->GetWorld() : nullptr;
	AWLPlayerController* PC = World ? Cast<AWLPlayerController>(LP->GetPlayerController(World)) : nullptr;
	AWLPlayerState* PS = PC ? PC->GetWLPlayerState() : nullptr;
	AWLPlayerCharacter* Pawn = PC ? PC->GetWLCharacter() : nullptr;

	if (PC != BoundPC.Get() || PS != BoundPS.Get() || Pawn != BoundPawn.Get())
	{
		const bool bNewController = PC != BoundPC.Get();
		BoundPC = PC;
		BoundPS = PS;
		BoundPawn = Pawn;
		RebindAll();
		if (bNewController && PC && Settings)
		{
			Settings->ApplyCameraSettings();
			UWLGameUserSettings::GetWLSettings()->ApplyAudio(PC);
		}
	}

	if (PC)
	{
		MaybeInstallDebugHUD(PC);
	}

	for (UWLViewModelBase* VM : AllViewModels())
	{
		if (VM)
		{
			VM->TickViewModel(DeltaTime);
		}
	}
}

void UWLUISubsystem::RebindAll()
{
	for (UWLViewModelBase* VM : AllViewModels())
	{
		if (VM)
		{
			VM->BindContext(this, BoundPC.Get(), BoundPS.Get(), BoundPawn.Get());
		}
	}
}

void UWLUISubsystem::MaybeInstallDebugHUD(AWLPlayerController* PC)
{
	const UWLUIThemeSettings* Theme = UWLUIThemeSettings::Get();
	if (!Theme->bAutoInstallDebugHUD || !PC->IsLocalController())
	{
		return;
	}
	UClass* HUDClass = Theme->DebugHUDClass.IsNull() ? AWLDebugHUD::StaticClass() : Theme->DebugHUDClass.LoadSynchronous();
	if (!HUDClass)
	{
		return;
	}
	const AHUD* Current = PC->GetHUD();
	if (!Current || !Current->IsA(HUDClass))
	{
		// A Client RPC invoked on the owning client (or a listen server's local controller) runs locally.
		PC->ClientSetHUD(HUDClass);
	}
}

bool UWLUISubsystem::IsPanelOpen(EWLUIPanel Panel) const
{
	return (OpenPanels & (1u << static_cast<uint32>(Panel))) != 0;
}

void UWLUISubsystem::SetPanelOpen(EWLUIPanel Panel, bool bOpen)
{
#if UE_BUILD_SHIPPING
	if (Panel == EWLUIPanel::DevPanel)
	{
		return; // Unity: DevPanel was editor-only
	}
#endif
	const uint32 Bit = 1u << static_cast<uint32>(Panel);
	const uint32 Old = OpenPanels;
	OpenPanels = bOpen ? (OpenPanels | Bit) : (OpenPanels & ~Bit);
	if (Old != OpenPanels)
	{
		if (bOpen && Panel == EWLUIPanel::Skills && Skills)
		{
			Skills->Refresh();
		}
		if (bOpen && Panel == EWLUIPanel::Equipment && Equipment)
		{
			Equipment->Refresh();
		}
		if (bOpen && Panel == EWLUIPanel::Music && Settings)
		{
			Settings->RefreshMusic();
		}
		OnPanelsChanged.Broadcast();
	}
}

void UWLUISubsystem::TogglePanel(EWLUIPanel Panel)
{
	SetPanelOpen(Panel, !IsPanelOpen(Panel));
}

bool UWLUISubsystem::CloseTopmost()
{
	if (ContextMenu && ContextMenu->bOpen)
	{
		ContextMenu->Close();
		return true;
	}
	if (Dialogue && Dialogue->bEggChoiceOpen)
	{
		Dialogue->CloseEggChoice();
		return true;
	}
	if (Crafting && Crafting->bOpen)
	{
		Crafting->Close();
		return true;
	}
	if (Bank && Bank->bOpen)
	{
		Bank->Close();
		return true;
	}
	// Windows: close the most recently relevant ones first (settings / menus over the side panels).
	static const EWLUIPanel Order[] = { EWLUIPanel::DevPanel, EWLUIPanel::Settings, EWLUIPanel::SystemMenu, EWLUIPanel::Music,
		EWLUIPanel::Build, EWLUIPanel::Equipment, EWLUIPanel::Skills, EWLUIPanel::CombatStyle, EWLUIPanel::Inventory };
	for (const EWLUIPanel P : Order)
	{
		if (IsPanelOpen(P))
		{
			SetPanelOpen(P, false);
			return true;
		}
	}
	return false;
}
