// Combat style, chat, dialogue, context menu, minimap and settings view models (see WLViewModels.h).

#include "UI/WLViewModels.h"
#include "UI/WLUISubsystem.h"
#include "UI/WLGameUserSettings.h"
#include "Audio/WLAudioEvents.h"
#include "Audio/WLMusicSubsystem.h"
#include "Player/WLPlayerController.h"
#include "Player/WLPlayerState.h"
#include "Player/WLPlayerCharacter.h"
#include "AbilitySystem/WLAbilitySystemComponent.h"
#include "Combat/WLCombatComponent.h"
#include "Combat/WLEnemyCharacter.h"
#include "Companions/WLCompanionComponent.h"
#include "Dialogue/WLDialogueComponent.h"
#include "Dialogue/WLDialogueTypes.h"
#include "Interaction/WLInteractable.h"
#include "Interaction/WLInteractorComponent.h"
#include "Items/WLGroundItem.h"
#include "Quests/WLQuestComponent.h"
#include "World/WLResourceNode.h"
#include "GameFramework/SpringArmComponent.h"
#include "EngineUtils.h"
#include "Engine/World.h"

#define LOCTEXT_NAMESPACE "WLViewModels"

#define WL_SET_TEXT(Member, NewText) \
	[&]() -> bool { const FText WLNew_ = (NewText); if (Member.EqualTo(WLNew_)) { return false; } Member = WLNew_; UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Member); return true; }()

// =================================================================================================
// Combat style
// =================================================================================================

FText UWLCombatStyleViewModel::HeadingFor(EWLCombatStyle InStyle)
{
	switch (InStyle)
	{
	case EWLCombatStyle::Ranged: return LOCTEXT("RangedStances", "MARKSMANSHIP STANCES");
	case EWLCombatStyle::Fission: return LOCTEXT("FissionStances", "FISSION STANCES");
	default: return LOCTEXT("MeleeStances", "MELEE STANCES");
	}
}

void UWLCombatStyleViewModel::Refresh()
{
	const AWLPlayerCharacter* Body = Pawn.Get();
	const UWLCombatComponent* Combat = Body ? Body->GetCombat() : nullptr;
	if (!Combat)
	{
		return;
	}
	const EWLCombatStyle NewStyle = Combat->GetStyle();
	const EWLCombatStance NewStance = Combat->GetStance();
	UE_MVVM_SET_PROPERTY_VALUE(Style, NewStyle);
	WL_SET_TEXT(StyleHeading, HeadingFor(NewStyle));

	// The equipped weapon decides the style; the list of stances is rebuilt only when it changes.
	if (!bBuilt || BuiltStyle != NewStyle || LastStance != NewStance)
	{
		bBuilt = true;
		BuiltStyle = NewStyle;
		LastStance = NewStance;
		Stances.Reset();
		for (const EWLCombatStance S : UWLCombatComponent::StancesFor(NewStyle))
		{
			FWLStanceView& View = Stances.AddDefaulted_GetRef();
			View.Stance = S;
			View.Label = UWLCombatComponent::StanceLabel(S);
			View.Description = UWLCombatComponent::StanceDescription(S, NewStyle);
			View.bActive = S == NewStance;
		}
		UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Stances);
	}
	NotifyChanged();
}

