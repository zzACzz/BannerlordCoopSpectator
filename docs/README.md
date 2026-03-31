# Documentation Index

Date: 2026-03-31
Project: `BannerlordCoopSpectator3`

## Start Here

Read in this order when opening a new window:

1. `README.md`
2. `PROJECT_CONTEXT.md`
3. `HUMAN_NOTES_MULTIPLAYER_PROGRESS.md`
4. `docs/BATTLE_MAP_STATUS_AND_HANDOFF_2026-03-30.md`
5. `NEW_CHAT_PROMPT_2026-03-30_BATTLE_MAP_SPAWN_STABLE.md`
6. `BUILD_RUNBOOK.md`

## Active Runtime Status

- `docs/BATTLE_MAP_STATUS_AND_HANDOFF_2026-03-30.md`
  Current battle-map status, validated fixes, remaining gaps, and next work.
- `docs/BATTLE_MAP_CLIENT_SPAWN_CRASH_MATRIX_2026-03-30.md`
  Ordered investigation log for the client spawn crash and the final successful isolation.
- `docs/CAMPAIGN_TO_MP_RUNTIME_CONTRACT_ANALYSIS_2026-03-31.md`
  Current best analysis of why exact `1:1` campaign scene transfer is still a runtime-contract problem, not a missing-data problem.
- `docs/DEDICATED_MAP_SERVER_AND_SCENE_CONTRACT_ANALYSIS_2026-03-31.md`
  Dedicated-specific analysis of map ownership, archive creation, scene assets, and why stock dedicated is biased toward MP-owned scenes.
- `docs/EXACT_CAMPAIGN_SCENE_BOOTSTRAP_ANALYSIS_2026-03-31.md`
  Vanilla exact-scene bootstrap path for `battle_terrain_*`, from `MapPatchData` to `MissionInitializerRecord` to SP `Battle` mission shell.
- `docs/DEDICATED_EXACT_CAMPAIGN_SCENE_BOOTSTRAP_PROBE_2026-03-31.md`
  Dedicated-only exact-scene probe for runtime files, `sp_battle_scenes.xml`, campaign assembly availability, and manual `PairSceneNameToModuleName(..., "SandBoxCore")`.
- `docs/BATTLE_MAP_FULL_CONTRACT_DIAGNOSTICS_2026-03-31.md`
  Full diagnostics model for `MissionInitializerRecord -> live mission -> spawn path -> deployment plan -> formation frame`.
- `NEW_CHAT_PROMPT_2026-03-30_BATTLE_MAP_SPAWN_STABLE.md`
  Copy-paste prompt for a fresh Codex window.
- `HUMAN_NOTES_MULTIPLAYER_PROGRESS.md`
  Short human-readable project snapshot.
- `PROJECT_CONTEXT.md`
  Architecture rules, hard constraints, and current focus.

## Battle-Map Architecture

- `docs/BATTLE_MAP_RUNTIME_STARTUP_SEQUENCE_2026-03-29.md`
- `docs/COOP_BATTLE_BEHAVIOR_STACK_INVENTORY_2026-03-27.md`
- `docs/COOP_SELECTION_UI_TECHNICAL_MAP_2026-03-27.md`
- `docs/COOP_SYSTEM_DEPENDENCY_MAP_2026-03-28.md`

## Campaign To MP Transfer

- `docs/CAMPAIGN_TO_MP_RUNTIME_CONTRACT_ANALYSIS_2026-03-31.md`
- `docs/DEDICATED_MAP_SERVER_AND_SCENE_CONTRACT_ANALYSIS_2026-03-31.md`
- `docs/EXACT_CAMPAIGN_SCENE_BOOTSTRAP_ANALYSIS_2026-03-31.md`
- `docs/DEDICATED_EXACT_CAMPAIGN_SCENE_BOOTSTRAP_PROBE_2026-03-31.md`
- `docs/DEDICATED_SCENE_RESOLUTION_PROBE_2026-03-31.md`
- `docs/BATTLE_MAP_FULL_CONTRACT_DIAGNOSTICS_2026-03-31.md`
- `docs/CAMPAIGN_SCENE_TO_MP_TRANSFER_ANALYSIS_2026-03-28.md`
- `CAMPAIGN_TO_MP_1_TO_1_TRANSFER_AUDIT_2026-03-19.md`
- `SNAPSHOT_CHARACTER_RESOLUTION_AUDIT_2026-03-18.md`

## Aftermath And Writeback

- `docs/CAMPAIGN_AFTERMATH_AND_DEDICATED_DATA_MAP_2026-03-26.md`
- `docs/HOST_AFTERMATH_MAPPING_PLAN_2026-03-26.md`
- `SESSION_HANDOFF_2026-03-22_WRITEBACK_AND_BATTLE_COMPLETION.md`

## Build / Run / Diagnostics

- `BUILD_RUNBOOK.md`
- `DEDICATED_TROUBLESHOOTING.md`
- `docs/DEDICATED_SCENE_RESOLUTION_PROBE_2026-03-31.md`
- `docs/DEDICATED_EXACT_CAMPAIGN_SCENE_BOOTSTRAP_PROBE_2026-03-31.md`
- `docs/BATTLE_MAP_FULL_CONTRACT_DIAGNOSTICS_2026-03-31.md`
- `README.md`

## Planning / Legacy Root Docs

These are still useful but are not the primary current source of truth.

- `bannerlord_coop_plan.md`
- `SESSION_HANDOFF_2026-03-17_TDM_OVERLAY_REMOVAL.md`
- `SESSION_HANDOFF_2026-03-17_VANILLA_SPAWN_PIVOT.md`
- `SESSION_HANDOFF_2026-03-18_STABLE_VANILLA_SPAWN_WITH_ARMY_LAYER.md`
- `SESSION_HANDOFF_2026-03-19_7B_EXISTING_AGENT_POSSESSION.md`
- `SESSION_HANDOFF_2026-03-19_7C_BATTLE_FLOW_ENGAGE.md`
- `NEW_CHAT_PROMPT_2026-03-22_WRITEBACK_AND_BATTLE_COMPLETION.md`

## Archive

- `docs/archive/`
  Historical docs that should not drive current decisions unless explicitly needed for archaeology.
