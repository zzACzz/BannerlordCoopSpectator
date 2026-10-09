# Code Map

`CoopCampaignMapConversationMissionScene` extends the tableau descriptor with
allowlisted land-scene/camera/climate data. `CoopCampaignMapMissionConversationVisualCapture`
reads the admitted native camera/visible actors; tableau and pose schema2 carry
resource identity and separate motion. `CoopCampaignMapConversationTableauView`
owns the terrain-scene branch. `ConversationMissionSceneTests` covers malformed
data, bounded framed poses and source wiring. Full lighting/mount/crowd/native
acceptance is open; see [current limits](CAMPAIGN_MAP_MISSION_DIALOGUE.md).

`Campaign/CoopCampaignMapConversationPublicationLease.cs` is a pure
map-anchor lifetime boundary for standalone companion dialogue. It binds exact
campaign/session/mission identities, revokes on context loss and never refreshes
snapshot timestamps. `ConversationPublicationLeaseTests` covers its boundaries.
The existing OpenNew postfix now binds it through PublisherBehavior.MissionConversation;
MissionConversationContext validates the native role/type/view and ConversationCapture
reads prepared mission text/HUD. Bounded mission 3D rendering is connected in
source through the types above, but not native-verified; see
[native investigation and next gates](CAMPAIGN_MAP_MISSION_DIALOGUE.md).

`GameMode/CoopCampaignMapLobbyLifetimePatch.cs` binds before server map opening;
`Infrastructure/CoopCampaignMapLobbyLifetimePolicy.cs` excludes only that map's
Playing lobby tick from stock match expiry. `LobbyLifetimeTests` checks gating
and wiring. See [failure and native gates](CAMPAIGN_MAP_LOBBY_LIFETIME.md).

`CoopCampaignMapSettlementEventPolicy` bounds event/quest bits by host inspection
or spotting; publisher and settlement overlay consume it. Exhaustive masks and
wiring: `SettlementEventDisclosureTests`. See [disclosure](CAMPAIGN_MAP_EVENT_DISCLOSURE.md).

`Infrastructure/CoopCampaignMapSettlementMarkerOrder.cs` is the pure bounded
ordered-event/quest representation; `SettlementMarkerOrderTests` enumerates every
ordered subset and malformed-input/disclosure cases. The publisher now reads the
existing host UI lists; full/dynamic contracts and message codecs carry their order,
and the settlement overlay preserves it without rebuilding unchanged lists.
`SettlementMarkerTransportTests` covers clones, codecs, order-only deltas, clearing,
party isolation and native message source wiring. Source format is32/27/8; installed
candidate is now cmp-b65 (32/27/8), staged for a fresh native run; acceptance is
pending. See integration/native gates in the same document.

`CoopCampaignMapEscapeInputPolicy` queues one owned-layer release for next-frame
native menu dispatch. Map and conversation views each own a policy instance;
conversation presentation defers queued host revisions while local modals own the
screen, including the native same-screen MissionOptions layer. The isolated map
mode explicitly adds the stock options view only on the client. `EscapeInputTests`
covers dispatch/cancellation and options/modal source wiring. See
[source correction and native gates](CAMPAIGN_MAP_ESCAPE_INPUT.md).

`Infrastructure/CoopCampaignMapMovementTarget.cs` defines detached host target
selection, hover suppression and decal layout; its Codec defines bounded wire,
disclosure and readiness rules. PublisherBehavior.MovementTarget captures host
state; state message/controller transport it; MissionView.MovementTargets owns
replica decals and teardown. MovementTargetTests and MovementTargetTransportTests
cover the managed rules. See [scope and native gates](CAMPAIGN_MAP_MOVEMENT_TARGETS.md).

`Infrastructure/CoopCampaignMapHoverDecal.cs` defines the bounded detached native
hover-frame/color descriptor. Host publisher captures it; snapshot/catalog codecs
and projection carry it; `UI/CoopCampaignMapPrototypeMissionView.HoverDecals.cs`
owns scene-local rendering and teardown. `HoverDecalTests.cs` covers wire validity,
transport and delta propagation. See [scope and native gates](CAMPAIGN_MAP_HOVER_DECALS.md).

`CoopCampaignMapPartyPickPolicy` in the prototype contract separates ordinary
hover admission from visible bodies: non-main rafts cannot be hovered. Picking,
pinned info and shared tooltip target admission consume it; host capture also
checks the current native raft flag. See the dated source-only refinement.