void UWLCombatStyleViewModel::TickViewModel(float DeltaSeconds)
{
	// Energy regenerates continuously, so the special bar is polled (Unity CombatStyleUI.RefreshSpecial).
	const AWLPlayerCharacter* Body = Pawn.Get();
	const UWLCombatComponent* Combat = Body ? Body->GetCombat() : nullptr;
	const AWLPlayerState* P = PS.Get();
	if (!Combat || !P)
	{
		return;
	}
	if (!bBuilt || Combat->GetStyle() != BuiltStyle || Combat->GetStance() != LastStance)
	{
		Refresh();
	}

	bool bDirty = false;
	bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bHasWeaponSpecial, Combat->HasWeaponSpecial());
	if (bHasWeaponSpecial)
	{
		bDirty |= WL_SET_TEXT(SpecialName, Combat->GetSpecialAttackName());
		bDirty |= WL_SET_TEXT(SpecialBlockReason, Combat->GetSpecialBlockReason());
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(SpecialCost, Combat->GetSpecialEnergyCost());
	}
	if (const UWLAbilitySystemComponent* ASC = P->GetWLAbilitySystem())
	{
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(SpecialEnergy, ASC->GetSpecialEnergyValue());
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(MaxSpecialEnergy, FMath::Max(1.f, ASC->GetMaxSpecialEnergyValue()));
	}

	if (const UWLCompanionComponent* Comp = P->GetCompanions())
	{
		const FWLCompanionStatus& Status = Comp->GetStatus();
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bHasBeastSpecial, Status.bHasCompanion && !Status.bIncubating);
		bDirty |= WL_SET_TEXT(BeastSpecialBlockReason, Comp->GetSpecialBlockReason());
		bDirty |= UE_MVVM_SET_PROPERTY_VALUE(BeastSpecialCooldown, Status.SpecialCooldown);
	}

	bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bUsesMagazine, Combat->UsesMagazine());
	bDirty |= UE_MVVM_SET_PROPERTY_VALUE(MagazineLoaded, Combat->GetMagazineLoaded());
	bDirty |= UE_MVVM_SET_PROPERTY_VALUE(MagazineSize, Combat->GetMagazineSize());
	bDirty |= UE_MVVM_SET_PROPERTY_VALUE(bReloading, Combat->IsReloading());
	bDirty |= UE_MVVM_SET_PROPERTY_VALUE(ReloadProgress, Combat->GetReloadProgress());
	if (bDirty)
	{
		NotifyChanged();
	}
}

void UWLCombatStyleViewModel::SetStance(EWLCombatStance InStance)
{
	AWLPlayerCharacter* Body = Pawn.Get();
	UWLCombatComponent* Combat = Body ? Body->GetCombat() : nullptr;
	if (!Combat || !UWLCombatComponent::StancesFor(Combat->GetStyle()).Contains(InStance))
	{
		return;
	}
	Combat->ServerSetStance(InStance);
	// Unity echoed the choice into chat right away.
	if (UWLUISubsystem* Root = UI.Get())
	{
		Root->GetChat()->AddLocalLine(FText::Format(LOCTEXT("StanceChat", "[COMBAT]: {0} stance ({1})."),
			UWLCombatComponent::StanceLabel(InStance), UWLCombatComponent::StanceDescription(InStance, Combat->GetStyle())));
	}
	UWLAudioEvents::PlayEvent(Body, TEXT("UIClick"));
}

void UWLCombatStyleViewModel::FireSpecial()
{
	if (AWLPlayerCharacter* Body = Pawn.Get())
	{
		if (UWLCombatComponent* Combat = Body->GetCombat())
		{
			Combat->RequestSpecialAttack();
		}
	}
}

void UWLCombatStyleViewModel::Reload()
{
	if (AWLPlayerCharacter* Body = Pawn.Get())
	{
		if (UWLCombatComponent* Combat = Body->GetCombat())
		{
			Combat->RequestReload();
		}
	}
}

void UWLCombatStyleViewModel::FireBeastSpecial()
{
	if (AWLPlayerState* P = PS.Get())
	{
		if (UWLCompanionComponent* Comp = P->GetCompanions())
		{
			Comp->RequestSpecial();
		}
	}
}

// =================================================================================================
// Chat
// =================================================================================================

void UWLChatViewModel::OnBind()
{
	if (AWLPlayerController* Controller = PC.Get())
	{
		Controller->OnGameMessage.AddUniqueDynamic(this, &UWLChatViewModel::HandleGameMessage);
	}
	// Lines are kept across rebinds (respawn keeps the log, like Unity's persistent chat).
}

