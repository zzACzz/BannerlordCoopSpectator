# Documentation Index

Index updated: 2026-08-31
Project: `BannerlordCoopSpectator3`

## Canonical Living Documentation

For new engineering work, start with `docs/ai/README.md`. The `docs/ai/` set is the maintained, source-oriented knowledge base for Codex:

1. `docs/ai/README.md` — current project snapshot, evidence rules, task routing, and maintenance workflow.
2. `docs/ai/ARCHITECTURE.md` — authority, system boundaries, component ownership, transports, mission composition, and feature defaults.
3. `docs/ai/CODE_MAP.md` — source, subsystem, adapter, patch, UI, command, test, and script map.
4. `docs/ai/RUNTIME_FLOWS.md` — campaign capture, topology handshake, snapshot transfer, materialization, phases, reconnect, and writeback.
5. `docs/ai/BUILD_TEST_DEBUG.md` — side-effecting build behavior, reference profiles, contract tests, scripts, logs, and diagnostics.
6. `docs/ai/RELEASE_PACKAGING.md` — canonical GitHub/Nexus archive layouts, commands, exclusions, validation, and publication boundaries.
7. `docs/ai/INVARIANTS_AND_RISKS.md` — protected runtime contracts, known limitations, high-risk files, and regression checklists.
8. `docs/ai/BATTLE_TEST_AUTOMATION_SPEC.md` — canonical staged battle-test automation requirements.
9. `docs/ai/BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md` — installed-runtime feasibility evidence and blockers.
10. `docs/ai/BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md` — run-scoped client launcher/lobby-control implementation evidence and remaining runtime gates.
11. `docs/ai/BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md` — completed non-runtime runner/protocol/compile-only foundation, authoritative L0/L1 evidence, and the Milestone 2B boundary.

Current source and runtime evidence outrank dated plans. Preserve the remaining documents below as focused investigation history, implementation evidence, and technical specifications; do not assume that a newer dated specification is already implemented.

## Start Here

Read in this order when opening a new window:

1. `docs/ai/README.md`.
2. The task-specific document under `docs/ai/`.
3. The current source/configuration files referenced by that document.
4. The relevant dated report below when historical evidence or a focused investigation is needed.

`PROJECT_CONTEXT.md`, `HUMAN_NOTES_MULTIPLAYER_PROGRESS.md`, old handoff prompts, and `BUILD_RUNBOOK.md` remain useful historical context but no longer define the primary read order.

## Active Runtime Status

- `docs/EXACT_SIEGE_ASSAULT_INITIAL_MATERIALIZATION_2026-07-28.md`
  Підтверджений послідовними прогонами опис адаптивної початкової матеріалізації зовнішньої облоги: єдиний штатний серверний творець, суто піша проєкція кампанійської кінноти, одноразова межа початкової готовності та штатні підкріплення без потрапляння до початкової черги. Перепідключення до активного бою ще не перевірено.
- `docs/EXACT_VILLAGE_BATTLE_DEPLOYMENT_AND_MATERIALIZATION_2026-07-28.md`
  Опис окремих меж розстановки, адаптивної початкової матеріалізації та підтвердження готовності битви в малому поселенні. Одномашинний і двомашинний прогони пройдено; на клієнті підтверджено правильні прапорці та фактичні межі.
- `docs/EXACT_FIELD_BATTLE_DEPLOYMENT_BOUNDARIES_2026-07-28.md`
  Підтверджений двомашинним прогоном опис правильних логічних і візуальних меж розстановки звичайної польової битви: прив'язка прапорців до фактичних армій, захист від одиночної аномальної точки, безпечне прийняття контуру та діагностика.
- `docs/CURRENT_EXACT_BATTLE_MATERIALIZATION_RUNTIME_2026-07-27.md`
  Головний актуальний опис гібридної системи точного створення та синхронізації бійців: кампанійський знімок, єдиний штатний фізичний творець, клієнтські початкові пакети, адаптивна матеріалізація польової вилазки, штатні підкріплення, оптимізація великих битв і повернення результату до кампанії.