Native picking candidate: `UI/CoopCampaignMapPrototypeMissionView.Picking.cs`
owns map-scene ray hits, referenced settlement geometry and owned party spheres;
`Infrastructure/CoopCampaignMapPickResolver.cs` resolves bounded admitted hits in
audited native order. `PickResolverTests.cs` covers pure decisions and wiring.
Source/build checks are not native acceptance. See [scope and gaps](CAMPAIGN_MAP_RESUME_2026-09-26.md#native-scene-picking-candidate-2026-09-28-not-runtime-verified).

Current tooltip integration: controller partial .Tooltips owns exact-peer grants,
relay scheduling and unicast chunks; CoopCampaignMapTooltipNetworkMessages defines
three native messages; CoopCampaignMapTooltipChunks owns bounded assembly.
UI/CoopCampaignMapTooltipPresenter.cs uses the stock movie for detached rows;
MissionView supplies hover and lifecycle cleanup. TooltipPresenterTests compiles
the real presenter with isolated test doubles. Earlier "unwired" entries below
are historical. See [current implementation and gates](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-stock-presenter-candidate-2026-09-28-not-native-verified).

`CoopCampaignMapTooltipRelay` defines the fixed local binary channel;
`CoopCampaignMapTooltipRelayClient` is the not-yet-network-connected dedicated
endpoint. `Campaign/CoopCampaignMapTooltipHost.cs` owns launcher Prepare/Attach
and host tick capture. See [relay candidate](CAMPAIGN_MAP_RESUME_2026-09-26.md#launcher-owned-tooltip-relay-2026-09-28-source-candidate).

`CoopCampaignMapTooltipWireCodec`/`CoopCampaignMapTooltipHandshake` define bounded
semantic messages and reject grants from a previous client challenge. Publisher
`TryCapturePublishedTooltip` applies `CoopCampaignMapTooltipTargetAdmission` before
native target lookup/capture. These have no live relay/network/UI caller. See
[envelope and host-entry candidate](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-semantic-envelopes-and-host-lookup-2026-09-28-unwired).

`Campaign/CoopCampaignMapTooltipCapture.cs` is a host-only native-provider
adapter reached by the new source host relay, with disclosure/routing checks and
detached row capture; native MP submission/acceptance is still open. The pure
`CoopCampaignMapTooltipTextTransaction<T>` restores variable entries and idle
parser stacks. `CoopCampaignMapTooltipLanguageState` plus the pure thread-state
scope isolate audited grammar fields and reject shared-empty writable templates.
This is not arbitrary nested-state isolation; native gates precede live wiring. See
[candidate and safety boundary](CAMPAIGN_MAP_RESUME_2026-09-26.md#host-native-tooltip-capture-candidate-2026-09-28-unwired).

`Infrastructure/CoopCampaignMapTooltipInterest.cs` owns pure client correlation,
request-age expiry and detached display; `CoopCampaignMapTooltipPeerGate.cs` owns
one exact peer/map/session grant and bounded pending capture. Both are unwired;
their dedicated contract tests are not network/native acceptance. See
[lifetime boundary](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-request-lifetime-boundary-2026-09-28-candidate).

`Infrastructure/CoopCampaignMapTooltipPayload.cs` is an unwired pure detached-row
format and bounded UTF-8 codec; `TooltipPayloadTests` exercises presentation
preservation, request correlation and hostile input. No host capture, transport
or UI integration exists yet. See
[foundation and native-provider risks](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-provider-isolation-and-detached-payload-foundation-2026-09-28).

`CoopCampaignMapReplicaStateProjection.BuildVisible` projects latest admitted
catalog/dynamic stores when MissionView drains deferred reconciliation, so queued
clones cannot overwrite newer visibility/entry/exit/membership. PartyBodyTests
covers temporal projection and consumer wiring. See
[ordering correction](CAMPAIGN_MAP_RESUME_2026-09-26.md#deferred-reconciliation-ordering-correction-2026-09-28).

`CoopCampaignMapPartyBodyPolicy` (prototype contract) separately gates mobile
bodies using InSettlement. MissionView applies it to allocation/render/fallbacks,
hover and upsert-time panel invalidation; InfoVM tracks DisplayedEntityId.
`PartyBodyTests` covers admission transitions and source wiring. See
[correction and validation](CAMPAIGN_MAP_RESUME_2026-09-26.md#mobile-body-admission-correction-2026-09-28).

cmp-b10: Publisher.CaptureSettlementPresentation reads disclosed host wall health
and siege preparation inputs. SettlementPresentationPolicy bounds/codifies them;
MissionView.ApplySettlementPresentation applies wall then preparation overrides
after level filtering, before owned-root reveal. Full/catalog/binary/native paths
carry both fields (25/21/6). See [implementation and bounded native result](CAMPAIGN_MAP_RESUME_2026-09-26.md#wall-and-siege-preparation-candidate-2026-09-27).

MissionView.RemoveFortificationAuthoringHelpers snapshots exact startup helper tags
under owned town/castle roots. SettlementAuthoringPolicy selects topmost native
removal roots before mutation. See [cmp-b9](CAMPAIGN_MAP_RESUME_2026-09-26.md#fortification-authoring-helper-candidate-2026-09-27).

Settlement levels: SettlementLevelPolicy (in the geometry admission file) maps
semantic/native masks; Publisher.CaptureSettlementLevels reads admitted host
visuals; MissionView.ApplySettlementLevels changes only owned scene children.
Protocol 24/bridge 20/catalog 5 carry SettlementLevelFlags through existing paths.
See [implementation and bounded result](CAMPAIGN_MAP_RESUME_2026-09-26.md#host-selected-settlement-levels-2026-09-27-bounded-native-result).

CoopCampaignMapSettlementGeometryAdmission<T> owns pure per-scene root admission;
SettlementGeometryTests covers transitions/wiring. MissionView discovers hidden
roots, reveals typed settlement upserts, hides removals and clears before disposal.
See [candidate and limits](CAMPAIGN_MAP_RESUME_2026-09-26.md#static-settlement-geometry-admission-2026-09-27).

Map scene helpers (2026-09-27): MissionView.RemoveSettlementAuthoringCircles
snapshots/removes exact native helper-tag entities once after isolated Scene.Read.
It does not update settlement variants; root admission is now handled separately
as above. The variant gap is recorded
in the [native investigation](CAMPAIGN_MAP_RESUME_2026-09-26.md#settlement-scene-presentation-investigation-2026-09-27).

Map camera (2026-09-27): `CoopCampaignMapReplicaCameraPolicy` owns local follow/
detach/reset; `ReplicaCameraTests` covers policy and bindings. Mission view, info VM
and prefab implement the return button/input guards. Settlement tracked/inspected
bits cross publisher, full/dynamic contracts/codecs/messages and settlement overlay.
Both settlement culling gates share `ShouldShowSettlement`; tracked admitted
entities retain their projection for the native widget's screen-edge placement.
See [candidate and verification](CAMPAIGN_MAP_RESUME_2026-09-26.md#host-camera-following-and-settlement-presentation-2026-09-27).

Campaign mission lifecycle foundation (2026-09-26):
`Infrastructure/CoopCampaignMissionLifecycle.cs` is the pure one-shot map/battle
transition reconciler; `Tests/CoopCampaignMapPrototype.ContractTests/MissionLifecycleTests.cs`
exercises its transition, identity, stale-input and aftermath gates.
`Infrastructure/CoopCampaignMissionChannel.cs` provides bounded atomic transport
and process/freshness admission, tested by `MissionChannelTests.cs`.
`Campaign/CoopCampaignMissionHost.cs` connects the supported dedicated launcher,
battle roster admission and applied-result journal; `DedicatedServer/CoopCampaignMissionServer.cs`
connects the reconciler to native readiness/opening/result observations and commands.
`Infrastructure/CoopCampaignMissionOpening.cs` scopes the exact OpenNew return
capture to one native dispatch/thread, with `MissionOpeningTests.cs` rejection
coverage. The existing `MissionStateOpenNewPatches` postfix observes the return;
it does not initialize or mutate missions. See the 2026-09-27 native failure in
the lifecycle document for why immediate Mission.Current is not sufficient.
Both SubModule ticks call their adapters. `CoopPreMissionTopologyNetworkComponent.TryPublishPreparedMission`
supplies the pre-open scene contract. Separate default-off automatic-mission gate;
see [source implementation and native acceptance boundary](CAMPAIGN_MAP_AUTOMATIC_LIFECYCLE.md).

Local-isolation source map (2026-09-09): CoopAutomationRuntimeContract owns profile/path constants, ResolveCoopFolderPath, TryResolveContainedPath and LocalResultSentinelText; CoopAutomationRuntimeBridge supplies process configuration and the lazy production provider. Shared path factories route through it. CoopAutomationSpawnSmokeBridge admits/checks the local sentinel; the dedicated observer/control contract emit/validate its scope. CoopAutomationRunner.Core.ps1 contains the matching local-result helpers and v2 validators; Invoke-CoopTest.ps1 uses them only for DedicatedSpawnSmoke. Runtime, spawn-smoke and campaign-result guard suites cover this boundary; RuntimeCompileStubs.cs belongs only to the spawn-smoke test project. [Exact scope and evidence](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#11-local-only-file-isolation-2026-09-09).

Milestone 4A adds CoopAutomationSpawnSmokeContract, CoopAutomationSpawnSmokeBridge, CoopAutomationDedicatedSpawnSmokeObserver, the DedicatedSpawnSmoke command and CoopAutomationSpawnSmoke.ContractTests. The [M4 source map](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#2-ownership-and-source-map) lists exact integration methods. The canonical inventory has 24 projects.

Last source verification: **2026-08-28**
Last automation-control source verification: **2026-09-02**

## Repository map

| Path | Responsibility |
|---|---|
| `SubModule.cs` | Client/campaign startup, Harmony grouping, game-mode registration, campaign behavior/model registration |
| `CoopRuntime.cs` | Process-level access to `NetworkManager` |
| `CoopSpectator.csproj` | Client/campaign compilation, conditional game-mode inclusion, client deployment, default chained dedicated build |
| `DedicatedServer/CoopSpectatorDedicated.csproj` | Explicit dedicated source graph, dedicated references, runtime staging, server deployment |
| `Campaign/` | Encounter capture, snapshot creation, dedicated launch, campaign adapters, result validation/writeback, hero creation |
| `GameMode/` | Multiplayer mode registration and native/cooperative mission behavior composition |
| `Mission/` | Live mission state owners, spawn/phase lifecycle, network transport, battle power, scenario controllers |
| `MissionModels/` | Campaign-derived combat, damage, difficulty, and morale models used in multiplayer mission runtime |
| `Infrastructure/` | Snapshot/runtime state, contracts, bridges, scenario adapters, feature flags, exact-transfer utilities, and run-scoped automation contracts |
| `Network/` | TCP prototype layer, pre-mission topology component, and network message schemas |
| `Patches/` | Harmony boundaries around native lobby, mission load, spawn, control, UI, siege, and compatibility code |
| `UI/` | Gauntlet views/view-models for selection, deployment, hero creator, hideout, battle power, and map prototype |
| `Commands/` | `coop.*` console commands and synthetic test controls |
| `DedicatedHelper/` | Dedicated launch settings/commands, hosting mode, helper launcher, web-panel authentication |
| `Module/` | Client and dedicated module descriptors, XML game data, GUI prefabs, shader-cache helper |
| `Tests/` | Standalone `net8.0` contract-test executables linked to narrow production contracts |
| `scripts/` | Development loop, release packaging, installed-DLL inventory audit, repository hygiene, and battle-test client launcher core |
| `docs/` | Dated investigations, status reports, specifications, and the living `docs/ai/` index |

Generated and investigative areas such as `bin/`, `obj/`, `.buildcheck/`, `.codex_tmp*/`, `dist/`, `work/`, copied root DLLs, ZIP packages, and decompilation scratch files are not primary source.

## Primary entry points

### `SubModule.cs`

Start here for client/campaign startup questions.

Key responsibilities and methods:

- `OnSubModuleLoad`: reset state, initialize runtime/networking, apply patches, register modes.
- `TryApplyHarmonyPatches`: client patch groups and isolation flags.
- assembly-load callback: reapplies patches whose target assembly arrived late.
- `OnGameStart`: campaign behaviors or non-campaign mission-model wrappers.
- application tick: network dispatcher, campaign map prototype, hero creation, default-off lobby automation controller, and `BattleDetector.Tick`.
- `OnSubModuleUnloaded`: shutdown and event cleanup.

Important local constants at the top of the file define which manual patch groups are active. Do not infer them from patch class existence.

### `DedicatedServer/SubModule.cs`

Start here for dedicated startup, registration, observer, or web-panel questions.

Key responsibilities:

- module-load proof logging and exact scene-script registration;
- dedicated Harmony compatibility and patch registration;
- cooperative game-mode registration and official mode overrides;
- application-tick mission classification and stable-start observation;
- delayed attachment of `CoopMissionNetworkBridge` and `CoopMissionSpawnLogic`;
- dedicated knockout outcome and shared mission-model registration;
- web-panel hooks and server lifecycle cleanup.

### `Campaign/BattleDetector.cs`

The campaign orchestration hotspot. It owns far more than detection:

- decides when a campaign encounter is eligible;
- validates supported scenario contracts;
- resolves scenario kind, siege subtype, scene, map patch, mission shell, and atmosphere;
- builds `BattleStartMessage` and the complete `BattleSnapshotMessage`;
- writes `battle_roster.json`;
- starts the helper/dedicated mission or sends a start notification;
- monitors campaign mission exit and authoritative result availability;
- validates campaign/result identity and applies casualties, hero outcomes, loot, prisoners, XP, and scenario-specific aftermath;
- prevents duplicate writeback through stable IDs and journaling.

Search by method before scanning this very large file. Useful anchors include:

- `Tick`
- `TryGetUnsupportedCoopMissionReason`
- `TrySendBattleStart`
- `TryStartDedicatedMissionForCampaignHost`
- `BuildBattleStartPayload`
- `ResolveScenarioKind`
- `ResolveSiegeSubtype`
- `TryWriteBattleRosterFile`
- result/writeback methods containing `BattleResult`, `Aftermath`, `Casualty`, `Prisoner`, or `Journal`.

### `Mission/CoopMissionBehaviors.cs`

This file contains two large top-level behavior owners:

- `CoopMissionClientLogic`: client mission lifecycle, local presentation/observer behavior, result reception, cleanup.
- `CoopMissionSpawnLogic`: authoritative battle runtime, roster entry tracking, teams, phases, selection/spawn, agent ledger, native reinforcements, battle completion, and result production.

Dedicated runtime also calls static bootstrap/observer entry points on `CoopMissionSpawnLogic`. Because this file owns many static dictionaries and scenario gates, any modification requires a full scenario and sequential-mission reset review.

Useful search anchors:

- `TryRunDedicatedMissionNetworkBootstrap`
- `TryRunDedicatedMissionObserver`
- `RunCoopBattleSpawnOwnerTick`
- `RunCoopBattlePhaseOwnerTick`
- `TryCompleteBattleIfResolved`
- `TryResolveAuthoritativeTrackedEntryId`
- `MarkInitialClientMaterializationComplete`
- methods containing `InitialMaterialization`, `Reinforcement`, `Result`, `Reset`, or `BattleEnded`.

### `Mission/CoopMissionNetworkBridge.cs`

The in-mission network and deployment hotspot. It owns:

- full battle snapshot transport V2;
- client/server message registration;
- entry-status and materialized-agent mapping transfers;
- selection, spawn, spectator, control, reconnect, and phase messages;
- initial materialization readiness for multiple scenarios;
- field/village/sally-out deployment boundary state;
- commander formation state and siege-machine state;
- siege mission-object ID maps and ladder interaction state;
- server/client readiness barriers and retry timers.

Use symbol search aggressively. Relevant method groups are clustered by message handler or scenario name, but this is not a small single-purpose class.

## Campaign adapters and aftermath

| Scenario | Campaign capture/validation | Result/aftermath |
|---|---|---|
| Field/general land | `Campaign/LandBattle/ExactLandBattleCampaignBattleAdapter.cs` | `ExactLandBattleNativeAftermathBridge.cs`, `ExactLandBattleEncounterContinuationPatch.cs` |
| Village | `Campaign/VillageBattle/ExactVillageBattleCampaignBattleAdapter.cs` | general land/native aftermath plus scenario data |
| Siege assault | `Campaign/SiegeAssault/ExactSiegeAssaultCampaignBattleAdapter.cs` | `ExactSiegeAssaultNativeAftermathRuntime.cs`, `ExactSiegeAssaultNativeAftermathCommitPatch.cs` |
| Sally out | `Campaign/SallyOut/SallyOutCampaignBattleAdapter.cs` | general result pipeline with sally-out contract data |
| Siege ambush | `Campaign/SiegeAmbush/SiegeAmbushCampaignBattleAdapter.cs` | `ExactSiegeAmbushNativeAftermathRuntime.cs` |
| Relief | `Campaign/Relief/ExactReliefCampaignBattleAdapter.cs` | scenario controller plus general writeback |
| Lords hall | `Campaign/LordsHall/LordsHallCampaignBattleAdapter.cs` | `LordsHallResultBridge.cs` |
| Day hideout | `Campaign/Hideout/HideoutCampaignBattleAdapter.cs` | `ExactHideoutNativeAftermathRuntime.cs` |
| Night hideout | `Campaign/Hideout/HideoutAmbushCampaignBattleAdapter.cs` | hideout aftermath plus ambush contract |
| Hero capture | n/a | `Campaign/Capture/ExactCampaignHeroCaptureRuntime.cs`, patch |

`BattleResultWritebackJournalBehavior` and `Infrastructure/CoopBattleResultCampaignGuardContract.cs` are cross-scenario idempotency/identity boundaries.

## Game modes and mission composition

| File | Role |
|---|---|
| `GameMode/MissionMultiplayerCoopBattleMode.cs` | General cooperative campaign battle mission opener and behavior stack |
| `GameMode/MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.cs` | Native siege-with-deployment wrapper and cooperative siege stack |
| `GameMode/MissionMultiplayerCoopHideoutDayMode.cs` | Day hideout mission stack |
| `GameMode/MissionMultiplayerCoopHideoutNightMode.cs` | Night ambush mission stack |
| `GameMode/MissionMultiplayerCoopHeroCreatorMode.cs` | Hero-creation mission stack |
| `GameMode/MissionMultiplayerCoopCampaignMapPrototypeMode.cs` | Opt-in campaign map replica/prototype |
| `GameMode/MissionMultiplayerCoopTdmMode.cs` | Cooperative TDM-derived mode |
| `GameMode/MissionMultiplayerTdmCloneMode.cs` | Disabled custom TDM experiment |
| `GameMode/MissionBehaviorHelpers.cs` | Reflection/compatibility helpers for optional native behaviors |
| `GameMode/MissionBehaviorDiagnostic.cs` | Mission stack diagnostics |

Each mode usually has server/client marker classes beside its `*Mode.cs` file. The mode class builds the behaviors; the marker classes help Bannerlord registration and side-specific identification.

### Campaign map prototype

Settlement occupant badge source (26/22/7): `CoopCampaignMapSettlementPartyBadgePolicy`
in the map contract owns admission/codec bounds; publisher
`CaptureSettlementPartyBadges` reads admitted host parties; catalog/full bridge,
binary codec and shared projection carry the field. `SendCatalogDelta` routes
nonempty lists to chunks. Settlement overlay `SettlementParties.Synchronize`
owns the passive stock-grid items. `SettlementPartyBadgeTests.cs` covers detached
contracts; native acceptance pending in the canonical resume report.

| Concern | Files |
|---|---|
| Host-visible map and dialogue publication | `Campaign/CoopCampaignMapPrototypePublisherBehavior.cs`, `Campaign/CoopCampaignMapConversationCapture.cs` |
| Bounded host/dedicated contract and bridge codec | `Infrastructure/CoopCampaignMapPrototypeContract.cs`, `Infrastructure/CoopCampaignMapPrototypeBridgeCodec.cs` |
| Static catalog payload | `Infrastructure/CoopCampaignMapCatalogBinarySerializer.cs` |
| Dedicated authority and client assembly | `Mission/CoopCampaignMapPrototypeNetworkController.cs` |
| Map and dialogue wire messages | `Network/Messages/CoopCampaignMapPrototypeNetworkMessages.cs`, `Network/Messages/CoopCampaignMapReplicaNetworkMessages.cs`, `Network/Messages/CoopCampaignMapConversationNetworkMessages.cs` |
| Client map rendering and interaction | `UI/CoopCampaignMapPrototypeMissionView.cs`, map overlay and replica-info view-models |
| Client synchronous dialogue | `UI/CoopCampaignMapConversationView.cs`, `UI/CoopCampaignMapConversationVM.cs` |
| Host-owned dialogue upper HUD/banner | `Infrastructure/CoopCampaignMapConversationHud.cs`, `Campaign/CoopCampaignMapConversationCapture.cs`, `UI/CoopCampaignMapConversationHudVM.cs`, `ConversationHudTests.cs`; [scope and verification](CAMPAIGN_MAP_CONVERSATION_HUD.md) |
| Client dialogue scene ownership | `UI/CoopCampaignMapConversationTableauView.cs`, `Infrastructure/CoopCampaignMapConversationVisualLifetime.cs`, `ConversationVisualLifetimeTests.cs` in the map contract project |
| Live dialogue body motion | `Infrastructure/CoopCampaignMapConversationPose.cs` (codec, exact receiver, pure `CoopConversationPosePlayback` rate steering), map contract `ConversationPoseTests.cs`, host capture, message/controller/view; protocol 22/bridge 18. cmt-b27 first-dialogue smoothness user-confirmed; first/repeat retention, read-only/text update and both closes passed. Installed b27 retained after exact cleanup; no full visual/battle-regression claim. |
| 3D dialogue descriptor and atomic transport | `Campaign/CoopCampaignMapConversationTableauCapture.cs`, `Infrastructure/CoopCampaignMapConversationTableauState.cs`, `Infrastructure/CoopCampaignMapConversationPayloadCodec.cs`, map contract `ConversationTableauTests.cs` and `ConversationPayloadTests.cs` |
| Contract coverage | `Tests/CoopCampaignMapPrototype.ContractTests/Program.cs` |

The dialogue projection is server-to-client only. The client uses the native `SPConversation` movie but its answer/continue commands are no-op; no campaign object or choice authority is created in the multiplayer process.

The 2026-09-26 tableau descriptor defines detached camera/atmosphere/weather and up to three native visible actors with a strict 64-KiB binary codec. Host dialogue capture now reads an already initialized native tableau and transports it via bridge schema 17. The whole dialogue uses a bounded 1-MiB atomic payload with 256-byte reliable chunks, two per peer per tick; protocol 21 replaces the old text header/option messages. The complete-payload assembler rejects mixed/stale/incomplete revisions. It preserves initial action identity, not continuous facial/voice/animation synchronization. The independent client scene owner now consumes the descriptor with bounded readiness and explicit cleanup; this candidate is not deployed or runtime-verified. Do not call this implemented live 3D dialogue. See the [implementation boundary and evidence](CAMPAIGN_MAP_RESUME_2026-09-26.md).

`CoopCampaignMapReplicaStateProjection.Merge` in the map contract is the single full/incremental visible-entity projection used by both the network controller and mission view. It preserves all marker, production-icon, label-color and hidden-information fields, with detached visual descriptors. The 2026-09-26 follow-up audit found and removed divergent hand-written merges; the earlier `cmr-b4`/`cmr-b5` candidates had incomplete consumer coverage despite successful tests. `cmr-b6` builds both roles without installed mutation; see the resume report for verification boundaries.

`CoopCampaignMapPartyLabelPolicy` in the map contract holds the inspected native relationship palette, caption precedence, status/label visibility, hidden-count redaction and text composition. The publisher resolves real host diplomacy and uses installed `SandBoxUIHelper` for healthy/wounded/prisoner attached-army aggregation and localization. `NameplateColor` is independent of banner/cloth colors. `IsPartyInformationHidden` zeroes transmitted detail numbers and restricts count/extra strings to native question-mark disclosure. `PartyNameplateNameText` is catalog-owned and separate from detail `DisplayName`; count/extra/speed strings and six presentation flags are dynamic. The shared merge feeds the overlay's native widget properties. Label suppression is separate from the return value used to gate 3D models. The party overlay uses spatial camera distance; the mission view projects a fixed 0.8-unit head anchor. Detail `PartySize` remains total members. Current source requires wire 22/bridge 18/catalog 4 and two-entity dynamic batches (the historical live `cmr-b10` used 20/16/4). Coherent `cmr-b10` passed bounded live mission startup, city selection, zoom, movement and read-only dialogue open/update/close; exact-owned cleanup and 64-file preservation passed. The candidate remains installed. Main-party-specific template, portrait, navigator and dialogue 3D tableau remain pending, as do broader visual and transfer-cost checks. See the resume report for exact build/test evidence and remaining parity gaps.

The replica information panel binds scalar scaled-position offsets; `CoopCampaignMapPrototypeInteractionPolicy.ResolveInformationPanelPosition` clamps shared panel dimensions using the actual layer UI scale. Ordinary dialogue answers supply an inert non-null persuasion VM to suppress default-visible native child widgets; this does not transport actual persuasion state. Movie replacement/close clears information hints. Source/contracts and both builds passed on 2026-09-26; live validation of these corrections remains pending in [the resume report](CAMPAIGN_MAP_RESUME_2026-09-26.md).

## Snapshot, identity, and exact transfer

| Concern | Files |
|---|---|
| Wire/domain snapshot model | `Network/Messages/BattleStartMessage.cs` |
| Snapshot line codec | `Network/Messages/BattleStartMessageCodec.cs` |
| Compact binary snapshot payload | `Infrastructure/BattleSnapshotBinarySerializer.cs` |
| Current normalized runtime projection | `Infrastructure/BattleSnapshotRuntimeState.cs` |
| Exact contract definition | `Infrastructure/ExactTransferSpawnContract.cs` |
| Contract construction | `Infrastructure/ExactTransferContractBuilder.cs` |
| Contract validation | `Infrastructure/ExactTransferContractValidator.cs` |
| Per-entry cache | `Infrastructure/ExactTransferContractRuntimeCache.cs` |
| Transfer stage state/machine | `Infrastructure/ExactTransferRuntimeState.cs`, `ExactTransferStageMachine.cs` |
| Server pre-spawn resolution | `Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs` |
| Pre-spawn body/equipment injection | `Patches/ExactCampaignPreSpawnLoadoutPatch.cs` |
| Native/client create-agent corridor | `Patches/BattleMapSpawnHandoffPatch.cs` |
| Item/object catalogs | `ExactCampaignObjectCatalogBootstrap.cs`, `ExactCampaignRuntimeItemRegistry.cs`, `ExactCampaignRuntimeObjectRegistry.cs` |
| MP compatibility mapping | `CampaignMultiplayerHeroClassResolver.cs`, class fallback/override patches |
| Equipment/mirror policy | `CoopCampaignMirrorEquipmentResolver.cs`, `ExactWeaponSlotMaterializationPolicy.cs` |
| Mounted identity/link contract | `CoopMountedHeroMountLinkContract.cs` |

The runtime object registry file exists but its feature is disabled. Do not treat its types as the active path.

## Scenario infrastructure

### Commander deployment

`Infrastructure/CommanderDeployment/` contains shared contracts plus separate runtimes for:

- generic campaign deployment;
- ordinary field battle;
- generic land battle;
- sally out;
- village battle.

The readiness, facing-order, and commander-death handoff contracts are intentionally isolated and contract-testable.

### Field/general land

- `Infrastructure/LandBattle/ExactLandBattleScenarioContract.cs`
- `Infrastructure/LandBattle/ExactFieldBattleInitialMaterializationRuntime.cs`
- field and land commander-deployment runtimes
- field boundary calculation currently lives primarily in `CoopMissionNetworkBridge`.

### Village battle

- `ExactVillageBattleScenarioContract.cs`
- `ExactVillageBattleInitialMaterializationRuntime.cs`
- `ExactVillageBattleDeploymentBoundaryRuntime.cs`
- `ExactVillageBattleCommanderDeploymentRuntime.cs`

Village boundary data is versioned, hashed, transmitted, applied, and acknowledged separately from general materialization readiness.

### Siege assault

`Infrastructure/SiegeAssault/` contains several distinct concerns:

- `SiegeAssaultMissionOpenBridge`: selects/captures the native mission-opening profile.
- `CoopPreMissionTopologyRuntimeState`: pre-open scene/mode contract state.
- `ExactCampaignSiegeAssaultWithDeploymentRuntime`: deployment lifecycle.
- `ExactCampaignSiegeAssaultNoDeploymentRuntime`: isolated no-deployment path.
- `ExactSiegeAssaultInitialMaterializationRuntime`: client pacing/readiness for initial foot army.
- `CoopSiegeMachineDeploymentController`: machine selection, auto-deploy, native controller synchronization, detachments, and visual normalization.
- `SiegeMissionObjectIdBridge` / `SiegeMissionObjectIdMapRuntime`: stable synchronized object mapping.
- ladder interaction and merlon parity contracts/runtimes.
- peer/perk and formation-membership safety contracts.
- `CoopSiegeSceneOcclusionSafetyContract`: narrow remote-client decision for exact `SiegeMissionWithDeployment` scene occlusion.

Do not combine machine selection, unused-machine finalization, ladder state, scene-object ID mapping, initial army materialization, and reinforcement ownership into one conceptual fix. They touch different owners.

### Siege ambush

- `Infrastructure/SiegeAmbush/SiegeAmbushScenarioContract.cs`
- `ExactSiegeAmbushInitialMaterializationRuntime.cs`
- `Mission/CoopExactCampaignSiegeAmbushMissionController.cs`
- `Network/Messages/CoopSiegeAmbushOrderNetworkMessages.cs`
- `Patches/ExactSiegeAmbushDeploymentControllerPatch.cs`

Mounted rider/mount grouping is valid here; external siege assault is foot-only.

### Relief, sally out, and lords hall

- contracts: `Infrastructure/Relief/`, `Infrastructure/SallyOut/`, `Infrastructure/LordsHall/`;
- mission controllers: `CoopExactCampaignReliefMissionController`, `CoopExactCampaignLordsHallMissionController`, and `Mission/LordsHall/LordsHallMissionRuntime.cs`;
- campaign adapters are listed above.

### Hideouts

- `Infrastructure/Hideout/`: day/night scenario contracts, boss-phase/ambush rules, scene manifest, mannequin isolation.
- `Mission/`: day/night mission controllers, boss phase, ambush network controller, runtime scene manifest.
- `UI/`: stealth view, call-troops cinematic, boss cinematic/conversation, objectives, markers.
- `Patches/HideoutAmbushArrowBarrelPatch.cs`: scenario interaction patch.

## Runtime state and local bridges

### Battle/session state

- `CoopBattlePhaseRuntimeState`
- `CoopBattleAuthorityState`
- `CoopBattleSelectionIntentState` and `CoopBattleSelectionRequestState`
- `CoopBattleSpawnIntentState`, `CoopBattleSpawnRequestState`, `CoopBattleSpawnRuntimeState`
- `CoopBattlePeerLifecycleRuntimeState`
- `CoopBattlePeerReconnectState`
- `CoopBattlePeerSessionState`
- `CoopBattleAgentControlRuntimeState`

These types overlap deliberately: intent, authoritative request, spawn result, lifecycle, reconnect, and composed UI/session state are different layers. Before adding another flag, locate the existing owner.

### Bridge files

- roster: `Campaign/BattleRosterFile.cs`
- result: `Infrastructure/CoopBattleResultBridgeFile.cs`
- phase: `CoopBattlePhaseBridgeFile.cs`
- entry status: `CoopBattleEntryStatusBridgeFile.cs`
- selection: `CoopBattleSelectionBridgeFile.cs`
- spawn requests: `CoopBattleSpawnBridgeFile.cs`
- hero creation: `CoopHeroCreationBridgeFile.cs`
- campaign map prototype: `CoopCampaignMapPrototypeBridgeFile.cs`
- synthetic role matrix: `CoopRoleMatrixStreamBridgeFile.cs`
- exact diagnostic artifacts: `ExactBattleAgentSpawnTraceBridgeFile.cs`, `ExactBattleEntryCompatibilityBridgeFile.cs`, `ExactBattleRuntimeBundleBridgeFile.cs`
- shared I/O: `AtomicBridgeFileIO.cs`
- temporary local-host redirect state: `HostSelfJoinRedirectState.cs`

### Battle-test client join

- `Infrastructure/Automation/CoopAutomationJoinContract.cs`: schema 3, request validation, SHA-256 binding, exact server selection, optional native serialized `UniqueMapId` matching distinct from scene `Map`, and terminal-state rules.
- `Infrastructure/Automation/CoopAutomationJoinBridge.cs`: complete environment/profile validation, loaded-assembly hash, run-scoped request read, and strict atomic status publication including native platform-login attempt/task/outcome evidence.
- `Multiplayer/Automation/CoopLobbyAutomationDriver.cs`: narrow reflection adapter that resolves `TaleWorlds.MountAndBlade.NetworkMain` and the exact installed `TaleWorlds.MountAndBlade.LobbyState` from their defining assemblies. It validates `Game.Current.GameStateManager.ActiveState`, exact state identity, lobby-client reference equality, native login state/privilege, invokes public `TryLogin()` once, and then performs custom-server discovery plus custom-game join. Structural and state failures are published distinctly.
- `Multiplayer/Automation/CoopLobbyAutomationController.cs`: main-thread polling, one retained native platform-login task with no blind retry or false cancellation, explicit login outcomes, exact local-server ownership gate, native join lifecycle, GameNetwork handoff observation, terminal connected acknowledgement, and safe cancellation boundary.
- `Commands/CoopAutomationConsoleCommands.cs`: `coop.automation_join` status/start/cancel surface.
- `Patches/LocalJoinAddressPatch.cs`: authoritative patch on actual `GameNetwork.StartMultiplayerOnClient(...)`; applies the one-shot same-machine loopback rewrite, then notifies the run-scoped controller with the final address and native handoff arguments.
- `Patches/LobbyCustomGameLocalJoinPatch.cs`: optional historical reflection-based lobby rewrite for version compatibility; it is not an authoritative handoff observer and emits no duplicate notification.

This path is disabled by default, is independent of battle type, and stops at network connection evidence. It does not open a mission or own battle readiness/result state.

### Battle-test automation foundation

- `Directory.Build.props`: shared default-false compile-only output/intermediate/package routing.
- `Infrastructure/Automation/CoopAutomationRunContract.cs`: manifest, role instance, port/fixture identity, lease, envelope, event, stable outcome/reason, recovery, and known-issue contracts.
- `Infrastructure/Automation/CoopAutomationProtocolFileIO.cs`: strict atomic JSON, bounded shared reads, append-safe JSONL, and same-volume inbox processing.
- `Infrastructure/Automation/CoopAutomationRuntimeContract.cs`: strict runtime profile, loaded-role identity, `Suppress` result policy, and run-scoped owned-host validation.
- `Infrastructure/Automation/CoopAutomationRuntimeBridge.cs`: loaded assembly hashing, role status publication, result-policy resolution, and exact live owned-host confirmation.
- `Infrastructure/Automation/CoopAutomationFixtureContract.cs`: schema-1 exact campaign-roster fixture metadata, ordinary SCN-001 admission, immutable byte/hash/provenance validation, stable rejection reasons, and run-root path containment.
- `Infrastructure/Automation/CoopAutomationFixtureRecorder.cs`: separately opted-in post-write `battle_roster.json` byte capture, loaded-module identity check, immutable run-scoped payload/metadata/status publication, and disabled-mode no-I/O boundary.
- `DedicatedServer/Automation/CoopAutomationDedicatedControlContract.cs`: fixed schema-1 dedicated ready/request/status identities, bounded `ConnectionFeasibilityV1` profile, request validation, and exact seven-acknowledgement terminal validation.
- `DedicatedServer/Automation/CoopAutomationDedicatedControlBridge.cs`: default-off binding to `InitialListedGameServerState.OnActivated`, atomic request claim/status publication, main-tick native command dispatch, and native option/map/listed-server verification.
- `DedicatedServer/SubModule.cs`: initializes and shuts down the dedicated control bridge after runtime-profile validation and pumps it before other early-return paths in `OnApplicationTick`.
- `scripts/Invoke-CoopTest.ps1`: `Doctor`, full `Contracts`, client/dedicated `CompileOnly`, runtime `Feasibility`, private campaign `Record`, existing-run `Inspect`, fail-closed exact `Cancel`, and opt-in exact `Recover`. It owns the run root, runner lock, nonce fingerprint, lease, schema-2 runner status, events, assertions, provisional-to-verified process identities, cleanup, logs, and reports. Protocol 1.1 adds `RoleHealthV1`, `CancellationV1`, `RecoveryV2`, and `FailureEvidenceV1`; runtime commands acquire canonical game/dedicated/bridge/profile/port locks before product launch and verify release. `Feasibility` waits for structured dedicated readiness and all seven acknowledgements before UDP/client work, then validates and registers the complete client handoff before fallible live revalidation. `Record` requires clean local/upstream identity and an explicit installed client hash, validates the aggregate-owned campaign handoff, waits for an exact ordinary field fixture, independently rechecks byte/provenance/qualification evidence, retains private/non-oracle/non-L2 boundaries, discovers exact descendants, and proves protected-result/cleanup isolation. The connectivity bootstrap still omits optional `UniqueMapId` because no authoritative serialized native value is available. Terminal/non-terminal client and fixture status survive bounded failure without outcome promotion; PID logs and exact-correlated crash/hang evidence remain run-scoped.
- `scripts/CoopAutomationRunner.Core.ps1`: shared process-identity, descendant, output-capture, dedicated-control, role-health, cancellation-signal, shared-lock, exact-stop, and failure-helper-correlation primitives. `Get-CoopSharedRuntimeResourceIdsCore` constructs and cardinality-checks independent bridge/game/dedicated/profile/port resource ids without PowerShell argument-expression concatenation. Its console handler only sets an atomic cancellation flag; cleanup and artifact I/O stay on the main runner thread.
- `Infrastructure/Automation/CoopAutomationRuntimeBridge.cs`: default-off common runtime role-health publisher. It is pumped by the existing client controller and dedicated control tick, writes at most once per second unless state changes, and uses monotonic elapsed values.
- `scripts/CoopAutomationRunner.Core.ps1`: fixed dedicated bootstrap request construction, strict ready/status validation, type-safe UTC normalization, schema-v4 client launch validation, module-independent .NET SHA-256 hashing, required/optional PID-log descriptors, bounded process observation with validated `Win32_Process` path fallback, provisional/verified identity matching, PID-reuse-resistant exact cleanup, supplementary stdout/stderr capture, and pure in-memory descendant discovery from one bounded process snapshot.
- `scripts/Start-CoopBattleTestClient.ps1`: standalone validation plus aggregate-owned live launch through `-UseExistingRunContract`; inherits the runner token/root/hash/result policy, records immediate provisional PID/path/parent/window ownership, performs bounded exact identity observation, publishes the schema-v4 complete verified-process handoff, and exactly cleans pre-handoff failures.
- `scripts/Start-CoopFieldFixtureCapture.ps1`: validation-only standalone campaign preflight plus aggregate-owned live launch through `-UseExistingRunContract`; verifies the exact run/root/token fingerprint/hash/revision/game version, uses the installed-1.4.8 singleplayer module list, supplies fixture flags only through the child environment, removes the server-password variable, publishes provisional then exact campaign ownership, and cleans exact pre-handoff failures without UI automation or save mutation.
- `scripts/New-CoopSanitizedFieldFixture.ps1`: offline deterministic sanitizer pinned to the independently reviewed private field-payload SHA-256; refuses source overwrite, nonempty output, and implicit repository writes; replaces campaign-derived identities while preserving static replay content; emits only linked sanitized payload/metadata bytes that are identical in Windows PowerShell 5.1 and PowerShell 7.
- `Tests/Fixtures/Automation/field-current`: three-file shareable fixture authority containing the sanitized campaign roster, provenance metadata linking private and derivative hashes, and a separately authored critical-value oracle. No raw capture, save, or run log belongs in this directory.
- `Tests/contract-tests.manifest.json`: exact reviewed inventory consumed by the aggregate runner.

This layer remains non-authoritative for campaign and battle state. Milestone 2A stays non-runtime. Milestone 2B.1 adds process/role ownership, and Milestone 2B.2D adds the bounded vanilla `TeamDeathmatch` connectivity bootstrap after fail-closed result isolation. Run `m2b2d-live-feasibility-20260901-01` confirms the exact dedicated binary, control acknowledgements, UDP ownership, real client launch, and client loaded hash. Revision 12 corrects the aggregate handoff; `m2d-live-r2-01` confirms that handoff and automatic cleanup, then exposes the exact-assembly `NetworkMain` resolver defect. Corrected client `1C500...79250` was transactionally staged and live-confirmed by `m2d-live-r3-01`: the resolver works, but the stock client remains `Not Logged In` with `LobbyState=Idle`. Revision 13 implements and contract-verifies one bounded native `LobbyState.TryLogin()` attempt before server discovery plus terminal client evidence retention. Final client candidate `6DE0B8...E8551` is compile-only evidence and is not staged or runtime-verified. This layer does not load a campaign fixture, advance cooperative battle phases, publish campaign-consumable results, or claim L2/L3 evidence.

## Network files

### Transport components

- `Network/NetworkManager.cs`: TCP server/client role and message dispatch.
- `Network/TcpClientConnection.cs`: connection lifecycle and line receive loop.
- `Network/TcpLineProtocol.cs`: framing/encoding.
- `Network/CoopPreMissionTopologyNetworkComponent.cs`: global pre-mission Bannerlord component.

### Message groups

| File | Messages |
|---|---|
| `BattleStartMessage.cs` | Complete campaign battle domain model |
| `BattleStartMessageCodec.cs` | TCP/start payload codec |
| `CoopPreMissionTopologyNetworkMessages.cs` | Pre-open compact scene/mode/scenario contract |
| `CoopBattleSelectionNetworkMessages.cs` | Snapshot chunks/control, entry status, selection, phase, materialization, deployment, siege state |
| `CoopBattleAgentControlNetworkMessages.cs` | Controlled-agent/AI authority state |
| `CoopBattlePowerNetworkMessages.cs` | Side power/casualty comparison |
| `CoopSiegeAssaultOrderNetworkMessages.cs` | Siege assault formation/player orders |
| `CoopSiegeAmbushOrderNetworkMessages.cs` | Siege ambush orders |
| `CoopHideoutBossPhaseNetworkMessages.cs` | Hideout boss-stage state |
| `CoopHideoutAmbushNetworkMessages.cs` | Night ambush state/interactions |
| `CoopHeroCreationNetworkMessages.cs` | Hero draft/progress/result transport |
| `CoopCampaignMapPrototypeNetworkMessages.cs` | Prototype host state, settlement events, and quest-marker flags |
| `CoopCampaignMapReplicaNetworkMessages.cs` | Replica/catalog state including the host-visible marker projection |
| `HostGameState.cs` / codec | TCP host state |
| `NetworkMessagePrefixes.cs` | Stable message prefixes/constants |

`CoopBattleSelectionNetworkMessages.cs` is broad and should eventually be split by protocol concern, but that refactor would affect registration and wire compatibility and must not be incidental.

## Harmony patch map

### Mission open and topology

- `PreMissionTopologyContractPatch`
- `MissionStateOpenNewPatches`
- `BattleShellSuppressionPatch`
- `SiegeMissionGameTypeAliasPatch`
- `FinishedLoadingMissionReadyGatePatch`
- `LateJoinPeerBootstrapGatePatch` (code present; dangerous hooks disabled by startup flags)

### Exact spawn and network compatibility

- `ExactCampaignArmyBootstrapPatch`
- `ExactCampaignNetworkObjectBootstrapPatch`
- `ExactCampaignPreSpawnLoadoutPatch`
- `BattleMapSpawnHandoffPatch`
- `ClientChangeCultureCanonicalizationPatch`
- `ServerChangeCultureCanonicalizationPatch`
- `MultiplayerCharacterClassFallbackPatch`
- `MultiplayerHeroClassOverridePatch`
- `CampaignCombatProfileAgentStatsPatch`
- `CoopBattleDisplayNameConsumerPatch`

### Mission safety and control

- `CampaignlessConversationMissionSafetyPatch`
- `MissionItemUsageSetFlagsGuardPatch`
- `CoopNetworkSafeAgentBlowPatch`
- `CoopBotsControlledCountPatch`
- `CoopMissionLobbySpawnPeriodGuardPatch`
- `CommanderDeploymentMissionNetworkComponentPatch`
- `CoopBattleEscapeMenuAiControlPatch`

`CampaignlessConversationMissionSafetyPatch` targets the native `ConversationMission.OneToOneConversationAgent` getter. Its pure `CampaignlessConversationMissionSafetyContract` allows the original getter when both campaign and conversation manager exist and returns a null result only when that campaign state is unavailable.

### Siege-specific

- `CoopSiegeFormationMembershipSafetyPatch`
- `CoopSiegeLadderInteractionPatch`
- `ExactSiegeLadderMerlonVisualParityPatch`
- `ExactSiegeAmbushDeploymentControllerPatch`
- `ExactSiegeStageAdvancePatch`
- `OrderOfBattleSiegeProjectedCountsPatch`

### Lobby/UI/diagnostics

- game-mode override and lobby join patches;
- entry/HUD suppression patches;
- mission camera preview patch;
- optional character tableau/visual diagnostics patches;
- campaign map prototype scene-load patch.

When diagnosing a patch, inspect both `Apply` targeting and the startup flag/call site. A patch class can exist but be inactive, partially applied, or reapplied after assembly load.

## UI and assets

Primary views/view-models:

- `CoopMissionSelectionView` and `CoopSelectionShellViewModels`: side/unit/spawn/deployment shell;
- `CoopSiegeMachineDeploymentVM` and `CoopSiegeOrderOfBattleVM`: siege deployment and projected counts;
- `CoopBattlePowerScoreView`, `CoopBattleAiControlHintVM`: battle HUD additions;
- hero creator view/model/culture selection;
- day/night hideout objective, stealth, cinematic, conversation, and marker views;
- `CoopCampaignMapPrototypeMissionView`, party visual, overlays, and replica info. The mission view loads the native `Main_map` atmosphere data after `Scene.Read` and before optimization, collects meshes tagged `ticked_map_entity`, calls `MBMapScene.TickVisuals` while the map scene is render-ready, and clears those cached arrays during scene release. The atmosphere load is required because the visual tick reaches the native map color-grade/atmosphere path even though this is an isolated client scene layer. Publisher-to-client marker state uses explicit bit flags for available/active/tracked issue and story-quest semantics plus settlement tournament/production events; hidden campaign objects clear these fields instead of leaking markers.
- `CoopCampaignMapConversationView` and `CoopCampaignMapConversationVM`: synchronized display-only projection of the host's already formatted native map dialogue into the `SPConversation` movie. It restores mission-layer activation/focus on close and exposes no client choice transport.
- `CoopCampaignMapConversationTableauView`: owns the independent scene, up to three visual actors and an off-screen native TableauView texture. Its private passive Gauntlet layer/instance-local widget draws below the native conversation without global provider registration. Texture callback failures are consumed by the mission tick; resize invalidates readiness while preserving actors. cmt-b20 corrected gross image scaling/displacement using stock movie-style physical-pixel sizing. cmt-b22 restored visible scar/age detail with bounded immediate actor preparation; repeated readiness/retention/close passed. Animation, expressions and top HUD still differ; this is not visual 1:1 acceptance. See the canonical cmt-b22 native face-detail result in the resume report.

Gauntlet prefabs live in `Module/CoopSpectator/GUI/Prefabs/`. When changing a view-model property or command, search the matching XML binding. `CoopSelection.xml` and `CoopCommanderDeployment.xml` are primary selection/deployment assets.

Game data under `Module/CoopSpectator/ModuleData/` defines custom/mirror items, crafting pieces, multiplayer characters/classes, and strings. Exact equipment resolution may depend on both snapshot data and these stable XML registrations.

## Console commands

All commands use the `coop` prefix.

### Connectivity and launch

From `CoopConsoleCommands`:

- `coop.host`
- `coop.join`
- `coop.send`
- `coop.status`
- `coop.test_mp_launch`
- `coop.test_mp_server`
- `coop.test_mp_mission`
- `coop.test_mp_join`
- `coop.dedicated_start`
- `coop.dedicated_start_vpn`
- `coop.dedicated_open_tokens`
- `coop.test_mp_team`
- `coop.automation_join status`
- `coop.automation_join start <RunId>`
- `coop.automation_join cancel`

### Selection and spawn

From `CoopBattleSelectionConsoleCommands`:

- `coop.select_side`
- `coop.select_troop`
- `coop.spawn_now`
- `coop.force_respawnable`
- `coop.entry_status`
- `coop.side_options`
- `coop.select_side_index`
- `coop.troop_options`
- `coop.select_troop_index`
- side-scoped troop variants and `coop.entry_menu`.

### Phase and synthetic scenarios

- `coop.phase_status`
- `coop.start_battle`
- `coop.test_campaign_roster`
- `coop.test_hero_roster`
- `coop.test_battle`
- campaign map prototype console command(s) in `CoopCampaignMapPrototypeConsoleCommands`.

Console commands often write bridge requests that the authoritative mission consumes. They do not bypass mission authority.

## Contract tests

Each project is a small `net8.0` executable. Most link a narrow production contract directly and avoid loading Bannerlord runtime.

| Test project | Main contract area |
|---|---|
| `CampaignlessConversationMissionSafety.ContractTests` | Campaign-null conversation safety |
| `CoopBattlePower.ContractTests` | Power and multiplayer HUD calculations |
| `CoopBattleResultCampaignGuard.ContractTests` | Campaign/result ID validation and journaling |
| `CoopBattleResultReadCache.ContractTests` | Stable file-stamp cache semantics |
| `CoopBattleStartup.ContractTests` | Startup, exact hero/equipment, commander, mount, relief, and safety contracts |
| `CoopCampaignMapPrototype.ContractTests` | Map codec, chunk/catalog, bounds and scene rules |
| `CoopExactCampaignSiegePeerPerkSafety.ContractTests` | Hero-class and perk safety on dedicated siege |
| `CoopExactSiegeLadderMerlonVisualParity.ContractTests` | Remote ladder/merlon visibility decisions |
| `CoopHeroBattleProgression.ContractTests` | Weapon identity, XP, ammo/perk, fatal-event matching |
| `CoopHeroCreation.ContractTests` | Draft budgets, chunks, progress, and state machine |
| `CoopHideoutBossPhase.ContractTests` | Day/night hideout and boss/ambush rules |
| `CoopRemoteSiegeOcclusionSafety.ContractTests` | Remote client occlusion safety |
| `CoopShaderCacheModeSwitch.ContractTests` | Module PowerShell wrapper state changes |
| `CoopSiegeFormationMembershipSafety.ContractTests` | Stored formation membership/coordinates |
| `CoopSiegeLadderInteraction.ContractTests` | Ladder interaction mutation guards |
| `CoopSiegeSceneScriptRegistration.ContractTests` | SandBox managed scene-script registration |
| `CoopAutomationJoin.ContractTests` | Run/token/hash request validation, exact server selection, atomic status, and automation source-graph compilation |
| `CoopAutomationFixture.ContractTests` | Exact post-write byte retention, disabled-mode no-I/O, first-field admission, other-scenario rejection, immutable metadata, corruption/schema/path rejection, plus committed sanitized-fixture allowlist/privacy/hash/oracle/reference/composition/equipment checks and production-schema contract replay |
| `CoopAutomationProtocol.ContractTests` | Protocol compatibility, role/run/nonce ordering, lease/recovery, stable outcomes/reasons, known issues, atomic/append file faults, locks, and repeat reads |
| `CoopAutomationRuntime.ContractTests` | Strict runtime profile, loaded-role hash status, fail-closed result suppression, and exact live owned-host validation |
| `CoopCompileOnly.ContractTests` | Shared compile-only property, output routing, and deployment-target guards in both main project files |
| `NativeAftermath.ContractTests` | Casualty math, staged siege totals, hideout/final siege aftermath |

These tests prove contract logic only. They do not prove native mission loading, synchronized network ordering, or visual/runtime stability.

## Scripts

- `scripts/CoopDevLoop.ps1`: optional client/dedicated builds, process restart/launch, DLL timestamp checks, and log-marker scanning. With no action switches it builds both and checks logs. Its default `ProjectRoot` points to `C:\dev\projects\BannerlordCoopSpectator3`, not necessarily the active Codex worktree; pass `-ProjectRoot` explicitly.
- `scripts/CreateReleasePackage.ps1`: Release builds unless `-SkipBuild`, then recreates selected `dist/` artifacts. `-GitHubAssetsOnly` creates full GitHub client/host archives, `-NexusAssetsOnly` derives validated Nexus client/HostLite archives from existing GitHub archives, and `-ReleaseAssetsOnly` creates both sets. Nexus validation excludes BAT/PS1/PDB files and compares every retained payload entry and embedded release document by SHA-256. See [RELEASE_PACKAGING.md](RELEASE_PACKAGING.md) for the canonical layouts and workflow.
- `scripts/DllInventoryAudit.ps1`: scans installed client/dedicated trees and writes repository reports/CSV. Its historical conclusion may not match the current installed version.
- `scripts/Test-RepositoryHygiene.ps1`: read-only validation of repository-local Git EOL configuration, tracked text EOL state, mixed endings, and optional final working-tree cleanliness.
- `scripts/Invoke-CoopTest.ps1`: canonical fresh-root runner for L0/L1 checks, controlled connectivity, and private exact field-fixture capture. `Record` is capture-only and never implies mission/full-battle or L2/L3 success.
- `scripts/Start-CoopBattleTestClient.ps1`: validates Steam, exact installed client hash, and run identity; `-ValidateOnly` performs no launch/run-root write; an aggregate-owned live call writes only the selected temporary run root, provisionally owns and verifies the exact multiplayer executable, and cleans any failure before final handoff.
- `scripts/Start-CoopFieldFixtureCapture.ps1`: validates the exact singleplayer executable/module/version/arguments; `-ValidateOnly` performs no launch/run-root write; an aggregate-owned live call publishes immutable launch evidence and starts only the exact campaign process.
- `run_battle_test_client.bat`: historical standalone wrapper for the PowerShell client launcher. Current source is validation-only for this route; live automation must inherit exact server ownership from aggregate `Feasibility`. The password remains environment-only and is not a positional argument.

## High-risk hotspots

These are high risk because of responsibility breadth and native/runtime sensitivity, not merely file length:

1. `Mission/CoopMissionBehaviors.cs`: mission lifecycle, static state, spawn, phases, result.
2. `Patches/BattleMapSpawnHandoffPatch.cs`: native `CreateAgent` interception and replay.
3. `Mission/CoopMissionNetworkBridge.cs`: protocol, retries, acknowledgements, deployment, siege objects.
4. `Campaign/BattleDetector.cs`: capture plus all scenario routing and campaign aftermath.
5. `Infrastructure/ExactCampaignArmyBootstrap.cs`: native supplier/bootstrap boundary.
6. `Infrastructure/SiegeAssault/CoopSiegeMachineDeploymentController.cs`: native siege controller state, detachments, visibility, and AI-sensitive machines.
7. `GameMode/MissionMultiplayerCoop*Mode.cs`: exact native behavior-stack composition and ordering.
8. `SubModule.cs` and `DedicatedServer/SubModule.cs`: patch registration differences and late-load behavior.
9. `UI/CoopMissionSelectionView.cs`: broad player-facing selection/deployment state integration.

## Search recipes

Run from repository root:

```powershell
# Find a type or method without searching generated output.
rg -n --glob '*.cs' --glob '!bin/**' --glob '!obj/**' --glob '!dist/**' 'SymbolName'

# Find every runtime feature flag and its use sites.
rg -n --glob '*.cs' 'ExperimentalFeatures\.|COOPSPECTATOR_'

# Find a network message definition and all handlers/senders.
rg -n --glob '*.cs' 'CoopSomeMessage'

# Find mission-state cleanup for a subsystem.
rg -n --glob '*.cs' 'Reset|Clear|OnEndMission|OnRemoveBehavior' Mission Infrastructure Patches

# Find scenario routing rather than guessing from file names.
rg -n --glob '*.cs' 'ScenarioKind|SiegeSubtype|IsCampaignBattle|TryValidateActiveMission'

# Find console command names.
rg -n --glob '*.cs' 'CommandLineArgumentFunction' Commands
```

Prefer symbol-first reading over opening the largest files at line 1.