void UWLChatViewModel::OnUnbind()
{
	if (AWLPlayerController* Controller = PC.Get())
	{
		Controller->OnGameMessage.RemoveDynamic(this, &UWLChatViewModel::HandleGameMessage);
	}
}

void UWLChatViewModel::HandleGameMessage(const FText& Message)
{
	AddLocalLine(Message);
}

void UWLChatViewModel::AddLocalLine(const FText& Line)
{
	if (Line.IsEmptyOrWhitespace())
	{
		return;
	}
	FWLUILogic::AppendChatLine(Lines, Line);
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Lines);
	NotifyChanged();
}

void UWLChatViewModel::Submit(const FString& Text)
{
	if (AWLPlayerController* Controller = PC.Get())
	{
		// Trimming, the 240-char cap and "/" command routing happen in AWLPlayerController::SubmitChat.
		Controller->SubmitChat(Text);
	}
}

void UWLChatViewModel::SetTyping(bool bInTyping)
{
	if (UE_MVVM_SET_PROPERTY_VALUE(bTyping, bInTyping))
	{
		NotifyChanged();
	}
}

// =================================================================================================
// Dialogue
// =================================================================================================

void UWLDialogueViewModel::OnBind()
{
	bDialogueBound = false;
	TryBindDialogue();
	if (AWLPlayerCharacter* Body = Pawn.Get())
	{
		if (UWLInteractorComponent* Interactor = Body->GetInteractor())
		{
			// World objects (the tutorial portal) can also ask for the starter-beast picker.
			Interactor->OnWorldEvent.AddWeakLambda(this, [this](FName EventId, const FText&, const FText&)
			{
				if (EventId == TEXT("EggChoice"))
				{
					HandleUIRequest(EventId);
				}
			});
		}
	}
	UE_MVVM_SET_PROPERTY_VALUE(ContinuePrompt, FText(LOCTEXT("ClickToContinue", "Click here to continue")));
}

bool UWLDialogueViewModel::TryBindDialogue()
{
	if (bDialogueBound)
	{
		return true;
	}
	// The quests system adds the component on the server at BeginPlay; on clients it replicates in later.
	UWLDialogueComponent* Dlg = UWLDialogueComponent::Get(PS.Get());
	if (!Dlg)
	{
		return false;
	}
	Dlg->OnDialogueUpdated.AddUObject(this, &UWLDialogueViewModel::HandleUpdated);
	Dlg->OnDialogueEnded.AddUObject(this, &UWLDialogueViewModel::HandleEnded);
	Dlg->OnUIRequest.AddUObject(this, &UWLDialogueViewModel::HandleUIRequest);
	bDialogueBound = true;
	if (Dlg->GetView().bActive)
	{
		HandleUpdated(Dlg->GetView());
	}
	return true;
}

void UWLDialogueViewModel::OnUnbind()
{
	if (UWLDialogueComponent* Dlg = UWLDialogueComponent::Get(PS.Get()))
	{
		Dlg->OnDialogueUpdated.RemoveAll(this);
		Dlg->OnDialogueEnded.RemoveAll(this);
		Dlg->OnUIRequest.RemoveAll(this);
	}
	if (AWLPlayerCharacter* Body = Pawn.Get())
	{
		if (UWLInteractorComponent* Interactor = Body->GetInteractor())
		{
			Interactor->OnWorldEvent.RemoveAll(this);
		}
	}
	bDialogueBound = false;
	HandleEnded();
}

void UWLDialogueViewModel::TickViewModel(float DeltaSeconds)
{
	if (!bDialogueBound && PS.IsValid())
	{
		TryBindDialogue();
	}
}

