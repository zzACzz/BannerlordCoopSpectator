# Clean CoopBattle Bootstrap Plan

Date: 2026-03-27
Project: `BannerlordCoopSpectator3`
Focus: `custom clean battle mode / bootstrap contract / client join safety`

Related docs:

- `docs/CAMPAIGN_AFTERMATH_AND_DEDICATED_DATA_MAP_2026-03-26.md`
- `docs/HOST_AFTERMATH_MAPPING_PLAN_2026-03-26.md`

Primary code references:

- `GameMode/MissionMultiplayerCoopBattleMode.cs`
- `GameMode/MissionMultiplayerCoopBattle.cs`
- `GameMode/MissionMultiplayerTdmCloneMode.cs`
- `GameMode/MissionBehaviorHelpers.cs`
- `GameMode/MissionBehaviorDiagnostic.cs`
- `DedicatedHelper/DedicatedHelperLauncher.cs`
- `DedicatedServer/SubModule.cs`
- `Patches/MissionStateOpenNewPatches.cs`

## 1. Purpose

This document defines the confirmed bootstrap constraints for moving from the current
`TDM`-anchored prototype to a clean `CoopBattle` runtime mode.

The goal is not to invent a fully new dedicated `GameType` first.

The goal is to identify the minimal safe contract that still:

- starts on dedicated reliably
- allows client join reliably
- opens a multiplayer mission reliably
- removes `TDM` gameplay lifecycle dependencies from the actual battle runtime

## 2. Executive conclusion

The right direction is:

- keep official multiplayer bootstrap where the engine still requires it
- move gameplay lifecycle to `MissionMultiplayerCoopBattleMode` + custom behavior stack
- stop depending on `TDM` round/cleanup/respawn semantics for battle runtime

In practical terms:

- dedicated startup config should remain on official `TeamDeathmatch` for now
- `MissionState.OpenNew(...)` should remain on `"MultiplayerTeamDeathmatch"` for now
- the custom behavior factory should become the real owner of battle runtime
- `TDM` should later remain only as a UI donor, not as gameplay lifecycle owner

## 3. Confirmed constraints

## 3.1 Dedicated config `GameType` must stay official

`DedicatedHelperLauncher.TryWriteStartupConfig(...)` explicitly documents and enforces
that dedicated config `GameType` must be one of the official modes such as
`TeamDeathmatch`, `Captain`, `Skirmish`, `FreeForAll`, `Duel`, or `Siege`.

The same file also documents the already observed failure for custom config values:

- custom `GameType=TdmClone` leads to `Cannot find game type: TdmClone`

Current safe startup choice:

- config `GameType = TeamDeathmatch`

Implication:

- do not build the migration plan around `ds_config` using `GameType=CoopBattle`
- a clean coop mode must initially ride on top of official dedicated startup

## 3.2 Custom mode registration is runtime-only

`DedicatedServer/SubModule.cs` registers:

- `CoopBattle`
- `CoopTdm`
- optionally `TdmClone`

through `Module.CurrentModule.AddMultiplayerGameMode(...)`.

This means custom mode registration exists, but it happens inside module load.
It is not the same thing as having a dedicated startup config that the engine accepts.

Implication:

- runtime registration is necessary
- runtime registration is not sufficient to replace official config bootstrap

## 3.3 Safe mission open still uses official mission name

`MissionMultiplayerCoopBattleMode.StartMultiplayerGame(...)` currently calls:

- `MissionState.OpenNew("MultiplayerTeamDeathmatch", ...)`

This is already a strong practical signal:

- the project has a custom coop battle mode
- but the stable mission-open path still relies on the official TDM mission name

`MissionStateOpenNewPatches.cs` also explicitly recognizes this case and skips the
vanilla TeamDeathmatch diagnostic wrapper when the behavior factory belongs to
`MissionMultiplayerCoopBattleMode`.

Implication:

- clean `CoopBattle` should be implemented first as a custom behavior stack over the
  safe official mission-open path
- only later, if ever needed, should the project test replacing the mission name itself

## 3.4 Current `CoopBattle` runtime already exists

`MissionMultiplayerCoopBattle.cs` is already the correct gameplay base:

- it is a separate runtime class
- it initializes attacker/defender teams
- it advances custom coop battle phase state
- it does not implement TDM score/round semantics itself

It still returns `MultiplayerGameType.TeamDeathmatch`, which is acceptable for the
current bootstrap stage.

Implication:

- the migration target is not "brand new mode from scratch"
- the migration target is "promote existing `CoopBattle` to the primary clean battle mode"

## 4. What is actually meant by "clean mode"

For this project, "clean mode" should mean:

- custom battle runtime lifecycle
- custom spawn/side/troop contract
- custom battle completion contract
- no dependency on TDM round end, respawn flow, or score cleanup

It does not need to mean all of the following immediately:

- custom dedicated config `GameType`
- custom engine mission name
- custom server browser identity
- custom UI from day one

That distinction is important.

The stable path is:

- keep official bootstrap identity where the engine requires it
- replace battle semantics behind that bootstrap

## 5. Minimal bootstrap contract by layer

## 5.1 Layer A: dedicated startup

Confirmed safe contract today:

- dedicated launcher starts official dedicated executable
- startup config uses official `GameType=TeamDeathmatch`
- module registration then injects/activates custom coop modes at runtime

Do not change first:

- dedicated executable choice
- dedicated config `GameType`
- startup config strategy

## 5.2 Layer B: mode resolution

Confirmed safe contract today:

- dedicated module registers `MissionMultiplayerCoopBattleMode`
- the custom mode id is `"CoopBattle"`

