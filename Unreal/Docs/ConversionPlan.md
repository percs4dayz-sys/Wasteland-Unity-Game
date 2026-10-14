# Wastelanders: Unity to Unreal Engine 5 MMORPG conversion plan

Status: in progress on branch `claude/ue5-conversion` (draft PR #6). Started 2026-10-14.

## Goal

Turn the single-player Unity game (OSRS-style skilling and tick combat in a mutated wasteland) into a
server-authoritative open world MMORPG on Unreal Engine 5 with modern rendering, keeping the game
design, numbers and progression intact.

**Not ported: Unity art.** Models, textures, materials, animations and audio from the Unity asset packs
do not fit the new art direction. The port carries code, systems and data only. Every asset reference
in the UE code is an empty slot (soft pointer / class reference) that falls back to engine placeholder
shapes until new art is authored.

## What the Unity project contains (audit)

| Area | Unity source | Size | Notes |
|---|---|---|---|
| Core tick | `Core/GameTick.cs` | small | 0.6 s fixed tick, OSRS cadence |
| Skills | `Skills/*` | ~1.1k lines | 13 skills, two XP curves (Endurance 10-220 is HP), beast tasks, skill guide |
| Items | `Items/*` | ~2.3k lines | static code registry: ~210 items, tiered gear, 70 combat modules, gear sets, gathering tools, journal lore |
| Combat | `Combat/*`, `Player3D/ActionCombat3D.cs`, `Enemy3D.cs` | ~3.5k lines | OSRS accuracy/max-hit math, stances, special energy, module procs, loot tables, companions |
| World | `World/*` | ~6k lines | resource nodes, crafting stations, spawners, tier gates, portals, building/housing, ambient life |
| NPCs / quests | `NPC/*`, `World/StarterGuide.cs`, `Blackwater*` | ~1.2k lines | Roxy chain, tutorial, wanderer, Blackwater arrival |
| Player | `Player/*`, `Player3D/*` | ~5k lines | movement, click-to-move, orbit camera, appearance, held models, animation |
| UI | `UI/*` | ~11k lines | uGUI HUD, inventory, bank, skills, crafting, chat, minimap, character create/select |
| Save | `Save/*` | ~300 lines | one JSON file per world on the local disk |
| Editor tools | `Assets/Editor`, `Scripts/Editor` | ~24k lines | world builders, asset audits, Android tweaks. Mostly asset-pipeline tooling; not ported |

The project is single player: player data hangs off static singletons (`PlayerEntity.Instance`), the
client decides every outcome, and the save is a local file. Those are the three things the MMO
conversion has to change everywhere.

## Target architecture

```
Clients (UE5, Lumen/Nanite)            Dedicated world servers (UE5 Server target)       Services
  input, prediction, UI, VFX   <--->     authoritative simulation per shard/region   <--->  character DB
  CMC-predicted movement                 0.6 s game tick for skilling/combat timing          account/auth (EOS)
  replicated state -> view models        GAS for combat, AI, loot, crafting                  chat, trade, market
```

### Mapping of Unity systems to UE5

| Unity | UE5 replacement | Where |
|---|---|---|
| `GameTick` MonoBehaviour singleton | `UWLGameTickSubsystem` (tickable world subsystem, server only) + `IWLTickable`; tick count replicated through `AWLGameState` | `Core/` |
| `ItemRegistry`, `CraftingRecipes`, `ModuleCatalog`, `GearSets` static classes | DataTables (`DT_Items`, `DT_Recipes`, `DT_Modules`, `DT_GearSets`, `DT_Skills`) generated from the Unity code by `Tools/UnityDataExport`, served by `UWLDataSubsystem` | `Data/`, `Content/Data` |
| `PlayerEntity` + `PlayerStats` singletons | `AWLPlayerState` with replicated components: skills, inventory, equipment, bank, flags, quests, companions; GAS ASC on the PlayerState | `Player/`, `Skills/`, `Items/`, `Quests/` |
| `Player3DController`, `ClickToMove3D`, `OrbitCamera3D` | `AWLPlayerCharacter` + `UWLCharacterMovementComponent` (client-predicted run and dodge via saved-move flags) + `AWLPlayerController` (Enhanced Input built in code, client-side navmesh path following) | `Player/` |
| `ActionCombat3D`, `CombatManager`, `CombatMath`, `CombatTarget` | Gameplay Ability System: `UWLAbilitySystemComponent`, `UWLCombatAttributeSet`, attack/special/reload abilities; `UWLCombatComponent` runs the tick-based attack loop on the server; `FWLCombatMath` keeps the OSRS formulas | `AbilitySystem/`, `Combat/` |
| `Enemy3D`, `CreatureMotion`, `EnemySpawner` | `AWLEnemyCharacter` (own ASC) + `AWLEnemyAIController` server state machine; StateTree later for richer bosses | `Combat/`, `World/` |
| `EnemyLoot`, `MonsterDropTable` ScriptableObject | `UWLLootLibrary` + `UWLDropTableAsset`; loot is owner-locked for 60 s (MMO loot rules) | `Items/` |
| `ResourceNode`, `SkillingManager`, `CraftingManager`, `CraftingStation` | `AWLResourceNode`, `AWLCraftingStation` implementing `IWLInteractable`; `UWLSkillingComponent` runs gathering/crafting on the tick | `World/`, `Skilling/` |
| `Interactor3D`, `ContextMenuUI`, `IExaminable` | `IWLInteractable` + `UWLInteractorComponent` (client walks into range, server validates and executes) | `Interaction/` |
| `RoxyNPC`, `TutorialNPC`, `DialogueUI`, `BeastTasks`, `JournalManager` | `AWLNPC`, data-driven `UWLDialogueAsset` with server-owned conversation state; Unity dialogue rebuilt in code so it works with zero assets; `UWLQuestComponent` | `NPC/`, `Dialogue/`, `Quests/` |
| `CompanionManager` | `UWLCompanionComponent` + `AWLCompanionPawn` (server AI) | `Companions/` |
| `SaveManager` local JSON | `UWLPersistenceSubsystem` with a pluggable backend; JSON-file backend for development; Unity saves importable | `Persistence/` |
| uGUI panels | CommonUI widgets + view models; an asset-free Canvas debug HUD so the game is playable before UMG layouts exist | `UI/` |
| `WorldCellStreamer`, `WorldNavMesh`, terrain tiles | World Partition (streaming, data layers per tier), runtime navmesh with invokers, HLODs | engine features |
| URP, baked lighting, `SceneLightingHealer` | Lumen GI and reflections, Nanite, virtual shadow maps, hardware ray tracing, sky atmosphere and volumetric clouds | `Config/DefaultEngine.ini` |
| `MusicManager`, `AudioEventPlayer` | `UWLMusicSubsystem`, MetaSounds later | `Audio/` |
| `ChatCommands` (`/stuck`, `/help`, `/ai`) | `FWLChatCommands` on the server; `/ai` dropped (it shipped a third-party API key in the client) | `Player/` |

### Networking model

- Dedicated servers only (`WastelandersServer.Target.cs`). Clients never decide outcomes: every
  inventory move, equip, attack, gather and craft is a validated server RPC.
- Replicated data is split by audience: owner-only for bank, flags, quest state; everyone for
  equipment, appearance, HP and combat level. Slot arrays use `FFastArraySerializer` so one changed
  slot sends one slot.
- Movement uses the stock predicted Character Movement Component with custom saved-move flags.
- GAS runs in Mixed replication mode for players (full effects to the owner, minimal to others).
- Scale path, in order: (1) tune net relevancy and dormancy (resource nodes and doors go dormant),
  (2) enable Iris replication, (3) Replication Graph with spatial grid for crowded hubs,
  (4) multiple world servers per region with travel between them (zones match the Unity tier areas),
  (5) evaluate server meshing only if one region needs more than a single server can hold.

### Persistence

The game only ever reads and writes `FWLCharacterSave`. Development uses one JSON file per character
under `Saved/Characters`. Production swaps in a database backend behind `IWLPersistenceBackend`:
a small service (Postgres for characters and bank, Redis for sessions and presence) that the world
servers call over HTTP or gRPC. Autosave every 60 s plus on logout, level-up and rare drops. Old Unity
saves (`wasteland_save.json`) import into a character record, keeping XP, inventory, bank, equipment
and flags, including the legacy skill renames.

### World

- One persistent open world map with World Partition. Each Unity tier area (Tier 1-5 overworld,
  Broken Crescent, Blackwater) becomes a region with its own data layer, so a server can load only the
  regions it hosts.
- Terrain is rebuilt in Landscape with PCG for scatter (rubble, dead trees, scrap), water via the
  Water plugin. Nothing from the Unity terrain or prefabs is reused because the art is changing.
- Navigation: runtime navmesh generation with invokers on players and enemies.
- Dungeons (Unity used DunGen) become instanced levels streamed on demand, one instance per party.

### Rendering targets

Lumen GI and reflections with hardware ray tracing where supported, Nanite for all static geometry,
virtual shadow maps, TSR anti-aliasing, virtual textures, DX12/SM6 and Vulkan SM6. Scalability
presets need a low tier without hardware RT for older GPUs.

## Phases

1. **Foundation** (done in this PR): project, targets, config, data export, core types, tick,
   data subsystem, framework classes (player state, character, controller, game mode, game state),
   porting conventions.
2. **Systems port** (in this PR): items and loot, skills and combat on GAS, enemies and AI,
   interaction, gathering and crafting, world objects and spawners, building, NPCs, dialogue, quests,
   companions, persistence, view models and the debug HUD.
3. **First playable** (needs the editor): compile, import the DataTables, build a graybox test map
   with resource nodes, stations, an enemy spawner and NPCs placed, run a listen server plus a client,
   fix what breaks.
4. **Art direction**: new character, creature, weapon and environment art; modular character
   system for armour; animation (Motion Matching is a good fit for the movement style).
5. **MMO services**: account login (EOS), character select listing from the database, the database
   persistence backend, chat channels, trading, a player market, parties.
6. **Content and scale**: the full world in World Partition, dungeons as instances, load testing,
   replication tuning, region servers.

## Verifying this PR in the editor

The port was written in a cloud container without Unreal, so nothing has been compiled yet.

1. Install UE 5.6 (Epic launcher is fine for client and editor; the dedicated server target needs a
   source build).
2. Right-click `Unreal/Wastelanders.uproject` > Generate project files, then build
   `WastelandersEditor` (Development Editor). Expect a round of compile fixes.
3. Open the editor, run Tools > Wastelanders > Import Game Data (or console `WL.ImportGameData`).
   It creates `/Game/Data/DT_*` from `Content/Data/*.json`.
4. Session Frontend > Automation: run `Wastelanders.*` tests.
5. Create a graybox map `Maps/L_Wasteland` with a Player Start and a nav mesh volume; Play with
   Net Mode "Play As Listen Server" and 2 players.

## Regenerating data

When item, recipe, skill, module or gear-set code changes in Unity:

```
cd Unreal/Tools/UnityDataExport
dotnet run -c Release
```

then re-run Import Game Data in the editor. Once the Unity project is retired, edit the DataTables
directly in Unreal instead and delete the exporter.