void UWLDialogueViewModel::HandleUpdated(const FWLDialogueView& View)
{
	UE_MVVM_SET_PROPERTY_VALUE(bActive, View.bActive);
	WL_SET_TEXT(Speaker, View.Speaker);
	WL_SET_TEXT(Line, View.Line);
	Choices = View.Choices;
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Choices);
	UE_MVVM_SET_PROPERTY_VALUE(bHasMorePages, View.bHasMorePages);
	NotifyChanged();
}

void UWLDialogueViewModel::HandleEnded()
{
	UE_MVVM_SET_PROPERTY_VALUE(bActive, false);
	Choices.Reset();
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Choices);
	NotifyChanged();
}

void UWLDialogueViewModel::HandleUIRequest(FName Request)
{
	if (Request == TEXT("EggChoice"))
	{
		UE_MVVM_SET_PROPERTY_VALUE(bEggChoiceOpen, true);
		NotifyChanged();
		return;
	}
	if (Request == TEXT("Bank"))
	{
		return; // the bank opens server-side and the bank view model follows OnBankOpened
	}
	OnUIRequest.Broadcast(Request);
}

void UWLDialogueViewModel::Continue()
{
	const UWLUISubsystem* Root = UI.Get();
	if (Root && Root->GetChat()->bTyping)
	{
		return; // Unity: don't advance while the player is typing in chat
	}
	if (UWLDialogueComponent* Dlg = UWLDialogueComponent::Get(PS.Get()))
	{
		// With choices showing, Continue does nothing; a choice must be picked.
		if (Choices.Num() == 0)
		{
			Dlg->Continue();
		}
	}
}

void UWLDialogueViewModel::Choose(int32 ChoiceIndex)
{
	if (!Choices.IsValidIndex(ChoiceIndex))
	{
		return;
	}
	if (UWLDialogueComponent* Dlg = UWLDialogueComponent::Get(PS.Get()))
	{
		Dlg->Choose(ChoiceIndex);
	}
}

void UWLDialogueViewModel::End()
{
	if (UWLDialogueComponent* Dlg = UWLDialogueComponent::Get(PS.Get()))
	{
		Dlg->RequestEnd();
	}
}

void UWLDialogueViewModel::ChooseStarterBeast(FName Species)
{
	if (AWLPlayerState* P = PS.Get())
	{
		if (UWLCompanionComponent* Comp = P->GetCompanions())
		{
			Comp->RequestChooseStarterBeast(Species);
		}
	}
	CloseEggChoice();
}

void UWLDialogueViewModel::CloseEggChoice()
{
	if (UE_MVVM_SET_PROPERTY_VALUE(bEggChoiceOpen, false))
	{
		NotifyChanged();
	}
}

// =================================================================================================
// Context menu
// =================================================================================================

void UWLContextMenuViewModel::OnBind()
{
	if (AWLPlayerController* Controller = PC.Get())
	{
		Controller->OnContextMenuRequested.AddUniqueDynamic(this, &UWLContextMenuViewModel::HandleContextMenuRequested);
	}
	Close();
}

void UWLContextMenuViewModel::OnUnbind()
{
	if (AWLPlayerController* Controller = PC.Get())
	{
		Controller->OnContextMenuRequested.RemoveDynamic(this, &UWLContextMenuViewModel::HandleContextMenuRequested);
	}
}

void UWLContextMenuViewModel::HandleContextMenuRequested(AActor* Target, FVector2D InScreenPosition)
{
	OpenForActor(Target, InScreenPosition);
}