- `docs/MATERIALIZED_SIEGE_REINFORCEMENT_RUNTIME_2026-07-25.md`
  Історичний опис замороженої експериментальної системи поетапної матеріалізації облогових підкріплень: відомі падіння і зависання, причина повернення до штатних хвиль та умови майбутнього відновлення роботи. Не використовувати як опис поточної стабільної архітектури.
- `docs/SIEGE_BATTLE_STATUS_AND_PROGRESS_2026-06-11.md`
  Current siege-battle status and progress: subtype routing, exact bootstrap split, LordsHall indoor controller, Blockade guardrails, and the remaining live test matrix.
- `docs/COOP_BATTLE_STABILIZATION_AND_CONNECTIVITY_HANDOFF_2026-04-18.md`
  Current move-on handoff after the battle/runtime recovery: what was stabilized, which low-level lessons actually worked, and which runtime files should now be treated as protected while connectivity work starts.
- `docs/NEW_WINDOW_PROMPT_PUBLIC_OFFICIAL_AND_RADMIN_VPN_2026-04-18.md`
  Copy-paste prompt for the next Codex window focused only on `Public` official connection and `Radmin VPN`, with explicit `ilspycmd` / low-level investigation requirements.
- `docs/WORKING_SPAWN_BASELINE_AND_DIFF_PLAN_2026-04-13.md`
  Current validated source of truth after the 2026-04-13 recovery: latest mainline join/public/VPN layer kept, spawn-core restored to the proven working baseline, and future perk/captain work split into clean diff buckets.
- `docs/SPAWN_BASELINE_RESET_AND_PERK_REINTRO_PLAN_2026-04-13.md`
  Control note for the 2026-04-13 rollback: restore known-good spawn first, then reintroduce captain/perk changes in narrow slices.
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
- `docs/EXACT_CAMPAIGN_ARMY_SPAWN_AND_SPAWN_ZONE_ANALYSIS_2026-04-01.md`
  Native campaign army spawn contract and spawn-zone contract: `MissionAgentSpawnLogic`, `PartyGroupTroopSupplier`, `BattleSpawnPathSelector`, and field-battle formation tags.
- `docs/EXACT_CAMPAIGN_POST_SPAWN_ARMY_BOOTSTRAP_ANALYSIS_2026-04-01.md`
  Exact-scene runtime evidence after `SelectAllFormations` suppression. Shows the blocker has moved beyond early captain handoff into post-possession army bootstrap and maps the native campaign deployment lifecycle that should replace the current hybrid path.
- `docs/EXACT_CAMPAIGN_COMMANDER_CONTROL_HANDOFF_2026-04-02.md`
  Current exact-scene commander/general-control status: what already works, what remains, and how the remaining client commander handoff differs from the separate future campaign-style order UI migration.
- `docs/DEDICATED_EXACT_CAMPAIGN_SCENE_BOOTSTRAP_PROBE_2026-03-31.md`
  Dedicated-only exact-scene probe for runtime files, `sp_battle_scenes.xml`, campaign assembly availability, and manual `PairSceneNameToModuleName(..., "SandBoxCore")`.
- `docs/BATTLE_MAP_FULL_CONTRACT_DIAGNOSTICS_2026-03-31.md`
  Full diagnostics model for `MissionInitializerRecord -> live mission -> spawn path -> deployment plan -> formation frame`.
- `NEW_CHAT_PROMPT_2026-03-30_BATTLE_MAP_SPAWN_STABLE.md`
  Copy-paste prompt for a fresh Codex window.
- `NEW_CHAT_PROMPT_2026-04-02_COMMANDER_CONTROL_HANDOFF.md`
  Copy-paste prompt for continuing exact-scene commander control work in a fresh Codex window.
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
- `docs/EXACT_CAMPAIGN_ARMY_SPAWN_AND_SPAWN_ZONE_ANALYSIS_2026-04-01.md`
- `docs/EXACT_CAMPAIGN_POST_SPAWN_ARMY_BOOTSTRAP_ANALYSIS_2026-04-01.md`
- `docs/EXACT_CAMPAIGN_COMMANDER_CONTROL_HANDOFF_2026-04-02.md`
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

- `docs/ai/RELEASE_PACKAGING.md`
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