Risk to note:

- `Infrastructure/CoopGameModeIds.cs` still does not define a `CoopBattle` constant
- that is not a blocker for research, but it is a consistency gap to close later

## 5.3 Layer C: mission open

Confirmed safe contract today:

- `CoopBattle` opens mission through `"MultiplayerTeamDeathmatch"`

Recommended rule:

- keep this unchanged during the first clean-mode migration

Reason:

- client join and mission bootstrap are historically fragile
- gameplay cleanup/lifecycle is the real thing we need to detach from `TDM`

## 5.4 Layer D: behavior stack

This is the real place where clean-mode migration should happen.

The engine bootstrap may still say "TeamDeathmatch", but the mission behavior list can
already be fully tailored to coop campaign battles.

## 5.5 Layer E: client join / UI survival

This is the main risk area.

Earlier experiments in `TdmClone` already captured which behaviors are fragile or have
hidden dependencies. Those findings should be treated as required bootstrap knowledge,
not as TDM-specific trivia.

## 6. Known behavior dependencies

## 6.1 Confirmed critical or near-critical pieces

From `MissionBehaviorHelpers.cs`, `MissionBehaviorDiagnostic.cs`, and
`MissionMultiplayerTdmCloneMode.cs`, the following are already known:

- `MissionBoundaryCrossingHandler` is effectively mandatory for boundary UI flow
- `MissionHardBorderPlacer` is required
- `MissionBoundaryPlacer` is required
- `MultiplayerPollComponent` is expected by vanilla UI shell
- `MissionOptionsComponent` is expected by vanilla UI shell
- `MissionLobbyEquipmentNetworkComponent` depends on
  `MultiplayerMissionAgentVisualSpawnComponent`

Implication:

- do not aggressively strip these behaviors in the first clean-mode pass
- first clean-mode pass should preserve client join and basic UI stability

## 6.2 Scoreboard caveat

`MissionBehaviorHelpers.TryCreateMissionScoreboardComponent()` documents an important
server-side caveat:

- `MissionCustomGameServerComponent.AfterStart` may crash on dedicated if
  `MissionScoreboardComponent` is missing

`TdmClone` already encoded this as a sanity rule for the experimental stack.

Implication:

- scoreboard presence must be treated as a boot-contract variable, not a cosmetic detail
- when slimming `CoopBattle`, test scoreboard removal explicitly instead of assuming it
  is safe

## 6.3 Client-only behavior separation

`TdmClone` also captured a useful negative rule:

- server stack must not include client-only behaviors

Examples already encoded there:

- `MissionLobbyEquipmentNetworkComponent`
- `MultiplayerMissionAgentVisualSpawnComponent`
- client custom mission behaviors

Implication:

- clean `CoopBattle` should preserve a strict server/client split
- do not collapse both stacks into a shared "simple" list

## 7. Recommended clean CoopBattle migration path

## 7.1 Phase 1: stabilize `CoopBattle` bootstrap without changing engine-facing identity

Goal:

- keep client join and mission start stable

Rules:

- keep dedicated config on `TeamDeathmatch`
- keep `MissionState.OpenNew("MultiplayerTeamDeathmatch", ...)`
- keep `MissionMultiplayerCoopBattle.GetMissionType()` on `TeamDeathmatch`
- move work only inside `CoopBattle` behavior factory and runtime behaviors

## 7.2 Phase 2: remove TDM gameplay lifecycle dependence

Goal:

- stop battle aftermath from being normalized by TDM round/cleanup logic

Work items:

- make `MissionMultiplayerCoopBattleMode` the primary runtime path
- keep custom side/troop selection flow
- keep custom authoritative battle completion
- keep custom result bridge / mission exit bridge
- keep current casualty/HP/reward/loot writeback pipeline

## 7.3 Phase 3: revalidate knockout / prisoner signal in clean runtime

Goal:

- see whether clean lifecycle preserves better final agent-state fidelity

Why this matters:

- current TDM-based runs show many agents ending in `OtherRemoved`
- current TDM-based runtime also gives dead combat callbacks
- therefore prisoner fidelity should be retested only after clean runtime migration

## 7.4 Phase 4: UI separation

Goal:

- reuse TDM UI parts without re-importing TDM gameplay lifecycle

Intended source material:

- side selection shell
- troop selection shell
- result presentation shell

Rule:

- UI may be borrowed from TDM later
- gameplay runtime should not be owned by TDM anymore

## 8. What not to do next

The following would be premature:

- setting dedicated config `GameType=CoopBattle`
- changing `MissionState.OpenNew(...)` to a brand-new mission name immediately
- removing multiple "annoying" vanilla behaviors at once without join tests
- resuming prisoner-fidelity work inside the current TDM runtime

## 9. Immediate next coding target

The next implementation step should be:

- treat `MissionMultiplayerCoopBattleMode` as the main clean battle bootstrap
- compare its current behavior list against the already learned `TdmClone` safety rules
- explicitly mark each behavior as:
  - required now
  - optional
  - candidate for later removal

Then perform a staged migration:

1. preserve bootstrap safety
2. move gameplay ownership to `CoopBattle`
3. only then strip leftover TDM assumptions further

## 10. Final recommendation

The project should not attempt "custom mode from zero" first.

The project should build a clean coop battle runtime by using:

- official dedicated startup bootstrap
- official TDM mission-open identity
- custom `CoopBattle` mission behavior stack
- custom coop battle runtime lifecycle

This is the safest path that still moves the project away from the current TDM
aftermath distortion problem.