void UWLContextMenuViewModel::OpenForActor(AActor* Target, FVector2D InScreenPosition)
{
	AWLPlayerCharacter* Body = Pawn.Get();
	UWLInteractorComponent* Interactor = Body ? Body->GetInteractor() : nullptr;
	if (!Target || !Interactor)
	{
		Close();
		return;
	}
	Entries.Reset();
	// The interactor lists the target's options, default first, then Examine (answered locally).
	for (const FWLInteractOption& Option : Interactor->GetOptionsFor(Target))
	{
		FWLMenuEntry& E = Entries.AddDefaulted_GetRef();
		E.Verb = Option.Verb;
		E.Label = Option.Label.IsEmpty() ? FText::FromName(Option.Verb) : Option.Label;
	}
	if (Entries.Num() == 0)
	{
		Close();
		return;
	}
	FWLMenuEntry& CancelEntry = Entries.AddDefaulted_GetRef();
	CancelEntry.Verb = WLItemVerbs::Cancel;
	CancelEntry.Label = FWLUILogic::VerbLabel(WLItemVerbs::Cancel);

	TargetActor = Target;
	TargetSlot = INDEX_NONE;
	WL_SET_TEXT(Title, Interactor->GetNameFor(Target));
	UE_MVVM_SET_PROPERTY_VALUE(ScreenPosition, InScreenPosition);
	UE_MVVM_SET_PROPERTY_VALUE(bOpen, true);
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Entries);
	NotifyChanged();
}

void UWLContextMenuViewModel::OpenForInventorySlot(int32 SlotIndex, FVector2D InScreenPosition)
{
	UWLUISubsystem* Root = UI.Get();
	if (!Root)
	{
		return;
	}
	Entries = Root->GetInventory()->GetMenuForSlot(SlotIndex);
	if (Entries.Num() == 0)
	{
		Close();
		return;
	}
	TargetActor.Reset();
	TargetSlot = SlotIndex;
	WL_SET_TEXT(Title, Root->GetInventory()->GetSlotTitle(SlotIndex));
	UE_MVVM_SET_PROPERTY_VALUE(ScreenPosition, InScreenPosition);
	UE_MVVM_SET_PROPERTY_VALUE(bOpen, true);
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Entries);
	NotifyChanged();
}

void UWLContextMenuViewModel::Choose(int32 EntryIndex)
{
	if (!bOpen || !Entries.IsValidIndex(EntryIndex))
	{
		Close();
		return;
	}
	const FName Verb = Entries[EntryIndex].Verb;
	const TWeakObjectPtr<AActor> Target = TargetActor;
	const int32 Slot = TargetSlot;
	Close();

	if (Verb == WLItemVerbs::Cancel)
	{
		return;
	}
	if (Slot != INDEX_NONE)
	{
		if (UWLUISubsystem* Root = UI.Get())
		{
			Root->GetInventory()->PerformVerb(Slot, Verb);
		}
		return;
	}
	AWLPlayerCharacter* Body = Pawn.Get();
	if (Body && Target.IsValid() && Body->GetInteractor())
	{
		if (AWLPlayerController* Controller = PC.Get())
		{
			Controller->StopPathFollowing();
		}
		Body->GetInteractor()->RequestInteract(Target.Get(), Verb);
	}
}

void UWLContextMenuViewModel::Close()
{
	TargetActor.Reset();
	TargetSlot = INDEX_NONE;
	if (UE_MVVM_SET_PROPERTY_VALUE(bOpen, false))
	{
		NotifyChanged();
	}
}

// =================================================================================================
// Minimap
// =================================================================================================

void UWLMinimapViewModel::TickViewModel(float DeltaSeconds)
{
	const AWLPlayerCharacter* Body = Pawn.Get();
	if (!Body)
	{
		return;
	}
	ScanTimer -= DeltaSeconds;
	if (ScanTimer <= 0.f)
	{
		ScanTimer = ScanInterval;
		ScanWorld();
	}
	Rebuild();
}

