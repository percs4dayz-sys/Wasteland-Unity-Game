# Porting conventions (Unity C# -> Unreal Engine 5 C++)

Read this before adding code under `Unreal/Source`. It is the contract every system in the port follows.

## Ground rules

- **Engine:** Unreal Engine 5.6, C++20, module `Wastelanders` (`WASTELANDERS_API`). Class prefix `WL`
  (`AWLResourceNode`, `UWLInventoryComponent`, `FWLItemStack`, `EWLSkill`).
- **Layout:** headers in `Source/Wastelanders/Public/<System>/`, sources in `Private/<System>/`.
  Include paths are relative to `Public/`: `#include "Items/WLInventoryComponent.h"`.
- **No Unity art.** Models, textures, materials, animations and audio from the Unity project are NOT
  ported (they don't fit the new art direction). Asset references are empty `TSoftObjectPtr` /
  `TSubclassOf` slots for new art; code must work with them unset (engine basic shapes, debug draw,
  or nothing). Never reference `Assets/...` paths.
- **Data comes from DataTables**, not hard-coded registries. Look items up by stable numeric id through
  `UWLDataSubsystem::Get(this)->FindItem(Id)`. Recipes by row name. Never duplicate item stats in code.
  If Unity hard-codes balance tables (loot odds, spawn tiers), port them as `UPROPERTY` defaults on a
  `UDataAsset`/settings class so designers can tune them, with the Unity numbers as the defaults.
- **Port behaviour faithfully.** The Unity code carries many hard-won fixes in its comments (dupes,
  off-by-ones, softlocks). Keep the fix and a one-line note of why. Keep the OSRS-style rules.

## MMO rules (the big change from Unity)

Unity was single player with static singletons (`PlayerEntity.Instance`). Here:

- **Server authoritative.** All gameplay state changes happen on the server (`HasAuthority()`).
  Clients send intent through `Server` RPCs on components they own (PlayerState / pawn / controller
  components). Every server RPC re-validates: item present, level met, in range, not dead, rate limits.
- **No singletons for player data.** Persistent character state lives in components on
  `AWLPlayerState`; per-body action state on `AWLPlayerCharacter`. Reach them through
  `PlayerState->GetInventory()`, `Character->GetCombat()`, etc.
- **Replication:** use `FFastArraySerializer` for slot arrays (inventory, bank) and `COND_OwnerOnly`
  for data only the owner needs (bank, flags, quest state). Things other players see (equipment,
  combat level, appearance, HP bar) replicate to everyone. Expose native multicast delegates
  (`OnInventoryChanged`, ...) that fire on both server and client (from OnRep) so UI can bind.
- **Game tick:** deterministic actions (gathering rolls, crafting cadence, attack cooldowns in ticks,
  eat delay) run on `UWLGameTickSubsystem` (0.6 s, server only). Implement `IWLTickable` and register
  in BeginPlay / unregister in EndPlay, or bind `OnGameTick`.
- **Randomness:** server only, via `FMath::FRand()` / `FMath::RandRange`, never on clients.
- **Cross-system events:** broadcast and listen through `UWLGameEventsSubsystem` (enemy killed,
  XP gained, level up, item gained, flag set, player died) instead of hard references.
- **Player messages:** `AWLPlayerController::ClientGameMessage(FText)` (the Unity chat log line).
  Get it via `Cast<AWLPlayerController>(PlayerState->GetOwningController())`.
- **Interaction:** world objects implement `IWLInteractable` (`Interaction/WLInteractable.h`).
  `Interact()` runs on the server and must validate.

## Save format

`Persistence/WLCharacterSave.h` defines `FWLCharacterSave`. Each persistent component implements
`void CaptureSave(FWLCharacterSave& Out) const;` and `void ApplySave(const FWLCharacterSave& In);`
and touches only its own fields. Field names are on-disk JSON keys: never rename.

## Shared classes and the API each owner provides

| Class | Header | Owner | Required API (others call these) |
|---|---|---|---|
| `AWLPlayerState` | `Player/WLPlayerState.h` | framework | getters for every component below, `GetCharacterId`, `IncrementKills`, `GetCombatLevel` |
| `AWLPlayerCharacter` | `Player/WLPlayerCharacter.h` | framework | `GetCombat()`, `GetInteractor()`, `GetSkilling()`, `GetWLPlayerState()`, `OnAbilitySystemReady` |
| `AWLPlayerController` | `Player/WLPlayerController.h` | framework | `ClientGameMessage`, `MoveToLocation`, `StopPathFollowing`, `OnContextMenuRequested` |
| `AWLGameMode` | `Core/WLGameMode.h` | framework | `RequestRespawn(Controller, Delay)`, `TeleportToSpawn`, `SavePlayer` |
| `UWLInventoryComponent` | `Items/WLInventoryComponent.h` | items | `AddItem(Id, Qty)->bool`, `AddStack(const FWLItemStack&)->bool`, `RemoveItem(Id, Qty)->bool`, `RemoveAt(Index, Qty)->bool`, `CountOf(Id)`, `Contains(Id, Qty)`, `FreeSlots()`, `GetCapacity()`, `GetSlot(Index)`, `SetBonusSlots(N)`, `OnInventoryChanged` |
| `UWLEquipmentComponent` | `Items/WLEquipmentComponent.h` | items | `GetItemId(Slot)`, `GetItem(Slot)->const FWLItemRow*`, `GetModules(Slot)`, `TotalAttackBonusFor(EWLWeaponStyle)`, `TotalStrengthBonus()`, `TotalDefenceBonus()`, `AmmoStrengthBonus()`, `GetAmmoQuantity()`, `ConsumeAmmo(Qty)->bool`, `GetSetDamageMultiplier(Style)`, `GetSetBonusDefence(Style)`, `ModulePower(FName Effect)->int32`, `OnEquipmentChanged` |
| `UWLBankComponent` | `Items/WLBankComponent.h` | items | `Deposit`, `Withdraw`, `OnBankChanged` |
| `FWLItemStack` | `Items/WLItemTypes.h` | items | `ItemId`, `Quantity`, `Modules` |
| `UWLLootLibrary` | `Items/WLLootLibrary.h` | items | `static void DropEnemyLoot(const FWLEnemyLootParams& Params, const FVector& Location, AWLPlayerState* Killer)` |
| `AWLGroundItem` | `Items/WLGroundItem.h` | items | `static AWLGroundItem* Spawn(UWorld*, const FWLItemStack&, const FVector&, AWLPlayerState* Owner)` |
| `UWLSkillsComponent` | `Skills/WLSkillsComponent.h` | combat | `AddXP(Skill, Amount)`, `GetLevel(Skill)`, `GetXP(Skill)`, `GetTotalLevel()`, `GetCombatLevel()`, `OnSkillsChanged`, `OnLevelUp` |
| `UWLAbilitySystemComponent` | `AbilitySystem/WLAbilitySystemComponent.h` | combat | `ApplyHeal(int32)`, `HealToFull()`, `RestoreHealth(int32 SavedHP)` (0 = full), `GetCurrentHealthInt()`, `Revive()`, `IsDead()` |
| `UWLCombatAttributeSet` | `AbilitySystem/WLCombatAttributeSet.h` | combat | Health, MaxHealth, SpecialEnergy, MaxSpecialEnergy (+ meta Damage, Healing) |
| `UWLCombatComponent` | `Combat/WLCombatComponent.h` | combat | `GetStyle()`, `GetStance()`, `ServerSetStance`, `SetTarget`, `DelayAttackAfterEating(Ticks)`, `RefreshStyleFromEquipment()` |
| `AWLEnemyCharacter` | `Combat/WLEnemyCharacter.h` | combat | `GetTier()`, `IsBoss()`, `IsDead()`, `OnEnemyDied` (native multicast, `AWLEnemyCharacter*`) |
| `UWLInteractorComponent` | `Interaction/WLInteractorComponent.h` | world | `RequestInteract(AActor*, FName Verb)` (client), `CancelPendingInteraction()` |
| `UWLSkillingComponent` | `Skilling/WLSkillingComponent.h` | world | `StartGathering(Node)`, `ServerCraft(RecipeRowName, Count)`, `StopAction()`, `IsBusy()` |
| `FWLGatheringTools` | `Skilling/WLGatheringTools.h` | world | `BestFor(PlayerState, RequiredToolId)`, `CycleSpeed(...)` |
| `UWLPlayerFlagsComponent` | `Quests/WLPlayerFlagsComponent.h` | quests | `HasFlag`, `SetFlag`, `RemoveFlag`, `OnFlagsChanged` |
| `UWLQuestComponent` | `Quests/WLQuestComponent.h` | quests | journal pages, beast tasks, quest stages |
| `UWLCompanionComponent` | `Companions/WLCompanionComponent.h` | quests | summon/dismiss beasts, saddlebag bonus slots |
| `UWLPersistenceSubsystem` | `Persistence/WLPersistenceSubsystem.h` | persistence | header is fixed; implement it |

## Verification

Unreal can't run in the cloud container this port is written in, so code is checked by inspection.
Keep it boringly correct: forward-declare in headers, include what you use in .cpp files, match
`GENERATED_BODY` / `.generated.h` names to file names, `UFUNCTION(Server, Reliable)` needs
`_Implementation` (add `WithValidation` + `_Validate` only when you want rejection to kick),
`GetLifetimeReplicatedProps` + `DOREPLIFETIME` for every `Replicated` property, and include
`Net/UnrealNetwork.h`. Pure logic gets an automation test under `Private/Tests/`
(`IMPLEMENT_SIMPLE_AUTOMATION_TEST`, inside `#if WITH_DEV_AUTOMATION_TESTS`).