void UWLMinimapViewModel::ScanWorld()
{
	// Creatures, NPCs, players and gathering spots: refreshed every couple of seconds rather than every
	// frame (Unity MinimapHUD). Ground items come live from their subsystem in Rebuild.
	Tracked.Reset();
	const AWLPlayerCharacter* Body = Pawn.Get();
	UWorld* World = Body ? Body->GetWorld() : nullptr;
	if (!World)
	{
		return;
	}
	const FVector Center = Body->GetActorLocation();
	const float ScanRadiusSq = FMath::Square(Radius * 1.5f);
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		AActor* A = *It;
		if (!A || A == Body || A->IsA<AWLGroundItem>() || FVector::DistSquared2D(A->GetActorLocation(), Center) > ScanRadiusSq)
		{
			continue;
		}
		if (const AWLEnemyCharacter* Enemy = Cast<AWLEnemyCharacter>(A))
		{
			if (!Enemy->IsDead())
			{
				Tracked.Add({ A, EWLMinimapMarkerKind::Creature, EWLSkill::Attack });
			}
		}
		else if (const AWLResourceNode* Node = Cast<AWLResourceNode>(A))
		{
			Tracked.Add({ A, EWLMinimapMarkerKind::GatherNode, Node->GetSkill() });
		}
		else if (A->IsA<AWLPlayerCharacter>())
		{
			Tracked.Add({ A, EWLMinimapMarkerKind::Player, EWLSkill::Attack });
		}
		else if (A->Implements<UWLInteractable>() && A->IsA<APawn>())
		{
			Tracked.Add({ A, EWLMinimapMarkerKind::Npc, EWLSkill::Attack });
		}
	}
}

void UWLMinimapViewModel::Rebuild()
{
	const AWLPlayerCharacter* Body = Pawn.Get();
	if (!Body)
	{
		return;
	}
	const FVector Center = Body->GetActorLocation();
	const float CamYaw = Body->GetCameraBoom() ? Body->GetCameraBoom()->GetComponentRotation().Yaw : Body->GetActorRotation().Yaw;

	Markers.Reset();
	auto Add = [this, &Center, CamYaw](const FVector& Loc, EWLMinimapMarkerKind Kind, EWLSkill Skill, bool bClamp)
	{
		bool bRim = false, bVisible = true;
		const FVector2D Map = FWLUILogic::WorldToMap(Loc - Center, CamYaw, Radius, bClamp, bRim, bVisible);
		if (!bVisible)
		{
			return;
		}
		FWLMinimapMarker& M = Markers.AddDefaulted_GetRef();
		M.Kind = Kind;
		M.WorldLocation = Loc;
		M.MapPosition = Map;
		M.bOnRim = bRim;
		M.Skill = Skill;
	};

	for (const FTracked& T : Tracked)
	{
		if (const AActor* A = T.Actor.Get())
		{
			Add(A->GetActorLocation(), T.Kind, T.Skill, false);
		}
	}
	// Ground items just off the map sit on the rim so you can see which way to go.
	if (const UWLGroundItemSubsystem* Ground = UWLGroundItemSubsystem::Get(Body))
	{
		for (const TWeakObjectPtr<AWLGroundItem>& Item : Ground->GetAll())
		{
			if (const AWLGroundItem* G = Item.Get())
			{
				Add(G->GetActorLocation(), EWLMinimapMarkerKind::GroundItem, EWLSkill::Attack, true);
			}
		}
	}
	if (const AWLPlayerState* P = PS.Get())
	{
		FVector Point;
		if (const UWLQuestComponent* Quests = P->GetQuests(); Quests && Quests->GetObjective().bActive && Quests->GetObjective().GetPoint(Point))
		{
			Add(Point, EWLMinimapMarkerKind::Objective, EWLSkill::Attack, true);
		}
	}
	if (const AWLPlayerController* Controller = PC.Get(); Controller && Controller->HasDestination())
	{
		Add(Controller->GetDestination(), EWLMinimapMarkerKind::Destination, EWLSkill::Attack, true);
	}

	UE_MVVM_SET_PROPERTY_VALUE(CameraYaw, CamYaw);
	UE_MVVM_SET_PROPERTY_VALUE(PlayerHeadingOnMap, FRotator::NormalizeAxis(Body->GetActorRotation().Yaw - CamYaw));
	UE_MVVM_SET_PROPERTY_VALUE(PlayerLocation, Center);
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Markers);
	NotifyChanged();
}

void UWLMinimapViewModel::WalkToMapPoint(FVector2D MapPos)
{
	AWLPlayerController* Controller = PC.Get();
	const AWLPlayerCharacter* Body = Pawn.Get();
	if (!Controller || !Body || MapPos.SizeSquared() > 1.f)
	{
		return;
	}
	FVector Dest = Body->GetActorLocation() + FWLUILogic::MapToWorld(MapPos, CameraYaw, Radius);
	// Ground height at that point (a tap over a roof still means the ground below it).
	FHitResult Hit;
	const FVector Top = Dest + FVector(0.f, 0.f, 5000.f);
	const FVector Bottom = Dest - FVector(0.f, 0.f, 5000.f);
	FCollisionQueryParams Params(SCENE_QUERY_STAT(WLMinimapGround), false, Body);
	if (Body->GetWorld()->LineTraceSingleByChannel(Hit, Top, Bottom, ECC_WorldStatic, Params))
	{
		Dest = Hit.ImpactPoint;
	}
	if (UWLInteractorComponent* Interactor = Body->GetInteractor())
	{
		Interactor->CancelPendingInteraction();
	}
	Controller->MoveToLocation(Dest);
}

void UWLMinimapViewModel::LookToward(float WorldYaw)
{
	if (AWLPlayerCharacter* Body = Pawn.Get())
	{
		const float Current = Body->GetCameraBoom() ? Body->GetCameraBoom()->GetComponentRotation().Yaw : 0.f;
		Body->AddCameraYaw(FRotator::NormalizeAxis(WorldYaw - Current));
	}
}

// =================================================================================================
// Settings + music
// =================================================================================================

void UWLSettingsViewModel::OnBind()
{
	if (UWLMusicSubsystem* Music = UWLMusicSubsystem::Get(PC.Get()))
	{
		BoundMusic = Music;
		MusicHandle = Music->OnTrackChanged.AddUObject(this, &UWLSettingsViewModel::RefreshMusic);
	}
	Refresh();
}

void UWLSettingsViewModel::OnUnbind()
{
	if (UWLMusicSubsystem* Music = BoundMusic.Get())
	{
		Music->OnTrackChanged.Remove(MusicHandle);
	}
	BoundMusic.Reset();
	MusicHandle.Reset();
	BaseKeyOrbitSpeed = BaseMouseOrbitSpeed = -1.f;
}

void UWLSettingsViewModel::Refresh()
{
	const UWLGameUserSettings* S = UWLGameUserSettings::GetWLSettings();
	UE_MVVM_SET_PROPERTY_VALUE(MasterVolume, S->GetMasterVolume());
	UE_MVVM_SET_PROPERTY_VALUE(MusicVolume, S->GetMusicVolume());
	UE_MVVM_SET_PROPERTY_VALUE(EffectsVolume, S->GetEffectsVolume());
	UE_MVVM_SET_PROPERTY_VALUE(InterfaceScale, S->GetInterfaceScale());
	UE_MVVM_SET_PROPERTY_VALUE(CameraOrbitSpeed, S->CameraOrbitSpeed);
	UE_MVVM_SET_PROPERTY_VALUE(LongPressMs, S->LongPressMs);
	RefreshMusic();
}

void UWLSettingsViewModel::RefreshMusic()
{
	Tracks.Reset();
	if (const UWLMusicSubsystem* Music = BoundMusic.Get())
	{
		for (int32 i = 0; i < Music->NumTracks(); ++i)
		{
			FWLMusicTrackView& T = Tracks.AddDefaulted_GetRef();
			T.Index = i;
			T.Name = FText::FromName(Music->GetTrackName(i));
			T.bEnabled = Music->IsTrackEnabled(i);
			T.bPlaying = i == Music->GetCurrentIndex();
		}
		WL_SET_TEXT(NowPlaying, FText::Format(LOCTEXT("NowPlaying", "Now playing: {0}"), Music->GetCurrentTrackDisplayName()));
	}
	else
	{
		WL_SET_TEXT(NowPlaying, LOCTEXT("NoMusic", "Now playing: (nothing)"));
	}
	UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED(Tracks);
	NotifyChanged();
}

void UWLSettingsViewModel::Save() const
{
	UWLGameUserSettings::GetWLSettings()->SaveSettings();
}

void UWLSettingsViewModel::SetMasterVolume(float V)
{
	UWLGameUserSettings* S = UWLGameUserSettings::GetWLSettings();
	S->SetMasterVolume(V);
	S->ApplyAudio(PC.Get());
	Save();
	Refresh();
}

void UWLSettingsViewModel::SetMusicVolume(float V)
{
	UWLGameUserSettings* S = UWLGameUserSettings::GetWLSettings();
	S->SetMusicVolume(V);
	S->ApplyAudio(PC.Get());
	Save();
	Refresh();
}

void UWLSettingsViewModel::SetEffectsVolume(float V)
{
	UWLGameUserSettings* S = UWLGameUserSettings::GetWLSettings();
	S->SetEffectsVolume(V);
	S->ApplyAudio(PC.Get());
	Save();
	Refresh();
}

void UWLSettingsViewModel::SetInterfaceScale(float V)
{
	UWLGameUserSettings::GetWLSettings()->SetInterfaceScale(V);
	Save();
	Refresh();
}

void UWLSettingsViewModel::SetCameraOrbitSpeed(float V)
{
	UWLGameUserSettings::GetWLSettings()->CameraOrbitSpeed = FMath::Clamp(V, 0.25f, 3.f);
	Save();
	ApplyCameraSettings();
	Refresh();
}

void UWLSettingsViewModel::SetLongPressMs(int32 Ms)
{
	UWLGameUserSettings::GetWLSettings()->LongPressMs = FMath::Clamp(Ms, UWLGameUserSettings::MinLongPressMs, UWLGameUserSettings::MaxLongPressMs);
	Save();
	Refresh();
}

void UWLSettingsViewModel::ApplyCameraSettings() const
{
	AWLPlayerController* Controller = PC.Get();
	if (!Controller)
	{
		return;
	}
	UWLSettingsViewModel* Self = const_cast<UWLSettingsViewModel*>(this);
	if (Self->BaseKeyOrbitSpeed < 0.f)
	{
		// Remember the controller's authored speeds once, so the multiplier never compounds.
		Self->BaseKeyOrbitSpeed = Controller->KeyOrbitSpeed;
		Self->BaseMouseOrbitSpeed = Controller->MouseOrbitSpeed;
	}
	const float Mult = UWLGameUserSettings::GetWLSettings()->CameraOrbitSpeed;
	Controller->KeyOrbitSpeed = BaseKeyOrbitSpeed * Mult;
	Controller->MouseOrbitSpeed = BaseMouseOrbitSpeed * Mult;
}

void UWLSettingsViewModel::PlayTrack(int32 Index)
{
	if (UWLMusicSubsystem* Music = BoundMusic.Get())
	{
		Music->PlayTrack(Index);
	}
}

void UWLSettingsViewModel::ToggleTrack(int32 Index)
{
	if (UWLMusicSubsystem* Music = BoundMusic.Get())
	{
		Music->SetTrackEnabled(Index, !Music->IsTrackEnabled(Index));
	}
}

void UWLSettingsViewModel::SkipTrack()
{
	if (UWLMusicSubsystem* Music = BoundMusic.Get())
	{
		Music->Next();
	}
}

#undef WL_SET_TEXT
#undef LOCTEXT_NAMESPACE
